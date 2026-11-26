using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;
using WebLingo.Hosting.Abstractions;
namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// How a file is sent (specification, section 6.12, "Files sent"): a site's own file, with the site's headers, and one a
/// page or a handler sent, <c>response.sendFile(path, type, { download })</c>, with the page's status and headers, both
/// through a host's sink (<see cref="IWebLingoResponseSink"/>), ASP.NET Core's or FastCGI's, by one plan
/// (<see cref="Plan"/>): its <c>ETag</c>, its length and its time, and its <c>Last-Modified</c>, unless the page gave its
/// own; for a GET or a HEAD answered 200, <c>Accept-Ranges: bytes</c>, a 304 where the request's <c>If-None-Match</c>, or
/// its <c>If-Modified-Since</c> where it has none, says the client has it, and one range of its bytes, a 206, where its
/// <c>Range</c> asks for one its <c>If-Range</c> allows, a 416 where the range is beyond the file, several ranges sending it
/// whole; a <c>Content-Disposition</c> where it is to be saved; and no body for a HEAD.
/// </summary>
public static class WebLingoFileSending
{
    /// <summary>How much of a file a piece of its body holds, within a FastCGI record's most.</summary>
    public const int Piece = 32 * 1024;

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>The media type a file's extension gives, as ASP.NET Core's static files have them, or null for one it does not know.</summary>
    public static string? TypeOf(string path) => ContentTypes.TryGetContentType(path, out var type) ? type : null;

    /// <summary>
    /// Sends a site's own file at <paramref name="path"/>, with the site's headers, <paramref name="site"/>: a 404 where its
    /// extension gives no type, as ASP.NET Core's static files are served.
    /// </summary>
    public static async Task SendSiteFileAsync(IWebLingoResponseSink sink, string method, IWebLingoHeaderCollection request, string path,
        IReadOnlyList<KeyValuePair<string, string>> site)
    {
        var headers = new WebLingoHeaderList();
        // The site's headers, X-Content-Type-Options and its kin, a file's too (section 6.12, "Security headers").
        foreach (var (name, value) in site) headers.Set(name, value);
        if (TypeOf(path) is not { } type)
        {
            await sink.StartAsync(404, headers, 0);
            return;
        }
        await SendAsync(sink, method, request, 200, headers, path, type, null);
    }

    /// <summary>
    /// Sends the file a page or a handler sent, <paramref name="file"/>, with its response's <paramref name="status"/> and
    /// <paramref name="headers"/>: its type the one it gave, else the response's <c>Content-Type</c>, else the one its
    /// extension gives, else <c>application/octet-stream</c>.
    /// </summary>
    public static Task SendAsync(IWebLingoResponseSink sink, string method, IWebLingoHeaderCollection request, WebLingoSentFile file, int status,
        IWebLingoHeaderCollection headers)
    {
        var type = file.Type ?? headers.Get("Content-Type") ?? TypeOf(file.Path) ?? "application/octet-stream";
        return SendAsync(sink, method, request, status, headers, file.Path, type, file.Download);
    }

