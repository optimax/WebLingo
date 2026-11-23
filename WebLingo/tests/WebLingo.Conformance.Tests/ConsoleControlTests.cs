using WebLingo.Cli;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:console</c>'s <c>Console</c> and <c>ConsoleColor</c> (specification, section 3.9, "The console: <c>std:console</c>";
/// the owner's answers, 20 November 2026, "Console Module", A4 to A6, and its part 2's, A1 to A5), each program run on both
/// VMs, which must give the same, with a console of the test's own, which records what is asked of it and answers as a
/// console would; and, through <c>weblingo</c>, its output a pipe's, as .NET's console gives it.
/// </summary>
public sealed class ConsoleControlTests
{
    private const string Import = "import { Console, ConsoleColor, writeLn } from \"std:console\"; ";

    [Fact]
    public void ConsolesMembersAreDotNetsInLowerCamelCase()
    {
        var console = new FakeConsole();
        var program = Import + "Console.title = \"tea\"; Console.foregroundColor = ConsoleColor.darkBlue; Console.backgroundColor = \"white\"; "
                      + "Console.cursorLeft = 3; Console.cursorTop = 4; Console.cursorSize = 50; Console.cursorVisible = false; "
                      + "Console.windowWidth = 100; Console.windowHeight = 30; Console.windowLeft = 1; Console.windowTop = 2; "
                      + "Console.bufferWidth = 120; Console.bufferHeight = 300; Console.treatControlCAsInput = true; "
                      + "writeLn([Console.title, Console.foregroundColor, Console.backgroundColor, Console.cursorLeft, Console.cursorTop, "
                      + "Console.cursorSize, Console.cursorVisible]); "
                      + "writeLn([Console.windowWidth, Console.windowHeight, Console.windowLeft, Console.windowTop, Console.bufferWidth, "
                      + "Console.bufferHeight, Console.largestWindowWidth, Console.largestWindowHeight]); "
                      + "writeLn([Console.keyAvailable, Console.capsLock, Console.numberLock, Console.treatControlCAsInput, "
                      + "Console.isInputRedirected, Console.isOutputRedirected, Console.isErrorRedirected, Console.inputEncoding, Console.outputEncoding]); "
                      + "Console.setCursorPosition(7, 8); writeLn(Console.getCursorPosition()); Console.beep(); Console.beep(440, 100); "
                      + "Console.clear(); Console.resetColor(); Console.setWindowSize(80, 25); Console.setWindowPosition(0, 1); "
                      + "Console.setBufferSize(80, 1000); Console.moveBufferArea(0, 0, 5, 1, 10, 6); Console.moveBufferArea(0, 0, 5, 1, 10, 7, \"*\", \"red\");";
        Assert.Equal(("", "[\"tea\", \"darkBlue\", \"white\", 3, 4, 50, false]\n[100, 30, 1, 2, 120, 300, 200, 60]\n"
                          + "[true, false, true, true, false, true, false, \"utf-8\", \"utf-8\"]\n{\"left\": 7, \"top\": 8}\n", ""),
            Run(program, console));
        Assert.Equal(["beep", "beep 440 100", "clear", "resetColor", "setWindowSize 80 25", "setWindowPosition 0 1",
            "setBufferSize 80 1000", "moveBufferArea 0 0 5 1 10 6   ", "moveBufferArea 0 0 5 1 10 7 * red "], console.Calls);
    }

