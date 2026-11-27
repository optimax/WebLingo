using System.Text;
using System.Text.RegularExpressions;
using WebLingo.Data.DotNet;
using WebLingo.Hosting.Abstractions;
using WebLingo.Hosting.DotNet;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// Sessions in a database (specification, section 6.12, "Sessions and cookies"; "Sessions and cookies, parts", part 3), on
/// SQLite, a file of the test's own: a table made by its administrator, or by the store where the configuration says
/// <c>"create": true</c>, a missing one an error naming the command that makes it; a session's values and its lock in its
/// row, which every process serving the site shares, one request of a session at a time across them; a lock a process
/// that ended held ending within a minute; a session unused for its timeout ended; and a table of a later schema reported.
/// </summary>
public sealed class DatabaseSessionTests : IDisposable
{
    private static readonly Regex Cookie = new("^WASPSESSIONID=([0-9a-f]{32}); Path=/; HttpOnly; SameSite=Lax$");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wasp-sessions-" + Guid.NewGuid().ToString("N"));
    private readonly DotNetDatabases _databases;

    public DatabaseSessionTests()
    {
        Directory.CreateDirectory(_folder);
        _databases = new DotNetDatabases(new Dictionary<string, string> { ["main"] = "sqlite:sessions.db" }, _folder);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void AMissingTableIsAnErrorThatNamesTheCommandThatMakesIt()
    {
        var app = Load(Store(create: false), ("/site/index.wasp", "<% param request; request.session[\"a\"] = 1; %>"),
            ("/site/look.wasp", "<% param request; try { var s = request.session; } catch (DatabaseError e) { %><%= e.message %><% } %>"));
        Assert.Contains("error WL3015: The session cannot be made: the database &#39;main&#39; has no table of sessions &#39;wasp_sessions&#39;, "
                        + "or cannot read it (SQLite Error 1: &#39;no such table: wasp_sessions&#39;): make it with "
                        + "&#39;wasp sessions create --database main&#39;.", Get(app, "/").Body);
        // A request whose cookie names a session finds the store failing before its page runs, and its page reads why.
        Assert.StartsWith("The session cannot be read: the database &#39;main&#39; has no table of sessions",
            Get(app, "/look", "0123456789abcdef0123456789abcdef").Body);
    }

    [Fact]
    public void SessionsAreKeptInTheTableTheStoreMakes()
    {
        var store = Store(create: true);
        var app = Load(store,
            ("/site/index.wasp", "<% param request; var n = (request.session?[\"n\"] ?? 0) + 1; request.session[\"n\"] = n;\n"
                                 + "request.session[\"kinds\"] = { x: 5.0, list: [1, \"two\", null, true], deep: { name: \"\\u{1F600}\" } }; %><%= n %>"),
            ("/site/look.wasp", "<% session readonly; param request; %><%- request.session?[\"kinds\"] %>"),
            ("/site/renew.wasp", "<% param request; request.session.renew(); %><%= request.session[\"n\"] %>"),
            ("/site/end.wasp", "<% param request; request.session.end(); %>ended"));
        var (body, response) = Get(app, "/");
        Assert.Equal("1", body);
        var id = Cookie.Match(Assert.Single(response.Headers.GetValues("Set-Cookie"))).Groups[1].Value;
        Assert.Equal("2", Get(app, "/", id).Body);
        Assert.Equal("{\"x\": 5.0, \"list\": [1, \"two\", null, true], \"deep\": {\"name\": \"\U0001F600\"}}", Get(app, "/look", id).Body);
        // A store of another process, on the same table, finds it: the table is the sessions'.
        Assert.Equal("3", Get(Load(Store(create: false), ("/site/index.wasp", "<% param request; var n = request.session[\"n\"] + 1; "
                                                            + "request.session[\"n\"] = n; %><%= n %>")), "/", id).Body);

        (body, response) = Get(app, "/renew", id);
        var renewed = Cookie.Match(Assert.Single(response.Headers.GetValues("Set-Cookie"))).Groups[1].Value;
        Assert.Equal("3", body);
        Assert.Equal("", Get(app, "/look", id).Body);
        Assert.Equal("4", Get(app, "/", renewed).Body);
        Assert.Equal("ended", Get(app, "/end", renewed).Body);
        Assert.Equal("", Get(app, "/look", renewed).Body);
        Assert.Equal(1, Rows()); // Its schema's row alone.
    }

    [Fact]
    public void ARequestWaitsForTheLockAnotherProcessHolds()
    {
        var clock = new ManualClock();
        var (first, second) = (Load(Store(create: true), clock, Pages()), Load(Store(create: true), clock, Pages()));
        var id = Cookie.Match(Get(first, "/set").Response.Headers.GetValues("Set-Cookie")[0]).Groups[1].Value;

        var holding = Begin(first, "/slow", id, toTimer: true);
        Assert.Equal(50L, holding.Outcome.WaitMilliseconds);
        var waiting = Begin(second, "/look", id);
        // Read again every 100 ms, it stays waiting while the first process holds the lock.
        Thread.Sleep(350);
        Assert.Equal(WebLingoRequestOutcomeKind.Waiting, waiting.Outcome.Kind);
        clock.Milliseconds += 50;
        Finish(holding);
        Finish(waiting);
        Assert.Equal("2", Body(waiting.Response));
    }

    [Fact]
    public void ALockWaitedForTooLongIsASessionLockError()
    {
        var clock = new ManualClock();
        var app = Load(Store(create: true, lockTimeout: TimeSpan.FromMilliseconds(300)), clock, Pages());
        var id = Cookie.Match(Get(app, "/set").Response.Headers.GetValues("Set-Cookie")[0]).Groups[1].Value;
        var holding = Begin(app, "/slow", id, toTimer: true);
        var waiting = Begin(app, "/locked", id);
        Finish(waiting);
        Assert.Equal("WL3024", Body(waiting.Response));
        clock.Milliseconds += 50;
        Finish(holding);
    }

    [Fact]
    public void ALockAProcessThatEndedHeldEndsWithinAMinute()
    {
        const string id = "0123456789abcdef0123456789abcdef";
        long now = 1_000_000;
        var gone = Store(create: true, now: () => now);
        var store = Store(create: true, TimeSpan.FromSeconds(1), () => now);
        Assert.True(gone.Create(id)); // Its process ends holding it, never closing it.

        // While the lock's life lasts, a request waits for it, reading the table again every 100 ms, until its own lock
        // timeout passes: awaited here, so that none of its reading is left running after the test, as it was on
        // windows-latest, which then could not remove the database (24 November 2026).
        WebLingoSessionOpening? waited = null;
        Assert.Equal(WebLingoSessionOpened.Waiting, store.Open(id, true, given => waited = given).Kind);
        Assert.False(SpinWait.SpinUntil(() => waited is not null, TimeSpan.FromMilliseconds(250)));
        now += 1_000;
        Assert.True(SpinWait.SpinUntil(() => waited is not null, TimeSpan.FromSeconds(10)));
        Assert.Equal(WebLingoSessionOpened.TimedOut, waited!.Kind);

        // Once its life has passed, the lock is free.
        now += (long)WebLingoDatabaseSessions.LockLife.TotalMilliseconds;
        Assert.Equal(WebLingoSessionOpened.Found, Open(store, id, locked: true, TimeSpan.FromSeconds(1)).Kind);
        // Each store lets go of what it holds, its renewals ended with it.
        store.Remove(id);
        gone.Remove(id);
    }

    [Fact]
    public void ASessionEndsOnceUnusedForItsTimeout()
    {
        long now = 1_000_000;
        var store = Store(create: true, now: () => now);
        Assert.True(store.Create("0123456789abcdef0123456789abcdef"));
        Close(store, "0123456789abcdef0123456789abcdef", new Dictionary<string, string> { ["a"] = "1" });
        now += 19 * 60_000;
        var found = Open(store, "0123456789abcdef0123456789abcdef", locked: false, TimeSpan.FromSeconds(1));
        Assert.Equal(("1", WebLingoSessionOpened.Found), (found.Values?["a"], found.Kind));
        now += 20 * 60_000;
        Assert.Equal(WebLingoSessionOpened.NotFound, Open(store, "0123456789abcdef0123456789abcdef", locked: false, TimeSpan.FromSeconds(1)).Kind);
        // Swept, once a minute at most, as a request comes.
        now += 61_000;
        Open(store, "fedcba9876543210fedcba9876543210", locked: false, TimeSpan.FromSeconds(1));
        Assert.Equal(1, Rows());
    }

    [Fact]
    public void ATableOfALaterSchemaIsReported()
    {
        using (var connection = _databases.Open("main")) WebLingoDatabaseSessions.MakeTable(connection, "wasp_sessions");
        using (var connection = _databases.Open("main")) connection.Execute("UPDATE wasp_sessions SET data = '{\"schema\": 2}' WHERE id = 'schema'", [], null);
        var failure = Assert.Throws<WebLingoSessionStoreException>(() => Store(create: true).Create("0123456789abcdef0123456789abcdef"));
        Assert.Equal("the table of sessions 'wasp_sessions' of the database 'main' was made by a later WASP, its schema 2, where this WASP "
                     + "reads schema 1", failure.Message);

        using (var connection = _databases.Open("main")) connection.Execute("DELETE FROM wasp_sessions WHERE id = 'schema'", [], null);
        failure = Assert.Throws<WebLingoSessionStoreException>(() => Store(create: true).Create("0123456789abcdef0123456789abcdef"));
        Assert.Equal("the table 'wasp_sessions' of the database 'main' is not a table of sessions WASP made: "
                     + "it has no row 'schema' giving its schema", failure.Message);
    }

    [Fact]
    public void TheStatementsThatMakeTheTableAreEachDatabasesOwn()
    {
        Assert.Equal(
        [
            "CREATE TABLE s (id VARCHAR(32) NOT NULL PRIMARY KEY, data NVARCHAR(MAX) NOT NULL, used BIGINT NOT NULL, locked_until BIGINT NULL)",
            "CREATE INDEX s_used ON s (used)",
            "INSERT INTO s (id, data, used, locked_until) VALUES ('schema', '{\"schema\": 1}', 9223372036854775807, NULL)"
        ], WebLingoDatabaseSessions.CreateStatements("sqlserver", "s"));
        Assert.EndsWith(") CHARACTER SET utf8mb4 COLLATE utf8mb4_bin", WebLingoDatabaseSessions.CreateStatements("mysql", "s")[0]);
        Assert.StartsWith("CREATE TABLE s (id VARCHAR(32) NOT NULL PRIMARY KEY, data TEXT NOT NULL",
            WebLingoDatabaseSessions.CreateStatements("postgresql", "s")[0]);
        Assert.Throws<ArgumentException>(() => new WebLingoDatabaseSessions(_databases, "main", "drop table x", true, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1)));
    }

