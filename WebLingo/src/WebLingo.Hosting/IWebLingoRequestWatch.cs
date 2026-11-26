using WebLingo.VM;
namespace WebLingo.Hosting;

/// <summary>
/// What a request's host is told of it while it runs, so that the host can end it once a reload has left it on an older
/// generation for its site's drain (specification, section 6.12, "Reloading"; section 9, "M0.22 parts", part 1): the
/// generation it is served from, before any of its code runs, and its debugger's thread, where it has one, whose stops the
/// drain does not end it in. <see cref="WebLingoApplication.Handle"/> tells it, on the request's worker; the host keeps the
/// time, the portable core having no timers.
/// </summary>
public interface IWebLingoRequestWatch
{
    /// <summary>The number of the generation the request is served from (<see cref="WebLingoApplication.GenerationNumber"/>), told once.</summary>
    void Serving(int generation);

    /// <summary>The request's debugger's thread, told once it is made, where the application runs under a debugger.</summary>
    void Debugged(IWebLingoDebuggerThread thread);
}
