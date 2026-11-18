using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A page's output sent as it is written (specification, section 6.12, "Streaming"; section 9, "M0.14.7 parts", part 1):
/// kept until it reaches 64 KB, so that a shorter page that fails is still a 500, then sent in pieces; and a page that fails
/// after that, its response ended unfinished, its error logged and, in development, at its output's end.
/// </summary>
public sealed class StreamingTests
{
    private const string Root = "/site";

    /// <summary>A page writing <paramref name="kilobytes"/> KB of <c>x</c>, a kilobyte at a time, then <paramref name="after"/>.</summary>
    private static string Page(int kilobytes, string after = "") =>
        "<% var i = 0; while (i < " + kilobytes + ") { %>" + new string('x', 1024) + "<% i = i + 1; } %>" + after;

    [Fact]
    public void AShortPageIsSentWholeOnceItHasRun()
    {
        var response = new RecordingResponse();
        Load(new MemoryFileSystem().Add("/site/index.wasp", Page(63))).Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        Assert.Equal([63 * 1024], response.Writes);
        Assert.Equal((200, false, "text/html; charset=utf-8"), (response.Inner.StatusCode, response.Inner.Aborted, response.Inner.Headers.Get("Content-Type")));
    }

    [Fact]
    public void ALongPageIsSentAsItIsWritten()
    {
        var response = new RecordingResponse();
        var app = Load(new MemoryFileSystem().Add("/site/index.wasp",
            "<% param response; response.headers[\"Content-Type\"] = \"text/plain\"; %>" + Page(100, "<% writeLn(response.status); %>")));
        app.Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        // Committed once 64 KB were written, then sent 16 KB at a time, the rest once the page has run.
        Assert.Equal([64 * 1024, 16 * 1024, 16 * 1024, 4 * 1024 + 4], response.Writes);
        Assert.Equal((200, false, "text/plain"), (response.Inner.StatusCode, response.Inner.Aborted, response.Inner.Headers.Get("Content-Type")));
        Assert.EndsWith("xx200\n", Encoding.UTF8.GetString(response.Inner.BodyBytes));
    }

    [Fact]
    public void APageThatFailsBeforeItsOutputIsSentIsA500()
    {
        var log = new List<string>();
        var response = new WebLingoBufferedResponse();
        Load(new MemoryFileSystem().Add("/site/index.wasp", Page(63, "<%= [1][5] %>")), log: log)
            .Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        Assert.Equal((500, false), (response.StatusCode, response.Aborted));
        Assert.DoesNotContain("xxx", Encoding.UTF8.GetString(response.BodyBytes));
        Assert.Empty(log);
    }

    [Theory]
    [InlineData(WebLingoMode.Development)]
    [InlineData(WebLingoMode.Production)]
    public void APageThatFailsAfterItsOutputIsSentEndsItUnfinished(WebLingoMode environment)
    {
        var log = new List<string>();
        var response = new WebLingoBufferedResponse();
        var outcome = Load(new MemoryFileSystem().Add("/site/index.wasp", Page(70, "\n<%= [\"<a>\"][5] %>")), environment, log)
            .Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        Assert.Equal(WebLingoRequestOutcomeKind.Failed, outcome.Kind);
        Assert.Equal((200, true), (response.StatusCode, response.Aborted));
        var body = Encoding.UTF8.GetString(response.BodyBytes);
        Assert.StartsWith(new string('x', 70 * 1024), body);
        var error = Assert.Single(log);
        Assert.StartsWith("A page failed after its response was sent: index.wasp(2,", error);
        Assert.Contains("error WL3005:", error);
        if (environment == WebLingoMode.Development)
        {
            Assert.EndsWith("\n<pre>" + error["A page failed after its response was sent: ".Length..].Replace("'", "&#39;", StringComparison.Ordinal) + "\n</pre>\n", body);
        }
        else
        {
            Assert.EndsWith(new string('x', 1024) + "\n", body);
        }
    }

    [Fact]
    public void AStatusOrAHeaderSetOnceTheOutputIsSentIsAnError()
    {
        var log = new List<string>();
        var response = new WebLingoBufferedResponse();
        Load(new MemoryFileSystem().Add("/site/index.wasp", "<% param response; %>" + Page(65, "<% response.status = 404; %>")), log: log)
            .Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        Assert.Equal((200, true), (response.StatusCode, response.Aborted));
        Assert.Contains("error WL3001: The response's status cannot be set: it has been sent.", Assert.Single(log));
    }

