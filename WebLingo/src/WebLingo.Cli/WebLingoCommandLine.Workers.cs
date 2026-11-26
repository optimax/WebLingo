using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Language;
using WebLingo.Platform.Abstractions;
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
        /// <summary>What a run of a program gives each of its workers: its program, its file, its schemes, its writers and its input.</summary>
        public sealed record Settings(WebLingoCommandLineProgram Program, string MainPath, IReadOnlyDictionary<string, string> GivenSchemes,
            TextWriter Output, TextWriter Error, IWebLingoConsoleInput? Input, Action? Console, int RegexTimeout);

        private readonly List<Worker> _started = [];
        private readonly Settings _run = run;

        public IWebLingoWorkerLink? Parent => parent;

        public bool TryStart(string path, out IWebLingoWorkerLink? worker, out WebLingoRuntimeError? error)
        {
            (worker, error) = (null, null);
            // From the program's own folder, as a browser finds a worker's script from its page's (Proposed), named as an
            // imported file is, by its path from the current folder, with / between its folders.
            var file = Relative(Directory.GetCurrentDirectory(), Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_run.MainPath)) ?? "", path));
            if (Path.GetExtension(file).Length == 0) file += _run.Program.Extension;
            if (!File.Exists(file))
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IOFailure, $"The worker's module '{path}' is not there.");
                return false;
            }
            var diagnostics = new StringWriter();
            var source = PrepareWorker(file, diagnostics);
            if (source is null)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"The worker's module '{path}' does not compile: {diagnostics.ToString().TrimEnd()}");
                return false;
            }
            // Its warnings where the program's are written.
            _run.Error.Write(diagnostics.ToString());
            var started = new Worker(this, source, path);
            lock (_started) _started.Add(started);
            started.Start();
            worker = started;
            return true;
        }

        /// <summary>A worker's module read and compiled with the program's configuration, its diagnostics written; null where it does not compile.</summary>
        private SourceRun? PrepareWorker(string file, TextWriter diagnostics)
        {
            if (!TryReadText(file, diagnostics, out var text)) return null;
            var sourceText = new WebLingoSourceText(text, file);
            if (TryHost(_run.Program, _run.MainPath, file, _run.GivenSchemes, _run.Output, diagnostics, _run.Input, _run.Console) is not { } host) return null;
            var compilation = _run.Program.Compile(sourceText, host.Options);
            var prepared = new SourceRun(sourceText, host, host.Options, compilation);
            foreach (var diagnostic in compilation.Result.Diagnostics) WriteDiagnostic(diagnostics, prepared.TextOf(diagnostic), diagnostic);
            return compilation.Result.Module is null ? null : prepared;
        }

        /// <summary>Every worker started ended, cancelled, and waited for a while, so that what they wrote is written: where the program, or the worker that started them, ends.</summary>
        public void End()
        {
            Worker[] started;
            lock (_started) started = [.. _started];
            foreach (var worker in started) worker.End();
            foreach (var worker in started) worker.Join();
        }

        /// <summary>
        /// A worker, as its parent holds it: its thread, its cancellation, and the two queues, what its parent posts it and
        /// what it posts back.
        /// </summary>
        private sealed class Worker(Workers workers, SourceRun source, string path) : IWebLingoWorkerLink
        {
            private readonly Mailbox _toWorker = new();
            private readonly Mailbox _fromWorker = new();
            private readonly WebLingoCancellationSignal _cancellation = new();
            private Thread? _thread;

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
                WebLingoExecutionResult result;
                try
                {
                    var context = Context(source.Host, new Dictionary<string, string>(), source.LineOf, workers._run.RegexTimeout) with
                    {
                        Cancellation = _cancellation,
                        Workers = own,
                        IsWorker = true
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
                    own.End(); // Its own workers end with it.
                }
                if (result.Succeeded || _cancellation.IsCancellationRequested) _fromWorker.End(new WebLingoWorkerReceipt(null, false));
                else _fromWorker.End(new WebLingoWorkerReceipt(Thrown(result), true));
            }

            /// <summary>
            /// The exception it ended with, for its parent, thrown where the worker's host locates it, <c>work.wlg(12,5)</c>:
            /// the value thrown, copied, or, where none was, or a message cannot hold it, an <c>Error</c> of its report.
            /// </summary>
            private WebLingoMessage Thrown(WebLingoExecutionResult result)
            {
                var located = source.Compilation.Locate(result.Error!);
                var text = source.TextOf(located);
                var at = text.GetLocation(located.Span.Start);
                var thrownAt = $"{text.Name}({at.Line},{at.Column})";
                if (result.Thrown is { } thrown) return thrown.ThrownAt(thrownAt);
                var code = result.Error!.Code;
                return WebLingoMessage.OfError(result.Error.ExceptionClass ?? WebLingoErrorClasses.NameOf(code) ?? "Error", result.Error.Message, code, thrownAt);
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
