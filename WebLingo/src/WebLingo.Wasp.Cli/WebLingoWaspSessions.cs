using WebLingo.Cli;
using WebLingo.Data.DotNet;
using WebLingo.Hosting;
using WebLingo.Hosting.DotNet;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
namespace WebLingo.Wasp.Cli;

/// <summary>
/// <c>wasp sessions create [--root &lt;folder&gt;] [--database &lt;name&gt;] [--table &lt;name&gt;] [--sql]</c> (specification,
/// section 6.12, "Sessions and cookies", A6): makes a site's table of sessions in the database its <c>wasp.config</c>
/// names, the site's root the current folder unless <c>--root</c> names another, its database and its table those of its
/// <c>"sessions"</c> unless <c>--database</c> and <c>--table</c> name others; run by an account allowed to make tables,
/// which a site's own need not be, as the database reaches it, Windows's own with SQL Server's integrated security. A table
/// that is there already is said to be, its schema with it, and left as it is. <c>--sql</c> prints the statements instead
/// of running them, for an administrator to run.
/// </summary>
public static class WebLingoWaspSessions
{
    public const string Usage = "Usage: wasp sessions create [--root <folder>] [--database <name>] [--table <name>] [--sql]";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is not ["sessions", "create", ..])
        {
            error.WriteLine(args.Length < 2 ? "wasp sessions needs what to do: create." : $"'{args[1]}' is not what wasp sessions does: create.");
            output.WriteLine(Usage);
            return WebLingoCommandLine.UsageError;
        }
        string? root = null, database = null, table = null;
        var sql = false;
        for (var at = 2; at < args.Length; at++)
        {
            var value = at + 1 < args.Length ? args[at + 1] : null;
            var given = (args[at], value) switch
            {
                ("--sql", _) when !sql => sql = true,
                ("--root", { } folder) when root is null => (root = folder) is not null,
                ("--database", { Length: > 0 } name) when database is null => (database = name) is not null,
                ("--table", { } name) when table is null && WebLingoSchemes.IsTable(name) => (table = name) is not null,
                _ => false
            };
            if (given)
            {
                if (args[at] != "--sql") at++;
                continue;
            }
            error.WriteLine($"'{args[at]}{(value is null || args[at] == "--sql" ? "" : " " + value)}' is not --root <folder>, --database <name>, "
                            + "--table <name>, a table's name of letters, digits and _, or --sql, or is given twice.");
            output.WriteLine(Usage);
            return WebLingoCommandLine.UsageError;
        }

        var full = Path.GetFullPath(root ?? ".");
        var configuration = Path.Combine(full, WebLingoRouteTable.ConfigurationFile);
        if (!WebLingoSchemes.TryLoadFolder(full, WebLingoRouteTable.ConfigurationFile, new Dictionary<string, string>(), out _, out _, out _,
                out _, out var databases, out _, out _, out _, out _, out _, out var sessions, out _, out _, out _, out _, out var why))
        {
            error.WriteLine(why);
            return WebLingoCommandLine.FileUnreadable;
        }
        database ??= sessions.Database;
        if (database is null)
        {
            error.WriteLine($"{configuration}: \"sessions\" names no database: give --database <name>, a name \"databases\" gives.");
            return WebLingoCommandLine.UsageError;
        }
        if (!databases.TryGetValue(database, out var secret))
        {
            error.WriteLine($"{configuration}: \"databases\" names no database '{database}'.");
            return WebLingoCommandLine.FileUnreadable;
        }
        table ??= sessions.Table ?? WebLingoDatabaseSessions.DefaultTable;

        if (sql)
        {
            string provider;
            try
            {
                var connection = new DotNetSecrets().Read(secret);
                provider = connection[..Math.Max(0, connection.IndexOf(':'))];
            }
            catch (WebLingoSecretException failure)
            {
                error.WriteLine($"{configuration}: the database '{database}''s connection string cannot be read: {failure.Message}.");
                return WebLingoCommandLine.FileUnreadable;
            }
            if (!DotNetDatabases.Providers.Contains(provider))
            {
                error.WriteLine($"{configuration}: the database '{database}' is not one WASP keeps sessions in: its provider is "
                                + $"'{provider}', not \"sqlite\", \"sqlserver\", \"mysql\" or \"postgresql\".");
                return WebLingoCommandLine.FileUnreadable;
            }
            foreach (var statement in WebLingoDatabaseSessions.CreateStatements(provider, table)) output.WriteLine(statement + ";");
            return WebLingoCommandLine.Success;
        }

        try
        {
            using var connection = new DotNetDatabases(databases, full, new DotNetSecrets()).Open(database);
            try
            {
                var schema = WebLingoDatabaseSessions.SchemaOf(connection, table, database);
                output.WriteLine($"The database '{database}' has its table of sessions '{table}' already, its schema {schema}.");
                return WebLingoCommandLine.Success;
            }
            catch (WebLingoDatabaseException)
            {
                // Missing, or not to be read: made below, or its making's failure said.
            }
            WebLingoDatabaseSessions.MakeTable(connection, table);
            output.WriteLine($"WASP made the table of sessions '{table}' in the database '{database}', its schema {WebLingoDatabaseSessions.Schema}.");
            return WebLingoCommandLine.Success;
        }
        catch (Exception failure) when (failure is WebLingoDatabaseException or WebLingoSecretException)
        {
            error.WriteLine($"The table of sessions '{table}' cannot be made in the database '{database}': {failure.Message}.");
            return WebLingoCommandLine.RuntimeFailed;
        }
        catch (WebLingoSessionStoreException failure)
        {
            error.WriteLine(char.ToUpperInvariant(failure.Message[0]) + failure.Message[1..] + ".");
            return WebLingoCommandLine.RuntimeFailed;
        }
    }
}
