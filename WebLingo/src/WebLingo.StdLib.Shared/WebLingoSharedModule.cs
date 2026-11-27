using System.Globalization;
using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Shared;

/// <summary>
/// <c>std:shared</c>, a standard module (specification, section 6.12, "Memory a site's requests share"; the owner's
/// answers, 27 November 2026, A1 to A4 (a) of "Memory a Site's Requests Share"), a native module installed with the runtime
/// in the <c>std</c> scheme's folder as <c>shared.dll</c>: the memory a site's requests share, or, under <c>weblingo</c>, a
/// program and its workers, through its host's store (<see cref="WebLingoHeap.Shared"/>), so that the portable core holds
/// no lock. Its class <c>Shared</c>, its members static: <c>get(key)</c>, <c>null</c> where there is none;
/// <c>set(key, value)</c>, or <c>set(key, value, seconds)</c>, kept that long; <c>remove(key)</c>, whether it was there;
/// and two that read and change in one step, which no other request comes between, <c>add(key, n)</c>, an integer added
/// and the new total given, and <c>take(key)</c>, the value given and removed. A value is copied in and out as a worker's
/// message is (<see cref="WebLingoMessage.TryCopyData"/>), so that what one request reads is a copy no other can change:
/// <c>null</c>, Booleans, numbers, decimals, strings, bytes, dates, and arrays, maps and sets of them; an integer kept as
/// itself, so that <c>add</c> adds to it. A key is a string of at most <see cref="MaxKey"/> characters; a <c>set</c> or an
/// <c>add</c> past the store's most a <c>SharedMemoryError</c> (WL3025), which a script may catch.
/// </summary>
public sealed class WebLingoSharedModule : IWebLingoNativeModule
{
    /// <summary>A key's most characters, Unicode scalar values (A3 (a)).</summary>
    public const int MaxKey = 1024;

    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Class("Shared")
            .DeclareStaticMethod("get", 1, Get)
            .DeclareStaticMethod("set", 3, 1, Set)
            .DeclareStaticMethod("remove", 1, Remove)
            .DeclareStaticMethod("add", 2, Add)
            .DeclareStaticMethod("take", 1, Take);
    }

    private static bool Get(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        return TryStore(heap, out var store, out error) && TryKey("get", 0, 1, a[0], heap, out var key, out error)
               && TryRead(store.Get(key), heap, out result, out error);
    }

    private static bool Take(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        return TryStore(heap, out var store, out error) && TryKey("take", 0, 1, a[0], heap, out var key, out error)
               && TryRead(store.Take(key), heap, out result, out error);
    }

    private static bool Remove(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryStore(heap, out var store, out error) || !TryKey("remove", 0, 1, a[0], heap, out var key, out error)) return false;
        result = WebLingoValue.FromBoolean(store.Remove(key));
        return true;
    }

    /// <summary><c>set(key, value, seconds)</c>: the value copied and kept, for that many seconds where they are given.</summary>
    private static bool Set(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryStore(heap, out var store, out error) || !TryKey("set", 0, 3, a[0], heap, out var key, out error)) return false;
        double? seconds = null;
        if (a[2].Kind != WebLingoValueKind.Null)
        {
            seconds = a[2].Kind switch
            {
                WebLingoValueKind.Int64 => a[2].AsInt64,
                WebLingoValueKind.Binary64 => a[2].AsBinary64,
                _ => null
            };
            if (seconds is not (> 0 and < double.PositiveInfinity))
            {
                error = new WebLingoArgument("set", 2, 3, "the seconds", "a number of seconds more than 0, or null to keep the value until it is removed")
                    .Wrong(a[2], heap);
                return false;
            }
        }
        object kept;
        long size;
        if (a[1].Kind == WebLingoValueKind.Int64)
        {
            (kept, size) = (a[1].AsInt64, 0);
        }
        else
        {
            if (!WebLingoMessage.TryCopyData(heap, a[1], "A shared value", out var message, out error)) return false;
            (kept, size) = (message!, message!.Size);
        }
        if (store.TrySet(key, kept, size, seconds)) return true;
        error = Full(store, $"'{key}''s value");
        return false;
    }

    /// <summary><c>add(key, n)</c>: <c>n</c> added to the integer kept under the key, or to 0, and the total given.</summary>
    private static bool Add(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryStore(heap, out var store, out error) || !TryKey("add", 0, 2, a[0], heap, out var key, out error)) return false;
        if (a[1].Kind != WebLingoValueKind.Int64)
        {
            error = new WebLingoArgument("add", 1, 2, "the amount", "an integer").Wrong(a[1], heap);
            return false;
        }
        switch (store.Add(key, a[1].AsInt64, out var total))
        {
            case WebLingoSharedAdded.Added:
                result = WebLingoValue.FromInt64(total);
                return true;
            case WebLingoSharedAdded.NotAnInteger:
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'add' adds to an integer, and the key '{key}' holds another kind of value.");
                return false;
            case WebLingoSharedAdded.Overflow:
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IntegerOverflow,
                    $"'add' gives an integer beyond int64's range for the key '{key}'.");
                return false;
            default:
                error = Full(store, $"the key '{key}'");
                return false;
        }
    }

    /// <summary>What the store gave, made anew in the heap: an integer as itself, a copy as it was copied, null as null.</summary>
    private static bool TryRead(object? kept, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        (result, error) = (WebLingoValue.Null, null);
        return kept switch
        {
            null => true,
            long integer => Made(WebLingoValue.FromInt64(integer), out result),
            WebLingoMessage message => message.TryRead(heap, WebLingoValue.Null, out result, out error),
            _ => Wrong("The shared memory holds a value its module cannot read.", out error)
        };
    }

    private static bool Made(WebLingoValue value, out WebLingoValue result)
    {
        result = value;
        return true;
    }

    /// <summary>A key, a string of at most <see cref="MaxKey"/> characters; WL3001 for anything else.</summary>
    private static bool TryKey(string member, int position, int of, WebLingoValue value, WebLingoHeap heap, out string key,
        out WebLingoRuntimeError? error)
    {
        (key, error) = ("", null);
        var argument = new WebLingoArgument(member, position, of, "the key", "a string of at most 1,024 characters");
        if (value.Kind != WebLingoValueKind.String)
        {
            error = argument.Wrong(value, heap);
            return false;
        }
        var length = heap.ScalarCount(value);
        if (length > MaxKey)
        {
            error = argument.Wrong($"a string of {length.ToString("N0", CultureInfo.InvariantCulture)} characters");
            return false;
        }
        key = heap.GetString(value);
        return true;
    }

    private static bool TryStore(WebLingoHeap heap, out IWebLingoSharedStore store, out WebLingoRuntimeError? error)
    {
        (store, error) = (heap.Shared!, null);
        return heap.Shared is not null || Wrong("This host keeps no shared memory.", out error);
    }

    /// <summary>WL3025: what would not fit, and the store's most.</summary>
    private static WebLingoRuntimeError Full(IWebLingoSharedStore store, string what) =>
        new(WebLingoDiagnosticCodes.SharedMemoryFull, store.Most is { } most
            ? $"The shared memory is full: {what} would make it hold more than its most, {Megabytes(most)}."
            : $"The shared memory is full: {what} does not fit.");

    private static string Megabytes(long bytes) =>
        bytes % (1024 * 1024) == 0
            ? (bytes / (1024 * 1024)).ToString("N0", CultureInfo.InvariantCulture) + " MB"
            : bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes";

    private static bool Wrong(string message, out WebLingoRuntimeError? error)
    {
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, message);
        return false;
    }
}
