using System.Text;
using WebLingo.Hosting;
namespace WebLingo.Hosting.Standalone;

/// <summary>What reading a request's head came to: more bytes needed, a head read, or the request refused.</summary>
public enum WebLingoHttpParsed { Incomplete, Done, Refused }

/// <summary>
/// A request's line and headers read from the bytes a client sent (specification, section 9, "M0.23 parts", part 2), as
/// RFC 9112 has them, strictly, so that nothing in front of the server reads one request where it reads another: each line
/// ended by CR LF; a method a token, a target in origin form, <c>/path?query</c>, in absolute form, or <c>*</c> for
/// <c>OPTIONS</c>, and <c>HTTP/1.1</c> or <c>HTTP/1.0</c>; each header a token, a colon with nothing before it, and a value
/// of visible characters, spaces and tabs, UTF-8; no header folded onto the next line; an HTTP/1.1 request's one
/// <c>Host</c>; a body by <c>Content-Length</c>, its values all one length, or by <c>Transfer-Encoding: chunked</c>, never
/// both. Anything else refused, a 400, or a 414, 431, 501 or 505, as RFC 9110 names them, its client's connection then
/// closed. Its path is decoded as ASP.NET Core decodes it, every <c>%xx</c> but <c>%2F</c>, its dot segments removed. It
/// never throws, whatever it is given.
/// </summary>
public static class WebLingoHttpParser
{
    /// <summary>
    /// Reads a request's head from the start of <paramref name="bytes"/>: <see cref="WebLingoHttpParsed.Done"/>, with the head
    /// and how many bytes it took; <see cref="WebLingoHttpParsed.Incomplete"/>, where they end before it does, within
    /// <paramref name="limits"/>; or <see cref="WebLingoHttpParsed.Refused"/>, with the status to answer and why.
    /// </summary>
    public static WebLingoHttpParsed Parse(ReadOnlySpan<byte> bytes, WebLingoHttpLimits limits, out WebLingoHttpHead? head, out int consumed,
        out int status, out string reason)
    {
        (head, consumed, status, reason) = (null, 0, 0, "");
        var at = 0;
        // Empty lines before a request's line are skipped (RFC 9112, section 2.2), as many as its line could hold.
        while (at + 1 < bytes.Length && at < limits.RequestLine && bytes[at] == '\r' && bytes[at + 1] == '\n') at += 2;
        var lineEnd = LineEnd(bytes, at);
        if (lineEnd == BareLineFeed) return Refuse(400, "A line ends without its CR.", out status, out reason);
        if (lineEnd < 0)
        {
            return bytes.Length - at > limits.RequestLine
                ? Refuse(414, "The request's line is too long.", out status, out reason)
                : WebLingoHttpParsed.Incomplete;
        }
        if (lineEnd - at > limits.RequestLine) return Refuse(414, "The request's line is too long.", out status, out reason);
        if (!TryReadLine(bytes[at..lineEnd], out var method, out var target, out var minor, out status, out reason)) return WebLingoHttpParsed.Refused;

        var headers = new List<(string Name, string Value)>();
        var start = lineEnd + 2;
        var position = start;
        while (true)
        {
            var end = LineEnd(bytes, position);
            if (end == BareLineFeed) return Refuse(400, "A line ends without its CR.", out status, out reason);
            if (end < 0)
            {
                return bytes.Length - start > limits.RequestHeaders
                    ? Refuse(431, "The request's headers are too large.", out status, out reason)
                    : WebLingoHttpParsed.Incomplete;
            }
            if (end + 2 - start > limits.RequestHeaders) return Refuse(431, "The request's headers are too large.", out status, out reason);
            if (end == position)
            {
                consumed = end + 2;
                break;
            }
            if (headers.Count >= limits.HeaderCount) return Refuse(431, "The request has too many headers.", out status, out reason);
            if (!TryReadHeader(bytes[position..end], out var name, out var value, out reason))
            {
                status = 400;
                return WebLingoHttpParsed.Refused;
            }
            headers.Add((name, value));
            position = end + 2;
        }
        return TryMakeHead(method, target, minor, headers, out head, out status, out reason) ? WebLingoHttpParsed.Done : WebLingoHttpParsed.Refused;
    }

    private const int BareLineFeed = -2;

    /// <summary>Where the line from <paramref name="from"/> ends, at its CR; -1 where it has not yet; <see cref="BareLineFeed"/> for an LF without a CR, or a CR without an LF.</summary>
    private static int LineEnd(ReadOnlySpan<byte> bytes, int from)
    {
        for (var i = from; i < bytes.Length; i++)
        {
            if (bytes[i] == '\n') return BareLineFeed;
            if (bytes[i] != '\r') continue;
            if (i + 1 >= bytes.Length) return -1;
            return bytes[i + 1] == '\n' ? i : BareLineFeed;
        }
        return -1;
    }

    private static WebLingoHttpParsed Refuse(int code, string why, out int status, out string reason)
    {
        (status, reason) = (code, why);
        return WebLingoHttpParsed.Refused;
    }

