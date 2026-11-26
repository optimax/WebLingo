using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// An execution's outcome, either VM's: success, or the error that stopped it, with where in the source it happened. It is
/// the runtime's, not a VM's, so that a host running one VM loads that VM's assembly alone.
/// </summary>
public sealed record WebLingoExecutionResult(bool Succeeded, WebLingoDiagnostic? Error)
{
    public static WebLingoExecutionResult Success { get; } = new(true, null);

    /// <summary>How many instructions the execution ran, which the instruction budget counts; 0 if none ran.</summary>
    public long Instructions { get; init; }

    /// <summary>
    /// Whether the execution has not ended, but waits, kept, for work off its worker, or for a timer's time
    /// (<see cref="WebLingoExecutionContext.Suspends"/>, specification, section 6.6): its VM's <c>Resume()</c> goes on once
    /// that work is posted back, or the time has come. Neither succeeded nor failed.
    /// </summary>
    public bool Waiting { get; init; }

    /// <summary>For an execution that waits: how long, in milliseconds, until its next timer is due; -1 for none, its work only.</summary>
    public long WaitMilliseconds { get; init; } = -1;

    /// <summary>An execution that waits, its next timer due in <paramref name="milliseconds"/>, or -1 for none.</summary>
    public static WebLingoExecutionResult Waits(long milliseconds) => new(false, null) { Waiting = true, WaitMilliseconds = milliseconds };

    /// <summary>
    /// For a worker's execution (<see cref="WebLingoExecutionContext.IsWorker"/>) that an exception nothing caught ended, the
    /// exception, copied, for its parent (section 9, "M0.26 parts", part 2); a runtime error as an instance of its class. Null
    /// otherwise, and where the exception holds what a message cannot, or its limits or a cancellation ended it.
    /// </summary>
    public WebLingoMessage? Thrown { get; init; }
}
