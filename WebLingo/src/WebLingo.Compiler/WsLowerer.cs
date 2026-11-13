using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// Lowers a bound program to the IR (<see cref="WsIrProgram"/>), reducing the bound tree to explicit operations.
/// A local that a closure captures lives in a cell, which every closure of it shares (specification, section 3.5):
/// its block gives it a new cell when it begins (so that a function declared in the block, whose closure is made
/// then too, can capture it), and its declaration initializes the cell. A function declaration's closure is made
/// when its block begins, since the function is in scope throughout. Every function the program declares, at any
/// depth, is lowered into one list.
/// </summary>
public sealed class WsLowerer
{
    private readonly List<WsIrFunction?> _functions = [];

    private WsLowerer() { }

    public static WsIrProgram Lower(WsBoundProgram program)
    {
        var lowerer = new WsLowerer();
        var statements = lowerer.LowerBlock(program.Statements);
        return new WsIrProgram(statements, Names(program.Locals), program.Builtins) { Functions = lowerer._functions! };
    }

    private static string[] Names(IReadOnlyList<WsLocalSymbol> locals)
    {
        var names = new string[locals.Count];
        foreach (var local in locals) names[local.Index] = local.Name;
        return names;
    }

    /// <summary>A block's statements, after the cells of its captured locals and the closures of its functions.</summary>
    private List<WsIrStatement> LowerBlock(IReadOnlyList<WsBoundStatement> block)
    {
        var lowered = new List<WsIrStatement>();
        foreach (var statement in block)
        {
            var local = statement switch
            {
                WsBoundVariableDeclaration declaration => declaration.Local,
                WsBoundFunctionDeclaration function => function.Local,
                _ => null
            };
            if (local is { IsCaptured: true }) lowered.Add(new WsIrNewCell(local.Index, statement.Span));
        }
        foreach (var statement in block)
        {
            if (statement is WsBoundFunctionDeclaration function)
            {
                lowered.Add(Initialize(function.Local, new WsIrMakeClosure(LowerFunction(function.Function), function.Span),
                    function.Span));
            }
        }
        foreach (var statement in block)
        {
            if (statement is not WsBoundFunctionDeclaration) lowered.Add(LowerStatement(statement));
        }
        return lowered;
    }

    private static WsIrStatement Initialize(WsLocalSymbol local, WsIrExpression value, WsSourceSpan span) =>
        local.IsCaptured
            ? new WsIrEvaluate(new WsIrStoreCell(local.Index, value, span), span)
            : new WsIrInitializeLocal(local.Index, value, span);

    private int LowerFunction(WsBoundFunction function)
    {
        var index = _functions.Count;
        _functions.Add(null); // Its place, so that the functions it declares come after it.
        var body = new List<WsIrStatement>();
        // A captured parameter moves into a cell of its own when the function begins.
        for (var i = 0; i < function.ParameterCount; i++)
        {
            var parameter = function.Locals[i];
            if (!parameter.IsCaptured) continue;
            body.Add(new WsIrBoxLocal(parameter.Index, parameter.DeclarationSpan));
        }
        body.AddRange(LowerBlock(function.Body));
        var captures = new List<WsIrCapture>();
        foreach (var capture in function.Captures) captures.Add(new WsIrCapture(capture.FromLocal, capture.Index));
        _functions[index] = new WsIrFunction(function.Name, function.ParameterCount, Names(function.Locals), captures, body,
            function.Span);
        return index;
    }

