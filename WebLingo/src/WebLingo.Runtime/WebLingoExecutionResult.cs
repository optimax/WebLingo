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
}
