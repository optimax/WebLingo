namespace WebLingo.Cli;

internal static class Program
{
    /// <summary>On a single-threaded apartment's thread on Windows, as a worker that runs scripts is, for COM (section 3.10).</summary>
    [STAThread]
    private static int Main(string[] args) => WebLingoCommandLine.Run(args, Console.Out, Console.Error);
}
