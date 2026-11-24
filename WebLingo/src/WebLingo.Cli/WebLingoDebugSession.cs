using System.Text;
using System.Text.Json;
using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.VM;
namespace WebLingo.Cli;

/// <summary>
/// One local launch, of a script, <c>weblingo</c>'s, or of a page, <c>wasp</c>'s, as <paramref name="programFor"/> gives the
/// program for a root (specification, section 5.7). The request loop and the register VM run on separate threads. A position
/// of the program is located in its file by the compilation (<see cref="SetSources"/>), a page's include's in the include.
/// </summary>
internal sealed class WebLingoDebugSession(Stream input, Stream output, Func<string?, WebLingoCommandLineProgram> programFor)
    : IWebLingoRegisterDebugger
{
    private const int ThreadId = 1;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly WebLingoDebugProtocol _protocol = new(input, output);
    private readonly object _gate = new();
    private readonly Dictionary<string, SourceFile> _files = new(PathComparer);
    private readonly Dictionary<(string Source, int Start), (SourceFile File, int Line)> _points = [];
    private readonly Dictionary<string, List<Breakpoint>> _breakpoints = new(PathComparer);
    private readonly Dictionary<int, IReadOnlyList<WebLingoDebugVariable>> _variables = [];
    private Func<string, int, (WebLingoSourceText Text, int Offset)>? _locate;
    private Action? _flushOutput;
    private LaunchSettings? _launch;
    private IReadOnlyList<WebLingoDebugFrame> _frames = [];
    private bool _configured, _finished, _disconnected, _pause, _entry;
    private bool _oneBasedLines = true, _oneBasedColumns = true;
    private int _nextBreakpoint, _nextVariable, _stepDepth;
    private Step _step;
    private string _reason = "";
    private int? _hit;
    private WebLingoDebugPoint? _last;
    private int _lastLine;

    public WebLingoCancellationSignal Cancellation { get; } = new();
    public DotNetSchedulerHost Scheduler { get; } = new();

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
                if (_configured && !_finished) Monitor.Wait(_gate, 1000);
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
                        _protocol.Respond(id, command, new { supportsConfigurationDoneRequest = true, supportsTerminateRequest = true });
                        break;
                    case "launch":
                        if (_launch is not null) throw new ArgumentException("The session has already launched a program.");
                        _launch = LaunchSettings.Read(arguments, programFor);
                        _entry = _launch.StopOnEntry;
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
                        _protocol.Respond(id, command);
                        _ = Task.Run(() => Execute(launch));
                        break;
                    case "threads":
                        _protocol.Respond(id, command, new
                        {
                            threads = _configured && !_finished ? new[] { new { id = ThreadId, name = "WebLingo" } } : []
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
                        if (_frames.Count == 0) _pause = true;
                        _protocol.Respond(id, command);
                        break;
                    case "continue":
                    case "next":
                    case "stepIn":
                    case "stepOut":
                        if (_frames.Count == 0 || _last is not { } point) throw new ArgumentException("The script is not paused.");
                        _stepDepth = point.Depth;
                        _step = command switch { "next" => Step.Over, "stepIn" => Step.In, "stepOut" => Step.Out, _ => Step.None };
                        _protocol.Respond(id, command, command == "continue" ? new { allThreadsContinued = true } : null);
                        Resume();
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

    private void Execute(LaunchSettings launch)
    {
        var exit = 1;
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(launch.Directory);
            using var stdout = new DebugWriter(_protocol, "stdout");
            using var stderr = new DebugWriter(_protocol, "stderr");
            exit = WebLingoCommandThread.Run(
                () => WebLingoCommandLine.RunDebugFile(launch.Runner, launch.Program, launch.Arguments, stdout, stderr, this), background: true);
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

    private void Cancel()
    {
        Cancellation.Cancel();
        Scheduler.Post(static () => { });
        Resume();
    }

    private void Resume()
    {
        _frames = [];
        _variables.Clear();
        Monitor.PulseAll(_gate);
    }

    private void SendEvent(string name, object? body = null)
    {
        try { _protocol.Event(name, body); }
        catch (IOException) { _disconnected = true; Cancel(); }
    }

    /// <summary>
    /// How a position of the program, its file's name as the compiler gives it and its offset there, is found in the file it
    /// is in, its text and its offset in it, as a diagnostic is, a page's include's in the include; and how the output is
    /// flushed before a stop is reported.
    /// </summary>
    public void SetSources(Func<string, int, (WebLingoSourceText Text, int Offset)> locate, Action flushOutput)
    {
        _locate = locate;
        _flushOutput = flushOutput;
    }

    /// <summary>A position's file, opened once, by its full path, and its line there; null where it is in no file.</summary>
    private (SourceFile File, int Line, int Offset)? Where(string source, int start)
    {
        if (_locate is not { } locate) return null;
        var (text, offset) = locate(source, start);
        var path = Path.GetFullPath(text.Name);
        if (!_files.TryGetValue(path, out var file)) _files[path] = file = new(path, text);
        return (file, text.GetLocation(offset).Line, offset);
    }

    public void ModuleLoaded(WebLingoCompiledModule module)
    {
        lock (_gate)
        {
            if (_disconnected) return;
            Record(module.EntryPoint);
            foreach (var function in module.Functions) Record(function);
            foreach (var (path, entries) in _breakpoints)
            {
                foreach (var point in entries)
                {
                    if (point.Verified || !CanBreak(path, point)) continue;
                    point.Verified = true;
                    SendEvent("breakpoint", new { reason = "changed", breakpoint = Report(point) });
                }
            }
        }
    }

    private void Record(WebLingoFunction function)
    {
        if (function.DebugInfo is not { } info) return;
        foreach (var span in info.Points.Values)
        {
            if (_points.ContainsKey((info.Source, span.Start)) || Where(info.Source, span.Start) is not var (file, line, _)) continue;
            _points[(info.Source, span.Start)] = (file, line);
            file.Executable.Add(line);
        }
    }

    public bool ShouldStop(WebLingoDebugPoint point)
    {
        lock (_gate)
        {
            if (_launch is null || _launch.NoDebug || Cancellation.IsCancellationRequested) return false;
            if (!_points.TryGetValue((point.Source, point.Span.Start), out var at)) return false;
            var (source, line) = at;
            var fresh = _last is not { } prior || prior.Activation != point.Activation
                || _lastLine != line || point.Instruction <= prior.Instruction;
            (_last, _lastLine) = (point, line);
            _hit = fresh && _breakpoints.TryGetValue(source.Path, out var points)
                ? points.Find(candidate => candidate.Verified && candidate.Line == line)?.Id : null;
            _reason = _entry ? "entry" : _pause ? "pause" : _hit is not null ? "breakpoint"
                : fresh && (_step == Step.In || (_step == Step.Over && point.Depth <= _stepDepth)
                    || (_step == Step.Out && point.Depth < _stepDepth)) ? "step" : "";
            return _reason.Length != 0;
        }
    }

    public void Stopped(IReadOnlyList<WebLingoDebugFrame> frames)
    {
        lock (_gate)
        {
            if (Cancellation.IsCancellationRequested) return;
            _flushOutput?.Invoke();
            (_entry, _pause, _step) = (false, false, Step.None);
            _frames = frames;
            SendEvent("stopped", new
            {
                reason = _reason, threadId = ThreadId, allThreadsStopped = true,
                hitBreakpointIds = _reason == "breakpoint" && _hit is { } id ? new[] { id } : []
            });
            while (_frames.Count > 0 && !Cancellation.IsCancellationRequested) Monitor.Wait(_gate);
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
                var allowed = Text(request, "condition") is null && Text(request, "hitCondition") is null && Text(request, "logMessage") is null;
                var point = new Breakpoint(++_nextBreakpoint, line, allowed);
                point.Verified = CanBreak(path, point);
                next.Add(point);
            }
        }
        _breakpoints[path] = next;
        return next.Select(Report).ToArray();
    }

    private bool CanBreak(string path, Breakpoint point) =>
        point.Allowed && _files.TryGetValue(path, out var file) && file.Executable.Contains(point.Line);

    private object Report(Breakpoint point) => new
    {
        id = point.Id, verified = point.Verified, line = ClientLine(Math.Max(1, point.Line)),
        message = point.Verified ? null : point.Allowed ? "No executable statement on this line." : "Only line breakpoints are supported."
    };

    private object StackTrace(JsonElement arguments)
    {
        var start = Math.Clamp(Number(arguments, "startFrame", 0), 0, _frames.Count);
        var count = Number(arguments, "levels", 0);
        var end = count <= 0 ? _frames.Count : start + Math.Min(count, _frames.Count - start);
        var shown = new List<object>();
        for (var i = start; i < end; i++)
        {
            var frame = _frames[i];
            if (Where(frame.Source, frame.Span.Start) is not var (file, _, offset)) continue;
            var at = file.Text.GetLocation(offset);
            var column = at.Offset - file.Text.GetLineStart(at.Line) + (_oneBasedColumns ? 1 : 0);
            shown.Add(new
            {
                id = i + 1, name = frame.Name, source = new { name = Path.GetFileName(file.Path), path = file.Path },
                line = ClientLine(at.Line), column
            });
        }
        return new { stackFrames = shown, totalFrames = _frames.Count };
    }

    private object Scopes(JsonElement arguments)
    {
        var index = Number(arguments, "frameId", 0) - 1;
        if ((uint)index >= (uint)_frames.Count) throw new ArgumentException("This frame is no longer paused.");
        var reference = Reference(_frames[index].Variables);
        return new { scopes = reference > 0 ? new[] { new { name = "Locals", variablesReference = reference, expensive = false } } : [] };
    }

    private object[] Variables(JsonElement arguments)
    {
        if (!_variables.TryGetValue(Number(arguments, "variablesReference", 0), out var items)) return [];
        return items.Select(item => (object)new
        {
            name = item.Name, value = item.Value, type = item.Type, variablesReference = Reference(item.Children)
        }).ToArray();
    }

    private int Reference(IReadOnlyList<WebLingoDebugVariable> items)
    {
        if (items.Count == 0) return 0;
        _variables[++_nextVariable] = items;
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

    private enum Step { None, In, Over, Out }
    private sealed record SourceFile(string Path, WebLingoSourceText Text)
    {
        public HashSet<int> Executable { get; } = [];
    }
    private sealed record Breakpoint(int Id, int Line, bool Allowed) { public bool Verified { get; set; } }

    /// <summary>
    /// A launch's settings, the Debug Adapter Protocol's <c>noDebug</c> and the adapter's own: <c>program</c>, the script or
    /// the page, from <c>cwd</c>, its working folder, the program's own by default; <c>args</c>, its parameters, each
    /// <c>name=value</c>; <c>stopOnEntry</c>; and, for a page, <c>root</c>, the application's, the page's folder by default,
    /// as <c>wasp run --root</c> gives it (specification, section 5.7).
    /// </summary>
    private sealed record LaunchSettings(WebLingoCommandLineProgram Runner, string Program, string Directory, string[] Arguments,
        bool StopOnEntry, bool NoDebug)
    {
        public static LaunchSettings Read(JsonElement message, Func<string?, WebLingoCommandLineProgram> programFor)
        {
            var kind = programFor(null);
            var what = kind.Extension == ".wasp" ? "WASP .wasp page" : "WebLingo .wlg script";
            var file = Text(message, "program") ?? throw new ArgumentException($"Set program to a {what}.");
            var directory = Text(message, "cwd") is { } cwd ? Path.GetFullPath(cwd) : null;
            var program = directory is null ? Path.GetFullPath(file) : Path.GetFullPath(file, directory);
            if (!program.EndsWith(kind.Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(program))
                throw new ArgumentException($"'{program}' is not an existing {what}.");
            var root = Text(message, "root");
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
