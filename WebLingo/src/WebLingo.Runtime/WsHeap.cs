using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// A worker-local heap (specification, sections 4.1 to 4.3): the objects a script makes, which values refer to by
/// handle, never by a .NET reference. It has WebLingo's own tracing collector, nonmoving mark-and-sweep: a
/// collection marks what the roots (the arrays its owner registers, such as the VM's registers and constants) refer
/// to, and frees the rest, whose handles are reused. M0.7 has strings only, which refer to nothing, so marking is
/// one pass over the roots.
/// <para>
/// Its size is counted in units: a string's length in UTF-16 code units, plus <see cref="ObjectOverhead"/>. A
/// collection runs when an allocation would pass the next threshold, which is twice what was live after the last
/// collection, and at least <see cref="FirstCollectionAt"/>; an allocation that does not fit in the budget even
/// after a collection is an error (WS3003). These numbers are arbitrary, and are constants that are easy to change.
/// </para>
/// </summary>
public sealed class WsHeap
{
    /// <summary>The default budget: 64 million units, about 128 MB of strings.</summary>
    public const long DefaultMaxUnits = 64L * 1024 * 1024;
    /// <summary>How large the heap may grow before its first collection.</summary>
    public const long FirstCollectionAt = 1024 * 1024;
    /// <summary>What every object counts, beyond its content, so that many small objects are bounded too.</summary>
    public const int ObjectOverhead = 16;

    private readonly long _maxUnits;
    private readonly List<string?> _strings = [];
    private readonly Stack<int> _free = new();
    private readonly List<WsValue[]> _roots = [];
    private long _nextCollectionAt = FirstCollectionAt;

    public WsHeap(long maxUnits = DefaultMaxUnits) => _maxUnits = maxUnits;

    /// <summary>The units the live and the not yet collected objects take.</summary>
    public long Units { get; private set; }
    public int ObjectCount => _strings.Count - _free.Count;
    public int CollectionCount { get; private set; }

    /// <summary>Registers values the collector must keep, and everything they refer to, for as long as the heap lives.</summary>
    public void AddRoots(WsValue[] roots) => _roots.Add(roots);

    public bool TryAllocateString(string text, out WsValue value, out WsRuntimeError? error)
    {
        var units = (long)text.Length + ObjectOverhead;
        if (Units + units > _nextCollectionAt || Units + units > _maxUnits) Collect();
        if (Units + units > _maxUnits)
        {
            value = WsValue.Null;
            error = new WsRuntimeError(WsDiagnosticCodes.BudgetExceeded,
                $"A string of {text.Length} characters does not fit in the memory this execution may use.");
            return false;
        }
        int handle;
        if (_free.Count > 0)
        {
            handle = _free.Pop();
            _strings[handle] = text;
        }
        else
        {
            handle = _strings.Count;
            _strings.Add(text);
        }
        Units += units;
        value = WsValue.FromHandle(WsValueKind.String, handle);
        error = null;
        return true;
    }

    /// <summary>The text of a string value of this heap.</summary>
    public string GetString(WsValue value)
    {
        if (value.Kind != WsValueKind.String) throw new ArgumentException("The value is not a string.", nameof(value));
        return _strings[value.Handle] ?? throw new InvalidOperationException("The string was collected while in use.");
    }

    /// <summary>Marks what the roots refer to, and frees every other object.</summary>
    public void Collect()
    {
        var marked = new bool[_strings.Count];
        foreach (var roots in _roots)
        {
            foreach (var value in roots)
            {
                if (value.Kind == WsValueKind.String) marked[value.Handle] = true;
            }
        }
        for (var handle = 0; handle < _strings.Count; handle++)
        {
            if (marked[handle] || _strings[handle] is not { } text) continue;
            Units -= text.Length + ObjectOverhead;
            _strings[handle] = null;
            _free.Push(handle);
        }
        _nextCollectionAt = Math.Max(FirstCollectionAt, Units * 2);
        CollectionCount++;
    }
}
