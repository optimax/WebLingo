using System.Text.Json;
namespace WebLingo.Cli;

/// <summary>
/// A command line's schemes (specification, section 3.9, "Native modules"), each a name and the full path of a folder of
/// native modules: <c>std</c>'s, <c>std/</c> beside the program, unless configured; then those of <c>weblingo.json</c>, in
/// the program's folder, <c>{ "schemes": { "native": "C:/WebLingo/native" } }</c>, a relative folder there being from the
/// file's folder; then those the command line gives, <c>--scheme native=folder</c>, a relative folder from the current one,
/// each over the file's of its name. A scheme's name is ASCII letters and digits, a letter first, at least two of them, so
/// that a drive letter is never one.
/// </summary>
public static class WebLingoSchemes
{
    /// <summary>The file a program's folder may hold, configuring its host.</summary>
    public const string ConfigurationFile = "weblingo.json";

    /// <summary>The schemes for the program at <paramref name="program"/>, the command line's given; false, with why, otherwise.</summary>
    public static bool TryLoad(string program, IReadOnlyDictionary<string, string> given, out Dictionary<string, string> schemes,
        out string? error)
    {
        schemes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["std"] = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "std"))
        };
        error = null;
        var folder = Path.GetDirectoryName(Path.GetFullPath(program)) ?? ".";
        var file = Path.Combine(folder, ConfigurationFile);
        if (File.Exists(file) && !TryRead(file, folder, schemes, out error)) return false;
        foreach (var (name, path) in given) schemes[name] = Path.GetFullPath(path);
        return true;
    }

    /// <summary>Whether <paramref name="name"/> may name a scheme.</summary>
    public static bool IsName(string name)
    {
        if (name.Length < 2 || !char.IsAsciiLetter(name[0])) return false;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c)) return false;
        }
        return true;
    }

    private static bool TryRead(string file, string folder, Dictionary<string, string> schemes, out string? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = $"{file}: the configuration is an object, {{ \"schemes\": {{ … }} }}.";
                return false;
            }
            foreach (var setting in document.RootElement.EnumerateObject())
            {
                if (setting.Name != "schemes")
                {
                    error = $"{file}: '{setting.Name}' is not a setting; the settings are \"schemes\".";
                    return false;
                }
                if (setting.Value.ValueKind != JsonValueKind.Object)
                {
                    error = $"{file}: \"schemes\" is an object, each scheme's name and its folder.";
                    return false;
                }
                foreach (var scheme in setting.Value.EnumerateObject())
                {
                    if (!IsName(scheme.Name) || scheme.Value.ValueKind != JsonValueKind.String
                        || string.IsNullOrEmpty(scheme.Value.GetString()))
                    {
                        error = $"{file}: '{scheme.Name}' is not a scheme's name, given a folder.";
                        return false;
                    }
                    schemes[scheme.Name] = Path.GetFullPath(Path.Combine(folder, scheme.Value.GetString()!));
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            error = $"{file}: cannot read the configuration: {exception.Message}";
            return false;
        }
    }
}
