using System.Text;
using Microsoft.AspNetCore.Http;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Hosting.DotNet;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A request's quantum (specification, section 9, "M0.24 parts", part 2; the owner's answers, A1 (a) of "M0.24: Fair Workers"
/// and of "M0.24, Part 2: The Quantum"): a request that has run its quantum of instructions yields its worker, as at an
/// <c>await</c> already done, before whichever instruction comes, its worker running the other requests' ready work, then
/// going on where it was, so that a page that computes for long does not hold up the others on its worker.
/// </summary>
public sealed class QuantumTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "weblingo-quantum-" + Guid.NewGuid().ToString("N"));
    private readonly WebLingoWorkerPool _workers = new(1);

    public QuantumTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "hello.wasp"), "<p>Hello.</p>");
        File.WriteAllText(Path.Combine(_root, "loops.wasp"), "<% var i = 0; while (true) { i = i + 1; } %>never");
        File.WriteAllText(Path.Combine(_root, "counts.wasp"),
            "<% var sum = 0; for (var i = 0; i < 1000000; i++) { sum += i; } var s = \"\"; for (var j = 0; j < 2000; j++) { s = s + \"x\"; } %>"
            + "<%= sum %> <%= s.length %>");
    }

    public void Dispose()
    {
        _workers.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheDefaultQuantumIsAHundredThousandInstructions()
    {
        Assert.Equal(100_000L, new WebLingoApplicationOptions(_root, new DotNetFileSystem()).Quantum);
    }

    [Fact]
    public async Task APageThatComputesYieldsItsWorker_TheOthersAnsweredMeanwhile()
    {
        // One worker: the looping page, stopped by its time, and the short one, asked for while it loops, answered first.
        var application = Load(quantum: WebLingoApplicationOptions.DefaultQuantum);
        var loops = Get(application, "/loops");
        await Task.Delay(200);
        var hello = await Get(application, "/hello");
        Assert.False(loops.IsCompleted);
        Assert.Equal((200, "<p>Hello.</p>"), hello);
        Assert.Equal(503, (await loops).Status);
    }

    [Fact]
    public async Task WithoutAQuantumAPageThatComputesHoldsItsWorker()
    {
        // The short page waits for the looping one's stop, 3 seconds after it began, its answer written after the loop's own.
        var application = Load(quantum: 0);
        var loops = Get(application, "/loops");
        await Task.Delay(200);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        var hello = await Get(application, "/hello");
        Assert.True(waited.Elapsed > TimeSpan.FromSeconds(2), waited.Elapsed.ToString());
        Assert.Equal(503, (await loops).Status);
        Assert.Equal((200, "<p>Hello.</p>"), hello);
    }

    [Fact]
    public async Task APageThatYieldsOftenGivesWhatItWouldHaveGiven()
    {
        // A quantum of 1,024 instructions, as often as the VM looks, the loop and its strings going on where they were each time;
        // its thousands of yields given a minute, each a timer's hop, which a loaded machine makes slowly (CI's Linux took
        // more than the others' 3 seconds, 27 November 2026).
        var (status, body) = await Get(Load(quantum: 1024, TimeSpan.FromMinutes(1)), "/counts");
        Assert.Equal((200, "499999500000 2000"), (status, body));
    }

    private WebLingoApplication Load(long quantum, TimeSpan? timeout = null)
    {
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(_root, new DotNetFileSystem())
        {
            Workers = _workers.Count,
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry(new WebLingoDelayFunction())),
            Clock = new WebLingoExecutionClock(new DotNetClock()),
            Scheduler = () => new DotNetSchedulerHost(),
            Mode = WebLingoMode.Development,
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(3),
            Quantum = quantum
        }, out var error);
        Assert.True(application is not null, error);
        return application;
    }

    private async Task<(int Status, string Body)> Get(WebLingoApplication application, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        await WebLingoAspNetCore.Serve(context, application, _workers);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }
}
