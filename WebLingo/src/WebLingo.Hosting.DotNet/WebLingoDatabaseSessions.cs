using System.Globalization;
using System.Text;
using System.Text.Json;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
namespace WebLingo.Hosting.DotNet;

/// <summary>
/// A site's sessions in a database (specification, section 6.12, "Sessions and cookies"; "Sessions and cookies, parts",
/// part 3): a table of the database the site's configuration names, made by its administrator, with
/// <see cref="CreateStatements"/>, which <c>wasp sessions create</c> runs or prints, or, where the configuration says
/// <c>"create": true</c>, by the store where it is missing. A row for each session, its id, its values, a JSON object of
/// each name's JSON text, when a request last used it, and, while a request holds its lock, when the lock ends unless its
/// holder renews it, every 20 seconds while it holds it, so that a lock a process that ended held ends within a minute;
/// and a row of its own, <c>schema</c>, the table's schema, which the store checks before it reads anything, an older or a
/// later table reported, never changed. Every process serving the site shares it, with the database's own locks: a
/// request takes a session's lock by an update that only a free lock lets through, and one whose session another holds
/// reads it again every 100 ms, off its worker, for the site's lock timeout at most. Times are the server's clock's, in
/// milliseconds since 1970, UTC. Opening a session and writing it back run off the worker; making, renewing and ending one,
/// which a page does by a member that gives no task, on it.
/// </summary>
public sealed class WebLingoDatabaseSessions : IWebLingoSessionStore
{
    /// <summary>The table's schema this WASP reads and makes; a later one adds <c>wasp sessions upgrade</c>.</summary>
    public const int Schema = 1;

    /// <summary>The id of the row that holds the table's schema, which no session's can be.</summary>
    public const string SchemaId = "schema";

    /// <summary>The table's name where the configuration names none.</summary>
    public const string DefaultTable = "wasp_sessions";

