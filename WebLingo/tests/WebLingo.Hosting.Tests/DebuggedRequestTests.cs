using System.Text;
using WebLingo.Bytecode;
using WebLingo.Hosting.Abstractions;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A site's requests under a debugger (specification, section 5.7, "Debugging WASP pages, parts", part 2): each a thread of
/// the debugger's, named by its method and its path; a request stopped waits as at an <c>await</c>, its worker serving other
/// requests meanwhile, and goes on once the debugger lets it, resumed through its scheduler host; and the time it is stopped
/// is not its client's, for the send timeout.
/// </summary>
public sealed class DebuggedRequestTests
{
    [Fact]
    public void ARequestStoppedWaitsWhileItsWorkerServesAnother()
    {
        var debugger = new Debugger("index.wasp", 2);
        var app = Load(new MemoryFileSystem()
            .Add("/site/index.wasp", "<% var a = 1; %>\n<% var b = a + 1; %>b=<%= b %>")
            .Add("/site/other.wasp", "other"), debugger);
        var host = new PostedHost();
        var first = new WebLingoBufferedResponse();
        var outcome = app.Handle(new WebLingoBufferedRequest("GET", "/"), first, new WebLingoWorkerId(0), scheduler: host);
        Assert.Equal((WebLingoRequestOutcomeKind.Waiting, -1L), (outcome.Kind, outcome.WaitMilliseconds));
        var thread = Assert.Single(debugger.Threads);
        Assert.Equal(("GET /", 1), (thread.Name, thread.Stops));
        Assert.Contains(thread.Frames[0].Variables, variable => variable is { Name: "a", Value: "1" });

        // Its worker serves another request meanwhile, under a thread of its own.
        var second = new WebLingoBufferedResponse();
        Assert.Equal(WebLingoRequestOutcomeKind.Page,
            app.Handle(new WebLingoBufferedRequest("GET", "/other"), second, new WebLingoWorkerId(0), scheduler: new PostedHost()).Kind);
        Assert.Equal(("other", "GET /other", true), (Encoding.UTF8.GetString(second.BodyBytes), debugger.Threads[1].Name, debugger.Threads[1].Ended));

        // Resumed while its debugger holds it, it waits still; let go, it is resumed through its scheduler host, and ends.
        Assert.Equal(WebLingoRequestOutcomeKind.Waiting, (outcome = outcome.Resume!()).Kind);
        thread.Go();
        Assert.Equal(1, host.Posted);
        outcome = outcome.Resume!();
        Assert.Equal((WebLingoRequestOutcomeKind.Page, "b=2"), (outcome.Kind, Encoding.UTF8.GetString(first.BodyBytes)));
        Assert.Equal((2, true), (thread.Stops, thread.Ended));
    }

