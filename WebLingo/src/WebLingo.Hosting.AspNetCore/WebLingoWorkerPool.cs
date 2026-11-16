using System.Collections.Concurrent;
using WebLingo.Runtime;
namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// The host's workers (specification, sections 6.10 and 7.8): a bounded pool of threads of their own, each a
/// <see cref="WebLingoWorkerId"/> from 0, which run requests one at a time each, taking them from one queue, so that a VM,
/// which keeps its thread while it runs, never holds one of ASP.NET Core's. Disposed, it finishes the work queued, then stops.
/// </summary>
public sealed class WebLingoWorkerPool : IDisposable
{
    private readonly BlockingCollection<(Action<WebLingoWorkerId> Work, TaskCompletionSource Done)> _queue = [];
    private readonly Thread[] _threads;

    public WebLingoWorkerPool(int workers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        _threads = new Thread[workers];
        for (var i = 0; i < workers; i++)
        {
            var worker = new WebLingoWorkerId(i);
            _threads[i] = new Thread(() => Serve(worker)) { IsBackground = true, Name = $"WebLingo worker {i}" };
            _threads[i].Start();
        }
    }

    /// <summary>How many workers it has.</summary>
    public int Count => _threads.Length;

    /// <summary>Runs <paramref name="work"/> on the first worker free, given its id; completes once it has run.</summary>
    public Task Run(Action<WebLingoWorkerId> work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add((work, done));
        return done.Task;
    }

    private void Serve(WebLingoWorkerId worker)
    {
        foreach (var (work, done) in _queue.GetConsumingEnumerable())
        {
            try
            {
                work(worker);
                done.SetResult();
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        foreach (var thread in _threads) thread.Join();
        _queue.Dispose();
    }
}
