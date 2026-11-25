namespace WebLingo.Platform.Abstractions;

/// <summary>A log's levels, the lowest first (specification, section 6.12, "Logging").</summary>
public enum WebLingoLogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// A host's log (specification, section 6.12, "Logging"): the host's own, its terminal, its standard error or IIS's
/// <c>stdout</c> log, or a file a day, which only the host writes. Each line its time in UTC, its level, its request where
/// it has one, and its text, written whole. Safe on several threads at once.
/// </summary>
public interface IWebLingoLog
{
    /// <summary>The lowest level it writes; a line below it is not written.</summary>
    WebLingoLogLevel Level { get; }

    /// <summary>The number of a request that writes to it, the next of the process's, the first 1.</summary>
    long NextRequest();

    /// <summary>A line of <paramref name="level"/>, for <paramref name="request"/>, <c>GET /cart #12</c>, or for none, its text as given.</summary>
    void Write(WebLingoLogLevel level, string? request, string text);
}

/// <summary>
/// What an execution writes to its log through (<c>std:log</c>, section 6.12, "Logging"): its host's, for its request
/// under <c>wasp</c>, the standard error under <c>weblingo</c>. An execution's own, used on its worker only.
/// </summary>
public interface IWebLingoLogWriter
{
    /// <summary>The lowest level it writes.</summary>
    WebLingoLogLevel Level { get; }

    /// <summary>A line of <paramref name="level"/>, its text as given, which a line below <see cref="Level"/> leaves out.</summary>
    void Write(WebLingoLogLevel level, string text);
}

/// <summary>The levels' names, and a log's text kept to a line.</summary>
public static class WebLingoLogText
{
    /// <summary>The level's name as a configuration and a script name it: <c>debug</c>, <c>info</c>, <c>warn</c> or <c>error</c>.</summary>
    public static string Name(WebLingoLogLevel level) => level switch
    {
        WebLingoLogLevel.Debug => "debug",
        WebLingoLogLevel.Info => "info",
        WebLingoLogLevel.Warn => "warn",
        _ => "error"
    };

    /// <summary>The level <paramref name="name"/> names, as <see cref="Name"/> writes it; false for any other.</summary>
    public static bool TryParse(string? name, out WebLingoLogLevel level)
    {
        (var known, level) = name switch
        {
            "debug" => (true, WebLingoLogLevel.Debug),
            "info" => (true, WebLingoLogLevel.Info),
            "warn" => (true, WebLingoLogLevel.Warn),
            "error" => (true, WebLingoLogLevel.Error),
            _ => (false, WebLingoLogLevel.Info)
        };
        return known;
    }

    /// <summary>
    /// <paramref name="text"/> on one line, so that no text a script or a client gave makes a line of its own, as a forged
    /// one would (OWASP's ASVS, V7.3.1): a carriage return written <c>\r</c>, a line feed <c>\n</c>, and any other control
    /// character but a tab as <c>\u{…}</c>, its number in hexadecimal; the text itself where it has none.
    /// </summary>
    public static string OneLine(string text)
    {
        var at = 0;
        while (at < text.Length && !Escaped(text[at])) at++;
        if (at == text.Length) return text;
        var line = new System.Text.StringBuilder(text.Length + 8);
        line.Append(text, 0, at);
        for (; at < text.Length; at++)
        {
            var c = text[at];
            if (!Escaped(c)) line.Append(c);
            else if (c == '\r') line.Append("\\r");
            else if (c == '\n') line.Append("\\n");
            else line.Append("\\u{").Append(((int)c).ToString("X", System.Globalization.CultureInfo.InvariantCulture)).Append('}');
        }
        return line.ToString();
    }

    private static bool Escaped(char c) => c is < ' ' and not '\t' or '\u007F' or '\u0085' or '\u2028' or '\u2029';
}
