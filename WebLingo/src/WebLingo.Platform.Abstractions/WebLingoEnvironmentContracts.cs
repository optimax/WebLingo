namespace WebLingo.Platform.Abstractions;

/// <summary>
/// The machine's environment (specification, section 7.15), which <c>std:env</c> reaches, so that the portable core reads
/// no machine's of its own: the machine's name and the process's environment variables, each as far as the application
/// may read them, and a variable set, for the process alone, where it may set one. What it may not read is as if it were
/// not there. Safe on several threads at once.
/// </summary>
public interface IWebLingoEnvironment
{
    /// <summary>The machine's name, or null where the application may not read it.</summary>
    string? MachineName { get; }

    /// <summary>
    /// The machine's architecture, <c>"x64"</c>, <c>"x86"</c>, <c>"arm64"</c> or <c>"arm"</c>, as .NET's runtime identifiers
    /// name them, another's its name in small letters; null where the application may not read it.
    /// </summary>
    string? Architecture { get; }

    /// <summary>
    /// The running program's architecture, as <see cref="Architecture"/> names it, <c>"x86"</c> for a 32-bit program on
    /// 64-bit Windows; null where the application may not read it.
    /// </summary>
    string? ProcessArchitecture { get; }

    /// <summary>
    /// The operating system, <c>"windows"</c>, <c>"linux"</c> or <c>"macos"</c>, <c>"freebsd"</c> too, <c>"other"</c> for any
    /// other; null where the application may not read it.
    /// </summary>
    string? OperatingSystemName { get; }

    /// <summary>A variable's value, or null where it is not set, or the application may not read it.</summary>
    string? GetVariable(string name);

    /// <summary>The variables the application may read, each its name and its value, ordered by name.</summary>
    IReadOnlyList<KeyValuePair<string, string>> GetVariables();

    /// <summary>Whether the application may set variables.</summary>
    bool MaySetVariables { get; }

    /// <summary>
    /// Sets a variable, for the process alone, or removes it, given null, where the application may set variables (see
    /// <see cref="MaySetVariables"/>); a name is not empty and holds neither <c>=</c> nor U+0000, a value no U+0000.
    /// </summary>
    void SetVariable(string name, string? value);
}
