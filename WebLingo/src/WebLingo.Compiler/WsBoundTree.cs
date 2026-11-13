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
    IReadOnlyList<WsBoundStatement> Body, WsSourceSpan Span)
{
    /// <summary>A method's or a constructor's: its first parameter is <c>this</c>, which its caller gives (section 3.7).</summary>
    public bool HasThis { get; init; }
}

public abstract record WsBoundStatement(WsSourceSpan Span);

public sealed record WsBoundVariableDeclaration(WsLocalSymbol Local, WsBoundExpression Initializer, WsSourceSpan Span)
    : WsBoundStatement(Span);

public sealed record WsBoundExpressionStatement(WsBoundExpression Expression, WsSourceSpan Span) : WsBoundStatement(Span);

public sealed record WsBoundBlock(IReadOnlyList<WsBoundStatement> Statements, WsSourceSpan Span) : WsBoundStatement(Span);

/// <summary>A function declaration; its closure is made when its block begins, since it is in scope throughout.</summary>
public sealed record WsBoundFunctionDeclaration(WsLocalSymbol Local, WsBoundFunction Function, WsSourceSpan Span)
    : WsBoundStatement(Span);

public sealed record WsBoundReturn(WsBoundExpression? Value, WsSourceSpan Span) : WsBoundStatement(Span);

/// <summary>
/// A class: its constructor, if it declares one, and its methods, each a function whose first parameter is
/// <c>this</c> (specification, section 3.7).
/// </summary>
public sealed record WsBoundClass(
    string Name, WsBoundFunction? Constructor, IReadOnlyList<(string Name, WsBoundFunction Function)> Methods, WsSourceSpan Span)
{
    /// <summary>The class it extends, evaluated when it is made, or null.</summary>
    public WsBoundExpression? Base { get; init; }

    /// <summary>Its static methods, functions without <c>this</c> (section 3.7).</summary>
    public IReadOnlyList<(string Name, WsBoundFunction Function)> StaticMethods { get; init; } = [];

    /// <summary>Its static fields, each with its value, set in their order once its block's classes are made.</summary>
    public IReadOnlyList<(string Name, WsBoundExpression Value, WsSourceSpan Span)> StaticFields { get; init; } = [];
}

/// <summary>A class declaration; the class is made when its block begins, since it is in scope throughout.</summary>
public sealed record WsBoundClassDeclaration(WsLocalSymbol Local, WsBoundClass Class, WsSourceSpan Span) : WsBoundStatement(Span);

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

/// <summary>
/// <c>foreach</c>: the collection's value goes into a hidden local, and the loop's position into the next; the item (or
/// the key, then the value) are the locals after them, so that the four are consecutive (specification, section 3.6).
/// </summary>
public sealed record WsBoundForEach(
    WsLocalSymbol Collection, WsLocalSymbol Position, WsLocalSymbol Item, WsLocalSymbol? Value,
    WsBoundExpression CollectionValue, WsBoundStatement Body, WsSourceSpan Span)
    : WsBoundStatement(Span);

/// <summary><c>using (var name = value) body</c>: the local disposed when the body ends, by any way out (section 3.7).</summary>
public sealed record WsBoundUsing(WsLocalSymbol Local, WsBoundExpression Value, WsBoundStatement Body, WsSourceSpan Span)
    : WsBoundStatement(Span);

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

/// <summary><c>[a, b, c]</c>: a new array (specification, section 3.6).</summary>
public sealed record WsBoundArrayLiteral(IReadOnlyList<WsBoundExpression> Elements, WsSourceSpan Span) : WsBoundExpression(Span);

/// <summary><c>#{a, b, c}</c>: a new set (specification, section 3.6).</summary>
public sealed record WsBoundSetLiteral(IReadOnlyList<WsBoundExpression> Elements, WsSourceSpan Span) : WsBoundExpression(Span);

/// <summary><c>{ key: value, … }</c>: a new map, its keys strings (specification, section 3.6).</summary>
public sealed record WsBoundMapLiteral(IReadOnlyList<(string Key, WsBoundExpression Value)> Entries, WsSourceSpan Span)
    : WsBoundExpression(Span);

/// <summary><c>target[index]</c>, checked when it runs (specification, section 3.6).</summary>
public sealed record WsBoundIndex(WsBoundExpression Target, WsBoundExpression Index, WsSourceSpan Span) : WsBoundExpression(Span)
{
    /// <summary><c>target?[index]</c> (section 3.6).</summary>
    public bool IsOptional { get; init; }
}

/// <summary><c>target[index] = value</c>, whose value is the value assigned.</summary>
public sealed record WsBoundIndexAssignment(
    WsBoundExpression Target, WsBoundExpression Index, WsBoundExpression Value, WsSourceSpan Span)
    : WsBoundExpression(Span);

/// <summary><c>target.name</c>, a member read, checked when it runs.</summary>
public sealed record WsBoundMember(WsBoundExpression Target, string Name, WsSourceSpan Span) : WsBoundExpression(Span)
{
    /// <summary><c>target?.name</c> (section 3.7).</summary>
    public bool IsOptional { get; init; }
}

/// <summary><c>target.name = value</c>, a field assigned, checked when it runs.</summary>
public sealed record WsBoundMemberAssignment(WsBoundExpression Target, string Name, WsBoundExpression Value, WsSourceSpan Span)
    : WsBoundExpression(Span);

/// <summary>
/// The base's constructor (<paramref name="Name"/> <c>constructor</c>) or method, of the class a method belongs to,
/// bound to <c>this</c>: what <c>super(…)</c> and <c>super.name</c> call and read (section 3.7).
/// </summary>
public sealed record WsBoundSuper(WsBoundExpression Class, WsBoundExpression This, string Name, WsSourceSpan Span)
    : WsBoundExpression(Span);

/// <summary><c>new Class(arguments)</c>, checked when it runs.</summary>
public sealed record WsBoundNew(WsBoundExpression Class, IReadOnlyList<WsBoundExpression> Arguments, WsSourceSpan Span)
    : WsBoundExpression(Span);

/// <summary><c>target.name(arguments)</c>, a method called, checked when it runs.</summary>
public sealed record WsBoundMethodCall(
    WsBoundExpression Target, string Name, IReadOnlyList<WsBoundExpression> Arguments, WsSourceSpan Span)
    : WsBoundExpression(Span);

/// <summary>A call of any value, checked to be a function, with the right number of arguments, at run time.</summary>
public sealed record WsBoundCall(WsBoundExpression Callee, IReadOnlyList<WsBoundExpression> Arguments, WsSourceSpan Span)
    : WsBoundExpression(Span);
