using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Console;

/// <summary>
/// <c>std:console</c>'s classes (specification, section 3.9, "The console: <c>std:console</c>"; the owner's answers, 20
/// November 2026, "Console Module", A4 to A6, and its part 2's, A1 to A5), through the platform's console
/// (<see cref="WebLingoHeap.Console"/>), which <c>weblingo</c> gives a script: <c>Console</c>, its members static, .NET's
/// <c>Console</c>'s in lower camel case, its colours, its cursor, its window and its buffer, its keys, Ctrl+C, its
/// redirections and its encodings, the writing and the reading among them; and <c>ConsoleColor</c>, the colours' names,
/// <c>ConsoleColor.darkBlue</c> being <c>"darkBlue"</c>, which a colour member takes and gives. A member without a console
/// is an <c>IOError</c>, WL3011, and so is one the console cannot do, its output not a console's; one the platform has not,
/// Windows's alone elsewhere, a <c>PlatformError</c>, WL3022 (A5); a value it does not take WL3001.
/// </summary>
public static class WebLingoConsoleClass
{
    /// <summary>The colours, .NET's <c>ConsoleColor</c>'s names in lower camel case, in its order.</summary>
    public static IReadOnlyList<string> Colors { get; } =
    [
        "black", "darkBlue", "darkGreen", "darkCyan", "darkRed", "darkMagenta", "darkYellow", "gray", "darkGray", "blue", "green",
        "cyan", "red", "magenta", "yellow", "white"
    ];

    /// <summary>
    /// <c>Console</c>'s members written in WebLingo: its writing, a value's text made as an interpolation makes it, so that an
    /// instance's <c>toString()</c> runs, as for <c>write</c>, <c>null</c> as <c>null</c>, given to its own <c>__write</c> and
    /// <c>__writeError</c>; and Ctrl+C's handler (A1), called on the script's worker with each press, a handler that gives
    /// anything but <c>true</c> ending the program as the press would have.
    /// </summary>
    private const string WebLingoMethods = """
        static write(value) { this.__write(value == null ? "null" : $"{value}"); }
        static writeLn(value) { this.__write((value == null ? "null" : $"{value}") + "\n"); }
        static print(value) { this.__write(value == null ? "null" : $"{value}"); }
        static printLn(value) { this.__write((value == null ? "null" : $"{value}") + "\n"); }
        static writeError(value) { this.__writeError(value == null ? "null" : $"{value}"); }
        static writeErrorLn(value) { this.__writeError((value == null ? "null" : $"{value}") + "\n"); }
        static onCancelKeyPress(handler) {
          var generation = this.__watchCancelKeyPress(handler);
          if (handler != null) this.__handleCancelKeyPress(handler, generation);
        }
        static async __handleCancelKeyPress(handler, generation) {
          var key = await this.__nextCancelKeyPress(generation);
          while (key != null) {
            var kept = handler(key);
            if (kept is Task) kept = await kept;
            if (kept != true) this.__endAsCancelled(key);
            key = await this.__nextCancelKeyPress(generation);
          }
        }
        """;

