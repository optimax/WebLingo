using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting;

/// <summary>
/// A request and its response as a page and a <c>.wlg</c> route handler see them (specification, section 6.12, "The request
/// and the response in a page"; section 9, "M0.14 parts", part 3): instances of the native classes <c>Request</c> and
/// <c>Response</c>, which a page's <c>param request;</c> and <c>param response;</c> are given, and a handler's function
/// is called with. Their headers are instances of <c>Headers</c>, whose names are compared without regard to case. A
/// request's members: <c>method</c>, <c>path</c>, <c>scheme</c>, <c>host</c>, <c>query</c>, a map of its query's names to
/// their values, decoded, a name given twice having its last value, and <c>headers</c>, which cannot be changed. A
/// response's: <c>status</c>, an integer from 100 to 999, and <c>headers</c>, which can be changed until it is sent. A
/// header is read by indexing, <c>headers["Accept"]</c>, its values joined with <c>", "</c>, a missing one WL3007, as a
/// map's key is, <c>?[]</c> giving <c>null</c> for it; set by indexing, its value a string; and <c>has(name)</c>,
/// <c>add(name, value)</c> and <c>remove(name)</c>. A wrong status or header is WL3001 where it is set. A request's body
/// (section 6.12, "Streaming"; section 9, "M0.14.7 parts", part 4): <c>await request.text()</c>, the whole body as text,
/// UTF-8 unless its <c>Content-Type</c> names another charset, and <c>await request.form()</c>, an
/// <c>application/x-www-form-urlencoded</c> form's fields as a map, decoded as a query's are, a name given twice having its
/// last value; each read as it arrives, off the worker, once, and kept, so that each gives the same again. And
/// <c>await request.read()</c>, the body's next piece as bytes as it arrives, <c>null</c> at its end, for a body too large
/// to hold whole (section 9, "M0.14.7 parts", part 5): a body read in pieces cannot then be read whole, and one read whole
/// is given by <c>read()</c> as one piece. A form posted as <c>multipart/form-data</c> (section 6.12, "Uploaded files"):
/// <c>await request.form()</c> its fields, and <c>await request.files()</c> its files, a map of each file input's name to an
/// array of <c>UploadedFile</c>s, read once, as the body arrives (<see cref="WebLingoMultipartReader"/>), a file kept in
/// memory up to the site's threshold, then in a temporary file, removed when the request ends; a body read so cannot be
/// read otherwise, nor one read otherwise so. An <c>UploadedFile</c>'s members: <c>name</c>, its input's, <c>fileName</c>,
/// as the browser gave it, never a path, <c>type</c>, <c>size</c>, in bytes, and <c>bytes()</c>, <c>text()</c> and
/// <c>save(path)</c>, each a task, <c>save</c> writing through the script's files, as <c>std:fs</c> writes, a file there
/// replaced. A body larger than the site takes (<see cref="WebLingoUploads.MaxBodySize"/>) is an error where it is read.
/// Cookies (section 6.12, "Sessions and cookies"; <see cref="WebLingoCookies"/>): a request's <c>cookies</c>, a map of
/// their names to their values, made when first read; and a response's <c>setCookie(name, value, options)</c> and
/// <c>deleteCookie(name, options)</c>, written in WebLingo, their options optional, calling its own <c>__setCookie</c> and
/// <c>__deleteCookie</c>, a cookie set or deleted once the response is being sent WL3001, as a header is.
/// </summary>
internal sealed class WebLingoHttpObjects
{
    private const string Prefix = "http";

    /// <summary>
    /// <c>response.write(value)</c> and <c>response.writeLn(value)</c>, written in WebLingo, so that a value's text is the
    /// text <c>write</c> writes, an instance's <c>toString()</c> run, as an interpolation runs it, <c>null</c> as
    /// <c>null</c>; the text then given to the response's own <c>__write</c>, a name a program's members are unlikely to
    /// take (the owner's answer, 20 November 2026, "Console Module", A2).
    /// </summary>
    private const string WritingSource = WebLingoSources.Response;

    private readonly WebLingoNativeClass _request = new("Request");
    private readonly WebLingoNativeClass _response = new("Response");
    private readonly WebLingoNativeClass _headers = new("Headers");
    private readonly WebLingoNativeClass _uploaded = new("UploadedFile");

