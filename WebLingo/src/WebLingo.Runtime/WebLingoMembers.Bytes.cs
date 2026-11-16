using System.Runtime.CompilerServices;
using WebLingo.Core;
namespace WebLingo.Runtime;

// Bytes' members (specification, section 3.2, "Bytes"): b.length, b[i], an integer from 0 to 255, b.slice(start, end),
// b.text(charset), and the class's Bytes.from(value, charset), Bytes.concat(parts) and Bytes.fixedTimeEqual(a, b). Bytes
// are immutable, as strings are: b[i] = v is WL3001. Text is encoded and decoded by the core's own codec,
// WebLingoTextCodec, UTF-8 unless a charset is named.
public static partial class WebLingoMembers
{
    /// <summary><c>b[i]</c>: the byte at <c>i</c>, from 0, as an integer from 0 to 255; WL3005 outside the bytes.</summary>
    private static bool TryGetByte(WebLingoValue target, WebLingoValue index, WebLingoHeap heap, bool optional, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var data = heap.GetBytes(target);
        if (!TryIndex(index, data.Length, out var at, out error)) return Absent(optional, ref error);
        result = WebLingoValue.FromInt64(data[(int)at]);
        return true;
    }

    /// <summary><c>b.slice(start, end)</c> and <c>b.text(charset)</c>.</summary>
    private static bool TryCallBytes(WebLingoValue target, WebLingoMember member, string name, WebLingoValue[] values, int first, int count,
        WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var data = heap.GetBytes(target);
        if (member == WebLingoMember.Text)
        {
            if (count > 1) return WrongArgumentCount(name, 0, 1, count, out error);
            if (!TryCharset(count == 1 ? values[first] : WebLingoValue.Null, heap, out var charset, out error)) return false;
            return heap.TryAllocateString(WebLingoTextCodec.Decode(data, charset), out result, out error);
        }
        // slice(start, end): from start to end, end left out the bytes' end; WL3005 for a range outside them.
        if (count is < 1 or > 2) return WrongArgumentCount(name, 1, 2, count, out error);
        var start = values[first];
        var end = count == 2 ? values[first + 1] : WebLingoValue.FromInt64(data.Length);
        if (start.Kind != WebLingoValueKind.Int64 || end.Kind != WebLingoValueKind.Int64)
        {
            var bad = start.Kind != WebLingoValueKind.Int64 ? start : end;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'slice' takes integers, not {WebLingoValueFormatter.Describe(bad, heap)}.");
            return false;
        }
        var (from, to) = (start.AsInt64, end.AsInt64);
        if (from < 0 || to > data.Length || from > to)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IndexOutOfRange,
                $"The range {from} to {to} is not within the bytes, 0 to {data.Length}.");
            return false;
        }
        return heap.TryAllocateBytes(data[(int)from..(int)to], out result, out error);
    }

    /// <summary>
    /// <c>Bytes.from(value, charset)</c>, <c>Bytes.concat(parts)</c> and <c>Bytes.fixedTimeEqual(a, b)</c>, the methods of
    /// the class <c>Bytes</c>.
    /// </summary>
    private static bool TryCallBytesClass(WebLingoMember member, string name, WebLingoValue[] values, int first, int count, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        switch (member)
        {
            case WebLingoMember.From:
            {
                // A string, encoded in a charset, UTF-8 unless one is named; an array of integers from 0 to 255; or bytes,
                // which are themselves.
                if (count is < 1 or > 2) return WrongArgumentCount(name, 1, 2, count, out error);
                var value = values[first];
                if (value.Kind == WebLingoValueKind.String)
                {
                    if (!TryCharset(count == 2 ? values[first + 1] : WebLingoValue.Null, heap, out var charset, out error)) return false;
                    return heap.TryAllocateBytes(WebLingoTextCodec.Encode(heap.GetString(value), charset), out result, out error);
                }
                if (count == 2)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "'from' takes a charset only with a string.");
                    return false;
                }
                if (value.Kind == WebLingoValueKind.Bytes)
                {
                    (result, error) = (value, null);
                    return true;
                }
                if (value.Kind == WebLingoValueKind.Array)
                {
                    var elements = heap.GetArray(value);
                    var data = new byte[elements.Count];
                    for (var i = 0; i < data.Length; i++)
                    {
                        if (!TryByte(elements[i], heap, "'from' takes an array of integers from 0 to 255", out data[i], out error)) return false;
                    }
                    return heap.TryAllocateBytes(data, out result, out error);
                }
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'from' makes bytes from a string, an array of integers from 0 to 255, or bytes, not {WebLingoValueFormatter.Describe(value, heap)}.");
                return false;
            }
            case WebLingoMember.Concat:
            {
                // An array of bytes and integers from 0 to 255, joined into new bytes, each copied once.
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (values[first].Kind != WebLingoValueKind.Array)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                        $"'concat' takes an array of bytes, not {WebLingoValueFormatter.Describe(values[first], heap)}.");
                    return false;
                }
                var parts = heap.GetArray(values[first]);
                var length = 0L;
                foreach (var part in parts)
                {
                    if (part.Kind == WebLingoValueKind.Bytes) length += heap.GetBytes(part).Length;
                    else if (TryByte(part, heap, "'concat' takes bytes and integers from 0 to 255", out _, out error)) length++;
                    else return false;
                }
                if (length > int.MaxValue)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BudgetExceeded, $"{length} bytes do not fit in one value.");
                    return false;
                }
                var whole = new byte[length];
                var at = 0;
                foreach (var part in parts)
                {
                    if (part.Kind == WebLingoValueKind.Bytes)
                    {
                        var data = heap.GetBytes(part);
                        data.CopyTo(whole, at);
                        at += data.Length;
                    }
                    else
                    {
                        whole[at++] = (byte)part.AsInt64;
                    }
                }
                return heap.TryAllocateBytes(whole, out result, out error);
            }
            default:
            {
                // fixedTimeEqual(a, b): whether two bytes are equal, taking as long whatever their contents, for secrets.
                if (count != 2) return WrongArgumentCount(name, 2, count, out error);
                var (a, b) = (values[first], values[first + 1]);
                if (a.Kind != WebLingoValueKind.Bytes || b.Kind != WebLingoValueKind.Bytes)
                {
                    var bad = a.Kind != WebLingoValueKind.Bytes ? a : b;
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                        $"'fixedTimeEqual' compares bytes, not {WebLingoValueFormatter.Describe(bad, heap)}.");
                    return false;
                }
                (result, error) = (WebLingoValue.FromBoolean(FixedTimeEqual(heap.GetBytes(a), heap.GetBytes(b))), null);
                return true;
            }
        }
    }

    /// <summary>
    /// Whether two byte arrays are equal, in a time that depends on their lengths, which are not secret, and never on where
    /// they differ, so that a secret compared with a guess gives nothing away: every byte is compared, and nothing branches
    /// on what a byte is.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static bool FixedTimeEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        var difference = 0;
        for (var i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
        return difference == 0;
    }

    /// <summary>A byte, an integer from 0 to 255; WL3001 for anything else, its message <paramref name="what"/> and the value.</summary>
    private static bool TryByte(WebLingoValue value, WebLingoHeap heap, string what, out byte result, out WebLingoRuntimeError? error)
    {
        (result, error) = (0, null);
        if (value.Kind == WebLingoValueKind.Int64 && value.AsInt64 is >= 0 and <= 255)
        {
            result = (byte)value.AsInt64;
            return true;
        }
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"{what}, not {WebLingoValueFormatter.Describe(value, heap)}.");
        return false;
    }

    /// <summary>A charset by name, or UTF-8 for <c>null</c>; WL3001 for one the core does not read and write.</summary>
    private static bool TryCharset(WebLingoValue name, WebLingoHeap heap, out WebLingoCharset charset, out WebLingoRuntimeError? error)
    {
        (charset, error) = (WebLingoCharset.Utf8, null);
        if (name.Kind == WebLingoValueKind.Null) return true;
        if (name.Kind == WebLingoValueKind.String && WebLingoTextCodec.TryFind(heap.GetString(name), out charset)) return true;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, name.Kind == WebLingoValueKind.String
            ? $"'{heap.GetString(name)}' is not a charset WebLingo reads: utf-8, utf-16, utf-16be, us-ascii or iso-8859-1."
            : $"A charset is named by a string, not {WebLingoValueFormatter.Describe(name, heap)}.");
        return false;
    }

    private static bool WrongArgumentCount(string name, int least, int most, int count, out WebLingoRuntimeError? error)
    {
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.WrongArgumentCount, $"'{name}' takes {least} to {most} arguments, not {count}.");
        return false;
    }
}
