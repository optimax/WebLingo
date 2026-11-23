using System.Text.Json;
using WebLingo.Bytecode;
namespace WebLingo.Cli;

/// <summary>
/// A command line's configuration, JSON, in the program's folder, or in an application's root (specification, sections
/// 3.9 and 6.12): <c>weblingo.config</c> for <c>weblingo</c>'s scripts, and <c>wasp.config</c> for <c>wasp</c>'s pages and
/// sites, its caller naming which. Its schemes, each a name and the full path of a folder of native modules:
/// <c>std</c>'s, <c>std/</c> beside the program, unless configured; then those of the configuration,
/// <c>{ "schemes": { "native": "C:/WebLingo/native" } }</c>, a relative folder there being from the file's folder; then
/// those the command line gives, <c>--scheme native=folder</c>, a relative folder from the current one, each over the
/// file's of its name. A scheme's name is ASCII letters and digits, a letter first, at least two of them, so that a drive
/// letter is never one. And an application's mode, <c>"mode": "development"</c> or <c>"production"</c>,
/// and how long, in seconds, a page waits for a client that has not taken its output, <c>"sendTimeout": 30</c>, a number
/// more than 0 (specification, section 6.12, "Streaming"), which only <c>wasp serve</c> reads; and the hosts its scripts may
/// reach, <c>"network": { "allow": ["api.example.com"] }</c>, which <c>wasp</c> reads, <c>weblingo</c>'s scripts reaching
/// any (section 7.9); and its databases, each a name and a connection string, a provider's name, a colon, and what the
/// provider reads, <c>"databases": { "shop": "sqlite:../data/shop.db" }</c>, which both read (section 7.11); and the
/// folders its scripts may read and write, <c>"files": { "read": ["../data"], "write": ["../uploads"] }</c>, a relative
/// folder from the file's folder, which <c>wasp</c> reads, <c>weblingo</c>'s scripts reaching any file (section 7.13,
/// "File capabilities"); and the COM objects its scripts may make, <c>"com": { "allow": ["ADODB.Connection"] }</c>, by
/// ProgID, CLSID or moniker, <c>"*"</c> any, which both read (section 3.10); and what its scripts may read of the machine's
/// environment, <c>"env": { "machineName": true, "variables": ["PATH", "APP_*"] }</c>, which <c>wasp</c> reads,
/// <c>weblingo</c>'s scripts reading all of it (section 7.15). On Windows, the <c>windows</c> scheme's folder is
/// <c>windows/</c> beside the program, unless configured, as <c>std</c>'s is <c>std/</c>.
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
        TryLoad(program, configuration, given, out schemes, out _, out _, out _, out error);

    /// <summary>
    /// As the other, and the hosts the configuration allows its scripts to reach, none when it names none, its databases,
    /// each name's connection string, and the folders its scripts may read and write, none when it names none.
    /// </summary>
    public static bool TryLoad(string program, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out IReadOnlyList<string> allowedHosts, out IReadOnlyDictionary<string, string> databases,
        out WebLingoFileFolders files, out string? error) =>
        TryLoad(program, configuration, given, out schemes, out allowedHosts, out databases, out files, out _, out _, out error);

    /// <summary>
    /// As the other, and what its settings allow native modules, <c>"com"</c>'s list, by its setting's name, and what its
    /// scripts may read of the machine's environment.
    /// </summary>
    public static bool TryLoad(string program, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out IReadOnlyList<string> allowedHosts, out IReadOnlyDictionary<string, string> databases,
        out WebLingoFileFolders files, out IReadOnlyDictionary<string, IReadOnlyList<string>> allowed, out WebLingoEnvSettings env,
        out string? error) =>
        TryLoadFolder(Path.GetDirectoryName(Path.GetFullPath(program)) ?? ".", configuration, given, out schemes, out _, out _, out allowedHosts,
            out databases, out files, out allowed, out env, out error);

    /// <summary>
    /// The configuration in <paramref name="folder"/>, its file <paramref name="configuration"/>: its schemes, the command
    /// line's given over them, its mode, <c>development</c>, <c>production</c>, or null if it names none, and its send
    /// timeout, in seconds, or null if it gives none; false, with why, otherwise.
    /// </summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? mode, out double? sendTimeout, out string? error) =>
        TryLoadFolder(folder, configuration, given, out schemes, out mode, out sendTimeout, out _, out _, out _, out _, out _, out error);

    /// <summary>
    /// As the other, and the hosts the configuration allows its scripts to reach, none when it names none, its databases,
    /// each name's connection string, and the folders its scripts may read and write, none when it names none.
    /// </summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? mode, out double? sendTimeout, out IReadOnlyList<string> allowedHosts,
        out IReadOnlyDictionary<string, string> databases, out WebLingoFileFolders files, out string? error) =>
        TryLoadFolder(folder, configuration, given, out schemes, out mode, out sendTimeout, out allowedHosts, out databases, out files,
            out _, out _, out error);

    /// <summary>
    /// As the other, and what its settings allow native modules, <c>"com"</c>'s list, by its setting's name, and what its
    /// scripts may read of the machine's environment, nothing when it says nothing.
    /// </summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? mode, out double? sendTimeout, out IReadOnlyList<string> allowedHosts,
        out IReadOnlyDictionary<string, string> databases, out WebLingoFileFolders files, out IReadOnlyDictionary<string, IReadOnlyList<string>> allowed,
        out WebLingoEnvSettings env, out string? error)
    {
        env = WebLingoEnvSettings.None;
        var allowing = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        allowed = allowing;
        sendTimeout = null;
        var hosts = new List<string>();
        allowedHosts = hosts;
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        databases = named;
        var (read, write) = (new List<string>(), new List<string>());
        files = new WebLingoFileFolders(read, write);
        schemes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["std"] = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "std"))
        };
        // Windows's own native modules, windows:com among them, beside the program on Windows only (section 3.10).
        if (OperatingSystem.IsWindows()) schemes["windows"] = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "windows"));
        (mode, error) = (null, null);
        folder = Path.GetFullPath(folder);
        var file = Path.Combine(folder, configuration);
        if (File.Exists(file) && !TryRead(file, folder, schemes, ref mode, ref sendTimeout, hosts, named, read, write,
            allowing, ref env, out error))
        {
            return false;
        }
        foreach (var (name, path) in given) schemes[name] = Path.GetFullPath(path);
        return true;
    }

    /// <summary>
    /// <c>"files": { "read": [folder, …], "write": [folder, …] }</c>, either list optional, each folder's full path, a
    /// relative one from the configuration's folder.
    /// </summary>
    private static bool TryReadFolders(string file, string folder, JsonElement files, List<string> read, List<string> write, out string? error)
    {
        error = $"{file}: \"files\" is an object, {{ \"read\": [\"../data\"], \"write\": [\"../uploads\"] }}, each a list of folders.";
        if (files.ValueKind != JsonValueKind.Object) return false;
        foreach (var list in files.EnumerateObject())
        {
            if (list.Name is not ("read" or "write") || list.Value.ValueKind != JsonValueKind.Array) return false;
            foreach (var entry in list.Value.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.GetString())) return false;
                (list.Name == "read" ? read : write).Add(Path.GetFullPath(Path.Combine(folder, entry.GetString()!)));
            }
        }
        error = null;
        return true;
    }

    /// <summary>
    /// <c>"env": { "machineName": true, "platform": true, "variables": ["PATH", "APP_*"] }</c>, each optional: whether its
    /// scripts may read the machine's name, and its architectures and operating system, and the variables they may read,
    /// each a name, or a name ending in <c>*</c>, or <c>"*"</c>.
    /// </summary>
    private static bool TryReadEnv(string file, JsonElement setting, out WebLingoEnvSettings env, out string? error)
    {
        env = WebLingoEnvSettings.None;
        error = $"{file}: \"env\" is an object, {{ \"machineName\": true, \"platform\": true, \"variables\": [\"PATH\", \"APP_*\"] }}, each optional.";
        if (setting.ValueKind != JsonValueKind.Object) return false;
        var (machineName, platform, variables) = (false, false, new List<string>());
        foreach (var entry in setting.EnumerateObject())
        {
            if (entry.Name == "machineName" && entry.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                machineName = entry.Value.ValueKind == JsonValueKind.True;
                continue;
            }
            if (entry.Name == "platform" && entry.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                platform = entry.Value.ValueKind == JsonValueKind.True;
                continue;
            }
            if (entry.Name != "variables" || entry.Value.ValueKind != JsonValueKind.Array) return false;
            foreach (var variable in entry.Value.EnumerateArray())
            {
                var name = variable.ValueKind == JsonValueKind.String ? variable.GetString()! : "";
                var star = name.IndexOf('*');
                if (name.Length == 0 || name.Contains('=') || star >= 0 && star != name.Length - 1)
                {
                    error = $"{file}: \"env\"'s \"variables\" lists variables' names, each a name, a name ending in '*' for those it "
                            + "begins, or \"*\" for any.";
                    return false;
                }
                variables.Add(name);
            }
        }
        (env, error) = (new WebLingoEnvSettings(machineName, variables, platform), null);
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

    private static bool TryRead(string file, string folder, Dictionary<string, string> schemes, ref string? mode, ref double? sendTimeout,
        List<string> hosts, Dictionary<string, string> databases, List<string> read, List<string> write,
        Dictionary<string, IReadOnlyList<string>> allowed, ref WebLingoEnvSettings env, out string? error)
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
                if (setting.Name == "mode")
                {
                    if (setting.Value.ValueKind != JsonValueKind.String || setting.Value.GetString() is not ("development" or "production"))
                    {
                        error = $"{file}: \"mode\" is \"development\" or \"production\".";
                        return false;
                    }
                    mode = setting.Value.GetString();
                    continue;
                }
                if (setting.Name == "env")
                {
                    // What its scripts may read of the machine's environment (section 7.15).
                    if (!TryReadEnv(file, setting.Value, out env, out error)) return false;
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
                if (setting.Name == "databases")
                {
                    // Its databases, each a name a script opens it by, without a colon, and a connection string (section 7.11).
                    if (setting.Value.ValueKind != JsonValueKind.Object)
                    {
                        error = $"{file}: \"databases\" is an object, {{ \"shop\": \"sqlite:../data/shop.db\" }}.";
                        return false;
                    }
                    foreach (var database in setting.Value.EnumerateObject())
                    {
                        var connection = database.Value.ValueKind == JsonValueKind.String ? database.Value.GetString()! : "";
                        if (database.Name.Length == 0 || database.Name.Contains(':') || connection.IndexOf(':') < 1)
                        {
                            error = $"{file}: \"databases\" names each database, without a colon, and gives its connection string, "
                                    + "its provider's name, a colon, and what the provider reads, \"sqlite:../data/shop.db\".";
                            return false;
                        }
                        databases[database.Name] = connection;
                    }
                    continue;
                }
                if (setting.Name == "com")
                {
                    // The COM objects its scripts may make, by ProgID, CLSID or moniker, "*" any (section 3.10).
                    var objects = new List<string>();
                    if (setting.Value.ValueKind != JsonValueKind.Object || !setting.Value.TryGetProperty("allow", out var listed)
                        || listed.ValueKind != JsonValueKind.Array || setting.Value.EnumerateObject().Count() != 1)
                    {
                        error = $"{file}: \"com\" is an object, {{ \"allow\": [\"ADODB.Connection\"] }}.";
                        return false;
                    }
                    foreach (var progId in listed.EnumerateArray())
                    {
                        if (progId.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(progId.GetString()))
                        {
                            error = $"{file}: \"com\"'s \"allow\" lists COM objects, each a ProgID, a CLSID, a moniker, or \"*\".";
                            return false;
                        }
                        objects.Add(progId.GetString()!);
                    }
                    allowed["com"] = objects;
                    continue;
                }
                if (setting.Name == "files")
                {
                    // The folders its scripts may read, and read and write (section 7.13, "File capabilities").
                    if (!TryReadFolders(file, folder, setting.Value, read, write, out error)) return false;
                    continue;
                }
                if (setting.Name != "schemes")
                {
                    error = $"{file}: '{setting.Name}' is not a setting; the settings are \"schemes\", \"mode\", "
                            + "\"sendTimeout\", \"network\", \"databases\", \"files\", \"com\" and \"env\".";
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

/// <summary>
/// The folders a configuration lets its scripts read, and read and write, each a full path (specification, section 7.13,
/// "File capabilities").
/// </summary>
public sealed record WebLingoFileFolders(IReadOnlyList<string> Read, IReadOnlyList<string> Write);

/// <summary>
/// What a configuration lets its scripts read of the machine's environment (specification, section 7.15): the machine's
/// name, or not, and the variables it lists, each a name, a name ending in <c>*</c> for those it begins, or <c>"*"</c>.
/// </summary>
public sealed record WebLingoEnvSettings(bool MachineName, IReadOnlyList<string> Variables, bool Platform = false)
{
    /// <summary>Nothing of it, as a configuration without <c>"env"</c> allows.</summary>
    public static WebLingoEnvSettings None { get; } = new(false, []);
}
