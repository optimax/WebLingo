using System.Globalization;
using System.Text;
using WebLingo.Core;
using WebLingo.Hosting.Abstractions;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting;

/// <summary>What opening a session came to (<see cref="IWebLingoSessionStore.Open"/>).</summary>
public enum WebLingoSessionOpened { Found, NotFound, Waiting, TimedOut }

/// <summary>
/// What opening a session came to: found, with its values, each a name and its value's JSON text; not found, there being no
/// such session, or none any more; waiting for its lock, another request holding it; or, its lock waited for too long,
/// timed out.
/// </summary>
public sealed record WebLingoSessionOpening(WebLingoSessionOpened Kind, IReadOnlyDictionary<string, string>? Values = null)
{
    public static WebLingoSessionOpening NotFound { get; } = new(WebLingoSessionOpened.NotFound);
    public static WebLingoSessionOpening Waiting { get; } = new(WebLingoSessionOpened.Waiting);
    public static WebLingoSessionOpening TimedOut { get; } = new(WebLingoSessionOpened.TimedOut);
}

/// <summary>
/// Where a site keeps its sessions (specification, section 6.12, "Sessions and cookies"): each by its id, with its values,
/// each a name and its value's JSON text, and a lock, which one request at a time holds, the others that ask for it waiting
/// in their order, each for <see cref="LockTimeout"/> at most; a session nothing has used for the site's timeout ended.
/// Every worker uses one store, from its own thread, and its waits end on others: so a store is an adapter's, which
/// synchronizes its own threads, the portable core taking no lock.
/// </summary>
public interface IWebLingoSessionStore
{
    /// <summary>How long a request waits for a session's lock, another request holding it, before it goes on without it.</summary>
    TimeSpan LockTimeout { get; }

    /// <summary>
    /// Opens the session <paramref name="id"/> for a request, its lock taken where <paramref name="locked"/>: found, or not
    /// found, at once; or waiting, another request holding its lock, <paramref name="later"/> then called once, from another
    /// thread, with what the wait came to: found, its lock the request's, not found, the session ended meanwhile, or timed
    /// out.
    /// </summary>
    WebLingoSessionOpening Open(string id, bool locked, Action<WebLingoSessionOpening> later);

    /// <summary>Makes the session <paramref name="id"/>, without values, its lock its maker's; false where one has that id.</summary>
    bool Create(string id);

    /// <summary>
    /// Gives up a session's lock, its values replaced by <paramref name="values"/> unless they are null, the first request
    /// that waits for it given it.
    /// </summary>
    void Close(string id, IReadOnlyDictionary<string, string>? values);

    /// <summary>Gives a session, its lock held still, the id <paramref name="renewed"/>, those waiting for it finding none; false where one has that id.</summary>
    bool Renew(string id, string renewed);

    /// <summary>Ends a session, its lock with it, those waiting for it finding none.</summary>
    void Remove(string id);
}

/// <summary>
/// A request's session (specification, section 6.12, "Sessions and cookies"; "Sessions and cookies, parts", part 2): its id
/// the request's cookie's, <see cref="CookieName"/>, opened, and its lock taken, before the page runs, unless the request
/// declares that it only reads it (<see cref="ReadOnly"/>); its values a copy, each read made anew from its JSON text, each
/// set checked and written as JSON (the owner's answer, 24 November 2026, part 2's A1 (b)); a session made, and its cookie
/// sent, once a value is set in it; and its lock given up, its values written back where they changed, by
/// <c>release()</c>, or as the request ends. Its worker's alone.
/// </summary>
internal sealed class WebLingoSession
{
    /// <summary>The session's cookie, which holds its id, until the browser closes.</summary>
    public const string CookieName = "WASPSESSIONID";

    /// <summary>What a session keeps, as its errors say it.</summary>
    private const string Keeps = "it keeps null, Booleans, integers, numbers, strings, and arrays and maps of them, a map's keys strings, as JSON holds them";

