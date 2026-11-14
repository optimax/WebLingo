namespace WebLingo.Cli;

internal static class Program
{
    private static int Main(string[] args) => WebLingoCommandLine.Run(args, Console.Out, Console.Error);
}
