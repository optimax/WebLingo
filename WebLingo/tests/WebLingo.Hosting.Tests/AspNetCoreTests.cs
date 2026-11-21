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

    [Fact]
    public async Task AWorkerRunsOtherRequestsWhileOneWaits()
    {
        // One worker: the request that waits for its timer lets the other run, and end, first.
        Write("slow.wasp", "<% import { delay } from \"std:system\"; await delay(400); %>slow");
        Write("quick.wasp", "quick");
        using var one = new WebLingoWorkerPool(1);
        var application = Load(workers: 1);
        var ended = new List<string>();
        async Task Get(string path)
        {
            var context = await Serve(application, "GET", path, one);
            lock (ended) ended.Add(Body(context));
        }
        var slow = Get("/slow");
        await Task.Delay(100);
        var quick = Get("/quick");
        await Task.WhenAll(slow, quick);
        Assert.Equal(["quick", "slow"], ended);
    }

    [Fact]
    public async Task ARequestThatWaitsEndsWhenItsClientGoes()
    {
        Write("forever.wasp", "<% import { delay } from \"std:system\"; await delay(60000); %>never");
        using var gone = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = gone.Token };
        context.Request.Method = "GET";
        context.Request.Path = "/forever";
        context.Response.Body = new MemoryStream();
        var serving = WebLingoAspNetCore.Serve(context, Load(), _root, _workers);
        await Task.Delay(200);
        Assert.False(serving.IsCompleted);
        gone.Cancel();
        await serving.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain("never", Body(context));
    }

    [Fact]
    public async Task APageWaitsForASlowClientWithoutItsWorker()
    {
        // One worker: a long page whose client takes nothing yet waits for it, and another request is served meanwhile.
        Write("quick.wasp", "quick");
        using var one = new WebLingoWorkerPool(1);
        var application = Load(workers: 1);
        var slow = new SlowBody();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/long";
        context.Response.Body = slow;
        var serving = WebLingoAspNetCore.Serve(context, application, _root, one);
        await slow.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("quick", Body(await Serve(application, "GET", "/quick", one)));
        Assert.False(serving.IsCompleted);
        slow.Open();
        await serving.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new string('x', 100 * 1024) + "end", Encoding.UTF8.GetString(slow.Written.ToArray()));
    }

    /// <summary>A response's body whose client takes nothing until it is opened, after its first write.</summary>
    private sealed class SlowBody : Stream
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MemoryStream Written { get; } = new();

        public void Open() => _open.TrySetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Written.Length > 0)
            {
                Blocked.TrySetResult();
                await _open.Task.WaitAsync(cancellationToken);
            }
            Written.Write(buffer.Span);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Flush() { }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written.Length;
        public override long Position { get => Written.Length; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ARequestsBodyIsReadAsItArrives_OnlyIfAScriptReadsIt()
    {
        // A page that reads nothing of a body that never comes is served all the same; one that reads it is given it.
        Write("body.wasp", "<% param request; %><%= await request.text() %>");
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/quick";
        context.Request.Body = new NeverEndingBody();
        context.Response.Body = new MemoryStream();
        Write("quick.wasp", "quick");
        await WebLingoAspNetCore.Serve(context, Load(), _root, _workers).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("quick", Body(context));

        context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/body";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("sent"));
        context.Response.Body = new MemoryStream();
        await WebLingoAspNetCore.Serve(context, Load(), _root, _workers);
        Assert.Equal("sent", Body(context));
    }

    /// <summary>A request's body that never arrives.</summary>
    private sealed class NeverEndingBody : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
        var request = WebLingoAspNetCore.Translate(context.Request);
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

    private WebLingoApplication Load(int workers = 0)
    {
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(_root, new DotNetFileSystem())
        {
            Workers = workers > 0 ? workers : _workers.Count,
            WorkerServices = _ => new WebLingoWorkerServices(
                new WebLingoBuiltinRegistry(new WebLingoDelayFunction())),
            Clock = new WebLingoExecutionClock(new DotNetClock()),
            Scheduler = () => new DotNetSchedulerHost()
        }, out var error);
        Assert.True(application is not null, error);
        return application;
    }

    private async Task<DefaultHttpContext> Serve(WebLingoApplication application, string method, string path, WebLingoWorkerPool? workers = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        await WebLingoAspNetCore.Serve(context, application, _root, workers ?? _workers);
        return context;
    }

    private async Task<DefaultHttpContext> Serve(string method, string path)
    {
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(_root, new DotNetFileSystem())
        {
            Workers = _workers.Count,
            WorkerServices = _ => new WebLingoWorkerServices(
                new WebLingoBuiltinRegistry(new WebLingoDelayFunction())),
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
