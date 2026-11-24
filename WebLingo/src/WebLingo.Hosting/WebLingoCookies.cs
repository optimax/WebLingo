using System.Globalization;
using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting;

/// <summary>
/// Cookies as a page and a handler see them (specification, section 6.12, "Sessions and cookies"; "Sessions and cookies,
/// parts", part 1): a request's, read from its <c>Cookie</c> headers, a name given twice its first value, as the browser
/// sends the most particular first, a value decoded, <c>%xx</c> a byte of UTF-8, its quotes, where it has them, taken
/// away; and a response's <c>Set-Cookie</c>, its value encoded where it must be, every byte RFC 6265 does not allow in
/// one, and <c>%</c>, as <c>%xx</c>, secure by default, <c>HttpOnly</c>, <c>SameSite=Lax</c>, <c>Path=/</c>, and
/// <c>Secure</c> over HTTPS, each changed by its option. A cookie set again, of the same name, path and domain, replaces
/// the one set before in the same response, a deletion among them.
/// </summary>
internal static class WebLingoCookies
{
    /// <summary>A cookie's name and value, together, the most a browser keeps of one.</summary>
    public const int MaxBytes = 4096;

    /// <summary>The options <c>setCookie</c> takes, as a message lists them.</summary>
    public static readonly string[] SetOptions = ["httpOnly", "secure", "sameSite", "path", "domain", "maxAge", "expires"];

    /// <summary>The options <c>deleteCookie</c> takes.</summary>
    public static readonly string[] DeleteOptions = ["path", "domain"];

