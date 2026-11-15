using System.Text;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// The file system on .NET (section 7.4): <see cref="File"/>, <see cref="Directory"/> and streams, adapter-only (section
/// 7.14); each failure translated into a portable category, its reason naming no path (WL-P015). Stateless, and so safe
/// on several threads at once.
/// </summary>
public sealed class DotNetFileSystem : IWebLingoFileSystem
{
    public string ReadText(string path, string encoding) => Translated(() =>
    {
        using var reader = Reader(path, encoding);
        return reader.ReadToEnd();
    });

    /// <summary>
    /// A reader in the encoding named, which skips that encoding's byte order mark, UTF-8's too, but takes no other mark as
    /// naming another encoding in its place.
    /// </summary>
    private static StreamReader Reader(string path, string encoding) =>
        new(path, EncodingOf(encoding), detectEncodingFromByteOrderMarks: IsUtf8(encoding));

    private static bool IsUtf8(string name) =>
        string.Equals(name, "utf-8", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "utf8", StringComparison.OrdinalIgnoreCase);

    public void WriteText(string path, string text, string encoding, bool append) => Translated(() =>
    {
        using var writer = new StreamWriter(path, append, EncodingOf(encoding));
        writer.Write(text);
        return true;
    });

    public bool Exists(string path) => Translated(() => File.Exists(path) || Directory.Exists(path));

    public void Remove(string path) => Translated(() =>
    {
        // File.Delete does nothing for a file that is not there, which is an error here.
        if (!File.Exists(path)) throw new FileNotFoundException();
        File.Delete(path);
        return true;
    });

    public IReadOnlyList<string> List(string path) => Translated(() =>
    {
        var names = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(path)) names.Add(Path.GetFileName(entry));
        names.Sort(StringComparer.Ordinal);
        return (IReadOnlyList<string>)names;
    });

    public void MakeFolder(string path) => Translated(() => Directory.CreateDirectory(path));

    public void RemoveFolder(string path) => Translated(() =>
    {
        if (Directory.EnumerateFileSystemEntries(path).Any()) throw new WebLingoFileSystemException(WebLingoFileErrorKind.NotEmpty, "it is not empty");
        Directory.Delete(path);
        return true;
    });

    public IWebLingoFile Open(string path, WebLingoFileMode mode, string encoding) => Translated<IWebLingoFile>(() => mode == WebLingoFileMode.Read
        ? new DotNetFile(Reader(path, encoding), null)
        : new DotNetFile(null, new StreamWriter(path, mode == WebLingoFileMode.Append, EncodingOf(encoding))));

    /// <summary>
    /// An encoding by its name, as .NET names them (<c>utf-8</c>, <c>utf-16</c>, <c>utf-16BE</c>, <c>utf-32</c>,
    /// <c>us-ascii</c>, <c>iso-8859-1</c>), throwing on bytes that are not valid in it; UTF-8 writes no byte order mark.
    /// </summary>
    private static Encoding EncodingOf(string name)
    {
        if (IsUtf8(name))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }
        try
        {
            return Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (ArgumentException)
        {
            throw new WebLingoFileSystemException(WebLingoFileErrorKind.UnknownEncoding, $"there is no encoding '{name}'");
        }
    }

    /// <summary>Runs an operation, its .NET exception translated: a category, and a reason naming no path.</summary>
    private static T Translated<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (WebLingoFileSystemException)
        {
            throw;
        }
        catch (Exception exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    private static WebLingoFileSystemException? Translate(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => new(WebLingoFileErrorKind.NotFound, "there is no such file or folder"),
        UnauthorizedAccessException => new(WebLingoFileErrorKind.AccessDenied, "access to it is denied"),
        PathTooLongException => new(WebLingoFileErrorKind.InvalidPath, "its path is too long"),
        DecoderFallbackException or EncoderFallbackException => new(WebLingoFileErrorKind.InvalidText, "its text is not valid in its encoding"),
        ArgumentException or NotSupportedException => new(WebLingoFileErrorKind.InvalidPath, "its path is not a valid one"),
        IOException io when io.HResult is unchecked((int)0x80070050) or unchecked((int)0x800700B7) or 17 =>
            new(WebLingoFileErrorKind.AlreadyExists, "it is there already"),
        IOException => new(WebLingoFileErrorKind.Other, "the operation failed"),
        _ => null
    };

    /// <summary>A file opened to read its lines, or to write text, one operation at a time.</summary>
    private sealed class DotNetFile(StreamReader? reader, StreamWriter? writer) : IWebLingoFile
    {
        private readonly object _gate = new();

        public string? ReadLine()
        {
            lock (_gate)
            {
                if (reader is null) throw new WebLingoFileSystemException(WebLingoFileErrorKind.Other, "it was opened to be written, not read");
                return Translated(reader.ReadLine);
            }
        }

        public void Write(string text)
        {
            lock (_gate)
            {
                if (writer is null) throw new WebLingoFileSystemException(WebLingoFileErrorKind.Other, "it was opened to be read, not written");
                Translated(() =>
                {
                    writer.Write(text);
                    return true;
                });
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                reader?.Dispose();
                writer?.Dispose();
            }
        }
    }
}

/// <summary>
/// An execution's scheduler host on .NET (section 7.8): blocking work on .NET's thread pool, and work posted back held in a
/// queue, which the worker runs, waiting on an event when it is empty. Adapter-only (section 7.14).
/// </summary>
public sealed class DotNetSchedulerHost : IWebLingoSchedulerHost
{
    private readonly Queue<Action> _posted = new();
    private readonly object _gate = new();

    public void RunBlocking(Action work) => ThreadPool.UnsafeQueueUserWorkItem(_ => work(), null);

    public void Post(Action work)
    {
        lock (_gate)
        {
            _posted.Enqueue(work);
            Monitor.Pulse(_gate);
        }
    }

    public bool RunPosted(int milliseconds)
    {
        Action[] works;
        lock (_gate)
        {
            if (_posted.Count == 0 && (milliseconds == 0 || !Monitor.Wait(_gate, milliseconds))) return false;
            works = _posted.ToArray();
            _posted.Clear();
        }
        foreach (var work in works) work();
        return works.Length > 0;
    }
}
