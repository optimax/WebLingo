using System.Text.Json;
using WebLingo.Bytecode;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
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
/// <c>weblingo</c>'s scripts reading all of it (section 7.15); and its mail servers, each a name and its settings,
/// <c>"mail": { "default": { "host": "smtp.example.com", "port": 587, "user": "…", "password": "…" } }</c>, which both
/// read (section 7.17); and where a site keeps its sessions, <c>"sessions": { "store": "database", "database": "main",
/// "table": "wasp_sessions", "create": false, "timeout": 20, "lockTimeout": 30 }</c>, which <c>wasp</c> reads (section 6.12,
/// "Sessions and cookies"). A secret, a mail server's
/// password or a database's connection string, may be given where it is
/// kept, <c>{ "env": "NAME" }</c> or <c>{ "file": "../secrets/name.txt" }</c>, a file from the file's folder, one a
/// site's <c>wasp.config</c> names outside the site, read by the host when it is used. On Windows, the <c>windows</c> scheme's folder is
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
        out Dictionary<string, string> schemes, out IReadOnlyList<string> allowedHosts, out IReadOnlyDictionary<string, WebLingoSecret> databases,
        out WebLingoFileFolders files, out string? error) =>
        TryLoad(program, configuration, given, out schemes, out allowedHosts, out databases, out files, out _, out _, out _, out error);

    /// <summary>
    /// As the other, and what its settings allow native modules, <c>"com"</c>'s list, by its setting's name, and what its
    /// scripts may read of the machine's environment, and its mail servers.
    /// </summary>
    public static bool TryLoad(string program, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out IReadOnlyList<string> allowedHosts, out IReadOnlyDictionary<string, WebLingoSecret> databases,
        out WebLingoFileFolders files, out IReadOnlyDictionary<string, IReadOnlyList<string>> allowed, out WebLingoEnvSettings env,
        out IReadOnlyDictionary<string, WebLingoMailServer> mail, out string? error) =>
        TryLoadFolder(Path.GetDirectoryName(Path.GetFullPath(program)) ?? ".", configuration, given, out schemes, out _, out _, out allowedHosts,
            out databases, out files, out allowed, out env, out mail, out _, out _, out _, out _, out _, out _, out _, out error);

    /// <summary>
    /// The limits the configuration beside <paramref name="program"/> gives, its file <paramref name="configuration"/>, a
    /// script's workers' among them (section 9, "M0.26 parts", part 4); false, with why, where it cannot be read.
    /// </summary>
    public static bool TryLoadLimits(string program, string configuration, IReadOnlyDictionary<string, string> given,
        out WebLingoLimitSettings limits, out string? error) =>
        TryLoadFolder(Path.GetDirectoryName(Path.GetFullPath(program)) ?? ".", configuration, given, out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out _, out limits, out _, out _, out _, out _, out error);

    /// <summary>
    /// The configuration in <paramref name="folder"/>, its file <paramref name="configuration"/>: its schemes, the command
    /// line's given over them, its mode, <c>development</c>, <c>production</c>, or null if it names none, and its send
    /// timeout, in seconds, or null if it gives none; false, with why, otherwise.
    /// </summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? mode, out double? sendTimeout, out string? error) =>
        TryLoadFolder(folder, configuration, given, out schemes, out mode, out sendTimeout, out _, out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out _, out _, out error);

    /// <summary>
    /// As the other, and the hosts the configuration allows its scripts to reach, none when it names none, its databases,
    /// each name's connection string, and the folders its scripts may read and write, none when it names none.
    /// </summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? mode, out double? sendTimeout, out IReadOnlyList<string> allowedHosts,
        out IReadOnlyDictionary<string, WebLingoSecret> databases, out WebLingoFileFolders files, out string? error) =>
        TryLoadFolder(folder, configuration, given, out schemes, out mode, out sendTimeout, out allowedHosts, out databases, out files,
            out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out error);

    /// <summary>
    /// As the other, and what its settings allow native modules, <c>"com"</c>'s list, by its setting's name, and what its
    /// scripts may read of the machine's environment, nothing when it says nothing, its mail servers, none when it names
    /// none, what it says of a request's body, where it keeps its sessions, a request's limits, its log, the headers its
    /// responses have, and the extensions of the files it serves that a site does not by default, a site's only.
    /// </summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? mode, out double? sendTimeout, out IReadOnlyList<string> allowedHosts,
        out IReadOnlyDictionary<string, WebLingoSecret> databases, out WebLingoFileFolders files, out IReadOnlyDictionary<string, IReadOnlyList<string>> allowed,
        out WebLingoEnvSettings env, out IReadOnlyDictionary<string, WebLingoMailServer> mail, out WebLingoUploadSettings uploads,
        out WebLingoSessionSettings sessions, out WebLingoLimitSettings limits, out WebLingoLogSettings log, out WebLingoHeaderSettings headers,
        out IReadOnlyList<string> served, out IReadOnlyList<string> proxies, out string? error)
    {
        var trusted = new List<string>();
        proxies = trusted;
        limits = WebLingoLimitSettings.Default;
        log = WebLingoLogSettings.Default;
        headers = WebLingoHeaderSettings.Default;
        var serving = new List<string>();
        served = serving;
        var servers = new Dictionary<string, WebLingoMailServer>(StringComparer.Ordinal);
        mail = servers;
        uploads = WebLingoUploadSettings.Default;
        sessions = WebLingoSessionSettings.Default;
        env = WebLingoEnvSettings.None;
        var allowing = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        allowed = allowing;
        sendTimeout = null;
        var hosts = new List<string>();
        allowedHosts = hosts;
        var named = new Dictionary<string, WebLingoSecret>(StringComparer.Ordinal);
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
        if (File.Exists(file))
        {
            // A site over a base: its base's settings first, those the site's own does not give, each path in them from the
            // base's folder (section 6.12, "A site over a base").
            if (!TryFindBase(folder, configuration, out var over, out var own, out error)) return false;
            var baseFile = over is null ? null : Path.Combine(over, configuration);
            if (baseFile is not null && File.Exists(baseFile) && !TryRead(baseFile, over!, schemes, ref mode, ref sendTimeout, hosts, named, read,
                write, allowing, ref env, servers, ref uploads, ref sessions, ref limits, ref log, ref headers, serving, trusted, own, out error))
            {
                return false;
            }
            if (!TryRead(file, folder, schemes, ref mode, ref sendTimeout, hosts, named, read, write,
                allowing, ref env, servers, ref uploads, ref sessions, ref limits, ref log, ref headers, serving, trusted, null, out error))
            {
                return false;
            }
        }
        foreach (var (name, path) in given) schemes[name] = Path.GetFullPath(path);
        return true;
    }

    /// <summary>
    /// <summary>
    /// The base a site's configuration in <paramref name="folder"/> names, <c>"base": "../forum/site"</c> (specification,
    /// section 6.12, "A site over a base"): the full path of the folder of the site it lies over, from the configuration's
    /// folder, whose files the site's own come before and whose configuration gives the settings the site's own does not;
    /// null where it names none; and <paramref name="own"/>, the settings the site's configuration gives. A site's alone,
    /// <c>wasp.config</c>'s, never a script's; one base, which names none of its own; neither folder within the other.
    /// A configuration that is not an object, or cannot be read, is left to <see cref="TryRead"/> to report.
    /// </summary>
    public static bool TryFindBase(string folder, string configuration, out string? baseFolder, out IReadOnlySet<string> own, out string? error)
    {
        (baseFolder, error) = (null, null);
        var names = new HashSet<string>(StringComparer.Ordinal);
        own = names;
        folder = Path.GetFullPath(folder);
        var file = Path.Combine(folder, configuration);
        string? given;
        try
        {
            if (!File.Exists(file)) return true;
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return true;
            foreach (var setting in document.RootElement.EnumerateObject()) names.Add(setting.Name);
            if (!document.RootElement.TryGetProperty("base", out var named)) return true;
            given = named.ValueKind == JsonValueKind.String ? named.GetString() : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
        if (configuration == ConfigurationFile)
        {
            error = $"{file}: \"base\" is a site's setting, its wasp.config's, not a script's.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(given))
        {
            error = $"{file}: \"base\" is the folder of the site this one lies over, \"../forum/site\".";
            return false;
        }
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(folder, given)));
        if (!Directory.Exists(full))
        {
            error = $"{file}: \"base\", {full}, is not a folder.";
            return false;
        }
        if (Within(full, folder) || Within(folder, full))
        {
            error = $"{file}: \"base\", {full}, and the site's folder are one within the other: a base is a folder of its own.";
            return false;
        }
        var baseFile = Path.Combine(full, configuration);
        try
        {
            if (File.Exists(baseFile))
            {
                using var based = JsonDocument.Parse(File.ReadAllText(baseFile));
                if (based.RootElement.ValueKind == JsonValueKind.Object && based.RootElement.TryGetProperty("base", out _))
                {
                    error = $"{baseFile}: a base names no base of its own: a site lies over one base, {file}'s.";
                    return false;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Its own reading, TryRead's, reports it.
        }
        baseFolder = full;
        return true;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or in it, as the file system compares names.</summary>
    private static bool Within(string path, string folder)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var (inner, outer) = (Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
        return inner.Equals(outer, comparison) || inner.StartsWith(outer + Path.DirectorySeparatorChar, comparison);
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
    /// <c>"mail": { "default": { "host": "smtp.example.com", "port": 587, "user": "…", "password": "…", "tls": "starttls",
    /// "thumbprint": "…" } }</c>: each server's name and its settings, its host required, its port by default its TLS's
    /// (587, 465 for <c>"implicit"</c>, 25 for <c>"none"</c>), its password a secret, which needs its user (section 7.17).
    /// </summary>
    private static bool TryReadMail(string file, string folder, JsonElement setting, Dictionary<string, WebLingoMailServer> mail, out string? error)
    {
        if (setting.ValueKind != JsonValueKind.Object)
        {
            error = $"{file}: \"mail\" is an object, each server's name and its settings, {{ \"default\": {{ \"host\": \"smtp.example.com\" }} }}.";
            return false;
        }
        foreach (var server in setting.EnumerateObject())
        {
            var what = $"\"mail\"'s \"{server.Name}\"";
            if (server.Name.Length == 0 || server.Value.ValueKind != JsonValueKind.Object)
            {
                error = $"{file}: \"mail\" names each server, and gives its settings, {{ \"host\": \"smtp.example.com\" }}.";
                return false;
            }
            string? host = null, user = null, thumbprint = null;
            int? port = null;
            var tls = WebLingoMailTls.StartTls;
            WebLingoSecret? password = null;
            foreach (var entry in server.Value.EnumerateObject())
            {
                var text = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString()! : "";
                if (entry.Name == "host" && text.Length > 0) host = text;
                else if (entry.Name == "user" && text.Length > 0) user = text;
                else if (entry.Name == "port" && entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var number)
                         && number is >= 1 and <= 65535) port = number;
                else if (entry.Name == "tls" && text is "starttls" or "implicit" or "none")
                    tls = text switch { "implicit" => WebLingoMailTls.Implicit, "none" => WebLingoMailTls.None, _ => WebLingoMailTls.StartTls };
                else if (entry.Name == "thumbprint" && text.Length > 0 && text.All(c => char.IsAsciiHexDigit(c) || c is ':' or ' ' or '-'))
                    thumbprint = text;
                else if (entry.Name == "password")
                {
                    if (!TryReadSecret(file, folder, entry.Value, $"{what}'s \"password\"", out password, out error)) return false;
                }
                else
                {
                    error = $"{file}: {what} gives its \"host\", and, each optional, its \"port\", from 1 to 65535, its \"user\" and its \"password\", "
                            + "its \"tls\", \"starttls\", \"implicit\" or \"none\", and a certificate's \"thumbprint\", in hexadecimal: "
                            + $"'{entry.Name}' is not one, or not given as one.";
                    return false;
                }
            }
            if (host is null || password is not null && user is null)
            {
                error = host is null ? $"{file}: {what} gives its \"host\"." : $"{file}: {what}'s \"password\" needs its \"user\".";
                return false;
            }
            mail[server.Name] = new WebLingoMailServer(host, port ?? WebLingoMailServer.DefaultPort(tls), tls, user, password, thumbprint);
        }
        error = null;
        return true;
    }

    /// <summary>
    /// <c>"limits": { "requestTimeout": 60, "requestInstructions": 1000000000, "requestMemory": 128, "drain": 60 }</c>, each
    /// optional: how many seconds a request may take, its waits included, a number more than 0, or <c>null</c> for no time
    /// limit; how many instructions it may run, an integer more than 0; how many megabytes of strings its heap may hold, an
    /// integer from 1 to 1,048,576 (section 6.12, "Limits"); and how many seconds a request may go on running on the site's
    /// files as they were before a reload, a number more than 0 (section 6.12, "Reloading"). Each it does not give is its
    /// host's default. A number of seconds is at most <see cref="MaxSeconds"/>, the longest a .NET timer keeps.
    /// </summary>
    private static bool TryReadLimits(string file, JsonElement setting, ref WebLingoLimitSettings limits, out string? error)
    {
        error = $"{file}: \"limits\" is an object, {{ \"requestTimeout\": 60, \"requestInstructions\": 1000000000, \"requestMemory\": 128, "
                + "\"drain\": 60, \"requestLine\": 8192, \"requestHeaders\": 32768, \"requestArrival\": 30, \"keepAlive\": 130, \"workers\": 8, "
                + "\"workerTimeout\": 60, \"workerInstructions\": 1000000000, \"workerMemory\": 128 }, each optional, \"requestTimeout\" and "
                + "\"workerTimeout\" numbers of seconds more than 0, or null for none, \"requestInstructions\" and \"workerInstructions\" integers "
                + "more than 0, \"workerInstructions\" null for none too, \"requestMemory\" and \"workerMemory\" numbers of megabytes from 1 to "
                + "1048576, \"workers\" an integer from 1 to 1024, \"drain\", \"requestArrival\" and \"keepAlive\" numbers of seconds more than 0, "
                + "and \"requestLine\" and \"requestHeaders\" numbers of bytes from 1 to 1048576; \"quantum\": 100000, the instructions a "
                + "request runs before it lets the others run, an integer more than 0, or null for none; and \"shared\": 64, the megabytes the "
                + "memory the requests share holds at most, an integer from 1 to 1048576, or null for no most; "
                + $"each number of seconds at most {MaxSeconds}.";
        if (setting.ValueKind != JsonValueKind.Object) return false;
        foreach (var entry in setting.EnumerateObject())
        {
            var number = entry.Value.ValueKind == JsonValueKind.Number;
            if (entry.Name == "requestTimeout" && number && entry.Value.TryGetDouble(out var seconds) && seconds is > 0 and <= MaxSeconds)
            {
                limits = limits with { TimeoutSeconds = seconds, NoTimeout = false };
            }
            else if (entry.Name == "requestTimeout" && entry.Value.ValueKind == JsonValueKind.Null)
            {
                limits = limits with { TimeoutSeconds = null, NoTimeout = true };
            }
            else if (entry.Name == "drain" && number && entry.Value.TryGetDouble(out var drain) && drain is > 0 and <= MaxSeconds)
            {
                limits = limits with { DrainSeconds = drain };
            }
            else if (entry.Name == "requestArrival" && number && entry.Value.TryGetDouble(out var arrival) && arrival is > 0 and <= MaxSeconds)
            {
                limits = limits with { RequestArrivalSeconds = arrival };
            }
            else if (entry.Name == "keepAlive" && number && entry.Value.TryGetDouble(out var keepAlive) && keepAlive is > 0 and <= MaxSeconds)
            {
                limits = limits with { KeepAliveSeconds = keepAlive };
            }
            else if (entry.Name == "requestLine" && number && entry.Value.TryGetInt32(out var line) && line is >= 1 and <= 1_048_576)
            {
                limits = limits with { RequestLine = line };
            }
            else if (entry.Name == "requestHeaders" && number && entry.Value.TryGetInt32(out var headers) && headers is >= 1 and <= 1_048_576)
            {
                limits = limits with { RequestHeaders = headers };
            }
            else if (entry.Name == "requestInstructions" && number && entry.Value.TryGetInt64(out var instructions) && instructions > 0)
            {
                limits = limits with { Instructions = instructions };
            }
            else if (entry.Name == "requestMemory" && number && entry.Value.TryGetInt64(out var megabytes) && megabytes is >= 1 and <= 1_048_576)
            {
                limits = limits with { MemoryMegabytes = megabytes };
            }
            // A request's quantum (section 9, "M0.24 parts", part 2).
            else if (entry.Name == "quantum" && number && entry.Value.TryGetInt64(out var quantum) && quantum > 0)
            {
                limits = limits with { Quantum = quantum, NoQuantum = false };
            }
            else if (entry.Name == "quantum" && entry.Value.ValueKind == JsonValueKind.Null)
            {
                limits = limits with { Quantum = null, NoQuantum = true };
            }
            // The memory the requests share, its most (section 6.12, "Memory a site's requests share").
            else if (entry.Name == "shared" && number && entry.Value.TryGetInt64(out var sharedMegabytes) && sharedMegabytes is >= 1 and <= 1_048_576)
            {
                limits = limits with { SharedMegabytes = sharedMegabytes, NoSharedMost = false };
            }
            else if (entry.Name == "shared" && entry.Value.ValueKind == JsonValueKind.Null)
            {
                limits = limits with { SharedMegabytes = null, NoSharedMost = true };
            }
            // A script's workers' limits, weblingo's (section 9, "M0.26 parts", part 4).
            else if (entry.Name == "workers" && number && entry.Value.TryGetInt32(out var workers) && workers is >= 1 and <= 1024)
            {
                limits = limits with { Workers = workers };
            }
            else if (entry.Name == "workerTimeout" && number && entry.Value.TryGetDouble(out var workerSeconds) && workerSeconds is > 0 and <= MaxSeconds)
            {
                limits = limits with { WorkerTimeoutSeconds = workerSeconds };
            }
            else if (entry.Name == "workerTimeout" && entry.Value.ValueKind == JsonValueKind.Null)
            {
                limits = limits with { WorkerTimeoutSeconds = null };
            }
            else if (entry.Name == "workerInstructions" && number && entry.Value.TryGetInt64(out var workerInstructions) && workerInstructions > 0)
            {
                limits = limits with { WorkerInstructions = workerInstructions };
            }
            else if (entry.Name == "workerInstructions" && entry.Value.ValueKind == JsonValueKind.Null)
            {
                limits = limits with { WorkerInstructions = null };
            }
            else if (entry.Name == "workerMemory" && number && entry.Value.TryGetInt64(out var workerMegabytes) && workerMegabytes is >= 1 and <= 1_048_576)
            {
                limits = limits with { WorkerMemoryMegabytes = workerMegabytes };
            }
            else
            {
                return false;
            }
        }
        error = null;
        return true;
    }

    /// <summary>The most seconds a limit of time may be, 49.7 days, the longest a .NET timer keeps, 4,294,967,294 ms.</summary>
    private const double MaxSeconds = 4_294_967;

    /// <summary>The policies <c>Referrer-Policy</c> takes (W3C's Referrer Policy, section 3).</summary>
    private static readonly string[] ReferrerPolicies =
    [
        "no-referrer", "no-referrer-when-downgrade", "origin", "origin-when-cross-origin", "same-origin", "strict-origin",
        "strict-origin-when-cross-origin", "unsafe-url"
    ];

    /// <summary>
    /// <c>"headers": { "nosniff": true, "referrerPolicy": "strict-origin-when-cross-origin", "frameAncestors": ["self"] }</c>,
    /// each optional, each given its default where it is not (section 6.12, "Security headers"): whether its responses say
    /// <c>X-Content-Type-Options: nosniff</c>; their <c>Referrer-Policy</c>, or <c>null</c> for none; and the sites that may
    /// show its pages in a frame, <c>Content-Security-Policy: frame-ancestors</c>'s sources, <c>"self"</c> and
    /// <c>"none"</c> written without its quotes, <c>"none"</c> alone, or <c>null</c> for no limit.
    /// </summary>
    private static bool TryReadHeaders(string file, JsonElement setting, ref WebLingoHeaderSettings headers, out string? error)
    {
        error = $"{file}: \"headers\" is an object, {{ \"nosniff\": true, \"referrerPolicy\": \"strict-origin-when-cross-origin\", "
                + "\"frameAncestors\": [\"self\"] }, each optional, \"nosniff\" a Boolean, \"referrerPolicy\" \"no-referrer\", "
                + "\"no-referrer-when-downgrade\", \"origin\", \"origin-when-cross-origin\", \"same-origin\", \"strict-origin\", "
                + "\"strict-origin-when-cross-origin\", \"unsafe-url\" or null for none, and \"frameAncestors\" the sites that may show its "
                + "pages in a frame, \"self\", \"none\" alone, or a scheme or a host, \"https://partner.example\", or null for any.";
        if (setting.ValueKind != JsonValueKind.Object) return false;
        foreach (var entry in setting.EnumerateObject())
        {
            var value = entry.Value;
            if (entry.Name == "nosniff" && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                headers = headers with { NoSniff = value.GetBoolean() };
            }
            else if (entry.Name == "referrerPolicy" && (value.ValueKind == JsonValueKind.Null
                     || value.ValueKind == JsonValueKind.String && Array.IndexOf(ReferrerPolicies, value.GetString()) >= 0))
            {
                headers = headers with { ReferrerPolicy = value.ValueKind == JsonValueKind.Null ? null : value.GetString() };
            }
            else if (entry.Name == "frameAncestors" && value.ValueKind == JsonValueKind.Null)
            {
                headers = headers with { FrameAncestors = null };
            }
            else if (entry.Name == "frameAncestors" && TryReadAncestors(value, out var ancestors))
            {
                headers = headers with { FrameAncestors = ancestors };
            }
            else
            {
                return false;
            }
        }
        error = null;
        return true;
    }

    /// <summary>
    /// <c>frame-ancestors</c>' sources: <c>"self"</c> and <c>"none"</c> as CSP quotes them, <c>"none"</c> alone, and a scheme
    /// or a host, its scheme and its port optional, as written, of the characters a URL's scheme, host and port have, so that
    /// none ends the header's directive or the header itself.
    /// </summary>
    private static bool TryReadAncestors(JsonElement value, out List<string> ancestors)
    {
        ancestors = [];
        if (value.ValueKind != JsonValueKind.Array) return false;
        foreach (var entry in value.EnumerateArray())
        {
            var text = entry.ValueKind == JsonValueKind.String ? entry.GetString()! : "";
            if (text is "self" or "none")
            {
                ancestors.Add("'" + text + "'");
                continue;
            }
            if (text.Length == 0 || text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '.' or ':' or '/' or '*' or '+'))) return false;
            ancestors.Add(text);
        }
        return ancestors.Count > 0 && (ancestors.Count == 1 || !ancestors.Contains("'none'"));
    }

    /// <summary>
    /// <c>"serve": { "allow": [".log"] }</c> (section 6.12, "Sensitive files"): the extensions, each with its dot, of the files
    /// a site does not serve by default that it serves, in lower case; its host checks that each is one of those.
    /// </summary>
    private static bool TryReadServe(string file, JsonElement setting, List<string> served, out string? error)
    {
        error = $"{file}: \"serve\" is an object, {{ \"allow\": [\".log\"] }}, the extensions, each with its dot, of the files a site "
                + "does not serve by default that it serves.";
        if (setting.ValueKind != JsonValueKind.Object) return false;
        foreach (var entry in setting.EnumerateObject())
        {
            if (entry.Name != "allow" || entry.Value.ValueKind != JsonValueKind.Array) return false;
            foreach (var extension in entry.Value.EnumerateArray())
            {
                var text = extension.ValueKind == JsonValueKind.String ? extension.GetString()! : "";
                if (text.Length < 2 || text[0] != '.' || text.IndexOfAny(['.', '/', '\\'], 1) >= 0) return false;
                served.Add(text.ToLowerInvariant());
            }
        }
        error = null;
        return true;
    }

    /// <summary>
    /// <c>"log": { "file": "../logs/shop.log", "level": "info" }</c>, each optional (section 6.12, "Logging"): the file its
    /// log is written to, a file a day, from the file's folder, the host's own log where it names none; and the lowest
    /// level written, <c>"debug"</c>, <c>"info"</c>, <c>"warn"</c> or <c>"error"</c>, <c>"info"</c> where it names none.
    /// </summary>
    private static bool TryReadLog(string file, string folder, JsonElement setting, ref WebLingoLogSettings log, out string? error)
    {
        error = $"{file}: \"log\" is an object, {{ \"file\": \"../logs/shop.log\", \"level\": \"info\" }}, each optional, \"file\" a file "
                + "outside the site, a file a day written with the day's date before its extension, and \"level\" the lowest written, "
                + "\"debug\", \"info\", \"warn\" or \"error\".";
        if (setting.ValueKind != JsonValueKind.Object) return false;
        foreach (var entry in setting.EnumerateObject())
        {
            var text = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
            if (entry.Name == "file" && !string.IsNullOrEmpty(text))
            {
                log = log with { File = Path.GetFullPath(Path.Combine(folder, text)) };
            }
            else if (entry.Name == "level" && WebLingoLogText.TryParse(text, out var level))
            {
                log = log with { Level = level };
            }
            else
            {
                return false;
            }
        }
        error = null;
        return true;
    }

    /// <summary>
    /// <c>"uploads": { "inMemory": 65536, "maxSize": 20000000, "folder": "../tmp" }</c>, each optional: how many bytes of a
    /// file a form uploads are kept in memory, the rest in a temporary file; the most its files may take in all; and the
    /// folder their temporary files are made in, from the file's folder, the system's temporary folder if none is named.
    /// </summary>
    private static bool TryReadUploads(string file, string folder, JsonElement setting, ref WebLingoUploadSettings uploads, out string? error)
    {
        error = $"{file}: \"uploads\" is an object, {{ \"inMemory\": 65536, \"maxSize\": 20000000, \"folder\": \"../tmp\" }}, each "
                + "optional, \"inMemory\" a number of bytes, 0 or more, \"maxSize\" one more than 0, and \"folder\" a folder.";
        if (setting.ValueKind != JsonValueKind.Object) return false;
        foreach (var entry in setting.EnumerateObject())
        {
            if (entry.Name == "inMemory" && entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var kept) && kept >= 0)
                uploads = uploads with { InMemory = kept };
            else if (entry.Name == "maxSize" && entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt64(out var most) && most > 0)
                uploads = uploads with { MaxSize = most };
            else if (entry.Name == "folder" && entry.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.Value.GetString()))
                uploads = uploads with { Folder = Path.GetFullPath(Path.Combine(folder, entry.Value.GetString()!)) };
            else return false;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// <c>"sessions": { "store": "database", "database": "main", "table": "wasp_sessions", "create": false, "timeout": 20,
    /// "lockTimeout": 30 }</c>, each optional: where a site keeps its sessions, <c>"memory"</c>, the process's, or
    /// <c>"database"</c>, a table of a database <c>"databases"</c> names, which <c>"database"</c> names, the table
    /// <c>"table"</c>, <c>wasp_sessions</c> unless it names another, made where it is missing where <c>"create"</c> is
    /// <c>true</c>; how many minutes a session lasts once no request uses it; and how many seconds a request waits for a
    /// session's lock (section 6.12, "Sessions and cookies").
    /// </summary>
    private static bool TryReadSessions(string file, JsonElement setting, ref WebLingoSessionSettings sessions, out string? error)
    {
        error = $"{file}: \"sessions\" is an object, {{ \"store\": \"database\", \"database\": \"main\", \"table\": \"wasp_sessions\", "
                + "\"create\": false, \"timeout\": 20, \"lockTimeout\": 30 }, each optional, \"store\" \"memory\" or \"database\", "
                + "\"database\" a name \"databases\" gives, \"table\" a table's name, letters, digits and _, \"create\" a Boolean, "
                + "\"timeout\" a number of minutes, and \"lockTimeout\" a number of seconds, each more than 0.";
        if (setting.ValueKind != JsonValueKind.Object) return false;
        foreach (var entry in setting.EnumerateObject())
        {
            var number = entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetDouble(out var given) && given > 0
                && given <= TimeSpan.MaxValue.TotalSeconds / 120 ? given : (double?)null;
            var text = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString()! : null;
            if (entry.Name == "store" && text is "memory" or "database") sessions = sessions with { Store = text };
            else if (entry.Name == "database" && text is { Length: > 0 } && !text.Contains(':')) sessions = sessions with { Database = text };
            else if (entry.Name == "table" && text is not null && IsTable(text)) sessions = sessions with { Table = text };
            else if (entry.Name == "create" && entry.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                sessions = sessions with { Create = entry.Value.ValueKind == JsonValueKind.True };
            else if (entry.Name == "timeout" && number is { } minutes) sessions = sessions with { TimeoutMinutes = minutes };
            else if (entry.Name == "lockTimeout" && number is { } seconds) sessions = sessions with { LockTimeoutSeconds = seconds };
            else return false;
        }
        if (sessions.Store == "database" && sessions.Database is null)
        {
            error = $"{file}: \"sessions\" kept in a database names it, \"database\": \"main\", one \"databases\" gives.";
            return false;
        }
        if (sessions.Store == "memory" && (sessions.Database is not null || sessions.Table is not null || sessions.Create))
        {
            error = $"{file}: \"sessions\" kept in memory, as they are unless \"store\" is \"database\", have no \"database\", \"table\" or \"create\".";
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>Whether a text may name a table of sessions: letters, digits and <c>_</c>, a letter or <c>_</c> first, 58 at most.</summary>
    public static bool IsTable(string name)
    {
        if (name.Length is 0 or > 58 || char.IsAsciiDigit(name[0])) return false;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    /// <summary>
    /// A secret, a string, itself, or where it is kept, <c>{ "env": "NAME" }</c>, an environment variable, or
    /// <c>{ "file": "../secrets/name.txt" }</c>, a file from the configuration's folder, one a site's <c>wasp.config</c>
    /// names outside the site, whose files may be served (section 7.17, "Secrets"); never written in an error.
    /// </summary>
    private static bool TryReadSecret(string file, string folder, JsonElement value, string what, out WebLingoSecret secret, out string? error)
    {
        (secret, error) = (null!, null);
        if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 0)
        {
            secret = WebLingoSecret.Given(value.GetString()!);
            return true;
        }
        var entries = value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().ToList() : [];
        var given = entries.Count == 1 && entries[0].Value.ValueKind == JsonValueKind.String ? entries[0].Value.GetString()! : "";
        if (entries.Count == 1 && entries[0].Name == "env" && given.Length > 0 && !given.Contains('='))
        {
            secret = new WebLingoSecret(WebLingoSecretSource.Variable, given);
            return true;
        }
        if (entries.Count == 1 && entries[0].Name == "file" && given.Length > 0)
        {
            var full = Path.GetFullPath(Path.Combine(folder, given));
            var within = full.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (within && string.Equals(Path.GetFileName(file), "wasp.config", StringComparison.Ordinal))
            {
                error = $"{file}: {what}'s file must be outside the site, whose files may be served, not \"{given}\".";
                return false;
            }
            secret = new WebLingoSecret(WebLingoSecretSource.File, full, given);
            return true;
        }
        error = $"{file}: {what} is a string, or where it is kept, {{ \"env\": \"NAME\" }} or {{ \"file\": \"../secrets/name.txt\" }}.";
        return false;
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
        List<string> hosts, Dictionary<string, WebLingoSecret> databases, List<string> read, List<string> write,
        Dictionary<string, IReadOnlyList<string>> allowed, ref WebLingoEnvSettings env, Dictionary<string, WebLingoMailServer> mail,
        ref WebLingoUploadSettings uploads, ref WebLingoSessionSettings sessions, ref WebLingoLimitSettings limits, ref WebLingoLogSettings log,
        ref WebLingoHeaderSettings headers, List<string> served, List<string> proxies, IReadOnlySet<string>? skipped, out string? error)
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
                // Its base, which TryFindBase read; and a base's setting the site's own configuration gives.
                if (setting.Name == "base" || skipped?.Contains(setting.Name) == true) continue;
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
                    // Its databases, each a name a script opens it by, without a colon, and a connection string (section 7.11), or
                    // where one is kept (section 7.17, "Secrets").
                    if (setting.Value.ValueKind != JsonValueKind.Object)
                    {
                        error = $"{file}: \"databases\" is an object, {{ \"shop\": \"sqlite:../data/shop.db\" }}.";
                        return false;
                    }
                    foreach (var database in setting.Value.EnumerateObject())
                    {
                        if (database.Value.ValueKind == JsonValueKind.Object && database.Name.Length > 0 && !database.Name.Contains(':'))
                        {
                            if (!TryReadSecret(file, folder, database.Value, $"\"databases\"'s \"{database.Name}\"", out var kept, out error)) return false;
                            databases[database.Name] = kept;
                            continue;
                        }
                        var connection = database.Value.ValueKind == JsonValueKind.String ? database.Value.GetString()! : "";
                        if (database.Name.Length == 0 || database.Name.Contains(':') || connection.IndexOf(':') < 1)
                        {
                            error = $"{file}: \"databases\" names each database, without a colon, and gives its connection string, "
                                    + "its provider's name, a colon, and what the provider reads, \"sqlite:../data/shop.db\".";
                            return false;
                        }
                        databases[database.Name] = WebLingoSecret.Given(connection);
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
                if (setting.Name == "maxBodySize")
                {
                    // The largest body a request may have, in bytes (section 6.12, "Uploaded files").
                    if (setting.Value.ValueKind != JsonValueKind.Number || !setting.Value.TryGetInt64(out var largest) || largest < 1)
                    {
                        error = $"{file}: \"maxBodySize\" is a number of bytes, more than 0.";
                        return false;
                    }
                    uploads = uploads with { MaxBodySize = largest };
                    continue;
                }
                if (setting.Name == "uploads")
                {
                    // What a form's files may be: how much of each is kept in memory, their largest in all, and where the
                    // rest of them is kept (section 6.12, "Uploaded files").
                    if (!TryReadUploads(file, folder, setting.Value, ref uploads, out error)) return false;
                    continue;
                }
                if (setting.Name == "sessions")
                {
                    // Where the site keeps its sessions, how long they last, and how long a request waits for one's lock (section 6.12).
                    if (!TryReadSessions(file, setting.Value, ref sessions, out error)) return false;
                    continue;
                }
                if (setting.Name == "log")
                {
                    // Where its log is written, and its lowest level (section 6.12, "Logging").
                    if (!TryReadLog(file, folder, setting.Value, ref log, out error)) return false;
                    continue;
                }
                if (setting.Name == "headers")
                {
                    // The headers its every response has unless a page changes them (section 6.12, "Security headers").
                    if (!TryReadHeaders(file, setting.Value, ref headers, out error)) return false;
                    continue;
                }
                if (setting.Name == "proxies")
                {
                    // The proxies it trusts, each an address or a range, which wasp reads (section 9, "M0.23 parts", part 4); a
                    // site's own in place of its base's.
                    if (setting.Value.ValueKind != JsonValueKind.Array
                        || setting.Value.EnumerateArray().Any(entry => entry.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.GetString())))
                    {
                        error = $"{file}: \"proxies\" is a list of the proxies it trusts, each an address or a range: [\"127.0.0.1\", \"10.0.0.0/8\"].";
                        return false;
                    }
                    proxies.Clear();
                    foreach (var entry in setting.Value.EnumerateArray()) proxies.Add(entry.GetString()!.Trim());
                    continue;
                }
                if (setting.Name == "serve")
                {
                    // The extensions of files a site does not serve by default that it does (section 6.12, "Sensitive files").
                    if (!TryReadServe(file, setting.Value, served, out error)) return false;
                    continue;
                }
                if (setting.Name == "limits")
                {
                    // How long a request may take, how many instructions it may run, and its heap's memory (section 6.12, "Limits").
                    if (!TryReadLimits(file, setting.Value, ref limits, out error)) return false;
                    continue;
                }
                if (setting.Name == "mail")
                {
                    // Its mail servers, each a name a script sends through it by, and its settings (section 7.17).
                    if (!TryReadMail(file, folder, setting.Value, mail, out error)) return false;
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
                            + "\"sendTimeout\", \"network\", \"databases\", \"files\", \"com\", \"env\", \"mail\", \"maxBodySize\", \"uploads\", "
                            + "\"sessions\", \"limits\", \"log\", \"headers\", \"serve\", \"proxies\" and \"base\".";
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
/// What a site's configuration says of a request's limits (specification, section 6.12, "Limits"): how many seconds it may
/// take, its waits included, or, where <see cref="NoTimeout"/>, no time limit at all; how many instructions it may run; and
/// how many megabytes of strings its heap may hold, two bytes a unit; and how many seconds it may go on running on the
/// site's files as they were before a reload (section 6.12, "Reloading"), <see cref="DrainSeconds"/>. Each null where it
/// says nothing of it, its host's default then.
/// </summary>
public sealed record WebLingoLimitSettings(double? TimeoutSeconds, long? Instructions, long? MemoryMegabytes)
{
    /// <summary>Whether it says that a request has no time limit, <c>"requestTimeout": null</c>.</summary>
    public bool NoTimeout { get; init; }

    /// <summary>The drain, in seconds, <c>"drain"</c>; null where it gives none.</summary>
    public double? DrainSeconds { get; init; }

    /// <summary>
    /// What its server takes of a client (section 9, "M0.23 parts", part 3): the bytes of a request's line,
    /// <c>"requestLine"</c>, and of its headers, <c>"requestHeaders"</c>; the seconds its line and headers may take to arrive,
    /// <c>"requestArrival"</c>; and the seconds a kept connection may wait, <c>"keepAlive"</c>; each null where it gives none.
    /// </summary>
    public int? RequestLine { get; init; }

    public int? RequestHeaders { get; init; }

    public double? RequestArrivalSeconds { get; init; }

    public double? KeepAliveSeconds { get; init; }

    /// <summary>
    /// A script's workers' limits under <c>weblingo</c> (section 9, "M0.26 parts", part 4): how many may run at once,
    /// <c>"workers"</c>; and each one's seconds, <c>"workerTimeout"</c>, instructions, <c>"workerInstructions"</c>, and
    /// megabytes of strings, <c>"workerMemory"</c>; each null where it gives none, the host's default then.
    /// </summary>
    public int? Workers { get; init; }

    /// <summary>
    /// A request's quantum, <c>"quantum"</c>, the instructions it runs before it yields its worker (section 9, "M0.24 parts",
    /// part 2), null where it gives none; and whether it says there is none, <c>"quantum": null</c>.
    /// </summary>
    public long? Quantum { get; init; }

    public bool NoQuantum { get; init; }

    public double? WorkerTimeoutSeconds { get; init; }

    public long? WorkerInstructions { get; init; }

    public long? WorkerMemoryMegabytes { get; init; }

    /// <summary>
    /// The megabytes the memory a site's requests share holds at most, <c>"shared"</c> (section 6.12, "Memory a site's requests
    /// share", A3 (a)), null where it gives none; and whether it says there is no most, <c>"shared": null</c>.
    /// </summary>
    public long? SharedMegabytes { get; init; }

    public bool NoSharedMost { get; init; }

    /// <summary>The shared memory's most, in bytes: what it gives, or 64 MB, or null for none.</summary>
    public long? SharedBytes => NoSharedMost ? null : SharedMegabytes is { } megabytes ? megabytes * 1024 * 1024 : DotNetSharedStore.DefaultMost;

    /// <summary>What a configuration that says nothing of them means: the host's defaults.</summary>
    public static WebLingoLimitSettings Default { get; } = new(null, null, null);
}

/// <summary>
/// The headers a site's every response has unless its page changes them, <c>wasp.config</c>'s <c>"headers"</c>
/// (specification, section 6.12, "Security headers"): <c>X-Content-Type-Options: nosniff</c>, where <see cref="NoSniff"/>;
/// its <c>Referrer-Policy</c>, where it has one; and <c>Content-Security-Policy: frame-ancestors</c> with its sources, where
/// it has them, and <c>X-Frame-Options</c> beside it, for browsers without <c>frame-ancestors</c>, <c>SAMEORIGIN</c> for
/// <c>'self'</c> alone and <c>DENY</c> for <c>'none'</c>, none for other sources, which it cannot name.
/// </summary>
public sealed record WebLingoHeaderSettings(bool NoSniff, string? ReferrerPolicy, IReadOnlyList<string>? FrameAncestors)
{
    /// <summary>
    /// What a configuration that says nothing of them means (the owner's answer, 25 November 2026, "Security Headers and
    /// Sensitive Files in WASP", A1 (b)): all four, <c>nosniff</c>, <c>strict-origin-when-cross-origin</c>, and frames of
    /// the site's own pages only.
    /// </summary>
    public static WebLingoHeaderSettings Default { get; } = new(true, "strict-origin-when-cross-origin", ["'self'"]);

    /// <summary>The headers, each a name and its value, in the order above.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers()
    {
        var headers = new List<KeyValuePair<string, string>>();
        if (NoSniff) headers.Add(new("X-Content-Type-Options", "nosniff"));
        if (ReferrerPolicy is { } policy) headers.Add(new("Referrer-Policy", policy));
        if (FrameAncestors is not { } ancestors) return headers;
        headers.Add(new("Content-Security-Policy", "frame-ancestors " + string.Join(' ', ancestors)));
        if (ancestors is ["'self'"]) headers.Add(new("X-Frame-Options", "SAMEORIGIN"));
        else if (ancestors is ["'none'"]) headers.Add(new("X-Frame-Options", "DENY"));
        return headers;
    }
}

/// <summary>
/// A site's log, <c>wasp.config</c>'s <c>"log"</c> (specification, section 6.12, "Logging"): the file it is written to, a
/// file a day, a full path, or null for the host's own log; and the lowest level written.
/// </summary>
public sealed record WebLingoLogSettings(string? File, WebLingoLogLevel Level)
{
    /// <summary>What a configuration that says nothing of it means: the host's log, from <c>info</c> on.</summary>
    public static WebLingoLogSettings Default { get; } = new(null, WebLingoLogLevel.Info);
}

/// <summary>
/// What a site's configuration says of a request's body (specification, section 6.12, "Uploaded files"): its largest, in
/// bytes; how much of a file a form uploads is kept in memory; the most its files may take in all, the body's largest where
/// it says nothing; and the folder their temporary files are made in, the system's temporary folder where it names none.
/// </summary>
public sealed record WebLingoUploadSettings(long MaxBodySize, int InMemory, long? MaxSize, string? Folder)
{
    /// <summary>What a configuration that says nothing of them means: 30,000,000 bytes, 64 KB in memory.</summary>
    public static WebLingoUploadSettings Default { get; } = new(30_000_000, 64 * 1024, null, null);
}

/// <summary>
/// Where a site keeps its sessions (specification, section 6.12, "Sessions and cookies"): <c>"memory"</c>, the process's,
/// by default, or <c>"database"</c>, a table, <see cref="Table"/>, <c>wasp_sessions</c> where it names none, of the database
/// <see cref="Database"/> names, made where it is missing where <see cref="Create"/>; how many minutes a session lasts once
/// no request uses it, 20 by default; and how many seconds a request waits for a session's lock, 30 by default.
/// </summary>
public sealed record WebLingoSessionSettings(double TimeoutMinutes, double LockTimeoutSeconds)
{
    /// <summary>What a configuration that says nothing of them means.</summary>
    public static WebLingoSessionSettings Default { get; } = new(20, 30);

    public string Store { get; init; } = "memory";
    public string? Database { get; init; }
    public string? Table { get; init; }
    public bool Create { get; init; }
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
