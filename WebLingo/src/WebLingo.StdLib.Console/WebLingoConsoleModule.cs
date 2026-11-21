using WebLingo.Runtime;
namespace WebLingo.StdLib.Console;

/// <summary>
/// <c>std:console</c>, a standard module (specification, section 3.9, "The console: <c>std:console</c>"), a native module
/// installed with the runtime in the <c>std</c> scheme's folder as <c>console.dll</c>, which a program imports, a script
/// being a console program only where it does (the owner's answers, 20 November 2026, "Console Module"): its writing,
/// <c>write</c> and <c>writeLn</c>, to the execution's output, and <c>writeError</c> and <c>writeErrorLn</c>, to its errors
/// (A9), and <c>print</c> and <c>printLn</c>, the same as <c>write</c> and <c>writeLn</c> (the owner's request, 20
/// November 2026, "Write Aliases"); and its reading, <c>readLine</c>, <c>readLineSync</c>, <c>prompt</c> and
/// <c>readPassword</c>, moved from
/// <c>std:system</c> (A1). Under <c>wasp</c>, where a site has no console, it is not installed, its import WL2015 (A3).
/// </summary>
public sealed class WebLingoConsoleModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.TextFunction("write", 1, new WebLingoWriteFunction().TryInvoke);
        registry.TextFunction("writeLn", 1, new WebLingoWriteLnFunction().TryInvoke);
        registry.TextFunction("print", 1, new WebLingoWriteFunction().TryInvoke);
        registry.TextFunction("printLn", 1, new WebLingoWriteLnFunction().TryInvoke);
        registry.TextFunction("writeError", 1, new WebLingoWriteErrorFunction(false).TryInvoke);
        registry.TextFunction("writeErrorLn", 1, new WebLingoWriteErrorFunction(true).TryInvoke);
        registry.Function("readLine", 0, new WebLingoReadLineFunction(false).TryInvoke);
        registry.Function("readLineSync", 0, new WebLingoReadLineFunction(true).TryInvoke);
        registry.Function("prompt", 1, new WebLingoPromptFunction().TryInvoke);
        registry.Function("readPassword", 0, new WebLingoReadPasswordFunction().TryInvoke);
    }
}
