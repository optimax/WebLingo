using WebLingo.Platform.Abstractions;
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
    public void TheMostThatOpenAConnectionIsThreeOfEveryFour() =>
        Assert.Equal(DotNetBlockingPool.MaxThreads - Math.Max(1, DotNetBlockingPool.MaxThreads / 4), DotNetBlockingPool.MaxOpening);

    /// <summary>
    /// Work that opens a connection never takes every thread (the owner's answer, 27 November 2026, A1 (a) of "SQL Server's
    /// Pages Deadlock at 256 Clients Since the Work Off the Workers Was Bounded"): with more of it waiting than there are
    /// threads, other work still runs at once, and no more than the most of it runs at once.
    /// </summary>
    [Fact]
    public void WorkThatOpensAConnectionLeavesThreadsForOtherWork()
    {
        var count = DotNetBlockingPool.MaxThreads + 8;
        var (opening, most) = (0, 0);
        using var gate = new ManualResetEventSlim();
        using var other = new ManualResetEventSlim();
        using var finished = new CountdownEvent(count);
        for (var i = 0; i < count; i++)
        {
            DotNetBlockingPool.Run(() =>
            {
                var now = Interlocked.Increment(ref opening);
                for (var seen = Volatile.Read(ref most); now > seen; seen = Volatile.Read(ref most))
                {
                    Interlocked.CompareExchange(ref most, now, seen);
                }
                gate.Wait(TimeSpan.FromSeconds(30));
                Interlocked.Decrement(ref opening);
                finished.Signal();
            }, opensConnection: true);
        }
        try
        {
            // As many begin as may, other tests' openings, which share the pool, perhaps holding a few for a moment.
            SpinWait.SpinUntil(() => Volatile.Read(ref opening) >= DotNetBlockingPool.MaxOpening, TimeSpan.FromSeconds(10));
            Thread.Sleep(100);
            Assert.InRange(Volatile.Read(ref most), 1, DotNetBlockingPool.MaxOpening);
            DotNetBlockingPool.Run(other.Set);
            Assert.True(other.Wait(TimeSpan.FromSeconds(10)), "the other work waited behind the work that opens");
        }
        finally
        {
            gate.Set();
        }
        Assert.True(finished.Wait(TimeSpan.FromSeconds(30)), $"only {count - finished.CurrentCount} of {count} ran");
        Assert.InRange(Volatile.Read(ref most), 1, DotNetBlockingPool.MaxOpening);
    }

    /// <summary>
    /// The deadlock it prevents: requests, more than its threads, each opening one of fewer connections than its threads,
    /// then querying on it, its query a piece of work of its own, and only then giving the connection back. Every thread
    /// waiting to open a connection, the queries of the requests that hold them would wait for ever, each open failing
    /// after its pool's time, as SqlClient's do after 15 s; so all of them finish, none timed out.
    /// </summary>
    [Fact]
    public void RequestsThatHoldAConnectionBetweenTwoPiecesOfWorkAllFinish()
    {
        var requests = 3 * DotNetBlockingPool.MaxThreads;
        using var connections = new SemaphoreSlim(Math.Max(1, DotNetBlockingPool.MaxThreads / 2));
        using var finished = new CountdownEvent(requests);
        var timedOut = 0;
        for (var i = 0; i < requests; i++)
        {
            DotNetBlockingPool.Run(() =>
            {
                if (!connections.Wait(TimeSpan.FromSeconds(20)))
                {
                    Interlocked.Increment(ref timedOut);
                    finished.Signal();
                    return;
                }
                DotNetBlockingPool.Run(() =>
                {
                    Thread.Sleep(1); // The query, on the connection the request holds.
                    connections.Release();
                    finished.Signal();
                });
            }, opensConnection: true);
        }
        Assert.True(finished.Wait(TimeSpan.FromSeconds(60)), $"only {requests - finished.CurrentCount} of {requests} finished");
        Assert.Equal(0, Volatile.Read(ref timedOut));
    }

    /// <summary>
    /// An opening whose database counts its pool's connections begins only once the gate has a place for it (the owner's
    /// answer, 27 November 2026, A1 (a) of "SQL Server's Page at 256 Clients a Fifth as Fast as Before the Bound"): ten
    /// openings of a pool of two, each keeping its place, as a connection does, two run, the rest waiting holding no thread,
    /// other work going ahead of them, and each place given back lets one more begin.
    /// </summary>
    [Fact]
    public void AnOpeningWaitsForItsGatesPlace_HoldingNoThread()
    {
        var gate = new Places(2);
        var opened = 0;
        using var all = new CountdownEvent(10);
        for (var i = 0; i < 10; i++)
        {
            DotNetBlockingPool.Run(() =>
            {
                Interlocked.Increment(ref opened);
                all.Signal();
            }, gate);
        }
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref opened) == 2, TimeSpan.FromSeconds(10)), $"{Volatile.Read(ref opened)} opened");
        Thread.Sleep(100);
        Assert.Equal((2, 0), (Volatile.Read(ref opened), gate.Room));
        using var other = new ManualResetEventSlim();
        DotNetBlockingPool.Run(other.Set);
        Assert.True(other.Wait(TimeSpan.FromSeconds(10)), "other work waited behind the openings");
        for (var given = 1; given <= 8; given++)
        {
            gate.Leave();
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref opened) == 2 + given, TimeSpan.FromSeconds(10)),
                $"{Volatile.Read(ref opened)} opened, {given} places given back");
        }
        Assert.True(all.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, gate.Room);
    }

    /// <summary>A pool's places, as a database's gate counts them, given back by the test.</summary>
    private sealed class Places(int most) : IWebLingoConnectionGate
    {
        private readonly object _sync = new();
        private readonly List<Action> _freed = [];
        private int _held;

        public int Room
        {
            get
            {
                lock (_sync) return most - _held;
            }
        }

        public bool TryEnter()
        {
            lock (_sync)
            {
                if (_held >= most) return false;
                _held++;
                return true;
            }
        }

        public void Leave()
        {
            Action[] told;
            lock (_sync)
            {
                _held--;
                told = [.. _freed];
            }
            foreach (var freed in told) freed();
        }

        public void WhenFreed(Action freed)
        {
            lock (_sync) _freed.Add(freed);
        }
    }

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
