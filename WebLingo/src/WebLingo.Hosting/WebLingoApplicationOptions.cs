using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting;

/// <summary>
/// An application's mode, whether it shows a runtime error's details in its response (specification, section 6.12),
/// its configuration's <c>"mode"</c>.
/// </summary>
public enum WebLingoMode { Production, Development }

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
/// one of them, and each worker's services; and the mode, which the host takes from the application's configuration.
/// </summary>
public sealed record WebLingoApplicationOptions(string Root, IWebLingoFileSystem FileSystem)
{
    /// <summary>How many workers the host runs requests on, <see cref="WebLingoWorkerId"/> 0 to this − 1.</summary>
    public int Workers { get; init; } = 1;

    /// <summary>Each worker's services, asked for once for each, when the application loads.</summary>
    public Func<WebLingoWorkerId, WebLingoWorkerServices> WorkerServices { get; init; } = _ => new WebLingoWorkerServices(new());

    public WebLingoMode Mode { get; init; } = WebLingoMode.Production;

    /// <summary>The clock an execution's timers wait on, or none, for virtual time.</summary>
    public IWebLingoExecutionClock? Clock { get; init; }

    /// <summary>
    /// The platform's wall clock, which a page's dates read (section 3.2, "Dates and times"); without one,
    /// <c>DateTime.now()</c> is WL3001.
    /// </summary>
    public WebLingo.Platform.Abstractions.IWebLingoClock? WallClock { get; init; }

    /// <summary>The server's own time zone, a date's local time's; without one, UTC.</summary>
    public WebLingo.Platform.Abstractions.IWebLingoTimeZone? TimeZone { get; init; }

    /// <summary>
    /// The files its scripts reach, through <c>std:fs</c>: those in the folders its configuration allows (section 7.13, "File
    /// capabilities"); <see cref="FileSystem"/>, through which the application's own are read, without one.
    /// </summary>
    public IWebLingoFileSystem? ScriptFileSystem { get; init; }

    /// <summary>The platform's cryptography, which <c>std:crypto</c> reaches (section 7.10); none without it.</summary>
    public WebLingo.Platform.Abstractions.IWebLingoCrypto? Crypto { get; init; }

    /// <summary>
    /// The machine's environment its scripts may read, through <c>std:env</c>, as far as its configuration allows (section
    /// 7.15); none without it.
    /// </summary>
    public IWebLingoEnvironment? Environment { get; init; }

    /// <summary>What its configuration allows its native modules, <c>"com"</c>'s objects (section 3.10); none without it.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Allowed { get; init; }

    /// <summary>The network its scripts may reach, the hosts its configuration allows; none without one (section 7.9).</summary>
    public WebLingo.Platform.Abstractions.IWebLingoNetwork? Network { get; init; }

    /// <summary>The databases its scripts may open, those its configuration names among them; none without them (section 7.11).</summary>
    public WebLingo.Platform.Abstractions.IWebLingoDatabases? Databases { get; init; }

    /// <summary>The mail its scripts may send, through the servers its configuration names, or their own (section 7.17); none without it.</summary>
    public WebLingo.Platform.Abstractions.IWebLingoMail? Mail { get; init; }

    /// <summary>
    /// What a request's body may be: its largest, a form's files' largest in all, how much of a file is kept in memory, and
    /// where the rest is kept (section 6.12, "Uploaded files"); 30,000,000 bytes, as much for files, 64 KB, and no
    /// temporary files, by default.
    /// </summary>
    public WebLingoUploads Uploads { get; init; } = WebLingoUploads.Default;

    /// <summary>The host's side of an execution's scheduler, a new one for each request, or none.</summary>
    public Func<IWebLingoSchedulerHost>? Scheduler { get; init; }

    /// <summary>How many instructions a request may run before it is stopped (WL3003); no limit by default.</summary>
    public long InstructionBudget { get; init; } = long.MaxValue;

    /// <summary>
    /// Where the application reports what no response can show, a line at a time, from any worker: a page that fails after
    /// its output was first sent (section 6.12, "Streaming"); nowhere by default.
    /// </summary>
    public Action<string>? Log { get; init; }

    /// <summary>
    /// How long a page waits for a client that has not taken its output before its request is cancelled (section 6.12,
    /// "Streaming"): the configuration's <c>"sendTimeout"</c>, 30 seconds by default.
    /// </summary>
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
