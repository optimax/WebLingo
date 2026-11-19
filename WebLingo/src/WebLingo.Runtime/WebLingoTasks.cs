using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>Where a task stands (specification, section 3.8).</summary>
public enum WebLingoTaskState : byte { Pending, Done, Faulted }

/// <summary>What a task without a function of its own waits for (section 3.8): none, all its values, any of them, or a timeout.</summary>
public enum WebLingoCombinator : byte { None, All, Any, Timeout }

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

    /// <summary>
    /// Whether <c>new Task()</c> made it, a task its program completes by hand, <c>complete</c> or <c>fail</c>, and where
    /// (section 3.8, "Tasks completed by hand").
    /// </summary>
    public bool ByHand { get; set; }
    public long MadeAt { get; set; } = WebLingoExceptions.Nowhere;

    /// <summary>
    /// For a task a class extending <c>Task</c> made, that class and its fields, the heap's, an instance of it as the task
    /// is (section 3.7, "No sealed classes"); null for any other task.
    /// </summary>
    internal object? Instance { get; init; }

    /// <summary>While its <c>async</c> function waits: the VM's frame of it, and the values that frame holds.</summary>
    public object? Frame { get; set; }
    public WebLingoValue[][]? FrameRoots { get; set; }

    /// <summary>Whether something awaited it, or forgot it (<c>t.forget()</c>): a fault neither saw stops the execution at its end.</summary>
    public bool Observed { get; set; }
    public bool Forgotten { get; set; }

    /// <summary>
    /// The combinators that wait for it (<c>Task.all</c>, <c>Task.any</c>, <c>Task.timeout</c>), each with the place it
    /// has among their values, told when it completes; null for none.
    /// </summary>
    public List<(WebLingoValue Combinator, int Index)>? Joins { get; set; }

    /// <summary>
    /// For a combinator: which, the array of results <c>Task.all</c> fills, the values it still waits for, and a timeout's
    /// time, in milliseconds.
    /// </summary>
    public WebLingoCombinator Combinator { get; set; }
    public WebLingoValue Results { get; set; }
    public int Remaining { get; set; }
    public long Milliseconds { get; set; }
}

/// <summary>What resumes a waiting task's <c>async</c> function: what its <c>await</c> gives, or the exception it throws there.</summary>
public readonly record struct WebLingoResumption(WebLingoValue Task, WebLingoValue Value, bool Throws, WebLingoRuntimeError? Error, long Origin);