    private readonly IWebLingoHttpResponse _response;
    private readonly IWebLingoCrypto? _crypto;
    private readonly bool _secure;
    private Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private bool _locked;
    private bool _changed;
    private bool _released;
    private bool _lockFailed;

    public WebLingoSession(IWebLingoSessionStore store, IWebLingoHttpRequest request, IWebLingoHttpResponse response, IWebLingoCrypto? crypto,
        string? readOnly)
    {
        Store = store;
        _response = response;
        _crypto = crypto;
        _secure = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        ReadOnly = readOnly;
        if (WebLingoCookies.Parse(request.Headers.GetValues("Cookie")).TryGetValue(CookieName, out var id) && IsId(id)) Id = id;
    }

    public IWebLingoSessionStore Store { get; }

    /// <summary>The session's id: the request's cookie's, until it is opened, then the session's, or null while there is none.</summary>
    public string? Id { get; private set; }

    /// <summary>Where the request declares that it only reads its session, as an error names it; null where it does not.</summary>
    public string? ReadOnly { get; }

    /// <summary>A session's id: 128 random bits, as 32 hexadecimal digits in lower case.</summary>
    private static bool IsId(string text)
    {
        if (text.Length != 32) return false;
        foreach (var c in text)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))) return false;
        }
        return true;
    }

    /// <summary>The session as opening it came to, before the page runs.</summary>
    public void Opened(WebLingoSessionOpening opening)
    {
        switch (opening.Kind)
        {
            case WebLingoSessionOpened.Found:
                _values = new Dictionary<string, string>(opening.Values!, StringComparer.Ordinal);
                _locked = ReadOnly is null;
                break;
            case WebLingoSessionOpened.NotFound:
                Id = null; // An id the site does not know is never taken for a new session's.
                break;
            default:
                (Id, _lockFailed) = (null, true);
                break;
        }
    }

    /// <summary>As the request ends: its lock given up, its values written back where they changed.</summary>
    public void End()
    {
        if (!_locked) return;
        Store.Close(Id!, _changed ? new Dictionary<string, string>(_values, StringComparer.Ordinal) : null);
        _locked = false;
    }

    /// <summary>Whether the page can read its session; a <c>SessionLockError</c> where its lock was waited for too long.</summary>
    public bool TryCheckUsable(out WebLingoRuntimeError? error)
    {
        error = null;
        if (!_lockFailed) return true;
        var seconds = Store.LockTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.SessionLocked,
            $"The session cannot be read: this request waited {seconds} seconds for its lock, the most this site waits, while another request of the session held it.");
        return false;
    }

    /// <summary>The value of <paramref name="name"/>, a copy made anew from its JSON text; WL3007 where it has none.</summary>
    public bool TryGet(string name, WebLingoHeap heap, out WebLingoValue value, out WebLingoRuntimeError? error)
    {
        value = WebLingoValue.Null;
        if (!TryCheckUsable(out error)) return false;
        if (_values.TryGetValue(name, out var json)) return WebLingoJsonParser.TryParse(json, heap, out value, out error);
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NoSuchKey, $"The session has no '{name}'.");
        return false;
    }

    public bool Has(string name) => _values.ContainsKey(name);

    /// <summary>Sets <paramref name="name"/> to a copy of <paramref name="value"/>, checked, the session made where there is none.</summary>
    public bool TrySet(string name, WebLingoValue value, WebLingoHeap heap, out WebLingoRuntimeError? error)
    {
        if (!TryCheckWritable(out error)) return false;
        if (!WebLingoSessionValues.TryWrite(value, heap, out var json, out var wrong))
        {
            error = Invalid($"The session cannot keep '{name}': {Keeps}; not {wrong}.");
            return false;
        }
        if (Id is null && !TryMake(out error)) return false;
        _values[name] = json;
        _changed = true;
        return true;
    }

    /// <summary>Removes <paramref name="name"/>, giving whether it was there.</summary>
    public bool TryRemove(string name, out bool had, out WebLingoRuntimeError? error)
    {
        had = false;
        if (!TryCheckWritable(out error)) return false;
        had = _values.Remove(name);
        _changed |= had;
        return true;
    }

    /// <summary><c>release()</c>: its lock given up now, its values written back where they changed; a change after it an error.</summary>
    public bool TryRelease(out WebLingoRuntimeError? error)
    {
        if (!TryCheckUsable(out error)) return false;
        End();
        _released = true;
        return true;
    }

    /// <summary><c>renew()</c>: the session given a new id, its values and its lock kept, its cookie sent anew; nothing where there is none.</summary>
    public bool TryRenew(out WebLingoRuntimeError? error)
    {
        if (!TryCheckWritable(out error) || Id is null) return error is null;
        if (_response.Headers.IsReadOnly)
        {
            error = Invalid("The session cannot be renewed: the response has been sent, and the session's cookie with it.");
            return false;
        }
        if (!TryNewId(out var renewed, out error)) return false;
        for (var tries = 0; !Store.Renew(Id, renewed); tries++)
        {
            if (tries == 3 || !TryNewId(out renewed, out error)) return false;
        }
        Id = renewed;
        SetCookie(renewed);
        return true;
    }

    /// <summary><c>end()</c>: the session removed, its cookie deleted where the response has not been sent; one set after it a new one.</summary>
    public bool TryEnd(out WebLingoRuntimeError? error)
    {
        if (!TryCheckWritable(out error) || Id is null) return error is null;
        Store.Remove(Id);
        WebLingoCookies.TrySet(_response.Headers, CookieName, Options(), WebLingoCookies.Deletion(CookieName, Options()));
        (Id, _locked, _changed, _values) = (null, false, false, new Dictionary<string, string>(StringComparer.Ordinal));
        return true;
    }

    private bool TryCheckWritable(out WebLingoRuntimeError? error)
    {
        if (!TryCheckUsable(out error)) return false;
        if (ReadOnly is not null)
        {
            error = Invalid($"The session cannot be changed: this request only reads it, as {ReadOnly} declares.");
            return false;
        }
        if (!_released) return true;
        error = Invalid("The session cannot be changed: request.session.release() has given it up.");
        return false;
    }

    /// <summary>A new session, its lock the request's, its cookie set on the response, which must not have been sent.</summary>
    private bool TryMake(out WebLingoRuntimeError? error)
    {
        if (_response.Headers.IsReadOnly)
        {
            error = Invalid("The session cannot be made: the response has been sent, and the session's cookie with it.");
            return false;
        }
        if (!TryNewId(out var id, out error)) return false;
        for (var tries = 0; !Store.Create(id); tries++)
        {
            if (tries == 3 || !TryNewId(out id, out error)) return false;
        }
        (Id, _locked) = (id, true);
        SetCookie(id);
        return true;
    }

    private void SetCookie(string id) => WebLingoCookies.TrySet(_response.Headers, CookieName, Options(), WebLingoCookies.Format(CookieName, id, Options()));

    /// <summary>The session's cookie's options: <c>HttpOnly</c>, <c>SameSite=Lax</c>, <c>Path=/</c>, and <c>Secure</c> over HTTPS, until the browser closes.</summary>
    private WebLingoCookies.Options Options() => new(_secure);

    /// <summary>A new id, 128 bits from the platform's cryptography, as 32 hexadecimal digits.</summary>
    private bool TryNewId(out string id, out WebLingoRuntimeError? error)
    {
        (id, error) = ("", null);
        if (_crypto is null)
        {
            error = Invalid("This host makes no sessions: it has no cryptography to make their ids with.");
            return false;
        }
        Span<byte> bits = stackalloc byte[16];
        _crypto.FillRandom(bits);
        var text = new StringBuilder(32);
        foreach (var b in bits) text.Append("0123456789abcdef"[b >> 4]).Append("0123456789abcdef"[b & 15]);
        id = text.ToString();
        return true;
    }

    private static WebLingoRuntimeError Invalid(string message) => new(WebLingoDiagnosticCodes.InvalidOperation, message);
}

