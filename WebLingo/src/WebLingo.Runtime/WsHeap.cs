using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// A worker-local heap (specification, sections 4.1 to 4.3): the objects a script makes, which values refer to by
/// handle, never by a .NET reference. It has WebLingo's own tracing collector, nonmoving mark-and-sweep: a
/// collection marks what the roots (the arrays its owner registers, such as the VM's registers and constants) refer
/// to, through every object that refers to others, and frees the rest, whose handles are reused. Its objects are
/// strings, closures (a function and the cells it captures), and cells (one variable that closures share, which
/// has no value until it is initialized).
/// <para>
/// Its size is counted in units: a string's length in UTF-16 code units, a closure's capture count, each plus
/// <see cref="ObjectOverhead"/>. A collection runs when an allocation would pass the next threshold, which is twice
/// what was live after the last collection, and at least <see cref="FirstCollectionAt"/>; an allocation that does
/// not fit in the budget even after a collection is an error (WS3003). These numbers are arbitrary, and are
/// constants that are easy to change.
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
    private readonly List<object?> _objects = [];
    private readonly Stack<int> _free = new();
    private readonly List<WsValue[]> _roots = [];
    private long _nextCollectionAt = FirstCollectionAt;

    public WsHeap(long maxUnits = DefaultMaxUnits) => _maxUnits = maxUnits;

    /// <summary>The units the live and the not yet collected objects take.</summary>
    public long Units { get; private set; }
    public int ObjectCount => _objects.Count - _free.Count;
    public int CollectionCount { get; private set; }

    /// <summary>Registers values the collector must keep, and everything they refer to, until they are removed.</summary>
    public void AddRoots(WsValue[] roots) => _roots.Add(roots);

    /// <summary>Removes the roots added last, which must be <paramref name="roots"/>: a call's frame, as it returns.</summary>
    public void RemoveLastRoots(WsValue[] roots)
    {
        if (_roots.Count == 0 || _roots[^1] != roots) throw new InvalidOperationException("The roots are not the last added.");
        _roots.RemoveAt(_roots.Count - 1);
    }

    public bool TryAllocateString(string text, out WsValue value, out WsRuntimeError? error) =>
        TryAllocate(text, WsValueKind.String, text.Length, out value, out error);

    /// <summary>The text of a string value of this heap.</summary>
    public string GetString(WsValue value) => Get<string>(value, WsValueKind.String);

    /// <summary>A new cell, holding <paramref name="value"/> if <paramref name="hasValue"/>, else nothing yet.</summary>
    public bool TryAllocateCell(WsValue value, bool hasValue, out WsValue cell, out WsRuntimeError? error) =>
        TryAllocate(new Cell { Value = value, HasValue = hasValue }, WsValueKind.Cell, 0, out cell, out error);

    /// <summary>The cell's value; false if it has none yet, which a hoisted function can meet (section 3.5).</summary>
    public bool TryReadCell(WsValue cell, out WsValue value)
    {
        var target = Get<Cell>(cell, WsValueKind.Cell);
        value = target.Value;
        return target.HasValue;
    }

    public void WriteCell(WsValue cell, WsValue value)
    {
        var target = Get<Cell>(cell, WsValueKind.Cell);
        target.Value = value;
        target.HasValue = true;
    }

    /// <summary>A closure of the module's function <paramref name="function"/>, sharing the given cells.</summary>
    public bool TryAllocateClosure(int function, WsValue[] cells, out WsValue closure, out WsRuntimeError? error) =>
        TryAllocate(new Closure(function, cells), WsValueKind.Function, cells.Length, out closure, out error);

    /// <summary>A closure's function index and captured cells.</summary>
    public (int Function, WsValue[] Cells) GetClosure(WsValue value)
    {
        var closure = Get<Closure>(value, WsValueKind.Function);
        return (closure.Function, closure.Cells);
    }

    /// <summary>Marks what the roots refer to, through every object, and frees every other object.</summary>
    public void Collect()
    {
        var marked = new bool[_objects.Count];
        var pending = new Stack<int>();
        foreach (var roots in _roots)
        {
            foreach (var value in roots) Mark(value, marked, pending);
        }
        while (pending.Count > 0)
        {
            switch (_objects[pending.Pop()])
            {
                case Closure closure:
                    foreach (var cell in closure.Cells) Mark(cell, marked, pending);
                    break;
                case Cell cell:
                    Mark(cell.Value, marked, pending);
                    break;
            }
        }
        for (var handle = 0; handle < _objects.Count; handle++)
        {
            if (marked[handle] || _objects[handle] is not { } item) continue;
            Units -= SizeOf(item);
            _objects[handle] = null;
            _free.Push(handle);
        }
        _nextCollectionAt = Math.Max(FirstCollectionAt, Units * 2);
        CollectionCount++;
    }

    private static void Mark(WsValue value, bool[] marked, Stack<int> pending)
    {
        if (value.Kind is not (WsValueKind.String or WsValueKind.Function or WsValueKind.Cell)) return;
        if (marked[value.Handle]) return;
        marked[value.Handle] = true;
        pending.Push(value.Handle);
    }

    private static long SizeOf(object item) => ObjectOverhead + item switch
    {
        string text => text.Length,
        Closure closure => closure.Cells.Length,
        _ => 0
    };

    private bool TryAllocate(object item, WsValueKind kind, long content, out WsValue value, out WsRuntimeError? error)
    {
        var units = content + ObjectOverhead;
        if (Units + units > _nextCollectionAt || Units + units > _maxUnits) Collect();
        if (Units + units > _maxUnits)
        {
            value = WsValue.Null;
            error = new WsRuntimeError(WsDiagnosticCodes.BudgetExceeded,
                item is string text
                    ? $"A string of {text.Length} characters does not fit in the memory this execution may use."
                    : "The program has used all the memory this execution may use.");
            return false;
        }
        int handle;
        if (_free.Count > 0)
        {
            handle = _free.Pop();
            _objects[handle] = item;
        }
        else
        {
            handle = _objects.Count;
            _objects.Add(item);
        }
        Units += units;
        value = WsValue.FromHandle(kind, handle);
        error = null;
        return true;
    }

    private T Get<T>(WsValue value, WsValueKind kind) where T : class
    {
        if (value.Kind != kind) throw new ArgumentException($"The value is not a {kind}.", nameof(value));
        return _objects[value.Handle] as T ?? throw new InvalidOperationException("The object was collected while in use.");
    }

    private sealed class Cell
    {
        public WsValue Value;
        public bool HasValue;
    }

    private sealed record Closure(int Function, WsValue[] Cells);
}