    [Fact]
    public void AColourIsANameOrAConstantOfConsoleColor()
    {
        // ConsoleColor's constants are the names, which a colour member takes and gives (A5); a wrong one WL3001.
        var program = Import + "writeLn([ConsoleColor.black, ConsoleColor.darkYellow, ConsoleColor.white, ConsoleColor.gray == \"gray\"]); "
                      + "Console.foregroundColor = \"darkCyan\"; writeLn(Console.foregroundColor == ConsoleColor.darkCyan); "
                      + "try { Console.foregroundColor = \"purple\"; } catch (TypeError e) { writeLn(e.message); } "
                      + "try { ConsoleColor.red = \"blue\"; } catch (TypeError e) { writeLn(e.code); }";
        Assert.Equal(("", "[\"black\", \"darkYellow\", \"white\", true]\ntrue\n"
                          + "'Console.foregroundColor' is a colour, one of ConsoleColor's names, \"black\" to \"white\", not a string.\nWL3001\n", ""),
            Run(program, new FakeConsole()));
        // .NET's colours, each by its name, the same as ConsoleColor's constants.
        string[] names = ["black", "darkBlue", "darkGreen", "darkCyan", "darkRed", "darkMagenta", "darkYellow", "gray", "darkGray",
            "blue", "green", "cyan", "red", "magenta", "yellow", "white"];
        Assert.Equal(names, Enum.GetValues<ConsoleColor>().Select(DotNetConsole.Name));
        Assert.Equal(("", "[\"" + string.Join("\", \"", names) + "\"]\n", ""),
            Run(Import + "writeLn([" + string.Join(", ", names.Select(n => "ConsoleColor." + n)) + "]);", new FakeConsole()));
    }

    [Fact]
    public void ConsolesWritingAndReadingAreItsMembersToo()
    {
        // As .NET's Console has them (A4), an instance's toString() run as write runs it; read() a character at a time (A3).
        var program = Import + "class P { toString() { return \"P!\"; } } Console.write(1); Console.writeLn(new P()); Console.print(null); "
                      + "Console.printLn([2]); Console.writeError(\"e\"); Console.writeErrorLn(new P()); "
                      + "writeLn([await Console.read(), Console.readSync(), await Console.read(), Console.readLineSync(), await Console.readLine(), "
                      + "Console.readSync(), await Console.read()]);";
        var errors = new WebLingoCollectedOutput();
        Assert.Equal(("", "1P!\nnull[2]\n[\"a\", \"😀\", \"b\", \"\", \"tea\", null, null]\n", ""),
            Run(program, new FakeConsole(), "a😀b\ntea\n", errors));
        Assert.Equal("eP!\n", errors.Text);
    }

    [Fact]
    public void AKeyReadIsAMap()
    {
        // A key read, a task or at once, a map of its key's name, its character, and its modifiers (A6).
        var console = new FakeConsole();
        console.Keys.Enqueue(new WebLingoConsoleKey("enter", "\r", false, false, false));
        console.Keys.Enqueue(new WebLingoConsoleKey("upArrow", null, true, false, true));
        var program = Import + "writeLn(await Console.readKey()); writeLn(Console.readKeySync(true));";
        Assert.Equal(("", "{\"key\": \"enter\", \"char\": \"\\r\", \"shift\": false, \"alt\": false, \"control\": false}\n"
                          + "{\"key\": \"upArrow\", \"char\": null, \"shift\": true, \"alt\": false, \"control\": true}\n", ""),
            Run(program, console));
        Assert.Equal(["readKey False", "readKey True"], console.Calls);
    }

    [Fact]
    public void WhatTheConsoleCannotDoIsAnError()
    {
        // A member the platform has not, a PlatformError, WL3022 (A5); one without a console, an IOError; a value it does
        // not take, WL3001, as an argument of the wrong kind is.
        var program = Import + "try { writeLn(Console.capsLock); } catch (PlatformError e) { writeLn(e.code + \" \" + e.message); } "
                      + "try { Console.setCursorPosition(1, 99999); } catch (TypeError e) { writeLn(e.message); } "
                      + "try { Console.setCursorPosition(\"1\", 2); } catch (TypeError e) { writeLn(e.message); } "
                      + "try { Console.beep(440); } catch (TypeError e) { writeLn(e.message); } "
                      + "try { Console.clear(); } catch (IOError e) { writeLn(e.code + \" \" + e.message); } "
                      + "try { Console.onCancelKeyPress(5); } catch (TypeError e) { writeLn(e.message); }";
        Assert.Equal(("", "WL3022 'Console.capsLock' is not supported here: reading Caps Lock is Windows's alone.\n"
                          + "'Console.setCursorPosition' was given what the console does not take: the top is beyond the buffer.\n"
                          + "The first argument of 'setCursorPosition', the left, must be an integer, not a string.\n"
                          + "The second argument of 'beep', the duration, must be an integer, in milliseconds, not null.\n"
                          + "WL3011 'Console.clear' needs a console, and this one cannot: its output is a pipe.\n"
                          + "The argument of 'onCancelKeyPress' must be a function, or null, not the integer 5.\n", ""),
            Run(program, new FakeConsole { Failing = true }));
        Assert.Equal(("", "", "WL3011: 'Console.title' needs a console, and this program has none."), Run(Import + "Console.title = \"x\";", null));
    }