    /// <summary>A request's cookies, from its <c>Cookie</c> headers, by name, a name given twice its first value.</summary>
    public static Dictionary<string, string> Parse(IReadOnlyList<string> headers)
    {
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            foreach (var pair in header.Split(';'))
            {
                var equals = pair.IndexOf('=', StringComparison.Ordinal);
                if (equals < 0) continue;
                var name = pair[..equals].Trim(' ', '\t');
                if (!IsName(name) || cookies.ContainsKey(name)) continue;
                var value = pair[(equals + 1)..].Trim(' ', '\t');
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
                cookies.Add(name, Decode(value));
            }
        }
        return cookies;
    }

    /// <summary>Whether a text is a cookie's name: a token, RFC 9110's, letters, digits and <c>!#$%&amp;'*+-.^_`|~</c>.</summary>
    public static bool IsName(string name)
    {
        if (name.Length == 0) return false;
        foreach (var c in name)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '!' or '#' or '$' or '%' or '&' or '\''
                or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~')) return false;
        }
        return true;
    }

    /// <summary>Whether a text is a cookie's path: from <c>/</c>, of ASCII's printable characters but <c>;</c>.</summary>
    public static bool IsPath(string path)
    {
        if (!path.StartsWith('/')) return false;
        foreach (var c in path)
        {
            if (c is < ' ' or > '~' or ';') return false;
        }
        return true;
    }

    /// <summary>Whether a text is a cookie's domain: a host's name, letters, digits, <c>-</c> and <c>.</c>, more than dots.</summary>
    public static bool IsDomain(string domain)
    {
        if (domain.TrimStart('.').Length == 0) return false;
        foreach (var c in domain)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '.')) return false;
        }
        return true;
    }

    /// <summary>
    /// An option's error's end: the option it may have meant, one or two letters from it, <c>; did you mean httpOnly?</c>, or
    /// else every option it takes.
    /// </summary>
    public static string Suggest(string? given, string[] options)
    {
        var (best, distance) = ("", int.MaxValue);
        if (given is not null)
        {
            foreach (var known in options)
            {
                var apart = Distance(given.ToLowerInvariant(), known.ToLowerInvariant());
                if (apart < distance) (best, distance) = (known, apart);
            }
        }
        if (given is not null && distance <= Math.Max(1, Math.Min(2, given.Length / 3))) return $"; did you mean {best}?";
        return options.Length == 1 ? $", whose one option is {options[0]}." : $", whose options are {string.Join(", ", options[..^1])} and {options[^1]}.";
    }

    /// <summary>The fewest letters added, removed or changed, or two beside each other swapped, that make one text the other.</summary>
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j], d[i, j - 1]) + 1, d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
    }

    /// <summary>A cookie's value as sent: each byte of its UTF-8 RFC 6265 does not allow in one, and <c>%</c>, as <c>%xx</c>.</summary>
    public static string Encode(string value)
    {
        var text = new StringBuilder(value.Length);
        foreach (var b in WebLingoTextCodec.Encode(value, WebLingoCharset.Utf8))
        {
            if (b is 0x21 or (>= 0x23 and <= 0x2B and not 0x25) or (>= 0x2D and <= 0x3A) or (>= 0x3C and <= 0x5B) or (>= 0x5D and <= 0x7E))
                text.Append((char)b);
            else
                text.Append('%').Append("0123456789ABCDEF"[b >> 4]).Append("0123456789ABCDEF"[b & 15]);
        }
        return text.ToString();
    }

    /// <summary>A cookie's value as it was meant: <c>%xx</c> a byte, the bytes read as UTF-8; a <c>%</c> without two hexadecimal digits after it as it is.</summary>
    public static string Decode(string value)
    {
        if (value.IndexOf('%', StringComparison.Ordinal) < 0) return value;
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length && Hex(value[i + 1]) is >= 0 and var high && Hex(value[i + 2]) is >= 0 and var low)
            {
                bytes.Add((byte)(high * 16 + low));
                i += 2;
                continue;
            }
            var start = i;
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length) i++;
            bytes.AddRange(WebLingoTextCodec.Encode(value.Substring(start, i - start + 1), WebLingoCharset.Utf8));
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

    /// <summary>A cookie's options, its defaults those of a response to a request over HTTPS where <paramref name="secure"/>.</summary>
    public sealed class Options(bool secure)
    {
        public bool HttpOnly { get; set; } = true;
        public bool Secure { get; set; } = secure;
        public string SameSite { get; set; } = "Lax";
        public string Path { get; set; } = "/";
        public string? Domain { get; set; }
        public long? MaxAge { get; set; }
        public long? Expires { get; set; }
    }

    /// <summary>The <c>Set-Cookie</c> header's value for a cookie, its value encoded.</summary>
    public static string Format(string name, string value, Options options)
    {
        var text = new StringBuilder(name).Append('=').Append(Encode(value)).Append("; Path=").Append(options.Path);
        if (options.Domain is { } domain) text.Append("; Domain=").Append(domain);
        if (options.MaxAge is { } seconds) text.Append("; Max-Age=").Append(seconds.ToString(CultureInfo.InvariantCulture));
        if (options.Expires is { } milliseconds) text.Append("; Expires=").Append(HttpDate(milliseconds));
        if (options.Secure) text.Append("; Secure");
        if (options.HttpOnly) text.Append("; HttpOnly");
        return text.Append("; SameSite=").Append(options.SameSite).ToString();
    }

    /// <summary>The <c>Set-Cookie</c> header's value that deletes a cookie: empty, its time over.</summary>
    public static string Deletion(string name, Options options)
    {
        var text = new StringBuilder(name).Append("=; Path=").Append(options.Path);
        if (options.Domain is { } domain) text.Append("; Domain=").Append(domain);
        text.Append("; Max-Age=0; Expires=").Append(HttpDate(0));
        if (options.Secure) text.Append("; Secure");
        return text.Append("; HttpOnly; SameSite=").Append(options.SameSite).ToString();
    }

    /// <summary>A time, its milliseconds since 1970 in UTC, as HTTP writes one: <c>Thu, 01 Jan 1970 00:00:00 GMT</c>.</summary>
    private static string HttpDate(long milliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToString("r", CultureInfo.InvariantCulture);

    /// <summary>
    /// Sets a cookie on a response's headers: its <c>Set-Cookie</c> added, one set before in the same response, of the same
    /// name, path and domain, taken away first; false where the headers cannot be changed, the response being sent.
    /// </summary>
    public static bool TrySet(IWebLingoHeaderCollection headers, string name, Options options, string header)
    {
        if (headers.IsReadOnly) return false;
        var kept = new List<string>();
        foreach (var value in headers.GetValues("Set-Cookie"))
        {
            if (!Same(value, name, options)) kept.Add(value);
        }
        if (kept.Count != headers.GetValues("Set-Cookie").Count)
        {
            headers.Remove("Set-Cookie");
            foreach (var value in kept) headers.Add("Set-Cookie", value);
        }
        return headers.Add("Set-Cookie", header) == WebLingoHeaderChange.Done;
    }

    /// <summary>Whether a <c>Set-Cookie</c> value sets the cookie of this name, path and domain.</summary>
    private static bool Same(string header, string name, Options options)
    {
        var parts = header.Split(';');
        var equals = parts[0].IndexOf('=', StringComparison.Ordinal);
        if (equals < 0 || parts[0][..equals].Trim() != name) return false;
        string? path = null, domain = null;
        foreach (var part in parts.AsSpan(1))
        {
            var attribute = part.Trim();
            if (attribute.StartsWith("Path=", StringComparison.OrdinalIgnoreCase)) path = attribute[5..];
            else if (attribute.StartsWith("Domain=", StringComparison.OrdinalIgnoreCase)) domain = attribute[7..].TrimStart('.');
        }
        return path == options.Path && string.Equals(domain, options.Domain?.TrimStart('.'), StringComparison.OrdinalIgnoreCase);
    }
}
