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

    /// <summary>
    /// The folder of the site it lies over (section 6.12, "A site over a base"), its configuration's <c>"base"</c>, a full
    /// path, whose files its own come before (<see cref="WebLingoBaseFiles"/>); null for none.
    /// </summary>
    public string? Base { get; init; }

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

    /// <summary>What its server takes of a client before it refuses it, <c>wasp.config</c>'s <c>"limits"</c>' (section 9, "M0.23 parts", part 3).</summary>
    public WebLingoHttpLimits HttpLimits { get; init; } = WebLingoHttpLimits.Default;

    /// <summary>
    /// The proxies it trusts, <c>wasp.config</c>'s <c>"proxies"</c>, each an address or a range, <c>10.0.0.0/8</c>, whose
    /// <c>Forwarded</c>, or <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>, give a request's client and scheme; none by
    /// default (section 9, "M0.23 parts", part 4). Its hosts read them, the portable core knowing no addresses.
    /// </summary>
    public IReadOnlyList<string> Proxies { get; init; } = [];

    /// <summary>
    /// Where the site keeps its sessions (section 6.12, "Sessions and cookies"), which a request's <c>request.session</c>
    /// reaches, its id made with <see cref="Crypto"/>; none by default, <c>request.session</c> then an error.
    /// </summary>
    public IWebLingoSessionStore? Sessions { get; init; }

    /// <summary>
    /// The memory the site's requests share, <c>std:shared</c>'s (section 6.12, "Memory a site's requests share"): one for the
    /// site, made where its host starts, kept across its reloads, which reload its files alone; none by default.
    /// </summary>
    public WebLingo.Platform.Abstractions.IWebLingoSharedStore? Shared { get; init; }

    /// <summary>The host's side of an execution's scheduler, a new one for each request, or none.</summary>
    public Func<IWebLingoSchedulerHost>? Scheduler { get; init; }

    /// <summary>
    /// How many instructions a request may run before it is stopped, a 503, WL3003 (section 6.12, "Limits"): the
    /// configuration's <c>"limits"</c>' <c>"requestInstructions"</c>, <see cref="DefaultInstructionBudget"/> by default.
    /// </summary>
    public long InstructionBudget { get; init; } = DefaultInstructionBudget;

    /// <summary>
    /// How many of its heap's units a request may hold, a unit two bytes of a string (section 6.12, "Limits"): the
    /// configuration's <c>"requestMemory"</c>, in megabytes, <see cref="WebLingoHeap.DefaultMaxUnits"/>, about 128 MB, by
    /// default.
    /// </summary>
    public long MaxHeapUnits { get; init; } = WebLingoHeap.DefaultMaxUnits;

    /// <summary>
    /// How long a request may take, from when its host began it, its waits included, before its host stops it, a 503,
    /// WL3003 (section 6.12, "Limits"): the configuration's <c>"requestTimeout"</c>, <see cref="DefaultRequestTimeout"/> by
    /// default; none, as under a debugger, whose stops a request's time must not count. The host keeps the time, the
    /// portable core having no timers, and stops the request through its cancellation signal's
    /// <see cref="WebLingoCancellationSignal.Exceeded"/>, with <see cref="TimeLimitMessage"/>.
    /// </summary>
    public TimeSpan? RequestTimeout { get; init; } = DefaultRequestTimeout;

    /// <summary>A request's instructions by default, 4 to 14 s of computing on a development machine (section 6.12, "Limits").</summary>
    public const long DefaultInstructionBudget = 1_000_000_000;

    /// <summary>
    /// How many instructions a request runs before it yields its worker to the other requests' ready work, then goes on
    /// (section 9, "M0.24 parts", part 2): the configuration's <c>"limits"</c>' <c>"quantum"</c>, <see cref="DefaultQuantum"/>
    /// by default, 0 for none; only where the request suspends, its host giving it a scheduler (<see cref="Scheduler"/>).
    /// </summary>
    public long Quantum { get; init; } = DefaultQuantum;

    /// <summary>A request's quantum by default (the owner's answer, 26 November 2026, A1 (a) of "M0.24: Fair Workers").</summary>
    public const long DefaultQuantum = 100_000;

    /// <summary>A request's time by default, as IIS's request timeout is (section 6.12, "Limits").</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(60);

    /// <summary>What a request stopped by its time limit is told, its host giving it to its cancellation signal.</summary>
    public string TimeLimitMessage =>
        $"The request took longer than {Seconds(RequestTimeout ?? TimeSpan.Zero)}, its site's limit, \"limits\"' \"requestTimeout\" in "
        + "wasp.config, and was stopped.";

    /// <summary>
    /// How long a request may go on running on an older generation once a reload has published a newer one, before its host
    /// stops it, as its time limit stops it, a 503, WL3003, the log saying that a reload ended it (section 6.12,
    /// "Reloading"; the owner's answer, 26 November 2026, A1 (c) of "M0.22, Part 1: The Drain"): the configuration's
    /// <c>"limits"</c>' <c>"drain"</c>, by default <see cref="DefaultDrain"/>'s for its time limit, so that by default a
    /// reload ends no request its own limit would not. A request its debugger has stopped is not stopped so while it is.
    /// </summary>
    public TimeSpan Drain { get; init; } = DefaultDrain(DefaultRequestTimeout);

    /// <summary>
    /// The drain when a site's configuration gives none: 60 seconds, or <paramref name="requestTimeout"/> where that is
    /// longer, 60 seconds where a request has no time limit.
    /// </summary>
    public static TimeSpan DefaultDrain(TimeSpan? requestTimeout) =>
        requestTimeout is { } limit && limit > TimeSpan.FromSeconds(60) ? limit : TimeSpan.FromSeconds(60);

    /// <summary>What a request the drain stopped is told, its host giving it to its cancellation signal.</summary>
    public string DrainMessage =>
        $"The site was reloaded, and the request, still running on its files as they were before, was stopped {Seconds(Drain)} after, "
        + "its site's limit, \"limits\"' \"drain\" in wasp.config.";

    private static string Seconds(TimeSpan time)
    {
        var seconds = time.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return seconds == "1" ? "1 second" : seconds + " seconds";
    }

    /// <summary>
    /// The application's log, its host's, from any worker (section 6.12, "Logging"): what a script writes with <c>std:log</c>,
    /// and what no response can show, each line for its request, <c>GET /cart #12</c>: a request's errors, its 500's and its
    /// 503's, whatever the mode, a page that fails after its output was first sent (section 6.12, "Streaming"), at
    /// <c>error</c>, and a request refused as another site's at <c>warn</c>; nowhere by default.
    /// </summary>
    public WebLingo.Platform.Abstractions.IWebLingoLog? Log { get; init; }

    /// <summary>
    /// How long a page waits for a client that has not taken its output before its request is cancelled (section 6.12,
    /// "Streaming"): the configuration's <c>"sendTimeout"</c>, 30 seconds by default.
    /// </summary>
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The headers every response of the application's has unless its page or its handler changes them (section 6.12,
    /// "Security headers"): its pages', its handlers', its error pages', which give them back after a page that failed, and
    /// its files', which its host sends; <c>wasp</c>'s from <c>wasp.config</c>'s <c>"headers"</c>; none by default.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>
    /// The extensions of <see cref="WebLingoRouteTable.Sensitive"/> whose files the application serves nonetheless, each with
    /// its dot, in lower case (section 6.12, "Sensitive files"): <c>wasp.config</c>'s <c>"serve"</c>; none by default.
    /// </summary>
    public IReadOnlyList<string> Served { get; init; } = [];

    /// <summary>
    /// The debugger the application's requests run under (specification, section 5.7, "Debugging WASP pages, parts", part
    /// 2), each a thread of its own, named for its method and its path, its pages compiled for a debugger; none by default.
    /// A request is debugged only where its host gives it a scheduler host, through which a stopped request is resumed.
    /// </summary>
    public WebLingo.VM.IWebLingoDebuggerThreads? Debugger { get; init; }
}
