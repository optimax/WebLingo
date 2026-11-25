using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using WebLingo.Cli;
using WebLingo.Conformance.Tests;
using WebLingo.Wasp.Cli;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// WebLingo Forum, <c>Examples.wasp/WebLingoForum</c> (<c>Documents/weblingo-forum.md</c>, section 5): its own tests, run
/// by <c>weblingo test</c>, and the forum served by <c>wasp serve</c>, its database made by its <c>setup.wlg</c>, on SQLite
/// always, and on SQL Server, MySQL and PostgreSQL where they are on this machine, as the other database tests are.
/// </summary>
public sealed class ForumTests
{
    private static string Forum()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Examples.wasp", "WebLingoForum", "site", "web.config")))
        {
            folder = folder.Parent;
        }
        Assert.NotNull(folder);
        return Path.Combine(folder.FullName, "Examples.wasp", "WebLingoForum");
    }

    [Fact]
    public void TheForumsModulesPassTheirOwnTests()
    {
        var tests = Directory.GetFiles(Path.Combine(Forum(), "tests"), "*.wlg").Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(tests);
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = WebLingoCommandLine.Run(["test", .. tests], output, error);
        Assert.True(exit == WebLingoCommandLine.Success, output.ToString() + error);
        Assert.Matches(@"^\d+ passed, 0 failed\.\n$", output.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public Task TheForumIsServedFromSqlite() => ServesTheForumAsync(null, "sqlite");

    [SqlServerFact]
    public async Task TheForumIsServedFromSqlServer()
    {
        var database = "WebLingoForumTests" + Guid.NewGuid().ToString("N");
        var server = new WebLingo.Data.DotNet.DotNetDatabases(
            new Dictionary<string, string> { ["master"] = SqlServerFactAttribute.ConnectionString("master") }, Path.GetTempPath());
        // The server's own time to make a database, or drop it, given five minutes, as on a loaded machine.
        using (var master = server.Open("master")) master.Execute($"CREATE DATABASE [{database}]", [], 300);
        try
        {
            await ServesTheForumAsync(SqlServerFactAttribute.ConnectionString(database), "sqlserver");
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            using var master = server.Open("master");
            master.Execute($"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
                           + $"DROP DATABASE [{database}]; END", [], 300);
        }
    }

    [MySqlFact]
    public async Task TheForumIsServedFromMySql()
    {
        var database = DatabaseServer.MySql.MakeDatabase();
        try
        {
            await ServesTheForumAsync(DatabaseServer.MySql.ConnectionString(database), "mysql");
        }
        finally
        {
            DatabaseServer.MySql.DropDatabase(database);
        }
    }

    [PostgreSqlFact]
    public async Task TheForumIsServedFromPostgreSql()
    {
        var database = DatabaseServer.PostgreSql.MakeDatabase();
        try
        {
            await ServesTheForumAsync(DatabaseServer.PostgreSql.ConnectionString(database), "postgresql");
        }
        finally
        {
            DatabaseServer.PostgreSql.DropDatabase(database);
        }
    }

    /// <summary>
    /// A copy of the forum, on the database a connection string names, or on its own SQLite file: its database made by
    /// <c>setup.wlg</c>, as its administrator makes it, then given 30 topics more, one of 45 posts, and served; its pages
    /// read as a reader reads them.
    /// </summary>
    private static async Task ServesTheForumAsync(string? connection, string provider)
    {
        var forum = Forum();
        var copy = Path.Combine(Path.GetTempPath(), "weblingo-forum-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(forum, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(forum, file);
            if (relative.StartsWith("data" + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !relative.EndsWith("README.md", StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(copy, relative))!);
            File.Copy(file, Path.Combine(copy, relative));
        }
        try
        {
            if (connection is not null)
            {
                var configuration = JsonSerializer.Serialize(new Dictionary<string, object> { ["databases"] = new Dictionary<string, string> { ["forum"] = connection } });
                File.WriteAllText(Path.Combine(copy, "setup", "weblingo.config"), configuration);
                File.WriteAllText(Path.Combine(copy, "site", "wasp.config"), configuration);
            }
            var setup = Path.Combine(copy, "setup", "setup.wlg");
            Assert.Equal((WebLingoCommandLine.Success, $"Made the forum's database 'forum' on {provider}, version 1, with its administrator Anna.\n", ""),
                Run("run", setup, "name=Anna", "email=anna@example.com", "password=correct-horse"));
            Assert.Equal((WebLingoCommandLine.Success, "The forum's database 'forum' is at version 1 already: nothing to do.\n", ""),
                Run("run", setup));

            // 30 topics, each of one post but the first, of 45, on 2 January 2026 and the day before, the last locked.
            var more = Path.Combine(copy, "setup", "more.wlg");
            File.WriteAllText(more, """
                import { Database } from "std:db";
                import { DateTime } from "std:datetime";
                import { insert } from "../site/lib/sql.wlg";
                using (var db = await Database.open("forum")) {
                    var start = DateTime.utc(2026, 1, 1);
                    var last = 0;
                    for (var i = 1; i <= 30; i++) {
                        var at = start.addHours(i);
                        var topic = await insert(db, "topics", { board_id: 1, user_id: 1, title: $"Topic {i}", created_at: at,
                            post_count: 1, view_count: 0, last_post_id: null, last_post_at: at, pinned: false, locked: i == 30, deleted: false });
                        var count = i == 1 ? 45 : 1;
                        for (var n = 1; n <= count; n++) {
                            last = await insert(db, "posts", { topic_id: topic, user_id: 1, body: $"Post {n} of topic {i}, <b>not bold</b>",
                                created_at: at.addMinutes(n), edited_at: null, edited_by: null, deleted: false });
                        }
                        await db.execute("UPDATE topics SET last_post_id = @post, post_count = @count, last_post_at = @at WHERE id = @id",
                            { post: last, count: count, at: at.addMinutes(count), id: topic });
                    }
                    await db.execute("UPDATE boards SET topic_count = 31, post_count = 75, last_post_id = @post WHERE id = 1", { post: last });
                    await db.execute("UPDATE users SET post_count = 75 WHERE id = 1");
                }
                """);
            Assert.Equal((WebLingoCommandLine.Success, "", ""), Run("run", more));

            var server = WebLingoWaspServer.Create(Path.Combine(copy, "site"), 0, new Dictionary<string, string>(), out var error, out _);
            Assert.True(server is not null, error);
            await server.StartAsync();
            try
            {
                var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                async Task<string> Page(string path, HttpStatusCode status = HttpStatusCode.OK)
                {
                    var response = await client.GetAsync(path);
                    var body = await response.Content.ReadAsStringAsync();
                    Assert.True(response.StatusCode == status, path + ": " + response.StatusCode + "\n" + body);
                    return body;
                }

                // The front page: the category, its board, its counts and its latest post.
                var home = await Page("/");
                Assert.Contains("<title>Boards · WebLingo Forum</title>", home);
                Assert.Contains("--accent: #2a6f97;", home);
                Assert.Contains("<section class=\"category\" id=\"category-1\">\n  <h2>General</h2>", home);
                Assert.Contains("<a class=\"board\" href=\"/board?id=1\">Welcome</a>", home);
                Assert.Contains("<td class=\"count\">31 topics<br>75 posts</td>", home);
                Assert.Contains("<a href=\"/topic?id=31\">Topic 30</a><br>\n    <span class=\"note\">by Anna, 2 January 2026, 06:01</span>", home);

                // A board: the pinned topic first, then the latest; 25 of 31 topics on its first page, the rest on its second.
                var board = await Page("/board?id=1");
                Assert.Contains("<a href=\"/\">Home</a> › <a href=\"/#category-1\">General</a>", board);
                var welcome = board.IndexOf("Welcome to the forum", StringComparison.Ordinal);
                var thirtieth = board.IndexOf(">Topic 30<", StringComparison.Ordinal);
                var seventh = board.IndexOf(">Topic 7<", StringComparison.Ordinal);
                Assert.True(welcome > 0 && welcome < thirtieth && thirtieth < seventh, board);
                Assert.DoesNotContain(">Topic 6<", board);
                Assert.Contains("<strong aria-current=\"page\">1</strong>", board);
                Assert.Contains("<a rel=\"next\" href=\"/board?id=1&amp;page=2\">Next</a>", board);
                Assert.Contains("<span class=\"tag\">Locked</span>", board);
                Assert.Contains("Started by Anna, 2 January 2026, 06:00", board);
                var second = await Page("/board?id=1&page=2");
                Assert.Contains(">Topic 6<", second);
                Assert.Contains(">Topic 1<", second);
                Assert.DoesNotContain(">Topic 7<", second);
                Assert.Contains("<td class=\"count\">44 replies<br>0 views</td>", second);
                Assert.Equal(second, await Page("/board?id=1&page=9"));

                // A topic: 20 of its 45 posts a page, numbered, its text escaped, its views counted.
                var topic = await Page("/topic?id=2&page=3");
                Assert.Contains("<title>Topic 1 · WebLingo Forum</title>", topic);
                Assert.Contains("› <a href=\"/board?id=1\">Welcome</a>", topic);
                Assert.Contains("<a href=\"#post-", topic);
                Assert.Contains(">#41</a>", topic);
                Assert.Contains(">#45</a>", topic);
                Assert.DoesNotContain(">#40</a>", topic);
                Assert.Contains("Post 45 of topic 1, &lt;b&gt;not bold&lt;/b&gt;", topic);
                Assert.Contains("<span class=\"note\">75 posts</span>", topic);
                Assert.Contains("<a rel=\"prev\" href=\"/topic?id=2&amp;page=2\">Previous</a>", topic);
                await Page("/topic?id=2");
                Assert.Contains("<td class=\"count\">44 replies<br>2 views</td>", await Page("/board?id=1&page=2"));
                Assert.Contains("This topic is locked: it takes no more replies.", await Page("/topic?id=31"));

                // What is not there, and what is never served.
                Assert.Contains("<h1>No such topic</h1>", await Page("/topic?id=99", HttpStatusCode.NotFound));
                Assert.Contains("<h1>No such board</h1>", await Page("/board?id=x", HttpStatusCode.NotFound));
                await Page("/lib/sql.wlg", HttpStatusCode.NotFound);
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

    private static (int, string, string) Run(params string[] arguments)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = WebLingoCommandLine.Run(arguments, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
