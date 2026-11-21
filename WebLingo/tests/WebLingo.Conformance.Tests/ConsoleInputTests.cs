using WebLingo.Cli;
using WebLingo.Platform.DotNet;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:system</c>'s console input (the owner's answers, 19 November 2026, "Console Input"), through <c>weblingo</c>,
/// given its input as piped text is given: <c>readLine()</c>, a task, and <c>readLineSync()</c>, each a line without its
/// line break, <c>null</c> at the input's end; <c>prompt(text)</c>, writing the text, then reading; <c>readPassword()</c>,
/// a line where the input is not a console's; the errors; and no console at all, an <c>IOError</c>. Each program runs as
/// a script, on the register VM, and as a <c>.wlbs</c> for the stack VM, given the same input, which must give the same.
/// </summary>
public sealed class ConsoleInputTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-input-" + Guid.NewGuid().ToString("N"));

    public ConsoleInputTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>The console's writing and reading, std:console's, which a script imports (section 3.9, "The console: std:console").</summary>
    private const string Reading = "import { writeLn, prompt, readLine, readLineSync, readPassword } from \"std:console\"; ";

    [Fact]
    public void AScriptReadsItsInputToItsEnd()
    {
        var program = Reading + "var name = await prompt(\"Your name: \"); writeLn($\"Hello, {name}!\"); var count = 0; var line = await readLine(); "
                      + "while (line != null) { count++; writeLn($\"{count}: {line}\"); line = readLineSync(); } "
                      + "writeLn([await readLine(), readLineSync(), await readPassword(), readLine() is Task]);";
        Assert.Equal(("Your name: Hello, Anna!\n1: żółw\n2: \n3: last\n[null, null, null, true]\n", ""),
            Run(program, "Anna\r\nżółw\n\nlast"));
    }

    [Fact]
    public void APasswordIsALineWhereTheInputIsNotAConsole()
    {
        Assert.Equal(("[\"s3cret\", \"next\"]\n", ""), Run(Reading + "writeLn([await readPassword(), readLineSync()]);", "s3cret\nnext\n"));
    }

    [Fact]
    public void WrongArgumentsAreTypeErrors()
    {
        Assert.Equal(("WL3001 The argument of 'prompt' must be a string, not the integer 5.\n", ""),
            Run(Reading + "try { await prompt(5); } catch (TypeError e) { writeLn(e.code + \" \" + e.message); }", ""));
    }

    [Fact]
    public void WithoutAConsoleAReadIsAnIOError()
    {
        // As a script reads where its host gives it no console: weblingo given no input.
        var path = Path.Combine(_folder, "noconsole.wlg");
        File.WriteAllText(path, Reading + "foreach (var read in [() => readLineSync(), () => readLine(), () => prompt(\"x\"), () => readPassword()]) "
                                + "{ try { read(); } catch (IOError e) { writeLn(e.message); } }");
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", path], output, new StringWriter()));
        var message = "reads a console, and this program has none.";
        Assert.Equal($"'readLineSync' {message}\n'readLine' {message}\n'prompt' {message}\n'readPassword' {message}\n", output.ToString());
    }

    /// <summary>
    /// The program run as a script, given <paramref name="input"/> as its console's, on the register VM, then compiled for
    /// the stack VM and run from its <c>.wlbs</c>, given it again: what both write, which must be the same, and their errors.
    /// </summary>
    private (string Output, string Error) Run(string program, string input)
    {
        var path = Path.Combine(_folder, "program.wlg");
        File.WriteAllText(path, program);
        var register = Weblingo(["run", path], input);
        Assert.Equal(WebLingoCommandLine.Success, Weblingo(["compile", "--stack", path], "").Exit);
        var stack = Weblingo(["run", Path.ChangeExtension(path, ".wlbs")], input);
        Assert.Equal((register.Output, register.Error), (stack.Output, stack.Error));
        return (register.Output, register.Error);
    }

    private static (int Exit, string Output, string Error) Weblingo(string[] args, string input)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(args, output, error, new DotNetConsoleInput(new StringReader(input), interactive: false));
        return (exit, output.ToString().ReplaceLineEndings("\n"), error.ToString());
    }
}
