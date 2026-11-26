using WebLingo.Platform.Abstractions;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// Files held in memory, by their full paths with <c>/</c> between folders, a folder being there when a file is in it; and
/// how many times each file was read, so that a test can see what was compiled.
/// </summary>
internal sealed class MemoryFileSystem : IWebLingoFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public Dictionary<string, int> Reads { get; } = new(StringComparer.Ordinal);

    /// <summary>The files whose next read fails, once, as a file another program is replacing may fail a read.</summary>
    public HashSet<string> FailOnce { get; } = new(StringComparer.Ordinal);

    public MemoryFileSystem Add(string path, string text)
    {
        _files[path] = text;
        return this;
    }

    public string ReadText(string path, string encoding)
    {
        if (!_files.TryGetValue(path, out var text)) throw new WebLingoFileSystemException(WebLingoFileErrorKind.NotFound, "there is no such file");
        if (FailOnce.Remove(path)) throw new WebLingoFileSystemException(WebLingoFileErrorKind.Other, "it is being used by another process");
        Reads[path] = Reads.GetValueOrDefault(path) + 1;
        return text;
    }

    public bool Exists(string path) => _files.ContainsKey(path) || IsFolder(path);

    public string ReadablePath(string path) => path.StartsWith("secret/", StringComparison.Ordinal)
        ? throw new WebLingoFileSystemException(WebLingoFileErrorKind.NotAllowed, "the site may not read it")
        : "/memory/" + path;

    public bool IsFolder(string path) => _files.Keys.Any(file => file.StartsWith(path + "/", StringComparison.Ordinal));

    public IReadOnlyList<string> List(string path)
    {
        if (!IsFolder(path)) throw new WebLingoFileSystemException(WebLingoFileErrorKind.NotFound, "there is no such folder");
        return [.. _files.Keys.Where(file => file.StartsWith(path + "/", StringComparison.Ordinal))
            .Select(file => file[(path.Length + 1)..].Split('/')[0]).Distinct().Order(StringComparer.Ordinal)];
    }

    public void WriteText(string path, string text, string encoding, bool append) => throw Unsupported();
    public byte[] ReadBytes(string path) => throw Unsupported();
    public void WriteBytes(string path, byte[] bytes, bool append) => throw Unsupported();
    public void Copy(string source, string target) => throw Unsupported();
    public void Remove(string path) => throw Unsupported();
    public void MakeFolder(string path) => throw Unsupported();
    public void RemoveFolder(string path) => throw Unsupported();
    public IWebLingoFile Open(string path, WebLingoFileMode mode, string encoding) => throw Unsupported();

    private static WebLingoFileSystemException Unsupported() => new(WebLingoFileErrorKind.Other, "not in these tests");
}
