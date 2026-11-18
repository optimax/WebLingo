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
/// on disk, served on Kestrel on a port of the system's choosing, and asked for its pages and files over HTTP.
/// </summary>
public sealed class WaspServeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wasp-serve-" + Guid.NewGuid().ToString("N"));

    public WaspServeTests()
    {
        Write("layouts/main.wasp", "<% param title; %><html><title><%= title %></title><body><% renderBody(); %></body></html>");
        Write("index.wasp", "<% import { write, writeLn } from \"std:io\"; layout \"/layouts/main.wasp\", { title: \"Home\" }; %><h1>Hello</h1><% writeLn(\"from std:system\"); %>");
        Write("broken.wasp", "<%= [1][2] %>");
        Write("css/site.css", "h1 { }");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task AnApplicationIsServedOverHttp()
    {
        var server = WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out var error, out var environment);
        Assert.True(server is not null, error);
        Assert.Equal(WebLingoMode.Development, environment);
        await server.StartAsync();
        try
        {
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            var page = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal("text/html; charset=utf-8", page.Content.Headers.ContentType?.ToString());
            Assert.Equal("<html><title>Home</title><body><h1>Hello</h1>from std:system\n</body></html>", await page.Content.ReadAsStringAsync());

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

    [Fact]
    public async Task TheExampleApplicationServesItsPages()
    {
        // Examples/example1, at the repository's root: the owner's example of a website built with WASP (M0.14.5), its
        // pages beside its web.config, as a PHP site's are.
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Examples", "example1", "web.config"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var server = WebLingoWaspServer.Create(Path.Combine(folder.FullName, "Examples", "example1"), 0,
            new Dictionary<string, string>(), out var error, out _);
        Assert.True(server is not null, error);
        await server.StartAsync();
        try
        {
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
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
            foreach (var path in new[] { "/lib/teas.wl", "/index.wasp", "/web.config" })
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
        // Examples/example2, the owner's example of a site with a database: copied, its database made by its setup.wl, as
        // its README says, outside the site's folder, then served (section 7.11).
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Examples", "example2", "site", "web.config"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var copy = Path.Combine(Path.GetTempPath(), "wasp-example2-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(Path.Combine(folder.FullName, "Examples", "example2"), "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Path.Combine(folder.FullName, "Examples", "example2"), file);
            if (relative.EndsWith(".db", StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(copy, relative))!);
            File.Copy(file, Path.Combine(copy, relative));
        }
        try
        {
            var (made, setupError) = (new StringWriter(), new StringWriter());
            Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", Path.Combine(copy, "database", "setup.wl")], made, setupError));
            Assert.Equal(("fish.db has 14 fish and 3 orders.\n", ""), (made.ToString(), setupError.ToString()));
            var server = WebLingoWaspServer.Create(Path.Combine(copy, "site"), 0, new Dictionary<string, string>(), out var error, out _);
            Assert.True(server is not null, error);
            await server.StartAsync();
            try
            {
                var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
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
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/lib/store.wl")).StatusCode);

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
        // Examples/example3, the owner's example of a site on SQL Server: copied, its database made on the machine's by its
        // setup.wl running its setup.sql, under a name of the test's own, which the copy's wasp.config names, then served,
        // its items added, changed, marked done and deleted (section 7.11). The database is dropped after it.
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Examples", "example3", "site", "web.config"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var example = Path.Combine(folder.FullName, "Examples", "example3");
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
            var setup = Path.Combine(copy, "database", "setup.wl");
            var script = Path.Combine(copy, "database", "setup.sql");
            Assert.Equal(WebLingoCommandLine.Success, WebLingoCommandLine.Run(["run", setup, "database=" + database, "script=" + script], made, setupError));
            Assert.Equal(($"{database} has 4 lists and 36 items, 30 of them open.\n", ""), (made.ToString(), setupError.ToString()));
            var server = WebLingoWaspServer.Create(Path.Combine(copy, "site"), 0, new Dictionary<string, string>(), out var error, out _);
            Assert.True(server is not null, error);
            await server.StartAsync();
            try
            {
                var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
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
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/lib/todo.wl")).StatusCode);

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
            var drop = Path.Combine(copy, "database", "drop.wl");
            File.WriteAllText(drop, "import { Database } from \"std:db\"; using (var db = await Database.open(\"server\")) { " +
                                    $"await db.execute(\"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] " +
                                    $"SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END\"); }}");
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
        var server = WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out var error, out _);
        Assert.True(server is not null, error);
        await server.StartAsync();
        try
        {
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            Assert.Equal("Sencha|WL3016", await client.GetStringAsync("/reads"));
            Assert.Contains("<h1>Hello</h1>", await client.GetStringAsync("/"));
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
        Assert.NotNull(WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out _, out var mode));
        Assert.Equal(WebLingoMode.Development, mode);

        Write("wasp.config", "{ \"mode\": \"production\" }");
        Assert.NotNull(WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out _, out mode));
        Assert.Equal(WebLingoMode.Production, mode);

        Write("wasp.config", "{ \"mode\": \"staging\" }");
        Assert.Null(WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out var error, out _));
        Assert.EndsWith("wasp.config: \"mode\" is \"development\" or \"production\".", error);

        // "environment", its name until the owner renamed it "mode" (18 November 2026), nothing kept for it.
        Write("wasp.config", "{ \"environment\": \"production\" }");
        Assert.Null(WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out error, out _));
        Assert.EndsWith("wasp.config: 'environment' is not a setting; the settings are \"schemes\", \"mode\", \"sendTimeout\", "
                        + "\"network\", \"databases\", \"files\", \"com\" and \"env\".", error);

        Write("wasp.config", "{ \"sendTimeout\": 5 }");
        Assert.NotNull(WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out error, out _));
        Write("wasp.config", "{ \"sendTimeout\": 0 }");
        Assert.Null(WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out error, out _));
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

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