    private static async Task SendAsync(IWebLingoResponseSink sink, string method, IWebLingoHeaderCollection request, int status,
        IWebLingoHeaderCollection headers, string path, string type, string? download)
    {
        FileStream source;
        DateTime modified;
        try
        {
            source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, useAsync: true);
            modified = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Gone, or locked, since it was found: nothing to send.
            await sink.StartAsync(404, headers, 0);
            return;
        }
        await using (source)
        {
            var plan = Plan(method, request, status, headers, source.Length, new DateTimeOffset(modified, TimeSpan.Zero), type, download);
            await sink.StartAsync(plan.Status, headers, plan.Length);
            if (!plan.Body || plan.Count == 0) return;
            source.Seek(plan.Offset, SeekOrigin.Begin);
            var buffer = new byte[(int)Math.Min(Piece, plan.Count)];
            for (var left = plan.Count; left > 0;)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)));
                if (read <= 0) break; // Shortened since it was opened: what there was is sent.
                await sink.WriteAsync(read == buffer.Length ? buffer : buffer[..read]);
                left -= read;
            }
        }
    }

    /// <summary>
    /// What a file of <paramref name="length"/> bytes, written last at <paramref name="modified"/>, is sent as, given the
    /// <paramref name="request"/>'s headers and its <paramref name="method"/>, the <paramref name="status"/> its response
    /// has, and its <paramref name="headers"/>, which the plan changes: its status, the length its <c>Content-Length</c>
    /// gives, or null for a 304, the range of its bytes sent, and whether a body is sent at all.
    /// </summary>
    public static WebLingoFilePlan Plan(string method, IWebLingoHeaderCollection request, int status, IWebLingoHeaderCollection headers, long length,
        DateTimeOffset modified, string type, string? download)
    {
        headers.Set("Content-Type", type);
        // HTTP's dates have whole seconds: the file's time so, for If-Modified-Since and If-Range to compare.
        var seconds = new DateTimeOffset(modified.UtcTicks - modified.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        var tag = headers.Get("ETag");
        if (tag is null)
        {
            tag = "\"" + length.ToString("x", CultureInfo.InvariantCulture) + "-" + modified.UtcTicks.ToString("x", CultureInfo.InvariantCulture) + "\"";
            headers.Set("ETag", tag);
        }
        if (headers.Get("Last-Modified") is null) headers.Set("Last-Modified", seconds.ToString("r", CultureInfo.InvariantCulture));
        if (download is not null) headers.Set("Content-Disposition", Disposition(download));
        var body = !method.Equals("HEAD", StringComparison.OrdinalIgnoreCase);
        var whole = new WebLingoFilePlan(status, length, 0, length, body);
        // Only a file answered as it is, to a GET or a HEAD, is answered in part, or not at all.
        if (status != 200 || (body && !method.Equals("GET", StringComparison.OrdinalIgnoreCase))) return whole;
        headers.Set("Accept-Ranges", "bytes");
        if (NotModified(request, tag, seconds))
        {
            headers.Remove("Content-Disposition");
            return new WebLingoFilePlan(304, null, 0, 0, false);
        }
        if (request.Get("Range") is not { } range || !RangeAllowed(request.Get("If-Range"), tag, seconds)) return whole;
        switch (Ranged(range, length, out var first, out var last))
        {
            case RangeKind.Ignored:
                return whole;
            case RangeKind.Unsatisfiable:
                headers.Set("Content-Range", "bytes */" + length.ToString(CultureInfo.InvariantCulture));
                headers.Remove("Content-Disposition");
                return new WebLingoFilePlan(416, 0, 0, 0, false);
            default:
                headers.Set("Content-Range", "bytes " + first.ToString(CultureInfo.InvariantCulture) + "-" + last.ToString(CultureInfo.InvariantCulture)
                    + "/" + length.ToString(CultureInfo.InvariantCulture));
                return new WebLingoFilePlan(206, last - first + 1, first, last - first + 1, body);
        }
    }

    /// <summary>
    /// Whether the client has the file: its <c>If-None-Match</c>'s tags, <c>*</c> or one that matches <paramref name="tag"/>
    /// weakly, or, where it gives none, its <c>If-Modified-Since</c> no earlier than the file's time.
    /// </summary>
    private static bool NotModified(IWebLingoHeaderCollection request, string tag, DateTimeOffset modified)
    {
        if (request.Get("If-None-Match") is { } match)
        {
            foreach (var part in match.Split(','))
            {
                var given = part.Trim();
                if (given == "*" || Opaque(given) == Opaque(tag)) return true;
            }
            return false;
        }
        return request.Get("If-Modified-Since") is { } since && TryDate(since, out var date) && modified <= date;
    }

    /// <summary>
    /// Whether a <c>Range</c> is to be read, given the request's <c>If-Range</c>: none; a strong tag equal to
    /// <paramref name="tag"/>; or the file's very time.
    /// </summary>
    private static bool RangeAllowed(string? condition, string tag, DateTimeOffset modified)
    {
        if (condition is null) return true;
        condition = condition.Trim();
        if (condition.StartsWith('"')) return condition == tag;
        if (condition.StartsWith("W/", StringComparison.Ordinal)) return false;
        return TryDate(condition, out var date) && date == modified;
    }

    /// <summary>A tag without its weakness, <c>W/</c>, as a weak comparison takes it.</summary>
    private static string Opaque(string tag) => tag.StartsWith("W/", StringComparison.Ordinal) ? tag[2..] : tag;

    private static bool TryDate(string text, out DateTimeOffset date) =>
        DateTimeOffset.TryParseExact(text.Trim(), "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);

    private enum RangeKind { Ignored, Unsatisfiable, Satisfiable }

    /// <summary>
    /// A <c>Range</c> of one range of bytes, <c>bytes=first-last</c>, <c>bytes=first-</c> or <c>bytes=-suffix</c>, read
    /// against a file of <paramref name="length"/> bytes; ignored where it is not one such range, several among them, and
    /// unsatisfiable where it begins beyond the file or asks for none of its end.
    /// </summary>
    private static RangeKind Ranged(string range, long length, out long first, out long last)
    {
        (first, last) = (0, 0);
        range = range.Trim();
        if (!range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return RangeKind.Ignored;
        var spec = range[6..].Trim();
        var dash = spec.IndexOf('-');
        if (spec.Contains(',') || dash < 0) return RangeKind.Ignored;
        var from = spec[..dash].Trim();
        var to = spec[(dash + 1)..].Trim();
        if (from.Length == 0)
        {
            if (!TryCount(to, out var suffix)) return RangeKind.Ignored;
            if (suffix == 0 || length == 0) return RangeKind.Unsatisfiable;
            (first, last) = (Math.Max(0, length - suffix), length - 1);
            return RangeKind.Satisfiable;
        }
        if (!TryCount(from, out first)) return RangeKind.Ignored;
        if (to.Length == 0)
        {
            last = length - 1;
        }
        else if (!TryCount(to, out last) || last < first)
        {
            return RangeKind.Ignored;
        }
        if (first >= length) return RangeKind.Unsatisfiable;
        last = Math.Min(last, length - 1);
        return RangeKind.Satisfiable;
    }

    private static bool TryCount(string text, out long count)
    {
        count = 0;
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c)) return false;
        }
        return text.Length > 0 && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out count);
    }

    /// <summary>
    /// A file's <c>Content-Disposition</c> to be saved as <paramref name="name"/>: its name in ASCII, any other character, a
    /// quote and a backslash as <c>_</c>, and, where it has any, as written, in UTF-8 (RFC 6266).
    /// </summary>
    public static string Disposition(string name)
    {
        var plain = new StringBuilder();
        var ascii = true;
        foreach (var c in name)
        {
            var kept = c is >= ' ' and <= '~' and not '"' and not '\\';
            ascii &= kept;
            plain.Append(kept ? c : '_');
        }
        var text = "attachment; filename=\"" + plain + "\"";
        if (ascii) return text;
        var encoded = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(name))
        {
            if (char.IsAsciiLetterOrDigit((char)b) || "!#$&+-.^_`|~".Contains((char)b)) encoded.Append((char)b);
            else encoded.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return text + "; filename*=UTF-8''" + encoded;
    }
}
