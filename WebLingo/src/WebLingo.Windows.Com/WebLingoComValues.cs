using WebLingo.Core;
using WebLingo.Runtime;
using static WebLingo.Windows.Com.WebLingoComInterop;
namespace WebLingo.Windows.Com;

/// <summary>
/// WebLingo's values as COM's <c>VARIANT</c>s, and back (specification, section 3.10, "Values", and the owner's answers of
/// 17 November 2026). Going out: <c>null</c> as <c>VT_NULL</c>; a Boolean as <c>VT_BOOL</c>; an integer as <c>VT_I4</c>
/// when it fits, else <c>VT_I8</c>; a number as <c>VT_R8</c>; a decimal as <c>VT_DECIMAL</c>; a string as
/// <c>VT_BSTR</c>; bytes as <c>VT_ARRAY | VT_UI1</c>; a date as <c>VT_DATE</c>, its time on the server's clocks; an array
/// as a <c>SAFEARRAY</c> of <c>VARIANT</c>s; a <c>ComObject</c> as <c>VT_DISPATCH</c>; anything else WL3001. Coming back:
/// <c>VT_EMPTY</c> and <c>VT_NULL</c> as <c>null</c>; <c>VT_BOOL</c> a Boolean; every integer type an integer, an
/// unsigned 64-bit one beyond int64 WL3002; <c>VT_R4</c> and <c>VT_R8</c> numbers; <c>VT_CY</c> and <c>VT_DECIMAL</c>
/// decimals; <c>VT_BSTR</c> a string; <c>VT_DATE</c> a date in the server's zone; <c>VT_DISPATCH</c>, and
/// <c>VT_UNKNOWN</c> that has <c>IDispatch</c>, a <c>ComObject</c>; <c>VT_ARRAY | VT_UI1</c> bytes; any other
/// <c>SAFEARRAY</c> an array, one of several dimensions arrays of arrays, its first dimension outermost.
/// </summary>
internal sealed unsafe class WebLingoComValues(WebLingoNativeClass objects)
{
    /// <summary>Writes a value into the <c>VARIANT</c> at <paramref name="variant"/>, which the caller clears once used.</summary>
    public bool TryWrite(WebLingoValue value, WebLingoHeap heap, byte* variant, out WebLingoRuntimeError? error)
    {
        error = null;
        new Span<byte>(variant, VariantSize).Clear();
        switch (value.Kind)
        {
            case WebLingoValueKind.Null:
                Type(variant) = Null;
                return true;
            case WebLingoValueKind.Boolean:
                Type(variant) = Bool;
                *(short*)(variant + 8) = (short)(value.AsBoolean ? -1 : 0);
                return true;
            case WebLingoValueKind.Int64 when value.AsInt64 is >= int.MinValue and <= int.MaxValue:
                Type(variant) = I4;
                *(int*)(variant + 8) = (int)value.AsInt64;
                return true;
            case WebLingoValueKind.Int64:
                Type(variant) = I8;
                *(long*)(variant + 8) = value.AsInt64;
                return true;
            case WebLingoValueKind.Binary64:
                Type(variant) = R8;
                *(double*)(variant + 8) = value.AsBinary64;
                return true;
            case WebLingoValueKind.Decimal:
                WriteDecimal(heap.GetDecimal(value), variant);
                return true;
            case WebLingoValueKind.String:
                var text = heap.GetString(value);
                Type(variant) = Bstr;
                *(nint*)(variant + 8) = SysAllocStringLen(text, (uint)text.Length);
                return true;
            case WebLingoValueKind.Bytes:
                var data = heap.GetBytes(value);
                var bytes = SafeArrayCreateVector(UI1, 0, (uint)data.Length);
                void* into;
                if (bytes == 0 || SafeArrayAccessData(bytes, &into) != Ok) return Unwritable(value, heap, out error);
                data.CopyTo(new Span<byte>(into, data.Length));
                SafeArrayUnaccessData(bytes);
                Type(variant) = (ushort)(ArrayFlag | UI1);
                *(nint*)(variant + 8) = bytes;
                return true;
            case WebLingoValueKind.DateTime:
                var offset = heap.TimeZone?.OffsetMinutesAt(value.UnixMilliseconds) ?? 0;
                var local = DateTime.UnixEpoch.AddMilliseconds(value.UnixMilliseconds + offset * 60_000L);
                Type(variant) = Date;
                *(double*)(variant + 8) = local.ToOADate();
                return true;
            case WebLingoValueKind.Array:
                return TryWriteArray(heap.GetArray(value), heap, variant, out error);
            case WebLingoValueKind.Instance when objects.IsInstance(value, heap):
                if (!objects.TryGetHostObject<WebLingoComHandle>(value, heap, out var handle, out error)) return false;
                AddRef(handle.Pointer);
                Type(variant) = Dispatch;
                *(nint*)(variant + 8) = handle.Pointer;
                return true;
            default:
                return Unwritable(value, heap, out error);
        }
    }

