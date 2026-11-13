using System.Globalization;
using System.Text;
namespace WebLingo.Runtime;

/// <summary>
/// How a value is shown as text (what <c>write</c> writes; proposed, section 15.2): <c>null</c>, <c>true</c>,
/// <c>false</c>, an int64 in decimal digits, and a binary64 as the shortest decimal that reads back as the same
/// number, always with a decimal point or an exponent so that it is not mistaken for an integer (<c>5.0</c>,
/// <c>3.5</c>, <c>1E+21</c>), or <c>NaN</c>, <c>Infinity</c>, <c>-Infinity</c>. A string is its own text, and an array
/// <c>[1, "a", null]</c>, its strings quoted, with their escapes (section 3.6). It never depends on the culture.
/// </summary>
public static class WsValueFormatter
{
    /// <summary>The text of a value; a string's, and a collection's, only their heap knows.</summary>
    public static string Format(WsValue value, WsHeap? heap)
    {
        if (heap is null) return Format(value);
        return value.Kind switch
        {
            WsValueKind.String => heap.GetString(value),
            WsValueKind.Array => FormatCollection(value, heap),
            _ => Format(value)
        };
    }

    public static string Format(WsValue value) => value.Kind switch
    {
        WsValueKind.Null => "null",
        WsValueKind.String => $"string#{value.Handle}",
        WsValueKind.Function or WsValueKind.Builtin => "function",
        WsValueKind.Cell => "cell",
        WsValueKind.Array => $"array#{value.Handle}",
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
        WsValueKind.Function or WsValueKind.Builtin => "a function",
        WsValueKind.Array => "an array",
        _ => $"the number {Format(value)}"
    };

    /// <summary>
    /// A string as a literal would write it: in double quotes, with <c>\\</c>, <c>\"</c>, <c>\n</c>, <c>\r</c>,
    /// <c>\t</c> and <c>\0</c>, and any other control character as <c>\u{…}</c>.
    /// </summary>
    public static string Quote(string text)
    {
        var quoted = new StringBuilder(text.Length + 2);
        quoted.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': quoted.Append("\\\\"); break;
                case '"': quoted.Append("\\\""); break;
                case '\n': quoted.Append("\\n"); break;
                case '\r': quoted.Append("\\r"); break;
                case '\t': quoted.Append("\\t"); break;
                case '\0': quoted.Append("\\0"); break;
                default:
                    if (c < ' ' || c == '\u007F')
                    {
                        quoted.Append("\\u{").Append(((int)c).ToString("X", CultureInfo.InvariantCulture)).Append('}');
                    }
                    else
                    {
                        quoted.Append(c);
                    }
                    break;
            }
        }
        return quoted.Append('"').ToString();
    }

    /// <summary>
    /// A collection as text, its collections written the same way, and one it is already writing as <c>[…]</c>. It
    /// keeps a stack of its own, not .NET's, so that a deeply nested collection cannot overflow the thread's stack.
    /// </summary>
    private static string FormatCollection(WsValue root, WsHeap heap)
    {
        var text = new StringBuilder("[");
        var open = new HashSet<int> { root.Handle };
        var pending = new Stack<(WsValue Array, int Next)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (array, next) = pending.Pop();
            var elements = heap.GetArray(array);
            if (next >= elements.Count)
            {
                text.Append(']');
                open.Remove(array.Handle);
                continue;
            }
            if (next > 0) text.Append(", ");
            pending.Push((array, next + 1));
            var element = elements[next];
            if (element.Kind == WsValueKind.Array)
            {
                if (!open.Add(element.Handle))
                {
                    text.Append("[…]");
                    continue;
                }
                text.Append('[');
                pending.Push((element, 0));
                continue;
            }
            text.Append(element.Kind == WsValueKind.String ? Quote(heap.GetString(element)) : Format(element));
        }
        return text.ToString();
    }

    private static string FormatBinary64(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') ? text : text + ".0";
    }
}
