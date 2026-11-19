using System.Collections;
using System.Runtime.InteropServices;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// The machine's environment on .NET (specification, section 7.15): all of it, and variables set, for <c>weblingo</c>'s
/// scripts; for a site's, under <c>wasp</c>, the machine's name only where its configuration allows it, and only the
/// variables it lists, each by its name, a name ending in <c>*</c> allowing those it begins, <c>"*"</c> any, compared as
/// the platform compares variables' names, ignoring case on Windows; the architectures and the operating system only
/// where it allows them, as .NET's <see cref="RuntimeInformation"/> reports them; and none set.
/// </summary>
public sealed class DotNetEnvironment : IWebLingoEnvironment
{
    private static readonly StringComparison Names = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly bool _all;
    private readonly bool _machineName;
    private readonly bool _platform;
    private readonly IReadOnlyList<string> _variables;

    /// <summary>All of the environment, as a script has it under <c>weblingo</c>, its variables set too.</summary>
    public DotNetEnvironment() => (_all, _machineName, _platform, _variables) = (true, true, true, []);

    /// <summary>
    /// What a site's configuration allows: the machine's name, or not, the variables it lists, and the architectures and the
    /// operating system, or not; none set.
    /// </summary>
    public DotNetEnvironment(bool machineName, IReadOnlyList<string> variables, bool platform = false) =>
        (_machineName, _variables, _platform) = (machineName, variables, platform);

    public string? MachineName => _machineName ? Environment.MachineName : null;

    public string? Architecture => _platform ? Name(RuntimeInformation.OSArchitecture) : null;

    public string? ProcessArchitecture => _platform ? Name(RuntimeInformation.ProcessArchitecture) : null;

    public string? OperatingSystemName => !_platform ? null
        : OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos"
        : OperatingSystem.IsLinux() ? "linux"
        : OperatingSystem.IsFreeBSD() ? "freebsd"
        : "other";

    /// <summary>An architecture as .NET's runtime identifiers name it, <c>win-x64</c>'s <c>x64</c>: its name in small letters.</summary>
    private static string Name(System.Runtime.InteropServices.Architecture architecture) =>
        architecture.ToString().ToLowerInvariant();

    public bool MaySetVariables => _all;

    public string? GetVariable(string name) => IsName(name) && Allows(name) ? Environment.GetEnvironmentVariable(name) : null;

    public IReadOnlyList<KeyValuePair<string, string>> GetVariables()
    {
        var variables = new List<KeyValuePair<string, string>>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value && Allows(name)) variables.Add(new(name, value));
        }
        variables.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return variables;
    }

    public void SetVariable(string name, string? value)
    {
        if (!_all) throw new InvalidOperationException("This application may not set environment variables.");
        if (!IsName(name)) throw new ArgumentException($"'{name}' is not an environment variable's name.", nameof(name));
        if (value is not null && value.Contains('\0')) throw new ArgumentException("A variable's value holds no U+0000.", nameof(value));
        Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>Whether a variable may have <paramref name="name"/>: not empty, without <c>=</c> or U+0000.</summary>
    private static bool IsName(string name) => name.Length > 0 && !name.Contains('=') && !name.Contains('\0');

    private bool Allows(string name)
    {
        if (_all) return true;
        foreach (var allowed in _variables)
        {
            if (allowed == "*") return true;
            if (allowed.EndsWith('*') ? name.StartsWith(allowed[..^1], Names) : string.Equals(name, allowed, Names)) return true;
        }
        return false;
    }
}
