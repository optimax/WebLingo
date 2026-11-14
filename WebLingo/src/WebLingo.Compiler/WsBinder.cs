using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// The binder (specification, section 8.4, "Binder/IR"; its choices are in section 15.2). It resolves every name
/// lexically. A declaration takes effect where it stands, after its initializer (section 3.1): before it, and in
/// its own initializer, the name means what it means outside, and is undefined (WS2001) if nothing outside
/// declares it. A function declaration is the exception: it is in scope throughout its block (section 3.5). The
/// host's built-ins are in a scope around the program's, so a declaration may shadow one. A name of a function
/// around the one being bound is captured: the local is marked, and every function between gets a capture of it.
/// Binding goes on after an error, so that every binding error is reported.
/// </summary>
public sealed class WsBinder
{
    private readonly Dictionary<string, WsBuiltinDeclaration> _hostBuiltins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WsBuiltinSymbol> _usedBuiltins = new(StringComparer.Ordinal);
    private readonly List<WsBuiltinSymbol> _builtinsInOrder = [];
    private readonly List<WsDiagnostic> _diagnostics = [];
    private FunctionContext _function;
    private Scope _scope;

    /// <summary>The program's function, and a scope around the program's, which holds <c>Object</c> once it is used.</summary>
    private readonly FunctionContext _main;
    private readonly Scope _prelude;

    /// <summary>The built-in class <c>Object</c>, bound once a program needs it (section 3.7).</summary>
    private WsBoundClassDeclaration? _object;

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
    private WsBoundClassDeclaration? _error;

    /// <summary>The built-in classes the program uses, in the order they are made, each after the class it extends.</summary>
    private readonly List<(string Name, WsBoundClassDeclaration Declaration)> _builtinClasses = [];

    /// <summary>
    /// <c>Error</c>, which extends <c>Object</c>: <c>new Error(message)</c>, with the fields <c>message</c> and
    /// <c>code</c>, <c>null</c> for one a program throws; a runtime error a handler catches becomes one, its code set
    /// (section 3.7).
    /// </summary>
    private const string ErrorSource = "class Error { constructor(message) { this.message = message; this.code = null; } }";

    public WsBinder(WsCompilerOptions options)
    {
        foreach (var builtin in options.Builtins) _hostBuiltins[builtin.Name] = builtin;
        _function = _main = new FunctionContext(null, "main");
        _prelude = new Scope(null, _function);
        _scope = new Scope(_prelude, _function);
    }

    private ClassContext? _class;

    /// <summary>
    /// What <c>this</c> names in a static method or a static field's value: nothing, rather than an outer method's
    /// <c>this</c> (section 3.7). It is never a local of a function.
    /// </summary>
    private readonly WsLocalSymbol _noThis = new("this", WsDeclarationKind.Parameter, -1, default, new object());

    public IReadOnlyList<WsDiagnostic> Diagnostics => _diagnostics;

    public WsBoundProgram Bind(WsCompilationUnitSyntax unit)
    {
        var statements = BindStatements(unit.Statements);
        // The built-in classes the program uses are made first, before its own.
        var builtins = new List<WsBoundStatement>();
        var builtinClasses = new List<(string, WsBoundClass)>();
        foreach (var (name, declaration) in _builtinClasses)
        {
            builtins.Add(declaration);
            builtinClasses.Add((name, declaration.Class));
        }
        statements.InsertRange(0, builtins);
        return new WsBoundProgram(statements, _function.Locals, _builtinsInOrder)
        {
            ErrorClass = _error?.Class,
            ObjectClass = _object?.Class,
            BuiltinClasses = builtinClasses,
            IsAsync = _main.Awaits
        };
    }

    /// <summary>A block's statements, in the current scope, its function declarations declared first.</summary>
    private List<WsBoundStatement> BindStatements(IReadOnlyList<WsStatementSyntax> syntax)
    {
        var declared = new Dictionary<WsStatementSyntax, WsLocalSymbol>();
        foreach (var statement in syntax)
        {
            var local = statement switch
            {
                WsFunctionDeclarationSyntax function => DeclareFunction(function),
                WsClassDeclarationSyntax declaration => Declare(declaration.Name, WsDeclarationKind.Class, declaration.NameSpan, null),
                _ => null
            };
            if (local is not null) declared.Add(statement, local);
        }
        // A class is made when its block begins, so its base is bound there too: it may name the block's classes and
        // functions, and what is declared around the block, but not the block's variables, which have no value yet.
        // A class without 'extends' extends Object (section 3.7), whatever the name 'Object' means where it is.
        var bases = new Dictionary<WsClassDeclarationSyntax, WsBoundExpression>();
        foreach (var statement in syntax)
        {
            if (statement is not WsClassDeclarationSyntax derived) continue;
            bases.Add(derived, derived.Base is { } baseSyntax ? BindExpression(baseSyntax) : Reference(UseObject(), derived.NameSpan));
        }
        // A class's static fields are set when it is made, so their values are bound where the block begins too, where
        // 'this' is not the class's (WS2009).
        var statics = new Dictionary<WsClassDeclarationSyntax, List<(string, WsBoundExpression, WsSourceSpan)>>();
        foreach (var statement in syntax)
        {
            if (statement is not WsClassDeclarationSyntax { StaticFields.Count: > 0 } withStatics) continue;
            _scope = new Scope(_scope, _function);
            _scope.Locals.Add("this", _noThis);
            var values = new List<(string, WsBoundExpression, WsSourceSpan)>();
            foreach (var field in withStatics.StaticFields) values.Add((field.Name, BindExpression(field.Value), field.NameSpan));
            _scope = _scope.Parent!;
            statics.Add(withStatics, values);
        }
        var statements = new List<WsBoundStatement>();
        foreach (var statement in syntax)
        {
            if (statement is WsFunctionDeclarationSyntax function)
            {
                var bound = BindFunction(function.Name, function.Parameters, function.Body, function.Span, isAsync: function.IsAsync);
                if (declared.TryGetValue(function, out var local))
                {
                    statements.Add(new WsBoundFunctionDeclaration(local, bound, function.Span));
                }
                continue;
            }
            if (statement is WsClassDeclarationSyntax declaration)
            {
                declared.TryGetValue(declaration, out var local);
                var bound = BindClass(declaration, local, bases.GetValueOrDefault(declaration),
                    statics.GetValueOrDefault(declaration) ?? []);
                if (local is not null) statements.Add(new WsBoundClassDeclaration(local, bound, declaration.Span));
                continue;
            }
            statements.Add(BindStatement(statement));
        }
        OrderClasses(statements);
        return statements;
    }

