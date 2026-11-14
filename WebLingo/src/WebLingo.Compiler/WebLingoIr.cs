using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

// The lowered IR (specification, section 8.4, "Binder/IR"): the bound program reduced to explicit operations. It is
// a tree, neither register- nor stack-shaped, so that either kind of VM can be generated from it (WL-BC002).
// Evaluation order is left to right; an assignment evaluates its value before it stores it (section 15.2). Every
// local has a value from its declaration on (section 3.1), so no operation needs a check that it has one.

/// <summary>
/// A lowered program: the statements of its outermost function, its locals' names (indexed by local, for runtime
/// error messages), the built-ins it calls, and every function it declares, at any depth, which
/// <see cref="WebLingoIrMakeClosure"/> refers to by index.
/// </summary>
public sealed record WebLingoIrProgram(
    IReadOnlyList<WebLingoIrStatement> Statements, IReadOnlyList<string> LocalNames, IReadOnlyList<WebLingoBuiltinSymbol> Builtins)
{
    public IReadOnlyList<WebLingoIrFunction> Functions { get; init; } = [];
    /// <summary>Its files, if it imports others, its own first (section 3.9).</summary>
    public IReadOnlyList<WebLingoIrUnit> Units { get; init; } = [];
    /// <summary>Every class the program declares, at any depth, which <see cref="WebLingoIrMakeClass"/> refers to by index.</summary>
    public IReadOnlyList<WebLingoIrClass> Classes { get; init; } = [];
    /// <summary>The class of <see cref="Classes"/> that is <c>Error</c>, or -1 (section 3.7).</summary>
    public int ErrorClass { get; init; } = -1;
    /// <summary>The class of <see cref="Classes"/> that is <c>Object</c>, or -1 (section 3.7).</summary>
    public int ObjectClass { get; init; } = -1;
    /// <summary>The kinds of values' classes, by <c>WebLingoValueClass</c>, -1 for one the program has not; empty if none.</summary>
    public IReadOnlyList<int> ValueClasses { get; init; } = [];
    /// <summary>The kinds of runtime errors' classes, by their codes.</summary>
    public IReadOnlyList<(string Code, int Class)> ErrorClasses { get; init; } = [];
    /// <summary>Whether its top level awaits, which makes it an <c>async</c> function (section 3.8).</summary>
    public bool IsAsync { get; init; }
}

/// <summary>A class: its constructor's function, or -1 if it has none, and its methods' (section 3.7).</summary>
public sealed record WebLingoIrClass(string Name, int Constructor, IReadOnlyList<(string Name, int Function)> Methods)
{
    /// <summary>Its static methods, functions without <c>this</c>, by name (section 3.7).</summary>
    public IReadOnlyList<(string Name, int Function)> StaticMethods { get; init; } = [];

    /// <summary>The names of its static fields, which exist, as <c>null</c>, from when it is made.</summary>
    public IReadOnlyList<string> StaticFields { get; init; } = [];

    /// <summary>Whether its constructor is the one a class that declares none gets, which only runs its base's.</summary>
    public bool DefaultConstructor { get; init; }
}

/// <summary>Where a closure's capture comes from: a local of the function making it, or one of that one's captures.</summary>
public sealed record WebLingoIrCapture(bool FromLocal, int Index);

/// <summary>A function: its parameters (its first locals), its locals' names, its captures, and its body.</summary>
public sealed record WebLingoIrFunction(
    string Name, int ParameterCount, IReadOnlyList<string> LocalNames, IReadOnlyList<WebLingoIrCapture> Captures,
    IReadOnlyList<WebLingoIrStatement> Body, WebLingoSourceSpan Span)
{
    /// <summary>The file it is in, when it is not the program's own (section 3.9).</summary>
    public string? Source { get; init; }
    /// <summary>A method's or a constructor's, whose first parameter is <c>this</c>.</summary>
    public bool HasThis { get; init; }
    /// <summary>How many of its last parameters have default values, and so may be left out (section 3.5).</summary>
    public int OptionalCount { get; init; }
    /// <summary>Whether it is <c>async</c> (section 3.8).</summary>
    public bool IsAsync { get; init; }
}