    public WebLingoHttpObjects()
    {
        _request
            .DeclareProperty("method", (a, heap, out r, out e) => Text(a, heap, s => s.Request.Method, out r, out e))
            .DeclareProperty("path", (a, heap, out r, out e) => Text(a, heap, s => s.Request.Path, out r, out e))
            .DeclareProperty("scheme", (a, heap, out r, out e) => Text(a, heap, s => s.Request.Scheme, out r, out e))
            .DeclareProperty("host", (a, heap, out r, out e) => Text(a, heap, s => s.Request.Host, out r, out e))
            .DeclareProperty("query", Query)
            .DeclareProperty("headers", (a, heap, out r, out e) => RequestHeaders(a, heap, out r, out e))
            .DeclareProperty("cookies", Cookies)
            .DeclareMethod("text", 0, BodyText)
            .DeclareMethod("form", 0, BodyForm)
            .DeclareMethod("files", 0, BodyFiles)
            .DeclareMethod("read", 0, BodyRead);
        _uploaded
            .DeclareProperty("name", (a, heap, out r, out e) => Uploaded(a, heap, part => part.Name, out r, out e))
            .DeclareProperty("fileName", (a, heap, out r, out e) => Uploaded(a, heap, part => part.FileName, out r, out e))
            .DeclareProperty("type", (a, heap, out r, out e) => Uploaded(a, heap, part => part.Type, out r, out e))
            .DeclareProperty("size", UploadedSize)
            .DeclareMethod("bytes", 0, UploadedBytes)
            .DeclareMethod("text", 0, UploadedText)
            .DeclareMethod("save", 1, UploadedSave);
        _response
            .DeclareProperty("status", Status, SetStatus)
            .DeclareProperty("headers", (a, heap, out r, out e) => ResponseHeaders(a, heap, out r, out e))
            .DeclareMethod("__write", 1, WriteText)
            .DeclareMethod("__setCookie", 3, SetCookie)
            .DeclareMethod("__deleteCookie", 2, DeleteCookie)
            .DeclareWebLingoMethods(WritingSource);
        _headers
            .DeclareIndexer(GetHeader, SetHeader)
            .DeclareMethod("has", 1, HasHeader)
            .DeclareMethod("add", 2, AddHeader)
            .DeclareMethod("remove", 1, RemoveHeader);

        var members = new WebLingoBuiltinRegistry();
        var request = Info(_request, members);
        var response = Info(_response, members);
        var headers = Info(_headers, members);
        var uploaded = Info(_uploaded, members);
        Members = members;
        Parameters =
        [
            new WebLingoHostParameter("request", Prefix + ".request", [request, headers, uploaded]),
            new WebLingoHostParameter("response", Prefix + ".response", [response, headers])
        ];
    }

    /// <summary>The built-ins that run the classes' members, the same for every request.</summary>
    public WebLingoBuiltinRegistry Members { get; }

    /// <summary>What the compiler gives a page's <c>param request;</c> and <c>param response;</c>, and a handler's caller's.</summary>
    public IReadOnlyList<WebLingoHostParameter> Parameters { get; }

    /// <summary>
    /// The built-ins that make a request's instances, given their classes, each made once, kept for the execution, the
    /// response's writing to <paramref name="output"/>, where the page's text goes, its body read within
    /// <paramref name="uploads"/>; and <paramref name="ending"/>, which its host disposes as the request ends, removing
    /// its uploads' temporary files.
    /// </summary>
    public IWebLingoBuiltinFunction[] For(IWebLingoHttpRequest request, IWebLingoHttpResponse response, IWebLingoTextOutput output,
        WebLingoUploads uploads, out IDisposable ending)
    {
        var requestState = new RequestState(request, uploads);
        var responseState = new ResponseState(response, output, string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase));
        ending = requestState;
        return
        [
            new Maker(Prefix + ".request", 3, (classes, heap, out made, out error) =>
            {
                requestState.Roots[4] = classes[2]; // UploadedFile's class, which files() makes its instances of.
                return Make(_request, requestState, classes, heap, requestState.Roots, request.Headers, out made, out error);
            }),
            new Maker(Prefix + ".response", 2, (classes, heap, out made, out error) =>
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

    /// <summary>The request's cookies, decoded, as a map of their names to their values, made when first read and the same map after.</summary>
    private bool Cookies(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_request.TryGetHostObject<RequestState>(a[0], heap, out var state, out error)) return false;
        if (state.Roots[5].Kind == WebLingoValueKind.Null
            && !TryMap(WebLingoCookies.Parse(state.Request.Headers.GetValues("Cookie")), heap, out state.Roots[5], out error)) return false;
        result = state.Roots[5];
        return true;
    }

    /// <summary><c>response.setCookie(name, value, options)</c>: its <c>Set-Cookie</c> added, giving <c>null</c>.</summary>
    private bool SetCookie(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_response.TryGetHostObject<ResponseState>(a[0], heap, out var state, out error)
            || !TryCookieName(a[1], heap, "setCookie", 3, out var name, out error)) return false;
        var value = heap.Held(a[2]);
        if (value.Kind != WebLingoValueKind.String)
        {
            error = new WebLingoArgument("setCookie", 1, 3, "the value", "a string").Wrong(value, heap);
            return false;
        }
        if (!TryCookieOptions(a[3], heap, "setCookie", 2, 3, WebLingoCookies.SetOptions, state.Secure, out var options, out error)) return false;
        var header = WebLingoCookies.Format(name, heap.GetString(value), options);
        var size = header.IndexOf(';', StringComparison.Ordinal); // Its name and its value, as sent.
        if (size > WebLingoCookies.MaxBytes)
        {
            error = Invalid($"The cookie '{name}' is {size} bytes, its name and its value as sent, more than the {WebLingoCookies.MaxBytes} a browser keeps of one.");
            return false;
        }
        if (WebLingoCookies.TrySet(state.Response.Headers, name, options, header)) return true;
        error = Invalid($"The cookie '{name}' cannot be set: the response has been sent.");
        return false;
    }

