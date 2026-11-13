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

public abstract record WsIrExpression(WsSourceSpan Span);

public sealed record WsIrInt64Constant(long Value, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrBinary64Constant(double Value, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>Reads a local.</summary>
public sealed record WsIrLoadLocal(int Local, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>Assigns a local and yields the value.</summary>
public sealed record WsIrStoreLocal(int Local, WsIrExpression Value, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrUnary(WsUnaryOperator Operator, WsIrExpression Operand, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrBinary(WsBinaryOperator Operator, WsIrExpression Left, WsIrExpression Right, WsSourceSpan Span)
    : WsIrExpression(Span);

public sealed record WsIrCallBuiltin(int Builtin, IReadOnlyList<WsIrExpression> Arguments, WsSourceSpan Span)
    : WsIrExpression(Span);
