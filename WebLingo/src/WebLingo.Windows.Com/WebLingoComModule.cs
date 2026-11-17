using WebLingo.Core;
using WebLingo.Runtime;
using static WebLingo.Windows.Com.WebLingoComInterop;
namespace WebLingo.Windows.Com;

/// <summary>
/// <c>windows:com</c>, COM's objects on Windows (specification, section 3.10), a native module installed with the runtime
/// as <c>windows/com.dll</c> on Windows: <c>com.create(progId)</c>, by a ProgID or a CLSID, and <c>com.get(moniker)</c>,
/// each giving a <c>ComObject</c>, only for what the configuration's <c>"com"</c> allows, by name, ignoring case, or
/// <c>"*"</c> any, anything else a <c>ComPermissionError</c> (WL3018); a <c>ComObject</c>'s members through its <c>IDispatch</c>, written as any member is, its fallback
/// reading, assigning and calling those it does not declare (<see cref="WebLingoNativeFallback"/>), a property that takes
/// arguments read as a call, its default member its indexer, <c>x[k]</c>, a member hidden by one of its own reached by its
/// <c>call(name, arguments)</c>, <c>get(name)</c> and <c>set(name, value)</c>; <c>foreach</c> through its <c>iterate()</c>,
/// a <c>ComEnumerator</c> over its <c>_NewEnum</c>; and a failure a <c>ComError</c>, WL3012, with its <c>number</c>,
/// <c>source</c> and <c>description</c>. Each call blocks its worker until it returns, on the worker's single-threaded
/// apartment, which a Windows host starts its workers in.
/// </summary>
public sealed class WebLingoComModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        var objects = registry.Class("ComObject").DeclareMadeByModule();
        var enumerators = registry.Class("ComEnumerator").DeclareMadeByModule();
        var com = new WebLingoComObjects(objects, enumerators);
        objects
            .DeclareMethod("call", 2, com.CallNamed)
            .DeclareMethod("get", 1, com.Get)
            .DeclareMethod("set", 2, com.Set)
            .DeclareMethod("iterate", 0, com.Iterate)
            .DeclareMethod("dispose", 0, com.Dispose)
            .DeclareIndexer(com.GetDefault, com.SetDefault)
            .DeclareFallback(com.Get, com.Set, com.Call);
        enumerators
            .DeclareMethod("hasNext", 0, com.HasNext)
            .DeclareMethod("next", 0, com.Next)
            .DeclareMethod("dispose", 0, com.DisposeEnumerator);
        registry.Function("create", 1, com.Create);
        registry.Function("get", 1, com.GetObject);
    }
}

/// <summary><c>windows:com</c>'s functions and its classes' members (section 3.10).</summary>
internal sealed unsafe class WebLingoComObjects(WebLingoNativeClass objects, WebLingoNativeClass enumerators)
{
    private readonly WebLingoComValues _values = new(objects);

    /// <summary>Whether this thread has entered COM: a worker, a single-threaded apartment's thread (section 3.10, "Threads").</summary>
    [ThreadStatic]
    private static bool _entered;