    /// <summary>
    /// Puts a block's classes in the order they can be made at its start: a base declared in the same block before the
    /// classes that extend it. A class that extends itself, directly or through others, is WS2013.
    /// </summary>
    private void OrderClasses(List<WsBoundStatement> statements)
    {
        var positions = new List<int>();
        var classes = new List<WsBoundClassDeclaration>();
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not WsBoundClassDeclaration declaration) continue;
            positions.Add(i);
            classes.Add(declaration);
        }
        if (classes.Count < 2) return;
        var ordered = new List<WsBoundClassDeclaration>();
        var state = new Dictionary<WsBoundClassDeclaration, int>(); // 1 being placed, 2 placed.
        foreach (var declaration in classes) Place(declaration, classes, ordered, state);
        for (var i = 0; i < positions.Count; i++) statements[positions[i]] = ordered[i];
    }

    private void Place(WsBoundClassDeclaration declaration, List<WsBoundClassDeclaration> classes,
        List<WsBoundClassDeclaration> ordered, Dictionary<WsBoundClassDeclaration, int> state)
    {
        if (state.TryGetValue(declaration, out var seen))
        {
            if (seen == 1)
            {
                Report(WsDiagnosticCodes.ClassExtendsItself, $"'{declaration.Local.Name}' extends itself.", declaration.Span);
            }
            return;
        }
        state[declaration] = 1;
        var baseLocal = declaration.Class.Base switch
        {
            WsBoundLocalReference reference => reference.Local,
            WsBoundCaptureReference capture => capture.Local,
            _ => null
        };
        var baseDeclaration = classes.Find(other => other.Local == baseLocal);
        if (baseDeclaration is not null) Place(baseDeclaration, classes, ordered, state);
        state[declaration] = 2;
        ordered.Add(declaration);
    }

    private WsBoundStatement BindStatement(WsStatementSyntax statement)
    {
        switch (statement)
        {
            case WsVariableDeclarationSyntax declaration:
            {
                // The initializer is bound first: the name it declares does not exist in it yet.
                var initializer = declaration.Initializer is null
                    ? new WsBoundError(declaration.Span)
                    : BindExpression(declaration.Initializer);
                var local = Declare(declaration.Name, declaration.Kind, declaration.NameSpan, null);
                return local is null
                    ? new WsBoundExpressionStatement(initializer, declaration.Span)
                    : new WsBoundVariableDeclaration(local, initializer, declaration.Span);
            }
            case WsExpressionStatementSyntax expressionStatement:
                return new WsBoundExpressionStatement(BindExpression(expressionStatement.Expression), statement.Span);
            case WsIfSyntax conditional:
            {
                var condition = BindExpression(conditional.Condition);
                var then = BindInOwnScope(conditional.Then);
                var otherwise = conditional.Else is null ? null : BindInOwnScope(conditional.Else);
                return new WsBoundIf(condition, then, otherwise, conditional.Span);
            }
            case WsWhileSyntax whileLoop:
            {
                var condition = BindExpression(whileLoop.Condition);
                return new WsBoundLoop(condition, BindLoopBody(whileLoop.Body), null, true, whileLoop.Span);
            }
            case WsDoWhileSyntax doLoop:
            {
                var body = BindLoopBody(doLoop.Body);
                return new WsBoundLoop(BindExpression(doLoop.Condition), body, null, false, doLoop.Span);
            }
            case WsForSyntax forLoop:
            {
                // The loop's own scope holds what its init declares.
                _scope = new Scope(_scope, _function);
                var init = forLoop.Init is null ? null : BindStatement(forLoop.Init);
                var perIteration = new List<WsLocalSymbol>(_scope.Locals.Values);
                var condition = forLoop.Condition is null ? null : BindExpression(forLoop.Condition);
                var step = forLoop.Step is null ? null : BindExpression(forLoop.Step);
                var body = BindLoopBody(forLoop.Body);
                _scope = _scope.Parent!;
                var loop = new WsBoundLoop(condition, body, step, true, forLoop.Span) { PerIterationLocals = perIteration };
                return new WsBoundBlock(init is null ? [loop] : [init, loop], forLoop.Span);
            }
            case WsForEachSyntax forEach:
            {
                // The collection is bound first: the loop's variables do not exist in it yet. They are the loop's,
                // with a binding of their own on each iteration (sections 3.5 and 3.6).
                var collectionValue = BindExpression(forEach.Collection);
                _scope = new Scope(_scope, _function);
                var collection = DeclareHidden("foreach collection", forEach.Span);
                var position = DeclareHidden("foreach position", forEach.Span);
                var item = Declare(forEach.ItemName, WsDeclarationKind.Var, forEach.ItemSpan, null)
                    ?? DeclareHidden(forEach.ItemName, forEach.ItemSpan);
                var value = forEach.ValueName is null
                    ? null
                    : Declare(forEach.ValueName, WsDeclarationKind.Var, forEach.ValueSpan, null)
                      ?? DeclareHidden(forEach.ValueName, forEach.ValueSpan);
                var body = BindLoopBody(forEach.Body);
                _scope = _scope.Parent!;
                return new WsBoundForEach(collection, position, item, value, collectionValue, body, forEach.Span);
            }
            case WsUsingSyntax usingStatement:
            {
                // The value is bound first: the name it declares does not exist in it yet. A using may await its
                // resource's async disposal: at the program's top level, which then becomes async (section 3.8).
                if (_function == _main) _main.Awaits = true;
                var value = BindExpression(usingStatement.Value);
                _scope = new Scope(_scope, _function);
                var local = Declare(usingStatement.Name, WsDeclarationKind.Var, usingStatement.NameSpan, null)
                    ?? DeclareHidden(usingStatement.Name, usingStatement.NameSpan);
                var exception = DeclareHidden("using exception", usingStatement.Span);
                var exceptionOrigin = DeclareHidden("using origin", usingStatement.Span);
                UseError(); // A runtime error its handler catches becomes an Error.
                var body = BindInOwnScope(usingStatement.Body);
                _scope = _scope.Parent!;
                return new WsBoundUsing(local, value, body, usingStatement.Span)
                {
                    ExceptionValue = exception,
                    ExceptionOrigin = exceptionOrigin
                };
            }
            case WsBreakSyntax or WsContinueSyntax:
            {
                var isBreak = statement is WsBreakSyntax;
                if (_function.LoopDepth == 0)
                {
                    Report(WsDiagnosticCodes.NotInLoop, $"'{(isBreak ? "break" : "continue")}' is only allowed in a loop.",
                        statement.Span);
                }
                else if (_function.Finallys.Count > 0 && _function.LoopDepth == _function.Finallys[^1])
                {
                    Report(WsDiagnosticCodes.JumpOutOfFinally,
                        $"'{(isBreak ? "break" : "continue")}' would leave a 'finally' block, which nothing may leave early.",
                        statement.Span);
                }
                return isBreak ? new WsBoundBreak(statement.Span) : new WsBoundContinue(statement.Span);
            }
            case WsReturnSyntax returnStatement:
            {
                var value = returnStatement.Value is null ? null : BindExpression(returnStatement.Value);
                if (_function.Parent is null)
                {
                    Report(WsDiagnosticCodes.ReturnOutsideFunction, "'return' is only allowed in a function.", statement.Span);
                }
                else if (_function.Finallys.Count > 0)
                {
                    Report(WsDiagnosticCodes.JumpOutOfFinally,
                        "'return' would leave a 'finally' block, which nothing may leave early.", statement.Span);
                }
                return new WsBoundReturn(value, statement.Span);
            }
            case WsTrySyntax tryStatement:
            {
                UseError(); // A runtime error its handlers catch becomes an Error, of its kind's class.
                var body = BindStatement(tryStatement.Body);
                var catches = new List<WsBoundCatch>();
                WsLocalSymbol? caughtValue = null;
                WsLocalSymbol? caughtOrigin = null;
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
                    var variable = Declare(catchClause.Name, WsDeclarationKind.Var, catchClause.NameSpan, null)
                        ?? DeclareHidden(catchClause.Name, catchClause.NameSpan);
                    _function.Catches.Add((caughtValue!, caughtOrigin!));
                    var statements = BindStatements(catchClause.Body.Statements);
                    _function.Catches.RemoveAt(_function.Catches.Count - 1);
                    _scope = _scope.Parent!;
                    catches.Add(new WsBoundCatch(caught, variable, new WsBoundBlock(statements, catchClause.Body.Span)));
                }
                WsBoundFinally? cleanup = null;
                if (tryStatement.Finally is { } finallyBlock)
                {
                    var value = DeclareHidden("finally exception", finallyBlock.Span);
                    var origin = DeclareHidden("finally origin", finallyBlock.Span);
                    _function.Finallys.Add(_function.LoopDepth);
                    var statements = BindStatement(finallyBlock);
                    _function.Finallys.RemoveAt(_function.Finallys.Count - 1);
                    cleanup = new WsBoundFinally(value, origin, statements);
                }
                return new WsBoundTry(body, catches, caughtValue, caughtOrigin, cleanup, tryStatement.Span);
            }
            case WsThrowSyntax throwStatement:
            {
                if (throwStatement.Value is { } thrown) return new WsBoundThrow(BindExpression(thrown), throwStatement.Span);
                if (_function.Catches.Count == 0)
                {
                    Report(WsDiagnosticCodes.ThrowOutsideCatch,
                        "'throw;' alone is only allowed in a 'catch' block, where it throws its exception again.", throwStatement.Span);
                    return new WsBoundBlock([], throwStatement.Span);
                }
                var (caught, where) = _function.Catches[^1];
                return new WsBoundRethrow(caught, where, throwStatement.Span);
            }
            case WsBlockSyntax block:
            {
                _scope = new Scope(_scope, _function);
                var statements = BindStatements(block.Statements);
                _scope = _scope.Parent!;
                return new WsBoundBlock(statements, block.Span);
            }
            case WsFunctionDeclarationSyntax function:
                // A function declared on its own, as a branch or a loop's body: a block of one.
                return new WsBoundBlock(BindStatements([function]), function.Span);
            case WsClassDeclarationSyntax declaration:
                return new WsBoundBlock(BindStatements([declaration]), declaration.Span);
            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
        }
    }

    private WsBoundStatement BindLoopBody(WsStatementSyntax body)
    {
        _function.LoopDepth++;
        var bound = BindInOwnScope(body);
        _function.LoopDepth--;
        return bound;
    }

    /// <summary>A branch or a loop's body: a declaration in it, without braces, is in scope only there.</summary>
    private WsBoundStatement BindInOwnScope(WsStatementSyntax statement)
    {
        _scope = new Scope(_scope, _function);
        var bound = BindStatements([statement]);
        _scope = _scope.Parent!;
        return new WsBoundBlock(bound, statement.Span);
    }

    /// <summary>
    /// A class: its base, bound where the class is, then its constructor and its methods, each a function whose first
    /// parameter is <c>this</c>; a name declared twice in it is WS2002 (section 3.7). A class without a constructor gets
    /// one that does nothing but run its base's; a constructor of a derived class that does not call <c>super(…)</c>
    /// runs its base's first, with no arguments. Its static members, fields and methods, have names of their own, apart
    /// from its methods'; its static methods are functions without <c>this</c> or <c>super</c> (WS2009).
    /// </summary>
    private WsBoundClass BindClass(WsClassDeclarationSyntax declaration, WsLocalSymbol? local, WsBoundExpression? baseClass,
        List<(string Name, WsBoundExpression Value, WsSourceSpan Span)> staticFields)
    {
        var hasBase = baseClass is not null;
        WsBoundFunction? constructor = null;
        var methods = new List<(string, WsBoundFunction)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<WsMethodSyntax>();
        var staticSyntax = new List<WsMethodSyntax>();
        foreach (var member in declaration.Methods) (member.IsStatic ? staticSyntax : members).Add(member);
        if (!members.Exists(member => member.Name == "constructor"))
        {
            var span = new WsSourceSpan(declaration.Span.Start, 0);
            members.Insert(0, new WsMethodSyntax("constructor", span, [], new WsBlockSyntax([], span), span));
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
                    Report(WsDiagnosticCodes.DisposeAndDestructor,
                        "A class declares 'dispose()' or 'destructor()', which are the same method, not both.", method.NameSpan);
                    continue;
                }
                method = method with { Name = "dispose" };
            }
            else if (method.Name == "dispose" && disposer is not null && disposer != method)
            {
                Report(WsDiagnosticCodes.DisposeAndDestructor,
                    "A class declares 'dispose()' or 'destructor()', which are the same method, not both.", method.NameSpan);
                continue;
            }
            var isConstructor = method.Name == "constructor";
            // Object's constructor does nothing, so a class that extends it without saying so does not run it.
            if (isConstructor && declaration.Base is not null && !method.CallsSuper)
            {
                var span = new WsSourceSpan(method.Body.Span.Start, 0);
                var superCall = new WsExpressionStatementSyntax(new WsCallExpressionSyntax(new WsSuperSyntax(span), [], span), span);
                method = method with { Body = method.Body with { Statements = [superCall, .. method.Body.Statements] } };
            }
            _class = new ClassContext(local, hasBase, isConstructor);
            var function = BindFunction(isConstructor ? declaration.Name : method.Name, method.Parameters, method.Body,
                method.Span, isMethod: true, isAsync: method.IsAsync);
            _class = outerClass;
            if (!names.Add(method.Name))
            {
                Report(WsDiagnosticCodes.DuplicateDeclaration, $"'{method.Name}' is already declared in this class.", method.NameSpan);
                continue;
            }
            if (isConstructor) constructor = function;
            else methods.Add((method.Name, function));
        }
        var staticNames = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<(string, WsBoundExpression, WsSourceSpan)>();
        foreach (var field in staticFields)
        {
            if (staticNames.Add(field.Name)) fields.Add(field);
            else Report(WsDiagnosticCodes.DuplicateDeclaration, $"'{field.Name}' is already declared in this class.", field.Span);
        }
        var staticMethods = new List<(string, WsBoundFunction)>();
        foreach (var method in staticSyntax)
        {
            _class = null; // No 'super' in a static method.
            var function = BindFunction(method.Name, method.Parameters, method.Body, method.Span, isStatic: true,
                isAsync: method.IsAsync);
            _class = outerClass;
            if (staticNames.Add(method.Name)) staticMethods.Add((method.Name, function));
            else Report(WsDiagnosticCodes.DuplicateDeclaration, $"'{method.Name}' is already declared in this class.", method.NameSpan);
        }
        return new WsBoundClass(declaration.Name, constructor, methods, declaration.Span)
        {
            Base = baseClass,
            StaticMethods = staticMethods,
            StaticFields = fields
        };
    }

    /// <summary>
    /// A function's body, in a function context of its own, its parameters its first locals; a method's first is
    /// <c>this</c>, which only a method declares (section 3.7).
    /// </summary>
    private WsBoundFunction BindFunction(string name, IReadOnlyList<WsParameterSyntax> parameters, WsBlockSyntax body,
        WsSourceSpan span, bool isMethod = false, bool isStatic = false, bool isAsync = false)
    {
        var outerFunction = _function;
        var outerScope = _scope;
        _function = new FunctionContext(outerFunction, name) { IsAsync = isAsync };
        _scope = new Scope(outerScope, _function);
        if (isMethod) Declare("this", WsDeclarationKind.Parameter, span, null);
        if (isStatic) _scope.Locals.Add("this", _noThis); // An outer method's 'this' is not a static method's (WS2009).
        var defaults = new List<WsBoundExpression?>();
        foreach (var parameter in parameters)
        {
            // A default is bound before its parameter is declared, as a variable's initializer is (section 3.1).
            defaults.Add(parameter.Default is { } value ? BindExpression(value) : null);
            Declare(parameter.Name, WsDeclarationKind.Parameter, parameter.Span, null);
        }
        _scope = new Scope(_scope, _function); // The body may declare a name its parameters have, shadowing it.
        var statements = BindStatements(body.Statements);
        var bound = new WsBoundFunction(name, parameters.Count + (isMethod ? 1 : 0), _function.Locals, _function.Captures,
            statements, span)
        {
            HasThis = isMethod,
            Defaults = defaults,
            IsAsync = isAsync
        };
        _function = outerFunction;
        _scope = outerScope;
        return bound;
    }

    /// <summary>A function declared by name, with how many arguments a call of it may give (section 3.5).</summary>
    private WsLocalSymbol? DeclareFunction(WsFunctionDeclarationSyntax function)
    {
        var local = Declare(function.Name, WsDeclarationKind.Function, function.NameSpan, function.Parameters.Count);
        if (local is null) return null;
        var required = 0;
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Default is null) required++;
        }
        local.FunctionMinArity = required;
        return local;
    }

    private WsLocalSymbol? Declare(string name, WsDeclarationKind kind, WsSourceSpan span, int? arity)
    {
        if (_scope.Locals.ContainsKey(name))
        {
            Report(WsDiagnosticCodes.DuplicateDeclaration, $"'{name}' is already declared in this scope.", span);
            return null;
        }
        var local = new WsLocalSymbol(name, kind, _function.Locals.Count, span, _function) { FunctionArity = arity };
        _function.Locals.Add(local);
        _scope.Locals.Add(name, local);
        return local;
    }

    /// <summary>
    /// The base's constructor or method, bound to <c>this</c>, in a method of a class that extends another; anywhere
    /// else <c>super</c> is WS2009 (section 3.7).
    /// </summary>
    private WsBoundExpression BindSuper(string name, WsSourceSpan span)
    {
        if (_class is not { HasBase: true, Local: { } classLocal } || LookupLocal("this") is not { } self
            || ReferenceEquals(self, _noThis))
        {
            Report(WsDiagnosticCodes.ThisOutsideMethod,
                "'super' is only allowed in a method or a constructor of a class that extends another.", span);
            return new WsBoundError(span);
        }
        return new WsBoundSuper(Reference(classLocal, span), Reference(self, span), name, span);
    }

    /// <summary>A local of the function that no name refers to: a loop's own state.</summary>
    private WsLocalSymbol DeclareHidden(string name, WsSourceSpan span)
    {
        var local = new WsLocalSymbol(name, WsDeclarationKind.Var, _function.Locals.Count, span, _function);
        _function.Locals.Add(local);
        return local;
    }

    private WsBoundExpression BindExpression(WsExpressionSyntax expression)
    {
        switch (expression)
        {
            case WsIntegerLiteralSyntax literal:
                return new WsBoundIntegerLiteral(literal.Value, literal.Span);
            case WsBinary64LiteralSyntax literal:
                return new WsBoundBinary64Literal(literal.Value, literal.Span);
            case WsBooleanLiteralSyntax literal:
                return new WsBoundBooleanLiteral(literal.Value, literal.Span);
            case WsNullLiteralSyntax literal:
                return new WsBoundNullLiteral(literal.Span);
            case WsStringLiteralSyntax literal:
                return new WsBoundStringLiteral(literal.Value, literal.Span);
            case WsNameSyntax name:
                return BindName(name);
            case WsParenthesizedExpressionSyntax parenthesized:
                return BindExpression(parenthesized.Expression);
            case WsUnaryExpressionSyntax unary:
                return new WsBoundUnary(unary.Operator, BindExpression(unary.Operand), unary.Span);
            case WsBinaryExpressionSyntax binary:
                return new WsBoundBinary(binary.Operator, BindExpression(binary.Left), BindExpression(binary.Right),
                    binary.OperatorSpan, binary.Span);
            case WsAssignmentExpressionSyntax assignment:
                return BindAssignment(assignment);
            case WsCallExpressionSyntax call:
                return BindCall(call);
            case WsArrayLiteralSyntax array:
            {
                var elements = new List<WsBoundExpression>();
                foreach (var element in array.Elements) elements.Add(BindExpression(element));
                return new WsBoundArrayLiteral(elements, array.Span);
            }
            case WsSetLiteralSyntax set:
            {
                var elements = new List<WsBoundExpression>();
                foreach (var element in set.Elements) elements.Add(BindExpression(element));
                return new WsBoundSetLiteral(elements, set.Span);
            }
            case WsMapLiteralSyntax map:
            {
                var entries = new List<(string, WsBoundExpression)>();
                foreach (var entry in map.Entries) entries.Add((entry.Key, BindExpression(entry.Value)));
                return new WsBoundMapLiteral(entries, map.Span);
            }
            case WsIndexExpressionSyntax index:
                return new WsBoundIndex(BindExpression(index.Target), BindExpression(index.Index), index.Span)
                {
                    IsOptional = index.IsOptional
                };
            case WsIndexAssignmentSyntax element:
                return new WsBoundIndexAssignment(BindExpression(element.Target.Target), BindExpression(element.Target.Index),
                    BindExpression(element.Value), element.Span);
            case WsMemberAccessSyntax { Target: WsSuperSyntax } superMember:
                return BindSuper(superMember.Name, superMember.Span);
            case WsMemberAccessSyntax member:
                if (member.Name is "toString" or "equal") UseObject(); // Every value has Object's (section 3.7).
                return new WsBoundMember(BindExpression(member.Target), member.Name, member.Span) { IsOptional = member.IsOptional };
            case WsMemberAssignmentSyntax field:
                return new WsBoundMemberAssignment(BindExpression(field.Target.Target), field.Target.Name, BindExpression(field.Value),
                    field.Span);
            case WsNewExpressionSyntax created:
            {
                var target = BindExpression(created.Class);
                var arguments = new List<WsBoundExpression>();
                foreach (var argument in created.Arguments) arguments.Add(BindExpression(argument));
                return new WsBoundNew(target, arguments, created.Span);
            }
            case WsSuperSyntax:
                Report(WsDiagnosticCodes.ThisOutsideMethod,
                    "'super' is either called, 'super(…)', or names a member of the base class, 'super.name'.", expression.Span);
                return new WsBoundError(expression.Span);
            case WsAwaitExpressionSyntax awaited:
            {
                // In an async function, or at the program's top level, which then becomes one (section 3.8).
                if (_function == _main) _main.Awaits = true;
                else if (!_function.IsAsync)
                {
                    Report(WsDiagnosticCodes.AwaitOutsideAsync,
                        "'await' is only allowed in an async function, or at a script's top level.", awaited.Span);
                }
                return new WsBoundAwait(BindExpression(awaited.Operand), awaited.Span);
            }
            case WsIsExpressionSyntax test:
                UseObject(); // A name that is not a class calls equal(), Object's for a value that is not an instance.
                return new WsBoundIs(BindExpression(test.Value), BindExpression(test.Class), test.Span);
            case WsThisSyntax self:
            {
                var local = LookupLocal("this");
                if (local is not null && !ReferenceEquals(local, _noThis)) return Reference(local, self.Span);
                Report(WsDiagnosticCodes.ThisOutsideMethod, local is null
                    ? "'this' is only allowed in a method or a constructor."
                    : "'this' is not allowed in a static method or a static field's value.", self.Span);
                return new WsBoundError(self.Span);
            }
            case WsFunctionExpressionSyntax function:
                return new WsBoundFunctionExpression(
                    BindFunction("", function.Parameters, function.Body, function.Span, isAsync: function.IsAsync), function.Span);
            case WsErrorExpressionSyntax error:
                return new WsBoundError(error.Span);
            default:
                throw new InvalidOperationException($"Unknown expression {expression.GetType().Name}.");
        }
    }

    private WsBoundExpression BindName(WsNameSyntax name)
    {
        if (LookupLocal(name.Name) is { } local) return Reference(local, name.Span);
        if (_hostBuiltins.TryGetValue(name.Name, out var builtin)) return new WsBoundBuiltinReference(UseBuiltin(builtin), name.Span);
        Report(WsDiagnosticCodes.UndefinedIdentifier, $"'{name.Name}' is not defined.", name.Span);
        return new WsBoundError(name.Span);
    }

    /// <summary>A local read where it is: in its own function, or through this function's capture of it.</summary>
    private WsBoundExpression Reference(WsLocalSymbol local, WsSourceSpan span) =>
        local.Owner == _function
            ? new WsBoundLocalReference(local, span)
            : new WsBoundCaptureReference(CaptureIndex(_function, local), local, span);

    /// <summary>The index of <paramref name="function"/>'s capture of a local of a function around it, made if new.</summary>
    private static int CaptureIndex(FunctionContext function, WsLocalSymbol local)
    {
        if (function.CaptureIndexes.TryGetValue(local, out var index)) return index;
        local.IsCaptured = true;
        var parent = function.Parent!;
        var capture = parent == local.Owner
            ? new WsBoundCapture(true, local.Index, local)
            : new WsBoundCapture(false, CaptureIndex(parent, local), local);
        index = function.Captures.Count;
        function.Captures.Add(capture);
        function.CaptureIndexes.Add(local, index);
        return index;
    }

    private WsBoundExpression BindAssignment(WsAssignmentExpressionSyntax assignment)
    {
        var value = BindExpression(assignment.Value);
        var target = assignment.Target;
        var local = LookupLocal(target.Name);
        if (local is null)
        {
            if (_hostBuiltins.ContainsKey(target.Name))
            {
                Report(WsDiagnosticCodes.InvalidOperation, $"'{target.Name}' is a built-in function and cannot be assigned.",
                    target.Span);
            }
            else
            {
                Report(WsDiagnosticCodes.UndefinedIdentifier,
                    $"'{target.Name}' is not declared: declare it first, with 'var {target.Name} = …;'.", target.Span);
            }
            return new WsBoundError(assignment.Span);
        }
        if (local.IsConstant)
        {
            var what = local.Kind == WsDeclarationKind.Function ? "a function" : "a constant";
            Report(WsDiagnosticCodes.AssignmentToConstant, $"'{target.Name}' is {what} and cannot be assigned.", target.Span);
            return new WsBoundError(assignment.Span);
        }
        return local.Owner == _function
            ? new WsBoundAssignment(local, value, assignment.Span)
            : new WsBoundCaptureAssignment(CaptureIndex(_function, local), local, value, assignment.Span);
    }

    private WsBoundExpression BindCall(WsCallExpressionSyntax call)
    {
        var arguments = new List<WsBoundExpression>();
        foreach (var argument in call.Arguments) arguments.Add(BindExpression(argument));

        var calleeSyntax = call.Callee;
        while (calleeSyntax is WsParenthesizedExpressionSyntax parenthesized) calleeSyntax = parenthesized.Expression;
        if (calleeSyntax is WsErrorExpressionSyntax) return new WsBoundError(call.Span);
        if (_inPrelude && calleeSyntax is WsNameSyntax { Name: "toText" } && arguments.Count == 1)
        {
            return new WsBoundToText(arguments[0], call.Span);
        }
        if (calleeSyntax is WsSuperSyntax)
        {
            if (_class is { InConstructor: false })
            {
                Report(WsDiagnosticCodes.ThisOutsideMethod, "'super(…)' is only allowed in a constructor.", calleeSyntax.Span);
                return new WsBoundError(call.Span);
            }
            return new WsBoundCall(BindSuper("constructor", calleeSyntax.Span), arguments, call.Span);
        }
        if (calleeSyntax is WsMemberAccessSyntax { Target: WsSuperSyntax } superMember)
        {
            return new WsBoundCall(BindSuper(superMember.Name, superMember.Span), arguments, call.Span);
        }
        if (calleeSyntax is WsMemberAccessSyntax member)
        {
            // A method, called on its target (specification, section 3.6); every value has Object's toString() (section 3.7).
            if (member.Name is "toString" or "equal") UseObject();
            return new WsBoundMethodCall(BindExpression(member.Target), member.Name, arguments, call.Span);
        }
        if (calleeSyntax is WsIntegerLiteralSyntax or WsBinary64LiteralSyntax or WsBooleanLiteralSyntax
            or WsNullLiteralSyntax or WsStringLiteralSyntax)
        {
            Report(WsDiagnosticCodes.NotAFunction, "A literal is not a function, and cannot be called.", calleeSyntax.Span);
            return new WsBoundError(call.Span);
        }
        if (calleeSyntax is WsNameSyntax className && LookupLocal(className.Name) is { Kind: WsDeclarationKind.Class })
        {
            Report(WsDiagnosticCodes.NotAFunction, $"'{className.Name}' is a class: make an instance with 'new {className.Name}(…)'.",
                calleeSyntax.Span);
            return new WsBoundError(call.Span);
        }
        if (calleeSyntax is WsNameSyntax name && LookupLocal(name.Name) is null
            && _hostBuiltins.TryGetValue(name.Name, out var declaration))
        {
            // A built-in called by its name: checked now, and called directly.
            if (!CheckArity(name.Name, declaration.Arity, arguments.Count, call.Span)) return new WsBoundError(call.Span);
            return new WsBoundBuiltinCall(UseBuiltin(declaration), arguments, call.Span);
        }
        if (calleeSyntax is WsNameSyntax functionName && LookupLocal(functionName.Name) is { FunctionArity: { } arity } function
            && !CheckArity(functionName.Name, arity, arguments.Count, call.Span, function.FunctionMinArity ?? arity))
        {
            return new WsBoundError(call.Span);
        }
        return new WsBoundCall(BindExpression(calleeSyntax), arguments, call.Span);
    }

    private bool CheckArity(string name, int arity, int count, WsSourceSpan span, int? minimum = null)
    {
        var least = minimum ?? arity;
        if (count >= least && count <= arity) return true;
        var noun = arity == 1 ? "argument" : "arguments";
        var takes = least == arity ? $"{arity} {noun}" : $"{least} to {arity} arguments";
        Report(WsDiagnosticCodes.WrongArgumentCount, $"'{name}' takes {takes}, not {count}.", span);
        return false;
    }

    private WsBuiltinSymbol UseBuiltin(WsBuiltinDeclaration declaration)
    {
        if (_usedBuiltins.TryGetValue(declaration.Name, out var symbol)) return symbol;
        symbol = new WsBuiltinSymbol(declaration.Name, declaration.Arity, _builtinsInOrder.Count);
        _usedBuiltins.Add(declaration.Name, symbol);
        _builtinsInOrder.Add(symbol);
        return symbol;
    }

    private WsLocalSymbol? LookupLocal(string name)
    {
        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Locals.TryGetValue(name, out var local)) return local;
        }
        return name switch
        {
            "Object" => UseObject(),
            "Error" => UseError(),
            _ when WsPrelude.IsValueClass(name) || WsPrelude.IsErrorClass(name) => UseBuiltinClass(name),
            _ => null
        };
    }

    /// <summary>
    /// A built-in class besides <c>Object</c> and <c>Error</c>, a kind of value's or of runtime error's (section 3.7):
    /// bound, in the scope around the program's, and made where the program begins, once something uses it.
    /// </summary>
    private WsLocalSymbol UseBuiltinClass(string name)
    {
        foreach (var (builtin, declaration) in _builtinClasses)
        {
            if (builtin == name) return declaration.Local;
        }
        var isError = WsPrelude.IsErrorClass(name);
        var baseLocal = isError ? UseError() : UseObject();
        foreach (var (builtin, declaration) in _builtinClasses)
        {
            if (builtin == name) return declaration.Local; // UseError() makes every runtime error's class.
        }
        return BindBuiltinClass(name, WsPrelude.Source(name), baseLocal);
    }

    /// <summary>Binds a built-in class from its source, extending <paramref name="baseLocal"/>, or nothing if null.</summary>
    private WsLocalSymbol BindBuiltinClass(string name, string source, WsLocalSymbol? baseLocal, bool inPrelude = false)
    {
        var local = new WsLocalSymbol(name, WsDeclarationKind.Class, _main.Locals.Count, default, _main);
        _main.Locals.Add(local);
        _prelude.Locals.Add(name, local);
        var syntax = (WsClassDeclarationSyntax)new WsParser(source, name).ParseCompilationUnit().Root.Statements[0];
        var (outerScope, outerFunction, outerClass) = (_scope, _function, _class);
        (_scope, _function, _inPrelude) = (_prelude, _main, inPrelude);
        var baseClass = baseLocal is null ? null : Reference(baseLocal, default);
        var declaration = new WsBoundClassDeclaration(local, BindClass(syntax, local, baseClass, []), default);
        (_scope, _function, _class, _inPrelude) = (outerScope, outerFunction, outerClass, false);
        _builtinClasses.Add((name, declaration));
        return local;
    }

    /// <summary>
    /// The built-in class <c>Error</c>, beside <c>Object</c>, which it extends: bound, and made where the program begins,
    /// once something uses it, a <c>try</c> or a <c>using</c>, whose handlers may catch a runtime error, included.
    /// </summary>
    private WsLocalSymbol UseError()
    {
        if (_error is not null) return _error.Local;
        var local = BindBuiltinClass("Error", ErrorSource, UseObject());
        _error = _builtinClasses[^1].Declaration;
        // Its runtime errors become instances of their kinds' classes, which the program has too.
        foreach (var (name, _) in WsPrelude.ErrorClasses) BindBuiltinClass(name, WsPrelude.Source(name), local);
        return local;
    }

    /// <summary>
    /// The built-in class <c>Object</c>: a local of the program, in a scope around the program's, so that a program may
    /// declare a name <c>Object</c> of its own; bound, and made where the program begins, once something uses it.
    /// </summary>
    private WsLocalSymbol UseObject()
    {
        if (_object is not null) return _object.Local;
        var local = BindBuiltinClass("Object", ObjectSource, null, inPrelude: true);
        _object = _builtinClasses[^1].Declaration;
        return local;
    }

    private void Report(string code, string message, WsSourceSpan span) =>
        _diagnostics.Add(new WsDiagnostic(code, WsDiagnosticSeverity.Error, message, span));

    /// <summary>A block's scope, chained through Parent; <see cref="Function"/> owns the locals declared in it.</summary>
    private sealed class Scope(Scope? parent, FunctionContext function)
    {
        public Scope? Parent { get; } = parent;
        public FunctionContext Function { get; } = function;
        /// <summary>The names declared so far in this block, each from its declaration on.</summary>
        public Dictionary<string, WsLocalSymbol> Locals { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A function being bound: the program, or one nested in it.</summary>
    /// <summary>A class whose methods are being bound: its local, whether it extends another, and whether in its constructor.</summary>
    private sealed record ClassContext(WsLocalSymbol? Local, bool HasBase, bool InConstructor);

    private sealed class FunctionContext(FunctionContext? parent, string name)
    {
        public FunctionContext? Parent { get; } = parent;
        public string Name { get; } = name;
        public List<WsLocalSymbol> Locals { get; } = [];
        public List<WsBoundCapture> Captures { get; } = [];
        public Dictionary<WsLocalSymbol, int> CaptureIndexes { get; } = [];
        public int LoopDepth { get; set; }
        /// <summary>Whether it is <c>async</c>, where <c>await</c> may stand, as at a script's top level (section 3.8).</summary>
        public bool IsAsync { get; set; }
        /// <summary>Whether it awaits: for the program's top level, which is then an <c>async</c> function.</summary>
        public bool Awaits { get; set; }
        /// <summary>The hidden locals of the <c>catch</c> blocks being bound, innermost last, which <c>throw;</c> throws from.</summary>
        public List<(WsLocalSymbol Value, WsLocalSymbol Origin)> Catches { get; } = [];
        /// <summary>The loop depth where each <c>finally</c> block being bound begins, innermost last (WS2012).</summary>
        public List<int> Finallys { get; } = [];
    }
}
