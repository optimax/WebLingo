namespace WebLingo.Runtime;

/// <summary>
/// The host's workers (specification, section 9, "M0.26 parts", part 2), which <c>std:workers</c> reaches through the heap
/// (<see cref="WebLingoHeap.Workers"/>): a module run on a worker of its own, a thread of the host's with an execution and
/// a heap of its own, the values exchanged with it copied (<see cref="WebLingoMessage"/>). Its threads, its queues and its
/// waits are the host's, the portable core taking no lock: what arrives is given to an execution through its scheduler
/// host's <c>Post</c>, as work done off its worker is.
/// </summary>
public interface IWebLingoWorkers
{
    /// <summary>
    /// A worker started, running the module at <paramref name="path"/>, from the program's own folder, <c>.wlg</c> added to
    /// a path without an extension; false, with its error, where the module cannot be read or does not compile.
    /// </summary>
    bool TryStart(string path, out IWebLingoWorkerLink? worker, out WebLingoRuntimeError? error);

    /// <summary>Where the execution runs as a worker, its link to its parent; null otherwise.</summary>
    IWebLingoWorkerLink? Parent { get; }
}

/// <summary>
/// One end of the link between a worker and its parent: what one end posts the other receives, in the order it was
/// posted. Not disposable, so that the collector, freeing the instance that holds it, ends nothing: a worker runs until it
/// ends, is ended (<see cref="End"/>), or its program ends, never as a collection happens to fall.
/// </summary>
public interface IWebLingoWorkerLink
{
    /// <summary>A message to the other end; dropped where that end has ended.</summary>
    void Post(WebLingoMessage message);

    /// <summary>
    /// Calls <paramref name="received"/>, once, from any thread, with the next message the other end posts; or, once that
    /// end has ended and all it posted is received, with the error it ended with, once, <see cref="WebLingoWorkerReceipt.Failed"/>,
    /// and with no message after.
    /// </summary>
    void Receive(Action<WebLingoWorkerReceipt> received);

    /// <summary>The worker ended, cancelled as a request past its time is, where this is its parent's end; nothing at a worker's own.</summary>
    void End();
}

/// <summary>What a worker's link gives a <c>receive()</c>: a message, the error its other end ended with (<see cref="Failed"/>), or, ended, nothing.</summary>
public readonly record struct WebLingoWorkerReceipt(WebLingoMessage? Message, bool Failed);
