namespace WebLingo.Platform.Abstractions;

/// <summary>
/// The platform's files and folders (specification, section 7.4, WL-P004), which <c>std:fs</c> reaches: each operation
/// synchronous, blocking its caller, a failure a <see cref="WebLingoFileSystemException"/>; the asynchronous forms are
/// these run off the VM's workers (<see cref="IWebLingoSchedulerHost.RunBlocking"/>). A path is the script's, relative to
/// the process's current folder or absolute, with <c>/</c> or the platform's separator between its folders.
/// </summary>
public interface IWebLingoFileSystem
{
    /// <summary>A whole file's text, in the encoding named (<c>utf-8</c>, its byte order mark skipped).</summary>
    string ReadText(string path, string encoding);

    /// <summary>Writes a file's text, in the encoding named, a UTF-8 file without a byte order mark, after its text if <paramref name="append"/>.</summary>
    void WriteText(string path, string text, string encoding, bool append);

    /// <summary>Whether a file or a folder is there.</summary>
    bool Exists(string path);

    /// <summary>Whether a folder is there; a file is not one.</summary>
    bool IsFolder(string path);

    /// <summary>Removes a file.</summary>
    void Remove(string path);

    /// <summary>The names of what a folder holds, files and folders, in ordinal order.</summary>
    IReadOnlyList<string> List(string path);

    /// <summary>Makes a folder, and the folders above it that are not there; one there already is not an error.</summary>
    void MakeFolder(string path);

    /// <summary>Removes an empty folder.</summary>
    void RemoveFolder(string path);

    /// <summary>Opens a file as a handle, to read its lines or to write text, in the encoding named.</summary>
    IWebLingoFile Open(string path, WebLingoFileMode mode, string encoding);
}

/// <summary>A file opened as a handle, whose operations, each blocking, run one at a time; disposed, it is closed.</summary>
public interface IWebLingoFile : IDisposable
{
    /// <summary>Its next line, without its line break, or null at its end.</summary>
    string? ReadLine();

    /// <summary>Writes text at its end.</summary>
    void Write(string text);
}

/// <summary>How a file is opened: to read it; to write it anew, made if it is not there; or to write after its text.</summary>
public enum WebLingoFileMode { Read, Write, Append }

/// <summary>
/// What failed, a portable category, not the host's exception (section 7.13, WL-P015); <see cref="NotAllowed"/>, a path a
/// site's scripts may not reach (<see cref="IWebLingoFileCapabilities"/>).
/// </summary>
public enum WebLingoFileErrorKind { NotFound, AccessDenied, AlreadyExists, NotEmpty, InvalidPath, InvalidText, UnknownEncoding, Other, NotAllowed }

/// <summary>
/// The folders a site's scripts may reach (specification, section 7.13, "File capabilities"): a script's path, relative to
/// the site's root or absolute, resolved to the file it names, its links and junctions followed, and checked against the
/// folders its configuration lets it read, or write, a folder it may write being one it may read too.
/// </summary>
public interface IWebLingoFileCapabilities
{
    /// <summary>
    /// The real path <paramref name="path"/> names, if the site may reach it, to write it too if <paramref name="write"/>;
    /// a <see cref="WebLingoFileSystemException"/> of <see cref="WebLingoFileErrorKind.NotAllowed"/>, whose reason names no
    /// path, otherwise.
    /// </summary>
    string Resolve(string path, bool write);
}

/// <summary>
/// A file system's failure: its category, and why, in words that name no path, so that the script's own path is the
/// only one its error names, never the host's (section 7.13).
/// </summary>
public sealed class WebLingoFileSystemException(WebLingoFileErrorKind kind, string reason) : Exception(reason)
{
    public WebLingoFileErrorKind Kind { get; } = kind;
}

/// <summary>
/// The host's side of an execution's scheduler (section 7.8, WL-P007, WL-P008): blocking work run off the VM's workers,
/// on a bounded pool of the host's threads; and work posted back, from any thread, for the execution's worker, which runs
/// it when it has nothing else to run.
/// </summary>
public interface IWebLingoSchedulerHost
{
    /// <summary>Runs blocking work on one of the host's threads, not the worker's.</summary>
    void RunBlocking(Action work);

    /// <summary>Posts work for the execution's worker, from any thread.</summary>
    void Post(Action work);

    /// <summary>
    /// On the worker: runs the work posted so far; with none, waits for some, up to <paramref name="milliseconds"/>
    /// (-1 without end), then runs it; whether any ran.
    /// </summary>
    bool RunPosted(int milliseconds);
}
