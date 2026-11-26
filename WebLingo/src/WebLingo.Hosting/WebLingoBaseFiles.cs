using WebLingo.Platform.Abstractions;
namespace WebLingo.Hosting;

/// <summary>
/// A site's files over its base's (specification, section 6.12, "A site over a base"): a path under the site's root,
/// <paramref name="root"/>, read from the site's folder where it has it, else from <paramref name="base"/>'s, at the same
/// path below it, so that the site holds only what it changes; a folder's files those of both, the site's first; whether
/// a file or a folder is there, either's. A path outside the root, and every write, is <paramref name="files"/>' as it is.
/// The root is the one its application is given, paths below it joined to it with <c>/</c> or <c>\</c>.
/// </summary>
public sealed class WebLingoBaseFiles(IWebLingoFileSystem files, string root, string @base) : IWebLingoFileSystem
{
    /// <summary>The base's folder, whose files the site's own come before.</summary>
    public string Base => @base;

    /// <summary>The same path in the base's folder, for a path under the root; null for one outside it.</summary>
    private string? InBase(string path)
    {
        if (path == root) return @base;
        if (path.Length > root.Length + 1 && path.StartsWith(root, StringComparison.Ordinal) && path[root.Length] is '/' or '\\')
        {
            return @base + "/" + path[(root.Length + 1)..];
        }
        return null;
    }

    /// <summary>Where a file to read is: the site's, where it has it, else the base's, else the site's, to be reported missing.</summary>
    private string Found(string path) => InBase(path) is { } based && !files.Exists(path) && files.Exists(based) ? based : path;

    public string ReadText(string path, string encoding) => files.ReadText(Found(path), encoding);

    public byte[] ReadBytes(string path) => files.ReadBytes(Found(path));

    public bool Exists(string path) => files.Exists(path) || (InBase(path) is { } based && files.Exists(based));

    public string ReadablePath(string path) => files.ReadablePath(Found(path));

    public bool IsFolder(string path) => files.IsFolder(path) || (!files.Exists(path) && InBase(path) is { } based && files.IsFolder(based));

    public IReadOnlyList<string> List(string path)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (files.IsFolder(path))
        {
            foreach (var name in files.List(path))
            {
                if (seen.Add(name)) names.Add(name);
            }
        }
        if (InBase(path) is { } based && files.IsFolder(based))
        {
            foreach (var name in files.List(based))
            {
                if (seen.Add(name)) names.Add(name);
            }
        }
        if (names.Count == 0 && !IsFolder(path)) return files.List(path); // Its own error, for a folder neither has.
        return names;
    }

    public IWebLingoFile Open(string path, WebLingoFileMode mode, string encoding) =>
        files.Open(mode is WebLingoFileMode.Read or WebLingoFileMode.ReadBytes ? Found(path) : path, mode, encoding);

    public void WriteText(string path, string text, string encoding, bool append) => files.WriteText(path, text, encoding, append);

    public void WriteBytes(string path, byte[] bytes, bool append) => files.WriteBytes(path, bytes, append);

    public void Copy(string source, string target) => files.Copy(Found(source), target);

    public void Remove(string path) => files.Remove(path);

    public void MakeFolder(string path) => files.MakeFolder(path);

    public void RemoveFolder(string path) => files.RemoveFolder(path);
}
