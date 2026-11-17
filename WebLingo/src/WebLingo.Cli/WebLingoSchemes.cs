using System.Text.Json;
namespace WebLingo.Cli;

/// <summary>
/// A command line's configuration, JSON, in the program's folder, or in an application's root (specification, sections
/// 3.9 and 6.12): <c>weblingo.config</c> for <c>weblingo</c>'s scripts, and <c>wasp.config</c> for <c>wasp</c>'s pages and
/// sites, its caller naming which. Its schemes, each a name and the full path of a folder of native modules:
/// <c>std</c>'s, <c>std/</c> beside the program, unless configured; then those of the configuration,
/// <c>{ "schemes": { "native": "C:/WebLingo/native" } }</c>, a relative folder there being from the file's folder; then
/// those the command line gives, <c>--scheme native=folder</c>, a relative folder from the current one, each over the
/// file's of its name. A scheme's name is ASCII letters and digits, a letter first, at least two of them, so that a drive
/// letter is never one. And an application's environment, <c>"environment": "development"</c> or <c>"production"</c>,
/// and how long, in seconds, a page waits for a client that has not taken its output, <c>"sendTimeout": 30</c>, a number
/// more than 0 (specification, section 6.12, "Streaming"), which only <c>wasp serve</c> reads; and the hosts its scripts may
/// reach, <c>"network": { "allow": ["api.example.com"] }</c>, which <c>wasp</c> reads, <c>weblingo</c>'s scripts reaching
/// any (section 7.9).
/// </summary>
public static class WebLingoSchemes
{
    /// <summary>The file a script's folder may hold, configuring <c>weblingo</c>'s host (a site's is <c>wasp.config</c>).</summary>
    public const string ConfigurationFile = "weblingo.config";

    /// <summary>
    /// The schemes for the program at <paramref name="program"/>, from its folder's <paramref name="configuration"/>, the
    /// command line's given over them; false, with why, otherwise.
    /// </summary>
    public static bool TryLoad(string program, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? error) =>
        TryLoad(program, configuration, given, out schemes, out _, out error);

    /// <summary>As the other, and the hosts the configuration allows its scripts to reach, none when it names none.</summary>
    public static bool TryLoad(string program, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out IReadOnlyList<string> allowedHosts, out string? error) =>
        TryLoadFolder(Path.GetDirectoryName(Path.GetFullPath(program)) ?? ".", configuration, given, out schemes, out _, out _, out allowedHosts,
            out error);

    /// <summary>
    /// The configuration in <paramref name="folder"/>, its file <paramref name="configuration"/>: its schemes, the command
    /// line's given over them, its environment, <c>development</c>, <c>production</c>, or null if it names none, and its send
    /// timeout, in seconds, or null if it gives none; false, with why, otherwise.
    /// </summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? environment, out double? sendTimeout, out string? error) =>
        TryLoadFolder(folder, configuration, given, out schemes, out environment, out sendTimeout, out _, out error);

    /// <summary>As the other, and the hosts the configuration allows its scripts to reach, none when it names none.</summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? environment, out double? sendTimeout, out IReadOnlyList<string> allowedHosts,
        out string? error)
    {
        sendTimeout = null;
        var hosts = new List<string>();
        allowedHosts = hosts;
        schemes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["std"] = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "std"))
        };
        (environment, error) = (null, null);
        folder = Path.GetFullPath(folder);
        var file = Path.Combine(folder, configuration);
        if (File.Exists(file) && !TryRead(file, folder, schemes, ref environment, ref sendTimeout, hosts, out error)) return false;
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

    private static bool TryRead(string file, string folder, Dictionary<string, string> schemes, ref string? environment, ref double? sendTimeout,
        List<string> hosts, out string? error)
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
                if (setting.Name == "environment")
                {
                    if (setting.Value.ValueKind != JsonValueKind.String || setting.Value.GetString() is not ("development" or "production"))
                    {
                        error = $"{file}: \"environment\" is \"development\" or \"production\".";
                        return false;
                    }
                    environment = setting.Value.GetString();
                    continue;
                }
                if (setting.Name == "sendTimeout")
                {
                    if (setting.Value.ValueKind != JsonValueKind.Number || !setting.Value.TryGetDouble(out var seconds) || !(seconds > 0)
                        || seconds > TimeSpan.MaxValue.TotalSeconds / 2)
                    {
                        error = $"{file}: \"sendTimeout\" is a number of seconds, more than 0.";
                        return false;
                    }
                    sendTimeout = seconds;
                    continue;
                }
                if (setting.Name == "network")
                {
                    // The hosts its scripts may reach, by name or address (section 7.9).
                    if (setting.Value.ValueKind != JsonValueKind.Object || !setting.Value.TryGetProperty("allow", out var allow)
                        || allow.ValueKind != JsonValueKind.Array)
                    {
                        error = $"{file}: \"network\" is an object, {{ \"allow\": [\"api.example.com\"] }}.";
                        return false;
                    }
                    foreach (var host in allow.EnumerateArray())
                    {
                        if (host.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(host.GetString()))
                        {
                            error = $"{file}: \"network\"'s \"allow\" lists hosts, each a name or an address.";
                            return false;
                        }
                        hosts.Add(host.GetString()!);
                    }
                    continue;
                }
                if (setting.Name != "schemes")
                {
                    error = $"{file}: '{setting.Name}' is not a setting; the settings are \"schemes\", \"environment\", \"sendTimeout\" and \"network\".";
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
