using WebLingo.Cli;
using WebLingo.StdLib;
using WebLingo.Templates;
namespace WebLingo.Pages.Cli;

/// <summary>
/// <c>welp</c>, WebLingo Pages' command line (specification, section 13, "Contracts"): <c>run</c>, which compiles a page
/// (a <c>.wlp</c>) with the template compiler and writes it to standard output, <c>check</c>, and <c>--version</c>, with
/// <c>weblingo</c>'s diagnostics, errors and exit codes (<see cref="WebLingoCommandLine"/>). It is the host: it registers
/// the standard library's built-ins and the ones a page calls (<see cref="WebLingoTemplateBuiltins"/>).
/// </summary>
public static class WebLingoPagesCommandLine
{
    public const string Version = "WebLingo Pages M0.12 (pre-alpha)";

    /// <summary><c>welp</c>: pages, <c>.wlp</c> files, compiled by the template compiler.</summary>
    public static WebLingoCommandLineProgram Pages { get; } =
        new("welp", Version, ".wlp", WebLingoTemplateBuiltins.Create, (page, options) => new WebLingoTemplateCompiler(options).Compile(page));

    public static int Run(string[] args, TextWriter output, TextWriter error) => WebLingoCommandLine.Run(Pages, args, output, error);
}
