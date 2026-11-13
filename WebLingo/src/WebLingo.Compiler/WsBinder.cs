using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// The binder (specification, section 8.4, "Binder/IR"; its choices are in section 15.2). It resolves every name
/// lexically. A declaration takes effect where it stands, after its initializer (section 3.1): before it, and in
/// its own initializer, the name means what it means outside, and is undefined (WS2001) if nothing outside
/// declares it. The host's built-ins are in a scope around the program's, so a declaration may shadow one. Binding
/// goes on after an error, so that every binding error is reported.
/// </summary>
public sealed class WsBinder
{
    private readonly Dictionary<string, WsBuiltinDeclaration> _hostBuiltins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WsBuiltinSymbol> _usedBuiltins = new(StringComparer.Ordinal);
    private readonly List<WsBuiltinSymbol> _builtinsInOrder = [];
    private readonly List<WsLocalSymbol> _locals = [];
    private readonly List<WsDiagnostic> _diagnostics = [];
    private Scope _scope = new(null);

    public WsBinder(WsCompilerOptions options)
    {
        foreach (var builtin in options.Builtins) _hostBuiltins[builtin.Name] = builtin;
    }

    public IReadOnlyList<WsDiagnostic> Diagnostics => _diagnostics;

    public WsBoundProgram Bind(WsCompilationUnitSyntax unit)
    {
        var statements = new List<WsBoundStatement>();
        foreach (var statement in unit.Statements) statements.Add(BindStatement(statement));
        return new WsBoundProgram(statements, _locals, _builtinsInOrder);
    }

    private WsBoundStatement BindStatement(WsStatementSyntax statement)
    {
        switch (statement)
        {
            case WsVariableDeclarationSyntax declaration:
                // The initializer is bound first: the name it declares does not exist in it yet.
                var initializer = declaration.Initializer is null
                    ? new WsBoundError(declaration.Span)
                    : BindExpression(declaration.Initializer);
                if (_scope.Locals.ContainsKey(declaration.Name))
                {
                    Report(WsDiagnosticCodes.DuplicateDeclaration,
                        $"'{declaration.Name}' is already declared in this scope.", declaration.NameSpan);
                    return new WsBoundExpressionStatement(initializer, declaration.Span);
                }
                var local = new WsLocalSymbol(declaration.Name, declaration.Kind, _locals.Count, declaration.NameSpan);
                _locals.Add(local);
                _scope.Locals.Add(declaration.Name, local);
                return new WsBoundVariableDeclaration(local, initializer, declaration.Span);
            case WsExpressionStatementSyntax expressionStatement:
                return new WsBoundExpressionStatement(BindExpression(expressionStatement.Expression), statement.Span);
            case WsIfSyntax conditional:
            {
                var condition = BindExpression(conditional.Condition);
                var then = BindInOwnScope(conditional.Then);
                var otherwise = conditional.Else is null ? null : BindInOwnScope(conditional.Else);
                return new WsBoundIf(condition, then, otherwise, conditional.Span);
            }
            case WsBlockSyntax block:
            {
                _scope = new Scope(_scope);
                var statements = new List<WsBoundStatement>();
                foreach (var inner in block.Statements) statements.Add(BindStatement(inner));
                _scope = _scope.Parent!;
                return new WsBoundBlock(statements, block.Span);
            }
            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
        }
    }

    /// <summary>A branch or a loop's body: a declaration in it, without braces, is in scope only there.</summary>
    private WsBoundStatement BindInOwnScope(WsStatementSyntax statement)
    {
        _scope = new Scope(_scope);
        var bound = BindStatement(statement);
        _scope = _scope.Parent!;
        return bound;
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
            case WsErrorExpressionSyntax error:
                return new WsBoundError(error.Span);
            default:
                throw new InvalidOperationException($"Unknown expression {expression.GetType().Name}.");
        }
    }

    private WsBoundExpression BindName(WsNameSyntax name)
    {
        if (LookupLocal(name.Name) is { } local) return new WsBoundLocalReference(local, name.Span);
        if (_hostBuiltins.ContainsKey(name.Name))
        {
            Report(WsDiagnosticCodes.InvalidOperation,
                $"'{name.Name}' is a built-in function: it can be called, but not used as a value yet.", name.Span);
            return new WsBoundError(name.Span);
        }
        Report(WsDiagnosticCodes.UndefinedIdentifier, $"'{name.Name}' is not defined.", name.Span);
        return new WsBoundError(name.Span);
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
            Report(WsDiagnosticCodes.AssignmentToConstant, $"'{target.Name}' is a constant and cannot be assigned.",
                target.Span);
            return new WsBoundError(assignment.Span);
        }
        return new WsBoundAssignment(local, value, assignment.Span);
    }

    private WsBoundExpression BindCall(WsCallExpressionSyntax call)
    {
        var arguments = new List<WsBoundExpression>();
        foreach (var argument in call.Arguments) arguments.Add(BindExpression(argument));

        var calleeSyntax = call.Callee;
        while (calleeSyntax is WsParenthesizedExpressionSyntax parenthesized) calleeSyntax = parenthesized.Expression;
        if (calleeSyntax is WsErrorExpressionSyntax) return new WsBoundError(call.Span);
        if (calleeSyntax is not WsNameSyntax callee)
        {
            BindExpression(calleeSyntax);
            Report(WsDiagnosticCodes.NotAFunction, "Only a built-in function can be called.", calleeSyntax.Span);
            return new WsBoundError(call.Span);
        }
        if (LookupLocal(callee.Name) is not null)
        {
            Report(WsDiagnosticCodes.NotAFunction, $"'{callee.Name}' is a variable, not a function.", callee.Span);
            return new WsBoundError(call.Span);
        }
        if (!_hostBuiltins.TryGetValue(callee.Name, out var declaration))
        {
            Report(WsDiagnosticCodes.UndefinedIdentifier, $"'{callee.Name}' is not defined.", callee.Span);
            return new WsBoundError(call.Span);
        }
        if (arguments.Count != declaration.Arity)
        {
            var noun = declaration.Arity == 1 ? "argument" : "arguments";
            Report(WsDiagnosticCodes.WrongArgumentCount,
                $"'{callee.Name}' takes {declaration.Arity} {noun}, not {arguments.Count}.", call.Span);
            return new WsBoundError(call.Span);
        }
        return new WsBoundBuiltinCall(UseBuiltin(declaration), arguments, call.Span);
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

    /// <summary>A block's scope: the program's, or a nested block's, chained through Parent.</summary>
    private sealed class Scope(Scope? parent)
    {
        public Scope? Parent { get; } = parent;
        /// <summary>The names declared so far in this block, each from its declaration on.</summary>
        public Dictionary<string, WsLocalSymbol> Locals { get; } = new(StringComparer.Ordinal);
    }
}
