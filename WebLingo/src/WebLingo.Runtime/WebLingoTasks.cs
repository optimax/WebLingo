namespace WebLingo.Runtime;

/// <summary>Where a task stands (specification, section 3.8).</summary>
public enum WebLingoTaskState : byte { Pending, Done, Faulted }

/// <summary>
/// A task (specification, section 3.8): what an <c>async</c> function's call gives, or <c>delay</c>'s, an object of
/// the execution's heap. While its <c>async</c> function waits, the task keeps that function's frame, which its VM
/// knows, and the values the frame holds, which are roots of the heap.
/// </summary>
public sealed class WebLingoTask
{
    public WebLingoTaskState State { get; set; }
    public WebLingoValue Result { get; set; }

    /// <summary>The exception it faulted with: a value, or a runtime error not made one yet, and where it was first thrown.</summary>
    public WebLingoValue Exception { get; set; }
    public WebLingoRuntimeError? Error { get; set; }
    public long Origin { get; set; }

    /// <summary>The tasks whose <c>async</c> functions wait for it, each resumed when it completes, in order.</summary>
    public List<WebLingoValue> Waiters { get; } = [];

    /// <summary>While its <c>async</c> function waits: the VM's frame of it, and the values that frame holds.</summary>
    public object? Frame { get; set; }
    public WebLingoValue[][]? FrameRoots { get; set; }

    /// <summary>Whether something awaited it, or forgot it (<c>t.forget()</c>): a fault neither saw stops the execution at its end.</summary>
    public bool Observed { get; set; }
    public bool Forgotten { get; set; }
}

/// <summary>What resumes a waiting task's <c>async</c> function: what its <c>await</c> gives, or the exception it throws there.</summary>
public readonly record struct WebLingoResumption(WebLingoValue Task, WebLingoValue Value, bool Throws, WebLingoRuntimeError? Error, long Origin);

/// <summary>
/// The host's clock, which an execution's timers (<c>delay</c>) wait on; a host gives one, or the execution runs in
/// virtual time (section 3.8). The portable core reads no clock itself (WL-P006, WL-P016).
/// </summary>
public interface IWebLingoExecutionClock
{
    /// <summary>A monotonic time, in milliseconds.</summary>
    long Milliseconds { get; }

    /// <summary>Waits until <see cref="Milliseconds"/> reaches a time, when the execution has nothing else to run.</summary>
    void WaitUntil(long milliseconds);
}

/// <summary>
/// An execution's tasks (specification, section 3.8): those ready to resume, in the order they became ready;
/// <c>delay</c>'s timers; and the faults nothing has awaited yet. One worker runs them, one at a time, switching only at
/// <c>await</c> (WL-H012, WL-P013). Time is the host's clock's, or, without one, virtual: a timer completes as soon as
/// nothing else can run, time advancing to it, so that a test's delays take no time.
/// </summary>
public sealed class WebLingoScheduler(WebLingoHeap heap, IWebLingoExecutionClock? clock)
{
    private readonly Queue<WebLingoResumption> _ready = new();
    private readonly List<(long Due, long Order, WebLingoValue Task)> _timers = [];
    private readonly List<WebLingoValue> _faulted = [];
    private long _virtualNow;
    private long _order;

    private long Now => clock?.Milliseconds ?? _virtualNow;

    /// <summary><c>delay(milliseconds)</c>: a task that completes, with <c>null</c>, once that time has passed.</summary>
    public bool TryDelay(long milliseconds, out WebLingoValue task, out WebLingoRuntimeError? error)
    {
        if (!heap.TryAllocateTask(out task, out error)) return false;
        _timers.Add((Now + milliseconds, _order++, task));
        return true;
    }

