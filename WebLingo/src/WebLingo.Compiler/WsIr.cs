using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

// The lowered IR (specification, section 8.4, "Binder/IR"): the bound program reduced to explicit operations. It is
// a tree, neither register- nor stack-shaped, so that either kind of VM can be generated from it (WS-BC002).
// Evaluation order is left to right; an assignment evaluates its value before it stores it (section 15.2). Every
// local has a value from its declaration on (section 3.1), so no operation needs a check that it has one.

/// <summary>
/// A lowered program: the statements of its outermost function, its locals' names (indexed by local, for runtime
/// error messages), the built-ins it calls, and every function it declares, at any depth, which
/// <see cref="WsIrMakeClosure"/> refers to by index.
/// </summary>
public sealed record WsIrProgram(
    IReadOnlyList<WsIrStatement> Statements, IReadOnlyList<string> LocalNames, IReadOnlyList<WsBuiltinSymbol> Builtins)
{
    public IReadOnlyList<WsIrFunction> Functions { get; init; } = [];
    /// <summary>Every class the program declares, at any depth, which <see cref="WsIrMakeClass"/> refers to by index.</summary>
    public IReadOnlyList<WsIrClass> Classes { get; init; } = [];
    /// <summary>The class of <see cref="Classes"/> that is <c>Error</c>, or -1 (section 3.7).</summary>
    public int ErrorClass { get; init; } = -1;
    /// <summary>The class of <see cref="Classes"/> that is <c>Object</c>, or -1 (section 3.7).</summary>
    public int ObjectClass { get; init; } = -1;
    /// <summary>The kinds of values' classes, by <c>WsValueClass</c>, -1 for one the program has not; empty if none.</summary>
    public IReadOnlyList<int> ValueClasses { get; init; } = [];
    /// <summary>The kinds of runtime errors' classes, by their codes.</summary>
    public IReadOnlyList<(string Code, int Class)> ErrorClasses { get; init; } = [];
}

/// <summary>A class: its constructor's function, or -1 if it has none, and its methods' (section 3.7).</summary>
public sealed record WsIrClass(string Name, int Constructor, IReadOnlyList<(string Name, int Function)> Methods)
{
    /// <summary>Its static methods, functions without <c>this</c>, by name (section 3.7).</summary>
    public IReadOnlyList<(string Name, int Function)> StaticMethods { get; init; } = [];

    /// <summary>The names of its static fields, which exist, as <c>null</c>, from when it is made.</summary>
    public IReadOnlyList<string> StaticFields { get; init; } = [];
}

/// <summary>Where a closure's capture comes from: a local of the function making it, or one of that one's captures.</summary>
public sealed record WsIrCapture(bool FromLocal, int Index);

/// <summary>A function: its parameters (its first locals), its locals' names, its captures, and its body.</summary>
public sealed record WsIrFunction(
    string Name, int ParameterCount, IReadOnlyList<string> LocalNames, IReadOnlyList<WsIrCapture> Captures,
    IReadOnlyList<WsIrStatement> Body, WsSourceSpan Span)
{
    /// <summary>A method's or a constructor's, whose first parameter is <c>this</c>.</summary>
    public bool HasThis { get; init; }
}

