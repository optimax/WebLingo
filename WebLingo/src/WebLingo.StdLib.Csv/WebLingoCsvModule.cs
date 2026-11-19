using System.Text;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Csv;

/// <summary>Comma-separated text as arrays of strings or header-keyed maps.</summary>
public sealed class WebLingoCsvModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Function("parse", 2, 1, Parse);
        registry.Function("stringify", 2, 1, Stringify);
    }

    private static bool Parse(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var source = new WebLingoArgument("parse", 0, 2, "the text", "a CSV string");
        if (args[0].Kind != WebLingoValueKind.String)
        {
            error = source.Wrong(args[0], heap);
            return false;
        }
        if (args[1].Kind is not (WebLingoValueKind.Null or WebLingoValueKind.Boolean))
        {
            error = new WebLingoArgument("parse", 1, 2, "whether the first row contains headers", "a Boolean").Wrong(args[1], heap);
            return false;
        }

        var hasHeaders = args[1].Kind == WebLingoValueKind.Boolean && args[1].AsBoolean;
        if (!TryRead(heap.GetString(args[0]), out var rows, out var problem))
        {
            error = source.Wrong(problem);
            return false;
        }
        var header = hasHeaders && rows.Count > 0 ? rows[0] : [];
        if (hasHeaders && !Unique(header))
        {
            error = source.Wrong("text with duplicate column headers");
            return false;
        }

        var first = hasHeaders && rows.Count > 0 ? 1 : 0;
        var made = new WebLingoValue[rows.Count - first];
        var keys = new WebLingoValue[header.Length];
        heap.AddRoots(made);
        heap.AddRoots(keys);
        try
        {
            for (var i = 0; i < keys.Length; i++)
            {
                if (!heap.TryAllocateString(header[i], out keys[i], out error)) return false;
            }
            for (var row = first; row < rows.Count; row++)
            {
                var fields = rows[row];
                if (hasHeaders && fields.Length != keys.Length)
                {
                    error = source.Wrong($"record {row + 1} with {fields.Length} fields for {keys.Length} headers");
                    return false;
                }
                if (!MakeRow(fields, keys, hasHeaders, heap, out made[row - first], out error)) return false;
            }
            return heap.TryAllocateArray(made, 0, made.Length, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(keys);
            heap.RemoveLastRoots(made);
        }
    }

    private static bool MakeRow(string[] fields, WebLingoValue[] keys, bool mapped, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var values = new WebLingoValue[mapped ? fields.Length * 2 : fields.Length];
        heap.AddRoots(values);
        try
        {
            for (var i = 0; i < fields.Length; i++)
            {
                if (mapped) values[2 * i] = keys[i];
                if (!heap.TryAllocateString(fields[i], out values[mapped ? 2 * i + 1 : i], out error)) return false;
            }
            return mapped
                ? heap.TryAllocateMap(values, 0, fields.Length, out result, out error)
                : heap.TryAllocateArray(values, 0, fields.Length, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(values);
        }
    }

    private static bool TryRead(string text, out List<string[]> rows, out string problem)
    {
        rows = [];
        problem = "";
        var at = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        var fields = new List<string>();
        while (at < text.Length)
        {
            if (!ReadField(text, ref at, out var field))
            {
                problem = $"a malformed field in record {rows.Count + 1}";
                return false;
            }
            fields.Add(field);
            if (at < text.Length && text[at] == ',')
            {
                at++;
                if (at < text.Length) continue;
                fields.Add("");
            }
            rows.Add(fields.ToArray());
            fields.Clear();
            if (at < text.Length && text[at++] == '\r' && at < text.Length && text[at] == '\n') at++;
        }
        return true;
    }

    private static bool ReadField(string text, ref int at, out string field)
    {
        field = "";
        if (text[at] != '"')
        {
            var start = at;
            while (at < text.Length && !IsDelimiter(text[at]))
            {
                if (text[at++] == '"') return false;
            }
            field = text[start..at];
            return true;
        }

        var value = new StringBuilder();
        at++;
        while (at < text.Length)
        {
            var c = text[at++];
            if (c == '"')
            {
                if (at == text.Length || IsDelimiter(text[at]))
                {
                    field = value.ToString();
                    return true;
                }
                if (text[at++] != '"') return false;
            }
            value.Append(c);
        }
        return false;
    }

    private static bool IsDelimiter(char value) => value is ',' or '\r' or '\n';

    private static bool Stringify(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var expected = new WebLingoArgument("stringify", 0, 2, "the rows", "an array of string rows or maps matching the headers");
        if (args[0].Kind != WebLingoValueKind.Array)
        {
            error = expected.Wrong(args[0], heap);
            return false;
        }
        var mapped = args[1].Kind != WebLingoValueKind.Null;
        string[] headers = [];
        if (mapped && (!TryStrings(args[1], heap, out headers) || headers.Length == 0 || !Unique(headers)))
        {
            error = new WebLingoArgument("stringify", 1, 2, "the headers", "a nonempty array of distinct strings or null")
                .Wrong(args[1], heap);
            return false;
        }
        var output = new StringBuilder();
        if (mapped) WriteRow(output, headers);
        var rows = heap.GetArray(args[0]);
        for (var i = 0; i < rows.Count; i++)
        {
            var valid = mapped ? TryMapStrings(rows[i], headers, heap, out var fields) : TryStrings(rows[i], heap, out fields);
            if (!valid || fields.Length == 0)
            {
                error = expected.Wrong($"an invalid row at index {i}");
                return false;
            }
            WriteRow(output, fields);
        }
        return heap.TryAllocateString(output.ToString(), out result, out error);
    }

    private static bool TryStrings(WebLingoValue value, WebLingoHeap heap, out string[] fields)
    {
        fields = [];
        value = heap.Held(value);
        if (value.Kind != WebLingoValueKind.Array) return false;
        var array = heap.GetArray(value);
        fields = new string[array.Count];
        for (var i = 0; i < fields.Length; i++)
        {
            var field = heap.Held(array[i]);
            if (field.Kind != WebLingoValueKind.String) return false;
            fields[i] = heap.GetString(field);
        }
        return true;
    }

    private static bool TryMapStrings(WebLingoValue row, string[] headers, WebLingoHeap heap, out string[] fields)
    {
        fields = [];
        row = heap.Held(row);
        if (row.Kind != WebLingoValueKind.Map || heap.MapCount(row) != headers.Length) return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < headers.Length; i++)
        {
            var (key, value) = heap.MapEntryAt(row, i);
            value = heap.Held(value);
            if (key.Kind != WebLingoValueKind.String || value.Kind != WebLingoValueKind.String) return false;
            values.Add(heap.GetString(key), heap.GetString(value));
        }
        fields = new string[headers.Length];
        for (var i = 0; i < headers.Length; i++)
        {
            if (!values.TryGetValue(headers[i], out var field)) return false;
            fields[i] = field;
        }
        return true;
    }

    private static bool Unique(string[] fields) => new HashSet<string>(fields, StringComparer.Ordinal).Count == fields.Length;

    private static void WriteRow(StringBuilder output, string[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) output.Append(',');
            var field = fields[i];
            if (field.IndexOfAny([',', '"', '\r', '\n']) >= 0)
                output.Append('"').Append(field.Replace("\"", "\"\"")).Append('"');
            else
                output.Append(field);
        }
        output.Append("\r\n");
    }
}
