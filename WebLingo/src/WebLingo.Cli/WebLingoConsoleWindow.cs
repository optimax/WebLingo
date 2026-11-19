using System.Runtime.InteropServices;

namespace WebLingo.Cli;

/// <summary>
/// A console window Windows opened for <c>weblingo</c> alone, as Explorer opens one for a script double-clicked: so that
/// what the script wrote stays to be read, <c>weblingo</c> waits for a key before it ends and the window closes, where the
/// window is its own, and its input and output a console's. Started from a console that was there before it, a shell's,
/// or with its input or its output redirected, or on another platform, it waits for nothing, as before.
/// </summary>
public static class WebLingoConsoleWindow
{
    /// <summary>Waits for a key where the console window is <c>weblingo</c>'s own, and says so; does nothing otherwise.</summary>
    public static void WaitWhereItsOwn(TextWriter output)
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected || !IsOwn()) return;
        output.Write("Press any key to close this window . . .");
        Console.ReadKey(intercept: true);
        output.WriteLine();
    }

    /// <summary>Whether this process is the only one attached to its console, which was then opened for it.</summary>
    private static bool IsOwn()
    {
        var processes = new uint[2];
        return GetConsoleProcessList(processes, (uint)processes.Length) == 1;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processes, uint count);
}
