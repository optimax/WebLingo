using System.Text;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// The file system on .NET (section 7.4): <see cref="File"/>, <see cref="Directory"/> and streams, adapter-only (section
/// 7.14); each failure translated into a portable category, its reason naming no path (WL-P015). Given a site's
/// capabilities (section 7.13, "File capabilities"), each path is the real path they resolve it to, if the site may reach
/// it, for writing too where the operation writes; without them, any path the process may reach, a relative one from its
/// current folder. Stateless, and so safe on several threads at once.
/// </summary>
public sealed class DotNetFileSystem(IWebLingoFileCapabilities? capabilities = null) : IWebLingoFileSystem
{
    /// <summary>The path to reach for <paramref name="path"/>: the real path the capabilities allow, or the path itself without them.</summary>
    private string Reach(string path, bool write) => capabilities is null ? path : capabilities.Resolve(path, write);

    public string ReadText(string path, string encoding) => Translated(() =>
    {
        using var reader = Reader(Reach(path, false), encoding);
        return reader.ReadToEnd();
    });

    /// <summary>
    /// A reader in the encoding named, which skips that encoding's byte order mark, UTF-8's too, but takes no other mark as
    /// naming another encoding in its place.
    /// </summary>
    private static StreamReader Reader(string path, string encoding)
    {
        var named = EncodingOf(encoding); // An encoding it does not know is the error, before the file is opened.
        return new(Shared(path), named, detectEncodingFromByteOrderMarks: IsUtf8(encoding));
    }

