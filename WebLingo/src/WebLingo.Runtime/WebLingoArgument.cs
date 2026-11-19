using System.Globalization;
using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// An argument of a built-in member, or of a standard module's function, as an error about it names it (the owner's
/// answers, 18 November 2026, "Argument Errors That Name the Argument"): the member, as the program called it, the
/// argument's place among the member's parameters, from 0, how many parameters the member has, the argument's name, as the
/// references name it, and what it must be. Its message names the member, and, where the member has several parameters,
/// the argument by its place and its name, then says what the argument must be and what it was given: "The argument of
/// 'trim' must be a string of characters to trim, not the integer 1.", "The first argument of 'padStart', the width, must
/// be an integer, not a string."
/// </summary>
public readonly struct WebLingoArgument(string member, int position, int of, string name, string must)
{
    /// <summary>The member, as the program called it, <c>includes</c> where it called <c>contains</c> so.</summary>
    public string Member { get; } = member;

    /// <summary>The argument's place among the member's parameters, from 0.</summary>
    public int Position { get; } = position;

    /// <summary>How many parameters the member has, its optional ones included.</summary>
    public int Of { get; } = of;

    /// <summary>The argument's name, as the references name it, with its article, <c>the width</c>, for a member of several.</summary>
    public string Name { get; } = name;

    /// <summary>What the argument must be, <c>an integer</c>.</summary>
    public string Must { get; } = must;

    /// <summary>The argument as a message begins with it: <c>The argument of 'trim'</c>, <c>The first argument of 'padStart', the width,</c>.</summary>
    public string Named => Of <= 1 ? $"The argument of '{Member}'" : $"The {Ordinal(Position)} argument of '{Member}', {Name},";

    /// <summary>WL3001: the argument given, not what it must be.</summary>
    public WebLingoRuntimeError Wrong(WebLingoValue given, WebLingoHeap heap) => Wrong(WebLingoValueFormatter.Describe(given, heap));

    /// <summary>WL3001: the argument given, a value its description needs no heap for, not what it must be.</summary>
    public WebLingoRuntimeError Wrong(WebLingoValue given) => Wrong(WebLingoValueFormatter.Describe(given));

    /// <summary>WL3001: what was given, described by the caller, not what the argument must be.</summary>
    public WebLingoRuntimeError Wrong(string given) => new(WebLingoDiagnosticCodes.InvalidOperation, $"{Named} must be {Must}, not {given}.");

    private static string Ordinal(int position) => position switch
    {
        0 => "first",
        1 => "second",
        2 => "third",
        3 => "fourth",
        4 => "fifth",
        5 => "sixth",
        6 => "seventh",
        7 => "eighth",
        _ => (position + 1).ToString(CultureInfo.InvariantCulture) + "th"
    };
}
