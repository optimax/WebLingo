using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Http;

/// <summary>
/// <c>std:http</c>, a standard module (specification, sections 3.9 and 7.9), a native module installed with the runtime in
/// the <c>std</c> scheme's folder as <c>http.dll</c>: HTTP requests through the platform's network
/// (<see cref="WebLingoHeap.Network"/>), each asynchronous, a task its response completes, the request run off the worker:
/// <c>get(url, headers)</c>, <c>post(url, body, headers)</c> and <c>request(method, url, body, headers)</c>. A response is
/// a map: its <c>"status"</c>, an integer; its <c>"headers"</c>, a map of each header's name, in lower case, to its
/// value; its <c>"body"</c>, its text, in the charset its <c>Content-Type</c> names, UTF-8 unless it names another the
/// core knows; and its <c>"bytes"</c>. A body to send is a string, sent as UTF-8, or bytes; headers, a map of strings. A
/// redirect is given as it is, not followed. A host the application may not reach is a <c>NetworkPermissionError</c>
/// (WL3017), an address that is not an <c>http</c> or <c>https</c> one WL3001, and a request that fails a
/// <c>NetworkError</c> (WL3014).
/// </summary>
public sealed class WebLingoHttpModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Function("get", 2, 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            Send(heap, new("get", 0, 2, "the address", Address), "GET", a[0], default, WebLingoValue.Null, new("get", 1, 2, "the headers", Headers), a[1],
                out result, out error));
        registry.Function("post", 3, 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            Send(heap, new("post", 0, 3, "the address", Address), "POST", a[0], new("post", 1, 3, "the body", Body), a[1],
                new("post", 2, 3, "the headers", Headers), a[2], out result, out error));
        registry.Function("request", 4, 2, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (a[0].Kind != WebLingoValueKind.String || !IsToken(heap.GetString(a[0])))
            {
                error = new WebLingoArgument("request", 0, 4, "the method", "a method's name, \"GET\" or \"PUT\"").Wrong(a[0], heap);
                return false;
            }
            return Send(heap, new("request", 1, 4, "the address", Address), heap.GetString(a[0]).ToUpperInvariant(), a[1],
                new("request", 2, 4, "the body", Body), a[2], new("request", 3, 4, "the headers", Headers), a[3], out result, out error);
        });
    }

    /// <summary>What an address, a body and headers must be, as an argument's error says.</summary>
    private const string Address = "a string, \"https://example.com/\"";
    private const string Body = "a string or bytes";
    private const string Headers = "a map of names to strings, { \"Accept\": \"text/plain\" }";

    private static bool Send(WebLingoHeap heap, WebLingoArgument urlArgument, string method, WebLingoValue url, WebLingoArgument bodyArgument,
        WebLingoValue body, WebLingoArgument headersArgument, WebLingoValue headers, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (url.Kind != WebLingoValueKind.String)
        {
            error = urlArgument.Wrong(url, heap);
            return false;
        }
        byte[]? data = null;
        if (body.Kind == WebLingoValueKind.String) data = WebLingoTextCodec.Encode(heap.GetString(body), WebLingoCharset.Utf8);
        else if (body.Kind == WebLingoValueKind.Bytes) data = heap.GetBytes(body);
        else if (body.Kind != WebLingoValueKind.Null)
        {
            error = bodyArgument.Wrong(body, heap);
            return false;
        }
        var sent = new List<KeyValuePair<string, string>>();
        if (headers.Kind == WebLingoValueKind.Map)
        {
            for (var i = 0; i < heap.MapCount(headers); i++)
            {
                var (name, value) = heap.MapEntryAt(headers, i);
                if (name.Kind != WebLingoValueKind.String || value.Kind != WebLingoValueKind.String || !IsToken(heap.GetString(name)))
                {
                    error = Wrong($"{headersArgument.Named} must be {Headers}.");
                    return false;
                }
                sent.Add(new(heap.GetString(name), heap.GetString(value)));
            }
        }
        else if (headers.Kind != WebLingoValueKind.Null)
        {
            error = headersArgument.Wrong(headers, heap);
            return false;
        }
        if (heap.Network is not { } network)
        {
            error = Wrong("This host gives scripts no network.");
            return false;
        }
        if (heap.Scheduler is not { } scheduler)
        {
            error = Wrong("std:http's functions need a VM that runs tasks.");
            return false;
        }
        var request = new WebLingoHttpOutgoing(method, heap.GetString(url), sent, data);
        return scheduler.TryRunOffWorker(() => network.Send(request), Response, Failed, out result, out error);
    }

    /// <summary>A response as a map: its status, its headers, its text and its bytes.</summary>
    private static bool Response(WebLingoHttpIncoming response, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var made = new List<WebLingoValue>(); // Kept where the collector sees them until the response is whole.
        heap.AddRoots(made);
        try
        {
            if (!heap.TryAllocateMap([], 0, 0, out var headers, out error)) return false;
            made.Add(headers);
            var charset = WebLingoCharset.Utf8;
            foreach (var (name, value) in response.Headers)
            {
                if (!heap.TryAllocateString(name, out var key, out error) || !heap.TryAllocateString(value, out var text, out error)) return false;
                made.Add(key);
                made.Add(text);
                if (!heap.TrySetInMap(headers, key, text, out error)) return false;
                if (name == "content-type" && Charset(value) is { } named && WebLingoTextCodec.TryFind(named, out var found)) charset = found;
            }
            if (!heap.TryAllocateString(WebLingoTextCodec.Decode(response.Body, charset), out var body, out error)) return false;
            made.Add(body);
            if (!heap.TryAllocateBytes(response.Body, out var bytes, out error)) return false;
            made.Add(bytes);
            WebLingoValue[] entries = [default, WebLingoValue.FromInt64(response.Status), default, headers, default, body, default, bytes];
            string[] names = ["status", "headers", "body", "bytes"];
            for (var i = 0; i < names.Length; i++)
            {
                if (!heap.TryAllocateString(names[i], out entries[2 * i], out error)) return false;
                made.Add(entries[2 * i]);
            }
            return heap.TryAllocateMap(entries, 0, names.Length, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(made);
        }
    }

    /// <summary>A <c>Content-Type</c>'s charset, <c>text/html; charset=utf-8</c>'s <c>utf-8</c>, or null.</summary>
    private static string? Charset(string contentType)
    {
        foreach (var part in contentType.Split(';'))
        {
            var setting = part.Trim();
            if (setting.StartsWith("charset=", StringComparison.OrdinalIgnoreCase)) return setting[8..].Trim('"');
        }
        return null;
    }

    private static WebLingoRuntimeError? Failed(Exception exception) => exception is WebLingoNetworkException failure
        ? new WebLingoRuntimeError(failure.Kind switch
            {
                WebLingoNetworkErrorKind.Failed => WebLingoDiagnosticCodes.NetworkFailure,
                WebLingoNetworkErrorKind.NotAllowed => WebLingoDiagnosticCodes.NetworkPermissionDenied,
                _ => WebLingoDiagnosticCodes.InvalidOperation
            },
            Capitalized(failure.Message) + ".")
        : null;

    /// <summary>A name as HTTP writes a method's and a header's: ASCII letters, digits and <c>-</c>, <c>_</c> and <c>.</c>.</summary>
    private static bool IsToken(string name)
    {
        if (name.Length == 0) return false;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')) return false;
        }
        return true;
    }

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static WebLingoRuntimeError Wrong(string message) => new(WebLingoDiagnosticCodes.InvalidOperation, message);
}
