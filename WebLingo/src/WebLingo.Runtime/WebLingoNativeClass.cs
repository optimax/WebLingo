using System.Diagnostics.CodeAnalysis;
using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>A native class's constructor, method or static method: its name, its arity, without the instance, and its function.</summary>
public sealed record WebLingoNativeMember(string Name, int Arity, WebLingoNativeFunction Function)
{
    /// <summary>How many of its last parameters a call may leave out, each then <c>null</c> to its function (section 3.9).</summary>
    public int Optional { get; init; }

    /// <summary>
    /// For a static method, whether it is a factory, shorthand for making an instance of the class it is called on and
    /// opening it with one of the instance's methods, <see cref="FactoryMethod"/>, as <c>File.open(path, mode)</c> makes
    /// <c>new this()</c> and awaits its <c>open(path, mode)</c> (section 3.7, "No sealed classes"). The compiler writes it
    /// in WebLingo, its function never called.
    /// </summary>
    public bool Factory { get; init; }

    /// <summary>A factory's instance method, which opens the instance it makes, <c>open</c>.</summary>
    public string? FactoryMethod { get; init; }

    /// <summary>Whether a factory's method gives a task, which the factory awaits, the factory then asynchronous too.</summary>
    public bool FactoryAsync { get; init; }

    /// <summary>Whether a factory's arguments go to <c>new</c>, as <c>Database.open(name)</c>'s do, rather than to its method.</summary>
    public bool FactoryToConstructor { get; init; }
}

/// <summary>
/// A native class's property (section 3.7): its getter, given the instance, and its setter, given the instance and the
/// value assigned, or null for a property that cannot be assigned.
/// </summary>
public sealed record WebLingoNativeProperty(string Name, WebLingoNativeFunction Getter, WebLingoNativeFunction? Setter);

/// <summary>
/// A native class's indexer, <c>x[i]</c> and <c>x[i] = v</c>: its getter, given the instance and the index, and its
/// setter, given the instance, the index and the value assigned, or null for one that cannot be assigned.
/// </summary>
public sealed record WebLingoNativeIndexer(WebLingoNativeFunction Getter, WebLingoNativeFunction? Setter);

/// <summary>
/// A native class's fallback (section 3.10): what reads, assigns and calls the members its instances have that it does not
/// declare, known only while the program runs, as a COM object's are. Its getter is given the instance and the member's
/// name, a string; its setter, the instance, the name and the value assigned; its call, the instance, the name, and the
/// call's arguments, as many as it has. The runtime runs them where a member is not found, so that neither VM knows them.
/// </summary>
public sealed record WebLingoNativeFallback(WebLingoNativeFunction Getter, WebLingoNativeFunction Setter, WebLingoNativeFunction Call);

/// <summary>
/// A native module's class (specification, sections 3.7 and 3.9), which <see cref="IWebLingoNativeRegistry.Class"/> gives:
/// its constructor, its methods and its static methods, each a native function, a constructor's and a method's first
/// argument the instance it runs on. The compiler makes it a class as any other is, extending <c>Object</c>, whose members
/// call those functions, so that a program may extend it, and its instances are disposed, written and compared as any
/// other's. Its constructor marks an instance as its own; a method given an instance that is not one of its class's, or
/// of a class extending it, is WL3001, without running. An instance may hold one host object, a .NET object no script
/// sees, which only the class's own functions reach, through <see cref="TrySetHostObject"/>,
/// <see cref="TryGetHostObject{T}"/> and <see cref="TryReleaseHostObject"/>; one never released is disposed, if it is
/// <see cref="IDisposable"/>, when the collector frees its instance, or when the execution ends.
/// </summary>
public sealed class WebLingoNativeClass(string name)
{
    private readonly List<WebLingoNativeMember> _methods = [];
    private readonly List<WebLingoNativeMember> _staticMethods = [];
    private readonly List<WebLingoNativeProperty> _properties = [];
    private readonly List<WebLingoNativeProperty> _staticProperties = [];
    private readonly List<string> _webLingoMethods = [];

