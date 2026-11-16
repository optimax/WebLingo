using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.VM;

public interface IWebLingoVirtualMachine
{
    WebLingoExecutionResult Execute(WebLingoCompiledModule module, WebLingoExecutionContext context);

    /// <summary>
    /// Goes on with the execution that its last <see cref="Execute"/>, or <see cref="Resume"/>, left waiting
    /// (<see cref="WebLingoExecutionResult.Waiting"/>, <see cref="WebLingoExecutionContext.Suspends"/>, specification,
    /// section 6.6), on the same worker.
    /// </summary>
    WebLingoExecutionResult Resume();
}
