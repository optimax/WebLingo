using WebLingo.Bytecode;
using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// WLBC on <c>weblingo</c>'s command line (specification, section 9, "M0.13 parts", part 2): <c>compile</c>, <c>run</c>
/// and <c>check</c> of a bytecode file, and <c>disasm</c>, end to end, with files, as a user would run them.
/// </summary>
public sealed class BytecodeCommandLineTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-wlbc-" + Guid.NewGuid().ToString("N"));

    public BytecodeCommandLineTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void CompileWritesAWlbFile()
    {
        // One VM, the register VM, the owner's choice (23 November 2026, WL-BC002), its bytecode a .wlb file (the owner's
        // request the same day), which no option chooses.
        var main = Write("main.wlg", "import { write, writeLn } from \"std:console\"; writeLn(42);\n");
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo("compile", main));
        Assert.Equal(1, File.ReadAllBytes(Path.ChangeExtension(main, ".wlb"))[8]);
        Assert.Equal(2, Files().Length);
        foreach (var option in new[] { "--register", "--stack", "--vm" })
        {
            var (exit, _, error) = Weblingo("compile", option, main);
            Assert.Equal((WebLingoCommandLine.UsageError, $"'{option}' is not an option of compile."), (exit, error.TrimEnd()));
        }
    }

    [Theory]
    [InlineData("register", 1)]
    public void ACompiledProgramRunsFromItsFileAsFromItsSource(string vm, int header)
    {
        Write("lib/greet.wlg", "export function greet(name) { return \"Hello, \" + name + \"!\"; }");
        var main = Write("main.wlg", "param who;import { write, writeLn } from \"std:console\"; \nimport { greet } from \"lib/greet.wlg\";\nwriteLn(greet(who));\n");
        var fromSource = Weblingo("run", main, "who=WLBC");
        Assert.Equal((WebLingoCommandLine.Success, "Hello, WLBC!\n", ""), fromSource);

        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo("compile", main));
        var compiled = Path.ChangeExtension(main, Extension(vm));
        Assert.Equal((ulong)header, (ulong)File.ReadAllBytes(compiled)[8]);
        // The program and the file it imports are in the compiled file: it runs without them.
        File.Delete(main);
        File.Delete(Path.Combine(_folder, "lib", "greet.wlg"));
        Assert.Equal(fromSource, Weblingo("run", compiled, "who=WLBC"));
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo("check", compiled));
    }

    [Theory]
    [InlineData("register")]
    public void ARuntimeErrorIsLocatedFromTheFileAlone(string vm)
    {
        Write("lib/util.wlg", "export function f(n) {\n  return [1][n];\n}\n");
        var main = Write("main.wlg", "import { write, writeLn } from \"std:console\"; import { f } from \"lib/util.wlg\";\nwriteLn(\"before\");\nf(2);\n");
        var fromSource = Weblingo("run", main);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, fromSource.Exit);
        Assert.EndsWith("lib/util.wlg(2,10): ERROR WL3005 (IndexError): The index 2 is outside 0 to 0.", fromSource.Error.TrimEnd());

        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        File.Delete(Path.Combine(_folder, "lib", "util.wlg"));
        Assert.Equal(fromSource, Weblingo("run", Path.ChangeExtension(main, Extension(vm))));

        // An error in the program's own file names it as the file beside the bytecode.
        main = Write("main.wlg", "var a = 1;\nvar b = a / 0 + null;\n");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var (exit, _, error) = Weblingo("run", Path.ChangeExtension(main, Extension(vm)));
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Equal(Weblingo("run", main).Error, error);
        Assert.StartsWith(main + "(2,", error);
    }

    [Theory]
    [InlineData("register")]
    public void AnUncaughtExceptionNamesItsOwnClass(string vm)
    {
        var main = Write("main.wlg", "class MyError extends Error {}\nthrow new MyError(\"broken\");\n");
        var fromSource = Weblingo("run", main);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, fromSource.Exit);
        Assert.Equal(main + "(2,1): ERROR WL3008 (MyError): An error was thrown and not caught: broken", fromSource.Error.TrimEnd());

        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        Assert.Equal(fromSource, Weblingo("run", Path.ChangeExtension(main, Extension(vm))));
    }

    [Fact]
    public void ARuntimeRegexTimeoutAppliesToSourceAndBothBytecodeKinds()
    {
        var main = Write("main.wlg", "import { Regex } from \"std:regex\";\n"
            + "new Regex(\"^(a+)+$\").test(\"" + new string('a', 120) + "!\");\n");
        var expected = Weblingo("run", "--timeout", "10", main);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, expected.Exit);
        Assert.Contains("ERROR WL3010 (TimeoutError): The regular expression /^(a+)+$/ took longer than 10 milliseconds to match.",
            expected.Error);
        foreach (var vm in new[] { "register", "stack" })
        {
            Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
            Assert.Equal(expected, Weblingo("run", "--timeout", "10", Path.ChangeExtension(main, Extension(vm))));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.5")]
    [InlineData("2147483647")]
    public void RunRejectsAnInvalidRegexTimeout(string? value)
    {
        var args = value is null ? new[] { "run", "--timeout" } : ["run", "--timeout", value, "main.wlg"];
        var (exit, _, error) = Weblingo(args);
        Assert.Equal(WebLingoCommandLine.UsageError, exit);
        Assert.StartsWith("--timeout requires a whole number of milliseconds", error);
    }

    [Theory]
    [InlineData("register", "r")]
    public void DisasmNamesAFilesBodyByItsFile(string vm, string kind)
    {
        // A file's top level, which has no name in the source, is headed as its file's body, and closed over as one (the
        // owner's request, 22 November 2026, "The Name of a File's Top Level in disasm"), the program's own main of its
        // file, as the owner asked the same day.
        Write("lib/util.wlg", "export function twice(x) { return x * 2; }");
        var main = Write("main.wlg", "import { writeLn } from \"std:console\";\nimport { twice } from \"lib/util.wlg\";\nwriteLn(twice(21));\n");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var disassembly = Weblingo("disasm", Path.ChangeExtension(main, Extension(vm))).Output;
        Assert.StartsWith("function main of main.wlg (", disassembly);
        Assert.Contains("\nfunction module_body of lib/util.wlg (", disassembly);
        Assert.Matches(@"MakeClosure (" + kind + @"\d+, )?module_body\(lib/util\.wlg\)\n", disassembly);
        Assert.Matches(@"\nfunction function\d+ twice \(", disassembly);
        Assert.DoesNotMatch(@"function\d+[ (]lib/util\.wlg", disassembly);
    }

    [Theory]
    [InlineData("register")]
    public void AClassThatDeclaresNoConstructorHasNone(string vm)
    {
        // A class that declares no constructor has none in the program, new passing its arguments on to a base's, or
        // making the instance without a call (the owner's question, 22 November 2026, "No Empty Constructors"); Object
        // keeps its own, which super() runs where no base declares one.
        var main = Write("main.wlg", """
            import { writeLn } from "std:console";
            class P { name() { return "p"; } }
            class Q extends P {}
            class R extends Q { constructor(x) { super(); this.x = x; } }
            writeLn([new P().name(), new Q().name(), new R(5).x]);
            new Q(1);
            """);
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var compiled = Path.ChangeExtension(main, Extension(vm));
        var disassembly = Weblingo("disasm", compiled).Output;
        Assert.DoesNotMatch(@"\nfunction function\d+ (P|Q) \(", disassembly);
        Assert.Matches(@"\nfunction function\d+ R \(", disassembly);
        Assert.Matches(@"\nfunction function\d+ Object \(", disassembly);
        var (exit, output, error) = Weblingo("run", compiled);
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "[\"p\", \"p\", 5]\n"), (exit, output));
        Assert.Matches(@"\(6,\d+\): ERROR WL2006 \(ArgumentError\): 'Q' takes 0 arguments, not 1\.", error);
    }

    [Fact]
    public void TheFileHoldsNoneOfTheHostsPaths()
    {
        Write("lib/util.wlg", "export function twice(x) { return x * 2; }");
        var main = Write("main.wlg", "import { write, writeLn } from \"std:console\"; import { twice } from \"lib/util.wlg\";\nwriteLn(twice(21));\n");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_folder, "main.wlb")));
        Assert.DoesNotContain(Path.GetFileName(_folder), text);
        Assert.Contains("lib/util.wlg", text);
    }

    [Theory]
    [InlineData("register", "function main of main.wlg (", " registers)")]
    public void DisasmShowsTheBytecode(string vm, string start, string kind)
    {
        var main = Write("main.wlg", "import { write, writeLn } from \"std:console\"; var x = 40;\nwriteLn(x + 2);\n");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var (exit, output, error) = Weblingo("disasm", Path.ChangeExtension(main, Extension(vm)));
        Assert.Equal((WebLingoCommandLine.Success, ""), (exit, error));
        Assert.StartsWith(start, output);
        Assert.Contains(kind, output.Split('\n')[0]);
        Assert.Contains("writeLn", output);
    }

    [Theory]
    [InlineData("register", "r")]
    public void ADebugFileNamesItsVariablesCapturesAndStatements(string vm, string local)
    {
        // compile --debug writes the debugger's metadata, which disasm shows (the owner's request, 22 November 2026, "Debug
        // Info"); the functions, classes and files instructions refer to are named with or without it.
        var main = Write("main.wlg", """
            import { writeLn } from "std:console";
            class Point { constructor(x) { this.x = x; } }
            function twice(n) { var doubled = n * 2; return doubled; }
            var make = (k) => () => k + twice(new Point(1).x);
            writeLn(make(3)());
            """);
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var compiled = Path.ChangeExtension(main, Extension(vm));
        var plain = Weblingo("disasm", compiled).Output;
        Assert.Matches(@"MakeClass .*class\d+\(Point\)", plain);
        Assert.Matches(@"MakeClosure .*function\d+\(twice\)", plain);
        Assert.Matches(@"file\d+\(main\.wlg\)", plain);
        Assert.DoesNotContain("\nvariable ", plain);
        Assert.DoesNotMatch(@"capture\d+\(k\)", plain);
        var size = new FileInfo(compiled).Length;

        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", "--debug", main).Exit);
        Assert.True(new FileInfo(compiled).Length > size);
        Assert.Equal((WebLingoCommandLine.Success, "5\n", ""), Weblingo("run", compiled));
        var (exit, output, error) = Weblingo("disasm", compiled);
        Assert.Equal((WebLingoCommandLine.Success, ""), (exit, error));
        Assert.Matches(@"LoadCapture .*capture\d+\(k\)", output);
        Assert.Contains("\n; 3:21\n", output);
        Assert.Contains($"\nvariable doubled: {local}1, visible 3:32 to 3:59\n", output);
        Assert.Matches($"\nvariable writeLn: {local}\\d+\\(import std:console\\)\\.\"writeLn\", visible from 1:17\n", output);
        var bytes = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(compiled));
        Assert.DoesNotContain(Path.GetFileName(_folder), bytes);
    }

    [Theory]
    [InlineData("register")]
    public void ANativeModulesNamedImportIsItsExportItself(string vm)
    {
        // A name an import takes of a native module's export, which nothing assigns, is the export itself, a function called
        // as the built-in it is, without reading the module's member (the owner's request, 22 November 2026, "A Native
        // Module's Named Imports Bound Directly"); what the program writes, and its errors, as before.
        var calls = Write("calls.wlg", "import { writeLn } from \"std:console\";\nimport { abs } from \"std:math\";\nwriteLn(abs(-2));\n");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", calls).Exit);
        var disassembly = Weblingo("disasm", Path.ChangeExtension(calls, Extension(vm))).Output;
        var main = disassembly.Split("\n\n")[0];
        Assert.Contains("CallBuiltin", main);
        Assert.Contains("std:console.writeLn", main);
        Assert.DoesNotContain(".\"writeLn\"", main);
        Assert.DoesNotContain(".\"abs\"", main);
        // Nothing using either module as a value, neither is made, nor imported (step 2), and nothing of theirs the program
        // does not name is in it, std:console's classes and their members none of its functions ("A Native Module's Unused
        // Exports Left Out", the owner's request, 22 November 2026).
        Assert.DoesNotMatch(@"(MakeModule|Import) .*\((std:console|std:math)\)", main);
        Assert.DoesNotMatch(@"RegisterUnit|MakeClass", main);
        Assert.DoesNotContain("\nfunction ", disassembly);
        Assert.Equal((WebLingoCommandLine.Success, "2\n", ""), Weblingo("run", Path.ChangeExtension(calls, Extension(vm))));

        // The same function, class and constant as the module's own, made where a whole import or a dynamic one first asks
        // for it, one for every importer; a call with other than its arguments an error where it runs.
        var same = Write("same.wlg", """
            import { writeLn, Console } from "std:console";
            import console from "std:console";
            import { PI, min } from "std:math";
            var f = writeLn;
            var math = await Module.import("std:math");
            writeLn([f == console.writeLn, Console == console.Console, PI == math.PI, min(3, 1, 2), math == await Module.import("std:math")]);
            writeLn();
            """);
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", same).Exit);
        Assert.Matches(@"Import .*\(std:console\)", Weblingo("disasm", Path.ChangeExtension(same, Extension(vm))).Output);
        var (exit, output, error) = Weblingo("run", Path.ChangeExtension(same, Extension(vm)));
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "[true, true, true, 1, true]\n"), (exit, output));
        Assert.Contains("(7,1): ERROR WL2006 (ArgumentError): 'std:console.writeLn' takes 1 argument, not 0.", error);
    }

    [Fact]
    public void ADynamicImportOfAFileTheProgramHasLinksNoNativeModuleWhole()
    {
        // Examples.wlg/16-modules.wlg's case (the owner's question, 22 November 2026): a dynamic import of a file the
        // program has, which runs as compiled, needs nothing of a native module's the program does not name; one of a file
        // it has not, compiled while it runs and joined to it, its every export, as a path known only then does.
        Write("teas.wlg", "param currency = \"$\";\nexport var shown = currency;\n");
        var has = Write("has.wlg", """
            import { writeLn } from "std:console";
            import teas from "teas.wlg";
            var francs = await Module.import("teas.wlg", { currency: "CHF" });
            writeLn([teas.shown, francs.shown]);
            """);
        Assert.Equal((WebLingoCommandLine.Success, "[\"$\", \"CHF\"]\n", ""), Weblingo("run", has));
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", has).Exit);
        Assert.DoesNotContain("beep", Weblingo("disasm", Path.ChangeExtension(has, ".wlb")).Output);

        Write("other.wlg", "import console from \"std:console\";\nimport { writeLn } from \"std:console\";\nexport var w = writeLn;\n");
        var hasNot = Write("hasNot.wlg", """
            import { writeLn } from "std:console";
            var other = await Module.import("other.wlg");
            writeLn(other.w == writeLn);
            """);
        Assert.Equal((WebLingoCommandLine.Success, "false\n", ""), Weblingo("run", hasNot));
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", hasNot).Exit);
        Assert.Contains("beep", Weblingo("disasm", Path.ChangeExtension(hasNot, ".wlb")).Output);
    }

    [Fact]
    public void ADamagedFileIsReportedAndNothingRuns()
    {
        var main = Write("main.wlg", "import { write, writeLn } from \"std:console\"; writeLn(\"never\");\n");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var compiled = Path.Combine(_folder, "main.wlb");
        var bytes = File.ReadAllBytes(compiled);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(compiled, bytes);
        foreach (var command in new[] { "run", "check", "disasm" })
        {
            var (exit, output, error) = Weblingo(command, compiled);
            Assert.Equal((WebLingoCommandLine.CompilationFailed, ""), (exit, output));
            Assert.StartsWith(compiled + ": ERROR WL4001: The file is damaged: its source positions section does not match its CRC-32.", error);
        }
        Assert.Equal(WebLingoCommandLine.FileUnreadable, Weblingo("run", Path.Combine(_folder, "none.wlbs")).Exit);
    }

    [Fact]
    public void CompileReportsErrorsAndWritesNothing()
    {
        var main = Write("main.wlg", "var x = ;\n");
        var (exit, _, error) = Weblingo("compile", main);
        Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
        Assert.StartsWith(main + "(1,9): ERROR WL1001:", error);
        Assert.Equal([main], Files());
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo("compile", "--fast", main).Exit);
    }

    [Fact]
    public void RunningAScriptWritesNothingToDisk()
    {
        // A1 of M0.13's questions: weblingo run script.wlg compiles in memory.
        Write("lib/util.wlg", "export function twice(x) { return x * 2; }");
        var main = Write("main.wlg", "import { write, writeLn } from \"std:console\"; import { twice } from \"lib/util.wlg\";\nwriteLn(twice(21));\n");
        var before = Files();
        Assert.Equal((WebLingoCommandLine.Success, "42\n", ""), Weblingo("run", main));
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo("check", main));
        Assert.Equal(before, Files());
    }

    [Fact]
    public void AFileNamedFirstIsRun()
    {
        // weblingo script.wlg as weblingo run script.wlg, its parameters after it, and bytecode too (the owner's answer,
        // 18 November 2026, "Minimal WebLingo Installer: Linux", Q1); anything else still a usage error.
        var main = Write("Main.WLG", "param name;\nimport { writeLn } from \"std:console\";\nwriteLn($\"Hello, {name}\");\n");
        Assert.Equal((WebLingoCommandLine.Success, "Hello, Anna\n", ""), Weblingo(main, "name=Anna"));
        Assert.Equal(Weblingo("run", main, "name=Bo"), Weblingo(main, "name=Bo"));
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        Assert.Equal((WebLingoCommandLine.Success, "Hello, Cy\n", ""), Weblingo(Path.ChangeExtension(main, ".wlb"), "name=Cy"));
        Assert.Equal(WebLingoCommandLine.FileUnreadable, Weblingo(Path.Combine(_folder, "none.wlg")).Exit);
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo("rnu", main).Exit);
        Assert.Equal(WebLingoCommandLine.UsageError, Weblingo(Write("notes.txt", "")).Exit);
        var usage = new StringWriter();
        WebLingoCommandLine.Run([], usage, new StringWriter());
        Assert.Contains("weblingo <file.wlg> [name=value …] is the same as run", usage.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AScriptsInterpreterLineIsSkipped()
    {
        // #!/usr/bin/env weblingo first, which Linux reads to run an executable script by its name (the owner's answer,
        // 18 November 2026, "Minimal WebLingo Installer: Linux", Q1): skipped, each line keeping its number.
        var main = Write("tool.wlg", "#!/usr/bin/env weblingo\r\nimport { writeLn } from \"std:console\";\nwriteLn(1 + 1);\nwriteLn(nope);\n");
        var (exit, output, error) = Weblingo("check", main);
        Assert.Equal((WebLingoCommandLine.CompilationFailed, ""), (exit, output));
        Assert.StartsWith(main + "(4,9): ERROR WL2001", error, StringComparison.Ordinal);
        File.WriteAllText(main, "#!/usr/bin/env weblingo\nimport { writeLn } from \"std:console\";\nwriteLn(1 + 1);\n");
        Assert.Equal((WebLingoCommandLine.Success, "2\n", ""), Weblingo(main));
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        Assert.Equal((WebLingoCommandLine.Success, "2\n", ""), Weblingo(Path.ChangeExtension(main, ".wlb")));
    }

    [Fact]
    public void TheUsageNamesTheBytecodesCommands()
    {
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run([], output, new StringWriter()));
        Assert.Contains("| compile [--debug] [--scheme <name>=<folder> …] <file.wlg> | disasm <bytecode-file>", output.ToString());
        Assert.Contains("run, check and disasm accept .wlb files, and earlier builds' .wlbr and .wlbc files", output.ToString());
    }

    [Fact]
    public void TheStackVmsBytecodeIsRefused()
    {
        // The stack VM's files, which WebLingo had until 23 November 2026: a .wlbs, the stack VM's extension, and a file
        // whose header names the stack VM, each refused, for run, check and disasm.
        var main = Write("main.wlg", "import { writeLn } from \"std:console\"; writeLn(42);");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var compiled = Path.ChangeExtension(main, ".wlb");
        var named = Path.ChangeExtension(main, ".WLBS");
        File.Copy(compiled, named);
        var headed = Write("stack.wlb", "");
        var bytes = File.ReadAllBytes(compiled);
        bytes[8] = 2;
        File.WriteAllBytes(headed, bytes);
        foreach (var (file, diagnostic) in new[]
                 {
                     (named, "The file extension is for the stack VM, but its header names the register VM."),
                     (headed, "The file holds the stack VM's bytecode, which WebLingo no longer runs: it has one VM, the register VM.")
                 })
        {
            foreach (var command in new[] { "run", "check", "disasm" })
            {
                var (exit, output, error) = Weblingo(command, file);
                Assert.Equal((WebLingoCommandLine.CompilationFailed, "", $"{file}: ERROR WL4001: {diagnostic}"), (exit, output, error.TrimEnd()));
            }
            Assert.Equal(Weblingo("run", file), Weblingo(file));
        }
    }

    [Fact]
    public void AnEarlierBuildsBytecodeFileStillRuns()
    {
        // A .wlbr, the register VM's before .wlb, and a .wlbc, WLBC's first, each read as a .wlb, by its header.
        var main = Write("main.wlg", "import { writeLn } from \"std:console\"; writeLn(42);");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", main).Exit);
        var compiled = Path.ChangeExtension(main, ".wlb");
        Assert.Equal((WebLingoCommandLine.Success, "42\n", ""), Weblingo(compiled));
        foreach (var extension in new[] { ".wlbr", ".wlbc" })
        {
            var earlier = Path.ChangeExtension(main, extension);
            File.Copy(compiled, earlier);
            Assert.Equal(Weblingo(compiled), Weblingo(earlier));
            Assert.Equal(Weblingo("disasm", compiled), Weblingo("disasm", earlier));
        }
    }

    private static string Extension(string vm) => ".wlb";

    [Theory]
    [InlineData("\"register\"")]
    [InlineData("\"stack\"")]
    public void AVmIsNoSetting(string value)
    {
        // One VM, which no setting chooses (23 November 2026, WL-BC002): "vm" is a setting the configuration does not know.
        var program = Write("main.wlg", "import { writeLn } from \"std:console\"; writeLn(42);");
        var config = Write("weblingo.config", "{\"vm\":" + value + "}");
        var (exit, output, error) = Weblingo("check", program);
        Assert.Equal((WebLingoCommandLine.FileUnreadable, ""), (exit, output));
        Assert.StartsWith(config + ": 'vm' is not a setting; the settings are \"schemes\", \"mode\", ", error);
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
