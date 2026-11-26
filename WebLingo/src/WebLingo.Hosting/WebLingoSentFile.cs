namespace WebLingo.Hosting;

/// <summary>
/// A file a page or a handler sends, <c>response.sendFile(path, type, { download })</c> (specification, section 6.12, "Files
/// sent"): its full path, which its host opens; its type, or null for the one the response's <c>Content-Type</c> or its
/// extension gives; and the name a browser saves it under, or null where it is to be shown.
/// </summary>
public sealed record WebLingoSentFile(string Path, string? Type, string? Download)
{
    /// <summary>
    /// Whether <paramref name="text"/> is a media type a response may give, <c>type/subtype</c>, each a token of HTTP's,
    /// its parameters after a <c>;</c>, all of it printable ASCII.
    /// </summary>
    public static bool IsMediaType(string text)
    {
        foreach (var c in text)
        {
            if (c is < ' ' or > '~') return false;
        }
        var end = text.IndexOf(';');
        var main = (end < 0 ? text : text[..end]).Trim();
        var slash = main.IndexOf('/');
        return slash > 0 && slash < main.Length - 1 && IsToken(main.AsSpan(0, slash)) && IsToken(main.AsSpan(slash + 1));
    }

    /// <summary>Whether <paramref name="name"/> may be a file's name a browser saves it under: not empty, not a path, without control characters.</summary>
    public static bool IsFileName(string name)
    {
        if (name.Length == 0 || name.Trim().Length == 0) return false;
        foreach (var c in name)
        {
            if (c is < ' ' or '\u007F' or '/' or '\\') return false;
        }
        return true;
    }

    /// <summary>The last part of <paramref name="path"/>, after its last <c>/</c> or <c>\</c>: the file's own name.</summary>
    public static string FileName(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

    private static bool IsToken(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c))) return false;
        }
        return text.Length > 0;
    }
}
