using System.Collections.Concurrent;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// Threads of WebLingo's own for work off the worker that blocks, a database's query, a file's reading, a subprocess's
/// capture, a mail server's conversation: the scheduler hosts' <c>RunBlocking</c> (specification, section 6.10, "bounded
/// blocking-operation pools"). Its threads are made as work comes and none waits for it, up to <see cref="MaxThreads"/>, its
/// work then queued for the first free; an idle thread waits for work, and never ends. Never .NET's thread pool, which
/// ASP.NET Core's requests, a socket's completions and every <c>async</c> continuation need: work blocking threads of that
/// pool left them nothing, its own starvation detection adding a thread or two a second, so that a site on SQL Server
/// stalled at 64 clients at once and waited 13 s at 256 (the specification's section 5.6, "Under load"; 24 November 2026,
/// "SQL Server Pages Under Many Clients"). Adapter-only (section 7.14).
/// </summary>
public static class DotNetBlockingPool
{
    /// <summary>How many threads it makes at most, an arbitrary number, easy to change.</summary>
    public const int MaxThreads = 256;

    private static readonly BlockingCollection<Action> Work = new(new ConcurrentQueue<Action>());
    private static int _threads;
    private static int _waiting;

    /// <summary>How many threads it has made, for the tests.</summary>
    public static int Threads => Volatile.Read(ref _threads);

    /// <summary>Runs <paramref name="work"/> on one of its threads, a new one where none waits and it has fewer than the most.</summary>
    public static void Run(Action work)
    {
        Work.Add(work);
        if (Work.Count > Volatile.Read(ref _waiting)) Grow();
    }

    private static void Grow()
    {
        while (true)
        {
            var count = Volatile.Read(ref _threads);
            if (count >= MaxThreads) return;
            if (Interlocked.CompareExchange(ref _threads, count + 1, count) == count) break;
        }
        new Thread(Serve) { IsBackground = true, Name = "WebLingo blocking work" }.Start();
    }

    private static void Serve()
    {
        while (true)
        {
            Interlocked.Increment(ref _waiting);
            var work = Work.Take();
            Interlocked.Decrement(ref _waiting);
            // As on .NET's pool, the work handles its own exceptions, posting its outcome back to its worker.
            work();
        }
    }
}
