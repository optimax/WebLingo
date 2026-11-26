using System.Runtime.CompilerServices;
using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Hosting.Abstractions;
using WebLingo.Language;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.Templates;
using WebLingo.VM;
namespace WebLingo.Hosting;

/// <summary>
/// What a request came to: a page's response, or a route handler's, a file the host serves as it is, no route, a handler
/// that does not answer its method, or a 500; or, not yet ended, a request that waits (specification, section 6.6).
/// </summary>
public enum WebLingoRequestOutcomeKind { Page, File, NotFound, Failed, Handler, MethodNotAllowed, Waiting, TooLarge, Forbidden }

/// <summary>
/// A request's outcome; for <see cref="WebLingoRequestOutcomeKind.File"/>, the file, from the application's root, or, where a
/// page or a handler sent it, <see cref="Sent"/>, its full path, the response left uncommitted for its host to send it with
/// the page's status and headers.
/// </summary>
public sealed record WebLingoRequestOutcome(WebLingoRequestOutcomeKind Kind, string? File = null)
{
    /// <summary>The file a page or a handler sent, or null for a site's own file.</summary>
    public WebLingoSentFile? Sent { get; init; }

    /// <summary>
    /// For a request that waits: goes on with it, on the worker it began on, giving its next outcome, once the scheduler
    /// host it was given has been posted work, or <see cref="WaitMilliseconds"/> have passed, or it is cancelled.
    /// </summary>
    public Func<WebLingoRequestOutcome>? Resume { get; init; }

    /// <summary>For a request that waits: how long, in milliseconds, until its next timer is due; -1 for none.</summary>
    public long WaitMilliseconds { get; init; } = -1;
}

