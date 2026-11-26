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
            Assert.Equal((WebLingoCommandLine.Success, $"Made the forum's database 'forum' on {provider}, version 6, with its administrator Anna.\n", ""),
                Run("run", setup, "name=Anna", "email=anna@example.com", "password=correct-horse"));
            Assert.Equal((WebLingoCommandLine.Success, "The forum's database 'forum' is at version 6 already: nothing to do.\n", ""),
                Run("run", setup));

            // 30 topics, each of one post but the first, of 45, on 2 January 2026 and the day before, the last locked; and a
            // second board, the staff's, which guests do not see, and members read only.
            var more = Path.Combine(copy, "setup", "more.wlg");
            File.WriteAllText(more, """
                import { Database } from "std:db";
                import { DateTime } from "std:datetime";
                import { insert } from "../site/lib/sql.wlg";
                import permissions from "../site/lib/permissions.wlg";
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
                    var staff = await insert(db, "boards", { category_id: 1, name: "Staff", description: "For the forum's staff.",
                        sort_order: 2, topic_count: 0, post_count: 0, last_post_id: null });
                    await permissions.giveDefaults(db, staff);
                    var kind = "SELECT id FROM member_groups WHERE kind = @kind";
                    await db.execute($"UPDATE board_permissions SET can_see = @no, can_read = @no WHERE board_id = @board AND group_id IN ({kind})",
                        { no: false, board: staff, kind: "guests" });
                    await db.execute("UPDATE board_permissions SET can_start = @no, can_reply = @no, can_edit = @no, can_delete = @no "
                        + $"WHERE board_id = @board AND group_id IN ({kind})", { no: false, board: staff, kind: "members" });
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

                using var bob = await MembersAsync(address, smtp);
                using var anna = await WritingAsync(address, smtp, bob);
                await ModerationAsync(address, anna, bob);
                await AdministrationAsync(address, anna, bob);
                await MessagesAsync(address, anna, bob);
                await SearchAsync(address, bob);
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
    /// A browser of the forum's, its cookies its own: a page's status and text, its line breaks LF, and, where it was sent
    /// on, the path it came to; and a form posted with the token against cross-site request forgery its page gives.
    /// </summary>
    private sealed class Browser(string address) : IDisposable
    {
        public HttpClient Client { get; } = new(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(address) };

        public async Task<(HttpStatusCode, string)> Get(string path)
        {
            var (status, body, _) = await Visit(path);
            return (status, body);
        }

        public async Task<(HttpStatusCode, string, string)> Visit(string path)
        {
            var response = await Client.GetAsync(path);
            return (response.StatusCode, (await response.Content.ReadAsStringAsync()).ReplaceLineEndings("\n"), response.RequestMessage!.RequestUri!.PathAndQuery);
        }

        public async Task<(HttpStatusCode, string, string)> Post(string path, params (string, string)[] fields)
        {
            // The form's page, the header's for signing out, as a browser has it before it posts; the front page's header
            // where the path has no page of its own, a handler's.
            var page = path == "/account/sign-out" ? "/"
                : path.Contains('?') && !path.StartsWith("/account/sign-in", StringComparison.Ordinal) ? path : path.Split('?')[0];
            var token = Token((await Get(page)).Item2);
            if (token.Length == 0) token = Token((await Get("/")).Item2);
            var response = await Client.PostAsync(path, new FormUrlEncodedContent([new("csrfToken", token), .. fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2))]));
            return (response.StatusCode, (await response.Content.ReadAsStringAsync()).ReplaceLineEndings("\n"), response.RequestMessage!.RequestUri!.PathAndQuery);
        }

        private static string Token(string page) => Regex.Match(page, "name=\"csrfToken\" value=\"([0-9a-f]{32})\"").Groups[1].Value;

        public void Dispose() => Client.Dispose();
    }

    /// <summary>
    /// The members' pages, as a newcomer, Bob, uses them: signing up, their address confirmed by the link mailed to them,
    /// their profile's time zone, signing out and in, too many failed sign-ins, a password reset by mail; and the members'
    /// list. Each form's token against cross-site request forgery read from the form, as a browser sends it. Gives Bob's
    /// browser, Bob signed in.
    /// </summary>
    private static async Task<Browser> MembersAsync(string address, SmtpTestServer smtp)
    {
        var bob = new Browser(address);
        Task<(HttpStatusCode, string)> Get(string path) => bob.Get(path);
        Task<(HttpStatusCode, string, string)> Post(string path, params (string, string)[] fields) => bob.Post(path, fields);
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
        var refused = await bob.Client.PostAsync("/account/profile", new FormUrlEncodedContent([new("csrfToken", "0".PadLeft(32, '0')), new("action", "profile")]));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("This form has expired: send it again.", await refused.Content.ReadAsStringAsync());
        (status, body, at) = await Post("/account/sign-out");
        Assert.Equal((HttpStatusCode.OK, "/"), (status, at));
        Assert.Contains(">Sign in</a>", body);
        Assert.Equal(HttpStatusCode.OK, (await Get("/account/profile")).Item1);
        Assert.EndsWith("/account/sign-in?back=/account/profile", (await bob.Visit("/account/profile")).Item3);

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
        return bob;
    }

    /// <summary>
    /// Writing, as Bob and Anna write: a topic started after its preview; a quote, replies, and the mail a watcher gets of
    /// them, once until they read the topic again; what is new to a member, and their first new post; a post and a title
    /// edited, a reply deleted, and a topic deleted with its only post, every count kept; and what a guest, a locked topic,
    /// another's post and a form from elsewhere are refused. Bob signed in, as the members' walk leaves him; gives Anna's
    /// browser, Anna signed in.
    /// </summary>
    private static async Task<Browser> WritingAsync(string address, SmtpTestServer smtp, Browser bob)
    {
        string Mail(int message) => smtp.Messages[message].Replace("=\r\n", "").Replace("=3D", "=");

        // A guest is sent to sign in first, and offered to; a topic has no form for them.
        using (var guest = new Browser(address))
        {
            Assert.Equal("/account/sign-in?back=/post/new?board=1", (await guest.Visit("/post/new?board=1")).Item3);
            Assert.Contains("Sign in to start a topic", (await guest.Get("/board?id=1")).Item2);
            var (_, page) = await guest.Get("/topic?id=2");
            Assert.Contains("Sign in to reply", page);
            Assert.DoesNotContain("id=\"reply\"", page);
            Assert.DoesNotContain("/post/reply?", page);
        }

        // Bob's preview: the post as it will look, its HTML escaped, nothing kept; a topic without a title refused; then
        // the topic, which he watches.
        var mails = smtp.Messages.Count;
        const string tea = "**Tea**, of course.\r\n\r\n<script>alert(1)</script>";
        Assert.Contains("<a class=\"button\" href=\"/post/new?board=1\">New topic</a>", (await bob.Get("/board?id=1")).Item2);
        var (status, body, at) = await bob.Post("/post/new?board=1", ("title", "Tea or coffee"), ("body", tea), ("action", "preview"));
        Assert.True(status == HttpStatusCode.OK, body);
        Assert.Contains("<p><strong>Tea</strong>, of course.</p>\n<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>", body);
        Assert.DoesNotContain("<script>alert", body);
        Assert.Contains(">**Tea**, of course.\n\n&lt;script&gt;alert(1)&lt;/script&gt;</textarea>", body);
        Assert.Contains("<td class=\"count\">31 topics<br>75 posts</td>", (await bob.Get("/")).Item2);
        (status, body, _) = await bob.Post("/post/new?board=1", ("title", " "), ("body", tea), ("action", "post"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("A topic needs a title.", body);
        (status, body, at) = await bob.Post("/post/new?board=1", ("title", "Tea or coffee"), ("body", tea), ("action", "post"));
        Assert.True(status == HttpStatusCode.OK && at == "/topic?id=32", at + "\n" + body);
        Assert.Contains("<h1>Tea or coffee</h1>", body);
        Assert.Contains("<div class=\"body\"><p><strong>Tea</strong>, of course.</p>\n<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>\n</div>", body);
        Assert.Contains("<div class=\"signature\"><p>Bob, of Lodz.</p>\n</div>", body);
        Assert.Contains(">Stop watching</button>", body);
        var home = (await bob.Get("/")).Item2;
        Assert.Contains("<td class=\"count\">32 topics<br>76 posts</td>", home);
        Assert.Contains("<a href=\"/topic?id=32\">Tea or coffee</a><br>\n    <span class=\"note\">by Bob,", home);

        // Anna finds the topic new, and is taken to its first new post; then it is new no more.
        var anna = new Browser(address);
        (status, body, at) = await anna.Post("/account/sign-in", ("name", "Anna"), ("password", "correct-horse"));
        Assert.Equal((HttpStatusCode.OK, "/"), (status, at));
        (_, body) = await anna.Get("/board?id=1");
        Assert.Contains("<a class=\"tag new\" href=\"/topic?id=32&amp;unread=1\">New</a>", body);
        Assert.Single(Regex.Matches(body, "class=\"tag new\""));
        Assert.Equal("/topic?id=32", (await anna.Visit("/topic?id=32&unread=1")).Item3);
        Assert.DoesNotContain("class=\"tag new\"", (await anna.Get("/board?id=1")).Item2);

        // Anna quotes Bob, who watches the topic he started, and is mailed of her reply; of her next, not, until he has read
        // the topic again.
        (_, body) = await anna.Get("/post/reply?topic=32&quote=76");
        Assert.Contains(">&gt; **Bob** wrote:\n&gt; **Tea**, of course.\n&gt;\n&gt; &lt;script&gt;alert(1)&lt;/script&gt;\n\n</textarea>", body);
        (status, body, at) = await anna.Post("/post/reply?topic=32", ("body", "> **Bob** wrote:\n> Tea\n\nCoffee, *always*."), ("action", "post"));
        Assert.True(status == HttpStatusCode.OK && at == "/topic?id=32", at + "\n" + body);
        Assert.Contains("<blockquote>\n<p><strong>Bob</strong> wrote:<br>\nTea</p>\n</blockquote>\n<p>Coffee, <em>always</em>.</p>", body);
        Assert.Equal(mails + 1, smtp.Messages.Count);
        Assert.Contains("Subject: Anna replied to Tea or coffee", Mail(mails));
        Assert.Contains("To: bob@example.com", Mail(mails));
        Assert.Contains("/topic?id=32&unread=1", Mail(mails));
        await anna.Post("/post/reply?topic=32", ("body", "Or tea."), ("action", "post"));
        Assert.Equal(mails + 1, smtp.Messages.Count);
        Assert.Equal("/topic?id=32", (await bob.Visit("/topic?id=32&unread=1")).Item3);
        await anna.Post("/post/reply?topic=32", ("body", "Both, then."), ("action", "post"));
        Assert.Equal(mails + 2, smtp.Messages.Count);

        // Bob edits his post, and the topic's title with it, the first post's; Anna's posts are not his to change, nor his
        // first post his to delete while the topic has replies.
        Assert.Contains("name=\"title\" value=\"Tea or coffee\"", (await bob.Get("/post/edit?id=76")).Item2);
        (status, body, at) = await bob.Post("/post/edit?id=76", ("title", "Tea, or coffee?"), ("body", "Tea, *of course*."), ("action", "post"));
        Assert.True(status == HttpStatusCode.OK && at == "/topic?id=32", at + "\n" + body);
        Assert.Contains("<h1>Tea, or coffee?</h1>", body);
        Assert.Contains("<p>Tea, <em>of course</em>.</p>", body);
        Assert.Contains(" · Edited ", body);
        Assert.Contains("href=\"/post/edit?id=76\"", body);
        Assert.DoesNotContain("href=\"/post/edit?id=77\"", body);
        Assert.DoesNotContain("href=\"/post/delete?id=76\"", body);
        Assert.Contains("href=\"/post/reply?topic=32&amp;quote=77\"", body);
        (status, body) = await bob.Get("/post/edit?id=77");
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("Only its author may change a post.", body);
        (status, body) = await bob.Get("/post/delete?id=76");
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("A topic&#39;s first post cannot be deleted while the topic has replies.", body);

        // Bob stops watching, and is mailed of no more replies; Anna deletes her last.
        (status, body, at) = await bob.Post("/post/watch", ("topic", "32"), ("action", "unwatch"));
        Assert.Equal((HttpStatusCode.OK, "/topic?id=32"), (status, at));
        Assert.Contains(">Watch this topic</button>", body);
        await anna.Post("/post/reply?topic=32", ("body", "Gone soon."), ("action", "post"));
        Assert.Equal(mails + 2, smtp.Messages.Count);
        (_, body) = await anna.Get("/post/delete?id=80");
        Assert.Contains("Delete this post?", body);
        Assert.Contains("<p>Gone soon.</p>", body);
        Assert.DoesNotContain("the topic is deleted with it", body);
        (status, body, at) = await anna.Post("/post/delete?id=80");
        Assert.Equal((HttpStatusCode.OK, "/topic?id=32"), (status, at));
        Assert.DoesNotContain("Gone soon.", body);
        Assert.Contains("<td class=\"count\">32 topics<br>79 posts</td>", (await anna.Get("/")).Item2);

        // A topic whose only post is deleted is deleted with it, and the board's counts and latest post with it.
        (status, body, at) = await bob.Post("/post/new?board=1", ("title", "Short-lived"), ("body", "Alone."), ("action", "post"));
        Assert.Equal((HttpStatusCode.OK, "/topic?id=33"), (status, at));
        Assert.Contains("It is the topic&#39;s only post: the topic is deleted with it.", (await bob.Get("/post/delete?id=81")).Item2);
        (status, body, at) = await bob.Post("/post/delete?id=81");
        Assert.Equal((HttpStatusCode.OK, "/board?id=1"), (status, at));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Get("/topic?id=33")).Item1);
        home = (await bob.Get("/")).Item2;
        Assert.Contains("<td class=\"count\">32 topics<br>79 posts</td>", home);
        Assert.Contains("<a href=\"/topic?id=32\">Tea, or coffee?</a>", home);
        Assert.Contains("<li>1 post</li>", (await bob.Get("/members/member?id=2")).Item2);

        // A locked topic takes no replies; a form whose token is not the browser's is refused, and nothing is kept.
        (_, body) = await bob.Get("/topic?id=31");
        Assert.DoesNotContain("id=\"reply\"", body);
        Assert.DoesNotContain("/post/reply?topic=31", body);
        (status, body) = await bob.Get("/post/reply?topic=31");
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("This topic is locked: it takes no more replies.", body);
        var refused = await bob.Client.PostAsync("/post/new?board=1", new FormUrlEncodedContent(
            [new("csrfToken", "0".PadLeft(32, '0')), new("title", "x"), new("body", "x"), new("action", "post")]));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("This form has expired: send it again.", await refused.Content.ReadAsStringAsync());
        Assert.Contains("<td class=\"count\">32 topics<br>79 posts</td>", (await bob.Get("/")).Item2);
        return anna;
    }

    /// <summary>
    /// Permissions and moderation: the staff's board, which a guest does not see and Bob reads but may not write on; a post
    /// Bob reports, and Anna, an administrator, resolves; his topic locked, pinned, unlocked, moved and back, deleted and
    /// restored; his post edited, and a reply of his deleted and restored from the log; and Bob banned, which keeps him from
    /// signing in, and the ban lifted. Each of Anna's actions in the moderation log.
    /// </summary>
    private static async Task ModerationAsync(string address, Browser anna, Browser bob)
    {
        // What each may see and do: the staff's board hidden from a guest; Bob reads it, but may not start a topic there;
        // Anna, an administrator, may, and moderates.
        using (var guest = new Browser(address))
        {
            Assert.DoesNotContain(">Staff</a>", (await guest.Get("/")).Item2);
            Assert.Equal(HttpStatusCode.NotFound, (await guest.Get("/board?id=2")).Item1);
        }
        var (status, body) = await bob.Get("/board?id=2");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("/post/new?board=2", body);
        Assert.DoesNotContain(">Moderation</a>", body);
        (status, body) = await bob.Get("/post/new?board=2");
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("Your groups may not start topics on this board.", body);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Get("/mod/reports")).Item1);
        Assert.Contains("<a class=\"button\" href=\"/post/new?board=2\">New topic</a>", (await anna.Get("/board?id=2")).Item2);
        Assert.Contains("<a href=\"/mod/reports\">Moderation</a>", (await anna.Get("/")).Item2);

        // Bob reports Anna's post; Anna finds the report, and resolves it.
        (status, body, var at) = await bob.Post("/post/report?id=77", ("reason", "Coffee? Really?"));
        Assert.True(status == HttpStatusCode.OK, body);
        Assert.Contains("Thank you: the moderators will look at it.", body);
        (_, body) = await anna.Get("/mod/reports");
        Assert.Contains("<p class=\"reason\">Coffee? Really?</p>", body);
        Assert.Contains("Reported by Bob,", body);
        var report = Regex.Match(body, "name=\"report\" value=\"(\\d+)\"").Groups[1].Value;
        (status, body, at) = await anna.Post("/mod/reports", ("report", report));
        Assert.Equal((HttpStatusCode.OK, "/mod/reports"), (status, at));
        Assert.Contains("There are no reports to deal with.", body);

        // Anna locks Bob's topic: Bob may no longer reply, nor edit his post; Anna, its moderator, still may. Pinned, it comes
        // first on its board; then unlocked.
        (status, body, at) = await anna.Post("/mod/topic", ("topic", "32"), ("action", "lock"));
        Assert.Equal((HttpStatusCode.OK, "/topic?id=32"), (status, at));
        Assert.Contains("This topic is locked: it takes no more replies.", body);
        Assert.Contains("<button name=\"action\" value=\"unlock\" class=\"secondary\">Unlock</button>", body);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Get("/post/reply?topic=32")).Item1);
        (status, body) = await bob.Get("/post/edit?id=76");
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("This topic is locked: its posts cannot be changed.", body);
        Assert.Equal(HttpStatusCode.OK, (await anna.Get("/post/edit?id=76")).Item1);
        await anna.Post("/mod/topic", ("topic", "32"), ("action", "pin"));
        await anna.Post("/mod/topic", ("topic", "32"), ("action", "unlock"));
        (_, body) = await bob.Get("/board?id=1");
        Assert.True(body.IndexOf(">Tea, or coffee?<", StringComparison.Ordinal) is > 0 and var tea
                    && tea < body.IndexOf(">Welcome to the forum<", StringComparison.Ordinal), body);

        // Anna edits Bob's post.
        (status, body, at) = await anna.Post("/post/edit?id=76", ("title", "Tea, or coffee?"), ("body", "Tea, *of course*. (Edited.)"), ("action", "post"));
        Assert.True(status == HttpStatusCode.OK && at == "/topic?id=32", at + "\n" + body);
        Assert.Contains("<p>Tea, <em>of course</em>. (Edited.)</p>", body);

        // Anna moves the topic to the staff's board, where Bob reads it but may not reply, and back; each board's counts
        // move with it.
        (status, body, at) = await anna.Post("/mod/topic", ("topic", "32"), ("action", "move"), ("board", "2"));
        Assert.Equal((HttpStatusCode.OK, "/topic?id=32"), (status, at));
        Assert.Contains("› <a href=\"/board?id=2\">Staff</a>", body);
        var home = (await anna.Get("/")).Item2;
        Assert.Contains("<td class=\"count\">31 topics<br>75 posts</td>", home);
        Assert.Contains("<td class=\"count\">1 topic<br>4 posts</td>", home);
        (status, body) = await bob.Get("/topic?id=32");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("id=\"reply\"", body);
        Assert.DoesNotContain("/post/edit?id=76", body);
        await anna.Post("/mod/topic", ("topic", "32"), ("action", "move"), ("board", "1"));
        Assert.Contains("<td class=\"count\">32 topics<br>79 posts</td>", (await anna.Get("/")).Item2);

        // Anna deletes the topic, which then only its moderators see, and restores it.
        (status, body, at) = await anna.Post("/mod/topic", ("topic", "32"), ("action", "delete"));
        Assert.Equal((HttpStatusCode.OK, "/board?id=1"), (status, at));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Get("/topic?id=32")).Item1);
        Assert.Contains("This topic is deleted: only its board&#39;s moderators see it.", (await anna.Get("/topic?id=32")).Item2);
        Assert.Contains("<td class=\"count\">31 topics<br>75 posts</td>", (await anna.Get("/")).Item2);
        await anna.Post("/mod/topic", ("topic", "32"), ("action", "restore"));
        Assert.Equal(HttpStatusCode.OK, (await bob.Get("/topic?id=32")).Item1);

        // Anna deletes a reply of Bob's, then restores it from the moderation log.
        (status, body, at) = await bob.Post("/post/reply?topic=32", ("body", "Tea!"), ("action", "post"));
        Assert.True(status == HttpStatusCode.OK && at == "/topic?id=32", at + "\n" + body);
        (status, body, at) = await anna.Post("/post/delete?id=82");
        Assert.Equal((HttpStatusCode.OK, "/topic?id=32"), (status, at));
        Assert.DoesNotContain("<p>Tea!</p>", body);
        Assert.Contains("<input type=\"hidden\" name=\"post\" value=\"82\">", (await anna.Get("/mod/log")).Item2);
        (status, body, at) = await anna.Post("/mod/post", ("post", "82"), ("action", "restore"));
        Assert.Equal((HttpStatusCode.OK, "/topic?id=32"), (status, at));
        Assert.Contains("<p>Tea!</p>", body);
        Assert.Contains("<td class=\"count\">32 topics<br>80 posts</td>", (await anna.Get("/")).Item2);

        // Anna bans Bob for a day: he is signed out, and may not sign in, told until when and why, until she lifts it. No
        // one but a moderator bans, and no one an administrator.
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Get("/mod/ban?member=1")).Item1);
        Assert.Contains("Bob is not banned.", (await anna.Get("/mod/ban?member=2")).Item2);
        (status, body, at) = await anna.Post("/mod/ban?member=2", ("length", "day"), ("reason", "Too much tea."), ("action", "ban"));
        Assert.True(status == HttpStatusCode.OK, body);
        Assert.Contains("Bob is banned until ", body);
        Assert.Contains(">Sign in</a>", (await bob.Get("/")).Item2);
        (status, body, _) = await bob.Post("/account/sign-in", ("name", "Bob"), ("password", "new-password"));
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("This account is banned until ", body);
        Assert.Contains("The reason given: Too much tea.", body);
        (_, body, _) = await anna.Post("/mod/ban?member=2", ("action", "lift"));
        Assert.Contains("Bob is not banned.", body);
        (status, body, at) = await bob.Post("/account/sign-in", ("name", "Bob"), ("password", "new-password"));
        Assert.Equal((HttpStatusCode.OK, "/"), (status, at));
        Assert.Contains(">Bob</a>", body);

        // The moderation log: each of Anna's actions, the latest first.
        (_, body) = await anna.Get("/mod/log");
        var actions = Regex.Matches(body, "<td>(Locked|Pinned|Unlocked|Edited a post|Moved|Deleted the topic|Restored the topic|"
                                          + "Deleted a post|Restored a post|Banned|Lifted the ban of|Resolved a report)</td>")
            .Select(m => m.Groups[1].Value).Reverse().ToArray();
        Assert.Equal(["Resolved a report", "Locked", "Pinned", "Unlocked", "Edited a post", "Moved", "Moved", "Deleted the topic",
                      "Restored the topic", "Deleted a post", "Restored a post", "Banned", "Lifted the ban of"], actions);
        Assert.Contains("from Welcome", body);
        Assert.Contains("from Staff", body);
    }

    /// <summary>
    /// Administration, as Anna, an administrator, uses it: the forum's settings, a time zone it does not know refused; a
    /// category and a board in it made, its groups' permissions changed, and both removed, a board with topics not; a group
    /// made and removed, the forum's own not; Bob made a moderator and back, Anna never taking herself out of the
    /// administrators; and a newcomer's address confirmed. A guest sent to sign in, and Bob refused.
    /// </summary>
    private static async Task AdministrationAsync(string address, Browser anna, Browser bob)
    {
        using (var guest = new Browser(address))
        {
            Assert.Equal("/account/sign-in?back=/admin/settings", (await guest.Visit("/admin/settings")).Item3);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Get("/admin")).Item1);
        Assert.DoesNotContain(">Administration</a>", (await bob.Get("/")).Item2);
        var (status, body) = await anna.Get("/admin");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<a href=\"/admin\">Administration</a>", body);

        // The settings: the forum renamed, its accent red, ten topics a page.
        var colors = new Dictionary<string, string> { ["accent"] = "#AA0000", ["accentText"] = "#ffffff", ["background"] = "#f6f7f9",
            ["surface"] = "#ffffff", ["text"] = "#1d232a", ["muted"] = "#5c6670", ["border"] = "#d9dee3" };
        (string, string)[] Settings(string zone) => [("name", "Tea Talk"), ("description", "About tea."), ("language", "en"), ("timeZone", zone),
            ("topicsPerPage", "10"), ("postsPerPage", "20"), ("signUp", "1"), ("confirmEmail", "1"), ("mailFrom", "Tea Talk <tea@example.com>"),
            ("address", ""), .. colors.Select(c => ("color." + c.Key, c.Value))];
        (status, body, _) = await anna.Post("/admin/settings", Settings("Mars/Olympus"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("That is not a time zone this server knows.", body);
        (status, body, var at) = await anna.Post("/admin/settings", Settings("Europe/Warsaw"));
        Assert.Equal((HttpStatusCode.OK, "/admin/settings?saved=1"), (status, at));
        Assert.Contains("Saved.", body);
        var home = (await bob.Get("/")).Item2;
        Assert.Contains("<title>Boards · Tea Talk</title>", home);
        Assert.Contains("--accent: #aa0000;", home);
        Assert.Contains("<a href=\"/board?id=1&amp;page=4\">4</a>", (await bob.Get("/board?id=1")).Item2);

        // A category, and a board in it, which guests see by default until its permissions say otherwise; then both removed.
        (status, body, at) = await anna.Post("/admin/boards", ("name", "Off topic"), ("action", "addCategory"));
        Assert.Equal((HttpStatusCode.OK, "/admin/boards"), (status, at));
        (status, body, at) = await anna.Post("/admin/boards", ("category", "2"), ("name", "Tea"), ("description", "All about tea."), ("action", "addBoard"));
        Assert.Equal((HttpStatusCode.OK, "/admin/board?id=3"), (status, at));
        using (var guest = new Browser(address))
        {
            var seen = (await guest.Get("/")).Item2;
            Assert.Contains("<h2>Off topic</h2>", seen);
            Assert.Contains("<a class=\"board\" href=\"/board?id=3\">Tea</a>", seen);
            (status, body, at) = await anna.Post("/admin/board?id=3", ("p.2.see", "1"), ("p.2.read", "1"), ("p.2.start", "1"), ("p.2.reply", "1"),
                ("p.3.see", "1"), ("p.3.read", "1"), ("p.3.moderate", "1"), ("action", "permissions"));
            Assert.Equal((HttpStatusCode.OK, "/admin/board?id=3&saved=1"), (status, at));
            Assert.DoesNotContain("Off topic", (await guest.Get("/")).Item2);
        }
        Assert.Contains("<a class=\"board\" href=\"/board?id=3\">Tea</a>", (await bob.Get("/")).Item2);
        (status, body, _) = await anna.Post("/admin/board?id=1", ("action", "remove"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Only a board that has never had a topic can be removed.", body);
        (status, _, at) = await anna.Post("/admin/board?id=3", ("action", "remove"));
        Assert.Equal((HttpStatusCode.OK, "/admin/boards"), (status, at));
        (status, _, at) = await anna.Post("/admin/boards", ("id", "2"), ("action", "removeCategory"));
        Assert.Equal((HttpStatusCode.OK, "/admin/boards"), (status, at));
        Assert.DoesNotContain("Off topic", (await bob.Get("/")).Item2);

        // A group made and removed; the forum's own stay.
        (status, body, at) = await anna.Post("/admin/groups", ("name", "Tea tasters"), ("action", "add"));
        Assert.Equal((HttpStatusCode.OK, "/admin/groups"), (status, at));
        var tasters = Regex.Match(body, "name=\"id\" value=\"(\\d+)\">\\s*<input name=\"name\" value=\"Tea tasters\"").Groups[1].Value;
        Assert.NotEqual("", tasters);
        (status, body, _) = await anna.Post("/admin/groups", ("id", "2"), ("action", "remove"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("The forum&#39;s own groups cannot be removed.", body);
        (status, body, at) = await anna.Post("/admin/groups", ("id", tasters), ("action", "remove"));
        Assert.Equal((HttpStatusCode.OK, "/admin/groups"), (status, at));
        Assert.DoesNotContain("Tea tasters", body);

        // Bob made a moderator, which gives him the moderators' pages, and back; Anna never takes herself out of the
        // administrators.
        (status, _, at) = await anna.Post("/admin/member?id=2", ("g.2", "1"), ("g.3", "1"), ("action", "groups"));
        Assert.Equal((HttpStatusCode.OK, "/admin/member?id=2&saved=1"), (status, at));
        Assert.Contains("<a href=\"/mod/reports\">Moderation</a>", (await bob.Get("/")).Item2);
        Assert.Equal(HttpStatusCode.OK, (await bob.Get("/mod/reports")).Item1);
        await anna.Post("/admin/member?id=2", ("g.2", "1"), ("action", "groups"));
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Get("/mod/reports")).Item1);
        (status, body, _) = await anna.Post("/admin/member?id=1", ("g.2", "1"), ("action", "groups"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("You cannot take yourself out of the administrators&#39; group.", body);

        // A newcomer, Carol, whose mail never came: Anna confirms her address, and Carol signs in.
        using var carol = new Browser(address);
        (_, body, _) = await carol.Post("/account/sign-up", ("name", "Carol"), ("email", "carol@example.com"), ("password", "carols-password"),
            ("passwordAgain", "carols-password"));
        Assert.Contains("We sent a link to carol@example.com.", body);
        Assert.Contains(">Carol</a> <span class=\"tag\">Unconfirmed</span>", (await anna.Get("/admin/members")).Item2);
        (status, _, at) = await anna.Post("/admin/member?id=3", ("action", "confirm"));
        Assert.Equal((HttpStatusCode.OK, "/admin/member?id=3&saved=1"), (status, at));
        (status, body, at) = await carol.Post("/account/sign-in", ("name", "Carol"), ("password", "carols-password"));
        Assert.Equal((HttpStatusCode.OK, "/"), (status, at));
        Assert.Contains(">Carol</a>", body);
    }

    /// <summary>
    /// Private messages: Bob writes to Anna and Carol, a name no member has refused, and himself alone; each finds the
    /// conversation new until they read it; Anna replies; Carol leaves it, and reads it no more. A guest is sent to sign in,
    /// and no one reads a conversation they are not in.
    /// </summary>
    private static async Task MessagesAsync(string address, Browser anna, Browser bob)
    {
        using (var guest = new Browser(address))
        {
            Assert.Equal("/account/sign-in?back=/messages", (await guest.Visit("/messages")).Item3);
        }
        Assert.Contains("<a href=\"/messages/new?to=Anna\">Send a message</a>", (await bob.Get("/members/member?id=1")).Item2);
        var (status, body, at) = await bob.Post("/messages/new", ("to", "Anna, Nobody"), ("subject", "Tea party"), ("body", "Come at **five**."), ("action", "post"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("No member has these names: Nobody.", body);
        (_, body, _) = await bob.Post("/messages/new", ("to", "Bob"), ("subject", "Tea party"), ("body", "Come at **five**."), ("action", "post"));
        Assert.Contains("Name at least one other member.", body);
        (status, body, at) = await bob.Post("/messages/new", ("to", "anna, CAROL"), ("subject", "Tea party"), ("body", "Come at **five**."), ("action", "post"));
        Assert.Equal((HttpStatusCode.OK, "/messages/conversation?id=1"), (status, at));
        Assert.Contains("In this conversation: Anna, Bob, Carol", body);
        Assert.Contains("<div class=\"body\"><p>Come at <strong>five</strong>.</p>\n</div>", body);

        // Anna finds it new, reads it, and replies; then it is new to Bob once, and to Carol twice.
        Assert.Contains("<a href=\"/messages\">Messages (1)</a>", (await anna.Get("/")).Item2);
        (_, body) = await anna.Get("/messages");
        Assert.Contains("<span class=\"tag new\">1 new</span>", body);
        Assert.Contains(">Tea party</a>", body);
        await anna.Get("/messages/conversation?id=1");
        Assert.Contains("<a href=\"/messages\">Messages</a>", (await anna.Get("/")).Item2);
        (status, body, at) = await anna.Post("/messages/conversation?id=1", ("body", "Five it is."), ("action", "post"));
        Assert.Equal((HttpStatusCode.OK, "/messages/conversation?id=1"), (status, at));
        Assert.Contains("<p>Five it is.</p>", body);
        Assert.Contains("<a href=\"/messages\">Messages (1)</a>", (await bob.Get("/")).Item2);
        using var carol = new Browser(address);
        (_, body, _) = await carol.Post("/account/sign-in", ("name", "Carol"), ("password", "carols-password"));
        Assert.Contains("<a href=\"/messages\">Messages (2)</a>", body);

        // Carol leaves: the conversation is no longer hers, but still Anna's and Bob's. No one reads one they are not in.
        (status, body, at) = await carol.Post("/messages/conversation?id=1", ("action", "leave"));
        Assert.Equal((HttpStatusCode.OK, "/messages"), (status, at));
        Assert.Contains("You are in no conversation yet.", body);
        Assert.Equal(HttpStatusCode.NotFound, (await carol.Get("/messages/conversation?id=1")).Item1);
        Assert.Contains("In this conversation: Anna, Bob</p>", (await bob.Get("/messages/conversation?id=1")).Item2);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Get("/messages/conversation?id=99")).Item1);
    }

    /// <summary>
    /// The search, of the posts the walks before it wrote and of the forum's first, which <c>setup.wlg</c> indexed: by
    /// every word given, in titles too, on a board and by an author, never a deleted post's or topic's; words too short and
    /// an author no member is refused.
    /// </summary>
    private static async Task SearchAsync(string address, Browser bob)
    {
        using var guest = new Browser(address);
        var (status, body) = await guest.Get("/search?q=coffee");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<p class=\"note\">2 posts</p>", body);
        var anna = body.IndexOf("href=\"/topic?id=32#post-77\"", StringComparison.Ordinal);
        Assert.True(anna > 0 && anna < body.IndexOf("href=\"/topic?id=32#post-76\"", StringComparison.Ordinal), body);
        Assert.Contains("<p>&gt; **Bob** wrote: &gt; Tea Coffee, *always*.</p>", body);
        Assert.Contains("<p class=\"note\">1 post</p>", (await guest.Get("/search?q=coffee&author=Anna")).Item2);
        Assert.Contains("<p class=\"note\">1 post</p>", (await guest.Get("/search?q=Tea+ALWAYS")).Item2);
        Assert.Contains("<p class=\"note\">1 post</p>", (await guest.Get("/search?q=welcome")).Item2);
        Assert.Contains("No post has all of those words.", (await guest.Get("/search?q=gone")).Item2);
        Assert.Contains("No post has all of those words.", (await guest.Get("/search?q=alone")).Item2);
        (status, body) = await guest.Get("/search?q=x");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Give a word of two letters or more.", body);
        (status, body) = await guest.Get("/search?q=tea&author=Nobody");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("No member has that name.", body);
        Assert.Contains("<p class=\"note\">4 posts</p>", (await bob.Get("/search?q=tea&board=1")).Item2);
        Assert.Contains("<a href=\"/search\">Search</a>", (await bob.Get("/")).Item2);
    }

    private static (int, string, string) Run(params string[] arguments)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = WebLingoCommandLine.Run(arguments, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
