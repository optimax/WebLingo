using WebLingo.Cli;
using WebLingo.Wasp.Cli;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// <c>wasp</c>, WASP's command line (specification, section 13, "Contracts"), end to end: each page is written to
/// a file and run through it, as a user would, with its output, its diagnostics and its exit code checked.
/// </summary>
public sealed class WaspTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wasp-tests-" + Guid.NewGuid().ToString("N"));

    public WaspTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void RunWritesThePageToStandardOutput()
    {
        var page = "<html>\n<% foreach (var n in [1, 2]) { %>\n<p><%= n %> &lt; <%= \"3 & 4\" %></p>\n<% } %>\n</html>\n";
        Assert.Equal((WebLingoCommandLine.Success, "<html>\n<p>1 &lt; 3 &amp; 4</p>\n<p>2 &lt; 3 &amp; 4</p>\n</html>\n", ""),
            Wasp("run", page));
    }

    [Fact]
    public void CheckCompilesThePageWithoutWritingIt()
    {
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Wasp("check", "<p><%= 1 %></p>"));
    }

    [Fact]
    public void APageTakesItsParametersAsAScriptDoes()
    {
        var page = "<% param name; %>\nHello, <%= name %>!";
        Assert.Equal((WebLingoCommandLine.Success, "Hello, &lt;World&gt;!", ""), Wasp("run", page, "name=<World>"));
    }

    [Fact]
    public void APageImportsModulesFromItsFolder()
    {
        File.WriteAllText(Path.Combine(_folder, "lib.wl"), "export function twice(x) { return x * 2; }");
        Assert.Equal((WebLingoCommandLine.Success, "<b>42</b>", ""), Wasp("run", "<% import { twice } from \"lib.wl\"; %><b><%= twice(21) %></b>"));
    }

    [Fact]
    public void AnImportFromSlashIsFromTheRoot_AsWaspServeHasIt()
    {
        Write("site/lib/util.wl", "export function twice(x) { return x * 2; }");
        var page = Write("site/pages/index.wasp", "<% import { twice } from \"/lib/util.wl\"; %><b><%= twice(21) %></b>");
        Assert.Equal((WebLingoCommandLine.Success, "<b>42</b>", ""), Run(["run", "--root", Path.Combine(_folder, "site"), page]));
    }

    [Fact]
    public void ErrorsNameThePageWithTheirLineAndColumn()
    {
        var (exit, output, error) = Wasp("check", "<p>\n  <% var x = 1;\n");
        Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
        Assert.Equal("", output);
        Assert.Equal("page.wasp(2,3): error WL5001: This block has no closing '%>'.", error.TrimEnd());
    }

    [Fact]
    public void ARuntimeErrorStopsThePageWhereItIs()
    {
        var (exit, output, error) = Wasp("run", "<p>before</p>\n<%= [1][5] %>\n<p>after</p>");
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Equal("<p>before</p>\n", output);
        Assert.StartsWith("page.wasp(2,", error);
        Assert.Contains("error WL3005", error);
    }

    [Fact]
    public void VersionAndUsageNameWasp()
    {
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoWaspCommandLine.Run(["--version"], output, new StringWriter()));
        Assert.Equal(WebLingoWaspCommandLine.Version, output.ToString().TrimEnd());

        output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoWaspCommandLine.Run(["page.wasp"], output, new StringWriter()));
        Assert.StartsWith("Usage: wasp --version | run [--root <folder>] [--scheme <name>=<folder> …] <file.wasp> [name=value …] | "
            + "check [--root <folder>] [--scheme <name>=<folder> …] <file.wasp>", output.ToString());
        Assert.Contains("  --root the application's root", output.ToString());

        output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoWaspCommandLine.Run(["run", "--root", "site"], output, new StringWriter()));
        Assert.StartsWith("Usage: wasp", output.ToString());
    }

    [Fact]
    public void AnIncludeIsFoundFromTheRootOrFromItsFolder()
    {
        // site/pages/index.wasp includes /includes/header.wasp, from the root, which includes nav.wasp, from its own folder.
        Write("site/includes/header.wasp", "<header><% include \"nav.wasp\"; %></header>\n");
        Write("site/includes/nav.wasp", "<nav><%= title %></nav>");
        var page = Write("site/pages/index.wasp", "<% var title = \"Home\"; %>\n<% include \"/includes/header.wasp\"; %>\n<main></main>\n");
        var root = Path.Combine(_folder, "site");

        var (exit, output, error) = Run(["run", "--root", root, page]);
        Assert.Equal((WebLingoCommandLine.Success, "<header><nav>Home</nav></header>\n<main></main>\n", ""), (exit, output, error));
        Assert.Equal(WebLingoCommandLine.Success, Run(["check", "--root", root, page]).Exit);

        // Without --root, the root is the page's folder, where there is no includes folder.
        (exit, _, error) = Run(["run", page]);
        Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
        Assert.Contains("(2,12): error WL5002: The template '/includes/header.wasp' cannot be included: there is no such file.", error);
    }

    [Fact]
    public void AnIncludeOutsideTheRootIsRefused()
    {
        Write("secret.wasp", "secret");
        var page = Write("site/index.wasp", "<% include \"../secret.wasp\"; %>");
        var (exit, _, error) = Run(["run", page]);
        Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
        Assert.Contains("(1,12): error WL5002: The template '../secret.wasp' cannot be included: it is outside the application's root.", error);
    }

    [Fact]
    public void AnErrorInAnIncludeNamesTheTemplate()
    {
        Write("parts/bad.wasp", "<p>\n<%= y %>");
        var page = Write("index.wasp", "<% include \"parts/bad.wasp\"; %>");
        var (exit, _, error) = Run(["check", page]);
        Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
        Assert.EndsWith("parts/bad.wasp(2,5): error WL2001: 'y' is not defined.", error.TrimEnd());

        Write("parts/fails.wasp", "<p>before</p>\n<%= [1][5] %>");
        page = Write("index.wasp", "<% include \"parts/fails.wasp\"; %>");
        (exit, var output, error) = Run(["run", page]);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Equal("<p>before</p>\n", output);
        Assert.Contains("parts/fails.wasp(2,", error);
        Assert.Contains("error WL3005", error);
    }

    [Fact]
    public void AComponentIsFoundAndCompiledWhenItIsRendered()
    {
        Write("site/components/card.wasp", "<% param product; param size = \"small\"; %>\n<article class=\"<%= size %>\"><%= product %></article>\n");
        var page = Write("site/index.wasp",
            "<% foreach (var p in [\"Tea\", \"<Cake>\"]) { %>\n<% await renderComponent(\"/components/card.wasp\", { product: p }); %>\n<% } %>\n");
        Assert.Equal((WebLingoCommandLine.Success, "<article class=\"small\">Tea</article>\n<article class=\"small\">&lt;Cake&gt;</article>\n", ""),
            Run(["run", page]));

        // A component's runtime error names it, and a missing one is reported where it is rendered.
        Write("site/components/bad.wasp", "<p>\n<%= [1][5] %>");
        page = Write("site/index.wasp", "<% await renderComponent(\"components/bad\"); %>");
        var (exit, output, error) = Run(["run", page]);
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, "<p>\n"), (exit, output));
        Assert.Contains("components/bad.wasp(2,", error);
        page = Write("site/index.wasp", "<% await renderComponent(\"/components/none.wasp\"); %>");
        (exit, _, error) = Run(["run", page]);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains("index.wasp(1,10): error WL5005: The component '/components/none.wasp' cannot be rendered: there is no such file.", error);
    }

    [Fact]
    public void TheSimpleWebsiteSampleRuns()
    {
        // A layout, an include and a component together; the sample's line breaks are as the checkout gives them.
        var sample = Path.Combine(SolutionFolder(), "samples", "SimpleWebsite", "index.wasp");
        var (exit, output, error) = Run(["run", sample]);
        Assert.Equal((WebLingoCommandLine.Success, ""), (exit, error));
        Assert.Equal("<!doctype html>\n<html><body><h1>Hello from WebLingo</h1>\n<article class=\"card\">Includes</article>\n"
            + "<article class=\"card\">Layouts</article>\n<article class=\"card\">Components</article>\n<footer>Shared footer</footer>\n"
            + "</body></html>\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void APageWithALayoutRunsThroughWasp()
    {
        Write("site/layouts/main.wasp", "<% param title; %>\n<html><title><%= title %></title>\n<% renderBody(); %>\n</html>\n");
        var page = Write("site/pages/index.wasp", "<% layout \"/layouts/main.wasp\", { title: \"A & B\" }; %>\n<h1>Body</h1>\n");
        Assert.Equal((WebLingoCommandLine.Success, "<html><title>A &amp; B</title>\n<h1>Body</h1>\n</html>\n", ""),
            Run(["run", "--root", Path.Combine(_folder, "site"), page]));

        // An error in the layout names it.
        Write("site/layouts/main.wasp", "<% param title; %>\n<%= nope %>");
        var (exit, _, error) = Run(["check", "--root", Path.Combine(_folder, "site"), page]);
        Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
        Assert.EndsWith("layouts/main.wasp(2,5): error WL2001: 'nope' is not defined.", error.TrimEnd());
    }

    [Fact]
    public void RunAndCheckWriteNothingToDisk()
    {
        // WASP compiles pages in memory only and never writes a .wlbc file (specification, section 5.1): a page with a
        // layout, an include, and a component that imports a module leaves the application's folder as it found it.
        Write("site/layouts/main.wasp", "<html><% renderBody(); %></html>");
        Write("site/includes/nav.wasp", "<nav></nav>");
        Write("site/components/lib.wl", "export function twice(x) { return x * 2; }");
        Write("site/components/card.wasp", "<% param n; import { twice } from \"lib.wl\"; %><p><%= twice(n) %></p>");
        var page = Write("site/index.wasp", "<% layout \"/layouts/main.wasp\"; %><% include \"/includes/nav.wasp\"; %>"
            + "<% await renderComponent(\"/components/card.wasp\", { n: 21 }); %>");
        var before = Files();

        Assert.Equal((WebLingoCommandLine.Success, "<html><nav></nav><p>42</p></html>", ""), Run(["run", page]));
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Run(["check", page]));
        Assert.Equal(before, Files());
    }

    private string[] Files() =>
        [.. Directory.GetFiles(_folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    private static string SolutionFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "WebLingo.slnx"))) return folder.FullName;
        }
        throw new InvalidOperationException("The solution folder was not found above the test's folder.");
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static (int Exit, string Output, string Error) Run(string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoWaspCommandLine.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>
    /// A page reaches only the hosts its folder's <c>wasp.config</c> allows (specification, section 7.9): none without one,
    /// a <c>NetworkPermissionError</c>, WL3017, where it tries; with <c>127.0.0.1</c> allowed, a port nothing listens on is
    /// a <c>NetworkError</c>, its request having been made.
    /// </summary>
    [Fact]
    public void APageReachesOnlyTheHostsWaspConfigAllows()
    {
        const string page = "<% import http from \"std:http\"; try { await http.get(\"http://127.0.0.1:1/\"); } "
                            + "catch (NetworkPermissionError e) { write(e.code + \" \" + e.message); } catch (e) { write(e.code); } %>";
        Assert.Equal((WebLingoCommandLine.Success, "WL3017 '127.0.0.1' is not a host this application may reach.", ""), Wasp("run", page));
        const string socket = "<% import { Socket } from \"std:net\"; try { await Socket.connect(\"127.0.0.1\", 1); } "
                              + "catch (NetworkPermissionError e) { write(e.code); } %>";
        Assert.Equal((WebLingoCommandLine.Success, "WL3017", ""), Wasp("run", socket));
        File.WriteAllText(Path.Combine(_folder, "wasp.config"), "{ \"network\": { \"allow\": [\"127.0.0.1\"] } }");
        Assert.Equal((WebLingoCommandLine.Success, "WL3014", ""), Wasp("run", page));
        File.WriteAllText(Path.Combine(_folder, "wasp.config"), "{ \"network\": [\"127.0.0.1\"] }");
        var (exit, _, error) = Wasp("run", page);
        Assert.Equal(WebLingoCommandLine.FileUnreadable, exit);
        Assert.Contains("\"network\" is an object", error, StringComparison.Ordinal);
    }

    [Fact]
    public void APageOpensTheDatabasesWaspConfigNames()
    {
        // A site's database outside its folder, its path from the configuration's folder (section 7.11).
        var data = _folder + "-data";
        Directory.CreateDirectory(data);
        try
        {
            File.WriteAllText(Path.Combine(_folder, "wasp.config"),
                "{ \"databases\": { \"shop\": \"sqlite:../" + Path.GetFileName(data) + "/shop.db\" } }");
            const string page = "<% import { Database } from \"std:db\"; using (var db = await Database.open(\"shop\")) { " +
                                "await db.execute(\"CREATE TABLE Fish (Name TEXT)\"); await db.execute(\"INSERT INTO Fish VALUES (@name)\", { name: \"<Neon>\" }); " +
                                "foreach (var fish in await db.query(\"SELECT Name FROM Fish\")) { %><p><%= fish[\"Name\"] %></p><% } } %>";
            Assert.Equal((WebLingoCommandLine.Success, "<p>&lt;Neon&gt;</p>", ""), Wasp("run", page));
            Assert.True(File.Exists(Path.Combine(data, "shop.db")));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void APageThatCannotBeReadIsReported()
    {
        var path = Path.Combine(_folder, "missing.wasp");
        var error = new StringWriter();
        Assert.Equal(WebLingoCommandLine.FileUnreadable, WebLingoWaspCommandLine.Run(["run", path], new StringWriter(), error));
        Assert.StartsWith(path + ": cannot read the file:", error.ToString());
    }

    [Fact]
    public void APagesFolderIsConfiguredByWaspConfig()
    {
        // wasp reads its page's folder's wasp.config, not weblingo.config, a script's (the owner's decision, 16 November 2026).
        Write("weblingo.config", "{ \"schemes\": { \"l\": \"native\" } }");
        Assert.Equal((WebLingoCommandLine.Success, "<p>2</p>", ""), Wasp("run", "<p><%= 1 + 1 %></p>"));

        Write("wasp.config", "{ \"schemes\": { \"l\": \"native\" } }");
        var (exit, _, error) = Wasp("run", "<p><%= 1 + 1 %></p>");
        Assert.Equal(WebLingoCommandLine.FileUnreadable, exit);
        Assert.Contains("wasp.config", error);
        Assert.Contains("'l' is not a scheme's name, given a folder.", error);

        var output = new StringWriter();
        WebLingoWaspCommandLine.Run(["page.wasp"], output, new StringWriter());
        Assert.Contains("  --scheme  names a folder of native modules, over wasp.config's, in the file's folder", output.ToString());
    }

    /// <summary>
    /// A page's scripts reach only the folders its <c>wasp.config</c> allows (specification, section 7.13, "File
    /// capabilities"): none without them; a folder listed to be read, read only; one listed to be written, read and written;
    /// a relative path from the root; <c>..</c> followed to where it leads; a refusal a <c>PermissionError</c>, WL3016.
    /// </summary>
    [Fact]
    public void APagesScriptsReachOnlyTheFoldersWaspConfigAllows()
    {
        Write("data/teas.txt", "Sencha");
        Write("secret/key.txt", "hidden");
        Directory.CreateDirectory(Path.Combine(_folder, "uploads"));
        var page = Write("site/page.wasp", Reaching(
            "fs.readTextSync(\"../data/teas.txt\")", "fs.readTextSync(\"../secret/key.txt\")", "fs.writeTextSync(\"../data/new.txt\", \"x\")",
            "{ fs.writeTextSync(\"../uploads/up.txt\", \"up\"); return fs.readTextSync(\"../uploads/up.txt\"); }",
            "fs.readTextSync(\"../data/../secret/key.txt\")", "fs.readTextSync(\"page.wasp\")", "fs.listSync(\"../data\")"));
        Assert.Equal((WebLingoCommandLine.Success,
            "WL3016 '../data/teas.txt' cannot be read: this site may reach no file, its configuration allowing no folder.\n" +
            "WL3016 '../secret/key.txt' cannot be read: this site may reach no file, its configuration allowing no folder.\n" +
            "WL3016 '../data/new.txt' cannot be written: this site may reach no file, its configuration allowing no folder.\n" +
            "WL3016 '../uploads/up.txt' cannot be written: this site may reach no file, its configuration allowing no folder.\n" +
            "WL3016 '../data/../secret/key.txt' cannot be read: this site may reach no file, its configuration allowing no folder.\n" +
            "WL3016 'page.wasp' cannot be read: this site may reach no file, its configuration allowing no folder.\n" +
            "WL3016 '../data' cannot be listed: this site may reach no file, its configuration allowing no folder.\n", ""), Run(["run", page]));

        Write("site/wasp.config", "{ \"files\": { \"read\": [\"../data\"], \"write\": [\"../uploads\"] } }");
        Assert.Equal((WebLingoCommandLine.Success,
            "Sencha\n" +
            "WL3016 '../secret/key.txt' cannot be read: it is not in a folder this site may read.\n" +
            "WL3016 '../data/new.txt' cannot be written: it is not in a folder this site may write.\n" +
            "up\n" +
            "WL3016 '../data/../secret/key.txt' cannot be read: it is not in a folder this site may read.\n" +
            "WL3016 'page.wasp' cannot be read: it is not in a folder this site may read.\n" +
            "[\"teas.txt\"]\n", ""), Run(["run", page]));
        Assert.False(File.Exists(Path.Combine(_folder, "data", "new.txt")));

        // From the root, which --root names, the folders from the configuration's own folder, the page's.
        var deeper = Write("site/pages/deeper.wasp", Reaching("fs.readTextSync(\"../data/teas.txt\")"));
        Write("site/pages/wasp.config", "{ \"files\": { \"read\": [\"../../data\"] } }");
        Assert.Equal((WebLingoCommandLine.Success, "Sencha\n", ""), Run(["run", "--root", Path.Combine(_folder, "site"), deeper]));
    }

    /// <summary>
    /// A path is checked as the file system reads it (section 7.13, "File capabilities"): a link or a junction inside an
    /// allowed folder that leads out of it refused; on Windows, a name in another case, and a device's name refused.
    /// </summary>
    [Fact]
    public void APathIsCheckedWhereItReallyLeads()
    {
        Write("data/teas.txt", "Sencha");
        Write("secret/key.txt", "hidden");
        var link = Path.Combine(_folder, "data", "link");
        MakeFolderLink(link, Path.Combine(_folder, "secret"));
        Write("site/wasp.config", "{ \"files\": { \"read\": [\"../data\"] } }");
        var page = Write("site/page.wasp", Reaching("fs.readTextSync(\"../data/link/key.txt\")"));
        try
        {
            Assert.Equal((WebLingoCommandLine.Success, "WL3016 '../data/link/key.txt' cannot be read: it is not in a folder this site may read.\n", ""),
                Run(["run", page]));
        }
        finally
        {
            Directory.Delete(link); // The link, not what it leads to, which the folder's removal would not take on Windows.
        }
        if (!OperatingSystem.IsWindows()) return;
        page = Write("site/page.wasp", Reaching("fs.readTextSync(\"../DATA/teas.txt\")", "fs.existsSync(\"../data/NUL\")",
            "fs.existsSync(\"../data/com1.txt\")", "fs.existsSync(\"//./C:/x\")"));
        Assert.Equal((WebLingoCommandLine.Success, "Sencha\n" +
            "WL3016 '../data/NUL' cannot be looked for: it names a device.\n" +
            "WL3016 '../data/com1.txt' cannot be looked for: it names a device.\n" +
            "WL3016 '//./C:/x' cannot be looked for: it names a device.\n", ""), Run(["run", page]));
    }

    /// <summary>
    /// A SQLite database a page's script names by its own connection string is checked as <c>std:fs</c>'s paths are
    /// (section 7.13, "File capabilities"): opened to be written in a folder the site may write; read-only in one it may
    /// read; a <c>file:</c> URI refused; one <c>wasp.config</c> names trusted.
    /// </summary>
    [Fact]
    public void APagesOwnDatabaseIsCheckedAsItsFilesAre()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "data"));
        Directory.CreateDirectory(Path.Combine(_folder, "uploads"));
        Write("site/wasp.config", "{ \"files\": { \"read\": [\"../data\"], \"write\": [\"../uploads\"] }, " +
                                  "\"databases\": { \"named\": \"sqlite:../data/named.db\" } }");
        try
        {
            var page = Write("site/page.wasp", "<% import { Database } from \"std:db\"; " +
                "async function open(name) { try { using (var db = await Database.open(name)) { return db.provider; } } " +
                "catch (PermissionError e) { return e.code + \" \" + e.message; } catch (DatabaseError e) { return e.code; } } %>" +
                "<%- await open(\"sqlite:../uploads/own.db\") %>\n<%- await open(\"sqlite:../data/own.db\") %>\n" +
                "<%- await open(\"sqlite:Data Source=../data/missing.db;Mode=ReadOnly\") %>\n<%- await open(\"sqlite:file:../uploads/own.db\") %>\n" +
                "<%- await open(\"named\") %>\n");
            Assert.Equal((WebLingoCommandLine.Success,
                "sqlite\n" +
                "WL3016 The database '../data/own.db' cannot be opened: it is not in a folder this site may write.\n" +
                "WL3015\n" +
                "WL3016 'file:../uploads/own.db' cannot be opened: a site's script names a SQLite database by its path, not by a file: URI.\n" +
                "sqlite\n", ""), Run(["run", page]));
            Assert.True(File.Exists(Path.Combine(_folder, "uploads", "own.db")));
            Assert.True(File.Exists(Path.Combine(_folder, "data", "named.db")));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }

    [Theory]
    [InlineData("{ \"files\": [\"../data\"] }")]
    [InlineData("{ \"files\": { \"read\": \"../data\" } }")]
    [InlineData("{ \"files\": { \"readWrite\": [\"../data\"] } }")]
    [InlineData("{ \"files\": { \"write\": [\"\"] } }")]
    public void FilesIsAnObjectOfTwoListsOfFolders(string configuration)
    {
        Write("wasp.config", configuration);
        var (exit, _, error) = Wasp("run", "<p>x</p>");
        Assert.Equal(WebLingoCommandLine.FileUnreadable, exit);
        Assert.Contains("\"files\" is an object, { \"read\": [\"../data\"], \"write\": [\"../uploads\"] }, each a list of folders.", error,
            StringComparison.Ordinal);
    }

    /// <summary>A page that tries each operation, written as <c>&lt;%- %&gt;</c>, a line each, a <c>PermissionError</c> as its code and message.</summary>
    private static string Reaching(params string[] operations)
    {
        var page = "<% import fs from \"std:fs\"; function t(f) { try { return f(); } catch (PermissionError e) { return e.code + \" \" + e.message; } } %>";
        foreach (var operation in operations)
        {
            var body = operation.StartsWith('{') ? operation : "{ return " + operation + "; }";
            page += "<%- t(() => " + body + ") %>\n";
        }
        return page;
    }

    /// <summary>A link to a folder: a junction on Windows, which needs no privilege, a symbolic link elsewhere.</summary>
    private static void MakeFolderLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", link, target },
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;
        mklink.StandardOutput.ReadToEnd();
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
    }

    [Fact]
    public void WeblingoStillRunsScriptsOnly()
    {
        // weblingo compiles the language only: a page's block is not WebLingo.
        var path = Path.Combine(_folder, "page.wl");
        File.WriteAllText(path, "<p><%= 1 %></p>");
        Assert.Equal(WebLingoCommandLine.CompilationFailed, WebLingoCommandLine.Run(["check", path], new StringWriter(), new StringWriter()));
    }

    private (int Exit, string Output, string Error) Wasp(string command, string page, params string[] parameters)
    {
        var path = Path.Combine(_folder, "page.wasp");
        File.WriteAllText(path, page);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoWaspCommandLine.Run([command, path, .. parameters], output, error);
        // Diagnostics name the file as it was given; show only its name, so the expectations hold on every machine.
        return (exit, output.ToString(), error.ToString().Replace(path, "page.wasp", StringComparison.Ordinal));
    }
}
