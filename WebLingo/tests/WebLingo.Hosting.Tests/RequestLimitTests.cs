using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using WebLingo.Bytecode;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Hosting.DotNet;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A request's limits (specification, section 6.12, "Limits"; the owner's answer, 24 November 2026, "Request Limits in
/// WASP", A1 (c)): its time, its waits included, kept by its host, the ASP.NET Core adapter here; its instructions; and its
/// heap's memory. A request that passes one is stopped, a 503, WL3003, its error shown in development only. And the drain
/// (section 6.12, "Reloading"; the owner's answer, 26 November 2026, A1 (c) of "M0.22, Part 1: The Drain"): a request still
/// running on an older generation once a reload has published a newer one stopped so too, once the drain has passed.
/// </summary>
public sealed class RequestLimitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "weblingo-limits-" + Guid.NewGuid().ToString("N"));
    private readonly WebLingoWorkerPool _workers = new(2);

    public RequestLimitTests()
    {
        Write("hello.wasp", "<p>Hello.</p>");
        Write("loops.wasp", "<% var i = 0; while (true) { i = i + 1; } %>never");
        Write("waits.wasp", "<% import { delay } from \"std:system\"; await delay(60000); %>never");
        Write("counts.wasp", "<% var sum = 0; for (var i = 0; i < 100000; i++) { sum += i; } %><%= sum %>");
        Write("grows.wasp", "<% var s = \"x\"; var i = 0; while (i < 30) { s = s + s; i = i + 1; } %><%= s.length %>");
    }

    public void Dispose()
    {
        _workers.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheDefaultsAreSixtySecondsABillionInstructionsAnd128Megabytes()
    {
        var options = new WebLingoApplicationOptions(_root, new DotNetFileSystem());
        Assert.Equal((TimeSpan.FromSeconds(60), 1_000_000_000L, 64L * 1024 * 1024),
            (options.RequestTimeout, options.InstructionBudget, options.MaxHeapUnits));
        Assert.Equal("The request took longer than 60 seconds, its site's limit, \"limits\"' \"requestTimeout\" in wasp.config, and was stopped.",
            options.TimeLimitMessage);
    }

    [Fact]
    public async Task ATimeLimitStopsAPageThatLoops()
    {
        var (status, body) = await Get(Load(timeout: TimeSpan.FromMilliseconds(300)), "/loops");
        Assert.Equal(503, status);
        Assert.Contains("<title>503 Service Unavailable</title>", body, StringComparison.Ordinal);
        Assert.Contains("error WL3003: The request took longer than 0.3 seconds, its site&#39;s limit, &quot;limits&quot;&#39; "
                        + "&quot;requestTimeout&quot; in wasp.config, and was stopped.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATimeLimitStopsAPageThatWaits_ItsWaitsCounted()
    {
        // Waiting for its timer, it is woken to see that it is stopped, long before its delay's minute.
        var clock = Stopwatch.StartNew();
        var (status, body) = await Get(Load(timeout: TimeSpan.FromMilliseconds(300)), "/waits");
        Assert.Equal(503, status);
        Assert.Contains("error WL3003: The request took longer than 0.3 seconds", body, StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), clock.Elapsed.ToString());
    }

    [Fact]
    public async Task AnInstructionLimitStopsAPage()
    {
        var (status, body) = await Get(Load(instructions: 10_000), "/counts");
        Assert.Equal(503, status);
        Assert.Contains("error WL3003: The program ran 10000 instructions, its whole budget, and was stopped.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMemoryLimitStopsAPage()
    {
        var (status, body) = await Get(Load(units: 1_000_000), "/grows");
        Assert.Equal(503, status);
        Assert.Contains("error WL3003", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InProductionALimitsErrorIsNotShown()
    {
        var (status, body) = await Get(Load(timeout: TimeSpan.FromMilliseconds(300), mode: WebLingoMode.Production), "/loops");
        Assert.Equal(503, status);
        Assert.DoesNotContain("WL3003", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDrainIsSixtySecondsOrTheTimeLimitWhereThatIsLonger()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), new WebLingoApplicationOptions(_root, new DotNetFileSystem()).Drain);
        Assert.Equal(TimeSpan.FromSeconds(60), WebLingoApplicationOptions.DefaultDrain(null));
        Assert.Equal(TimeSpan.FromSeconds(60), WebLingoApplicationOptions.DefaultDrain(TimeSpan.FromSeconds(20)));
        Assert.Equal(TimeSpan.FromSeconds(300), WebLingoApplicationOptions.DefaultDrain(TimeSpan.FromSeconds(300)));
        Assert.Equal("The site was reloaded, and the request, still running on its files as they were before, was stopped 60 seconds "
                     + "after, its site's limit, \"limits\"' \"drain\" in wasp.config.",
            new WebLingoApplicationOptions(_root, new DotNetFileSystem()).DrainMessage);
    }

    [Fact]
    public async Task TheDrainStopsARequestStillRunningOnAnOlderGeneration_ALaterOneRunningOn()
    {
        var log = new StringWriter();
        var application = Load(noTimeout: true, drain: TimeSpan.FromMilliseconds(300),
            log: new DotNetLog(TextWriter.Synchronized(log), WebLingoLogLevel.Info));
        var drain = WebLingoDrain.Of(application);
        var clock = Stopwatch.StartNew();
        var waiting = Get(application, "/waits");
        await Until(() => drain.Running == 1);
        Write("hello.wasp", "<p>Hello again.</p>");
        Assert.True(application.TryReload(out var error), error);
        drain.Reloaded(application.GenerationNumber);
        var (status, body) = await waiting;
        Assert.Equal(503, status);
        Assert.Contains("error WL3003: The site was reloaded, and the request, still running on its files as they were before, was stopped "
                        + "0.3 seconds after, its site&#39;s limit, &quot;limits&quot;&#39; &quot;drain&quot; in wasp.config.", body,
            StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), clock.Elapsed.ToString());
        Assert.Contains("GET /waits", log.ToString(), StringComparison.Ordinal);
        Assert.Contains("The site was reloaded, and the request, still running on its files as they were before, was stopped", log.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(0, drain.Running);
        // A request begun on the generation now served waits past the drain, which ends none of its requests.
        Write("second.wasp", "<% import { delay } from \"std:system\"; await delay(1000); %>waited");
        Assert.True(application.TryReload(out error), error);
        drain.Reloaded(application.GenerationNumber);
        Assert.Equal((200, "waited"), await Get(application, "/second"));
        Assert.Equal((200, "<p>Hello again.</p>"), await Get(application, "/hello"));
    }

    [Fact]
    public async Task ARequestOnTheGenerationServedIsNotDrained_NorOneThatHasEnded()
    {
        var application = Load(drain: TimeSpan.FromMilliseconds(100));
        var drain = WebLingoDrain.Of(application);
        var (current, ended) = (new WebLingoCancellationSignal(), new WebLingoCancellationSignal());
        using var wake = new CancellationTokenSource();
        using var running = drain.Begin(current, wake);
        var done = drain.Begin(ended, wake);
        done.Serving(application.GenerationNumber);
        Assert.True(application.TryReload(out var error), error);
        running.Serving(application.GenerationNumber);
        done.Dispose();
        drain.Reloaded(application.GenerationNumber);
        await Task.Delay(500);
        Assert.False(current.IsCancellationRequested);
        Assert.False(ended.IsCancellationRequested);
        Assert.False(wake.IsCancellationRequested);
    }

    [Fact]
    public async Task ARequestItsDebuggerHasStoppedIsNotDrainedWhileItIsStopped()
    {
        var application = Load(drain: TimeSpan.FromMilliseconds(150));
        var drain = WebLingoDrain.Of(application);
        var cancellation = new WebLingoCancellationSignal();
        using var wake = new CancellationTokenSource();
        var thread = new Thread { Stops = 1 };
        using var request = drain.Begin(cancellation, wake);
        request.Serving(application.GenerationNumber);
        request.Debugged(thread);
        Assert.True(application.TryReload(out var error), error);
        drain.Reloaded(application.GenerationNumber);
        // Stopped, for more than its drain, three times over.
        await Task.Delay(600);
        Assert.False(cancellation.IsCancellationRequested);
        // Gone on: stopped since its drain began, which begins anew, then ends it.
        thread.Stops = 2;
        await Until(() => cancellation.IsCancellationRequested);
        Assert.Equal(application.DrainMessage, cancellation.Limit);
        Assert.True(wake.IsCancellationRequested);
    }

    [Fact]
    public async Task TheLongestTimeAConfigurationGivesIsOneATimerKeeps()
    {
        // 4,294,967 seconds, wasp.config's most for "requestTimeout" and "drain": a request's timer made with it, and its drain's.
        var longest = TimeSpan.FromSeconds(4_294_967);
        var application = Load(timeout: longest, drain: longest);
        var drain = WebLingoDrain.Of(application);
        using var gone = new CancellationTokenSource();
        var waiting = Get(application, "/waits", gone.Token);
        await Until(() => drain.Running == 1);
        Assert.True(application.TryReload(out var error), error);
        drain.Reloaded(application.GenerationNumber);
        Assert.Equal((200, "<p>Hello.</p>"), await Get(application, "/hello"));
        Assert.False(waiting.IsCompleted);
        // Its client gone, it ends.
        gone.Cancel();
        await waiting;
        Assert.Equal(0, drain.Running);
    }

    [Fact]
    public async Task APageWithinItsLimitsIsServed()
    {
        var application = Load(timeout: TimeSpan.FromSeconds(30), instructions: 1_000_000, units: 1_000_000);
        Assert.Equal((200, "<p>Hello.</p>"), await Get(application, "/hello"));
        Assert.Equal((200, "4999950000"), await Get(application, "/counts"));
    }

    private WebLingoApplication Load(TimeSpan? timeout = null, long instructions = WebLingoApplicationOptions.DefaultInstructionBudget,
        long units = WebLingoHeap.DefaultMaxUnits, WebLingoMode mode = WebLingoMode.Development, bool noTimeout = false, TimeSpan? drain = null,
        IWebLingoLog? log = null)
    {
        timeout = noTimeout ? null : timeout ?? WebLingoApplicationOptions.DefaultRequestTimeout;
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(_root, new DotNetFileSystem())
        {
            Workers = _workers.Count,
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry(new WebLingoDelayFunction())),
            Clock = new WebLingoExecutionClock(new DotNetClock()),
            Scheduler = () => new DotNetSchedulerHost(),
            Mode = mode,
            RequestTimeout = timeout,
            InstructionBudget = instructions,
            MaxHeapUnits = units,
            Drain = drain ?? WebLingoApplicationOptions.DefaultDrain(timeout),
            Log = log
        }, out var error);
        Assert.True(application is not null, error);
        return application;
    }

    private async Task<(int Status, string Body)> Get(WebLingoApplication application, string path, CancellationToken gone = default)
    {
        var context = new DefaultHttpContext { RequestAborted = gone };
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        await WebLingoAspNetCore.Serve(context, application, _workers);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    /// <summary>Waits for <paramref name="condition"/>, 30 seconds at most, for a loaded machine.</summary>
    private static async Task Until(Func<bool> condition)
    {
        for (var waited = 0; !condition() && waited < 30_000; waited += 10) await Task.Delay(10);
        Assert.True(condition());
    }

    /// <summary>A debugger's thread, which a test says is stopped, or not, by its count of stops, odd while it is.</summary>
    private sealed class Thread : IWebLingoDebuggerThread
    {
        private volatile int _stops;

        public int Stops
        {
            get => _stops;
            set => _stops = value;
        }

        public void ModuleLoaded(WebLingoCompiledModule module)
        {
        }

        public bool ShouldStop(WebLingoDebugPoint point, IWebLingoDebugScope scope) => false;
        public bool Stopped(IReadOnlyList<WebLingoDebugFrame> frames) => true;
        public bool Waits => false;

        public void Dispose()
        {
        }
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
