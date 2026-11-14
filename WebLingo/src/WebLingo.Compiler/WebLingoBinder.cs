using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// The binder (specification, section 8.4, "Binder/IR"; its choices are in section 15.2). It resolves every name
/// lexically. A declaration takes effect where it stands, after its initializer (section 3.1): before it, and in
/// its own initializer, the name means what it means outside, and is undefined (WL2001) if nothing outside
/// declares it. A function declaration is the exception: it is in scope throughout its block (section 3.5). The
/// host's built-ins are in a scope around the program's, so a declaration may shadow one. A name of a function
/// around the one being bound is captured: the local is marked, and every function between gets a capture of it.
/// Binding goes on after an error, so that every binding error is reported.
/// </summary>
public sealed class WebLingoBinder
{
    private readonly Dictionary<string, WebLingoBuiltinDeclaration> _hostBuiltins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WebLingoBuiltinSymbol> _usedBuiltins = new(StringComparer.Ordinal);
    private readonly List<WebLingoBuiltinSymbol> _builtinsInOrder = [];
    private readonly List<WebLingoDiagnostic> _diagnostics = [];
    private FunctionContext _function;
    private Scope _scope;

    /// <summary>The program's function, and a scope around the program's, which holds <c>Object</c> once it is used.</summary>
    private readonly FunctionContext _main;
    private readonly Scope _prelude;

    /// <summary>The built-in class <c>Object</c>, bound once a program needs it (section 3.7).</summary>
    private WebLingoBoundClassDeclaration? _object;

    /// <summary>
    /// <c>Object</c>, which every class without <c>extends</c> extends: <c>equal(other)</c> gives <c>this == other</c>,
    /// and <c>toString()</c> the text <c>write</c> writes, an instance's class and fields; any class may override them
    /// (section 3.7).
    /// </summary>
    private const string ObjectSource =
        "class Object { equal(other) { return this == other; } toString() { return toText(this); } }";

    /// <summary>Whether <c>Object</c> is being bound, whose <c>toString()</c> calls <c>toText</c>, which nothing else has.</summary>
    private bool _inPrelude;

    /// <summary>The built-in class <c>Error</c>, bound once a program needs it (section 3.7).</summary>
    private WebLingoBoundClassDeclaration? _error;

    /// <summary>The built-in classes the program uses, in the order they are made, each after the class it extends.</summary>
    private readonly List<(string Name, WebLingoBoundClassDeclaration Declaration)> _builtinClasses = [];

    /// <summary>
    /// <c>Error</c>, which extends <c>Object</c>: <c>new Error(message)</c>, with the fields <c>message</c> and
    /// <c>code</c>, <c>null</c> for one a program throws; a runtime error a handler catches becomes one, its code set
    /// (section 3.7).
    /// </summary>
    private const string ErrorSource = "class Error { constructor(message) { this.message = message; this.code = null; } }";

    /// <summary>The host's built-ins, in the order it gives them, which <c>std:system</c> exports (section 3.9).</summary>
    private readonly IReadOnlyList<WebLingoBuiltinDeclaration> _builtinDeclarations;

    public WebLingoBinder(WebLingoCompilerOptions options)
    {
        _builtinDeclarations = options.Builtins;
        foreach (var builtin in options.Builtins) _hostBuiltins[builtin.Name] = builtin;
        _function = _main = new FunctionContext(null, "main");
        _prelude = new Scope(null, _function);
        _scope = new Scope(_prelude, _function);
    }

    private ClassContext? _class;

    /// <summary>
    /// The property whose getter or setter is being bound, where <c>field</c> is its own field (section 3.7): an
    /// instance's hidden field, or its class's for a static property, named <c>field:name</c>.
    /// </summary>
    private Accessor? _accessor;

    /// <summary>A property's getter or setter being bound: its property, whether it is static, its class, and the scope around it.</summary>
    private sealed record Accessor(string Property, bool IsStatic, WebLingoLocalSymbol? Class, Scope Outer);

    private static bool IsAccessor(string name) =>
        name.StartsWith("get:", StringComparison.Ordinal) || name.StartsWith("set:", StringComparison.Ordinal);

    /// <summary>A member's name as a program writes it: a getter's or a setter's, its property's.</summary>
    private static string MemberName(string name) => IsAccessor(name) ? name[4..] : name;

    /// <summary>
    /// Adds a member's name to its class's, or its extension's, false for one declared there already: a property's name
    /// is a method's or a field's too, and a property has one getter and one setter (section 3.7).
    /// </summary>
    private static bool AddMemberName(HashSet<string> names, string name)
    {
        if (!IsAccessor(name)) return !names.Contains("property:" + name) && names.Add(name);
        var property = name[4..];
        if (names.Contains(property) || !names.Add(name)) return false;
        names.Add("property:" + property);
        return true;
    }

    /// <summary>
    /// What <c>this</c> names in a static method or a static field's value: nothing, rather than an outer method's
    /// <c>this</c> (section 3.7). It is never a local of a function.
    /// </summary>
    private readonly WebLingoLocalSymbol _noThis = new("this", WebLingoDeclarationKind.Parameter, -1, default, new object());

    public IReadOnlyList<WebLingoDiagnostic> Diagnostics => _diagnostics;

    /// <summary>The file being bound, when it is not the program's own (section 3.9), whose diagnostics name it.</summary>
    private string? _source;

    /// <summary>The top-level scope of the file being bound, where imports and exports stand, and what it exports.</summary>
    private Scope? _unitScope;
    private List<(string Name, WebLingoLocalSymbol Local)> _exports = [];
    private IReadOnlyDictionary<WebLingoImportSyntax, int> _importTargets = new Dictionary<WebLingoImportSyntax, int>();

    public WebLingoBoundProgram Bind(WebLingoCompilationUnitSyntax unit) =>
        Bind([new WebLingoModuleSource("program", unit, new Dictionary<WebLingoImportSyntax, int>())]);

    /// <summary>
    /// A program and the files it imports (section 3.9): its own top level as now, each other file's as an <c>async</c>
    /// function of the scope around it, which sees the built-in classes, but none of the program's names.
    /// </summary>
    public WebLingoBoundProgram Bind(IReadOnlyList<WebLingoModuleSource> modules, bool isImport = false)
    {
        _unitScope = _scope;
        _importTargets = modules[0].Imports;
        _moduleSources = modules;
        var units = new List<WebLingoBoundUnit>();
        // An import's top level, or a program's with parameters: the values given, its one parameter, first (section 3.9).
        var mainParameters = ParametersOf(modules[0].Root);
        var importedValues = isImport || mainParameters.Count > 0 ? DeclareHidden("values", default) : null;
        // A program that imports, itself included, has a table of its files.
        var mainInstance = modules.Count > 1 || modules[0].Imports.Count > 0 ? DeclareHidden("module", default) : null;
        var programParameters = new List<WebLingoModuleParameter>();
        var statements = BindProgramParameters(mainParameters, importedValues, programParameters);
        statements.AddRange(BindStatements(AfterParameters(modules[0].Root, mainParameters.Count)));
        if (isImport)
        {
            // It imports its file, the program's second, with the values given, which the import checks when it runs, and
            // gives its instance (section 3.9).
            var imported = DeclareHidden("import", default);
            statements.Add(new WebLingoBoundImport(1, imported, default) { Values = Reference(importedValues!, default) });
            statements.Add(new WebLingoBoundReturn(Reference(imported, default), default));
            _main.Awaits = true;
        }
        if (mainInstance is not null) units.Add(new WebLingoBoundUnit(modules[0].Name, null, _exports, mainInstance));
        for (var i = 1; i < modules.Count; i++)
        {
            var module = modules[i];
            if (module.IsStandard)
            {
                // std:system: the host's built-ins, its exports, made where the program begins (section 3.9).
                var exported = new List<WebLingoBuiltinSymbol>();
                foreach (var declaration in _builtinDeclarations)
                {
                    // A component's render is no function, and so no export (section 13).
                    if (!declaration.RendersComponent) exported.Add(UseBuiltin(declaration));
                }
                units.Add(new WebLingoBoundUnit(module.Name, null, [], DeclareHidden(module.Name, default)) { Builtins = exported });
                continue;
            }
            var (outerFunction, outerScope) = (_function, _scope);
            _source = module.Name;
            _importTargets = module.Imports;
            _function = new FunctionContext(_main, module.Name) { IsAsync = true, IsUnit = true };
            _scope = _unitScope = new Scope(_prelude, _function);
            _exports = [];
            // A module with parameters takes their values as one map, its function's one parameter (section 3.9).
            var parameters = ParametersOf(module.Root);
            var values = parameters.Count > 0 ? DeclareHidden("values", default) : null;
            var instance = DeclareHidden("module", default);
            var body = new List<WebLingoBoundStatement>();
            var declared = new List<WebLingoModuleParameter>();
            foreach (var parameter in parameters)
            {
                // A default is bound before its parameter is declared, as a variable's initializer is (section 3.1).
                var value = parameter.Default is { } given ? BindExpression(given) : null;
                if (Declare(parameter.Name, WebLingoDeclarationKind.Const, parameter.NameSpan, null) is { } local)
                {
                    body.Add(new WebLingoBoundModuleParameter(local, values!, parameter.Name, value, parameter.Span));
                    declared.Add(new WebLingoModuleParameter(parameter.Name, parameter.Default is not null));
                }
            }
            body.AddRange(BindStatements(AfterParameters(module.Root, parameters.Count)));
            var function = new WebLingoBoundFunction(module.Name, values is null ? 0 : 1, _function.Locals, _function.Captures, body,
                module.Root.Span)
            {
                IsAsync = true,
                Source = module.Name
            };
            units.Add(new WebLingoBoundUnit(module.Name, function, _exports, instance) { Parameters = declared });
            (_function, _scope, _source) = (outerFunction, outerScope, null);
        }
        // The built-in classes the program uses are made first, before its own.
        var builtins = new List<WebLingoBoundStatement>();
        var builtinClasses = new List<(string, WebLingoBoundClass)>();
        foreach (var (name, declaration) in _builtinClasses)
        {
            builtins.Add(declaration);
            builtinClasses.Add((name, declaration.Class));
        }
        statements.InsertRange(0, builtins);
        return new WebLingoBoundProgram(statements, _function.Locals, _builtinsInOrder)
        {
            ErrorClass = _error?.Class,
            ObjectClass = _object?.Class,
            BuiltinClasses = builtinClasses,
            IsAsync = _main.Awaits,
            IsImport = isImport,
            Parameters = programParameters,
            Units = units
        };
    }

