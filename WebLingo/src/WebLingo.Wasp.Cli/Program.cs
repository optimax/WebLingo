namespace WebLingo.Wasp.Cli;

internal static class Program
{
    private static int Main(string[] args) => WebLingoWaspCommandLine.Run(args, Console.Out, Console.Error);
}
