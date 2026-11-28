using System.Text;
using System.Text.Json;
using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.VM;
namespace WebLingo.Cli;

/// <summary>
/// Serves a site for a debugger (specification, section 5.7, "Debugging WASP pages, parts", part 2): the site at
/// <paramref name="root"/>, on <paramref name="port"/> of the local machine, 0 for one the system chooses, its server's own
/// where it is null, its requests each a thread of <paramref name="debugger"/>'s, until <paramref name="stop"/> is
/// signalled; where it serves, and what it reports, written to <paramref name="output"/>, and why it cannot serve to
/// <paramref name="error"/>. Its exit code.
/// </summary>
public delegate int WebLingoSiteServer(string root, int? port, IWebLingoDebuggerThreads debugger, TextWriter output, TextWriter error,
    CancellationToken stop);

/// <summary>
/// A site that runs in this process, served by its host, to which a debugger attaches (specification, section 5.7,
/// "Debugging a site where it runs, parts", part 1): its root, which the debugger names its files from, and how a debugger is
/// attached to it, each of its requests from then on a thread of the debugger's, until what it gives is disposed, which
/// detaches it.
/// </summary>
public sealed record WebLingoRunningSite(string Root, Func<IWebLingoDebuggerThreads, IDisposable> Attach);

/// <summary>
/// One local launch (specification, section 5.7): of a script, <c>weblingo</c>'s, or of a page, <c>wasp</c>'s, as
/// <paramref name="programFor"/> gives the program for a root, one thread, its stops waiting on its own thread for the
/// debugger; or, where <paramref name="serve"/> is given, of a site, served by it, each request a thread of its own, named
/// <c>GET /cart #12</c>, its stops waiting as at an <c>await</c>, its worker serving the other requests meanwhile. The request
/// loop and the programs run on separate threads. A position of a program is located in its file by its thread's locator,
/// a page's include's in the include. Given <paramref name="running"/>, it attaches to that site, which runs already, rather
/// than launch one, and detaches when it ends, the site going on ("Debugging a site where it runs, parts", part 1).
/// </summary>
internal sealed class WebLingoDebugSession(Stream input, Stream output, Func<string?, WebLingoCommandLineProgram> programFor,
    WebLingoSiteServer? serve = null, WebLingoRunningSite? running = null) : IWebLingoDebuggerThreads
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly WebLingoDebugProtocol _protocol = new(input, output);
    private readonly object _gate = new();
    private readonly Dictionary<string, SourceFile> _files = new(PathComparer);
    private readonly HashSet<string> _missing = new(PathComparer);
    private readonly Dictionary<string, List<Breakpoint>> _breakpoints = new(PathComparer);
    private readonly SortedDictionary<int, DebugThread> _threads = [];
    private readonly Dictionary<int, IReadOnlyList<WebLingoDebugVariable>> _variables = [];
    private readonly CancellationTokenSource _stopServing = new();
    private LaunchSettings? _launch;
    private string _base = "";
    private bool _configured, _finished, _disconnected, _pause, _entry;
    private bool _oneBasedLines = true, _oneBasedColumns = true;
    private int _nextBreakpoint, _nextVariable, _nextFrame, _nextThread;

    public WebLingoCancellationSignal Cancellation { get; } = new();
    public DotNetSchedulerHost Scheduler { get; } = new();

    /// <summary>The thread of the program a launch runs, a script or a page, from its configuration on; none for a site.</summary>
    public DebugThread? Program { get; private set; }

    public int Run()
    {
        try
        {
            while (_protocol.Read() is { } request)
            {
                if (Handle(request)) return 0;
            }
            return 0;
        }
        catch (EndOfStreamException)
        {
            return 0;
        }
        catch (Exception failure) when (failure is IOException or JsonException or InvalidDataException)
        {
            return 1;
        }
        finally
        {
            lock (_gate)
            {
                _disconnected = true;
                Cancel();
                if (_configured && !_finished) Monitor.Wait(_gate, _launch?.Site is null ? 1000 : 5000);
            }
        }
    }

    private bool Handle(JsonElement request)
    {
        if (Text(request, "type") != "request" || Text(request, "command") is not { } command) return false;
        var id = Number(request, "seq", 0);
        var arguments = Property(request, "arguments");
        lock (_gate)
        {
            try
            {
                switch (command)
                {
                    case "initialize":
                        _oneBasedLines = Flag(arguments, "linesStartAt1", true);
                        _oneBasedColumns = Flag(arguments, "columnsStartAt1", true);
                        if (Text(arguments, "pathFormat") is { } format && format != "path")
                            throw new ArgumentException("The debugger uses local file paths.");
                        _protocol.Respond(id, command, new
                        {
                            supportsConfigurationDoneRequest = true, supportsTerminateRequest = true,
                            supportsSingleThreadExecutionRequests = true, supportsConditionalBreakpoints = true
                        });
                        break;
                    case "launch":
                        if (_launch is not null) throw new ArgumentException("The session has already launched a program.");
                        _launch = LaunchSettings.Read(arguments, programFor, serve is not null);
                        // A program's files are named from its working folder, a site's from its root.
                        _base = _launch.Site ?? _launch.Directory;
                        _entry = _launch.StopOnEntry;
                        _protocol.Respond(id, command);
                        _protocol.Event("initialized");
                        break;
                    case "attach":
                        // A site that runs already, its requests debugged from configurationDone on (section 5.7, "Debugging
                        // a site where it runs, parts", part 1).
                        if (_launch is not null) throw new ArgumentException("The session has already launched or attached to a program.");
                        if (running is null) throw new ArgumentException("This debug adapter attaches to no running site.");
                        _launch = new LaunchSettings(programFor(running.Root), null, running.Root, [], false, Flag(arguments, "noDebug"))
                        {
                            Site = running.Root, Attached = true
                        };
                        _base = running.Root;
                        _protocol.Respond(id, command);
                        _protocol.Event("initialized");
                        break;
                    case "setBreakpoints":
                        _protocol.Respond(id, command, new { breakpoints = SetBreakpoints(arguments) });
                        break;
                    case "setExceptionBreakpoints":
                        _protocol.Respond(id, command, new { breakpoints = Array.Empty<object>() });
                        break;
                    case "configurationDone":
                        if (_launch is not { } launch || _configured) throw new ArgumentException("There is no pending launch.");
                        _configured = true;
                        if (launch.Site is null)
                        {
                            Program = new DebugThread(this, ++_nextThread, "WebLingo", null);
                            _threads.Add(Program.Id, Program);
                        }
                        _protocol.Respond(id, command);
                        _ = Task.Run(() => Execute(launch));
                        break;
                    case "threads":
                        _protocol.Respond(id, command, new
                        {
                            threads = _configured && !_finished ? _threads.Values.Select(thread => new { id = thread.Id, name = thread.Name }).ToArray() : []
                        });
                        break;
                    case "stackTrace":
                        _protocol.Respond(id, command, StackTrace(arguments));
                        break;
                    case "scopes":
                        _protocol.Respond(id, command, Scopes(arguments));
                        break;
                    case "variables":
                        _protocol.Respond(id, command, new { variables = Variables(arguments) });
                        break;
                    case "pause":
                        if (!_configured || _finished) throw new ArgumentException("The script is not running.");
                        // A site's every request pauses, those that come after too, until the debugger goes on (section 5.7).
                        if (_launch!.Site is not null || Program is not { IsStopped: true }) _pause = true;
                        _protocol.Respond(id, command);
                        break;
                    case "continue":
                    case "next":
                    case "stepIn":
                    case "stepOut":
                        if (ThreadOf(arguments) is not { IsStopped: true } thread) throw new ArgumentException("The script is not paused.");
                        _pause = false;
                        thread.Go(command switch { "next" => Step.Over, "stepIn" => Step.In, "stepOut" => Step.Out, _ => Step.None });
                        if (command == "continue" && !Flag(arguments, "singleThread"))
                        {
                            // A pause's snapshot over: every request it stopped goes on, those a breakpoint stopped waiting still.
                            foreach (var other in _threads.Values)
                            {
                                if (other.IsStopped && other.Reason == "pause") other.Go(Step.None);
                            }
                        }
                        _protocol.Respond(id, command,
                            command == "continue" ? new { allThreadsContinued = !_threads.Values.Any(other => other.IsStopped) } : null);
                        break;
                    case "terminate":
                        _protocol.Respond(id, command);
                        Cancel();
                        if (!_configured) _protocol.Event("terminated");
                        break;
                    case "disconnect":
                        _protocol.Respond(id, command);
                        return true;
                    default:
                        _protocol.Respond(id, command, error: $"The debugger does not support '{command}'.");
                        break;
                }
            }
            catch (Exception failure) when (failure is ArgumentException or NotSupportedException or IOException)
            {
                // A closed transport will also reject the response and exits Run.
                _protocol.Respond(id, command, error: failure.Message);
            }
        }
        return false;
    }

    /// <summary>The thread a request names by its <c>threadId</c>, or the program's, where it names none.</summary>
    private DebugThread? ThreadOf(JsonElement arguments) =>
        Property(arguments, "threadId").ValueKind == JsonValueKind.Number
            ? _threads.GetValueOrDefault(Number(arguments, "threadId", 0))
            : Program;

    private void Execute(LaunchSettings launch)
    {
        if (launch.Attached)
        {
            Attach();
            return;
        }
        var exit = 1;
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(launch.Directory);
            // A site's workers write at once: each line whole.
            using var stdout = TextWriter.Synchronized(new DebugWriter(_protocol, "stdout"));
            using var stderr = TextWriter.Synchronized(new DebugWriter(_protocol, "stderr"));
            exit = launch.Site is { } site
                ? serve!(site, launch.Port, this, stdout, stderr, _stopServing.Token)
                : WebLingoCommandThread.Run(
                    () => WebLingoCommandLine.RunDebugFile(launch.Runner, launch.Program!, launch.Arguments, stdout, stderr, this), background: true);
        }
        catch (Exception failure)
        {
            lock (_gate)
            {
                if (!_disconnected) SendEvent("output", new { category = "stderr", output = failure.Message + "\n" });
            }
        }
        finally
        {
            try { Directory.SetCurrentDirectory(originalDirectory); }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // The adapter exits after this session, even if the launcher's directory has disappeared.
            }
            lock (_gate)
            {
                _finished = true;
                if (!_disconnected)
                {
                    SendEvent("exited", new { exitCode = exit });
                    SendEvent("terminated");
                }
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>
    /// Attached to the running site until the session ends, its requests threads of this session's meanwhile, then detached,
    /// the site going on, its process's current folder and its output left as they are; the client told that the session
    /// has ended, not that the site has exited (section 5.7, "Debugging a site where it runs, parts", part 1).
    /// </summary>
    private void Attach()
    {
        try
        {
            using (running!.Attach(this)) _stopServing.Token.WaitHandle.WaitOne();
        }
        catch (Exception failure)
        {
            lock (_gate)
            {
                if (!_disconnected) SendEvent("output", new { category = "stderr", output = failure.Message + "\n" });
            }
        }
        finally
        {
            lock (_gate)
            {
                _finished = true;
                if (!_disconnected) SendEvent("terminated");
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>Ends the program, or stops serving the site, every stopped thread going on, no more to stop.</summary>
    private void Cancel()
    {
        Cancellation.Cancel();
        _stopServing.Cancel();
        Scheduler.Post(static () => { });
        foreach (var thread in _threads.Values)
        {
            if (thread.IsStopped) thread.Go(Step.None);
        }
        Monitor.PulseAll(_gate);
    }

    /// <summary>Whether its threads stop at nothing: it runs without debugging, or it is ending.</summary>
    private bool Ignores => _launch is null || _launch.NoDebug || Cancellation.IsCancellationRequested;

    private void SendEvent(string name, object? body = null)
    {
        try { _protocol.Event(name, body); }
        catch (IOException) { _disconnected = true; Cancel(); }
    }

    /// <summary>
    /// How a position of the program, its file's name as the compiler gives it and its offset there, is found in the file it
    /// is in, as a diagnostic is, a page's include's in the include; and how the output is flushed before a stop is reported.
    /// </summary>
    public void SetSources(WebLingoDebugLocator locate, Action flushOutput)
    {
        lock (_gate)
        {
            Program!.Locate = locate;
            Program.FlushOutput = flushOutput;
        }
    }

    /// <summary>A request of the site's begun: a thread of its own, named by its method, its path and its number (section 5.7).</summary>
    public IWebLingoDebuggerThread Begin(string name, WebLingoDebugLocator locate, IWebLingoSchedulerHost scheduler)
    {
        lock (_gate)
        {
            var id = ++_nextThread;
            var thread = new DebugThread(this, id, $"{name} #{id}", scheduler) { Locate = locate };
            _threads.Add(id, thread);
            if (!_disconnected) SendEvent("thread", new { reason = "started", threadId = id });
            return thread;
        }
    }

    /// <summary>
    /// A position's file, opened once, by its full path, from the program's folder or the site's root, and its line there;
    /// null where it is in no file, as a handler's request's own code is. A file whose text has changed, a site's reloaded, is
    /// opened anew.
    /// </summary>
    private (SourceFile File, int Line, int Offset)? Where(WebLingoDebugLocator? locate, string source, int start)
    {
        if (locate is null) return null;
        var (text, offset) = locate(source, start);
        var path = Path.GetFullPath(text.Name, _base);
        if (!_files.TryGetValue(path, out var file) || !file.Known.Contains(text))
        {
            if (file is not null && file.Text.Text == text.Text)
            {
                file.Known.Add(text);
            }
            else
            {
                if (_missing.Contains(path)) return null;
                if (!File.Exists(path))
                {
                    _missing.Add(path);
                    return null;
                }
                _files[path] = file = new(path, text);
                file.Known.Add(text);
            }
        }
        return (file, text.GetLocation(offset).Line, offset);
    }

    /// <summary>
    /// The breakpoints a module just loaded makes verified, each told to the client; and one whose line it makes known but
    /// whose condition does not fit the line's scope, told why, once.
    /// </summary>
    private void VerifyBreakpoints()
    {
        foreach (var (path, entries) in _breakpoints)
        {
            foreach (var point in entries)
            {
                if (point.Verified) continue;
                var why = Why(path, point);
                if (why is null) point.Verified = true;
                else if (why == point.Told) continue;
                point.Told = why;
                SendEvent("breakpoint", new { reason = "changed", breakpoint = Report(point) });
            }
        }
    }

    private object[] SetBreakpoints(JsonElement arguments)
    {
        var path = Path.GetFullPath(Text(Property(arguments, "source"), "path")
            ?? throw new ArgumentException("A breakpoint needs a local source path."));
        var next = new List<Breakpoint>();
        var requested = Property(arguments, "breakpoints");
        if (requested.ValueKind == JsonValueKind.Array)
        {
            foreach (var request in requested.EnumerateArray())
            {
                var line = Number(request, "line", -1);
                if (!_oneBasedLines && line is >= 0 and < int.MaxValue) line++;
                var allowed = Text(request, "hitCondition") is null && Text(request, "logMessage") is null;
                var point = new Breakpoint(++_nextBreakpoint, line, allowed);
                // A condition read where it is set, its syntax and what it holds checked now, its names once its line's scope is known.
                if (Text(request, "condition") is { } condition && condition.Trim().Length > 0)
                {
                    WebLingoDebugCondition.TryRead(condition, out var read, out var names, out var wrong);
                    (point.Condition, point.Names, point.ConditionError) = (read, names, wrong);
                }
                point.Told = Why(path, point);
                point.Verified = point.Told is null;
                next.Add(point);
            }
        }
        _breakpoints[path] = next;
        return next.Select(Report).ToArray();
    }

    /// <summary>
    /// Why a breakpoint cannot be verified yet, or null where it can: a kind the debugger does not support, its condition's
    /// error, its line no statement's, or a name its condition reads that is not in scope on its line (section 5.7, part 3).
    /// </summary>
    private string? Why(string path, Breakpoint point)
    {
        if (!point.Allowed) return "Only line breakpoints, and their conditions, are supported.";
        if (point.ConditionError is { } error) return error;
        if (!_files.TryGetValue(path, out var file) || !file.Executable.Contains(point.Line)) return "No executable statement on this line.";
        if (point.Names is null) return null;
        file.Names.TryGetValue(point.Line, out var visible);
        foreach (var name in point.Names)
        {
            if (visible is null || !visible.Contains(name)) return $"The condition reads '{name}', which is not in scope on this line.";
        }
        return null;
    }

    private object Report(Breakpoint point) => new
    {
        id = point.Id, verified = point.Verified, line = ClientLine(Math.Max(1, point.Line)),
        message = point.Verified ? null : point.Told
    };

    private object StackTrace(JsonElement arguments)
    {
        if (ThreadOf(arguments) is not { } thread) return new { stackFrames = Array.Empty<object>(), totalFrames = 0 };
        var frames = thread.Frames;
        var start = Math.Clamp(Number(arguments, "startFrame", 0), 0, frames.Count);
        var count = Number(arguments, "levels", 0);
        var end = count <= 0 ? frames.Count : start + Math.Min(count, frames.Count - start);
        var shown = new List<object>();
        for (var i = start; i < end; i++)
        {
            var frame = frames[i];
            if (thread.Located[i] is not var (file, offset)) continue;
            var at = file.Text.GetLocation(offset);
            var column = at.Offset - file.Text.GetLineStart(at.Line) + (_oneBasedColumns ? 1 : 0);
            shown.Add(new
            {
                id = thread.FirstFrame + i, name = frame.Name, source = new { name = Path.GetFileName(file.Path), path = file.Path },
                line = ClientLine(at.Line), column
            });
        }
        return new { stackFrames = shown, totalFrames = frames.Count };
    }

    private object Scopes(JsonElement arguments)
    {
        var id = Number(arguments, "frameId", 0);
        var thread = _threads.Values.FirstOrDefault(candidate => id >= candidate.FirstFrame && id < candidate.FirstFrame + candidate.Frames.Count)
            ?? throw new ArgumentException("This frame is no longer paused.");
        var reference = Reference(thread, thread.Frames[id - thread.FirstFrame].Variables);
        return new { scopes = reference > 0 ? new[] { new { name = "Locals", variablesReference = reference, expensive = false } } : [] };
    }

    private object[] Variables(JsonElement arguments)
    {
        var id = Number(arguments, "variablesReference", 0);
        if (!_variables.TryGetValue(id, out var items)) return [];
        var thread = _threads.Values.First(candidate => candidate.References.Contains(id));
        return items.Select(item => (object)new
        {
            name = item.Name, value = item.Value, type = item.Type, variablesReference = Reference(thread, item.Children)
        }).ToArray();
    }

    /// <summary>A reference to variables a stopped thread shows, until it goes on.</summary>
    private int Reference(DebugThread thread, IReadOnlyList<WebLingoDebugVariable> items)
    {
        if (items.Count == 0) return 0;
        _variables[++_nextVariable] = items;
        thread.References.Add(_nextVariable);
        return _nextVariable;
    }

    private int ClientLine(int line) => _oneBasedLines ? line : line - 1;

    private static JsonElement Property(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) ? property : default;
    private static string? Text(JsonElement value, string key) =>
        Property(value, key) is { ValueKind: JsonValueKind.String } property ? property.GetString() : null;
    private static int Number(JsonElement value, string key, int fallback) =>
        Property(value, key) is { ValueKind: JsonValueKind.Number } property && property.TryGetInt32(out var number) ? number : fallback;
    private static bool Flag(JsonElement value, string key, bool fallback = false) =>
        Property(value, key).ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => fallback };

    internal enum Step { None, In, Over, Out }

    internal sealed record SourceFile(string Path, WebLingoSourceText Text)
    {
        public HashSet<int> Executable { get; } = [];
        /// <summary>The names in scope at each line's statements, as the Variables view shows them, which a condition may read.</summary>
        public Dictionary<int, HashSet<string>> Names { get; } = [];
        /// <summary>The texts of it, each compilation's own, known to be the text it was opened with.</summary>
        public HashSet<WebLingoSourceText> Known { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private sealed record Breakpoint(int Id, int Line, bool Allowed)
    {
        public bool Verified { get; set; }
        /// <summary>Why it is not verified, as last told to the client.</summary>
        public string? Told { get; set; }
        /// <summary>Its condition, read where it was set, the names it reads, or why it cannot be one (section 5.7, part 3).</summary>
        public WebLingoDebugExpression? Condition { get; set; }
        public HashSet<string>? Names { get; set; }
        public string? ConditionError { get; set; }
    }

    /// <summary>
    /// A thread of the debugger's: the program a launch runs, whose stops wait on its own thread until the debugger goes on;
    /// or a site's request, given its scheduler host, whose stops leave it waiting as at an <c>await</c>, resumed through
    /// that host when the debugger goes on (section 5.7, "Debugging WASP pages, parts", part 2). Its points are its own,
    /// located by its locator, each page's compilation positioning its includes in its own way.
    /// </summary>
    internal sealed class DebugThread(WebLingoDebugSession session, int id, string name, IWebLingoSchedulerHost? scheduler)
        : IWebLingoDebuggerThread
    {
        private readonly Dictionary<(string Source, int Start), (SourceFile File, int Line)> _points = [];
        private Step _step;
        private int _stepDepth;
        private int? _hit;
        private string? _why;
        private WebLingoDebugPoint? _last;
        private int _lastLine;
        private int _stops;
        private bool _ended;

        public int Id => id;
        public string Name => name;
        public WebLingoDebugLocator? Locate { get; set; }
        public Action? FlushOutput { get; set; }

        /// <summary>Its frames while it is stopped, the first's id <see cref="FirstFrame"/>, the next's one more; none otherwise.</summary>
        public IReadOnlyList<WebLingoDebugFrame> Frames { get; private set; } = [];

        /// <summary>
        /// Where each of its frames is, located as it stopped, on its own thread, which alone uses its page's compilation; null
        /// for one in no file.
        /// </summary>
        public (SourceFile File, int Offset)?[] Located { get; private set; } = [];
        public int FirstFrame { get; private set; }
        public string Reason { get; private set; } = "";

        /// <summary>The references to variables given while it is stopped.</summary>
        public HashSet<int> References { get; } = [];

        public bool IsStopped => Frames.Count > 0;
        public int Stops => Volatile.Read(ref _stops);

        public void ModuleLoaded(WebLingoCompiledModule module)
        {
            lock (session._gate)
            {
                if (session._disconnected) return;
                Record(module.EntryPoint);
                foreach (var function in module.Functions) Record(function);
                session.VerifyBreakpoints();
            }
        }

        private void Record(WebLingoFunction function)
        {
            if (function.DebugInfo is not { } info) return;
            foreach (var span in info.Points.Values)
            {
                if (_points.ContainsKey((info.Source, span.Start)) || session.Where(Locate, info.Source, span.Start) is not var (file, line, _)) continue;
                _points[(info.Source, span.Start)] = (file, line);
                file.Executable.Add(line);
                // The names a condition on this line may read: those in scope at its statements.
                if (!file.Names.TryGetValue(line, out var names)) file.Names[line] = names = new(StringComparer.Ordinal);
                foreach (var capture in info.Captures)
                {
                    if (capture.Length > 0) names.Add(capture);
                }
                foreach (var local in info.Locals)
                {
                    if (span.Start >= local.VisibleFrom && span.Start < local.VisibleUntil) names.Add(local.Name);
                }
            }
        }

        public bool ShouldStop(WebLingoDebugPoint point, IWebLingoDebugScope scope)
        {
            lock (session._gate)
            {
                if (session.Ignores) return false;
                if (!_points.TryGetValue((point.Source, point.Span.Start), out var at)) return false;
                var (source, line) = at;
                var fresh = _last is not { } prior || prior.Activation != point.Activation
                    || _lastLine != line || point.Instruction <= prior.Instruction;
                (_last, _lastLine) = (point, line);
                var hit = fresh && session._breakpoints.TryGetValue(source.Path, out var points)
                    ? points.Find(candidate => candidate.Verified && candidate.Line == line) : null;
                _why = null;
                // A condition, tested where it stands: it stops where it holds, or where it fails, saying why (section 5.7, part 3).
                if (hit?.Condition is { } condition && scope.TryTest(condition, out var holds, out _why) && !holds) hit = null;
                _hit = hit?.Id;
                Reason = session._entry ? "entry" : session._pause ? "pause" : _hit is not null ? "breakpoint"
                    : fresh && (_step == Step.In || (_step == Step.Over && point.Depth <= _stepDepth)
                        || (_step == Step.Out && point.Depth < _stepDepth)) ? "step" : "";
                return Reason.Length != 0;
            }
        }

        public bool Stopped(IReadOnlyList<WebLingoDebugFrame> frames)
        {
            lock (session._gate)
            {
                if (session.Ignores || _ended) return true;
                FlushOutput?.Invoke();
                session._entry = false;
                if (scheduler is null) session._pause = false;
                _step = Step.None;
                Located = new (SourceFile, int)?[frames.Count];
                for (var i = 0; i < frames.Count; i++)
                {
                    if (session.Where(Locate, frames[i].Source, frames[i].Span.Start) is var (file, _, offset)) Located[i] = (file, offset);
                }
                (Frames, FirstFrame) = (frames, session._nextFrame + 1);
                session._nextFrame += frames.Count;
                Interlocked.Increment(ref _stops);
                if (Reason == "breakpoint" && _why is { } why)
                {
                    // Its condition failed: it stops, saying why (the owner's answer, 24 November 2026, part 3's A2).
                    session.SendEvent("output", new { category = "stderr", output = $"The breakpoint's condition failed: {why}\n" });
                    session.SendEvent("stopped", new
                    {
                        reason = Reason, description = "Paused on a breakpoint whose condition failed", text = why, threadId = id,
                        allThreadsStopped = scheduler is null, hitBreakpointIds = _hit is { } failed ? new[] { failed } : []
                    });
                }
                else
                {
                    session.SendEvent("stopped", new
                    {
                        reason = Reason, threadId = id, allThreadsStopped = scheduler is null,
                        hitBreakpointIds = Reason == "breakpoint" && _hit is { } hit ? new[] { hit } : []
                    });
                }
                // A site's request waits as at an await, its worker free; the program waits here.
                if (scheduler is not null) return false;
                while (IsStopped && !session.Ignores) Monitor.Wait(session._gate);
                return true;
            }
        }

        public bool Waits
        {
            get
            {
                lock (session._gate) return IsStopped && !session.Ignores;
            }
        }

        /// <summary>Goes on from where it stopped, stepping as <paramref name="step"/> says; within the session's lock.</summary>
        public void Go(Step step)
        {
            (_step, _stepDepth) = (step, _last?.Depth ?? 0);
            (Frames, Located) = ([], []);
            Interlocked.Increment(ref _stops);
            foreach (var reference in References) session._variables.Remove(reference);
            References.Clear();
            if (scheduler is null) Monitor.PulseAll(session._gate);
            else scheduler.Post(static () => { }); // Its worker resumes it, to see that it may go on.
        }

        /// <summary>Its request ended: the thread gone, told to the client.</summary>
        public void Dispose()
        {
            lock (session._gate)
            {
                if (_ended) return;
                _ended = true;
                (Frames, Located) = ([], []);
                foreach (var reference in References) session._variables.Remove(reference);
                References.Clear();
                session._threads.Remove(id);
                if (!session._disconnected) session.SendEvent("thread", new { reason = "exited", threadId = id });
            }
        }
    }

    /// <summary>
    /// A launch's settings, the Debug Adapter Protocol's <c>noDebug</c> and the adapter's own: <c>program</c>, the script or
    /// the page, from <c>cwd</c>, its working folder, the program's own by default; <c>args</c>, its parameters, each
    /// <c>name=value</c>; <c>stopOnEntry</c>; and, for a page, <c>root</c>, the application's, the page's folder by default,
    /// as <c>wasp run --root</c> gives it (specification, section 5.7). Where the adapter serves sites, a launch without
    /// <c>program</c> serves the site at <c>root</c>, on <c>port</c>, the server's own by default, as <c>wasp serve</c> does.
    /// </summary>
    private sealed record LaunchSettings(WebLingoCommandLineProgram Runner, string? Program, string Directory, string[] Arguments,
        bool StopOnEntry, bool NoDebug)
    {
        /// <summary>The site's root, where it serves a site, not a program.</summary>
        public string? Site { get; init; }
        public int? Port { get; init; }

        /// <summary>Whether it attaches to a site that runs already, rather than serve one ("Debugging a site where it runs, parts", part 1).</summary>
        public bool Attached { get; init; }

        public static LaunchSettings Read(JsonElement message, Func<string?, WebLingoCommandLineProgram> programFor, bool serves)
        {
            var kind = programFor(null);
            var what = kind.Extension == ".wasp" ? "WASP .wasp page" : "WebLingo .wlg script";
            var file = Text(message, "program");
            var directory = Text(message, "cwd") is { } cwd ? Path.GetFullPath(cwd) : null;
            var root = Text(message, "root");
            if (file is null && serves)
            {
                // A site served, each request a thread (section 5.7, "Debugging WASP pages, parts", part 2).
                if (root is null) throw new ArgumentException($"Set program to a {what}, or root to the site to serve.");
                root = directory is null ? Path.GetFullPath(root) : Path.GetFullPath(root, directory);
                if (!System.IO.Directory.Exists(root)) throw new ArgumentException($"The root '{root}' does not exist.");
                directory ??= root;
                if (!System.IO.Directory.Exists(directory)) throw new ArgumentException($"The working directory '{directory}' does not exist.");
                int? port = Property(message, "port").ValueKind == JsonValueKind.Undefined ? null : Number(message, "port", -1);
                if (port is < 0 or > 65535) throw new ArgumentException("port must be a port's number, from 0 to 65535.");
                return new(programFor(root), null, directory, [], false, Flag(message, "noDebug")) { Site = root, Port = port };
            }
            if (file is null) throw new ArgumentException($"Set program to a {what}.");
            var program = directory is null ? Path.GetFullPath(file) : Path.GetFullPath(file, directory);
            if (!program.EndsWith(kind.Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(program))
                throw new ArgumentException($"'{program}' is not an existing {what}.");
            if (root is not null && kind.Extension != ".wasp") throw new ArgumentException("root is a page's setting, not a script's.");
            root = root is null ? null : directory is null ? Path.GetFullPath(root) : Path.GetFullPath(root, directory);
            if (root is not null && !System.IO.Directory.Exists(root)) throw new ArgumentException($"The root '{root}' does not exist.");
            directory ??= Path.GetDirectoryName(program)!;
            if (!System.IO.Directory.Exists(directory)) throw new ArgumentException($"The working directory '{directory}' does not exist.");
            var values = Property(message, "args");
            var args = new List<string>();
            if (values.ValueKind != JsonValueKind.Undefined)
            {
                if (values.ValueKind != JsonValueKind.Array) throw new ArgumentException("args must be an array of name=value strings.");
                foreach (var value in values.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.String) throw new ArgumentException("args must be an array of name=value strings.");
                    args.Add(value.GetString()!);
                }
            }
            return new(programFor(root), program, directory, args.ToArray(), Flag(message, "stopOnEntry"), Flag(message, "noDebug"));
        }
    }

    private sealed class DebugWriter(WebLingoDebugProtocol protocol, string category) : TextWriter
    {
        private readonly StringBuilder _line = new();
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            _line.Append(value);
            if (value == '\n' || _line.Length >= 4096 && !char.IsHighSurrogate(value)) Flush();
        }

        public override void Flush()
        {
            if (_line.Length == 0) return;
            var text = _line.ToString();
            _line.Clear();
            protocol.Event("output", new { category, output = text });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Flush();
            base.Dispose(disposing);
        }
    }
}
