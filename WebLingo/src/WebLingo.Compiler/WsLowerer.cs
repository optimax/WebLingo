namespace WebLingo.Compiler;

/// <summary>
/// Lowers a bound program to the IR (<see cref="WsIrProgram"/>). It follows each local through the program, in
/// evaluation order, from the temporal dead zone (before its declaration runs) to declared without a value
/// (<c>var x;</c>) to having one, and keeps a runtime check only where a read or a store would otherwise go
/// unchecked. M0.6's programs are straight-line, so what it follows is exact: a check it keeps always fails.
/// Branches and loops will make it a may-analysis that keeps the checks it cannot decide.
/// </summary>
public static class WsLowerer
{
    public static WsIrProgram Lower(WsBoundProgram program)
    {
        var state = new LocalState[program.Locals.Count];
        var statements = new List<WsIrStatement>();
        foreach (var statement in program.Statements)
        {
            switch (statement)
            {
                case WsBoundVariableDeclaration { Initializer: null } declaration:
                    statements.Add(new WsIrDeclareUnassigned(declaration.Local.Index, declaration.Span));
                    state[declaration.Local.Index] = LocalState.Unassigned; // As the declaration does at run time.
                    break;
                case WsBoundVariableDeclaration declaration:
                    var value = LowerExpression(declaration.Initializer!, state);
                    statements.Add(new WsIrInitializeLocal(declaration.Local.Index, value, declaration.Span));
                    state[declaration.Local.Index] = LocalState.Assigned;
                    break;
                case WsBoundExpressionStatement expressionStatement:
                    statements.Add(new WsIrEvaluate(LowerExpression(expressionStatement.Expression, state), statement.Span));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
            }
        }
        var names = new string[program.Locals.Count];
        foreach (var local in program.Locals) names[local.Index] = local.Name;
        return new WsIrProgram(statements, names, program.Builtins);
    }

    private static WsIrExpression LowerExpression(WsBoundExpression expression, LocalState[] state)
    {
        switch (expression)
        {
            case WsBoundIntegerLiteral literal:
                return new WsIrInt64Constant(literal.Value, literal.Span);
            case WsBoundLocalReference reference:
                return new WsIrLoadLocal(reference.Local.Index, state[reference.Local.Index] != LocalState.Assigned,
                    reference.Span);
            case WsBoundUnary unary:
                return new WsIrUnary(unary.Operator, LowerExpression(unary.Operand, state), unary.Span);
            case WsBoundBinary binary:
                var left = LowerExpression(binary.Left, state);
                var right = LowerExpression(binary.Right, state);
                return new WsIrBinary(binary.Operator, left, right, binary.Span);
            case WsBoundAssignment assignment:
                var value = LowerExpression(assignment.Value, state);
                var local = assignment.Local.Index;
                var store = new WsIrStoreLocal(local, value, state[local] == LocalState.DeadZone, assignment.Span);
                state[local] = LocalState.Assigned;
                return store;
            case WsBoundBuiltinCall call:
                var arguments = new List<WsIrExpression>();
                foreach (var argument in call.Arguments) arguments.Add(LowerExpression(argument, state));
                return new WsIrCallBuiltin(call.Builtin.Index, arguments, call.Span);
            default:
                throw new InvalidOperationException($"Cannot lower {expression.GetType().Name}: binding had errors.");
        }
    }

    private enum LocalState : byte { DeadZone, Unassigned, Assigned }
}
