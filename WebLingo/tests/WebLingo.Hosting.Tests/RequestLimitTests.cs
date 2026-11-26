using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A request's limits (specification, section 6.12, "Limits"; the owner's answer, 24 November 2026, "Request Limits in
/// WASP", A1 (c)): its time, its waits included, kept by its host, the ASP.NET Core adapter here; its instructions; and its
/// heap's memory. A request that passes one is stopped, a 503, WL3003, its error shown in development only.
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
    public async Task APageWithinItsLimitsIsServed()
    {
        var application = Load(timeout: TimeSpan.FromSeconds(30), instructions: 1_000_000, units: 1_000_000);
        Assert.Equal((200, "<p>Hello.</p>"), await Get(application, "/hello"));
        Assert.Equal((200, "4999950000"), await Get(application, "/counts"));
    }

    private WebLingoApplication Load(TimeSpan? timeout = null, long instructions = WebLingoApplicationOptions.DefaultInstructionBudget,
        long units = WebLingoHeap.DefaultMaxUnits, WebLingoMode mode = WebLingoMode.Development)
    {
        var application = WebLingoApplication.Load(new WebLingoApplicationOptions(_root, new DotNetFileSystem())
        {
            Workers = _workers.Count,
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry(new WebLingoDelayFunction())),
            Clock = new WebLingoExecutionClock(new DotNetClock()),
            Scheduler = () => new DotNetSchedulerHost(),
            Mode = mode,
            RequestTimeout = timeout ?? WebLingoApplicationOptions.DefaultRequestTimeout,
            InstructionBudget = instructions,
            MaxHeapUnits = units
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

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