    private static (string Path, string Text)[] Pages() =>
    [
        ("/site/set.wasp", "<% param request; request.session[\"a\"] = 0; %>"),
        ("/site/slow.wasp", "<% param request; import { delay } from \"std:system\"; request.session[\"a\"] = 1; await delay(50); request.session[\"a\"] = 2; %>"),
        ("/site/look.wasp", "<% param request; %><%= request.session[\"a\"] %>"),
        ("/site/locked.wasp", "<% param request; try { var s = request.session; } catch (SessionLockError e) { %><%= e.code %><% } %>")
    ];

    private WebLingoDatabaseSessions Store(bool create, TimeSpan? lockTimeout = null, Func<long>? now = null) =>
        new(_databases, "main", "wasp_sessions", create, TimeSpan.FromMinutes(20), lockTimeout ?? TimeSpan.FromSeconds(30), null, now);

    /// <summary>The rows of the table, its schema's among them.</summary>
    private int Rows()
    {
        using var connection = _databases.Open("main");
        return connection.Query("SELECT id FROM wasp_sessions", [], null).Rows.Count;
    }

    private static WebLingoSessionOpening Open(IWebLingoSessionStore store, string id, bool locked, TimeSpan within)
    {
        WebLingoSessionOpening? later = null;
        var opening = store.Open(id, locked, given => later = given);
        if (opening.Kind != WebLingoSessionOpened.Waiting) return opening;
        return SpinWait.SpinUntil(() => later is not null, within) ? later! : opening;
    }

