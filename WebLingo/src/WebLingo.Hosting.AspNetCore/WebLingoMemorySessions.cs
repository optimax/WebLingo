namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// A site's sessions in the process's memory (specification, section 6.12, "Sessions and cookies"; "Sessions and cookies,
/// parts", part 2): each by its id, its values those its last request wrote, and its lock, which one request at a time holds,
/// those that ask for it waiting in their order, each for <see cref="LockTimeout"/> at most, then told it timed out; a session
/// that no request has used for <see cref="Timeout"/>, and that none holds or waits for, ended, found so as a request asks
/// for it, and swept once a minute at most as requests come. Kept while the site reloads for a change to its files, the
/// store being the application's; lost as its process ends, and seen by that process alone. Its methods are called from
/// every worker at once, and a wait's end comes on the thread that gave the lock up, or a timer's: one lock guards it all,
/// and what a request is told is told outside it.
/// </summary>
public sealed class WebLingoMemorySessions : IWebLingoSessionStore
{
    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _sessions = new(StringComparer.Ordinal);
    private readonly Func<long> _now;
    private long _swept;

    /// <summary>
    /// A store whose sessions end once unused for <paramref name="timeout"/>, a request waiting for a lock for
    /// <paramref name="lockTimeout"/> at most, the time read, in milliseconds, from <paramref name="now"/>, the system's
    /// monotonic clock by default.
    /// </summary>
    public WebLingoMemorySessions(TimeSpan timeout, TimeSpan lockTimeout, Func<long>? now = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lockTimeout, TimeSpan.Zero);
        (Timeout, LockTimeout, _now) = (timeout, lockTimeout, now ?? (() => Environment.TickCount64));
        _swept = _now();
    }

    /// <summary>How long a session lasts once no request uses it: 20 minutes unless the site's configuration says otherwise.</summary>
    public TimeSpan Timeout { get; }

    public TimeSpan LockTimeout { get; }

    /// <summary>How many sessions it keeps, those ended but not yet swept among them.</summary>
    public int Count
    {
        get
        {
            lock (_gate) return _sessions.Count;
        }
    }

    public WebLingoSessionOpening Open(string id, bool locked, Action<WebLingoSessionOpening> later)
    {
        lock (_gate)
        {
            var now = _now();
            Sweep(now);
            if (!_sessions.TryGetValue(id, out var entry) || Ended(entry, now))
            {
                _sessions.Remove(id);
                return WebLingoSessionOpening.NotFound;
            }
            entry.Used = now;
            if (!locked) return new WebLingoSessionOpening(WebLingoSessionOpened.Found, entry.Values);
            if (!entry.Locked)
            {
                entry.Locked = true;
                return new WebLingoSessionOpening(WebLingoSessionOpened.Found, entry.Values);
            }
            var waiter = new Waiter(later);
            entry.Waiting.Enqueue(waiter);
            waiter.Timer = new Timer(_ => TimedOut(id, waiter), null, LockTimeout, System.Threading.Timeout.InfiniteTimeSpan);
            return WebLingoSessionOpening.Waiting;
        }
    }

    public bool Create(string id)
    {
        lock (_gate)
        {
            var now = _now();
            Sweep(now);
            if (_sessions.TryGetValue(id, out var entry) && !Ended(entry, now)) return false;
            _sessions[id] = new Entry { Values = NoValues, Locked = true, Used = now };
            return true;
        }
    }

    public void Close(string id, IReadOnlyDictionary<string, string>? values)
    {
        Waiter? next;
        IReadOnlyDictionary<string, string> given;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var entry)) return;
            if (values is not null) entry.Values = values;
            entry.Used = _now();
            entry.Locked = false;
            next = Next(entry);
            given = entry.Values;
        }
        next?.Tell(new WebLingoSessionOpening(WebLingoSessionOpened.Found, given));
    }

    public bool Renew(string id, string renewed)
    {
        List<Waiter> gone;
        lock (_gate)
        {
            if (!_sessions.Remove(id, out var entry)) return true;
            if (_sessions.TryGetValue(renewed, out var other) && !Ended(other, _now()))
            {
                _sessions[id] = entry;
                return false;
            }
            gone = Abandon(entry);
            _sessions[renewed] = entry;
        }
        foreach (var waiter in gone) waiter.Tell(WebLingoSessionOpening.NotFound);
        return true;
    }

    public void Remove(string id)
    {
        List<Waiter> gone;
        lock (_gate)
        {
            if (!_sessions.Remove(id, out var entry)) return;
            gone = Abandon(entry);
        }
        foreach (var waiter in gone) waiter.Tell(WebLingoSessionOpening.NotFound);
    }

    /// <summary>A request that waited too long for a session's lock, told so, unless it has been given the lock meanwhile.</summary>
    private void TimedOut(string id, Waiter waiter)
    {
        lock (_gate)
        {
            if (waiter.Told) return;
            waiter.Told = true;
            // Taken out of the queue of the session it waits for, whatever id the session has now.
            foreach (var entry in _sessions.Values)
            {
                if (!entry.Waiting.Contains(waiter)) continue;
                var kept = new Queue<Waiter>();
                foreach (var other in entry.Waiting)
                {
                    if (other != waiter) kept.Enqueue(other);
                }
                entry.Waiting = kept;
                break;
            }
        }
        waiter.Timer?.Dispose();
        waiter.Done(WebLingoSessionOpening.TimedOut);
    }

    /// <summary>Under the lock: the first request that waits for a session's lock given it, or none where none waits.</summary>
    private static Waiter? Next(Entry entry)
    {
        while (entry.Waiting.TryDequeue(out var waiter))
        {
            if (waiter.Told) continue;
            waiter.Told = true;
            entry.Locked = true;
            return waiter;
        }
        return null;
    }

    /// <summary>Under the lock: every request that waits for a session, taken out of its queue, to be told that it has ended.</summary>
    private static List<Waiter> Abandon(Entry entry)
    {
        var gone = new List<Waiter>();
        while (entry.Waiting.TryDequeue(out var waiter))
        {
            if (waiter.Told) continue;
            waiter.Told = true;
            gone.Add(waiter);
        }
        return gone;
    }

    /// <summary>Whether a session has ended: unused for its timeout, held by no request, and waited for by none.</summary>
    private bool Ended(Entry entry, long now) => !entry.Locked && entry.Waiting.Count == 0 && now - entry.Used >= (long)Timeout.TotalMilliseconds;

    /// <summary>Under the lock: the sessions that have ended removed, once a minute at most.</summary>
    private void Sweep(long now)
    {
        if (now - _swept < 60_000) return;
        _swept = now;
        var ended = new List<string>();
        foreach (var (id, entry) in _sessions)
        {
            if (Ended(entry, now)) ended.Add(id);
        }
        foreach (var id in ended) _sessions.Remove(id);
    }

    /// <summary>A session: its values, whether a request holds its lock, those that wait for it, and when a request last used it.</summary>
    private sealed class Entry
    {
        public IReadOnlyDictionary<string, string> Values = NoValues;
        public bool Locked;
        public Queue<Waiter> Waiting = new();
        public long Used;
    }

    /// <summary>A request that waits for a session's lock: what it is told, once, and its timer.</summary>
    private sealed class Waiter(Action<WebLingoSessionOpening> done)
    {
        public Action<WebLingoSessionOpening> Done { get; } = done;
        public bool Told;
        public Timer? Timer;

        /// <summary>Outside the lock: tells the request what its wait came to, its timer stopped.</summary>
        public void Tell(WebLingoSessionOpening opening)
        {
            Timer?.Dispose();
            Done(opening);
        }
    }
}