    private bool TryWriteArray(List<WebLingoValue> elements, WebLingoHeap heap, byte* variant, out WebLingoRuntimeError? error)
    {
        var array = SafeArrayCreateVector(Variant, 0, (uint)elements.Count);
        if (array == 0) return Unwritable(WebLingoValue.Null, heap, out error);
        var element = stackalloc byte[VariantSize];
        for (var i = 0; i < elements.Count; i++)
        {
            if (!TryWrite(elements[i], heap, element, out error))
            {
                SafeArrayDestroy(array);
                return false;
            }
            var index = i;
            SafeArrayPutElement(array, &index, element); // It copies the element, which is then cleared.
            VariantClear(element);
        }
        Type(variant) = (ushort)(ArrayFlag | Variant);
        *(nint*)(variant + 8) = array;
        error = null;
        return true;
    }

    private static bool Unwritable(WebLingoValue value, WebLingoHeap heap, out WebLingoRuntimeError? error)
    {
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
            $"{Capitalized(WebLingoValueFormatter.Describe(value, heap))} cannot be given to a COM object.");
        return false;
    }

    /// <summary>A decimal as a <c>VT_DECIMAL</c>, which fills the whole <c>VARIANT</c>, its type where a <c>DECIMAL</c>'s reserved field is.</summary>
    private static void WriteDecimal(decimal value, byte* variant)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        Type(variant) = DecimalType;
        variant[2] = (byte)((bits[3] >> 16) & 0xFF);
        variant[3] = (byte)(bits[3] < 0 ? 0x80 : 0);
        *(uint*)(variant + 4) = (uint)bits[2];
        *(ulong*)(variant + 8) = (uint)bits[0] | ((ulong)(uint)bits[1] << 32);
    }

    private static decimal ReadDecimal(byte* variant)
    {
        var low = *(ulong*)(variant + 8);
        return new decimal((int)(uint)low, (int)(uint)(low >> 32), (int)*(uint*)(variant + 4), (variant[3] & 0x80) != 0, variant[2]);
    }

    /// <summary>The type of the <c>VARIANT</c> at <paramref name="variant"/>.</summary>
    public static ref ushort Type(byte* variant) => ref *(ushort*)variant;

    /// <summary>The value the <c>VARIANT</c> at <paramref name="variant"/> holds, which stays the caller's to clear.</summary>
    public bool TryRead(byte* variant, WebLingoHeap heap, out WebLingoValue value, out WebLingoRuntimeError? error)
    {
        (value, error) = (WebLingoValue.Null, null);
        var type = Type(variant);
        if ((type & ByRefFlag) != 0) return TryReadByRef(variant, heap, out value, out error);
        if ((type & ArrayFlag) != 0) return TryReadArray(*(nint*)(variant + 8), (ushort)(type & ~ArrayFlag), heap, out value, out error);
        var at = variant + 8;
        switch (type)
        {
            case Empty or Null:
                return true;
            case Bool:
                value = WebLingoValue.FromBoolean(*(short*)at != 0);
                return true;
            case I1:
                value = WebLingoValue.FromInt64(*(sbyte*)at);
                return true;
            case UI1:
                value = WebLingoValue.FromInt64(*at);
                return true;
            case I2:
                value = WebLingoValue.FromInt64(*(short*)at);
                return true;
            case UI2:
                value = WebLingoValue.FromInt64(*(ushort*)at);
                return true;
            case I4 or Int:
                value = WebLingoValue.FromInt64(*(int*)at);
                return true;
            case UI4 or UInt:
                value = WebLingoValue.FromInt64(*(uint*)at);
                return true;
            case I8:
                value = WebLingoValue.FromInt64(*(long*)at);
                return true;
            case UI8:
                if (*(ulong*)at > long.MaxValue)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IntegerOverflow,
                        $"A COM object gave {*(ulong*)at}, beyond what an integer holds.");
                    return false;
                }
                value = WebLingoValue.FromInt64((long)*(ulong*)at);
                return true;
            case R4:
                value = WebLingoValue.FromBinary64(*(float*)at);
                return true;
            case R8:
                value = WebLingoValue.FromBinary64(*(double*)at);
                return true;
            case Currency:
                return heap.TryAllocateDecimal(*(long*)at / 10_000m, out value, out error);
            case DecimalType:
                return heap.TryAllocateDecimal(ReadDecimal(variant), out value, out error);
            case Bstr:
                var bstr = *(nint*)at;
                return heap.TryAllocateString(bstr == 0 ? "" : new string((char*)bstr, 0, (int)SysStringLen(bstr)), out value, out error);
            case Date:
                var local = DateTime.FromOADate(*(double*)at);
                var localMilliseconds = (long)(local - DateTime.UnixEpoch).TotalMilliseconds;
                var offset = heap.TimeZone?.OffsetMinutesOfLocal(localMilliseconds) ?? 0;
                value = WebLingoValue.FromDateTime(localMilliseconds - offset * 60_000L, offset);
                return true;
            case Error:
                // An optional argument COM left out, or an error's code.
                if (*(int*)at != ParameterNotFound) value = WebLingoValue.FromInt64(*(int*)at);
                return true;
            case Dispatch or Unknown:
                return TryReadObject(*(nint*)at, type == Unknown, heap, out value, out error);
            default:
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"A COM object gave a value of a type WebLingo does not read, VARIANT type {type}.");
                return false;
        }
    }

    /// <summary>A <c>VT_BYREF</c> value, read as the value it points to.</summary>
    private bool TryReadByRef(byte* variant, WebLingoHeap heap, out WebLingoValue value, out WebLingoRuntimeError? error)
    {
        var type = (ushort)(Type(variant) & ~ByRefFlag);
        var pointer = *(byte**)(variant + 8);
        if (type == Variant) return TryRead(pointer, heap, out value, out error);
        var copy = stackalloc byte[VariantSize];
        new Span<byte>(copy, VariantSize).Clear();
        if (type == DecimalType) Buffer.MemoryCopy(pointer, copy, 16, 16);
        else *(long*)(copy + 8) = (type & ArrayFlag) != 0 ? *(nint*)pointer : *(long*)pointer;
        Type(copy) = type;
        return TryRead(copy, heap, out value, out error); // Not cleared: what it points to is the object's.
    }

    /// <summary>An object, as a <c>ComObject</c> holding its <c>IDispatch</c>, which it adds a reference to; null for none.</summary>
    private bool TryReadObject(nint pointer, bool unknown, WebLingoHeap heap, out WebLingoValue value, out WebLingoRuntimeError? error)
    {
        (value, error) = (WebLingoValue.Null, null);
        if (pointer == 0) return true;
        nint dispatch;
        if (unknown)
        {
            var id = DispatchInterface;
            if (QueryInterface(pointer, &id, &dispatch) != Ok || dispatch == 0)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    "A COM object gave an object without IDispatch, whose members WebLingo cannot reach.");
                return false;
            }
        }
        else
        {
            dispatch = pointer;
            AddRef(dispatch);
        }
        return TryMakeObject(dispatch, heap, out value, out error);
    }

    /// <summary>A new <c>ComObject</c> holding <paramref name="dispatch"/>, whose reference it takes, releasing it if it cannot.</summary>
    public bool TryMakeObject(nint dispatch, WebLingoHeap heap, out WebLingoValue value, out WebLingoRuntimeError? error)
    {
        var handle = new WebLingoComHandle(dispatch);
        if (objects.TryMakeOwn(heap, out value, out error) && objects.TrySetHostObject(value, handle, heap, out error)) return true;
        handle.Dispose();
        return false;
    }

    /// <summary>A <c>SAFEARRAY</c> of elements of <paramref name="type"/>: bytes for a one-dimensional one of bytes, else arrays.</summary>
    private bool TryReadArray(nint array, ushort type, WebLingoHeap heap, out WebLingoValue value, out WebLingoRuntimeError? error)
    {
        (value, error) = (WebLingoValue.Null, null);
        if (array == 0) return true;
        var dimensions = SafeArrayGetDim(array);
        var lower = new int[dimensions];
        var upper = new int[dimensions];
        for (var d = 0; d < dimensions; d++)
        {
            int low, high;
            SafeArrayGetLBound(array, (uint)(d + 1), &low);
            SafeArrayGetUBound(array, (uint)(d + 1), &high);
            (lower[d], upper[d]) = (low, high);
        }
        if (dimensions == 1 && type == UI1)
        {
            var count = Math.Max(0, upper[0] - lower[0] + 1);
            void* data;
            if (SafeArrayAccessData(array, &data) != Ok) return Unreadable(out error);
            var bytes = new ReadOnlySpan<byte>(data, count).ToArray();
            SafeArrayUnaccessData(array);
            return heap.TryAllocateBytes(bytes, out value, out error);
        }
        if (dimensions == 0) return heap.TryAllocateArray([], 0, 0, out value, out error);
        // SafeArrayGetElement takes the indices in the dimensions' order, the first dimension's first.
        var indices = new int[dimensions];
        return TryReadDimension(array, type, 0, lower, upper, indices, heap, out value, out error);
    }

    private bool TryReadDimension(nint array, ushort type, int dimension, int[] lower, int[] upper, int[] indices, WebLingoHeap heap,
        out WebLingoValue value, out WebLingoRuntimeError? error)
    {
        var count = Math.Max(0, upper[dimension] - lower[dimension] + 1);
        var elements = new WebLingoValue[count];
        heap.AddRoots(elements); // While the elements are made, which may collect.
        try
        {
            for (var i = 0; i < count; i++)
            {
                indices[dimension] = lower[dimension] + i;
                if (dimension + 1 < lower.Length)
                {
                    if (!TryReadDimension(array, type, dimension + 1, lower, upper, indices, heap, out elements[i], out error))
                    {
                        value = WebLingoValue.Null;
                        return false;
                    }
                    continue;
                }
                if (!TryReadElement(array, type, indices, heap, out elements[i], out error))
                {
                    value = WebLingoValue.Null;
                    return false;
                }
            }
            return heap.TryAllocateArray(elements, 0, count, out value, out error);
        }
        finally
        {
            heap.RemoveLastRoots(elements);
        }
    }

    private bool TryReadElement(nint array, ushort type, int[] indices, WebLingoHeap heap, out WebLingoValue value, out WebLingoRuntimeError? error)
    {
        var element = stackalloc byte[VariantSize];
        new Span<byte>(element, VariantSize).Clear();
        fixed (int* at = indices)
        {
            var copied = type switch
            {
                Variant => SafeArrayGetElement(array, at, element),
                DecimalType => SafeArrayGetElement(array, at, element),
                _ => SafeArrayGetElement(array, at, element + 8)
            };
            if (copied != Ok)
            {
                value = WebLingoValue.Null;
                return Unreadable(out error);
            }
        }
        if (type != Variant) Type(element) = type;
        try
        {
            return TryRead(element, heap, out value, out error);
        }
        finally
        {
            VariantClear(element); // The copy SafeArrayGetElement made: a string's, an object's reference.
        }
    }

    private static bool Unreadable(out WebLingoRuntimeError? error)
    {
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "A COM object gave an array WebLingo cannot read.");
        return false;
    }

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

