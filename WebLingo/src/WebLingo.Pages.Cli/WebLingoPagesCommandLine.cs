using WebLingo.Cli;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.StdLib;
using WebLingo.Templates;
namespace WebLingo.Pages.Cli;

/// <summary>
/// <c>welp</c>, WebLingo Pages' command line (specification, section 13, "Contracts"): <c>run</c>, which compiles a page
/// (a <c>.wlp</c>) with the template compiler and writes it to standard output, <c>check</c>, and <c>--version</c>, with
/// <c>weblingo</c>'s diagnostics, errors and exit codes (<see cref="WebLingoCommandLine"/>); <c>--root &lt;folder&gt;</c>,
/// before the page, names the application's root, which an include's path starting with <c>/</c> is from, the page's
/// folder when it is left out. It is the host: it registers the standard library's built-ins and the ones a page calls
/// (<see cref="WebLingoTemplateBuiltins"/>), and finds the templates a page includes (<see cref="WebLingoTemplateFileResolver"/>).
/// </summary>
public static class WebLingoPagesCommandLine
{
    public const string Version = "WebLingo Pages M0.12 (pre-alpha)";

    /// <summary><c>welp</c>: pages, <c>.wlp</c> files, compiled by the template compiler, with the application's root given, or not.</summary>
    public static WebLingoCommandLineProgram Pages(string? root) =>
        new("welp", Version, ".wlp", WebLingoTemplateBuiltins.Create, (page, options) => Compile(page, options, root))
        {
            Options = "[--root <folder>] ",
            OptionsHelp = ["  --root the application's root, which an include's path starting with / is from; the page's folder when left out"]
        };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        // welp run|check [--root <folder>] [--scheme name=folder]… <page.wlp> …: --root taken out, among the options before
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

    private static WebLingoCommandLineCompilation Compile(WebLingoSourceText page, WebLingoCompilerOptions options, string? root)
    {
        var compiler = new WebLingoTemplateCompiler(options, new WebLingoTemplateFileResolver(page.Name, root));
        var result = compiler.Compile(page);
        return new WebLingoCommandLineCompilation(result)
        {
            Locate = compiler.Sources.Locate, TextOf = compiler.Sources.TextOf, Components = compiler.Components
        };
    }
}