    public string Name { get; } = name;
    public WebLingoNativeMember? Constructor { get; private set; }
    public IReadOnlyList<WebLingoNativeMember> Methods => _methods;
    public IReadOnlyList<WebLingoNativeMember> StaticMethods => _staticMethods;
    public IReadOnlyList<WebLingoNativeProperty> Properties => _properties;
    public IReadOnlyList<WebLingoNativeProperty> StaticProperties => _staticProperties;
    public WebLingoNativeIndexer? Indexer { get; private set; }

    /// <summary>Its fallback for the members it does not declare, if it has one (section 3.10).</summary>
    public WebLingoNativeFallback? Fallback { get; private set; }

    /// <summary>Whether its module's own functions make its instances (<see cref="TryMakeOwn"/>), which the program then tells it its class.</summary>
    public bool MadeByModule { get; private set; }

    /// <summary>
    /// Lets its module's own functions make its instances, without a factory given its class, <c>com.create(progId)</c>
    /// (<see cref="TryMakeOwn"/>, section 3.10): the program tells the class its class value when it begins.
    /// </summary>
    public WebLingoNativeClass DeclareMadeByModule()
    {
        MadeByModule = true;
        return this;
    }

    /// <summary>
    /// Its fallback, which reads, assigns and calls the members its instances have that it does not declare: one that a
    /// field, a method, a property or a static member of its class, or of a class extending it, does not hide; declared
    /// once.
    /// </summary>
    public WebLingoNativeClass DeclareFallback(WebLingoNativeFunction getter, WebLingoNativeFunction setter, WebLingoNativeFunction call)
    {
        if (Error is null && Fallback is not null) Error = $"class '{Name}' declares its fallback twice";
        if (Error is null) Fallback = new(getter, setter, call);
        return this;
    }

    /// <summary>Its methods written in WebLingo, each source as <see cref="DeclareWebLingoMethods"/> was given it.</summary>
    public IReadOnlyList<string> WebLingoMethods => _webLingoMethods;

    /// <summary>
    /// A property, <c>x.name</c>, read by its getter, given the instance, and assigned by its setter, given the instance and
    /// the value, which a property without one cannot be (section 3.7). A property's name is not a method's.
    /// </summary>
    public WebLingoNativeClass DeclareProperty(string name, WebLingoNativeFunction getter, WebLingoNativeFunction? setter = null)
    {
        string? twice = _methods.Exists(m => m.Name == name) || _properties.Exists(p => p.Name == name) ? name : null;
        if (Check(name, 0, twice)) _properties.Add(new(name, getter, setter));
        return this;
    }

    /// <summary>
    /// A static property, <c>Name.name</c>, read by its getter, given nothing, and assigned by its setter, given the value,
    /// which a property without one cannot be (section 3.7). Its name is not a static method's.
    /// </summary>
    public WebLingoNativeClass DeclareStaticProperty(string name, WebLingoNativeFunction getter, WebLingoNativeFunction? setter = null)
    {
        string? twice = _staticMethods.Exists(m => m.Name == name) || _staticProperties.Exists(p => p.Name == name) ? name : null;
        if (Check(name, 0, twice)) _staticProperties.Add(new(name, getter, setter));
        return this;
    }

    /// <summary>
    /// Its indexer, <c>x[i]</c>, read by its getter, given the instance and the index, and assigned by its setter, given the
    /// instance, the index and the value, which an indexer without one cannot be; declared once.
    /// </summary>
    public WebLingoNativeClass DeclareIndexer(WebLingoNativeFunction getter, WebLingoNativeFunction? setter = null)
    {
        if (Error is null && Indexer is not null) Error = $"class '{Name}' declares its indexer twice";
        if (Error is null) Indexer = new(getter, setter);
        return this;
    }

    /// <summary>
    /// Methods written in WebLingo, as a class's body declares them, <c>async transaction(work) { … }</c>, for what a native
    /// function cannot do, calling a script's function and awaiting what it gives; they reach the class's native members
    /// through <c>this</c>, and an error in one is reported where it was called, as a native member's is (section 3.9). The
    /// host reads them when it loads the module: a source that does not parse, or a method whose name another member has,
    /// fails the module.
    /// </summary>
    public WebLingoNativeClass DeclareWebLingoMethods(string source)
    {
        if (Error is null) _webLingoMethods.Add(source);
        return this;
    }

