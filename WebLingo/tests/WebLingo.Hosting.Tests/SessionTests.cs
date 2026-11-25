using System.Text;
using System.Text.RegularExpressions;
using WebLingo.Hosting.Abstractions;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// Sessions in memory (specification, section 6.12, "Sessions and cookies"; "Sessions and cookies, parts", part 2):
/// <c>request.session</c>, opened, and its lock taken, before the page runs; a session made, and its cookie sent, once a
/// value is set in it; its values copies, each checked where it is set (part 2's A1 (b)); a request whose session another
/// holds waiting for it, unless it declares that it only reads it; <c>release()</c>, <c>renew()</c> and <c>end()</c>; a
/// lock waited for too long a <c>SessionLockError</c>; and a session unused for its timeout ended.
/// </summary>
public sealed class SessionTests
{
    private static readonly Regex Cookie = new("^WASPSESSIONID=([0-9a-f]{32}); Path=/; HttpOnly; SameSite=Lax$");

    [Fact]
    public void AValueSetIsKeptForTheSessionsNextRequest()
    {
        var store = Store();
        var app = Load(store, ("/site/index.wasp",
            "<% param request; var visits = (request.session?[\"visits\"] ?? 0) + 1; request.session[\"visits\"] = visits; %><%= visits %>"),
            ("/site/look.wasp", "<% param request; %><%= request.session.has(\"visits\") %>"));

        // A request that sets nothing makes no session, and sends no cookie.
        var (body, response) = Get(app, "/look");
        Assert.Equal(("false", 0, 0), (body, response.Headers.GetValues("Set-Cookie").Count, store.Count));

        (body, response) = Get(app, "/");
        Assert.Equal("1", body);
        var id = Cookie.Match(Assert.Single(response.Headers.GetValues("Set-Cookie"))).Groups[1].Value;
        Assert.Equal(32, id.Length);
        (body, response) = Get(app, "/", id);
        Assert.Equal(("2", 0), (body, response.Headers.GetValues("Set-Cookie").Count));
        Assert.Equal("3", Get(app, "/", id).Body);

        // An id the site does not know is never taken: a new session gets a new one.
        (body, response) = Get(app, "/", "0123456789abcdef0123456789abcdef");
        Assert.Equal("1", body);
        Assert.NotEqual("0123456789abcdef0123456789abcdef", Cookie.Match(response.Headers.GetValues("Set-Cookie")[0]).Groups[1].Value);
        Assert.Equal("4", Get(app, "/", id).Body);
    }

