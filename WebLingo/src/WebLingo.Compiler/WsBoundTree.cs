using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

// The bound tree: the syntax tree with every name resolved to its symbol. Parentheses are gone; spans remain, for
// the diagnostics of later stages.

/// <summary>A bound program: its statements, its locals in declaration order, and the built-ins it calls.</summary>
public sealed record WsBoundProgram(
    IReadOnlyList<WsBoundStatement> Statements, IReadOnlyList<WsLocalSymbol> Locals, IReadOnlyList<WsBuiltinSymbol> Builtins);

/// <summary>
/// Where a function's capture comes from when its closure is made: a local of the function that makes it
/// (<paramref name="FromLocal"/>), or one of that function's own captures.
/// </summary>
public sealed record WsBoundCapture(bool FromLocal, int Index, WsLocalSymbol Local);

/// <summary>
/// A function: its parameters (its first locals), all its locals, what it captures from the functions around it,
/// and its body.
/// </summary>
public sealed record WsBoundFunction(
    string Name, int ParameterCount, IReadOnlyList<WsLocalSymbol> Locals, IReadOnlyList<WsBoundCapture> Captures,
    IReadOnlyList<WsBoundStatement> Body, WsSourceSpan Span);

public abstract record WsBoundStatement(WsSourceSpan Span);

public sealed record WsBoundVariableDeclaration(WsLocalSymbol Local, WsBoundExpression Initializer, WsSourceSpan Span)
    : WsBoundStatement(Span);

public sealed record WsBoundExpressionStatement(WsBoundExpression Expression, WsSourceSpan Span) : WsBoundStatement(Span);

public sealed record WsBoundBlock(IReadOnlyList<WsBoundStatement> Statements, WsSourceSpan Span) : WsBoundStatement(Span);

/// <summary>A function declaration; its closure is made when its block begins, since it is in scope throughout.</summary>
public sealed record WsBoundFunctionDeclaration(WsLocalSymbol Local, WsBoundFunction Function, WsSourceSpan Span)
    : WsBoundStatement(Span);

public sealed record WsBoundReturn(WsBoundExpression? Value, WsSourceSpan Span) : WsBoundStatement(Span);

public sealed record WsBoundIf(WsBoundExpression Condition, WsBoundStatement Then, WsBoundStatement? Else, WsSourceSpan Span)
    : WsBoundStatement(Span);

/// <summary>
/// A loop: <c>while</c> and <c>for</c> test first (<paramref name="TestFirst"/>), <c>do … while</c> last; a missing
/// condition is true. <paramref name="Step"/> runs after the body and after each <c>continue</c>.
/// </summary>
public sealed record WsBoundLoop(
    WsBoundExpression? Condition, WsBoundStatement Body, WsBoundExpression? Step, bool TestFirst, WsSourceSpan Span)
    : WsBoundStatement(Span)
{
    /// <summary>A <c>for</c>'s variables, each of which gets a binding of its own for every iteration.</summary>
    public IReadOnlyList<WsLocalSymbol> PerIterationLocals { get; init; } = [];
}

public sealed record WsBoundBreak(WsSourceSpan Span) : WsBoundStatement(Span);

public sealed record WsBoundContinue(WsSourceSpan Span) : WsBoundStatement(Span);

public abstract record WsBoundExpression(WsSourceSpan Span);

public sealed record WsBoundIntegerLiteral(long Value, WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundBinary64Literal(double Value, WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundBooleanLiteral(bool Value, WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundNullLiteral(WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundStringLiteral(string Value, WsSourceSpan Span) : WsBoundExpression(Span);

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

/// <summary>A local of a function around this one, through this function's capture <paramref name="Index"/>.</summary>
public sealed record WsBoundCaptureReference(int Index, WsLocalSymbol Local, WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundCaptureAssignment(int Index, WsLocalSymbol Local, WsBoundExpression Value, WsSourceSpan Span)
    : WsBoundExpression(Span);

/// <summary>A built-in used as a value.</summary>
public sealed record WsBoundBuiltinReference(WsBuiltinSymbol Builtin, WsSourceSpan Span) : WsBoundExpression(Span);

public sealed record WsBoundFunctionExpression(WsBoundFunction Function, WsSourceSpan Span) : WsBoundExpression(Span);

/// <summary>A call of any value, checked to be a function, with the right number of arguments, at run time.</summary>
public sealed record WsBoundCall(WsBoundExpression Callee, IReadOnlyList<WsBoundExpression> Arguments, WsSourceSpan Span)
    : WsBoundExpression(Span);
