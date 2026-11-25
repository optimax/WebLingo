using System.Text.Json;
using WebLingo.Bytecode;
using WebLingo.Platform.Abstractions;
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
            out databases, out files, out allowed, out env, out mail, out _, out _, out _, out _, out error);

    /// <summary>
    /// The configuration in <paramref name="folder"/>, its file <paramref name="configuration"/>: its schemes, the command
    /// line's given over them, its mode, <c>development</c>, <c>production</c>, or null if it names none, and its send
    /// timeout, in seconds, or null if it gives none; false, with why, otherwise.
    /// </summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? mode, out double? sendTimeout, out string? error) =>
        TryLoadFolder(folder, configuration, given, out schemes, out mode, out sendTimeout, out _, out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out error);

    /// <summary>
    /// As the other, and the hosts the configuration allows its scripts to reach, none when it names none, its databases,
    /// each name's connection string, and the folders its scripts may read and write, none when it names none.
    /// </summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? mode, out double? sendTimeout, out IReadOnlyList<string> allowedHosts,
        out IReadOnlyDictionary<string, WebLingoSecret> databases, out WebLingoFileFolders files, out string? error) =>
        TryLoadFolder(folder, configuration, given, out schemes, out mode, out sendTimeout, out allowedHosts, out databases, out files,
            out _, out _, out _, out _, out _, out _, out _, out error);

    /// <summary>
    /// As the other, and what its settings allow native modules, <c>"com"</c>'s list, by its setting's name, and what its
    /// scripts may read of the machine's environment, nothing when it says nothing, its mail servers, none when it names
    /// none, what it says of a request's body, where it keeps its sessions, a request's limits, and its log, a site's only.
    /// </summary>
    public static bool TryLoadFolder(string folder, string configuration, IReadOnlyDictionary<string, string> given,
        out Dictionary<string, string> schemes, out string? mode, out double? sendTimeout, out IReadOnlyList<string> allowedHosts,
        out IReadOnlyDictionary<string, WebLingoSecret> databases, out WebLingoFileFolders files, out IReadOnlyDictionary<string, IReadOnlyList<string>> allowed,
        out WebLingoEnvSettings env, out IReadOnlyDictionary<string, WebLingoMailServer> mail, out WebLingoUploadSettings uploads,
        out WebLingoSessionSettings sessions, out WebLingoLimitSettings limits, out WebLingoLogSettings log, out string? error)
    {
        limits = WebLingoLimitSettings.Default;
        log = WebLingoLogSettings.Default;
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
        if (File.Exists(file) && !TryRead(file, folder, schemes, ref mode, ref sendTimeout, hosts, named, read, write,
            allowing, ref env, servers, ref uploads, ref sessions, ref limits, ref log, out error))
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
    /// <c>"limits": { "requestTimeout": 60, "requestInstructions": 1000000000, "requestMemory": 128 }</c>, each optional: how
    /// many seconds a request may take, its waits included, a number more than 0; how many instructions it may run, an
    /// integer more than 0; and how many megabytes of strings its heap may hold, an integer from 1 to 1,048,576 (section
    /// 6.12, "Limits"). Each it does not give is its host's default.
    /// </summary>
    private static bool TryReadLimits(string file, JsonElement setting, ref WebLingoLimitSettings limits, out string? error)
    {
        error = $"{file}: \"limits\" is an object, {{ \"requestTimeout\": 60, \"requestInstructions\": 1000000000, \"requestMemory\": 128 }}, "
                + "each optional, \"requestTimeout\" a number of seconds more than 0, \"requestInstructions\" an integer more than 0, and "
                + "\"requestMemory\" a number of megabytes from 1 to 1048576.";
        if (setting.ValueKind != JsonValueKind.Object) return false;
        foreach (var entry in setting.EnumerateObject())
        {
            var number = entry.Value.ValueKind == JsonValueKind.Number;
            if (entry.Name == "requestTimeout" && number && entry.Value.TryGetDouble(out var seconds) && seconds > 0
                && seconds <= TimeSpan.MaxValue.TotalSeconds / 2)
            {
                limits = limits with { TimeoutSeconds = seconds };
            }
            else if (entry.Name == "requestInstructions" && number && entry.Value.TryGetInt64(out var instructions) && instructions > 0)
            {
                limits = limits with { Instructions = instructions };
            }
            else if (entry.Name == "requestMemory" && number && entry.Value.TryGetInt64(out var megabytes) && megabytes is >= 1 and <= 1_048_576)
            {
                limits = limits with { MemoryMegabytes = megabytes };
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
        out string? error)
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
                            + "\"sessions\", \"limits\" and \"log\".";
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
/// take, its waits included; how many instructions it may run; and how many megabytes of strings its heap may hold, two
/// bytes a unit. Each null where it says nothing of it, its host's default then.
/// </summary>
public sealed record WebLingoLimitSettings(double? TimeoutSeconds, long? Instructions, long? MemoryMegabytes)
{
    /// <summary>What a configuration that says nothing of them means: the host's defaults.</summary>
    public static WebLingoLimitSettings Default { get; } = new(null, null, null);
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
