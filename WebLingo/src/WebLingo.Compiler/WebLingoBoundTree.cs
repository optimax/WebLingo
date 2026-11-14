using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

// The bound tree: the syntax tree with every name resolved to its symbol. Parentheses are gone; spans remain, for
// the diagnostics of later stages.

/// <summary>A bound program: its statements, its locals in declaration order, and the built-ins it calls.</summary>
public sealed record WebLingoBoundProgram(
    IReadOnlyList<WebLingoBoundStatement> Statements, IReadOnlyList<WebLingoLocalSymbol> Locals, IReadOnlyList<WebLingoBuiltinSymbol> Builtins)
{
    /// <summary>The built-in class <c>Error</c>, among the program's classes if it uses it, or null (section 3.7).</summary>
    public WebLingoBoundClass? ErrorClass { get; init; }
    /// <summary>The built-in class <c>Object</c>, among the program's classes if it uses it, or null (section 3.7).</summary>
    public WebLingoBoundClass? ObjectClass { get; init; }
    /// <summary>Every built-in class the program uses, by its name, <c>Object</c> and <c>Error</c> included.</summary>
    public IReadOnlyList<(string Name, WebLingoBoundClass Class)> BuiltinClasses { get; init; } = [];
    /// <summary>Whether its top level awaits, which makes it an <c>async</c> function itself (section 3.8).</summary>
    public bool IsAsync { get; init; }
    /// <summary>Its files, if it imports others, its own first (section 3.9); empty if it imports none.</summary>
    public IReadOnlyList<WebLingoBoundUnit> Units { get; init; } = [];
}

/// <summary>
/// A file of a program (section 3.9): its name, its top level as a function (null for the program's own), the locals it
/// exports, by name, and the local holding its instance.
/// </summary>
public sealed record WebLingoBoundUnit(string Name, WebLingoBoundFunction? Function,
    IReadOnlyList<(string Name, WebLingoLocalSymbol Local)> Exports, WebLingoLocalSymbol Instance)
{
    /// <summary>For a standard module, <c>std:system</c>: the built-ins it exports, and no function (section 3.9).</summary>
    public IReadOnlyList<WebLingoBuiltinSymbol>? Builtins { get; init; }

    /// <summary>Its parameters (section 3.9), whose values its function takes as one map.</summary>
    public IReadOnlyList<WebLingoModuleParameter> Parameters { get; init; } = [];
}

/// <summary>
/// A module's parameter, a constant of it: the value of its name in the map its function is given, or its default
/// (section 3.9).
/// </summary>
public sealed record WebLingoBoundModuleParameter(WebLingoLocalSymbol Local, WebLingoLocalSymbol Values, string Name,
    WebLingoBoundExpression? Default, WebLingoSourceSpan Span) : WebLingoBoundStatement(Span);

/// <summary>An import: the program's file <paramref name="Unit"/>'s instance, run if it has not been, into a local (section 3.9).</summary>
public sealed record WebLingoBoundImport(int Unit, WebLingoLocalSymbol Local, WebLingoSourceSpan Span) : WebLingoBoundStatement(Span)
{
    /// <summary>The values it gives the module's parameters, a map, for a module that has some (section 3.9).</summary>
    public WebLingoBoundExpression? Values { get; init; }
}

/// <summary>
/// Where a function's capture comes from when its closure is made: a local of the function that makes it
/// (<paramref name="FromLocal"/>), or one of that function's own captures.
/// </summary>
public sealed record WebLingoBoundCapture(bool FromLocal, int Index, WebLingoLocalSymbol Local);

/// <summary>
/// A function: its parameters (its first locals), all its locals, what it captures from the functions around it,
/// and its body.
/// </summary>
public sealed record WebLingoBoundFunction(
    string Name, int ParameterCount, IReadOnlyList<WebLingoLocalSymbol> Locals, IReadOnlyList<WebLingoBoundCapture> Captures,
    IReadOnlyList<WebLingoBoundStatement> Body, WebLingoSourceSpan Span)
{
    /// <summary>A method's or a constructor's: its first parameter is <c>this</c>, which its caller gives (section 3.7).</summary>
    public bool HasThis { get; init; }
    /// <summary>Its parameters' default values, but <c>this</c>'s, null for one that has none (section 3.5).</summary>
    public IReadOnlyList<WebLingoBoundExpression?> Defaults { get; init; } = [];
    /// <summary>Whether it is <c>async</c>: its call gives a task, and it may <c>await</c> (section 3.8).</summary>
    public bool IsAsync { get; init; }
    /// <summary>The file it is in, when it is not the program's own (section 3.9).</summary>
    public string? Source { get; init; }
}

