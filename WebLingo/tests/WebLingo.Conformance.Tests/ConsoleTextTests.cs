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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureFailureStopsADescendantAfterTheLauncherExits(bool overflow)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;

        var marker = Path.Combine(_folder, "descendant.pid");
        var (launcher, arguments) = OperatingSystem.IsWindows()
            ? MakeWindowsLauncher(marker, overflow)
            : MakeLinuxLauncher(marker, overflow);
        var script = Write("run.wlg", """
            param executable;
            param launchArgs;
            import { Json } from "std:json";
            import process from "std:process";
            try {
                var result = await process.run(executable, Json.parse(launchArgs), {
                    timeoutMs: 6000, maxOutputBytes: 4096
                });
                writeLn(result);
            }
            catch (TimeoutError e) { writeLn(e.code + " " + e.message); }
            catch (IOError e) { writeLn(e.code + " " + e.message); }
            """);
        var output = new StringWriter();
        var error = new StringWriter();
        var launchArgs = System.Text.Json.JsonSerializer.Serialize(arguments);
        var exit = WebLingoCommandLine.Run(
            ["run", script, "executable=" + launcher, "launchArgs=" + launchArgs],
            output, error);
        Assert.True(File.Exists(marker), $"The descendant did not run after its launcher exited. CLI: {output}{error}");
        var pid = int.Parse(File.ReadAllText(marker), System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            var expected = overflow ? "WL3011 Process output exceeded 4096 bytes.\n"
                : "WL3010 The process exceeded its 6000 ms timeout.\n";
            var observed = File.Exists(marker + ".output") ? File.ReadAllText(marker + ".output") : "no output log";
            Assert.True((WebLingoCommandLine.Success, expected, "") == (exit, output.ToString(), error.ToString()),
                $"Expected {expected.TrimEnd()}, got {exit}: {output}{error}. Descendant: {observed}.");
            Assert.True(SpinWait.SpinUntil(() => !IsRunning(pid), TimeSpan.FromSeconds(3)),
                $"Descendant {pid} survived capture cleanup.");
        }
        finally
        {
            if (IsRunning(pid))
            {
                using var leftover = Process.GetProcessById(pid);
                leftover.Kill(entireProcessTree: true);
            }
        }
    }

    private static (string Program, string[] Arguments) MakeWindowsLauncher(string marker, bool overflow)
    {
        var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var writeOutput = $$"""
            $raw = [Console]::OpenStandardOutput()
            $bytes = [Text.Encoding]::ASCII.GetBytes('x' * 8192)
            try {
              $raw.Write($bytes, 0, $bytes.Length)
              $raw.Flush()
              [IO.File]::WriteAllText('{{marker.Replace("'", "''")}}.output', $raw.GetType().FullName)
            }
            catch { [IO.File]::WriteAllText('{{marker.Replace("'", "''")}}.output', [string]$_) }
            """;
        var launcher = $$"""
            $child = @'
            $launcher = Get-Process -Id PARENT_ID -ErrorAction SilentlyContinue
            if ($launcher -and !$launcher.WaitForExit(10000)) { exit 1 }
            [IO.File]::WriteAllText('{{marker.Replace("'", "''")}}', [string]$PID)
            {{(overflow ? writeOutput : "")}}
            Start-Sleep -Seconds 60
            '@ -replace 'PARENT_ID', [string]$PID
            $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($child))
            Add-Type @'
            using System;
            using System.ComponentModel;
            using System.Runtime.InteropServices;
            using System.Text;

            public static class TestLauncher {
                [StructLayout(LayoutKind.Sequential)]
                struct Startup {
                    public int Size;
                    public IntPtr Reserved, Desktop, Title;
                    public int X, Y, XSize, YSize, Columns, Rows, Fill, Flags;
                    public short Show, ReservedLength;
                    public IntPtr ReservedBytes, Input, Output, Error;
                }
                [StructLayout(LayoutKind.Sequential)]
                struct Child {
                    public IntPtr Process, Thread;
                    public int ProcessId, ThreadId;
                }
                [DllImport("kernel32")] static extern IntPtr GetStdHandle(int kind);
                [DllImport("kernel32")] static extern bool CloseHandle(IntPtr handle);
                [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
                static extern bool CreateProcess(string path, StringBuilder args, IntPtr ps, IntPtr ts,
                    bool inherit, uint flags, IntPtr environment, IntPtr directory, ref Startup startup, out Child child);

                public static void Start(string path, string args) {
                    var info = new Startup { Size = Marshal.SizeOf(typeof(Startup)), Flags = 0x100,
                        Input = GetStdHandle(-10), Output = GetStdHandle(-11), Error = GetStdHandle(-12) };
                    Child child;
                    if (!CreateProcess(path, new StringBuilder("\"" + path + "\" " + args),
                        IntPtr.Zero, IntPtr.Zero, true, 0x08000000, IntPtr.Zero, IntPtr.Zero, ref info, out child))
                        throw new Win32Exception();
                    CloseHandle(child.Thread);
                    CloseHandle(child.Process);
                }
            }
            '@
            [TestLauncher]::Start('{{shell.Replace("'", "''")}}', "-NoProfile -NonInteractive -EncodedCommand $encoded")
            """;
        return (shell, ["-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(launcher))]);
    }

    private static (string Program, string[] Arguments) MakeLinuxLauncher(string marker, bool overflow)
    {
        const string launcher = """
            launcher=$$
            (
                while read -r pid name state rest < "/proc/$launcher/stat"; do
                    if [ "$state" = Z ]; then break; fi
                    sleep .01
                done 2>/dev/null
                exec /bin/sh -c 'echo $$ > "$1"; if [ "$2" = yes ]; then printf "%8192s" overflow; fi; exec sleep 60' child "$1" "$2"
            ) &
            """;
        return ("/bin/sh", ["-c", launcher, "launcher", marker, overflow ? "yes" : "no"]);
    }

    private static bool IsRunning(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var stat = File.ReadAllText($"/proc/{pid}/stat");
                return stat[stat.LastIndexOf(')') + 2] != 'Z';
            }
            catch (IOException) { return false; }
        }
        try
        {
            using var child = Process.GetProcessById(pid);
            return !child.HasExited;
        }
        catch (ArgumentException) { return false; }
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
