using System.Diagnostics;
using System.Text;
using WebLingo.Cli;
using Xunit;

namespace WebLingo.Conformance.Tests;

/// <summary>
/// The text <c>weblingo</c> writes to a pipe, its output and its errors, is UTF-8 without a byte-order mark, on Windows as
/// elsewhere, whatever the console's code page (<c>WebLingoConsoleText</c>; the owner's answer, 18 November 2026, "Text
/// Written on Windows: UTF-8"): <c>weblingo</c> run as a program of its own, from beside the tests.
/// </summary>
public sealed class ConsoleTextTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-console-" + Guid.NewGuid().ToString("N"));

    public ConsoleTextTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task TextIsWrittenAsUtf8()
    {
        var script = Path.Combine(_folder, "letters.wlg");
        File.WriteAllText(script, "writeLn(\"żółw € 😀\");\nthrow new Error(\"ŻÓŁW\");\n");
        var start = new ProcessStartInfo(Program(), ["run", script])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // A program of .NET's own, run so, writes in the console's code page on Windows; weblingo writes UTF-8.
        using var process = Process.Start(start)!;
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(output), process.StandardError.BaseStream.CopyToAsync(error));
        await process.WaitForExitAsync();

        Assert.Equal(3, process.ExitCode);
        Assert.Equal("żółw € 😀\n", Strict(output.ToArray()));
        Assert.EndsWith("An error was thrown and not caught: ŻÓŁW", Strict(error.ToArray()).TrimEnd());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASubprocessReceivesLiteralArgumentsAndReturnsItsOutputAndExitCode(bool stack)
    {
        var child = Write("child.wlg", "param message; writeLn(message); throw new Error(\"from the child\");");
        var script = Write("run.wlg", """
            param executable;
            param child;
            import process from "std:process";
            var result = await process.run(executable, ["run", child, "message= spaces & \"quoted\" żółw"]);
            writeLn([result["exitCode"], result["stdout"] == " spaces & \"quoted\" żółw\n",
                result["stderr"].contains("ERROR WL3008 (Error): An error was thrown and not caught: from the child")]);
            """);
        var output = new StringWriter();
        var error = new StringWriter();
        if (stack)
        {
            Assert.Equal(WebLingoCommandLine.Success,
                WebLingoCommandLine.Run(["compile", "--stack", script], output, error));
            script = Path.ChangeExtension(script, ".wlbc");
        }
        var exit = WebLingoCommandLine.Run(["run", script, "executable=" + Program(), "child=" + child], output, error);
        Assert.Equal((WebLingoCommandLine.Success, "[3, true, true]\n", ""), (exit, output.ToString(), error.ToString()));
    }

    [Fact]
    public void ASubprocessReportsOutputLimitsAndTimeouts()
    {
        var child = Write("child.wlg", "param slow = \"no\"; if (slow == \"yes\") await delay(60000); writeLn(\"tea\");");
        var script = Write("run.wlg", """
            param executable;
            param child;
            import process from "std:process";
            try { await process.run(executable, ["run", child], { maxOutputBytes: 3 }); }
            catch (IOError e) { writeLn(e.code + " " + e.message); }
            try { await process.run(executable, ["run", child, "slow=yes"], { timeoutMs: 100 }); }
            catch (TimeoutError e) { writeLn(e.code + " " + e.message); }
            """);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(["run", script, "executable=" + Program(), "child=" + child], output, error);
        Assert.Equal((WebLingoCommandLine.Success,
            "WL3011 Process output exceeded 3 bytes.\nWL3010 The process exceeded its 100 ms timeout.\n", ""),
            (exit, output.ToString(), error.ToString()));
    }

    private string Write(string name, string source)
    {
        var file = Path.Combine(_folder, name);
        File.WriteAllText(file, source);
        return file;
    }

    /// <summary>The bytes read as UTF-8, a byte-order mark or a byte that is not UTF-8 failing the test.</summary>
    private static string Strict(byte[] bytes)
    {
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], "The text begins with a byte-order mark.");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
    }

    /// <summary>weblingo's program, beside the tests, as its project's reference puts it there.</summary>
    private static string Program()
    {
        var program = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "weblingo.exe" : "weblingo");
        Assert.True(File.Exists(program), $"{program} is not beside the tests.");
        return program;
    }
}
