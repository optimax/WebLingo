namespace WebLingo.Core;

/// <summary>
/// What an interpolated string is compiled to use (specification, section 3.5): <see cref="Text"/>, the hidden class
/// whose <c>of(value)</c> gives a value's text, and <see cref="Format"/>, its built-in method, which the runtime gives
/// every class, writing a value with a format, an alignment, or both. Neither name is one a program can write, so that
/// no program calls, hides or replaces them.
/// </summary>
public static class WebLingoInterpolation
{
    public const string Text = "<text>";
    public const string Format = "<format>";

    /// <summary>The widest an alignment pads a value's text, either way.</summary>
    public const int MaxWidth = 999_999;

    /// <summary>The largest precision a standard format may give (<c>N999</c>), so that no format makes a huge string.</summary>
    public const int MaxPrecision = 999;
}
