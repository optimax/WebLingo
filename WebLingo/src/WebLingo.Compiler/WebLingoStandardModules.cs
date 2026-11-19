namespace WebLingo.Compiler;

/// <summary>
/// The standard modules the compiler makes itself (specification, section 3.9), any other <c>std:</c> module being a
/// native one: those that export built-in classes, <c>std:json</c>'s <c>Json</c>, <c>std:html</c>'s <c>Html</c>,
/// <c>std:bytes</c>'s <c>Bytes</c> and <c>BytesBuilder</c>, <c>std:datetime</c>'s <c>DateTime</c> and <c>std:regex</c>'s
/// <c>Regex</c>, a value of those kinds keeping its members without an import, each of which a program has only by
/// importing it; and <c>std:system</c>, which exports the host's built-ins, those its implementation gives, another
/// implementation being free to give others, <c>write</c>, <c>writeLn</c> and <c>delay</c> among them, which every program
/// imports implicitly, an import of them allowed and meaning the same (the owner's decisions, 18 November 2026; section 9,
/// "M0.20 parts", parts 2 to 4).
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
            ["std:bytes"] = ["Bytes", "BytesBuilder"],
            ["std:datetime"] = ["DateTime"],
            ["std:regex"] = ["Regex"],
        };

    /// <summary>
    /// Each module that exports host's built-ins, and the standard names among them, which a program has only by importing
    /// them unless every program imports the module implicitly, as it imports <c>std:system</c>, which exports the others
    /// its implementation gives too.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Functions { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [System] = ["write", "writeLn", "delay"],
        };

    /// <summary>Whether <paramref name="path"/> names a standard module the compiler makes itself.</summary>
    public static bool IsCompilerMade(string path) => Classes.ContainsKey(path) || Functions.ContainsKey(path);

    /// <summary>Whether a built-in class is a standard module's export, which a program has only by importing it.</summary>
    public static bool IsImported(string className) => Exports(Classes, className);

    /// <summary>
    /// Whether every program imports <paramref name="module"/> implicitly, its names in its scope without an import, an
    /// import of them allowed and meaning the same: <c>std:system</c> (the owner's answer, 18 November 2026).
    /// </summary>
    public static bool IsImplicit(string module) => module == System;

    /// <summary>
    /// Whether a host's built-in is the export of a standard module every program does not import implicitly, which a
    /// program has only by importing it.
    /// </summary>
    public static bool IsImportedFunction(string name)
    {
        foreach (var (module, exported) in Functions)
        {
            if (IsImplicit(module)) continue;
            foreach (var export in exported)
            {
                if (export == name) return true;
            }
        }
        return false;
    }

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
