using System.Globalization;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Language;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
namespace WebLingo.Cli;

public static partial class WebLingoCommandLine
{
    /// <summary>
    /// A script's workers under <c>weblingo</c> (specification, section 9, "M0.26 parts", part 2), <c>std:workers</c>'
    /// host (<see cref="IWebLingoWorkers"/>): each worker a module of the program, found from the program's own folder, read
    /// and compiled where its <c>Worker</c> is made, with the program's configuration, and run on a thread of its own, with
    /// the compiler's stack (<see cref="WebLingoParser.CompilerStackSize"/>), an execution, a heap and a scheduler host of its
    /// own; what each end posts kept in a queue of the other's, locked, and given to its execution through its scheduler
    /// host's <c>Post</c>. A worker ends once its module's top level has ended and none of its tasks has work, when its
    /// <c>Worker</c> is disposed, cancelled, or when its parent ends, its own workers with it.
    /// </summary>
    private sealed class Workers(Workers.Settings run, IWebLingoWorkerLink? parent) : IWebLingoWorkers
    {
        /// <summary>
        /// What a run of a program gives each of its workers: its program, its file, its schemes, its writers and its input;
        /// its configuration's limits, and the count of its workers running, which all of them share (section 9, "M0.26
        /// parts", part 4).
        /// </summary>
        public sealed record Settings(WebLingoCommandLineProgram Program, string MainPath, IReadOnlyDictionary<string, string> GivenSchemes,
            TextWriter Output, TextWriter Error, IWebLingoConsoleInput? Input, Action? Console, int RegexTimeout, WebLingoLimitSettings Limits,
            Running Count);

        /// <summary>
        /// How many of a program's workers run at once, its own, its workers' and its pools' alike, and the most that may,
        /// <c>"limits"</c>' <c>"workers"</c> (section 9, "M0.26 parts", part 4), the machine's processors by default; each pool
        /// told when one ends, so that a call it keeps waiting may start one.
        /// </summary>
        public sealed class Running(int most)
        {
            private readonly object _gate = new();
            private readonly List<Action> _told = [];
            private int _count;

            public int Most => most;

            public bool TryTake()
            {
                lock (_gate)
                {
                    if (_count >= most) return false;
                    _count++;
                    return true;
                }
            }

            public void Give()
            {
                Action[] told;
                lock (_gate)
                {
                    _count--;
                    told = [.. _told];
                }
                foreach (var tell in told) tell();
            }

            public void WhenOneEnds(Action tell)
            {
                lock (_gate) _told.Add(tell);
            }
        }

        private readonly List<Worker> _started = [];
        private readonly Dictionary<string, Pool> _pools = new(StringComparer.Ordinal);
        private readonly Settings _run = run;
        private bool _ended;

        public IWebLingoWorkerLink? Parent => parent;

        public bool TryStart(string path, out IWebLingoWorkerLink? worker, out WebLingoRuntimeError? error)
        {
            worker = null;
            if (!TryFind(path, "worker's", out var file, out error)) return false;
            var diagnostics = new StringWriter();
            var source = TryReadText(file, diagnostics, out var text) ? Prepare(file, text, diagnostics) : null;
            if (source is null)
            {
                error = NotCompiled("worker's", path, diagnostics);
                return false;
            }
            // Its warnings where the program's are written.
            _run.Error.Write(diagnostics.ToString());
            worker = Launch(source, path, perCall: false, out error, out _);
            return worker is not null;
        }

