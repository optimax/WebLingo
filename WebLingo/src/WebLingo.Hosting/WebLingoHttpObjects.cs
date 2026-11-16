using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting;

/// <summary>
/// A request and its response as a page and a <c>.wl</c> route handler see them (specification, section 6.12, "The request
/// and the response in a page"; section 9, "M0.14 parts", part 3): instances of the native classes <c>Request</c> and
/// <c>Response</c>, which a page's <c>param request;</c> and <c>param response;</c> are given, and a handler's function
/// is called with. Their headers are instances of <c>Headers</c>, whose names are compared without regard to case. A
/// request's members: <c>method</c>, <c>path</c>, <c>scheme</c>, <c>host</c>, <c>query</c>, a map of its query's names to
/// their values, decoded, a name given twice having its last value, and <c>headers</c>, which cannot be changed. A
/// response's: <c>status</c>, an integer from 100 to 999, and <c>headers</c>, which can be changed until it is sent. A
/// header is read by indexing, <c>headers["Accept"]</c>, its values joined with <c>", "</c>, a missing one WL3007, as a
/// map's key is, <c>?[]</c> giving <c>null</c> for it; set by indexing, its value a string; and <c>has(name)</c>,
/// <c>add(name, value)</c> and <c>remove(name)</c>. A wrong status or header is WL3001 where it is set.
/// </summary>
internal sealed class WebLingoHttpObjects
{
    private const string Prefix = "http";

    private readonly WebLingoNativeClass _request = new("Request");
    private readonly WebLingoNativeClass _response = new("Response");
    private readonly WebLingoNativeClass _headers = new("Headers");

    public WebLingoHttpObjects()
    {
        _request
            .DeclareProperty("method", (a, heap, out r, out e) => Text(a, heap, s => s.Request.Method, out r, out e))
            .DeclareProperty("path", (a, heap, out r, out e) => Text(a, heap, s => s.Request.Path, out r, out e))
            .DeclareProperty("scheme", (a, heap, out r, out e) => Text(a, heap, s => s.Request.Scheme, out r, out e))
            .DeclareProperty("host", (a, heap, out r, out e) => Text(a, heap, s => s.Request.Host, out r, out e))
            .DeclareProperty("query", Query)
            .DeclareProperty("headers", (a, heap, out r, out e) => RequestHeaders(a, heap, out r, out e));
        _response
            .DeclareProperty("status", Status, SetStatus)
            .DeclareProperty("headers", (a, heap, out r, out e) => ResponseHeaders(a, heap, out r, out e));
        _headers
            .DeclareIndexer(GetHeader, SetHeader)
            .DeclareMethod("has", 1, HasHeader)
            .DeclareMethod("add", 2, AddHeader)
            .DeclareMethod("remove", 1, RemoveHeader);

        var members = new WebLingoBuiltinRegistry();
        var request = Info(_request, members);
        var response = Info(_response, members);
        var headers = Info(_headers, members);
        Members = members;
        Parameters =
        [
            new WebLingoHostParameter("request", Prefix + ".request", [request, headers]),
            new WebLingoHostParameter("response", Prefix + ".response", [response, headers])
        ];
    }

    /// <summary>The built-ins that run the classes' members, the same for every request.</summary>
    public WebLingoBuiltinRegistry Members { get; }

    /// <summary>What the compiler gives a page's <c>param request;</c> and <c>param response;</c>, and a handler's caller's.</summary>
    public IReadOnlyList<WebLingoHostParameter> Parameters { get; }

    /// <summary>The built-ins that make a request's instances, given their classes, each made once, kept for the execution.</summary>
    public IWebLingoBuiltinFunction[] For(IWebLingoHttpRequest request, IWebLingoHttpResponse response)
    {
        var requestState = new RequestState(request);
        var responseState = new ResponseState(response);
        return
        [
            new Maker(Prefix + ".request", (classes, heap, out made, out error) =>
                Make(_request, requestState, classes, heap, requestState.Roots, request.Headers, out made, out error)),
            new Maker(Prefix + ".response", (classes, heap, out made, out error) =>
                Make(_response, responseState, classes, heap, responseState.Roots, response.Headers, out made, out error))
        ];
    }

