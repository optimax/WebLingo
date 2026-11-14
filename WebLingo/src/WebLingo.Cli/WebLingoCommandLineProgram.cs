using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.Cli;

/// <summary>
/// What a command line compiles (<see cref="WebLingoCommandLine"/>): <c>weblingo</c>'s scripts, or another program's
/// files, such as <c>welp</c>'s pages (<c>WebLingo.Pages.Cli</c>, specification, section 13): its name, its version, its
/// files' extension, the built-ins it gives besides the standard library's, and its compiler. Everything else, the
/// commands, their output, the diagnostics and the exit codes, the programs share.
/// </summary>
public sealed record WebLingoCommandLineProgram(
    string Name,
    string Version,
    string Extension,
    Func<IWebLingoTextOutput, IWebLingoBuiltinFunction[]> Builtins,
    Func<WebLingoSourceText, WebLingoCompilerOptions, WebLingoCommandLineCompilation> Compile)
{
    /// <summary>What the usage shows before a file, such as an option the program takes there (<c>[--root &lt;folder&gt;] </c>).</summary>
    public string Options { get; init; } = "";

    /// <summary>The usage's lines saying what <see cref="Options"/> are.</summary>
    public IReadOnlyList<string> OptionsHelp { get; init; } = [];
}

/// <summary>
/// A file compiled by a command line's program: the compiler's result, its diagnostics each in the file it is in; how to
/// find where a runtime error is, from its position in the program; and the text of a file a diagnostic names, which the
/// program read itself, as a page's includes are (specification, section 13).
/// </summary>
public sealed record WebLingoCommandLineCompilation(WebLingoCompilationResult Result)
{
    public Func<WebLingoDiagnostic, WebLingoDiagnostic> Locate { get; init; } = diagnostic => diagnostic;
    public Func<string, WebLingoSourceText?> TextOf { get; init; } = _ => null;

    /// <summary>What finds and compiles the components the program renders while it runs, if it renders any (section 13).</summary>
    public IWebLingoModuleLoader? Components { get; init; }
}
