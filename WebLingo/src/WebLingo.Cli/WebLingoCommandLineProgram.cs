using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.Cli;

/// <summary>
/// What a command line compiles (<see cref="WebLingoCommandLine"/>): <c>weblingo</c>'s scripts, or another program's
/// files, such as <c>wasp</c>'s pages (<c>WebLingo.Wasp.Cli</c>, specification, section 13): its name, its version, its
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

    /// <summary>The optional source compiler for the stack VM.</summary>
    public Func<WebLingoSourceText, WebLingoCompilerOptions, WebLingoCommandLineCompilation>? CompileForStack { get; init; }

    /// <summary>
    /// Whether it has WLBC files (specification, section 5.3): <c>compile</c> and <c>disasm</c>, and <c>run</c> and
    /// <c>check</c> of a bytecode file, as <c>weblingo</c> has; WASP never writes one (section 5.1).
    /// </summary>
    public bool Bytecode { get; init; }

    /// <summary>
    /// Whether a file named first, without a command, is run, <c>weblingo script.wlg</c> as <c>weblingo run script.wlg</c>, as
    /// a scripting language's runner runs one: a file of its <see cref="Extension"/>, or a bytecode file where it has them.
    /// </summary>
    public bool RunsFileNamedFirst { get; init; }

    /// <summary>
    /// The configuration's file in a file's folder (specification, sections 3.9 and 6.12): <c>weblingo.config</c>, a
    /// script's, unless the program has another, as <c>wasp</c> has <c>wasp.config</c>, a site's.
    /// </summary>
    public string Configuration { get; init; } = WebLingoSchemes.ConfigurationFile;

    /// <summary>
    /// Whether its scripts may reach any host on the network (specification, section 7.9): <c>weblingo</c>'s; else only
    /// those its configuration allows, as <c>wasp</c>'s pages may.
    /// </summary>
    public bool ReachesAnyHost { get; init; } = true;

    /// <summary>
    /// Whether its scripts may reach any file (specification, section 7.13, "File capabilities"): <c>weblingo</c>'s, a
    /// relative path from the current folder; else only those in the folders its configuration allows, a relative path from
    /// <see cref="Root"/>, as <c>wasp</c>'s pages may.
    /// </summary>
    public bool ReachesAnyFile { get; init; } = true;

    /// <summary>
    /// Whether its scripts may read all of the machine's environment, and set its variables (specification, section 7.15):
    /// <c>weblingo</c>'s; else only what its configuration's <c>"env"</c> allows, and none set, as <c>wasp</c>'s pages.
    /// </summary>
    public bool ReachesAllEnvironment { get; init; } = true;

    /// <summary>Whether this command line gives scripts the subprocess runner.</summary>
    public bool AllowsProcesses { get; init; }

    /// <summary>The application's root, from which a script's relative path is where it reaches only some files; the file's folder if null.</summary>
    public string? Root { get; init; }
}

/// <summary>
/// A file compiled by a command line's program: the compiler's result, its diagnostics each in the file it is in; how to
/// find where a runtime error is, from its position in the program; and the text of a file a diagnostic names, which the
/// program read itself, as a page's includes are (specification, section 13).
/// </summary>
public sealed record WebLingoCommandLineCompilation(WebLingoCompilationResult Result)
{
    public WebLingoCommandLineCompilation(WebLingoStackCompilationResult result)
        : this(new WebLingoCompilationResult(null, result.Diagnostics)) => StackModule = result.Module;

    internal WebLingoStackModule? StackModule { get; }

    public Func<WebLingoDiagnostic, WebLingoDiagnostic> Locate { get; init; } = diagnostic => diagnostic;
    public Func<string, WebLingoSourceText?> TextOf { get; init; } = _ => null;

    /// <summary>What finds and compiles the components the program renders while it runs, if it renders any (section 13).</summary>
    public IWebLingoModuleLoader? Components { get; init; }
    public IWebLingoStackModuleLoader? StackComponents { get; init; }
}
