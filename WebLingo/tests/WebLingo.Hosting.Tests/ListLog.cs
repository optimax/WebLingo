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
