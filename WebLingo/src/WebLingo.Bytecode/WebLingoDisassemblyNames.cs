using System.Globalization;
using System.Text;
namespace WebLingo.Bytecode;

/// <summary>
/// What both disassemblers name alike (the owner's request, 22 November 2026, "Debug Info"): a function, a class or a file
/// an instruction refers to by its index, by the name its module's tables give it, and, where a function has the
/// debugger's metadata (<c>weblingo compile --debug</c>), its captures, its statements' lines and columns, and its
/// variables, from the file's line and column tables.
/// </summary>
internal static class WebLingoDisassemblyNames
{
    /// <summary>
    /// <c>function3(twice)</c>; an anonymous function, which the compiler names <c>function</c>, as <c>function3</c>.
    /// </summary>
    public static string Function<T>(int index, IReadOnlyList<T> functions, Func<T, string> name) =>
        index >= 0 && index < functions.Count && name(functions[index]) is { Length: > 0 } named && named != "function"
            ? $"function{index}({named})" : $"function{index}";

    /// <summary><c>class2(Point)</c>.</summary>
    public static string Class(int index, IReadOnlyList<WebLingoClassInfo> classes) =>
        index >= 0 && index < classes.Count && classes[index].Name.Length > 0
            ? $"class{index}({classes[index].Name})" : $"class{index}";

    /// <summary><c>file1(std:console)</c>.</summary>
    public static string File(int index, IReadOnlyList<WebLingoUnitInfo> units) =>
        index >= 0 && index < units.Count && units[index].Name.Length > 0
            ? $"file{index}({units[index].Name})" : $"file{index}";

    /// <summary><c>capture0(k)</c>, where the debugger's metadata names it.</summary>
    public static string Capture(int index, WebLingoFunctionDebugInfo? info) =>
        info is not null && index >= 0 && index < info.Captures.Count && info.Captures[index].Length > 0
            ? $"capture{index}({info.Captures[index]})" : $"capture{index}";

    /// <summary>The line and column table of a function's file: its own, or the program's, where the file has one.</summary>
    public static WebLingoSourcePositions? TableOf(IReadOnlyList<WebLingoSourcePositions> positions, string? source)
    {
        foreach (var table in positions)
        {
            if (table.Source == source) return table;
        }
        return null;
    }

    /// <summary>A position as <c>line:column</c>.</summary>
    public static string At(WebLingoSourcePositions table, int offset)
    {
        var location = table.GetLocation(offset);
        return location.Line.ToString(CultureInfo.InvariantCulture) + ":" + location.Column.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A statement's first instruction, <c>; 6:21</c>, where the function's file has a line and column table.</summary>
    public static void AppendStatement(StringBuilder text, WebLingoFunctionDebugInfo? info, WebLingoSourcePositions? table,
        int instruction)
    {
        if (info is not null && table is not null && info.Points.TryGetValue(instruction, out var span))
            text.Append("; ").Append(At(table, span.Start)).Append('\n');
    }

    /// <summary>
    /// Its variables, after its code: <c>variable doubled: r1, visible 6:36 to 6:60</c>, a named import's as the export it
    /// reads, <c>variable writeLn: r1(import std:console)."writeLn"</c>; where they are visible only where the function's
    /// file has a line and column table.
    /// </summary>
    public static void AppendVariables(StringBuilder text, WebLingoFunctionDebugInfo? info, WebLingoSourcePositions? table,
        Func<int, string> local, Func<int, string> named)
    {
        if (info is null) return;
        foreach (var variable in info.Locals)
        {
            text.Append("variable ").Append(variable.Name).Append(": ");
            text.Append(variable.ExportName is { } export ? $"{named(variable.Register)}.\"{export}\"" : local(variable.Register));
            if (table is not null)
            {
                var ends = variable.VisibleUntil != int.MaxValue;
                text.Append(", visible ").Append(ends ? "" : "from ").Append(At(table, variable.VisibleFrom));
                if (ends) text.Append(" to ").Append(At(table, variable.VisibleUntil));
            }
            text.Append('\n');
        }
    }
}
