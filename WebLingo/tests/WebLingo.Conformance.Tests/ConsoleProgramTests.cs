using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// A script is a console program only where it imports <c>std:console</c> (specification, section 3.9, "The console:
/// <c>std:console</c>"; the owner's answers, 20 November 2026, "Console Module", A7): <c>weblingo</c> asks for a console,
/// which its program makes where the process has none (<see cref="WebLingoConsoleSession"/>), when it first loads
/// <c>std:console</c> to run a script, by a static import or a dynamic one, from source or from bytecode, never to check or
/// compile one, nor for a script that does not import it; and <c>weblingo.exe</c>'s manifest says <c>detached</c>,
/// <c>weblingow.exe</c> being a Windows application.
/// </summary>
public sealed class ConsoleProgramTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-console-programs-" + Guid.NewGuid().ToString("N"));

    public ConsoleProgramTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void OnlyAScriptThatImportsStdConsoleIsGivenAConsole()
    {
        var quiet = Write("quiet.wlg", "var x = 1 + 1;");
        var loud = Write("loud.wlg", "import { writeLn } from \"std:console\"; writeLn(2);");
        var later = Write("later.wlg", "var c = await Module.import(\"std:console\"); c.writeLn(3);");
        Assert.Equal((WebLingoCommandLine.Success, "", false), Run("run", quiet));
        Assert.Equal((WebLingoCommandLine.Success, "2\n", true), Run("run", loud));
        Assert.Equal((WebLingoCommandLine.Success, "3\n", true), Run("run", later));
        Assert.Equal((WebLingoCommandLine.Success, "", false), Run("check", loud));
        Assert.Equal((WebLingoCommandLine.Success, "", false), Run("compile", loud));
        Assert.Equal((WebLingoCommandLine.Success, "2\n", true), Run("run", Path.ChangeExtension(loud, ".wlbr")));
        Assert.Equal((WebLingoCommandLine.Success, "", false), Run("check", Path.ChangeExtension(loud, ".wlbr")));
    }

    [Fact]
    public void WeblingoIsDetachedAndWeblingowIsAWindowsApplication()
    {
        if (!OperatingSystem.IsWindows()) return;
        // weblingo.exe, a console program, whose manifest asks Windows for no console where it is started without one;
        // weblingow.exe, a Windows application, which Windows never gives one.
        var weblingo = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "weblingo.exe"));
        Assert.Equal(3, Subsystem(weblingo));
        Assert.Contains("<consoleAllocationPolicy xmlns=\"http://schemas.microsoft.com/SMI/2024/WindowsSettings\">detached</consoleAllocationPolicy>",
            System.Text.Encoding.UTF8.GetString(weblingo), StringComparison.Ordinal);
        Assert.Equal(2, Subsystem(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "weblingow.exe"))));
    }

    /// <summary>A Windows program's subsystem, from its PE header: 2 a Windows application's, 3 a console program's.</summary>
    private static int Subsystem(byte[] program)
    {
        var header = BitConverter.ToInt32(program, 0x3C);
        return BitConverter.ToUInt16(program, header + 4 + 20 + 68);
    }

    private (int Exit, string Output, bool Asked) Run(string command, string file)
    {
        var asked = false;
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run([command, file], output, error, null, () => asked = true);
        Assert.True(error.ToString() == "", error.ToString());
        return (exit, output.ToString(), asked);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }
}
