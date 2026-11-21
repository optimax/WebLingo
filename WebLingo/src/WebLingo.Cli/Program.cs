namespace WebLingo.Cli;

internal static class Program
{
    /// <summary><c>weblingo</c>: a console program, whose manifest says <c>detached</c> (<see cref="WebLingoMain"/>).</summary>
    private static int Main(string[] args) => WebLingoMain.Run(args);
}
