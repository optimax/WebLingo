using System.Globalization;
using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// An interpolation's format and alignment (specification, section 3.5), <c>{price,10:N2}</c>, which the parser writes as
/// a call of the hidden class's built-in method, <c>&lt;text&gt;.&lt;format&gt;(value, width, format)</c>. A format is one
/// of .NET's numeric format strings, standard or custom, applied with the invariant culture to an integer or a number, a
/// class's instance holding one included, or of its date and time format strings, to a date (section 3.2), and gives
/// <c>null</c> nothing; any other value is WL3001. An alignment pads
/// the text with spaces to its width, counted in Unicode scalar values, on the left when it is positive and on the
/// right when it is negative.
/// </summary>
public static class WebLingoInterpolationFormat
{
    /// <summary>
    /// The text of <paramref name="value"/>, formatted by <paramref name="format"/> when it is a string, and else already
    /// the value's text, then aligned by <paramref name="width"/>, an integer.
    /// </summary>
    public static bool TryFormat(WebLingoValue value, WebLingoValue width, WebLingoValue format, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        string text;
        if (format.Kind == WebLingoValueKind.String)
        {
            if (!TryApply(value, heap.GetString(format), heap, out text, out error)) return false;
        }
        else
        {
            text = heap.GetString(value);
        }
        if (width.Kind != WebLingoValueKind.Int64)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"An alignment is an integer, not {WebLingoValueFormatter.Describe(width, heap)}.");
            return false;
        }
        var columns = width.AsInt64;
        if (columns is > WebLingoInterpolation.MaxWidth or < -WebLingoInterpolation.MaxWidth)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"An alignment is at most {WebLingoInterpolation.MaxWidth.ToString(CultureInfo.InvariantCulture)} either way, "
                + $"not {columns.ToString(CultureInfo.InvariantCulture)}.");
            return false;
        }
        var padding = Math.Abs(columns) - WebLingoMembers.ScalarCount(text);
        if (padding > 0) text = columns > 0 ? new string(' ', (int)padding) + text : text + new string(' ', (int)padding);
        return heap.TryAllocateString(text, out result, out error);
    }

    /// <summary>A value formatted: <c>null</c> as nothing, an integer or a number by .NET's format, anything else WL3001.</summary>
    private static bool TryApply(WebLingoValue value, string format, WebLingoHeap heap, out string text, out WebLingoRuntimeError? error)
    {
        text = "";
        error = null;
        if (value.Kind == WebLingoValueKind.Null) return true;
        var held = heap.Held(value);
        try
        {
            switch (held.Kind)
            {
                case WebLingoValueKind.Int64:
                    text = held.AsInt64.ToString(format, CultureInfo.InvariantCulture);
                    return true;
                case WebLingoValueKind.Binary64:
                    text = held.AsBinary64.ToString(format, CultureInfo.InvariantCulture);
                    return true;
                case WebLingoValueKind.Decimal:
                    text = heap.GetDecimal(held).ToString(format, CultureInfo.InvariantCulture);
                    return true;
                case WebLingoValueKind.DateTime:
                    // A date by .NET's date and time formats (section 3.2, "Dates and times").
                    return WebLingoMembers.TryFormatDateTime(held, format, out text, out error);
            }
        }
        catch (FormatException)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'{format}' is not a format of {WebLingoValueFormatter.Describe(value, heap)}.");
            return false;
        }
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
            $"A format applies to an integer, a number or a date and time, not {WebLingoValueFormatter.Describe(value, heap)}.");
        return false;
    }
}
