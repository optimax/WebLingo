using System.Collections.Concurrent;
using WebLingo.Language;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting.DotNet;

/// <summary>
/// The host's workers (specification, sections 6.6, 6.10 and 7.8): a bounded pool of threads of their own, each a
/// <see cref="WebLingoWorkerId"/> from 0, so that a VM never holds one of ASP.NET Core's. A worker takes new work from one
/// queue that every worker shares, and a request that waits (<see cref="WebLingoRequestOutcomeKind.Waiting"/>) stays its
/// own, resumed only by it (WL-P013), when the request's scheduler host is posted work, its wait is over, or its client
/// goes, its resumption queued for that worker alone, and taken before new work; meanwhile the worker runs other requests,
/// up to <see cref="WaitingPerWorker"/> waiting at once, beyond which it takes no new work. Disposed, it finishes the work
/// queued, cancels the requests that wait, then stops. Each worker's stack is the one a compiler needs
/// (<see cref="WebLingoParser.CompilerStackSize"/>), since a page is compiled on the worker it is first requested on.
/// </summary>
public sealed class WebLingoWorkerPool : IDisposable
{
    /// <summary>How many requests may wait on one worker before it takes no new work until one has ended.</summary>
    public const int WaitingPerWorker = 64;

    private readonly BlockingCollection<Item> _shared = [];
    private readonly BlockingCollection<Item>[] _own;
    private readonly Thread[] _threads;

    public WebLingoWorkerPool(int workers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        _threads = new Thread[workers];
        _own = new BlockingCollection<Item>[workers];
        for (var i = 0; i < workers; i++) _own[i] = [];
        for (var i = 0; i < workers; i++)
        {
            var worker = new WebLingoWorkerId(i);
            _threads[i] = new Thread(() => Serve(worker), WebLingoParser.CompilerStackSize) { IsBackground = true, Name = $"WebLingo worker {i}" };
            // Each worker a single-threaded apartment's thread on Windows, its COM objects its own, its waits pumping (section 3.10).
            if (OperatingSystem.IsWindows()) _threads[i].SetApartmentState(ApartmentState.STA);
            _threads[i].Start();
        }
    }

    /// <summary>How many workers it has.</summary>
    public int Count => _threads.Length;

    /// <summary>Runs <paramref name="work"/> on the first worker free, given its id; completes once it has run.</summary>
    public Task Run(Action<WebLingoWorkerId> work)
    {
        var item = new Item(null, null, CancellationToken.None) { Work = work };
        _shared.Add(item);
        return item.Done.Task;
    }

    /// <summary>
    /// Starts a request with <paramref name="start"/> on the first worker free, given its id and the request's own
    /// scheduler host, through which the worker resumes it while it waits, until its outcome is not
    /// <see cref="WebLingoRequestOutcomeKind.Waiting"/>; that outcome is the task's. <paramref name="gone"/>, signalled,
    /// resumes a request that waits, to see that it is cancelled; <paramref name="ended"/> runs on the worker once it ends,
    /// whatever it came to.
    /// </summary>
    public Task<WebLingoRequestOutcome> Serve(Func<WebLingoWorkerId, IWebLingoSchedulerHost, WebLingoRequestOutcome> start, CancellationToken gone,
        Action? ended = null)
    {
        var item = new Item(start, ended, gone);
        _shared.Add(item);
        return item.Outcome.Task;
    }

    private void Serve(WebLingoWorkerId worker)
    {
        var own = _own[worker.Value];
        var waiting = new HashSet<Item>();
        BlockingCollection<Item>[] both = [own, _shared];
        while (true)
        {
            Item? item;
            try
            {
                if (waiting.Count >= WaitingPerWorker) item = own.Take();
                else BlockingCollection<Item>.TakeFromAny(both, out item);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                break; // Disposed, and nothing more queued.
            }
            if (item is null) continue;
            if (item.Work is { } work)
            {
                try
                {
                    work(worker);
                    item.Done.SetResult();
                }
                catch (Exception exception)
                {
                    item.Done.SetException(exception);
                }
                continue;
            }
            Step(item, worker, waiting);
        }
        foreach (var item in waiting) item.End(null, new OperationCanceledException("The workers have stopped."));
    }