    /// <summary>Whether a name's local is the built-in class of that name, which no declaration of the program's hides.</summary>
    private bool IsBuiltinClass(WebLingoLocalSymbol? local, string name)
    {
        foreach (var (builtin, declaration) in _builtinClasses)
        {
            if (builtin == name) return ReferenceEquals(declaration.Local, local);
        }
        return false;
    }

    /// <summary>The program and the files it imports, by their index in its table of files (section 3.9).</summary>
    private IReadOnlyList<WebLingoModuleSource> _moduleSources = [];

    /// <summary>A file's parameters: its <c>param</c> statements, which come first, before its other statements (section 3.9).</summary>
    private static List<WebLingoParamSyntax> ParametersOf(WebLingoCompilationUnitSyntax root)
    {
        var parameters = new List<WebLingoParamSyntax>();
        foreach (var statement in root.Statements)
        {
            if (statement is not WebLingoParamSyntax parameter) break;
            parameters.Add(parameter);
        }
        return parameters;
    }

    private static List<WebLingoStatementSyntax> AfterParameters(WebLingoCompilationUnitSyntax root, int count)
    {
        var rest = new List<WebLingoStatementSyntax>();
        for (var i = count; i < root.Statements.Count; i++) rest.Add(root.Statements[i]);
        return rest;
    }

    /// <summary>
    /// The program's own parameters (section 3.9): each a constant of the value its host gives it, from the map its top
    /// level takes, or of its default; the host's values are checked when the program starts.
    /// </summary>
    private List<WebLingoBoundStatement> BindProgramParameters(List<WebLingoParamSyntax> parameters, WebLingoLocalSymbol? values,
        List<WebLingoModuleParameter> declared)
    {
        var statements = new List<WebLingoBoundStatement>();
        foreach (var parameter in parameters)
        {
            var value = parameter.Default is { } given ? BindExpression(given) : null;
            if (Declare(parameter.Name, WebLingoDeclarationKind.Const, parameter.NameSpan, null) is { } local)
            {
                statements.Add(new WebLingoBoundModuleParameter(local, values!, parameter.Name, value, parameter.Span));
                declared.Add(new WebLingoModuleParameter(parameter.Name, parameter.Default is not null));
            }
        }
        return statements;
    }

