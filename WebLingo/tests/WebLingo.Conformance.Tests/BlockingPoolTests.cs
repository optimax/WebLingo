using WebLingo.Platform.DotNet;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// The threads blocking work off the worker runs on, WebLingo's own (specification, section 6.10; 24 November 2026, "SQL
/// Server Pages Under Many Clients"): as many as there is work blocking at once, up to its most, made at once, never waiting
/// for .NET's thread pool to grow, which added a thread or two a second; its most four for each processor, work beyond
/// them queued until a thread is free (section 9, "M0.24 parts", part 1). Its threads are the process's, which other tests
/// share, so that each test holds them for a moment only.
/// </summary>
public sealed class BlockingPoolTests
{
    [Fact]
    public void TheMostIsFourThreadsForEachProcessor() => Assert.Equal(4 * Environment.ProcessorCount, DotNetBlockingPool.MaxThreads);

    [Fact]
    public void WorkThatBlocksRunsAllAtOnce_UpToTheMost()
    {
        var count = DotNetBlockingPool.MaxThreads / 2;
        var started = 0;
        using var gate = new ManualResetEventSlim();
        for (var i = 0; i < count; i++)
        {
            DotNetBlockingPool.Run(() =>
            {
                Interlocked.Increment(ref started);
                gate.Wait(TimeSpan.FromSeconds(30));
            });
        }
        try
        {
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref started) == count, TimeSpan.FromSeconds(10)),
                $"only {Volatile.Read(ref started)} of {count} began");
            Assert.InRange(DotNetBlockingPool.Threads, count, DotNetBlockingPool.MaxThreads);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public void WorkBeyondTheMostWaitsForAThread_AndAllOfItRuns()
    {
        var count = DotNetBlockingPool.MaxThreads + 16;
        var (running, most, done) = (0, 0, 0);
        using var gate = new ManualResetEventSlim();
        using var finished = new CountdownEvent(count);
        for (var i = 0; i < count; i++)
        {
            DotNetBlockingPool.Run(() =>
            {
                var now = Interlocked.Increment(ref running);
                for (var seen = Volatile.Read(ref most); now > seen; seen = Volatile.Read(ref most))
                {
                    Interlocked.CompareExchange(ref most, now, seen);
                }
                gate.Wait(TimeSpan.FromSeconds(30));
                Interlocked.Decrement(ref running);
                Interlocked.Increment(ref done);
                finished.Signal();
            });
        }
        try
        {
            // As many as there are threads begin, and no more, the rest waiting.
            SpinWait.SpinUntil(() => Volatile.Read(ref running) >= DotNetBlockingPool.MaxThreads / 2, TimeSpan.FromSeconds(10));
            Thread.Sleep(100);
            Assert.InRange(Volatile.Read(ref most), 1, DotNetBlockingPool.MaxThreads);
            Assert.Equal(0, Volatile.Read(ref done));
        }
        finally
        {
            gate.Set();
        }
        Assert.True(finished.Wait(TimeSpan.FromSeconds(30)), $"only {Volatile.Read(ref done)} of {count} ran");
        Assert.InRange(Volatile.Read(ref most), 1, DotNetBlockingPool.MaxThreads);
        Assert.InRange(DotNetBlockingPool.Threads, 1, DotNetBlockingPool.MaxThreads);
    }
}
