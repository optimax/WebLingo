using System.Net;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Url;

/// <summary>URL components, relative resolution, and ordered query pairs.</summary>
public sealed class WebLingoUrlModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Function("parse", 1, Parse);
        registry.Function("resolve", 2, Resolve);
        registry.Function("parseQuery", 1, ParseQuery);
        registry.Function("formatQuery", 1, FormatQuery);
    }

    private static bool Parse(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (args[0].Kind != WebLingoValueKind.String
            || !Uri.TryCreate(heap.GetString(args[0]), UriKind.Absolute, out var uri))
        {
            error = new WebLingoArgument("parse", 0, 1, "the URL", "an absolute URL string").Wrong(args[0], heap);
            return false;
        }
        var components = new (string Name, string? Text)[]
        {
            ("scheme", uri.Scheme), ("host", uri.Host), ("port", null), ("path", uri.AbsolutePath),
            ("query", WithoutPrefix(uri.Query, '?')), ("fragment", WithoutPrefix(uri.Fragment, '#'))
        };
        var entries = new WebLingoValue[2 * components.Length];
        heap.AddRoots(entries);
        try
        {
            for (var i = 0; i < components.Length; i++)
            {
                var (name, value) = components[i];
                if (!heap.TryAllocateString(name, out entries[2 * i], out error)) return false;
                if (value is not null && !heap.TryAllocateString(value, out entries[2 * i + 1], out error)) return false;
            }
            if (!uri.IsDefaultPort && uri.Port >= 0) entries[5] = WebLingoValue.FromInt64(uri.Port);
            return heap.TryAllocateMap(entries, 0, components.Length, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(entries);
        }
    }

    private static bool Resolve(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (args[0].Kind != WebLingoValueKind.String
            || !Uri.TryCreate(heap.GetString(args[0]), UriKind.Absolute, out var basis))
        {
            error = new WebLingoArgument("resolve", 0, 2, "the base", "an absolute URL string").Wrong(args[0], heap);
            return false;
        }
        if (args[1].Kind != WebLingoValueKind.String
            || !Uri.TryCreate(basis, heap.GetString(args[1]), out var target))
        {
            error = new WebLingoArgument("resolve", 1, 2, "the relative URL", "a URL reference string").Wrong(args[1], heap);
            return false;
        }
        return heap.TryAllocateString(target.AbsoluteUri, out result, out error);
    }

    private static bool ParseQuery(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (args[0].Kind != WebLingoValueKind.String)
        {
            error = new WebLingoArgument("parseQuery", 0, 1, "the query", "a string").Wrong(args[0], heap);
            return false;
        }
        var fields = WithoutPrefix(heap.GetString(args[0]), '?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var pairs = new WebLingoValue[fields.Length];
        var pair = new WebLingoValue[2];
        heap.AddRoots(pairs);
        heap.AddRoots(pair);
        try
        {
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                var split = field.IndexOf('=');
                if (split < 0) split = field.Length;
                var name = WebUtility.UrlDecode(field[..split]);
                var value = split == field.Length ? "" : WebUtility.UrlDecode(field[(split + 1)..]);
                if (!heap.TryAllocateString(name, out pair[0], out error)
                    || !heap.TryAllocateString(value, out pair[1], out error)
                    || !heap.TryAllocateArray(pair, 0, pair.Length, out pairs[i], out error))
                    return false;
            }
            return heap.TryAllocateArray(pairs, 0, pairs.Length, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(pair);
            heap.RemoveLastRoots(pairs);
        }
    }

    private static bool FormatQuery(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var expected = new WebLingoArgument("formatQuery", 0, 1, "the pairs", "an array of [name, value] string pairs");
        if (args[0].Kind != WebLingoValueKind.Array)
        {
            error = expected.Wrong(args[0], heap);
            return false;
        }
        var pairs = heap.GetArray(args[0]);
        var fields = new string[pairs.Count];
        for (var i = 0; i < fields.Length; i++)
        {
            var pair = heap.Held(pairs[i]);
            if (pair.Kind == WebLingoValueKind.Array)
            {
                var values = heap.GetArray(pair);
                if (values.Count == 2)
                {
                    var (name, value) = (heap.Held(values[0]), heap.Held(values[1]));
                    if (name.Kind == WebLingoValueKind.String && value.Kind == WebLingoValueKind.String)
                    {
                        fields[i] = WebUtility.UrlEncode(heap.GetString(name)) + "=" + WebUtility.UrlEncode(heap.GetString(value));
                        continue;
                    }
                }
            }
            error = expected.Wrong($"an invalid pair at index {i}");
            return false;
        }
        return heap.TryAllocateString(string.Join('&', fields), out result, out error);
    }

    private static string WithoutPrefix(string value, char prefix) => value.Length > 0 && value[0] == prefix ? value[1..] : value;
}