/// <summary>
/// A parameter's default value, given it at the function's start when its caller left it out; in its cell if it is
/// captured (section 3.5).
/// </summary>
public sealed record WebLingoIrDefaultParameter(int Local, bool Captured, WebLingoIrExpression Value, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>Gives the local a new cell, without a value until it is initialized.</summary>
public sealed record WebLingoIrNewCell(int Local, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>Moves the local's value into a new cell, which the local then holds: a captured parameter.</summary>
public sealed record WebLingoIrBoxLocal(int Local, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>Gives each local a new cell holding the value of its old one: a <c>for</c>'s per-iteration bindings.</summary>
public sealed record WebLingoIrCopyCells(IReadOnlyList<int> Locals, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>Ends the function, giving the value, or <c>null</c>.</summary>
public sealed record WebLingoIrReturn(WebLingoIrExpression? Value, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

public abstract record WebLingoIrStatement(WebLingoSourceSpan Span);

/// <summary>Evaluates an expression and discards its value.</summary>
public sealed record WebLingoIrEvaluate(WebLingoIrExpression Expression, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary><c>var x = e;</c> or <c>const x = e;</c>: the local gets its first value.</summary>
public sealed record WebLingoIrInitializeLocal(int Local, WebLingoIrExpression Value, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>Statements in order. Scopes are resolved already: a block only groups its statements.</summary>
public sealed record WebLingoIrBlock(IReadOnlyList<WebLingoIrStatement> Statements, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>Runs <paramref name="Then"/> if the condition, which must be a Boolean, is true, else <paramref name="Else"/>.</summary>
public sealed record WebLingoIrIf(WebLingoIrExpression Condition, WebLingoIrStatement Then, WebLingoIrStatement? Else, WebLingoSourceSpan Span)
    : WebLingoIrStatement(Span);

/// <summary>A loop; see <c>WebLingoBoundLoop</c>. A missing condition is true.</summary>
public sealed record WebLingoIrLoop(WebLingoIrExpression? Condition, WebLingoIrStatement Body, WebLingoIrExpression? Step, bool TestFirst, WebLingoSourceSpan Span)
    : WebLingoIrStatement(Span)
{
    /// <summary>Run after the body and before the step: a <c>for</c>'s captured variables, given new cells.</summary>
    public WebLingoIrCopyCells? PerIteration { get; init; }
}

/// <summary>
/// <c>foreach</c>: the collection's value into local <paramref name="FirstLocal"/>, the position, from 0, into the next,
/// and on each iteration the next item (<paramref name="ItemCount"/> 1), or key and value (2), into the locals after
/// them, those that closures capture each given a new cell, then the body; a <c>continue</c> goes on to the next
/// item, a <c>break</c> past the end.
/// </summary>
public sealed record WebLingoIrForEach(
    int FirstLocal, WebLingoIrExpression Collection, int ItemCount, IReadOnlyList<int> CapturedItems, WebLingoIrStatement Body,
    WebLingoSourceSpan Span)
    : WebLingoIrStatement(Span);

/// <summary>
/// <c>using</c>: the local's initialization, then the body; the resource, read by <paramref name="Resource"/>, is disposed
/// on every way out of the body, its end, <c>break</c>, <c>continue</c> and <c>return</c>, unless it is <c>null</c>.
/// </summary>
public sealed record WebLingoIrUsing(
    IReadOnlyList<WebLingoIrStatement> Initialize, WebLingoIrExpression Resource, WebLingoIrStatement Body, int Value, int Origin, WebLingoSourceSpan Span)
    : WebLingoIrStatement(Span);

/// <summary>
/// <c>try</c>: its body, protected by its <c>catch</c>'s handler, and both by its <c>finally</c>'s (section 3.7). The
/// finally's body runs on each way out; on an exception's, its handler keeps the exception in its locals while it runs
/// it, then throws it again.
/// </summary>
public sealed record WebLingoIrTry(WebLingoIrStatement Body, WebLingoIrCatch? Catch, WebLingoIrFinally? Finally, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>A <c>catch</c>: the locals its handler receives the exception and its origin in, and its body, which begins by setting its variable.</summary>
public sealed record WebLingoIrCatch(int Value, int Origin, IReadOnlyList<WebLingoIrStatement> Body);

/// <summary>A <c>finally</c>: its body, and the locals its handler keeps an exception and its origin in.</summary>
public sealed record WebLingoIrFinally(int Value, int Origin, WebLingoIrStatement Body);

/// <summary><c>throw value;</c> (section 3.7).</summary>
public sealed record WebLingoIrThrow(WebLingoIrExpression Value, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>The class <paramref name="Target"/> gives, given the methods and static members of the program's class
/// <paramref name="Class"/>, an extension's (section 3.7).</summary>
public sealed record WebLingoIrExtend(WebLingoIrExpression Target, int Class, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>Throws the exception in local <paramref name="Value"/> again, from where local <paramref name="Origin"/> says.</summary>
public sealed record WebLingoIrRethrow(int Value, int Origin, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>Leaves the innermost loop.</summary>
public sealed record WebLingoIrBreak(WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>Goes on to the innermost loop's step, then its condition.</summary>
public sealed record WebLingoIrContinue(WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

public abstract record WebLingoIrExpression(WebLingoSourceSpan Span);

public sealed record WebLingoIrInt64Constant(long Value, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

public sealed record WebLingoIrBinary64Constant(double Value, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

public sealed record WebLingoIrBooleanConstant(bool Value, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

public sealed record WebLingoIrNullConstant(WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

public sealed record WebLingoIrStringConstant(string Value, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>
/// <c>&amp;&amp;</c> (<paramref name="IsAnd"/>) or <c>||</c>: the left operand, which must be a Boolean, and only if it
/// does not decide the result, the right one, which must be a Boolean too.
/// </summary>
public sealed record WebLingoIrLogical(bool IsAnd, WebLingoIrExpression Left, WebLingoIrExpression Right, WebLingoSourceSpan Span)
    : WebLingoIrExpression(Span);

/// <summary>Reads a local.</summary>
public sealed record WebLingoIrLoadLocal(int Local, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>Assigns a local and yields the value.</summary>
public sealed record WebLingoIrStoreLocal(int Local, WebLingoIrExpression Value, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

public sealed record WebLingoIrUnary(WebLingoUnaryOperator Operator, WebLingoIrExpression Operand, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary><c>await value</c>: suspends the <c>async</c> function until the value, or a task's result, is there (section 3.8).</summary>
public sealed record WebLingoIrAwait(WebLingoIrExpression Value, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary><c>value is Class</c>: the value, then the class, evaluated (section 3.7).</summary>
public sealed record WebLingoIrIs(WebLingoIrExpression Value, WebLingoIrExpression Class, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>The text of a value, an instance as its fields, as <c>Object</c>'s <c>toString()</c> gives it (section 3.7).</summary>
public sealed record WebLingoIrToText(WebLingoIrExpression Value, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>An instance holding a value, of its class's kind; gives null (section 3.7).</summary>
public sealed record WebLingoIrHold(WebLingoIrExpression Instance, WebLingoIrExpression Value, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

public sealed record WebLingoIrBinary(WebLingoBinaryOperator Operator, WebLingoIrExpression Left, WebLingoIrExpression Right, WebLingoSourceSpan Span)
    : WebLingoIrExpression(Span);

/// <summary>Reads a captured local of this function, through the cell its frame holds.</summary>
public sealed record WebLingoIrLoadCell(int Local, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>Stores into a captured local's cell; its value is the value stored.</summary>
public sealed record WebLingoIrStoreCell(int Local, WebLingoIrExpression Value, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>Reads a local of a function around this one, through this function's capture of it.</summary>
public sealed record WebLingoIrLoadCapture(int Capture, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

public sealed record WebLingoIrStoreCapture(int Capture, WebLingoIrExpression Value, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>A new closure of the program's function <paramref name="Function"/>.</summary>
public sealed record WebLingoIrMakeClosure(int Function, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>The program's file <paramref name="Unit"/>'s instance, or the task of running it (section 3.9).</summary>
public sealed record WebLingoIrImport(int Unit, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>The program's file <paramref name="Unit"/>'s instance, its exports the cells of those locals (section 3.9).</summary>
public sealed record WebLingoIrMakeModule(int Unit, IReadOnlyList<int> Locals, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>A file the program imports given the closure of its function, which runs it (section 3.9).</summary>
public sealed record WebLingoIrRegisterUnit(int Unit, WebLingoIrExpression Closure, WebLingoSourceSpan Span) : WebLingoIrStatement(Span);

/// <summary>A file of the program: its name, its function (-1 for the program's own), and its exports' names.</summary>
public sealed record WebLingoIrUnit(string Name, int Function, IReadOnlyList<string> Exports);

public sealed record WebLingoIrLoadBuiltin(int Builtin, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>A new array of the elements' values, evaluated in order (specification, section 3.6).</summary>
public sealed record WebLingoIrNewArray(IReadOnlyList<WebLingoIrExpression> Elements, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>A new set of the elements' values, evaluated in order, an equal value given twice there once (section 3.6).</summary>
public sealed record WebLingoIrNewSet(IReadOnlyList<WebLingoIrExpression> Elements, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary>One entry of a new map: its key, a string, and its value.</summary>
public sealed record WebLingoIrMapEntry(string Key, WebLingoIrExpression Value);

/// <summary>
/// A new map of the entries, their values evaluated in order, a later key replacing an earlier equal one
/// (specification, section 3.6).
/// </summary>
public sealed record WebLingoIrNewMap(IReadOnlyList<WebLingoIrMapEntry> Entries, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary><c>target[index]</c> (specification, section 3.6).</summary>
public sealed record WebLingoIrIndex(WebLingoIrExpression Target, WebLingoIrExpression Index, WebLingoSourceSpan Span) : WebLingoIrExpression(Span)
{
    /// <summary>
    /// <c>target?[index]</c>: <c>null</c> where <c>target[index]</c> would raise a missing key or an index out of range,
    /// or the target is <c>null</c>; the index is evaluated either way.
    /// </summary>
    public bool IsOptional { get; init; }
}

/// <summary><c>target[index] = value</c>: the target, the index, then the value, evaluated; its value is the value.</summary>
public sealed record WebLingoIrStoreIndex(WebLingoIrExpression Target, WebLingoIrExpression Index, WebLingoIrExpression Value, WebLingoSourceSpan Span)
    : WebLingoIrExpression(Span);

/// <summary><c>target.name</c>, a member read.</summary>
public sealed record WebLingoIrMember(WebLingoIrExpression Target, string Name, WebLingoSourceSpan Span) : WebLingoIrExpression(Span)
{
    /// <summary><c>target?.name</c>: <c>null</c> for a <c>null</c> target or a missing member (section 3.7).</summary>
    public bool IsOptional { get; init; }
}

/// <summary><c>target.name = value</c>: the target, then the value, evaluated; its value is the value.</summary>
public sealed record WebLingoIrStoreMember(WebLingoIrExpression Target, string Name, WebLingoIrExpression Value, WebLingoSourceSpan Span)
    : WebLingoIrExpression(Span);

/// <summary>A new class value of the program's class <paramref name="Class"/>, its methods closures made now.</summary>
public sealed record WebLingoIrMakeClass(int Class, WebLingoSourceSpan Span) : WebLingoIrExpression(Span)
{
    /// <summary>The class it extends, evaluated now, or null.</summary>
    public WebLingoIrExpression? Base { get; init; }
}

/// <summary>The base's constructor or method, of the class value given, bound to <c>this</c> (section 3.7).</summary>
public sealed record WebLingoIrGetSuper(WebLingoIrExpression Class, WebLingoIrExpression This, string Name, WebLingoSourceSpan Span) : WebLingoIrExpression(Span);

/// <summary><c>new Class(arguments)</c>: the class, then the arguments, evaluated; an instance, its constructor run.</summary>
public sealed record WebLingoIrNew(WebLingoIrExpression Class, IReadOnlyList<WebLingoIrExpression> Arguments, WebLingoSourceSpan Span)
    : WebLingoIrExpression(Span);

/// <summary><c>target.name(arguments)</c>: the target, then the arguments, evaluated, and the method called.</summary>
public sealed record WebLingoIrCallMethod(WebLingoIrExpression Target, string Name, IReadOnlyList<WebLingoIrExpression> Arguments, WebLingoSourceSpan Span)
    : WebLingoIrExpression(Span);

/// <summary>Calls the callee's value, which must be a function taking as many arguments as are given.</summary>
public sealed record WebLingoIrCall(WebLingoIrExpression Callee, IReadOnlyList<WebLingoIrExpression> Arguments, WebLingoSourceSpan Span)
    : WebLingoIrExpression(Span);

public sealed record WebLingoIrCallBuiltin(int Builtin, IReadOnlyList<WebLingoIrExpression> Arguments, WebLingoSourceSpan Span)
    : WebLingoIrExpression(Span);