    [Fact]
    public void CtrlCGoesToTheHandlerWhichKeepsTheProgramRunning()
    {
        // Each press to the handler, on the script's worker, a handler giving true keeping the program running, anything
        // else ending it as the press would have (A1); a handler may be asynchronous, and null takes it away.
        var console = new FakeConsole();
        console.Presses.Enqueue("c");
        console.Presses.Enqueue("c");
        console.Presses.Enqueue("break");
        var program = Import + "Console.onCancelKeyPress(async key => { writeLn(\"pressed \" + key); return key == \"c\"; }); "
                      + "await delay(10); writeLn(\"still running\");";
        Assert.Equal(("", "pressed c\npressed c\npressed break\nstill running\n", ""), Run(program, console));
        Assert.Equal("break", console.Ended);

        console = new FakeConsole();
        program = Import + "Console.onCancelKeyPress(key => { writeLn(\"first \" + key); return true; }); Console.onCancelKeyPress(null); "
                  + "await delay(10); writeLn(Console.title);";
        Assert.Equal(("", "fake\n", ""), Run(program, console));
        Assert.Null(console.Ended);
        Assert.False(console.Watching);
    }

    [Fact]
    public void WeblingoGivesAScriptTheConsoleItHas()
    {
        // Through weblingo, its output a pipe's: what .NET's console says of it, and an IOError where it cannot.
        var folder = Path.Combine(Path.GetTempPath(), "weblingo-console-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var script = Path.Combine(folder, "console.wlg");
            File.WriteAllText(script, Import + "writeLn([Console.isOutputRedirected, Console.outputEncoding, ConsoleColor.red]);");
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", script], output, error, null));
            Assert.Equal(("[true, \"utf-8\", \"red\"]\n", ""), (output.ToString(), error.ToString()));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The program, given <paramref name="console"/>, or none, and <paramref name="input"/> as its console's input, compiled
    /// and run: its diagnostics, what it writes, and its runtime error.
    /// </summary>
    private static (string Diagnostics, string Output, string Error) Run(string source, FakeConsole? console, string input = "",
        WebLingoCollectedOutput? errors = null)
    {
        var services = new WebLingoBuiltinRegistry(new WebLingoDelayFunction());
        var schemes = new Dictionary<string, string>(StringComparer.Ordinal) { ["std"] = Path.Combine(AppContext.BaseDirectory, "std") };
        var options = new WebLingoCompilerOptions([new WebLingoBuiltinDeclaration("delay", 1)])
        {
            Native = new WebLingoNativeModuleLoader(schemes, services)
        };
        var output = new WebLingoCollectedOutput();
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
        {
            Services = services,
            Output = output,
            ErrorOutput = errors ?? new WebLingoCollectedOutput(),
            Console = console,
            ConsoleInput = new DotNetConsoleInput(new StringReader(input), interactive: false),
            SchedulerHost = new DotNetSchedulerHost(),
            InstructionBudget = 1_000_000
        };
        string diagnostics;
        WebLingoExecutionResult? result;
        var compiled = new WebLingoCompiler(options).Compile(source, "program.wlg");
        diagnostics = string.Join(" | ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
        result = compiled.Module is { } module
            ? new WebLingoVirtualMachine { Loader = new WebLingoModuleLoader(options) }.Execute(module, context)
            : null;
        return (diagnostics, output.Text, result?.Error is { } error ? $"{error.Code}: {error.Message}" : "");
    }

    /// <summary>A console of the test's own: what is asked of it recorded, keys and presses given as queued.</summary>
    private sealed class FakeConsole : IWebLingoConsole
    {
        private readonly Queue<WebLingoConsoleKey> _keys = new();
        private readonly Queue<string> _presses = new();
        private int _generation;
        private Action<string?>? _pending;

        public List<string> Calls { get; } = [];
        public Queue<WebLingoConsoleKey> Keys => _keys;
        public Queue<string> Presses => _presses;
        public string? Ended { get; private set; }
        public bool Watching { get; private set; }

        /// <summary>Whether it fails as a console whose output is a pipe on a platform without Windows's members does.</summary>
        public bool Failing { get; init; }

        /// <summary>One for the other VM, given what this one was given.</summary>
        public FakeConsole Fresh()
        {
            var fresh = new FakeConsole { Failing = Failing };
            foreach (var key in _keys) fresh._keys.Enqueue(key);
            foreach (var press in _presses) fresh._presses.Enqueue(press);
            return fresh;
        }

        public string Title { get; set; } = "fake";
        public string ForegroundColor { get; set; } = "gray";
        public string BackgroundColor { get; set; } = "black";
        public int CursorLeft { get; set; }
        public int CursorTop { get; set; }
        public int CursorSize { get; set; } = 25;
        public bool CursorVisible { get; set; } = true;
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public int BufferWidth { get; set; }
        public int BufferHeight { get; set; }
        public int LargestWindowWidth => 200;
        public int LargestWindowHeight => 60;
        public bool KeyAvailable => true;
        public bool CapsLock => Failing ? throw new PlatformNotSupportedException("reading Caps Lock is Windows's alone") : false;
        public bool NumberLock => true;
        public bool TreatControlCAsInput { get; set; }
        public bool IsInputRedirected => false;
        public bool IsOutputRedirected => true;
        public bool IsErrorRedirected => false;

        public void Beep() => Calls.Add("beep");
        public void Beep(int frequency, int duration) => Calls.Add($"beep {frequency} {duration}");

        public void Clear()
        {
            if (Failing) throw new IOException("its output is a pipe");
            Calls.Add("clear");
        }

        public void ResetColor() => Calls.Add("resetColor");
        public (int Left, int Top) GetCursorPosition() => (CursorLeft, CursorTop);

        public void SetCursorPosition(int left, int top)
        {
            if (top > 1000) throw new ArgumentOutOfRangeException(nameof(top), top, "the top is beyond the buffer.");
            (CursorLeft, CursorTop) = (left, top);
        }

        public void SetWindowSize(int width, int height) => Calls.Add($"setWindowSize {width} {height}");
        public void SetWindowPosition(int left, int top) => Calls.Add($"setWindowPosition {left} {top}");
        public void SetBufferSize(int width, int height) => Calls.Add($"setBufferSize {width} {height}");

        public void MoveBufferArea(int sourceLeft, int sourceTop, int sourceWidth, int sourceHeight, int targetLeft, int targetTop,
            string? sourceCharacter, string? sourceForeground, string? sourceBackground) =>
            Calls.Add($"moveBufferArea {sourceLeft} {sourceTop} {sourceWidth} {sourceHeight} {targetLeft} {targetTop} {sourceCharacter} "
                      + $"{sourceForeground} {sourceBackground}");

        public WebLingoConsoleKey ReadKey(bool intercept)
        {
            Calls.Add($"readKey {intercept}");
            return _keys.Dequeue();
        }

        public int WatchCancelKeyPress(bool watching)
        {
            Watching = watching;
            var pending = _pending;
            _pending = null;
            pending?.Invoke(null);
            return ++_generation;
        }

        public void NextCancelKeyPress(int generation, Action<string?> pressed)
        {
            // A press queued comes at once, as if typed; none, and no press comes, as when nobody types one.
            if (generation != _generation || !Watching) pressed(null);
            else if (_presses.TryDequeue(out var key)) pressed(key);
            else _pending = pressed;
        }

        public void EndAsCancelled(string key) => Ended = key;
    }
}