/// <summary>
/// A session's values as JSON (specification, section 6.12, "Sessions and cookies"): <c>null</c>, Booleans, integers, finite
/// numbers, strings, and arrays and maps of them, a map's keys strings, written so that <see cref="WebLingoJsonParser"/>
/// reads each back as the same kind of value, a number as one, <c>5.0</c>; anything else, a value that holds itself, and one
/// nested more deeply than JSON is read, refused.
/// </summary>
internal static class WebLingoSessionValues
{
    public static bool TryWrite(WebLingoValue value, WebLingoHeap heap, out string json, out string? wrong)
    {
        var text = new StringBuilder();
        wrong = Write(value, heap, text, [], 0);
        json = text.ToString();
        return wrong is null;
    }

    /// <summary>Writes a value; what it cannot keep, as its error names it, or null.</summary>
    private static string? Write(WebLingoValue value, WebLingoHeap heap, StringBuilder text, HashSet<int> open, int depth)
    {
        switch (value.Kind)
        {
            case WebLingoValueKind.Null:
                text.Append("null");
                return null;
            case WebLingoValueKind.Boolean:
                text.Append(value.AsBoolean ? "true" : "false");
                return null;
            case WebLingoValueKind.Int64:
                text.Append(value.AsInt64.ToString(CultureInfo.InvariantCulture));
                return null;
            case WebLingoValueKind.Binary64:
                if (!double.IsFinite(value.AsBinary64)) return Held(depth, WebLingoValueFormatter.Describe(value, heap));
                text.Append(WebLingoValueFormatter.Format(value)); // As write writes it, 5.0 a number, so that it reads back as one.
                return null;
            case WebLingoValueKind.String:
                Quote(heap.GetString(value), text);
                return null;
            case WebLingoValueKind.Array or WebLingoValueKind.Map when !value.HasClass:
            {
                if (depth >= WebLingoJsonParser.MaxDepth) return Held(depth, $"one nested more than {WebLingoJsonParser.MaxDepth} deep");
                if (!open.Add(value.Handle)) return Held(depth, "itself");
                if (value.Kind == WebLingoValueKind.Array)
                {
                    text.Append('[');
                    var items = heap.GetArray(value);
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (i > 0) text.Append(',');
                        if (Write(items[i], heap, text, open, depth + 1) is { } wrong) return wrong;
                    }
                    text.Append(']');
                }
                else
                {
                    text.Append('{');
                    for (var i = 0; i < heap.MapCount(value); i++)
                    {
                        var (key, entry) = heap.MapEntryAt(value, i);
                        if (key.Kind != WebLingoValueKind.String) return Held(depth, $"a map whose key is {WebLingoValueFormatter.Describe(key, heap)}");
                        if (i > 0) text.Append(',');
                        Quote(heap.GetString(key), text);
                        text.Append(':');
                        if (Write(entry, heap, text, open, depth + 1) is { } wrong) return wrong;
                    }
                    text.Append('}');
                }
                open.Remove(value.Handle);
                return null;
            }
            case WebLingoValueKind.Array or WebLingoValueKind.Map:
                // Its class's, which a copy would not keep.
                var kind = value.Kind == WebLingoValueKind.Array ? "an array" : "a map";
                return Held(depth, $"{kind} of the class {heap.ClassName(heap.ClassOfValue(value))}");
            default:
                return Held(depth, WebLingoValueFormatter.Describe(value, heap));
        }
    }

    /// <summary>What cannot be kept, as the value set, or one it holds.</summary>
    private static string Held(int depth, string what) => depth == 0 ? what : "one that holds " + what;

    /// <summary>A string as JSON writes it: quoted, its quotes, backslashes and control characters escaped.</summary>
    private static void Quote(string value, StringBuilder text)
    {
        text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                default:
                    if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else text.Append(c);
                    break;
            }
        }
        text.Append('"');
    }
}
