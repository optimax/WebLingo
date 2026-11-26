namespace WebLingo.Platform.DotNet;

/// <summary>
/// Threads of WebLingo's own for work off the worker that blocks, a database's query, a file's reading, a subprocess's
/// capture, a mail server's conversation: the scheduler hosts' <c>RunBlocking</c> (specification, section 6.10, "bounded
/// blocking-operation pools"). Its threads are made as work comes and none waits for it, up to <see cref="MaxThreads"/>, four
/// for each processor (the owner's answer, 26 November 2026, A2 (a) of "M0.24: Fair Workers"; section 9, "M0.24 parts",
/// part 1), its work then queued and run in the order it came, by the first thread free; an idle thread waits for work,
/// and never ends. Never .NET's thread pool, which
/// ASP.NET Core's requests, a socket's completions and every <c>async</c> continuation need: work blocking threads of that
/// pool left them nothing, its own starvation detection adding a thread or two a second, so that a site on SQL Server
/// stalled at 64 clients at once and waited 13 s at 256 (the specification's section 5.6, "Under load"; 24 November 2026,
/// "SQL Server Pages Under Many Clients"). Adapter-only (section 7.14).
/// </summary>
public static class DotNetBlockingPool
{
    /// <summary>
    /// How many threads it makes at most: four for each of the machine's processors, 32 on a machine of 8, in place of 256
    /// on any machine until 26 November 2026, so that blocking work cannot take more of the machine than its share.
    /// </summary>
    public static int MaxThreads { get; } = 4 * Environment.ProcessorCount;

    // Its work and its threads' counts, read and changed under one lock, so that a piece of work queued always has a
    // thread woken or made for it, or a busy thread takes it next: until 26 November 2026 a thread that had just taken work
    // still counted as waiting for a moment, so that work queued then had no thread made for it, and waited for one to come
    // free ("M0.24, Part 1"), 15 of 16 pieces beginning, under load, where all should have.
    private static readonly object Sync = new();
    private static readonly Queue<Action> Work = new();
    private static int _threads;
    private static int _idle;

    /// <summary>How many threads it has made, for the tests.</summary>
    public static int Threads
    {
        get
        {
            lock (Sync) return _threads;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on one of its threads: one waiting for work, woken for it; else a new one, where it has
    /// fewer than the most; else the first that comes free, after the work queued before it.
    /// </summary>
    public static void Run(Action work)
    {
        var grow = false;
        lock (Sync)
        {
            Work.Enqueue(work);
            if (_idle > 0)
            {
                _idle--;
                Monitor.Pulse(Sync);
            }
            else if (_threads < MaxThreads)
            {
                _threads++;
                grow = true;
            }
        }
        if (grow) new Thread(Serve) { IsBackground = true, Name = "WebLingo blocking work" }.Start();
    }

    private static void Serve()
    {
        while (true)
        {
            Action work;
            lock (Sync)
            {
                // Idle until work is queued, counted so; a thread woken for work another took first waits again.
                while (Work.Count == 0)
                {
                    _idle++;
                    Monitor.Wait(Sync);
                }
                work = Work.Dequeue();
            }
            // As on .NET's pool, the work handles its own exceptions, posting its outcome back to its worker.
            work();
        }
    }
}
