using System.Runtime.InteropServices;
using System.Text;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;

namespace WebLingo.Cli;

/// <summary>
/// The console a command line's text goes to (specification, section 3.9, "The console: <c>std:console</c>"; the owner's
/// answers, 20 November 2026, "Console Module", A7 and A8): the one it was started with, a terminal's, or its output and
/// errors where they are redirected to a file or a pipe; or, on Windows, where it has neither, as <c>weblingo.exe</c>
/// double-clicked, its manifest saying <c>detached</c>, and <c>weblingow.exe</c>, a Windows application, always, a console
/// window of its own, made when a script that imports <c>std:console</c> runs (<see cref="Ensure"/>). Until then, what the
/// command line writes is kept, and shown in a message box if it fails (<see cref="Finish"/>); a script that does not import
/// <c>std:console</c> runs without a window. Nothing of .NET's <see cref="Console"/> is touched without a console, since .NET
/// keeps what it first finds.
/// </summary>
public sealed class WebLingoConsoleSession
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Input _input;
    private bool _console;

    private WebLingoConsoleSession(bool console)
    {
        _console = console;
        if (console)
        {
            // Text written as UTF-8, to a file, a pipe or a console alike (WebLingoConsoleText).
            // The console's own writers, so that an error in a console stands out as before (WebLingoConsoleText).
            (_output, _error) = WebLingoConsoleText.Open();
            _input = new Input(Opened());
        }
        else
        {
            _output = new Writer(null);
            _error = new Writer(null);
            _input = new Input(null);
        }
    }

    /// <summary>The session of this process, opened before anything is written: its console's, or one that is kept until there is one.</summary>
    public static WebLingoConsoleSession Open() => new(!OperatingSystem.IsWindows() || HasConsoleOrHandles());

    /// <summary>Where the command line writes its output, a script's <c>write</c> among it.</summary>
    public TextWriter Output => _output;

    /// <summary>Where the command line writes its errors, a script's <c>writeError</c> and its diagnostics among them.</summary>
    public TextWriter Error => _error;

    /// <summary>The console's input, which a script reads, typed or piped; a read without a console an <c>IOError</c>.</summary>
    public IWebLingoConsoleInput ConsoleInput => _input;

    /// <summary>
    /// A console for a script that imports <c>std:console</c>: the session's, if it has one; else, on Windows, a console window
    /// made for it, what was kept written to it, and the session's writers and input its from then on. Nothing if one cannot be made.
    /// </summary>
    public void Ensure()
    {
        if (_console || !OperatingSystem.IsWindows() || !MakeConsole()) return;
        _console = true;
        var (output, error) = WebLingoConsoleText.Open();
        ((Writer)_output).Switch(output);
        ((Writer)_error).Switch(error);
        _input.Open(Opened());
    }

    /// <summary>
    /// The end of the command, its exit code given: where no console was ever had, a failure's text shown in a message box,
    /// so that a script double-clicked that fails says why (A8); in a console window of the session's own, made for it or
    /// opened by Windows for <c>weblingo.exe</c> where its manifest is not read, a key waited for before the window closes,
    /// so that what was written stays to be read (<see cref="WebLingoConsoleWindow"/>).
    /// </summary>
    public void Finish(int exit)
    {
        if (!_console)
        {
            var (output, error) = (((Writer)_output).Kept, ((Writer)_error).Kept);
            var text = error.Length > 0 ? error : output;
            if (exit != WebLingoCommandLine.Success && text.Length > 0 && OperatingSystem.IsWindows()) Show(text);
            return;
        }
        _output.Flush();
        WebLingoConsoleWindow.WaitWhereItsOwn(_output);
    }

    /// <summary>The console's input as .NET reads it, once there is a console: UTF-8 from a file or a pipe, Unicode typed.</summary>
    private static DotNetConsoleInput Opened() => new(Console.In, interactive: !Console.IsInputRedirected);

    /// <summary>Whether the process has a console, or a standard output or error a file or a pipe takes, as its parent gave it.</summary>
    private static bool HasConsoleOrHandles() =>
        GetConsoleWindow() != IntPtr.Zero || IsUsable(GetStdHandle(StandardOutput)) || IsUsable(GetStdHandle(StandardError));

    private static bool IsUsable(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1) && GetFileType(handle) != FileTypeUnknown;

    /// <summary>
    /// A console window of the process's own: <c>AllocConsoleWithOptions</c>'s, which Windows 11 24H2 has, asked for a new
    /// window, as Microsoft's "Console allocation policy" has it; <c>AllocConsole</c> on earlier Windows.
    /// </summary>
    private static bool MakeConsole()
    {
        try
        {
            var options = new AllocConsoleOptions { Mode = AllocConsoleNewWindow };
            if (AllocConsoleWithOptions(ref options, out var result) >= 0) return result is AllocConsoleNewConsole or AllocConsoleExistingConsole;
        }
        catch (EntryPointNotFoundException)
        {
        }
        return AllocConsole();
    }

    /// <summary>A failure's text in a message box, its title the program's, for a command line that has no console to write it to.</summary>
    private static void Show(string text)
    {
        const uint iconError = 0x10;
        var title = Path.GetFileNameWithoutExtension(Environment.ProcessPath) is { Length: > 0 } name ? name : "weblingo";
        MessageBoxW(IntPtr.Zero, text.TrimEnd(), title, iconError);
    }

    /// <summary>The writer <paramref name="writer"/> writes to, a session's own once it has a console; else itself.</summary>
    internal static TextWriter Unwrapped(TextWriter writer) => writer is Writer { Target: { } target } ? target : writer;

    /// <summary>A writer whose text is kept until the session switches it to the console's, to which it then writes what was kept.</summary>
    private sealed class Writer(TextWriter? target) : TextWriter
    {
        private TextWriter? _target = target;
        private readonly StringBuilder _kept = new();

        public override Encoding Encoding => _target?.Encoding ?? Encoding.UTF8;

        public string Kept => _kept.ToString();

        public TextWriter? Target => _target;

        public void Switch(TextWriter target)
        {
            target.Write(_kept.ToString());
            _kept.Clear();
            _target = target;
        }

        public override void Write(char value)
        {
            if (_target is null) _kept.Append(value);
            else _target.Write(value);
        }

        public override void Write(string? value)
        {
            if (_target is null) _kept.Append(value);
            else _target.Write(value);
        }

        public override void Flush() => _target?.Flush();
    }

    /// <summary>The console's input, once there is one; before, every read an <c>IOError</c>, as a host without a console gives.</summary>
    private sealed class Input(IWebLingoConsoleInput? input) : IWebLingoConsoleInput
    {
        private IWebLingoConsoleInput? _input = input;

        public void Open(IWebLingoConsoleInput input) => _input = input;

        public string? ReadLine() => (_input ?? throw NoConsole()).ReadLine();

        public string? ReadPassword() => (_input ?? throw NoConsole()).ReadPassword();

        private static IOException NoConsole() => new("this program has no console");
    }

    private const int StandardOutput = -11;
    private const int StandardError = -12;
    private const uint FileTypeUnknown = 0;
    private const int AllocConsoleNewWindow = 1;
    private const int AllocConsoleNewConsole = 1;
    private const int AllocConsoleExistingConsole = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct AllocConsoleOptions
    {
        public int Mode;
        public int UseShowWindow;
        public ushort ShowWindow;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern int AllocConsoleWithOptions(ref AllocConsoleOptions options, out int result);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
