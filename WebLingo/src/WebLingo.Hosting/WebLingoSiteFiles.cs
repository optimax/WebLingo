using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Hosting;

/// <summary>
/// The files an application's pages include, are laid out by, render and import (specification, sections 6.12 and 13),
/// read through the platform's files: a path starting with <c>/</c> from the application's root, any other from the
/// including or importing file's folder, <paramref name="extension"/> added to one without an extension. A file is named
/// by its path from the root, with <c>/</c> between its folders, never by the host's, so that an error names only the
/// application's own files; a path outside the root cannot be had.
/// </summary>
internal sealed class WebLingoSiteFiles(IWebLingoFileSystem files, string root, string extension) : IWebLingoSourceResolver
{
    /// <summary>The files read, by name, for their errors' lines and columns.</summary>
    public Dictionary<string, WebLingoSourceText> Texts { get; } = new(StringComparer.Ordinal);

    public bool TryResolve(string includer, string path, out string name, out string source, out string? error)
    {
        (name, source, error) = ("", "", null);
        if (Normalize(path.StartsWith('/') ? path : Folder(includer) + path) is not { } normal)
        {
            error = "it is outside the application's root";
            return false;
        }
        name = Name(normal);
        if (Texts.TryGetValue(name, out var known))
        {
            source = known.Text;
            return true;
        }
        if (Read(name) is not { } text)
        {
            error = "there is no such file";
            return false;
        }
        source = text;
        Texts.Add(name, new WebLingoSourceText(text, name));
        return true;
    }

    /// <summary>A file's text, by its name from the root, or null if it cannot be read.</summary>
    public string? Read(string name)
    {
        try
        {
            var full = root + "/" + name;
            return files.Exists(full) && !files.IsFolder(full) ? files.ReadText(full, "utf-8") : null;
        }
        catch (WebLingoFileSystemException)
        {
            return null;
        }
    }

    private string Name(string normal)
    {
        var last = normal.LastIndexOf('/');
        return normal.IndexOf('.', last + 1) < 0 ? normal + extension : normal;
    }

    /// <summary>The folder of a file named from the root, with its closing <c>/</c>; empty for the root's own files.</summary>
    private static string Folder(string name)
    {
        var last = name.LastIndexOf('/');
        return last < 0 ? "" : name[..(last + 1)];
    }

    /// <summary>
    /// A path from the root, its <c>.</c> segments dropped and its <c>..</c> ones taking off the folder before them; null for
    /// one that leaves the root, names nothing, or has a character no portable file name has (<c>\</c>, <c>:</c>, a control).
    /// </summary>
    public static string? Normalize(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            foreach (var c in segment)
            {
                if (c is '\\' or ':' or < ' ') return null;
            }
            segments.Add(segment);
        }
        return segments.Count == 0 ? null : string.Join("/", segments);
    }
}