    /// <summary>A request's line: its method, a token; one space; its target, visible characters; one space; its version.</summary>
    private static bool TryReadLine(ReadOnlySpan<byte> line, out string method, out string target, out int minor, out int status, out string reason)
    {
        (method, target, minor, status, reason) = ("", "", 0, 400, "");
        var first = line.IndexOf((byte)' ');
        if (first <= 0)
        {
            reason = "The request's line is not a method, a target and a version.";
            return false;
        }
        var rest = line[(first + 1)..];
        var second = rest.IndexOf((byte)' ');
        if (second <= 0)
        {
            reason = "The request's line is not a method, a target and a version.";
            return false;
        }
        var methodBytes = line[..first];
        foreach (var b in methodBytes)
        {
            if (!IsTokenByte(b))
            {
                reason = "The request's method is not a token.";
                return false;
            }
        }
        var targetBytes = rest[..second];
        foreach (var b in targetBytes)
        {
            if (b is < 0x21 or > 0x7E)
            {
                reason = "The request's target has a character a target cannot have.";
                return false;
            }
        }
        var version = rest[(second + 1)..];
        if (version.SequenceEqual("HTTP/1.1"u8)) minor = 1;
        else if (version.SequenceEqual("HTTP/1.0"u8)) minor = 0;
        else if (version.Length == 8 && version.StartsWith("HTTP/"u8) && IsDigit(version[5]) && version[6] == '.' && IsDigit(version[7]))
        {
            (status, reason) = (505, "The request's HTTP version is not 1.1 or 1.0.");
            return false;
        }
        else
        {
            reason = "The request's line does not end with its HTTP version.";
            return false;
        }
        method = Encoding.ASCII.GetString(methodBytes);
        target = Encoding.ASCII.GetString(targetBytes);
        status = 0;
        return true;
    }

