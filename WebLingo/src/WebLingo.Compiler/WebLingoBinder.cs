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
    /// <summary>The class that spreads values into a collection's literal or a call's arguments, once a program spreads one.</summary>
    private WebLingoLocalSymbol? _spread;
    /// <summary>The extension of <c>Array</c> with the methods that take a function, once a program names one.</summary>
    private WebLingoBoundStatement? _arrayMethods;

    /// <summary>
    /// <c>Object</c>, which every class without <c>extends</c> extends: <c>equal(other)</c> gives <c>this == other</c>,
    /// and <c>toString()</c> the text <c>write</c> writes, an instance's class and fields; any class may override them
    /// (section 3.7).
    /// </summary>
    private const string ObjectSource =
        "class Object { equal(other) { return this == other; } toString() { return toText(this); } }";

    /// <summary>Whether <c>Object</c> is being bound, whose <c>toString()</c> calls <c>toText</c>, which nothing else has.</summary>
    private bool _inPrelude;

    /// <summary>While a native class is bound, the built-ins its members call, by their names, which no program can write (section 3.9).</summary>
    private Dictionary<string, WebLingoBuiltinDeclaration>? _nativeCallees;

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

    /// <summary>The program's parameters its host gives as instances of native classes (section 6.12).</summary>
    private readonly IReadOnlyList<WebLingoHostParameter> _hostParameters;

    /// <summary>The native classes the host's parameters are instances of, each bound once, by name.</summary>
    private readonly Dictionary<string, WebLingoLocalSymbol> _hostClasses = new(StringComparer.Ordinal);

    /// <summary>The host's native modules, which name the standard modules an undefined name's import is suggested from.</summary>
    private readonly IWebLingoNativeResolver? _native;

    /// <summary>The standard modules the host has, asked for once, by the first undefined name (section 3.9).</summary>
    private IReadOnlyList<WebLingoNativeModuleInfo>? _standardModules;

    public WebLingoBinder(WebLingoCompilerOptions options)
    {
        _builtinDeclarations = options.Builtins;
        _hostParameters = options.HostParameters;
        _native = options.Native;
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
        var importedValues = isImport || mainParameters.Exists(p => HostParameter(p.Name) is null) ? DeclareHidden("values", default) : null;
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
            if (module.IsStandard && WebLingoStandardModules.Classes.TryGetValue(module.Name, out var moduleClasses))
            {
                // std:json, std:html, std:bytes, std:datetime and std:regex: the built-in classes it exports, made where the
                // program begins, as every built-in class is, whose names a program has only by importing them, a value of
                // their kinds keeping its members without (section 3.9, the owner's decisions, 18 November 2026).
                var classesExported = new List<(string, WebLingoLocalSymbol)>();
                foreach (var moduleClass in moduleClasses) classesExported.Add((moduleClass, UseBuiltinClass(moduleClass)));
                units.Add(new WebLingoBoundUnit(module.Name, null, classesExported, DeclareHidden(module.Name, default))
                {
                    Builtins = [], IsStandard = true
                });
                continue;
            }
            if (module.IsStandard && WebLingoStandardModules.Functions.TryGetValue(module.Name, out var moduleFunctions))
            {
                // std:system: the host's built-ins, its exports, those its implementation gives, which another may replace
                // (section 3.9), write, writeLn and delay among them, which a program has only by importing them (the
                // owner's decisions, 18 November 2026); a component's render is no function, and so no export (section 13).
                var functionsExported = new List<WebLingoBuiltinSymbol>();
                if (module.Name == WebLingoStandardModules.System)
                {
                    foreach (var declaration in _builtinDeclarations)
                    {
                        if (!declaration.RendersComponent) functionsExported.Add(UseBuiltin(declaration));
                    }
                }
                else
                {
                    foreach (var exportedName in moduleFunctions)
                    {
                        if (_hostBuiltins.TryGetValue(exportedName, out var given)) functionsExported.Add(UseBuiltin(given));
                    }
                }
                units.Add(new WebLingoBoundUnit(module.Name, null, [], DeclareHidden(module.Name, default))
                {
                    Builtins = functionsExported, IsStandard = true
                });
                continue;
            }
            if (module.Native is { } native)
            {
                // A native module: its functions, the host's built-ins, and its classes, made with the built-in ones, its
                // exports, made where the program begins (section 3.9).
                var functions = new List<WebLingoBuiltinSymbol>();
                var names = new List<string>();
                var classes = new List<(string, WebLingoLocalSymbol)>();
                foreach (var export in native.Exports)
                {
                    if (export.Optional > 0 || export.Rest)
                    {
                        // One whose last parameters a call may leave out, or whose last is a rest parameter: a function
                        // written in WebLingo, which calls it.
                        classes.Add((export.Name, BindNativeFunction(module.Name, export)));
                        continue;
                    }
                    functions.Add(UseBuiltin(new WebLingoBuiltinDeclaration(export.Builtin, export.Arity)));
                    names.Add(export.Name);
                }
                foreach (var nativeClass in native.Classes) classes.Add((nativeClass.Name, BindNativeClass(module.Name, nativeClass)));
                foreach (var constant in native.Constants) classes.Add((constant.Name, BindNativeConstant(module.Name, constant)));
                var nativeInstance = DeclareHidden(module.Name, default);
                units.Add(new WebLingoBoundUnit(module.Name, null, classes, nativeInstance) { Builtins = functions, BuiltinNames = names });
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
                // A default is bound before its parameter is declared, as a variable's initializer is (section 3.1); a host's
                // parameter, a layout's or a component's request, is the host's unless its importer gives another (section 6.12).
                var hosted = parameter.Default is null ? HostParameter(parameter.Name) : null;
                var value = parameter.Default is { } given ? BindExpression(given)
                    : hosted is not null ? MakeHostParameter(hosted, parameter.Span) : null;
                if (Declare(parameter.Name, WebLingoDeclarationKind.Const, parameter.NameSpan, null) is { } local)
                {
                    body.Add(new WebLingoBoundModuleParameter(local, values!, parameter.Name, value, parameter.Span));
                    declared.Add(new WebLingoModuleParameter(parameter.Name, value is not null));
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
        var madeFirst = new List<WebLingoBoundStatement>(_nativeFunctions);
        if (_arrayMethods is not null) madeFirst.Add(_arrayMethods); // After the classes it extends and uses (section 3.6).
        builtins.AddRange(madeFirst);
        statements.InsertRange(0, builtins);
        return new WebLingoBoundProgram(statements, _function.Locals, _builtinsInOrder)
        {
            ErrorClass = _error?.Class,
            ObjectClass = _object?.Class,
            BuiltinClasses = builtinClasses,
            IsAsync = _main.Awaits,
            IsImport = isImport,
            Parameters = programParameters,
            Units = units,
            MadeFirst = madeFirst
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
            if (HostParameter(parameter.Name) is { } hosted)
            {
                // The host's (section 6.12): an instance its built-in makes, given its classes, when the statement runs,
                // before the program's imports, which may give it on.
                var made = MakeHostParameter(hosted, parameter.Span);
                if (Declare(parameter.Name, WebLingoDeclarationKind.Const, parameter.NameSpan, null) is { } hostLocal)
                {
                    statements.Add(new WebLingoBoundVariableDeclaration(hostLocal, made, parameter.Span) { IsHostParameter = true });
                }
                continue;
            }
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
    /// A statement that calls a built-in which writes output (<see cref="WebLingoBuiltinDeclaration.Writes"/>), followed,
    /// where the program can <c>await</c>, by a wait for its output, if it holds more than its reader has taken (section
    /// 6.12, "Streaming"): <c>if (full()) await drained();</c>, each a host's built-in no program can name.
    /// </summary>
    private WebLingoBoundStatement AfterWrite(WebLingoBoundExpressionStatement call, string written)
    {
        if (!_hostBuiltins.TryGetValue(written, out var declaration) || !declaration.Writes || (_function != _main && !_function.IsAsync)
            || !_hostBuiltins.TryGetValue(WebLingoTemplateWriters.Full, out var full)
            || !_hostBuiltins.TryGetValue(WebLingoTemplateWriters.Drained, out var drained))
        {
            return call;
        }
        if (_function == _main) _main.Awaits = true;
        var span = call.Span;
        var wait = new WebLingoBoundExpressionStatement(new WebLingoBoundAwait(new WebLingoBoundBuiltinCall(UseBuiltin(drained), [], span), span), span);
        // A template's own writer says itself whether the output is full, which saves a call for each of a page's writes.
        if (WebLingoTemplateWriters.IsWriter(written)) return new WebLingoBoundIf(call.Expression, wait, null, span);
        var check = new WebLingoBoundIf(new WebLingoBoundBuiltinCall(UseBuiltin(full), [], span), wait, null, span);
        return new WebLingoBoundBlock([call, check], span);
    }

    /// <summary>The host's instance for its parameter: its built-in's, given its classes (section 6.12).</summary>
    private WebLingoBoundBuiltinCall MakeHostParameter(WebLingoHostParameter hosted, WebLingoSourceSpan span)
    {
        var arguments = new List<WebLingoBoundExpression>();
        foreach (var info in hosted.Classes) arguments.Add(Reference(HostClass(info), span));
        return new WebLingoBoundBuiltinCall(UseBuiltin(new WebLingoBuiltinDeclaration(hosted.Builtin, arguments.Count)), arguments, span);
    }

    /// <summary>The host's parameter of that name, or null (section 6.12).</summary>
    private WebLingoHostParameter? HostParameter(string name)
    {
        foreach (var parameter in _hostParameters)
        {
            if (parameter.Name == name) return parameter;
        }
        return null;
    }

    /// <summary>A native class a host's parameter is an instance of, bound once, as a native module's class is, under <c>host</c>.</summary>
    private WebLingoLocalSymbol HostClass(WebLingoNativeClassInfo info)
    {
        if (!_hostClasses.TryGetValue(info.Name, out var local))
        {
            local = BindNativeClass("host", info);
            _hostClasses.Add(info.Name, local);
        }
        return local;
    }

    /// <summary>
    /// The values an import gives its module's parameters, a map (section 3.9): each a parameter's, and every parameter
    /// without a default given (WL2016 otherwise, at compile time); null for a module without parameters given none.
    /// </summary>
    private WebLingoBoundExpression? BindImportValues(WebLingoImportSyntax import, int unit)
    {
        var parameters = _moduleSources[unit].IsStandard || _moduleSources[unit].Native is not null ? [] : ParametersOf(_moduleSources[unit].Root);
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
            if (parameter.Default is null && !given.Contains(parameter.Name) && HostParameter(parameter.Name) is null)
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
                    // A standard module's host's built-in, std:system's writeLn, called directly as one every program had was:
                    // any the host gives, for std:system, which exports them all.
                    var standard = _moduleSources[unit];
                    if (standard.IsStandard && (standard.Name == WebLingoStandardModules.System
                            || WebLingoStandardModules.ExportsFunction(standard.Name, imported.Name))
                        && _hostBuiltins.TryGetValue(imported.Name, out var hosted) && !hosted.RendersComponent)
                    {
                        alias.ImportedBuiltin = hosted;
                    }
                }
            }
            imports.Add(import, new WebLingoBoundImport(unit, instance, import.Span) { Values = BindImportValues(import, unit) });
            // Its imports are awaited (section 3.9), but a standard module's the compiler makes, which nothing runs.
            if (_function == _main && !_moduleSources[unit].IsStandard) _main.Awaits = true;
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
        if (statement is WebLingoSwitchSyntax switched) return BindSwitch(switched);
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
            {
                if (expressionStatement.Expression is WebLingoCallExpressionSyntax
                    {
                        Callee: WebLingoMemberAccessSyntax { Name: "sort" or "reverse", Target: not WebLingoSuperSyntax } discarded
                    })
                {
                    // An array's sort() and reverse() give a new array, the array as it was (the owner's answer, 16 November 2026).
                    _diagnostics.Add(new WebLingoDiagnostic(WebLingoDiagnosticCodes.DiscardedResult, WebLingoDiagnosticSeverity.Warning,
                        $"'{discarded.Name}' gives a new array, and leaves the one it is called on as it was: its result is discarded here.",
                        discarded.NameSpan) { Source = _source });
                }
                var bound = new WebLingoBoundExpressionStatement(BindExpression(expressionStatement.Expression), statement.Span);
                return bound.Expression is WebLingoBoundBuiltinCall { Builtin.Name: var written } ? AfterWrite(bound, written) : bound;
            }
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
        var (outerClass, outerAccessor) = (_class, _accessor);
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
            (_class, _accessor) = (outerClass, outerAccessor);
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
            (_class, _accessor) = (outerClass, outerAccessor);
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
        var (outerClass, outerAccessor) = (_class, _accessor);
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
            (_class, _accessor) = (outerClass, outerAccessor);
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
        // An outer method's 'this' is not a static method's (WL2009), but for one with a receiver, its class, a native
        // class's factory (section 3.7, "No sealed classes").
        if (isStatic && !receiver) _scope.Locals.Add("this", _noThis);
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
            Source = _source,
            IsNative = _nativeCallees is not null,
            HasRest = parameters.Count > 0 && parameters[^1].IsRest
        };
        _function = outerFunction;
        _scope = outerScope;
        (_class, _accessor) = (outerClass, outerAccessor);
        return bound;
    }

    /// <summary>A function declared by name, with how many arguments a call of it may give (section 3.5).</summary>
    private WebLingoLocalSymbol? DeclareFunction(WebLingoFunctionDeclarationSyntax function)
    {
        var rest = function.Parameters.Count > 0 && function.Parameters[^1].IsRest;
        var local = Declare(function.Name, WebLingoDeclarationKind.Function, function.NameSpan, rest ? AnyNumber : function.Parameters.Count);
        if (local is null) return null;
        var required = 0;
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Default is null && !parameter.IsRest) required++;
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
    /// <summary>How many temporaries the compiler has declared, each a variable of a name no program can write.</summary>
    private int _temporaries;

    /// <summary>
    /// A temporary, a variable in the current scope, holding <paramref name="value"/> once <paramref name="store"/> has run:
    /// what the compiler writes so that an expression it repeats is evaluated once (section 3.5).
    /// </summary>
    private WebLingoNameSyntax Temporary(WebLingoExpressionSyntax value, WebLingoSourceSpan span, out WebLingoExpressionSyntax store)
    {
        var name = "<temporary " + (_temporaries++).ToString(System.Globalization.CultureInfo.InvariantCulture) + ">";
        Declare(name, WebLingoDeclarationKind.Var, span, null);
        var reference = new WebLingoNameSyntax(name, span);
        store = new WebLingoAssignmentExpressionSyntax(reference, value, span);
        return reference;
    }

    /// <summary>Whether the subject held in <paramref name="subject"/> matches any of the patterns: by <c>==</c>, or by its class.</summary>
    private static WebLingoExpressionSyntax Matches(WebLingoNameSyntax subject, IReadOnlyList<WebLingoSwitchPattern> patterns)
    {
        WebLingoExpressionSyntax? any = null;
        foreach (var pattern in patterns)
        {
            WebLingoExpressionSyntax test = pattern.Class is { } type
                ? new WebLingoIsExpressionSyntax(subject, type, pattern.Span)
                : new WebLingoBinaryExpressionSyntax(WebLingoBinaryOperator.Equal, subject, pattern.Value!, pattern.Span, pattern.Span);
            any = any is null ? test : new WebLingoBinaryExpressionSyntax(WebLingoBinaryOperator.Or, any, test, pattern.Span, pattern.Span);
        }
        return any!;
    }

    /// <summary>
    /// <c>switch (value) { … }</c> as the <c>if</c>s it means (section 3.5): its value held once, each case's patterns
    /// tested in order, a class's pattern naming the value in its block; so that <c>break</c> and <c>continue</c> in a
    /// case act on the loop around the <c>switch</c>, as in an <c>if</c>.
    /// </summary>
    private WebLingoBoundStatement BindSwitch(WebLingoSwitchSyntax switched)
    {
        var name = "<temporary " + (_temporaries++).ToString(System.Globalization.CultureInfo.InvariantCulture) + ">";
        var subject = new WebLingoNameSyntax(name, switched.Subject.Span);
        WebLingoStatementSyntax? chain = switched.Default;
        for (var i = switched.Cases.Count - 1; i >= 0; i--)
        {
            var (patterns, body) = (switched.Cases[i].Patterns, switched.Cases[i].Body);
            WebLingoStatementSyntax then = body;
            if (patterns is [{ Binding: { } binding } pattern])
            {
                then = new WebLingoBlockSyntax(
                    [new WebLingoVariableDeclarationSyntax(WebLingoDeclarationKind.Var, binding, pattern.Span, subject, pattern.Span), body], body.Span);
            }
            chain = new WebLingoIfSyntax(Matches(subject, patterns), then, chain, body.Span);
        }
        var held = new WebLingoVariableDeclarationSyntax(WebLingoDeclarationKind.Var, name, switched.Subject.Span, switched.Subject,
            switched.Subject.Span);
        return BindStatement(new WebLingoBlockSyntax(chain is null ? [held] : [held, chain], switched.Span));
    }

    /// <summary>
    /// <c>value switch { … }</c> as the conditionals it means (section 3.5): its value held once, its arms tested in order,
    /// <c>_</c>'s last, which it must have, a class's pattern naming the value in its arm's result only.
    /// </summary>
    private WebLingoBoundExpression BindSwitchExpression(WebLingoSwitchExpressionSyntax switched)
    {
        var subject = Temporary(switched.Subject, switched.Subject.Span, out var store);
        var stored = BindExpression(store);
        WebLingoSwitchArm? otherwise = null;
        foreach (var arm in switched.Arms)
        {
            if (arm.IsDefault) otherwise ??= arm;
        }
        if (otherwise is null)
        {
            Report(WebLingoDiagnosticCodes.UnexpectedToken, "A 'switch' expression needs a '_ => …' arm, for a value no other arm matches.",
                switched.Span);
            return new WebLingoBoundError(switched.Span);
        }
        var chain = BindExpression(otherwise.Value);
        for (var i = switched.Arms.Count - 1; i >= 0; i--)
        {
            var arm = switched.Arms[i];
            if (arm.IsDefault) continue;
            var test = BindExpression(Matches(subject, arm.Patterns));
            WebLingoBoundExpression result;
            if (arm.Patterns is [{ Binding: { } binding } pattern])
            {
                _scope = new Scope(_scope, _function);
                Declare(binding, WebLingoDeclarationKind.Var, pattern.Span, null);
                var named = BindExpression(new WebLingoAssignmentExpressionSyntax(new WebLingoNameSyntax(binding, pattern.Span), subject, pattern.Span));
                result = new WebLingoBoundSequence(named, BindExpression(arm.Value), arm.Value.Span);
                _scope = _scope.Parent!;
            }
            else
            {
                result = BindExpression(arm.Value);
            }
            chain = new WebLingoBoundConditional(test, result, chain, arm.Value.Span);
        }
        return new WebLingoBoundSequence(stored, chain, switched.Span);
    }

    private static WebLingoExpressionSyntax IsNull(WebLingoExpressionSyntax value, WebLingoSourceSpan span) =>
        new WebLingoBinaryExpressionSyntax(WebLingoBinaryOperator.Equal, value, new WebLingoNullLiteralSyntax(span), span, span);

    /// <summary><c>left ?? right</c>: <c>left</c>, evaluated once, unless it is <c>null</c>, and only then <c>right</c> (section 3.5).</summary>
    private WebLingoExpressionSyntax Coalesce(WebLingoExpressionSyntax left, WebLingoExpressionSyntax right, WebLingoSourceSpan span)
    {
        if (left is WebLingoNameSyntax name) return new WebLingoConditionalExpressionSyntax(IsNull(name, span), right, name, span);
        var held = Temporary(left, span, out var store);
        return new WebLingoSequenceSyntax(store, new WebLingoConditionalExpressionSyntax(IsNull(held, span), right, held, span), span);
    }

    /// <summary>
    /// What a compound assignment, an increment or a decrement changes, held once (section 3.5): a variable as it is; an
    /// element's target and index, and a member's target, in temporaries, whose stores go to <paramref name="steps"/>.
    /// </summary>
    private (WebLingoExpressionSyntax Read, Func<WebLingoExpressionSyntax, WebLingoExpressionSyntax> Write) Place(
        WebLingoExpressionSyntax target, List<WebLingoExpressionSyntax> steps, WebLingoSourceSpan span)
    {
        switch (target)
        {
            case WebLingoIndexExpressionSyntax element:
            {
                var held = Temporary(element.Target, element.Target.Span, out var storeTarget);
                var at = Temporary(element.Index, element.Index.Span, out var storeIndex);
                steps.Add(storeTarget);
                steps.Add(storeIndex);
                var read = new WebLingoIndexExpressionSyntax(held, at, element.Span);
                return (read, value => new WebLingoIndexAssignmentSyntax(read, value, span));
            }
            case WebLingoMemberAccessSyntax member:
            {
                var held = Temporary(member.Target, member.Target.Span, out var storeTarget);
                steps.Add(storeTarget);
                var read = new WebLingoMemberAccessSyntax(held, member.Name, member.NameSpan, member.Span);
                return (read, value => new WebLingoMemberAssignmentSyntax(read, value, span));
            }
            default:
            {
                var name = (WebLingoNameSyntax)target;
                return (name, value => new WebLingoAssignmentExpressionSyntax(name, value, span));
            }
        }
    }

    private static WebLingoExpressionSyntax InOrder(List<WebLingoExpressionSyntax> steps, WebLingoExpressionSyntax last, WebLingoSourceSpan span)
    {
        for (var i = steps.Count - 1; i >= 0; i--) last = new WebLingoSequenceSyntax(steps[i], last, span);
        return last;
    }

    /// <summary>
    /// <c>target op= value</c> as <c>target = target op value</c>, its target held once; <c>target ??= value</c> assigning
    /// only a <c>null</c>, its value the target's otherwise (section 3.5).
    /// </summary>
    private WebLingoExpressionSyntax Compound(WebLingoCompoundAssignmentSyntax compound)
    {
        var steps = new List<WebLingoExpressionSyntax>();
        var (read, write) = Place(compound.Target, steps, compound.Span);
        if (compound.Operator != WebLingoBinaryOperator.Coalesce)
        {
            var combined = new WebLingoBinaryExpressionSyntax(compound.Operator, read, compound.Value, compound.OperatorSpan, compound.Span);
            return InOrder(steps, write(combined), compound.Span);
        }
        var current = Temporary(read, compound.Span, out var storeCurrent);
        steps.Add(storeCurrent);
        return InOrder(steps, new WebLingoConditionalExpressionSyntax(IsNull(current, compound.Span), write(compound.Value), current,
            compound.Span), compound.Span);
    }

    /// <summary><c>++x</c> as <c>x += 1</c>; <c>x++</c> the same, its value the old one (section 3.5).</summary>
    private WebLingoExpressionSyntax Increment(WebLingoIncrementSyntax increment)
    {
        var steps = new List<WebLingoExpressionSyntax>();
        var (read, write) = Place(increment.Target, steps, increment.Span);
        var op = increment.IsIncrement ? WebLingoBinaryOperator.Add : WebLingoBinaryOperator.Subtract;
        var one = new WebLingoIntegerLiteralSyntax(1, increment.OperatorSpan);
        if (increment.IsPrefix)
        {
            return InOrder(steps, write(new WebLingoBinaryExpressionSyntax(op, read, one, increment.OperatorSpan, increment.Span)), increment.Span);
        }
        var old = Temporary(read, increment.Span, out var storeOld);
        steps.Add(storeOld);
        var changed = write(new WebLingoBinaryExpressionSyntax(op, old, one, increment.OperatorSpan, increment.Span));
        return InOrder(steps, new WebLingoSequenceSyntax(changed, old, increment.Span), increment.Span);
    }

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
            case WebLingoConditionalExpressionSyntax conditional:
                return new WebLingoBoundConditional(BindExpression(conditional.Condition), BindExpression(conditional.WhenTrue),
                    BindExpression(conditional.WhenFalse), conditional.Span);
            case WebLingoSequenceSyntax sequence:
                return new WebLingoBoundSequence(BindExpression(sequence.First), BindExpression(sequence.Then), sequence.Span);
            case WebLingoBinaryExpressionSyntax { Operator: WebLingoBinaryOperator.Coalesce } coalesce:
                return BindExpression(Coalesce(coalesce.Left, coalesce.Right, coalesce.Span));
            case WebLingoCompoundAssignmentSyntax compound:
                return BindExpression(Compound(compound));
            case WebLingoIncrementSyntax increment:
                return BindExpression(Increment(increment));
            case WebLingoSwitchExpressionSyntax switched:
                return BindSwitchExpression(switched);
            case WebLingoIntegerLiteralSyntax literal:
                return new WebLingoBoundIntegerLiteral(literal.Value, literal.Span);
            case WebLingoBinary64LiteralSyntax literal:
                return new WebLingoBoundBinary64Literal(literal.Value, literal.Span);
            case WebLingoDecimalLiteralSyntax literal:
                return new WebLingoBoundDecimalLiteral(literal.Digits, literal.Span);
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
            case WebLingoArrayLiteralSyntax array when HasSpread(array.Elements):
                return BindSpread("array", array.Elements, array.Span);
            case WebLingoSetLiteralSyntax set when HasSpread(set.Elements):
                return BindSpread("set", set.Elements, set.Span);
            case WebLingoMapLiteralSyntax map when HasSpreadEntry(map.Entries):
                return BindSpreadMap(map);
            case WebLingoSpreadSyntax spread:
                Report(WebLingoDiagnosticCodes.UnexpectedToken,
                    "'...' spreads values only into a call's arguments, or an array's, a set's or a map's literal.", spread.Span);
                return new WebLingoBoundError(spread.Span);
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
                if (IsArrayMethodName(member.Name)) UseArrayMethods();
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
                if (HasSpread(created.Arguments))
                {
                    // new Class(...values): one array, whose elements the VMs give the constructor (section 3.5).
                    return new WebLingoBoundNew(target, [BindSpread("array", created.Arguments, created.Span)], created.Span) { IsSpread = true };
                }
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
            if (local.ImportedBuiltin is { } imported) return new WebLingoBoundBuiltinReference(UseBuiltin(imported), name.Span);
            // A name an import takes is its module's export, read as it is when read (section 3.9).
            return local.ImportOf is { } module
                ? new WebLingoBoundMember(Reference(module, name.Span), local.ImportName!, name.Span)
                : Reference(local, name.Span);
        }
        if (_hostBuiltins.TryGetValue(name.Name, out var builtin) && !WebLingoStandardModules.IsImportedFunction(name.Name))
        {
            if (!builtin.RendersComponent) return new WebLingoBoundBuiltinReference(UseBuiltin(builtin), name.Span);
            Report(WebLingoDiagnosticCodes.InvalidOperation, $"'{name.Name}' renders a component, and can only be called.",
                name.Span);
            return new WebLingoBoundError(name.Span);
        }
        var suggestion = Suggestion(name.Name);
        var message = suggestion.Length > 0 ? $"'{name.Name}' is not defined{suggestion}" : $"'{name.Name}' is not defined.";
        Report(WebLingoDiagnosticCodes.UndefinedIdentifier, message, name.Span);
        return new WebLingoBoundError(name.Span);
    }

    /// <summary>
    /// What a name that is not defined may have been meant as, for its message (section 3.9, the owner's decision, 18
    /// November 2026): a name in scope it differs from only by case, <c>; did you mean Json?</c>; else a standard module's
    /// export of that name, or of one it differs from only by case, <c>; did you mean Json, from std:json?</c>, so that a
    /// script learns the import it needs; else nothing.
    /// </summary>
    private string Suggestion(string name)
    {
        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            foreach (var declared in scope.Locals.Keys)
            {
                if (IsCaseOf(declared, name)) return $"; did you mean {declared}?";
            }
        }
        foreach (var known in EveryProgramsNames())
        {
            if (IsCaseOf(known, name)) return $"; did you mean {known}?";
        }
        string? byCase = null;
        _standardModules ??= _native?.StandardModules() ?? [];
        var modules = new List<(string Name, List<string> Exports)>();
        foreach (var (module, classes) in WebLingoStandardModules.Classes) modules.Add((module, new List<string>(classes)));
        foreach (var (module, functions) in WebLingoStandardModules.Functions) modules.Add((module, new List<string>(functions)));
        foreach (var module in _standardModules)
        {
            var exports = new List<string>();
            foreach (var export in module.Exports) exports.Add(export.Name);
            foreach (var nativeClass in module.Classes) exports.Add(nativeClass.Name);
            foreach (var constant in module.Constants) exports.Add(constant.Name);
            modules.Add((module.Name, exports));
        }
        foreach (var (module, exports) in modules)
        {
            foreach (var export in exports)
            {
                if (export == name) return $"; did you mean {export}, from {module}?";
                if (byCase is null && IsCaseOf(export, name)) byCase = $"; did you mean {export}, from {module}?";
            }
        }
        return byCase ?? "";
    }

    /// <summary>
    /// The names every program has without declaring them: the built-in classes', but a standard module's, and the host's
    /// built-ins'. A list, not an iterator, whose state machine reads <c>System.Environment</c>, which the portable core
    /// does not (<c>PortabilityTests</c>).
    /// </summary>
    private List<string> EveryProgramsNames()
    {
        var names = new List<string> { "Object", "Error" };
        foreach (var (valueClass, _) in WebLingoPrelude.ValueClasses)
        {
            if (!WebLingoStandardModules.IsImported(valueClass)) names.Add(valueClass);
        }
        foreach (var (errorClass, _) in WebLingoPrelude.ErrorClasses) names.Add(errorClass);
        foreach (var builtin in _hostBuiltins.Keys)
        {
            if (!WebLingoStandardModules.IsImportedFunction(builtin)) names.Add(builtin);
        }
        return names;
    }

    /// <summary>
    /// The host's built-in a name means: a standard module's, where an import takes it, <c>import { writeLn } from
    /// "std:system";</c>; else, where nothing declares the name, one every program has, no standard module's (section 3.9).
    /// </summary>
    private WebLingoBuiltinDeclaration? HostBuiltinNamed(string name)
    {
        if (LookupLocal(name) is { } local) return local.ImportedBuiltin;
        return _hostBuiltins.TryGetValue(name, out var declaration) && !WebLingoStandardModules.IsImportedFunction(name) ? declaration : null;
    }

    /// <summary>Whether <paramref name="known"/>, a name a program can write, differs from <paramref name="name"/> only by case.</summary>
    private static bool IsCaseOf(string known, string name)
    {
        if (known == name || !string.Equals(known, name, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var c in known)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }
        return true;
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
        if (HasSpread(call.Arguments)) return BindSpreadCall(call);
        var arguments = new List<WebLingoBoundExpression>();
        foreach (var argument in call.Arguments) arguments.Add(BindExpression(argument));

        var calleeSyntax = call.Callee;
        while (calleeSyntax is WebLingoParenthesizedExpressionSyntax parenthesized) calleeSyntax = parenthesized.Expression;
        if (calleeSyntax is WebLingoErrorExpressionSyntax) return new WebLingoBoundError(call.Span);
        if (_nativeCallees is { } natives && calleeSyntax is WebLingoNameSyntax native && natives.TryGetValue(native.Name, out var nativeMember))
        {
            return new WebLingoBoundBuiltinCall(UseBuiltin(nativeMember), arguments, call.Span); // A native class's member (section 3.9).
        }
        if (_inPrelude && calleeSyntax is WebLingoNameSyntax { Name: "toJson" } && arguments.Count == 2)
        {
            return new WebLingoBoundToJson(arguments[0], arguments[1], call.Span);
        }
        if (_inPrelude && calleeSyntax is WebLingoNameSyntax { Name: "toText" } && arguments.Count == 1)
        {
            return new WebLingoBoundToText(arguments[0], call.Span);
        }
        if (_inPrelude && calleeSyntax is WebLingoNameSyntax { Name: "writtenText" } && arguments.Count == 1)
        {
            return new WebLingoBoundToText(arguments[0], call.Span, AsWritten: true); // As write writes it (section 3.5).
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
            if (IsArrayMethodName(member.Name)) UseArrayMethods();
            if (member.Name is "toString" or "equal") UseObject();
            // A collection's or a string's iterate() gives an Iterator, which the runtime makes (section 3.6).
            if (member.Name == "iterate") UseBuiltinClass("Iterator");
            return new WebLingoBoundMethodCall(BindExpression(member.Target), member.Name, arguments, call.Span);
        }
        if (calleeSyntax is WebLingoIntegerLiteralSyntax or WebLingoBinary64LiteralSyntax or WebLingoDecimalLiteralSyntax or WebLingoBooleanLiteralSyntax
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
        if (calleeSyntax is WebLingoNameSyntax name && HostBuiltinNamed(name.Name) is { } declaration)
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
            if (WebLingoTemplateWriters.TryEscapedAs(name.Name, out var escape, out _) && escape == WebLingoHtmlEscape.Script)
            {
                // A page's value in a script, <%= x %>, written as Html.js gives it, its JSON safe there (section 13,
                // "Context-aware escaping"): the built-in class's own, whatever the page names Html.
                var html = Reference(UseBuiltinClass("Html"), call.Span);
                arguments[0] = new WebLingoBoundMethodCall(html, "js", [arguments[0]], call.Span);
            }
            return new WebLingoBoundBuiltinCall(UseBuiltin(declaration), arguments, call.Span);
        }
        if (calleeSyntax is WebLingoNameSyntax functionName && LookupLocal(functionName.Name) is { FunctionArity: { } arity } function
            && !CheckArity(functionName.Name, arity, arguments.Count, call.Span, function.FunctionMinArity ?? arity))
        {
            return new WebLingoBoundError(call.Span);
        }
        return new WebLingoBoundCall(BindExpression(calleeSyntax), arguments, call.Span);
    }

    /// <summary>The arity of a function with a rest parameter, which takes any number of arguments after the others.</summary>
    private const int AnyNumber = int.MaxValue;

    /// <summary>
    /// A call with spread arguments, <c>f(a, ...values)</c> (section 3.5): its arguments one array, made as an array's
    /// literal with spread elements is, whose elements the VMs pass as the call's arguments, a function's arity checked
    /// when it runs. A built-in called by its name is called as a value; <c>Module.import</c> and a component's render,
    /// whose arguments the compiler reads, take no spread (WL1001).
    /// </summary>
    private WebLingoBoundExpression BindSpreadCall(WebLingoCallExpressionSyntax call)
    {
        var calleeSyntax = call.Callee;
        while (calleeSyntax is WebLingoParenthesizedExpressionSyntax parenthesized) calleeSyntax = parenthesized.Expression;
        if (calleeSyntax is WebLingoErrorExpressionSyntax) return new WebLingoBoundError(call.Span);
        var fixedArguments = calleeSyntax switch
        {
            WebLingoMemberAccessSyntax { Name: "import", Target: WebLingoNameSyntax { Name: "Module" } }
                when IsBuiltinClass(LookupLocal("Module"), "Module") => "Module.import",
            WebLingoNameSyntax name when LookupLocal(name.Name) is null && _hostBuiltins.TryGetValue(name.Name, out var builtin)
                && builtin.RendersComponent => name.Name,
            _ => null
        };
        if (fixedArguments is not null)
        {
            var spreadAt = call.Span;
            for (var i = call.Arguments.Count - 1; i >= 0; i--)
            {
                if (call.Arguments[i] is WebLingoSpreadSyntax spread) spreadAt = spread.Span;
            }
            Report(WebLingoDiagnosticCodes.UnexpectedToken, $"'...' cannot spread into '{fixedArguments}', whose arguments are read as written.",
                spreadAt);
            return new WebLingoBoundError(call.Span);
        }
        if (calleeSyntax is WebLingoIntegerLiteralSyntax or WebLingoBinary64LiteralSyntax or WebLingoDecimalLiteralSyntax or WebLingoBooleanLiteralSyntax
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
        if (calleeSyntax is WebLingoSuperSyntax && _class is { InConstructor: false, InExtension: false })
        {
            Report(WebLingoDiagnosticCodes.ThisOutsideMethod, "'super(…)' is only allowed in a constructor.", calleeSyntax.Span);
            return new WebLingoBoundError(call.Span);
        }
        var values = BindSpread("array", call.Arguments, call.Span);
        WebLingoBoundExpression callee;
        switch (calleeSyntax)
        {
            case WebLingoSuperSyntax:
                callee = BindSuper("constructor", calleeSyntax.Span);
                break;
            case WebLingoMemberAccessSyntax { Target: WebLingoSuperSyntax } superMember:
                callee = BindSuper(superMember.Name, superMember.Span);
                break;
            case WebLingoMemberAccessSyntax member:
                if (IsArrayMethodName(member.Name)) UseArrayMethods();
                if (member.Name is "toString" or "equal") UseObject();
                if (member.Name == "iterate") UseBuiltinClass("Iterator");
                return new WebLingoBoundMethodCall(BindExpression(member.Target), member.Name, [values], call.Span) { IsSpread = true };
            default:
                callee = BindExpression(calleeSyntax);
                break;
        }
        return new WebLingoBoundCall(callee, [values], call.Span) { IsSpread = true };
    }

    private static bool HasSpread(IReadOnlyList<WebLingoExpressionSyntax> elements)
    {
        foreach (var element in elements)
        {
            if (element is WebLingoSpreadSyntax) return true;
        }
        return false;
    }

    private static bool HasSpreadEntry(IReadOnlyList<WebLingoMapEntrySyntax> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsSpread) return true;
        }
        return false;
    }

    /// <summary>
    /// Elements some of which are spread, <c>[1, ...values, 2]</c> (section 3.5): a call of the spreading class's method,
    /// <c>array</c> or <c>set</c>, its parts in order, the plain elements grouped in arrays of their own, and the values
    /// spread, each of which it visits as <c>foreach</c> does.
    /// </summary>
    private WebLingoBoundExpression BindSpread(string method, IReadOnlyList<WebLingoExpressionSyntax> elements, WebLingoSourceSpan span)
    {
        var parts = new List<WebLingoBoundExpression>();
        List<WebLingoBoundExpression>? group = null;
        foreach (var element in elements)
        {
            if (element is not WebLingoSpreadSyntax spread)
            {
                (group ??= []).Add(BindExpression(element));
                continue;
            }
            if (group is not null) parts.Add(new WebLingoBoundArrayLiteral(group, span));
            group = null;
            parts.Add(BindExpression(spread.Value));
        }
        if (group is not null) parts.Add(new WebLingoBoundArrayLiteral(group, span));
        return new WebLingoBoundMethodCall(Reference(UseSpread(), span), method, parts, span);
    }

    /// <summary>A map's literal with spread entries, <c>{ ...defaults, size: 10 }</c> (section 3.5): the plain entries grouped in maps of their own.</summary>
    private WebLingoBoundExpression BindSpreadMap(WebLingoMapLiteralSyntax map)
    {
        var parts = new List<WebLingoBoundExpression>();
        List<(string, WebLingoBoundExpression)>? group = null;
        foreach (var entry in map.Entries)
        {
            if (!entry.IsSpread)
            {
                (group ??= []).Add((entry.Key, BindExpression(entry.Value)));
                continue;
            }
            if (group is not null) parts.Add(new WebLingoBoundMapLiteral(group, map.Span));
            group = null;
            parts.Add(BindExpression(entry.Value));
        }
        if (group is not null) parts.Add(new WebLingoBoundMapLiteral(group, map.Span));
        return new WebLingoBoundMethodCall(Reference(UseSpread(), map.Span), "map", parts, map.Span);
    }

    private bool CheckArity(string name, int arity, int count, WebLingoSourceSpan span, int? minimum = null)
    {
        var least = minimum ?? arity;
        if (count >= least && count <= arity) return true;
        var noun = arity == 1 ? "argument" : "arguments";
        var takes = arity == AnyNumber ? $"at least {least} {(least == 1 ? "argument" : "arguments")}"
            : least == arity ? $"{arity} {noun}" : $"{least} to {arity} arguments";
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
            WebLingoParser.InterpolationText => UseInterpolationText(),
            // A standard module's class, Json's and Html's, only by its import, but in the built-in classes' own sources.
            _ when WebLingoPrelude.IsValueClass(name) && (_inPrelude || !WebLingoStandardModules.IsImported(name))
                => UseBuiltinClass(name),
            _ when WebLingoPrelude.IsErrorClass(name) => UseBuiltinClass(name),
            _ => null
        };
    }

    /// <summary>
    /// A built-in class besides <c>Object</c> and <c>Error</c>, a kind of value's or of runtime error's (section 3.7):
    /// bound, in the scope around the program's, and made where the program begins, once something uses it.
    /// </summary>
    /// <summary>
    /// The class an interpolated string's values are written by (section 3.5): <c>of(value)</c> gives the text
    /// <c>write</c> writes, built as <c>write</c> builds it (<c>writtenText</c>), an instance's <c>toString()</c>
    /// included, a <c>toString()</c> that gives anything but a string WL3001, and <c>""</c> for <c>null</c>; written in
    /// WebLingo, and made where the program begins, as the built-in classes are, under a name no program can write.
    /// </summary>
    private WebLingoLocalSymbol UseInterpolationText()
    {
        foreach (var (builtin, declaration) in _builtinClasses)
        {
            if (builtin == WebLingoParser.InterpolationText) return declaration.Local;
        }
        return BindBuiltinClass(WebLingoParser.InterpolationText,
            "class Text { static of(value) { if (value == null) { return \"\"; } return writtenText(value); } }", UseObject(),
            inPrelude: true);
    }

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

    /// <summary>
    /// Binds a built-in class from its source, extending <paramref name="baseLocal"/>, or nothing if null. Its functions
    /// are native, so that the VMs report an error raised in one where the program called it (section 3.9): its positions
    /// are its source's, which is no file of the program's, and would name some other place in the caller's file.
    /// </summary>
    private WebLingoLocalSymbol BindBuiltinClass(string name, string source, WebLingoLocalSymbol? baseLocal, bool inPrelude = false)
    {
        var tree = new WebLingoParser(source, name).ParseCompilationUnit();
        foreach (var diagnostic in tree.Diagnostics)
        {
            // The compiler's own source, which no program can fix: a fault of the compiler's, never reported as a program's.
            if (diagnostic.Severity == WebLingoDiagnosticSeverity.Error)
            {
                throw new InvalidOperationException($"The built-in class {name}'s source does not parse: {diagnostic.Message}");
            }
        }
        var outer = _nativeCallees;
        _nativeCallees ??= new Dictionary<string, WebLingoBuiltinDeclaration>(StringComparer.Ordinal);
        var made = BindBuiltinClass(name, (WebLingoClassDeclarationSyntax)tree.Root.Statements[0], baseLocal, inPrelude);
        _nativeCallees = outer;
        return made;
    }

    /// <summary>
    /// A native module's class (section 3.9): a class extending <c>Object</c>, bound and made as the built-in ones are, under
    /// a name no program can write, the module's and the class's, so that only the module's instance exports it. Each
    /// member calls its built-in, which <c>WebLingoNativeClass</c> runs: a constructor and a method with <c>this</c> first,
    /// then their arguments, a static method with its arguments; its properties, a getter and a setter, as <c>get:</c> and
    /// <c>set:</c> methods; and its indexer, as methods no program can name (<see cref="WebLingoIndexers"/>), its setter
    /// giving the value assigned. Its functions are native, so that the VMs report an error in one where it was called.
    /// </summary>
    private WebLingoLocalSymbol BindNativeClass(string module, WebLingoNativeClassInfo info)
    {
        var callees = new Dictionary<string, WebLingoBuiltinDeclaration>(StringComparer.Ordinal);
        var name = module + "." + info.Name;
        var methods = new List<WebLingoMethodSyntax> { NativeMember("constructor", info.Constructor, false, callees, returns: false) };
        foreach (var method in info.Methods) methods.Add(NativeMember(method.Name, method, false, callees));
        foreach (var method in info.StaticMethods) methods.Add(method.Factory ? NativeFactory(method) : NativeMember(method.Name, method, true, callees));
        foreach (var property in info.Properties)
        {
            methods.Add(NativeMember("get:" + property.Name, property.Getter, false, callees));
            // A setter's value is the assignment's, whatever its built-in gives.
            if (property.Setter is { } setter) methods.Add(NativeMember("set:" + property.Name, setter, false, callees, returns: false));
        }
        foreach (var property in info.StaticProperties)
        {
            // A static property, Name.name, its getter given nothing and its setter the value (section 3.7).
            methods.Add(NativeMember("get:" + property.Name, property.Getter, true, callees));
            if (property.Setter is { } setter) methods.Add(NativeMember("set:" + property.Name, setter, true, callees, returns: false));
        }
        if (info.IndexGetter is { } getter) methods.Add(NativeMember(WebLingoIndexers.Getter, getter, false, callees));
        if (info.IndexSetter is { } indexSetter) methods.Add(NativeMember(WebLingoIndexers.Setter, indexSetter, false, callees, returnsLast: true));
        if (info.IndexOptional is { } optional) methods.Add(NativeMember(WebLingoIndexers.OptionalGetter, optional, false, callees));
        // Its methods written in WebLingo, which reach its native members through this, native functions as they are.
        methods.AddRange(info.WebLingoMethods);
        var outer = _nativeCallees;
        _nativeCallees = callees;
        var local = BindBuiltinClass(name, new WebLingoClassDeclarationSyntax(info.Name, default, methods, default), UseObject());
        _nativeCallees = outer;
        if (info.Registration is { } registration)
        {
            // A class its module makes instances of: told its class value where the program begins, after the built-in
            // classes (section 3.10).
            var told = new WebLingoBoundBuiltinCall(UseBuiltin(new WebLingoBuiltinDeclaration(registration.Builtin, registration.Arity)),
                [Reference(local, default)], default);
            _nativeFunctions.Add(new WebLingoBoundExpressionStatement(told, default));
        }
        return local;
    }

    /// <summary>
    /// A native module's function with optional parameters (section 3.9): a function written in WebLingo, made where the
    /// program begins, after the built-in classes, its last parameters <c>null</c> by default, which calls the function's
    /// built-in with all of them; its local, which the module's instance exports.
    /// </summary>
    private WebLingoLocalSymbol BindNativeFunction(string module, WebLingoNativeExport export)
    {
        var callees = new Dictionary<string, WebLingoBuiltinDeclaration>(StringComparer.Ordinal);
        var syntax = NativeMember(export.Name, export, true, callees);
        // A cell, as an export's is, which the module's instance, made before the program's statements, holds (section 3.9).
        var local = new WebLingoLocalSymbol(module + "." + export.Name, WebLingoDeclarationKind.Const, _main.Locals.Count, default, _main)
        {
            IsCaptured = true
        };
        _main.Locals.Add(local);
        var (outerScope, outerFunction, outerClass, outerCallees) = (_scope, _function, _class, _nativeCallees);
        (_scope, _function, _class, _nativeCallees) = (_prelude, _main, null, callees);
        var function = BindFunction(export.Name, syntax.Parameters, syntax.Body, default);
        (_scope, _function, _class, _nativeCallees) = (outerScope, outerFunction, outerClass, outerCallees);
        _nativeFunctions.Add(new WebLingoBoundVariableDeclaration(local, new WebLingoBoundFunctionExpression(function, default), default));
        return local;
    }

    /// <summary>
    /// A native module's constant (section 3.9): a cell, as an export's is, given its value, as a literal gives it, where the
    /// program begins, with the module's functions written in WebLingo; its local, which the module's instance exports.
    /// </summary>
    private WebLingoLocalSymbol BindNativeConstant(string module, WebLingoNativeConstant constant)
    {
        var local = new WebLingoLocalSymbol(module + "." + constant.Name, WebLingoDeclarationKind.Const, _main.Locals.Count, default, _main)
        {
            IsCaptured = true
        };
        _main.Locals.Add(local);
        WebLingoBoundExpression value = constant.Value switch
        {
            bool b => new WebLingoBoundBooleanLiteral(b, default),
            long l => new WebLingoBoundIntegerLiteral(l, default),
            double d => new WebLingoBoundBinary64Literal(d, default),
            _ => new WebLingoBoundNullLiteral(default)
        };
        _nativeFunctions.Add(new WebLingoBoundVariableDeclaration(local, value, default));
        return local;
    }

    /// <summary>The native modules' functions with optional parameters, made after the built-in classes (section 3.9).</summary>
    private readonly List<WebLingoBoundStatement> _nativeFunctions = [];

    /// <summary>
    /// A native class's member: a method whose body calls its built-in, giving what it gives, or nothing, for a constructor
    /// and a setter, or its last argument, for an indexer's setter, the value assigned.
    /// </summary>
    private static WebLingoMethodSyntax NativeMember(string name, WebLingoNativeExport export, bool isStatic,
        Dictionary<string, WebLingoBuiltinDeclaration> callees, bool returns = true, bool returnsLast = false)
    {
        callees[export.Builtin] = new WebLingoBuiltinDeclaration(export.Builtin, export.Arity);
        var parameters = new List<WebLingoParameterSyntax>();
        var arguments = new List<WebLingoExpressionSyntax>();
        if (!isStatic) arguments.Add(new WebLingoThisSyntax(default));
        var count = export.Arity - (isStatic ? 0 : 1);
        for (var i = 0; i < count; i++)
        {
            // Its last parameters optional, each null when a call leaves it out, or its last a rest parameter (section 3.9).
            parameters.Add(new WebLingoParameterSyntax("a" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), default)
            {
                Default = i >= count - export.Optional ? new WebLingoNullLiteralSyntax(default) : null,
                IsRest = export.Rest && i == count - 1
            });
            arguments.Add(new WebLingoNameSyntax(parameters[^1].Name, default));
        }
        var call = new WebLingoCallExpressionSyntax(new WebLingoNameSyntax(export.Builtin, default), arguments, default);
        List<WebLingoStatementSyntax> body = returns && !returnsLast
            ? [new WebLingoReturnSyntax(call, default)]
            : [new WebLingoExpressionStatementSyntax(call, default)];
        if (returnsLast) body.Add(new WebLingoReturnSyntax(new WebLingoNameSyntax(parameters[^1].Name, default), default));
        return new WebLingoMethodSyntax(name, default, parameters, new WebLingoBlockSyntax(body, default), default) { IsStatic = isStatic };
    }

    /// <summary>
    /// A native class's factory (section 3.7, "No sealed classes"), shorthand for both steps of making an instance, created,
    /// then opened: a static method taking the class it is called on, its receiver, <c>this</c>, so that a class extending
    /// the native one gets its own instance, which makes <c>new this()</c>, or <c>new this(…)</c> given the factory's
    /// arguments, and calls its method with them, or with none, awaiting it where it is asynchronous, and gives the instance:
    /// <c>static async open(this, a0, a1, a2 = null) { var made = new this(); await made.open(a0, a1, a2); return made; }</c>.
    /// </summary>
    private static WebLingoMethodSyntax NativeFactory(WebLingoNativeExport export)
    {
        var parameters = new List<WebLingoParameterSyntax> { new("this", default) };
        var arguments = new List<WebLingoExpressionSyntax>();
        for (var i = 0; i < export.Arity; i++)
        {
            parameters.Add(new WebLingoParameterSyntax("a" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), default)
            {
                Default = i >= export.Arity - export.Optional ? new WebLingoNullLiteralSyntax(default) : null
            });
            arguments.Add(new WebLingoNameSyntax(parameters[^1].Name, default));
        }
        const string made = "made";
        var created = new WebLingoNewExpressionSyntax(new WebLingoThisSyntax(default), export.FactoryToConstructor ? arguments : [], default);
        WebLingoExpressionSyntax opened = new WebLingoCallExpressionSyntax(
            new WebLingoMemberAccessSyntax(new WebLingoNameSyntax(made, default), export.FactoryMethod!, default, default),
            export.FactoryToConstructor ? [] : arguments, default);
        if (export.FactoryAsync) opened = new WebLingoAwaitExpressionSyntax(opened, default);
        List<WebLingoStatementSyntax> body =
        [
            new WebLingoVariableDeclarationSyntax(WebLingoDeclarationKind.Var, made, default, created, default),
            new WebLingoExpressionStatementSyntax(opened, default),
            new WebLingoReturnSyntax(new WebLingoNameSyntax(made, default), default)
        ];
        return new WebLingoMethodSyntax(export.Name, default, parameters, new WebLingoBlockSyntax(body, default), default)
        {
            IsStatic = true,
            IsAsync = export.FactoryAsync
        };
    }

    /// <summary>Binds a built-in class from its syntax, extending <paramref name="baseLocal"/>, or nothing if null.</summary>
    private WebLingoLocalSymbol BindBuiltinClass(string name, WebLingoClassDeclarationSyntax syntax, WebLingoLocalSymbol? baseLocal,
        bool inPrelude = false)
    {
        var local = new WebLingoLocalSymbol(name, WebLingoDeclarationKind.Class, _main.Locals.Count, default, _main);
        _main.Locals.Add(local);
        _prelude.Locals.Add(name, local);
        // What it was binding is restored after, prelude code included, which a built-in class bound while it is bound, as
        // a spread's is, must not end.
        var (outerScope, outerFunction, outerClass, outerPrelude) = (_scope, _function, _class, _inPrelude);
        (_scope, _function, _inPrelude) = (_prelude, _main, inPrelude);
        var baseClass = baseLocal is null ? null : Reference(baseLocal, default);
        var declaration = new WebLingoBoundClassDeclaration(local, BindClass(syntax, local, baseClass, []), default);
        (_scope, _function, _class, _inPrelude) = (outerScope, outerFunction, outerClass, outerPrelude);
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

    /// <summary>
    /// The class that spreads values (section 3.5), written in WebLingo, under a name no program can write: its static
    /// <c>array</c>, <c>set</c> and <c>map</c> visit each of their parts as <c>foreach</c> does, a map's as its entries,
    /// into a new collection. Its functions are native, as every built-in class's are, so that an error in one is reported
    /// where the spread is.
    /// </summary>
    private WebLingoLocalSymbol UseSpread()
    {
        if (_spread is not null) return _spread;
        _spread = BindBuiltinClass("spread:Spread", SpreadSource, UseObject());
        return _spread;
    }

    private const string SpreadSource =
        "class Spread { static array(...parts) { var result = []; foreach (var part in parts) { foreach (var item in part) result.push(item); } return result; } static set(...parts) { var result = #{}; foreach (var part in parts) { foreach (var item in part) result.add(item); } return result; } static map(...parts) { var result = {}; foreach (var part in parts) { foreach (var (key, value) in part) result[key] = value; } return result; } }";

    private static bool IsArrayMethodName(string name) =>
        name is "map" or "select" or "filter" or "where" or "reduce" or "find" or "any" or "some" or "all" or "every" or "sort";

    /// <summary>
    /// The methods of arrays that take a function (section 3.6, "Array methods"), written in WebLingo, an extension of
    /// <c>Array</c> made where the program begins, once it names one of them, on an array or on anything else, its kind
    /// being known only when it runs: <c>map</c> or <c>select</c>, <c>filter</c> or <c>where</c>, <c>reduce</c>,
    /// <c>find</c>, <c>any</c> or <c>some</c>, <c>all</c> or <c>every</c>, and <c>sort</c>. A function that gives a task
    /// is awaited, the method then giving a task of its result. Its functions are native, so that an error in one is
    /// reported where it was called.
    /// </summary>
    private void UseArrayMethods()
    {
        if (_arrayMethods is not null) return;
        _arrayMethods = new WebLingoBoundBlock([], default); // Its place, while it is bound.
        var tree = new WebLingoParser(ArrayMethodsSource, "Array").ParseCompilationUnit();
        foreach (var diagnostic in tree.Diagnostics)
        {
            if (diagnostic.Severity == WebLingoDiagnosticSeverity.Error)
            {
                throw new InvalidOperationException($"The array methods' source does not parse: {diagnostic.Message}");
            }
        }
        var extension = (WebLingoExtendSyntax)tree.Root.Statements[0];
        var array = UseBuiltinClass("Array");
        UseBuiltinClass("Task");
        var (outerScope, outerFunction, outerClass, outerCallees) = (_scope, _function, _class, _nativeCallees);
        (_scope, _function, _class) = (_prelude, _main, null);
        _nativeCallees = new Dictionary<string, WebLingoBuiltinDeclaration>(StringComparer.Ordinal);
        var bound = BindExtension(extension, []);
        (_scope, _function, _class, _nativeCallees) = (outerScope, outerFunction, outerClass, outerCallees);
        _arrayMethods = new WebLingoBoundExtension(new WebLingoBoundLocalReference(array, default), bound, default);
    }

    private const string ArrayMethodsSource =
        "extend Array {\n" +
        "  map(f) {\n" +
        "    var items = this;\n" +
        "    var result = [];\n" +
        "    for (var i = 0; i < items.length; i++) {\n" +
        "      var value = f(items[i]);\n" +
        "      if (value is Task) {\n" +
        "        var from = i;\n" +
        "        var later = async () => {\n" +
        "          result.push(await value);\n" +
        "          for (var j = from + 1; j < items.length; j++) result.push(await f(items[j]));\n" +
        "          return result;\n" +
        "        };\n" +
        "        return later();\n" +
        "      }\n" +
        "      result.push(value);\n" +
        "    }\n" +
        "    return result;\n" +
        "  }\n" +
        "  select(f) { return this.map(f); }\n" +
        "  filter(f) {\n" +
        "    var items = this;\n" +
        "    var result = [];\n" +
        "    for (var i = 0; i < items.length; i++) {\n" +
        "      var item = items[i];\n" +
        "      var keep = f(item);\n" +
        "      if (keep is Task) {\n" +
        "        var from = i;\n" +
        "        var later = async () => {\n" +
        "          if (await keep) result.push(item);\n" +
        "          for (var j = from + 1; j < items.length; j++) {\n" +
        "            var next = items[j];\n" +
        "            if (await f(next)) result.push(next);\n" +
        "          }\n" +
        "          return result;\n" +
        "        };\n" +
        "        return later();\n" +
        "      }\n" +
        "      if (keep) result.push(item);\n" +
        "    }\n" +
        "    return result;\n" +
        "  }\n" +
        "  where(f) { return this.filter(f); }\n" +
        "  reduce(f, initial) {\n" +
        "    var items = this;\n" +
        "    var total = initial;\n" +
        "    for (var i = 0; i < items.length; i++) {\n" +
        "      total = f(total, items[i]);\n" +
        "      if (total is Task) {\n" +
        "        var from = i;\n" +
        "        var pending = total;\n" +
        "        var later = async () => {\n" +
        "          var sum = await pending;\n" +
        "          for (var j = from + 1; j < items.length; j++) sum = await f(sum, items[j]);\n" +
        "          return sum;\n" +
        "        };\n" +
        "        return later();\n" +
        "      }\n" +
        "    }\n" +
        "    return total;\n" +
        "  }\n" +
        "  find(f) {\n" +
        "    var items = this;\n" +
        "    for (var i = 0; i < items.length; i++) {\n" +
        "      var item = items[i];\n" +
        "      var found = f(item);\n" +
        "      if (found is Task) {\n" +
        "        var from = i;\n" +
        "        var later = async () => {\n" +
        "          if (await found) return item;\n" +
        "          for (var j = from + 1; j < items.length; j++) {\n" +
        "            var next = items[j];\n" +
        "            if (await f(next)) return next;\n" +
        "          }\n" +
        "          return null;\n" +
        "        };\n" +
        "        return later();\n" +
        "      }\n" +
        "      if (found) return item;\n" +
        "    }\n" +
        "    return null;\n" +
        "  }\n" +
        "  any(f) {\n" +
        "    var items = this;\n" +
        "    for (var i = 0; i < items.length; i++) {\n" +
        "      var found = f(items[i]);\n" +
        "      if (found is Task) {\n" +
        "        var from = i;\n" +
        "        var later = async () => {\n" +
        "          if (await found) return true;\n" +
        "          for (var j = from + 1; j < items.length; j++) {\n" +
        "            if (await f(items[j])) return true;\n" +
        "          }\n" +
        "          return false;\n" +
        "        };\n" +
        "        return later();\n" +
        "      }\n" +
        "      if (found) return true;\n" +
        "    }\n" +
        "    return false;\n" +
        "  }\n" +
        "  some(f) { return this.any(f); }\n" +
        "  all(f) {\n" +
        "    var items = this;\n" +
        "    for (var i = 0; i < items.length; i++) {\n" +
        "      var kept = f(items[i]);\n" +
        "      if (kept is Task) {\n" +
        "        var from = i;\n" +
        "        var later = async () => {\n" +
        "          if (!(await kept)) return false;\n" +
        "          for (var j = from + 1; j < items.length; j++) {\n" +
        "            if (!(await f(items[j]))) return false;\n" +
        "          }\n" +
        "          return true;\n" +
        "        };\n" +
        "        return later();\n" +
        "      }\n" +
        "      if (!kept) return false;\n" +
        "    }\n" +
        "    return true;\n" +
        "  }\n" +
        "  every(f) { return this.all(f); }\n" +
        "  sort(compare = null) {\n" +
        "    var a = [];\n" +
        "    foreach (var item in this) a.push(item);\n" +
        "    var b = [];\n" +
        "    foreach (var item in a) b.push(item);\n" +
        "    var n = a.length;\n" +
        "    for (var width = 1; width < n; width *= 2) {\n" +
        "      for (var low = 0; low < n; low += 2 * width) {\n" +
        "        var middle = low + width < n ? low + width : n;\n" +
        "        var high = low + 2 * width < n ? low + 2 * width : n;\n" +
        "        var i = low;\n" +
        "        var j = middle;\n" +
        "        for (var k = low; k < high; k++) {\n" +
        "          var right = j < high && (i >= middle || (compare == null ? a[j] < a[i] : compare(a[j], a[i]) < 0));\n" +
        "          if (right) {\n" +
        "            b[k] = a[j];\n" +
        "            j++;\n" +
        "          } else {\n" +
        "            b[k] = a[i];\n" +
        "            i++;\n" +
        "          }\n" +
        "        }\n" +
        "      }\n" +
        "      var swap = a;\n" +
        "      a = b;\n" +
        "      b = swap;\n" +
        "    }\n" +
        "    return a;\n" +
        "  }\n" +
        "}\n";

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
