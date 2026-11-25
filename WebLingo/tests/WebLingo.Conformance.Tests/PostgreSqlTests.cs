using System.Text.Json;
using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:db</c> (specification, section 7.11), through <c>weblingo</c>, on PostgreSQL, as <see cref="SqlServerTests"/> on
/// SQL Server: a database of the test's own on the server the tests run (<see cref="DatabaseServer"/>), dropped after it,
/// its names in lower case, as PostgreSQL folds a name not quoted. Skipped where PostgreSQL's download is not there.
/// </summary>
public sealed class PostgreSqlTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-postgresql-test-" + Guid.NewGuid().ToString("N"));
    private string? _database;

    public void Dispose()
    {
        try
        {
            if (_database is not null) DatabaseServer.PostgreSql.DropDatabase(_database);
        }
        finally
        {
            try { Directory.Delete(_folder, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    [PostgreSqlFact]
    public void StatementsGiveRowsCountsAndValues()
    {
        Make();
        var (exit, output, error) = Weblingo("""
            import { writeLn } from "std:console"; import { Bytes } from "std:bytes"; import { DateTime } from "std:datetime";
            using (var db = await Database.open("shop")) {
              await db.execute("CREATE TABLE items (id SERIAL PRIMARY KEY, title VARCHAR(100) NOT NULL, price NUMERIC(10, 2) NULL, " +
                               "done BOOLEAN NOT NULL, due DATE NULL, added TIMESTAMPTZ NULL, photo BYTEA NULL, score DOUBLE PRECISION NULL, " +
                               "stars SMALLINT NULL)");
              var id = (await db.query("INSERT INTO items (title, price, done, due, added, photo, score, stars) " +
                                       "VALUES (@title, @price, @done, @due, @added, @photo, @score, @stars) RETURNING id",
                { title: "Café", price: 2.50m, done: false, due: DateTime.local(2026, 11, 20), added: DateTime.utc(2026, 11, 16, 9, 30),
                  photo: Bytes.from("hi"), score: 0.5, stars: 3 }))[0]["id"];
              writeLn(id);
              writeLn(await db.execute("INSERT INTO items (title, done) VALUES (@a, TRUE), (@b, FALSE)", { a: "Tea", b: "Cake" }));
              writeLn((await db.query("SELECT title, price, done, added, photo, score, stars FROM items WHERE id = @id", { id: id }))[0]);
              writeLn((await db.query("SELECT due FROM items WHERE id = @id", { id: id }))[0]["due"] == DateTime.local(2026, 11, 20));
              writeLn(await db.query("SELECT title FROM items WHERE done = @done ORDER BY id", { done: false }));
              writeLn([db.provider, db.serverVersion.length > 0]);
            }
            """);
        Assert.Equal((WebLingoCommandLine.Success,
            "1\n2\n" +
            "{\"title\": \"Café\", \"price\": 2.5m, \"done\": false, \"added\": DateTime:2026-11-16T09:30:00Z, \"photo\": Bytes:[104, 105], " +
            "\"score\": 0.5, \"stars\": 3}\n" +
            "true\n[{\"title\": \"Café\"}, {\"title\": \"Cake\"}]\n[\"postgresql\", true]\n", ""), (exit, output, error));
    }

    [PostgreSqlFact]
    public void TransactionsAndFailures()
    {
        Make();
        var (exit, output, error) = Weblingo("""
            import { writeLn } from "std:console";
            using (var db = await Database.open("shop")) {
              await db.execute("CREATE TABLE t (id INT PRIMARY KEY)");
              await db.execute("INSERT INTO t VALUES (1)");
              try {
                await db.transaction(async () => { await db.execute("INSERT INTO t VALUES (2)"); await db.execute("INSERT INTO t VALUES (1)"); });
              } catch (DatabaseError e) {
                writeLn([e.code, e.message.contains("duplicate key")]);
              }
              writeLn(await db.query("SELECT COUNT(*) AS n FROM t"));
              await db.transaction(async () => { await db.execute("INSERT INTO t VALUES (3)"); }, { isolation: "serializable" });
              writeLn(await db.query("SELECT id FROM t ORDER BY id"));
              try { await db.query("SELECT * FROM nowhere"); } catch (DatabaseError e) { writeLn(e.message); }
            }
            """);
        Assert.Equal((WebLingoCommandLine.Success,
            "[\"WL3015\", true]\n[{\"n\": 1}]\n[{\"id\": 1}, {\"id\": 3}]\n42P01: relation \"nowhere\" does not exist\n\nPOSITION: 15.\n", ""), (exit, output, error));
    }

    /// <summary>The test's database, made on the server, and a configuration naming it "shop".</summary>
    private void Make()
    {
        Directory.CreateDirectory(_folder);
        _database = DatabaseServer.PostgreSql.MakeDatabase();
        var databases = new Dictionary<string, string> { ["shop"] = DatabaseServer.PostgreSql.ConnectionString(_database) };
        File.WriteAllText(Path.Combine(_folder, WebLingoSchemes.ConfigurationFile), JsonSerializer.Serialize(new { databases }));
    }

    private (int Exit, string Output, string Error) Weblingo(string source)
    {
        var path = Path.Combine(_folder, "program.wlg");
        File.WriteAllText(path, "import { Database } from \"std:db\";\n" + source);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(["run", path], output, error);
        return (exit, output.ToString(), error.ToString().Replace(path, "program.wlg", StringComparison.Ordinal));
    }
}