    private static void Close(IWebLingoSessionStore store, string id, IReadOnlyDictionary<string, string> values)
    {
        var done = false;
        if (!store.Close(id, values, () => done = true)) Assert.True(SpinWait.SpinUntil(() => done, TimeSpan.FromSeconds(10)));
    }

    private static WebLingoApplication Load(IWebLingoSessionStore store, params (string Path, string Text)[] files) => Load(store, null, files);

    private static WebLingoApplication Load(IWebLingoSessionStore store, ManualClock? clock, params (string Path, string Text)[] files)
    {
        var site = new MemoryFileSystem();
        foreach (var (path, text) in files) site.Add(path, text);
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions("/site", site)
        {
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry(new WebLingoDelayFunction())),
            Mode = WebLingoMode.Development,
            Sessions = store,
            Crypto = new DotNetCrypto(),
            Clock = clock,
            Scheduler = () => new DotNetSchedulerHost()
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }

    private static WebLingoBufferedRequest Request(string path, string? id) =>
        new("GET", path, headers: id is null ? null : [("Cookie", "WASPSESSIONID=" + id)]);

    /// <summary>A request run to its end on the test's thread, waiting there for the database.</summary>
    private static (string Body, WebLingoBufferedResponse Response) Get(WebLingoApplication app, string path, string? id = null)
    {
        var response = new WebLingoBufferedResponse();
        app.Handle(Request(path, id), response, new WebLingoWorkerId(0));
        return (Body(response), response);
    }