        public bool TryPool(string path, out IWebLingoWorkerPool? pool, out WebLingoRuntimeError? error)
        {
            pool = null;
            if (!TryFind(path, "pool's", out var file, out error)) return false;
            lock (_pools)
            {
                if (_pools.TryGetValue(file, out var found))
                {
                    pool = found;
                    return true;
                }
            }
            var diagnostics = new StringWriter();
            if (!TryReadText(file, diagnostics, out var text))
            {
                error = NotCompiled("pool's", path, diagnostics);
                return false;
            }
            // Its driver beside it, which no file holds, so that it imports the module as the module's own imports are found.
            var exports = Pool.Exports(text, file);
            var driver = Relative(Directory.GetCurrentDirectory(),
                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)) ?? "", $".weblingo-pool-{Guid.NewGuid():N}{_run.Program.Extension}"));
            var driverText = Pool.Driver(Path.GetFileName(file), exports);
            if (Prepare(driver, driverText, diagnostics) is not { } first)
            {
                error = NotCompiled("pool's", path, diagnostics);
                return false;
            }
            _run.Error.Write(diagnostics.ToString());
            var made = new Pool(this, path, driver, driverText, exports, first);
            lock (_pools)
            {
                if (!_pools.TryGetValue(file, out var found)) _pools[file] = found = made;
                pool = found;
            }
            return true;
        }

        /// <summary>
        /// A module's file, from the program's own folder, as a browser finds a worker's script from its page's (Proposed),
        /// named as an imported file is, by its path from the current folder, with / between its folders; an
        /// <c>IOError</c> where it is not there.
        /// </summary>
        private bool TryFind(string path, string whose, out string file, out WebLingoRuntimeError? error)
        {
            error = null;
            file = Relative(Directory.GetCurrentDirectory(), Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_run.MainPath)) ?? "", path));
            if (Path.GetExtension(file).Length == 0) file += _run.Program.Extension;
            if (File.Exists(file)) return true;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IOFailure, $"The {whose} module '{path}' is not there.");
            return false;
        }

        private static WebLingoRuntimeError NotCompiled(string whose, string path, StringWriter diagnostics) =>
            new(WebLingoDiagnosticCodes.InvalidOperation, $"The {whose} module '{path}' does not compile: {diagnostics.ToString().TrimEnd()}");

        private static WebLingoRuntimeError Ended() =>
            new(WebLingoDiagnosticCodes.InvalidOperation, "The program has ended: it starts no more workers.");

        private static WebLingoRuntimeError TooMany(int most) =>
            new(WebLingoDiagnosticCodes.InvalidOperation, $"This program runs {most} {(most == 1 ? "worker" : "workers")} at once, the most "
                + "\"limits\"' \"workers\" in weblingo.config allows: one must end before another starts.");

        /// <summary>A worker's time, or a pool's call's, passed: what it is stopped with, as a request past its time is.</summary>
        private static string TimedOut(string what, double seconds) =>
            $"The {what} took longer than {seconds.ToString(CultureInfo.InvariantCulture)} {(seconds == 1 ? "second" : "seconds")}, its limit, "
            + "\"limits\"' \"workerTimeout\" in weblingo.config.";

        /// <summary>A program for a worker, its text given, compiled with the program's configuration, its diagnostics written; null where it does not compile.</summary>
        private SourceRun? Prepare(string name, string text, TextWriter diagnostics)
        {
            var sourceText = new WebLingoSourceText(text, name);
            if (TryHost(_run.Program, _run.MainPath, name, _run.GivenSchemes, _run.Output, diagnostics, _run.Input, _run.Console) is not { } host) return null;
            var compilation = _run.Program.Compile(sourceText, host.Options);
            var prepared = new SourceRun(sourceText, host, host.Options, compilation);
            foreach (var diagnostic in compilation.Result.Diagnostics) WriteDiagnostic(diagnostics, prepared.TextOf(diagnostic), diagnostic);
            return compilation.Result.Module is null ? null : prepared;
        }

        /// <summary>
        /// A worker started, running the program compiled, its time each call's where <paramref name="perCall"/>, a pool's;
        /// null, with why, once its program has ended, which starts no more, or where as many run as may, <paramref name="tooMany"/>.
        /// </summary>
        private Worker? Launch(SourceRun source, string path, bool perCall, out WebLingoRuntimeError? error, out bool tooMany)
        {
            (error, tooMany) = (null, false);
            var worker = new Worker(this, source, path, perCall);
            lock (_started)
            {
                if (_ended) error = Ended();
                else if (!_run.Count.TryTake()) (error, tooMany) = (TooMany(_run.Count.Most), true);
                else _started.Add(worker);
            }
            if (error is not null) return null;
            worker.Start();
            return worker;
        }

        /// <summary>Every worker started ended, cancelled, and waited for a while, so that what they wrote is written: where the program, or the worker that started them, ends.</summary>
        public void End()
        {
            Worker[] started;
            lock (_started)
            {
                _ended = true;
                started = [.. _started];
            }
            foreach (var worker in started) worker.End();
            foreach (var worker in started) worker.Join();
        }

        /// <summary>
        /// The pool of a module's exported functions (section 9, "M0.26 parts", part 3): each of its workers a driver of its
        /// own, compiled for it, which imports the module, so that the module runs once on each, then calls the function
        /// each message names, one at a time, posting back what it gave; an exception it throws ending that worker, its
        /// parent's receipt its error, a new worker taking the next call. Its workers made as calls come, as many as the
        /// machine's processors at most, a call beyond them queued until one is free.
        /// </summary>
        private sealed class Pool(Workers workers, string path, string driver, string driverText,
            IReadOnlyDictionary<string, (int Least, int Most)?> exports, SourceRun first) : IWebLingoWorkerPool
        {
            private static readonly int Most = Environment.ProcessorCount;
            private readonly object _gate = new();
            private readonly Stack<Worker> _free = new();
            private readonly Queue<(WebLingoMessage Call, Action<WebLingoWorkerReceipt> Done)> _waiting = new();
            private SourceRun? _first = first;
            private int _count;
            private bool _told;

            /// <summary>
            /// What a module exports that a call may name: each function with the arguments it takes, the least and the
            /// most, and each variable or constant, which may hold a function, with none known. Nothing where it does not
            /// parse, which its compiling reports.
            /// </summary>
            public static Dictionary<string, (int Least, int Most)?> Exports(string text, string name)
            {
                var exports = new Dictionary<string, (int Least, int Most)?>(StringComparer.Ordinal);
                foreach (var statement in new WebLingoParser(text, name).ParseCompilationUnit().Root.Statements)
                {
                    if (statement is not WebLingoExportSyntax export) continue;
                    if (export.Declaration is WebLingoFunctionDeclarationSyntax function)
                    {
                        var (least, most) = (0, function.Parameters.Count);
                        for (var i = 0; i < function.Parameters.Count; i++)
                        {
                            if (function.Parameters[i].IsRest) most = int.MaxValue;
                            else if (function.Parameters[i].Default is null) least = i + 1;
                        }
                        exports[function.Name] = (least, most);
                    }
                    else if (export.Declaration is WebLingoVariableDeclarationSyntax variable) exports[variable.Name] = null;
                }
                return exports;
            }

            /// <summary>The driver's text: the module imported, then each call's function called, its result posted back, until its parent ends it.</summary>
            public static string Driver(string fileName, IReadOnlyDictionary<string, (int Least, int Most)?> exports)
            {
                var text = new System.Text.StringBuilder("import { parent } from \"std:workers\";\nimport work from ")
                    .Append(WebLingoValueFormatter.Quote("./" + fileName)).Append(";\nwhile (true) {\n")
                    .Append("    var call = await parent.receive();\n    if (call == null) break;\n    switch (call[0]) {\n");
                foreach (var name in exports.Keys) text.Append($"        case \"{name}\" {{ parent.post(await work.{name}(...call[1])); }}\n");
                return text.Append("    }\n}\n").ToString();
            }

            public bool TryCheck(string name, int count, out WebLingoRuntimeError? error)
            {
                error = null;
                if (!exports.TryGetValue(name, out var takes))
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NoSuchMember, $"The module '{path}' exports no function '{name}'.");
                    return false;
                }
                if (takes is not { } arity || count >= arity.Least && count <= arity.Most) return true;
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.WrongArgumentCount,
                    arity.Most == int.MaxValue ? $"'{name}' takes at least {arity.Least} {(arity.Least == 1 ? "argument" : "arguments")}, not {count}."
                    : arity.Least == arity.Most ? $"'{name}' takes {arity.Most} {(arity.Most == 1 ? "argument" : "arguments")}, not {count}."
                    : $"'{name}' takes {arity.Least} to {arity.Most} arguments, not {count}.");
                return false;
            }

            public void Run(WebLingoMessage call, Action<WebLingoWorkerReceipt> done)
            {
                Worker? worker;
                lock (_gate)
                {
                    if (!_free.TryPop(out worker))
                    {
                        if (_count >= Most)
                        {
                            _waiting.Enqueue((call, done));
                            return;
                        }
                        _count++;
                    }
                }
                if (worker is null && (worker = Start(call, done)) is null) return;
                Give(worker, call, done);
            }

            /// <summary>
            /// A new worker of the pool, its driver compiled anew but for the first; null where it cannot be had, the call
            /// then kept waiting where as many workers run as may, until one ends, or given why.
            /// </summary>
            private Worker? Start(WebLingoMessage call, Action<WebLingoWorkerReceipt> done)
            {
                var source = Interlocked.Exchange(ref _first, null);
                var diagnostics = new StringWriter();
                source ??= workers.Prepare(driver, driverText, diagnostics);
                WebLingoRuntimeError? error = null;
                var tooMany = false;
                var worker = source is null ? null : workers.Launch(source, path, perCall: true, out error, out tooMany);
                if (worker is not null) return worker;
                if (source is not null) _first = source; // Compiled, kept for the next.
                var tell = false;
                lock (_gate)
                {
                    _count--;
                    if (tooMany)
                    {
                        // Waiting for a worker of its own, or, told when any ends, for one it may start (section 9, part 4).
                        _waiting.Enqueue((call, done));
                        tell = !_told;
                        _told = true;
                    }
                }
                if (tell) workers._run.Count.WhenOneEnds(Retry);
                if (tooMany) return null;
                var why = source is null ? NotCompiled("pool's", path, diagnostics).Message : error!.Message;
                done(new WebLingoWorkerReceipt(WebLingoMessage.OfError("TypeError", why, WebLingoDiagnosticCodes.InvalidOperation, null), true));
                return null;
            }

            /// <summary>A worker of the program's ended: a call kept waiting given a new worker of the pool's, where it may start one.</summary>
            private void Retry()
            {
                (WebLingoMessage Call, Action<WebLingoWorkerReceipt> Done) next;
                lock (_gate)
                {
                    if (_count >= Most || !_waiting.TryDequeue(out next)) return;
                    _count++;
                }
                if (Start(next.Call, next.Done) is { } worker) Give(worker, next.Call, next.Done);
            }

            /// <summary>The call given the worker, its time its own, and its answer to the call's caller: the worker then free, or, ended, gone.</summary>
            private void Give(Worker worker, WebLingoMessage call, Action<WebLingoWorkerReceipt> done)
            {
                worker.Time();
                worker.Post(call);
                worker.Receive(receipt =>
                {
                    worker.Untime();
                    if (!receipt.Failed && receipt.Message is not null)
                    {
                        // Free before its caller is answered, so that the call it makes next finds it free.
                        Free(worker);
                        done(receipt);
                        return;
                    }
                    // It ended, by the call's exception, or with the program, before it answered.
                    done(receipt.Failed ? receipt : new WebLingoWorkerReceipt(
                        WebLingoMessage.OfError("Error", "The pool's worker ended before it answered: its program has ended.", null, null), true));
                    Gone();
                });
            }

            private void Free(Worker worker)
            {
                (WebLingoMessage Call, Action<WebLingoWorkerReceipt> Done) next;
                lock (_gate)
                {
                    if (!_waiting.TryDequeue(out next))
                    {
                        _free.Push(worker);
                        return;
                    }
                }
                Give(worker, next.Call, next.Done);
            }

            /// <summary>A worker gone, which a waiting call replaces with one of its own.</summary>
            private void Gone()
            {
                (WebLingoMessage Call, Action<WebLingoWorkerReceipt> Done) next;
                lock (_gate)
                {
                    if (!_waiting.TryDequeue(out next))
                    {
                        _count--;
                        return;
                    }
                }
                if (Start(next.Call, next.Done) is { } worker) Give(worker, next.Call, next.Done);
            }
        }

        /// <summary>
        /// A worker, as its parent holds it: its thread, its cancellation, and the two queues, what its parent posts it and
        /// what it posts back.
        /// </summary>
        private sealed class Worker(Workers workers, SourceRun source, string path, bool perCall) : IWebLingoWorkerLink
        {
            private readonly Mailbox _toWorker = new();
            private readonly Mailbox _fromWorker = new();
            private readonly WebLingoCancellationSignal _cancellation = new();
            private Thread? _thread;
            private Timer? _timer;

            /// <summary>Its time begun, <c>"workerTimeout"</c>'s, where it has one: a pool's worker's for each call, another's for its life.</summary>
            public void Time()
            {
                if (workers._run.Limits.WorkerTimeoutSeconds is not { } seconds) return;
                var limit = TimedOut(perCall ? "call" : "worker", seconds);
                _timer = new Timer(_ => _cancellation.Exceeded(limit), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
            }

            /// <summary>A call's time ended, it having answered.</summary>
            public void Untime() => Interlocked.Exchange(ref _timer, null)?.Dispose();

            public void Start()
            {
                _thread = new Thread(Body, WebLingoParser.CompilerStackSize) { Name = $"WebLingo worker {path}", IsBackground = true };
                // Each worker's thread a single-threaded apartment's on Windows, as a script's is, for COM (section 3.10).
                if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }

            public void Post(WebLingoMessage message) => _toWorker.Post(message);

            public void Receive(Action<WebLingoWorkerReceipt> received) => _fromWorker.Receive(received);

            /// <summary>The worker ended, cancelled, as a request past its time is; nothing more is received from it.</summary>
            public void End()
            {
                _cancellation.Cancel();
                _fromWorker.End(new WebLingoWorkerReceipt(null, false));
            }

            /// <summary>Waits a while for its thread to end, a worker blocked where no cancellation reaches left to end with the process.</summary>
            public void Join() => _thread?.Join(TimeSpan.FromSeconds(5));

            private void Body()
            {
                var own = new Workers(workers._run, new ParentEnd(_toWorker, _fromWorker));
                var limits = workers._run.Limits;
                WebLingoExecutionResult result;
                if (!perCall) Time();
                try
                {
                    // Its instructions and its heap's memory, "workerInstructions" and "workerMemory" (section 9, part 4).
                    var context = Context(source.Host, new Dictionary<string, string>(), source.LineOf, workers._run.RegexTimeout) with
                    {
                        Cancellation = _cancellation,
                        // Its timers' waits ended where it is cancelled, disposed or past its time.
                        Clock = new SystemClock(new DotNetClock(), _cancellation),
                        Workers = own,
                        IsWorker = true,
                        InstructionBudget = limits.WorkerInstructions ?? long.MaxValue,
                        Resources = limits.WorkerMemoryMegabytes is { } megabytes
                            ? WebLingoResourceBudget.Default with { MaxHeapUnits = megabytes * 512 * 1024 }
                            : WebLingoResourceBudget.Default
                    };
                    result = OnVm(source.Compilation.Result.Module!, new WebLingoModuleLoader(source.Options), source.Compilation.Components, context);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    result = new WebLingoExecutionResult(false, new WebLingoDiagnostic(WebLingoDiagnosticCodes.InvalidOperation,
                        WebLingoDiagnosticSeverity.Error, $"The worker failed: {exception.Message}", new WebLingoSourceSpan(0, 0)));
                }
                finally
                {
                    Untime();
                    own.End(); // Its own workers end with it.
                }
                // Stopped past its time, as a request is, WL3003; else ended, cancelled, or by an exception nothing caught.
                if (_cancellation.Limit is { } limit && result.Error is { } stopped)
                {
                    _fromWorker.End(new WebLingoWorkerReceipt(WebLingoMessage.OfError("Error", limit, WebLingoDiagnosticCodes.BudgetExceeded, At(stopped)), true));
                }
                else if (result.Succeeded || _cancellation.IsCancellationRequested) _fromWorker.End(new WebLingoWorkerReceipt(null, false));
                else _fromWorker.End(new WebLingoWorkerReceipt(Thrown(result), true));
                workers._run.Count.Give(); // No longer one of its program's workers running.
            }

            /// <summary>
            /// The exception it ended with, for its parent, thrown where the worker's host locates it, <c>work.wlg(12,5)</c>:
            /// the value thrown, copied, or, where none was, or a message cannot hold it, an <c>Error</c> of its report.
            /// </summary>
            private WebLingoMessage Thrown(WebLingoExecutionResult result)
            {
                var thrownAt = At(result.Error!);
                if (result.Thrown is { } thrown) return thrown.ThrownAt(thrownAt);
                var code = result.Error!.Code;
                return WebLingoMessage.OfError(result.Error.ExceptionClass ?? WebLingoErrorClasses.NameOf(code) ?? "Error", result.Error.Message, code, thrownAt);
            }

            /// <summary>Where a report stands, as the worker's host locates it, <c>work.wlg(12,5)</c>.</summary>
            private string At(WebLingoDiagnostic reported)
            {
                var located = source.Compilation.Locate(reported);
                var text = source.TextOf(located);
                var at = text.GetLocation(located.Span.Start);
                return $"{text.Name}({at.Line},{at.Column})";
            }
        }

        /// <summary>A worker's own end of its link with its parent, which <c>std:workers</c>' <c>parent</c> holds; ending it ends nothing.</summary>
        private sealed class ParentEnd(Mailbox fromParent, Mailbox toParent) : IWebLingoWorkerLink
        {
            public void Post(WebLingoMessage message) => toParent.Post(message);

            public void Receive(Action<WebLingoWorkerReceipt> received) => fromParent.Receive(received);

            public void End() { }
        }

        /// <summary>
        /// What one end posts the other, in order, and the other's <c>receive()</c>s waiting for it, in order; once its sender
        /// has ended, what it ended with given once, after all it posted, and nothing after that.
        /// </summary>
        private sealed class Mailbox
        {
            private readonly object _gate = new();
            private readonly Queue<WebLingoMessage> _messages = new();
            private readonly Queue<Action<WebLingoWorkerReceipt>> _waiting = new();
            private WebLingoWorkerReceipt? _end;

            public void Post(WebLingoMessage message)
            {
                Action<WebLingoWorkerReceipt>? waiting;
                lock (_gate)
                {
                    if (_end is not null) return; // Dropped: its receiver's end has ended.
                    if (!_waiting.TryDequeue(out waiting))
                    {
                        _messages.Enqueue(message);
                        return;
                    }
                }
                waiting(new WebLingoWorkerReceipt(message, false));
            }

            public void Receive(Action<WebLingoWorkerReceipt> received)
            {
                WebLingoWorkerReceipt receipt;
                lock (_gate)
                {
                    if (_messages.TryDequeue(out var message)) receipt = new WebLingoWorkerReceipt(message, false);
                    else if (_end is { } end)
                    {
                        receipt = end;
                        _end = new WebLingoWorkerReceipt(null, false); // Its error given once, nothing after.
                    }
                    else
                    {
                        _waiting.Enqueue(received);
                        return;
                    }
                }
                received(receipt);
            }

            /// <summary>Its sender ended, with <paramref name="end"/>: those waiting given it, the first, then nothing; the first end kept.</summary>
            public void End(WebLingoWorkerReceipt end)
            {
                var given = new List<Action<WebLingoWorkerReceipt>>();
                lock (_gate)
                {
                    if (_end is not null) return;
                    _end = end;
                    if (_messages.Count > 0) return; // Those waiting were given what it posted first: none waits.
                    while (_waiting.TryDequeue(out var waiting)) given.Add(waiting);
                    if (given.Count > 0) _end = new WebLingoWorkerReceipt(null, false);
                }
                for (var i = 0; i < given.Count; i++) given[i](i == 0 ? end : new WebLingoWorkerReceipt(null, false));
            }
        }
    }
}
