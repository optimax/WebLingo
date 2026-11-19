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
        "import { Database } from \"std:db\"; import { write, writeLn } from \"std:system\"; import { Bytes } from \"std:bytes\"; import { DateTime } from \"std:datetime\"; var db = await Database.open(\"shop\"); " +
        "await db.execute(\"CREATE TABLE Fish (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price DECIMAL(10,2), Marine BOOLEAN, Added DATETIME, Photo BLOB, Length REAL)\"); " +
        "await db.execute(\"INSERT INTO Fish (Name, Price, Marine, Length) VALUES ('Neon tetra', 2.50, 0, 3.5), ('Clownfish', 24.99, 1, 8.0)\"); ";

    [Fact]
    public void ADatabaseIsMadeThenOpened()
    {
        // Made, new Database(name), then opened, db.open(); Database.open(name) shorthand for both, making an instance of
        // the class it is called on (the owner's answer, 18 November 2026; section 3.7, "No sealed classes").
        var (exit, output, error) = Weblingo(Setup + """
            class Shop extends Database {
                constructor() { super("shop"); }
                async count() { return (await this.query("SELECT COUNT(*) AS n FROM Fish"))[0]["n"]; }
            }
            var shop = new Shop();
            try { await shop.query("SELECT 1"); } catch (e) { writeLn(e.message); }
            writeLn(await shop.open() == shop);
            writeLn([await shop.count(), shop is Database, shop is Shop]);
            try { await shop.open(); } catch (e) { writeLn(e.message); }
            shop.dispose();
            try { await shop.open(); } catch (e) { writeLn(e.message); }
            class Named extends Database {}
            var named = await Named.open("shop");
            writeLn([named is Named, named is Database, db is Named]);
            named.dispose();
            try { new Database(); } catch (e) { writeLn(e.code); }
            """);
        Assert.Equal((WebLingoCommandLine.Success, """
            This Database is not open: open it first, await db.open().
            true
            [2, true, true]
            This Database is open already: it is opened once.
            This Database has been disposed, and cannot be opened again.
            [true, true, false]
            WL2006

            """.ReplaceLineEndings("\n"), ""), (exit, output, error));
    }

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
    // Operations started without being awaited run one at a time, in no defined order (section 7.11): neither touches the
    // other's row.
    [InlineData("var all = await Task.all([db.query(\"SELECT COUNT(*) AS n FROM Fish WHERE Id = 1\"), db.execute(\"DELETE FROM Fish WHERE Id = 2\")]); " +
                "writeLn(all);",
        "[[{\"n\": 1}], 1]")]
    public void StatementsGiveRowsAndCounts(string source, string printed)
    {
        var (exit, output, error) = Weblingo(Setup + source);
        Assert.Equal((WebLingoCommandLine.Success, printed + "\n", ""), (exit, output, error));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RowCursorsStreamAndReleaseTheConnection(bool stack)
    {
        var (exit, output, error) = Weblingo(Setup + """
            using (var rows = await db.rows("SELECT Name, Price FROM Fish WHERE Id >= @id ORDER BY Id", { id: 1 })) {
                writeLn(await rows.next());
                try { await db.query("SELECT 1"); } catch (TypeError e) { writeLn(e.code + " " + e.message); }
                writeLn(await rows.next());
                writeLn(await rows.next());
                writeLn(await rows.next());
                writeLn((await db.query("SELECT 3 AS n"))[0]["n"]);
            }
            using (var rows = await db.rows("SELECT Name FROM Fish ORDER BY Id")) {
                writeLn((await rows.next())["Name"]);
            }
            writeLn((await db.query("SELECT COUNT(*) AS n FROM Fish"))[0]["n"]);
            using (var rows = await db.rows("SELECT Name FROM Fish WHERE Id = 0")) {
                writeLn(await rows.next());
            }
            using (var rows = await db.rows(
                    "SELECT CASE Id WHEN 2 THEN abs(-9223372036854775808) ELSE Id END AS n FROM Fish ORDER BY Id")) {
                writeLn((await rows.next())["n"]);
                try { await rows.next(); } catch (DatabaseError e) { writeLn(e.code); }
                writeLn((await db.query("SELECT 4 AS n"))[0]["n"]);
            }
            """, stack);
        Assert.Equal((WebLingoCommandLine.Success, """
            {"Name": "Neon tetra", "Price": 2.5m}
            WL3001 A row cursor is open: finish or dispose it first.
            {"Name": "Clownfish", "Price": 24.99m}
            null
            null
            3
            Neon tetra
            2
            null
            1
            WL3015
            4

            """.ReplaceLineEndings("\n"), ""), (exit, output, error));
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
        "[\"WL3001\", \"The third argument of 'query', the options, must give \\\"timeout\\\" alone, not \\\"wait\\\".\"]")]
    [InlineData("try { await db.query(\"SELECT @x\", { x: [1] }); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"The second argument of 'query', the values, must give @x null, a Boolean, a number, a decimal, a string, bytes or a date, which a database takes, not an array.\"]")]
    [InlineData("try { await db.query(\"SELECT @x\", [1]); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"The second argument of 'query', the values, must be a map of names to values, { max: 10 }, not an array.\"]")]
    [InlineData("try { await db.query(1); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"The first argument of 'query', the statement, must be SQL, a string, not the integer 1.\"]")]
    [InlineData("try { await db.begin({ isolation: \"loose\" }); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"The argument of 'begin' must give \\\"isolation\\\" one of \\\"readUncommitted\\\", \\\"readCommitted\\\", \\\"repeatableRead\\\", \\\"serializable\\\", \\\"snapshot\\\", not a string.\"]")]
    [InlineData("try { await Database.open(5); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"The argument of 'Database' must be the configuration's name for a database, or a connection string, \\\"sqlite:C:/data/shop.db\\\", not the integer 5.\"]")]
    [InlineData("try { await Database.open(\"nowhere\"); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"'nowhere' is not a database the configuration names.\"]")]
    [InlineData("try { await Database.open(\"postgres:x\"); } catch (e) { writeLn(e.message); }",
        "'postgres' is not a database's provider WebLingo has, which are \"sqlite\", \"sqlserver\".")]
    [InlineData("try { await Database.open(\"sqlserver:Nonsense=1\"); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"'Nonsense=1' is not a SQL Server connection string: Keyword not supported: 'nonsense'.\"]")]
    [InlineData("try { await Database.open(\"sqlserver:Database=Todo\"); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"A SQL Server connection string names its server, as Server=name does.\"]")]
    [InlineData("db.dispose(); db.dispose(); try { await db.query(\"SELECT 1\"); } catch (e) { writeLn(e.code); }", "WL3001")]
    public void FailuresAreErrors(string source, string printed)
    {
        var (exit, output, error) = Weblingo(Setup + source);
        Assert.Equal((WebLingoCommandLine.Success, printed + "\n", ""), (exit, output, error));
    }

    [Fact]
    public void AnErrorInTransactionAfterItHasAwaitedIsReportedWhereItWasCalled()
    {
        // transaction, written in WebLingo, calls what it is given once its transaction has begun, after an await, with no
        // caller below it: the error is the call's, on line 6, not a place its own source's positions name (section 3.9).
        var (exit, _, error) = Weblingo(Setup + "\n\n\n\n\nawait db.transaction(5);\n");
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.StartsWith("program.wlg(6,7): error WL2005:", error);
    }

    [Fact]
    public void AScriptOpensADatabaseByItsConnectionString()
    {
        // :memory: is a database in memory; SQLite's own connection string opens a file read-only. The process's current
        // folder, which a relative path is from, is not changed here, the tests running at once.
        var file = Path.Combine(_folder, "data", "shop.db").Replace('\\', '/');
        File.WriteAllBytes(file, []);
        var source = "import { write, writeLn } from \"std:system\"; import { Database } from \"std:db\"; " +
                     "using (var db = await Database.open(\"sqlite::memory:\")) { writeLn(await db.query(\"SELECT 1 + 1 AS two\")); } " +
                     $"using (var db = await Database.open(\"sqlite:Data Source={file};Mode=ReadOnly\")) {{ " +
                     "try { await db.execute(\"CREATE TABLE T (x INTEGER)\"); } catch (DatabaseError e) { writeLn(e.code); } }";
        var (exit, output, error) = Weblingo(source);
        Assert.Equal((WebLingoCommandLine.Success, "[{\"two\": 2}]\nWL3015\n", ""), (exit, output, error));
    }

    [Fact]
    public void ASqlServerThatIsNotThereIsADatabaseError()
    {
        // Nothing listens on port 1, so SqlClient's managed network layer, the one WebLingo ships, fails to connect,
        // where its native one, left out, would fail to load.
        var source = "import { write, writeLn } from \"std:system\"; import { Database } from \"std:db\"; " +
                     "try { await Database.open(\"sqlserver:Server=tcp:127.0.0.1,1;Connect Timeout=1;Encrypt=false\"); } " +
                     "catch (DatabaseError e) { writeLn([e.code, e.message.startsWith(\"The database cannot be opened: \")]); }";
        var (exit, output, error) = Weblingo(source);
        Assert.Equal((WebLingoCommandLine.Success, "[\"WL3015\", true]\n", ""), (exit, output, error));
    }

    [Fact]
    public void SqlClientsNativeNetworkLayerIsNotShipped()
    {
        // Microsoft.Data.SqlClient.SNI.runtime, under Microsoft's own terms, is left out (specification, section 7.11).
        var found = Directory.GetFiles(AppContext.BaseDirectory, "Microsoft.Data.SqlClient.SNI*", SearchOption.AllDirectories);
        Assert.Empty(found);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.Data.SqlClient.dll")));
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
        var (exit, _, error) = Weblingo("import { write, writeLn } from \"std:system\"; writeLn(1);");
        Assert.Equal(WebLingoCommandLine.FileUnreadable, exit);
        Assert.Contains("\"databases\" names each database, without a colon, and gives its connection string", error, StringComparison.Ordinal);
    }

    private string Write(string source)
    {
        var path = Path.Combine(_folder, "scripts", "program.wlg");
        File.WriteAllText(path, source);
        return path;
    }

    private (int Exit, string Output, string Error) Weblingo(string source, bool stack = false)
    {
        var path = Write(source);
        var output = new StringWriter();
        var error = new StringWriter();
        if (stack)
        {
            var compile = WebLingoCommandLine.Run(["compile", "--stack", path], output, error);
            if (compile != WebLingoCommandLine.Success) return (compile, output.ToString(), error.ToString());
            path = Path.ChangeExtension(path, ".wlbc");
            output.GetStringBuilder().Clear();
        }
        var exit = WebLingoCommandLine.Run(["run", path], output, error);
        return (exit, output.ToString(), error.ToString().Replace(path, "program.wlg", StringComparison.Ordinal));
    }
}