    /// <summary>The first fault in what the module declared, or null: a name that is not one, an arity below 0, or a name twice.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Its constructor, which <c>new</c> runs with the instance first, then the arguments; a class without one takes none,
    /// its instances marked as its own all the same. A constructor is declared once.
    /// </summary>
    public WebLingoNativeClass DeclareConstructor(int arity, WebLingoNativeFunction constructor)
    {
        if (Check("constructor", arity, Constructor is not null ? "constructor" : null)) Constructor = new("constructor", arity, constructor);
        return this;
    }

    /// <summary>A method, called on an instance, which its function is given first; <c>dispose</c> is the class's disposal.</summary>
    public WebLingoNativeClass DeclareMethod(string name, int arity, WebLingoNativeFunction method) => DeclareMethod(name, arity, 0, method);

    /// <summary>A method whose last <paramref name="optional"/> parameters a call may leave out, each then <c>null</c> to its function.</summary>
    public WebLingoNativeClass DeclareMethod(string name, int arity, int optional, WebLingoNativeFunction method)
    {
        if (CheckMember(name, arity, _methods) && CheckOptional(name, arity, optional)) _methods.Add(new(name, arity, method) { Optional = optional });
        return this;
    }

    /// <summary>A static method, called on the class, without an instance.</summary>
    public WebLingoNativeClass DeclareStaticMethod(string name, int arity, WebLingoNativeFunction method) => DeclareStaticMethod(name, arity, 0, method);

    /// <summary>A static method whose last <paramref name="optional"/> parameters a call may leave out, each then <c>null</c>.</summary>
    public WebLingoNativeClass DeclareStaticMethod(string name, int arity, int optional, WebLingoNativeFunction method)
    {
        if (CheckMember(name, arity, _staticMethods) && CheckOptional(name, arity, optional))
        {
            _staticMethods.Add(new(name, arity, method) { Optional = optional });
        }
        return this;
    }

    /// <summary>
    /// A factory, shorthand for both steps of making an instance, created, then opened (the owner's answer, 18 November
    /// 2026; section 3.7, "No sealed classes"): <c>File.open(path, mode)</c> makes <c>new this()</c>, the class it is
    /// called on, so that a class extending this one gets its own instance, and awaits its <paramref name="method"/>,
    /// <c>open(path, mode)</c>, given the factory's arguments, the last <paramref name="optional"/> of which a call may
    /// leave out, or gives them to <c>new</c> instead, <paramref name="toConstructor"/>, as <c>Database.open(name)</c>
    /// does; asynchronous, a task giving the instance, where the method is, <paramref name="async"/>. The compiler writes
    /// it in WebLingo.
    /// </summary>
    public WebLingoNativeClass DeclareFactory(string name, int arity, int optional, string method, bool async, bool toConstructor = false)
    {
        if (CheckMember(name, arity, _staticMethods) && CheckOptional(name, arity, optional))
        {
            _staticMethods.Add(new(name, arity, NeverCalled)
            {
                Optional = optional, Factory = true, FactoryMethod = method, FactoryAsync = async, FactoryToConstructor = toConstructor
            });
        }
        return this;
    }