/// <summary>
/// A <c>ComObject</c>'s host object (section 3.9): an object's <c>IDispatch</c>, released once, by <c>dispose()</c>, the
/// collector or the execution's end, on the worker that made it; and the members' numbers it has found, by their names,
/// as COM compares them, ignoring case.
/// </summary>
internal sealed class WebLingoComHandle(nint pointer) : IDisposable
{
    public nint Pointer { get; private set; } = pointer;

    public Dictionary<string, int> Members { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (Pointer == 0) return;
        WebLingoComInterop.Release(Pointer);
        Pointer = 0;
    }
}

/// <summary>
/// A <c>ComEnumerator</c>'s host object: a collection's <c>IEnumVARIANT</c>, and the item it has read ahead, to answer
/// <c>hasNext()</c> (section 3.10, "Collections").
/// </summary>
internal sealed unsafe class WebLingoComEnumeration(nint pointer) : IDisposable
{
    private readonly byte[] _ahead = new byte[WebLingoComInterop.VariantSize];

    public nint Pointer { get; private set; } = pointer;
    public bool Read { get; set; }
    public bool HasItem { get; set; }

    /// <summary>The item read ahead, a <c>VARIANT</c>.</summary>
    public byte[] Ahead => _ahead;

    public void Dispose()
    {
        ClearAhead();
        if (Pointer == 0) return;
        WebLingoComInterop.Release(Pointer);
        Pointer = 0;
    }

    public void ClearAhead()
    {
        if (!HasItem) return;
        fixed (byte* item = _ahead) WebLingoComInterop.VariantClear(item);
        HasItem = false;
    }
}
