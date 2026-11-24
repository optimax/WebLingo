namespace WebLingo.VM;

/// <summary>
/// A breakpoint's condition (specification, section 5.7, "Debugging WASP pages, parts", part 3), as the VM evaluates it in
/// the scope of the frame it stops in, without running the program's code: names, literals, members, indexing, operators,
/// and the built-in classes' methods that only read, of strings, arrays, maps and sets (<see cref="ReadsOnly"/>). A
/// debugger makes it from a condition's syntax, refusing what it cannot hold.
/// </summary>
public abstract record WebLingoDebugExpression
{
    /// <summary>A literal: <c>null</c>, a Boolean, an integer (<see cref="long"/>), a number (<see cref="double"/>), a decimal, or a string.</summary>
    public sealed record Literal(object? Value) : WebLingoDebugExpression;

    /// <summary>A name, a variable of the frame's scope, as the Variables view shows it.</summary>
    public sealed record Name(string Identifier) : WebLingoDebugExpression;

    /// <summary><c>target.name</c>, or <c>target?.name</c>: a field, or a property a native class gives.</summary>
    public sealed record Member(WebLingoDebugExpression Target, string Identifier, bool Optional) : WebLingoDebugExpression;

    /// <summary><c>target[key]</c>, or <c>target?[key]</c>: a collection's entry, or a native class's indexer's.</summary>
    public sealed record Index(WebLingoDebugExpression Target, WebLingoDebugExpression Key, bool Optional) : WebLingoDebugExpression;

    /// <summary><c>target.method(arguments)</c>, or <c>target?.method(…)</c>: a method of a string, an array, a map or a set that only reads.</summary>
    public sealed record Call(WebLingoDebugExpression Target, string Method, IReadOnlyList<WebLingoDebugExpression> Arguments, bool Optional)
        : WebLingoDebugExpression;

    /// <summary><c>!x</c>, <c>-x</c> or <c>+x</c>.</summary>
    public sealed record Unary(WebLingoDebugOperator Operator, WebLingoDebugExpression Operand) : WebLingoDebugExpression;

    /// <summary>A binary operator, <c>&amp;&amp;</c>, <c>||</c> and <c>??</c> among them, which read their right operand only where they must.</summary>
    public sealed record Binary(WebLingoDebugOperator Operator, WebLingoDebugExpression Left, WebLingoDebugExpression Right) : WebLingoDebugExpression;

    /// <summary><c>test ? whenTrue : whenFalse</c>.</summary>
    public sealed record Conditional(WebLingoDebugExpression Test, WebLingoDebugExpression WhenTrue, WebLingoDebugExpression WhenFalse)
        : WebLingoDebugExpression;

    private static readonly HashSet<string> StringMethods = new(StringComparer.Ordinal)
    {
        "indexOf", "lastIndexOf", "contains", "includes", "startsWith", "endsWith", "slice", "split", "trim", "trimStart", "trimEnd",
        "toUpper", "toUpperCase", "toLower", "toLowerCase", "replace", "replaceAll", "repeat", "padStart", "padLeft", "padEnd",
        "padRight", "join", "compare", "removePrefix", "removeSuffix"
    };

    private static readonly HashSet<string> ArrayMethods = new(StringComparer.Ordinal)
    {
        "indexOf", "contains", "includes", "slice", "concat", "join", "reverse", "sort"
    };

    private static readonly HashSet<string> KeyedMethods = new(StringComparer.Ordinal) { "has" };

    /// <summary>Whether a built-in class has a method of this name that only reads, which a condition may call.</summary>
    public static bool ReadsOnly(string method) => StringMethods.Contains(method) || ArrayMethods.Contains(method) || KeyedMethods.Contains(method);

    /// <summary>Whether a string's, an array's, a map's or a set's method of this name only reads.</summary>
    internal static bool ReadsOnly(WebLingo.Runtime.WebLingoValueKind kind, string method) => kind switch
    {
        WebLingo.Runtime.WebLingoValueKind.String => StringMethods.Contains(method),
        WebLingo.Runtime.WebLingoValueKind.Array => ArrayMethods.Contains(method),
        WebLingo.Runtime.WebLingoValueKind.Map or WebLingo.Runtime.WebLingoValueKind.Set => KeyedMethods.Contains(method),
        _ => false
    };
}

public enum WebLingoDebugOperator
{
    Add, Subtract, Multiply, Divide, Remainder, IntegerDivide,
    Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual,
    And, Or, Coalesce,
    Not, Negate, Plus
}

/// <summary>The scope a debugger stops in, which tests a breakpoint's condition there (specification, section 5.7, part 3).</summary>
public interface IWebLingoDebugScope
{
    /// <summary>
    /// Evaluates a condition, without running the program's code: true, with whether it holds, where it gives a Boolean;
    /// false, with why, where it fails or gives anything else.
    /// </summary>
    bool TryTest(WebLingoDebugExpression condition, out bool holds, out string? why);
}