    /// <summary><c>response.deleteCookie(name, options)</c>: a <c>Set-Cookie</c> that deletes it, its path and domain those it was set with.</summary>
    private bool DeleteCookie(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_response.TryGetHostObject<ResponseState>(a[0], heap, out var state, out error)
            || !TryCookieName(a[1], heap, "deleteCookie", 2, out var name, out error)
            || !TryCookieOptions(a[2], heap, "deleteCookie", 1, 2, WebLingoCookies.DeleteOptions, state.Secure, out var options, out error)) return false;
        if (WebLingoCookies.TrySet(state.Response.Headers, name, options, WebLingoCookies.Deletion(name, options))) return true;
        error = Invalid($"The cookie '{name}' cannot be deleted: the response has been sent.");
        return false;
    }

    private static bool TryCookieName(WebLingoValue given, WebLingoHeap heap, string member, int of, out string name, out WebLingoRuntimeError? error)
    {
        (name, error) = ("", null);
        var argument = new WebLingoArgument(member, 0, of, "the name", "a cookie's name, a string of letters, digits and !#$%&'*+-.^_`|~");
        given = heap.Held(given);
        if (given.Kind != WebLingoValueKind.String)
        {
            error = argument.Wrong(given, heap);
            return false;
        }
        name = heap.GetString(given);
        if (WebLingoCookies.IsName(name)) return true;
        error = argument.Wrong(WebLingoValueFormatter.Quote(name));
        return false;
    }

    /// <summary>
    /// A cookie's options, a map, or <c>null</c> for none, each checked: <c>httpOnly</c> and <c>secure</c> Booleans,
    /// <c>sameSite</c> <c>"lax"</c>, <c>"strict"</c> or <c>"none"</c>, the last only for a secure cookie, <c>path</c> a path
    /// from <c>/</c>, <c>domain</c> a host's name, <c>maxAge</c> seconds, an integer, and <c>expires</c> a date; one it does
    /// not take an error naming the one it may have meant.
    /// </summary>
    private static bool TryCookieOptions(WebLingoValue given, WebLingoHeap heap, string member, int position, int of, string[] allowed, bool secure,
        out WebLingoCookies.Options options, out WebLingoRuntimeError? error)
    {
        options = new WebLingoCookies.Options(secure);
        error = null;
        given = heap.Held(given);
        if (given.Kind == WebLingoValueKind.Null) return true;
        var argument = new WebLingoArgument(member, position, of, "the options", "a map of the cookie's options");
        if (given.Kind != WebLingoValueKind.Map)
        {
            error = argument.Wrong(given, heap);
            return false;
        }
        for (var i = 0; i < heap.MapCount(given); i++)
        {
            var (key, entry) = heap.MapEntryAt(given, i);
            var name = key.Kind == WebLingoValueKind.String ? heap.GetString(key) : null;
            if (name is null || Array.IndexOf(allowed, name) < 0)
            {
                var shown = name is null ? WebLingoValueFormatter.Describe(key, heap) : $"'{name}'";
                error = Invalid($"{argument.Named} has {shown}, which is not an option of '{member}'{WebLingoCookies.Suggest(name, allowed)}");
                return false;
            }
            var value = heap.Held(entry);
            string? must = null;
            switch (name)
            {
                case "httpOnly" or "secure":
                    if (value.Kind != WebLingoValueKind.Boolean) must = "a Boolean";
                    else if (name == "httpOnly") options.HttpOnly = value.AsBoolean;
                    else options.Secure = value.AsBoolean;
                    break;
                case "sameSite":
                    var same = value.Kind == WebLingoValueKind.String ? heap.GetString(value).ToLowerInvariant() : "";
                    if (same is "lax" or "strict" or "none") options.SameSite = char.ToUpperInvariant(same[0]) + same[1..];
                    else must = "\"lax\", \"strict\" or \"none\"";
                    break;
                case "path":
                    if (value.Kind == WebLingoValueKind.String && heap.GetString(value) is var path && path.StartsWith('/')
                        && WebLingoCookies.IsPath(path)) options.Path = path;
                    else must = "a path from /, without ; or a character outside ASCII";
                    break;
                case "domain":
                    if (value.Kind == WebLingoValueKind.String && heap.GetString(value) is var domain && domain.TrimStart('.').Length > 0
                        && WebLingoCookies.IsDomain(domain)) options.Domain = domain;
                    else must = "a host's name, letters, digits, - and .";
                    break;
                case "maxAge":
                    if (value.Kind == WebLingoValueKind.Int64 && value.AsInt64 >= 0) options.MaxAge = value.AsInt64;
                    else must = "a number of seconds, an integer, 0 or more";
                    break;
                case "expires":
                    if (value.Kind == WebLingoValueKind.DateTime) options.Expires = value.UnixMilliseconds;
                    else must = "a date";
                    break;
            }
            if (must is null) continue;
            error = Invalid($"{argument.Named} must give {name} {must}, not {WebLingoValueFormatter.Describe(value, heap)}.");
            return false;
        }
        if (options.SameSite == "None" && !options.Secure)
        {
            error = Invalid($"A cookie whose sameSite is \"none\" must be secure: browsers refuse it otherwise; {argument.Named} gives secure false, or the request is not over HTTPS.");
            return false;
        }
        return true;
    }

    /// <summary>The query's names and values, decoded, as a map, made when first read and the same map after.</summary>
    private bool Query(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_request.TryGetHostObject<RequestState>(a[0], heap, out var state, out error)) return false;
        if (state.Roots[2].Kind == WebLingoValueKind.Null && !TryMap(Pairs(state.Request.QueryString), heap, out state.Roots[2], out error)) return false;
        result = state.Roots[2];
        return true;
    }

    /// <summary><c>request.text()</c>: a task giving the whole body as text, in its charset.</summary>
    private bool BodyText(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        ReadBody(a, heap, (byte[] body, RequestState state, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? failed) =>
        {
            made = WebLingoValue.Null;
            return TryDecode(body, state, out var text, out failed) && h.TryAllocateString(text, out made, out failed);
        }, out result, out error);

    /// <summary>
    /// <c>request.form()</c>: a task giving a form's fields as a map, an <c>application/x-www-form-urlencoded</c> one's or a
    /// <c>multipart/form-data</c> one's.
    /// </summary>
    private bool BodyForm(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_request.TryGetHostObject<RequestState>(a[0], heap, out var state, out error)) return false;
        var media = Media(state);
        if (media.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return ReadMultipart(a, heap, (WebLingoMultipartReader form, RequestState s, WebLingoHeap h, out WebLingoValue made,
                out WebLingoRuntimeError? failed) => TryMap(form.Fields, h, out made, out failed), out result, out error);
        }
        if (!media.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            error = Invalid($"request.form() reads a form sent as application/x-www-form-urlencoded or multipart/form-data, not as '{media}'.");
            return false;
        }
        return ReadBody(a, heap, (byte[] body, RequestState s, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? failed) =>
            TryMap(Pairs(WebLingoTextCodec.Decode(body, WebLingoCharset.Utf8)), h, out made, out failed), out result, out error);
    }

    /// <summary>
    /// <c>request.files()</c>: a task giving a <c>multipart/form-data</c> form's files, a map of each input's name to an
    /// array of <c>UploadedFile</c>s, made once; an <c>application/x-www-form-urlencoded</c> form's none, an empty map.
    /// </summary>
    private bool BodyFiles(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_request.TryGetHostObject<RequestState>(a[0], heap, out var state, out error)) return false;
        var media = Media(state);
        if (media.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return ReadBody(a, heap, (byte[] body, RequestState s, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? failed) =>
                h.TryAllocateMap([], 0, 0, out made, out failed), out result, out error);
        }
        if (!media.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            error = Invalid($"request.files() reads a form sent as multipart/form-data, not as '{media}'.");
            return false;
        }
        return ReadMultipart(a, heap, Files, out result, out error);
    }

    /// <summary>The request's media type, its <c>Content-Type</c> without its parameters.</summary>
    private static string Media(RequestState state)
    {
        var type = state.Request.Headers.Get("Content-Type") ?? "";
        var semicolon = type.IndexOf(';', StringComparison.Ordinal);
        return (semicolon < 0 ? type : type[..semicolon]).Trim();
    }

    /// <summary>A form's files as a map of each input's name to an array of <c>UploadedFile</c>s, made once, the same after.</summary>
    private bool Files(WebLingoMultipartReader form, RequestState state, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        (result, error) = (state.Roots[3], null);
        if (result.Kind != WebLingoValueKind.Null) return true;
        var made = new List<WebLingoValue>(); // Kept where the collector sees them until the map is whole.
        heap.AddRoots(made);
        try
        {
            var entries = new WebLingoValue[2 * form.FileNames.Count];
            for (var i = 0; i < form.FileNames.Count; i++)
            {
                var name = form.FileNames[i];
                var parts = form.Files[name];
                var instances = new WebLingoValue[parts.Count];
                for (var j = 0; j < parts.Count; j++)
                {
                    if (!_uploaded.TryMake(state.Roots[4], heap, out instances[j], out error)) return false;
                    made.Add(instances[j]);
                    if (!_uploaded.TrySetHostObject(instances[j], parts[j], heap, out error)) return false;
                }
                if (!heap.TryAllocateString(name, out entries[2 * i], out error)) return false;
                made.Add(entries[2 * i]);
                if (!heap.TryAllocateArray(instances, 0, instances.Length, out entries[2 * i + 1], out error)) return false;
                made.Add(entries[2 * i + 1]);
            }
            if (!heap.TryAllocateMap(entries, 0, form.FileNames.Count, out result, out error)) return false;
            state.Roots[3] = result;
            return true;
        }
        finally
        {
            heap.RemoveLastRoots(made);
        }
    }

    private delegate bool FormFinish(WebLingoMultipartReader form, RequestState state, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error);

    /// <summary>A task that reads the request's body as a <c>multipart/form-data</c> form, once, then gives what <paramref name="finish"/> makes of it.</summary>
    private bool ReadMultipart(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, FormFinish finish, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_request.TryGetHostObject<RequestState>(a[0], heap, out var state, out error)) return false;
        if (heap.Scheduler is not { } scheduler || heap.SchedulerHost is not { } host)
        {
            error = Invalid("This host cannot read a request's body.");
            return false;
        }
        if (state.ReadInPieces)
        {
            error = Invalid("The request's body is being read in pieces, with request.read(), and cannot be read as a form too.");
            return false;
        }
        if (WebLingoMultipartReader.Boundary(state.Request.Headers.Get("Content-Type") ?? "") is not { } boundary)
        {
            error = Invalid("The request's form is sent as multipart/form-data without a boundary.");
            return false;
        }
        return scheduler.TryWaitForHost<WebLingoMultipartReader?>(done => state.ReadForm(host, boundary, done), (WebLingoMultipartReader? form,
            WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? failed) =>
        {
            made = WebLingoValue.Null;
            if (form is null)
            {
                failed = Invalid(state.TooLarge is { } limit ? TooLargeMessage(limit) : "The request's body could not be read: its client has gone.");
                return false;
            }
            if (form.Error is { } why)
            {
                failed = Invalid(why);
                return false;
            }
            return finish(form, state, h, out made, out failed);
        }, out result, out error);
    }

    private static string TooLargeMessage(long limit) => $"The request's body is larger than the {limit} bytes this site takes.";

    /// <summary>An <c>UploadedFile</c>'s text member.</summary>
    private bool Uploaded(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, Func<UploadedPart, string> read, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        return _uploaded.TryGetHostObject<UploadedPart>(a[0], heap, out var part, out error) && heap.TryAllocateString(read(part), out result, out error);
    }

    private bool UploadedSize(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_uploaded.TryGetHostObject<UploadedPart>(a[0], heap, out var part, out error)) return false;
        result = WebLingoValue.FromInt64(part.Size);
        return true;
    }

    /// <summary><c>file.bytes()</c>: a task giving the file's bytes, read off the worker.</summary>
    private bool UploadedBytes(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        ReadUploaded(a, heap, (byte[] bytes, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? failed) =>
            h.TryAllocateBytes(bytes, out made, out failed), out result, out error);

    /// <summary><c>file.text()</c>: a task giving the file's text, its bytes read as UTF-8.</summary>
    private bool UploadedText(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        ReadUploaded(a, heap, (byte[] bytes, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? failed) =>
            h.TryAllocateString(WebLingoTextCodec.Decode(bytes, WebLingoCharset.Utf8), out made, out failed), out result, out error);

    private bool ReadUploaded(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, WebLingoOffWorkerFinish<byte[]> finish, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_uploaded.TryGetHostObject<UploadedPart>(a[0], heap, out var part, out error)) return false;
        if (heap.Scheduler is not { } scheduler)
        {
            error = Invalid("This host runs no operations off its worker.");
            return false;
        }
        return scheduler.TryRunOffWorker(() => Content(part), finish, Failed, out result, out error);
    }

    /// <summary>An upload's bytes, from memory or its temporary file; a <see cref="WebLingo.Platform.Abstractions.WebLingoFileSystemException"/> where they cannot be read.</summary>
    private static byte[] Content(UploadedPart part)
    {
        if (part.Bytes is { } bytes) return bytes;
        var file = part.File!;
        if (file.Length > Array.MaxLength)
        {
            throw new WebLingo.Platform.Abstractions.WebLingoFileSystemException(WebLingo.Platform.Abstractions.WebLingoFileErrorKind.Other,
                "the file is too large to hold whole");
        }
        var content = new byte[file.Length];
        var at = 0;
        while (at < content.Length)
        {
            var read = file.Read(at, content, at, content.Length - at);
            if (read <= 0) break;
            at += read;
        }
        return content;
    }

    /// <summary>
    /// <c>file.save(path)</c>: a task that writes the file at <paramref name="a"/>[1]'s path through the script's files,
    /// checked as <c>std:fs</c>'s are (section 7.13, "File capabilities"), a file there replaced; <c>null</c> once written.
    /// </summary>
    private bool UploadedSave(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_uploaded.TryGetHostObject<UploadedPart>(a[0], heap, out var part, out error)) return false;
        var given = heap.Held(a[1]);
        if (given.Kind != WebLingoValueKind.String)
        {
            error = new WebLingoArgument("save", 0, 1, "the path", "a string, a file's path").Wrong(given, heap);
            return false;
        }
        var path = heap.GetString(given);
        if (heap.FileSystem is not { } files || heap.Scheduler is not { } scheduler)
        {
            error = Invalid("This host gives scripts no files.");
            return false;
        }
        return scheduler.TryRunOffWorker(() =>
        {
            Write(part, files, path);
            return 0;
        }, (int _, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? failed) =>
        {
            (made, failed) = (WebLingoValue.Null, null);
            return true;
        }, exception => exception is WebLingo.Platform.Abstractions.WebLingoFileSystemException failure
            ? new WebLingoRuntimeError(failure.Kind == WebLingo.Platform.Abstractions.WebLingoFileErrorKind.NotAllowed
                ? WebLingoDiagnosticCodes.FilePermissionDenied
                : WebLingoDiagnosticCodes.IOFailure, $"'{path}' cannot be written: {failure.Message}.")
            : null, out result, out error);
    }

    /// <summary>Off the worker: an upload written at a path, from memory whole, or from its temporary file in pieces.</summary>
    private static void Write(UploadedPart part, WebLingo.Platform.Abstractions.IWebLingoFileSystem files, string path)
    {
        using var target = files.Open(path, WebLingo.Platform.Abstractions.WebLingoFileMode.WriteBytes, "utf-8");
        if (part.Bytes is { } bytes)
        {
            target.WriteBytes(bytes);
            return;
        }
        var buffer = new byte[64 * 1024];
        for (long at = 0; ; )
        {
            var read = part.File!.Read(at, buffer, 0, buffer.Length);
            if (read <= 0) break;
            target.WriteBytes(read == buffer.Length ? buffer : buffer[..read]);
            at += read;
        }
    }

    private static WebLingoRuntimeError? Failed(Exception exception) => exception is WebLingo.Platform.Abstractions.WebLingoFileSystemException failure
        ? new WebLingoRuntimeError(WebLingoDiagnosticCodes.IOFailure, $"The uploaded file cannot be read: {failure.Message}.")
        : null;

    /// <summary><c>request.read()</c>: a task giving the body's next piece as bytes, as it arrives, or <c>null</c> at its end.</summary>
    private bool BodyRead(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_request.TryGetHostObject<RequestState>(a[0], heap, out var state, out error)) return false;
        if (heap.Scheduler is not { } scheduler || heap.SchedulerHost is not { } host)
        {
            error = Invalid("This host cannot read a request's body.");
            return false;
        }
        if (state.PieceAwaited)
        {
            error = Invalid("request.read() is called again before the piece it gives has come: await each piece first.");
            return false;
        }
        if (state.ReadAsForm)
        {
            error = Invalid("The request's body has been read as a form, with request.form() or request.files(), and cannot be read again.");
            return false;
        }
        state.PieceAwaited = true;
        return scheduler.TryWaitForHost<byte[]?>(done => state.ReadPiece(host, done), (byte[]? piece, WebLingoHeap h, out WebLingoValue made,
            out WebLingoRuntimeError? failed) =>
        {
            state.PieceAwaited = false;
            (made, failed) = (WebLingoValue.Null, null);
            if (piece is null)
            {
                failed = Invalid(state.TooLarge is { } limit ? TooLargeMessage(limit) : "The request's body could not be read: its client has gone.");
                return false;
            }
            return piece.Length == 0 || h.TryAllocateBytes(piece, out made, out failed);
        }, out result, out error);
    }

    private delegate bool BodyFinish(byte[] body, RequestState state, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error);

    /// <summary>A task that reads the request's whole body, once, off the worker, then gives what <paramref name="finish"/> makes of it.</summary>
    private bool ReadBody(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, BodyFinish finish, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_request.TryGetHostObject<RequestState>(a[0], heap, out var state, out error)) return false;
        if (heap.Scheduler is not { } scheduler)
        {
            error = Invalid("This host cannot read a request's body.");
            return false;
        }
        if (state.ReadInPieces)
        {
            error = Invalid("The request's body is being read in pieces, with request.read(), and cannot be read whole too.");
            return false;
        }
        if (state.ReadAsForm)
        {
            error = Invalid("The request's body has been read as a form, with request.form() or request.files(), and cannot be read again.");
            return false;
        }
        if (heap.SchedulerHost is not { } host)
        {
            error = Invalid("This host cannot read a request's body.");
            return false;
        }
        return scheduler.TryWaitForHost<byte[]?>(done => state.ReadWhole(host, done), (byte[]? body, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? failed) =>
        {
            if (body is not null) return finish(body, state, h, out made, out failed);
            made = WebLingoValue.Null;
            failed = Invalid(state.TooLarge is { } limit ? TooLargeMessage(limit) : "The request's body could not be read: its client has gone.");
            return false;
        }, out result, out error);
    }

    /// <summary>
    /// The body as text, in the charset its <c>Content-Type</c> names, UTF-8 if none (<see cref="WebLingoTextCodec"/>);
    /// WL3001 for one the core does not read.
    /// </summary>
    private static bool TryDecode(byte[] body, RequestState state, out string text, out WebLingoRuntimeError? error)
    {
        (text, error) = ("", null);
        var encoding = WebLingoCharset.Utf8;
        foreach (var parameter in (state.Request.Headers.Get("Content-Type") ?? "").Split(';'))
        {
            var equals = parameter.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0 || !parameter[..equals].Trim().Equals("charset", StringComparison.OrdinalIgnoreCase)) continue;
            var charset = parameter[(equals + 1)..].Trim().Trim('"');
            if (!WebLingoTextCodec.TryFind(charset, out encoding))
            {
                error = Invalid($"The request's body is in the charset '{charset}', which this host cannot read.");
                return false;
            }
        }
        text = WebLingoTextCodec.Decode(body, encoding);
        return true;
    }

    /// <summary>A query's, or a form's, names and values, decoded, a name given twice having its last value.</summary>
    private static Dictionary<string, string> Pairs(string encoded)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in encoded.Split('&'))
        {
            if (part.Length == 0) continue;
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            var name = Decode(equals < 0 ? part : part[..equals]);
            pairs.Remove(name);
            pairs.Add(name, equals < 0 ? "" : Decode(part[(equals + 1)..]));
        }
        return pairs;
    }

    /// <summary>A map of strings to strings, its entries made where the collector keeps them.</summary>
    private static bool TryMap(Dictionary<string, string> pairs, WebLingoHeap heap, out WebLingoValue map, out WebLingoRuntimeError? error)
    {
        map = WebLingoValue.Null;
        error = null;
        var entries = new WebLingoValue[2 * pairs.Count];
        heap.AddRoots(entries);
        try
        {
            var at = 0;
            foreach (var (name, value) in pairs)
            {
                if (!heap.TryAllocateString(name, out entries[at++], out error) || !heap.TryAllocateString(value, out entries[at++], out error)) return false;
            }
            return heap.TryAllocateMap(entries, 0, pairs.Count, out map, out error);
        }
        finally
        {
            heap.RemoveLastRoots(entries);
        }
    }

    /// <summary>
    /// A query's, or a form's, name or value as the client meant it: <c>+</c> a space, and <c>%xx</c> a byte, the bytes read
    /// as UTF-8, a broken sequence U+FFFD; a <c>%</c> without two hexadecimal digits after it as it is.
    /// </summary>
    private static string Decode(string text)
    {
        if (text.IndexOf('%', StringComparison.Ordinal) < 0) return text.Replace('+', ' ');
        var bytes = new List<byte>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '%' && i + 2 < text.Length && Hex(text[i + 1]) is >= 0 and var high && Hex(text[i + 2]) is >= 0 and var low)
            {
                bytes.Add((byte)(high * 16 + low));
                i += 2;
                continue;
            }
            var start = i;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length) i++;
            bytes.AddRange(WebLingoTextCodec.Encode(c == '+' ? " " : text.Substring(start, i - start + 1), WebLingoCharset.Utf8));
        }
        return WebLingoTextCodec.Decode(bytes.ToArray(), WebLingoCharset.Utf8);
    }

    private static int Hex(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    private bool Status(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_response.TryGetHostObject<ResponseState>(a[0], heap, out var state, out error)) return false;
        result = WebLingoValue.FromInt64(state.Response.StatusCode);
        return true;
    }

    /// <summary>The response's <c>__write(text)</c>: a string, as it stands, where the page's text goes, giving <c>null</c>.</summary>
    private bool WriteText(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_response.TryGetHostObject<ResponseState>(a[0], heap, out var state, out error)) return false;
        if (a[1].Kind != WebLingoValueKind.String)
        {
            error = Invalid($"A response writes a string, not {WebLingoValueFormatter.Describe(a[1], heap)}.");
            return false;
        }
        state.Output.Write(heap.GetString(a[1]));
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
        // Its methods written in WebLingo, read as a native module's are; a source of the host's own that does not parse is its fault.
        var written = new List<WebLingo.Language.WebLingoMethodSyntax>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methods) taken.Add(method.Name);
        foreach (var property in properties) taken.Add(property.Name);
        foreach (var source in owner.WebLingoMethods)
        {
            if (!WebLingoNativeClassInfo.TryParseMethods(source, taken, out var parsed, out var wrong))
            {
                throw new InvalidOperationException($"The class '{owner.Name}': {wrong}");
            }
            written.AddRange(parsed);
        }
        return new WebLingoNativeClassInfo(owner.Name, constructor, methods, [])
        {
            WebLingoMethods = written,
            Properties = properties,
            IndexGetter = indexGetter,
            IndexSetter = indexSetter,
            IndexOptional = indexOptional
        };
    }

    private static WebLingoNativeExport Export(string name, IWebLingoBuiltinFunction builtin) => new(name, builtin.Name, builtin.Arity);

    /// <summary>A request as its instance holds it: the request, and the heap's roots of its instance, its headers and its query.</summary>
    /// <summary>
    /// A request as its instance holds it: the request, the heap's roots of its instance, its headers and its query, and its
    /// body once read. Its fields are the worker's alone: the body's reading runs off the worker, its pieces touched by
    /// nothing else, and hands its end over through the scheduler host, which posts it to the worker.
    /// </summary>
    private sealed class RequestState(IWebLingoHttpRequest request, WebLingoUploads uploads) : IDisposable
    {
        private List<Action<byte[]?>>? _waiting;
        private byte[]? _body;
        private bool _failed;
        private bool _wholeGiven;
        private long _read;
        private WebLingoMultipartReader? _form;
        private bool _formFailed;
        private List<Action<WebLingoMultipartReader?>>? _formWaiting;

        public IWebLingoHttpRequest Request { get; } = request;

        /// <summary>The heap's roots of its instance, its headers, its query, its files' map, UploadedFile's class and its cookies.</summary>
        public WebLingoValue[] Roots { get; } = new WebLingoValue[6];

        /// <summary>Whether its body has been read as a form, as it arrived, and so cannot be read otherwise.</summary>
        public bool ReadAsForm { get; private set; }

        /// <summary>The site's limit, where its body passed it, read so far; null otherwise.</summary>
        public long? TooLarge { get; private set; }

        /// <summary>Its uploads' temporary files removed: by its host, as it ends.</summary>
        public void Dispose()
        {
            _form?.Dispose();
            _form = null;
        }

        /// <summary>Off the worker: <paramref name="count"/> more of its body read; false, the limit kept, once it passes the site's.</summary>
        private bool Within(int count)
        {
            _read += count;
            if (_read <= uploads.MaxBodySize) return true;
            TooLarge = uploads.MaxBodySize;
            return false;
        }

        /// <summary>
        /// On the worker: gives <paramref name="done"/> the body read as a <c>multipart/form-data</c> form, its parts divided by
        /// <paramref name="boundary"/>, once, kept; null if it cannot be read: read off the worker as it arrives, or from the
        /// body already read whole.
        /// </summary>
        public void ReadForm(WebLingo.Platform.Abstractions.IWebLingoSchedulerHost host, string boundary, Action<WebLingoMultipartReader?> done)
        {
            if (_form is not null || _formFailed)
            {
                done(_form);
                return;
            }
            if (_body is not null || _failed || _waiting is not null)
            {
                ReadWhole(host, body =>
                {
                    if (body is not null && _form is null)
                    {
                        _form = Reader(boundary);
                        if (_form.Feed(body, body.Length)) _form.End();
                    }
                    done(body is null ? null : _form);
                });
                return;
            }
            var first = _formWaiting is null;
            (_formWaiting ??= []).Add(done);
            if (!first) return;
            ReadAsForm = true;
            var reader = Reader(boundary);
            var buffer = new byte[16 * 1024];
            void Next() => Request.Body.ReadAsync(buffer, 0, buffer.Length, count =>
            {
                if (count > 0)
                {
                    if (!Within(count)) count = -1;
                    else if (reader.Feed(buffer, count))
                    {
                        Next();
                        return;
                    }
                }
                else if (count == 0)
                {
                    reader.End();
                }
                var arrived = count < 0 ? null : reader;
                if (arrived is null) reader.Dispose();
                host.Post(() => FormArrived(arrived));
            });
            Next();
        }

        private WebLingoMultipartReader Reader(string boundary) => new(boundary, uploads.InMemory, uploads.MaxFiles, uploads.TemporaryFiles);

        /// <summary>On the worker: the form, or null if it could not be read, given to everything that waits for it.</summary>
        private void FormArrived(WebLingoMultipartReader? form)
        {
            (_form, _formFailed) = (form, form is null);
            var waiting = _formWaiting!;
            _formWaiting = null;
            foreach (var done in waiting) done(_form);
        }

        /// <summary>Whether <c>read()</c> has read a piece of the body, which then cannot be read whole.</summary>
        public bool ReadInPieces { get; private set; }

        /// <summary>Whether a piece <c>read()</c> asked for has yet to come.</summary>
        public bool PieceAwaited { get; set; }

        /// <summary>
        /// On the worker: gives <paramref name="done"/> the body's next piece, an empty one at its end, or null if it cannot be
        /// read: read off the worker, as it arrives, then posted back; or, for a body read whole, the whole of it, once.
        /// </summary>
        public void ReadPiece(WebLingo.Platform.Abstractions.IWebLingoSchedulerHost host, Action<byte[]?> done)
        {
            if (_body is not null || _failed || _waiting is not null)
            {
                ReadWhole(host, body =>
                {
                    var given = _wholeGiven;
                    _wholeGiven = true;
                    done(body is null ? null : given ? [] : body);
                });
                return;
            }
            ReadInPieces = true;
            var buffer = new byte[16 * 1024];
            Request.Body.ReadAsync(buffer, 0, buffer.Length, count => done(count < 0 || count > 0 && !Within(count) ? null : buffer[..count]));
        }

        /// <summary>
        /// On the worker: gives <paramref name="done"/> the whole body once it has arrived, read once, then kept; null if it
        /// cannot be read.
        /// </summary>
        public void ReadWhole(WebLingo.Platform.Abstractions.IWebLingoSchedulerHost host, Action<byte[]?> done)
        {
            if (_body is not null || _failed)
            {
                done(_body);
                return;
            }
            var first = _waiting is null;
            (_waiting ??= []).Add(done);
            if (first) ReadNext(host, new List<byte[]>(), new byte[16 * 1024]);
        }

        /// <summary>Off the worker: reads the body's next piece, and at its end posts what was read to the worker.</summary>
        private void ReadNext(WebLingo.Platform.Abstractions.IWebLingoSchedulerHost host, List<byte[]> pieces, byte[] buffer) =>
            Request.Body.ReadAsync(buffer, 0, buffer.Length, count =>
            {
                if (count > 0 && Within(count))
                {
                    pieces.Add(buffer[..count]);
                    ReadNext(host, pieces, buffer);
                    return;
                }
                if (count > 0) count = -1; // Larger than the site takes.
                byte[]? whole = null;
                if (count == 0)
                {
                    var length = 0;
                    foreach (var piece in pieces) length += piece.Length;
                    whole = new byte[length];
                    var at = 0;
                    foreach (var piece in pieces)
                    {
                        piece.CopyTo(whole, at);
                        at += piece.Length;
                    }
                }
                host.Post(() => Arrived(whole));
            });

        /// <summary>On the worker: the body, or null if it could not be read, given to everything that waits for it.</summary>
        private void Arrived(byte[]? whole)
        {
            (_body, _failed) = (whole, whole is null);
            var waiting = _waiting!;
            _waiting = null;
            foreach (var done in waiting) done(_body);
        }
    }

    /// <summary>
    /// A response as its instance holds it: the response, the heap's roots of its instance and its headers, and whether its
    /// request came over HTTPS, its cookies then secure unless they say otherwise.
    /// </summary>
    private sealed class ResponseState(IWebLingoHttpResponse response, IWebLingoTextOutput output, bool secure)
    {
        public IWebLingoHttpResponse Response { get; } = response;
        public bool Secure { get; } = secure;
        public IWebLingoTextOutput Output { get; } = output;
        public WebLingoValue[] Roots { get; } = new WebLingoValue[2];
    }

    /// <summary>A built-in that makes an instance, given its classes.</summary>
    private sealed class Maker(string name, int arity, WebLingoNativeFunction make) : IWebLingoBuiltinFunction
    {
        public string Name => name;
        public int Arity => arity;

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            make(arguments, heap, out result, out error);
    }
}