    [Fact]
    public void ACookieOverHttpsIsSecure()
    {
        var app = Load(Store(), ("/site/index.wasp", "<% param request; request.session[\"a\"] = 1; %>"));
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("GET", "/", "", "https", "example.com"), response, new WebLingoWorkerId(0));
        Assert.Matches("^WASPSESSIONID=[0-9a-f]{32}; Path=/; Secure; HttpOnly; SameSite=Lax$", Assert.Single(response.Headers.GetValues("Set-Cookie")));
    }

    [Fact]
    public void AValueReadIsACopyKeptOnlyOnceSetAgain()
    {
        var app = Load(Store(),
            ("/site/set.wasp", "<% param request; request.session[\"cart\"] = [\"tea\"];\n"
                               + "request.session[\"kinds\"] = { n: 5.0, i: 5, big: 1e300, b: true, z: null, s: \"a\\\"b\\n\\u{1F600}\", m: { list: [1, [2]] } }; %>"),
            ("/site/push.wasp", "<% param request; var cart = request.session[\"cart\"]; cart.push(\"cake\"); %><%= cart %>|<%= request.session[\"cart\"] %>|"
                                + "<%= request.session[\"cart\"] == request.session[\"cart\"] %>"),
            ("/site/keep.wasp", "<% param request; var cart = request.session[\"cart\"]; cart.push(\"cake\"); request.session[\"cart\"] = cart; cart.push(\"jam\"); %>"),
            ("/site/look.wasp", "<% param request; var k = request.session[\"kinds\"]; %><%- request.session[\"cart\"] %>|<%- k %>|"
                                + "<%= k[\"n\"] is Number %> <%= k[\"i\"] is Integer %> <%= k[\"big\"] is Number %>"));
        var (_, response) = Get(app, "/set");
        var id = Cookie.Match(response.Headers.GetValues("Set-Cookie")[0]).Groups[1].Value;
        // A change to a value read is the page's own, the session's unchanged, and each read is a copy of its own.
        Assert.Equal("[&quot;tea&quot;, &quot;cake&quot;]|[&quot;tea&quot;]|false", Get(app, "/push", id).Body);
        Assert.Equal("[\"tea\"]", Get(app, "/look", id).Body.Split('|')[0]);
        // Set again, it is kept as it was then.
        Get(app, "/keep", id);
        var look = Get(app, "/look", id).Body.Split('|');
        Assert.Equal("[\"tea\", \"cake\"]", look[0]);
        // Each value read back as the kind it was set as, a number as a number.
        Assert.Contains("\"n\": 5.0", look[1]);
        Assert.Contains("\"i\": 5", look[1]);
        Assert.Contains("\"s\": \"a\\\"b\\n\U0001F600\"", look[1]);
        Assert.Contains("\"m\": {\"list\": [1, [2]]}", look[1]);
        Assert.Contains("\"z\": null", look[1]);
        Assert.Equal("true true true", look[2]);
    }

    [Theory]
    [InlineData("1.5m", "not the decimal 1.5m.")]
    [InlineData("#{1}", "not a set.")]
    [InlineData("0.0 / 0.0", "not the number NaN.")]
    [InlineData("[1, Infinity]", "not one that holds the number Infinity.")]
    [InlineData("(x) => x", "not a function.")]
    [InlineData("new Cup()", "not an instance of Cup.")]
    [InlineData("new Stack()", "not an array of the class Stack.")]
    [InlineData("loop()", "not one that holds itself.")]
    [InlineData("keyed()", "not one that holds a map whose key is the integer 1.")]
    public void WhatASessionCannotKeepIsAnErrorWhereItIsSet(string value, string error)
    {
        var app = Load(Store(), ("/site/wrong.wasp", "<% param request; class Cup {} class Stack extends Array {}\n"
            + "function loop() { var a = []; a.push({ a: a }); return a; } function keyed() { var m = {}; m[1] = 2; return [m]; }\n"
            + "request.session[\"x\"] = " + value + "; %>"));
        var (body, _) = Get(app, "/wrong");
        Assert.Contains("wrong.wasp(3,1): error WL3001: The session cannot keep &#39;x&#39;: it keeps null, Booleans, integers, numbers, strings, "
                        + "and arrays and maps of them, a map&#39;s keys strings, as JSON holds them; " + WebUtility(error), body);
    }

    [Fact]
    public void ANameTheSessionHasNotIsAKeyError()
    {
        var app = Load(Store(), ("/site/index.wasp", "<% param request; var s = request.session; s[\"a\"] = 1; %>"
            + "<%= s?[\"none\"] %>|<%= s.has(\"a\") %> <%= s.remove(\"a\") %> <%= s.remove(\"a\") %> <%= s.has(\"a\") %>|<%= s == request.session %>|"
            + "<% try { var x = s[\"none\"]; } catch (KeyError e) { %><%= e.message %><% } %>|"
            + "<% try { s[1] = 1; } catch (TypeError e) { %><%= e.message %><% } %>"));
        Assert.Equal("|true true false false|true|The session has no &#39;none&#39;.|A session&#39;s names are strings, not the integer 1.", Get(app, "/").Body);
    }

    [Fact]
    public void ARequestWaitsForTheSessionAnotherHolds()
    {
        var clock = new ManualClock();
        var app = Load(Store(), clock,
            ("/site/set.wasp", "<% param request; request.session[\"a\"] = 0; %>"),
            ("/site/slow.wasp", "<% param request; import { delay } from \"std:system\"; request.session[\"a\"] = 1; await delay(50); request.session[\"a\"] = 2; %>slow"),
            ("/site/look.wasp", "<% param request; %><%= request.session[\"a\"] %>"),
            ("/site/peek.wasp", "<% session readonly; %><% param request; %><%= request.session[\"a\"] %>"),
            ("/site/api/peek.wlg", "export const session = \"readonly\";\nexport function get(request, response) { response.write(request.session[\"a\"]); }"));
        var id = Cookie.Match(Get(app, "/set").Response.Headers.GetValues("Set-Cookie")[0]).Groups[1].Value;

        var slow = new WebLingoBufferedResponse();
        var holding = app.Handle(Request("/slow", id), slow, new WebLingoWorkerId(0), scheduler: new Posts());
        Assert.Equal((WebLingoRequestOutcomeKind.Waiting, 50L), (holding.Kind, holding.WaitMilliseconds));

        // Another request of the session waits, as at an await, for no time of its own, until the first gives its lock up.
        var posts = new Posts();
        var look = new WebLingoBufferedResponse();
        var waiting = app.Handle(Request("/look", id), look, new WebLingoWorkerId(0), scheduler: posts);
        Assert.Equal((WebLingoRequestOutcomeKind.Waiting, -1L), (waiting.Kind, waiting.WaitMilliseconds));
        Assert.Equal(WebLingoResponseState.Uncommitted, look.State);
        Assert.Equal(WebLingoRequestOutcomeKind.Waiting, waiting.Resume!().Kind);

        // A page and a handler that only read it take no lock: they run at once, reading the values last written.
        Assert.Equal("0", Get(app, "/peek", id).Body);
        Assert.Equal("0", Get(app, "/api/peek", id).Body);

        clock.Milliseconds += 50;
        Assert.Equal(WebLingoRequestOutcomeKind.Page, holding.Resume!().Kind);
        Assert.Equal(1, posts.Count);
        Assert.Equal(WebLingoRequestOutcomeKind.Page, waiting.Resume!().Kind);
        Assert.Equal("2", Body(look));
    }

    [Fact]
    public void ReleaseGivesTheLockUpEarly()
    {
        var clock = new ManualClock();
        var app = Load(Store(), clock,
            ("/site/set.wasp", "<% param request; request.session[\"a\"] = 0; %>"),
            ("/site/early.wasp", "<% param request; import { delay } from \"std:system\"; request.session[\"a\"] = 1; request.session.release();\n"
                                 + "await delay(50); %><%= request.session[\"a\"] %>|<% try { request.session[\"a\"] = 2; } catch (e) { %><%= e.message %><% } %>"),
            ("/site/look.wasp", "<% param request; %><%= request.session[\"a\"] %>"));
        var id = Cookie.Match(Get(app, "/set").Response.Headers.GetValues("Set-Cookie")[0]).Groups[1].Value;

        var early = new WebLingoBufferedResponse();
        var running = app.Handle(Request("/early", id), early, new WebLingoWorkerId(0), scheduler: new Posts());
        Assert.Equal(WebLingoRequestOutcomeKind.Waiting, running.Kind);
        // Its lock given up, and its values written back, another request of the session runs at once.
        Assert.Equal("1", Get(app, "/look", id).Body);
        clock.Milliseconds += 50;
        running.Resume!();
        Assert.Equal("1|The session cannot be changed: request.session.release() has given it up.", Body(early));
        Assert.Equal("1", Get(app, "/look", id).Body);
    }

    [Fact]
    public void RenewGivesTheSessionANewIdAndEndRemovesIt()
    {
        var store = Store();
        var app = Load(store,
            ("/site/set.wasp", "<% param request; request.session[\"user\"] = \"ann\"; %>"),
            ("/site/renew.wasp", "<% param request; request.session.renew(); %><%= request.session[\"user\"] %>"),
            ("/site/end.wasp", "<% param request; request.session.end(); %><%= request.session.has(\"user\") %>"),
            ("/site/look.wasp", "<% param request; %><%= request.session?[\"user\"] %>"));
        var first = Cookie.Match(Get(app, "/set").Response.Headers.GetValues("Set-Cookie")[0]).Groups[1].Value;

        var (body, response) = Get(app, "/renew", first);
        var renewed = Cookie.Match(Assert.Single(response.Headers.GetValues("Set-Cookie"))).Groups[1].Value;
        Assert.Equal("ann", body);
        Assert.NotEqual(first, renewed);
        Assert.Equal("", Get(app, "/look", first).Body);
        Assert.Equal("ann", Get(app, "/look", renewed).Body);

        (body, response) = Get(app, "/end", renewed);
        Assert.Equal("false", body);
        Assert.Equal("WASPSESSIONID=; Path=/; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT; HttpOnly; SameSite=Lax",
            Assert.Single(response.Headers.GetValues("Set-Cookie")));
        Assert.Equal("", Get(app, "/look", renewed).Body);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void ALockWaitedForTooLongIsASessionLockErrorWhereThePageReadsIt()
    {
        var clock = new ManualClock();
        var app = Load(new WebLingoMemorySessions(TimeSpan.FromMinutes(20), TimeSpan.FromMilliseconds(100)), clock,
            ("/site/set.wasp", "<% param request; request.session[\"a\"] = 0; %>"),
            ("/site/slow.wasp", "<% param request; import { delay } from \"std:system\"; request.session[\"a\"] = 1; await delay(50); %>"),
            ("/site/look.wasp", "<% param request; %>runs|<% try { var s = request.session; } catch (SessionLockError e) { %><%= e.code %>: <%= e.message %><% } %>"));
        var id = Cookie.Match(Get(app, "/set").Response.Headers.GetValues("Set-Cookie")[0]).Groups[1].Value;
        var holding = app.Handle(Request("/slow", id), new WebLingoBufferedResponse(), new WebLingoWorkerId(0), scheduler: new Posts());

        var posts = new Posts();
        var look = new WebLingoBufferedResponse();
        var waiting = app.Handle(Request("/look", id), look, new WebLingoWorkerId(0), scheduler: posts);
        Assert.Equal(WebLingoRequestOutcomeKind.Waiting, waiting.Kind);
        Assert.True(SpinWait.SpinUntil(() => posts.Count > 0, TimeSpan.FromSeconds(10)));
        Assert.Equal(WebLingoRequestOutcomeKind.Page, waiting.Resume!().Kind);
        Assert.Equal("runs|WL3024: The session cannot be read: this request waited 0.1 seconds for its lock, the most this site waits, "
                     + "while another request of the session held it.", Body(look));

        // The request that held it ends as it would have, its values written back.
        clock.Milliseconds += 50;
        holding.Resume!();
        Assert.Equal("runs|", Get(app, "/look", id).Body);
    }

    [Fact]
    public void ASessionEndsOnceUnusedForItsTimeout()
    {
        long now = 0;
        var store = new WebLingoMemorySessions(TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(30), () => now);
        var app = Load(store, ("/site/set.wasp", "<% param request; request.session[\"a\"] = 1; %>"),
            ("/site/look.wasp", "<% param request; %><%= request.session?[\"a\"] %>"));
        var id = Cookie.Match(Get(app, "/set").Response.Headers.GetValues("Set-Cookie")[0]).Groups[1].Value;
        now += 19 * 60_000;
        Assert.Equal("1", Get(app, "/look", id).Body);
        // Each request uses it anew: its timeout runs from the last.
        now += 19 * 60_000;
        Assert.Equal("1", Get(app, "/look", id).Body);
        now += 20 * 60_000;
        Assert.Equal("", Get(app, "/look", id).Body);
        Assert.Equal(0, store.Count);
    }

    [Theory]
    [InlineData("/page", "&#39;session readonly;&#39; at page.wasp(1,4)")]
    [InlineData("/api/handler", "&#39;export const session = &quot;readonly&quot;;&#39; at api/handler.wlg(1,1)")]
    public void ARequestThatOnlyReadsItsSessionCannotChangeIt(string path, string declared)
    {
        var app = Load(Store(), ("/site/set.wasp", "<% param request; request.session[\"a\"] = 1; %>"),
            ("/site/page.wasp", "<% session readonly; param request; request.session[\"a\"] = 2; %>"),
            ("/site/api/handler.wlg", "export const session = \"readonly\";\nexport function get(request, response) { request.session.remove(\"a\"); }"));
        var id = Cookie.Match(Get(app, "/set").Response.Headers.GetValues("Set-Cookie")[0]).Groups[1].Value;
        Assert.Contains($"error WL3001: The session cannot be changed: this request only reads it, as {declared} declares.", Get(app, path, id).Body);
    }

    [Fact]
    public void AHandlersSessionExportIsOnlyReadOnly()
    {
        var app = Load(Store(), ("/site/api/handler.wlg", "export var session = \"readonly\";\nexport function get(request, response) { response.write(1); }"));
        var (body, response) = Get(app, "/api/handler");
        Assert.Equal(500, response.StatusCode);
        Assert.Contains("api/handler.wlg(1,1): error WL5009: A handler&#39;s &#39;session&#39; export declares that it only reads its session, "
                        + "&#39;export const session = &quot;readonly&quot;;&#39;, and is nothing else.", body);
    }

    [Fact]
    public void ASessionMadeOnceTheResponseIsSentIsAnError()
    {
        var log = new List<string>();
        var app = Load(Store(), null, log, ("/site/index.wasp", "<% param request; %>" + new string('x', 70 * 1024) + "<% request.session[\"late\"] = 1; %>"));
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        Assert.Contains("error WL3001: The session cannot be made: the response has been sent, and the session's cookie with it.", ListLog.Error(log));
        Assert.Empty(response.Headers.GetValues("Set-Cookie"));
    }

    [Fact]
    public void AHostWithoutSessionsSaysSo()
    {
        var app = Load(null, ("/site/index.wasp", "<% param request; try { var s = request.session; } catch (e) { %><%= e.message %><% } %>"));
        Assert.Equal("This host keeps no sessions.", Get(app, "/").Body);
    }

    private static WebLingoMemorySessions Store() => new(TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(30));

    private static WebLingoApplication Load(IWebLingoSessionStore? store, params (string Path, string Text)[] files) => Load(store, null, null, files);

    private static WebLingoApplication Load(IWebLingoSessionStore? store, ManualClock? clock, params (string Path, string Text)[] files) =>
        Load(store, clock, null, files);

    private static WebLingoApplication Load(IWebLingoSessionStore? store, ManualClock? clock, List<string>? log, params (string Path, string Text)[] files)
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
            Log = log is null ? null : new ListLog(log)
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }

    private static WebLingoBufferedRequest Request(string path, string? id) =>
        new("GET", path, headers: id is null ? null : [("Cookie", "other=1; WASPSESSIONID=" + id)]);

    private static (string Body, WebLingoBufferedResponse Response) Get(WebLingoApplication app, string path, string? id = null)
    {
        var response = new WebLingoBufferedResponse();
        app.Handle(Request(path, id), response, new WebLingoWorkerId(0));
        return (Body(response), response);
    }

    private static string Body(WebLingoBufferedResponse response) => Encoding.UTF8.GetString(response.BodyBytes);

    /// <summary>A message as a page's error shows it, its quotes escaped.</summary>
    private static string WebUtility(string text) => text.Replace("'", "&#39;", StringComparison.Ordinal);

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

        public void RunBlocking(Action work) => throw new InvalidOperationException();

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
