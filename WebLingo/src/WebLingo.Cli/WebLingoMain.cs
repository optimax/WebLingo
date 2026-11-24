using System.Runtime.CompilerServices;
namespace WebLingo.Cli;

/// <summary>
/// <c>weblingo</c>'s, and <c>weblingow</c>'s, program (specification, section 3.9, "The console: <c>std:console</c>"): the
/// command, on a thread with the stack a compiler needs, a single-threaded apartment's on Windows, as a worker that runs
/// scripts is, for COM (section 3.10), writing to the process's console session, which gives a script that imports
/// <c>std:console</c> a console where the process has none (<see cref="WebLingoConsoleSession"/>); and the debug
/// adapter, VS Code's, on the standard input and output.
/// </summary>
public static class WebLingoMain
{
    public static int Run(string[] args)
    {
        if (args is ["--debug-adapter"]) return RunDebugAdapter();
        var session = WebLingoConsoleSession.Open();
        var exit = WebLingoCommandThread.Run(() =>
            WebLingoCommandLine.Run(args, session.Output, session.Error, session.ConsoleInput, session.Ensure));
        // A failure without a console in a message box; in a console window of its own, a key waited for first.
        session.Finish(exit);
        return exit;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunDebugAdapter() =>
        WebLingoDebugAdapter.Run(_ => WebLingoCommandLine.Scripts, Console.OpenStandardInput(), Console.OpenStandardOutput());
}

/// <summary>
/// The debug adapter (specification, section 5.7): <c>weblingo --debug-adapter</c>'s, a script's, and
/// <c>wasp --debug-adapter</c>'s, a page's, speaking the Debug Adapter Protocol on <paramref name="input"/> and
/// <paramref name="output"/>, the program it runs given its root by <paramref name="programFor"/>.
/// </summary>
public static class WebLingoDebugAdapter
{
    public static int Run(Func<string?, WebLingoCommandLineProgram> programFor, Stream input, Stream output) =>
        new WebLingoDebugSession(input, output, programFor).Run();
}
