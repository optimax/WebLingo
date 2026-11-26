using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using WebLingo.Cli;
using WebLingo.Conformance.Tests;
using WebLingo.Hosting;
using WebLingo.Wasp.Cli;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// <c>wasp serve</c> (specification, sections 6.12 and 8.4; section 9, "M0.14 parts", part 5), end to end: an application
/// on disk, served on Kestrel on a port of the system's choosing, and asked for its pages and files over HTTP; and, every
/// test the same, served by WebLingo's own HTTP server, <c>--host standalone</c> (<see cref="StandaloneWaspServeTests"/>;
/// section 9, "M0.23 parts", part 2).
/// </summary>
public class WaspServeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wasp-serve-" + Guid.NewGuid().ToString("N"));

    /// <summary>The host that serves the site, <c>wasp serve --host</c>'s.</summary>
    protected virtual string Host => "aspnetcore";

    /// <summary>The site's server on <see cref="Host"/>, as <c>wasp serve</c> makes it; null, with why, where it cannot be.</summary>
    private IWebLingoWaspServer? Create(string? root, int port, IReadOnlyDictionary<string, string> schemes, out string? error,
        out WebLingoMode mode, TextWriter? log = null) =>
        WebLingoWaspStandalone.Create(Host, root, port, schemes, out error, out mode, log);

    public WaspServeTests()
    {
        Write("layouts/main.wasp", "<% param title; %><html><title><%= title %></title><body><% renderBody(); %></body></html>");
        Write("index.wasp", "<% layout \"/layouts/main.wasp\", { title: \"Home\" }; %><h1>Hello</h1><% writeLn(\"from the page\"); %>");
        Write("broken.wasp", "<%= [1][2] %>");
        Write("css/site.css", "h1 { }");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AnApplicationIsServedOverHttp()
    {
        Write("api/answer.wlg", "export async function get(request, response) { await delay(10); response.write(42); }");
        Write("components/value.wasp", "<% param n; await delay(10); %><%= n %>");
        Write("component.wasp", "<% await renderComponent(\"/components/value.wasp\", {n: 42}); %>");
        var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out var environment);
        Assert.True(server is not null, error);
        Assert.Equal(WebLingoMode.Development, environment);
        await server.StartAsync();
        try
        {
            var address = server.Address;
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            var page = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal("text/html; charset=utf-8", page.Content.Headers.ContentType?.ToString());
            Assert.Equal("<html><title>Home</title><body><h1>Hello</h1>from the page\n</body></html>", await page.Content.ReadAsStringAsync());
            Assert.Equal("42", await client.GetStringAsync("/api/answer"));
            Assert.Equal("42", await client.GetStringAsync("/component"));
            Assert.Equal("42", await client.GetStringAsync("/component"));

            var css = await client.GetAsync("/css/site.css");
            Assert.Equal((HttpStatusCode.OK, "text/css"), (css.StatusCode, css.Content.Headers.ContentType?.MediaType));
            Assert.Equal("h1 { }", await css.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/index.wasp")).StatusCode);
            var broken = await client.GetAsync("/broken");
            Assert.Equal(HttpStatusCode.InternalServerError, broken.StatusCode);
            Assert.Contains("broken.wasp(1,", await broken.Content.ReadAsStringAsync());
        }
        finally
        {
            await server.StopAsync();
        }
    }

    /// <summary>
    /// Files uploaded by a form through Kestrel (specification, section 6.12, "Uploaded files"): read as they arrive, a file
    /// past the site's threshold kept in a temporary file in the folder its <c>wasp.config</c> names, removed as its request
    /// ends; a body larger than its <c>"maxBodySize"</c> a 413, and one larger than Kestrel's own 30,000,000 bytes taken
    /// where the site takes it.
    /// </summary>
    [Fact]
    public async Task AFormsFilesAreUploaded()
    {
        var temporary = Path.Combine(_root, "..", Path.GetFileName(_root) + "-tmp");
        Directory.CreateDirectory(temporary);
        try
        {
            Write("wasp.config", "{ \"maxBodySize\": 31000000, \"uploads\": { \"inMemory\": 1024, \"folder\": \"../" + Path.GetFileName(temporary) + "\" } }");
            Write("upload.wasp", "<% param request; var f = await request.form(); var d = (await request.files())[\"doc\"][0]; %>"
                                 + "<%= f[\"title\"] %>|<%= d.fileName %>|<%= d.size %>|<%= (await d.bytes())[d.size - 1] %>");
            var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
            Assert.True(server is not null, error);
            await server.StartAsync();
            try
            {
                var address = server.Address;
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                async Task<HttpResponseMessage> Upload(int size)
                {
                    var bytes = new byte[size];
                    bytes[^1] = 7;
                    using var form = new MultipartFormDataContent { { new StringContent("Tea"), "title" }, { new ByteArrayContent(bytes), "doc", "big.bin" } };
                    return await client.PostAsync("/upload", form);
                }
                var small = await Upload(100);
                Assert.Equal((HttpStatusCode.OK, "Tea|big.bin|100|7"), (small.StatusCode, await small.Content.ReadAsStringAsync()));
                // Larger than Kestrel's own limit, within the site's, and kept in a temporary file, removed after.
                var large = await Upload(30_500_000);
                Assert.Equal((HttpStatusCode.OK, "Tea|big.bin|30500000|7"), (large.StatusCode, await large.Content.ReadAsStringAsync()));
                Assert.Empty(Directory.GetFiles(temporary));
                // Larger than the site's: 413, before its body is read, which a client that waits for 100 Continue sees, one
                // that sends its body at once finding the connection closed as it sends.
                client.DefaultRequestHeaders.ExpectContinue = true;
                var refused = await Upload(31_500_000);
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
            }
            finally
            {
                await server.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    /// <summary>
    /// Sessions in memory under <c>wasp serve</c> (specification, section 6.12, "Sessions and cookies"; "Sessions and cookies,
    /// parts", part 2): a value kept from one request to the next, by the session's cookie; a request of the session that
    /// comes while another holds it waiting for it, through Kestrel and the workers; and <c>wasp.config</c>'s
    /// <c>"sessions"</c>, each of its settings checked.
    /// </summary>
    [Fact]
    public async Task ASessionIsKeptBetweenRequestsAndOneAtATime()
    {
        Write("wasp.config", "{ \"sessions\": { \"store\": \"memory\", \"timeout\": 20, \"lockTimeout\": 10 } }");
        Write("count.wasp", "<% param request; var n = (request.session?[\"n\"] ?? 0) + 1; request.session[\"n\"] = n; %><%= n %>");
        Write("slow.wasp", "<% param request; import { delay } from \"std:system\"; request.session[\"step\"] = \"begun\"; await delay(700);\n"
                           + "request.session[\"step\"] = \"ended\"; %>slow");
        Write("step.wasp", "<% param request; %><%= request.session[\"step\"] %>");
        var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
        Assert.True(server is not null, error);
        await server.StartAsync();
        try
        {
            var address = server.Address;
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(address) };
            Assert.Equal("1", await client.GetStringAsync("/count"));
            Assert.Equal("2", await client.GetStringAsync("/count"));
            var slow = client.GetStringAsync("/slow");
            await Task.Delay(200);
            // Sent while the slow request holds the session, it waits for it, and reads what the slow one wrote last.
            Assert.Equal("ended", await client.GetStringAsync("/step"));
            Assert.Equal("slow", await slow);
            // Another client has a session of its own.
            using var other = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(address) };
            Assert.Equal("1", await other.GetStringAsync("/count"));
            Assert.Equal("3", await client.GetStringAsync("/count"));
        }
        finally
        {
            await server.StopAsync();
        }

        foreach (var (setting, message) in new[]
                 {
                     ("{ \"store\": \"database\" }", "\"sessions\" kept in a database names it, \"database\": \"main\", one \"databases\" gives."),
                     ("{ \"store\": \"database\", \"database\": \"nowhere\" }", "\"sessions\"'s \"database\" names 'nowhere', which \"databases\" does not."),
                     ("{ \"table\": \"t\" }", "\"sessions\" kept in memory, as they are unless \"store\" is \"database\", have no \"database\", "
                                              + "\"table\" or \"create\"."),
                     ("{ \"store\": \"database\", \"database\": \"main\", \"table\": \"1t\" }", "\"lockTimeout\" a number of seconds, each more than 0."),
                     ("{ \"store\": \"files\" }", "\"lockTimeout\" a number of seconds, each more than 0."),
                     ("{ \"timeout\": 0 }", "\"sessions\" is an object, { \"store\": \"database\", \"database\": \"main\", \"table\": "
                                            + "\"wasp_sessions\", \"create\": false, \"timeout\": 20, \"lockTimeout\": 30 }, each optional, \"store\" "
                                            + "\"memory\" or \"database\", \"database\" a name \"databases\" gives, \"table\" a table's name, letters, "
                                            + "digits and _, \"create\" a Boolean, \"timeout\" a number of minutes, and \"lockTimeout\" a number of seconds, "
                                            + "each more than 0."),
                     ("{ \"lockTimeout\": \"30\" }", "\"lockTimeout\" a number of seconds, each more than 0."),
                     ("{ \"cookie\": \"x\" }", "\"lockTimeout\" a number of seconds, each more than 0."),
                     ("[]", "\"lockTimeout\" a number of seconds, each more than 0.")
                 })
        {
            Write("wasp.config", "{ \"sessions\": " + setting + " }");
            Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out error, out _));
            Assert.EndsWith(message, error);
        }
    }

    /// <summary>
    /// Sessions in a database (specification, section 6.12, "Sessions and cookies"; "Sessions and cookies, parts", part 3), a
    /// SQLite file outside the site: <c>wasp sessions create</c> prints the SQL that makes the table with <c>--sql</c>, makes
    /// it, and says it is there when it is; and <c>wasp serve</c> keeps a session in it, from one request to the next.
    /// </summary>
    [Fact]
    public async Task SessionsAreKeptInTheSitesDatabase()
    {
        var data = Path.Combine(_root, "..", Path.GetFileName(_root) + "-data");
        Directory.CreateDirectory(data);
        try
        {
            Write("wasp.config", "{ \"databases\": { \"main\": \"sqlite:../" + Path.GetFileName(data) + "/sessions.db\" }, "
                                 + "\"sessions\": { \"store\": \"database\", \"database\": \"main\" } }");
            Write("count.wasp", "<% param request; var n = (request.session?[\"n\"] ?? 0) + 1; request.session[\"n\"] = n; %><%= n %>");
            Assert.Equal((WebLingoCommandLine.Success, "CREATE TABLE wasp_sessions (id TEXT NOT NULL PRIMARY KEY, data TEXT NOT NULL, used INTEGER NOT NULL, "
                                                       + "locked_until INTEGER NULL);\nCREATE INDEX wasp_sessions_used ON wasp_sessions (used);\n"
                                                       + "INSERT INTO wasp_sessions (id, data, used, locked_until) VALUES ('schema', '{\"schema\": 1}', "
                                                       + "9223372036854775807, NULL);\n", ""), Sessions("create", "--root", _root, "--sql"));
            Assert.False(File.Exists(Path.Combine(data, "sessions.db")));
            Assert.Equal((WebLingoCommandLine.Success, "WASP made the table of sessions 'wasp_sessions' in the database 'main', its schema 1.\n", ""),
                Sessions("create", "--root", _root));
            Assert.Equal((WebLingoCommandLine.Success, "The database 'main' has its table of sessions 'wasp_sessions' already, its schema 1.\n", ""),
                Sessions("create", "--root", _root));
            Assert.Equal((WebLingoCommandLine.UsageError, "Usage: wasp sessions create [--root <folder>] [--database <name>] [--table <name>] [--sql]\n",
                "'remove' is not what wasp sessions does: create.\n"), Sessions("remove"));

            var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
            Assert.True(server is not null, error);
            await server.StartAsync();
            try
            {
                var address = server.Address;
                using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(address) };
                Assert.Equal("1", await client.GetStringAsync("/count"));
                Assert.Equal("2", await client.GetStringAsync("/count"));
            }
            finally
            {
                await server.StopAsync();
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(data, recursive: true);
        }
    }

    /// <summary>
    /// Sessions in SQL Server, on the machine's own, in a database of the test's own, dropped after it: the table made by
    /// <c>wasp sessions create</c>, as the user running the tests, and a session kept and locked in it by <c>wasp serve</c>.
    /// </summary>
    [SqlServerFact]
    public async Task SessionsAreKeptInSqlServer()
    {
        var database = "WebLingoSessionTests" + Guid.NewGuid().ToString("N");
        var server = new WebLingo.Data.DotNet.DotNetDatabases(new Dictionary<string, string> { ["master"] = SqlServerFactAttribute.ConnectionString("master") }, _root);
        // The server's own time to make a database, or drop it, given five minutes: more than a statement's 30 s on a
        // loaded machine (24 November 2026, "Flaky Tests Found Under Load").
        using (var master = server.Open("master")) master.Execute($"CREATE DATABASE [{database}]", [], 300);
        try
        {
            await SessionsAreKeptAndLockedInAsync(SqlServerFactAttribute.ConnectionString(database));
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            using var master = server.Open("master");
            master.Execute($"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
                           + $"DROP DATABASE [{database}]; END", [], 300);
        }
    }

    /// <summary>
    /// Sessions in MySQL, on the server the tests run from its download (<see cref="DatabaseServer"/>), in a database of the
    /// test's own, dropped after it, as in SQL Server.
    /// </summary>
    [MySqlFact]
    public async Task SessionsAreKeptInMySql()
    {
        var database = DatabaseServer.MySql.MakeDatabase();
        try
        {
            await SessionsAreKeptAndLockedInAsync(DatabaseServer.MySql.ConnectionString(database));
        }
        finally
        {
            DatabaseServer.MySql.DropDatabase(database);
        }
    }

    /// <summary>
    /// Sessions in PostgreSQL, on the server the tests run from its download (<see cref="DatabaseServer"/>), in a database of
    /// the test's own, dropped after it, as in SQL Server.
    /// </summary>
    [PostgreSqlFact]
    public async Task SessionsAreKeptInPostgreSql()
    {
        var database = DatabaseServer.PostgreSql.MakeDatabase();
        try
        {
            await SessionsAreKeptAndLockedInAsync(DatabaseServer.PostgreSql.ConnectionString(database));
        }
        finally
        {
            DatabaseServer.PostgreSql.DropDatabase(database);
        }
    }

    /// <summary>
    /// A site whose sessions are in the database <paramref name="connection"/> names: its table made by <c>wasp sessions
    /// create</c>, which says, run again, that it is there; a value kept from one request to the next by <c>wasp serve</c>;
    /// and a request of the session that comes while another holds it waiting for it, reading what the other wrote last.
    /// </summary>
    private async Task SessionsAreKeptAndLockedInAsync(string connection)
    {
        Write("wasp.config", "{ \"databases\": { \"main\": \"" + connection + "\" }, "
                             + "\"sessions\": { \"store\": \"database\", \"database\": \"main\", \"table\": \"sessions\" } }");
        Write("count.wasp", "<% param request; var n = (request.session?[\"n\"] ?? 0) + 1; request.session[\"n\"] = n; %><%= n %>");
        Write("slow.wasp", "<% param request; import { delay } from \"std:system\"; request.session[\"step\"] = \"begun\"; await delay(700);\n"
                           + "request.session[\"step\"] = \"ended\"; %>slow");
        Write("step.wasp", "<% param request; %><%= request.session[\"step\"] %>");
        Assert.Equal((WebLingoCommandLine.Success, "WASP made the table of sessions 'sessions' in the database 'main', its schema 1.\n", ""),
            Sessions("create", "--root", _root));
        Assert.Equal((WebLingoCommandLine.Success, "The database 'main' has its table of sessions 'sessions' already, its schema 1.\n", ""),
            Sessions("create", "--root", _root));
        var site = Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
        Assert.True(site is not null, error);
        await site.StartAsync();
        try
        {
            var address = site.Address;
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(address) };
            Assert.Equal("1", await client.GetStringAsync("/count"));
            Assert.Equal("2", await client.GetStringAsync("/count"));
            var slow = client.GetStringAsync("/slow");
            await Task.Delay(200);
            // Sent while the slow request holds the session's lock, it waits for it, and reads what the slow one wrote last.
            Assert.Equal("ended", await client.GetStringAsync("/step"));
            Assert.Equal("slow", await slow);
            Assert.Equal("3", await client.GetStringAsync("/count"));
        }
        finally
        {
            await site.StopAsync();
        }
    }

    /// <summary>
    /// The security headers under <c>wasp serve</c> (the owner's answers, 25 November 2026, "Security Headers and Sensitive Files
    /// in WASP"; specification, section 6.12): <c>nosniff</c>, <c>Referrer-Policy</c>, <c>frame-ancestors 'self'</c> and
    /// <c>X-Frame-Options</c> on every response, a page's, a file's, a 404's and a 500's, a page's own
    /// <c>Content-Security-Policy</c> in place of WASP's; each changed or removed by <c>wasp.config</c>'s <c>"headers"</c>,
    /// whose every setting is checked; and a sensitive file a 404 unless <c>"serve"</c> allows its extension.
    /// </summary>
    [Fact]
    public async Task EveryResponseHasTheSecurityHeadersWaspConfigChanges()
    {
        Write("own.wasp", "<% param response; response.headers[\"Content-Security-Policy\"] = \"default-src 'self'\"; %>own");
        Write("logs/app.log", "a line");
        Write("keys/site.pfx", "key");
        Write("data/shop.db", "SQLite format 3");

        async Task<Dictionary<string, (int Status, string? NoSniff, string? Referrer, string? Policy, string? Frames)>> Serve(params string[] paths)
        {
            var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
            Assert.True(server is not null, error);
            await server.StartAsync();
            try
            {
                var address = server.Address;
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                var answers = new Dictionary<string, (int, string?, string?, string?, string?)>();
                foreach (var path in paths)
                {
                    var response = await client.GetAsync(path);
                    string? Header(string name) => response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;
                    answers[path] = ((int)response.StatusCode, Header("X-Content-Type-Options"), Header("Referrer-Policy"),
                        Header("Content-Security-Policy"), Header("X-Frame-Options"));
                }
                return answers;
            }
            finally
            {
                await server.StopAsync();
            }
        }

        var all = (NoSniff: "nosniff", Referrer: "strict-origin-when-cross-origin", Policy: "frame-ancestors 'self'", Frames: "SAMEORIGIN");
        var answers = await Serve("/", "/css/site.css", "/missing", "/broken", "/own", "/logs/app.log", "/keys/site.pfx", "/data/shop.db");
        Assert.Equal((200, all.NoSniff, all.Referrer, all.Policy, all.Frames), answers["/"]);
        Assert.Equal((200, all.NoSniff, all.Referrer, all.Policy, all.Frames), answers["/css/site.css"]);
        Assert.Equal((404, all.NoSniff, all.Referrer, all.Policy, all.Frames), answers["/missing"]);
        Assert.Equal((500, all.NoSniff, all.Referrer, all.Policy, all.Frames), answers["/broken"]);
        Assert.Equal((200, all.NoSniff, all.Referrer, "default-src 'self'", all.Frames), answers["/own"]);
        Assert.Equal((404, all.NoSniff, all.Referrer, all.Policy, all.Frames), answers["/logs/app.log"]);
        Assert.Equal((404, all.NoSniff, all.Referrer, all.Policy, all.Frames), answers["/keys/site.pfx"]);
        Assert.Equal((404, all.NoSniff, all.Referrer, all.Policy, all.Frames), answers["/data/shop.db"]);

        Write("wasp.config", "{ \"headers\": { \"nosniff\": false, \"referrerPolicy\": null, \"frameAncestors\": [\"self\", \"https://partner.example\"] }, "
                             + "\"serve\": { \"allow\": [\".PFX\", \".log\"] } }");
        answers = await Serve("/", "/keys/site.pfx", "/logs/app.log", "/data/shop.db");
        Assert.Equal((200, null, null, "frame-ancestors 'self' https://partner.example", null), answers["/"]);
        Assert.Equal((200, null, null, "frame-ancestors 'self' https://partner.example", null), answers["/keys/site.pfx"]);
        // Served, but, as any file, only where its extension has a content type the host knows, which .log has not.
        Assert.Equal((404, null, null, "frame-ancestors 'self' https://partner.example", null), answers["/logs/app.log"]);
        Assert.Equal(404, answers["/data/shop.db"].Status);
        Write("wasp.config", "{ \"headers\": { \"referrerPolicy\": \"no-referrer\", \"frameAncestors\": [\"none\"] } }");
        Assert.Equal((200, all.NoSniff, "no-referrer", "frame-ancestors 'none'", "DENY"), (await Serve("/"))["/"]);
        Write("wasp.config", "{ \"headers\": { \"frameAncestors\": null } }");
        Assert.Equal((200, all.NoSniff, all.Referrer, null, null), (await Serve("/"))["/"]);

        var headers = "\"headers\" is an object, { \"nosniff\": true, \"referrerPolicy\": \"strict-origin-when-cross-origin\", "
                      + "\"frameAncestors\": [\"self\"] }, each optional, \"nosniff\" a Boolean, \"referrerPolicy\" \"no-referrer\", "
                      + "\"no-referrer-when-downgrade\", \"origin\", \"origin-when-cross-origin\", \"same-origin\", \"strict-origin\", "
                      + "\"strict-origin-when-cross-origin\", \"unsafe-url\" or null for none, and \"frameAncestors\" the sites that may show its "
                      + "pages in a frame, \"self\", \"none\" alone, or a scheme or a host, \"https://partner.example\", or null for any.";
        var serve = "\"serve\" is an object, { \"allow\": [\".log\"] }, the extensions, each with its dot, of the files a site does not "
                    + "serve by default that it serves.";
        foreach (var (setting, message) in new[]
                 {
                     ("{ \"headers\": [] }", headers),
                     ("{ \"headers\": { \"nosniff\": \"yes\" } }", headers),
                     ("{ \"headers\": { \"referrerPolicy\": \"never\" } }", headers),
                     ("{ \"headers\": { \"frameAncestors\": \"self\" } }", headers),
                     ("{ \"headers\": { \"frameAncestors\": [] } }", headers),
                     ("{ \"headers\": { \"frameAncestors\": [\"none\", \"self\"] } }", headers),
                     ("{ \"headers\": { \"frameAncestors\": [\"https://a.example; script-src *\"] } }", headers),
                     ("{ \"headers\": { \"frameAncestors\": [\"'self'\"] } }", headers),
                     ("{ \"headers\": { \"xFrameOptions\": \"DENY\" } }", headers),
                     ("{ \"serve\": [\".log\"] }", serve),
                     ("{ \"serve\": { \"allow\": [\"log\"] } }", serve),
                     ("{ \"serve\": { \"allow\": [\".log/x\"] } }", serve),
                     ("{ \"serve\": { \"deny\": [\".css\"] } }", serve),
                     ("{ \"serve\": { \"allow\": [\".css\"] } }", "\"serve\"'s \"allow\" names '.css', which a site serves by default: it names "
                                                                + "those it does not, .db, .db3, .sqlite, .sqlite3, .mdb, .accdb, .mdf, .ldf, .sql, .bak, "
                                                                + ".backup, .old, .orig, .swp, .tmp, .log, .env, .config, .ini, .pfx, .p12, .pem, .key.")
                 })
        {
            Write("wasp.config", setting);
            Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out var error, out _));
            Assert.EndsWith("wasp.config: " + message, error);
        }
    }

    /// <summary><c>wasp sessions …</c>, run in the test's process: its exit code, what it wrote, and its errors, line breaks LF.</summary>
    private static (int Exit, string Output, string Error) Sessions(params string[] args)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = WebLingoWaspCommandLine.Run(["sessions", .. args], output, error);
        return (exit, output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), error.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheExampleApplicationServesItsPages()
    {
        // Examples.wasp/example1, at the repository's root: the owner's example of a website built with WASP (M0.14.5), its
        // pages beside its web.config, as a PHP site's are.
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Examples.wasp", "example1", "web.config"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var server = Create(Path.Combine(folder.FullName, "Examples.wasp", "example1"), 0,
            new Dictionary<string, string>(), out var error, out _);
        Assert.True(server is not null, error);
        await server.StartAsync();
        try
        {
            var address = server.Address;
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            foreach (var (path, text) in new[]
                     {
                         ("/", "<h3>Darjeeling</h3>"), ("/teas", "Brew at 90 °C (194 °F) for 3 minutes."),
                         ("/brewing", "<tr><td>Assam</td><td>100 °C, 212 °F</td><td>4 minutes</td></tr>"),
                         ("/about", "Tea &amp; patience &lt;always&gt;"), ("/css/site.css", "--leaf")
                     })
            {
                var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains(text, await response.Content.ReadAsStringAsync());
            }
            foreach (var path in new[] { "/lib/teas.wlg", "/index.wasp", "/web.config" })
            {
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
            }
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task TheFishStoreServesItsDatabase()
    {
        // Examples.wasp/example2, the owner's example of a site with a database: copied, its database made by its setup.wlg, as
        // its README says, outside the site's folder, then served (section 7.11).
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Examples.wasp", "example2", "site", "web.config"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var copy = Path.Combine(Path.GetTempPath(), "wasp-example2-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(Path.Combine(folder.FullName, "Examples.wasp", "example2"), "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Path.Combine(folder.FullName, "Examples.wasp", "example2"), file);
            if (relative.EndsWith(".db", StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(copy, relative))!);
            File.Copy(file, Path.Combine(copy, relative));
        }
        try
        {
            var (made, setupError) = (new StringWriter(), new StringWriter());
            Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", Path.Combine(copy, "database", "setup.wlg")], made, setupError));
            Assert.Equal(("fish.db has 14 fish and 3 orders.\n", ""), (made.ToString(), setupError.ToString()));
            var server = Create(Path.Combine(copy, "site"), 0, new Dictionary<string, string>(), out var error, out _);
            Assert.True(server is not null, error);
            await server.StartAsync();
            try
            {
                var address = server.Address;
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                foreach (var (path, text) in new[]
                         {
                             ("/", "4 kinds, 41 fish in the tanks, from €24.90"), ("/fish?water=marine", "4 kinds of fish."),
                             ("/fish?q=tetra&max=3", "<h3>Neon tetra</h3>"), ("/fish/item?id=6", "<td>26 to 31 °C</td>"),
                             ("/orders", "<td>Tomás</td>"), ("/credits", "CC BY-SA 3.0"), ("/api/fish?water=marine", "\"Slug\":\"royal-gramma\"")
                         })
                {
                    var response = await client.GetAsync(path);
                    var body = await response.Content.ReadAsStringAsync();
                    Assert.True(response.StatusCode == HttpStatusCode.OK, path + ": " + body);
                    Assert.Contains(text, body);
                }
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/fish/item?id=99")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/lib/store.wlg")).StatusCode);

                // An order, in a transaction: the stock lowered, the order recorded; one for more than is left refused.
                var order = await client.PostAsync("/order", new FormUrlEncodedContent(
                    [new("fish", "13"), new("quantity", "3"), new("customer", "Mia")]));
                Assert.Equal(HttpStatusCode.OK, order.StatusCode);
                Assert.Contains("Your order of 3 Mandarinfish comes to €117.00. We have 1 left.", await order.Content.ReadAsStringAsync());
                var refused = await client.PostAsync("/order", new FormUrlEncodedContent(
                    [new("fish", "13"), new("quantity", "2"), new("customer", "Leo")]));
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                Assert.Contains("We have only 1 Mandarinfish left.", await refused.Content.ReadAsStringAsync());
                Assert.Contains("<td>Mia</td>", await client.GetStringAsync("/orders"));

                // A shopping cart, its id in a cookie the client keeps: fish added, by form actions that answer 303 (See
                // Other), which the client follows to the cart; quantities changed in one form; a fish removed by its own
                // button's formaction; a checkout refused, the cart as it was, then placed, in one transaction.
                Assert.Contains("Your cart is empty", await client.GetStringAsync("/cart"));
                var added = await client.PostAsync("/cart/add", new FormUrlEncodedContent([new("fish", "6"), new("quantity", "2")]));
                Assert.Equal(HttpStatusCode.OK, added.StatusCode);
                Assert.Equal("/cart", added.RequestMessage!.RequestUri!.AbsolutePath);
                using (var stranger = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) })
                {
                    // Answered by 303 (See Other) to the cart, with a new cart's cookie, which no script on a page reads.
                    var first = await stranger.PostAsync("/cart/add", new FormUrlEncodedContent([new("fish", "1"), new("quantity", "1")]));
                    Assert.Equal((HttpStatusCode.SeeOther, "/cart"), (first.StatusCode, first.Headers.Location?.OriginalString));
                    Assert.Matches("^cart=[0-9a-f]{32}; Path=/; Max-Age=2592000; HttpOnly; SameSite=Lax$", Assert.Single(first.Headers.GetValues("Set-Cookie")));
                }
                await client.PostAsync("/cart/add", new FormUrlEncodedContent([new("fish", "1"), new("quantity", "3")]));
                await client.PostAsync("/cart/add", new FormUrlEncodedContent([new("fish", "13"), new("quantity", "1")]));
                var cart = await client.GetStringAsync("/cart");
                Assert.Contains("Cart (6)", cart);
                Assert.Contains("name=\"quantity-6\" min=\"0\" max=\"100\" value=\"2\"", cart);
                Assert.Contains("€98.00", cart);
                await client.PostAsync("/cart/update", new FormUrlEncodedContent([new("quantity-6", "1"), new("quantity-1", "4")]));
                await client.PostAsync("/cart/remove", new FormUrlEncodedContent([new("quantity-6", "1"), new("fish", "13")]));
                cart = await client.GetStringAsync("/cart");
                Assert.Contains("Cart (5)", cart);
                Assert.DoesNotContain("Mandarinfish", cart);
                var wrong = await client.PostAsync("/cart/update", new FormUrlEncodedContent([new("quantity-6", "many")]));
                Assert.Equal((HttpStatusCode.BadRequest, "A cart takes 0 to 100 of a fish, 0 taking it out."),
                    (wrong.StatusCode, await wrong.Content.ReadAsStringAsync()));
                await client.PostAsync("/cart/add", new FormUrlEncodedContent([new("fish", "13"), new("quantity", "2")]));
                var short1 = await client.PostAsync("/cart/checkout", new FormUrlEncodedContent([new("customer", "Ida")]));
                Assert.Equal(HttpStatusCode.BadRequest, short1.StatusCode);
                Assert.Contains("We have only 1 Mandarinfish left.", await short1.Content.ReadAsStringAsync());
                Assert.Contains("Cart (7)", await client.GetStringAsync("/cart"));
                await client.PostAsync("/cart/remove", new FormUrlEncodedContent([new("fish", "13")]));
                var placed = await client.PostAsync("/cart/checkout", new FormUrlEncodedContent([new("customer", "Ida")]));
                var thanks = await placed.Content.ReadAsStringAsync();
                Assert.True(placed.StatusCode == HttpStatusCode.OK, thanks);
                Assert.Contains("<li>1 Discus</li>", thanks);
                Assert.Contains("<li>4 Neon tetra</li>", thanks);
                Assert.Contains("It comes to €59.00.", thanks);
                Assert.Contains("Your cart is empty", await client.GetStringAsync("/cart"));
                Assert.Contains("<td>Ida</td>", await client.GetStringAsync("/orders"));
                Assert.Contains("<td>5</td>", await client.GetStringAsync("/fish/item?id=6"));
            }
            finally
            {
                await server.StopAsync();
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(copy, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task TheToDoListServesItsSqlServerDatabase()
    {
        // Examples.wasp/example3, the owner's example of a site on SQL Server: copied, its database made on the machine's by its
        // setup.wlg running its setup.sql, under a name of the test's own, which the copy's wasp.config names, then served,
        // its items added, changed, marked done and deleted (section 7.11). The database is dropped after it.
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Examples.wasp", "example3", "site", "web.config"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var example = Path.Combine(folder.FullName, "Examples.wasp", "example3");
        var copy = Path.Combine(Path.GetTempPath(), "wasp-example3-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(example, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(example, file);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(copy, relative))!);
            File.Copy(file, Path.Combine(copy, relative));
        }
        var database = "WebLingoTodoTests" + Guid.NewGuid().ToString("N");
        var config = Path.Combine(copy, "site", "wasp.config");
        File.WriteAllText(config, File.ReadAllText(config).Replace("Database=WebLingoTodo;", $"Database={database};", StringComparison.Ordinal));
        try
        {
            var (made, setupError) = (new StringWriter(), new StringWriter());
            var setup = Path.Combine(copy, "database", "setup.wlg");
            var script = Path.Combine(copy, "database", "setup.sql");
            Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", setup, "database=" + database, "script=" + script], made, setupError));
            Assert.Equal(($"{database} has 4 lists and 36 items, 30 of them open.\n", ""), (made.ToString(), setupError.ToString()));
            var server = Create(Path.Combine(copy, "site"), 0, new Dictionary<string, string>(), out var error, out _);
            Assert.True(server is not null, error);
            await server.StartAsync();
            try
            {
                var address = server.Address;
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                foreach (var (path, text) in new[]
                         {
                             ("/", "<strong>30</strong> to do, <strong class=\"late\">3</strong> overdue"), ("/?show=done", "Rake the leaves on the lawn"),
                             ("/?list=4", "<h1>To do: Shopping</h1>"), ("/?q=bulbs", "<p class=\"count\">2 items.</p>"),
                             ("/edit?id=1", "value=\"Pay the electricity bill\""), ("/api/items?list=3", "\"Title\":\"Plant the tulip bulbs\"")
                         })
                {
                    var response = await client.GetAsync(path);
                    var body = await response.Content.ReadAsStringAsync();
                    Assert.True(response.StatusCode == HttpStatusCode.OK, path + ": " + body);
                    Assert.Contains(text, body);
                }
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/edit?id=999")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/lib/todo.wlg")).StatusCode);

                // Added, then the browser sent back to the list (303), which shows it; a form without a title refused.
                var added = await client.PostAsync("/add", new FormUrlEncodedContent(
                    [new("title", "Water the plants"), new("list", "1"), new("priority", "1"), new("due", "")]));
                Assert.Equal(HttpStatusCode.OK, added.StatusCode);
                var list = await added.Content.ReadAsStringAsync();
                Assert.Contains("Water the plants", list);
                Assert.Contains("<strong>31</strong> to do", list);
                var refused = await client.PostAsync("/add", new FormUrlEncodedContent([new("title", " "), new("list", "1")]));
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                Assert.Contains("Say what is to be done", await refused.Content.ReadAsStringAsync());

                // Marked done, changed, and deleted.
                var toggled = await client.PostAsync("/toggle", new FormUrlEncodedContent([new("id", "1"), new("back", "/?show=done")]));
                Assert.Contains("Pay the electricity bill", await toggled.Content.ReadAsStringAsync());
                var edited = await client.PostAsync("/edit?id=2", new FormUrlEncodedContent(
                    [new("title", "Book the boiler service"), new("list", "1"), new("priority", "2"), new("due", "2026-12-01"), new("notes", "")]));
                Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
                Assert.Contains("value=\"2026-12-01\"", await client.GetStringAsync("/edit?id=2"));
                var deleted = await client.PostAsync("/delete?id=3", new FormUrlEncodedContent([]));
                Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/edit?id=3")).StatusCode);
            }
            finally
            {
                await server.StopAsync();
            }
        }
        finally
        {
            var drop = Path.Combine(copy, "database", "drop.wlg");
            File.WriteAllText(drop, "import { Database } from \"std:db\"; using (var db = await Database.open(\"server\")) { " +
                                    $"await db.execute(\"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] " +
                                    $"SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END\", {{}}, {{ timeout: 300 }}); }}");
            WebLingoCommandLine.Run(["run", drop], new StringWriter(), new StringWriter());
            Directory.Delete(copy, recursive: true);
        }
    }

    /// <summary>
    /// Under <c>wasp serve</c>, as under <c>wasp run</c>, a site's scripts reach only the folders its <c>wasp.config</c>
    /// allows, a relative path from its root (specification, section 7.13, "File capabilities"), while the site's own pages
    /// are read as before.
    /// </summary>
    [Fact]
    public async Task ASitesScriptsReachOnlyTheFoldersItsConfigurationAllows()
    {
        Write("wasp.config", "{ \"files\": { \"read\": [\"data\"] } }");
        Write("data/teas.txt", "Sencha");
        Write("reads.wasp", "<% import fs from \"std:fs\"; %><%= await fs.readText(\"data/teas.txt\") %>|" +
                            "<% try { await fs.readText(\"index.wasp\"); } catch (FilePermissionError e) { %><%= e.code %><% } %>");
        var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
        Assert.True(server is not null, error);
        await server.StartAsync();
        try
        {
            var address = server.Address;
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            Assert.Equal("Sencha|WL3016", await client.GetStringAsync("/reads"));
            Assert.Contains("<h1>Hello</h1>", await client.GetStringAsync("/"));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    /// <summary>
    /// Files sent through Kestrel (specification, section 6.12, "Files sent"): a file a page sends, its path within the
    /// folders <c>wasp.config</c> lets its scripts read, with the page's headers, its <c>ETag</c> and its time, a 304 for a
    /// client that has it, one range of it a 206, a range beyond it a 416, a HEAD without its body, and a name to save it
    /// under; a site's own file with its tag and ranges too; and bytes a handler writes, as they are.
    /// </summary>
    [Fact]
    public async Task FilesAreSentWithTheirTagsRangesAndNames()
    {
        Write("wasp.config", "{ \"files\": { \"read\": [\"data\"] } }");
        Write("data/report.pdf", string.Concat(Enumerable.Repeat("0123456789", 100)));
        Write("report.wasp", "<% param request; param response; response.headers[\"Cache-Control\"] = \"private\"; %>not sent"
            + "<% await response.sendFile(\"data/report.pdf\", null, { download: request.query?[\"name\"] }); %>");
        Write("api/bytes.wlg", "import { Bytes } from \"std:bytes\";\n"
            + "export function get(request, response) { response.write(\"a\"); response.write(Bytes.fromHex(\"00ff80\")); response.write(\"b\"); }");
        var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
        Assert.True(server is not null, error);
        await server.StartAsync();
        try
        {
            var address = server.Address;
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var whole = await client.GetAsync("/report");
            Assert.Equal((HttpStatusCode.OK, "application/pdf", 1000L), (whole.StatusCode, whole.Content.Headers.ContentType?.MediaType,
                whole.Content.Headers.ContentLength));
            Assert.Equal(string.Concat(Enumerable.Repeat("0123456789", 100)), await whole.Content.ReadAsStringAsync());
            var tag = whole.Headers.ETag!.Tag;
            Assert.Equal(["bytes"], whole.Headers.AcceptRanges);
            Assert.Equal("private", whole.Headers.CacheControl?.ToString());
            Assert.Equal(["nosniff"], whole.Headers.GetValues("X-Content-Type-Options"));
            Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(_root, "data/report.pdf")).ToString("r"),
                whole.Content.Headers.LastModified?.ToString("r"));
            Assert.False(whole.Content.Headers.Contains("Content-Disposition"));

            using var cached = new HttpRequestMessage(HttpMethod.Get, "/report");
            cached.Headers.TryAddWithoutValidation("If-None-Match", tag);
            var notModified = await client.SendAsync(cached);
            Assert.Equal((HttpStatusCode.NotModified, ""), (notModified.StatusCode, await notModified.Content.ReadAsStringAsync()));

            using var ranged = new HttpRequestMessage(HttpMethod.Get, "/report");
            ranged.Headers.TryAddWithoutValidation("Range", "bytes=13-22");
            var part = await client.SendAsync(ranged);
            Assert.Equal((HttpStatusCode.PartialContent, "3456789012"), (part.StatusCode, await part.Content.ReadAsStringAsync()));
            Assert.Equal("bytes 13-22/1000", part.Content.Headers.ContentRange?.ToString());

            using var beyond = new HttpRequestMessage(HttpMethod.Get, "/report");
            beyond.Headers.TryAddWithoutValidation("Range", "bytes=1000-");
            var unsatisfiable = await client.SendAsync(beyond);
            Assert.Equal((HttpStatusCode.RequestedRangeNotSatisfiable, "bytes */1000"),
                (unsatisfiable.StatusCode, unsatisfiable.Content.Headers.ContentRange?.ToString()));

            var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/report"));
            Assert.Equal((HttpStatusCode.OK, 1000L), (head.StatusCode, head.Content.Headers.ContentLength));
            Assert.Empty(await head.Content.ReadAsByteArrayAsync());

            var named = await client.GetAsync("/report?name=" + Uri.EscapeDataString("Raport \u017c.pdf"));
            Assert.Equal("attachment; filename=\"Raport _.pdf\"; filename*=UTF-8''Raport%20%C5%BC.pdf",
                string.Join(", ", named.Content.Headers.GetValues("Content-Disposition")));

            var css = await client.GetAsync("/css/site.css");
            Assert.Equal(["bytes"], css.Headers.AcceptRanges);
            using var cachedCss = new HttpRequestMessage(HttpMethod.Get, "/css/site.css");
            cachedCss.Headers.TryAddWithoutValidation("If-None-Match", css.Headers.ETag!.Tag);
            Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(cachedCss)).StatusCode);
            using var cssPart = new HttpRequestMessage(HttpMethod.Get, "/css/site.css");
            cssPart.Headers.TryAddWithoutValidation("Range", "bytes=0-1");
            Assert.Equal("h1", await (await client.SendAsync(cssPart)).Content.ReadAsStringAsync());

            Assert.Equal(new byte[] { 0x61, 0x00, 0xFF, 0x80, 0x62 }, await client.GetByteArrayAsync("/api/bytes"));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    /// <summary>
    /// A request's limits from <c>wasp.config</c>'s <c>"limits"</c> (specification, section 6.12, "Limits"): a page that loops
    /// stopped by its time, a 503, and one that counts by its instructions; every setting checked where the configuration is
    /// read, a wrong one an error that says what each is.
    /// </summary>
    [Fact]
    public async Task ARequestsLimitsAreTheConfigurations()
    {
        Write("wasp.config", "{ \"mode\": \"development\", \"limits\": { \"requestTimeout\": 0.5, \"requestInstructions\": 50000, "
                             + "\"requestMemory\": 16 } }");
        Write("loops.wasp", "<% import { delay } from \"std:system\"; while (true) { await delay(10); } %>never");
        Write("counts.wasp", "<% var sum = 0; for (var i = 0; i < 100000; i++) { sum += i; } %><%= sum %>");
        Write("quick.wasp", "quick");
        var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
        Assert.True(server is not null, error);
        await server.StartAsync();
        try
        {
            var address = server.Address;
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            Assert.Equal("quick", await client.GetStringAsync("/quick"));
            using var loops = await client.GetAsync("/loops");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, loops.StatusCode);
            Assert.Contains("error WL3003: The request took longer than 0.5 seconds", await loops.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var counts = await client.GetAsync("/counts");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, counts.StatusCode);
            Assert.Contains("error WL3003: The program ran 50000 instructions", await counts.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }

        const string Says = "\"limits\" is an object, { \"requestTimeout\": 60, \"requestInstructions\": 1000000000, \"requestMemory\": 128, "
                            + "\"drain\": 60 }, each optional, \"requestTimeout\" a number of seconds more than 0, or null for none, "
                            + "\"requestInstructions\" an integer more than 0, \"requestMemory\" a number of megabytes from 1 to 1048576, and "
                            + "\"drain\" a number of seconds more than 0, each number of seconds at most 4294967.";
        foreach (var setting in new[] { "[]", "{ \"requestTimeout\": 0 }", "{ \"requestTimeout\": \"60\" }", "{ \"requestInstructions\": 1.5 }",
                                         "{ \"requestInstructions\": 0 }", "{ \"requestMemory\": 0 }", "{ \"requestMemory\": 2000000 }",
                                         "{ \"time\": 60 }", "{ \"requestTimeout\": 5000000 }", "{ \"drain\": 0 }", "{ \"drain\": null }",
                                         "{ \"drain\": \"60\" }", "{ \"drain\": 4294968 }" })
        {
            Write("wasp.config", "{ \"limits\": " + setting + " }");
            Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out error, out _));
            Assert.EndsWith(Says, error);
        }
    }

    /// <summary>
    /// The drain (section 6.12, "Reloading"; the owner's answer, 26 November 2026, A1 (c) of "M0.22, Part 1: The Drain"):
    /// <c>wasp.config</c>'s <c>"limits"</c>' <c>"drain"</c>, a request with no time limit, <c>"requestTimeout": null</c>,
    /// still running on the site's files as they were once its page has changed, stopped when the drain has passed since the
    /// reload, a 503, the log saying that a reload ended it; the page then served as it is now.
    /// </summary>
    [Fact]
    public async Task AReloadEndsARequestStillRunningOnTheSiteAsItWas_OnceItsDrainHasPassed()
    {
        Write("wasp.config", "{ \"mode\": \"development\", \"limits\": { \"requestTimeout\": null, \"drain\": 0.5 } }");
        Write("waits.wasp", "<% import { delay } from \"std:system\"; while (true) { await delay(10); } %>never");
        var log = new StringWriter();
        var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out _, TextWriter.Synchronized(log));
        Assert.True(server is not null, error);
        await server.StartAsync();
        try
        {
            var address = server.Address;
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var waiting = client.GetAsync("/waits");
            // It runs on the site as it is, then the page changes, and the site reloads a quarter of a second after.
            await Task.Delay(1000);
            Write("waits.wasp", "quick");
            using var drained = await waiting;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, drained.StatusCode);
            Assert.Contains("error WL3003: The site was reloaded, and the request, still running on its files as they were before, was stopped "
                            + "0.5 seconds after", await drained.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal("quick", await client.GetStringAsync("/waits"));
            var written = log.ToString();
            Assert.Contains("WASP reloaded the application: generation 2.", written, StringComparison.Ordinal);
            Assert.Contains("GET /waits", written, StringComparison.Ordinal);
            Assert.Contains("The site was reloaded, and the request, still running on its files as they were before, was stopped", written,
                StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    /// <summary>
    /// A site's log, <c>wasp.config</c>'s <c>"log"</c> (section 6.12, "Logging"): a file a day, outside the site, from its
    /// level on, each line its time, its level, its request and its text, what a page logs with <c>std:log</c> and a
    /// request's failure, in production too; a file in the site, a level it does not know, and a setting it does not have,
    /// each refused.
    /// </summary>
    [Fact]
    public async Task ASitesLogIsTheFileItsConfigurationNames()
    {
        var logs = _root + "-logs";
        try
        {
            Write("wasp.config", "{ \"mode\": \"production\", \"log\": { \"file\": \"" + Path.Combine(logs, "site.log").Replace('\\', '/')
                                 + "\", \"level\": \"info\" } }");
            Write("logs.wasp", "<% import log from \"std:log\"; log.debug(\"hidden\"); log.info(\"shown\"); log.warn(\"also\"); %>ok");
            Write("fail.wasp", "<% throw \"no tea\"; %>");
            var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
            Assert.True(server is not null, error);
            await server.StartAsync();
            try
            {
                var address = server.Address;
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                Assert.Equal("ok", await client.GetStringAsync("/logs"));
                using var failed = await client.GetAsync("/fail");
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                Assert.DoesNotContain("no tea", await failed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            finally
            {
                await server.StopAsync();
            }
            var lines = Directory.GetFiles(logs, "site-*.log").OrderBy(file => file, StringComparer.Ordinal)
                .SelectMany(File.ReadAllLines).Select(line => line[(line.IndexOf(' ', StringComparison.Ordinal) + 1)..]).ToArray();
            Assert.Equal(["INFO GET /logs #1 shown", "WARN GET /logs #1 also",
                          "ERROR GET /fail #2 fail.wasp(1,4): error WL3008: An exception was thrown and not caught: no tea",
                          "ERROR GET /fail #2   at the top level of fail.wasp(1,4)"], lines);
        }
        finally
        {
            if (Directory.Exists(logs)) Directory.Delete(logs, recursive: true);
        }

        Write("wasp.config", "{ \"log\": { \"file\": \"logs/site.log\" } }");
        Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out var inside, out _));
        Assert.Contains("\"log\"'s \"file\", ", inside, StringComparison.Ordinal);
        Assert.EndsWith(", is in the site's folder, where it would be served and the site reloaded at each line: name a file outside it.", inside);
        const string Says = "\"log\" is an object, { \"file\": \"../logs/shop.log\", \"level\": \"info\" }, each optional, \"file\" a file "
                            + "outside the site, a file a day written with the day's date before its extension, and \"level\" the lowest written, "
                            + "\"debug\", \"info\", \"warn\" or \"error\".";
        foreach (var setting in new[] { "\"../site.log\"", "{ \"level\": \"verbose\" }", "{ \"level\": \"INFO\" }", "{ \"file\": \"\" }",
                                         "{ \"file\": 5 }", "{ \"days\": 7 }" })
        {
            Write("wasp.config", "{ \"log\": " + setting + " }");
            Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out var error, out _));
            Assert.EndsWith(Says, error);
        }
    }

    /// <summary>
    /// A page replaced while it is served, as a deployment replaces it, written beside it and moved over it: each answer
    /// one version's whole (section 6.5, WL-H004). Until 24 November 2026, a page read while it was being replaced failed
    /// to be read, and was compiled as an empty page, which its worker served until the next reload, 5% of the answers
    /// under the load benchmark's reload ("A Page Replaced While WASP Reads It").
    /// </summary>
    [Fact]
    public async Task APageReplacedWhileItIsServedIsServedWhole()
    {
        string[] versions = ["<p>first</p>", "<p>second</p>"];
        Write("page.wasp", versions[0]);
        var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
        Assert.True(server is not null, error);
        await server.StartAsync();
        try
        {
            var address = server.Address;
            var until = DateTime.UtcNow.AddSeconds(5);
            var replacing = Task.Run(async () =>
            {
                var path = Path.Combine(_root, "page.wasp");
                for (var i = 1; DateTime.UtcNow < until; i++)
                {
                    // Longer than the reloader waits for quiet, 250 ms, so that each replacement is a generation of its own.
                    await Task.Delay(300);
                    File.WriteAllText(path + ".new", versions[i % 2]);
                    // Windows refuses to move a file over one a request has open: tried again, as a deployment's tools try.
                    for (var tries = 0; ; tries++)
                    {
                        try
                        {
                            File.Move(path + ".new", path, overwrite: true);
                            break;
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && tries < 500)
                        {
                            await Task.Delay(5);
                        }
                    }
                }
            });
            var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();
            var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
            var asked = 0;
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                while (DateTime.UtcNow < until)
                {
                    using var response = await client.GetAsync("/page");
                    var body = await response.Content.ReadAsStringAsync();
                    Interlocked.Increment(ref asked);
                    if (response.StatusCode != HttpStatusCode.OK || !versions.Contains(body)) wrong.Add($"{(int)response.StatusCode} '{body}'");
                    else seen[body] = true;
                }
            })));
            await replacing;
            Assert.True(asked > 100, $"only {asked} requests");
            Assert.Equal(2, seen.Count); // Reloaded: both versions served.
            Assert.True(wrong.IsEmpty, $"{wrong.Count} of {asked} answers were neither version, the first {wrong.FirstOrDefault()}");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public void UnderIisTheSiteIsTheFolderTheModuleStartsWaspIn()
    {
        // Out of process, as a site's web.config runs wasp, IIS's ASP.NET Core Module gives the application's id as its
        // ASPNETCORE_IIS_PHYSICAL_PATH, and starts wasp in the site's folder (found 18 November 2026): the folder it starts in,
        // as when the variable is not set; a folder's full path, as in process, that folder.
        Assert.Equal(_root, WebLingoWaspServer.SiteFolder("/LM/W3SVC/2/ROOT", _root));
        Assert.Equal(_root, WebLingoWaspServer.SiteFolder(null, _root));
        Assert.Equal(_root, WebLingoWaspServer.SiteFolder("site", _root));
        var other = Path.GetTempPath();
        Assert.Equal(other, WebLingoWaspServer.SiteFolder(other, _root));
    }

    [Fact]
    public void TheConfigurationNamesTheMode()
    {
        // A site's configuration is wasp.config (the owner's decision, 16 November 2026); its weblingo.config is not read.
        Assert.Equal("wasp.config", WebLingoRouteTable.ConfigurationFile);
        Write("weblingo.config", "{ \"mode\": \"production\" }");
        Assert.NotNull(Create(_root, 0, new Dictionary<string, string>(), out _, out var mode));
        Assert.Equal(WebLingoMode.Development, mode);

        Write("wasp.config", "{ \"mode\": \"production\" }");
        Assert.NotNull(Create(_root, 0, new Dictionary<string, string>(), out _, out mode));
        Assert.Equal(WebLingoMode.Production, mode);

        Write("wasp.config", "{ \"mode\": \"staging\" }");
        Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out var error, out _));
        Assert.EndsWith("wasp.config: \"mode\" is \"development\" or \"production\".", error);

        // "environment", its name until the owner renamed it "mode" (18 November 2026), nothing kept for it.
        Write("wasp.config", "{ \"environment\": \"production\" }");
        Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out error, out _));
        Assert.EndsWith("wasp.config: 'environment' is not a setting; the settings are \"schemes\", \"mode\", "
                        + "\"sendTimeout\", \"network\", \"databases\", \"files\", \"com\", \"env\", \"mail\", \"maxBodySize\", \"uploads\", "
                        + "\"sessions\", \"limits\", \"log\", \"headers\", \"serve\" and \"base\".", error);

        Write("wasp.config", "{ \"sendTimeout\": 5 }");
        Assert.NotNull(Create(_root, 0, new Dictionary<string, string>(), out error, out _));
        Write("wasp.config", "{ \"sendTimeout\": 0 }");
        Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out error, out _));
        Assert.EndsWith("wasp.config: \"sendTimeout\" is a number of seconds, more than 0.", error);
    }

    [Fact]
    public void ServeTakesARootAPortAndSchemesOnly()
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoWaspCommandLine.Run(["serve", "--port", "http"], output, error));
        Assert.StartsWith("Usage: wasp serve [--root <folder>] [--port <port>]", output.ToString());
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoWaspCommandLine.Run(["serve", "--root", "a", "--root", "b"], output, error));
        Assert.Equal(WebLingoCommandLine.FileUnreadable,
            WebLingoWaspCommandLine.Run(["serve", "--root", Path.Combine(_root, "missing")], new StringWriter(), error = new StringWriter()));
        Assert.Contains("the application's root is not a folder", error.ToString());
    }

    /// <summary>
    /// A site over a base (specification, section 6.12, "A site over a base"), through Kestrel: a client's folder, its
    /// <c>wasp.config</c> naming the forum's site as its base, served with the base's pages and files where it has none of
    /// its own, its own first, its layout laying out the base's page; the base's settings where the client's gives none,
    /// its mode, its headers and its scripts' folders, each path from the base's folder; a page added to the base routed
    /// once the site reloads; and a base that is not a folder, is within the site, or names a base of its own, refused.
    /// </summary>
    [Fact]
    public async Task ASiteIsServedOverItsBase()
    {
        var forum = _root + "-forum";
        void InForum(string relative, string text)
        {
            var path = Path.Combine(forum, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        try
        {
            InForum("wasp.config", "{ \"mode\": \"production\", \"headers\": { \"referrerPolicy\": \"no-referrer\" }, \"files\": { \"read\": [\"data\"] } }");
            InForum("data/motto.txt", "Tea first.");
            InForum("about.wasp", "<% layout \"/layouts/main.wasp\", { title: \"About\" }; %><p>The forum's about</p>");
            InForum("motto.wasp", "<% import fs from \"std:fs\"; %><%= await fs.readText(\"../" + Path.GetFileName(forum) + "/data/motto.txt\") %>");
            InForum("index.wasp", "<p>The forum's index</p>");
            InForum("css/forum.css", "body { }");
            Write("wasp.config", "{ \"base\": \"../" + Path.GetFileName(forum) + "\" }");
            var server = Create(_root, 0, new Dictionary<string, string>(), out var error, out var mode);
            Assert.True(server is not null, error);
            Assert.Equal(WebLingoMode.Production, mode);
            await server.StartAsync();
            try
            {
                var address = server.Address;
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                var home = await client.GetAsync("/");
                Assert.Equal("<html><title>Home</title><body><h1>Hello</h1>from the page\n</body></html>", await home.Content.ReadAsStringAsync());
                Assert.Equal(["no-referrer"], home.Headers.GetValues("Referrer-Policy"));
                Assert.Equal("<html><title>About</title><body><p>The forum's about</p></body></html>", await client.GetStringAsync("/about"));
                var css = await client.GetAsync("/css/forum.css");
                Assert.Equal((HttpStatusCode.OK, "text/css", "body { }"), (css.StatusCode, css.Content.Headers.ContentType?.MediaType,
                    await css.Content.ReadAsStringAsync()));
                Assert.NotNull(css.Headers.ETag);
                Assert.Equal("h1 { }", await client.GetStringAsync("/css/site.css"));
                Assert.Equal("Tea first.", await client.GetStringAsync("/motto"));
                var broken = await client.GetAsync("/broken");
                Assert.Equal(HttpStatusCode.InternalServerError, broken.StatusCode);
                Assert.DoesNotContain("broken.wasp(1,", await broken.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/wasp.config")).StatusCode);

                InForum("news.wasp", "<p>News</p>");
                var found = false;
                for (var tries = 0; tries < 100 && !found; tries++)
                {
                    found = (await client.GetAsync("/news")).StatusCode == HttpStatusCode.OK;
                    if (!found) await Task.Delay(100);
                }
                Assert.True(found, "a page added to the base was not routed");
            }
            finally
            {
                await server.StopAsync();
            }

            Write("wasp.config", "{ \"base\": \"../" + Path.GetFileName(forum) + "-none\" }");
            Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out error, out _));
            Assert.EndsWith("-none, is not a folder.", error);
            Write("wasp.config", "{ \"base\": \"css\" }");
            Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out error, out _));
            Assert.EndsWith("and the site's folder are one within the other: a base is a folder of its own.", error);
            Write("wasp.config", "{ \"base\": 5 }");
            Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out error, out _));
            Assert.EndsWith("wasp.config: \"base\" is the folder of the site this one lies over, \"../forum/site\".", error);
            InForum("wasp.config", "{ \"base\": \"../elsewhere\" }");
            Write("wasp.config", "{ \"base\": \"../" + Path.GetFileName(forum) + "\" }");
            Assert.Null(Create(_root, 0, new Dictionary<string, string>(), out error, out _));
            Assert.Contains("wasp.config: a base names no base of its own: a site lies over one base", error);
        }
        finally
        {
            Directory.Delete(forum, recursive: true);
        }
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}

/// <summary>Every test of <c>wasp serve</c>, the site served by WebLingo's own HTTP server, <c>--host standalone</c>.</summary>
public sealed class StandaloneWaspServeTests : WaspServeTests
{
    protected override string Host => "standalone";
}
