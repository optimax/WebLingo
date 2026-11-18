namespace WebLingo.Compiler;

/// <summary>
/// The standard modules the compiler makes itself (specification, section 3.9), any other <c>std:</c> module being a
/// native one: <c>std:system</c>, the host's built-ins, which every program imports implicitly, for now; those that export
/// built-in classes, <c>std:json</c>'s <c>Json</c> and <c>std:html</c>'s <c>Html</c>; and those that export host's
/// built-ins, <c>std:io</c>'s <c>write</c> and <c>writeLn</c>; each of which a program has only by importing it (the owner's
/// decision, 18 November 2026; section 9, "M0.20 parts", parts 2 and 3).
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

    /// <summary>Each module that exports host's built-ins, and the built-ins it exports, by name, those the host gives.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Functions { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["std:io"] = ["write", "writeLn"],
        };

    /// <summary>Whether <paramref name="path"/> names a standard module the compiler makes itself.</summary>
    public static bool IsCompilerMade(string path) => path == System || Classes.ContainsKey(path) || Functions.ContainsKey(path);

    /// <summary>Whether a built-in class is a standard module's export, which a program has only by importing it.</summary>
    public static bool IsImported(string className) => Exports(Classes, className);

    /// <summary>Whether a host's built-in is a standard module's export, which a program has only by importing it.</summary>
    public static bool IsImportedFunction(string name) => Exports(Functions, name);

    /// <summary>Whether <paramref name="module"/> exports the host's built-in <paramref name="name"/>.</summary>
    public static bool ExportsFunction(string module, string name)
    {
        if (!Functions.TryGetValue(module, out var functions)) return false;
        foreach (var function in functions)
        {
            if (function == name) return true;
        }
        return false;
    }

    private static bool Exports(IReadOnlyDictionary<string, IReadOnlyList<string>> modules, string name)
    {
        foreach (var (_, exported) in modules)
        {
            foreach (var export in exported)
            {
                if (export == name) return true;
            }
        }
        return false;
    }
}
