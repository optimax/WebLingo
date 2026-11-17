using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// A site's file capabilities on .NET (specification, section 7.13, "File capabilities"; M0.16): a script's path made
/// absolute from the site's root, its <c>.</c> and <c>..</c> resolved, then its real path, each folder or file along it
/// that is a link or a junction replaced by what it finally points to; compared with the folders the site may read and
/// write, resolved so when it is made, as the file system compares names, ignoring case on Windows and macOS; a Windows
/// device's name (<c>CON</c>, <c>NUL</c>, <c>COM1</c>, <c>\\.\…</c>) never reached. The file system then opens the real
/// path, not the script's, so that what is checked is what is opened. Adapter-only (section 7.14); stateless once made,
/// and so safe on several threads at once.
/// </summary>
public sealed class DotNetFileCapabilities : IWebLingoFileCapabilities
{
    private static readonly StringComparison Names =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _root;
    private readonly string[] _read;
    private readonly string[] _write;

    /// <summary>
    /// The capabilities of a site whose root is <paramref name="root"/>, which may read the folders
    /// <paramref name="read"/> and read and write the folders <paramref name="write"/>, each absolute, or relative to the
    /// root.
    /// </summary>
    public DotNetFileCapabilities(string root, IReadOnlyList<string> read, IReadOnlyList<string> write)
    {
        _root = Path.GetFullPath(root);
        _write = [.. write.Select(folder => RealPath(Path.GetFullPath(folder, _root)))];
        _read = [.. read.Select(folder => RealPath(Path.GetFullPath(folder, _root))), .. _write];
    }

    public string Resolve(string path, bool write)
    {
        if (IsDevice(path)) throw NotAllowed("it names a device");
        string real;
        try
        {
            real = RealPath(Path.GetFullPath(path, _root));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new WebLingoFileSystemException(WebLingoFileErrorKind.InvalidPath, "its path is not a valid one");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw NotAllowed("its links cannot be followed");
        }
        if (IsDevice(real)) throw NotAllowed("it names a device");
        foreach (var folder in write ? _write : _read)
        {
            if (IsWithin(real, folder)) return real;
        }
        if (_read.Length == 0) throw NotAllowed("this site may reach no file, its configuration allowing no folder");
        throw NotAllowed(write ? "it is not in a folder this site may write" : "it is not in a folder this site may read");
    }

    private static WebLingoFileSystemException NotAllowed(string reason) => new(WebLingoFileErrorKind.NotAllowed, reason);

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/>, or within it.</summary>
    private static bool IsWithin(string path, string folder)
    {
        if (!path.StartsWith(folder, Names)) return false;
        return path.Length == folder.Length || IsSeparator(folder[^1]) || IsSeparator(path[folder.Length]);
    }

    private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

    /// <summary>
    /// An absolute path's real path: from its root, each part that is there and is a link or a junction replaced by its final
    /// target, the parts after the first that is not there kept as they are, since no link can be among them.
    /// </summary>
    private static string RealPath(string full)
    {
        var root = Path.GetPathRoot(full) ?? "";
        var current = root;
        var parts = full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var next = Path.Combine(current, parts[i]);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (info.LinkTarget is not null)
            {
                next = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next;
            }
            else if (!info.Exists)
            {
                current = Path.Combine([next, .. parts[(i + 1)..]]);
                break;
            }
            current = next;
        }
        current = Path.GetFullPath(current);
        return current.Length > root.Length ? current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : current;
    }

    /// <summary>
    /// Whether a path names a Windows device, by a device's path (<c>\\.\</c>, <c>\\?\</c>) or by a device's name among its
    /// parts, with or without an extension (<c>CON</c>, <c>nul.txt</c>, <c>COM1</c>); never elsewhere, where they are names
    /// as any other.
    /// </summary>
    private static bool IsDevice(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (path.Length >= 4 && IsSeparator(path[0]) && IsSeparator(path[1]) && path[2] is '.' or '?' && IsSeparator(path[3])) return true;
        if (path.StartsWith(@"\??\", StringComparison.Ordinal)) return true;
        foreach (var part in path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part;
            var end = name.IndexOfAny(['.', ':']);
            if (end >= 0) name = name[..end];
            name = name.TrimEnd(' ').ToUpperInvariant();
            if (name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" || (name.Length == 4 && name[..3] is "COM" or "LPT"
                    && (char.IsAsciiDigit(name[3]) || name[3] is '¹' or '²' or '³')))
            {
                return true;
            }
        }
        return false;
    }
}
