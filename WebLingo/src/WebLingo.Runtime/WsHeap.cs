using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// A worker-local heap (specification, sections 4.1 to 4.3): the objects a script makes, which values refer to by
/// handle, never by a .NET reference. It has WebLingo's own tracing collector, nonmoving mark-and-sweep: a
/// collection marks what the roots (the arrays its owner registers, such as the VM's registers and constants) refer
/// to, through every object that refers to others, and frees the rest, whose handles are reused. Its objects are
/// strings, closures (a function and the cells it captures), cells (one variable that closures share, which has no
/// value until it is initialized), arrays, maps and sets (section 3.6), and classes, instances and bound methods
/// (section 3.7).
/// <para>
/// Its size is counted in units: a string's length in UTF-16 code units, a closure's capture count, an array's
/// element count and a map's or a set's entry count (which grow and shrink with them), each plus
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

    /// <summary>A new array of the <paramref name="count"/> values from <paramref name="values"/>[<paramref name="first"/>] on.</summary>
    public bool TryAllocateArray(WsValue[] values, int first, int count, out WsValue array, out WsRuntimeError? error)
    {
        var elements = new List<WsValue>(count);
        for (var i = 0; i < count; i++) elements.Add(values[first + i]);
        return TryAllocate(new ArrayObject(elements), WsValueKind.Array, count, out array, out error);
    }

    /// <summary>An array's elements, which its owner may read and replace, but grows only by <see cref="TryAddToArray"/>.</summary>
    public List<WsValue> GetArray(WsValue value) => Get<ArrayObject>(value, WsValueKind.Array).Elements;

    /// <summary>Adds a value at the end of an array: one more unit, within the budget (WS3003).</summary>
    public bool TryAddToArray(WsValue array, WsValue value, out WsRuntimeError? error)
    {
        var elements = GetArray(array);
        if (!TryReserve(1, out error)) return false;
        elements.Add(value);
        Units++;
        return true;
    }

    /// <summary>Removes an array's last element, which must exist, and gives it.</summary>
    public WsValue RemoveLastOfArray(WsValue array)
    {
        var elements = GetArray(array);
        var last = elements[^1];
        elements.RemoveAt(elements.Count - 1);
        Units--;
        return last;
    }

    /// <summary>
    /// A new map of the <paramref name="count"/> key-value pairs from <paramref name="values"/>[<paramref name="first"/>]
    /// on, a later key replacing an earlier equal one; a value that cannot be a key is WS3001 (section 3.6).
    /// </summary>
    public bool TryAllocateMap(WsValue[] values, int first, int count, out WsValue map, out WsRuntimeError? error)
    {
        var entries = new MapObject();
        for (var i = 0; i < count; i++)
        {
            if (!TryKey(values[first + 2 * i], out var key, out error))
            {
                map = WsValue.Null;
                return false;
            }
            entries.Set(key, values[first + 2 * i], values[first + 2 * i + 1]);
        }
        return TryAllocate(entries, WsValueKind.Map, entries.Count, out map, out error);
    }

    /// <summary>
    /// A class: its name, its constructor's closure (<c>null</c> if it has none), and its methods' closures by name,
    /// each a function whose first parameter is <c>this</c> (section 3.7).
    /// </summary>
    public bool TryAllocateClass(string name, WsValue constructor, Dictionary<string, WsValue> methods, out WsValue value,
        out WsRuntimeError? error) =>
        TryAllocateClass(name, constructor, methods, WsValue.Null, out value, out error);

    /// <summary>
    /// A class that extends <paramref name="baseClass"/> (<c>null</c> for none): its methods are the base's, with its
    /// own in place of those it declares again (section 3.7). A base that is not a class is WS3001.
    /// </summary>
    public bool TryAllocateClass(string name, WsValue constructor, Dictionary<string, WsValue> methods, WsValue baseClass,
        out WsValue value, out WsRuntimeError? error)
    {
        value = WsValue.Null;
        var all = methods;
        if (baseClass.Kind != WsValueKind.Null)
        {
            if (baseClass.Kind != WsValueKind.Class)
            {
                error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                    $"{Capitalized(WsValueFormatter.Describe(baseClass, this))} cannot be extended: only a class can.");
                return false;
            }
            all = new Dictionary<string, WsValue>(Get<ClassObject>(baseClass, WsValueKind.Class).Methods, StringComparer.Ordinal);
            foreach (var (methodName, closure) in methods) all[methodName] = closure;
        }
        // Its disposal: its own dispose method, if it declares one, then its base's disposal (section 3.7).
        var inherited = baseClass.Kind == WsValueKind.Class ? Get<ClassObject>(baseClass, WsValueKind.Class).DisposeChain : [];
        var disposeChain = inherited;
        if (methods.TryGetValue("dispose", out var own))
        {
            disposeChain = new WsValue[inherited.Length + 1];
            disposeChain[0] = own;
            for (var i = 0; i < inherited.Length; i++) disposeChain[i + 1] = inherited[i];
        }
        var made = new ClassObject(name, constructor, all) { Base = baseClass, DisposeChain = disposeChain };
        return TryAllocate(made, WsValueKind.Class, all.Count + 1, out value, out error);
    }

    /// <summary>
    /// A class's disposal: the dispose methods that disposing an instance runs on it, in order, its own first, then its
    /// bases', each once (section 3.7); none if neither it nor a base declares one.
    /// </summary>
    public WsValue[] DisposeChain(WsValue classValue) => Get<ClassObject>(classValue, WsValueKind.Class).DisposeChain;

    /// <summary>Marks an instance disposed, giving whether it was not already (section 3.7).</summary>
    public bool TryMarkDisposed(WsValue instance)
    {
        var target = Get<InstanceObject>(instance, WsValueKind.Instance);
        if (target.Disposed) return false;
        target.Disposed = true;
        return true;
    }

    /// <summary>The class a class extends, or <c>null</c>.</summary>
    public WsValue ClassBase(WsValue value) => Get<ClassObject>(value, WsValueKind.Class).Base;

    public string ClassName(WsValue value) => Get<ClassObject>(value, WsValueKind.Class).Name;

    /// <summary>A class's constructor's closure, or <c>null</c> if it has none.</summary>
    public WsValue ClassConstructor(WsValue value) => Get<ClassObject>(value, WsValueKind.Class).Constructor;

    /// <summary>A method's closure, by its name, from a class.</summary>
    public bool TryGetMethod(WsValue value, string name, out WsValue closure) =>
        Get<ClassObject>(value, WsValueKind.Class).Methods.TryGetValue(name, out closure);

    /// <summary>A new instance of a class, without fields yet.</summary>
    public bool TryAllocateInstance(WsValue classValue, out WsValue instance, out WsRuntimeError? error) =>
        TryAllocate(new InstanceObject(classValue), WsValueKind.Instance, 0, out instance, out error);

    public WsValue ClassOf(WsValue instance) => Get<InstanceObject>(instance, WsValueKind.Instance).Class;

    public bool TryGetField(WsValue instance, string name, out WsValue value) =>
        Get<InstanceObject>(instance, WsValueKind.Instance).TryGet(name, out value);

    /// <summary>Assigns a field, which exists from then on: a new one is one more unit, within the budget (WS3003).</summary>
    public bool TrySetField(WsValue instance, string name, WsValue value, out WsRuntimeError? error)
    {
        var fields = Get<InstanceObject>(instance, WsValueKind.Instance);
        error = null;
        if (fields.TryGet(name, out _))
        {
            fields.Set(name, value);
            return true;
        }
        if (!TryReserve(1, out error)) return false;
        fields.Set(name, value);
        Units++;
        return true;
    }

    public int FieldCount(WsValue instance) => Get<InstanceObject>(instance, WsValueKind.Instance).Count;

    /// <summary>An instance's field at a position from 0 to its count − 1, in no defined order.</summary>
    public (string Name, WsValue Value) FieldAt(WsValue instance, int position) =>
        Get<InstanceObject>(instance, WsValueKind.Instance).At(position);

    /// <summary>
    /// A method bound to an instance: a function that runs the method on it (section 3.7); with
    /// <paramref name="disposes"/>, the instance's <c>dispose</c>, whose call disposes it as <c>x.dispose()</c> does.
    /// </summary>
    public bool TryAllocateBoundMethod(WsValue instance, WsValue closure, out WsValue bound, out WsRuntimeError? error,
        bool disposes = false) =>
        TryAllocate(new BoundMethodObject(instance, closure, disposes), WsValueKind.BoundMethod, 2, out bound, out error);

    /// <summary>Whether a bound method is an instance's <c>dispose</c>, whose call disposes it (section 3.7).</summary>
    public bool IsDisposal(WsValue bound) => Get<BoundMethodObject>(bound, WsValueKind.BoundMethod).Disposes;

    public (WsValue Instance, WsValue Closure) GetBoundMethod(WsValue bound)
    {
        var target = Get<BoundMethodObject>(bound, WsValueKind.BoundMethod);
        return (target.Instance, target.Closure);
    }

    /// <summary>A new set of the <paramref name="count"/> values from <paramref name="values"/>[<paramref name="first"/>] on.</summary>
    public bool TryAllocateSet(WsValue[] values, int first, int count, out WsValue set, out WsRuntimeError? error)
    {
        var entries = new MapObject();
        for (var i = 0; i < count; i++)
        {
            if (!TryKey(values[first + i], out var key, out error, inSet: true))
            {
                set = WsValue.Null;
                return false;
            }
            entries.Set(key, values[first + i], WsValue.Null);
        }
        return TryAllocate(entries, WsValueKind.Set, entries.Count, out set, out error);
    }

    /// <summary>Adds a value to a set, giving whether it was new: a new one is one more unit, within the budget.</summary>
    public bool TryAddToSet(WsValue set, WsValue value, out bool added, out WsRuntimeError? error)
    {
        added = false;
        var entries = Get<MapObject>(set, WsValueKind.Set);
        if (!TryKey(value, out var key, out error, inSet: true)) return false;
        if (entries.TryGet(key, out _)) return true;
        if (!TryReserve(1, out error)) return false;
        entries.Set(key, value, WsValue.Null);
        Units++;
        added = true;
        return true;
    }

    /// <summary>Whether a set holds a value; WS3001 if the value cannot be in a set.</summary>
    public bool TryFindInSet(WsValue set, WsValue value, out bool found, out WsRuntimeError? error)
    {
        found = false;
        if (!TryKey(value, out var key, out error, inSet: true)) return false;
        found = Get<MapObject>(set, WsValueKind.Set).TryGet(key, out _);
        return true;
    }

    /// <summary>Removes a value from a set, giving whether it was there.</summary>
    public bool TryRemoveFromSet(WsValue set, WsValue value, out bool removed, out WsRuntimeError? error)
    {
        removed = false;
        if (!TryKey(value, out var key, out error, inSet: true)) return false;
        removed = Get<MapObject>(set, WsValueKind.Set).Remove(key);
        if (removed) Units--;
        return true;
    }

    /// <summary>How many values the set has.</summary>
    public int SetCount(WsValue set) => Get<MapObject>(set, WsValueKind.Set).Count;

    /// <summary>The set's value at a position from 0 to its count − 1, in no defined order (section 3.6).</summary>
    public WsValue SetValueAt(WsValue set, int position) => Get<MapObject>(set, WsValueKind.Set).At(position).Key;

    /// <summary>The map's value for the key: false if it has none; WS3001 if the value cannot be a key.</summary>
    public bool TryFindInMap(WsValue map, WsValue key, out WsValue value, out bool found, out WsRuntimeError? error)
    {
        value = WsValue.Null;
        found = false;
        if (!TryKey(key, out var mapKey, out error)) return false;
        found = Get<MapObject>(map, WsValueKind.Map).TryGet(mapKey, out value);
        return true;
    }

    /// <summary>Adds or replaces the key's value: a new entry is one more unit, within the budget (WS3003).</summary>
    public bool TrySetInMap(WsValue map, WsValue key, WsValue value, out WsRuntimeError? error)
    {
        var entries = Get<MapObject>(map, WsValueKind.Map);
        if (!TryKey(key, out var mapKey, out error)) return false;
        if (entries.TryGet(mapKey, out _))
        {
            entries.Set(mapKey, key, value);
            return true;
        }
        if (!TryReserve(1, out error)) return false;
        entries.Set(mapKey, key, value);
        Units++;
        return true;
    }

    /// <summary>Removes the key's entry, giving whether there was one.</summary>
    public bool TryRemoveFromMap(WsValue map, WsValue key, out bool removed, out WsRuntimeError? error)
    {
        removed = false;
        if (!TryKey(key, out var mapKey, out error)) return false;
        removed = Get<MapObject>(map, WsValueKind.Map).Remove(mapKey);
        if (removed) Units--;
        return true;
    }

    /// <summary>How many entries the map has.</summary>
    public int MapCount(WsValue map) => Get<MapObject>(map, WsValueKind.Map).Count;

    /// <summary>The map's entry at a position from 0 to its count − 1, in no defined order (section 3.6).</summary>
    public (WsValue Key, WsValue Value) MapEntryAt(WsValue map, int position) => Get<MapObject>(map, WsValueKind.Map).At(position);

    /// <summary>
    /// The key a value is: strings, int64s, Booleans and null may be keys, compared as <c>==</c> compares them; any
    /// other value is WS3001 (section 3.6).
    /// </summary>
    private bool TryKey(WsValue value, out MapKey key, out WsRuntimeError? error, bool inSet = false)
    {
        switch (value.Kind)
        {
            case WsValueKind.String:
                key = new MapKey(WsValueKind.String, 0, GetString(value));
                break;
            case WsValueKind.Int64:
                key = new MapKey(WsValueKind.Int64, value.AsInt64, null);
                break;
            case WsValueKind.Boolean:
                key = new MapKey(WsValueKind.Boolean, value.AsBoolean ? 1 : 0, null);
                break;
            case WsValueKind.Null:
                key = new MapKey(WsValueKind.Null, 0, null);
                break;
            default:
                key = default;
                var what = Capitalized(WsValueFormatter.Describe(value));
                error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation, inSet
                    ? $"{what} cannot be in a set: a set's values are strings, integers, Booleans and null."
                    : $"{what} cannot be a key: keys are strings, integers, Booleans and null.");
                return false;
        }
        error = null;
        return true;
    }

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

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
                case ArrayObject array:
                    foreach (var element in array.Elements) Mark(element, marked, pending);
                    break;
                case ClassObject classObject:
                    Mark(classObject.Base, marked, pending);
                    Mark(classObject.Constructor, marked, pending);
                    foreach (var method in classObject.Methods.Values) Mark(method, marked, pending);
                    foreach (var method in classObject.DisposeChain) Mark(method, marked, pending);
                    break;
                case InstanceObject instance:
                    Mark(instance.Class, marked, pending);
                    for (var i = 0; i < instance.Count; i++) Mark(instance.At(i).Value, marked, pending);
                    break;
                case BoundMethodObject bound:
                    Mark(bound.Instance, marked, pending);
                    Mark(bound.Closure, marked, pending);
                    break;
                case MapObject map: // A set's too, whose values are its keys.
                    for (var i = 0; i < map.Count; i++)
                    {
                        var (key, value) = map.At(i);
                        Mark(key, marked, pending);
                        Mark(value, marked, pending);
                    }
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
        if (value.Kind is not (WsValueKind.String or WsValueKind.Function or WsValueKind.Cell or WsValueKind.Array
            or WsValueKind.Map or WsValueKind.Set or WsValueKind.Class or WsValueKind.Instance or WsValueKind.BoundMethod))
        {
            return;
        }
        if (marked[value.Handle]) return;
        marked[value.Handle] = true;
        pending.Push(value.Handle);
    }

    private static long SizeOf(object item) => ObjectOverhead + item switch
    {
        string text => text.Length,
        Closure closure => closure.Cells.Length,
        ArrayObject array => array.Elements.Count,
        MapObject map => map.Count,
        ClassObject classObject => classObject.Methods.Count + 1,
        InstanceObject instance => instance.Count,
        BoundMethodObject => 2,
        _ => 0
    };

    /// <summary>Makes room for <paramref name="units"/> more, collecting first if the next collection is due.</summary>
    private bool TryReserve(long units, out WsRuntimeError? error)
    {
        if (Units + units > _nextCollectionAt || Units + units > _maxUnits) Collect();
        if (Units + units > _maxUnits)
        {
            error = new WsRuntimeError(WsDiagnosticCodes.BudgetExceeded, "The program has used all the memory this execution may use.");
            return false;
        }
        error = null;
        return true;
    }

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

    private sealed record ArrayObject(List<WsValue> Elements);

    private sealed record ClassObject(string Name, WsValue Constructor, Dictionary<string, WsValue> Methods)
    {
        public WsValue Base { get; init; }
        public WsValue[] DisposeChain { get; init; } = [];
    }

    private sealed record BoundMethodObject(WsValue Instance, WsValue Closure, bool Disposes);

    /// <summary>An instance: its class, and its fields, by name, in no defined order.</summary>
    private sealed class InstanceObject(WsValue classValue)
    {
        private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);
        private readonly List<(string Name, WsValue Value)> _fields = [];

        public WsValue Class { get; } = classValue;
        public int Count => _fields.Count;
        /// <summary>Whether its disposal has begun, which runs at most once (section 3.7).</summary>
        public bool Disposed { get; set; }

        public bool TryGet(string name, out WsValue value)
        {
            if (_positions.TryGetValue(name, out var position))
            {
                value = _fields[position].Value;
                return true;
            }
            value = WsValue.Null;
            return false;
        }

        public void Set(string name, WsValue value)
        {
            if (_positions.TryGetValue(name, out var position))
            {
                _fields[position] = (name, value);
                return;
            }
            _positions.Add(name, _fields.Count);
            _fields.Add((name, value));
        }

        public (string Name, WsValue Value) At(int position) => _fields[position];
    }

    /// <summary>A map's key as the map compares it: a string by its text, anything else by its kind and payload.</summary>
    private readonly record struct MapKey(WsValueKind Kind, long Bits, string? Text);

    /// <summary>
    /// A hash table, its entries kept in a list that a removal fills from its end, so that an entry is found by its key
    /// and visited by its position, in no defined order (section 3.6).
    /// </summary>
    private sealed class MapObject
    {
        private readonly Dictionary<MapKey, int> _positions = [];
        private readonly List<(MapKey Key, WsValue KeyValue, WsValue Value)> _entries = [];

        public int Count => _entries.Count;

        public bool TryGet(MapKey key, out WsValue value)
        {
            if (_positions.TryGetValue(key, out var position))
            {
                value = _entries[position].Value;
                return true;
            }
            value = WsValue.Null;
            return false;
        }

        public void Set(MapKey key, WsValue keyValue, WsValue value)
        {
            if (_positions.TryGetValue(key, out var position))
            {
                _entries[position] = (key, _entries[position].KeyValue, value);
                return;
            }
            _positions.Add(key, _entries.Count);
            _entries.Add((key, keyValue, value));
        }

        public bool Remove(MapKey key)
        {
            if (!_positions.Remove(key, out var position)) return false;
            var last = _entries.Count - 1;
            if (position != last)
            {
                _entries[position] = _entries[last];
                _positions[_entries[position].Key] = position;
            }
            _entries.RemoveAt(last);
            return true;
        }

        public (WsValue Key, WsValue Value) At(int position) => (_entries[position].KeyValue, _entries[position].Value);
    }
}
