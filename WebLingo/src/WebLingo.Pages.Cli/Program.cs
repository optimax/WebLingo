namespace WebLingo.Pages.Cli;

internal static class Program
{
    private static int Main(string[] args) => WebLingoPagesCommandLine.Run(args, Console.Out, Console.Error);
}
