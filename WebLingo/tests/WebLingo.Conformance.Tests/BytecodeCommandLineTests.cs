using WebLingo.Bytecode;
using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// WLBC on <c>weblingo</c>'s command line (specification, section 9, "M0.13 parts", part 2): <c>compile</c>, <c>run</c>
/// and <c>check</c> of a <c>.wlbc</c>, and <c>disasm</c>, end to end, with files, as a user would run them.
/// </summary>
public sealed class BytecodeCommandLineTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-wlbc-" + Guid.NewGuid().ToString("N"));

    public BytecodeCommandLineTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(1)]
    [InlineData(1, "--register")]
    [InlineData(1, "--vm", "register")]
    [InlineData(2, "--stack")]
    [InlineData(2, "--vm", "stack")]
    public void CompileWritesTheRegisterVmsBytecodeUnlessAskedForTheStackVms(int header, params string[] options)
    {
        var main = Write("main.wl", "import { write, writeLn } from \"std:system\"; writeLn(42);\n");
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo(["compile", .. options, main]));
        Assert.Equal(header, File.ReadAllBytes(Path.Combine(_folder, "main.wlbc"))[8]);
    }

    [Theory]
    [InlineData("register", 1)]
    [InlineData("stack", 2)]
    public void ACompiledProgramRunsFromItsFileAsFromItsSource(string vm, int header)
    {
        Write("lib/greet.wl", "export function greet(name) { return \"Hello, \" + name + \"!\"; }");
        var main = Write("main.wl", "param who;import { write, writeLn } from \"std:system\"; \nimport { greet } from \"lib/greet.wl\";\nwriteLn(greet(who));\n");
        var fromSource = Weblingo("run", main, "who=WLBC");
        Assert.Equal((WebLingoCommandLine.Success, "Hello, WLBC!\n", ""), fromSource);

        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo("compile", "--" + vm, main));
        var compiled = Path.Combine(_folder, "main.wlbc");
        Assert.Equal((ulong)header, (ulong)File.ReadAllBytes(compiled)[8]);
        // The program and the file it imports are in the .wlbc: it runs without them.
        File.Delete(main);
        File.Delete(Path.Combine(_folder, "lib", "greet.wl"));
        Assert.Equal(fromSource, Weblingo("run", compiled, "who=WLBC"));
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo("check", compiled));
    }

    [Theory]
    [InlineData("register")]
    [InlineData("stack")]
    public void ARuntimeErrorIsLocatedFromTheFileAlone(string vm)
    {
        Write("lib/util.wl", "export function f(n) {\n  return [1][n];\n}\n");
        var main = Write("main.wl", "import { write, writeLn } from \"std:system\"; import { f } from \"lib/util.wl\";\nwriteLn(\"before\");\nf(2);\n");
        var fromSource = Weblingo("run", main);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, fromSource.Exit);
        Assert.EndsWith("lib/util.wl(2,10): error WL3005: The index 2 is outside 0 to 0.", fromSource.Error.TrimEnd());

        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", "--" + vm, main).Exit);
        File.Delete(Path.Combine(_folder, "lib", "util.wl"));
        Assert.Equal(fromSource, Weblingo("run", Path.Combine(_folder, "main.wlbc")));

        // An error in the program's own file names it as the file beside the .wlbc.
        main = Write("main.wl", "var a = 1;\nvar b = a / 0 + null;\n");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", "--" + vm, main).Exit);
        var (exit, _, error) = Weblingo("run", Path.Combine(_folder, "main.wlbc"));
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Equal(Weblingo("run", main).Error, error);
        Assert.StartsWith(main + "(2,", error);
    }

    [Fact]
    public void TheFileHoldsNoneOfTheHostsPaths()
    {
        Write("lib/util.wl", "export function twice(x) { return x * 2; }");
        var main = Write("main.wl", "import { write, writeLn } from \"std:system\"; import { twice } from \"lib/util.wl\";\nwriteLn(twice(21));\n");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_folder, "main.wlbc")));
        Assert.DoesNotContain(Path.GetFileName(_folder), text);
        Assert.Contains("lib/util.wl", text);
    }

    [Theory]
    [InlineData("register", "function main (", " registers)")]
    [InlineData("stack", "function main (", " stack)")]
    public void DisasmShowsTheBytecode(string vm, string start, string kind)
    {
        var main = Write("main.wl", "import { write, writeLn } from \"std:system\"; var x = 40;\nwriteLn(x + 2);\n");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", "--" + vm, main).Exit);
        var (exit, output, error) = Weblingo("disasm", Path.Combine(_folder, "main.wlbc"));
        Assert.Equal((WebLingoCommandLine.Success, ""), (exit, error));
        Assert.StartsWith(start, output);
        Assert.Contains(kind, output.Split('\n')[0]);
        Assert.Contains("writeLn", output);
    }

    [Fact]
    public void ADamagedFileIsReportedAndNothingRuns()
    {
        var main = Write("main.wl", "import { write, writeLn } from \"std:system\"; writeLn(\"never\");\n");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var compiled = Path.Combine(_folder, "main.wlbc");
        var bytes = File.ReadAllBytes(compiled);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(compiled, bytes);
        foreach (var command in new[] { "run", "check", "disasm" })
        {
            var (exit, output, error) = Weblingo(command, compiled);
            Assert.Equal((WebLingoCommandLine.CompilationFailed, ""), (exit, output));
            Assert.StartsWith(compiled + ": error WL4001: The file is damaged: its source positions section does not match its CRC-32.", error);
        }
        Assert.Equal(WebLingoCommandLine.FileUnreadable, Weblingo("run", Path.Combine(_folder, "none.wlbc")).Exit);
    }

    [Fact]
    public void CompileReportsErrorsAndWritesNothing()
    {
        var main = Write("main.wl", "var x = ;\n");
        var (exit, _, error) = Weblingo("compile", main);
        Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
        Assert.StartsWith(main + "(1,9): error WL1001:", error);
        Assert.False(File.Exists(Path.Combine(_folder, "main.wlbc")));
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo("compile", "--fast", main).Exit);
        var (usage, _, why) = Weblingo("compile", "--stack", "--vm", "register", main);
        Assert.Equal((WebLingoCommandLine.UsageError, "Only one VM may be given: --register or --stack."), (usage, why.TrimEnd()));
        (usage, _, why) = Weblingo("compile", "--vm", "fast", main);
        Assert.Equal((WebLingoCommandLine.UsageError, "'fast' is not a VM: register or stack."), (usage, why.TrimEnd()));
    }

    [Fact]
    public void RunningAScriptWritesNothingToDisk()
    {
        // A1 of M0.13's questions: weblingo run script.wl compiles in memory, and saves no .wlbc.
        Write("lib/util.wl", "export function twice(x) { return x * 2; }");
        var main = Write("main.wl", "import { write, writeLn } from \"std:system\"; import { twice } from \"lib/util.wl\";\nwriteLn(twice(21));\n");
        var before = Files();
        Assert.Equal((WebLingoCommandLine.Success, "42\n", ""), Weblingo("run", main));
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo("check", main));
        Assert.Equal(before, Files());
    }

    [Fact]
    public void AFileNamedFirstIsRun()
    {
        // weblingo script.wl as weblingo run script.wl, its parameters after it, and a .wlbc so too (the owner's answer,
        // 18 November 2026, "Minimal WebLingo Installer: Linux", Q1); anything else still a usage error.
        var main = Write("Main.WL", "param name;\nimport { writeLn } from \"std:system\";\nwriteLn($\"Hello, {name}\");\n");
        Assert.Equal((WebLingoCommandLine.Success, "Hello, Ann\n", ""), Weblingo(main, "name=Ann"));
        Assert.Equal(Weblingo("run", main, "name=Bo"), Weblingo(main, "name=Bo"));
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        Assert.Equal((WebLingoCommandLine.Success, "Hello, Cy\n", ""), Weblingo(Path.ChangeExtension(main, ".wlbc"), "name=Cy"));
        Assert.Equal(WebLingoCommandLine.FileUnreadable, Weblingo(Path.Combine(_folder, "none.wl")).Exit);
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo("rnu", main).Exit);
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo(Write("notes.txt", "")).Exit);
        var usage = new StringWriter();
        WebLingoCommandLine.Run([], usage, new StringWriter());
        Assert.Contains("weblingo <file.wl> [name=value …] is the same as run", usage.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheUsageNamesTheBytecodesCommands()
    {
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run([], output, new StringWriter()));
        Assert.Contains("| compile [--register|--stack] [--scheme <name>=<folder> …] <file.wl> | disasm <file.wlbc>", output.ToString());
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string[] Files() => [.. Directory.GetFiles(_folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    private static (int Exit, string Output, string Error) Weblingo(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
