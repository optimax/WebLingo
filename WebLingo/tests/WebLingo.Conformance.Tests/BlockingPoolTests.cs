using WebLingo.Platform.DotNet;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// The threads blocking work off the worker runs on, WebLingo's own (specification, section 6.10; 24 November 2026, "SQL
/// Server Pages Under Many Clients"): as many as there is work blocking at once, up to its most, made at once, never waiting
/// for .NET's thread pool to grow, which added a thread or two a second.
/// </summary>
public sealed class BlockingPoolTests
{
    [Fact]
    public void WorkThatBlocksRunsAllAtOnce()
    {
        const int count = 64;
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
}
