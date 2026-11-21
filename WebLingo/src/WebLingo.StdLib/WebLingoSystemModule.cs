using WebLingo.Runtime;
namespace WebLingo.StdLib;

/// <summary>
/// <c>std:system</c> as a native module (specification, section 3.9; section 9, "Native modules", part 3), installed with
/// the runtime in the <c>std</c> scheme's folder as <c>system.dll</c>, which another implementation may replace: its
/// export, <c>delay</c>, which a host gives as its built-in, every program importing <c>std:system</c> implicitly; the
/// console's writing and reading are <c>std:console</c>'s, which a program imports (the owner's answers, 20 November
/// 2026, "Console Module", A1).
/// </summary>
public sealed class WebLingoSystemModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Function("delay", 1, new WebLingoDelayFunction().TryInvoke);
    }
}
