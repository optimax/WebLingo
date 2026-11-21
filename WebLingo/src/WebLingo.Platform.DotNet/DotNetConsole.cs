using System.Runtime.Versioning;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// A script's console on .NET (the owner's answers, 20 November 2026, "Console Module" and its part 2): .NET's
/// <see cref="Console"/>, its colours and keys named in lower camel case, what it has on Windows alone a
/// <see cref="PlatformNotSupportedException"/> elsewhere, so that the program says why rather than .NET's own message; and
/// Ctrl+C and Ctrl+Break, which a script's handler takes while it waits for a press, through .NET's <c>CancelKeyPress</c>.
/// </summary>
public sealed class DotNetConsole : IWebLingoConsole
{
    private readonly object _gate = new();
    private bool _subscribed;
    private bool _watching;
    private int _generation;
    private Action<string?>? _waiting;

    public string Title
    {
        get => OperatingSystem.IsWindows() ? Console.Title : throw WindowsOnly("reading the title");
        set => Console.Title = value;
    }

    public string ForegroundColor { get => Name(Console.ForegroundColor); set => Console.ForegroundColor = Color(value); }
    public string BackgroundColor { get => Name(Console.BackgroundColor); set => Console.BackgroundColor = Color(value); }
    public int CursorLeft { get => Console.CursorLeft; set => Console.CursorLeft = value; }
    public int CursorTop { get => Console.CursorTop; set => Console.CursorTop = value; }

    public int CursorSize
    {
        get => Console.CursorSize;
        set
        {
            if (!OperatingSystem.IsWindows()) throw WindowsOnly("setting the cursor's size");
            Console.CursorSize = value;
        }
    }

    public bool CursorVisible
    {
        get => OperatingSystem.IsWindows() ? Console.CursorVisible : throw WindowsOnly("reading whether the cursor is visible");
        set => Console.CursorVisible = value;
    }

    public int WindowWidth
    {
        get => Console.WindowWidth;
        set
        {
            if (!OperatingSystem.IsWindows()) throw WindowsOnly("setting the window's size");
            Console.WindowWidth = value;
        }
    }

    public int WindowHeight
    {
        get => Console.WindowHeight;
        set
        {
            if (!OperatingSystem.IsWindows()) throw WindowsOnly("setting the window's size");
            Console.WindowHeight = value;
        }
    }

    public int WindowLeft
    {
        get => Console.WindowLeft;
        set
        {
            if (!OperatingSystem.IsWindows()) throw WindowsOnly("setting the window's position");
            Console.WindowLeft = value;
        }
    }

    public int WindowTop
    {
        get => Console.WindowTop;
        set
        {
            if (!OperatingSystem.IsWindows()) throw WindowsOnly("setting the window's position");
            Console.WindowTop = value;
        }
    }

    public int BufferWidth
    {
        get => Console.BufferWidth;
        set
        {
            if (!OperatingSystem.IsWindows()) throw WindowsOnly("setting the buffer's size");
            Console.BufferWidth = value;
        }
    }

    public int BufferHeight
    {
        get => Console.BufferHeight;
        set
        {
            if (!OperatingSystem.IsWindows()) throw WindowsOnly("setting the buffer's size");
            Console.BufferHeight = value;
        }
    }

    public int LargestWindowWidth => Console.LargestWindowWidth;
    public int LargestWindowHeight => Console.LargestWindowHeight;
    public bool KeyAvailable => Console.KeyAvailable;
    public bool CapsLock => OperatingSystem.IsWindows() ? Console.CapsLock : throw WindowsOnly("reading Caps Lock");
    public bool NumberLock => OperatingSystem.IsWindows() ? Console.NumberLock : throw WindowsOnly("reading Num Lock");
    public bool TreatControlCAsInput { get => Console.TreatControlCAsInput; set => Console.TreatControlCAsInput = value; }
    public bool IsInputRedirected => Console.IsInputRedirected;
    public bool IsOutputRedirected => Console.IsOutputRedirected;
    public bool IsErrorRedirected => Console.IsErrorRedirected;

    public void Beep() => Console.Beep();

    public void Beep(int frequency, int duration)
    {
        if (!OperatingSystem.IsWindows()) throw WindowsOnly("a beep of a frequency and a duration");
        Console.Beep(frequency, duration);
    }

    public void Clear() => Console.Clear();
    public void ResetColor() => Console.ResetColor();
    public (int Left, int Top) GetCursorPosition() => Console.GetCursorPosition();
    public void SetCursorPosition(int left, int top) => Console.SetCursorPosition(left, top);

    public void SetWindowSize(int width, int height)
    {
        if (!OperatingSystem.IsWindows()) throw WindowsOnly("setting the window's size");
        Console.SetWindowSize(width, height);
    }

