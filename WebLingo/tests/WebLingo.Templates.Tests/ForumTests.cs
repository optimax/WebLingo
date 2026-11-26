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
            Assert.Equal((WebLingoCommandLine.Success, $"Made the forum's database 'forum' on {provider}, version 3, with its administrator Anna.\n", ""),
                Run("run", setup, "name=Anna", "email=anna@example.com", "password=correct-horse"));
            Assert.Equal((WebLingoCommandLine.Success, "The forum's database 'forum' is at version 3 already: nothing to do.\n", ""),
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

                using var bob = await MembersAsync(address, smtp);
                await WritingAsync(address, smtp, bob);
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
    /// another's post and a form from elsewhere are refused. Bob signed in, as the members' walk leaves him.
    /// </summary>
    private static async Task WritingAsync(string address, SmtpTestServer smtp, Browser bob)
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
        using var anna = new Browser(address);
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
    }

    private static (int, string, string) Run(params string[] arguments)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = WebLingoCommandLine.Run(arguments, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
