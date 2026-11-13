using WebLingo.Bytecode;
using WebLingo.Cli;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// The specification's initial conformance tests (section 8.6), end to end: each program is written to a file and
/// run through the <c>weblingo</c> command line, as a user would, with its output, its diagnostics and its exit code
/// checked.
/// </summary>
public sealed class ConformanceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-conformance-" + Guid.NewGuid().ToString("N"));

    public ConformanceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("var x = 10;")]
    [InlineData("const x = 10;")]
    [InlineData("var x = 10 + 20;")]
    [InlineData("var x = 10 / 2;")]
    public void ValidProgramsCompileAndRun(string source)
    {
        Assert.Equal((WsCommandLine.Success, "", ""), Weblingo("check", source));
        Assert.Equal((WsCommandLine.Success, "", ""), Weblingo("run", source));
    }

    [Theory]
    [InlineData("const x;", "WS1006", 1, 7)]
    [InlineData("var x;", "WS1006", 1, 5)]
    [InlineData("writeLn(x); var x = 1;", "WS2001", 1, 9)]
    [InlineData("var x = 10", "WS1002", 1, 11)]
    [InlineData("var x = 1; var x = 2;", "WS2002", 1, 16)]
    [InlineData("x = 10;", "WS2001", 1, 1)]
    [InlineData("{ var y = 1; } writeLn(y);", "WS2001", 1, 24)]
    [InlineData("{ var y = 1; var y = 2; }", "WS2002", 1, 18)]
    public void CompileErrorsAreReportedAndNothingRuns(string source, string code, int line, int column)
    {
        foreach (var command in new[] { "check", "run" })
        {
            var (exit, output, error) = Weblingo(command, source);
            Assert.Equal(WsCommandLine.CompilationFailed, exit);
            Assert.Equal("", output);
            Assert.Equal($"program.ws({line},{column}): error {code}:", FirstLineStart(error));
        }
    }

    [Theory]
    [InlineData("var x = 10 + 20; writeLn(x);", "30")]
    [InlineData("var x = 10 / 2; writeLn(x);", "5.0")]
    [InlineData("var x = 1; { var x = 2; writeLn(x); } writeLn(x);", "2\n1")]
    [InlineData("var x = 1; { x = 3; } writeLn(x);", "3")]
    [InlineData("{ var y = 4; { writeLn(y * y); } }", "16")]
    [InlineData("{} { { } }", "")]
    public void ResultsAreTheSpecifiedOnes(string source, string printed)
    {
        Assert.Equal((WsCommandLine.Success, printed.Length == 0 ? "" : printed + "\n", ""), Weblingo("run", source));
    }

    [Fact]
    public void SignedInt64OverflowIsARuntimeError()
    {
        const string source = "var x = 9223372036854775807 + 1;";
        Assert.Equal(WsCommandLine.Success, Weblingo("check", source).Exit);
        var (exit, _, error) = Weblingo("run", source);
        Assert.Equal(WsCommandLine.RuntimeFailed, exit);
        Assert.Equal("program.ws(1,9): error WS3002:", FirstLineStart(error));
    }

    [Fact]
    public void TheSpecificationsExamplePrints30()
    {
        Assert.Equal((WsCommandLine.Success, "30\n", ""),
            Weblingo("run", "var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);\n"));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("--unknown")]
    [InlineData("run", "a.ws", "b.ws")]
    public void AUsageErrorExitsWith2(params string[] args)
    {
        var output = new StringWriter();
        Assert.Equal(WsCommandLine.UsageError, WsCommandLine.Run(args, output, new StringWriter()));
        Assert.StartsWith("Usage: weblingo", output.ToString());
    }

    [Fact]
    public void AFileThatCannotBeReadExitsWith4()
    {
        var error = new StringWriter();
        var missing = Path.Combine(_folder, "missing.ws");
        Assert.Equal(WsCommandLine.FileUnreadable, WsCommandLine.Run(["run", missing], new StringWriter(), error));
        Assert.Contains("cannot read the file", error.ToString());
    }

    [Fact]
    public void TheArithmeticSamplePrints30()
    {
        var sample = Path.Combine(SolutionFolder(), "samples", "Arithmetic", "arithmetic.ws");
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(WsCommandLine.Success, WsCommandLine.Run(["run", sample], output, error));
        Assert.Equal(("30\n", ""), (output.ToString(), error.ToString()));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void EveryLineEndingGivesTheSameResultAndPositions(string newline)
    {
        var good = string.Join(newline, "var a = 2;", "var b = a * 3;", "writeLn(b);");
        Assert.Equal((WsCommandLine.Success, "6\n", ""), Weblingo("run", good));
        var bad = string.Join(newline, "var x = 1;", "writeLn(y);");
        Assert.Equal("program.ws(2,9): error WS2001:", FirstLineStart(Weblingo("run", bad).Error));
    }

    [Fact]
    public void MalformedSourceGivesDiagnosticsNotACrash()
    {
        string[] fragments =
        [
            "var", "const", "let", "x", "y", "writeLn", "=", "+", "-", "*", "/", "(", ")", ";", ",", "10", "0", "1.5", "1e309", "Infinity", "NaN",
            "9223372036854775807", "9223372036854775808", "1x", "@", "\"", ".", "/*", "*/", "//", "\n", "\r\n", " ", "é",
        ];
        var random = new Random(8_6);
        var compiler = new WsCompiler(new WsCompilerOptions([new WsBuiltinDeclaration("writeLn", 1)]));
        var vm = new WsVirtualMachine();
        var compiled = 0;
        for (var round = 0; round < 5_000; round++)
        {
            var source = string.Concat(Enumerable.Range(0, random.Next(1, 30)).Select(_ => fragments[random.Next(fragments.Length)] + " "));
            var result = compiler.Compile(source, "fuzz.ws");
            if (result.Module is null)
            {
                Assert.NotEmpty(result.Diagnostics);
                Assert.All(result.Diagnostics, d => Assert.InRange(d.Span.Start, 0, source.Length));
                continue;
            }
            compiled++;
            var context = new WsExecutionContext(new WsWorkerId(0))
            {
                Services = new WsBuiltinRegistry(WsStandardBuiltins.Create(new WsCollectedOutput())),
                InstructionBudget = 10_000,
            };
            var run = vm.Execute(result.Module, context);
            Assert.True(run.Succeeded || run.Error is not null);
        }
        Assert.InRange(compiled, 1, 4_999);
    }

    [Fact]
    public void MalformedBytecodeFailsVerificationNotACrash()
    {
        var module = new WsCompiler(new WsCompilerOptions([new WsBuiltinDeclaration("writeLn", 1)]))
            .Compile("var a = 10; var b = a * 2 - 3; var c = 0; c = -b / a; writeLn(c);", "base.ws").Module!;
        var random = new Random(8_6);
        var opCodes = Enum.GetValues<WsOpCode>();
        var vm = new WsVirtualMachine();
        var rejected = 0;
        for (var round = 0; round < 5_000; round++)
        {
            var code = module.EntryPoint.Code.ToArray();
            for (var mutation = random.Next(1, 4); mutation > 0; mutation--)
            {
                var i = random.Next(code.Length);
                code[i] = random.Next(4) switch
                {
                    0 => code[i] with { OpCode = random.Next(8) == 0 ? (WsOpCode)random.Next(256) : opCodes[random.Next(opCodes.Length)] },
                    1 => code[i] with { A = random.Next(-2, 8) },
                    2 => code[i] with { B = random.Next(-2, 8) },
                    _ => code[i] with { C = random.Next(-2, 8), D = random.Next(-1, 3) },
                };
            }
            var mutated = module with { EntryPoint = module.EntryPoint with { Code = code } };
            var context = new WsExecutionContext(new WsWorkerId(0))
            {
                Services = new WsBuiltinRegistry(WsStandardBuiltins.Create(new WsCollectedOutput())),
                InstructionBudget = 10_000,
            };
            var result = vm.Execute(mutated, context);
            if (result.Error?.Code == WsDiagnosticCodes.BytecodeVerificationFailure) rejected++;
        }
        Assert.InRange(rejected, 1, 5_000);
    }

    private (int Exit, string Output, string Error) Weblingo(string command, string source)
    {
        var path = Path.Combine(_folder, "program.ws");
        File.WriteAllText(path, source);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WsCommandLine.Run([command, path], output, error);
        // Diagnostics name the file as it was given; show only its name, so the expectations hold on every machine.
        return (exit, output.ToString(), error.ToString().Replace(path, "program.ws", StringComparison.Ordinal));
    }

    /// <summary>The first line of the error output, up to the end of its "error CODE:".</summary>
    private static string FirstLineStart(string error)
    {
        var line = error.Split('\n')[0];
        var end = line.IndexOf(": error ", StringComparison.Ordinal);
        return end < 0 ? line : line[..line.IndexOf(':', end + ": error ".Length)] + ":";
    }

    private static string SolutionFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "WebLingo.slnx"))) return folder.FullName;
        }
        throw new InvalidOperationException("The solution folder was not found above the test's folder.");
    }
}