    public void SetWindowPosition(int left, int top)
    {
        if (!OperatingSystem.IsWindows()) throw WindowsOnly("setting the window's position");
        Console.SetWindowPosition(left, top);
    }

    public void SetBufferSize(int width, int height)
    {
        if (!OperatingSystem.IsWindows()) throw WindowsOnly("setting the buffer's size");
        Console.SetBufferSize(width, height);
    }

    public void MoveBufferArea(int sourceLeft, int sourceTop, int sourceWidth, int sourceHeight, int targetLeft, int targetTop,
        string? sourceCharacter, string? sourceForeground, string? sourceBackground)
    {
        if (!OperatingSystem.IsWindows()) throw WindowsOnly("moving an area of the buffer");
        if (sourceCharacter is null)
        {
            Console.MoveBufferArea(sourceLeft, sourceTop, sourceWidth, sourceHeight, targetLeft, targetTop);
            return;
        }
        Move(sourceLeft, sourceTop, sourceWidth, sourceHeight, targetLeft, targetTop, sourceCharacter,
            Color(sourceForeground ?? Name(Console.ForegroundColor)), Color(sourceBackground ?? Name(Console.BackgroundColor)));
    }

    [SupportedOSPlatform("windows")]
    private static void Move(int sourceLeft, int sourceTop, int sourceWidth, int sourceHeight, int targetLeft, int targetTop,
        string character, ConsoleColor foreground, ConsoleColor background)
    {
        if (character.Length != 1) throw new ArgumentOutOfRangeException(nameof(character), "a buffer's area is filled with one character");
        Console.MoveBufferArea(sourceLeft, sourceTop, sourceWidth, sourceHeight, targetLeft, targetTop, character[0], foreground, background);
    }

    public WebLingoConsoleKey ReadKey(bool intercept)
    {
        ConsoleKeyInfo key;
        try
        {
            key = Console.ReadKey(intercept);
        }
        catch (InvalidOperationException failure)
        {
            // .NET's own, where the input is not a console's: no key to read there.
            throw new IOException("its input is not a console's, whose keys it reads", failure);
        }
        return new WebLingoConsoleKey(Enum.IsDefined(key.Key) ? LowerFirst(key.Key.ToString()) : null,
            key.KeyChar == '\0' ? null : key.KeyChar.ToString(), (key.Modifiers & ConsoleModifiers.Shift) != 0,
            (key.Modifiers & ConsoleModifiers.Alt) != 0, (key.Modifiers & ConsoleModifiers.Control) != 0);
    }

    public int WatchCancelKeyPress(bool watching)
    {
        Action<string?>? ended;
        int generation;
        lock (_gate)
        {
            _watching = watching;
            generation = ++_generation;
            (ended, _waiting) = (_waiting, null);
            if (watching && !_subscribed)
            {
                Console.CancelKeyPress += Pressed;
                _subscribed = true;
            }
        }
        ended?.Invoke(null);
        return generation;
    }

    public void NextCancelKeyPress(int generation, Action<string?> pressed)
    {
        lock (_gate)
        {
            if (_watching && generation == _generation)
            {
                _waiting = pressed;
                return;
            }
        }
        pressed(null);
    }

    public void EndAsCancelled(string key) =>
        // As a program Ctrl+C ends: Windows's STATUS_CONTROL_C_EXIT, and a shell's 128 + SIGINT elsewhere.
        Environment.Exit(OperatingSystem.IsWindows() ? unchecked((int)0xC000013A) : 130);

    /// <summary>A press: to the handler waiting for one, the program kept running; with none waiting, .NET's default, its end.</summary>
    private void Pressed(object? sender, ConsoleCancelEventArgs e)
    {
        Action<string?>? waiting;
        lock (_gate)
        {
            (waiting, _waiting) = (_watching ? _waiting : null, null);
        }
        if (waiting is null) return;
        e.Cancel = true;
        waiting(e.SpecialKey == ConsoleSpecialKey.ControlBreak ? "break" : "c");
    }

    /// <summary>A colour's name, ConsoleColor's in lower camel case, <c>"darkBlue"</c>.</summary>
    public static string Name(ConsoleColor color) => LowerFirst(color.ToString());

    /// <summary>The colour a name names, ConsoleColor's in lower camel case; ArgumentOutOfRangeException for any other.</summary>
    public static ConsoleColor Color(string name)
    {
        foreach (var color in Enum.GetValues<ConsoleColor>())
        {
            if (Name(color) == name) return color;
        }
        throw new ArgumentOutOfRangeException(nameof(name), $"'{name}' is not a colour's name");
    }

    /// <summary>The colours' names, in .NET's order: black, darkBlue, … white.</summary>
    public static IReadOnlyList<string> ColorNames { get; } = [.. Enum.GetValues<ConsoleColor>().Select(Name)];

    private static string LowerFirst(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static PlatformNotSupportedException WindowsOnly(string what) => new($"{what} is Windows's alone");
}
