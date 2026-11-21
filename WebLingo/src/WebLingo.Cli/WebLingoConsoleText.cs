using System.Runtime.InteropServices;
using System.Text;

namespace WebLingo.Cli;

/// <summary>
/// The text <c>weblingo</c> and <c>wasp</c> write, their output and their errors, UTF-8 on every platform, so that no
/// letter is lost: on Windows, whose consoles and .NET's writers otherwise use the console's code page, UTF-8 without a
/// byte-order mark to a file or a pipe, and to a console through Windows's own Unicode output, <c>WriteConsoleW</c>,
/// whatever its code page, which is left as it is (the owner's answer, 18 November 2026, "Text Written on Windows:
/// UTF-8"); elsewhere, .NET's own writers, which write UTF-8. Every writer of the process, <see cref="Console.Out"/> and
/// <see cref="Console.Error"/> among them, ASP.NET Core's logging's, writes so once <see cref="Open"/> has run.
/// </summary>
public static class WebLingoConsoleText
{
    /// <summary>Sets the process's writers as the class says, and gives them: the output's, then the errors'.</summary>
    public static (TextWriter Output, TextWriter Error) Open()
    {
        if (!OperatingSystem.IsWindows()) return (Console.Out, Console.Error);
        // .NET writes to a console through WriteConsoleW where its output encoding is UTF-16, and then sets no code page.
        if (!Console.IsOutputRedirected || !Console.IsErrorRedirected) Console.OutputEncoding = Encoding.Unicode;
        var output = Console.IsOutputRedirected ? Utf8(Console.OpenStandardOutput()) : Console.Out;
        var error = Console.IsErrorRedirected ? Utf8(Console.OpenStandardError()) : Console.Error;
        Console.SetOut(output);
        Console.SetError(error);
        // Input, as output: UTF-8 from a file or a pipe, and Unicode from a console, through .NET's ReadConsoleW.
        if (Console.IsInputRedirected) Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)));
        else Console.InputEncoding = Encoding.Unicode;
        return (Console.Out, Console.Error);
    }

    /// <summary>A file's or a pipe's writer, UTF-8 without a byte-order mark, each write sent at once, as .NET's own are.</summary>
    private static TextWriter Utf8(Stream stream) =>
        new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

    internal static void WriteErrorLine(TextWriter error, string location, string description)
    {
        // A console a session made for a script, once it has one, as the console it was started with.
        error = WebLingoConsoleSession.Unwrapped(error);
        const string bold = "\u001b[1mERROR\u001b[22m";
        if (!ReferenceEquals(error, Console.Error) || Console.IsErrorRedirected
            || Environment.GetEnvironmentVariable("TERM") == "dumb")
        {
            error.WriteLine($"{location}: ERROR {description}");
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            error.WriteLine($"{location}: {bold} {description}");
            return;
        }

        const int standardError = -12;
        const uint virtualTerminal = 0x0004;
        var handle = GetStdHandle(standardError);
        if (!GetConsoleMode(handle, out var mode) || !SetConsoleMode(handle, mode | virtualTerminal))
        {
            error.WriteLine($"{location}: ERROR {description}");
            return;
        }
        try
        {
            error.WriteLine($"{location}: {bold} {description}");
        }
        finally
        {
            if ((mode & virtualTerminal) == 0) SetConsoleMode(handle, mode);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