/// <summary>Gives the local a new cell, without a value until it is initialized.</summary>
public sealed record WsIrNewCell(int Local, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary>Moves the local's value into a new cell, which the local then holds: a captured parameter.</summary>
public sealed record WsIrBoxLocal(int Local, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary>Gives each local a new cell holding the value of its old one: a <c>for</c>'s per-iteration bindings.</summary>
public sealed record WsIrCopyCells(IReadOnlyList<int> Locals, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary>Ends the function, giving the value, or <c>null</c>.</summary>
public sealed record WsIrReturn(WsIrExpression? Value, WsSourceSpan Span) : WsIrStatement(Span);

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
    : WsIrStatement(Span)
{
    /// <summary>Run after the body and before the step: a <c>for</c>'s captured variables, given new cells.</summary>
    public WsIrCopyCells? PerIteration { get; init; }
}

/// <summary>
/// <c>foreach</c>: the collection's value into local <paramref name="FirstLocal"/>, the position, from 0, into the next,
/// and on each iteration the next item (<paramref name="ItemCount"/> 1), or key and value (2), into the locals after
/// them, those that closures capture each given a new cell, then the body; a <c>continue</c> goes on to the next
/// item, a <c>break</c> past the end.
/// </summary>
public sealed record WsIrForEach(
    int FirstLocal, WsIrExpression Collection, int ItemCount, IReadOnlyList<int> CapturedItems, WsIrStatement Body,
    WsSourceSpan Span)
    : WsIrStatement(Span);

/// <summary>
/// <c>using</c>: the local's initialization, then the body; the resource, read by <paramref name="Resource"/>, is disposed
/// on every way out of the body, its end, <c>break</c>, <c>continue</c> and <c>return</c>, unless it is <c>null</c>.
/// </summary>
public sealed record WsIrUsing(
    IReadOnlyList<WsIrStatement> Initialize, WsIrExpression Resource, WsIrStatement Body, int Value, int Origin, WsSourceSpan Span)
    : WsIrStatement(Span);

/// <summary>
/// <c>try</c>: its body, protected by its <c>catch</c>'s handler, and both by its <c>finally</c>'s (section 3.7). The
/// finally's body runs on each way out; on an exception's, its handler keeps the exception in its locals while it runs
/// it, then throws it again.
/// </summary>
public sealed record WsIrTry(WsIrStatement Body, WsIrCatch? Catch, WsIrFinally? Finally, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary>A <c>catch</c>: the locals its handler receives the exception and its origin in, and its body, which begins by setting its variable.</summary>
public sealed record WsIrCatch(int Value, int Origin, IReadOnlyList<WsIrStatement> Body);

/// <summary>A <c>finally</c>: its body, and the locals its handler keeps an exception and its origin in.</summary>
public sealed record WsIrFinally(int Value, int Origin, WsIrStatement Body);

/// <summary><c>throw value;</c> (section 3.7).</summary>
public sealed record WsIrThrow(WsIrExpression Value, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary>Throws the exception in local <paramref name="Value"/> again, from where local <paramref name="Origin"/> says.</summary>
public sealed record WsIrRethrow(int Value, int Origin, WsSourceSpan Span) : WsIrStatement(Span);

/// <summary>Leaves the innermost loop.</summary>
public sealed record WsIrBreak(WsSourceSpan Span) : WsIrStatement(Span);

/// <summary>Goes on to the innermost loop's step, then its condition.</summary>
public sealed record WsIrContinue(WsSourceSpan Span) : WsIrStatement(Span);

public abstract record WsIrExpression(WsSourceSpan Span);

public sealed record WsIrInt64Constant(long Value, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrBinary64Constant(double Value, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrBooleanConstant(bool Value, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrNullConstant(WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrStringConstant(string Value, WsSourceSpan Span) : WsIrExpression(Span);

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

/// <summary><c>value is Class</c>: the value, then the class, evaluated (section 3.7).</summary>
public sealed record WsIrIs(WsIrExpression Value, WsIrExpression Class, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>The text of a value, an instance as its fields, as <c>Object</c>'s <c>toString()</c> gives it (section 3.7).</summary>
public sealed record WsIrToText(WsIrExpression Value, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrBinary(WsBinaryOperator Operator, WsIrExpression Left, WsIrExpression Right, WsSourceSpan Span)
    : WsIrExpression(Span);

/// <summary>Reads a captured local of this function, through the cell its frame holds.</summary>
public sealed record WsIrLoadCell(int Local, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>Stores into a captured local's cell; its value is the value stored.</summary>
public sealed record WsIrStoreCell(int Local, WsIrExpression Value, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>Reads a local of a function around this one, through this function's capture of it.</summary>
public sealed record WsIrLoadCapture(int Capture, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrStoreCapture(int Capture, WsIrExpression Value, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>A new closure of the program's function <paramref name="Function"/>.</summary>
public sealed record WsIrMakeClosure(int Function, WsSourceSpan Span) : WsIrExpression(Span);

public sealed record WsIrLoadBuiltin(int Builtin, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>A new array of the elements' values, evaluated in order (specification, section 3.6).</summary>
public sealed record WsIrNewArray(IReadOnlyList<WsIrExpression> Elements, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>A new set of the elements' values, evaluated in order, an equal value given twice there once (section 3.6).</summary>
public sealed record WsIrNewSet(IReadOnlyList<WsIrExpression> Elements, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary>One entry of a new map: its key, a string, and its value.</summary>
public sealed record WsIrMapEntry(string Key, WsIrExpression Value);

/// <summary>
/// A new map of the entries, their values evaluated in order, a later key replacing an earlier equal one
/// (specification, section 3.6).
/// </summary>
public sealed record WsIrNewMap(IReadOnlyList<WsIrMapEntry> Entries, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary><c>target[index]</c> (specification, section 3.6).</summary>
public sealed record WsIrIndex(WsIrExpression Target, WsIrExpression Index, WsSourceSpan Span) : WsIrExpression(Span)
{
    /// <summary>
    /// <c>target?[index]</c>: <c>null</c> where <c>target[index]</c> would raise a missing key or an index out of range,
    /// or the target is <c>null</c>; the index is evaluated either way.
    /// </summary>
    public bool IsOptional { get; init; }
}

/// <summary><c>target[index] = value</c>: the target, the index, then the value, evaluated; its value is the value.</summary>
public sealed record WsIrStoreIndex(WsIrExpression Target, WsIrExpression Index, WsIrExpression Value, WsSourceSpan Span)
    : WsIrExpression(Span);

/// <summary><c>target.name</c>, a member read.</summary>
public sealed record WsIrMember(WsIrExpression Target, string Name, WsSourceSpan Span) : WsIrExpression(Span)
{
    /// <summary><c>target?.name</c>: <c>null</c> for a <c>null</c> target or a missing member (section 3.7).</summary>
    public bool IsOptional { get; init; }
}

/// <summary><c>target.name = value</c>: the target, then the value, evaluated; its value is the value.</summary>
public sealed record WsIrStoreMember(WsIrExpression Target, string Name, WsIrExpression Value, WsSourceSpan Span)
    : WsIrExpression(Span);

/// <summary>A new class value of the program's class <paramref name="Class"/>, its methods closures made now.</summary>
public sealed record WsIrMakeClass(int Class, WsSourceSpan Span) : WsIrExpression(Span)
{
    /// <summary>The class it extends, evaluated now, or null.</summary>
    public WsIrExpression? Base { get; init; }
}

/// <summary>The base's constructor or method, of the class value given, bound to <c>this</c> (section 3.7).</summary>
public sealed record WsIrGetSuper(WsIrExpression Class, WsIrExpression This, string Name, WsSourceSpan Span) : WsIrExpression(Span);

/// <summary><c>new Class(arguments)</c>: the class, then the arguments, evaluated; an instance, its constructor run.</summary>
public sealed record WsIrNew(WsIrExpression Class, IReadOnlyList<WsIrExpression> Arguments, WsSourceSpan Span)
    : WsIrExpression(Span);

/// <summary><c>target.name(arguments)</c>: the target, then the arguments, evaluated, and the method called.</summary>
public sealed record WsIrCallMethod(WsIrExpression Target, string Name, IReadOnlyList<WsIrExpression> Arguments, WsSourceSpan Span)
    : WsIrExpression(Span);

/// <summary>Calls the callee's value, which must be a function taking as many arguments as are given.</summary>
public sealed record WsIrCall(WsIrExpression Callee, IReadOnlyList<WsIrExpression> Arguments, WsSourceSpan Span)
    : WsIrExpression(Span);

public sealed record WsIrCallBuiltin(int Builtin, IReadOnlyList<WsIrExpression> Arguments, WsSourceSpan Span)
    : WsIrExpression(Span);
