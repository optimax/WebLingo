using System.Globalization;
using System.Text;
namespace WebLingo.Runtime;

/// <summary>
/// How a value is shown as text (what <c>write</c> writes; proposed, section 15.2): <c>null</c>, <c>true</c>,
/// <c>false</c>, an int64 in decimal digits, and a binary64 as the shortest decimal that reads back as the same
/// number, always with a decimal point or an exponent so that it is not mistaken for an integer (<c>5.0</c>,
/// <c>3.5</c>, <c>1E+21</c>), or <c>NaN</c>, <c>Infinity</c>, <c>-Infinity</c>. A string is its own text, an array
/// <c>[1, "a", null]</c>, a map <c>{"active": true}</c> and a set <c>#{1, "a"}</c>, their strings quoted, with their
/// escapes (section 3.6). It
/// never depends on the culture.
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
            WsValueKind.Instance when heap.IsHolding(value) => Held(value, heap),
            WsValueKind.Array or WsValueKind.Map or WsValueKind.Set or WsValueKind.Instance => FormatCollection(value, heap),
            WsValueKind.Class => "class " + heap.ClassName(value),
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
        WsValueKind.Map => $"map#{value.Handle}",
        WsValueKind.Set => $"set#{value.Handle}",
        WsValueKind.Class => $"class#{value.Handle}",
        WsValueKind.Instance => $"instance#{value.Handle}",
        WsValueKind.BoundMethod => "function",
        WsValueKind.Missing => "missing",
        WsValueKind.Task => "task",
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
        WsValueKind.Function or WsValueKind.Builtin or WsValueKind.BoundMethod => "a function",
        WsValueKind.Array => "an array",
        WsValueKind.Class => "a class",
        WsValueKind.Instance => "an instance",
        WsValueKind.Map => "a map",
        WsValueKind.Set => "a set",
        WsValueKind.Task => "a task",
        _ => $"the number {Format(value)}"
    };

    /// <summary>A value described in a message, an instance and a class with their class's name.</summary>
    public static string Describe(WsValue value, WsHeap heap) => value.Kind switch
    {
        WsValueKind.Instance => $"an instance of {heap.ClassName(heap.ClassOf(value))}",
        WsValueKind.Class => $"the class {heap.ClassName(value)}",
        _ => Describe(value)
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
    /// A collection as text: an array <c>[1, "a"]</c>, a map <c>{"k": 1}</c>, its collections written the same way,
    /// and one it is already writing as <c>[…]</c> or <c>{…}</c>. It keeps a stack of its own, not .NET's, so that a
    /// deeply nested collection cannot overflow the thread's stack.
    /// </summary>
    private static string FormatCollection(WsValue root, WsHeap heap)
    {
        var text = new StringBuilder();
        var open = new HashSet<int>();
        var pending = new Stack<(WsValue Collection, int Next)>();
        Open(root, text, open, pending, heap);
        while (pending.Count > 0)
        {
            var (collection, next) = pending.Pop();
            var isMap = collection.Kind == WsValueKind.Map;
            var count = collection.Kind switch
            {
                WsValueKind.Map => heap.MapCount(collection),
                WsValueKind.Set => heap.SetCount(collection),
                WsValueKind.Instance => heap.FieldCount(collection),
                _ => heap.GetArray(collection).Count
            };
            if (next >= count)
            {
                text.Append(collection.Kind == WsValueKind.Array ? ']' : '}');
                open.Remove(collection.Handle);
                continue;
            }
            if (next > 0) text.Append(", ");
            pending.Push((collection, next + 1));
            WsValue item;
            if (isMap)
            {
                var (key, value) = heap.MapEntryAt(collection, next);
                text.Append(Scalar(key, heap)).Append(": ");
                item = value;
            }
            else if (collection.Kind == WsValueKind.Instance)
            {
                var (name, value) = heap.FieldAt(collection, next);
                text.Append(name).Append(": ");
                item = value;
            }
            else
            {
                item = collection.Kind == WsValueKind.Set ? heap.SetValueAt(collection, next) : heap.GetArray(collection)[next];
            }
            if (item.Kind is WsValueKind.Array or WsValueKind.Map or WsValueKind.Set or WsValueKind.Instance && !heap.IsHolding(item))
            {
                if (open.Contains(item.Handle))
                {
                    text.Append(ClassPrefix(item, heap)).Append(item.Kind switch
                    {
                        WsValueKind.Map => "{…}",
                        WsValueKind.Set => "#{…}",
                        WsValueKind.Instance => heap.ClassName(heap.ClassOf(item)) + " {…}",
                        _ => "[…]"
                    });
                    continue;
                }
                Open(item, text, open, pending, heap);
                continue;
            }
            text.Append(Scalar(item, heap));
        }
        return text.ToString();
    }

    private static void Open(WsValue collection, StringBuilder text, HashSet<int> open, Stack<(WsValue, int)> pending,
        WsHeap heap)
    {
        text.Append(ClassPrefix(collection, heap)).Append(collection.Kind switch
        {
            WsValueKind.Map => "{",
            WsValueKind.Set => "#{",
            WsValueKind.Instance => heap.ClassName(heap.ClassOf(collection)) + " {",
            _ => "["
        });
        open.Add(collection.Handle);
        pending.Push((collection, 0));
    }

    /// <summary>
    /// What comes before a collection that a class extending Array, Map or Set made: its class's name and a colon
    /// (<c>Stack:[1, 2]</c>; section 3.7).
    /// </summary>
    internal static string ClassPrefix(WsValue collection, WsHeap heap) =>
        collection.Kind is WsValueKind.Array or WsValueKind.Map or WsValueKind.Set
        && heap.ClassOfValue(collection) is { Kind: WsValueKind.Class } classValue
            ? heap.ClassName(classValue) + ":"
            : "";

    /// <summary>A value in a collection that is not a collection itself: a string quoted, anything else as written.</summary>
    internal static string Scalar(WsValue value, WsHeap heap) =>
        value.Kind == WsValueKind.String ? Quote(heap.GetString(value)) : Format(value, heap);

    /// <summary>
    /// An instance of a class extending a kind of value: its class's name, a colon, and the value it held, as a
    /// collection writes it, <c>Celsius:21.5</c>, <c>Name:"Ann"</c> (section 3.7).
    /// </summary>
    internal static string Held(WsValue value, WsHeap heap) =>
        heap.ClassName(heap.ClassOf(value)) + ":" + Scalar(heap.Held(value), heap);

    private static string FormatBinary64(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') ? text : text + ".0";
    }
}
