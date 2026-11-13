using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

// The lowered IR (specification, section 8.4, "Binder/IR"): the bound program reduced to explicit operations, with
// the temporal dead zone made explicit as checks. It is a tree, neither register- nor stack-shaped, so that either
// kind of VM can be generated from it (WS-BC002). Evaluation order is left to right; an assignment evaluates its
// value before it checks and stores (section 15.2).

/// <summary>A lowered program; <see cref="LocalNames"/> are indexed by local, for runtime error messages.</summary>
public sealed record WsIrProgram(
    IReadOnlyList<WsIrStatement> Statements, IReadOnlyList<string> LocalNames, IReadOnlyList<WsBuiltinSymbol> Builtins);

public abstract record WsIrStatement(WsSourceSpan Span);

/// <summary>Evaluates an expression and discards its value.</summary>
public sealed record WsIrEvaluate(WsIrExpression Expression, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary><c>var x;</c> runs: the local leaves the temporal dead zone, declared but without a value.</summary>
public sealed record WsIrDeclareUnassigned(int Local, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary><c>var x = e;</c> or <c>const x = e;</c> runs: the local leaves the temporal dead zone with a value.</summary>
public sealed record WsIrInitializeLocal(int Local, WsIrExpression Value, WsSourceSpan Span) : WsIrStatement(Span);

public abstract record WsIrExpression(WsSourceSpan Span);

public sealed record WsIrInt64Constant(long Value, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>
/// Reads a local. With <paramref name="CheckInitialized"/>, the read fails (WS2004) if the local is still in its
/// temporal dead zone or has no value yet; the lowerer drops the check where the local is known to have a value.
/// </summary>
public sealed record WsIrLoadLocal(int Local, bool CheckInitialized, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>
/// Assigns a local and yields the value. With <paramref name="CheckDeclared"/>, the store fails (WS2004) if the
/// local is still in its temporal dead zone.
/// </summary>
public sealed record WsIrStoreLocal(int Local, WsIrExpression Value, bool CheckDeclared, WsSourceSpan Span)
    : WsIrExpression(Span);

public sealed record WsIrUnary(WsUnaryOperator Operator, WsIrExpression Operand, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrBinary(WsBinaryOperator Operator, WsIrExpression Left, WsIrExpression Right, WsSourceSpan Span)
    : WsIrExpression(Span);

public sealed record WsIrCallBuiltin(int Builtin, IReadOnlyList<WsIrExpression> Arguments, WsSourceSpan Span)
    : WsIrExpression(Span);
