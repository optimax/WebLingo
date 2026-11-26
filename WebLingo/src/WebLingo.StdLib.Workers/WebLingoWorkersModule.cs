using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Workers;

/// <summary>
/// <c>std:workers</c>, a standard module of <c>weblingo</c>'s (specification, section 9, "M0.26 parts", part 2; the owner's
/// answers, A1 (c) and A3 (b) of "M0.26: Parallel Work", A1 (a) of "M0.26, Part 2: A Worker"), a native module installed
/// with the runtime in the <c>std</c> scheme's folder as <c>workers.dll</c>, through the host's workers
/// (<see cref="WebLingoHeap.Workers"/>), which <c>wasp</c> refuses: its class <c>Worker</c>, <c>new Worker("work.wlg")</c>
/// a module run on a worker of its own, <c>worker.post(value)</c> a value copied to it and <c>await worker.receive()</c>
/// the next it posted back, <c>null</c> once it has ended, its exception thrown there, and <c>dispose()</c>, which ends it;
/// and <c>parent</c>, in the worker's module, the same two members towards its <c>Worker</c>, <c>null</c> in a program not
/// run as a worker. Every value is copied as <see cref="WebLingoMessage"/> copies it.
/// </summary>
public sealed class WebLingoWorkersModule : IWebLingoNativeModule
{
    private WebLingoNativeClass _worker = null!;
    private WebLingoNativeClass _parent = null!;

    public void Register(IWebLingoNativeRegistry registry)
    {
        // Each makes errors, a worker's exception, which a message holds (WebLingoMessage).
        _worker = registry.Class("Worker")
            .DeclareMakesErrors()
            .DeclareConstructor(1, Construct)
            .DeclareMethod("post", 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
                Post(_worker, a, heap, out result, out error))
            .DeclareMethod("receive", 0, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
                Receive(_worker, a, heap, out result, out error))
            .DeclareMethod("dispose", 0, Dispose);
        _parent = registry.Class("WorkerParent")
            .DeclareMadeByModule()
            .DeclareMakesErrors()
            .DeclareMethod("post", 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
                Post(_parent, a, heap, out result, out error))
            .DeclareMethod("receive", 0, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
                Receive(_parent, a, heap, out result, out error));
        registry.Value("parent", Parent);
    }

    /// <summary><c>new Worker(path)</c>: the module at the path, from the program's own folder, started on a worker of its own.</summary>
    private bool Construct(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (a[1].Kind != WebLingoValueKind.String)
        {
            error = new WebLingoArgument("Worker", 0, 1, "the path", "a module's path, a string").Wrong(a[1], heap);
            return false;
        }
        if (heap.Workers is not { } workers)
        {
            error = Invalid("This host runs no workers.");
            return false;
        }
        if (!workers.TryStart(heap.GetString(a[1]), out var worker, out error)) return false;
        if (_worker.TrySetHostObject(a[0], worker!, heap, out error)) return true;
        worker!.Dispose();
        return false;
    }

    /// <summary><c>post(value)</c>: the value copied to the other end, <c>null</c> given; WL3001 for a value a message cannot hold.</summary>
    private static bool Post(WebLingoNativeClass type, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!type.TryGetHostObject<IWebLingoWorkerLink>(a[0], heap, out var link, out error)) return false;
        if (!WebLingoMessage.TryCopy(heap, a[1], heap.ErrorClass, null, out var message, out error)) return false;
        link.Post(message!);
        return true;
    }

    /// <summary>
    /// <c>receive()</c>: a task of the next value the other end posts; of <c>null</c> once it has ended and all it posted is
    /// received; and throwing, once, the exception a worker ended with, at the parent's end.
    /// </summary>
    private static bool Receive(WebLingoNativeClass type, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!type.TryGetHostObject<IWebLingoWorkerLink>(a[0], heap, out var link, out error)) return false;
        if (heap.Scheduler is not { } scheduler)
        {
            error = Invalid("This host runs no tasks.");
            return false;
        }
        return scheduler.TryWaitForHost<WebLingoWorkerReceipt>(link.Receive, Received, out result, out error);
    }

    /// <summary>What arrived, read into the receiving heap, on its worker: its value, the error it throws, or <c>null</c>.</summary>
    private static bool Received(WebLingoWorkerReceipt receipt, WebLingoHeap heap, out WebLingoValue result, out bool throws,
        out WebLingoRuntimeError? error)
    {
        (result, throws, error) = (WebLingoValue.Null, receipt.Failed, null);
        return receipt.Message is not { } message || message.TryRead(heap, heap.ErrorClass, out result, out error);
    }

    /// <summary><c>dispose()</c>: the worker ended, as a request past its time is, once; nothing more is received from it.</summary>
    private bool Dispose(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_worker.TryReleaseHostObject(a[0], heap, out var worker, out error)) return false;
        (worker as IWebLingoWorkerLink)?.Dispose();
        return true;
    }

    /// <summary><c>parent</c>: its worker's parent where the program runs as a worker, made where the program begins; <c>null</c> otherwise.</summary>
    private bool Parent(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        (result, error) = (WebLingoValue.Null, null);
        if (heap.Workers?.Parent is not { } link) return true;
        return _parent.TryMakeOwn(heap, out result, out error) && _parent.TrySetHostObject(result, link, heap, out error);
    }

    private static WebLingoRuntimeError Invalid(string message) => new(WebLingoDiagnosticCodes.InvalidOperation, message);
}
