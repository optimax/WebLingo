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

    public WsBinder(WsCompilerOptions options)
    {
        foreach (var builtin in options.Builtins) _hostBuiltins[builtin.Name] = builtin;
        _function = new FunctionContext(null, "main");
        _scope = new Scope(null, _function);
    }

    public IReadOnlyList<WsDiagnostic> Diagnostics => _diagnostics;

    public WsBoundProgram Bind(WsCompilationUnitSyntax unit)
    {
        var statements = BindStatements(unit.Statements);
        return new WsBoundProgram(statements, _function.Locals, _builtinsInOrder);
    }

    /// <summary>A block's statements, in the current scope, its function declarations declared first.</summary>
    private List<WsBoundStatement> BindStatements(IReadOnlyList<WsStatementSyntax> syntax)
    {
        var declared = new Dictionary<WsFunctionDeclarationSyntax, WsLocalSymbol>();
        foreach (var statement in syntax)
        {
            if (statement is not WsFunctionDeclarationSyntax function) continue;
            if (Declare(function.Name, WsDeclarationKind.Function, function.NameSpan, function.Parameters.Count) is { } local)
            {
                declared.Add(function, local);
            }
        }
        var statements = new List<WsBoundStatement>();
        foreach (var statement in syntax)
        {
            if (statement is WsFunctionDeclarationSyntax function)
            {
                var bound = BindFunction(function.Name, function.Parameters, function.Body, function.Span);
                if (declared.TryGetValue(function, out var local))
                {
                    statements.Add(new WsBoundFunctionDeclaration(local, bound, function.Span));
                }
                continue;
            }
            statements.Add(BindStatement(statement));
        }
        return statements;
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
            case WsBreakSyntax or WsContinueSyntax:
            {
                var isBreak = statement is WsBreakSyntax;
                if (_function.LoopDepth == 0)
                {
                    Report(WsDiagnosticCodes.NotInLoop, $"'{(isBreak ? "break" : "continue")}' is only allowed in a loop.",
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
                return new WsBoundReturn(value, statement.Span);
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

    /// <summary>A function's body, in a function context of its own, its parameters its first locals.</summary>
    private WsBoundFunction BindFunction(string name, IReadOnlyList<WsParameterSyntax> parameters, WsBlockSyntax body,
        WsSourceSpan span)
    {
        var outerFunction = _function;
        var outerScope = _scope;
        _function = new FunctionContext(outerFunction, name);
        _scope = new Scope(outerScope, _function);
        foreach (var parameter in parameters) Declare(parameter.Name, WsDeclarationKind.Parameter, parameter.Span, null);
        _scope = new Scope(_scope, _function); // The body may declare a name its parameters have, shadowing it.
        var statements = BindStatements(body.Statements);
        var bound = new WsBoundFunction(name, parameters.Count, _function.Locals, _function.Captures, statements, span);
        _function = outerFunction;
        _scope = outerScope;
        return bound;
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
            case WsMemberAccessSyntax member:
                return new WsBoundMember(BindExpression(member.Target), member.Name, member.Span);
            case WsFunctionExpressionSyntax function:
                return new WsBoundFunctionExpression(BindFunction("", function.Parameters, function.Body, function.Span),
                    function.Span);
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
        if (calleeSyntax is WsMemberAccessSyntax member)
        {
            // A method, called on its target (specification, section 3.6).
            return new WsBoundMethodCall(BindExpression(member.Target), member.Name, arguments, call.Span);
        }
        if (calleeSyntax is WsIntegerLiteralSyntax or WsBinary64LiteralSyntax or WsBooleanLiteralSyntax
            or WsNullLiteralSyntax or WsStringLiteralSyntax)
        {
            Report(WsDiagnosticCodes.NotAFunction, "A literal is not a function, and cannot be called.", calleeSyntax.Span);
            return new WsBoundError(call.Span);
        }
        if (calleeSyntax is WsNameSyntax name && LookupLocal(name.Name) is null
            && _hostBuiltins.TryGetValue(name.Name, out var declaration))
        {
            // A built-in called by its name: checked now, and called directly.
            if (!CheckArity(name.Name, declaration.Arity, arguments.Count, call.Span)) return new WsBoundError(call.Span);
            return new WsBoundBuiltinCall(UseBuiltin(declaration), arguments, call.Span);
        }
        if (calleeSyntax is WsNameSyntax functionName && LookupLocal(functionName.Name) is { FunctionArity: { } arity }
            && !CheckArity(functionName.Name, arity, arguments.Count, call.Span))
        {
            return new WsBoundError(call.Span);
        }
        return new WsBoundCall(BindExpression(calleeSyntax), arguments, call.Span);
    }

    private bool CheckArity(string name, int arity, int count, WsSourceSpan span)
    {
        if (count == arity) return true;
        var noun = arity == 1 ? "argument" : "arguments";
        Report(WsDiagnosticCodes.WrongArgumentCount, $"'{name}' takes {arity} {noun}, not {count}.", span);
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
        return null;
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
    private sealed class FunctionContext(FunctionContext? parent, string name)
    {
        public FunctionContext? Parent { get; } = parent;
        public string Name { get; } = name;
        public List<WsLocalSymbol> Locals { get; } = [];
        public List<WsBoundCapture> Captures { get; } = [];
        public Dictionary<WsLocalSymbol, int> CaptureIndexes { get; } = [];
        public int LoopDepth { get; set; }
    }
}