    /// <summary>
    /// An instance of <paramref name="owner"/>, holding <paramref name="state"/>, and its headers', both kept by the heap's
    /// lasting roots for the execution's life, so that the headers, which only the state names, are never freed; the same one
    /// each time it is asked for.
    /// </summary>
    private bool Make(WebLingoNativeClass owner, object state, ReadOnlySpan<WebLingoValue> classes, WebLingoHeap heap, WebLingoValue[] roots,
        IWebLingoHeaderCollection headers, out WebLingoValue made, out WebLingoRuntimeError? error)
    {
        error = null;
        if (roots[0].Kind != WebLingoValueKind.Null)
        {
            made = roots[0];
            return true;
        }
        made = WebLingoValue.Null;
        heap.AddLastingRoots(roots);
        if (!owner.TryMake(classes[0], heap, out roots[0], out error) || !owner.TrySetHostObject(roots[0], state, heap, out error)) return false;
        if (!_headers.TryMake(classes[1], heap, out roots[1], out error) || !_headers.TrySetHostObject(roots[1], headers, heap, out error)) return false;
        made = roots[0];
        return true;
    }

    private bool Text(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, Func<RequestState, string> read, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        return _request.TryGetHostObject<RequestState>(a[0], heap, out var state, out error) && heap.TryAllocateString(read(state), out result, out error);
    }

