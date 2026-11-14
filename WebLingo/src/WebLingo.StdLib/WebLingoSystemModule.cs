using WebLingo.Runtime;
namespace WebLingo.StdLib;

/// <summary>
/// <c>std:system</c> as a native module (specification, section 3.9; section 9, "Native modules", part 3), installed with
/// the runtime in the <c>std</c> scheme's folder as <c>system.dll</c>, which another implementation may replace: its
/// exports, <c>write</c> and <c>writeLn</c>, writing to the execution's output, and <c>delay</c>, which a host gives every
/// module as its built-ins.
/// </summary>
public sealed class WebLingoSystemModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.TextFunction("write", 1, new WebLingoWriteFunction().TryInvoke);
        registry.TextFunction("writeLn", 1, new WebLingoWriteLnFunction().TryInvoke);
        registry.Function("delay", 1, new WebLingoDelayFunction().TryInvoke);
    }
}
