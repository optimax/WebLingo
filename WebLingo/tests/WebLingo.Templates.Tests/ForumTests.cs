using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    /// <c>setup.wlg</c>, as its administrator makes it, then given 30 topics more, one of 45 posts, and served, its mail
    /// sent to the tests' SMTP server; its pages read as a reader reads them, and its members' pages used as a member
    /// uses them.
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
        using var smtp = new SmtpTestServer { Tls = SmtpTestTls.None }.Start();
        try
        {
            var databases = new Dictionary<string, string> { ["forum"] = connection ?? "sqlite:../data/forum.db" };
            if (connection is not null)
            {
                File.WriteAllText(Path.Combine(copy, "setup", "weblingo.config"),
                    JsonSerializer.Serialize(new Dictionary<string, object> { ["databases"] = databases }));
            }
            File.WriteAllText(Path.Combine(copy, "site", "wasp.config"), JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["databases"] = databases,
                ["mail"] = new Dictionary<string, object> { ["default"] = new Dictionary<string, object> { ["host"] = "127.0.0.1", ["port"] = smtp.Port, ["tls"] = "none" } }
            }));
            var setup = Path.Combine(copy, "setup", "setup.wlg");
            Assert.Equal((WebLingoCommandLine.Success, $"Made the forum's database 'forum' on {provider}, version 2, with its administrator Anna.\n", ""),
                Run("run", setup, "name=Anna", "email=anna@example.com", "password=correct-horse"));
            Assert.Equal((WebLingoCommandLine.Success, "The forum's database 'forum' is at version 2 already: nothing to do.\n", ""),
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
                // A page's text, its line breaks LF, whichever its templates' files have: a checkout on Windows may give them CRLF.
                async Task<string> Page(string path, HttpStatusCode status = HttpStatusCode.OK)
                {
                    var response = await client.GetAsync(path);
                    var body = (await response.Content.ReadAsStringAsync()).ReplaceLineEndings("\n");
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
                // A page past the last is the last, but for the link to sign in, which comes back to the page asked for.
                Assert.Equal(second, (await Page("/board?id=1&page=9")).Replace("page%3D9", "page%3D2"));

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

                await MembersAsync(address, smtp);
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

    /// <summary>
    /// The members' pages, as a newcomer, Bob, uses them: signing up, their address confirmed by the link mailed to them,
    /// their profile's time zone, signing out and in, too many failed sign-ins, a password reset by mail; and the members'
    /// list. Each form's token against cross-site request forgery read from the form, as a browser sends it.
    /// </summary>
    private static async Task MembersAsync(string address, SmtpTestServer smtp)
    {
        using var bob = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(address) };
        async Task<(HttpStatusCode, string)> Get(string path)
        {
            var response = await bob.GetAsync(path);
            return (response.StatusCode, (await response.Content.ReadAsStringAsync()).ReplaceLineEndings("\n"));
        }
        async Task<(HttpStatusCode, string, string)> Post(string path, params (string, string)[] fields)
        {
            // The form's page, the header's for signing out, as a browser has it before it posts.
            var page = path == "/account/sign-out" ? "/"
                : path.Contains('?') && !path.StartsWith("/account/sign-in", StringComparison.Ordinal) ? path : path.Split('?')[0];
            var (_, form) = await Get(page);
            var token = Regex.Match(form, "name=\"csrfToken\" value=\"([0-9a-f]{32})\"").Groups[1].Value;
            var response = await bob.PostAsync(path, new FormUrlEncodedContent([new("csrfToken", token), .. fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2))]));
            return (response.StatusCode, (await response.Content.ReadAsStringAsync()).ReplaceLineEndings("\n"), response.RequestMessage!.RequestUri!.PathAndQuery);
        }
        string Link(int message, string page)
        {
            var mail = smtp.Messages[message].Replace("=\r\n", "").Replace("=3D", "=");
            return Regex.Match(mail, "/account/" + page + "\\?token=([0-9a-f]{64})").Value;
        }

        // Signing up: a form that is wrong said so, a name taken refused, then Bob made and mailed a link.
        var (status, body, _) = await Post("/account/sign-up", ("name", "Bob"), ("email", "bob@example.com"), ("password", "bobs-password"), ("passwordAgain", "other"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("The two passwords differ.", body);
        Assert.Contains("value=\"bob@example.com\"", body);
        (status, body, _) = await Post("/account/sign-up", ("name", "anna"), ("email", "ANNA@example.com"), ("password", "bobs-password"), ("passwordAgain", "bobs-password"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("That name is someone&#39;s already.", body);
        Assert.Contains("That address has an account already", body);
        (status, body, _) = await Post("/account/sign-up", ("name", "Bob"), ("email", "bob@example.com"), ("password", "bobs-password"), ("passwordAgain", "bobs-password"));
        Assert.True(status == HttpStatusCode.OK, body);
        Assert.Contains("We sent a link to bob@example.com.", body);
        Assert.Single(smtp.Messages);
        Assert.Contains("Subject: Confirm your address for WebLingo Forum", smtp.Messages[0]);
        Assert.Contains("To: bob@example.com", smtp.Messages[0]);
        var confirm = Link(0, "confirm");
        Assert.StartsWith("/account/confirm?token=", confirm);

        // Signing in before confirming: a new link mailed; the first still confirms, once, and signs Bob in.
        (status, body, _) = await Post("/account/sign-in", ("name", "bob"), ("password", "bobs-password"));
        Assert.Contains("Your address is not confirmed yet: we sent you a new link, to bob@example.com.", body);
        Assert.Equal(2, smtp.Messages.Count);
        (status, body) = await Get(confirm);
        Assert.True(status == HttpStatusCode.OK, body);
        Assert.Contains("Your address is confirmed, and you are signed in. Welcome, Bob.", body);
        (status, body) = await Get(confirm);
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("That link is not valid any more", body);
        (_, body) = await Get("/");
        Assert.Contains("<a href=\"/account/profile\">Your profile</a>", body);
        Assert.DoesNotContain(">Sign in<", body);

        // The profile: a time zone, Warsaw's, in which the board's dates then show; one the server does not know refused.
        (status, body, var at) = await Post("/account/profile", ("action", "profile"), ("timeZone", "Europe/Warsaw"), ("language", "en"), ("signature", "Bob, of Lodz."));
        Assert.True(status == HttpStatusCode.OK && at == "/account/profile?saved=1", at + "\n" + body);
        Assert.Contains("Your profile is saved.", body);
        Assert.Contains("value=\"Europe/Warsaw\"", body);
        Assert.Contains("Started by Anna, 2 January 2026, 07:00", (await Get("/board?id=1")).Item2);
        (status, body, _) = await Post("/account/profile", ("action", "profile"), ("timeZone", "Mars/Olympus"), ("language", ""), ("signature", ""));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("That is not a time zone this server knows.", body);

        // A form whose token is not the browser's refused; signing out, and the header says so.
        var refused = await bob.PostAsync("/account/profile", new FormUrlEncodedContent([new("csrfToken", "0".PadLeft(32, '0')), new("action", "profile")]));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("This form has expired: send it again.", await refused.Content.ReadAsStringAsync());
        (status, body, at) = await Post("/account/sign-out");
        Assert.Equal((HttpStatusCode.OK, "/"), (status, at));
        Assert.Contains(">Sign in</a>", body);
        Assert.Equal(HttpStatusCode.OK, (await Get("/account/profile")).Item1);
        Assert.EndsWith("/account/sign-in?back=/account/profile", (await bob.GetAsync("/account/profile")).RequestMessage!.RequestUri!.PathAndQuery);

        // Signing in, back where Bob was, but never to another site; five failures make the sixth wait, even with the right
        // password.
        (status, body, at) = await Post("/account/sign-in?back=/topic?id=2", ("name", "bob@example.com"), ("password", "bobs-password"));
        Assert.Equal((HttpStatusCode.OK, "/topic?id=2"), (status, at));
        await Post("/account/sign-out");
        (status, body, at) = await Post("/account/sign-in?back=//attacker.example/", ("name", "Bob"), ("password", "bobs-password"));
        Assert.Equal((HttpStatusCode.OK, "/"), (status, at));
        await Post("/account/sign-out");
        for (var i = 0; i < 5; i++)
        {
            (status, body, _) = await Post("/account/sign-in", ("name", "Bob"), ("password", "wrong-password"));
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("That name or address and that password do not match.", body);
        }
        (status, body, _) = await Post("/account/sign-in", ("name", "Bob"), ("password", "bobs-password"));
        Assert.Equal((HttpStatusCode)429, status);
        Assert.Contains("Too many sign-ins to that account failed", body);
        (status, body, _) = await Post("/account/sign-in", ("name", "Nobody"), ("password", "bobs-password"));
        Assert.Contains("That name or address and that password do not match.", body);

        // A password reset by mail, for a member's address only, the answer the same for any; the new password signs
        // Bob in, and the failures are forgotten.
        (status, body, _) = await Post("/account/forgot", ("email", "nobody@example.com"));
        Assert.Contains("If nobody@example.com is a member&#39;s, we sent it a link", body);
        Assert.Equal(2, smtp.Messages.Count);
        (status, body, _) = await Post("/account/forgot", ("email", "BOB@example.com"));
        Assert.Contains("If BOB@example.com is a member&#39;s, we sent it a link", body);
        Assert.Equal(3, smtp.Messages.Count);
        Assert.Contains("Subject: Reset your password for WebLingo Forum", smtp.Messages[2]);
        var reset = Link(2, "reset");
        (status, body, _) = await Post(reset, ("token", reset[^64..]), ("password", "new-password"), ("passwordAgain", "new-pass"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("The two passwords differ.", body);
        (status, body, at) = await Post(reset, ("token", reset[^64..]), ("password", "new-password"), ("passwordAgain", "new-password"));
        Assert.Equal((HttpStatusCode.OK, "/"), (status, at));
        Assert.Contains(">Bob</a>", body);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(reset)).Item1);
        await Post("/account/sign-out");
        (status, body, at) = await Post("/account/sign-in", ("name", "Bob"), ("password", "new-password"));
        Assert.Equal((HttpStatusCode.OK, "/"), (status, at));

        // The members, by name, and one member's page.
        (_, body) = await Get("/members");
        var table = body[body.IndexOf("<table class=\"list members\">", StringComparison.Ordinal)..];
        Assert.True(table.IndexOf(">Anna</a>", StringComparison.Ordinal) is > 0 and var anna && anna < table.IndexOf(">Bob</a>", StringComparison.Ordinal), body);
        (_, body) = await Get("/members/member?id=2");
        Assert.Contains("<h1>Bob</h1>", body);
        Assert.Contains("Bob, of Lodz.", body);
        Assert.Equal(HttpStatusCode.NotFound, (await Get("/members/member?id=99")).Item1);
    }

    private static (int, string, string) Run(params string[] arguments)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = WebLingoCommandLine.Run(arguments, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