    /// <summary>A factory's function, which the factory written in WebLingo in its place never calls.</summary>
    private static bool NeverCalled(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "A native factory is written in WebLingo, and its function is not called.");
        return false;
    }

    /// <summary>
    /// A new instance of <paramref name="classValue"/>, this class or one extending it, which a factory was given, marked as
    /// this class's own, as its constructor marks one, its constructor not run (section 3.9); WL3001 for any other value.
    /// </summary>
    public bool TryMake(WebLingoValue classValue, WebLingoHeap heap, out WebLingoValue instance, out WebLingoRuntimeError? error)
    {
        instance = WebLingoValue.Null;
        if (classValue.Kind != WebLingoValueKind.Class)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"The native class {Name} makes its instances only of a class.");
            return false;
        }
        if (!heap.TryAllocateInstance(classValue, out instance, out error)) return false;
        if (heap.TryMarkNative(instance, this)) return true;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
            $"The native class {Name} cannot make {WebLingoValueFormatter.Describe(instance, heap)} one of its instances.");
        return false;
    }

    /// <summary>
    /// A new instance of this class itself, which one of its module's functions makes, as a factory does, its constructor not
    /// run (section 3.10): WL3001 for a class not declared <see cref="DeclareMadeByModule"/>, or one the program has not made.
    /// </summary>
    public bool TryMakeOwn(WebLingoHeap heap, out WebLingoValue instance, out WebLingoRuntimeError? error)
    {
        if (heap.TryGetNativeClassValue(this, out var classValue)) return TryMake(classValue, heap, out instance, out error);
        instance = WebLingoValue.Null;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"The native class {Name}'s module cannot make its instances.");
        return false;
    }

    private bool CheckOptional(string name, int arity, int optional)
    {
        if (Error is null && (optional < 0 || optional > arity)) Error = $"class '{Name}' declares '{name}' with {optional} of its {arity} parameters optional";
        return Error is null;
    }

    private bool CheckMember(string name, int arity, List<WebLingoNativeMember> members)
    {
        string? twice = null;
        foreach (var member in members)
        {
            if (member.Name == name) twice = name;
        }
        if (ReferenceEquals(members, _methods) && _properties.Exists(p => p.Name == name)) twice = name;
        if (ReferenceEquals(members, _staticMethods) && _staticProperties.Exists(p => p.Name == name)) twice = name;
        if (Error is null && name is "constructor" or "destructor")
        {
            Error = name == "constructor"
                ? $"class '{Name}' declares a member named 'constructor': declare its constructor as one"
                : $"class '{Name}' declares a member named 'destructor': declare its disposal as 'dispose'";
        }
        return Check(name, arity, twice);
    }

    private bool Check(string name, int arity, string? twice)
    {
        if (Error is not null) return false;
        if (!IsName(name)) Error = $"class '{Name}' declares a member named '{name}', which is not a name";
        else if (arity < 0) Error = $"class '{Name}' declares '{name}' with an arity of {arity}";
        else if (twice is not null) Error = $"class '{Name}' declares '{twice}' twice";
        return Error is null;
    }

    /// <summary>
    /// The built-ins that run its members, each named <paramref name="prefix"/> and the member's name: its constructor's
    /// <c>.constructor</c>, a method's <c>.name</c>, a static method's <c>.static.name</c>, names no program can write; a
    /// constructor's and a method's arity one more than the member's, for the instance it is given first. A class without a
    /// constructor gets one that only marks its instances.
    /// </summary>
    /// <remarks>
    /// After them, each property's getter, <c>.get.name</c>, given the instance, and its setter, if it has one,
    /// <c>.set.name</c>, given the instance and the value; then each static property's getter, <c>.static.get.name</c>,
    /// given nothing, and its setter, <c>.static.set.name</c>, given the value; then the indexer's getter,
    /// <c>.index.get</c>, given the instance and the index, its setter, <c>.index.set</c>, given the instance, the index
    /// and the value, and its optional getter,
    /// <c>.index.optional</c>, which <c>x?[k]</c> calls, the getter giving <c>null</c> where it raises a missing key or an
    /// index out of range (section 3.6).
    /// </remarks>
    public IReadOnlyList<IWebLingoBuiltinFunction> Builtins(string prefix)
    {
        var builtins = new List<IWebLingoBuiltinFunction>
        {
            new MemberBuiltin(prefix + ".constructor", this, Constructor ?? new("constructor", 0, MarksOnly), MemberKind.Constructor)
        };
        foreach (var method in _methods) builtins.Add(new MemberBuiltin(prefix + "." + method.Name, this, method, MemberKind.Method));
        foreach (var method in _staticMethods) builtins.Add(new MemberBuiltin(prefix + ".static." + method.Name, this, method, MemberKind.Static));
        foreach (var property in _properties)
        {
            builtins.Add(new MemberBuiltin(prefix + ".get." + property.Name, this, new(property.Name, 0, property.Getter), MemberKind.Method));
            if (property.Setter is { } setter)
            {
                builtins.Add(new MemberBuiltin(prefix + ".set." + property.Name, this, new(property.Name, 1, setter), MemberKind.Method));
            }
        }
        foreach (var property in _staticProperties)
        {
            builtins.Add(new MemberBuiltin(prefix + ".static.get." + property.Name, this, new(property.Name, 0, property.Getter), MemberKind.Static));
            if (property.Setter is { } setter)
            {
                builtins.Add(new MemberBuiltin(prefix + ".static.set." + property.Name, this, new(property.Name, 1, setter), MemberKind.Static));
            }
        }
        if (Indexer is { } indexer)
        {
            builtins.Add(new MemberBuiltin(prefix + ".index.get", this, new("[]", 1, indexer.Getter), MemberKind.Method));
            if (indexer.Setter is { } setter) builtins.Add(new MemberBuiltin(prefix + ".index.set", this, new("[]", 2, setter), MemberKind.Method));
            builtins.Add(new MemberBuiltin(prefix + ".index.optional", this, new("?[]", 1, Optional(indexer.Getter)), MemberKind.Method));
        }
        // Last, for a class its module makes: what the program calls when it begins, given the class (section 3.10).
        if (MadeByModule) builtins.Add(new MemberBuiltin(prefix + ".class", this, new("class", 0, MarksOnly), MemberKind.Class));
        return builtins;
    }

    /// <summary>An indexer's getter for <c>x?[k]</c>: <c>null</c> where it raises a missing key or an index out of range.</summary>
    private static WebLingoNativeFunction Optional(WebLingoNativeFunction getter) =>
        (ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            if (getter(arguments, heap, out result, out error)) return true;
            if (!WebLingoMembers.IsOptionalMiss(error)) return false;
            result = WebLingoValue.Null;
            error = null;
            return true;
        };

    private static bool MarksOnly(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = null;
        return true;
    }

    /// <summary>Whether <paramref name="instance"/> is one of this class's, its constructor having marked it.</summary>
    public bool IsInstance(WebLingoValue instance, WebLingoHeap heap) => ReferenceEquals(heap.NativeClassOf(instance), this);

    /// <summary>
    /// Gives an instance of this class its host object, in place of one it held, which is not released (the module
    /// releases it first if it must), and no longer disposed: WL3001 for any other value.
    /// </summary>
    public bool TrySetHostObject(WebLingoValue instance, object hostObject, WebLingoHeap heap, out WebLingoRuntimeError? error)
    {
        ArgumentNullException.ThrowIfNull(hostObject);
        if (!CheckInstance(instance, heap, out error)) return false;
        heap.SetHostObject(instance, hostObject);
        return true;
    }

    /// <summary>
    /// An instance's host object, of the type <typeparamref name="T"/>: WL3001 for a value that is not one of this class's
    /// instances, for one whose host object was released, which is disposed, and for one without such an object.
    /// </summary>
    public bool TryGetHostObject<T>(WebLingoValue instance, WebLingoHeap heap, [NotNullWhen(true)] out T? hostObject,
        out WebLingoRuntimeError? error) where T : class => TryGetHostObject(instance, heap, null, out hostObject, out error);

    /// <summary>
    /// An instance's host object, as <see cref="TryGetHostObject{T}(WebLingoValue, WebLingoHeap, out T, out WebLingoRuntimeError)"/>
    /// gives it, <paramref name="notOpen"/> the message for one not yet given its object, made but not opened (section
    /// 3.7, "No sealed classes").
    /// </summary>
    public bool TryGetHostObject<T>(WebLingoValue instance, WebLingoHeap heap, string? notOpen, [NotNullWhen(true)] out T? hostObject,
        out WebLingoRuntimeError? error) where T : class
    {
        hostObject = null;
        if (!CheckInstance(instance, heap, out error)) return false;
        var held = heap.HostObjectOf(instance, out var released);
        if (held is T found)
        {
            hostObject = found;
            return true;
        }
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, released
            ? $"This {Name} has been disposed."
            : notOpen ?? $"This {Name} has nothing of its own to use: its constructor did not give it.");
        return false;
    }

    /// <summary>
    /// Whether an instance may be opened, created but not yet opened (section 3.7, "No sealed classes"): one of this
    /// class's, its host object, if any, not yet <typeparamref name="TOpen"/>, what opening gives it, nor released; WL3001
    /// otherwise, its message saying which, open already or disposed.
    /// </summary>
    public bool TryCheckUnopened<TOpen>(WebLingoValue instance, WebLingoHeap heap, out WebLingoRuntimeError? error) where TOpen : class
    {
        if (!CheckInstance(instance, heap, out error)) return false;
        var held = heap.HostObjectOf(instance, out var released);
        if (held is not TOpen && !released) return true;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, released
            ? $"This {Name} has been disposed, and cannot be opened again."
            : $"This {Name} is open already: it is opened once.");
        return false;
    }

    /// <summary>
    /// Takes an instance's host object out of it, for the module to close or dispose, the instance then disposed, so that
    /// <see cref="TryGetHostObject{T}"/> reports it as such: null when it held none, or was released already, so that
    /// releasing twice does nothing. WL3001 for a value that is not one of this class's instances.
    /// </summary>
    public bool TryReleaseHostObject(WebLingoValue instance, WebLingoHeap heap, out object? hostObject, out WebLingoRuntimeError? error)
    {
        hostObject = null;
        if (!CheckInstance(instance, heap, out error)) return false;
        hostObject = heap.ReleaseHostObject(instance);
        return true;
    }

    private bool CheckInstance(WebLingoValue instance, WebLingoHeap heap, out WebLingoRuntimeError? error)
    {
        error = null;
        if (IsInstance(instance, heap)) return true;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
            $"{Capitalized(WebLingoValueFormatter.Describe(instance, heap))} is not an instance that the native class {Name}'s constructor made.");
        return false;
    }

    /// <summary>A name as a program writes one: ASCII letters, digits and <c>_</c>, not first a digit.</summary>
    private static bool IsName(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsAsciiDigit(name[0])) return false;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private enum MemberKind { Constructor, Method, Static, Class }

    /// <summary>
    /// A member as a built-in: a constructor marks its instance, then runs; a method runs only on an instance of its class;
    /// a static method as it is called.
    /// </summary>
    private sealed class MemberBuiltin(string name, WebLingoNativeClass owner, WebLingoNativeMember member, MemberKind kind)
        : IWebLingoBuiltinFunction
    {
        public string Name => name;
        public int Arity => member.Arity + (kind == MemberKind.Static ? 0 : 1);

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
        {
            result = WebLingoValue.Null;
            if (kind == MemberKind.Constructor && !heap.TryMarkNative(arguments[0], owner))
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"The native class {owner.Name} cannot make {WebLingoValueFormatter.Describe(arguments[0], heap)} one of its instances.");
                return false;
            }
            if (kind == MemberKind.Class)
            {
                // The class its module makes instances of, told it when the program begins (section 3.10).
                heap.RememberNativeClass(owner, arguments[0]);
                error = null;
                return true;
            }
            if (kind == MemberKind.Method && !owner.IsInstance(arguments[0], heap))
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'{member.Name}' of the native class {owner.Name} runs only on an instance that its constructor made, "
                    + $"not on {WebLingoValueFormatter.Describe(arguments[0], heap)}.");
                return false;
            }
            // Its arguments, after the instance it runs on, a held value as its kind's (section 3.7, "No sealed classes").
            if (heap.TryUnwrapHeld(arguments, kind == MemberKind.Static ? 0 : 1, out var unwrapped)) return member.Function(unwrapped, heap, out result, out error);
            return member.Function(arguments, heap, out result, out error);
        }
    }
}
