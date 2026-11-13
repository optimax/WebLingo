using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.VM;

/// <summary>An execution's outcome: success, or the error that stopped it, with where in the source it happened.</summary>
public sealed record WsExecutionResult(bool Succeeded, WsDiagnostic? Error)
{
    public static WsExecutionResult Success { get; } = new(true, null);

    /// <summary>How many instructions the execution ran, which the instruction budget counts; 0 if none ran.</summary>
    public long Instructions { get; init; }
}

public interface IWsVirtualMachine
{
    WsExecutionResult Execute(WsCompiledModule module, WsExecutionContext context);
}
