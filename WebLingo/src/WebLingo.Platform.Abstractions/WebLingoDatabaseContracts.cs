namespace WebLingo.Platform.Abstractions;

/// <summary>
/// The platform's databases (specification, sections 7.11 and 7.13, WL-P011), which <c>std:db</c> reaches: a connection
/// opened by a name the host's configuration gives, <c>"databases": { "fish": "sqlite:../data/fish.db" }</c>, or by a
/// connection string a script gives, its provider's name, a colon, and what that provider reads,
/// <c>"sqlite:C:/data/fish.db"</c>. Each operation blocks, and <c>std:db</c> runs it off the worker.
/// </summary>
public interface IWebLingoDatabases
{
    /// <summary>
    /// A connection to the database the configuration names <paramref name="nameOrConnection"/>, or, when it holds a colon,
    /// to the one it describes; a <see cref="WebLingoDatabaseException"/> for a name the configuration does not give, a
    /// provider the platform does not have, or a database that cannot be opened.
    /// </summary>
    IWebLingoDatabaseConnection Open(string nameOrConnection);
}

/// <summary>
/// An open connection to a database. Its values, going in and coming out, are <c>null</c>, <see cref="bool"/>,
/// <see cref="long"/>, <see cref="double"/>, <see cref="decimal"/>, <see cref="string"/>, a <see cref="byte"/> array, and
/// <see cref="DateTimeOffset"/>; a statement's values are named, each name without its <c>@</c>. Its operations may be
/// called from any thread, one at a time, a second waiting for the first; <see cref="IDisposable.Dispose"/> closes it,
/// once, a transaction still open rolled back.
/// </summary>
public interface IWebLingoDatabaseConnection : IDisposable
{
    /// <summary>Its provider's name, <c>"sqlite"</c>.</summary>
    string Provider { get; }

    /// <summary>The database's version, as its provider gives it, <c>"3.46.1"</c>.</summary>
    string ServerVersion { get; }

    /// <summary>A statement's rows: its columns' names, and each row's values, in their order.</summary>
    WebLingoDatabaseRows Query(string sql, IReadOnlyList<KeyValuePair<string, object?>> values, int? timeoutSeconds);

    /// <summary>A statement run for what it changes: the number of rows it changed.</summary>
    long Execute(string sql, IReadOnlyList<KeyValuePair<string, object?>> values, int? timeoutSeconds);

    /// <summary>
    /// Begins a transaction, its isolation named as the options name it (<c>"serializable"</c>), or the provider's own;
    /// one at a time, transactions not nesting.
    /// </summary>
    void Begin(string? isolation);

    /// <summary>Commits the open transaction.</summary>
    void Commit();

    /// <summary>Rolls the open transaction back.</summary>
    void Rollback();
}

/// <summary>A query's result: its columns' names, and its rows, each a value for each column.</summary>
public sealed record WebLingoDatabaseRows(IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows);

/// <summary>
/// What a database's operation failed of: something the script asked for that cannot be, a name no configuration gives, a
/// provider that is not there, an option or a value the provider does not take, a transaction that is not open (WL3001);
/// or the database's own failure, a statement it refuses, a file it cannot open (WL3015, <c>DatabaseError</c>); or a
/// database's file a site's scripts may not reach (WL3016, <c>PermissionError</c>, section 7.13, "File capabilities").
/// </summary>
public enum WebLingoDatabaseErrorKind { Invalid, Failed, NotAllowed }

/// <summary>A database's failure, its message for a script to read, without a capital or a full stop.</summary>
public sealed class WebLingoDatabaseException(WebLingoDatabaseErrorKind kind, string message) : Exception(message)
{
    public WebLingoDatabaseErrorKind Kind { get; } = kind;
}
