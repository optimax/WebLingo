using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// The memory a site's requests share on .NET (specification, section 6.12, "Memory a site's requests share"): a
/// dictionary of the values its owners copied, under one lock, each operation one step; at most <see cref="Most"/> bytes,
/// its values' sizes, its keys', two bytes a character, and <see cref="EntrySize"/> for each entry, 64 MB by default
/// (A3 (a) of "Memory a Site's Requests Share"). A value whose time has passed is removed where it is next asked for, and
/// all of them where the store would otherwise be full. One for each site, made where its host starts and kept across
/// its reloads, or for a program and its workers under <c>weblingo</c>. Adapter-only (section 7.14).
/// </summary>
public sealed class DotNetSharedStore : IWebLingoSharedStore
{
    /// <summary>A site's most by default, 64 MB.</summary>
    public const long DefaultMost = 64L * 1024 * 1024;

    /// <summary>What each entry costs beside its key and its value, its dictionary's share.</summary>
    public const long EntrySize = 64;

    /// <summary>What an integer kept as itself costs.</summary>
    public const long IntegerSize = 8;

    private sealed record Entry(object Value, long Size, long Expires);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<long> _now;
    private long _size;

    /// <param name="most">The most it holds, in bytes; null for no most.</param>
    /// <param name="now">Its clock, in milliseconds, for the tests; the machine's monotonic clock by default.</param>
    public DotNetSharedStore(long? most, Func<long>? now = null) => (Most, _now) = (most, now ?? (() => Environment.TickCount64));

    public long? Most { get; }

    /// <summary>The bytes it holds now, its entries' sizes.</summary>
    public long Size
    {
        get
        {
            lock (_gate) return _size;
        }
    }

    public object? Get(string key)
    {
        lock (_gate) return Live(key)?.Value;
    }

    public bool TrySet(string key, object value, long size, double? seconds)
    {
        var expires = seconds is { } s ? _now() + (long)Math.Min(Math.Ceiling(s * 1000), long.MaxValue / 2) : long.MaxValue;
        var entry = new Entry(value, Cost(key, size), expires);
        lock (_gate)
        {
            // What it replaces, unless its time has passed, which no sweep then removes before it is counted.
            var old = Live(key)?.Size ?? 0;
            if (!Fits(entry.Size - old)) return false;
            _size += entry.Size - old;
            _entries[key] = entry;
            return true;
        }
    }

    public bool Remove(string key)
    {
        lock (_gate) return Live(key) is not null && Drop(key);
    }

    public object? Take(string key)
    {
        lock (_gate)
        {
            if (Live(key) is not { } entry) return null;
            Drop(key);
            return entry.Value;
        }
    }

    public WebLingoSharedAdded Add(string key, long amount, out long total)
    {
        total = 0;
        lock (_gate)
        {
            if (Live(key) is { } entry)
            {
                if (entry.Value is not long held) return WebLingoSharedAdded.NotAnInteger;
                try
                {
                    total = checked(held + amount);
                }
                catch (OverflowException)
                {
                    return WebLingoSharedAdded.Overflow;
                }
                _entries[key] = entry with { Value = total };
                return WebLingoSharedAdded.Added;
            }
            var made = new Entry(amount, Cost(key, IntegerSize), long.MaxValue);
            if (!Fits(made.Size)) return WebLingoSharedAdded.Full;
            _entries[key] = made;
            _size += made.Size;
            total = amount;
            return WebLingoSharedAdded.Added;
        }
    }

    private static long Cost(string key, long size) => EntrySize + 2L * key.Length + size;

    /// <summary>Under the lock: the entry kept under <paramref name="key"/>, removed where its time has passed.</summary>
    private Entry? Live(string key)
    {
        if (!_entries.TryGetValue(key, out var entry)) return null;
        if (entry.Expires > _now()) return entry;
        Drop(key);
        return null;
    }

    private bool Drop(string key)
    {
        if (!_entries.Remove(key, out var entry)) return false;
        _size -= entry.Size;
        return true;
    }

    /// <summary>Under the lock: whether <paramref name="more"/> bytes fit, the values whose time has passed removed first where they would not.</summary>
    private bool Fits(long more)
    {
        if (Most is not { } most || _size + more <= most) return true;
        var now = _now();
        foreach (var (key, entry) in _entries.ToArray())
        {
            if (entry.Expires <= now) Drop(key);
        }
        return _size + more <= most;
    }
}