    private sealed record Running(WebLingoBufferedResponse Response, Posts Posts)
    {
        public WebLingoRequestOutcome Outcome { get; set; } = null!;
    }

    /// <summary>
    /// A request begun with a scheduler host of its own, gone on with while it waits only for the database: where
    /// <paramref name="toTimer"/> is true, until it waits for a timer, as a page holding the lock while it awaits a delay,
    /// each answer of the database waited for 10 s at most, however slow the machine; otherwise until the database has
    /// answered nothing for 200 ms, as for a request waiting for the lock another holds, which reads the table again every
    /// 100 ms and answers only once it has the lock. Until 24 November 2026 a request to its timer stopped too once 200 ms
    /// passed without an answer, so that on a loaded machine it was left before its delay, which then fell at a time the
    /// test's clock never reached, and <see cref="Finish"/> resumed it for ever.
    /// </summary>
    private static Running Begin(WebLingoApplication app, string path, string id, bool toTimer = false)
    {
        var running = new Running(new WebLingoBufferedResponse(), new Posts());
        running.Outcome = app.Handle(Request(path, id), running.Response, new WebLingoWorkerId(0), scheduler: running.Posts);
        var within = TimeSpan.FromMilliseconds(toTimer ? 10_000 : 200);
        while (running.Outcome is { Kind: WebLingoRequestOutcomeKind.Waiting, WaitMilliseconds: < 0 })
        {
            if (!SpinWait.SpinUntil(() => running.Posts.Count > 0, within))
            {
                Assert.False(toTimer, $"{path} had no answer from the database within 10 s.");
                break;
            }
            running.Outcome = running.Outcome.Resume!();
        }
        Assert.True(!toTimer || running.Outcome.WaitMilliseconds >= 0, $"{path} did not come to its timer.");
        return running;
    }

    /// <summary>
    /// A request gone on with until it ends, its waits for the database and for its timers resumed; a timer whose time
    /// the test's clock does not reach failing the test, not resumed for ever.
    /// </summary>
    private static void Finish(Running running)
    {
        var timers = 0;
        while (running.Outcome.Kind == WebLingoRequestOutcomeKind.Waiting)
        {
            if (running.Outcome.WaitMilliseconds < 0) Assert.True(SpinWait.SpinUntil(() => running.Posts.Count > 0, TimeSpan.FromSeconds(10)));
            else Assert.True(++timers < 100, $"The request waits for a timer, {running.Outcome.WaitMilliseconds} ms, that the test's clock does not reach.");
            running.Outcome = running.Outcome.Resume!();
        }
    }

    private static string Body(WebLingoBufferedResponse response) => Encoding.UTF8.GetString(response.BodyBytes);

    private sealed class ManualClock : IWebLingoExecutionClock
    {
        public long Milliseconds { get; set; }
        public void WaitUntil(long milliseconds) { }
    }

    /// <summary>A request's scheduler host whose work posted back is held until the request is resumed.</summary>
    private sealed class Posts : IWebLingoSchedulerHost
    {
        private readonly Queue<Action> _posted = new();

        public int Count
        {
            get
            {
                lock (_posted) return _posted.Count;
            }
        }

        public void RunBlocking(Action work, bool opensConnection) => ThreadPool.UnsafeQueueUserWorkItem(_ => work(), null);

        public void RunBlocking(Action work, IWebLingoConnectionGate gate) => ThreadPool.UnsafeQueueUserWorkItem(_ => work(), null);

        public void Post(Action work)
        {
            lock (_posted) _posted.Enqueue(work);
        }

        public bool RunPosted(int milliseconds)
        {
            var ran = false;
            while (true)
            {
                Action? work;
                lock (_posted)
                {
                    if (!_posted.TryDequeue(out work)) return ran;
                }
                work();
                ran = true;
            }
        }
    }
}
