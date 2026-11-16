using WebLingo.Compiler;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting;

/// <summary>Whether an application shows a runtime error's details in its response (specification, section 6.12).</summary>
public enum WebLingoEnvironment { Production, Development }

/// <summary>
/// What a host gives an application (specification, sections 6.3 and 6.12): its root, a folder's full path, and the
/// platform's files, through which the application's are read; the host's workers, numbered from 0, each request run on
/// one of them; the host's built-ins, <c>std:system</c>'s among them, which every page sees, and the services that find
/// them and the native modules' (a registry the native resolver adds to); and the environment, which the application's
/// configuration may override.
/// </summary>
public sealed record WebLingoApplicationOptions(string Root, IWebLingoFileSystem FileSystem)
{
    /// <summary>How many workers the host runs requests on, <see cref="WebLingoWorkerId"/> 0 to this − 1.</summary>
    public int Workers { get; init; } = 1;

    /// <summary>The host's built-ins, declared to the compiler, and found, with the native modules', by <see cref="Services"/>.</summary>
    public WebLingoBuiltinRegistry Services { get; init; } = new();

    /// <summary>The host's native modules (section 3.9), or none.</summary>
    public IWebLingoNativeResolver? Native { get; init; }

    public WebLingoEnvironment Environment { get; init; } = WebLingoEnvironment.Production;

    /// <summary>The clock an execution's timers wait on, or none, for virtual time.</summary>
    public IWebLingoExecutionClock? Clock { get; init; }

    /// <summary>The host's side of an execution's scheduler, a new one for each request, or none.</summary>
    public Func<IWebLingoSchedulerHost>? Scheduler { get; init; }

    /// <summary>How many instructions a request may run before it is stopped (WL3003); no limit by default.</summary>
    public long InstructionBudget { get; init; } = long.MaxValue;
}
