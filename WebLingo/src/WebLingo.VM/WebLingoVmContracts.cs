using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.VM;

public interface IWebLingoVirtualMachine
{
    WebLingoExecutionResult Execute(WebLingoCompiledModule module, WebLingoExecutionContext context);
}
