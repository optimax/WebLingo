using WebLingo.Cli;
namespace WebLingo.Windowed.Cli;

internal static class Program
{
    /// <summary><c>weblingow</c>: <c>weblingo</c> as a Windows application, which a double-click runs (<see cref="WebLingoMain"/>).</summary>
    private static int Main(string[] args) => WebLingoMain.Run(args);
}
