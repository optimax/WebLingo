using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// Threads of WebLingo's own for work off the worker that blocks, a database's query, a file's reading, a subprocess's
/// capture, a mail server's conversation: the scheduler hosts' <c>RunBlocking</c> (specification, section 6.10, "bounded
/// blocking-operation pools"). Its threads are made as work comes and none waits for it, up to <see cref="MaxThreads"/>, four
/// for each processor (the owner's answer, 26 November 2026, A2 (a) of "M0.24: Fair Workers"; section 9, "M0.24 parts",
/// part 1), its work then queued and run in the order it came, by the first thread free; an idle thread waits for work,
/// and never ends. Work that waits for a pooled connection, a database's open, runs on at most
/// <see cref="MaxOpening"/> of them at once, three of every four, the rest kept for work on a connection a request holds,
/// its queries, so that a request holding one always finishes and gives it back (the owner's answer, 27 November 2026, A1
/// (a) of "SQL Server's Pages Deadlock at 256 Clients Since the Work Off the Workers Was Bounded"): until then, 256 requests
/// held SqlClient's 100 connections while their queries waited in its queue behind every thread waiting to open another.
/// And an open whose database counts its pool's connections (<see cref="IWebLingoConnectionGate"/>) begins only once the
/// gate has a place for it, waiting in the queue until then, holding no thread, other work going ahead of it (A1 (a) of
/// "SQL Server's Page at 256 Clients a Fifth as Fast as Before the Bound"), so that no thread waits in the database's pool.
/// Never .NET's thread pool, which
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

    /// <summary>
    /// How many of its threads at most run work that opens a connection at once: three of every four, 24 of 32, a quarter,
    /// at least one, kept for the other work.
    /// </summary>
    public static int MaxOpening { get; } = MaxThreads - Math.Max(1, MaxThreads / 4);

    /// <summary>A connection's opening, numbered as it came, and the gate whose place it waits for, if any.</summary>
    private sealed record Opening(long Order, Action Work, IWebLingoConnectionGate? Gate);

    // Its work, in two queues, a connection's opening in one and everything else in the other, each piece numbered as it
    // came, and its threads' counts, read and changed under one lock, so that a piece of work that can begin always has a
    // thread woken or made for it, or a busy thread takes it next: until 26 November 2026 a thread that had just taken work
    // still counted as waiting for a moment, so that work queued then had no thread made for it, and waited for one to come
    // free ("M0.24, Part 1"), 15 of 16 pieces beginning, under load, where all should have. How many openings wait on each
    // gate, and on none, are counted as they come and go, so that what may begin is known without a look at each.
    private static readonly object Sync = new();
    private static readonly Queue<(long Order, Action Work)> Other = new();
    private static readonly List<Opening> Opens = [];
    private static readonly Dictionary<IWebLingoConnectionGate, int> Waiting = [];
    private static int _ungated;
    private static long _order;
    private static int _threads;
    private static int _idle;
    private static int _coming; // Threads woken or made for work that have not yet taken any.
    private static int _opening; // Threads running work that opens a connection.

    // The gates it is told of when a place is given back, each once, before the first work that waits on it is queued, so
    // that no place given back is missed.
    private static readonly object WatchedSync = new();
    private static readonly HashSet<IWebLingoConnectionGate> Watched = [];

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
    public static void Run(Action work) => Run(work, false);

    /// <summary>
    /// Runs <paramref name="work"/> as the other overload does; where it <paramref name="opensConnection"/>, waiting for a
    /// pooled one, only while fewer than <see cref="MaxOpening"/> threads run such work, else after one of them finishes.
    /// </summary>
    public static void Run(Action work, bool opensConnection) => Queue(work, opensConnection, null);

    /// <summary>
    /// Runs <paramref name="work"/>, which opens a connection of <paramref name="gate"/>'s pool, as work that opens one, once
    /// the gate has a place for it, which it takes for the work (<see cref="IWebLingoConnectionGate.TryEnter"/>).
    /// </summary>
    public static void Run(Action work, IWebLingoConnectionGate gate)
    {
        lock (WatchedSync)
        {
            if (Watched.Add(gate)) gate.WhenFreed(Freed);
        }
        Queue(work, true, gate);
    }

    private static void Queue(Action work, bool opensConnection, IWebLingoConnectionGate? gate)
    {
        int made;
        lock (Sync)
        {
            if (!opensConnection) Other.Enqueue((_order++, work));
            else
            {
                Opens.Add(new Opening(_order++, work, gate));
                if (gate is null) _ungated++;
                else Waiting[gate] = Waiting.GetValueOrDefault(gate) + 1;
            }
            made = Dispatch();
        }
        Start(made);
    }

    /// <summary>A gate's place given back: the openings waiting on it may begin.</summary>
    private static void Freed()
    {
        int made;
        lock (Sync) made = Dispatch();
        Start(made);
    }

    private static void Serve()
    {
        var opened = false;
        var called = true; // Made for work, and counted as coming.
        while (true)
        {
            Action work;
            int made;
            lock (Sync)
            {
                if (opened) _opening--;
                if (called) _coming--;
                // Idle until work it may begin is queued, counted so; a thread woken for work another took first waits again.
                while (!TryTake(out work, out opened))
                {
                    _idle++;
                    Monitor.Wait(Sync);
                    _coming--; // Woken by Dispatch, which counted it coming.
                }
                called = false;
                // Work it has made room for, an opening's slot it gave back, given a thread of its own.
                made = Dispatch();
            }
            Start(made);
            // As on .NET's pool, the work handles its own exceptions, posting its outcome back to its worker.
            work();
        }
    }

    /// <summary>
    /// The first work queued that may begin now: an opening only while fewer than the most run, and only where its gate, if
    /// it has one, gives it a place, taken for it here; other work queued before it going first.
    /// </summary>
    private static bool TryTake(out Action work, out bool opening)
    {
        var at = -1;
        if (_opening < MaxOpening)
        {
            List<IWebLingoConnectionGate>? full = null;
            for (var i = 0; i < Opens.Count; i++)
            {
                var candidate = Opens[i];
                if (Other.Count > 0 && Other.Peek().Order < candidate.Order) break; // Older work goes first.
                if (candidate.Gate is null)
                {
                    at = i;
                    break;
                }
                if (full is not null && full.Contains(candidate.Gate)) continue;
                if (candidate.Gate.TryEnter())
                {
                    at = i;
                    break;
                }
                (full ??= []).Add(candidate.Gate);
            }
        }
        if (at >= 0)
        {
            var taken = Opens[at];
            Opens.RemoveAt(at);
            if (taken.Gate is null) _ungated--;
            else if (--Waiting[taken.Gate] == 0) Waiting.Remove(taken.Gate);
            _opening++;
            (work, opening) = (taken.Work, true);
            return true;
        }
        if (Other.Count > 0)
        {
            (work, opening) = (Other.Dequeue().Work, false);
            return true;
        }
        (work, opening) = (() => { }, false);
        return false;
    }

    /// <summary>Under the lock: how many queued openings may begin now, those without a gate and those their gates have room for.</summary>
    private static int OpeningsThatMayBegin()
    {
        var room = MaxOpening - _opening;
        if (room <= 0) return 0;
        long may = _ungated;
        foreach (var (gate, waiting) in Waiting) may += Math.Min(waiting, Math.Max(0, gate.Room));
        return (int)Math.Min(may, room);
    }

    /// <summary>
    /// Under the lock: as many threads woken, or counted to be made, as there is work that may begin and no thread coming
    /// for, up to the most; how many to make.
    /// </summary>
    private static int Dispatch()
    {
        var made = 0;
        while (Other.Count + OpeningsThatMayBegin() > _coming)
        {
            if (_idle > 0)
            {
                _idle--;
                _coming++;
                Monitor.Pulse(Sync);
            }
            else if (_threads < MaxThreads)
            {
                _threads++;
                _coming++;
                made++;
            }
            else break;
        }
        return made;
    }

    private static void Start(int made)
    {
        for (var i = 0; i < made; i++) new Thread(Serve) { IsBackground = true, Name = "WebLingo blocking work" }.Start();
    }
}