/// <summary>
/// A web application (specification, sections 6.3 to 6.6 and 6.12): its root's files, as a generation, its routes made when
/// it is loaded, which a request's path finds. A page is compiled when it is first requested, once on each worker, the
/// worker's own (a generation's state shared between workers is never changed, so that nothing needs a lock, WL-P013), and
/// cached, with the components it renders, for the generation's life, all in memory (section 5.1). A request runs on the
/// worker the host gives, its output kept until it reaches 64 KB, then sent as it is written, as UTF-8
/// (<see cref="WebLingoResponseText"/>); a page that does not compile, or fails before its output is sent, is a 500, its
/// errors shown in development only, and one that fails after it ends its response unfinished, its error logged, and shown
/// at the output's end in development only. <see cref="Handle"/> never throws for a script's error.
/// A page's <c>param request;</c> and <c>param response;</c> are the request's instances (<see cref="WebLingoHttpObjects"/>).
/// A <c>.wlg</c>'s route is a handler's, a module exporting a function for each method it answers, <c>get</c>,
/// <c>post</c>, and so on, which is called with the request and the response, and awaited, as a page that imports the
/// module would call it; <c>HEAD</c> calls <c>head</c>, or <c>get</c> if it has none; any other method is a 405, whose
/// <c>Allow</c> names the ones it answers; and a <c>.wlg</c> exporting none is not a handler, its path a 404.
/// A request's session (section 6.12, "Sessions and cookies"), where the host keeps sessions
/// (<see cref="WebLingoApplicationOptions.Sessions"/>), is opened before its page or its handler runs, and its lock taken,
/// unless the page declares that it only reads it, <c>session readonly;</c>, or the handler, <c>export const session =
/// "readonly";</c>: a request whose session another holds waits, as at an <c>await</c>, its worker running other requests
/// meanwhile, for the site's lock timeout at most, then runs without it, a <c>SessionLockError</c> where it reads it; and
/// its lock is given up, its values written back where they changed, as it ends, whatever it came to.
/// </summary>
public sealed class WebLingoApplication
{
    /// <summary>The methods a handler may answer, each by the function it exports of that name, in lower case.</summary>
    private static readonly string[] Methods = ["GET", "HEAD", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"];

    private readonly WebLingoHttpObjects _http = new();
    private readonly WebLingoApplicationOptions _options;
    private readonly WebLingoWorkerServices[] _services;
    private readonly IReadOnlyList<WebLingoBuiltinDeclaration>[] _declarations;
    private volatile Generation _current;

    private WebLingoApplication(WebLingoApplicationOptions options, Generation generation)
    {
        _options = options;
        _services = new WebLingoWorkerServices[options.Workers];
        _declarations = new IReadOnlyList<WebLingoBuiltinDeclaration>[options.Workers];
        for (var i = 0; i < options.Workers; i++)
        {
            _services[i] = options.WorkerServices(new WebLingoWorkerId(i));
            var declarations = new List<WebLingoBuiltinDeclaration>();
            foreach (var builtin in _services[i].Builtins.Builtins) declarations.Add(new WebLingoBuiltinDeclaration(builtin.Name, builtin.Arity));
            _declarations[i] = declarations;
        }
        _current = generation;
    }

    /// <summary>
    /// The application at <paramref name="options"/>' root, its routes made, its files over its base's where it has one
    /// (section 6.12, "A site over a base"); null, with why, if they cannot be.
    /// </summary>
    public static WebLingoApplication? Load(WebLingoApplicationOptions options, out string? error)
    {
        if (options.Workers < 1)
        {
            error = "an application needs a worker or more";
            return null;
        }
        if (options.Base is { } over) options = options with { FileSystem = new WebLingoBaseFiles(options.FileSystem, options.Root, over) };
        var routes = WebLingoRouteTable.Make(options.FileSystem, options.Root, options.Served, out error);
        return routes is null ? null : new WebLingoApplication(options, new Generation(routes, options.Workers));
    }

    /// <summary>The routes of the generation requests are served from.</summary>
    public WebLingoRouteTable Routes => _current.Routes;

    /// <summary>The number of the generation requests are served from: 1 when the application loads, one more for each reload.</summary>
    public int GenerationNumber => _current.Number;

    /// <summary>
    /// The generation requests are served from, as an object to hold a weak reference to, and nothing else: so that a test
    /// sees it let go, with its compiled pages and modules, once a reload has replaced it and its last request has ended
    /// (section 9, "M0.22 parts", part 2).
    /// </summary>
    public object CurrentGeneration => _current;

    /// <summary>
    /// Makes a new generation from the root's files as they are now (section 6.5, WL-T007), and publishes it, at once and
    /// whole, if its routes can be made: from then on, a request is served from it, its pages compiled anew when first
    /// requested; a request already running finishes on the generation it began on, which goes when the last such one has.
    /// False, with why, and the generation served unchanged, if its routes cannot be made. Its host calls it when the files
    /// change, from any thread, one call at a time.
    /// </summary>
    public bool TryReload(out string? error)
    {
        var routes = WebLingoRouteTable.Make(_options.FileSystem, _options.Root, _options.Served, out error);
        if (routes is null) return false;
        _current = new Generation(routes, _options.Workers) { Number = _current.Number + 1 };
        return true;
    }

    /// <summary>
    /// Serves <paramref name="request"/> on <paramref name="worker"/>, from 0 to the host's workers − 1, writing and
    /// completing <paramref name="response"/>, but for a file the host serves as it is, which it leaves as it was. Given
    /// <paramref name="scheduler"/>, the request's own scheduler host, through which its host resumes it, a request whose
    /// execution waits for work off its worker, or for a timer, gives <see cref="WebLingoRequestOutcomeKind.Waiting"/>
    /// rather than hold its worker's thread (section 6.6), and its host calls its <see cref="WebLingoRequestOutcome.Resume"/>
    /// on the same worker once the scheduler host has been posted work, its wait is over, or it is cancelled; without one,
    /// the request runs to its end, its scheduler host the options' (<see cref="WebLingoApplicationOptions.Scheduler"/>).
    /// <paramref name="watch"/>, where given, is told the generation it is served from before any of its code runs, and its
    /// debugger's thread, where it has one, so that its host can end it once a reload has left it on an older generation for
    /// the site's drain (section 6.12, "Reloading").
    /// </summary>
    public WebLingoRequestOutcome Handle(IWebLingoHttpRequest request, IWebLingoHttpResponse response, WebLingoWorkerId worker,
        WebLingoCancellationSignal? cancellation = null, IWebLingoSchedulerHost? scheduler = null, IWebLingoRequestWatch? watch = null)
    {
        var generation = _current;
        watch?.Serving(generation.Number);
        var route = generation.Routes.Find(request.Path);
        if (route is { Kind: WebLingoRouteKind.File } file) return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.File, file.File);
        // The site's headers, X-Content-Type-Options and its kin, on every response it gives, before its page runs, which may
        // change each (section 6.12, "Security headers"); a file's its host gives it (Headers).
        foreach (var (name, value) in _options.Headers) response.Headers.Set(name, value);
        switch (route)
        {
            case { Kind: WebLingoRouteKind.Page or WebLingoRouteKind.Handler } when TooLarge(request):
                WebLingoErrorPages.TooLarge(response, _options.Headers);
                return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.TooLarge);
            case { Kind: WebLingoRouteKind.Page } page:
                var compiled = generation.Page(worker, page.File, this);
                return Run(compiled, request, response, worker, cancellation, scheduler, WebLingoRequestOutcomeKind.Page, compiled.ReadOnly,
                    compiled.CrossOrigin, watch);
            case { Kind: WebLingoRouteKind.Handler } handler:
                return Handler(generation, handler.File, request, response, worker, cancellation, scheduler, watch);
            default:
                WebLingoErrorPages.NotFound(response, _options.Headers);
                return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.NotFound);
        }
    }

    /// <summary>
    /// A <c>.wlg</c>'s route: its function for the request's method run as a page that imports it and calls it would run;
    /// a 405 if it has none, and a 404 if it is no handler. Its exports are read from its text, once on each worker, a text
    /// that does not parse being a handler's, so that its errors are the 500's.
    /// </summary>
    private WebLingoRequestOutcome Handler(Generation generation, string file, IWebLingoHttpRequest request, IWebLingoHttpResponse response,
        WebLingoWorkerId worker, WebLingoCancellationSignal? cancellation, IWebLingoSchedulerHost? scheduler, IWebLingoRequestWatch? watch)
    {
        var handler = generation.Exports(worker, file, this);
        if (handler?.Error is { } wrong) return Failed(response, [wrong], LogFor(request));
        var exports = handler?.Names;
        string? function = null;
        var allowed = new List<string>();
        foreach (var method in Methods)
        {
            var name = method.ToLowerInvariant();
            var answers = exports is null || exports.Contains(name) || (method == "HEAD" && exports.Contains("get"));
            if (answers) allowed.Add(method);
            if (answers && method == request.Method) function = method == "HEAD" && exports?.Contains("head") == false ? "get" : name;
        }
        if (allowed.Count == 0)
        {
            WebLingoErrorPages.NotFound(response, _options.Headers);
            return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.NotFound);
        }
        if (function is null)
        {
            WebLingoErrorPages.MethodNotAllowed(response, _options.Headers, string.Join(", ", allowed));
            return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.MethodNotAllowed);
        }
        return Run(generation.Handler(worker, file, function, this), request, response, worker, cancellation, scheduler,
            WebLingoRequestOutcomeKind.Handler, handler?.ReadOnly, handler?.CrossOrigin ?? false, watch);
    }

    /// <summary>An error's stack, each call a line of the request's log at <c>error</c>, after the error's own.</summary>
    private static void WriteStack(WebLingoRequestLog? log, IReadOnlyList<string> stack)
    {
        foreach (var call in stack) log?.Write(WebLingoLogLevel.Error, call);
    }

    /// <summary>The request's log, the application's for it, <c>GET /cart</c>, its number given when it first writes; none without one.</summary>
    private WebLingoRequestLog? LogFor(IWebLingoHttpRequest request) =>
        _options.Log is { } log ? new WebLingoRequestLog(log, WebLingoLogText.OneLine(request.Method + " " + request.Path)) : null;

    /// <summary>The application's log, its host's (section 6.12, "Logging"), where its host writes what it does too; none without one.</summary>
    public IWebLingoLog? Log => _options.Log;

    /// <summary>The folder of the site it lies over (section 6.12, "A site over a base"), which its host watches too; null for none.</summary>
    public string? Base => _options.Base;

    /// <summary>
    /// Where a file a request's outcome leaves its host is, <see cref="WebLingoRequestOutcome.File"/>, from the root: in the
    /// site's folder, or in its base's where the site has none of its own (section 6.12, "A site over a base").
    /// </summary>
    public string PathOf(string file) => _options.FileSystem.ReadablePath(_options.Root + "/" + file);

    /// <summary>The site's headers (<see cref="WebLingoApplicationOptions.Headers"/>), which its host gives a file it sends, and its 404 for one it cannot.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers => _options.Headers;

    /// <summary>The largest body a site's request may have (section 6.12, "Uploaded files"), which its hosts let through.</summary>
    public long MaxBodySize => _options.Uploads.MaxBodySize;

    /// <summary>
    /// How long a request may take, which its host keeps, the portable core having no timers, telling the request's
    /// cancellation signal <see cref="TimeLimitMessage"/> once it has passed (section 6.12, "Limits"); null for none.
    /// </summary>
    public TimeSpan? RequestTimeout => _options.RequestTimeout;

    /// <summary>What a request its time limit stopped is told, its error's message.</summary>
    public string TimeLimitMessage => _options.TimeLimitMessage;

    /// <summary>
    /// How long a request may go on running on an older generation once a reload has published a newer one, before its host
    /// stops it, as its time limit would (section 6.12, "Reloading"): <see cref="WebLingoApplicationOptions.Drain"/>.
    /// </summary>
    public TimeSpan Drain => _options.Drain;

    /// <summary>What a request the drain stopped is told, its error's message, which says that a reload ended it.</summary>
    public string DrainMessage => _options.DrainMessage;

    /// <summary>Whether a request says its body is larger than the site takes, by its <c>Content-Length</c>, before it is read.</summary>
    private bool TooLarge(IWebLingoHttpRequest request) =>
        long.TryParse(request.Headers.Get("Content-Length"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
            out var length) && length > _options.Uploads.MaxBodySize;

    /// <summary>
    /// What a <c>.wlg</c> exports, read from its text, or null if it does not parse: the names, and where it declares that
    /// it only reads its session, <c>export const session = "readonly";</c>, or its error, a <c>session</c> export that is not
    /// that (section 6.12, "Sessions and cookies"); and whether it takes requests other sites send,
    /// <c>export const crossorigin = "allowed";</c>, a <c>crossorigin</c> export that is not that its error (section 6.12,
    /// "Cross-site request forgery").
    /// </summary>
    private HandlerExports? ExportsOf(string file)
    {
        var text = new WebLingoSiteFiles(_options.FileSystem, _options.Root, ".wlg").Read(file) ?? "";
        var tree = new WebLingoParser(text, file).ParseCompilationUnit();
        foreach (var diagnostic in tree.Diagnostics)
        {
            if (diagnostic.Severity == WebLingoDiagnosticSeverity.Error) return null;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        string? readOnly = null, error = null;
        var crossOrigin = false;
        var source = new WebLingoSourceText(text, file);
        foreach (var statement in tree.Root.Statements)
        {
            if (statement is not WebLingoExportSyntax export) continue;
            var name = export.Declaration switch
            {
                WebLingoFunctionDeclarationSyntax declared => declared.Name,
                WebLingoClassDeclarationSyntax declared => declared.Name,
                WebLingoVariableDeclarationSyntax declared => declared.Name,
                _ => null
            };
            if (name is not null) names.Add(name);
            if (name is not (SessionExport or CrossOriginExport)) continue;
            var at = source.GetLocation(export.Span.Start);
            var where = $"{file}({at.Line},{at.Column})";
            if (name == CrossOriginExport)
            {
                if (export.Declaration is WebLingoVariableDeclarationSyntax { Kind: WebLingoDeclarationKind.Const, Initializer: WebLingoStringLiteralSyntax { Value: "allowed" } })
                {
                    crossOrigin = true;
                }
                else
                {
                    error = $"{where}: error {WebLingoDiagnosticCodes.MisplacedCrossOrigin}: A handler's 'crossorigin' export declares that it "
                            + "takes requests other sites send, 'export const crossorigin = \"allowed\";', and is nothing else.";
                }
                continue;
            }
            if (export.Declaration is WebLingoVariableDeclarationSyntax { Kind: WebLingoDeclarationKind.Const, Initializer: WebLingoStringLiteralSyntax { Value: "readonly" } })
            {
                readOnly = $"'export const session = \"readonly\";' at {where}";
            }
            else
            {
                error = $"{where}: error {WebLingoDiagnosticCodes.MisplacedSession}: A handler's 'session' export declares that it only reads "
                        + "its session, 'export const session = \"readonly\";', and is nothing else.";
            }
        }
        return new HandlerExports(names, readOnly, error, crossOrigin);
    }

    /// <summary>The export that declares how a handler uses its session.</summary>
    private const string SessionExport = "session";

    /// <summary>The export that declares that a handler takes requests other sites send.</summary>
    private const string CrossOriginExport = "crossorigin";

    /// <summary>A handler's exports, where it declares that it only reads its session, and what is wrong with its declaration.</summary>
    private sealed record HandlerExports(HashSet<string> Names, string? ReadOnly, string? Error, bool CrossOrigin);

    /// <summary>
    /// A page run, or a handler's, its session opened first, waiting for its lock where another request holds it:
    /// <paramref name="done"/> once it has run, unless it fails, or waits. <paramref name="readOnly"/> says where the page or
    /// the handler declares that it only reads its session, null where it does not.
    /// </summary>
    private WebLingoRequestOutcome Run(CompiledPage page, IWebLingoHttpRequest request, IWebLingoHttpResponse response, WebLingoWorkerId worker,
        WebLingoCancellationSignal? cancellation, IWebLingoSchedulerHost? scheduler, WebLingoRequestOutcomeKind done, string? readOnly = null,
        bool crossOrigin = false, IWebLingoRequestWatch? watch = null)
    {
        var log = LogFor(request);
        if (page.Module is null) return Failed(response, page.Errors, log);
        if (!crossOrigin && CrossSite(request) is { } why)
        {
            log?.Write(WebLingoLogLevel.Warn, WebLingoLogText.OneLine(why));
            WebLingoErrorPages.Forbidden(response, _options.Headers, _options.Mode, [why]);
            return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.Forbidden);
        }
        // A client that takes nothing for too long cancels the request, as a client that goes does.
        cancellation ??= new WebLingoCancellationSignal();
        var host = scheduler ?? _options.Scheduler?.Invoke();
        var session = _options.Sessions is { } store ? new WebLingoSession(store, request, response, _options.Crypto, readOnly) : null;
        var start = () => Start(page, request, response, worker, cancellation, scheduler, host, done, session, log, watch);
        if (session?.Id is not { } id) return start();
        WebLingoSessionOpening? arrived = null;
        var opening = session.Store.Open(id, readOnly is null, later => host?.Post(() => arrived = later));
        if (opening.Kind != WebLingoSessionOpened.Waiting)
        {
            session.Opened(opening);
            return start();
        }
        if (scheduler is null)
        {
            // A request run to its end waits on its own thread; one with nothing to wait on goes on without its session.
            while (arrived is null && host is not null) host.RunPosted(-1);
            session.Opened(arrived ?? WebLingoSessionOpening.TimedOut);
            return start();
        }
        return WaitForSession(scheduler, () => arrived, session, start);
    }

    /// <summary>
    /// Why a request that may change things, any method but GET, HEAD and OPTIONS, is another site's, refused as a cross-site
    /// request forgery may be, as Go's <c>CrossOriginProtection</c> refuses one (section 6.12, "Cross-site request forgery";
    /// the owner's answer, 24 November 2026, A1 (b)): by its <c>Sec-Fetch-Site</c> where it has one, anything but
    /// <c>same-origin</c> or <c>none</c>; else by its <c>Origin</c>, where it has one, naming a host other than the request's;
    /// null where it is the site's own, or says neither, as no browser that sends a cookie leaves out both.
    /// </summary>
    private static string? CrossSite(IWebLingoHttpRequest request)
    {
        if (request.Method is "GET" or "HEAD" or "OPTIONS") return null;
        var refused = $"A {request.Method} from another site was refused, as a cross-site request forgery may be: ";
        var allow = "; 'crossorigin allowed;' lets a page take one, and 'export const crossorigin = \"allowed\";' a handler.";
        if (request.Headers.Get("Sec-Fetch-Site") is { } fetch)
        {
            return fetch is "same-origin" or "none" ? null : $"{refused}its Sec-Fetch-Site is '{fetch}'{allow}";
        }
        if (request.Headers.Get("Origin") is not { } origin) return null;
        var scheme = origin.IndexOf("://", StringComparison.Ordinal);
        var host = scheme < 0 ? null : origin[(scheme + 3)..];
        if (host is not null && host.Length > 0 && host.Equals(request.Host, StringComparison.OrdinalIgnoreCase)) return null;
        return $"{refused}its Origin, '{origin}', is not this site's, '{request.Host}'{allow}";
    }

    /// <summary>
    /// A request that waits for its session's lock, resumed once the store has posted what the wait came to, then run; one
    /// whose client has gone waits on for its turn, so that the lock it may be given is given up at once.
    /// </summary>
    private static WebLingoRequestOutcome WaitForSession(IWebLingoSchedulerHost scheduler, Func<WebLingoSessionOpening?> arrived, WebLingoSession session,
        Func<WebLingoRequestOutcome> start) =>
        new(WebLingoRequestOutcomeKind.Waiting)
        {
            Resume = () =>
            {
                scheduler.RunPosted(0);
                if (arrived() is not { } opening) return WaitForSession(scheduler, arrived, session, start);
                session.Opened(opening);
                return start();
            }
        };

    /// <summary>A page run, or a handler's, once its session is open: <paramref name="done"/> once it has run, unless it fails, or waits.</summary>
    private WebLingoRequestOutcome Start(CompiledPage page, IWebLingoHttpRequest request, IWebLingoHttpResponse response, WebLingoWorkerId worker,
        WebLingoCancellationSignal cancellation, IWebLingoSchedulerHost? scheduler, IWebLingoSchedulerHost? host, WebLingoRequestOutcomeKind done,
        WebLingoSession? session, WebLingoRequestLog? log, IWebLingoRequestWatch? watch)
    {
        // Under a debugger, the request a thread of its, which its stops resume through the scheduler host (section 5.7);
        // its host told of it, whose drain does not end the request while it is stopped (section 6.12, "Reloading").
        var thread = scheduler is null ? null : _options.Debugger?.Begin(request.Method + " " + request.Path, page.Locate, scheduler);
        if (thread is not null) watch?.Debugged(thread);
        var output = new WebLingoResponseText(response, _options.SendTimeout, cancellation.Cancel, thread);
        var made = _http.For(request, response, output, _options.Uploads, session, out var ending);
        var context = new WebLingoExecutionContext(worker)
        {
            Services = new RequestServices([.. WebLingoTemplateBuiltins.Common(output), .. made], _http.Members,
                _services[worker.Value].Builtins, output),
            Output = output,
            FileSystem = _options.ScriptFileSystem ?? _options.FileSystem,
            Network = _options.Network,
            Allowed = _options.Allowed,
            Crypto = _options.Crypto,
            Environment = _options.Environment,
            Databases = _options.Databases,
            Mail = _options.Mail,
            Clock = _options.Clock,
            WallClock = _options.WallClock,
            TimeZone = _options.TimeZone,
            SchedulerHost = host,
            Suspends = scheduler is not null,
            InstructionBudget = _options.InstructionBudget,
            Resources = WebLingoResourceBudget.Default with { MaxHeapUnits = _options.MaxHeapUnits },
            Cancellation = cancellation,
            Lines = page.LineOf,
            Log = log
        };
        var vm = new WebLingoVirtualMachine { Loader = page.Modules, Components = page.Components, Debugger = thread };
        return Ended(vm.Execute(page.Module!, context), vm.Resume, page, output, response, done, ending, thread, session, scheduler, host,
            cancellation, log);
    }

    /// <summary>What a run came to: <paramref name="done"/>, a failure, or a wait, which its host resumes.</summary>
    private WebLingoRequestOutcome Ended(WebLingoExecutionResult result, Func<WebLingoExecutionResult> resume, CompiledPage page,
        WebLingoResponseText output, IWebLingoHttpResponse response, WebLingoRequestOutcomeKind done, IDisposable ending,
        IWebLingoDebuggerThread? thread, WebLingoSession? session, IWebLingoSchedulerHost? scheduler, IWebLingoSchedulerHost? host,
        WebLingoCancellationSignal cancellation, WebLingoRequestLog? log)
    {
        if (result.Waiting)
        {
            return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.Waiting)
            {
                Resume = () => Ended(resume(), resume, page, output, response, done, ending, thread, session, scheduler, host, cancellation, log),
                WaitMilliseconds = result.WaitMilliseconds
            };
        }
        // A request its host stopped for a limit, its time, failed as the VM's own limits fail it, WL3003, not as cancelled.
        if (result is { Succeeded: false, Error: { Code: WebLingoDiagnosticCodes.Cancelled } cancelled } && cancellation.Limit is { } limit)
        {
            result = result with { Error = cancelled with { Code = WebLingoDiagnosticCodes.BudgetExceeded, Message = limit } };
        }
        thread?.Dispose(); // Its debugger's thread ended with it.
        ending.Dispose(); // Its uploads' temporary files removed (section 6.12, "Uploaded files").
        // Its session's lock given up, its values written back where they changed, before its response ends, so that the
        // request its client sends next finds them (section 6.12, "Sessions and cookies"): at once, or, where the store
        // writes them off the worker, once it has, the request waiting meanwhile.
        var closed = false;
        if (session is null || session.End(() => host?.Post(() => closed = true))) return Finished(result, page, output, response, done, log);
        if (scheduler is null)
        {
            while (!closed && host is not null) host.RunPosted(-1);
            return Finished(result, page, output, response, done, log);
        }
        return WaitForClose(scheduler, () => closed, () => Finished(result, page, output, response, done, log));
    }

    /// <summary>A request that waits for its session to be written, resumed once the store has posted that it is, then finished.</summary>
    private static WebLingoRequestOutcome WaitForClose(IWebLingoSchedulerHost scheduler, Func<bool> closed, Func<WebLingoRequestOutcome> finish) =>
        new(WebLingoRequestOutcomeKind.Waiting)
        {
            Resume = () =>
            {
                scheduler.RunPosted(0);
                return closed() ? finish() : WaitForClose(scheduler, closed, finish);
            }
        };

    /// <summary>
    /// A run that has ended, its session closed: <paramref name="done"/>, or a failure, a 503 where the request passed one of
    /// its limits (section 6.12, "Limits"), WL3003, a 500 otherwise.
    /// </summary>
    private WebLingoRequestOutcome Finished(WebLingoExecutionResult result, CompiledPage page, WebLingoResponseText output,
        IWebLingoHttpResponse response, WebLingoRequestOutcomeKind done, WebLingoRequestLog? log)
    {
        if (!result.Succeeded)
        {
            var error = page.Describe(result.Error!);
            // The calls it was raised in, each a line of the log after its own (section 6.12, "Logging, parts", part 2).
            var stack = page.DescribeStack(result.Error!);
            if (output.Sending) return FailedWhileSending(output, response, error, log, stack);
            if (result.Error!.Code != WebLingoDiagnosticCodes.BudgetExceeded) return Failed(response, [error], log, stack);
            log?.Write(WebLingoLogLevel.Error, WebLingoLogText.OneLine("A request passed one of its limits: " + error));
            WriteStack(log, stack);
            WebLingoErrorPages.Limited(response, _options.Headers, _options.Mode, [error]);
            return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.Failed);
        }
        // A file it sent: its response left uncommitted, for its host to send the file with its status and headers.
        if (output.Sent is { } file) return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.File, file.Path) { Sent = file };
        output.Send();
        response.Complete();
        return new WebLingoRequestOutcome(done);
    }

    /// <summary>
    /// A page that fails once its output is being sent (section 6.12, "Streaming"): its response ended there, unfinished,
    /// its error in the host's log and, in development, at the end of what was sent; or, cancelled because its client took
    /// nothing for longer than the send timeout, its response ended, the timeout in the log.
    /// </summary>
    private WebLingoRequestOutcome FailedWhileSending(WebLingoResponseText output, IWebLingoHttpResponse response, string error,
        WebLingoRequestLog? log, IReadOnlyList<string> stack)
    {
        if (output.TimedOut)
        {
            // Its client has taken nothing for too long: nothing more is sent.
            var seconds = _options.SendTimeout.TotalSeconds;
            log?.Write(WebLingoLogLevel.Error, WebLingoLogText.OneLine(
                $"A page's client took nothing of its output for {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} "
                + (seconds == 1 ? "second" : "seconds") + ", and its request was cancelled: " + error));
            WriteStack(log, stack);
            response.Abort();
            return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.Failed);
        }
        log?.Write(WebLingoLogLevel.Error, WebLingoLogText.OneLine("A page failed after its response was sent: " + error));
        WriteStack(log, stack);
        if (_options.Mode == WebLingoMode.Development) output.Write("\n<pre>" + WebLingoTemplateWriteFunction.Escape(error) + "\n</pre>\n");
        output.Send(); // What it wrote before it failed.
        response.Abort();
        return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.Failed);
    }

    private WebLingoRequestOutcome Failed(IWebLingoHttpResponse response, IReadOnlyList<string> errors, WebLingoRequestLog? log,
        IReadOnlyList<string>? stack = null)
    {
        // Its errors in the log, whatever the mode, a production page showing none (section 6.12, "Logging").
        foreach (var error in errors) log?.Write(WebLingoLogLevel.Error, WebLingoLogText.OneLine(error));
        if (stack is not null) WriteStack(log, stack);
        WebLingoErrorPages.Failed(response, _options.Headers, _options.Mode, errors);
        return new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.Failed);
    }

    /// <summary>
    /// A page compiled on a worker, with its files found from the application's root, or its errors, each as a line: the
    /// file's, or, for a handler's <paramref name="function"/>, a page that imports the file and calls it, named
    /// <c>file#function</c>, whose errors are the file's, but where it calls a function that it does not have.
    /// </summary>
    private CompiledPage Compile(string file, WebLingoWorkerId worker, string? function = null)
    {
        var templates = new WebLingoSiteFiles(_options.FileSystem, _options.Root, ".wasp");
        var modules = new WebLingoSiteFiles(_options.FileSystem, _options.Root, ".wlg");
        var options = new WebLingoCompilerOptions(_declarations[worker.Value])
        {
            Resolver = modules,
            Native = _services[worker.Value].Native,
            HostParameters = _http.Parameters,
            Debugging = _options.Debugger is not null // Its points and its variables, for the debugger (section 5.7).
        };
        var compiler = new WebLingoTemplateCompiler(options, templates);
        var read = function is null ? templates.Read(file) : null;
        var text = function is null
            ? new WebLingoSourceText(read ?? "", file)
            : new WebLingoSourceText("<% param request; param response; import handler from \"/" + Escaped(file) + "\"; await handler."
                                     + function + "(request, response); %>", file + "#" + function);
        var page = new CompiledPage(text, compiler, modules, new WebLingoModuleLoader(options)) { CallsHandler = function is not null };
        if (function is null && read is null)
        {
            // Its route found, its file not read just now, as one being replaced may not be: a 500, not an empty page, and
            // not kept, so that its next request reads it again (24 November 2026, "A Page Replaced While WASP Reads It").
            page.Errors.Add($"{file}(1,1): error {WebLingoDiagnosticCodes.TemplateNotFound}: The page cannot be read just now; its next "
                            + "request reads it again.");
            page.Unread = true;
            return page;
        }
        var result = compiler.Compile(text);
        page.Module = result.Module;
        page.CrossOrigin = compiler.CrossOriginAllowed is not null;
        if (compiler.ReadOnlySession is { } declared)
        {
            var at = text.GetLocation(declared.Start);
            page.ReadOnly = $"'session readonly;' at {text.Name}({at.Line},{at.Column})";
        }
        foreach (var diagnostic in result.Diagnostics)
        {
            if (diagnostic.Severity == WebLingoDiagnosticSeverity.Error) page.Errors.Add(page.Describe(diagnostic));
        }
        return page;
    }

    /// <summary>A file's name as a string literal's text in a page's block: <c>"</c> escaped, and <c>&gt;</c>, so that <c>%&gt;</c> cannot end it.</summary>
    private static string Escaped(string file) => file.Replace("\"", "\\\"", StringComparison.Ordinal).Replace(">", "\\u{3E}", StringComparison.Ordinal);

    /// <summary>
    /// A generation (section 6.5): its routes, and, for each worker, the pages and handlers compiled on it, and the
    /// handlers' exports, which only that worker reads and adds to.
    /// </summary>
    private sealed class Generation(WebLingoRouteTable routes, int workers)
    {
        private readonly Dictionary<string, CompiledPage>[] _pages = MakePages(workers);
        private readonly Dictionary<string, HandlerExports?>[] _exports = MakeExports(workers);

        /// <summary>A handler's exports on a worker, read once; null for one that does not parse.</summary>
        public HandlerExports? Exports(WebLingoWorkerId worker, string file, WebLingoApplication application)
        {
            var exports = _exports[worker.Value];
            if (!exports.TryGetValue(file, out var names))
            {
                names = application.ExportsOf(file);
                exports.Add(file, names);
            }
            return names;
        }

        /// <summary>A handler's function as a page that calls it, compiled once on each worker, kept by a name no file has.</summary>
        public CompiledPage Handler(WebLingoWorkerId worker, string file, string function, WebLingoApplication application)
        {
            var pages = _pages[worker.Value];
            var key = file + "#" + function;
            if (!pages.TryGetValue(key, out var page))
            {
                page = application.Compile(file, worker, function);
                pages.Add(key, page);
            }
            return page;
        }

        private static Dictionary<string, HandlerExports?>[] MakeExports(int workers)
        {
            var exports = new Dictionary<string, HandlerExports?>[workers];
            for (var i = 0; i < workers; i++) exports[i] = new Dictionary<string, HandlerExports?>(StringComparer.Ordinal);
            return exports;
        }

        public WebLingoRouteTable Routes { get; } = routes;

        /// <summary>Its number: 1 for the application's first, one more for each reload's.</summary>
        public int Number { get; init; } = 1;

        public CompiledPage Page(WebLingoWorkerId worker, string file, WebLingoApplication application)
        {
            var pages = _pages[worker.Value];
            if (!pages.TryGetValue(file, out var page))
            {
                page = application.Compile(file, worker);
                if (!page.Unread) pages.Add(file, page);
            }
            return page;
        }

        private static Dictionary<string, CompiledPage>[] MakePages(int workers)
        {
            var pages = new Dictionary<string, CompiledPage>[workers];
            for (var i = 0; i < workers; i++) pages[i] = new Dictionary<string, CompiledPage>(StringComparer.Ordinal);
            return pages;
        }
    }

    /// <summary>A page as its worker keeps it: its module, or its errors, and how to name where an error is.</summary>
    private sealed class CompiledPage(WebLingoSourceText text, WebLingoTemplateCompiler compiler, WebLingoSiteFiles modules,
        WebLingoModuleLoader imports)
    {
        public WebLingoCompiledModule? Module { get; set; }
        public List<string> Errors { get; } = [];

        /// <summary>Where the page declares that it only reads its session, as an error names it; null where it does not.</summary>
        public string? ReadOnly { get; set; }

        /// <summary>Whether its file could not be read when it was compiled, so that its worker does not keep it.</summary>
        public bool Unread { get; set; }

        /// <summary>Whether it is a page the host made to call a handler, <c>file#function</c>, whose text is none of the site's.</summary>
        public bool CallsHandler { get; init; }

        /// <summary>Whether it declares that it takes requests other sites send, <c>crossorigin allowed;</c>.</summary>
        public bool CrossOrigin { get; set; }

        /// <summary>
        /// What finds and compiles a module the page imports while it runs (section 3.9), each compiled once, when first
        /// imported, and kept for the generation, as a component is; joined to each request's program anew.
        /// </summary>
        public CachedLoader Modules { get; } = new CachedLoader(imports);

        /// <summary>The components the page renders (section 13), each compiled once, when first rendered.</summary>
        public CachedLoader Components { get; } = new CachedLoader(compiler.Components);

        /// <summary>An error as a line, <c>file(line,column): error CODE: message</c>, in the file it is in.</summary>
        public string Describe(WebLingoDiagnostic diagnostic)
        {
            var located = compiler.Sources.Locate(diagnostic);
            var file = FileOf(located);
            var location = file.GetLocation(located.Span.Start);
            return $"{file.Name}({location.Line},{location.Column}): error {located.Code}: {located.Message}";
        }

        /// <summary>
        /// The calls an error nothing caught was raised in, the innermost first, each located as the error is, in the file it
        /// is in, an include's, a layout's or a component's: <c>  at total in cart.wlg(12,5)</c>, <c>  at a function in …</c>
        /// for one without a name, <c>  at the top level of page.wasp(3,1)</c> for a file's own (section 6.12, "Logging,
        /// parts", part 2); none where the VM kept none.
        /// </summary>
        public List<string> DescribeStack(WebLingoDiagnostic diagnostic)
        {
            var lines = new List<string>();
            foreach (var call in diagnostic.Stack ?? [])
            {
                var located = compiler.Sources.Locate(new WebLingoDiagnostic(diagnostic.Code, WebLingoDiagnosticSeverity.Error, "", call.Span)
                {
                    Source = call.Source
                });
                var file = FileOf(located);
                if (CallsHandler && ReferenceEquals(file, text)) continue; // The host's call of the handler, no file of the site's.
                var location = file.GetLocation(located.Span.Start);
                var where = $"{file.Name}({location.Line},{location.Column})";
                lines.Add(call.Function switch
                {
                    null => "  at the top level of " + where,
                    "function" => "  at a function in " + where,
                    var name => $"  at {name} in {where}"
                });
            }
            return lines;
        }

        /// <summary>A position's line, in the file it is in, located as an error is, for a message that names it (section 3.8).</summary>
        public int LineOf(string? source, int offset)
        {
            var (file, at) = Locate(source, offset);
            return file.GetLocation(at).Line;
        }

        /// <summary>A position, its source's name and its offset there, as the file it is in and its offset there, located as an error is.</summary>
        public (WebLingoSourceText Text, int Offset) Locate(string? source, int offset)
        {
            var located = compiler.Sources.Locate(new WebLingoDiagnostic("", WebLingoDiagnosticSeverity.Error, "", new WebLingoSourceSpan(offset, 0))
            {
                Source = source
            });
            return (FileOf(located), located.Span.Start);
        }

        private WebLingoSourceText FileOf(WebLingoDiagnostic located) => located.Source is not { } name ? text
            : compiler.Sources.TextOf(name) ?? (modules.Texts.TryGetValue(name, out var module) ? module : text);
    }

    /// <summary>
    /// A loader that compiles each module once, its result kept (WL-T007: a component is not compiled for every render, nor
    /// a module a page imports while it runs, for every request).
    /// </summary>
    private sealed class CachedLoader(WebLingoModuleLoader loader) : IWebLingoModuleLoader
    {
        private Dictionary<string, (WebLingoCompiledModule? Module, WebLingoDiagnostic? Error)>? _compiled;

        public bool TryResolve(string importer, string path, out string name, out string? error) =>
            loader.TryResolve(importer, path, out name, out error);

        public WebLingoCompiledModule? Compile(string name, out WebLingoDiagnostic? error) =>
            CompileOnce(_compiled ??= new(StringComparer.Ordinal), ((IWebLingoModuleLoader)loader).Compile, name, out error);

        private delegate T? CompileModule<T>(string name, out WebLingoDiagnostic? error) where T : class;

        private static T? CompileOnce<T>(Dictionary<string, (T? Module, WebLingoDiagnostic? Error)> entries,
            CompileModule<T> compile, string name, out WebLingoDiagnostic? error) where T : class
        {
            if (!entries.TryGetValue(name, out var compiled))
            {
                compiled.Module = compile(name, out compiled.Error);
                entries.Add(name, compiled);
            }
            error = compiled.Error;
            return compiled.Module;
        }
    }

    /// <summary>
    /// A request's built-ins: the page's writers, writing to its output, those of <c>&lt;%= … %&gt;</c> that escape outside
    /// HTML's text made when the page names them (section 13, "Context-aware escaping"), and the makers of its request's and
    /// its response's instances; then the members of their classes; then the host's.
    /// </summary>
    private sealed class RequestServices(IWebLingoBuiltinFunction[] request, WebLingoBuiltinRegistry members, WebLingoBuiltinRegistry host,
        IWebLingoTextOutput output)
        : IWebLingoRuntimeServices
    {
        public IReadOnlyList<IWebLingoBuiltinFunction> Builtins { get; } = [.. request, .. members.Builtins, .. host.Builtins];

        public bool TryGetBuiltin(string name, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out IWebLingoBuiltinFunction builtin)
        {
            foreach (var own in request)
            {
                if (own.Name == name)
                {
                    builtin = own;
                    return true;
                }
            }
            if (WebLingoTemplateBuiltins.TryCreateWriter(name, output, out builtin)) return true;
            return members.TryGetBuiltin(name, out builtin) || host.TryGetBuiltin(name, out builtin);
        }
    }
}