    /// <summary>
    /// The values an import gives its module's parameters, a map (section 3.9): each a parameter's, and every parameter
    /// without a default given (WL2016 otherwise, at compile time); null for a module without parameters given none.
    /// </summary>
    private WebLingoBoundExpression? BindImportValues(WebLingoImportSyntax import, int unit)
    {
        var parameters = _moduleSources[unit].IsStandard ? [] : ParametersOf(_moduleSources[unit].Root);
        if (import.Values is null && parameters.Count == 0) return null;
        var entries = new List<(string, WebLingoBoundExpression)>();
        var given = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in import.Values ?? [])
        {
            var bound = BindExpression(value.Value);
            var declares = false;
            foreach (var parameter in parameters) declares |= parameter.Name == value.Name;
            if (!declares)
            {
                Report(WebLingoDiagnosticCodes.ModuleParameter, $"'{value.Name}' is not a parameter of \"{import.Path}\".", value.NameSpan);
            }
            else if (!given.Add(value.Name))
            {
                Report(WebLingoDiagnosticCodes.DuplicateDeclaration, $"'{value.Name}' is given twice.", value.NameSpan);
            }
            else
            {
                entries.Add((value.Name, bound));
            }
        }
        foreach (var parameter in parameters)
        {
            if (parameter.Default is null && !given.Contains(parameter.Name))
            {
                Report(WebLingoDiagnosticCodes.ModuleParameter,
                    $"\"{import.Path}\"'s parameter '{parameter.Name}' is not given: '{{ {parameter.Name}: … }}' after its path.", import.PathSpan);
            }
        }
        return new WebLingoBoundMapLiteral(entries, import.Span);
    }

    /// <summary>A block's statements, in the current scope, its function declarations declared first.</summary>
    private List<WebLingoBoundStatement> BindStatements(IReadOnlyList<WebLingoStatementSyntax> syntax)
    {
        // A file's top level may export its declarations and import other files (section 3.9).
        var exported = new HashSet<WebLingoStatementSyntax>();
        var imports = new Dictionary<WebLingoImportSyntax, WebLingoBoundImport>();
        if (HasModuleStatements(syntax)) syntax = DeclareModuleStatements(syntax, exported, imports);
        var declared = new Dictionary<WebLingoStatementSyntax, WebLingoLocalSymbol>();
        foreach (var statement in syntax)
        {
            var local = statement switch
            {
                WebLingoFunctionDeclarationSyntax function => DeclareFunction(function),
                WebLingoClassDeclarationSyntax declaration => Declare(declaration.Name, WebLingoDeclarationKind.Class, declaration.NameSpan, null),
                _ => null
            };
            if (local is not null) declared.Add(statement, local);
        }
        // A class is made when its block begins, so its base is bound there too: it may name the block's classes and
        // functions, and what is declared around the block, but not the block's variables, which have no value yet.
        // A class without 'extends' extends Object (section 3.7), whatever the name 'Object' means where it is.
        var bases = new Dictionary<WebLingoClassDeclarationSyntax, WebLingoBoundExpression>();
        foreach (var statement in syntax)
        {
            if (statement is not WebLingoClassDeclarationSyntax derived) continue;
            bases.Add(derived, derived.Base is { } baseSyntax ? BindExpression(baseSyntax) : Reference(UseObject(), derived.NameSpan));
        }
        // An extension runs where its block begins too, after its classes are made (section 3.7), so its class is bound
        // there, as a base is.
        var targets = new Dictionary<WebLingoExtendSyntax, WebLingoBoundExpression>();
        foreach (var statement in syntax)
        {
            if (statement is WebLingoExtendSyntax extension) targets.Add(extension, BindExpression(extension.Target));
        }
        // A class's static fields are set when it is made, so their values are bound where the block begins too, where
        // 'this' is not the class's (WL2009).
        var statics = new Dictionary<WebLingoStatementSyntax, List<(string, WebLingoBoundExpression, WebLingoSourceSpan)>>();
        foreach (var statement in syntax)
        {
            var fields = statement switch
            {
                WebLingoClassDeclarationSyntax withStatics => withStatics.StaticFields,
                WebLingoExtendSyntax extension => extension.StaticFields,
                _ => []
            };
            if (fields.Count == 0) continue;
            _scope = new Scope(_scope, _function);
            _scope.Locals.Add("this", _noThis);
            var values = new List<(string, WebLingoBoundExpression, WebLingoSourceSpan)>();
            foreach (var field in fields)
            {
                var value = BindExpression(field.Value);
                values.Add((field.Name, field.IsConstant ? new WebLingoBoundStaticConstant(value, value.Span) : value, field.NameSpan));
            }
            _scope = _scope.Parent!;
            statics.Add(statement, values);
        }
        var statements = new List<WebLingoBoundStatement>();
        foreach (var statement in syntax)
        {
            if (statement is WebLingoFunctionDeclarationSyntax function)
            {
                var bound = BindFunction(function.Name, function.Parameters, function.Body, function.Span, isAsync: function.IsAsync);
                if (declared.TryGetValue(function, out var local))
                {
                    statements.Add(new WebLingoBoundFunctionDeclaration(local, bound, function.Span));
                }
                continue;
            }
            if (statement is WebLingoClassDeclarationSyntax declaration)
            {
                declared.TryGetValue(declaration, out var local);
                var bound = BindClass(declaration, local, bases.GetValueOrDefault(declaration),
                    statics.GetValueOrDefault(declaration) ?? []);
                if (local is not null) statements.Add(new WebLingoBoundClassDeclaration(local, bound, declaration.Span));
                continue;
            }
            if (statement is WebLingoExtendSyntax extended)
            {
                var bound = BindExtension(extended, statics.GetValueOrDefault(extended) ?? []);
                statements.Add(new WebLingoBoundExtension(targets[extended], bound, extended.Span));
                continue;
            }
            if (statement is WebLingoImportSyntax import)
            {
                if (imports.TryGetValue(import, out var boundImport)) statements.Add(boundImport);
                continue;
            }
            statements.Add(BindStatement(statement));
        }
        foreach (var statement in exported) Export(statement, statements, declared);
        OrderClasses(statements);
        return statements;
    }

    private static bool HasModuleStatements(IReadOnlyList<WebLingoStatementSyntax> syntax)
    {
        foreach (var statement in syntax)
        {
            if (statement is WebLingoImportSyntax or WebLingoExportSyntax) return true;
        }
        return false;
    }

    /// <summary>
    /// A file's imports and exports (section 3.9), at its top level only: an import declares its names, constants, the
    /// module's instance under its own name, or a hidden one; an export is its declaration, noted to be exported once
    /// bound. Gives the statements with each export's declaration in its place.
    /// </summary>
    private List<WebLingoStatementSyntax> DeclareModuleStatements(IReadOnlyList<WebLingoStatementSyntax> syntax,
        HashSet<WebLingoStatementSyntax> exported, Dictionary<WebLingoImportSyntax, WebLingoBoundImport> imports)
    {
        var atTop = _scope == _unitScope;
        var statements = new List<WebLingoStatementSyntax>();
        foreach (var statement in syntax)
        {
            if (statement is WebLingoExportSyntax export)
            {
                if (atTop) exported.Add(export.Declaration);
                else Report(WebLingoDiagnosticCodes.UnexpectedToken, "'export' stands at a module's top level.", export.Span);
                statements.Add(export.Declaration);
                continue;
            }
            statements.Add(statement);
            if (statement is not WebLingoImportSyntax import) continue;
            if (!atTop)
            {
                Report(WebLingoDiagnosticCodes.UnexpectedToken, "'import' stands at a module's top level.", import.Span);
                continue;
            }
            if (!_importTargets.TryGetValue(import, out var unit)) continue; // Its module was not found, which is reported.
            var instance = import.Name is { } name
                ? Declare(name, WebLingoDeclarationKind.Const, import.NameSpan, null)
                : DeclareHidden("import " + import.Path, import.Span);
            if (instance is null) continue;
            foreach (var imported in import.Names)
            {
                if (Declare(imported.Name, WebLingoDeclarationKind.Const, imported.Span, null) is { } alias)
                {
                    alias.ImportOf = instance;
                    alias.ImportName = imported.Name;
                }
            }
            imports.Add(import, new WebLingoBoundImport(unit, instance, import.Span) { Values = BindImportValues(import, unit) });
            if (_function == _main) _main.Awaits = true; // Its imports are awaited (section 3.9).
        }
        return statements;
    }

    /// <summary>An exported declaration's local, in a cell, which the file's instance reads (section 3.9).</summary>
    private void Export(WebLingoStatementSyntax declaration, List<WebLingoBoundStatement> statements,
        Dictionary<WebLingoStatementSyntax, WebLingoLocalSymbol> declared)
    {
        var local = declared.GetValueOrDefault(declaration);
        if (local is null && declaration is WebLingoVariableDeclarationSyntax variable)
        {
            foreach (var bound in statements)
            {
                if (bound is WebLingoBoundVariableDeclaration { Local: var candidate } && candidate.Name == variable.Name
                    && _scope.Locals.TryGetValue(variable.Name, out var inScope) && inScope == candidate)
                {
                    local = candidate;
                }
            }
        }
        if (local is null) return;
        local.IsCaptured = true;
        _exports.Add((local.Name, local));
    }

    /// <summary>
    /// Puts a block's classes in the order they can be made at its start: a base declared in the same block before the
    /// classes that extend it. A class that extends itself, directly or through others, is WL2013.
    /// </summary>
    private void OrderClasses(List<WebLingoBoundStatement> statements)
    {
        var positions = new List<int>();
        var classes = new List<WebLingoBoundClassDeclaration>();
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not WebLingoBoundClassDeclaration declaration) continue;
            positions.Add(i);
            classes.Add(declaration);
        }
        if (classes.Count < 2) return;
        var ordered = new List<WebLingoBoundClassDeclaration>();
        var state = new Dictionary<WebLingoBoundClassDeclaration, int>(); // 1 being placed, 2 placed.
        foreach (var declaration in classes) Place(declaration, classes, ordered, state);
        for (var i = 0; i < positions.Count; i++) statements[positions[i]] = ordered[i];
    }

    private void Place(WebLingoBoundClassDeclaration declaration, List<WebLingoBoundClassDeclaration> classes,
        List<WebLingoBoundClassDeclaration> ordered, Dictionary<WebLingoBoundClassDeclaration, int> state)
    {
        if (state.TryGetValue(declaration, out var seen))
        {
            if (seen == 1)
            {
                Report(WebLingoDiagnosticCodes.ClassExtendsItself, $"'{declaration.Local.Name}' extends itself.", declaration.Span);
            }
            return;
        }
        state[declaration] = 1;
        var baseLocal = declaration.Class.Base switch
        {
            WebLingoBoundLocalReference reference => reference.Local,
            WebLingoBoundCaptureReference capture => capture.Local,
            _ => null
        };
        var baseDeclaration = classes.Find(other => other.Local == baseLocal);
        if (baseDeclaration is not null) Place(baseDeclaration, classes, ordered, state);
        state[declaration] = 2;
        ordered.Add(declaration);
    }

    private WebLingoBoundStatement BindStatement(WebLingoStatementSyntax statement)
    {
        switch (statement)
        {
            case WebLingoVariableDeclarationSyntax declaration:
            {
                // The initializer is bound first: the name it declares does not exist in it yet.
                var initializer = declaration.Initializer is null
                    ? new WebLingoBoundError(declaration.Span)
                    : BindExpression(declaration.Initializer);
                var local = Declare(declaration.Name, declaration.Kind, declaration.NameSpan, null);
                return local is null
                    ? new WebLingoBoundExpressionStatement(initializer, declaration.Span)
                    : new WebLingoBoundVariableDeclaration(local, initializer, declaration.Span);
            }
            case WebLingoExpressionStatementSyntax expressionStatement:
                return new WebLingoBoundExpressionStatement(BindExpression(expressionStatement.Expression), statement.Span);
            case WebLingoIfSyntax conditional:
            {
                var condition = BindExpression(conditional.Condition);
                var then = BindInOwnScope(conditional.Then);
                var otherwise = conditional.Else is null ? null : BindInOwnScope(conditional.Else);
                return new WebLingoBoundIf(condition, then, otherwise, conditional.Span);
            }
            case WebLingoWhileSyntax whileLoop:
            {
                var condition = BindExpression(whileLoop.Condition);
                return new WebLingoBoundLoop(condition, BindLoopBody(whileLoop.Body), null, true, whileLoop.Span);
            }
            case WebLingoDoWhileSyntax doLoop:
            {
                var body = BindLoopBody(doLoop.Body);
                return new WebLingoBoundLoop(BindExpression(doLoop.Condition), body, null, false, doLoop.Span);
            }
            case WebLingoForSyntax forLoop:
            {
                // The loop's own scope holds what its init declares.
                _scope = new Scope(_scope, _function);
                var init = forLoop.Init is null ? null : BindStatement(forLoop.Init);
                var perIteration = new List<WebLingoLocalSymbol>(_scope.Locals.Values);
                var condition = forLoop.Condition is null ? null : BindExpression(forLoop.Condition);
                var step = forLoop.Step is null ? null : BindExpression(forLoop.Step);
                var body = BindLoopBody(forLoop.Body);
                _scope = _scope.Parent!;
                var loop = new WebLingoBoundLoop(condition, body, step, true, forLoop.Span) { PerIterationLocals = perIteration };
                return new WebLingoBoundBlock(init is null ? [loop] : [init, loop], forLoop.Span);
            }
            case WebLingoForEachSyntax forEach:
            {
                // The collection is bound first: the loop's variables do not exist in it yet. They are the loop's,
                // with a binding of their own on each iteration (sections 3.5 and 3.6).
                var collectionValue = BindExpression(forEach.Collection);
                _scope = new Scope(_scope, _function);
                var collection = DeclareHidden("foreach collection", forEach.Span);
                var position = DeclareHidden("foreach position", forEach.Span);
                var item = Declare(forEach.ItemName, WebLingoDeclarationKind.Var, forEach.ItemSpan, null)
                    ?? DeclareHidden(forEach.ItemName, forEach.ItemSpan);
                var value = forEach.ValueName is null
                    ? null
                    : Declare(forEach.ValueName, WebLingoDeclarationKind.Var, forEach.ValueSpan, null)
                      ?? DeclareHidden(forEach.ValueName, forEach.ValueSpan);
                var body = BindLoopBody(forEach.Body);
                _scope = _scope.Parent!;
                return new WebLingoBoundForEach(collection, position, item, value, collectionValue, body, forEach.Span);
            }
            case WebLingoUsingSyntax usingStatement:
            {
                // The value is bound first: the name it declares does not exist in it yet. A using may await its
                // resource's async disposal: at the program's top level, which then becomes async (section 3.8).
                if (_function == _main) _main.Awaits = true;
                var value = BindExpression(usingStatement.Value);
                _scope = new Scope(_scope, _function);
                var local = Declare(usingStatement.Name, WebLingoDeclarationKind.Var, usingStatement.NameSpan, null)
                    ?? DeclareHidden(usingStatement.Name, usingStatement.NameSpan);
                var exception = DeclareHidden("using exception", usingStatement.Span);
                var exceptionOrigin = DeclareHidden("using origin", usingStatement.Span);
                UseError(); // A runtime error its handler catches becomes an Error.
                var body = BindInOwnScope(usingStatement.Body);
                _scope = _scope.Parent!;
                return new WebLingoBoundUsing(local, value, body, usingStatement.Span)
                {
                    ExceptionValue = exception,
                    ExceptionOrigin = exceptionOrigin
                };
            }
            case WebLingoBreakSyntax or WebLingoContinueSyntax:
            {
                var isBreak = statement is WebLingoBreakSyntax;
                if (_function.LoopDepth == 0)
                {
                    Report(WebLingoDiagnosticCodes.NotInLoop, $"'{(isBreak ? "break" : "continue")}' is only allowed in a loop.",
                        statement.Span);
                }
                else if (_function.Finallys.Count > 0 && _function.LoopDepth == _function.Finallys[^1])
                {
                    Report(WebLingoDiagnosticCodes.JumpOutOfFinally,
                        $"'{(isBreak ? "break" : "continue")}' would leave a 'finally' block, which nothing may leave early.",
                        statement.Span);
                }
                return isBreak ? new WebLingoBoundBreak(statement.Span) : new WebLingoBoundContinue(statement.Span);
            }
            case WebLingoReturnSyntax returnStatement:
            {
                var value = returnStatement.Value is null ? null : BindExpression(returnStatement.Value);
                if (_function.Parent is null || _function.IsUnit)
                {
                    Report(WebLingoDiagnosticCodes.ReturnOutsideFunction, "'return' is only allowed in a function.", statement.Span);
                }
                else if (_function.Finallys.Count > 0)
                {
                    Report(WebLingoDiagnosticCodes.JumpOutOfFinally,
                        "'return' would leave a 'finally' block, which nothing may leave early.", statement.Span);
                }
                if (_function.SetterValue is { } assigned)
                {
                    // A setter gives the value assigned (section 3.7).
                    if (value is not null)
                    {
                        Report(WebLingoDiagnosticCodes.UnexpectedToken, "A setter's 'return' gives no value: an assignment's value is the value assigned.",
                            statement.Span);
                    }
                    value = Reference(assigned, statement.Span);
                }
                return new WebLingoBoundReturn(value, statement.Span);
            }
            case WebLingoTrySyntax tryStatement:
            {
                UseError(); // A runtime error its handlers catch becomes an Error, of its kind's class.
                var body = BindStatement(tryStatement.Body);
                var catches = new List<WebLingoBoundCatch>();
                WebLingoLocalSymbol? caughtValue = null;
                WebLingoLocalSymbol? caughtOrigin = null;
                if (tryStatement.Catches.Count > 0)
                {
                    caughtValue = DeclareHidden("catch exception", tryStatement.Span);
                    caughtOrigin = DeclareHidden("catch origin", tryStatement.Span);
                }
                foreach (var catchClause in tryStatement.Catches)
                {
                    // Its class is evaluated where the try is, when an exception reaches it (section 3.7).
                    var caught = catchClause.Class is null ? null : BindExpression(catchClause.Class);
                    _scope = new Scope(_scope, _function);
                    var variable = Declare(catchClause.Name, WebLingoDeclarationKind.Var, catchClause.NameSpan, null)
                        ?? DeclareHidden(catchClause.Name, catchClause.NameSpan);
                    _function.Catches.Add((caughtValue!, caughtOrigin!));
                    var statements = BindStatements(catchClause.Body.Statements);
                    _function.Catches.RemoveAt(_function.Catches.Count - 1);
                    _scope = _scope.Parent!;
                    catches.Add(new WebLingoBoundCatch(caught, variable, new WebLingoBoundBlock(statements, catchClause.Body.Span)));
                }
                WebLingoBoundFinally? cleanup = null;
                if (tryStatement.Finally is { } finallyBlock)
                {
                    var value = DeclareHidden("finally exception", finallyBlock.Span);
                    var origin = DeclareHidden("finally origin", finallyBlock.Span);
                    _function.Finallys.Add(_function.LoopDepth);
                    var statements = BindStatement(finallyBlock);
                    _function.Finallys.RemoveAt(_function.Finallys.Count - 1);
                    cleanup = new WebLingoBoundFinally(value, origin, statements);
                }
                return new WebLingoBoundTry(body, catches, caughtValue, caughtOrigin, cleanup, tryStatement.Span);
            }
            case WebLingoThrowSyntax throwStatement:
            {
                if (throwStatement.Value is { } thrown) return new WebLingoBoundThrow(BindExpression(thrown), throwStatement.Span);
                if (_function.Catches.Count == 0)
                {
                    Report(WebLingoDiagnosticCodes.ThrowOutsideCatch,
                        "'throw;' alone is only allowed in a 'catch' block, where it throws its exception again.", throwStatement.Span);
                    return new WebLingoBoundBlock([], throwStatement.Span);
                }
                var (caught, where) = _function.Catches[^1];
                return new WebLingoBoundRethrow(caught, where, throwStatement.Span);
            }
            case WebLingoBlockSyntax block:
            {
                _scope = new Scope(_scope, _function);
                var statements = BindStatements(block.Statements);
                _scope = _scope.Parent!;
                return new WebLingoBoundBlock(statements, block.Span);
            }
            case WebLingoFunctionDeclarationSyntax function:
                // A function declared on its own, as a branch or a loop's body: a block of one.
                return new WebLingoBoundBlock(BindStatements([function]), function.Span);
            case WebLingoClassDeclarationSyntax declaration:
                return new WebLingoBoundBlock(BindStatements([declaration]), declaration.Span);
            case WebLingoExtendSyntax extension:
                return new WebLingoBoundBlock(BindStatements([extension]), extension.Span);
            case WebLingoParamSyntax parameter:
                Report(WebLingoDiagnosticCodes.UnexpectedToken, "'param' stands at a module's top level, before its other statements.",
                    parameter.Span);
                return new WebLingoBoundBlock([], parameter.Span);
            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
        }
    }

    private WebLingoBoundStatement BindLoopBody(WebLingoStatementSyntax body)
    {
        _function.LoopDepth++;
        var bound = BindInOwnScope(body);
        _function.LoopDepth--;
        return bound;
    }

    /// <summary>A branch or a loop's body: a declaration in it, without braces, is in scope only there.</summary>
    private WebLingoBoundStatement BindInOwnScope(WebLingoStatementSyntax statement)
    {
        _scope = new Scope(_scope, _function);
        var bound = BindStatements([statement]);
        _scope = _scope.Parent!;
        return new WebLingoBoundBlock(bound, statement.Span);
    }

    /// <summary>
    /// A class: its base, bound where the class is, then its constructor and its methods, each a function whose first
    /// parameter is <c>this</c>; a name declared twice in it is WL2002 (section 3.7). A class without a constructor gets
    /// one that does nothing but run its base's; a constructor of a derived class that does not call <c>super(…)</c>
    /// runs its base's first, with no arguments. Its static members, fields and methods, have names of their own, apart
    /// from its methods'; its static methods are functions without <c>this</c> or <c>super</c> (WL2009).
    /// </summary>
    private WebLingoBoundClass BindClass(WebLingoClassDeclarationSyntax declaration, WebLingoLocalSymbol? local, WebLingoBoundExpression? baseClass,
        List<(string Name, WebLingoBoundExpression Value, WebLingoSourceSpan Span)> staticFields)
    {
        var hasBase = baseClass is not null;
        WebLingoBoundFunction? constructor = null;
        var methods = new List<(string, WebLingoBoundFunction)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<WebLingoMethodSyntax>();
        var staticSyntax = new List<WebLingoMethodSyntax>();
        foreach (var member in declaration.Methods) (member.IsStatic ? staticSyntax : members).Add(member);
        var defaultConstructor = !members.Exists(member => member.Name == "constructor");
        if (defaultConstructor)
        {
            var span = new WebLingoSourceSpan(declaration.Span.Start, 0);
            members.Insert(0, new WebLingoMethodSyntax("constructor", span, [], new WebLingoBlockSyntax([], span), span));
        }
        var outerClass = _class;
        var disposer = members.Find(member => member.Name is "dispose" or "destructor");
        foreach (var syntax in members)
        {
            var method = syntax;
            if (method.Name == "destructor")
            {
                // The same method as dispose(), which callers call (section 3.7).
                if (disposer is not null && disposer != method)
                {
                    Report(WebLingoDiagnosticCodes.DisposeAndDestructor,
                        "A class declares 'dispose()' or 'destructor()', which are the same method, not both.", method.NameSpan);
                    continue;
                }
                method = method with { Name = "dispose" };
            }
            else if (method.Name == "dispose" && disposer is not null && disposer != method)
            {
                Report(WebLingoDiagnosticCodes.DisposeAndDestructor,
                    "A class declares 'dispose()' or 'destructor()', which are the same method, not both.", method.NameSpan);
                continue;
            }
            var isConstructor = method.Name == "constructor";
            // Object's constructor does nothing, so a class that extends it without saying so does not run it.
            if (isConstructor && declaration.Base is not null && !method.CallsSuper)
            {
                var span = new WebLingoSourceSpan(method.Body.Span.Start, 0);
                var superCall = new WebLingoExpressionStatementSyntax(new WebLingoCallExpressionSyntax(new WebLingoSuperSyntax(span), [], span), span);
                method = method with { Body = method.Body with { Statements = [superCall, .. method.Body.Statements] } };
            }
            _class = new ClassContext(local, hasBase, isConstructor);
            _accessor = IsAccessor(method.Name) ? new Accessor(method.Name[4..], false, local, _scope) : null;
            var function = BindFunction(isConstructor ? declaration.Name : method.Name, method.Parameters, method.Body,
                method.Span, isMethod: true, isAsync: method.IsAsync);
            (_class, _accessor) = (outerClass, null);
            if (!AddMemberName(names, method.Name))
            {
                Report(WebLingoDiagnosticCodes.DuplicateDeclaration, $"'{MemberName(method.Name)}' is already declared in this class.",
                    method.NameSpan);
                continue;
            }
            if (isConstructor) constructor = function;
            else methods.Add((method.Name, function));
        }
        var staticNames = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<(string, WebLingoBoundExpression, WebLingoSourceSpan)>();
        foreach (var field in staticFields)
        {
            if (AddMemberName(staticNames, field.Name)) fields.Add(field);
            else Report(WebLingoDiagnosticCodes.DuplicateDeclaration, $"'{field.Name}' is already declared in this class.", field.Span);
        }
        var staticMethods = new List<(string, WebLingoBoundFunction)>();
        foreach (var method in staticSyntax)
        {
            _class = null; // No 'super' in a static method.
            _accessor = IsAccessor(method.Name) ? new Accessor(method.Name[4..], true, local, _scope) : null;
            var function = BindFunction(method.Name, method.Parameters, method.Body, method.Span, isStatic: true,
                isAsync: method.IsAsync);
            (_class, _accessor) = (outerClass, null);
            if (AddMemberName(staticNames, method.Name)) staticMethods.Add((method.Name, function));
            else Report(WebLingoDiagnosticCodes.DuplicateDeclaration, $"'{MemberName(method.Name)}' is already declared in this class.", method.NameSpan);
        }
        return new WebLingoBoundClass(declaration.Name, constructor, methods, declaration.Span)
        {
            Base = baseClass,
            StaticMethods = staticMethods,
            StaticFields = fields,
            DefaultConstructor = defaultConstructor
        };
    }

    /// <summary>
    /// An extension's members (section 3.7): methods, <c>destructor()</c> being <c>dispose()</c> as in a class, and
    /// static members, which have names of their own; a constructor, which the parser reported, is left out. Its
    /// methods have <c>this</c>, but no <c>super</c> (WL2009).
    /// </summary>
    private WebLingoBoundClass BindExtension(WebLingoExtendSyntax extension,
        List<(string Name, WebLingoBoundExpression Value, WebLingoSourceSpan Span)> staticFields)
    {
        var name = extension.Target switch
        {
            WebLingoNameSyntax target => target.Name,
            WebLingoMemberAccessSyntax target => target.Name,
            _ => "extension"
        };
        var methods = new List<(string, WebLingoBoundFunction)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var staticMethods = new List<(string, WebLingoBoundFunction)>();
        var staticNames = new HashSet<string>(StringComparer.Ordinal);
        var outerClass = _class;
        WebLingoMethodSyntax? disposer = null;
        foreach (var member in extension.Methods)
        {
            if (!member.IsStatic && member.Name is "dispose" or "destructor") disposer ??= member;
        }
        foreach (var method in extension.Methods)
        {
            if (method.Name == "constructor" && !method.IsStatic) continue;
            var methodName = method.Name;
            if (!method.IsStatic && method.Name is "dispose" or "destructor")
            {
                if (disposer != method)
                {
                    Report(WebLingoDiagnosticCodes.DisposeAndDestructor,
                        "An extension gives 'dispose()' or 'destructor()', which are the same method, not both.", method.NameSpan);
                    continue;
                }
                methodName = "dispose";
            }
            _class = method.IsStatic ? null : new ClassContext(null, false, false) { InExtension = true };
            _accessor = IsAccessor(methodName) ? new Accessor(methodName[4..], method.IsStatic, null, _scope) : null;
            var function = method.IsStatic
                ? BindFunction(methodName, method.Parameters, method.Body, method.Span, isStatic: true, isAsync: method.IsAsync)
                : BindFunction(methodName, method.Parameters, method.Body, method.Span, isMethod: true, isAsync: method.IsAsync);
            (_class, _accessor) = (outerClass, null);
            if (!AddMemberName(method.IsStatic ? staticNames : names, methodName))
            {
                Report(WebLingoDiagnosticCodes.DuplicateDeclaration, $"'{MemberName(methodName)}' is already declared in this extension.",
                    method.NameSpan);
                continue;
            }
            (method.IsStatic ? staticMethods : methods).Add((methodName, function));
        }
        var fields = new List<(string, WebLingoBoundExpression, WebLingoSourceSpan)>();
        foreach (var field in staticFields)
        {
            if (AddMemberName(staticNames, field.Name)) fields.Add(field);
            else Report(WebLingoDiagnosticCodes.DuplicateDeclaration, $"'{field.Name}' is already declared in this extension.", field.Span);
        }
        return new WebLingoBoundClass(name, null, methods, extension.Span) { StaticMethods = staticMethods, StaticFields = fields };
    }

    /// <summary>
    /// A function's body, in a function context of its own, its parameters its first locals; a method's first is
    /// <c>this</c>, which only a method declares (section 3.7).
    /// </summary>
    private WebLingoBoundFunction BindFunction(string name, IReadOnlyList<WebLingoParameterSyntax> parameters, WebLingoBlockSyntax body,
        WebLingoSourceSpan span, bool isMethod = false, bool isStatic = false, bool isAsync = false)
    {
        var outerFunction = _function;
        var outerScope = _scope;
        var (outerClass, outerAccessor) = (_class, _accessor);
        // A function with a receiver, its first parameter `this`, is bound as a method is, but of no class: it has no
        // `super`, and no property's `field` (section 3.7).
        var receiver = parameters.Count > 0 && parameters[0].Name == "this";
        if (receiver)
        {
            var rest = new List<WebLingoParameterSyntax>();
            for (var i = 1; i < parameters.Count; i++) rest.Add(parameters[i]);
            span = parameters[0].Span;
            parameters = rest;
            isMethod = true;
            (_class, _accessor) = (null, null);
        }
        _function = new FunctionContext(outerFunction, name) { IsAsync = isAsync };
        _scope = new Scope(outerScope, _function);
        if (isMethod) Declare("this", WebLingoDeclarationKind.Parameter, span, null);
        if (isStatic) _scope.Locals.Add("this", _noThis); // An outer method's 'this' is not a static method's (WL2009).
        var defaults = new List<WebLingoBoundExpression?>();
        foreach (var parameter in parameters)
        {
            // A default is bound before its parameter is declared, as a variable's initializer is (section 3.1).
            defaults.Add(parameter.Default is { } value ? BindExpression(value) : null);
            Declare(parameter.Name, WebLingoDeclarationKind.Parameter, parameter.Span, null);
        }
        _scope = new Scope(_scope, _function); // The body may declare a name its parameters have, shadowing it.
        // A setter gives the value assigned, which the assignment's value is, whatever its body does with its parameter.
        var setterParameter = name.StartsWith("set:", StringComparison.Ordinal) && parameters.Count == 1
            ? _function.Locals[^1]
            : null;
        if (setterParameter is not null) _function.SetterValue = DeclareHidden("assigned", span);
        var statements = BindStatements(body.Statements);
        if (_function.SetterValue is { } assigned)
        {
            statements.Insert(0, new WebLingoBoundVariableDeclaration(assigned, Reference(setterParameter!, span), span));
            statements.Add(new WebLingoBoundReturn(Reference(assigned, span), span));
        }
        var bound = new WebLingoBoundFunction(name, parameters.Count + (isMethod ? 1 : 0), _function.Locals, _function.Captures,
            statements, span)
        {
            HasThis = isMethod,
            Defaults = defaults,
            IsAsync = isAsync,
            Source = _source
        };
        _function = outerFunction;
        _scope = outerScope;
        (_class, _accessor) = (outerClass, outerAccessor);
        return bound;
    }

    /// <summary>A function declared by name, with how many arguments a call of it may give (section 3.5).</summary>
    private WebLingoLocalSymbol? DeclareFunction(WebLingoFunctionDeclarationSyntax function)
    {
        var local = Declare(function.Name, WebLingoDeclarationKind.Function, function.NameSpan, function.Parameters.Count);
        if (local is null) return null;
        var required = 0;
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Default is null) required++;
        }
        local.FunctionMinArity = required;
        return local;
    }

    private WebLingoLocalSymbol? Declare(string name, WebLingoDeclarationKind kind, WebLingoSourceSpan span, int? arity)
    {
        if (_scope.Locals.ContainsKey(name))
        {
            Report(WebLingoDiagnosticCodes.DuplicateDeclaration, $"'{name}' is already declared in this scope.", span);
            return null;
        }
        var local = new WebLingoLocalSymbol(name, kind, _function.Locals.Count, span, _function) { FunctionArity = arity };
        _function.Locals.Add(local);
        _scope.Locals.Add(name, local);
        return local;
    }

    /// <summary>
    /// The base's constructor or method, bound to <c>this</c>, in a method of a class that extends another; anywhere
    /// else <c>super</c> is WL2009 (section 3.7).
    /// </summary>
    private WebLingoBoundExpression BindSuper(string name, WebLingoSourceSpan span)
    {
        if (_class is { InExtension: true })
        {
            Report(WebLingoDiagnosticCodes.ThisOutsideMethod, "'super' is not allowed in an extension's methods.", span);
            return new WebLingoBoundError(span);
        }
        if (_class is not { HasBase: true, Local: { } classLocal } || LookupLocal("this") is not { } self
            || ReferenceEquals(self, _noThis))
        {
            Report(WebLingoDiagnosticCodes.ThisOutsideMethod,
                "'super' is only allowed in a method or a constructor of a class that extends another.", span);
            return new WebLingoBoundError(span);
        }
        return new WebLingoBoundSuper(Reference(classLocal, span), Reference(self, span), name, span);
    }

    /// <summary>A local of the function that no name refers to: a loop's own state.</summary>
    private WebLingoLocalSymbol DeclareHidden(string name, WebLingoSourceSpan span)
    {
        var local = new WebLingoLocalSymbol(name, WebLingoDeclarationKind.Var, _function.Locals.Count, span, _function);
        _function.Locals.Add(local);
        return local;
    }

    private WebLingoBoundExpression BindExpression(WebLingoExpressionSyntax expression)
    {
        switch (expression)
        {
            case WebLingoIntegerLiteralSyntax literal:
                return new WebLingoBoundIntegerLiteral(literal.Value, literal.Span);
            case WebLingoBinary64LiteralSyntax literal:
                return new WebLingoBoundBinary64Literal(literal.Value, literal.Span);
            case WebLingoBooleanLiteralSyntax literal:
                return new WebLingoBoundBooleanLiteral(literal.Value, literal.Span);
            case WebLingoNullLiteralSyntax literal:
                return new WebLingoBoundNullLiteral(literal.Span);
            case WebLingoStringLiteralSyntax literal:
                return new WebLingoBoundStringLiteral(literal.Value, literal.Span);
            case WebLingoNameSyntax name:
                return BindName(name);
            case WebLingoParenthesizedExpressionSyntax parenthesized:
                return BindExpression(parenthesized.Expression);
            case WebLingoUnaryExpressionSyntax unary:
                return new WebLingoBoundUnary(unary.Operator, BindExpression(unary.Operand), unary.Span);
            case WebLingoBinaryExpressionSyntax binary:
                return new WebLingoBoundBinary(binary.Operator, BindExpression(binary.Left), BindExpression(binary.Right),
                    binary.OperatorSpan, binary.Span);
            case WebLingoAssignmentExpressionSyntax assignment:
                return BindAssignment(assignment);
            case WebLingoCallExpressionSyntax call:
                return BindCall(call);
            case WebLingoArrayLiteralSyntax array:
            {
                var elements = new List<WebLingoBoundExpression>();
                foreach (var element in array.Elements) elements.Add(BindExpression(element));
                return new WebLingoBoundArrayLiteral(elements, array.Span);
            }
            case WebLingoSetLiteralSyntax set:
            {
                var elements = new List<WebLingoBoundExpression>();
                foreach (var element in set.Elements) elements.Add(BindExpression(element));
                return new WebLingoBoundSetLiteral(elements, set.Span);
            }
            case WebLingoMapLiteralSyntax map:
            {
                var entries = new List<(string, WebLingoBoundExpression)>();
                foreach (var entry in map.Entries) entries.Add((entry.Key, BindExpression(entry.Value)));
                return new WebLingoBoundMapLiteral(entries, map.Span);
            }
            case WebLingoIndexExpressionSyntax index:
                return new WebLingoBoundIndex(BindExpression(index.Target), BindExpression(index.Index), index.Span)
                {
                    IsOptional = index.IsOptional
                };
            case WebLingoIndexAssignmentSyntax element:
                return new WebLingoBoundIndexAssignment(BindExpression(element.Target.Target), BindExpression(element.Target.Index),
                    BindExpression(element.Value), element.Span);
            case WebLingoMemberAccessSyntax { Target: WebLingoSuperSyntax } superMember:
                return BindSuper(superMember.Name, superMember.Span);
            case WebLingoMemberAccessSyntax member:
                if (member.Name is "toString" or "equal") UseObject(); // Every value has Object's (section 3.7).
                if (member.Name == "__instanceOf")
                {
                    // A value's class, which its kind's is for one that is not an instance (section 3.7).
                    foreach (var (name, _) in WebLingoPrelude.ValueClasses) UseBuiltinClass(name);
                }
                return new WebLingoBoundMember(BindExpression(member.Target), member.Name, member.Span) { IsOptional = member.IsOptional };
            case WebLingoMemberAssignmentSyntax field:
                return new WebLingoBoundMemberAssignment(BindExpression(field.Target.Target), field.Target.Name, BindExpression(field.Value),
                    field.Span);
            case WebLingoNewExpressionSyntax created:
            {
                var target = BindExpression(created.Class);
                var arguments = new List<WebLingoBoundExpression>();
                foreach (var argument in created.Arguments) arguments.Add(BindExpression(argument));
                return new WebLingoBoundNew(target, arguments, created.Span);
            }
            case WebLingoSuperSyntax:
                Report(WebLingoDiagnosticCodes.ThisOutsideMethod,
                    "'super' is either called, 'super(…)', or names a member of the base class, 'super.name'.", expression.Span);
                return new WebLingoBoundError(expression.Span);
            case WebLingoAwaitExpressionSyntax awaited:
            {
                // In an async function, or at the program's top level, which then becomes one (section 3.8).
                if (_function == _main) _main.Awaits = true;
                else if (!_function.IsAsync)
                {
                    Report(WebLingoDiagnosticCodes.AwaitOutsideAsync,
                        "'await' is only allowed in an async function, or at a script's top level.", awaited.Span);
                }
                return new WebLingoBoundAwait(BindExpression(awaited.Operand), awaited.Span);
            }
            case WebLingoIsExpressionSyntax test:
                UseObject(); // A name that is not a class calls equal(), Object's for a value that is not an instance.
                return new WebLingoBoundIs(BindExpression(test.Value), BindExpression(test.Class), test.Span);
            case WebLingoThisSyntax self:
            {
                var local = LookupLocal("this");
                if (local is not null && !ReferenceEquals(local, _noThis)) return Reference(local, self.Span);
                Report(WebLingoDiagnosticCodes.ThisOutsideMethod, local is null
                    ? "'this' is only allowed in a method or a constructor."
                    : "'this' is not allowed in a static method or a static field's value.", self.Span);
                return new WebLingoBoundError(self.Span);
            }
            case WebLingoFunctionExpressionSyntax function:
                return new WebLingoBoundFunctionExpression(
                    BindFunction("", function.Parameters, function.Body, function.Span, isAsync: function.IsAsync), function.Span);
            case WebLingoErrorExpressionSyntax error:
                return new WebLingoBoundError(error.Span);
            default:
                throw new InvalidOperationException($"Unknown expression {expression.GetType().Name}.");
        }
    }

    private WebLingoBoundExpression BindName(WebLingoNameSyntax name)
    {
        if (name.Name == "field" && _accessor is { } accessor && !DeclaresField(accessor) && AccessorOwner(accessor, name.Span) is { } owner)
        {
            // A property's own field, null until its accessors give it a value (section 3.7).
            return new WebLingoBoundMember(owner, "field:" + accessor.Property, name.Span) { IsOptional = true };
        }
        if (LookupLocal(name.Name) is { } local)
        {
            // A name an import takes is its module's export, read as it is when read (section 3.9).
            return local.ImportOf is { } module
                ? new WebLingoBoundMember(Reference(module, name.Span), local.ImportName!, name.Span)
                : Reference(local, name.Span);
        }
        if (_hostBuiltins.TryGetValue(name.Name, out var builtin))
        {
            if (!builtin.RendersComponent) return new WebLingoBoundBuiltinReference(UseBuiltin(builtin), name.Span);
            Report(WebLingoDiagnosticCodes.InvalidOperation, $"'{name.Name}' renders a component, and can only be called.",
                name.Span);
            return new WebLingoBoundError(name.Span);
        }
        Report(WebLingoDiagnosticCodes.UndefinedIdentifier, $"'{name.Name}' is not defined.", name.Span);
        return new WebLingoBoundError(name.Span);
    }

    /// <summary>What holds a property's own field: the instance, or, for a static property, its class (section 3.7).</summary>
    /// <summary>
    /// Whether a variable named <c>field</c> is declared in the accessor being bound, which then means it there, in place
    /// of its property's field; one declared around the property does not reach into it (section 3.7).
    /// </summary>
    private bool DeclaresField(Accessor accessor)
    {
        for (var scope = _scope; scope is not null && scope != accessor.Outer; scope = scope.Parent)
        {
            if (scope.Locals.ContainsKey("field")) return true;
        }
        return false;
    }

    private WebLingoBoundExpression? AccessorOwner(Accessor accessor, WebLingoSourceSpan span)
    {
        if (accessor.IsStatic) return accessor.Class is { } classLocal ? Reference(classLocal, span) : null;
        return LookupLocal("this") is { } self && !ReferenceEquals(self, _noThis) ? Reference(self, span) : null;
    }

    /// <summary>A local read where it is: in its own function, or through this function's capture of it.</summary>
    private WebLingoBoundExpression Reference(WebLingoLocalSymbol local, WebLingoSourceSpan span) =>
        local.Owner == _function
            ? new WebLingoBoundLocalReference(local, span)
            : new WebLingoBoundCaptureReference(CaptureIndex(_function, local), local, span);

    /// <summary>The index of <paramref name="function"/>'s capture of a local of a function around it, made if new.</summary>
    private static int CaptureIndex(FunctionContext function, WebLingoLocalSymbol local)
    {
        if (function.CaptureIndexes.TryGetValue(local, out var index)) return index;
        local.IsCaptured = true;
        var parent = function.Parent!;
        var capture = parent == local.Owner
            ? new WebLingoBoundCapture(true, local.Index, local)
            : new WebLingoBoundCapture(false, CaptureIndex(parent, local), local);
        index = function.Captures.Count;
        function.Captures.Add(capture);
        function.CaptureIndexes.Add(local, index);
        return index;
    }

    private WebLingoBoundExpression BindAssignment(WebLingoAssignmentExpressionSyntax assignment)
    {
        var value = BindExpression(assignment.Value);
        var target = assignment.Target;
        if (target.Name == "field" && _accessor is { } accessor && !DeclaresField(accessor)
            && AccessorOwner(accessor, target.Span) is { } owner)
        {
            return new WebLingoBoundMemberAssignment(owner, "field:" + accessor.Property, value, assignment.Span);
        }
        var local = LookupLocal(target.Name);
        if (local is null)
        {
            if (_hostBuiltins.ContainsKey(target.Name))
            {
                Report(WebLingoDiagnosticCodes.InvalidOperation, $"'{target.Name}' is a built-in function and cannot be assigned.",
                    target.Span);
            }
            else
            {
                Report(WebLingoDiagnosticCodes.UndefinedIdentifier,
                    $"'{target.Name}' is not declared: declare it first, with 'var {target.Name} = …;'.", target.Span);
            }
            return new WebLingoBoundError(assignment.Span);
        }
        if (local.IsConstant)
        {
            var what = local.Kind == WebLingoDeclarationKind.Function ? "a function" : "a constant";
            Report(WebLingoDiagnosticCodes.AssignmentToConstant, $"'{target.Name}' is {what} and cannot be assigned.", target.Span);
            return new WebLingoBoundError(assignment.Span);
        }
        return local.Owner == _function
            ? new WebLingoBoundAssignment(local, value, assignment.Span)
            : new WebLingoBoundCaptureAssignment(CaptureIndex(_function, local), local, value, assignment.Span);
    }

    private WebLingoBoundExpression BindCall(WebLingoCallExpressionSyntax call)
    {
        var arguments = new List<WebLingoBoundExpression>();
        foreach (var argument in call.Arguments) arguments.Add(BindExpression(argument));

        var calleeSyntax = call.Callee;
        while (calleeSyntax is WebLingoParenthesizedExpressionSyntax parenthesized) calleeSyntax = parenthesized.Expression;
        if (calleeSyntax is WebLingoErrorExpressionSyntax) return new WebLingoBoundError(call.Span);
        if (_inPrelude && calleeSyntax is WebLingoNameSyntax { Name: "toText" } && arguments.Count == 1)
        {
            return new WebLingoBoundToText(arguments[0], call.Span);
        }
        if (_inPrelude && calleeSyntax is WebLingoNameSyntax { Name: "hold" } && arguments.Count == 2)
        {
            return new WebLingoBoundHold(arguments[0], arguments[1], call.Span);
        }
        if (calleeSyntax is WebLingoMemberAccessSyntax { Name: "import", Target: WebLingoNameSyntax { Name: "Module" } }
            && IsBuiltinClass(LookupLocal("Module"), "Module"))
        {
            // Module.import(path, values): a module imported while the program runs (section 3.9).
            if (arguments.Count is 1 or 2) return new WebLingoBoundImportDynamic(arguments[0], arguments.Count == 2 ? arguments[1] : null, call.Span);
            Report(WebLingoDiagnosticCodes.WrongArgumentCount, $"'import' takes 1 to 2 arguments, not {arguments.Count}.", call.Span);
            return new WebLingoBoundError(call.Span);
        }
        if (calleeSyntax is WebLingoSuperSyntax)
        {
            if (_class is { InConstructor: false, InExtension: false })
            {
                Report(WebLingoDiagnosticCodes.ThisOutsideMethod, "'super(…)' is only allowed in a constructor.", calleeSyntax.Span);
                return new WebLingoBoundError(call.Span);
            }
            return new WebLingoBoundCall(BindSuper("constructor", calleeSyntax.Span), arguments, call.Span);
        }
        if (calleeSyntax is WebLingoMemberAccessSyntax { Target: WebLingoSuperSyntax } superMember)
        {
            return new WebLingoBoundCall(BindSuper(superMember.Name, superMember.Span), arguments, call.Span);
        }
        if (calleeSyntax is WebLingoMemberAccessSyntax member)
        {
            // A method, called on its target (specification, section 3.6); every value has Object's toString() (section 3.7).
            if (member.Name is "toString" or "equal") UseObject();
            return new WebLingoBoundMethodCall(BindExpression(member.Target), member.Name, arguments, call.Span);
        }
        if (calleeSyntax is WebLingoIntegerLiteralSyntax or WebLingoBinary64LiteralSyntax or WebLingoBooleanLiteralSyntax
            or WebLingoNullLiteralSyntax or WebLingoStringLiteralSyntax)
        {
            Report(WebLingoDiagnosticCodes.NotAFunction, "A literal is not a function, and cannot be called.", calleeSyntax.Span);
            return new WebLingoBoundError(call.Span);
        }
        if (calleeSyntax is WebLingoNameSyntax className && LookupLocal(className.Name) is { Kind: WebLingoDeclarationKind.Class })
        {
            Report(WebLingoDiagnosticCodes.NotAFunction, $"'{className.Name}' is a class: make an instance with 'new {className.Name}(…)'.",
                calleeSyntax.Span);
            return new WebLingoBoundError(call.Span);
        }
        if (calleeSyntax is WebLingoNameSyntax name && LookupLocal(name.Name) is null
            && _hostBuiltins.TryGetValue(name.Name, out var declaration))
        {
            if (declaration.RendersComponent)
            {
                // name(path, values): a component's render (section 13), a dynamic import that runs its file anew.
                if (!CheckArity(name.Name, 2, arguments.Count, call.Span, 1)) return new WebLingoBoundError(call.Span);
                var values = arguments.Count == 2 ? arguments[1] : null;
                return new WebLingoBoundImportDynamic(arguments[0], values, call.Span) { Anew = true };
            }
            // A built-in called by its name: checked now, and called directly.
            if (!CheckArity(name.Name, declaration.Arity, arguments.Count, call.Span)) return new WebLingoBoundError(call.Span);
            return new WebLingoBoundBuiltinCall(UseBuiltin(declaration), arguments, call.Span);
        }
        if (calleeSyntax is WebLingoNameSyntax functionName && LookupLocal(functionName.Name) is { FunctionArity: { } arity } function
            && !CheckArity(functionName.Name, arity, arguments.Count, call.Span, function.FunctionMinArity ?? arity))
        {
            return new WebLingoBoundError(call.Span);
        }
        return new WebLingoBoundCall(BindExpression(calleeSyntax), arguments, call.Span);
    }

    private bool CheckArity(string name, int arity, int count, WebLingoSourceSpan span, int? minimum = null)
    {
        var least = minimum ?? arity;
        if (count >= least && count <= arity) return true;
        var noun = arity == 1 ? "argument" : "arguments";
        var takes = least == arity ? $"{arity} {noun}" : $"{least} to {arity} arguments";
        Report(WebLingoDiagnosticCodes.WrongArgumentCount, $"'{name}' takes {takes}, not {count}.", span);
        return false;
    }

    private WebLingoBuiltinSymbol UseBuiltin(WebLingoBuiltinDeclaration declaration)
    {
        if (_usedBuiltins.TryGetValue(declaration.Name, out var symbol)) return symbol;
        symbol = new WebLingoBuiltinSymbol(declaration.Name, declaration.Arity, _builtinsInOrder.Count);
        _usedBuiltins.Add(declaration.Name, symbol);
        _builtinsInOrder.Add(symbol);
        return symbol;
    }

    private WebLingoLocalSymbol? LookupLocal(string name)
    {
        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Locals.TryGetValue(name, out var local)) return local;
        }
        return name switch
        {
            "Object" => UseObject(),
            "Error" => UseError(),
            _ when WebLingoPrelude.IsValueClass(name) || WebLingoPrelude.IsErrorClass(name) => UseBuiltinClass(name),
            _ => null
        };
    }

    /// <summary>
    /// A built-in class besides <c>Object</c> and <c>Error</c>, a kind of value's or of runtime error's (section 3.7):
    /// bound, in the scope around the program's, and made where the program begins, once something uses it.
    /// </summary>
    private WebLingoLocalSymbol UseBuiltinClass(string name)
    {
        foreach (var (builtin, declaration) in _builtinClasses)
        {
            if (builtin == name) return declaration.Local;
        }
        var isError = WebLingoPrelude.IsErrorClass(name);
        var baseLocal = isError ? UseError() : UseObject();
        foreach (var (builtin, declaration) in _builtinClasses)
        {
            if (builtin == name) return declaration.Local; // UseError() makes every runtime error's class.
        }
        return BindBuiltinClass(name, WebLingoPrelude.Source(name), baseLocal, inPrelude: true);
    }

    /// <summary>Binds a built-in class from its source, extending <paramref name="baseLocal"/>, or nothing if null.</summary>
    private WebLingoLocalSymbol BindBuiltinClass(string name, string source, WebLingoLocalSymbol? baseLocal, bool inPrelude = false)
    {
        var local = new WebLingoLocalSymbol(name, WebLingoDeclarationKind.Class, _main.Locals.Count, default, _main);
        _main.Locals.Add(local);
        _prelude.Locals.Add(name, local);
        var syntax = (WebLingoClassDeclarationSyntax)new WebLingoParser(source, name).ParseCompilationUnit().Root.Statements[0];
        var (outerScope, outerFunction, outerClass) = (_scope, _function, _class);
        (_scope, _function, _inPrelude) = (_prelude, _main, inPrelude);
        var baseClass = baseLocal is null ? null : Reference(baseLocal, default);
        var declaration = new WebLingoBoundClassDeclaration(local, BindClass(syntax, local, baseClass, []), default);
        (_scope, _function, _class, _inPrelude) = (outerScope, outerFunction, outerClass, false);
        _builtinClasses.Add((name, declaration));
        return local;
    }

    /// <summary>
    /// The built-in class <c>Error</c>, beside <c>Object</c>, which it extends: bound, and made where the program begins,
    /// once something uses it, a <c>try</c> or a <c>using</c>, whose handlers may catch a runtime error, included.
    /// </summary>
    private WebLingoLocalSymbol UseError()
    {
        if (_error is not null) return _error.Local;
        var local = BindBuiltinClass("Error", ErrorSource, UseObject());
        _error = _builtinClasses[^1].Declaration;
        // Its runtime errors become instances of their kinds' classes, which the program has too.
        foreach (var (name, _) in WebLingoPrelude.ErrorClasses) BindBuiltinClass(name, WebLingoPrelude.Source(name), local);
        return local;
    }

    /// <summary>
    /// The built-in class <c>Object</c>: a local of the program, in a scope around the program's, so that a program may
    /// declare a name <c>Object</c> of its own; bound, and made where the program begins, once something uses it.
    /// </summary>
    private WebLingoLocalSymbol UseObject()
    {
        if (_object is not null) return _object.Local;
        var local = BindBuiltinClass("Object", ObjectSource, null, inPrelude: true);
        _object = _builtinClasses[^1].Declaration;
        return local;
    }

    private void Report(string code, string message, WebLingoSourceSpan span) =>
        _diagnostics.Add(new WebLingoDiagnostic(code, WebLingoDiagnosticSeverity.Error, message, span) { Source = _source });

    /// <summary>A block's scope, chained through Parent; <see cref="Function"/> owns the locals declared in it.</summary>
    private sealed class Scope(Scope? parent, FunctionContext function)
    {
        public Scope? Parent { get; } = parent;
        public FunctionContext Function { get; } = function;
        /// <summary>The names declared so far in this block, each from its declaration on.</summary>
        public Dictionary<string, WebLingoLocalSymbol> Locals { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A function being bound: the program, or one nested in it.</summary>
    /// <summary>A class whose methods are being bound: its local, whether it extends another, and whether in its constructor.</summary>
    private sealed record ClassContext(WebLingoLocalSymbol? Local, bool HasBase, bool InConstructor)
    {
        /// <summary>Whether it is an extension's, whose methods have no <c>super</c> (section 3.7).</summary>
        public bool InExtension { get; init; }
    }

    private sealed class FunctionContext(FunctionContext? parent, string name)
    {
        public FunctionContext? Parent { get; } = parent;
        public string Name { get; } = name;
        public List<WebLingoLocalSymbol> Locals { get; } = [];
        public List<WebLingoBoundCapture> Captures { get; } = [];
        public Dictionary<WebLingoLocalSymbol, int> CaptureIndexes { get; } = [];
        public int LoopDepth { get; set; }
        /// <summary>Whether it is <c>async</c>, where <c>await</c> may stand, as at a script's top level (section 3.8).</summary>
        public bool IsAsync { get; set; }
        /// <summary>Whether it awaits: for the program's top level, which is then an <c>async</c> function.</summary>
        public bool Awaits { get; set; }
        /// <summary>Whether it is a file's top level, an imported module's (section 3.9), where <c>return</c> may not stand.</summary>
        public bool IsUnit { get; init; }
        /// <summary>For a setter: the local holding the value assigned, which it gives (section 3.7).</summary>
        public WebLingoLocalSymbol? SetterValue { get; set; }
        /// <summary>The hidden locals of the <c>catch</c> blocks being bound, innermost last, which <c>throw;</c> throws from.</summary>
        public List<(WebLingoLocalSymbol Value, WebLingoLocalSymbol Origin)> Catches { get; } = [];
        /// <summary>The loop depth where each <c>finally</c> block being bound begins, innermost last (WL2012).</summary>
        public List<int> Finallys { get; } = [];
    }
}