public abstract record WebLingoBoundStatement(WebLingoSourceSpan Span);

public sealed record WebLingoBoundVariableDeclaration(WebLingoLocalSymbol Local, WebLingoBoundExpression Initializer, WebLingoSourceSpan Span)
    : WebLingoBoundStatement(Span);

public sealed record WebLingoBoundExpressionStatement(WebLingoBoundExpression Expression, WebLingoSourceSpan Span) : WebLingoBoundStatement(Span);

public sealed record WebLingoBoundBlock(IReadOnlyList<WebLingoBoundStatement> Statements, WebLingoSourceSpan Span) : WebLingoBoundStatement(Span);

/// <summary>A function declaration; its closure is made when its block begins, since it is in scope throughout.</summary>
public sealed record WebLingoBoundFunctionDeclaration(WebLingoLocalSymbol Local, WebLingoBoundFunction Function, WebLingoSourceSpan Span)
    : WebLingoBoundStatement(Span);

public sealed record WebLingoBoundReturn(WebLingoBoundExpression? Value, WebLingoSourceSpan Span) : WebLingoBoundStatement(Span);

/// <summary>
/// A class: its constructor, if it declares one, and its methods, each a function whose first parameter is
/// <c>this</c> (specification, section 3.7).
/// </summary>
public sealed record WebLingoBoundClass(
    string Name, WebLingoBoundFunction? Constructor, IReadOnlyList<(string Name, WebLingoBoundFunction Function)> Methods, WebLingoSourceSpan Span)
{
    /// <summary>The class it extends, evaluated when it is made, or null.</summary>
    public WebLingoBoundExpression? Base { get; init; }

    /// <summary>Whether its constructor is the one a class that declares none gets, which only runs its base's.</summary>
    public bool DefaultConstructor { get; init; }

    /// <summary>Its static methods, functions without <c>this</c> (section 3.7).</summary>
    public IReadOnlyList<(string Name, WebLingoBoundFunction Function)> StaticMethods { get; init; } = [];

    /// <summary>Its static fields, each with its value, set in their order once its block's classes are made.</summary>
    public IReadOnlyList<(string Name, WebLingoBoundExpression Value, WebLingoSourceSpan Span)> StaticFields { get; init; } = [];
}

/// <summary>A class declaration; the class is made when its block begins, since it is in scope throughout.</summary>
public sealed record WebLingoBoundClassDeclaration(WebLingoLocalSymbol Local, WebLingoBoundClass Class, WebLingoSourceSpan Span) : WebLingoBoundStatement(Span);

/// <summary>
/// <c>extend Target { … }</c>: the class <paramref name="Target"/> gives, evaluated where its block begins, given
/// <paramref name="Class"/>'s methods and static members, which has no constructor (section 3.7).
/// </summary>
public sealed record WebLingoBoundExtension(WebLingoBoundExpression Target, WebLingoBoundClass Class, WebLingoSourceSpan Span) : WebLingoBoundStatement(Span);

public sealed record WebLingoBoundIf(WebLingoBoundExpression Condition, WebLingoBoundStatement Then, WebLingoBoundStatement? Else, WebLingoSourceSpan Span)
    : WebLingoBoundStatement(Span);

/// <summary>
/// A loop: <c>while</c> and <c>for</c> test first (<paramref name="TestFirst"/>), <c>do … while</c> last; a missing
/// condition is true. <paramref name="Step"/> runs after the body and after each <c>continue</c>.
/// </summary>
public sealed record WebLingoBoundLoop(
    WebLingoBoundExpression? Condition, WebLingoBoundStatement Body, WebLingoBoundExpression? Step, bool TestFirst, WebLingoSourceSpan Span)
    : WebLingoBoundStatement(Span)
{
    /// <summary>A <c>for</c>'s variables, each of which gets a binding of its own for every iteration.</summary>
    public IReadOnlyList<WebLingoLocalSymbol> PerIterationLocals { get; init; } = [];
}

