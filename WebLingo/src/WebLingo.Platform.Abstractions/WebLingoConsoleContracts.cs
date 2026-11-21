namespace WebLingo.Platform.Abstractions;

/// <summary>
/// A script's console input, its standard input, which a host that has one gives, <c>weblingo</c>, interactive or piped;
/// a site's pages and handlers, under <c>wasp</c>, have none (the owner's answers, 19 November 2026, "Console Input").
/// Each read blocks until a line has come, so that a script's asynchronous reads run off its worker.
/// </summary>
public interface IWebLingoConsoleInput
{
    /// <summary>The next line, without its line break; null at the end of the input.</summary>
    string? ReadLine();

    /// <summary>
    /// The next line, as <see cref="ReadLine"/> reads it, but not shown as it is typed where the input is a console's,
    /// a password's; null at the end of the input.
    /// </summary>
    string? ReadPassword();

    /// <summary>
    /// The input's next character, a Unicode scalar value, as a string, both halves of a surrogate pair read together;
    /// null at the end of the input (the owner's answer, 20 November 2026, "Console Module, Part 2", A3).
    /// </summary>
    string? ReadCharacter();
}

/// <summary>
/// A key a console read, as .NET's <c>ConsoleKeyInfo</c> gives it: its key's name, .NET's <c>ConsoleKey</c>'s in lower
/// camel case, <c>"enter"</c>, <c>"upArrow"</c>, null for a key .NET does not name; the character it typed, null for none;
/// and whether Shift, Alt and Control were down (the owner's answer, 20 November 2026, "Console Module", A6).
/// </summary>
public readonly record struct WebLingoConsoleKey(string? Key, string? Character, bool Shift, bool Alt, bool Control);

/// <summary>
/// A script's console, which <c>std:console</c>'s <c>Console</c> controls, as .NET's <c>Console</c> does (the owner's
/// answers, 20 November 2026, "Console Module", A4 to A6, and its part 2's, A1 to A5): <c>weblingo</c> gives a script one,
/// <c>wasp</c> never does. A colour is .NET's <c>ConsoleColor</c>'s name in lower camel case, <c>"darkBlue"</c>. A member
/// throws <see cref="PlatformNotSupportedException"/> where the platform has no such thing, Windows's alone elsewhere;
/// <see cref="IOException"/> where the console cannot do it, its input or output not a console's, or there being none;
/// and <see cref="ArgumentOutOfRangeException"/> for a value outside what the console takes, a cursor beyond its buffer.
/// </summary>
public interface IWebLingoConsole
{
    string Title { get; set; }
    string ForegroundColor { get; set; }
    string BackgroundColor { get; set; }
    int CursorLeft { get; set; }
    int CursorTop { get; set; }
    int CursorSize { get; set; }
    bool CursorVisible { get; set; }
    int WindowWidth { get; set; }
    int WindowHeight { get; set; }
    int WindowLeft { get; set; }
    int WindowTop { get; set; }
    int BufferWidth { get; set; }
    int BufferHeight { get; set; }
    int LargestWindowWidth { get; }
    int LargestWindowHeight { get; }
    bool KeyAvailable { get; }
    bool CapsLock { get; }
    bool NumberLock { get; }
    bool TreatControlCAsInput { get; set; }
    bool IsInputRedirected { get; }
    bool IsOutputRedirected { get; }
    bool IsErrorRedirected { get; }

    void Beep();
    void Beep(int frequency, int duration);
    void Clear();
    void ResetColor();
    (int Left, int Top) GetCursorPosition();
    void SetCursorPosition(int left, int top);
    void SetWindowSize(int width, int height);
    void SetWindowPosition(int left, int top);
    void SetBufferSize(int width, int height);

    /// <summary>
    /// Copies an area of the buffer to another place in it, the area left as it is, or, given a character and its colours,
    /// filled with them, as .NET's two overloads do.
    /// </summary>
    void MoveBufferArea(int sourceLeft, int sourceTop, int sourceWidth, int sourceHeight, int targetLeft, int targetTop,
        string? sourceCharacter, string? sourceForeground, string? sourceBackground);

    /// <summary>The next key typed, shown where <paramref name="intercept"/> is false, as .NET's <c>ReadKey</c>; blocking.</summary>
    WebLingoConsoleKey ReadKey(bool intercept);

    /// <summary>
    /// Whether a press of Ctrl+C or Ctrl+Break goes to a script's handler, the program kept running, rather than ending it,
    /// as .NET's <c>CancelKeyPress</c> with <c>e.Cancel</c> set; given the generation its handler's presses come in, each
    /// call a new one, so that an earlier handler's next press is none (A1).
    /// </summary>
    int WatchCancelKeyPress(bool watching);

    /// <summary>
    /// Calls <paramref name="pressed"/>, once, from another thread, with <c>"c"</c> or <c>"break"</c> at the next press of
    /// Ctrl+C or Ctrl+Break, or with null where <paramref name="generation"/> is no longer the one watched; a press while
    /// none waits, the handler still busy with an earlier one, ends the program, as .NET's default does.
    /// </summary>
    void NextCancelKeyPress(int generation, Action<string?> pressed);

    /// <summary>Ends the program as Ctrl+C or Ctrl+Break would have, its handler having not kept it running.</summary>
    void EndAsCancelled(string key);
}