    private bool RequestHeaders(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_request.TryGetHostObject<RequestState>(a[0], heap, out var state, out error)) return false;
        result = state.Roots[1];
        return true;
    }

    private bool ResponseHeaders(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_response.TryGetHostObject<ResponseState>(a[0], heap, out var state, out error)) return false;
        result = state.Roots[1];
        return true;
    }

    /// <summary>The query's names and values, decoded, as a map, made when first read and the same map after.</summary>
    private bool Query(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_request.TryGetHostObject<RequestState>(a[0], heap, out var state, out error)) return false;
        if (state.Roots[2].Kind == WebLingoValueKind.Null)
        {
            var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in state.Request.QueryString.Split('&'))
            {
                if (part.Length == 0) continue;
                var equals = part.IndexOf('=', StringComparison.Ordinal);
                var name = Decode(equals < 0 ? part : part[..equals]);
                pairs.Remove(name);
                pairs.Add(name, equals < 0 ? "" : Decode(part[(equals + 1)..]));
            }
            var entries = new WebLingoValue[2 * pairs.Count];
            heap.AddRoots(entries);
            var at = 0;
            foreach (var (name, value) in pairs)
            {
                if (!heap.TryAllocateString(name, out entries[at++], out error) || !heap.TryAllocateString(value, out entries[at++], out error))
                {
                    heap.RemoveLastRoots(entries);
                    return false;
                }
            }
            var made = heap.TryAllocateMap(entries, 0, pairs.Count, out state.Roots[2], out error);
            heap.RemoveLastRoots(entries);
            if (!made) return false;
        }
        result = state.Roots[2];
        return true;
    }

    /// <summary>A query's name or value as the client meant it: <c>+</c> a space, and <c>%xx</c> its byte, as UTF-8.</summary>
    private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

    private bool Status(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_response.TryGetHostObject<ResponseState>(a[0], heap, out var state, out error)) return false;
        result = WebLingoValue.FromInt64(state.Response.StatusCode);
        return true;
    }

    private bool SetStatus(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_response.TryGetHostObject<ResponseState>(a[0], heap, out var state, out error)) return false;
        if (a[1].Kind != WebLingoValueKind.Int64 || a[1].AsInt64 is < 100 or > 999)
        {
            error = Invalid($"A response's status is an integer from 100 to 999, not {WebLingoValueFormatter.Describe(a[1], heap)}.");
            return false;
        }
        if (state.Response.TrySetStatusCode((int)a[1].AsInt64)) return true;
        error = Invalid("The response's status cannot be set: it has been sent.");
        return false;
    }

    private bool GetHeader(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_headers.TryGetHostObject<IWebLingoHeaderCollection>(a[0], heap, out var headers, out error) || !TryName(a[1], heap, out var name, out error))
        {
            return false;
        }
        if (headers.Get(name) is { } value) return heap.TryAllocateString(value, out result, out error);
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NoSuchKey, $"There is no header '{name}'.");
        return false;
    }

    private bool SetHeader(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        Change(a, heap, (headers, name, value) => headers.Set(name, value!), withValue: true, out result, out error);

    private bool AddHeader(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        Change(a, heap, (headers, name, value) => headers.Add(name, value!), withValue: true, out result, out error);

    private bool HasHeader(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_headers.TryGetHostObject<IWebLingoHeaderCollection>(a[0], heap, out var headers, out error) || !TryName(a[1], heap, out var name, out error))
        {
            return false;
        }
        result = WebLingoValue.FromBoolean(headers.Get(name) is not null);
        return true;
    }

    /// <summary>Removes a header, giving whether it was there, as a map's <c>remove</c> does.</summary>
    private bool RemoveHeader(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        var had = false;
        var done = Change(a, heap, (headers, name, _) =>
        {
            had = headers.Get(name) is not null;
            return headers.Remove(name);
        }, withValue: false, out result, out error);
        if (done) result = WebLingoValue.FromBoolean(had);
        return done;
    }

    /// <summary>A change to headers, given a name and, <paramref name="withValue"/>, a value, both strings; WL3001 if it cannot be made.</summary>
    private bool Change(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, Func<IWebLingoHeaderCollection, string, string?, WebLingoHeaderChange> change,
        bool withValue, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_headers.TryGetHostObject<IWebLingoHeaderCollection>(a[0], heap, out var headers, out error) || !TryName(a[1], heap, out var name, out error))
        {
            return false;
        }
        string? value = null;
        if (withValue)
        {
            if (a[2].Kind != WebLingoValueKind.String)
            {
                error = Invalid($"A header's value is a string, not {WebLingoValueFormatter.Describe(a[2], heap)}.");
                return false;
            }
            value = heap.GetString(a[2]);
        }
        error = change(headers, name, value) switch
        {
            WebLingoHeaderChange.Done => null,
            WebLingoHeaderChange.ReadOnly => Invalid("These headers cannot be changed: a request's never can, and a response's only until it is sent."),
            WebLingoHeaderChange.InvalidName => Invalid($"'{name}' is not a header's name."),
            _ => Invalid($"The value given the header '{name}' has a character that a header's value cannot have.")
        };
        return error is null;
    }

    private static bool TryName(WebLingoValue value, WebLingoHeap heap, out string name, out WebLingoRuntimeError? error)
    {
        (name, error) = ("", null);
        if (value.Kind == WebLingoValueKind.String)
        {
            name = heap.GetString(value);
            return true;
        }
        error = Invalid($"A header's name is a string, not {WebLingoValueFormatter.Describe(value, heap)}.");
        return false;
    }

    private static WebLingoRuntimeError Invalid(string message) => new(WebLingoDiagnosticCodes.InvalidOperation, message);

    /// <summary>
    /// A class as the compiler sees it, its members' built-ins registered in <paramref name="members"/>, in the order
    /// <see cref="WebLingoNativeClass.Builtins"/> gives them, as a native module's are (<c>WebLingoNativeModuleLoader</c>).
    /// </summary>
    private static WebLingoNativeClassInfo Info(WebLingoNativeClass owner, WebLingoBuiltinRegistry members)
    {
        var builtins = owner.Builtins(Prefix + "." + owner.Name);
        foreach (var builtin in builtins) members.Register(builtin);
        var next = 0;
        var constructor = Export("constructor", builtins[next++]);
        var methods = new List<WebLingoNativeExport>();
        foreach (var method in owner.Methods) methods.Add(Export(method.Name, builtins[next++]));
        var properties = new List<WebLingoNativePropertyInfo>();
        foreach (var property in owner.Properties)
        {
            var getter = Export(property.Name, builtins[next++]);
            properties.Add(new WebLingoNativePropertyInfo(property.Name, getter, property.Setter is null ? null : Export(property.Name, builtins[next++])));
        }
        WebLingoNativeExport? indexGetter = null, indexSetter = null, indexOptional = null;
        if (owner.Indexer is { } indexer)
        {
            indexGetter = Export("[]", builtins[next++]);
            if (indexer.Setter is not null) indexSetter = Export("[]=", builtins[next++]);
            indexOptional = Export("?[]", builtins[next++]);
        }
        return new WebLingoNativeClassInfo(owner.Name, constructor, methods, [])
        {
            Properties = properties,
            IndexGetter = indexGetter,
            IndexSetter = indexSetter,
            IndexOptional = indexOptional
        };
    }

    private static WebLingoNativeExport Export(string name, IWebLingoBuiltinFunction builtin) => new(name, builtin.Name, builtin.Arity);

    /// <summary>A request as its instance holds it: the request, and the heap's roots of its instance, its headers and its query.</summary>
    private sealed class RequestState(IWebLingoHttpRequest request)
    {
        public IWebLingoHttpRequest Request { get; } = request;
        public WebLingoValue[] Roots { get; } = new WebLingoValue[3];
    }

    /// <summary>A response as its instance holds it: the response, and the heap's roots of its instance and its headers.</summary>
    private sealed class ResponseState(IWebLingoHttpResponse response)
    {
        public IWebLingoHttpResponse Response { get; } = response;
        public WebLingoValue[] Roots { get; } = new WebLingoValue[2];
    }

    /// <summary>A built-in that makes an instance, given its classes.</summary>
    private sealed class Maker(string name, WebLingoNativeFunction make) : IWebLingoBuiltinFunction
    {
        public string Name => name;
        public int Arity => 2;

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            make(arguments, heap, out result, out error);
    }
}