    /// <summary>
    /// Enters COM on this thread once, in a single-threaded apartment; a thread .NET entered in another, which a Windows host's
    /// worker never is, goes on in it.
    /// </summary>
    private static bool TryEnter(out WebLingoRuntimeError? error)
    {
        error = null;
        if (_entered) return true;
        if (!OperatingSystem.IsWindows())
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "COM objects are Windows's, and this is not Windows.");
            return false;
        }
        CoInitializeEx(0, 2); // COINIT_APARTMENTTHREADED: S_OK, S_FALSE, or RPC_E_CHANGED_MODE for a thread already in another.
        _entered = true;
        return true;
    }

    /// <summary>Whether the configuration's <c>"com"</c> allows <paramref name="name"/>, a ProgID, a CLSID or a moniker, or lists <c>"*"</c>.</summary>
    private static bool TryAllow(string name, WebLingoHeap heap, out WebLingoRuntimeError? error)
    {
        error = null;
        if (heap.Allowed is { } allowed && allowed.TryGetValue("com", out var listed))
        {
            foreach (var each in listed)
            {
                if (each == "*" || string.Equals(each, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.ComPermissionDenied,
            $"'{name}' is not a COM object this application may make: its configuration's \"com\" does not allow it.");
        return false;
    }

    /// <summary><c>com.create(progId)</c>: a new object, by its ProgID or its CLSID (<c>{…}</c>).</summary>
    public bool Create(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryName(a[0], "create", "a ProgID or a CLSID", heap, out var name, out error) || !TryAllow(name, heap, out error)
            || !TryEnter(out error))
        {
            return false;
        }
        Guid classId;
        var found = name.StartsWith('{') ? CLSIDFromString(name, &classId) : CLSIDFromProgID(name, &classId);
        if (found != Ok) return Failed(found, "", $"there is no COM class '{name}'", "create", out error);
        var interfaceId = DispatchInterface;
        nint dispatch;
        var made = CoCreateInstance(&classId, 0, 0x1 | 0x4, &interfaceId, &dispatch); // CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER
        if (made != Ok || dispatch == 0) return Failed(made, "", $"'{name}' cannot be made", "create", out error);
        return _values.TryMakeObject(dispatch, heap, out result, out error);
    }

    /// <summary><c>com.get(moniker)</c>: an object that exists, bound to by its moniker, as <c>GetObject</c> binds.</summary>
    public bool GetObject(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryName(a[0], "get", "a moniker", heap, out var moniker, out error) || !TryAllow(moniker, heap, out error) || !TryEnter(out error))
        {
            return false;
        }
        var interfaceId = DispatchInterface;
        nint dispatch;
        var bound = CoGetObject(moniker, 0, &interfaceId, &dispatch);
        if (bound != Ok || dispatch == 0) return Failed(bound, "", $"'{moniker}' cannot be bound to", "get", out error);
        return _values.TryMakeObject(dispatch, heap, out result, out error);
    }

    /// <summary>The fallback's and <c>get(name)</c>'s: a property read, or a method called without arguments.</summary>
    public bool Get(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryHandle(a[0], heap, out var handle, out error) || !TryName(a[1], "get", "a member's name", heap, out var name, out error)) return false;
        return TryInvoke(handle, name, null, PropertyGet | Method, [], heap, out result, out error);
    }

    /// <summary>The fallback's and <c>set(name, value)</c>'s: a property assigned, by reference for an object if it is not by value.</summary>
    public bool Set(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryHandle(a[0], heap, out var handle, out error) || !TryName(a[1], "set", "a member's name", heap, out var name, out error)) return false;
        return TryPut(handle, name, null, [a[2]], heap, out error);
    }

    /// <summary>The fallback's call: a method, or a property that takes arguments, with the call's arguments.</summary>
    public bool Call(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryHandle(a[0], heap, out var handle, out error) || !TryName(a[1], "call", "a member's name", heap, out var name, out error)) return false;
        return TryInvoke(handle, name, null, Method | PropertyGet, a[2..], heap, out result, out error);
    }

    /// <summary><c>call(name, arguments)</c>: a member called by its name, with an array's elements, one that <c>ComObject</c>'s own hide.</summary>
    public bool CallNamed(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryHandle(a[0], heap, out var handle, out error) || !TryName(a[1], "call", "a member's name", heap, out var name, out error)) return false;
        if (a[2].Kind != WebLingoValueKind.Array)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'call' takes its member's arguments as an array, not {WebLingoValueFormatter.Describe(a[2], heap)}.");
            return false;
        }
        return TryInvoke(handle, name, null, Method | PropertyGet, [.. heap.GetArray(a[2])], heap, out result, out error);
    }

    /// <summary><c>x[k]</c>: the object's default member with <c>k</c>, or, if that takes none, its default member's own.</summary>
    public bool GetDefault(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryHandle(a[0], heap, out var handle, out error)) return false;
        if (TryInvoke(handle, null, ValueMember, PropertyGet | Method, a[1..], heap, out result, out error, out var code)) return true;
        if (code is not (BadParameterCount or MemberNotFound)) return false;
        // As VBScript's rs("Name") reads rs.Fields("Name"): the default member, then its own default member, with the index.
        if (!TryInvoke(handle, null, ValueMember, PropertyGet | Method, [], heap, out var inner, out error)) return false;
        if (!objects.IsInstance(inner, heap) || !TryHandle(inner, heap, out var innerHandle, out error)) return false;
        return TryInvoke(innerHandle, null, ValueMember, PropertyGet | Method, a[1..], heap, out result, out error);
    }

    /// <summary><c>x[k] = v</c>: the object's default member with <c>k</c>, assigned.</summary>
    public bool SetDefault(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = a[2];
        if (!TryHandle(a[0], heap, out var handle, out error)) return false;
        return TryPut(handle, null, ValueMember, [a[1], a[2]], heap, out error);
    }

    /// <summary><c>iterate()</c>: a <c>ComEnumerator</c> over the object's <c>_NewEnum</c>, which <c>foreach</c> visits.</summary>
    public bool Iterate(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryHandle(a[0], heap, out var handle, out error)) return false;
        var variant = stackalloc byte[VariantSize];
        if (!TryCall(handle, null, NewEnumMember, PropertyGet | Method, [], variant, heap, out error, out _)) return false;
        try
        {
            var type = WebLingoComValues.Type(variant);
            var unknown = *(nint*)(variant + 8);
            var interfaceId = EnumVariantInterface;
            nint enumerator;
            if (type is not (Unknown or Dispatch) || unknown == 0 || QueryInterface(unknown, &interfaceId, &enumerator) != Ok || enumerator == 0)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "This COM object is not a collection foreach can visit.");
                return false;
            }
            var enumeration = new WebLingoComEnumeration(enumerator);
            if (enumerators.TryMakeOwn(heap, out result, out error) && enumerators.TrySetHostObject(result, enumeration, heap, out error)) return true;
            enumeration.Dispose();
            return false;
        }
        finally
        {
            VariantClear(variant);
        }
    }

    /// <summary>An enumerator's <c>hasNext()</c>: whether it has another item, read ahead.</summary>
    public bool HasNext(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!enumerators.TryGetHostObject<WebLingoComEnumeration>(a[0], heap, out var enumeration, out error)) return false;
        ReadAhead(enumeration);
        result = WebLingoValue.FromBoolean(enumeration.HasItem);
        return true;
    }

    /// <summary>An enumerator's <c>next()</c>: its next item, WL3005 past its last.</summary>
    public bool Next(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!enumerators.TryGetHostObject<WebLingoComEnumeration>(a[0], heap, out var enumeration, out error)) return false;
        ReadAhead(enumeration);
        if (!enumeration.HasItem)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IndexOutOfRange, "The COM collection has no more items.");
            return false;
        }
        fixed (byte* item = enumeration.Ahead)
        {
            var read = _values.TryRead(item, heap, out result, out error);
            enumeration.ClearAhead();
            enumeration.Read = false;
            return read;
        }
    }

    private static void ReadAhead(WebLingoComEnumeration enumeration)
    {
        if (enumeration.Read || enumeration.Pointer == 0) return;
        enumeration.Read = true;
        fixed (byte* item = enumeration.Ahead)
        {
            new Span<byte>(item, VariantSize).Clear();
            uint fetched;
            enumeration.HasItem = WebLingoComInterop.Next(enumeration.Pointer, item, &fetched) == Ok && fetched == 1;
        }
    }

    /// <summary>A <c>ComObject</c>'s <c>dispose()</c>: its object released, once.</summary>
    public bool Dispose(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!objects.TryReleaseHostObject(a[0], heap, out var held, out error)) return false;
        (held as IDisposable)?.Dispose();
        return true;
    }

    /// <summary>A <c>ComEnumerator</c>'s <c>dispose()</c>.</summary>
    public bool DisposeEnumerator(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!enumerators.TryReleaseHostObject(a[0], heap, out var held, out error)) return false;
        (held as IDisposable)?.Dispose();
        return true;
    }

    private bool TryHandle(WebLingoValue instance, WebLingoHeap heap, out WebLingoComHandle handle, out WebLingoRuntimeError? error)
    {
        handle = null!;
        if (!objects.TryGetHostObject<WebLingoComHandle>(instance, heap, out var found, out error)) return false;
        handle = found;
        if (handle.Pointer != 0) return true;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "This ComObject has been disposed.");
        return false;
    }

    private static bool TryName(WebLingoValue value, string function, string what, WebLingoHeap heap, out string name, out WebLingoRuntimeError? error)
    {
        (name, error) = ("", null);
        if (value.Kind == WebLingoValueKind.String && heap.GetString(value).Length > 0)
        {
            name = heap.GetString(value);
            return true;
        }
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
            $"'{function}' takes {what}, a string, not {WebLingoValueFormatter.Describe(value, heap)}.");
        return false;
    }

    /// <summary>A member's number, from its name, kept for the object; a name it does not have WL3006.</summary>
    private static bool TryMember(WebLingoComHandle handle, string name, out int member, out WebLingoRuntimeError? error)
    {
        error = null;
        if (handle.Members.TryGetValue(name, out member)) return true;
        int id;
        var found = GetIdOfName(handle.Pointer, name, &id);
        if (found == Ok)
        {
            handle.Members[name] = member = id;
            return true;
        }
        if (found is UnknownName or MemberNotFound)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NoSuchMember, $"The COM object has no member '{name}'.");
            return false;
        }
        return Failed(found, "", $"its member '{name}' cannot be found", name, out error);
    }

    /// <summary>A member called, its result read as a value.</summary>
    private bool TryInvoke(WebLingoComHandle handle, string? name, int? member, ushort flags, ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error) =>
        TryInvoke(handle, name, member, flags, arguments, heap, out result, out error, out _);

    private bool TryInvoke(WebLingoComHandle handle, string? name, int? member, ushort flags, ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error, out int code)
    {
        result = WebLingoValue.Null;
        var variant = stackalloc byte[VariantSize];
        new Span<byte>(variant, VariantSize).Clear();
        if (!TryCall(handle, name, member, flags, arguments, variant, heap, out error, out code)) return false;
        try
        {
            return _values.TryRead(variant, heap, out result, out error);
        }
        finally
        {
            VariantClear(variant);
        }
    }

    /// <summary>A property assigned, the last argument its value: by value, or, for an object an object will not take so, by reference.</summary>
    private bool TryPut(WebLingoComHandle handle, string? name, int? member, ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap,
        out WebLingoRuntimeError? error)
    {
        var byReference = objects.IsInstance(arguments[^1], heap);
        if (TryCall(handle, name, member, byReference ? PropertyPutRef : PropertyPut, arguments, null, heap, out error, out var code, put: true)) return true;
        if (code != MemberNotFound) return false;
        return TryCall(handle, name, member, byReference ? PropertyPut : PropertyPutRef, arguments, null, heap, out error, out _, put: true);
    }

    /// <summary>
    /// <c>IDispatch::Invoke</c>, its arguments written last first, a property's new value named <c>DISPID_PROPERTYPUT</c>,
    /// its result into <paramref name="result"/>, if given; its failure a <c>ComError</c>, its code given.
    /// </summary>
    private bool TryCall(WebLingoComHandle handle, string? name, int? member, ushort flags, ReadOnlySpan<WebLingoValue> arguments, byte* result,
        WebLingoHeap heap, out WebLingoRuntimeError? error, out int code, bool put = false)
    {
        code = Ok;
        if (!TryEnter(out error)) return false;
        var id = member ?? 0;
        if (member is null && !TryMember(handle, name!, out id, out error)) return false;
        var count = arguments.Length;
        var written = stackalloc byte[Math.Max(1, count) * VariantSize];
        var ready = 0;
        try
        {
            for (; ready < count; ready++)
            {
                if (!_values.TryWrite(arguments[count - 1 - ready], heap, written + ready * VariantSize, out error)) return false;
            }
            var named = PropertyPutMember;
            var parameters = new DispatchParameters
            {
                Arguments = count == 0 ? null : written, ArgumentCount = (uint)count,
                NamedArguments = put ? &named : null, NamedArgumentCount = put ? 1u : 0u
            };
            var exception = new ExceptionInfo();
            uint wrongArgument;
            SetErrorInfo(0, 0); // No earlier failure's information is taken for this one's.
            code = Invoke(handle.Pointer, id, flags, &parameters, result, &exception, &wrongArgument);
            if (code == Ok) return true;
            var (number, source, description) = code == WebLingoComInterop.Exception ? Read(&exception) : (code, "", "");
            if (code != WebLingoComInterop.Exception) (source, description) = TakeErrorInfo();
            return Failed(number, source, description.TrimEnd(), name ?? "the default member", out error);
        }
        finally
        {
            for (var i = 0; i < ready; i++) VariantClear(written + i * VariantSize);
        }
    }

    /// <summary>
    /// A failure as a <c>ComError</c>, WL3012: its <c>number</c>, the <c>HRESULT</c>, its <c>source</c> and its
    /// <c>description</c>, each as COM gave it, "" where it gave none.
    /// </summary>
    private static bool Failed(int number, string source, string description, string what, out WebLingoRuntimeError? error)
    {
        // Its description as COM gave it, "" for none, which the message then puts in its own words.
        var text = description.Length > 0 ? description : Described(number);
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.ComFailure,
            $"'{what}' failed: {text.TrimEnd('.')} (0x{number:X8}{(source.Length > 0 ? ", " + source : "")}).")
        {
            Fields = [new("number", (long)number), new("source", source), new("description", description)]
        };
        return false;
    }

    private static string Described(int code) => code switch
    {
        TypeMismatch => "a value of the wrong type",
        BadParameterCount => "the wrong number of arguments",
        MemberNotFound => "no such member, or not for this use",
        ParameterNotFound => "an argument it needs is missing",
        unchecked((int)0x8002000A) => "a value out of range",
        unchecked((int)0x80020008) => "a value of a type it does not take",
        unchecked((int)0x80040154) => "the class is not registered",
        unchecked((int)0x800401F3) => "there is no such ProgID",
        unchecked((int)0x80070005) => "access is denied",
        _ => "the COM object failed"
    };
}
