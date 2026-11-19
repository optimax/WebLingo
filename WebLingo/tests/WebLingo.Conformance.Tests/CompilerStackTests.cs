using WebLingo.Cli;
using WebLingo.Language;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// Source as deep as the parser allows (WL1008; <see cref="WebLingoParser.MaxRecursionDepth"/> and
/// <see cref="WebLingoParser.MaxExpressionDepth"/>) compiles and runs on the thread <c>weblingo</c> runs its commands on
/// (<see cref="WebLingoCommandThread"/>), whose stack is the compiler's (<see cref="WebLingoParser.CompilerStackSize"/>),
/// though .NET has not yet optimized the compiler, whose frames are then larger; and deeper source is WL1008, not a crash.
/// </summary>
public sealed class CompilerStackTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-stack-" + Guid.NewGuid().ToString("N"));

    public CompilerStackTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void SourceAsDeepAsTheParserAllowsCompilesOnTheCommandLinesThread()
    {
        const int depth = WebLingoParser.MaxRecursionDepth - 6;
        const int terms = WebLingoParser.MaxExpressionDepth - 10;
        var nested = new string('(', depth) + "1" + new string(')', depth);
        var chain = "1" + string.Concat(Enumerable.Repeat(" + 1", terms - 1));
        var file = Write("deep.wlg", $"import {{ write, writeLn }} from \"std:system\"; writeLn({nested});\nwriteLn({chain});\n");
        Assert.Equal((WebLingoCommandLine.Success, $"1\n{terms}\n", ""), Weblingo("run", file));
    }

    [Fact]
    public void DeeperSourceIsADiagnosticNotACrash()
    {
        var file = Write("deeper.wlg", "import { write, writeLn } from \"std:system\"; writeLn(" + new string('(', 100_000) + "1" + new string(')', 100_000) + ");\n");
        var (exit, output, error) = Weblingo("check", file);
        Assert.Equal((WebLingoCommandLine.CompilationFailed, ""), (exit, output));
        Assert.Contains("error WL1008: The expression nests more than 256 levels deep.", error, StringComparison.Ordinal);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary><c>weblingo</c>, as its <c>Main</c> runs it: the command on <see cref="WebLingoCommandThread"/>.</summary>
    private static (int Exit, string Output, string Error) Weblingo(params string[] args)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = WebLingoCommandThread.Run(() => WebLingoCommandLine.Run(args, output, error));
        return (exit, output.ToString(), error.ToString());
    }
}
