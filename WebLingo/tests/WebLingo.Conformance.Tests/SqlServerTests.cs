using System.Text.Json;
using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:db</c> (specification, section 7.11), through <c>weblingo</c>, on SQL Server: a database of the test's own on
/// the machine's default instance, made by a script and dropped after it, through SqlClient's managed network layer, the
/// only one WebLingo ships. Skipped where no SQL Server answers on the machine by TCP/IP.
/// </summary>
public sealed class SqlServerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-sqlserver-" + Guid.NewGuid().ToString("N"));
    private readonly string _database = "WebLingoTests" + Guid.NewGuid().ToString("N");
    private bool _made;

    public void Dispose()
    {
        try
        {
            // The database dropped, the connections the provider keeps in its pool closed with it.
            if (_made) Weblingo($"using (var db = await Database.open(\"master\")) {{ await db.execute(\"ALTER DATABASE [{_database}] " +
                                $"SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];\"); }}");
        }
        finally
        {
            try { Directory.Delete(_folder, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    [SqlServerFact]
    public void StatementsGiveRowsCountsAndValues()
    {
        Make();
        var (exit, output, error) = Weblingo("""
            import { writeLn } from "std:console"; import { Bytes } from "std:bytes"; import { DateTime } from "std:datetime";
            using (var db = await Database.open("shop")) {
              await db.execute("CREATE TABLE Items (Id INT IDENTITY PRIMARY KEY, Title NVARCHAR(100) NOT NULL, Price DECIMAL(10, 2) NULL, " +
                               "Done BIT NOT NULL, Due DATE NULL, Added DATETIMEOFFSET NULL, Photo VARBINARY(MAX) NULL, Score FLOAT NULL, " +
                               "Rank TINYINT NULL)");
              var id = (await db.query("INSERT INTO Items (Title, Price, Done, Due, Added, Photo, Score, Rank) OUTPUT INSERTED.Id " +
                                       "VALUES (@title, @price, @done, @due, @added, @photo, @score, @rank)",
                { title: "Café", price: 2.50m, done: false, due: DateTime.local(2026, 11, 20), added: DateTime.utc(2026, 11, 16, 9, 30),
                  photo: Bytes.from("hi"), score: 0.5, rank: 3 }))[0]["Id"];
              writeLn(id);
              writeLn(await db.execute("INSERT INTO Items (Title, Done) VALUES (@a, 1), (@b, 0)", { a: "Tea", b: "Cake" }));
              writeLn((await db.query("SELECT Title, Price, Done, Added, Photo, Score, Rank FROM Items WHERE Id = @id", { id: id }))[0]);
              writeLn((await db.query("SELECT Due FROM Items WHERE Id = @id", { id: id }))[0]["Due"] == DateTime.local(2026, 11, 20));
              writeLn(await db.query("SELECT Title FROM Items WHERE Done = @done ORDER BY Id", { done: false }));
              writeLn([db.provider, db.serverVersion.length > 0]);
            }
            """);
        Assert.Equal((WebLingoCommandLine.Success,
            "1\n2\n" +
            "{\"Title\": \"Café\", \"Price\": 2.5m, \"Done\": false, \"Added\": DateTime:2026-11-16T09:30:00Z, \"Photo\": Bytes:[104, 105], " +
            "\"Score\": 0.5, \"Rank\": 3}\n" +
            "true\n[{\"Title\": \"Café\"}, {\"Title\": \"Cake\"}]\n[\"sqlserver\", true]\n", ""), (exit, output, error));
    }

    [SqlServerFact]
    public void TransactionsAndFailures()
    {
        Make();
        var (exit, output, error) = Weblingo("""
            import { writeLn } from "std:console";
            using (var db = await Database.open("shop")) {
              await db.execute("CREATE TABLE T (Id INT PRIMARY KEY)");
              await db.execute("INSERT INTO T VALUES (1)");
              try {
                await db.transaction(async () => { await db.execute("INSERT INTO T VALUES (2)"); await db.execute("INSERT INTO T VALUES (1)"); });
              } catch (DatabaseError e) {
                writeLn([e.code, e.message.contains("PRIMARY KEY")]);
              }
              writeLn(await db.query("SELECT COUNT(*) AS n FROM T"));
              await db.transaction(async () => { await db.execute("INSERT INTO T VALUES (3)"); }, { isolation: "serializable" });
              writeLn(await db.query("SELECT Id FROM T ORDER BY Id"));
              try { await db.query("SELECT * FROM Nowhere"); } catch (DatabaseError e) { writeLn(e.message); }
            }
            """);
        Assert.Equal((WebLingoCommandLine.Success,
            "[\"WL3015\", true]\n[{\"n\": 1}]\n[{\"Id\": 1}, {\"Id\": 3}]\nInvalid object name 'Nowhere'.\n", ""), (exit, output, error));
    }

    /// <summary>The test's database, made on the server, and a configuration naming it "shop", and the server's master "master".</summary>
    private void Make()
    {
        Directory.CreateDirectory(_folder);
        var databases = new Dictionary<string, string>
        {
            ["master"] = SqlServerFactAttribute.ConnectionString("master"),
            ["shop"] = SqlServerFactAttribute.ConnectionString(_database)
        };
        File.WriteAllText(Path.Combine(_folder, WebLingoSchemes.ConfigurationFile), JsonSerializer.Serialize(new { databases }));
        var (exit, output, error) = Weblingo($"using (var db = await Database.open(\"master\")) {{ await db.execute(\"CREATE DATABASE [{_database}]\"); }}");
        Assert.Equal((WebLingoCommandLine.Success, "", ""), (exit, output, error));
        _made = true;
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