/// <summary>
/// What an operation run off the worker gives its task, made on the worker from the work's result, in the execution's heap:
/// its value, or its error (section 3.8, WL-P007).
/// </summary>
public delegate bool WebLingoOffWorkerFinish<in T>(T value, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error);

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

    /// <summary>A task done, with its result: the tasks waiting for it become ready, and the combinators told.</summary>
    public void Complete(WebLingoValue task, WebLingoValue result)
    {
        var state = heap.GetTask(task);
        state.State = WebLingoTaskState.Done;
        state.Result = result;
        foreach (var waiter in state.Waiters) _ready.Enqueue(new WebLingoResumption(waiter, result, false, null, 0));
        state.Waiters.Clear();
        Joined(state, false, result, null, 0);
    }

    /// <summary>
    /// <c>Task.all(values)</c>: a task that completes with an array of the values' results, in their order, once all
    /// have, a value that is not a task its own result; or faults, at once, with the first exception one of them throws,
    /// the others going on (section 3.8). Every task it is given is observed.
    /// </summary>
    public bool TryAll(List<WebLingoValue> values, out WebLingoValue combined, out WebLingoRuntimeError? error)
    {
        var initial = new WebLingoValue[values.Count];
        if (!heap.TryAllocateTask(out combined, out error) || !heap.TryAllocateArray(initial, 0, initial.Length, out var results, out error))
        {
            return false;
        }
        var state = heap.GetTask(combined);
        (state.Combinator, state.Results) = (WebLingoCombinator.All, results);
        var elements = heap.GetArray(results);
        for (var i = 0; i < values.Count && state.State == WebLingoTaskState.Pending; i++)
        {
            if (values[i].Kind != WebLingoValueKind.Task)
            {
                elements[i] = values[i];
                continue;
            }
            var given = heap.GetTask(values[i]);
            given.Observed = true;
            if (given.State == WebLingoTaskState.Done) elements[i] = given.Result;
            else if (given.State == WebLingoTaskState.Faulted) Fault(combined, given.Exception, given.Error, given.Origin);
            else
            {
                Join(given, combined, i);
                state.Remaining++;
            }
        }
        // The tasks after a fault are observed too, which it no longer waits for.
        foreach (var value in values)
        {
            if (value.Kind == WebLingoValueKind.Task) heap.GetTask(value).Observed = true;
        }
        if (state.State == WebLingoTaskState.Pending && state.Remaining == 0) Complete(combined, results);
        return true;
    }

    /// <summary>
    /// <c>Task.any(values)</c>: a task that completes, or faults, as the first of the values to complete does, a value that
    /// is not a task, or a task already complete, at once, the first such in their order; the others go on, observed
    /// (section 3.8). At least one value is needed (WL3001).
    /// </summary>
    public bool TryAny(List<WebLingoValue> values, out WebLingoValue combined, out WebLingoRuntimeError? error)
    {
        combined = WebLingoValue.Null;
        if (values.Count == 0)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "'any' needs at least one value, the first of which to complete it gives.");
            return false;
        }
        if (!heap.TryAllocateTask(out combined, out error)) return false;
        var state = heap.GetTask(combined);
        state.Combinator = WebLingoCombinator.Any;
        foreach (var value in values)
        {
            if (value.Kind == WebLingoValueKind.Task) heap.GetTask(value).Observed = true;
        }
        for (var i = 0; i < values.Count && state.State == WebLingoTaskState.Pending; i++)
        {
            if (values[i].Kind != WebLingoValueKind.Task)
            {
                Complete(combined, values[i]);
                break;
            }
            var given = heap.GetTask(values[i]);
            if (given.State == WebLingoTaskState.Done) Complete(combined, given.Result);
            else if (given.State == WebLingoTaskState.Faulted) Fault(combined, given.Exception, given.Error, given.Origin);
        }
        for (var i = 0; i < values.Count && state.State == WebLingoTaskState.Pending; i++)
        {
            if (values[i].Kind == WebLingoValueKind.Task) Join(heap.GetTask(values[i]), combined, i);
        }
        return true;
    }

    /// <summary>
    /// <c>Task.timeout(task, milliseconds)</c>: a task that completes, or faults, as the task does, or faults with a
    /// timeout (WL3010, a <c>TimeoutError</c> when caught) once that time has passed first, the task going on, observed; a
    /// value that is not a task completes it at once (section 3.8). The time is an integer of 0 or more (WL3001).
    /// </summary>
    public bool TryTimeout(WebLingoValue value, WebLingoValue milliseconds, out WebLingoValue combined, out WebLingoRuntimeError? error)
    {
        combined = WebLingoValue.Null;
        if (milliseconds.Kind != WebLingoValueKind.Int64 || milliseconds.AsInt64 < 0)
        {
            error = new WebLingoArgument("timeout", 1, 2, "the time in milliseconds", "an integer, 0 or more").Wrong(milliseconds, heap);
            return false;
        }
        if (!heap.TryAllocateTask(out combined, out error)) return false;
        var state = heap.GetTask(combined);
        (state.Combinator, state.Milliseconds) = (WebLingoCombinator.Timeout, milliseconds.AsInt64);
        if (value.Kind != WebLingoValueKind.Task)
        {
            Complete(combined, value);
            return true;
        }
        var given = heap.GetTask(value);
        given.Observed = true;
        if (given.State == WebLingoTaskState.Done) Complete(combined, given.Result);
        else if (given.State == WebLingoTaskState.Faulted) Fault(combined, given.Exception, given.Error, given.Origin);
        else
        {
            Join(given, combined, 0);
            _timers.Add((Now + milliseconds.AsInt64, _order++, combined));
        }
        return true;
    }

    private static void Join(WebLingoTask given, WebLingoValue combinator, int index)
    {
        (given.Joins ??= []).Add((combinator, index));
    }

    /// <summary>A task has completed, or faulted: each combinator still waiting for it learns how (section 3.8).</summary>
    private void Joined(WebLingoTask state, bool faulted, WebLingoValue value, WebLingoRuntimeError? error, long origin)
    {
        if (state.Joins is not { } joins) return;
        state.Joins = null;
        foreach (var (combinator, index) in joins)
        {
            var waiting = heap.GetTask(combinator);
            if (waiting.State != WebLingoTaskState.Pending) continue;
            if (faulted)
            {
                Fault(combinator, value, error, origin);
            }
            else if (waiting.Combinator != WebLingoCombinator.All)
            {
                Complete(combinator, value);
            }
            else
            {
                heap.GetArray(waiting.Results)[index] = value;
                if (--waiting.Remaining == 0) Complete(combinator, waiting.Results);
            }
        }
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
        Joined(state, true, exception, error, origin);
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
    /// Throws <paramref name="error"/> at the <c>await</c> where <paramref name="waiter"/>'s function waits for
    /// <paramref name="task"/>, which goes on waiting for nothing: a program stuck awaiting a task made by hand (section
    /// 3.8, "Tasks completed by hand").
    /// </summary>
    public void Raise(WebLingoValue task, WebLingoValue waiter, WebLingoRuntimeError error)
    {
        heap.GetTask(task).Waiters.Remove(waiter);
        _ready.Enqueue(new WebLingoResumption(waiter, WebLingoValue.Null, true, error, WebLingoExceptions.Nowhere));
    }

    /// <summary>The tasks of operations running off the worker, which the collector keeps until they finish.</summary>
    private readonly List<WebLingoValue> _operations = [];

    /// <summary>
    /// Runs blocking work off the worker, on the host's threads (WL-P004, WL-P008), giving at once a task that completes,
    /// on the worker, with what <paramref name="finish"/> makes of the work's result, or faults with the error
    /// <paramref name="failed"/> makes of its exception, when the worker next has nothing else to run (WL-P007). WL3001 for
    /// a host without a scheduler of its own (<see cref="WebLingoHeap.SchedulerHost"/>).
    /// </summary>
    public bool TryRunOffWorker<T>(Func<T> work, WebLingoOffWorkerFinish<T> finish, Func<Exception, WebLingoRuntimeError?> failed,
        out WebLingoValue task, out WebLingoRuntimeError? error) =>
        TryRunOffWorker(work, finish, failed, WebLingoValue.Null, out task, out error);

    /// <summary>
    /// Runs blocking work off the worker, as the other overload does, the collector keeping <paramref name="keep"/>, a
    /// value of the heap <paramref name="finish"/> uses, such as the instance an operation opens, until the work finishes.
    /// </summary>
    public bool TryRunOffWorker<T>(Func<T> work, WebLingoOffWorkerFinish<T> finish, Func<Exception, WebLingoRuntimeError?> failed,
        WebLingoValue keep, out WebLingoValue task, out WebLingoRuntimeError? error)
    {
        task = WebLingoValue.Null;
        if (heap.SchedulerHost is not { } host)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "This host runs no operations off its worker.");
            return false;
        }
        if (!heap.TryAllocateTask(out var made, out error)) return false;
        task = made;
        _operations.Add(made);
        var kept = keep.Kind != WebLingoValueKind.Null;
        if (kept) _operations.Add(keep);
        host.RunBlocking(() =>
        {
            T value;
            try
            {
                value = work();
            }
            catch (Exception exception) when (failed(exception) is { } wrong)
            {
                host.Post(() =>
                {
                    if (kept) _operations.Remove(keep);
                    Finished(made, wrong);
                });
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                var unexpected = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"The operation failed: {exception.Message}");
                host.Post(() =>
                {
                    if (kept) _operations.Remove(keep);
                    Finished(made, unexpected);
                });
                return;
            }
            host.Post(() =>
            {
                if (kept) _operations.Remove(keep);
                if (finish(value, heap, out var result, out var finishError)) Finished(made, null, result);
                else Finished(made, finishError);
            });
        });
        return true;
    }

    /// <summary>
    /// A task that its host completes, with <c>null</c>, by calling, once, from any thread, the action
    /// <paramref name="start"/> is given; posted back to the worker, as an operation's end is, so that the execution waits
    /// for it as for an operation off its worker (WL-P007): a page waiting for its client (section 6.12, "Streaming"). WL3001
    /// for a host without a scheduler of its own (<see cref="WebLingoHeap.SchedulerHost"/>).
    /// </summary>
    public bool TryWaitForHost(Action<Action> start, out WebLingoValue task, out WebLingoRuntimeError? error) =>
        TryWaitForHost<bool>(done => start(() => done(true)), (bool _, WebLingoHeap _, out WebLingoValue result, out WebLingoRuntimeError? failed) =>
        {
            (result, failed) = (WebLingoValue.Null, null);
            return true;
        }, out task, out error);

    /// <summary>
    /// A task that its host completes by calling, once, from any thread, the action <paramref name="start"/> is given, with
    /// a value, which <paramref name="finish"/> makes the task's result of, or its error, on the worker, as an operation's
    /// end is (WL-P007): a request's body read as it arrives (section 6.12, "Streaming"). WL3001 for a host without a
    /// scheduler of its own.
    /// </summary>
    public bool TryWaitForHost<T>(Action<Action<T>> start, WebLingoOffWorkerFinish<T> finish, out WebLingoValue task, out WebLingoRuntimeError? error)
    {
        task = WebLingoValue.Null;
        if (heap.SchedulerHost is not { } host)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "This host runs no operations off its worker.");
            return false;
        }
        if (!heap.TryAllocateTask(out var made, out error)) return false;
        task = made;
        _operations.Add(made);
        // The host calls it once, from any thread; its scheduler host's Post hands it to the worker.
        start(value =>
        {
            host.Post(() =>
            {
                if (finish(value, heap, out var result, out var failed)) Finished(made, null, result);
                else Finished(made, failed);
            });
        });
        return true;
    }

    /// <summary>An operation's end, on the worker: its task completed with its result, or faulted with its error.</summary>
    private void Finished(WebLingoValue task, WebLingoRuntimeError? error, WebLingoValue result = default)
    {
        _operations.Remove(task);
        if (error is null) Complete(task, result);
        else Fault(task, WebLingoValue.Null, error, WebLingoExceptions.Nowhere);
    }

    /// <summary>
    /// With operations running off the worker: runs those that have finished, waiting for one up to
    /// <paramref name="milliseconds"/> (-1 without end); whether any did. False at once without any.
    /// </summary>
    private bool TryRunFinished(int milliseconds) =>
        _operations.Count > 0 && heap.SchedulerHost is { } host && host.RunPosted(milliseconds);

    /// <summary>
    /// Completes the earliest timer, waiting for it on the host's clock, or advancing virtual time to it; false if there
    /// is none. Timers due at the same time complete in the order they were made.
    /// </summary>
    /// <remarks>
    /// Operations off the worker that have finished come first; with some still running, it waits for them, up to the next
    /// timer's time on the host's clock, a short while at a time, so that its VM looks at the cancellation signal between
    /// waits; and true then means only that it may look again (WL-P007). With <see cref="Suspends"/>, it never waits, but
    /// gives false, <see cref="Waiting"/>, where it would.
    /// </remarks>
    public bool TryAdvanceTime()
    {
        Waiting = false;
        if (TryRunFinished(0)) return true;
        if (Suspends)
        {
            // Rather than wait on the worker's thread, for an operation or for a timer's time, the execution returns
            // to its host, which resumes it once that work is posted back, or the time has come (section 6.6).
            var dueIn = _timers.Count == 0 ? -1 : clock is null ? 0 : Math.Max(0, EarliestDue() - clock.Milliseconds);
            if (dueIn != 0 && (_operations.Count > 0 || dueIn > 0))
            {
                (Waiting, WaitMilliseconds) = (true, dueIn);
                return false;
            }
        }
        if (_operations.Count > 0)
        {
            if (_timers.Count == 0 || clock is not null)
            {
                var wait = OperationWait;
                if (_timers.Count > 0) wait = (int)Math.Clamp(EarliestDue() - clock!.Milliseconds, 0, OperationWait);
                if (wait > 0 || _timers.Count == 0)
                {
                    TryRunFinished(wait);
                    return true;
                }
            }
        }
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
        var timed = heap.GetTask(task);
        if (timed.Combinator != WebLingoCombinator.Timeout) Complete(task, WebLingoValue.Null);
        else if (timed.State == WebLingoTaskState.Pending)
        {
            // A timeout whose task has not completed in its time (section 3.8).
            Fault(task, WebLingoValue.Null, new WebLingoRuntimeError(WebLingoDiagnosticCodes.Timeout,
                $"The task did not complete within {timed.Milliseconds} milliseconds."), WebLingoExceptions.Nowhere);
        }
        return true;
    }

    /// <summary>How long, in milliseconds, it waits for an operation at a time, between looks at the cancellation signal.</summary>
    private const int OperationWait = 100;

    /// <summary>
    /// Whether, rather than wait, <see cref="TryAdvanceTime"/> gives false with <see cref="Waiting"/> set when it would
    /// wait for an operation or a timer's time on the host's clock (<see cref="WebLingoExecutionContext.Suspends"/>).
    /// </summary>
    public bool Suspends { get; set; }

    /// <summary>Whether <see cref="TryAdvanceTime"/>'s last false was a wait, not a deadlock.</summary>
    public bool Waiting { get; private set; }

    /// <summary>For a wait: how long, in milliseconds, until the next timer is due; -1 for none.</summary>
    public long WaitMilliseconds { get; private set; }

    private long EarliestDue()
    {
        var due = long.MaxValue;
        foreach (var timer in _timers) due = Math.Min(due, timer.Due);
        return due;
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
        roots.AddRange(_operations);
    }
}
