using System.Globalization;
namespace WebLingo.Runtime;

/// <summary>
/// How a value is shown as text (what <c>write</c> writes; proposed, section 15.2): <c>null</c>, <c>true</c>,
/// <c>false</c>, an int64 in decimal digits, and a binary64 as the shortest decimal that reads back as the same
/// number, always with a decimal point or an exponent so that it is not mistaken for an integer (<c>5.0</c>,
/// <c>3.5</c>, <c>1E+21</c>), or <c>NaN</c>, <c>Infinity</c>, <c>-Infinity</c>. It never depends on the culture.
/// </summary>
public static class WsValueFormatter
{
    /// <summary>The text of a value; a string is its own text, which only its heap knows.</summary>
    public static string Format(WsValue value, WsHeap? heap) =>
        value.Kind == WsValueKind.String && heap is not null ? heap.GetString(value) : Format(value);

    public static string Format(WsValue value) => value.Kind switch
    {
        WsValueKind.Null => "null",
        WsValueKind.String => $"string#{value.Handle}",
        WsValueKind.Boolean => value.AsBoolean ? "true" : "false",
        WsValueKind.Int64 => value.AsInt64.ToString(CultureInfo.InvariantCulture),
        _ => FormatBinary64(value.AsBinary64)
    };

    /// <summary>A value described in a message: its kind, and the value itself where that helps.</summary>
    public static string Describe(WsValue value) => value.Kind switch
    {
        WsValueKind.Null => "null",
        WsValueKind.Boolean => $"the Boolean {Format(value)}",
        WsValueKind.Int64 => $"the integer {Format(value)}",
        WsValueKind.String => "a string",
        _ => $"the number {Format(value)}"
    };

    private static string FormatBinary64(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') ? text : text + ".0";
    }
}
