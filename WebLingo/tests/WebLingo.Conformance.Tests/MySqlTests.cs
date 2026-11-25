using System.Text.Json;
using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:db</c> (specification, section 7.11), through <c>weblingo</c>, on MySQL, as <see cref="SqlServerTests"/> on SQL
/// Server: a database of the test's own on the server the tests run (<see cref="DatabaseServer"/>), dropped after it.
/// Skipped where MySQL's download is not there.
/// </summary>
public sealed class MySqlTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-mysql-test-" + Guid.NewGuid().ToString("N"));
    private string? _database;

    public void Dispose()
    {
        try
        {
            if (_database is not null) DatabaseServer.MySql.DropDatabase(_database);
        }
        finally
        {
            try { Directory.Delete(_folder, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    [MySqlFact]
    public void StatementsGiveRowsCountsAndValues()
    {
        Make();
        var (exit, output, error) = Weblingo("""
            import { writeLn } from "std:console"; import { Bytes } from "std:bytes"; import { DateTime } from "std:datetime";
            using (var db = await Database.open("shop")) {
              await db.execute("CREATE TABLE Items (Id INT AUTO_INCREMENT PRIMARY KEY, Title VARCHAR(100) NOT NULL, Price DECIMAL(10, 2) NULL, " +
                               "Done BOOLEAN NOT NULL, Due DATE NULL, Added DATETIME(3) NULL, Photo BLOB NULL, Score DOUBLE NULL, " +
                               "Stars TINYINT UNSIGNED NULL) CHARACTER SET utf8mb4");
              writeLn(await db.execute("INSERT INTO Items (Title, Price, Done, Due, Added, Photo, Score, Stars) " +
                                       "VALUES (@title, @price, @done, @due, @added, @photo, @score, @stars)",
                { title: "Café", price: 2.50m, done: false, due: DateTime.local(2026, 11, 20), added: DateTime.utc(2026, 11, 16, 9, 30),
                  photo: Bytes.from("hi"), score: 0.5, stars: 3 }));
              var id = (await db.query("SELECT LAST_INSERT_ID() AS Id"))[0]["Id"];
              writeLn(id);
              writeLn(await db.execute("INSERT INTO Items (Title, Done) VALUES (@a, TRUE), (@b, FALSE)", { a: "Tea", b: "Cake" }));
              writeLn((await db.query("SELECT Title, Price, Done, Photo, Score, Stars FROM Items WHERE Id = @id", { id: id }))[0]);
              var row = (await db.query("SELECT Due, Added FROM Items WHERE Id = @id", { id: id }))[0];
              writeLn([row["Due"] == DateTime.local(2026, 11, 20), row["Added"] == DateTime.utc(2026, 11, 16, 9, 30)]);
              writeLn(await db.query("SELECT Title FROM Items WHERE Done = @done ORDER BY Id", { done: false }));
              writeLn([db.provider, db.serverVersion.length > 0]);
            }
            """);
        Assert.Equal((WebLingoCommandLine.Success,
            "1\n1\n2\n" +
            "{\"Title\": \"Café\", \"Price\": 2.5m, \"Done\": false, \"Photo\": Bytes:[104, 105], \"Score\": 0.5, \"Stars\": 3}\n" +
            "[true, true]\n[{\"Title\": \"Café\"}, {\"Title\": \"Cake\"}]\n[\"mysql\", true]\n", ""), (exit, output, error));
    }

    [MySqlFact]
    public void TransactionsAndFailures()
    {
        Make();
        var (exit, output, error) = Weblingo("""
            import { writeLn } from "std:console";
            using (var db = await Database.open("shop")) {
              await db.execute("CREATE TABLE T (Id INT PRIMARY KEY) ENGINE = InnoDB");
              await db.execute("INSERT INTO T VALUES (1)");
              try {
                await db.transaction(async () => { await db.execute("INSERT INTO T VALUES (2)"); await db.execute("INSERT INTO T VALUES (1)"); });
              } catch (DatabaseError e) {
                writeLn([e.code, e.message.contains("Duplicate entry")]);
              }
              writeLn(await db.query("SELECT COUNT(*) AS n FROM T"));
              await db.transaction(async () => { await db.execute("INSERT INTO T VALUES (3)"); }, { isolation: "serializable" });
              writeLn(await db.query("SELECT Id FROM T ORDER BY Id"));
              try { await db.query("SELECT * FROM Nowhere"); } catch (DatabaseError e) { writeLn(e.message); }
            }
            """);
        Assert.Equal((WebLingoCommandLine.Success,
            "[\"WL3015\", true]\n[{\"n\": 1}]\n[{\"Id\": 1}, {\"Id\": 3}]\nTable 'shop.nowhere' doesn't exist.\n", ""), (exit, output, error));
    }

    /// <summary>The test's database, made on the server, and a configuration naming it "shop".</summary>
    private void Make()
    {
        Directory.CreateDirectory(_folder);
        _database = DatabaseServer.MySql.MakeDatabase();
        var databases = new Dictionary<string, string> { ["shop"] = DatabaseServer.MySql.ConnectionString(_database) };
        File.WriteAllText(Path.Combine(_folder, WebLingoSchemes.ConfigurationFile), JsonSerializer.Serialize(new { databases }));
    }

    /// <summary>The program run by <c>weblingo</c>, its database's name in what it writes given as "shop".</summary>
    private (int Exit, string Output, string Error) Weblingo(string source)
    {
        var path = Path.Combine(_folder, "program.wlg");
        File.WriteAllText(path, "import { Database } from \"std:db\";\n" + source);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(["run", path], output, error);
        return (exit, output.ToString().Replace(_database!, "shop", StringComparison.Ordinal),
            error.ToString().Replace(path, "program.wlg", StringComparison.Ordinal));
    }
}