    /// <summary>A task done, with its result: the tasks waiting for it become ready.</summary>
    public void Complete(WebLingoValue task, WebLingoValue result)
    {
        var state = heap.GetTask(task);
        state.State = WebLingoTaskState.Done;
        state.Result = result;
        foreach (var waiter in state.Waiters) _ready.Enqueue(new WebLingoResumption(waiter, result, false, null, 0));
        state.Waiters.Clear();
    }

    /// <summary>
    /// A task faulted: the tasks waiting for it become ready to throw its exception; with none waiting, it is kept, so
    /// that the execution's end reports it if nothing awaits it before then.
    /// </summary>
    public void Fault(WebLingoValue task, WebLingoValue exception, WebLingoRuntimeError? error, long origin)
    {
        var state = heap.GetTask(task);
        state.State = WebLingoTaskState.Faulted;
        state.Exception = exception;
        state.Error = error;
        state.Origin = origin;
        if (state.Waiters.Count > 0) state.Observed = true;
        foreach (var waiter in state.Waiters) _ready.Enqueue(new WebLingoResumption(waiter, exception, true, error, origin));
        state.Waiters.Clear();
        if (!state.Observed) _faulted.Add(task);
    }

    /// <summary>
    /// <c>await value</c> in the <c>async</c> function of the task <paramref name="waiting"/>: it resumes once the value,
    /// a task, completes; any other value, or a task already complete, at once, after what is ready already, so that
    /// <c>await</c> always lets other tasks run (section 3.8).
    /// </summary>
    public void Await(WebLingoValue waiting, WebLingoValue value)
    {
        if (value.Kind != WebLingoValueKind.Task)
        {
            _ready.Enqueue(new WebLingoResumption(waiting, value, false, null, 0));
            return;
        }
        var awaited = heap.GetTask(value);
        awaited.Observed = true;
        switch (awaited.State)
        {
            case WebLingoTaskState.Pending:
                awaited.Waiters.Add(waiting);
                break;
            case WebLingoTaskState.Done:
                _ready.Enqueue(new WebLingoResumption(waiting, awaited.Result, false, null, 0));
                break;
            default:
                _ready.Enqueue(new WebLingoResumption(waiting, awaited.Exception, true, awaited.Error, awaited.Origin));
                break;
        }
    }

    public bool TryTakeReady(out WebLingoResumption resumption) => _ready.TryDequeue(out resumption);

    /// <summary>
    /// Completes the earliest timer, waiting for it on the host's clock, or advancing virtual time to it; false if there
    /// is none. Timers due at the same time complete in the order they were made.
    /// </summary>
    public bool TryAdvanceTime()
    {
        if (_timers.Count == 0) return false;
        var next = 0;
        for (var i = 1; i < _timers.Count; i++)
        {
            if (_timers[i].Due < _timers[next].Due || (_timers[i].Due == _timers[next].Due && _timers[i].Order < _timers[next].Order))
            {
                next = i;
            }
        }
        var (due, _, task) = _timers[next];
        _timers.RemoveAt(next);
        if (clock is not null)
        {
            if (clock.Milliseconds < due) clock.WaitUntil(due);
        }
        else if (_virtualNow < due)
        {
            _virtualNow = due;
        }
        Complete(task, WebLingoValue.Null);
        return true;
    }

    /// <summary>The first fault that nothing awaited, nor forgot, which stops the execution at its end (section 3.8).</summary>
    public bool TryFindUnobserved(out WebLingoTask faulted)
    {
        foreach (var task in _faulted)
        {
            var state = heap.GetTask(task);
            if (state.Observed || state.Forgotten) continue;
            faulted = state;
            return true;
        }
        faulted = null!;
        return false;
    }

    /// <summary>The values it holds, which the heap's collections keep.</summary>
    internal void CopyRoots(List<WebLingoValue> roots)
    {
        foreach (var resumption in _ready)
        {
            roots.Add(resumption.Task);
            roots.Add(resumption.Value);
        }
        foreach (var (_, _, task) in _timers) roots.Add(task);
        roots.AddRange(_faulted);
    }
}
