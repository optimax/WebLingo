using WebLingo.Bytecode;
using WebLingo.Runtime;
namespace WebLingo.VM;

public sealed record WsExecutionResult(bool Succeeded, string? Error);
public interface IWsVirtualMachine
{
    WsExecutionResult Execute(WsCompiledModule module, WsExecutionContext context);
}