    private WsIrStatement LowerStatement(WsBoundStatement statement)
    {
        switch (statement)
        {
            case WsBoundVariableDeclaration declaration:
                return Initialize(declaration.Local, LowerExpression(declaration.Initializer), declaration.Span);
            case WsBoundExpressionStatement expressionStatement:
                return new WsIrEvaluate(LowerExpression(expressionStatement.Expression), statement.Span);
            case WsBoundIf conditional:
                return new WsIrIf(LowerExpression(conditional.Condition), LowerStatement(conditional.Then),
                    conditional.Else is null ? null : LowerStatement(conditional.Else), conditional.Span);
            case WsBoundLoop loop:
            {
                var copies = new List<int>();
                foreach (var local in loop.PerIterationLocals)
                {
                    if (local.IsCaptured) copies.Add(local.Index);
                }
                return new WsIrLoop(loop.Condition is null ? null : LowerExpression(loop.Condition), LowerStatement(loop.Body),
                    loop.Step is null ? null : LowerExpression(loop.Step), loop.TestFirst, loop.Span)
                {
                    PerIteration = copies.Count == 0 ? null : new WsIrCopyCells(copies, loop.Span)
                };
            }
            case WsBoundForEach forEach:
            {
                var captured = new List<int>();
                if (forEach.Item.IsCaptured) captured.Add(forEach.Item.Index);
                if (forEach.Value is { IsCaptured: true } value) captured.Add(value.Index);
                return new WsIrForEach(forEach.Collection.Index, LowerExpression(forEach.CollectionValue),
                    forEach.Value is null ? 1 : 2, captured, LowerStatement(forEach.Body), forEach.Span);
            }
            case WsBoundBreak breakStatement:
                return new WsIrBreak(breakStatement.Span);
            case WsBoundContinue continueStatement:
                return new WsIrContinue(continueStatement.Span);
            case WsBoundReturn returnStatement:
                return new WsIrReturn(returnStatement.Value is null ? null : LowerExpression(returnStatement.Value),
                    returnStatement.Span);
            case WsBoundBlock block:
                return new WsIrBlock(LowerBlock(block.Statements), block.Span);
            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
        }
    }

    private WsIrExpression LowerExpression(WsBoundExpression expression)
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
                return reference.Local.IsCaptured
                    ? new WsIrLoadCell(reference.Local.Index, reference.Span)
                    : new WsIrLoadLocal(reference.Local.Index, reference.Span);
            case WsBoundCaptureReference capture:
                return new WsIrLoadCapture(capture.Index, capture.Span);
            case WsBoundUnary unary:
                return new WsIrUnary(unary.Operator, LowerExpression(unary.Operand), unary.Span);
            case WsBoundBinary binary:
                var left = LowerExpression(binary.Left);
                var right = LowerExpression(binary.Right);
                return new WsIrBinary(binary.Operator, left, right, binary.Span);
            case WsBoundAssignment assignment:
                return assignment.Local.IsCaptured
                    ? new WsIrStoreCell(assignment.Local.Index, LowerExpression(assignment.Value), assignment.Span)
                    : new WsIrStoreLocal(assignment.Local.Index, LowerExpression(assignment.Value), assignment.Span);
            case WsBoundCaptureAssignment assignment:
                return new WsIrStoreCapture(assignment.Index, LowerExpression(assignment.Value), assignment.Span);
            case WsBoundBuiltinReference builtin:
                return new WsIrLoadBuiltin(builtin.Builtin.Index, builtin.Span);
            case WsBoundFunctionExpression function:
                return new WsIrMakeClosure(LowerFunction(function.Function), function.Span);
            case WsBoundBuiltinCall call:
                return new WsIrCallBuiltin(call.Builtin.Index, LowerArguments(call.Arguments), call.Span);
            case WsBoundCall call:
                return new WsIrCall(LowerExpression(call.Callee), LowerArguments(call.Arguments), call.Span);
            case WsBoundArrayLiteral array:
                return new WsIrNewArray(LowerArguments(array.Elements), array.Span);
            case WsBoundMapLiteral map:
            {
                var entries = new List<WsIrMapEntry>();
                foreach (var (key, value) in map.Entries) entries.Add(new WsIrMapEntry(key, LowerExpression(value)));
                return new WsIrNewMap(entries, map.Span);
            }
            case WsBoundIndex index:
                return new WsIrIndex(LowerExpression(index.Target), LowerExpression(index.Index), index.Span);
            case WsBoundIndexAssignment element:
                return new WsIrStoreIndex(LowerExpression(element.Target), LowerExpression(element.Index),
                    LowerExpression(element.Value), element.Span);
            case WsBoundMember member:
                return new WsIrMember(LowerExpression(member.Target), member.Name, member.Span);
            case WsBoundMethodCall call:
                return new WsIrCallMethod(LowerExpression(call.Target), call.Name, LowerArguments(call.Arguments), call.Span);
            default:
                throw new InvalidOperationException($"Cannot lower {expression.GetType().Name}: binding had errors.");
        }
    }

    private List<WsIrExpression> LowerArguments(IReadOnlyList<WsBoundExpression> arguments)
    {
        var lowered = new List<WsIrExpression>();
        foreach (var argument in arguments) lowered.Add(LowerExpression(argument));
        return lowered;
    }
}