    /// <summary>A request's start, or its resumption, on its worker; kept while it waits, ended otherwise.</summary>
    private void Step(Item item, WebLingoWorkerId worker, HashSet<Item> waiting)
    {
        Interlocked.Exchange(ref item.Queued, 0);
        WebLingoRequestOutcome outcome;
        try
        {
            if (item.Scheduler is null)
            {
                item.Scheduler = new Scheduler(this, worker.Value, item);
                item.Gone = item.GoneSignal.Register(() => item.Scheduler!.Wake());
                outcome = item.Start!(worker, item.Scheduler);
            }
            else if (item.Resume is { } resume)
            {
                item.Resume = null;
                outcome = resume();
            }
            else
            {
                return; // Woken once it had ended.
            }
        }
        catch (Exception exception)
        {
            waiting.Remove(item);
            item.End(null, exception);
            return;
        }
        if (outcome is { Kind: WebLingoRequestOutcomeKind.Waiting, Resume: { } next })
        {
            item.Resume = next;
            waiting.Add(item);
            item.Timer?.Dispose();
            item.Timer = outcome.WaitMilliseconds < 0 ? null
                : new Timer(_ => item.Scheduler!.Wake(), null, Math.Min(outcome.WaitMilliseconds, int.MaxValue), Timeout.Infinite);
            return;
        }
        waiting.Remove(item);
        item.End(outcome, null);
    }

    /// <summary>Queues a request's resumption for its worker, once until it is taken.</summary>
    private void Wake(int worker, Item item)
    {
        if (Interlocked.Exchange(ref item.Queued, 1) == 1) return;
        try
        {
            _own[worker].Add(item);
        }
        catch (InvalidOperationException)
        {
            // Disposed: the worker cancels it as it stops.
        }
    }

    public void Dispose()
    {
        _shared.CompleteAdding();
        foreach (var own in _own) own.CompleteAdding();
        foreach (var thread in _threads) thread.Join();
        _shared.Dispose();
        foreach (var own in _own) own.Dispose();
    }

    /// <summary>A piece of work, or a request, with what it waits for while it waits.</summary>
    private sealed class Item(Func<WebLingoWorkerId, IWebLingoSchedulerHost, WebLingoRequestOutcome>? start, Action? ended, CancellationToken gone)
    {
        public Action<WebLingoWorkerId>? Work { get; init; }
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<WebLingoWorkerId, IWebLingoSchedulerHost, WebLingoRequestOutcome>? Start { get; } = start;
        public TaskCompletionSource<WebLingoRequestOutcome> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken GoneSignal { get; } = gone;
        public Scheduler? Scheduler;
        public Func<WebLingoRequestOutcome>? Resume;
        public Timer? Timer;
        public CancellationTokenRegistration Gone;
        public int Queued;

        public void End(WebLingoRequestOutcome? outcome, Exception? exception)
        {
            Timer?.Dispose();
            Gone.Dispose();
            try
            {
                ended?.Invoke();
            }
            finally
            {
                if (exception is null) Outcome.TrySetResult(outcome!);
                else Outcome.TrySetException(exception);
            }
        }
    }

    /// <summary>
    /// A request's scheduler host (section 7.8): blocking work on WebLingo's own threads (<c>DotNetBlockingPool</c>), never
    /// .NET's pool, which ASP.NET Core's requests need, and work posted back held for its worker, which it wakes to resume
    /// the request.
    /// </summary>
    private sealed class Scheduler(WebLingoWorkerPool pool, int worker, Item item) : IWebLingoSchedulerHost
    {
        private readonly ConcurrentQueue<Action> _posted = new();
        private readonly SemaphoreSlim _signal = new(0);

        public void RunBlocking(Action work) => WebLingo.Platform.DotNet.DotNetBlockingPool.Run(work);

        public void Post(Action work)
        {
            _posted.Enqueue(work);
            _signal.Release();
            Wake();
        }

        public void Wake() => pool.Wake(worker, item);

        public bool RunPosted(int milliseconds)
        {
            if (_posted.IsEmpty && (milliseconds == 0 || !_signal.Wait(milliseconds))) return false;
            var ran = false;
            while (_posted.TryDequeue(out var work))
            {
                work();
                ran = true;
            }
            return ran;
        }
    }
}
