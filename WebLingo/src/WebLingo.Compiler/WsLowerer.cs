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
        foreach (var statement in program.Statements)
        {
            switch (statement)
            {
                case WsBoundVariableDeclaration declaration:
                    statements.Add(new WsIrInitializeLocal(declaration.Local.Index, LowerExpression(declaration.Initializer),
                        declaration.Span));
                    break;
                case WsBoundExpressionStatement expressionStatement:
                    statements.Add(new WsIrEvaluate(LowerExpression(expressionStatement.Expression), statement.Span));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
            }
        }
        var names = new string[program.Locals.Count];
        foreach (var local in program.Locals) names[local.Index] = local.Name;
        return new WsIrProgram(statements, names, program.Builtins);
    }

    private static WsIrExpression LowerExpression(WsBoundExpression expression)
    {
        switch (expression)
        {
            case WsBoundIntegerLiteral literal:
                return new WsIrInt64Constant(literal.Value, literal.Span);
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
