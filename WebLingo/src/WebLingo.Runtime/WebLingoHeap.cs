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
/// not fit in the budget even after a collection is an error (WL3003). These numbers are arbitrary, and are
/// constants that are easy to change.
/// </para>
/// </summary>
public sealed class WebLingoHeap
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
    private readonly List<WebLingoValue[]> _roots = [];
    private long _nextCollectionAt = FirstCollectionAt;

    public WebLingoHeap(long maxUnits = DefaultMaxUnits) => _maxUnits = maxUnits;

    /// <summary>The units the live and the not yet collected objects take.</summary>
    public long Units { get; private set; }
    public int ObjectCount => _objects.Count - _free.Count;
    public int CollectionCount { get; private set; }

    /// <summary>Registers values the collector must keep, and everything they refer to, until they are removed.</summary>
    public void AddRoots(WebLingoValue[] roots) => _roots.Add(roots);

    /// <summary>Roots that grow and shrink, as a text being built holds them (<see cref="WebLingoTextBuilder"/>), last added first removed.</summary>
    private readonly List<List<WebLingoValue>> _listRoots = [];

    public void AddRoots(List<WebLingoValue> roots) => _listRoots.Add(roots);

    public void RemoveLastRoots(List<WebLingoValue> roots)
    {
        if (_listRoots.Count == 0 || _listRoots[^1] != roots) throw new InvalidOperationException("The roots are not the last added.");
        _listRoots.RemoveAt(_listRoots.Count - 1);
    }

    /// <summary>
    /// <c>Object</c>'s <c>toString()</c>, once the program has made <c>Object</c>: what every value that is not an
    /// instance has as its <c>toString</c>, and what a class that does not override it has (section 3.7).
    /// </summary>
    public WebLingoValue DefaultToString { get; set; }

    /// <summary>The execution's tasks, which its VM runs, and which <c>delay</c> adds a timer to (section 3.8).</summary>
    public WebLingoScheduler? Scheduler { get; set; }

    /// <summary>A new task, pending (section 3.8).</summary>
    public bool TryAllocateTask(out WebLingoValue task, out WebLingoRuntimeError? error) =>
        TryAllocate(new WebLingoTask(), WebLingoValueKind.Task, 2, out task, out error);

    public WebLingoTask GetTask(WebLingoValue task) => Get<WebLingoTask>(task, WebLingoValueKind.Task);

    /// <summary>The class <c>Object</c>, once the program has made it, whose methods every value has (section 3.7).</summary>
    public WebLingoValue ObjectClass { get; set; }

    /// <summary>
    /// Seals a class, a kind of value's (section 3.7, "Classes of values"): no class extends it, and <c>new</c> makes
    /// none of its values.
    /// </summary>
    public void SealClass(WebLingoValue classValue) => Get<ClassObject>(classValue, WebLingoValueKind.Class).Sealed = true;

    /// <summary>
    /// Marks a class as a kind of collection's, <c>Array</c>'s, <c>Map</c>'s or <c>Set</c>'s (section 3.7): <c>new</c>
    /// makes an empty one, and a class that extends it makes collections of that kind, of its own class, with fields.
    /// </summary>
    /// <summary>Whether an <c>extend</c> has run, so that a kind of value's class may have methods of its own (section 3.7).</summary>
    public bool Extended { get; private set; }

    /// <summary>
    /// The classes of the kinds of values the program has made, by <c>WebLingoValueClass</c>, which its VM keeps as roots; a
    /// value's methods are its kind's class's, once an <c>extend</c> has run (section 3.7).
    /// </summary>
    public WebLingoValue[]? ValueClasses { get; set; }

    /// <summary>
    /// How many extensions have run: a class whose methods were gathered from its bases before the last gathers them
    /// again when they are next looked up (section 3.7).
    /// </summary>
    private int _classVersion;

    /// <summary>
    /// <c>extend</c> (section 3.7): a class given methods and static members, each replacing one of its own of that name;
    /// its instances and subclasses, made before or after, have the methods. Anything but a class is WL3001.
    /// </summary>
    public bool TryExtendClass(WebLingoValue classValue, Dictionary<string, WebLingoValue> methods, Dictionary<string, WebLingoValue> staticMethods,
        IReadOnlyList<string> staticFields, out WebLingoRuntimeError? error)
    {
        if (classValue.Kind != WebLingoValueKind.Class)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"{Capitalized(WebLingoValueFormatter.Describe(classValue, this))} cannot be extended: only a class can.");
            return false;
        }
        var target = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        var before = Size(target);
        foreach (var (name, closure) in methods) target.OwnMethods[name] = closure;
        foreach (var (name, closure) in staticMethods)
        {
            target.StaticFields.Remove(name);
            target.StaticMethods[name] = closure;
        }
        foreach (var name in staticFields)
        {
            target.StaticMethods.Remove(name);
            target.StaticFields.TryAdd(name, WebLingoValue.Null);
        }
        _classVersion++;
        Extended = true;
        var grown = Size(target) - before;
        if (!TryReserve(grown, out error)) return false;
        Units += grown;
        return true;
    }

    /// <summary>A class, its methods gathered anew from its bases if an extension has run since they last were.</summary>
    private ClassObject Current(ClassObject target) => target.Version == _classVersion ? target : Gather(target);

    /// <summary>A class's methods, its base's with its own in their place, and its disposal (section 3.7).</summary>
    private ClassObject Gather(ClassObject target)
    {
        var methods = target.Methods;
        methods.Clear();
        WebLingoValue[] inherited = [];
        if (target.Base.Kind == WebLingoValueKind.Class)
        {
            var baseObject = Current(Get<ClassObject>(target.Base, WebLingoValueKind.Class));
            foreach (var (name, closure) in baseObject.Methods) methods[name] = closure;
            inherited = baseObject.DisposeChain;
        }
        foreach (var (name, closure) in target.OwnMethods) methods[name] = closure;
        // Its properties' getters and setters, by the property's name, so that a member's read or assignment finds one
        // without making a name (section 3.7).
        target.Getters = target.Setters = null;
        foreach (var (name, closure) in methods)
        {
            if (name.StartsWith("get:", StringComparison.Ordinal))
            {
                (target.Getters ??= new Dictionary<string, WebLingoValue>(StringComparer.Ordinal))[name[4..]] = closure;
            }
            else if (name.StartsWith("set:", StringComparison.Ordinal))
            {
                (target.Setters ??= new Dictionary<string, WebLingoValue>(StringComparer.Ordinal))[name[4..]] = closure;
            }
        }
        // Its disposal: its own dispose method, if it has one, then its base's disposal (section 3.7).
        var disposeChain = inherited;
        if (target.OwnMethods.TryGetValue("dispose", out var own))
        {
            disposeChain = new WebLingoValue[inherited.Length + 1];
            disposeChain[0] = own;
            for (var i = 0; i < inherited.Length; i++) disposeChain[i + 1] = inherited[i];
        }
        target.DisposeChain = disposeChain;
        target.Version = _classVersion;
        return target;
    }

    /// <summary>
    /// Marks a class as a kind of value's, <c>Boolean</c>'s, <c>Integer</c>'s, <c>Number</c>'s or <c>String</c>'s
    /// (section 3.7): <c>new</c> on it makes a plain value, and a class that extends it makes instances that hold one.
    /// </summary>
    public void MarkValueClass(WebLingoValue classValue, WebLingoValueKind kind)
    {
        var target = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        target.Holds = kind;
        target.IsValueClass = true;
    }

    /// <summary>
    /// Whether <c>new</c> on a class with so many arguments makes a plain value: on a kind of value's class itself,
    /// <c>new Integer(42)</c>, but for <c>new Function()</c>, an instance of <c>Function</c> holding a function that does
    /// nothing (section 3.7).
    /// </summary>
    public bool MakesPlainValue(WebLingoValue classValue, int count) =>
        Get<ClassObject>(classValue, WebLingoValueKind.Class) is { IsValueClass: true } target
        && !(target.Holds == WebLingoValueKind.Function && count == 0);

    /// <summary>Whether a value can be called: a function, a built-in, a bound method, or an instance holding one.</summary>
    public bool IsCallable(WebLingoValue value) =>
        Held(value).Kind is WebLingoValueKind.Function or WebLingoValueKind.Builtin or WebLingoValueKind.BoundMethod;

    /// <summary>Whether a value is an instance of a class extending a kind of value, which holds one (section 3.7).</summary>
    public bool IsHolding(WebLingoValue value) => value.Kind == WebLingoValueKind.Instance && Get<InstanceObject>(value, WebLingoValueKind.Instance).Holds;

    /// <summary>The value an instance of a class extending a kind of value holds; any other value itself.</summary>
    public WebLingoValue Held(WebLingoValue value) =>
        value.Kind == WebLingoValueKind.Instance && Get<InstanceObject>(value, WebLingoValueKind.Instance) is { Holds: true } holding
            ? holding.Value
            : value;

    /// <summary>
    /// <c>hold(this, value)</c>, the built-in classes' constructors': the instance holds the value, which must be of its
    /// class's kind, a number given to a class extending <c>Number</c> as a binary64 (WL3001 otherwise).
    /// </summary>
    public bool TrySetHeld(WebLingoValue instance, WebLingoValue value, out WebLingoRuntimeError? error)
    {
        error = null;
        if (instance.Kind == WebLingoValueKind.Class && value.Kind == WebLingoValueKind.Array) return TryShapeClass(instance, value, out error);
        if (!IsHolding(instance))
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"{Capitalized(WebLingoValueFormatter.Describe(instance, this))} cannot hold a value.");
            return false;
        }
        var target = Get<InstanceObject>(instance, WebLingoValueKind.Instance);
        var classObject = Get<ClassObject>(target.Class, WebLingoValueKind.Class);
        if (!TryOfKind(classObject.Holds, classObject.Name, Held(value), out var held, out error)) return false;
        target.Value = held;
        return true;
    }

    /// <summary>
    /// <c>new Integer(42)</c>, <c>new Integer()</c> and the like: a plain value, the one given, or its kind's first (0,
    /// 0.0, <c>""</c>, <c>false</c>), not an instance (section 3.7).
    /// </summary>
    public bool TryMakeValue(WebLingoValue classValue, WebLingoValue[] values, int first, int count, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        // The result may go where the argument is, so the argument is read before the result is written.
        var classObject = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        if (classObject.MakesClasses) return TryNewClass(values, first, count, out result, out error);
        if (count == 1) return TryOfKind(classObject.Holds, classObject.Name, Held(values[first]), out result, out error);
        result = WebLingoValue.Null;
        if (count > 1)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.WrongArgumentCount,
                $"'{classObject.Name}' takes 0 to 1 arguments, not {count}.");
            return false;
        }
        error = null;
        switch (classObject.Holds)
        {
            case WebLingoValueKind.Boolean:
                result = WebLingoValue.FromBoolean(false);
                return true;
            case WebLingoValueKind.Int64:
                result = WebLingoValue.FromInt64(0);
                return true;
            case WebLingoValueKind.Binary64:
                result = WebLingoValue.FromBinary64(0.0);
                return true;
            default:
                return TryAllocateString("", out result, out error);
        }
    }

    /// <summary>A value as a class holding the kind given holds it: of that kind, an int64 a binary64 for <c>Number</c>.</summary>
    private bool TryOfKind(WebLingoValueKind kind, string className, WebLingoValue value, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        error = null;
        result = kind == WebLingoValueKind.Binary64 && value.Kind == WebLingoValueKind.Int64 ? WebLingoValue.FromBinary64(value.AsInt64) : value;
        if (result.Kind == kind) return true;
        if (value.Kind == WebLingoValueKind.String && kind is WebLingoValueKind.Int64 or WebLingoValueKind.Binary64 or WebLingoValueKind.Boolean)
        {
            // A string read as a literal of the class's kind, as its parse reads it (section 3.7).
            return WebLingoConversions.TryParse(kind, GetString(value), out result, out error);
        }
        if (kind == WebLingoValueKind.Function && result.Kind is WebLingoValueKind.Builtin or WebLingoValueKind.BoundMethod) return true;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
            $"The class {className} holds {KindName(kind)}, not {WebLingoValueFormatter.Describe(value, this)}.");
        result = WebLingoValue.Null;
        return false;
    }

    private static string KindName(WebLingoValueKind kind) => kind switch
    {
        WebLingoValueKind.Boolean => "a Boolean",
        WebLingoValueKind.Int64 => "an integer",
        WebLingoValueKind.Binary64 => "a number",
        WebLingoValueKind.Function => "a function",
        _ => "a string"
    };

    /// <summary>
    /// Arithmetic's result on an instance holding a value (section 3.7): a new instance of its class, holding the
    /// result, its constructor not run, with its fields, copied.
    /// </summary>
    public bool TryHoldAnew(WebLingoValue source, WebLingoValue value, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        var from = Get<InstanceObject>(source, WebLingoValueKind.Instance);
        var made = new InstanceObject(from.Class) { Holds = true, Value = value };
        for (var i = 0; i < from.Count; i++)
        {
            var (name, field) = from.At(i);
            made.Set(name, field);
        }
        return TryAllocate(made, WebLingoValueKind.Instance, made.Count, out result, out error);
    }

    /// <summary>The kind of value a class extending a kind of value holds, or null's for any other class.</summary>
    public WebLingoValueKind HeldKind(WebLingoValue instance) =>
        Get<ClassObject>(Get<InstanceObject>(instance, WebLingoValueKind.Instance).Class, WebLingoValueKind.Class).Holds;

    /// <summary>
    /// The constructor <c>new</c> runs (section 3.7): a class that declares none passes its arguments on to the nearest
    /// base that declares one, as if its constructor called <c>super(…)</c> with them; with no such base, its own, which
    /// takes none.
    /// </summary>
    public WebLingoValue ConstructorFor(WebLingoValue classValue)
    {
        var own = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        for (var target = own; target.DefaultConstructor && target.Base.Kind == WebLingoValueKind.Class;)
        {
            target = Get<ClassObject>(target.Base, WebLingoValueKind.Class);
            if (!target.DefaultConstructor) return target.Constructor;
        }
        return own.Constructor;
    }

    /// <summary>
    /// A module's instance (section 3.9): its name, and its exports, each a cell of its top level's, read as it is when
    /// read, from the <paramref name="count"/> values from <paramref name="first"/> on.
    /// </summary>
    public bool TryAllocateModule(string name, IReadOnlyList<string> exports, WebLingoValue[] values, int first, int count,
        out WebLingoValue module, out WebLingoRuntimeError? error)
    {
        var made = new ModuleObject(name);
        for (var i = 0; i < count; i++) made.Exports[exports[i]] = values[first + i];
        return TryAllocate(made, WebLingoValueKind.Module, count, out module, out error);
    }

    public string ModuleName(WebLingoValue module) => Get<ModuleObject>(module, WebLingoValueKind.Module).Name;

    /// <summary>The cell an export of a module is, by its name.</summary>
    public bool TryGetExport(WebLingoValue module, string name, out WebLingoValue cell) =>
        Get<ModuleObject>(module, WebLingoValueKind.Module).Exports.TryGetValue(name, out cell);

    public void MarkCollectionClass(WebLingoValue classValue, WebLingoValueKind kind)
    {
        var target = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        target.Makes = kind;
        target.IsCollectionClass = true;
    }

    /// <summary>
    /// The class of an instance, or of a collection a class extending <c>Array</c>, <c>Map</c> or <c>Set</c> made;
    /// <c>null</c> for any other value (section 3.7).
    /// </summary>
    public WebLingoValue ClassOfValue(WebLingoValue value) => value.Kind switch
    {
        WebLingoValueKind.Instance => Get<InstanceObject>(value, WebLingoValueKind.Instance).Class,
        // A class a metaclass made is its instance; any other class is Class's, which the caller finds (section 3.7).
        WebLingoValueKind.Class => Get<ClassObject>(value, WebLingoValueKind.Class).Metaclass,
        WebLingoValueKind.Array when value.HasClass => Get<ArrayObject>(value, WebLingoValueKind.Array).Instance?.Class ?? WebLingoValue.Null,
        WebLingoValueKind.Map or WebLingoValueKind.Set when value.HasClass =>
            Get<MapObject>(value, value.Kind).Instance?.Class ?? WebLingoValue.Null,
        _ => WebLingoValue.Null
    };

    /// <summary>Declares a static field of a class, a member it does not have, as assigning one does (section 3.7).</summary>
    public bool TryAddStatic(WebLingoValue classValue, string name, WebLingoValue value, out WebLingoRuntimeError? error)
    {
        if (!TryReserve(1, out error)) return false;
        Get<ClassObject>(classValue, WebLingoValueKind.Class).StaticFields[name] = value;
        Units++;
        return true;
    }

    /// <summary>The fields of an instance, or of a collection of a class that extends <c>Array</c>, <c>Map</c> or <c>Set</c>.</summary>
    private InstanceObject Fields(WebLingoValue value) => value.Kind switch
    {
        WebLingoValueKind.Array => Get<ArrayObject>(value, WebLingoValueKind.Array).Instance,
        WebLingoValueKind.Map or WebLingoValueKind.Set => Get<MapObject>(value, value.Kind).Instance,
        _ => Get<InstanceObject>(value, WebLingoValueKind.Instance)
    } ?? throw new ArgumentException("The collection has no class, and no fields.", nameof(value));

    /// <summary>Removes the roots added last, which must be <paramref name="roots"/>: a call's frame, as it returns.</summary>
    public void RemoveLastRoots(WebLingoValue[] roots)
    {
        if (_roots.Count == 0 || _roots[^1] != roots) throw new InvalidOperationException("The roots are not the last added.");
        _roots.RemoveAt(_roots.Count - 1);
    }

    public bool TryAllocateString(string text, out WebLingoValue value, out WebLingoRuntimeError? error) =>
        TryAllocate(text, WebLingoValueKind.String, text.Length, out value, out error);

    /// <summary>The text of a string value of this heap.</summary>
    public string GetString(WebLingoValue value) => Get<string>(value, WebLingoValueKind.String);

    /// <summary>A new cell, holding <paramref name="value"/> if <paramref name="hasValue"/>, else nothing yet.</summary>
    public bool TryAllocateCell(WebLingoValue value, bool hasValue, out WebLingoValue cell, out WebLingoRuntimeError? error) =>
        TryAllocate(new Cell { Value = value, HasValue = hasValue }, WebLingoValueKind.Cell, 0, out cell, out error);

    /// <summary>The cell's value; false if it has none yet, which a hoisted function can meet (section 3.5).</summary>
    public bool TryReadCell(WebLingoValue cell, out WebLingoValue value)
    {
        var target = Get<Cell>(cell, WebLingoValueKind.Cell);
        value = target.Value;
        return target.HasValue;
    }

    public void WriteCell(WebLingoValue cell, WebLingoValue value)
    {
        var target = Get<Cell>(cell, WebLingoValueKind.Cell);
        target.Value = value;
        target.HasValue = true;
    }

    /// <summary>A closure of the module's function <paramref name="function"/>, sharing the given cells.</summary>
    public bool TryAllocateClosure(int function, WebLingoValue[] cells, out WebLingoValue closure, out WebLingoRuntimeError? error) =>
        TryAllocate(new Closure(function, cells), WebLingoValueKind.Function, cells.Length, out closure, out error);

    /// <summary>A closure's function index and captured cells.</summary>
    public (int Function, WebLingoValue[] Cells) GetClosure(WebLingoValue value)
    {
        var closure = Get<Closure>(value, WebLingoValueKind.Function);
        return (closure.Function, closure.Cells);
    }

    /// <summary>A new array of the <paramref name="count"/> values from <paramref name="values"/>[<paramref name="first"/>] on.</summary>
    public bool TryAllocateArray(WebLingoValue[] values, int first, int count, out WebLingoValue array, out WebLingoRuntimeError? error)
    {
        var elements = new List<WebLingoValue>(count);
        for (var i = 0; i < count; i++) elements.Add(values[first + i]);
        return TryAllocate(new ArrayObject(elements), WebLingoValueKind.Array, count, out array, out error);
    }

    /// <summary>An array's elements, which its owner may read and replace, but grows only by <see cref="TryAddToArray"/>.</summary>
    public List<WebLingoValue> GetArray(WebLingoValue value) => Get<ArrayObject>(value, WebLingoValueKind.Array).Elements;

    /// <summary>Adds a value at the end of an array: one more unit, within the budget (WL3003).</summary>
    public bool TryAddToArray(WebLingoValue array, WebLingoValue value, out WebLingoRuntimeError? error)
    {
        var elements = GetArray(array);
        if (!TryReserve(1, out error)) return false;
        elements.Add(value);
        Units++;
        return true;
    }

    /// <summary>Removes an array's last element, which must exist, and gives it.</summary>
    public WebLingoValue RemoveLastOfArray(WebLingoValue array)
    {
        var elements = GetArray(array);
        var last = elements[^1];
        elements.RemoveAt(elements.Count - 1);
        Units--;
        return last;
    }

    /// <summary>
    /// A new map of the <paramref name="count"/> key-value pairs from <paramref name="values"/>[<paramref name="first"/>]
    /// on, a later key replacing an earlier equal one; a value that cannot be a key is WL3001 (section 3.6).
    /// </summary>
    public bool TryAllocateMap(WebLingoValue[] values, int first, int count, out WebLingoValue map, out WebLingoRuntimeError? error)
    {
        var entries = new MapObject();
        for (var i = 0; i < count; i++)
        {
            if (!TryKey(values[first + 2 * i], out var key, out error))
            {
                map = WebLingoValue.Null;
                return false;
            }
            entries.Set(key, values[first + 2 * i], values[first + 2 * i + 1]);
        }
        return TryAllocate(entries, WebLingoValueKind.Map, entries.Count, out map, out error);
    }

    /// <summary>
    /// A class: its name, its constructor's closure (<c>null</c> if it has none), and its methods' closures by name,
    /// each a function whose first parameter is <c>this</c> (section 3.7).
    /// </summary>
    public bool TryAllocateClass(string name, WebLingoValue constructor, Dictionary<string, WebLingoValue> methods, out WebLingoValue value,
        out WebLingoRuntimeError? error) =>
        TryAllocateClass(name, constructor, methods, WebLingoValue.Null, out value, out error);

    /// <summary>
    /// A class that extends <paramref name="baseClass"/> (<c>null</c> for none): its methods are the base's, with its
    /// own in place of those it declares again (section 3.7). A base that is not a class is WL3001. Its static methods'
    /// closures, by name, and its static fields, <c>null</c> until they are set, are its own.
    /// </summary>
    public bool TryAllocateClass(string name, WebLingoValue constructor, Dictionary<string, WebLingoValue> methods, WebLingoValue baseClass,
        out WebLingoValue value, out WebLingoRuntimeError? error, Dictionary<string, WebLingoValue>? staticMethods = null,
        IReadOnlyList<string>? staticFields = null, bool defaultConstructor = false)
    {
        value = WebLingoValue.Null;
        var makes = WebLingoValueKind.Instance;
        var holds = WebLingoValueKind.Null;
        if (baseClass.Kind != WebLingoValueKind.Null)
        {
            if (baseClass.Kind != WebLingoValueKind.Class)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"{Capitalized(WebLingoValueFormatter.Describe(baseClass, this))} cannot be extended: only a class can.");
                return false;
            }
            var baseObject = Get<ClassObject>(baseClass, WebLingoValueKind.Class);
            if (baseObject.Sealed)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"The class {baseObject.Name} cannot be extended.");
                return false;
            }
            makes = baseObject.Makes;
            holds = baseObject.Holds;
        }
        var fields = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
        if (staticFields is not null)
        {
            foreach (var field in staticFields) fields[field] = WebLingoValue.Null;
        }
        var made = new ClassObject(name, constructor, new Dictionary<string, WebLingoValue>(StringComparer.Ordinal))
        {
            Base = baseClass,
            OwnMethods = new Dictionary<string, WebLingoValue>(methods, StringComparer.Ordinal),
            Makes = makes,
            Holds = holds,
            DefaultConstructor = defaultConstructor,
            StaticMethods = staticMethods ?? new Dictionary<string, WebLingoValue>(StringComparer.Ordinal),
            StaticFields = fields
        };
        Gather(made);
        return TryAllocate(made, WebLingoValueKind.Class, Size(made), out value, out error);
    }

    /// <summary>A class's static member, its own, or its base's if it has none of that name (section 3.7).</summary>
    public bool TryGetStatic(WebLingoValue classValue, string name, out WebLingoValue value)
    {
        for (var current = classValue; current.Kind == WebLingoValueKind.Class;)
        {
            var target = Get<ClassObject>(current, WebLingoValueKind.Class);
            if (target.StaticFields.TryGetValue(name, out value) || target.StaticMethods.TryGetValue(name, out value)) return true;
            current = target.Base;
        }
        value = WebLingoValue.Null;
        return false;
    }

    /// <summary>
    /// Assigns a static field, the class's own or its base's, as <see cref="TryGetStatic"/> finds it, or replaces a static
    /// method there with the value, as a static field (section 3.7); false if there is none of that name, or if it is a
    /// constant (<paramref name="isConstant"/>).
    /// </summary>
    public bool TrySetStatic(WebLingoValue classValue, string name, WebLingoValue value, out bool isConstant)
    {
        isConstant = false;
        for (var current = classValue; current.Kind == WebLingoValueKind.Class;)
        {
            var target = Get<ClassObject>(current, WebLingoValueKind.Class);
            if (target.StaticFields.ContainsKey(name))
            {
                isConstant = target.Constants?.Contains(name) == true;
                if (isConstant) return false;
                target.StaticFields[name] = value;
                return true;
            }
            if (target.StaticMethods.Remove(name))
            {
                target.StaticFields[name] = value;
                return true;
            }
            current = target.Base;
        }
        return false;
    }

    /// <summary>
    /// <c>Point.setConstant(name, value)</c>: a static member that cannot be assigned, the class's own, given the first
    /// time, replaced after, in place of a static field or method of its name (section 3.7).
    /// </summary>
    public bool TrySetConstant(WebLingoValue classValue, string name, WebLingoValue value, out WebLingoRuntimeError? error)
    {
        var target = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        var before = Size(target);
        target.StaticMethods.Remove(name);
        target.StaticFields[name] = value;
        (target.Constants ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
        var grown = Size(target) - before;
        if (!TryReserve(grown, out error)) return false;
        Units += grown;
        return true;
    }

    /// <summary>
    /// <c>Point.setProperty(name, getter, setter)</c>, or with <paramref name="isStatic"/> <c>setStaticProperty</c>: a
    /// property's getter and setter, given the first time, replaced after, <c>null</c> for none (section 3.7).
    /// </summary>
    public bool TrySetProperty(WebLingoValue classValue, string name, WebLingoValue getter, WebLingoValue setter, bool isStatic,
        out WebLingoRuntimeError? error)
    {
        var target = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        var members = isStatic ? target.StaticMethods : target.OwnMethods;
        var before = Size(target);
        foreach (var (prefix, accessor) in new[] { ("get:", getter), ("set:", setter) })
        {
            if (accessor.Kind == WebLingoValueKind.Null) members.Remove(prefix + name);
            else members[prefix + name] = accessor;
        }
        if (!isStatic)
        {
            // Its instances, and its subclasses', find the property once they gather their methods anew.
            _classVersion++;
            Extended = true;
        }
        var grown = Size(target) - before;
        if (!TryReserve(grown, out error)) return false;
        Units += grown;
        return true;
    }

    /// <summary>
    /// A class's disposal: the dispose methods that disposing an instance runs on it, in order, its own first, then its
    /// bases', each once (section 3.7); none if neither it nor a base declares one.
    /// </summary>
    public WebLingoValue[] DisposeChain(WebLingoValue classValue) => Current(Get<ClassObject>(classValue, WebLingoValueKind.Class)).DisposeChain;

    /// <summary>Marks an instance disposed, giving whether it was not already (section 3.7).</summary>
    public bool TryMarkDisposed(WebLingoValue instance)
    {
        var target = Get<InstanceObject>(instance, WebLingoValueKind.Instance);
        if (target.Disposed) return false;
        target.Disposed = true;
        return true;
    }

    /// <summary>The class a class extends, or <c>null</c>.</summary>
    public WebLingoValue ClassBase(WebLingoValue value) => Get<ClassObject>(value, WebLingoValueKind.Class).Base;

    public string ClassName(WebLingoValue value) => Get<ClassObject>(value, WebLingoValueKind.Class).Name is { Length: > 0 } name ? name : "class";

    /// <summary>A class's constructor's closure, or <c>null</c> if it has none.</summary>
    public WebLingoValue ClassConstructor(WebLingoValue value) => Get<ClassObject>(value, WebLingoValueKind.Class).Constructor;

    /// <summary>
    /// A property's getter, or its <paramref name="setter"/>, of a class's instances, its own or a base's (section 3.7);
    /// false at once for a class that has none.
    /// </summary>
    public bool TryGetAccessor(WebLingoValue classValue, bool setter, string name, out WebLingoValue closure)
    {
        var target = Current(Get<ClassObject>(classValue, WebLingoValueKind.Class));
        closure = WebLingoValue.Null;
        return (setter ? target.Setters : target.Getters) is { } accessors && accessors.TryGetValue(name, out closure);
    }

    /// <summary>What assigning <c>x.name</c> meets on the class of <c>x</c> (section 3.7).</summary>
    public enum Assignee : byte
    {
        /// <summary>None of its members: a field.</summary>
        Field,
        /// <summary>A method, which cannot be assigned.</summary>
        Method,
        /// <summary>A property's setter, which the assignment calls.</summary>
        Setter,
        /// <summary>A property with only a getter, which cannot be assigned.</summary>
        ReadOnly
    }

    /// <summary>
    /// What assigning <c>x.name</c> meets on a class's value, its own members or a base's (section 3.7), with the
    /// class looked up once: a method, a property's <paramref name="setter"/>, a property without one, or a field.
    /// </summary>
    public Assignee FindAssignee(WebLingoValue classValue, string name, out WebLingoValue setter)
    {
        var target = Current(Get<ClassObject>(classValue, WebLingoValueKind.Class));
        setter = WebLingoValue.Null;
        if (target.Methods.ContainsKey(name)) return Assignee.Method;
        if (target.Setters is { } setters && setters.TryGetValue(name, out setter)) return Assignee.Setter;
        return target.Getters is { } getters && getters.ContainsKey(name) ? Assignee.ReadOnly : Assignee.Field;
    }

    /// <summary>
    /// Marks the built-in class <c>Class</c>, whose <c>new</c> makes a class (<see cref="TryNewClass"/>), not an
    /// instance; no class may extend it yet (section 3.7).
    /// </summary>
    public void MarkClassClass(WebLingoValue classValue)
    {
        var target = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        target.IsValueClass = true;
        target.MakesClasses = true;
        target.Makes = WebLingoValueKind.Class; // A class extending it, a metaclass, makes classes (section 3.7).
    }

    /// <summary>
    /// A new instance of a metaclass, a class extending <c>Class</c>: a class, unnamed and extending <c>Object</c> until
    /// its constructor's <c>super(name, base)</c> runs (<see cref="TryShapeClass"/>), whose class is the metaclass
    /// (section 3.7).
    /// </summary>
    private bool TryAllocateMetaclassInstance(WebLingoValue metaclass, out WebLingoValue made, out WebLingoRuntimeError? error)
    {
        if (!TryAllocateClass("", WebLingoValue.Null, new Dictionary<string, WebLingoValue>(StringComparer.Ordinal), ObjectClass, out made,
                out error, defaultConstructor: true))
        {
            return false;
        }
        Get<ClassObject>(made, WebLingoValueKind.Class).Metaclass = metaclass;
        return true;
    }

    /// <summary>
    /// <c>Class</c>'s constructor's <c>hold(this, [name, base])</c>: the class a metaclass made named, a string, or
    /// unnamed for <c>null</c>, and extending the base given, a class, or <c>Object</c> for <c>null</c> (WL3001 otherwise,
    /// and for a base that is the class itself, or extends it).
    /// </summary>
    private bool TryShapeClass(WebLingoValue classValue, WebLingoValue shape, out WebLingoRuntimeError? error)
    {
        var parts = GetArray(shape);
        var (name, baseClass) = (parts[0], parts[1]);
        error = null;
        if (name.Kind is not (WebLingoValueKind.Null or WebLingoValueKind.String))
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"A class's name is a string, not {WebLingoValueFormatter.Describe(name, this)}.");
            return false;
        }
        if (baseClass.Kind == WebLingoValueKind.Null) baseClass = ObjectClass;
        if (baseClass.Kind != WebLingoValueKind.Class || Get<ClassObject>(baseClass, WebLingoValueKind.Class).Sealed)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"{Capitalized(WebLingoValueFormatter.Describe(baseClass, this))} cannot be extended.");
            return false;
        }
        for (var current = baseClass; current.Kind == WebLingoValueKind.Class; current = Get<ClassObject>(current, WebLingoValueKind.Class).Base)
        {
            if (current != classValue) continue;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "A class cannot extend itself.");
            return false;
        }
        var target = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        var baseObject = Get<ClassObject>(baseClass, WebLingoValueKind.Class);
        target.Name = name.Kind == WebLingoValueKind.String ? GetString(name) : "";
        target.Base = baseClass;
        target.Makes = baseObject.Makes;
        target.Holds = baseObject.Holds;
        _classVersion++;
        return true;
    }

    /// <summary>
    /// <c>new Class(name, base)</c>: a class made while the program runs, named by a string, or unnamed for none, and
    /// extending <c>Object</c>, or the class given (section 3.7). It has no methods, nor a constructor of its own, until
    /// <see cref="TrySetMethod"/> gives it some.
    /// </summary>
    private bool TryNewClass(WebLingoValue[] values, int first, int count, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        // Its arguments are read before its result is written, which may go where they are.
        var name = count > 0 ? values[first] : WebLingoValue.Null;
        var baseClass = count > 1 ? values[first + 1] : WebLingoValue.Null;
        result = WebLingoValue.Null;
        if (count > 2)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.WrongArgumentCount, $"'Class' takes 0 to 2 arguments, not {count}.");
            return false;
        }
        if (name.Kind is not (WebLingoValueKind.Null or WebLingoValueKind.String))
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"A class's name is a string, not {WebLingoValueFormatter.Describe(name, this)}.");
            return false;
        }
        var text = name.Kind == WebLingoValueKind.String ? GetString(name) : "";
        if (baseClass.Kind == WebLingoValueKind.Null) baseClass = ObjectClass;
        return TryAllocateClass(text, WebLingoValue.Null, new Dictionary<string, WebLingoValue>(StringComparer.Ordinal), baseClass, out result,
            out error, defaultConstructor: true);
    }

    /// <summary>
    /// <c>Point.setMethod(name, f)</c>: a class's method, which its instances, and its subclasses', made before or after,
    /// then have, replacing one of that name as an extension's does; <c>constructor</c> its constructor, and
    /// <c>destructor</c> its <c>dispose</c> (section 3.7).
    /// </summary>
    public bool TrySetMethod(WebLingoValue classValue, string name, WebLingoValue method, out WebLingoRuntimeError? error)
    {
        var target = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        error = null;
        if (name == "constructor")
        {
            target.Constructor = method;
            target.DefaultConstructor = false;
            return true;
        }
        var before = Size(target);
        target.OwnMethods[name == "destructor" ? "dispose" : name] = method;
        _classVersion++;
        Extended = true;
        var grown = Size(target) - before;
        if (!TryReserve(grown, out error)) return false;
        Units += grown;
        return true;
    }

    /// <summary><c>Point.__className = name</c>: a class named, or renamed, or unnamed by an empty name (section 3.7).</summary>
    public void RenameClass(WebLingoValue classValue, string name) => Get<ClassObject>(classValue, WebLingoValueKind.Class).Name = name;

    /// <summary>
    /// <c>Point.removeMethod(name)</c>: a class's own method removed, declared or given while the program runs, its base's
    /// of that name then reaching its instances; <c>constructor</c> its constructor, the class then acting as one that
    /// declares none, and <c>destructor</c> its <c>dispose</c>. Whether it had one (section 3.7).
    /// </summary>
    public bool RemoveMethod(WebLingoValue classValue, string name)
    {
        var target = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        if (name == "constructor")
        {
            if (target.DefaultConstructor) return false;
            target.Constructor = WebLingoValue.Null;
            target.DefaultConstructor = true;
            return true;
        }
        if (!target.OwnMethods.Remove(name == "destructor" ? "dispose" : name)) return false;
        _classVersion++;
        Units--;
        return true;
    }

    /// <summary>Whether a class has a name; one <c>new Class()</c> made has none (section 3.7).</summary>
    public bool IsNamed(WebLingoValue classValue) => Get<ClassObject>(classValue, WebLingoValueKind.Class).Name.Length > 0;

    /// <summary>A method's closure, by its name, from a class.</summary>
    public bool TryGetMethod(WebLingoValue value, string name, out WebLingoValue closure) =>
        Current(Get<ClassObject>(value, WebLingoValueKind.Class)).Methods.TryGetValue(name, out closure);

    /// <summary>A new instance of a class, without fields yet.</summary>
    public bool TryAllocateInstance(WebLingoValue classValue, out WebLingoValue instance, out WebLingoRuntimeError? error)
    {
        var classObject = Get<ClassObject>(classValue, WebLingoValueKind.Class);
        if (classObject.Sealed)
        {
            instance = WebLingoValue.Null;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'new' cannot make a value of the class {classObject.Name}.");
            return false;
        }
        // A class extending Array, Map or Set makes a collection of its own class; Array, Map and Set, a plain one.
        var own = classObject.IsCollectionClass ? null : new InstanceObject(classValue);
        return classObject.Makes switch
        {
            WebLingoValueKind.Array =>
                TryAllocate(new ArrayObject([]) { Instance = own }, WebLingoValueKind.Array, 0, out instance, out error, own is not null),
            WebLingoValueKind.Map or WebLingoValueKind.Set =>
                TryAllocate(new MapObject { Instance = own }, classObject.Makes, 0, out instance, out error, own is not null),
            WebLingoValueKind.Class => TryAllocateMetaclassInstance(classValue, out instance, out error),
            _ => TryAllocate(new InstanceObject(classValue) { Holds = classObject.Holds != WebLingoValueKind.Null },
                WebLingoValueKind.Instance, 0, out instance, out error)
        };
    }

    public WebLingoValue ClassOf(WebLingoValue instance) => Get<InstanceObject>(instance, WebLingoValueKind.Instance).Class;

    public bool TryGetField(WebLingoValue instance, string name, out WebLingoValue value) => Fields(instance).TryGet(name, out value);

    /// <summary>
    /// Assigns a field, which exists from then on: a new one is one more unit, within the budget (WL3003). Given its
    /// class, <c>p.count = 5</c> assigns a static field of the class, its own or a base's, when the instance has no field
    /// of that name (section 3.7); a static constant is WL3001.
    /// </summary>
    public bool TrySetField(WebLingoValue instance, string name, WebLingoValue value, out WebLingoRuntimeError? error,
        WebLingoValue classValue = default)
    {
        var fields = Fields(instance);
        error = null;
        if (fields.TryGet(name, out _))
        {
            fields.Set(name, value);
            return true;
        }
        return TryAddField(fields, name, value, classValue, out error);
    }

    /// <summary>A field the instance does not have: its class's static field of that name assigned, or a new field made.</summary>
    private bool TryAddField(InstanceObject fields, string name, WebLingoValue value, WebLingoValue classValue, out WebLingoRuntimeError? error)
    {
        error = null;
        for (var current = classValue; current.Kind == WebLingoValueKind.Class;)
        {
            var target = Get<ClassObject>(current, WebLingoValueKind.Class);
            if (target.StaticFields.Count > 0 && target.StaticFields.ContainsKey(name))
            {
                if (target.Constants?.Contains(name) == true)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                        $"'{name}' is a constant of the class {target.Name}, and cannot be assigned.");
                    return false;
                }
                target.StaticFields[name] = value;
                return true;
            }
            current = target.Base;
        }
        if (!TryReserve(1, out error)) return false;
        fields.Set(name, value);
        Units++;
        return true;
    }

    public int FieldCount(WebLingoValue instance) => Fields(instance).Count;

    /// <summary>An instance's field at a position from 0 to its count − 1, in no defined order.</summary>
    public (string Name, WebLingoValue Value) FieldAt(WebLingoValue instance, int position) => Fields(instance).At(position);

    /// <summary>
    /// A method bound to an instance: a function that runs the method on it (section 3.7); with
    /// <paramref name="disposes"/>, the instance's <c>dispose</c>, whose call disposes it as <c>x.dispose()</c> does.
    /// </summary>
    public bool TryAllocateBoundMethod(WebLingoValue instance, WebLingoValue closure, out WebLingoValue bound, out WebLingoRuntimeError? error,
        bool disposes = false) =>
        TryAllocate(new BoundMethodObject(instance, closure, disposes), WebLingoValueKind.BoundMethod, 2, out bound, out error);

    /// <summary>Whether a bound method is an instance's <c>dispose</c>, whose call disposes it (section 3.7).</summary>
    public bool IsDisposal(WebLingoValue bound) => Get<BoundMethodObject>(bound, WebLingoValueKind.BoundMethod).Disposes;

    public (WebLingoValue Instance, WebLingoValue Closure) GetBoundMethod(WebLingoValue bound)
    {
        var target = Get<BoundMethodObject>(bound, WebLingoValueKind.BoundMethod);
        return (target.Instance, target.Closure);
    }

    /// <summary>A new set of the <paramref name="count"/> values from <paramref name="values"/>[<paramref name="first"/>] on.</summary>
    public bool TryAllocateSet(WebLingoValue[] values, int first, int count, out WebLingoValue set, out WebLingoRuntimeError? error)
    {
        var entries = new MapObject();
        for (var i = 0; i < count; i++)
        {
            if (!TryKey(values[first + i], out var key, out error, inSet: true))
            {
                set = WebLingoValue.Null;
                return false;
            }
            entries.Set(key, values[first + i], WebLingoValue.Null);
        }
        return TryAllocate(entries, WebLingoValueKind.Set, entries.Count, out set, out error);
    }

    /// <summary>Adds a value to a set, giving whether it was new: a new one is one more unit, within the budget.</summary>
    public bool TryAddToSet(WebLingoValue set, WebLingoValue value, out bool added, out WebLingoRuntimeError? error)
    {
        added = false;
        var entries = Get<MapObject>(set, WebLingoValueKind.Set);
        if (!TryKey(value, out var key, out error, inSet: true)) return false;
        if (entries.TryGet(key, out _)) return true;
        if (!TryReserve(1, out error)) return false;
        entries.Set(key, value, WebLingoValue.Null);
        Units++;
        added = true;
        return true;
    }

    /// <summary>Whether a set holds a value; WL3001 if the value cannot be in a set.</summary>
    public bool TryFindInSet(WebLingoValue set, WebLingoValue value, out bool found, out WebLingoRuntimeError? error)
    {
        found = false;
        if (!TryKey(value, out var key, out error, inSet: true)) return false;
        found = Get<MapObject>(set, WebLingoValueKind.Set).TryGet(key, out _);
        return true;
    }

    /// <summary>Removes a value from a set, giving whether it was there.</summary>
    public bool TryRemoveFromSet(WebLingoValue set, WebLingoValue value, out bool removed, out WebLingoRuntimeError? error)
    {
        removed = false;
        if (!TryKey(value, out var key, out error, inSet: true)) return false;
        removed = Get<MapObject>(set, WebLingoValueKind.Set).Remove(key);
        if (removed) Units--;
        return true;
    }

    /// <summary>How many values the set has.</summary>
    public int SetCount(WebLingoValue set) => Get<MapObject>(set, WebLingoValueKind.Set).Count;

    /// <summary>The set's value at a position from 0 to its count − 1, in no defined order (section 3.6).</summary>
    public WebLingoValue SetValueAt(WebLingoValue set, int position) => Get<MapObject>(set, WebLingoValueKind.Set).At(position).Key;

    /// <summary>The map's value for the key: false if it has none; WL3001 if the value cannot be a key.</summary>
    public bool TryFindInMap(WebLingoValue map, WebLingoValue key, out WebLingoValue value, out bool found, out WebLingoRuntimeError? error)
    {
        value = WebLingoValue.Null;
        found = false;
        if (!TryKey(key, out var mapKey, out error)) return false;
        found = Get<MapObject>(map, WebLingoValueKind.Map).TryGet(mapKey, out value);
        return true;
    }

    /// <summary>Adds or replaces the key's value: a new entry is one more unit, within the budget (WL3003).</summary>
    public bool TrySetInMap(WebLingoValue map, WebLingoValue key, WebLingoValue value, out WebLingoRuntimeError? error)
    {
        var entries = Get<MapObject>(map, WebLingoValueKind.Map);
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
    public bool TryRemoveFromMap(WebLingoValue map, WebLingoValue key, out bool removed, out WebLingoRuntimeError? error)
    {
        removed = false;
        if (!TryKey(key, out var mapKey, out error)) return false;
        removed = Get<MapObject>(map, WebLingoValueKind.Map).Remove(mapKey);
        if (removed) Units--;
        return true;
    }

    /// <summary>How many entries the map has.</summary>
    public int MapCount(WebLingoValue map) => Get<MapObject>(map, WebLingoValueKind.Map).Count;

    /// <summary>The map's entry at a position from 0 to its count − 1, in no defined order (section 3.6).</summary>
    public (WebLingoValue Key, WebLingoValue Value) MapEntryAt(WebLingoValue map, int position) => Get<MapObject>(map, WebLingoValueKind.Map).At(position);

    /// <summary>
    /// The key a value is: strings, int64s, Booleans and null may be keys, compared as <c>==</c> compares them; any
    /// other value is WL3001 (section 3.6).
    /// </summary>
    private bool TryKey(WebLingoValue value, out MapKey key, out WebLingoRuntimeError? error, bool inSet = false)
    {
        switch (value.Kind)
        {
            case WebLingoValueKind.String:
                key = new MapKey(WebLingoValueKind.String, 0, GetString(value));
                break;
            case WebLingoValueKind.Int64:
                key = new MapKey(WebLingoValueKind.Int64, value.AsInt64, null);
                break;
            case WebLingoValueKind.Boolean:
                key = new MapKey(WebLingoValueKind.Boolean, value.AsBoolean ? 1 : 0, null);
                break;
            case WebLingoValueKind.Null:
                key = new MapKey(WebLingoValueKind.Null, 0, null);
                break;
            default:
                key = default;
                var what = Capitalized(WebLingoValueFormatter.Describe(value));
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, inSet
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
        foreach (var roots in _listRoots)
        {
            foreach (var value in roots) Mark(value, marked, pending);
        }
        if (Scheduler is not null)
        {
            var scheduled = new List<WebLingoValue>();
            Scheduler.CopyRoots(scheduled);
            foreach (var value in scheduled) Mark(value, marked, pending);
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
                    MarkFields(array.Instance, marked, pending);
                    break;
                case ClassObject classObject:
                    Mark(classObject.Base, marked, pending);
                    Mark(classObject.Metaclass, marked, pending);
                    Mark(classObject.Constructor, marked, pending);
                    foreach (var method in classObject.Methods.Values) Mark(method, marked, pending);
                    foreach (var method in classObject.OwnMethods.Values) Mark(method, marked, pending);
                    foreach (var method in classObject.DisposeChain) Mark(method, marked, pending);
                    foreach (var method in classObject.StaticMethods.Values) Mark(method, marked, pending);
                    foreach (var field in classObject.StaticFields.Values) Mark(field, marked, pending);
                    break;
                case ModuleObject module:
                    foreach (var cell in module.Exports.Values) Mark(cell, marked, pending);
                    break;
                case InstanceObject instance:
                    Mark(instance.Class, marked, pending);
                    Mark(instance.Value, marked, pending);
                    for (var i = 0; i < instance.Count; i++) Mark(instance.At(i).Value, marked, pending);
                    break;
                case WebLingoTask task:
                    Mark(task.Result, marked, pending);
                    Mark(task.Exception, marked, pending);
                    Mark(task.Results, marked, pending);
                    if (task.Joins is not null)
                    {
                        foreach (var (combinator, _) in task.Joins) Mark(combinator, marked, pending);
                    }
                    foreach (var waiter in task.Waiters) Mark(waiter, marked, pending);
                    if (task.FrameRoots is not null)
                    {
                        foreach (var roots in task.FrameRoots)
                        {
                            foreach (var value in roots) Mark(value, marked, pending);
                        }
                    }
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
                    MarkFields(map.Instance, marked, pending);
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

    private static void Mark(WebLingoValue value, bool[] marked, Stack<int> pending)
    {
        if (value.Kind is not (WebLingoValueKind.String or WebLingoValueKind.Function or WebLingoValueKind.Cell or WebLingoValueKind.Array
            or WebLingoValueKind.Map or WebLingoValueKind.Set or WebLingoValueKind.Class or WebLingoValueKind.Instance or WebLingoValueKind.BoundMethod
            or WebLingoValueKind.Task or WebLingoValueKind.Module))
        {
            return;
        }
        if (marked[value.Handle]) return;
        marked[value.Handle] = true;
        pending.Push(value.Handle);
    }

    /// <summary>A collection's class and fields, if a class extending Array, Map or Set made it.</summary>
    private static void MarkFields(InstanceObject? fields, bool[] marked, Stack<int> pending)
    {
        if (fields is null) return;
        Mark(fields.Class, marked, pending);
        for (var i = 0; i < fields.Count; i++) Mark(fields.At(i).Value, marked, pending);
    }

    private static long SizeOf(object item) => ObjectOverhead + item switch
    {
        string text => text.Length,
        Closure closure => closure.Cells.Length,
        ArrayObject array => array.Elements.Count + (array.Instance?.Count ?? 0),
        MapObject map => map.Count + (map.Instance?.Count ?? 0),
        ClassObject classObject => Size(classObject),
        InstanceObject instance => instance.Count,
        BoundMethodObject => 2,
        WebLingoTask => 2,
        ModuleObject module => module.Exports.Count,
        _ => 0
    };

    /// <summary>Makes room for <paramref name="units"/> more, collecting first if the next collection is due.</summary>
    private bool TryReserve(long units, out WebLingoRuntimeError? error)
    {
        if (Units + units > _nextCollectionAt || Units + units > _maxUnits) Collect();
        if (Units + units > _maxUnits)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BudgetExceeded, "The program has used all the memory this execution may use.");
            return false;
        }
        error = null;
        return true;
    }

    private bool TryAllocate(object item, WebLingoValueKind kind, long content, out WebLingoValue value, out WebLingoRuntimeError? error,
        bool hasClass = false)
    {
        var units = content + ObjectOverhead;
        if (Units + units > _nextCollectionAt || Units + units > _maxUnits) Collect();
        if (Units + units > _maxUnits)
        {
            value = WebLingoValue.Null;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BudgetExceeded,
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
        value = WebLingoValue.FromHandle(kind, handle, hasClass);
        error = null;
        return true;
    }

    private T Get<T>(WebLingoValue value, WebLingoValueKind kind) where T : class
    {
        if (value.Kind != kind) throw new ArgumentException($"The value is not a {kind}.", nameof(value));
        return _objects[value.Handle] as T ?? throw new InvalidOperationException("The object was collected while in use.");
    }

    private sealed class Cell
    {
        public WebLingoValue Value;
        public bool HasValue;
    }

    private sealed record Closure(int Function, WebLingoValue[] Cells);

    private sealed record ArrayObject(List<WebLingoValue> Elements)
    {
        /// <summary>Its class and fields, if a class extending Array made it (section 3.7).</summary>
        public InstanceObject? Instance { get; init; }
    }

    /// <summary>
    /// A class: <see cref="Methods"/> are those its instances have, its base's with its own in their place, gathered
    /// from <see cref="OwnMethods"/>, those it declares and those extensions gave it, as of <see cref="Version"/>.
    /// </summary>
    private sealed record ClassObject(string Name, WebLingoValue Constructor, Dictionary<string, WebLingoValue> Methods)
    {
        /// <summary>Its constructor's closure, which <c>setMethod("constructor", f)</c> may give it while the program runs.</summary>
        public WebLingoValue Constructor { get; set; } = Constructor;
        /// <summary>Its name, empty for an unnamed class, which <c>__className</c> may change while the program runs.</summary>
        public string Name { get; set; } = Name;
        public WebLingoValue Base { get; set; }
        public Dictionary<string, WebLingoValue> OwnMethods { get; init; } = new(StringComparer.Ordinal);
        public int Version { get; set; }
        public WebLingoValue[] DisposeChain { get; set; } = [];
        public bool Sealed { get; set; }
        /// <summary>What <c>new</c> makes: an instance, or a collection, for Array, Map, Set and the classes extending them.</summary>
        public WebLingoValueKind Makes { get; set; } = WebLingoValueKind.Instance;
        /// <summary>Whether it is Array, Map or Set itself, whose <c>new</c> makes a plain collection.</summary>
        public bool IsCollectionClass { get; set; }
        /// <summary>The kind of value its instances hold, a class extending Boolean, Integer, Number or String, or null's.</summary>
        public WebLingoValueKind Holds { get; set; } = WebLingoValueKind.Null;
        /// <summary>Whether it is Boolean, Integer, Number or String itself, whose <c>new</c> makes a plain value.</summary>
        public bool IsValueClass { get; set; }
        /// <summary>
        /// Whether its constructor is the one a class declaring none gets, which only runs its base's, or, for a class made
        /// while the program runs, none yet.
        /// </summary>
        public bool DefaultConstructor { get; set; }
        /// <summary>Whether it is <c>Class</c>, whose <c>new</c> makes a class (section 3.7).</summary>
        public bool MakesClasses { get; set; }
        /// <summary>Its static fields that are constants, which cannot be assigned; null for none (section 3.7).</summary>
        public HashSet<string>? Constants { get; set; }
        /// <summary>For a class a metaclass made, that metaclass, its class; null for any other (section 3.7).</summary>
        public WebLingoValue Metaclass { get; set; }
        /// <summary>Its properties' getters and setters, its own and its bases', by the property's name; null for none (section 3.7).</summary>
        public Dictionary<string, WebLingoValue>? Getters { get; set; }
        public Dictionary<string, WebLingoValue>? Setters { get; set; }
        public Dictionary<string, WebLingoValue> StaticMethods { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, WebLingoValue> StaticFields { get; init; } = new(StringComparer.Ordinal);
    }

    /// <summary>A class's units: one, and one for each method of its own and static member.</summary>
    private static int Size(ClassObject classObject) =>
        classObject.OwnMethods.Count + classObject.StaticMethods.Count + classObject.StaticFields.Count + 1;

    /// <summary>A module's instance: its name, and its exports' cells, by name (section 3.9).</summary>
    private sealed class ModuleObject(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, WebLingoValue> Exports { get; } = new(StringComparer.Ordinal);
    }

    private sealed record BoundMethodObject(WebLingoValue Instance, WebLingoValue Closure, bool Disposes);

    /// <summary>An instance: its class, and its fields, by name, in no defined order.</summary>
    private sealed class InstanceObject(WebLingoValue classValue)
    {
        private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);
        private readonly List<(string Name, WebLingoValue Value)> _fields = [];

        public WebLingoValue Class { get; } = classValue;
        public int Count => _fields.Count;
        /// <summary>Whether it holds a value, as an instance of a class extending a kind of value does, and which (section 3.7).</summary>
        public bool Holds { get; init; }
        public WebLingoValue Value { get; set; }
        /// <summary>Whether its disposal has begun, which runs at most once (section 3.7).</summary>
        public bool Disposed { get; set; }

        public bool TryGet(string name, out WebLingoValue value)
        {
            if (_positions.TryGetValue(name, out var position))
            {
                value = _fields[position].Value;
                return true;
            }
            value = WebLingoValue.Null;
            return false;
        }

        public void Set(string name, WebLingoValue value)
        {
            if (_positions.TryGetValue(name, out var position))
            {
                _fields[position] = (name, value);
                return;
            }
            _positions.Add(name, _fields.Count);
            _fields.Add((name, value));
        }

        public (string Name, WebLingoValue Value) At(int position) => _fields[position];
    }

    /// <summary>A map's key as the map compares it: a string by its text, anything else by its kind and payload.</summary>
    private readonly record struct MapKey(WebLingoValueKind Kind, long Bits, string? Text);

    /// <summary>
    /// A hash table, its entries kept in a list that a removal fills from its end, so that an entry is found by its key
    /// and visited by its position, in no defined order (section 3.6).
    /// </summary>
    private sealed class MapObject
    {
        private readonly Dictionary<MapKey, int> _positions = [];

        /// <summary>Its class and fields, if a class extending Map or Set made it (section 3.7).</summary>
        public InstanceObject? Instance { get; init; }
        private readonly List<(MapKey Key, WebLingoValue KeyValue, WebLingoValue Value)> _entries = [];

        public int Count => _entries.Count;

        public bool TryGet(MapKey key, out WebLingoValue value)
        {
            if (_positions.TryGetValue(key, out var position))
            {
                value = _entries[position].Value;
                return true;
            }
            value = WebLingoValue.Null;
            return false;
        }

        public void Set(MapKey key, WebLingoValue keyValue, WebLingoValue value)
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

        public (WebLingoValue Key, WebLingoValue Value) At(int position) => (_entries[position].KeyValue, _entries[position].Value);
    }
}
