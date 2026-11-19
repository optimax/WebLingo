using WebLingo.Cli;
namespace WebLingo.Wasp.Cli;

internal static class Program
{
    /// <summary>
    /// The command, on a thread with the stack a compiler needs, a single-threaded apartment's on Windows, as a worker that
    /// runs scripts is, for COM (section 3.10).
    /// </summary>
    private static int Main(string[] args)
    {
        // Text written as UTF-8, to a file, a pipe or a console alike (WebLingoConsoleText).
        var (output, error) = WebLingoConsoleText.Open();
        return WebLingoCommandThread.Run(() => WebLingoWaspCommandLine.Run(args, output, error));
    }
}
