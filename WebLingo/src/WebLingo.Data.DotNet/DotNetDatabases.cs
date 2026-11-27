using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Data.DotNet;

/// <summary>
/// The platform's databases on .NET (specification, section 7.11), through ADO.NET's providers: SQLite's,
/// Microsoft.Data.Sqlite, <c>sqlite</c>, SQL Server's, Microsoft.Data.SqlClient, <c>sqlserver</c>, MySqlConnector,
/// <c>mysql</c>, and Npgsql, <c>postgresql</c>. A connection
/// string is its provider's name, a colon, and, for SQLite, a file's path, or the provider's own connection string, which
/// holds an <c>=</c>, <c>"sqlite:Data Source=shop.db;Mode=ReadOnly"</c>, <c>:memory:</c> being a database in memory; for
/// SQL Server, SqlClient's own, <c>"sqlserver:Server=localhost;Database=Todo;Integrated Security=true"</c>. A name the
/// configuration gives stands for its connection string, a relative path in which is from the configuration's folder,
/// <paramref name="folder"/>; one in a script's, from the program's current folder, or, given a site's file capabilities
/// (<see cref="Files"/>, section 7.13, "File capabilities"), checked as <c>std:fs</c>'s paths are, from the site's root, a
/// database opened to be written in a folder the site may write, one opened read-only (<c>Mode=ReadOnly</c>) in one it may
/// read, a <c>file:</c> URI refused, those the configuration names being trusted. SQLite makes a file that is not
/// there. A site's MySQL and PostgreSQL databases must be named in its configuration.
/// SqlClient uses its managed network layer on Windows too, as it does elsewhere,
/// its native one, under Microsoft's own terms, not shipped: it reaches a server by TCP/IP, never by its shared memory.
/// Each database it opens a pool's connections of, SQL Server's, MySQL's and PostgreSQL's, by the name or the connection
/// string a script gives, has a gate of its own (<see cref="Gate"/>), which counts the connections it holds against the
/// pool's size, its connection string's <c>Max Pool Size</c>, or MySqlConnector's <c>Maximum Pool Size</c>, 100 by default,
/// none where it says <c>Pooling=false</c>, learned when it first opens one, so that an open beyond them waits in its host's
/// queue, holding no thread (section 6.10; the owner's answer, 27 November 2026, A1 (a) of "SQL Server's Page at 256
/// Clients a Fifth as Fast as Before the Bound"). Two names of one connection string have a gate each, as two of these do,
/// a program's workers' among them: each counts its own, the pool's waiting then done in the pool, as before.
/// </summary>
public sealed class DotNetDatabases(IReadOnlyDictionary<string, WebLingoSecret> configured, string folder, IWebLingoSecrets? secrets = null)
    : IWebLingoDatabases
{
    /// <summary>The databases a configuration names, each by its connection string itself.</summary>
    public DotNetDatabases(IReadOnlyDictionary<string, string> configured, string folder) : this(Given(configured), folder) { }

    private static Dictionary<string, WebLingoSecret> Given(IReadOnlyDictionary<string, string> configured)
    {
        var given = new Dictionary<string, WebLingoSecret>(StringComparer.Ordinal);
        foreach (var (name, connection) in configured) given[name] = WebLingoSecret.Given(connection);
        return given;
    }

    /// <summary>The providers this platform has.</summary>
    public static readonly IReadOnlyList<string> Providers = ["sqlite", "sqlserver", "mysql", "postgresql"];

    static DotNetDatabases()
    {
        // Read once, when SqlClient first connects: its managed network layer on Windows, its native one not being there.
        AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);
    }

    /// <summary>The site's file capabilities, which a SQLite database a script names by its own connection string is checked by; none for any file.</summary>
    public IWebLingoFileCapabilities? Files { get; init; }

    private readonly ConcurrentDictionary<string, ConnectionGate> _gates = new(StringComparer.Ordinal);

    /// <summary>
    /// The gate of the database <paramref name="nameOrConnection"/> names, or describes: null for SQLite's, whose pool has no
    /// most, where that is known without reading a secret. Nothing is read, a configured name's secret not at all.
    /// </summary>
    public IWebLingoConnectionGate? Gate(string nameOrConnection) => GateOf(nameOrConnection);

    private ConnectionGate? GateOf(string nameOrConnection)
    {
        var known = nameOrConnection.Contains(':') ? nameOrConnection
            : configured.TryGetValue(nameOrConnection, out var found) && found.Source == WebLingoSecretSource.Given ? found.Text : null;
        if (known is not null && known.StartsWith("sqlite:", StringComparison.Ordinal)) return null;
        return _gates.GetOrAdd(nameOrConnection, _ => new ConnectionGate());
    }

    /// <summary>A connection, its place in its database's gate taken without waiting, as a script's blocking work takes one.</summary>
    public IWebLingoDatabaseConnection Open(string nameOrConnection)
    {
        var gate = GateOf(nameOrConnection);
        gate?.Enter();
        return Opened(nameOrConnection, gate);
    }

    public IWebLingoDatabaseConnection Open(string nameOrConnection, IWebLingoConnectionGate gate)
    {
        if (gate is not ConnectionGate own || !ReferenceEquals(GateOf(nameOrConnection), own))
        {
            throw new ArgumentException("The gate is not the one this database's Gate gave.", nameof(gate));
        }
        return Opened(nameOrConnection, own);
    }

    /// <summary>A connection opened in the place <paramref name="gate"/> holds for it, given back where it cannot be opened.</summary>
    private IWebLingoDatabaseConnection Opened(string nameOrConnection, ConnectionGate? gate)
    {
        try
        {
            return OpenedIn(nameOrConnection, gate);
        }
        catch
        {
            gate?.Leave();
            throw;
        }
    }

    private IWebLingoDatabaseConnection OpenedIn(string nameOrConnection, ConnectionGate? gate)
    {
        string connection;
        string? relativeTo = null;
        if (nameOrConnection.Contains(':')) connection = nameOrConnection;
        else if (configured.TryGetValue(nameOrConnection, out var found)) (connection, relativeTo) = (Read(nameOrConnection, found), folder);
        else throw Invalid($"'{nameOrConnection}' is not a database the configuration names");
        var colon = connection.IndexOf(':');
        if (colon < 0) throw Invalid("a configured database connection string must start with its provider and a colon");
        var (provider, rest) = (connection[..colon], connection[(colon + 1)..]);
        var (opened, most) = provider switch
        {
            "sqlite" => (Sqlite(rest, relativeTo, relativeTo is null ? Files : null), int.MaxValue),
            "sqlserver" => SqlServer(rest),
            "mysql" => MySql(rest, relativeTo is null && Files is not null),
            "postgresql" => PostgreSql(rest, relativeTo is null && Files is not null),
            _ => throw Invalid($"'{provider}' is not a database's provider WebLingo has, which are \"{string.Join("\", \"", Providers)}\"")
        };
        gate?.Learn(most); // Its pool's size, as its connection string has it now.
        try
        {
            opened.Open();
            return new Connection(provider, opened, gate);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            opened.Dispose();
            throw new WebLingoDatabaseException(WebLingoDatabaseErrorKind.Failed, $"the database cannot be opened: {Said(exception)}");
        }
    }

    /// <summary>
    /// A configured database's connection string, read where the configuration keeps it (section 7.17, "Secrets"), each
    /// time it is opened.
    /// </summary>
    private string Read(string name, WebLingoSecret secret)
    {
        if (secret.Source == WebLingoSecretSource.Given) return secret.Text;
        try
        {
            return (secrets ?? throw new WebLingoSecretException("this host reads no secrets")).Read(secret);
        }
        catch (WebLingoSecretException failure)
        {
            throw new WebLingoDatabaseException(WebLingoDatabaseErrorKind.Failed, $"the database '{name}''s connection string cannot be read: {failure.Message}");
        }
    }

    /// <summary>
    /// A SQLite connection, not yet open, to a file's path or by SQLite's own connection string, its file checked by
    /// <paramref name="files"/>, if given.
    /// </summary>
    private static SqliteConnection Sqlite(string rest, string? relativeTo, IWebLingoFileCapabilities? files)
    {
        SqliteConnectionStringBuilder builder;
        try
        {
            builder = rest.Contains('=') ? new SqliteConnectionStringBuilder(rest) : new SqliteConnectionStringBuilder { DataSource = rest };
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw Invalid($"'{rest}' is not a SQLite connection string: {exception.Message}");
        }
        if (builder.DataSource.Length == 0) throw Invalid("a SQLite database is a file's path, or :memory:");
        var inMemory = builder.DataSource == ":memory:" || builder.Mode == SqliteOpenMode.Memory;
        var uri = builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (files is not null && uri)
        {
            throw new WebLingoDatabaseException(WebLingoDatabaseErrorKind.NotAllowed,
                $"'{builder.DataSource}' cannot be opened: a site's script names a SQLite database by its path, not by a file: URI");
        }
        if (files is not null && !inMemory)
        {
            try
            {
                builder.DataSource = files.Resolve(builder.DataSource, write: builder.Mode != SqliteOpenMode.ReadOnly);
            }
            catch (WebLingoFileSystemException failure)
            {
                throw new WebLingoDatabaseException(failure.Kind == WebLingoFileErrorKind.NotAllowed ? WebLingoDatabaseErrorKind.NotAllowed
                    : WebLingoDatabaseErrorKind.Invalid, $"the database '{builder.DataSource}' cannot be opened: {failure.Message}");
            }
        }
        else if (builder.DataSource != ":memory:" && !uri)
        {
            builder.DataSource = relativeTo is null ? Path.GetFullPath(builder.DataSource) : Path.GetFullPath(builder.DataSource, relativeTo);
        }
        return new SqliteConnection(builder.ConnectionString);
    }

    /// <summary>
    /// A SQL Server connection, not yet open, by SqlClient's own connection string. It returns a <see cref="DbConnection"/>
    /// in a method of its own, so that .NET loads SqlClient, and what it needs, only for a script that opens SQL Server.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DbConnection, int) SqlServer(string rest)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(rest);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw Invalid($"'{rest}' is not a SQL Server connection string: {exception.Message}");
        }
        if (builder.DataSource.Length == 0) throw Invalid("a SQL Server connection string names its server, as Server=name does");
        return (new SqlConnection(builder.ConnectionString), builder.Pooling ? builder.MaxPoolSize : int.MaxValue);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DbConnection, int) MySql(string rest, bool siteSupplied)
    {
        if (siteSupplied)
        {
            throw new WebLingoDatabaseException(WebLingoDatabaseErrorKind.NotAllowed,
                "a site opens MySQL by a name in wasp.config");
        }
        try
        {
            var builder = new MySqlConnectionStringBuilder(rest);
            if (builder.Server.Length == 0) throw Invalid("a MySQL connection string names its server, as Server=name does");
            return (new MySqlConnection(builder.ConnectionString), builder.Pooling ? (int)Math.Min(builder.MaximumPoolSize, int.MaxValue) : int.MaxValue);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw Invalid($"invalid MySQL connection string: {exception.Message}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DbConnection, int) PostgreSql(string rest, bool siteSupplied)
    {
        if (siteSupplied)
        {
            throw new WebLingoDatabaseException(WebLingoDatabaseErrorKind.NotAllowed,
                "a site opens PostgreSQL by a name in wasp.config");
        }
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(rest);
            if (string.IsNullOrWhiteSpace(builder.Host)) throw Invalid("a PostgreSQL connection string names its server, as Host=name does");
            return (new NpgsqlConnection(builder.ConnectionString), builder.Pooling ? builder.MaxPoolSize : int.MaxValue);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw Invalid($"invalid PostgreSQL connection string: {exception.Message}");
        }
    }

    private static WebLingoDatabaseException Invalid(string message) => new(WebLingoDatabaseErrorKind.Invalid, message);

    /// <summary>
    /// A provider's message, its line breaks LF on every platform: Npgsql's, which adds its error's position, and any
    /// detail, on lines of their own, breaks them as the platform does, so that a script would read another text on Windows.
    /// </summary>
    private static string Said(Exception exception) => exception.Message.ReplaceLineEndings("\n");

    /// <summary>
    /// The connections this process holds of one database's pool, its places, counted against the pool's size, unknown,
    /// and so without a most, until a connection string has given it: one taken by a host's queue for an open that waits
    /// (<see cref="TryEnter"/>), or at once by an open that does not (<see cref="Enter"/>), and given back when the
    /// connection is closed, or not opened, each time telling those that wait.
    /// </summary>
    private sealed class ConnectionGate : IWebLingoConnectionGate
    {
        private readonly object _sync = new();
        private readonly List<Action> _freed = [];
        private int _most = int.MaxValue;
        private int _held;

        public int Room
        {
            get
            {
                lock (_sync) return _most == int.MaxValue ? int.MaxValue : Math.Max(0, _most - _held);
            }
        }

        public bool TryEnter()
        {
            lock (_sync)
            {
                if (_held >= _most) return false;
                _held++;
                return true;
            }
        }

        /// <summary>A place taken whatever the room, by an open that waits in the pool itself where there is none.</summary>
        public void Enter()
        {
            lock (_sync) _held++;
        }

        public void Leave()
        {
            lock (_sync) _held--;
            Tell();
        }

        /// <summary>The pool's size, as a connection string gives it, those waiting told where it grew.</summary>
        public void Learn(int most)
        {
            bool grew;
            lock (_sync)
            {
                grew = most > _most;
                _most = most;
            }
            if (grew) Tell();
        }

        public void WhenFreed(Action freed)
        {
            lock (_sync) _freed.Add(freed);
        }

        private void Tell()
        {
            Action[] told;
            lock (_sync) told = [.. _freed];
            foreach (var freed in told) freed();
        }
    }

    /// <summary>An open connection: one operation at a time, a transaction at most, each command given it; its gate's place given back when it is closed.</summary>
    private sealed class Connection(string provider, DbConnection connection, ConnectionGate? gate) : IWebLingoStreamingDatabaseConnection
    {
        private readonly object _gate = new();
        private DbTransaction? _transaction;
        private Cursor? _cursor;
        private bool _closed;

        public string Provider => provider;
        public string ServerVersion { get; } = connection.ServerVersion;

        public WebLingoDatabaseRows Query(string sql, IReadOnlyList<KeyValuePair<string, object?>> values, int? timeoutSeconds) => Run(() =>
        {
            using var command = Command(sql, values, timeoutSeconds);
            using var reader = command.ExecuteReader();
            var columns = Columns(reader);
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                rows.Add(ReadRow(reader, columns.Length));
            }
            return new WebLingoDatabaseRows(columns, rows);
        });

        public IWebLingoDatabaseCursor OpenRows(string sql, IReadOnlyList<KeyValuePair<string, object?>> values, int? timeoutSeconds)
            => Run(() =>
            {
                var command = Command(sql, values, timeoutSeconds);
                DbDataReader? reader = null;
                try
                {
                    reader = command.ExecuteReader();
                    return _cursor = new Cursor(this, command, reader, Columns(reader));
                }
                catch
                {
                    try { reader?.Dispose(); }
                    finally { command.Dispose(); }
                    throw;
                }
            });

        public long Execute(string sql, IReadOnlyList<KeyValuePair<string, object?>> values, int? timeoutSeconds) => Run(() =>
        {
            using var command = Command(sql, values, timeoutSeconds);
            return Math.Max(0L, command.ExecuteNonQuery());
        });

        public void Begin(string? isolation) => Run(() =>
        {
            if (_transaction is not null) throw Invalid("a transaction is open already: a connection's transactions do not nest");
            try
            {
                _transaction = connection.BeginTransaction(Isolation(isolation));
            }
            catch (ArgumentException exception)
            {
                throw Invalid($"{provider} does not take the isolation '{isolation}': {exception.Message}");
            }
            return true;
        });

        public void Commit() => Run(() =>
        {
            var transaction = _transaction ?? throw Invalid("no transaction is open to commit");
            _transaction = null;
            using (transaction) transaction.Commit();
            return true;
        });

        public void Rollback() => Run(() =>
        {
            var transaction = _transaction ?? throw Invalid("no transaction is open to roll back");
            _transaction = null;
            using (transaction) transaction.Rollback();
            return true;
        });

        public void Dispose()
        {
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                try { _cursor?.Dispose(); }
                finally
                {
                    try { _transaction?.Dispose(); }
                    finally
                    {
                        _transaction = null;
                        try { connection.Dispose(); }
                        finally { gate?.Leave(); }
                    }
                }
            }
        }

        /// <summary>An operation, one at a time, its provider's failures the database's, its script's mistakes WL3001's.</summary>
        private T Run<T>(Func<T> operation, bool readingCursor = false)
        {
            lock (_gate)
            {
                if (_closed) throw Invalid("the database is closed");
                if (!readingCursor && _cursor is not null) throw Invalid("a row cursor is open: finish or dispose it first");
                try
                {
                    return operation();
                }
                catch (DbException exception)
                {
                    throw new WebLingoDatabaseException(WebLingoDatabaseErrorKind.Failed, Said(exception));
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException
                                                  or InvalidCastException or OverflowException)
                {
                    throw Invalid(exception.Message);
                }
            }
        }

        private static string[] Columns(DbDataReader reader)
        {
            var columns = new string[reader.FieldCount];
            for (var i = 0; i < columns.Length; i++) columns[i] = reader.GetName(i);
            return columns;
        }

        private static object?[] ReadRow(DbDataReader reader, int count)
        {
            var row = new object?[count];
            for (var i = 0; i < row.Length; i++) row[i] = Value(reader, i);
            return row;
        }

        private sealed class Cursor(Connection owner, DbCommand command, DbDataReader reader, string[] columns)
            : IWebLingoDatabaseCursor
        {
            public IReadOnlyList<string> Columns => columns;

            public object?[]? Next() => owner.Run<object?[]?>(() =>
            {
                if (owner._cursor != this) return null;
                try
                {
                    if (reader.Read()) return ReadRow(reader, columns.Length);
                }
                catch
                {
                    Dispose();
                    throw;
                }
                Dispose();
                return null;
            }, readingCursor: true);

            public void Dispose()
            {
                lock (owner._gate)
                {
                    if (owner._cursor != this) return;
                    owner._cursor = null;
                    try { reader.Dispose(); }
                    finally { command.Dispose(); }
                }
            }
        }

        private DbCommand Command(string sql, IReadOnlyList<KeyValuePair<string, object?>> values, int? timeoutSeconds)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = _transaction;
            if (timeoutSeconds is { } seconds) command.CommandTimeout = seconds;
            foreach (var (name, value) in values)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@" + name;
                parameter.Value = value switch
                {
                    // Npgsql sends a date with an offset as timestamptz, which accepts DateTimeOffset values in UTC.
                    DateTimeOffset date when provider == "postgresql" => date.ToUniversalTime(),
                    // MySQL's DATETIME, DATE and TIMESTAMP hold no offset, and are read as a time on the server's clocks
                    // (Value): a date goes in so, where MySqlConnector would send its time in UTC.
                    DateTimeOffset date when provider == "mysql" => date.LocalDateTime,
                    null => DBNull.Value,
                    _ => value
                };
                command.Parameters.Add(parameter);
            }
            return command;
        }

        private static IsolationLevel Isolation(string? name) => name switch
        {
            null => IsolationLevel.Unspecified,
            "readUncommitted" => IsolationLevel.ReadUncommitted,
            "readCommitted" => IsolationLevel.ReadCommitted,
            "repeatableRead" => IsolationLevel.RepeatableRead,
            "serializable" => IsolationLevel.Serializable,
            "snapshot" => IsolationLevel.Snapshot,
            _ => throw Invalid($"'{name}' is not an isolation")
        };

        /// <summary>
        /// A column's value as one the contract allows. SQLite's are an integer, a real, text or a blob, a column declared
        /// a decimal (<c>DECIMAL</c>, <c>NUMERIC</c>, <c>MONEY</c>) read as a decimal, a Boolean (<c>BOOL</c>) as one, and a date
        /// (<c>DATE</c>, <c>DATETIME</c>, <c>TIMESTAMP</c>) as one where its text reads as a date; another provider's as
        /// its type says, SQL Server's <c>BIT</c> a Boolean, a date without an offset (<c>DATE</c>, <c>DATETIME2</c>) one
        /// on the server's clocks, a <c>UNIQUEIDENTIFIER</c> or a <c>TIME</c> its text.
        /// </summary>
        private static object? Value(DbDataReader reader, int i)
        {
            if (reader.IsDBNull(i)) return null;
            var declared = reader.GetDataTypeName(i).ToUpperInvariant();
            if (reader is SqliteDataReader)
            {
                if (declared.Contains("DECIMAL") || declared.Contains("NUMERIC") || declared.Contains("MONEY")) return reader.GetDecimal(i);
                if (declared.Contains("BOOL")) return reader.GetInt64(i) != 0;
                if ((declared.Contains("DATE") || declared.Contains("TIMESTAMP")) && reader.GetValue(i) is string text
                    && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
                {
                    return date;
                }
            }
            return reader.GetValue(i) switch
            {
                bool b => b,
                long l => l,
                int n => (long)n,
                short s => (long)s,
                byte b => (long)b,
                sbyte b => (long)b,
                ushort u => (long)u,
                uint u => (long)u,
                ulong u when u <= long.MaxValue => (long)u,
                ulong u => (decimal)u,
                double d => d,
                float f => (double)f,
                decimal m => m,
                string s => s,
                byte[] bytes => bytes,
                DateTimeOffset date => date,
                DateTime date => new DateTimeOffset(DateTime.SpecifyKind(date, date.Kind == DateTimeKind.Unspecified ? DateTimeKind.Local : date.Kind)),
                DateOnly date => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)),
                var other => Convert.ToString(other, CultureInfo.InvariantCulture)
            };
        }
    }
}
