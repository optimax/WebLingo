using System.Diagnostics.CodeAnalysis;
using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>A native class's constructor, method or static method: its name, its arity, without the instance, and its function.</summary>
public sealed record WebLingoNativeMember(string Name, int Arity, WebLingoNativeFunction Function)
{
    /// <summary>How many of its last parameters a call may leave out, each then <c>null</c> to its function (section 3.9).</summary>
    public int Optional { get; init; }

    /// <summary>
    /// For a static method, whether it is a factory, given its class first, with which it makes the class's instances
    /// (<see cref="WebLingoNativeClass.TryMake"/>), as <c>File.open(path, mode)</c> does.
    /// </summary>
    public bool Factory { get; init; }
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

    public string Name { get; } = name;
    public WebLingoNativeMember? Constructor { get; private set; }
    public IReadOnlyList<WebLingoNativeMember> Methods => _methods;
    public IReadOnlyList<WebLingoNativeMember> StaticMethods => _staticMethods;
    public IReadOnlyList<WebLingoNativeProperty> Properties => _properties;
    public WebLingoNativeIndexer? Indexer { get; private set; }

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
    /// Its indexer, <c>x[i]</c>, read by its getter, given the instance and the index, and assigned by its setter, given the
    /// instance, the index and the value, which an indexer without one cannot be; declared once.
    /// </summary>
    public WebLingoNativeClass DeclareIndexer(WebLingoNativeFunction getter, WebLingoNativeFunction? setter = null)
    {
        if (Error is null && Indexer is not null) Error = $"class '{Name}' declares its indexer twice";
        if (Error is null) Indexer = new(getter, setter);
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
    /// A factory, a static method given its class first, then its arguments, the last <paramref name="optional"/> of which
    /// a call may leave out, which makes the class's instances with <see cref="TryMake"/>, opened, connected or read, as a
    /// constructor cannot, being synchronous: <c>File.open(path, mode)</c>, a task giving the file.
    /// </summary>
    public WebLingoNativeClass DeclareFactory(string name, int arity, int optional, WebLingoNativeFunction factory)
    {
        if (CheckMember(name, arity, _staticMethods) && CheckOptional(name, arity, optional))
        {
            _staticMethods.Add(new(name, arity, factory) { Optional = optional, Factory = true });
        }
        return this;
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
    /// <c>.set.name</c>, given the instance and the value; then the indexer's getter, <c>.index.get</c>, given the instance
    /// and the index, its setter, <c>.index.set</c>, given the instance, the index and the value, and its optional getter,
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
        if (Indexer is { } indexer)
        {
            builtins.Add(new MemberBuiltin(prefix + ".index.get", this, new("[]", 1, indexer.Getter), MemberKind.Method));
            if (indexer.Setter is { } setter) builtins.Add(new MemberBuiltin(prefix + ".index.set", this, new("[]", 2, setter), MemberKind.Method));
            builtins.Add(new MemberBuiltin(prefix + ".index.optional", this, new("?[]", 1, Optional(indexer.Getter)), MemberKind.Method));
        }
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
            : $"This {Name} has nothing of its own to use: its constructor did not give it.");
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

    private enum MemberKind { Constructor, Method, Static }

    /// <summary>
    /// A member as a built-in: a constructor marks its instance, then runs; a method runs only on an instance of its class;
    /// a static method as it is called.
    /// </summary>
    private sealed class MemberBuiltin(string name, WebLingoNativeClass owner, WebLingoNativeMember member, MemberKind kind)
        : IWebLingoBuiltinFunction
    {
        public string Name => name;
        public int Arity => member.Arity + (kind == MemberKind.Static && !member.Factory ? 0 : 1);

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
        {
            result = WebLingoValue.Null;
            if (kind == MemberKind.Constructor && !heap.TryMarkNative(arguments[0], owner))
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"The native class {owner.Name} cannot make {WebLingoValueFormatter.Describe(arguments[0], heap)} one of its instances.");
                return false;
            }
            if (kind == MemberKind.Method && !owner.IsInstance(arguments[0], heap))
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'{member.Name}' of the native class {owner.Name} runs only on an instance that its constructor made, "
                    + $"not on {WebLingoValueFormatter.Describe(arguments[0], heap)}.");
                return false;
            }
            return member.Function(arguments, heap, out result, out error);
        }
    }
}