    [Fact]
    public void TheSendTimeoutLeavesOutTheTimeARequestIsStopped()
    {
        // A task writes more than its client takes, and waits for it; the page stops meanwhile, and the send timeout passes.
        var debugger = new Debugger("index.wasp", 3);
        var app = Load(new MemoryFileSystem().Add("/site/index.wasp",
            "<% async function fill() { var i = 0; while (i < 70) { write(\"" + new string('x', 1024) + "\"); i = i + 1; } } %>\n"
            + "<% var filled = fill(); %>\n"
            + "<% var a = 1; %>\n"
            + "<% await filled; %>end"), debugger, TimeSpan.FromMilliseconds(50));
        var response = new RecordingResponse { FullAfter = 1 };
        var host = new PostedHost();
        var outcome = app.Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0), scheduler: host);
        var thread = Assert.Single(debugger.Threads);
        Assert.Equal((WebLingoRequestOutcomeKind.Waiting, 1, 1), (outcome.Kind, thread.Stops, response.Waiters));

        // The client's time passes while it is stopped: the request is not cancelled, and waits for the client anew.
        response.TimeOut();
        Assert.Equal((0, 1), (host.Posted, response.Waiters));
        thread.Go();
        outcome = outcome.Resume!();
        Assert.Equal(WebLingoRequestOutcomeKind.Waiting, outcome.Kind);
        response.Take();
        outcome = outcome.Resume!();
        Assert.Equal(WebLingoRequestOutcomeKind.Page, outcome.Kind);
        Assert.EndsWith("xxend", Encoding.UTF8.GetString(response.Inner.BodyBytes));
    }

    private static WebLingoApplication Load(MemoryFileSystem files, Debugger debugger, TimeSpan? sendTimeout = null)
    {
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions("/site", files)
        {
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry()),
            Debugger = debugger,
            SendTimeout = sendTimeout ?? TimeSpan.FromSeconds(30)
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }

    /// <summary>A debugger whose threads each stop once, at a line of one file, and hold there until told to go on.</summary>
    private sealed class Debugger(string file, int line) : IWebLingoDebuggerThreads
    {
        public List<Thread> Threads { get; } = [];

        public IWebLingoDebuggerThread Begin(string name, WebLingoDebugLocator locate, IWebLingoSchedulerHost scheduler)
        {
            var thread = new Thread(name, locate, scheduler, file, line);
            Threads.Add(thread);
            return thread;
        }
    }

    private sealed class Thread(string name, WebLingoDebugLocator locate, IWebLingoSchedulerHost scheduler, string file, int line)
        : IWebLingoDebuggerThread
    {
        public string Name => name;
        public int Stops { get; private set; }
        public bool Ended { get; private set; }
        public IReadOnlyList<WebLingoDebugFrame> Frames { get; private set; } = [];

        public void ModuleLoaded(WebLingoCompiledModule module) { }

        public bool ShouldStop(WebLingoDebugPoint point)
        {
            if (Stops > 0) return false;
            var (text, offset) = locate(point.Source, point.Span.Start);
            return text.Name == file && text.GetLocation(offset).Line == line;
        }

        public bool Stopped(IReadOnlyList<WebLingoDebugFrame> frames)
        {
            (Frames, Stops) = (frames, Stops + 1);
            return false;
        }

        public bool Waits => (Stops & 1) == 1;

        public void Go()
        {
            Stops++;
            scheduler.Post(static () => { });
        }

        public void Dispose() => Ended = true;
    }

    /// <summary>A scheduler host whose posted work waits for the worker's next look, counted.</summary>
    private sealed class PostedHost : IWebLingoSchedulerHost
    {
        private readonly Queue<Action> _posted = new();

        public int Posted { get; private set; }

        public void RunBlocking(Action work) => throw new InvalidOperationException();

        public void Post(Action work)
        {
            _posted.Enqueue(work);
            Posted++;
        }

        public bool RunPosted(int milliseconds)
        {
            var ran = _posted.Count > 0;
            while (_posted.Count > 0) _posted.Dequeue()();
            return ran;
        }
    }

    /// <summary>
    /// A response in memory, full, as a slow client's is, once <see cref="FullAfter"/> writes are made, until
    /// <see cref="Take"/>; <see cref="TimeOut"/>, its send timeout passing for what waits.
    /// </summary>
    private sealed class RecordingResponse : IWebLingoHttpResponse, IWebLingoBodyWriter
    {
        private readonly List<Action<bool>> _waiting = [];
        private int _writes;

        public WebLingoBufferedResponse Inner { get; } = new();
        public int FullAfter { get; set; } = int.MaxValue;
        public int Waiters => _waiting.Count;
        public bool IsFull => _writes >= FullAfter;

        public void WhenReady(TimeSpan timeout, Action<bool> ready)
        {
            if (!IsFull) ready(true);
            else _waiting.Add(ready);
        }

        public void TimeOut()
        {
            var waiting = _waiting.ToArray();
            _waiting.Clear();
            foreach (var ready in waiting) ready(false);
        }

        public void Take()
        {
            FullAfter = int.MaxValue;
            var waiting = _waiting.ToArray();
            _waiting.Clear();
            foreach (var ready in waiting) ready(true);
        }

        public WebLingoResponseState State => Inner.State;
        public int StatusCode => Inner.StatusCode;
        public IWebLingoHeaderCollection Headers => Inner.Headers;
        public IWebLingoBodyWriter Body => this;
        public bool TrySetStatusCode(int code) => Inner.TrySetStatusCode(code);
        public void Commit() => Inner.Commit();
        public void Complete() => Inner.Complete();
        public void Abort() => Inner.Abort();

        public bool Write(byte[] buffer, int offset, int count)
        {
            _writes++;
            return Inner.Body.Write(buffer, offset, count);
        }
    }
}
