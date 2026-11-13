using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

// The lowered IR (specification, section 8.4, "Binder/IR"): the bound program reduced to explicit operations. It is
// a tree, neither register- nor stack-shaped, so that either kind of VM can be generated from it (WS-BC002).
// Evaluation order is left to right; an assignment evaluates its value before it stores it (section 15.2). Every
// local has a value from its declaration on (section 3.1), so no operation needs a check that it has one.

/// <summary>A lowered program; <see cref="LocalNames"/> are indexed by local, for runtime error messages.</summary>
public sealed record WsIrProgram(
    IReadOnlyList<WsIrStatement> Statements, IReadOnlyList<string> LocalNames, IReadOnlyList<WsBuiltinSymbol> Builtins);

public abstract record WsIrStatement(WsSourceSpan Span);

/// <summary>Evaluates an expression and discards its value.</summary>
public sealed record WsIrEvaluate(WsIrExpression Expression, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary><c>var x = e;</c> or <c>const x = e;</c>: the local gets its first value.</summary>
public sealed record WsIrInitializeLocal(int Local, WsIrExpression Value, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary>Statements in order. Scopes are resolved already: a block only groups its statements.</summary>
public sealed record WsIrBlock(IReadOnlyList<WsIrStatement> Statements, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary>Runs <paramref name="Then"/> if the condition, which must be a Boolean, is true, else <paramref name="Else"/>.</summary>
public sealed record WsIrIf(WsIrExpression Condition, WsIrStatement Then, WsIrStatement? Else, WsSourceSpan Span)
    : WsIrStatement(Span);

/// <summary>A loop; see <c>WsBoundLoop</c>. A missing condition is true.</summary>
public sealed record WsIrLoop(WsIrExpression? Condition, WsIrStatement Body, WsIrExpression? Step, bool TestFirst, WsSourceSpan Span)
    : WsIrStatement(Span);

/// <summary>Leaves the innermost loop.</summary>
public sealed record WsIrBreak(WsSourceSpan Span) : WsIrStatement(Span);

/// <summary>Goes on to the innermost loop's step, then its condition.</summary>
public sealed record WsIrContinue(WsSourceSpan Span) : WsIrStatement(Span);

public abstract record WsIrExpression(WsSourceSpan Span);

public sealed record WsIrInt64Constant(long Value, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrBinary64Constant(double Value, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrBooleanConstant(bool Value, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrNullConstant(WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>
/// <c>&amp;&amp;</c> (<paramref name="IsAnd"/>) or <c>||</c>: the left operand, which must be a Boolean, and only if it
/// does not decide the result, the right one, which must be a Boolean too.
/// </summary>
public sealed record WsIrLogical(bool IsAnd, WsIrExpression Left, WsIrExpression Right, WsSourceSpan Span)
    : WsIrExpression(Span);

/// <summary>Reads a local.</summary>
public sealed record WsIrLoadLocal(int Local, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>Assigns a local and yields the value.</summary>
public sealed record WsIrStoreLocal(int Local, WsIrExpression Value, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrUnary(WsUnaryOperator Operator, WsIrExpression Operand, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrBinary(WsBinaryOperator Operator, WsIrExpression Left, WsIrExpression Right, WsSourceSpan Span)
    : WsIrExpression(Span);

public sealed record WsIrCallBuiltin(int Builtin, IReadOnlyList<WsIrExpression> Arguments, WsSourceSpan Span)
    : WsIrExpression(Span);
