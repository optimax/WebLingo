using System.Runtime.CompilerServices;
using WebLingo.Core;
namespace WebLingo.Runtime;

// Bytes' members (specification, section 3.2, "Bytes"): b.length, b[i], an integer from 0 to 255, b.slice(start, end),
// b.text(charset), b.toHex() and b.toBase64(urlSafe), and the class's Bytes.from(value, charset), Bytes.concat(parts),
// Bytes.fixedTimeEqual(a, b), Bytes.fromHex(text) and Bytes.fromBase64(text, urlSafe). Bytes are immutable, as strings
// are: b[i] = v is WL3001. Text is encoded and decoded by the core's own codec, WebLingoTextCodec, UTF-8 unless a charset
// is named; hexadecimal and Base64 by WebLingoBinaryText.
public static partial class WebLingoMembers
{
    private static bool IsBytesMethod(WebLingoMember member) =>
        member is WebLingoMember.Slice or WebLingoMember.Text or WebLingoMember.ToHex or WebLingoMember.ToBase64;

    private static bool IsBytesClassMethod(WebLingoMember member) =>
        member is WebLingoMember.From or WebLingoMember.Concat or WebLingoMember.FixedTimeEqual or WebLingoMember.FromHex
            or WebLingoMember.FromBase64;

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

    /// <summary><c>b.slice(start, end)</c>, <c>b.text(charset)</c>, <c>b.toHex()</c> and <c>b.toBase64(urlSafe)</c>.</summary>
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
        if (member == WebLingoMember.ToHex)
        {
            if (count != 0) return WrongArgumentCount(name, 0, count, out error);
            return heap.TryAllocateString(WebLingoBinaryText.ToHex(data), out result, out error);
        }
        if (member == WebLingoMember.ToBase64)
        {
            // toBase64(urlSafe): RFC 4648's standard alphabet, padded, or, given true, its URL-safe one, unpadded.
            if (count > 1) return WrongArgumentCount(name, 0, 1, count, out error);
            if (!TryUrlSafe(values, first, count == 1, name, heap, out var urlSafe, out error)) return false;
            return heap.TryAllocateString(WebLingoBinaryText.ToBase64(data, urlSafe), out result, out error);
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
    /// <c>Bytes.from(value, charset)</c>, <c>Bytes.concat(parts)</c>, <c>Bytes.fixedTimeEqual(a, b)</c>,
    /// <c>Bytes.fromHex(text)</c> and <c>Bytes.fromBase64(text, urlSafe)</c>, the methods of the class <c>Bytes</c>.
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
            case WebLingoMember.FromHex:
            case WebLingoMember.FromBase64:
            {
                // fromHex(text) and fromBase64(text, urlSafe): the bytes the text writes, WL3001 for text that writes none.
                var hex = member == WebLingoMember.FromHex;
                if (hex ? count != 1 : count is < 1 or > 2)
                {
                    return hex ? WrongArgumentCount(name, 1, count, out error) : WrongArgumentCount(name, 1, 2, count, out error);
                }
                if (values[first].Kind != WebLingoValueKind.String)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                        $"'{name}' reads a string, not {WebLingoValueFormatter.Describe(values[first], heap)}.");
                    return false;
                }
                var text = heap.GetString(values[first]);
                var urlSafe = false;
                if (!hex && !TryUrlSafe(values, first + 1, count == 2, name, heap, out urlSafe, out error)) return false;
                if (hex ? WebLingoBinaryText.TryFromHex(text, out var read) : WebLingoBinaryText.TryFromBase64(text, urlSafe, out read))
                {
                    return heap.TryAllocateBytes(read, out result, out error);
                }
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, hex
                    ? $"\"{text}\" is not hexadecimal: two digits, 0 to 9 and a to f, for each byte."
                    : urlSafe
                        ? $"\"{text}\" is not URL-safe Base64: A to Z, a to z, 0 to 9, - and _, padded with = or not."
                        : $"\"{text}\" is not Base64: A to Z, a to z, 0 to 9, + and /, padded with = to a multiple of four.");
                return false;
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

    /// <summary>Base64's <c>urlSafe</c>, a Boolean, at <paramref name="at"/> if <paramref name="given"/>, else <c>false</c>.</summary>
    private static bool TryUrlSafe(WebLingoValue[] values, int at, bool given, string name, WebLingoHeap heap, out bool urlSafe,
        out WebLingoRuntimeError? error)
    {
        (urlSafe, error) = (false, null);
        if (!given) return true;
        if (values[at].Kind == WebLingoValueKind.Boolean)
        {
            urlSafe = values[at].AsBoolean;
            return true;
        }
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
            $"'{name}' takes whether the URL-safe alphabet is meant, a Boolean, not {WebLingoValueFormatter.Describe(values[at], heap)}.");
        return false;
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

    /// <summary>
    /// A <c>BytesBuilder</c>'s methods (section 3.2, "Bytes"), native since 18 November 2026, as bytes' own are: <c>add(value)</c>,
    /// bytes, an integer from 0 to 255, or a string, as UTF-8, at its end, giving the builder, so that adds chain; and
    /// <c>toBytes()</c>, its bytes so far, as new bytes, which no later add changes.
    /// </summary>
    private static bool TryCallBytesBuilder(WebLingoValue target, WebLingoMember member, string name, WebLingoValue[] values, int first,
        int count, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (member == WebLingoMember.ToBytes)
        {
            if (count != 0) return WrongArgumentCount(name, 0, count, out error);
            return heap.TryBuilderToBytes(target, out result, out error);
        }
        if (count != 1) return WrongArgumentCount(name, 1, count, out error);
        var value = heap.Held(values[first]);
        bool added;
        switch (value.Kind)
        {
            case WebLingoValueKind.Bytes:
                added = heap.TryAddToBuilder(target, heap.GetBytes(value), out error);
                break;
            case WebLingoValueKind.Int64 when value.AsInt64 is >= 0 and <= 255:
                added = heap.TryAddToBuilder(target, [(byte)value.AsInt64], out error);
                break;
            case WebLingoValueKind.String:
                if (!TryCharset(WebLingoValue.Null, heap, out var utf8, out error)) return false;
                added = heap.TryAddToBuilder(target, WebLingoTextCodec.Encode(heap.GetString(value), utf8), out error);
                break;
            default:
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'{name}' takes bytes, an integer from 0 to 255, or a string, not {WebLingoValueFormatter.Describe(value, heap)}.");
                return false;
        }
        if (!added) return false;
        result = target;
        return true;
    }

    private static bool WrongArgumentCount(string name, int least, int most, int count, out WebLingoRuntimeError? error)
    {
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.WrongArgumentCount, $"'{name}' takes {least} to {most} arguments, not {count}.");
        return false;
    }
}