/// <summary>
/// <c>foreach</c>: the collection's value goes into a hidden local, and the loop's position into the next; the item (or
/// the key, then the value) are the locals after them, so that the four are consecutive (specification, section 3.6).
/// </summary>
public sealed record WebLingoBoundForEach(
    WebLingoLocalSymbol Collection, WebLingoLocalSymbol Position, WebLingoLocalSymbol Item, WebLingoLocalSymbol? Value,
    WebLingoBoundExpression CollectionValue, WebLingoBoundStatement Body, WebLingoSourceSpan Span)
    : WebLingoBoundStatement(Span);

/// <summary><c>using (var name = value) body</c>: the local disposed when the body ends, by any way out (section 3.7).</summary>
public sealed record WebLingoBoundUsing(WebLingoLocalSymbol Local, WebLingoBoundExpression Value, WebLingoBoundStatement Body, WebLingoSourceSpan Span)
    : WebLingoBoundStatement(Span)
{
    /// <summary>The hidden locals its handler keeps an exception in, and where it was thrown, while it disposes.</summary>
    public required WebLingoLocalSymbol ExceptionValue { get; init; }
    public required WebLingoLocalSymbol ExceptionOrigin { get; init; }
}

/// <summary>
/// A <c>catch</c>: the class it catches, evaluated when an exception reaches it, or null for any exception; its
/// variable; and its body. Its <c>try</c>'s hidden locals receive the exception and where it was thrown (section 3.7).
/// </summary>
public sealed record WebLingoBoundCatch(WebLingoBoundExpression? Class, WebLingoLocalSymbol Variable, WebLingoBoundStatement Body);

/// <summary>A <c>finally</c>: its body, and the hidden locals its handler keeps an exception in while it runs it.</summary>
public sealed record WebLingoBoundFinally(WebLingoLocalSymbol Value, WebLingoLocalSymbol Origin, WebLingoBoundStatement Body);

/// <summary><c>try</c>, with a <c>catch</c>, a <c>finally</c>, or both (section 3.7).</summary>
public sealed record WebLingoBoundTry(
    WebLingoBoundStatement Body, IReadOnlyList<WebLingoBoundCatch> Catches, WebLingoLocalSymbol? CatchValue, WebLingoLocalSymbol? CatchOrigin,
    WebLingoBoundFinally? Finally, WebLingoSourceSpan Span)
    : WebLingoBoundStatement(Span);

/// <summary><c>throw value;</c> (section 3.7).</summary>
public sealed record WebLingoBoundThrow(WebLingoBoundExpression Value, WebLingoSourceSpan Span) : WebLingoBoundStatement(Span);

/// <summary><c>throw;</c>: the exception of the <c>catch</c> it is in, thrown again from where it was first thrown.</summary>
public sealed record WebLingoBoundRethrow(WebLingoLocalSymbol Value, WebLingoLocalSymbol Origin, WebLingoSourceSpan Span) : WebLingoBoundStatement(Span);

public sealed record WebLingoBoundBreak(WebLingoSourceSpan Span) : WebLingoBoundStatement(Span);

public sealed record WebLingoBoundContinue(WebLingoSourceSpan Span) : WebLingoBoundStatement(Span);

public abstract record WebLingoBoundExpression(WebLingoSourceSpan Span);

