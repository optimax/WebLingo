using WebLingo.Bytecode;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Hosting;

/// <summary>What a route leads to: a page, a <c>.wlg</c> that may be a route handler, or a file served as it is.</summary>
public enum WebLingoRouteKind { Page, Handler, File }

/// <summary>A route: its kind, and its file, from the application's root, with <c>/</c> between its folders.</summary>
public sealed record WebLingoRoute(WebLingoRouteKind Kind, string File);

/// <summary>
/// An application's routes (specification, section 6.12, "Routes"), made once for each generation from the files under its
/// root: a page or a <c>.wlg</c> by its path without its extension, <c>/products</c> for <c>products.wasp</c>, a folder's
/// <c>index</c> by the folder's path, <c>/</c> and <c>/docs/</c> (and <c>/docs</c>, when nothing else is that), and any
/// other file by its own path. A path is compared as it is, its case included, on every platform. Never routed: files
/// and folders whose names start with <c>.</c>, WLBC files, and, in any folder, whatever their names' case, since
/// Windows finds them so, the site's configuration (<c>wasp.config</c>), a script's (<c>weblingo.config</c>), and IIS's
/// <c>web.config</c>, which IIS reads in a site's folders too; nor, as files served as they are, a page or a <c>.wlg</c> by
/// its own name, nor one whose extension has another case, <c>DB.WLG</c>, which Windows imports as <c>db.wlg</c>, and which
/// is no route of its own, a page or a handler routed only by <c>.wasp</c> and <c>.wlg</c> as written (until 25 November
/// 2026, "Security Review of WASP's Request Path", such a file was served as it is, its source with it, and so was a
/// folder's <c>web.config</c>); nor a file whose extension is one of <see cref="Sensitive"/>, whatever its case, unless the
/// site serves that extension (section 6.12, "Sensitive files"), so that a request for it is a 404.
/// </summary>
public sealed class WebLingoRouteTable
{
    /// <summary>A site's configuration, in its root (specification, section 6.12).</summary>
    public const string ConfigurationFile = "wasp.config";

    private static readonly string[] Unrouted = [ConfigurationFile, "weblingo.config", "web.config"];

    /// <summary>
    /// The extensions of files a site never serves as they are unless its configuration's <c>"serve"</c> allows them (the
    /// owner's answer, 25 November 2026, "Security Headers and Sensitive Files in WASP", A2 (a)), each with its dot, in lower
    /// case: databases, <c>.db</c>, <c>.db3</c>, <c>.sqlite</c>, <c>.sqlite3</c>, <c>.mdb</c>, <c>.accdb</c>, <c>.mdf</c> and
    /// <c>.ldf</c>; SQL, <c>.sql</c>; backups and an editor's copies, <c>.bak</c>, <c>.backup</c>, <c>.old</c>, <c>.orig</c>,
    /// <c>.swp</c> and <c>.tmp</c>; logs, <c>.log</c>; environments and configurations, <c>.env</c>, <c>.config</c> and
    /// <c>.ini</c>; and keys and their stores, <c>.pfx</c>, <c>.p12</c>, <c>.pem</c> and <c>.key</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> Sensitive =
    [
        ".db", ".db3", ".sqlite", ".sqlite3", ".mdb", ".accdb", ".mdf", ".ldf", ".sql", ".bak", ".backup", ".old", ".orig", ".swp", ".tmp",
        ".log", ".env", ".config", ".ini", ".pfx", ".p12", ".pem", ".key"
    ];

    private readonly Dictionary<string, WebLingoRoute> _routes = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _served = [];

    private WebLingoRouteTable() { }

    /// <summary>How many paths have routes.</summary>
    public int Count => _routes.Count;

    /// <summary>The route of a request's path, or null if it has none.</summary>
    public WebLingoRoute? Find(string path) => _routes.TryGetValue(path, out var route) ? route : null;

    /// <summary>
    /// The routes of the files under <paramref name="root"/>, a file whose extension is <see cref="Sensitive"/> routed only
    /// where <paramref name="served"/> has it; null, with why, if two files are one route or the root cannot be read.
    /// </summary>
    public static WebLingoRouteTable? Make(IWebLingoFileSystem files, string root, IReadOnlyList<string> served, out string? error)
    {
        var table = new WebLingoRouteTable { _served = served };
        error = null;
        try
        {
            if (!files.IsFolder(root))
            {
                error = "the application's root is not a folder";
                return null;
            }
            table.Add(files, root, "", ref error);
        }
        catch (WebLingoFileSystemException exception)
        {
            error = "the application's files cannot be read: " + exception.Message;
        }
        if (error is null)
        {
            // A folder's path without its closing slash leads to its index too, unless something else is that path.
            foreach (var (path, route) in new List<KeyValuePair<string, WebLingoRoute>>(table._routes))
            {
                if (path.Length > 1 && path.EndsWith('/') && route.Kind != WebLingoRouteKind.File) table._routes.TryAdd(path[..^1], route);
            }
        }
        return error is null ? table : null;
    }

    /// <summary>Whether a file of <paramref name="extension"/>, in lower case, is one a site does not serve, <see cref="Sensitive"/>'s and not served.</summary>
    private bool IsRefused(string extension)
    {
        var sensitive = false;
        foreach (var refused in Sensitive) sensitive |= refused == extension;
        if (!sensitive) return false;
        foreach (var allowed in _served)
        {
            if (allowed == extension) return false;
        }
        return true;
    }

    private static bool IsUnrouted(string name)
    {
        foreach (var unrouted in Unrouted)
        {
            if (name.Equals(unrouted, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private void Add(IWebLingoFileSystem files, string root, string folder, ref string? error)
    {
        foreach (var name in files.List(folder.Length == 0 ? root : root + "/" + folder))
        {
            if (error is not null) return;
            if (name.StartsWith('.')) continue;
            var file = folder.Length == 0 ? name : folder + "/" + name;
            if (files.IsFolder(root + "/" + file))
            {
                Add(files, root, file, ref error);
                continue;
            }
            if (IsUnrouted(name)) continue;
            var dot = name.LastIndexOf('.');
            var extension = dot > 0 ? name[dot..] : "";
            if (WebLingoBytecodeFile.IsBytecodeExtension(extension.ToLowerInvariant())) continue;
            var source = extension.Equals(".wasp", StringComparison.OrdinalIgnoreCase) || extension.Equals(".wlg", StringComparison.OrdinalIgnoreCase);
            if (source && extension is not (".wasp" or ".wlg")) continue; // Source, never served, nor a route.
            if (!source)
            {
                if (IsRefused(extension.ToLowerInvariant())) continue; // A database, a log, a key: never served (section 6.12).
                // A page or a handler of the same path is the route, whichever is listed first.
                _routes.TryAdd("/" + file, new WebLingoRoute(WebLingoRouteKind.File, file));
                continue;
            }
            // A page or a handler is compiled from its name, which its includes and imports are found from: one whose name no
            // portable file has (WebLingoSiteFiles.Normalize: \, : or a control character, as on Linux) could never be.
            if (WebLingoSiteFiles.Normalize(file) is null) continue;
            var stem = name[..dot];
            var path = "/" + (folder.Length == 0 ? "" : folder + "/") + (stem == "index" ? "" : stem);
            var route = new WebLingoRoute(extension == ".wasp" ? WebLingoRouteKind.Page : WebLingoRouteKind.Handler, file);
            if (_routes.TryGetValue(path, out var other) && other.Kind != WebLingoRouteKind.File)
            {
                error = $"'{other.File}' and '{file}' are both the route {path}";
                return;
            }
            _routes[path] = route;
        }
    }
}
