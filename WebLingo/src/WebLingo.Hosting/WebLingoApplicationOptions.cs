using WebLingo.Compiler;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting;

/// <summary>Whether an application shows a runtime error's details in its response (specification, section 6.12).</summary>
public enum WebLingoEnvironment { Production, Development }

/// <summary>
/// What a host gives one worker (specification, section 6.12): its built-ins, <c>std:system</c>'s among them, which every
/// page sees, declared to the compiler, and its native modules, whose functions the resolver adds to the built-ins as it
/// loads them. Each worker's are its own, so that loading a native module on one never changes what another reads, and
/// nothing needs a lock (WL-P013).
/// </summary>
public sealed record WebLingoWorkerServices(WebLingoBuiltinRegistry Builtins, IWebLingoNativeResolver? Native = null);

/// <summary>
/// What a host gives an application (specification, sections 6.3 and 6.12): its root, a folder's full path, and the
/// platform's files, through which the application's are read; the host's workers, numbered from 0, each request run on
/// one of them, and each worker's services; and the environment, which the host takes from the application's
/// configuration.
/// </summary>
public sealed record WebLingoApplicationOptions(string Root, IWebLingoFileSystem FileSystem)
{
    /// <summary>How many workers the host runs requests on, <see cref="WebLingoWorkerId"/> 0 to this − 1.</summary>
    public int Workers { get; init; } = 1;

    /// <summary>Each worker's services, asked for once for each, when the application loads.</summary>
    public Func<WebLingoWorkerId, WebLingoWorkerServices> WorkerServices { get; init; } = _ => new WebLingoWorkerServices(new());

    public WebLingoEnvironment Environment { get; init; } = WebLingoEnvironment.Production;

    /// <summary>The clock an execution's timers wait on, or none, for virtual time.</summary>
    public IWebLingoExecutionClock? Clock { get; init; }

    /// <summary>The host's side of an execution's scheduler, a new one for each request, or none.</summary>
    public Func<IWebLingoSchedulerHost>? Scheduler { get; init; }

    /// <summary>How many instructions a request may run before it is stopped (WL3003); no limit by default.</summary>
    public long InstructionBudget { get; init; } = long.MaxValue;
}
