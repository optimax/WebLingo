using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// Lowers a bound program to the IR (<see cref="WsIrProgram"/>). Every local has a value from its declaration on
/// (specification, section 3.1), so lowering adds no checks: it reduces the bound tree to explicit operations.
/// </summary>
public static class WsLowerer
{
    public static WsIrProgram Lower(WsBoundProgram program)
    {
        var statements = new List<WsIrStatement>();
        foreach (var statement in program.Statements) statements.Add(LowerStatement(statement));
        var names = new string[program.Locals.Count];
        foreach (var local in program.Locals) names[local.Index] = local.Name;
        return new WsIrProgram(statements, names, program.Builtins);
    }

    private static WsIrStatement LowerStatement(WsBoundStatement statement)
    {
        switch (statement)
        {
            case WsBoundVariableDeclaration declaration:
                return new WsIrInitializeLocal(declaration.Local.Index, LowerExpression(declaration.Initializer), declaration.Span);
            case WsBoundExpressionStatement expressionStatement:
                return new WsIrEvaluate(LowerExpression(expressionStatement.Expression), statement.Span);
            case WsBoundIf conditional:
                return new WsIrIf(LowerExpression(conditional.Condition), LowerStatement(conditional.Then),
                    conditional.Else is null ? null : LowerStatement(conditional.Else), conditional.Span);
            case WsBoundLoop loop:
                return new WsIrLoop(loop.Condition is null ? null : LowerExpression(loop.Condition), LowerStatement(loop.Body),
                    loop.Step is null ? null : LowerExpression(loop.Step), loop.TestFirst, loop.Span);
            case WsBoundBreak breakStatement:
                return new WsIrBreak(breakStatement.Span);
            case WsBoundContinue continueStatement:
                return new WsIrContinue(continueStatement.Span);
            case WsBoundBlock block:
            {
                var statements = new List<WsIrStatement>();
                foreach (var inner in block.Statements) statements.Add(LowerStatement(inner));
                return new WsIrBlock(statements, block.Span);
            }
            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
        }
    }

    private static WsIrExpression LowerExpression(WsBoundExpression expression)
    {
        switch (expression)
        {
            case WsBoundIntegerLiteral literal:
                return new WsIrInt64Constant(literal.Value, literal.Span);
            case WsBoundBinary64Literal literal:
                return new WsIrBinary64Constant(literal.Value, literal.Span);
            case WsBoundBooleanLiteral literal:
                return new WsIrBooleanConstant(literal.Value, literal.Span);
            case WsBoundNullLiteral literal:
                return new WsIrNullConstant(literal.Span);
            case WsBoundStringLiteral literal:
                return new WsIrStringConstant(literal.Value, literal.Span);
            case WsBoundBinary { Operator: WsBinaryOperator.And or WsBinaryOperator.Or } logical:
                return new WsIrLogical(logical.Operator == WsBinaryOperator.And, LowerExpression(logical.Left),
                    LowerExpression(logical.Right), logical.Span);
            case WsBoundLocalReference reference:
                return new WsIrLoadLocal(reference.Local.Index, reference.Span);
            case WsBoundUnary unary:
                return new WsIrUnary(unary.Operator, LowerExpression(unary.Operand), unary.Span);
            case WsBoundBinary binary:
                var left = LowerExpression(binary.Left);
                var right = LowerExpression(binary.Right);
                return new WsIrBinary(binary.Operator, left, right, binary.Span);
            case WsBoundAssignment assignment:
                return new WsIrStoreLocal(assignment.Local.Index, LowerExpression(assignment.Value), assignment.Span);
            case WsBoundBuiltinCall call:
                var arguments = new List<WsIrExpression>();
                foreach (var argument in call.Arguments) arguments.Add(LowerExpression(argument));
                return new WsIrCallBuiltin(call.Builtin.Index, arguments, call.Span);
            default:
                throw new InvalidOperationException($"Cannot lower {expression.GetType().Name}: binding had errors.");
        }
    }
}