    /// <summary>A header's line: a token, a colon right after it, and a value, its spaces and tabs around it left out.</summary>
    private static bool TryReadHeader(ReadOnlySpan<byte> line, out string name, out string value, out string reason)
    {
        (name, value, reason) = ("", "", "");
        if (line[0] is (byte)' ' or (byte)'\t')
        {
            reason = "A header is folded onto the line after it.";
            return false;
        }
        var colon = line.IndexOf((byte)':');
        if (colon <= 0)
        {
            reason = "A header's line is not a name, a colon and a value.";
            return false;
        }
        foreach (var b in line[..colon])
        {
            if (!IsTokenByte(b))
            {
                reason = "A header's name is not a token, or has a space before its colon.";
                return false;
            }
        }
        var raw = line[(colon + 1)..];
        while (raw.Length > 0 && raw[0] is (byte)' ' or (byte)'\t') raw = raw[1..];
        while (raw.Length > 0 && raw[^1] is (byte)' ' or (byte)'\t') raw = raw[..^1];
        foreach (var b in raw)
        {
            if (b is < 0x20 and not (byte)'\t' || b == 0x7F)
            {
                reason = "A header's value has a control character.";
                return false;
            }
        }
        try
        {
            value = Strict.GetString(raw);
        }
        catch (DecoderFallbackException)
        {
            reason = "A header's value is not UTF-8.";
            return false;
        }
        name = Encoding.ASCII.GetString(line[..colon]);
        return true;
    }

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The head of a request whose line and headers were read: its body's framing, its host, its target's path and query.</summary>
    private static bool TryMakeHead(string method, string target, int minor, List<(string Name, string Value)> headers, out WebLingoHttpHead? head,
        out int status, out string reason)
    {
        (head, status, reason) = (null, 400, "");
        long? length = null;
        var (chunked, encoded, keepAlive, expects) = (false, false, minor == 1, false);
        string? host = null;
        var hosts = 0;
        foreach (var (name, value) in headers)
        {
            if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                encoded = true;
                foreach (var coding in value.Split(','))
                {
                    var trimmed = coding.Trim();
                    if (trimmed.Length == 0) continue;
                    if (!trimmed.Equals("chunked", StringComparison.OrdinalIgnoreCase) || chunked)
                    {
                        (status, reason) = (501, $"The request's transfer coding, '{value}', is not one this server reads: it reads 'chunked'.");
                        return false;
                    }
                    chunked = true;
                }
            }
            else if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var part in value.Split(','))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length is 0 or > 18 || !trimmed.All(char.IsAsciiDigit) || (length is { } earlier && earlier != long.Parse(trimmed, System.Globalization.CultureInfo.InvariantCulture)))
                    {
                        reason = "The request's Content-Length is not one length.";
                        return false;
                    }
                    length = long.Parse(trimmed, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                hosts++;
                host = value;
            }
            else if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var option in value.Split(','))
                {
                    var trimmed = option.Trim();
                    if (trimmed.Equals("close", StringComparison.OrdinalIgnoreCase)) keepAlive = false;
                    else if (trimmed.Equals("keep-alive", StringComparison.OrdinalIgnoreCase) && minor == 0) keepAlive = true;
                }
            }
            else if (name.Equals("Expect", StringComparison.OrdinalIgnoreCase) && minor == 1)
            {
                expects |= value.Trim().Equals("100-continue", StringComparison.OrdinalIgnoreCase);
            }
        }
        if (encoded && !chunked)
        {
            (status, reason) = (501, "The request's Transfer-Encoding names no coding.");
            return false;
        }
        if (chunked && length is not null)
        {
            reason = "The request has both a Content-Length and a Transfer-Encoding, which RFC 9112 refuses, against request smuggling.";
            return false;
        }
        if (chunked && minor == 0)
        {
            reason = "An HTTP/1.0 request cannot have a Transfer-Encoding.";
            return false;
        }
        if (hosts > 1 || (hosts == 0 && minor == 1))
        {
            reason = hosts > 1 ? "The request has more than one Host." : "An HTTP/1.1 request has a Host.";
            return false;
        }
        string pathAndQuery;
        if (target.StartsWith('/'))
        {
            pathAndQuery = target;
        }
        else if (target == "*" && method == "OPTIONS")
        {
            pathAndQuery = "*";
        }
        else if ((target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                 && Uri.TryCreate(target, UriKind.Absolute, out var absolute) && absolute.Authority.Length > 0)
        {
            // Absolute form: its authority the request's host, in place of its Host header's (RFC 9112, section 3.2.2).
            host = absolute.Authority;
            var authorityEnd = target.IndexOfAny(['/', '?'], target.IndexOf("//", StringComparison.Ordinal) + 2);
            pathAndQuery = authorityEnd < 0 ? "/" : target[authorityEnd] == '?' ? "/" + target[authorityEnd..] : target[authorityEnd..];
        }
        else
        {
            reason = "The request's target is not a path, an absolute URL, or '*' for OPTIONS.";
            return false;
        }
        var question = pathAndQuery.IndexOf('?');
        var rawPath = question >= 0 ? pathAndQuery[..question] : pathAndQuery;
        var queryString = question >= 0 ? pathAndQuery[(question + 1)..] : "";
        if (!TryDecodePath(rawPath, out var path))
        {
            reason = "The request's path is not UTF-8 once decoded.";
            return false;
        }
        head = new WebLingoHttpHead(method, path, queryString, minor, host ?? "localhost", headers)
        {
            ContentLength = length,
            Chunked = chunked,
            KeepAlive = keepAlive,
            ExpectsContinue = expects
        };
        status = 0;
        return true;
    }

    /// <summary>
    /// A target's path as ASP.NET Core decodes it: every <c>%xx</c> decoded but <c>%2F</c>, which stays as it is, so that it
    /// names no folder; a <c>%</c> not followed by two hexadecimal digits left as it is; then its dot segments removed (RFC
    /// 3986, section 5.2.4). False where what is decoded is not UTF-8.
    /// </summary>
    public static bool TryDecodePath(string raw, out string path)
    {
        path = raw;
        if (raw == "*") return true;
        var bytes = new List<byte>(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '%' && i + 2 < raw.Length && Uri.IsHexDigit(raw[i + 1]) && Uri.IsHexDigit(raw[i + 2]))
            {
                var value = (byte)Convert.ToInt32(raw.Substring(i + 1, 2), 16);
                if (value == '/')
                {
                    bytes.AddRange("%2F"u8);
                }
                else
                {
                    bytes.Add(value);
                }
                i += 2;
                continue;
            }
            bytes.Add((byte)raw[i]);
        }
        string decoded;
        try
        {
            decoded = Strict.GetString([.. bytes]);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        path = RemoveDotSegments(decoded);
        return true;
    }

    /// <summary>A path's <c>.</c> and <c>..</c> segments removed, as RFC 3986, section 5.2.4, removes them; never above <c>/</c>.</summary>
    private static string RemoveDotSegments(string path)
    {
        if (!path.Contains('.')) return path;
        var segments = path.Split('/');
        var kept = new List<string>();
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            var last = i == segments.Length - 1;
            if (segment == ".")
            {
                if (last) kept.Add("");
                continue;
            }
            if (segment == "..")
            {
                if (kept.Count > 0) kept.RemoveAt(kept.Count - 1);
                if (last) kept.Add("");
                continue;
            }
            kept.Add(segment);
        }
        return "/" + string.Join('/', kept);
    }

    private static bool IsDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    /// <summary>Whether a byte may be in a token, RFC 9110's tchar.</summary>
    private static bool IsTokenByte(byte b) =>
        b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9'
            or (byte)'!' or (byte)'#' or (byte)'$' or (byte)'%' or (byte)'&' or (byte)'\'' or (byte)'*' or (byte)'+' or (byte)'-' or (byte)'.'
            or (byte)'^' or (byte)'_' or (byte)'`' or (byte)'|' or (byte)'~';
}
