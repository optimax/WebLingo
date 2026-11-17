namespace WebLingo.Cli;

internal static class Program
{
    /// <summary>
    /// The command, on a thread with the stack a compiler needs, a single-threaded apartment's on Windows, as a worker that
    /// runs scripts is, for COM (section 3.10).
    /// </summary>
    private static int Main(string[] args) => WebLingoCommandThread.Run(() => WebLingoCommandLine.Run(args, Console.Out, Console.Error));
}