    /// <summary>
    /// A file opened for reading that shares its writing and its deletion with every other program: so that a read never
    /// fails because another program replaces the file meanwhile, as a deployment replaces a site's pages, written beside
    /// them and moved over them, which with .NET's default sharing failed a read of a page being replaced on Windows, a
    /// page then served empty (24 November 2026, "A Page Replaced While WASP Reads It").
    /// </summary>
    private static FileStream Shared(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static bool IsUtf8(string name) =>
        string.Equals(name, "utf-8", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "utf8", StringComparison.OrdinalIgnoreCase);

    public void WriteText(string path, string text, string encoding, bool append) => Translated(() =>
    {
        using var writer = new StreamWriter(Reach(path, true), append, EncodingOf(encoding));
        writer.Write(text);
        return true;
    });

    public byte[] ReadBytes(string path) => Translated(() =>
    {
        using var stream = Shared(Reach(path, false));
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    });

    public void WriteBytes(string path, byte[] bytes, bool append) => Translated(() =>
    {
        using var stream = new FileStream(Reach(path, true), append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        return true;
    });

    public void Copy(string source, string target) => Translated(() =>
    {
        var from = Reach(source, false);
        var to = Reach(target, true);
        if (Directory.Exists(from)) throw new WebLingoFileSystemException(WebLingoFileErrorKind.Other, "it is a folder, and only a file is copied");
        File.Copy(from, to, overwrite: true);
        return true;
    });

    public bool Exists(string path) => Translated(() =>
    {
        var reached = Reach(path, false);
        return File.Exists(reached) || Directory.Exists(reached);
    });

    public bool IsFolder(string path) => Translated(() => Directory.Exists(Reach(path, false)));

    public string ReadablePath(string path) => Translated(() => Path.GetFullPath(Reach(path, false)));

    public void Remove(string path) => Translated(() =>
    {
        var reached = Reach(path, true);
        // File.Delete does nothing for a file that is not there, which is an error here.
        if (!File.Exists(reached)) throw new FileNotFoundException();
        File.Delete(reached);
        return true;
    });

    public IReadOnlyList<string> List(string path) => Translated(() =>
    {
        var names = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(Reach(path, false))) names.Add(Path.GetFileName(entry));
        names.Sort(StringComparer.Ordinal);
        return (IReadOnlyList<string>)names;
    });

    public void MakeFolder(string path) => Translated(() => Directory.CreateDirectory(Reach(path, true)));

    public void RemoveFolder(string path) => Translated(() =>
    {
        var reached = Reach(path, true);
        if (Directory.EnumerateFileSystemEntries(reached).Any()) throw new WebLingoFileSystemException(WebLingoFileErrorKind.NotEmpty, "it is not empty");
        Directory.Delete(reached);
        return true;
    });

    public IWebLingoFile Open(string path, WebLingoFileMode mode, string encoding) => Translated<IWebLingoFile>(() => mode switch
    {
        WebLingoFileMode.Read => new DotNetFile(Reader(Reach(path, false), encoding), null, null),
        WebLingoFileMode.ReadBytes => new DotNetFile(null, null, Shared(Reach(path, false))),
        WebLingoFileMode.WriteBytes => new DotNetFile(null, null, new FileStream(Reach(path, true), FileMode.Create, FileAccess.Write, FileShare.Read)),
        WebLingoFileMode.AppendBytes => new DotNetFile(null, null, new FileStream(Reach(path, true), FileMode.Append, FileAccess.Write, FileShare.Read)),
        _ => new DotNetFile(null, new StreamWriter(Reach(path, true), mode == WebLingoFileMode.Append, EncodingOf(encoding)), null)
    });

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

    /// <summary>A file opened to read its lines, or to write text, or to read or write its bytes, one operation at a time.</summary>
    private sealed class DotNetFile(StreamReader? reader, StreamWriter? writer, FileStream? bytes) : IWebLingoFile
    {
        private readonly object _gate = new();

        public string? ReadLine()
        {
            lock (_gate)
            {
                if (reader is null) throw Misused(bytes is not null ? "it was opened for its bytes, not its text" : "it was opened to be written, not read");
                return Translated(reader.ReadLine);
            }
        }

        public void Write(string text)
        {
            lock (_gate)
            {
                if (writer is null) throw Misused(bytes is not null ? "it was opened for its bytes, not its text" : "it was opened to be read, not written");
                Translated(() =>
                {
                    writer.Write(text);
                    return true;
                });
            }
        }

        public byte[]? ReadBytes(int count)
        {
            lock (_gate)
            {
                if (bytes is null) throw Misused("it was opened for its text, not its bytes");
                if (!bytes.CanRead) throw Misused("it was opened to be written, not read");
                return Translated(() =>
                {
                    // No more than what is left, so that a large count makes no large buffer for a small file.
                    var size = (int)Math.Min(count, Math.Max(0, bytes.Length - bytes.Position));
                    var buffer = new byte[size];
                    var read = 0;
                    while (read < size)
                    {
                        var got = bytes.Read(buffer, read, size - read);
                        if (got == 0) break;
                        read += got;
                    }
                    if (read == 0) return null;
                    if (read < size) Array.Resize(ref buffer, read);
                    return buffer;
                });
            }
        }

        public void WriteBytes(byte[] data)
        {
            lock (_gate)
            {
                if (bytes is null) throw Misused("it was opened for its text, not its bytes");
                if (!bytes.CanWrite) throw Misused("it was opened to be read, not written");
                Translated(() =>
                {
                    bytes.Write(data);
                    return true;
                });
            }
        }

        private static WebLingoFileSystemException Misused(string reason) => new(WebLingoFileErrorKind.Other, reason);

        public void Dispose()
        {
            lock (_gate)
            {
                reader?.Dispose();
                writer?.Dispose();
                bytes?.Dispose();
            }
        }
    }
}

/// <summary>
/// An execution's scheduler host on .NET (section 7.8): blocking work on WebLingo's own threads (<see cref="DotNetBlockingPool"/>),
/// and work posted back held in a queue, which the worker runs, waiting on an event when it is empty. Adapter-only (section
/// 7.14).
/// </summary>
public sealed class DotNetSchedulerHost : IWebLingoSchedulerHost
{
    private readonly Queue<Action> _posted = new();
    private readonly object _gate = new();

    public void RunBlocking(Action work, bool opensConnection) => DotNetBlockingPool.Run(work, opensConnection);

    public void RunBlocking(Action work, IWebLingoConnectionGate gate) => DotNetBlockingPool.Run(work, gate);

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
