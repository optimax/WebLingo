using Microsoft.Data.Sqlite;
using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:db</c> (specification, section 7.11), through <c>weblingo</c>, on SQLite: a database the folder's
/// <c>weblingo.config</c> names, <c>shop</c>, a file in a folder beside the script's, and ones a script's connection string
/// describes; each test in a folder of its own.
/// </summary>
public sealed class DatabaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-db-" + Guid.NewGuid().ToString("N"));

    private const string Setup =
        "import { Database } from \"std:db\"; var db = await Database.open(\"shop\"); " +
        "await db.execute(\"CREATE TABLE Fish (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price DECIMAL(10,2), Marine BOOLEAN, Added DATETIME, Photo BLOB, Length REAL)\"); " +
        "await db.execute(\"INSERT INTO Fish (Name, Price, Marine, Length) VALUES ('Neon tetra', 2.50, 0, 3.5), ('Clownfish', 24.99, 1, 8.0)\"); ";

    public DatabaseTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "scripts"));
        Directory.CreateDirectory(Path.Combine(_folder, "data"));
        File.WriteAllText(Path.Combine(_folder, "scripts", WebLingoSchemes.ConfigurationFile), "{ \"databases\": { \"shop\": \"sqlite:../data/shop.db\" } }");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // The provider's pool keeps a file open after its connection is closed.
        try { Directory.Delete(_folder, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData("foreach (var row in await db.query(\"SELECT Name, Price, Marine, Length FROM Fish ORDER BY Id\")) writeLn(row);",
        "{\"Name\": \"Neon tetra\", \"Price\": 2.5m, \"Marine\": false, \"Length\": 3.5}\n{\"Name\": \"Clownfish\", \"Price\": 24.99m, \"Marine\": true, \"Length\": 8.0}")]
    [InlineData("writeLn(await db.query(\"SELECT Name FROM Fish WHERE Price < @max AND Marine = @marine\", { max: 10, marine: false }));",
        "[{\"Name\": \"Neon tetra\"}]")]
    [InlineData("writeLn(await db.execute(\"UPDATE Fish SET Price = Price * 2\")); writeLn((await db.query(\"SELECT Price FROM Fish WHERE Id = 1\"))[0][\"Price\"]);",
        "2\n5m")]
    [InlineData("await db.execute(\"INSERT INTO Fish (Name, Added, Photo) VALUES (@name, @added, @photo)\", " +
                "{ name: \"Guppy\", added: DateTime.utc(2026, 11, 16, 9, 30), photo: Bytes.from(\"hi\") }); " +
                "var row = (await db.query(\"SELECT Name, Price, Added, Photo FROM Fish WHERE Id = 3\"))[0]; writeLn(row);",
        "{\"Name\": \"Guppy\", \"Price\": null, \"Added\": DateTime:2026-11-16T09:30:00Z, \"Photo\": Bytes:[104, 105]}")]
    [InlineData("writeLn([db.provider, db.serverVersion.startsWith(\"3.\"), db is Database]);", "[\"sqlite\", true, true]")]
    [InlineData("var all = await Task.all([db.query(\"SELECT COUNT(*) AS n FROM Fish\"), db.execute(\"DELETE FROM Fish WHERE Id = 2\")]); writeLn(all);",
        "[[{\"n\": 2}], 1]")]
    public void StatementsGiveRowsAndCounts(string source, string printed)
    {
        var (exit, output, error) = Weblingo(Setup + source);
        Assert.Equal((WebLingoCommandLine.Success, printed + "\n", ""), (exit, output, error));
    }

    [Theory]
    [InlineData("var n = await db.transaction(async () => { await db.execute(\"DELETE FROM Fish WHERE Id = 1\"); " +
                "return (await db.query(\"SELECT COUNT(*) AS n FROM Fish\"))[0][\"n\"]; }); writeLn(n);", "1\n1")]
    [InlineData("try { await db.transaction(async () => { await db.execute(\"DELETE FROM Fish\"); throw new Error(\"no\"); }); } " +
                "catch (e) { writeLn(e.message); }", "no\n2")]
    [InlineData("await db.transaction(() => db.execute(\"DELETE FROM Fish WHERE Id = 2\"), { isolation: \"serializable\" });", "1")]
    [InlineData("await db.begin(); await db.execute(\"DELETE FROM Fish\"); await db.rollback(); " +
                "try { await db.commit(); } catch (e) { writeLn([e.code, e.message]); }", "[\"WL3001\", \"No transaction is open to commit.\"]\n2")]
    [InlineData("await db.begin(); try { await db.begin(); } catch (e) { writeLn(e.code); } await db.commit();", "WL3001\n2")]
    public void TransactionsCommitOrRollBack(string source, string printed)
    {
        var (exit, output, error) = Weblingo(Setup + source + " writeLn((await db.query(\"SELECT COUNT(*) AS n FROM Fish\"))[0][\"n\"]);");
        Assert.Equal((WebLingoCommandLine.Success, printed + "\n", ""), (exit, output, error));
    }

    [Theory]
    [InlineData("try { await db.query(\"SELECT * FROM Fishes\"); } catch (DatabaseError e) { writeLn([e.code, e.message]); }",
        "[\"WL3015\", \"SQLite Error 1: 'no such table: Fishes'.\"]")]
    [InlineData("try { await db.query(\"SELECT 1\", null, { wait: 1 }); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"query's option is \\\"timeout\\\", not \\\"wait\\\".\"]")]
    [InlineData("try { await db.query(\"SELECT @x\", { x: [1] }); } catch (e) { writeLn(e.code); }", "WL3001")]
    [InlineData("try { await db.query(\"SELECT @x\", [1]); } catch (e) { writeLn(e.code); }", "WL3001")]
    [InlineData("try { await db.query(1); } catch (e) { writeLn(e.code); }", "WL3001")]
    [InlineData("try { await db.begin({ isolation: \"loose\" }); } catch (e) { writeLn(e.code); }", "WL3001")]
    [InlineData("try { await Database.open(\"nowhere\"); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"'nowhere' is not a database the configuration names.\"]")]
    [InlineData("try { await Database.open(\"postgres:x\"); } catch (e) { writeLn(e.message); }",
        "'postgres' is not a database's provider WebLingo has, which are \"sqlite\".")]
    [InlineData("db.dispose(); db.dispose(); try { await db.query(\"SELECT 1\"); } catch (e) { writeLn(e.code); }", "WL3001")]
    public void FailuresAreErrors(string source, string printed)
    {
        var (exit, output, error) = Weblingo(Setup + source);
        Assert.Equal((WebLingoCommandLine.Success, printed + "\n", ""), (exit, output, error));
    }

    [Fact]
    public void AScriptOpensADatabaseByItsConnectionString()
    {
        // :memory: is a database in memory; SQLite's own connection string opens a file read-only. The process's current
        // folder, which a relative path is from, is not changed here, the tests running at once.
        var file = Path.Combine(_folder, "data", "shop.db").Replace('\\', '/');
        File.WriteAllBytes(file, []);
        var source = "import { Database } from \"std:db\"; " +
                     "using (var db = await Database.open(\"sqlite::memory:\")) { writeLn(await db.query(\"SELECT 1 + 1 AS two\")); } " +
                     $"using (var db = await Database.open(\"sqlite:Data Source={file};Mode=ReadOnly\")) {{ " +
                     "try { await db.execute(\"CREATE TABLE T (x INTEGER)\"); } catch (DatabaseError e) { writeLn(e.code); } }";
        var (exit, output, error) = Weblingo(source);
        Assert.Equal((WebLingoCommandLine.Success, "[{\"two\": 2}]\nWL3015\n", ""), (exit, output, error));
    }

    [Fact]
    public void TheStackVmRunsItFromAWlbcFile()
    {
        // transaction, written in WebLingo, compiled into the program, for either VM (section 3.9).
        var path = Write(Setup + "await db.transaction(async () => { await db.execute(\"DELETE FROM Fish WHERE Id = 1\"); }); " +
                         "writeLn(await db.query(\"SELECT Name FROM Fish\"));");
        var (compiled, error) = (new StringWriter(), new StringWriter());
        Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["compile", "--stack", path], compiled, error));
        var output = new StringWriter();
        var exit = WebLingoCommandLine.Run(["run", Path.ChangeExtension(path, ".wlbc")], output, error);
        Assert.Equal((WebLingoCommandLine.Success, "[{\"Name\": \"Clownfish\"}]\n", ""), (exit, output.ToString(), error.ToString()));
    }

    [Fact]
    public void AConfigurationsDatabasesAreChecked()
    {
        File.WriteAllText(Path.Combine(_folder, "scripts", WebLingoSchemes.ConfigurationFile), "{ \"databases\": { \"shop\": \"shop.db\" } }");
        var (exit, _, error) = Weblingo("writeLn(1);");
        Assert.Equal(WebLingoCommandLine.FileUnreadable, exit);
        Assert.Contains("\"databases\" names each database, without a colon, and gives its connection string", error, StringComparison.Ordinal);
    }

    private string Write(string source)
    {
        var path = Path.Combine(_folder, "scripts", "program.wl");
        File.WriteAllText(path, source);
        return path;
    }

    private (int Exit, string Output, string Error) Weblingo(string source)
    {
        var path = Write(source);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(["run", path], output, error);
        return (exit, output.ToString(), error.ToString().Replace(path, "program.wl", StringComparison.Ordinal));
    }
}