/// <summary>
/// A static constant's value (<c>static const name = value;</c>, section 3.7), which only a class's or an extension's
/// static fields hold: set as the class's built-in <c>setConstant</c> sets one, so that it cannot be assigned after.
/// </summary>
public sealed record WebLingoBoundStaticConstant(WebLingoBoundExpression Value, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundIntegerLiteral(long Value, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundBinary64Literal(double Value, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundBooleanLiteral(bool Value, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundNullLiteral(WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundStringLiteral(string Value, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundLocalReference(WebLingoLocalSymbol Local, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

/// <summary>
/// The text of a value, as <c>write</c> writes it, an instance as its class's name and its fields: what <c>Object</c>'s
/// <c>toString()</c> gives; nothing but <c>Object</c> writes it (section 3.7).
/// </summary>
/// <summary><c>await value</c> (section 3.8).</summary>
public sealed record WebLingoBoundAwait(WebLingoBoundExpression Value, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

/// <summary><c>value is Class</c> (section 3.7).</summary>
public sealed record WebLingoBoundIs(WebLingoBoundExpression Value, WebLingoBoundExpression Class, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundToText(WebLingoBoundExpression Value, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

/// <summary>The built-in classes' <c>hold(this, value)</c>: the instance holds the value, of its class's kind (section 3.7).</summary>
public sealed record WebLingoBoundHold(WebLingoBoundExpression Instance, WebLingoBoundExpression Value, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundUnary(WebLingoUnaryOperator Operator, WebLingoBoundExpression Operand, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundBinary(
    WebLingoBinaryOperator Operator, WebLingoBoundExpression Left, WebLingoBoundExpression Right, WebLingoSourceSpan OperatorSpan, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundAssignment(WebLingoLocalSymbol Local, WebLingoBoundExpression Value, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundBuiltinCall(WebLingoBuiltinSymbol Builtin, IReadOnlyList<WebLingoBoundExpression> Arguments, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

/// <summary>Where binding failed; its diagnostic is already reported, and nothing is lowered from it.</summary>
public sealed record WebLingoBoundError(WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

/// <summary>A local of a function around this one, through this function's capture <paramref name="Index"/>.</summary>
public sealed record WebLingoBoundCaptureReference(int Index, WebLingoLocalSymbol Local, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundCaptureAssignment(int Index, WebLingoLocalSymbol Local, WebLingoBoundExpression Value, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

/// <summary>A built-in used as a value.</summary>
public sealed record WebLingoBoundBuiltinReference(WebLingoBuiltinSymbol Builtin, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

public sealed record WebLingoBoundFunctionExpression(WebLingoBoundFunction Function, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

/// <summary><c>[a, b, c]</c>: a new array (specification, section 3.6).</summary>
public sealed record WebLingoBoundArrayLiteral(IReadOnlyList<WebLingoBoundExpression> Elements, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

/// <summary><c>#{a, b, c}</c>: a new set (specification, section 3.6).</summary>
public sealed record WebLingoBoundSetLiteral(IReadOnlyList<WebLingoBoundExpression> Elements, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span);

/// <summary><c>{ key: value, … }</c>: a new map, its keys strings (specification, section 3.6).</summary>
public sealed record WebLingoBoundMapLiteral(IReadOnlyList<(string Key, WebLingoBoundExpression Value)> Entries, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

/// <summary><c>target[index]</c>, checked when it runs (specification, section 3.6).</summary>
public sealed record WebLingoBoundIndex(WebLingoBoundExpression Target, WebLingoBoundExpression Index, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span)
{
    /// <summary><c>target?[index]</c> (section 3.6).</summary>
    public bool IsOptional { get; init; }
}

/// <summary><c>target[index] = value</c>, whose value is the value assigned.</summary>
public sealed record WebLingoBoundIndexAssignment(
    WebLingoBoundExpression Target, WebLingoBoundExpression Index, WebLingoBoundExpression Value, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

/// <summary><c>target.name</c>, a member read, checked when it runs.</summary>
public sealed record WebLingoBoundMember(WebLingoBoundExpression Target, string Name, WebLingoSourceSpan Span) : WebLingoBoundExpression(Span)
{
    /// <summary><c>target?.name</c> (section 3.7).</summary>
    public bool IsOptional { get; init; }
}

/// <summary><c>target.name = value</c>, a field assigned, checked when it runs.</summary>
public sealed record WebLingoBoundMemberAssignment(WebLingoBoundExpression Target, string Name, WebLingoBoundExpression Value, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

/// <summary>
/// The base's constructor (<paramref name="Name"/> <c>constructor</c>) or method, of the class a method belongs to,
/// bound to <c>this</c>: what <c>super(…)</c> and <c>super.name</c> call and read (section 3.7).
/// </summary>
public sealed record WebLingoBoundSuper(WebLingoBoundExpression Class, WebLingoBoundExpression This, string Name, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

/// <summary><c>new Class(arguments)</c>, checked when it runs.</summary>
public sealed record WebLingoBoundNew(WebLingoBoundExpression Class, IReadOnlyList<WebLingoBoundExpression> Arguments, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

/// <summary><c>target.name(arguments)</c>, a method called, checked when it runs.</summary>
public sealed record WebLingoBoundMethodCall(
    WebLingoBoundExpression Target, string Name, IReadOnlyList<WebLingoBoundExpression> Arguments, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);

/// <summary>A call of any value, checked to be a function, with the right number of arguments, at run time.</summary>
public sealed record WebLingoBoundCall(WebLingoBoundExpression Callee, IReadOnlyList<WebLingoBoundExpression> Arguments, WebLingoSourceSpan Span)
    : WebLingoBoundExpression(Span);
