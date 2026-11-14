using WebLingo.Compiler;
namespace WebLingo.Pages.Cli;

/// <summary>
/// The templates a page includes, as <c>welp</c> finds them (specification, section 13, "Contracts"): a path starting with
/// <c>/</c> from the application's root, <paramref name="root"/>, or the page's folder when that is null, any other from
/// the including file's folder, <c>.wlp</c> added to one without an extension. A template outside the root cannot be
/// included. A template is named by its path from the current folder, with <c>/</c> between its folders, and the page by
/// the path it was given, so that each is one template however many include it.
/// </summary>
public sealed class WebLingoTemplateFileResolver(string pagePath, string? root) : IWebLingoSourceResolver
{
    private readonly string _pageFull = Path.GetFullPath(pagePath);
    private readonly string _rootFull = Path.GetFullPath(root ?? Path.GetDirectoryName(Path.GetFullPath(pagePath)) ?? ".");
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);

    public bool TryResolve(string includer, string path, out string name, out string source, out string? error)
    {
        name = "";
        source = "";
        error = null;
        try
        {
            var includerFull = includer == pagePath ? _pageFull : Path.GetFullPath(includer);
            var full = Path.GetFullPath(path.StartsWith('/')
                ? Path.Combine(_rootFull, path.TrimStart('/'))
                : Path.Combine(Path.GetDirectoryName(includerFull) ?? "", path));
            if (Path.GetExtension(full).Length == 0) full += ".wlp";
            var relative = Path.GetRelativePath(_rootFull, full);
            if (Path.IsPathRooted(relative) || (relative.StartsWith("..", StringComparison.Ordinal) && (relative.Length == 2 || relative[2] is '/' or '\\')))
            {
                error = "it is outside the application's root";
                return false;
            }
            name = full == _pageFull ? pagePath : Path.GetRelativePath(Directory.GetCurrentDirectory(), full).Replace('\\', '/');
            if (_texts.TryGetValue(name, out var known))
            {
                source = known;
                return true;
            }
            source = File.ReadAllText(full);
            _texts.Add(name, source);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            error = exception is FileNotFoundException or DirectoryNotFoundException ? "there is no such file" : exception.Message;
            return false;
        }
    }
}