    /// <summary>How long a lock lasts unless its holder renews it.</summary>
    public static readonly TimeSpan LockLife = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Renewal = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);
    private static readonly JsonDocumentOptions Reading = new() { MaxDepth = 1024 };

    private readonly IWebLingoDatabases _databases;
    private readonly string _database;
    private readonly string _table;
    private readonly bool _create;
    private readonly Action<string>? _log;
    private readonly Func<long> _now;
    private readonly object _gate = new();
    private readonly object _readying = new();
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private Timer? _renewing;
    private bool _ready;
    private long _swept;

    /// <summary>
    /// A store in the table <paramref name="table"/> of the database <paramref name="database"/>, which
    /// <paramref name="databases"/> opens by its name, made where it is missing where <paramref name="create"/>; its
    /// sessions ending once unused for <paramref name="timeout"/>, a request waiting for a lock for
    /// <paramref name="lockTimeout"/> at most; what fails as a session is written back told to <paramref name="log"/>; the
    /// time read from <paramref name="now"/>, milliseconds since 1970, the system's clock by default.
    /// </summary>
    public WebLingoDatabaseSessions(IWebLingoDatabases databases, string database, string table, bool create, TimeSpan timeout,
        TimeSpan lockTimeout, Action<string>? log = null, Func<long>? now = null)
    {
        if (!IsTable(table)) throw new ArgumentException($"'{table}' is not a table's name: letters, digits and _, a letter or _ first, 58 at most.");
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lockTimeout, TimeSpan.Zero);
        (_databases, _database, _table, _create, Timeout, LockTimeout, _log) = (databases, database, table, create, timeout, lockTimeout, log);
        _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>How long a session lasts once no request uses it.</summary>
    public TimeSpan Timeout { get; }

    public TimeSpan LockTimeout { get; }

    /// <summary>Whether a text may name the table: letters, digits and <c>_</c>, a letter or <c>_</c> first, 58 at most, so that its index's name, <c>_used</c> after it, is 64.</summary>
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
    /// The statements that make the table of sessions <paramref name="table"/> in a database of <paramref name="provider"/>'s,
    /// <c>sqlite</c>, <c>sqlserver</c>, <c>mysql</c> or <c>postgresql</c>: the table, an index of when its sessions were
    /// last used, and its schema's row.
    /// </summary>
    public static IReadOnlyList<string> CreateStatements(string provider, string table)
    {
        var columns = provider switch
        {
            "sqlite" => "id TEXT NOT NULL PRIMARY KEY, data TEXT NOT NULL, used INTEGER NOT NULL, locked_until INTEGER NULL",
            "sqlserver" => "id VARCHAR(32) NOT NULL PRIMARY KEY, data NVARCHAR(MAX) NOT NULL, used BIGINT NOT NULL, locked_until BIGINT NULL",
            "mysql" => "id VARCHAR(32) NOT NULL PRIMARY KEY, data LONGTEXT NOT NULL, used BIGINT NOT NULL, locked_until BIGINT NULL",
            "postgresql" => "id VARCHAR(32) NOT NULL PRIMARY KEY, data TEXT NOT NULL, used BIGINT NOT NULL, locked_until BIGINT NULL",
            _ => throw new ArgumentException($"'{provider}' is not a database's provider WASP keeps sessions in.")
        };
        var after = provider == "mysql" ? " CHARACTER SET utf8mb4 COLLATE utf8mb4_bin" : "";
        return
        [
            $"CREATE TABLE {table} ({columns}){after}",
            $"CREATE INDEX {table}_used ON {table} (used)",
            $"INSERT INTO {table} (id, data, used, locked_until) VALUES ('{SchemaId}', '{{\"schema\": {Schema}}}', {long.MaxValue}, NULL)"
        ];
    }

    /// <summary>The table of sessions <paramref name="table"/> made through <paramref name="connection"/>, in one transaction where the database's statements of tables take part in one.</summary>
    public static void MakeTable(IWebLingoDatabaseConnection connection, string table)
    {
        connection.Begin(null);
        foreach (var statement in CreateStatements(connection.Provider, table)) connection.Execute(statement, [], null);
        connection.Commit();
    }

    /// <summary>
    /// The table's schema, from its schema's row, read through <paramref name="connection"/>; a
    /// <see cref="WebLingoDatabaseException"/> where the table cannot be read, as where it is missing, and a
    /// <see cref="WebLingoSessionStoreException"/> where it is not WASP's.
    /// </summary>
    public static int SchemaOf(IWebLingoDatabaseConnection connection, string table, string database)
    {
        var rows = connection.Query($"SELECT data FROM {table} WHERE id = @id", [new("id", SchemaId)], null);
        if (rows.Rows.Count == 1 && rows.Rows[0][0] is string data)
        {
            try
            {
                using var document = JsonDocument.Parse(data);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("schema", out var schema)
                    && schema.TryGetInt32(out var version) && version >= 1) return version;
            }
            catch (JsonException)
            {
                // Not WASP's, as below.
            }
        }
        throw new WebLingoSessionStoreException($"the table '{table}' of the database '{database}' is not a table of sessions WASP made: "
                                                + $"it has no row '{SchemaId}' giving its schema");
    }

    public WebLingoSessionOpening Open(string id, bool locked, Action<WebLingoSessionOpening> later)
    {
        var deadline = _now() + (long)LockTimeout.TotalMilliseconds;
        // Its database's work on WebLingo's own threads, as a script's is, never .NET's pool (DotNetBlockingPool).
        DotNetBlockingPool.Run(() => Try(id, locked, deadline, later));
        return WebLingoSessionOpening.Waiting;
    }

    /// <summary>Off the worker: the session opened, its lock taken; or, another request holding it, tried again in 100 ms.</summary>
    private void Try(string id, bool locked, long deadline, Action<WebLingoSessionOpening> later)
    {
        WebLingoSessionOpening opening;
        try
        {
            opening = TryOpen(id, locked);
        }
        catch (WebLingoSessionStoreException failure)
        {
            opening = WebLingoSessionOpening.Failed(failure.Message);
        }
        if (opening.Kind != WebLingoSessionOpened.Waiting)
        {
            later(opening);
            return;
        }
        if (_now() >= deadline)
        {
            later(WebLingoSessionOpening.TimedOut);
            return;
        }
        // Its next try on the blocking pool too, the timer's continuation only handing it on.
        Task.Delay(Poll).ContinueWith(_ => DotNetBlockingPool.Run(() => Try(id, locked, deadline, later)), TaskScheduler.Default);
    }

    private WebLingoSessionOpening TryOpen(string id, bool locked) => Run(connection =>
    {
        var now = _now();
        var alive = now - (long)Timeout.TotalMilliseconds;
        Sweep(connection, now, alive);
        if (!locked)
        {
            var read = connection.Query($"SELECT data, used FROM {_table} WHERE id = @id", [new("id", id)], null);
            if (read.Rows.Count == 0 || Long(read.Rows[0][1]) <= alive) return WebLingoSessionOpening.NotFound;
            connection.Execute($"UPDATE {_table} SET used = @now WHERE id = @id AND used < @now", [new("id", id), new("now", now)], null);
            return new WebLingoSessionOpening(WebLingoSessionOpened.Found, Values((string)read.Rows[0][0]!));
        }
        var taken = connection.Execute($"UPDATE {_table} SET locked_until = @until, used = @now WHERE id = @id AND used > @alive "
                                       + "AND (locked_until IS NULL OR locked_until < @now)",
            [new("until", now + (long)LockLife.TotalMilliseconds), new("now", now), new("id", id), new("alive", alive)], null);
        if (taken == 1)
        {
            var rows = connection.Query($"SELECT data FROM {_table} WHERE id = @id", [new("id", id)], null);
            if (rows.Rows.Count == 0) return WebLingoSessionOpening.NotFound;
            Hold(id);
            return new WebLingoSessionOpening(WebLingoSessionOpened.Found, Values((string)rows.Rows[0][0]!));
        }
        var found = connection.Query($"SELECT used FROM {_table} WHERE id = @id", [new("id", id)], null);
        return found.Rows.Count == 0 || Long(found.Rows[0][0]) <= alive ? WebLingoSessionOpening.NotFound : WebLingoSessionOpening.Waiting;
    });

    public bool Create(string id) => Run(connection =>
    {
        var now = _now();
        try
        {
            connection.Execute($"INSERT INTO {_table} (id, data, used, locked_until) VALUES (@id, @data, @now, @until)",
                [new("id", id), new("data", "{}"), new("now", now), new("until", now + (long)LockLife.TotalMilliseconds)], null);
        }
        catch (WebLingoDatabaseException) when (connection.Query($"SELECT id FROM {_table} WHERE id = @id", [new("id", id)], null).Rows.Count == 1)
        {
            return false; // An id another session has: the caller makes another.
        }
        Hold(id);
        return true;
    });

    public bool Close(string id, IReadOnlyDictionary<string, string>? values, Action done)
    {
        Let(id);
        DotNetBlockingPool.Run(() =>
        {
            try
            {
                Run(connection => connection.Execute(values is null
                        ? $"UPDATE {_table} SET used = @now, locked_until = NULL WHERE id = @id"
                        : $"UPDATE {_table} SET data = @data, used = @now, locked_until = NULL WHERE id = @id",
                    values is null ? [new("now", _now()), new("id", id)] : [new("data", Data(values)), new("now", _now()), new("id", id)], null));
            }
            catch (WebLingoSessionStoreException failure)
            {
                _log?.Invoke($"A session could not be written back: {failure.Message}.");
            }
            done();
        });
        return false;
    }

    public bool Renew(string id, string renewed) => Run(connection =>
    {
        try
        {
            connection.Execute($"UPDATE {_table} SET id = @renewed WHERE id = @id", [new("renewed", renewed), new("id", id)], null);
        }
        catch (WebLingoDatabaseException) when (connection.Query($"SELECT id FROM {_table} WHERE id = @id", [new("id", renewed)], null).Rows.Count == 1)
        {
            return false;
        }
        Let(id);
        Hold(renewed);
        return true;
    });

    public void Remove(string id)
    {
        Run(connection => connection.Execute($"DELETE FROM {_table} WHERE id = @id", [new("id", id)], null));
        Let(id);
    }

    /// <summary>
    /// What a statement gives, through a connection of the database's own, the table checked first, made where it is missing
    /// and the configuration says so; its failure a <see cref="WebLingoSessionStoreException"/>.
    /// </summary>
    private T Run<T>(Func<IWebLingoDatabaseConnection, T> work)
    {
        try
        {
            using var connection = _databases.Open(_database);
            Ready(connection);
            return work(connection);
        }
        catch (Exception failure) when (failure is WebLingoDatabaseException or WebLingoSecretException)
        {
            throw new WebLingoSessionStoreException($"the database '{_database}' fails: {failure.Message}");
        }
    }

    /// <summary>The table checked once, its schema this WASP's, or made where it is missing and the configuration says so.</summary>
    private void Ready(IWebLingoDatabaseConnection connection)
    {
        // One request at a time checks it, or makes it, the others waiting, so that two never make it at once.
        lock (_readying)
        {
            if (!_ready) Check(connection);
        }
    }

    private void Check(IWebLingoDatabaseConnection connection)
    {
        int schema;
        try
        {
            schema = SchemaOf(connection, _table, _database);
        }
        catch (WebLingoDatabaseException missing)
        {
            if (!_create)
            {
                throw new WebLingoSessionStoreException($"the database '{_database}' has no table of sessions '{_table}', or cannot read it "
                                                        + $"({missing.Message.TrimEnd('.')}): make it with 'wasp sessions create --database {_database}'");
            }
            MakeTable(connection, _table);
            schema = Schema;
        }
        if (schema != Schema)
        {
            throw new WebLingoSessionStoreException($"the table of sessions '{_table}' of the database '{_database}' was made by a later WASP, "
                                                    + $"its schema {schema}, where this WASP reads schema {Schema}");
        }
        _ready = true;
    }

    /// <summary>The sessions that have ended removed, once a minute at most, by this process.</summary>
    private void Sweep(IWebLingoDatabaseConnection connection, long now, long alive)
    {
        lock (_gate)
        {
            if (now - _swept < 60_000) return;
            _swept = now;
        }
        connection.Execute($"DELETE FROM {_table} WHERE used <= @alive AND (locked_until IS NULL OR locked_until < @now)",
            [new("alive", alive), new("now", now)], null);
    }

    /// <summary>A lock this process holds, renewed while it holds it.</summary>
    private void Hold(string id)
    {
        lock (_gate)
        {
            _held.Add(id);
            _renewing ??= new Timer(_ => RenewLocks(), null, Renewal, Renewal);
        }
    }

    /// <summary>A lock this process holds no more.</summary>
    private void Let(string id)
    {
        lock (_gate)
        {
            if (!_held.Remove(id) || _held.Count > 0) return;
            _renewing?.Dispose();
            _renewing = null;
        }
    }

    /// <summary>The locks this process holds, renewed, each to last another minute; what fails told to the log.</summary>
    private void RenewLocks()
    {
        string[] held;
        lock (_gate) held = [.. _held];
        foreach (var id in held)
        {
            try
            {
                Run(connection => connection.Execute($"UPDATE {_table} SET locked_until = @until WHERE id = @id AND locked_until IS NOT NULL",
                    [new("until", _now() + (long)LockLife.TotalMilliseconds), new("id", id)], null));
            }
            catch (WebLingoSessionStoreException failure)
            {
                _log?.Invoke($"A session's lock could not be renewed: {failure.Message}.");
            }
        }
    }

    /// <summary>A session's values as its row holds them: a JSON object, each name's JSON text.</summary>
    private static string Data(IReadOnlyDictionary<string, string> values)
    {
        var text = new StringBuilder("{");
        foreach (var (name, json) in values)
        {
            if (text.Length > 1) text.Append(',');
            text.Append(JsonSerializer.Serialize(name)).Append(':').Append(json);
        }
        return text.Append('}').ToString();
    }

    /// <summary>A row's values, each name's JSON text as it was written.</summary>
    private static Dictionary<string, string> Values(string data)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(data, Reading);
            foreach (var property in document.RootElement.EnumerateObject()) values[property.Name] = property.Value.GetRawText();
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException)
        {
            throw new WebLingoSessionStoreException($"a session's values are not a JSON object: {failure.Message}");
        }
        return values;
    }

    private static long Long(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);
}