    public static void Register(WebLingoNativeClass console, WebLingoNativeClass consoleColor)
    {
        console
            .DeclareStaticProperty("title", Text("title", c => c.Title), SetText("title", (c, v) => c.Title = v))
            .DeclareStaticProperty("foregroundColor", Text("foregroundColor", c => c.ForegroundColor),
                SetColor("foregroundColor", (c, v) => c.ForegroundColor = v))
            .DeclareStaticProperty("backgroundColor", Text("backgroundColor", c => c.BackgroundColor),
                SetColor("backgroundColor", (c, v) => c.BackgroundColor = v))
            .DeclareStaticProperty("cursorLeft", Integer("cursorLeft", c => c.CursorLeft), SetInteger("cursorLeft", (c, v) => c.CursorLeft = v))
            .DeclareStaticProperty("cursorTop", Integer("cursorTop", c => c.CursorTop), SetInteger("cursorTop", (c, v) => c.CursorTop = v))
            .DeclareStaticProperty("cursorSize", Integer("cursorSize", c => c.CursorSize), SetInteger("cursorSize", (c, v) => c.CursorSize = v))
            .DeclareStaticProperty("cursorVisible", Boolean("cursorVisible", c => c.CursorVisible),
                SetBoolean("cursorVisible", (c, v) => c.CursorVisible = v))
            .DeclareStaticProperty("windowWidth", Integer("windowWidth", c => c.WindowWidth), SetInteger("windowWidth", (c, v) => c.WindowWidth = v))
            .DeclareStaticProperty("windowHeight", Integer("windowHeight", c => c.WindowHeight),
                SetInteger("windowHeight", (c, v) => c.WindowHeight = v))
            .DeclareStaticProperty("windowLeft", Integer("windowLeft", c => c.WindowLeft), SetInteger("windowLeft", (c, v) => c.WindowLeft = v))
            .DeclareStaticProperty("windowTop", Integer("windowTop", c => c.WindowTop), SetInteger("windowTop", (c, v) => c.WindowTop = v))
            .DeclareStaticProperty("bufferWidth", Integer("bufferWidth", c => c.BufferWidth), SetInteger("bufferWidth", (c, v) => c.BufferWidth = v))
            .DeclareStaticProperty("bufferHeight", Integer("bufferHeight", c => c.BufferHeight),
                SetInteger("bufferHeight", (c, v) => c.BufferHeight = v))
            .DeclareStaticProperty("largestWindowWidth", Integer("largestWindowWidth", c => c.LargestWindowWidth))
            .DeclareStaticProperty("largestWindowHeight", Integer("largestWindowHeight", c => c.LargestWindowHeight))
            .DeclareStaticProperty("keyAvailable", Boolean("keyAvailable", c => c.KeyAvailable))
            .DeclareStaticProperty("capsLock", Boolean("capsLock", c => c.CapsLock))
            .DeclareStaticProperty("numberLock", Boolean("numberLock", c => c.NumberLock))
            .DeclareStaticProperty("treatControlCAsInput", Boolean("treatControlCAsInput", c => c.TreatControlCAsInput),
                SetBoolean("treatControlCAsInput", (c, v) => c.TreatControlCAsInput = v))
            .DeclareStaticProperty("isInputRedirected", Boolean("isInputRedirected", c => c.IsInputRedirected))
            .DeclareStaticProperty("isOutputRedirected", Boolean("isOutputRedirected", c => c.IsOutputRedirected))
            .DeclareStaticProperty("isErrorRedirected", Boolean("isErrorRedirected", c => c.IsErrorRedirected))
            // A script's text is UTF-8 always, in and out (A4; "Text Written on Windows: UTF-8"), read only.
            .DeclareStaticProperty("inputEncoding", Utf8)
            .DeclareStaticProperty("outputEncoding", Utf8)
            .DeclareStaticMethod("beep", 2, 2, Beep)
            .DeclareStaticMethod("clear", 0, Action("clear", c => c.Clear()))
            .DeclareStaticMethod("resetColor", 0, Action("resetColor", c => c.ResetColor()))
            .DeclareStaticMethod("getCursorPosition", 0, GetCursorPosition)
            .DeclareStaticMethod("setCursorPosition", 2, Pair("setCursorPosition", "the left", "the top", (c, x, y) => c.SetCursorPosition(x, y)))
            .DeclareStaticMethod("setWindowSize", 2, Pair("setWindowSize", "the width", "the height", (c, x, y) => c.SetWindowSize(x, y)))
            .DeclareStaticMethod("setWindowPosition", 2, Pair("setWindowPosition", "the left", "the top", (c, x, y) => c.SetWindowPosition(x, y)))
            .DeclareStaticMethod("setBufferSize", 2, Pair("setBufferSize", "the width", "the height", (c, x, y) => c.SetBufferSize(x, y)))
            .DeclareStaticMethod("moveBufferArea", 9, 3, MoveBufferArea)
            .DeclareStaticMethod("readKey", 1, 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
                out WebLingoRuntimeError? error) => ReadKey(false, a, heap, out result, out error))
            .DeclareStaticMethod("readKeySync", 1, 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
                out WebLingoRuntimeError? error) => ReadKey(true, a, heap, out result, out error))
            .DeclareStaticMethod("read", 0, new WebLingoReadCharacterFunction(false).TryInvoke)
            .DeclareStaticMethod("readSync", 0, new WebLingoReadCharacterFunction(true).TryInvoke)
            .DeclareStaticMethod("readLine", 0, new WebLingoReadLineFunction(false).TryInvoke)
            .DeclareStaticMethod("readLineSync", 0, new WebLingoReadLineFunction(true).TryInvoke)
            .DeclareStaticMethod("prompt", 1, new WebLingoPromptFunction().TryInvoke)
            .DeclareStaticMethod("readPassword", 0, new WebLingoReadPasswordFunction().TryInvoke)
            .DeclareStaticMethod("__write", 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
                out WebLingoRuntimeError? error) => WriteText(false, a, heap, out result, out error))
            .DeclareStaticMethod("__writeError", 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
                out WebLingoRuntimeError? error) => WriteText(true, a, heap, out result, out error))
            .DeclareStaticMethod("__watchCancelKeyPress", 1, WatchCancelKeyPress)
            .DeclareStaticMethod("__nextCancelKeyPress", 1, NextCancelKeyPress)
            .DeclareStaticMethod("__endAsCancelled", 1, EndAsCancelled)
            .DeclareWebLingoMethods(WebLingoMethods);
        foreach (var color in Colors)
        {
            consoleColor.DeclareStaticProperty(color, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
                out WebLingoRuntimeError? error) => heap.TryAllocateString(color, out result, out error));
        }
    }

    // The members' makers, each a native function of the console, its value checked and given.

    private static WebLingoNativeFunction Text(string member, Func<IWebLingoConsole, string> read) =>
        (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            return TryOn(member, heap, read, out var text, out error) && heap.TryAllocateString(text, out result, out error);
        };

    private static WebLingoNativeFunction Integer(string member, Func<IWebLingoConsole, int> read) =>
        (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!TryOn(member, heap, read, out var number, out error)) return false;
            result = WebLingoValue.FromInt64(number);
            return true;
        };

    private static WebLingoNativeFunction Boolean(string member, Func<IWebLingoConsole, bool> read) =>
        (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!TryOn(member, heap, read, out var value, out error)) return false;
            result = WebLingoValue.FromBoolean(value);
            return true;
        };

    private static WebLingoNativeFunction SetText(string member, Action<IWebLingoConsole, string> write) =>
        (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (a[0].Kind != WebLingoValueKind.String)
            {
                error = Invalid(member, "a string", a[0], heap);
                return false;
            }
            var text = heap.GetString(a[0]);
            return TryOn(member, heap, c => { write(c, text); return true; }, out _, out error);
        };

    private static WebLingoNativeFunction SetColor(string member, Action<IWebLingoConsole, string> write) =>
        (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!TryColor(a[0], heap, out var color))
            {
                error = Invalid(member, "a colour, one of ConsoleColor's names, \"black\" to \"white\"", a[0], heap);
                return false;
            }
            return TryOn(member, heap, c => { write(c, color); return true; }, out _, out error);
        };

    private static WebLingoNativeFunction SetInteger(string member, Action<IWebLingoConsole, int> write) =>
        (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (a[0].Kind != WebLingoValueKind.Int64 || a[0].AsInt64 is < int.MinValue or > int.MaxValue)
            {
                error = Invalid(member, "an integer", a[0], heap);
                return false;
            }
            var number = (int)a[0].AsInt64;
            return TryOn(member, heap, c => { write(c, number); return true; }, out _, out error);
        };

    private static WebLingoNativeFunction SetBoolean(string member, Action<IWebLingoConsole, bool> write) =>
        (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (a[0].Kind != WebLingoValueKind.Boolean)
            {
                error = Invalid(member, "a Boolean", a[0], heap);
                return false;
            }
            var value = a[0].AsBoolean;
            return TryOn(member, heap, c => { write(c, value); return true; }, out _, out error);
        };

    private static WebLingoNativeFunction Action(string member, Action<IWebLingoConsole> act) =>
        (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            return TryOn(member, heap, c => { act(c); return true; }, out _, out error);
        };

    /// <summary>A method of two integers, <c>setCursorPosition(left, top)</c> and the like.</summary>
    private static WebLingoNativeFunction Pair(string member, string first, string second, Action<IWebLingoConsole, int, int> act) =>
        (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!TryInteger(new WebLingoArgument(member, 0, 2, first, "an integer"), a[0], heap, out var x, out error)
                || !TryInteger(new WebLingoArgument(member, 1, 2, second, "an integer"), a[1], heap, out var y, out error))
            {
                return false;
            }
            return TryOn(member, heap, c => { act(c, x, y); return true; }, out _, out error);
        };

    private static bool Utf8(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        heap.TryAllocateString("utf-8", out result, out error);

    /// <summary><c>beep()</c>, or <c>beep(frequency, duration)</c>, Windows's alone, both given or neither.</summary>
    private static bool Beep(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (a[0].Kind == WebLingoValueKind.Null && a[1].Kind == WebLingoValueKind.Null)
        {
            return TryOn("beep", heap, c => { c.Beep(); return true; }, out _, out error);
        }
        if (!TryInteger(new WebLingoArgument("beep", 0, 2, "the frequency", "an integer, in hertz"), a[0], heap, out var frequency, out error)
            || !TryInteger(new WebLingoArgument("beep", 1, 2, "the duration", "an integer, in milliseconds"), a[1], heap, out var duration, out error))
        {
            return false;
        }
        return TryOn("beep", heap, c => { c.Beep(frequency, duration); return true; }, out _, out error);
    }

    /// <summary><c>getCursorPosition()</c>: a map, <c>{ left, top }</c>.</summary>
    private static bool GetCursorPosition(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryOn("getCursorPosition", heap, c => c.GetCursorPosition(), out var position, out error)) return false;
        var entries = new WebLingoValue[4];
        heap.AddRoots(entries);
        try
        {
            if (!heap.TryAllocateString("left", out entries[0], out error) || !heap.TryAllocateString("top", out entries[2], out error)) return false;
            entries[1] = WebLingoValue.FromInt64(position.Left);
            entries[3] = WebLingoValue.FromInt64(position.Top);
            return heap.TryAllocateMap(entries, 0, 2, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(entries);
        }
    }

    /// <summary>
    /// <c>moveBufferArea(sourceLeft, sourceTop, sourceWidth, sourceHeight, targetLeft, targetTop, sourceChar, sourceForeColor,
    /// sourceBackColor)</c>, the last three optional, as .NET's second overload's, the area left as it is without them.
    /// </summary>
    private static bool MoveBufferArea(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        const string member = "moveBufferArea";
        result = WebLingoValue.Null;
        string[] names = ["the source's left", "the source's top", "the source's width", "the source's height", "the target's left", "the target's top"];
        var numbers = new int[6];
        for (var i = 0; i < 6; i++)
        {
            if (!TryInteger(new WebLingoArgument(member, i, 9, names[i], "an integer"), a[i], heap, out numbers[i], out error)) return false;
        }
        string? character = null, foreground = null, background = null;
        if (a[6].Kind != WebLingoValueKind.Null)
        {
            if (a[6].Kind != WebLingoValueKind.String || heap.GetString(a[6]) is not { Length: 1 } one)
            {
                error = new WebLingoArgument(member, 6, 9, "the character", "a string of one character").Wrong(a[6], heap);
                return false;
            }
            character = one;
        }
        if (a[7].Kind != WebLingoValueKind.Null)
        {
            if (!TryColor(a[7], heap, out var color))
            {
                error = new WebLingoArgument(member, 7, 9, "its colour", "a colour, one of ConsoleColor's names").Wrong(a[7], heap);
                return false;
            }
            foreground = color;
        }
        if (a[8].Kind != WebLingoValueKind.Null)
        {
            if (!TryColor(a[8], heap, out var color))
            {
                error = new WebLingoArgument(member, 8, 9, "its background's colour", "a colour, one of ConsoleColor's names").Wrong(a[8], heap);
                return false;
            }
            background = color;
        }
        if (character is null && (foreground is not null || background is not null))
        {
            error = new WebLingoArgument(member, 6, 9, "the character", "given with its colours").Wrong(a[6], heap);
            return false;
        }
        return TryOn(member, heap, c =>
        {
            c.MoveBufferArea(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5], character, foreground, background);
            return true;
        }, out _, out error);
    }

    /// <summary>
    /// <c>readKey(intercept)</c>, a task, its reading off the worker, and <c>readKeySync(intercept)</c>, at once: the next key
    /// typed, a map, <c>{ key, char, shift, alt, control }</c> (A6), not shown where <c>intercept</c> is <c>true</c>.
    /// </summary>
    private static bool ReadKey(bool synchronous, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        var member = synchronous ? "readKeySync" : "readKey";
        result = WebLingoValue.Null;
        var intercept = false;
        if (a[0].Kind == WebLingoValueKind.Boolean) intercept = a[0].AsBoolean;
        else if (a[0].Kind != WebLingoValueKind.Null)
        {
            error = new WebLingoArgument(member, 0, 1, "whether the key is not shown", "a Boolean").Wrong(a[0], heap);
            return false;
        }
        if (synchronous) return TryOn(member, heap, c => c.ReadKey(intercept), out var key, out error) && TryKey(key, heap, out result, out error);
        if (heap.Console is not { } console)
        {
            error = NoConsole(member);
            return false;
        }
        if (heap.Scheduler is not { } scheduler)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{member}' needs a VM that runs tasks.");
            return false;
        }
        return scheduler.TryRunOffWorker(() => console.ReadKey(intercept), TryKey, exception => Failed(member, exception), out result, out error);
    }

    private static bool TryKey(WebLingoConsoleKey key, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var entries = new WebLingoValue[10];
        heap.AddRoots(entries);
        try
        {
            string[] names = ["key", "char", "shift", "alt", "control"];
            for (var i = 0; i < names.Length; i++)
            {
                if (!heap.TryAllocateString(names[i], out entries[2 * i], out error)) return false;
            }
            if (key.Key is { } name && !heap.TryAllocateString(name, out entries[1], out error)) return false;
            if (key.Character is { } character && !heap.TryAllocateString(character, out entries[3], out error)) return false;
            entries[5] = WebLingoValue.FromBoolean(key.Shift);
            entries[7] = WebLingoValue.FromBoolean(key.Alt);
            entries[9] = WebLingoValue.FromBoolean(key.Control);
            return heap.TryAllocateMap(entries, 0, names.Length, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(entries);
        }
    }

    /// <summary><c>this.__write(text)</c> and <c>__writeError(text)</c>: a string, as it stands, to the output or the errors.</summary>
    private static bool WriteText(bool toErrors, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (a[0].Kind != WebLingoValueKind.String)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "The console writes a string.");
            return false;
        }
        if ((toErrors ? heap.ErrorOutput : heap.Output) is not { } output)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                toErrors ? "This execution has no error output to write to." : "This execution has no output to write to.");
            return false;
        }
        output.Write(heap.GetString(a[0]));
        error = null;
        return true;
    }

    /// <summary><c>this.__watchCancelKeyPress(handler)</c>: Ctrl+C's presses watched for a handler, or not for null; the generation.</summary>
    private static bool WatchCancelKeyPress(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var watching = a[0].Kind != WebLingoValueKind.Null;
        if (watching && !heap.IsCallable(a[0]))
        {
            error = new WebLingoArgument("onCancelKeyPress", 0, 1, "the handler", "a function, or null").Wrong(a[0], heap);
            return false;
        }
        if (!TryOn("onCancelKeyPress", heap, c => c.WatchCancelKeyPress(watching), out var generation, out error)) return false;
        result = WebLingoValue.FromInt64(generation);
        return true;
    }

    /// <summary><c>this.__nextCancelKeyPress(generation)</c>: a task of the next press's key, <c>"c"</c> or <c>"break"</c>; null once no longer watched.</summary>
    private static bool NextCancelKeyPress(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (heap.Console is not { } console)
        {
            error = NoConsole("onCancelKeyPress");
            return false;
        }
        if (heap.Scheduler is not { } scheduler)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "'onCancelKeyPress' needs a VM that runs tasks.");
            return false;
        }
        var generation = a[0].Kind == WebLingoValueKind.Int64 ? (int)a[0].AsInt64 : -1;
        return scheduler.TryWaitForHost<string?>(pressed => console.NextCancelKeyPress(generation, pressed),
            (string? key, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? failed) =>
            {
                (made, failed) = (WebLingoValue.Null, null);
                return key is null || h.TryAllocateString(key, out made, out failed);
            }, out result, out error);
    }

    /// <summary><c>this.__endAsCancelled(key)</c>: the program ended as the press would have, its handler not keeping it running.</summary>
    private static bool EndAsCancelled(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var key = a[0].Kind == WebLingoValueKind.String ? heap.GetString(a[0]) : "c";
        return TryOn("onCancelKeyPress", heap, c => { c.EndAsCancelled(key); return true; }, out _, out error);
    }

    // What the members share.

    /// <summary>A colour's name, ConsoleColor's, a string one of <see cref="Colors"/>.</summary>
    private static bool TryColor(WebLingoValue value, WebLingoHeap heap, out string color)
    {
        color = value.Kind == WebLingoValueKind.String ? heap.GetString(value) : "";
        foreach (var known in Colors)
        {
            if (known == color) return true;
        }
        return false;
    }

    private static bool TryInteger(WebLingoArgument argument, WebLingoValue value, WebLingoHeap heap, out int number, out WebLingoRuntimeError? error)
    {
        number = 0;
        if (value.Kind != WebLingoValueKind.Int64 || value.AsInt64 is < int.MinValue or > int.MaxValue)
        {
            error = argument.Wrong(value, heap);
            return false;
        }
        (number, error) = ((int)value.AsInt64, null);
        return true;
    }

    /// <summary>WL3001: a property set to what it does not take, <c>'Console.cursorLeft' is an integer, not a string.</c></summary>
    private static WebLingoRuntimeError Invalid(string member, string must, WebLingoValue given, WebLingoHeap heap) =>
        new(WebLingoDiagnosticCodes.InvalidOperation, $"'Console.{member}' is {must}, not {WebLingoValueFormatter.Describe(given, heap)}.");

    private static WebLingoRuntimeError NoConsole(string member) =>
        new(WebLingoDiagnosticCodes.IOFailure, $"'Console.{member}' needs a console, and this program has none.");

    /// <summary>Work on the console, its failure the error it means: none, an <c>IOError</c>; the platform's, a <c>PlatformError</c>.</summary>
    private static bool TryOn<T>(string member, WebLingoHeap heap, Func<IWebLingoConsole, T> work, out T value, out WebLingoRuntimeError? error)
    {
        value = default!;
        if (heap.Console is not { } console)
        {
            error = NoConsole(member);
            return false;
        }
        try
        {
            value = work(console);
            error = null;
            return true;
        }
        catch (Exception exception) when (Failed(member, exception) is { } failure)
        {
            error = failure;
            return false;
        }
    }

    /// <summary>The error an exception of the console means, or null for one it does not.</summary>
    private static WebLingoRuntimeError? Failed(string member, Exception exception) => exception switch
    {
        PlatformNotSupportedException => new WebLingoRuntimeError(WebLingoDiagnosticCodes.PlatformNotSupported,
            $"'Console.{member}' is not supported here: {exception.Message}."),
        ArgumentOutOfRangeException => new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
            $"'Console.{member}' was given what the console does not take: {Reason(exception)}."),
        IOException or InvalidOperationException or UnauthorizedAccessException => new WebLingoRuntimeError(
            WebLingoDiagnosticCodes.IOFailure, $"'Console.{member}' needs a console, and this one cannot: {Reason(exception)}."),
        _ => null
    };

    /// <summary>An exception's message, its first line, without .NET's parameter's name, its end's full stop left out.</summary>
    private static string Reason(Exception exception)
    {
        var message = exception.Message;
        var end = message.IndexOfAny(['\r', '\n']);
        if (end >= 0) message = message[..end];
        var parameter = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
        if (parameter >= 0) message = message[..parameter];
        return message.TrimEnd('.', ' ');
    }
}
