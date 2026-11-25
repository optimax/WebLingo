using WebLingo.Platform.Abstractions;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A log a test reads (specification, section 6.12, "Logging"): each line's text added to <paramref name="lines"/>, and
/// each line whole, its level, its request and its text, to <see cref="Entries"/>, from <paramref name="level"/> on.
/// </summary>
internal sealed class ListLog(List<string> lines, WebLingoLogLevel level = WebLingoLogLevel.Debug) : IWebLingoLog
{
    private long _requests;

    public ListLog(WebLingoLogLevel level = WebLingoLogLevel.Debug) : this([], level)
    {
    }

    public List<(WebLingoLogLevel Level, string? Request, string Text)> Entries { get; } = [];

    /// <summary>
    /// The one error <paramref name="lines"/> hold, the first, the others the calls it was raised in, each <c>  at …</c>
    /// (section 6.12, "Logging, parts", part 2).
    /// </summary>
    public static string Error(List<string> lines)
    {
        Xunit.Assert.NotEmpty(lines);
        Xunit.Assert.All(lines.Skip(1), line => Xunit.Assert.StartsWith("  at ", line));
        return lines[0];
    }

    public WebLingoLogLevel Level => level;

    public long NextRequest() => Interlocked.Increment(ref _requests);

    public void Write(WebLingoLogLevel written, string? request, string text)
    {
        if (written < level) return;
        lock (Entries)
        {
            lines.Add(text);
            Entries.Add((written, request, text));
        }
    }
}
