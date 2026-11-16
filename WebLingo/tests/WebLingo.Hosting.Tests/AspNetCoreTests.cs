using System.Text;
using Microsoft.AspNetCore.Http;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// The ASP.NET Core adapter (specification, section 6.1, WL-H009; section 9, "M0.14 parts", part 4), on ASP.NET Core's own
/// request and response, an application's files on disk: a request translated, run on a worker, and its response, a page's,
/// a file's or an error's, translated back.
/// </summary>
public sealed class AspNetCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "weblingo-aspnetcore-" + Guid.NewGuid().ToString("N"));
    private readonly WebLingoWorkerPool _workers = new(2);

    public AspNetCoreTests()
    {
        Write("index.wasp", "<h1>Home</h1>");
        Write("hello.wasp", "<p>Hello, <%= \"<you>\" %>!</p>");
        Write("fails.wasp", "<%= [1][5] %>");
        Write("css/site.css", "body { color: red; }");
        Write("data.unknownextension", "?");
        Write("long.wasp", "<% var i = 0; while (i < 100) { %>" + new string('x', 1024) + "<% i = i + 1; } %>end");
        Write("longfails.wasp", "<% var i = 0; while (i < 100) { %>" + new string('x', 1024) + "<% i = i + 1; } %><%= [1][5] %>");
    }

    [Fact]
    public async Task ALongPageIsSentAsItIsWritten_WithoutALength()
    {
        var context = await Serve("GET", "/long");
        Assert.Equal((200, "text/html; charset=utf-8", (long?)null), (context.Response.StatusCode, context.Response.ContentType, context.Response.ContentLength));
        Assert.Equal(new string('x', 100 * 1024) + "end", Body(context));
    }

    [Fact]
    public async Task ALongPageThatFailsAbortsItsConnection()
    {
        var lifetime = new Lifetime();
        var context = new DefaultHttpContext();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature>(lifetime);
        context.Request.Method = "GET";
        context.Request.Path = "/longfails";
        context.Response.Body = new MemoryStream();
        await WebLingoAspNetCore.Serve(context, Load(), _root, _workers);
        Assert.True(lifetime.Aborted);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(new string('x', 100 * 1024), Body(context));
    }

    /// <summary>A request's lifetime that records its abort.</summary>
    private sealed class Lifetime : Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature
    {
        public bool Aborted { get; private set; }
        public CancellationToken RequestAborted { get; set; }
        public void Abort() => Aborted = true;
    }

    public void Dispose()
    {
        _workers.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task APageIsServed()
    {
        var context = await Serve("GET", "/hello");
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
        Assert.Equal("<p>Hello, &lt;you&gt;!</p>", Body(context));
        Assert.Equal(Encoding.UTF8.GetByteCount("<p>Hello, &lt;you&gt;!</p>"), context.Response.ContentLength);
        Assert.Equal("<h1>Home</h1>", Body(await Serve("GET", "/")));
    }

    [Fact]
    public async Task AMissingPageIs404_AndAFailingOne500()
    {
        var context = await Serve("GET", "/missing");
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Contains("<h1>404 Not Found</h1>", Body(context));
        context = await Serve("GET", "/fails");
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Contains("<h1>500 Internal Server Error</h1>", Body(context));
    }

    [Fact]
    public async Task AnyOtherFileIsSentAsItIs_IfASPNETCoreKnowsItsType()
    {
        var context = await Serve("GET", "/css/site.css");
        Assert.Equal((200, "text/css"), (context.Response.StatusCode, context.Response.ContentType));
        Assert.Equal("body { color: red; }", Body(context));

        context = await Serve("GET", "/data.unknownextension");
        Assert.Equal((404, ""), (context.Response.StatusCode, Body(context)));
    }

    [Fact]
    public async Task ARequestIsTranslatedWhole()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com", 8443);
        context.Request.PathBase = "/app";
        context.Request.Path = "/products/7";
        context.Request.QueryString = new QueryString("?sort=name&page=2");
        context.Request.Headers["X-Tag"] = new Microsoft.Extensions.Primitives.StringValues(["a", "b"]);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("body"));
        var request = await WebLingoAspNetCore.Translate(context.Request);
        Assert.Equal(("POST", "/products/7", "sort=name&page=2", "https", "example.com:8443"),
            (request.Method, request.Path, request.QueryString, request.Scheme, request.Host));
        Assert.Equal(["a", "b"], request.Headers.GetValues("x-tag"));
        var buffer = new byte[16];
        Assert.Equal("body", Encoding.UTF8.GetString(buffer, 0, request.Body.Read(buffer, 0, buffer.Length)));
    }

    [Fact]
    public async Task TheWorkersRunRequestsAtOnce()
    {
        // Two pieces of work that each wait for the other finish only if both workers run them at once.
        using var both = new Barrier(2);
        var workers = new List<int>();
        Task Wait() => _workers.Run(worker =>
        {
            lock (workers) workers.Add(worker.Value);
            Assert.True(both.SignalAndWait(TimeSpan.FromSeconds(10)));
        });
        await Task.WhenAll(Wait(), Wait());
        Assert.Equal([0, 1], workers.Order());
    }

    [Fact]
    public async Task TheReloaderReloadsTheApplicationWhenAFileChanges()
    {
        var application = Load();
        var reloads = new List<(int, string?)>();
        using var reloader = new WebLingoReloader(application, _root, (generation, error) => { lock (reloads) reloads.Add((generation, error)); });
        Assert.Equal(404, (await Serve(application, "GET", "/news")).Response.StatusCode);

        Write("news.wasp", "<p>Fresh</p>");
        for (var waited = 0; application.GenerationNumber < 2 && waited < 10_000; waited += 50) await Task.Delay(50);
        Assert.Equal(2, application.GenerationNumber);
        var context = await Serve(application, "GET", "/news");
        Assert.Equal((200, "<p>Fresh</p>"), (context.Response.StatusCode, Body(context)));
        lock (reloads) Assert.Equal((2, (string?)null), reloads[0]);
    }

    private WebLingoApplication Load()
    {
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(_root, new DotNetFileSystem())
        {
            Workers = _workers.Count,
            WorkerServices = _ => new WebLingoWorkerServices(
                new WebLingoBuiltinRegistry(new WebLingoWriteFunction(), new WebLingoWriteLnFunction(), new WebLingoDelayFunction())),
            Clock = new WebLingoExecutionClock(new DotNetClock()),
            Scheduler = () => new DotNetSchedulerHost()
        }, out var error);
        Assert.True(application is not null, error);
        return application;
    }

    private async Task<DefaultHttpContext> Serve(WebLingoApplication application, string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        await WebLingoAspNetCore.Serve(context, application, _root, _workers);
        return context;
    }

    private async Task<DefaultHttpContext> Serve(string method, string path)
    {
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(_root, new DotNetFileSystem())
        {
            Workers = _workers.Count,
            WorkerServices = _ => new WebLingoWorkerServices(
                new WebLingoBuiltinRegistry(new WebLingoWriteFunction(), new WebLingoWriteLnFunction(), new WebLingoDelayFunction())),
            Clock = new WebLingoExecutionClock(new DotNetClock()),
            Scheduler = () => new DotNetSchedulerHost()
        }, out var error);
        Assert.True(application is not null, error);
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        await WebLingoAspNetCore.Serve(context, application, _root, _workers);
        return context;
    }

    private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
