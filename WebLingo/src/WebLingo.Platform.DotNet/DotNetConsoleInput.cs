using System.Text;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// A script's console input on .NET (the owner's answers, 19 November 2026, "Console Input"): its lines read from a
/// reader, the console's standard input for <c>weblingo</c>, or any other, as tests give; and a password read key by key,
/// not shown, where <paramref name="interactive"/> says the input is a console a user types in, else read as a line.
/// </summary>
public sealed class DotNetConsoleInput(TextReader reader, bool interactive) : IWebLingoConsoleInput
{
    public string? ReadLine() => reader.ReadLine();

    public string? ReadPassword()
    {
        if (!interactive) return reader.ReadLine();
        var typed = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return typed.ToString();
                case ConsoleKey.Backspace:
                    if (typed.Length > 0) typed.Length -= char.IsLowSurrogate(typed[^1]) && typed.Length > 1 ? 2 : 1;
                    continue;
                // The end of the input, as Ctrl+Z, then Enter, or Ctrl+D, end a line's reading, where nothing is typed yet.
                case ConsoleKey.Z or ConsoleKey.D when key.Modifiers == ConsoleModifiers.Control && typed.Length == 0:
                    Console.WriteLine();
                    return null;
            }
            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar)) typed.Append(key.KeyChar);
        }
    }
}
