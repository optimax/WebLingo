using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Db;

/// <summary>
/// <c>std:db</c>, a standard module (specification, sections 3.9 and 7.11), a native module installed with the runtime in
/// the <c>std</c> scheme's folder as <c>db.dll</c>: databases through the platform's (<see cref="WebLingoHeap.Databases"/>),
/// one module for every database, the SQL passed on as it is. Its class <c>Database</c>: <c>Database.open(name)</c>, a
/// task of a connection to the database the configuration names, or to the one a connection string describes,
/// <c>"sqlite:C:/data/fish.db"</c>; its <c>query(sql, values, options)</c>, a task of an array of maps, a map for each
/// row, from each column's name to its value; its <c>execute(sql, values, options)</c>, a task of the number of rows it
/// changed; its <c>transaction(work, options)</c>, written in WebLingo, which begins a transaction, awaits the function
/// <c>work</c>, and commits, or rolls back where <c>work</c> throws, throwing on; <c>begin(options)</c>, <c>commit()</c>
/// and <c>rollback()</c>, its steps; <c>provider</c> and <c>serverVersion</c>; and <c>dispose()</c>, which closes it.
/// A statement's values are a map of names to values, each <c>@name</c> in its SQL; its options a map, <c>timeout</c>,
/// in seconds, a transaction's <c>isolation</c>. Each operation runs off the worker. What cannot be asked for, a name no
/// configuration gives, an option or a value the provider does not take, is WL3001; the database's own failure a
/// <c>DatabaseError</c> (WL3015).
/// </summary>
public sealed class WebLingoDbModule : IWebLingoNativeModule
{
    /// <summary>The isolation levels a transaction's options may name.</summary>
    private static readonly string[] Isolations = ["readUncommitted", "readCommitted", "repeatableRead", "serializable", "snapshot"];

    /// <summary><c>transaction</c>, written in WebLingo, since it calls the script's function and awaits it (section 3.9).</summary>
    private const string TransactionSource =
        "async transaction(work, options = null) {\n" +
        "  await this.begin(options);\n" +
        "  var result = null;\n" +
        "  try {\n" +
        "    result = await work();\n" +
        "  } catch (e) {\n" +
        "    await this.rollback();\n" +
        "    throw;\n" +
        "  }\n" +
        "  await this.commit();\n" +
        "  return result;\n" +
        "}\n";

