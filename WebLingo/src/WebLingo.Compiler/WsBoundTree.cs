using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

// The bound tree: the syntax tree with every name resolved to its symbol. Parentheses are gone; spans remain, for
// the diagnostics of later stages.

/// <summary>A bound program: its statements, its locals in declaration order, and the built-ins it calls.</summary>
public sealed record WsBoundProgram(
    IReadOnlyList<WsBoundStatement> Statements, IReadOnlyList<WsLocalSymbol> Locals, IReadOnlyList<WsBuiltinSymbol> Builtins);

public abstract record WsBoundStatement(WsSourceSpan Span);

public sealed record WsBoundVariableDeclaration(WsLocalSymbol Local, WsBoundExpression Initializer, WsSourceSpan Span)
    : WsBoundStatement(Span);

public sealed record WsBoundExpressionStatement(WsBoundExpression Expression, WsSourceSpan Span) : WsBoundStatement(Span);

public sealed record WsBoundBlock(IReadOnlyList<WsBoundStatement> Statements, WsSourceSpan Span) : WsBoundStatement(Span);

public sealed record WsBoundIf(WsBoundExpression Condition, WsBoundStatement Then, WsBoundStatement? Else, WsSourceSpan Span)
    : WsBoundStatement(Span);

/// <summary>
/// A loop: <c>while</c> and <c>for</c> test first (<paramref name="TestFirst"/>), <c>do … while</c> last; a missing
/// condition is true. <paramref name="Step"/> runs after the body and after each <c>continue</c>.
/// </summary>
public sealed record WsBoundLoop(
    WsBoundExpression? Condition, WsBoundStatement Body, WsBoundExpression? Step, bool TestFirst, WsSourceSpan Span)
    : WsBoundStatement(Span);

public sealed record WsBoundBreak(WsSourceSpan Span) : WsBoundStatement(Span);

public sealed record WsBoundContinue(WsSourceSpan Span) : WsBoundStatement(Span);

public abstract record WsBoundExpression(WsSourceSpan Span);

public sealed record WsBoundIntegerLiteral(long Value, WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundBinary64Literal(double Value, WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundBooleanLiteral(bool Value, WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundNullLiteral(WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundLocalReference(WsLocalSymbol Local, WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundUnary(WsUnaryOperator Operator, WsBoundExpression Operand, WsSourceSpan Span)
    : WsBoundExpression(Span);

public sealed record WsBoundBinary(
    WsBinaryOperator Operator, WsBoundExpression Left, WsBoundExpression Right, WsSourceSpan OperatorSpan, WsSourceSpan Span)
    : WsBoundExpression(Span);

public sealed record WsBoundAssignment(WsLocalSymbol Local, WsBoundExpression Value, WsSourceSpan Span)
    : WsBoundExpression(Span);

public sealed record WsBoundBuiltinCall(WsBuiltinSymbol Builtin, IReadOnlyList<WsBoundExpression> Arguments, WsSourceSpan Span)
    : WsBoundExpression(Span);

/// <summary>Where binding failed; its diagnostic is already reported, and nothing is lowered from it.</summary>
public sealed record WsBoundError(WsSourceSpan Span) : WsBoundExpression(Span);