    [Theory]
    [InlineData("/", "<% param response; %>")]
    [InlineData("/api/rows", null)]
    public void APageWaitsForAClientThatHasNotTakenItsOutput(string path, string? page)
    {
        // A page's writers, and a handler's write, wait once the response is full, its worker free meanwhile.
        var files = new MemoryFileSystem()
            .Add("/site/api/rows.wl", "export async function get(request, response) {\n  var i = 0;\n  while (i < 70) { write(\"" + new string('x', 1024)
                                      + "\"); i = i + 1; }\n  write(\"end\");\n}");
        if (page is not null) files.Add("/site/index.wasp", page + Page(70, "end"));
        var response = new RecordingResponse { FullAfter = 1 };
        var host = new PostedHost();
        var outcome = Load(files).Handle(new WebLingoBufferedRequest("GET", path), response, new WebLingoWorkerId(0), scheduler: host);
        Assert.Equal((WebLingoRequestOutcomeKind.Waiting, -1L), (outcome.Kind, outcome.WaitMilliseconds));
        Assert.Equal([64 * 1024], response.Writes);
        Assert.Equal(1, response.Waiters);

        // Resumed before the client has taken anything, it waits on.
        outcome = outcome.Resume!();
        Assert.Equal(WebLingoRequestOutcomeKind.Waiting, outcome.Kind);
        response.Take();
        Assert.Equal(1, host.Posted);
        outcome = outcome.Resume!();
        Assert.NotEqual(WebLingoRequestOutcomeKind.Waiting, outcome.Kind);
        Assert.EndsWith("xxend", Encoding.UTF8.GetString(response.Inner.BodyBytes));
        Assert.Equal(70 * 1024 + 3, response.Inner.BodyBytes.Length);
    }

    [Fact]
    public void AClientThatTakesNothingForTooLongCancelsItsRequest()
    {
        var log = new List<string>();
        var response = new RecordingResponse { FullAfter = 1 };
        var host = new PostedHost();
        var app = Load(new MemoryFileSystem().Add("/site/index.wasp", Page(70, "end")), log: log, sendTimeout: TimeSpan.FromMilliseconds(50));
        var outcome = app.Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0), scheduler: host);
        Assert.Equal((WebLingoRequestOutcomeKind.Waiting, TimeSpan.FromMilliseconds(50)), (outcome.Kind, response.Timeout));
        response.TimeOut();
        Assert.Equal(1, host.Posted);
        outcome = outcome.Resume!();
        Assert.Equal((WebLingoRequestOutcomeKind.Failed, true), (outcome.Kind, response.Inner.Aborted));
        Assert.StartsWith("A page's client took nothing of its output for 0.05 seconds, and its request was cancelled: index.wasp", Assert.Single(log));
        Assert.Contains("error WL3004:", log[0]);
        Assert.DoesNotContain("end", Encoding.UTF8.GetString(response.Inner.BodyBytes));
    }

    /// <summary>A scheduler host whose posted work waits for the worker's next look, counted.</summary>
    private sealed class PostedHost : WebLingo.Platform.Abstractions.IWebLingoSchedulerHost
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _posted = new();
        private int _count;

        public int Posted => Volatile.Read(ref _count);

        public void RunBlocking(Action work) => throw new InvalidOperationException();

        public void Post(Action work)
        {
            _posted.Enqueue(work);
            Interlocked.Increment(ref _count);
        }

        public bool RunPosted(int milliseconds)
        {
            var ran = false;
            while (_posted.TryDequeue(out var work))
            {
                work();
                ran = true;
            }
            return ran;
        }
    }

    private static WebLingoApplication Load(MemoryFileSystem files, WebLingoMode environment = WebLingoMode.Production,
        List<string>? log = null, TimeSpan? sendTimeout = null)
    {
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions(Root, files)
        {
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry(new WebLingoWriteFunction(), new WebLingoWriteLnFunction())),
            Mode = environment,
            Log = log is null ? null : log.Add,
            SendTimeout = sendTimeout ?? TimeSpan.FromSeconds(30)
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }

    /// <summary>
    /// A response in memory that records each write's size; full, as a slow client's is, once <see cref="FullAfter"/>
    /// writes are made, until <see cref="Take"/>, as the client taking what waits.
    /// </summary>
    private sealed class RecordingResponse : IWebLingoHttpResponse, IWebLingoBodyWriter
    {
        private readonly List<Action> _ready = [];

        public WebLingoBufferedResponse Inner { get; } = new();
        public List<int> Writes { get; } = [];
        public int FullAfter { get; set; } = int.MaxValue;
        public int Waiters => _ready.Count;

        public bool IsFull => Writes.Count >= FullAfter;

        /// <summary>Its client's time to take what waits, given; past it, only <see cref="TimeOut"/> tells the page.</summary>
        public TimeSpan Timeout { get; private set; }

        private readonly List<Action<bool>> _timing = [];

        public void WhenReady(TimeSpan timeout, Action<bool> ready)
        {
            Timeout = timeout;
            if (!IsFull)
            {
                ready(true);
                return;
            }
            _ready.Add(() => ready(true));
            _timing.Add(ready);
        }

        /// <summary>The send timeout passing, for what waits.</summary>
        public void TimeOut()
        {
            foreach (var ready in _timing) ready(false);
            _timing.Clear();
            _ready.Clear();
        }

        public void Take()
        {
            FullAfter = int.MaxValue;
            foreach (var ready in _ready) ready();
            _ready.Clear();
            _timing.Clear();
        }

        public WebLingoResponseState State => Inner.State;
        public int StatusCode => Inner.StatusCode;
        public IWebLingoHeaderCollection Headers => Inner.Headers;
        public IWebLingoBodyWriter Body => this;
        public bool TrySetStatusCode(int code) => Inner.TrySetStatusCode(code);
        public void Complete() => Inner.Complete();
        public void Abort() => Inner.Abort();

        public void Commit() => Inner.Commit();

        public bool Write(byte[] buffer, int offset, int count)
        {
            Writes.Add(count);
            return Inner.Body.Write(buffer, offset, count);
        }
    }
}
