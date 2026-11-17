using System.Runtime.ExceptionServices;
using WebLingo.Language;
namespace WebLingo.Cli;

/// <summary>
/// A command run on a thread of its own, whose stack is the one a compiler needs (<see cref="WebLingoParser.CompilerStackSize"/>),
/// rather than on the process's first thread, whose stack the platform sizes, 1 MB on Windows (the owner's answer, 17
/// November 2026): <c>weblingo</c>'s and <c>wasp</c>'s every command, so that whatever they compile, at the start or while
/// a program runs, is compiled there. On Windows, a single-threaded apartment's thread, as a worker that runs scripts is,
/// for COM (section 3.10).
/// </summary>
public static class WebLingoCommandThread
{
    /// <summary>Runs <paramref name="command"/> on such a thread, waits for it, and gives its exit code, or throws what it threw.</summary>
    public static int Run(Func<int> command)
    {
        var exit = 0;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                exit = command();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        }, WebLingoParser.CompilerStackSize) { Name = "WebLingo command" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return exit;
    }
}
