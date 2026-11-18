namespace WebLingo.Compiler;

/// <summary>
/// The standard modules the compiler makes itself (specification, section 3.9), any other <c>std:</c> module being a
/// native one: <c>std:system</c>, the host's built-ins, which every program imports implicitly, for now; and those that
/// export built-in classes, <c>std:json</c>'s <c>Json</c> and <c>std:html</c>'s <c>Html</c>, which a program has only by
/// importing them (the owner's decision, 18 November 2026; section 9, "M0.20 parts", part 2).
/// </summary>
public static class WebLingoStandardModules
{
    /// <summary>The module of the host's built-ins.</summary>
    public const string System = "std:system";

    /// <summary>Each module that exports built-in classes, and the classes it exports, by name.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Classes { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["std:json"] = ["Json"],
            ["std:html"] = ["Html"],
        };

    /// <summary>Whether <paramref name="path"/> names a standard module the compiler makes itself.</summary>
    public static bool IsCompilerMade(string path) => path == System || Classes.ContainsKey(path);

    /// <summary>Whether a built-in class is a standard module's export, which a program has only by importing it.</summary>
    public static bool IsImported(string className)
    {
        foreach (var (_, classes) in Classes)
        {
            foreach (var exported in classes)
            {
                if (exported == className) return true;
            }
        }
        return false;
    }
}
