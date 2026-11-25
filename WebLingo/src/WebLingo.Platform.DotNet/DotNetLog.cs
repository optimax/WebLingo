using System.Globalization;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// A host's log on .NET (specification, section 6.12, "Logging"): its lines written to a writer, the host's own, its
/// terminal, its standard error, or IIS's <c>stdout</c> log, or to a file a day, <c>shop.log</c> written as
/// <c>shop-2026-11-25.log</c>, the day's in UTC, its folder made where it is not there, which only the host writes. Each line
/// is <c>2026-11-25T08:12:03.123Z INFO GET /cart #12 text</c>, its time in UTC, to the millisecond, its level, its request
/// where it has one, and its text, written whole and flushed, one at a time, from any thread. A line the file cannot take
/// is written to the writer instead, if there is one, with why, so that a request never fails for its log.
/// </summary>
public sealed class DotNetLog : IWebLingoLog, IWebLingoLogWriter, IDisposable
{
    private readonly object _lock = new();
    private readonly TextWriter? _writer;
    private readonly string? _file;
    private readonly Func<DateTimeOffset> _now;
    private long _requests;
    private StreamWriter? _open;
    private string? _openDay;
    private bool _failed;

    /// <summary>A log that writes to <paramref name="writer"/>, from <paramref name="level"/> on.</summary>
    public DotNetLog(TextWriter writer, WebLingoLogLevel level, Func<DateTimeOffset>? now = null)
    {
        (_writer, Level, _now) = (writer, level, now ?? (() => DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// A log that writes a file a day, <paramref name="file"/>'s name with the day's date before its extension, from
    /// <paramref name="level"/> on; what it cannot write there written to <paramref name="fallback"/>, if given.
    /// </summary>
    public DotNetLog(string file, WebLingoLogLevel level, TextWriter? fallback, Func<DateTimeOffset>? now = null)
    {
        (_file, Level, _writer, _now) = (Path.GetFullPath(file), level, fallback, now ?? (() => DateTimeOffset.UtcNow));
    }

    public WebLingoLogLevel Level { get; }

    public long NextRequest() => Interlocked.Increment(ref _requests);

    /// <summary>Under <c>weblingo</c>, a script's line, for no request.</summary>
    public void Write(WebLingoLogLevel level, string text) => Write(level, null, text);

    public void Write(WebLingoLogLevel level, string? request, string text)
    {
        if (level < Level) return;
        var now = _now().ToUniversalTime();
        var line = now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) + " " + LevelText(level)
                   + (request is null ? " " : " " + request + " ") + text;
        lock (_lock)
        {
            if (_file is not null && TryWriteFile(now, line)) return;
            if (_writer is null) return;
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    /// <summary>The file of <paramref name="now"/>'s day, <c>shop-2026-11-25.log</c> for <c>shop.log</c>.</summary>
    public static string FileOf(string file, DateTimeOffset now)
    {
        var day = now.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var extension = Path.GetExtension(file);
        return Path.Combine(Path.GetDirectoryName(file) ?? ".", Path.GetFileNameWithoutExtension(file) + "-" + day + extension);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _open?.Dispose();
            _open = null;
        }
    }

    private static string LevelText(WebLingoLogLevel level) => level switch
    {
        WebLingoLogLevel.Debug => "DEBUG",
        WebLingoLogLevel.Info => "INFO",
        WebLingoLogLevel.Warn => "WARN",
        _ => "ERROR"
    };

    /// <summary>The line written to the day's file, the file opened anew when the day changes; false, said once, if it cannot be.</summary>
    private bool TryWriteFile(DateTimeOffset now, string line)
    {
        var path = FileOf(_file!, now);
        try
        {
            if (_open is null || _openDay != path)
            {
                _open?.Dispose();
                _open = null;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                _open = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                _openDay = path;
            }
            _open.WriteLine(line);
            _open.Flush();
            _failed = false;
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            _open?.Dispose();
            _open = null;
            if (!_failed && _writer is not null) _writer.WriteLine($"WASP cannot write its log, {path}: {failure.Message}");
            _failed = true;
            return false;
        }
    }
}
