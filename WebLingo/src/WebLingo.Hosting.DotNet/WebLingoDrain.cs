using System.Runtime.CompilerServices;
using WebLingo.Runtime;
using WebLingo.VM;
namespace WebLingo.Hosting.DotNet;

/// <summary>
/// An application's drain (specification, section 6.12, "Reloading"; section 9, "M0.22 parts", part 1): once a reload has
/// published a newer generation, each request still running on an older one is stopped when its site's drain has passed
/// (<see cref="WebLingoApplication.Drain"/>), as its time limit stops a request, its cancellation signal told why, with
/// <see cref="WebLingoApplication.DrainMessage"/>, and the request woken where it waits, so that a reload's old generation is
/// gone within the drain. A request its debugger has stopped since its drain began, or stops now, has its drain begin anew,
/// so that the time it is stopped does not count, as a page's send timeout leaves it out. Its timers are the host's, the
/// portable core having none: ASP.NET Core's host and the FastCGI host each begin one for every request they serve, and the
/// reloader tells it of each reload.
/// </summary>
public sealed class WebLingoDrain
{
    private static readonly ConditionalWeakTable<WebLingoApplication, WebLingoDrain> Drains = new();

    private readonly WebLingoApplication _application;
    private readonly Lock _lock = new();
    private readonly HashSet<Request> _running = [];
    private int _published;

    private WebLingoDrain(WebLingoApplication application) => (_application, _published) = (application, application.GenerationNumber);

    /// <summary>The application's drain, the same for each of its hosts and its reloader.</summary>
    public static WebLingoDrain Of(WebLingoApplication application) => Drains.GetValue(application, static made => new WebLingoDrain(made));

    /// <summary>
    /// A request begun, its watch, which <see cref="WebLingoApplication.Handle"/> is given and tells of its generation, kept
    /// until it is disposed, once the request has ended; <paramref name="wake"/> wakes the request where it waits; and
    /// <paramref name="limit"/>, its time limit's timer (<see cref="WebLingoRequestTimer"/>), stopped where its page or handler
    /// declares that it has none.
    /// </summary>
    public Request Begin(WebLingoCancellationSignal cancellation, CancellationTokenSource wake, IDisposable? limit = null)
    {
        var request = new Request(this, cancellation, wake, limit);
        lock (_lock) _running.Add(request);
        return request;
    }

    /// <summary>
    /// Told that <paramref name="generation"/> has been published, from any thread: each request running on an older one has
    /// its drain begun.
    /// </summary>
    public void Reloaded(int generation)
    {
        lock (_lock)
        {
            if (generation <= _published) return;
            _published = generation;
            foreach (var request in _running)
            {
                if (request.Generation is { } served && served < generation) request.Drain();
            }
        }
    }

    /// <summary>How many of its requests are running, their application having said which generation each is served from, which tests read.</summary>
    public int Running
    {
        get
        {
            lock (_lock) return _running.Count(request => request.Generation is not null);
        }
    }

    /// <summary>One request's part of the drain, its watch: the generation it is served from, its debugger's thread, and its timer.</summary>
    public sealed class Request : IWebLingoRequestWatch, IDisposable
    {
        private readonly WebLingoDrain _drain;
        private readonly WebLingoCancellationSignal _cancellation;
        private readonly CancellationTokenSource _wake;
        private IDisposable? _limit;
        private IWebLingoDebuggerThread? _thread;
        private Timer? _timer;
        private int _stops;
        private bool _ended;

        internal Request(WebLingoDrain drain, WebLingoCancellationSignal cancellation, CancellationTokenSource wake, IDisposable? limit) =>
            (_drain, _cancellation, _wake, _limit) = (drain, cancellation, wake, limit);

        /// <summary>The generation it is served from, once its application has said; null until then.</summary>
        internal int? Generation { get; private set; }

        public void Serving(int generation)
        {
            lock (_drain._lock)
            {
                Generation = generation;
                // Published while it began: its drain begins now.
                if (generation < _drain._published) Drain();
            }
        }

        public void Debugged(IWebLingoDebuggerThread thread)
        {
            lock (_drain._lock) _thread = thread;
        }

        public void Untimed()
        {
            IDisposable? limit;
            lock (_drain._lock) (limit, _limit) = (_limit, null);
            limit?.Dispose();
        }

        /// <summary>Its drain begun, once, under the drain's lock.</summary>
        internal void Drain()
        {
            if (_timer is not null || _ended) return;
            _stops = _thread?.Stops ?? 0;
            _timer = new Timer(_ => Due(), null, _drain._application.Drain, Timeout.InfiniteTimeSpan);
        }

        /// <summary>Its drain passed: the request stopped, unless its debugger has stopped it since its drain began.</summary>
        private void Due()
        {
            lock (_drain._lock)
            {
                if (_ended) return;
                if (_thread is { } thread && (thread.Stops != _stops || (thread.Stops & 1) == 1))
                {
                    _stops = thread.Stops;
                    _timer!.Change(_drain._application.Drain, Timeout.InfiniteTimeSpan);
                    return;
                }
            }
            _cancellation.Exceeded(_drain._application.DrainMessage);
            try
            {
                _wake.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The request has ended already.
            }
        }

        /// <summary>The request ended: its timer stopped, and the drain forgets it.</summary>
        public void Dispose()
        {
            lock (_drain._lock)
            {
                _ended = true;
                _drain._running.Remove(this);
                _timer?.Dispose();
            }
        }
    }
}
