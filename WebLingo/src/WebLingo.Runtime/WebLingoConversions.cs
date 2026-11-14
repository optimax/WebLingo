using System.Globalization;
using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// A string read as a value of a kind (specification, section 3.7): <c>Integer.parse("10")</c>, <c>Number.parse("1.5")</c>
/// and <c>Boolean.parse("true")</c>, which <c>new Integer("10")</c> and the like read the same way. The text is the
/// kind's literal, as WebLingo writes it, a number's with a sign before it if it has one, and nothing around it.
/// </summary>
public static class WebLingoConversions
{
    /// <summary>A string as a value of <paramref name="kind"/>, an integer, a number or a Boolean; WL3001 for one that is not its literal.</summary>
    public static bool TryParse(WebLingoValueKind kind, string text, out WebLingoValue value, out WebLingoRuntimeError? error)
    {
        error = null;
        value = WebLingoValue.Null;
        if (kind == WebLingoValueKind.Int64)
        {
            if (TryParseInteger(text, out var integer, out var overflows))
            {
                value = WebLingoValue.FromInt64(integer);
                return true;
            }
            error = new WebLingoRuntimeError(overflows ? WebLingoDiagnosticCodes.IntegerOverflow : WebLingoDiagnosticCodes.InvalidOperation,
                overflows ? $"\"{text}\" is an integer outside the 64-bit range." : $"\"{text}\" is not an integer.");
            return false;
        }
        switch (kind)
        {
            case WebLingoValueKind.Binary64 when TryParseNumber(text, out var number):
                value = WebLingoValue.FromBinary64(number);
                return true;
            case WebLingoValueKind.Boolean when text is "true" or "false":
                value = WebLingoValue.FromBoolean(text == "true");
                return true;
            default:
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, kind == WebLingoValueKind.Boolean
                    ? $"\"{text}\" is not a Boolean, \"true\" or \"false\"."
                    : $"\"{text}\" is not a number.");
                return false;
        }
    }

    /// <summary>An integer literal, decimal digits, with <c>-</c> or <c>+</c> before them if it has a sign.</summary>
    private static bool TryParseInteger(string text, out long value, out bool overflows)
    {
        value = 0;
        overflows = false;
        var start = text.Length > 0 && text[0] is '-' or '+' ? 1 : 0;
        var negative = start == 1 && text[0] == '-';
        if (start == text.Length) return false;
        var limit = negative ? 9223372036854775808UL : 9223372036854775807UL;
        ulong magnitude = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i])) return false;
            var digit = (ulong)(text[i] - '0');
            if (magnitude > (limit - digit) / 10)
            {
                // Its digits go on being read, so that a string that is no integer is not called one out of range.
                overflows = true;
                continue;
            }
            magnitude = magnitude * 10 + digit;
        }
        if (overflows) return false;
        value = negative ? (long)(0UL - magnitude) : (long)magnitude;
        return true;
    }

    /// <summary>
    /// A number literal: an integer's digits, a fraction's (digits on both sides of the point), an exponent, or both, or
    /// <c>Infinity</c> or <c>NaN</c>, with a sign before it if it has one; one too large for a binary64 is not one.
    /// </summary>
    private static bool TryParseNumber(string text, out double value)
    {
        value = 0;
        var start = text.Length > 0 && text[0] is '-' or '+' ? 1 : 0;
        var sign = start == 1 && text[0] == '-' ? -1.0 : 1.0;
        var body = text[start..];
        if (body == "Infinity" || body == "NaN")
        {
            value = body == "NaN" ? double.NaN : sign * double.PositiveInfinity;
            return true;
        }
        var i = Digits(body, 0);
        if (i == 0) return false;
        if (i < body.Length && body[i] == '.')
        {
            var fraction = Digits(body, i + 1);
            if (fraction == i + 1) return false;
            i = fraction;
        }
        if (i < body.Length && body[i] is 'e' or 'E')
        {
            var exponent = i + 1 < body.Length && body[i + 1] is '-' or '+' ? i + 2 : i + 1;
            var end = Digits(body, exponent);
            if (end == exponent) return false;
            i = end;
        }
        if (i != body.Length) return false;
        value = sign * double.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture);
        return !double.IsInfinity(value);
    }

    private static int Digits(string text, int start)
    {
        while (start < text.Length && char.IsAsciiDigit(text[start])) start++;
        return start;
    }
}