    public void Register(IWebLingoNativeRegistry registry)
    {
        var database = registry.Class("Database");
        database.DeclareFactory("open", 1, 0, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            var classValue = a[0];
            if (a[1].Kind != WebLingoValueKind.String || heap.GetString(a[1]).Length == 0)
            {
                error = Wrong($"A database is named by the configuration's name for it, or by a connection string, \"sqlite:C:/data/shop.db\", not {WebLingoValueFormatter.Describe(a[1], heap)}.");
                return false;
            }
            if (!TryServices(heap, out var databases, out var scheduler, out error)) return false;
            var named = heap.GetString(a[1]);
            return scheduler.TryRunOffWorker(() => databases.Open(named),
                (IWebLingoDatabaseConnection connection, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? wrong) =>
                {
                    if (database.TryMake(classValue, h, out made, out wrong) && database.TrySetHostObject(made, connection, h, out wrong)) return true;
                    connection.Dispose();
                    return false;
                }, Failed, out result, out error);
        });
        database.DeclareMethod("query", 3, 2, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!TryStatement(database, "query", a, heap, out var connection, out var sql, out var values, out var timeout, out var scheduler, out error))
            {
                return false;
            }
            return scheduler.TryRunOffWorker(() => connection.Query(sql, values, timeout), Rows, Failed, out result, out error);
        });
        database.DeclareMethod("execute", 3, 2, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!TryStatement(database, "execute", a, heap, out var connection, out var sql, out var values, out var timeout, out var scheduler, out error))
            {
                return false;
            }
            return scheduler.TryRunOffWorker(() => connection.Execute(sql, values, timeout),
                (long changed, WebLingoHeap h, out WebLingoValue count, out WebLingoRuntimeError? wrong) =>
                {
                    (count, wrong) = (WebLingoValue.FromInt64(changed), null);
                    return true;
                }, Failed, out result, out error);
        });
        database.DeclareMethod("begin", 1, 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!database.TryGetHostObject<IWebLingoDatabaseConnection>(a[0], heap, out var connection, out error)
                || !TryServices(heap, out _, out var scheduler, out error) || !TryOptions(a[1], "begin", heap, out _, out var isolation, out error))
            {
                return false;
            }
            return scheduler.TryRunOffWorker(() =>
            {
                connection.Begin(isolation);
                return true;
            }, Nothing, Failed, out result, out error);
        });
        foreach (var (name, commit) in new[] { ("commit", true), ("rollback", false) })
        {
            database.DeclareMethod(name, 0, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            {
                result = WebLingoValue.Null;
                if (!database.TryGetHostObject<IWebLingoDatabaseConnection>(a[0], heap, out var connection, out error)
                    || !TryServices(heap, out _, out var scheduler, out error))
                {
                    return false;
                }
                return scheduler.TryRunOffWorker(() =>
                {
                    if (commit) connection.Commit();
                    else connection.Rollback();
                    return true;
                }, Nothing, Failed, out result, out error);
            });
        }
        database.DeclareMethod("dispose", 0, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            // Closed once: disposing again does nothing (section 3.4).
            result = WebLingoValue.Null;
            if (!database.TryReleaseHostObject(a[0], heap, out var held, out error)) return false;
            (held as IDisposable)?.Dispose();
            return true;
        });
        database.DeclareProperty("provider", (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            return database.TryGetHostObject<IWebLingoDatabaseConnection>(a[0], heap, out var connection, out error)
                   && heap.TryAllocateString(connection.Provider, out result, out error);
        });
        database.DeclareProperty("serverVersion", (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            return database.TryGetHostObject<IWebLingoDatabaseConnection>(a[0], heap, out var connection, out error)
                   && heap.TryAllocateString(connection.ServerVersion, out result, out error);
        });
        database.DeclareWebLingoMethods(TransactionSource);
    }

    /// <summary>A statement's connection, text, values and timeout, and the scheduler to run it with.</summary>
    private static bool TryStatement(WebLingoNativeClass database, string method, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap,
        out IWebLingoDatabaseConnection connection, out string sql, out IReadOnlyList<KeyValuePair<string, object?>> values, out int? timeout,
        out WebLingoScheduler scheduler, out WebLingoRuntimeError? error)
    {
        (connection, sql, values, timeout, scheduler) = (null!, "", [], null, null!);
        if (!database.TryGetHostObject<IWebLingoDatabaseConnection>(a[0], heap, out var open, out error)) return false;
        connection = open;
        if (a[1].Kind != WebLingoValueKind.String)
        {
            error = Wrong($"A statement is SQL, a string, not {WebLingoValueFormatter.Describe(a[1], heap)}.");
            return false;
        }
        sql = heap.GetString(a[1]);
        return TryValues(a[2], heap, out values, out error) && TryOptions(a[3], method, heap, out timeout, out _, out error)
               && TryServices(heap, out _, out scheduler, out error);
    }

    /// <summary>A statement's values: a map of names, each <c>@name</c> in its SQL, to values a database takes, or none.</summary>
    private static bool TryValues(WebLingoValue given, WebLingoHeap heap, out IReadOnlyList<KeyValuePair<string, object?>> values,
        out WebLingoRuntimeError? error)
    {
        var list = new List<KeyValuePair<string, object?>>();
        values = list;
        error = null;
        if (given.Kind == WebLingoValueKind.Null) return true;
        if (given.Kind != WebLingoValueKind.Map)
        {
            error = Wrong($"A statement's values are a map of names to values, {{ max: 10 }}, not {WebLingoValueFormatter.Describe(given, heap)}.");
            return false;
        }
        for (var i = 0; i < heap.MapCount(given); i++)
        {
            var (key, value) = heap.MapEntryAt(given, i);
            if (key.Kind != WebLingoValueKind.String || !IsName(heap.GetString(key)))
            {
                error = Wrong($"A statement's value is named by a name, letters, digits and _, as @max is, not {WebLingoValueFormatter.Describe(key, heap)}.");
                return false;
            }
            object? held;
            switch (value.Kind)
            {
                case WebLingoValueKind.Null: held = null; break;
                case WebLingoValueKind.Boolean: held = value.AsBoolean; break;
                case WebLingoValueKind.Int64: held = value.AsInt64; break;
                case WebLingoValueKind.Binary64: held = value.AsBinary64; break;
                case WebLingoValueKind.Decimal: held = heap.GetDecimal(value); break;
                case WebLingoValueKind.String: held = heap.GetString(value); break;
                case WebLingoValueKind.Bytes: held = heap.GetBytes(value); break;
                case WebLingoValueKind.DateTime:
                    held = DateTimeOffset.FromUnixTimeMilliseconds(value.UnixMilliseconds).ToOffset(TimeSpan.FromMinutes(value.OffsetMinutes));
                    break;
                default:
                    error = Wrong($"@{heap.GetString(key)} is null, a Boolean, a number, a decimal, a string, bytes or a date, which a database takes, not {WebLingoValueFormatter.Describe(value, heap)}.");
                    return false;
            }
            list.Add(new(heap.GetString(key), held));
        }
        return true;
    }

    /// <summary>
    /// An operation's options: a map, or none, of those the method takes, <c>timeout</c>, a statement's, an integer of
    /// seconds, 0 or more, and <c>isolation</c>, a transaction's, named; WL3001 for any other.
    /// </summary>
    private static bool TryOptions(WebLingoValue given, string method, WebLingoHeap heap, out int? timeout, out string? isolation,
        out WebLingoRuntimeError? error)
    {
        (timeout, isolation, error) = (null, null, null);
        if (given.Kind == WebLingoValueKind.Null) return true;
        var takes = method == "begin" ? "isolation" : "timeout";
        if (given.Kind != WebLingoValueKind.Map)
        {
            error = Wrong($"{method}'s options are a map, {{ {takes}: … }}, not {WebLingoValueFormatter.Describe(given, heap)}.");
            return false;
        }
        for (var i = 0; i < heap.MapCount(given); i++)
        {
            var (key, value) = heap.MapEntryAt(given, i);
            var name = key.Kind == WebLingoValueKind.String ? heap.GetString(key) : null;
            if (name != takes)
            {
                var named = name is null ? WebLingoValueFormatter.Describe(key, heap) : $"\"{name}\"";
                error = Wrong($"{method}'s option is \"{takes}\", not {named}.");
                return false;
            }
            if (name == "timeout")
            {
                if (value.Kind != WebLingoValueKind.Int64 || value.AsInt64 is < 0 or > int.MaxValue)
                {
                    error = Wrong($"A statement's timeout is a number of seconds, an integer, 0 or more, not {WebLingoValueFormatter.Describe(value, heap)}.");
                    return false;
                }
                timeout = (int)value.AsInt64;
            }
            else
            {
                if (value.Kind != WebLingoValueKind.String || Array.IndexOf(Isolations, heap.GetString(value)) < 0)
                {
                    error = Wrong($"A transaction's isolation is \"{string.Join("\", \"", Isolations)}\", not {WebLingoValueFormatter.Describe(value, heap)}.");
                    return false;
                }
                isolation = heap.GetString(value);
            }
        }
        return true;
    }

    /// <summary>A query's rows as an array of maps, each from its columns' names to its values, kept from the collector until whole.</summary>
    private static bool Rows(WebLingoDatabaseRows rows, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var made = new List<WebLingoValue>();
        heap.AddRoots(made);
        try
        {
            var columns = new WebLingoValue[rows.Columns.Count];
            for (var c = 0; c < columns.Length; c++)
            {
                if (!heap.TryAllocateString(rows.Columns[c], out columns[c], out error)) return false;
                made.Add(columns[c]);
            }
            var maps = new WebLingoValue[rows.Rows.Count];
            var entries = new WebLingoValue[2 * columns.Length];
            for (var r = 0; r < maps.Length; r++)
            {
                var row = rows.Rows[r];
                for (var c = 0; c < columns.Length; c++)
                {
                    if (!TryValue(row[c], heap, out var value, out error)) return false;
                    made.Add(value);
                    (entries[2 * c], entries[2 * c + 1]) = (columns[c], value);
                }
                if (!heap.TryAllocateMap(entries, 0, columns.Length, out maps[r], out error)) return false;
                made.Add(maps[r]);
            }
            return heap.TryAllocateArray(maps, 0, maps.Length, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(made);
        }
    }

    /// <summary>A database's value as WebLingo's: null, a Boolean, an integer, a number, a decimal, a string, bytes or a date.</summary>
    private static bool TryValue(object? value, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        error = null;
        switch (value)
        {
            case null: result = WebLingoValue.Null; return true;
            case bool b: result = WebLingoValue.FromBoolean(b); return true;
            case long l: result = WebLingoValue.FromInt64(l); return true;
            case double d: result = WebLingoValue.FromBinary64(d); return true;
            case decimal m: return heap.TryAllocateDecimal(m, out result, out error);
            case byte[] bytes: return heap.TryAllocateBytes(bytes, out result, out error);
            case DateTimeOffset date:
                result = WebLingoValue.FromDateTime(date.ToUnixTimeMilliseconds(), (int)date.Offset.TotalMinutes);
                return true;
            default: return heap.TryAllocateString(value as string ?? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "", out result, out error);
        }
    }

    private static bool Nothing(bool _, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        (result, error) = (WebLingoValue.Null, null);
        return true;
    }

    private static bool TryServices(WebLingoHeap heap, out IWebLingoDatabases databases, out WebLingoScheduler scheduler, out WebLingoRuntimeError? error)
    {
        (databases, scheduler, error) = (heap.Databases!, heap.Scheduler!, null);
        if (heap.Databases is null) error = Wrong("This host gives scripts no databases.");
        else if (heap.Scheduler is null) error = Wrong("std:db's operations need a VM that runs tasks.");
        return error is null;
    }

    private static bool IsName(string name)
    {
        if (name.Length == 0 || char.IsAsciiDigit(name[0])) return false;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    private static WebLingoRuntimeError? Failed(Exception exception) => exception is WebLingoDatabaseException failure
        ? new WebLingoRuntimeError(failure.Kind == WebLingoDatabaseErrorKind.Failed ? WebLingoDiagnosticCodes.DatabaseFailure : WebLingoDiagnosticCodes.InvalidOperation,
            Sentence(failure.Message))
        : null;

    /// <summary>A message as a sentence: its first letter a capital, a full stop at its end, unless it has one.</summary>
    private static string Sentence(string text)
    {
        if (text.Length == 0) return text;
        text = char.ToUpperInvariant(text[0]) + text[1..];
        return text.EndsWith('.') ? text : text + ".";
    }

    private static WebLingoRuntimeError Wrong(string message) => new(WebLingoDiagnosticCodes.InvalidOperation, message);
}
