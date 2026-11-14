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
    Func<WebLingoSourceText, WebLingoCompilerOptions, WebLingoCompilationResult> Compile);
