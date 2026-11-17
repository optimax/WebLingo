using WebLingo.Cli;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Hosting;
using WebLingo.StdLib;
using WebLingo.Templates;
namespace WebLingo.Wasp.Cli;

/// <summary>
/// <c>wasp</c>, WASP's command line (specification, section 13, "Contracts"): <c>run</c>, which compiles a page
/// (a <c>.wasp</c>) with the template compiler and writes it to standard output, <c>check</c>, and <c>--version</c>, with
/// <c>weblingo</c>'s diagnostics, errors and exit codes (<see cref="WebLingoCommandLine"/>); <c>--root &lt;folder&gt;</c>,
/// before the page, names the application's root, which an include's path starting with <c>/</c> is from, the page's
/// folder when it is left out; and the page's folder's <c>wasp.config</c> gives its schemes. It is the host: it registers
/// the standard library's built-ins and the ones a page calls
/// (<see cref="WebLingoTemplateBuiltins"/>), and finds the templates a page includes (<see cref="WebLingoTemplateFileResolver"/>).
/// </summary>
public static class WebLingoWaspCommandLine
{
    public const string Version = "WASP M0.12 (pre-alpha)";

    /// <summary><c>wasp</c>: pages, <c>.wasp</c> files, compiled by the template compiler, with the application's root given, or not.</summary>
    public static WebLingoCommandLineProgram Pages(string? root) =>
        new("wasp", Version, ".wasp", WebLingoTemplateBuiltins.Create, (page, options) => Compile(page, options, root))
        {
            Configuration = WebLingoRouteTable.ConfigurationFile,
            ReachesAnyHost = false,
            Options = "[--root <folder>] ",
            OptionsHelp =
            [
                "  --root the application's root, which an include's path starting with / is from; the page's folder when left out",
                $"  serve     wasp serve [--root <folder>] [--port <port>] [--scheme <name>=<folder> …] serves the application at the root, "
                + $"the current folder by default, on port {WebLingoWaspServer.DefaultPort}"
            ]
        };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length >= 1 && args[0] == "serve") return Serve(args, output, error);
        // wasp run|check [--root <folder>] [--scheme name=folder]… <page.wasp> …: --root taken out, among the options before
        // the page, the rest what weblingo's command line takes.
        string? root = null;
        var rest = new List<string>(args);
        for (var at = 1; args.Length >= 2 && args[0] is "run" or "check" && at < rest.Count && rest[at].StartsWith("--", StringComparison.Ordinal);)
        {
            if (rest[at] != "--root")
            {
                at += 2;
                continue;
            }
            if (root is not null || at + 2 >= rest.Count) return WebLingoCommandLine.Run(Pages(null), ["--root"], output, error);
            root = rest[at + 1];
            rest.RemoveRange(at, 2);
        }
        return WebLingoCommandLine.Run(Pages(root), [.. rest], output, error);
    }

    /// <summary>
    /// <c>wasp serve [--root &lt;folder&gt;] [--port &lt;port&gt;] [--scheme name=folder]…</c>: the application at the root, the
    /// current folder by default, served until the process is stopped (<see cref="WebLingoWaspServer"/>).
    /// </summary>
    private static int Serve(string[] args, TextWriter output, TextWriter error)
    {
        string? root = null;
        var port = WebLingoWaspServer.DefaultPort;
        var schemes = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var at = 1; at < args.Length; at += 2)
        {
            var value = at + 1 < args.Length ? args[at + 1] : null;
            var equals = value?.IndexOf('=', StringComparison.Ordinal) ?? -1;
            var given = (args[at], value) switch
            {
                ("--root", { } folder) when root is null => (root = folder) is not null,
                ("--port", { } number) => int.TryParse(number, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out port) && port is > 0 and < 65536,
                ("--scheme", { } scheme) => equals > 0 && WebLingoSchemes.IsName(scheme[..equals]) && equals + 1 < scheme.Length
                                            && schemes.TryAdd(scheme[..equals], scheme[(equals + 1)..]),
                _ => false
            };
            if (given) continue;
            error.WriteLine($"'{args[at]}{(value is null ? "" : " " + value)}' is not --root <folder>, --port <port> or --scheme <name>=<folder>, "
                            + "or is given twice.");
            output.WriteLine("Usage: wasp serve [--root <folder>] [--port <port>] [--scheme <name>=<folder> …]");
            return WebLingoCommandLine.UsageError;
        }
        var server = WebLingoWaspServer.Create(root, port, schemes, out var why, out var environment, output);
        if (server is null)
        {
            error.WriteLine(why);
            return WebLingoCommandLine.FileUnreadable;
        }
        output.WriteLine(WebLingoWaspServer.UnderIis
            ? $"WASP serving under IIS ({environment})."
            : $"WASP serving http://localhost:{port}/ ({environment}); Ctrl+C stops it.");
        output.Flush();
        server.Run();
        return WebLingoCommandLine.Success;
    }

    private static WebLingoCommandLineCompilation Compile(WebLingoSourceText page, WebLingoCompilerOptions options, string? root)
    {
        var rootFull = Path.GetFullPath(root ?? Path.GetDirectoryName(Path.GetFullPath(page.Name)) ?? ".");
        options = options with { Resolver = options.Resolver is { } modules ? new FromRoot(modules, rootFull) : null };
        var compiler = new WebLingoTemplateCompiler(options, new WebLingoTemplateFileResolver(page.Name, root));
        var result = compiler.Compile(page);
        return new WebLingoCommandLineCompilation(result)
        {
            Locate = compiler.Sources.Locate, TextOf = compiler.Sources.TextOf, Components = compiler.Components
        };
    }

    /// <summary>
    /// The modules a page imports, as <c>wasp serve</c> finds them (specification, section 6.12): a path starting with
    /// <c>/</c> from the application's root, as an include's is, not from the drive's, so that a page imports the same
    /// files whichever command runs it; any other as a script's import, from the importing file's folder.
    /// </summary>
    private sealed class FromRoot(IWebLingoSourceResolver modules, string root) : IWebLingoSourceResolver
    {
        public bool TryResolve(string importer, string path, out string name, out string source, out string? error) =>
            path.StartsWith('/')
                ? modules.TryResolve(Path.Combine(root, "index.wasp"), "./" + path.TrimStart('/'), out name, out source, out error)
                : modules.TryResolve(importer, path, out name, out source, out error);
    }
}
