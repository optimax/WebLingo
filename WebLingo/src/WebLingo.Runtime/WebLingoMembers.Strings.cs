using System.Text;
using WebLingo.Core;
namespace WebLingo.Runtime;

// Strings' methods (specification, section 3.5, "String methods"; the owner's answers, 16 November 2026): s.compare(other);
// indexOf(search, from), lastIndexOf(search), contains(search) or includes(search), startsWith(prefix) and endsWith(suffix);
// slice(start, end); split(separator); trim(), trimStart() and trimEnd(), white space, or any of the characters given,
// trim(chars); removePrefix(text) and removeSuffix(text), text removed once; toUpper() or toUpperCase() and toLower() or
// toLowerCase(), by the invariant culture; replace(old, new) or replaceAll(old, new), every one replaced; repeat(n);
// padStart(width, pad) or padLeft(…), padEnd(width, pad) or padRight(…); and separator.join(array). Positions and widths
// count Unicode scalar values, as indexing and length do; searching compares code units, ordinally, never by culture.
public static partial class WebLingoMembers
{
    private static bool IsStringMethod(WebLingoMember member) =>
        member is WebLingoMember.Compare or WebLingoMember.IndexOf or WebLingoMember.LastIndexOf or WebLingoMember.Contains
            or WebLingoMember.StartsWith or WebLingoMember.EndsWith or WebLingoMember.Slice or WebLingoMember.Split or WebLingoMember.Trim
            or WebLingoMember.TrimStart or WebLingoMember.TrimEnd or WebLingoMember.ToUpper or WebLingoMember.ToLower
            or WebLingoMember.Replace or WebLingoMember.Repeat or WebLingoMember.PadStart or WebLingoMember.PadEnd or WebLingoMember.Join
            or WebLingoMember.Match or WebLingoMember.MatchAll or WebLingoMember.RemovePrefix or WebLingoMember.RemoveSuffix;

    /// <summary>Where <paramref name="text"/> begins once any of <paramref name="characters"/>' scalar values are trimmed from its start.</summary>
    private static int TrimmedStart(string text, string characters)
    {
        var i = 0;
        while (i < text.Length)
        {
            var length = ScalarLength(text, i);
            if (!HoldsScalar(characters, text.AsSpan(i, length))) break;
            i += length;
        }
        return i;
    }

    /// <summary>Where <paramref name="text"/> ends once any of <paramref name="characters"/>' scalar values are trimmed from its end, not before <paramref name="start"/>.</summary>
    private static int TrimmedEnd(string text, string characters, int start)
    {
        var end = text.Length;
        while (end > start)
        {
            var length = end - 2 >= start && char.IsSurrogatePair(text[end - 2], text[end - 1]) ? 2 : 1;
            if (!HoldsScalar(characters, text.AsSpan(end - length, length))) break;
            end -= length;
        }
        return end;
    }

    /// <summary>The code units of the scalar value at <paramref name="i"/>: two for a surrogate pair, else one.</summary>
    private static int ScalarLength(string text, int i) => i + 1 < text.Length && char.IsSurrogatePair(text[i], text[i + 1]) ? 2 : 1;

    /// <summary>Whether <paramref name="characters"/> holds a scalar value whole, a pair's half being no character of its own.</summary>
    private static bool HoldsScalar(string characters, ReadOnlySpan<char> scalar)
    {
        for (var i = 0; i < characters.Length; i += ScalarLength(characters, i))
        {
            if (characters.AsSpan(i, ScalarLength(characters, i)).SequenceEqual(scalar)) return true;
        }
        return false;
    }

    private static bool TryCallString(WebLingoValue target, WebLingoMember member, string name, WebLingoValue[] values, int first, int count,
        WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var text = heap.GetString(target);
        switch (member)
        {
            case WebLingoMember.Compare:
            {
                // s.compare(other): -1, 0 or 1, by their Unicode scalar values, as < orders them (section 3.5).
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (values[first].Kind != WebLingoValueKind.String)
                {
                    error = new WebLingoArgument(name, 0, 1, "the other string", "a string").Wrong(values[first], heap);
                    return false;
                }
                return Done(WebLingoValue.FromInt64(WebLingoOperators.CompareStrings(text, heap.GetString(values[first]))), out result, out error);
            }
            case WebLingoMember.IndexOf:
            {
                // The first place the search starts, at from or after it, else -1.
                if (count is < 1 or > 2) return WrongArgumentCount(name, 1, 2, count, out error);
                if (!TryText(new(name, 0, 2, "the search", "a string"), values[first], heap, out var search, out error)) return false;
                var from = 0L;
                if (count == 2 && !TryPosition(new(name, 1, 2, "the start", "an integer"), values[first + 1], ScalarCount(text), out from, out error))
                {
                    return false;
                }
                var at = text.IndexOf(search, ScalarStart(text, from), StringComparison.Ordinal);
                return Done(WebLingoValue.FromInt64(at < 0 ? -1 : ScalarsBefore(text, at)), out result, out error);
            }
            case WebLingoMember.LastIndexOf:
            {
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (!TryText(new(name, 0, 1, "the search", "a string"), values[first], heap, out var search, out error)) return false;
                var at = search.Length == 0 ? text.Length : text.LastIndexOf(search, StringComparison.Ordinal);
                return Done(WebLingoValue.FromInt64(at < 0 ? -1 : ScalarsBefore(text, at)), out result, out error);
            }
            case WebLingoMember.Contains or WebLingoMember.StartsWith or WebLingoMember.EndsWith:
            {
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (!TryText(new(name, 0, 1, "the text to look for", "a string"), values[first], heap, out var search, out error)) return false;
                var found = member switch
                {
                    WebLingoMember.Contains => text.Contains(search, StringComparison.Ordinal),
                    WebLingoMember.StartsWith => text.StartsWith(search, StringComparison.Ordinal),
                    _ => text.EndsWith(search, StringComparison.Ordinal)
                };
                return Done(WebLingoValue.FromBoolean(found), out result, out error);
            }
            case WebLingoMember.Slice:
            {
                // slice(start, end): from start to end, end left out the string's end; WL3005 outside it, as bytes' slice.
                if (count is < 1 or > 2) return WrongArgumentCount(name, 1, 2, count, out error);
                var length = ScalarCount(text);
                var start = values[first];
                var end = count == 2 ? values[first + 1] : WebLingoValue.FromInt64(length);
                if (start.Kind != WebLingoValueKind.Int64 || end.Kind != WebLingoValueKind.Int64)
                {
                    error = SliceArgument(name, start).Wrong(start.Kind != WebLingoValueKind.Int64 ? start : end, heap);
                    return false;
                }
                var (from, to) = (start.AsInt64, end.AsInt64);
                if (from < 0 || to > length || from > to)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IndexOutOfRange,
                        $"The range {from} to {to} is not within the string, 0 to {length}.");
                    return false;
                }
                var (begin, finish) = (ScalarStart(text, from), ScalarStart(text, to));
                return heap.TryAllocateString(text[begin..finish], out result, out error);
            }
            case WebLingoMember.Split:
            {
                // The parts between the separators, empty ones included; an empty separator, each Unicode scalar value.
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (values[first].Kind == WebLingoValueKind.Regex) return TryRegex(values[first], member, text, "", heap, out result, out error);
                if (!TryText(new(name, 0, 1, "the separator", "a string or a regular expression"), values[first], heap, out var separator, out error))
                {
                    return false;
                }
                if (separator.Length == 0) return TrySnapshot(target, heap, out result, out error);
                return TryStrings(text.Split(separator), heap, out result, out error);
            }
            case WebLingoMember.Trim or WebLingoMember.TrimStart or WebLingoMember.TrimEnd:
            {
                // trim(), trimStart() and trimEnd(): white space; given characters, trim(chars), any of them, each a Unicode
                // scalar value, as C#'s Trim(chars) and Python's strip(chars) take them, an empty one trimming nothing (the
                // owner's answers, 18 November 2026).
                if (count > 1) return WrongArgumentCount(name, 0, 1, count, out error);
                if (count == 0)
                {
                    return heap.TryAllocateString(member switch
                    {
                        WebLingoMember.Trim => text.Trim(),
                        WebLingoMember.TrimStart => text.TrimStart(),
                        _ => text.TrimEnd()
                    }, out result, out error);
                }
                if (!TryText(new(name, 0, 1, "the characters", "a string of characters to trim"), values[first], heap, out var characters, out error))
                {
                    return false;
                }
                var start = member == WebLingoMember.TrimEnd ? 0 : TrimmedStart(text, characters);
                var end = member == WebLingoMember.TrimStart ? text.Length : TrimmedEnd(text, characters, start);
                return heap.TryAllocateString(text[start..end], out result, out error);
            }
            case WebLingoMember.RemovePrefix or WebLingoMember.RemoveSuffix:
            {
                // removePrefix(text) and removeSuffix(text): text removed once, only where the string begins, or ends, with it,
                // else the string as it is, as Python's and Kotlin's (the owner's answer, 18 November 2026).
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (!TryText(new(name, 0, 1, "the text", "a string"), values[first], heap, out var affix, out error)) return false;
                var prefix = member == WebLingoMember.RemovePrefix;
                if (prefix ? !text.StartsWith(affix, StringComparison.Ordinal) : !text.EndsWith(affix, StringComparison.Ordinal))
                {
                    result = target;
                    return true;
                }
                return heap.TryAllocateString(prefix ? text[affix.Length..] : text[..^affix.Length], out result, out error);
            }
            case WebLingoMember.ToUpper or WebLingoMember.ToLower:
                // By the invariant culture, the same on every server (the owner's answer, 16 November 2026).
                if (count != 0) return WrongArgumentCount(name, 0, count, out error);
                return heap.TryAllocateString(member == WebLingoMember.ToUpper ? text.ToUpperInvariant() : text.ToLowerInvariant(),
                    out result, out error);
            case WebLingoMember.Replace:
            {
                // Every one replaced, left to right, as found, none overlapping.
                if (count != 2) return WrongArgumentCount(name, 2, count, out error);
                if (values[first].Kind == WebLingoValueKind.Regex)
                {
                    // A regular expression's matches, each replaced, by .NET's substitutions (section 3.6).
                    if (!TryText(new(name, 1, 2, "the replacement", "a string"), values[first + 1], heap, out var substitution, out error)) return false;
                    return TryRegex(values[first], member, text, substitution, heap, out result, out error);
                }
                if (!TryText(new(name, 0, 2, "the text replaced", "a string or a regular expression"), values[first], heap, out var old, out error)
                    || !TryText(new(name, 1, 2, "the replacement", "a string"), values[first + 1], heap, out var replacement, out error))
                {
                    return false;
                }
                if (old.Length == 0)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{name}' replaces a string that is not empty.");
                    return false;
                }
                var found = 0L;
                for (var at = text.IndexOf(old, StringComparison.Ordinal); at >= 0; at = text.IndexOf(old, at + old.Length, StringComparison.Ordinal))
                {
                    found++;
                }
                if (!heap.TryMakeRoom(text.Length + found * (replacement.Length - old.Length), out error)) return false;
                return heap.TryAllocateString(text.Replace(old, replacement, StringComparison.Ordinal), out result, out error);
            }
            case WebLingoMember.Repeat:
            {
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (values[first].Kind != WebLingoValueKind.Int64 || values[first].AsInt64 < 0)
                {
                    error = new WebLingoArgument(name, 0, 1, "the count", "an integer, 0 or more").Wrong(values[first], heap);
                    return false;
                }
                var times = values[first].AsInt64;
                if (text.Length == 0 || times == 0) return heap.TryAllocateString("", out result, out error);
                if (!heap.TryMakeRoom((long)Math.Min((decimal)text.Length * times, long.MaxValue), out error)) return false;
                var repeated = new StringBuilder((int)(text.Length * times));
                for (var i = 0L; i < times; i++) repeated.Append(text);
                return heap.TryAllocateString(repeated.ToString(), out result, out error);
            }
            case WebLingoMember.PadStart or WebLingoMember.PadEnd:
            {
                // To a width of Unicode scalar values, the pad, a space unless given, repeated and cut where it reaches it.
                if (count is < 1 or > 2) return WrongArgumentCount(name, 1, 2, count, out error);
                if (values[first].Kind != WebLingoValueKind.Int64)
                {
                    error = new WebLingoArgument(name, 0, 2, "the width", "an integer").Wrong(values[first], heap);
                    return false;
                }
                var pad = " ";
                if (count == 2 && !TryText(new(name, 1, 2, "the pad", "a string"), values[first + 1], heap, out pad, out error)) return false;
                var missing = values[first].AsInt64 - ScalarCount(text);
                if (missing <= 0 || pad.Length == 0) return Done(target, out result, out error);
                if (!heap.TryMakeRoom(text.Length + (long)Math.Min((decimal)missing * 2, long.MaxValue), out error)) return false;
                var padding = new StringBuilder();
                for (var (i, added) = (0, 0L); added < missing; added++)
                {
                    var length = i + 1 < pad.Length && char.IsSurrogatePair(pad[i], pad[i + 1]) ? 2 : 1;
                    padding.Append(pad, i, length);
                    i = (i + length) % pad.Length;
                }
                return heap.TryAllocateString(member == WebLingoMember.PadStart ? padding + text : text + padding, out result, out error);
            }
            case WebLingoMember.Match or WebLingoMember.MatchAll:
            {
                // s.match(r) and s.matchAll(r): as r.match(s) and r.matchAll(s) (section 3.6).
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (values[first].Kind != WebLingoValueKind.Regex)
                {
                    error = new WebLingoArgument(name, 0, 1, "the regular expression", "a regular expression, new Regex(…)").Wrong(values[first], heap);
                    return false;
                }
                return TryRegex(values[first], member, text, "", heap, out result, out error);
            }
            default:
                return TryJoin(text, name, values, first, count, true, heap, out result, out error);
        }
    }

    /// <summary>
    /// <c>separator.join(array)</c>: the array's elements' text, between separators: a string as it is, <c>null</c> as
    /// nothing, as an interpolation writes it, an integer, a number, a decimal, a Boolean or a date as <c>write</c> writes
    /// it; anything else WL3001, its text being its <c>toString()</c>'s, which a program gives first. The array is the
    /// argument, <paramref name="given"/>, or, for an array's own <c>join(separator)</c>, the array itself.
    /// </summary>
    private static bool TryJoin(string separator, string name, WebLingoValue[] values, int first, int count, bool given, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (count != 1) return WrongArgumentCount(name, 1, count, out error);
        if (values[first].Kind != WebLingoValueKind.Array)
        {
            error = new WebLingoArgument(name, 0, 1, "the array", "an array").Wrong(values[first], heap);
            return false;
        }
        var elements = heap.GetArray(values[first]);
        var texts = new string[elements.Count];
        var length = (long)separator.Length * Math.Max(0, elements.Count - 1);
        for (var i = 0; i < elements.Count; i++)
        {
            var element = heap.Held(elements[i]); // A held value as its kind's (section 3.7).
            texts[i] = element.Kind switch
            {
                WebLingoValueKind.String => heap.GetString(element),
                WebLingoValueKind.Null => "",
                WebLingoValueKind.Int64 or WebLingoValueKind.Binary64 or WebLingoValueKind.Boolean or WebLingoValueKind.Decimal
                    or WebLingoValueKind.DateTime when !element.HasClass => WebLingoValueFormatter.Format(element, heap),
                _ => null!
            };
            if (texts[i] is null)
            {
                var what = given ? $"{new WebLingoArgument(name, 0, 1, "the array", "an array").Named} must hold" : $"'{name}' writes";
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"{what} strings, numbers, Booleans, decimals, dates and null, not {WebLingoValueFormatter.Describe(element, heap)}: "
                    + "give its text first, x.toString().");
                return false;
            }
            length += texts[i].Length;
        }
        if (!heap.TryMakeRoom(length, out error)) return false;
        return heap.TryAllocateString(string.Join(separator, texts), out result, out error);
    }

    /// <summary>An array of new strings, each kept where the collector sees it while the next is made.</summary>
    private static bool TryStrings(string[] parts, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var made = new List<WebLingoValue>(parts.Length);
        heap.AddRoots(made);
        try
        {
            foreach (var part in parts)
            {
                if (!heap.TryAllocateString(part, out var piece, out error)) return false;
                made.Add(piece);
            }
            return heap.TryAllocateArray([.. made], 0, made.Count, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(made);
        }
    }

    /// <summary>An argument that must be a string, its text; WL3001 naming the argument otherwise.</summary>
    private static bool TryText(WebLingoArgument argument, WebLingoValue value, WebLingoHeap heap, out string text, out WebLingoRuntimeError? error)
    {
        if (value.Kind == WebLingoValueKind.String)
        {
            (text, error) = (heap.GetString(value), null);
            return true;
        }
        text = "";
        error = argument.Wrong(value, heap);
        return false;
    }

    /// <summary>
    /// <c>slice(start, end)</c>'s argument that is not an integer, of a string, an array or bytes: its start, unless that is
    /// one, then its end.
    /// </summary>
    private static WebLingoArgument SliceArgument(string name, WebLingoValue start) => start.Kind != WebLingoValueKind.Int64
        ? new(name, 0, 2, "the start", "an integer")
        : new(name, 1, 2, "the end", "an integer");

    /// <summary>A position in a string, an integer from 0 to its length; WL3005 outside it.</summary>
    private static bool TryPosition(WebLingoArgument argument, WebLingoValue value, long length, out long position, out WebLingoRuntimeError? error)
    {
        position = 0;
        if (value.Kind != WebLingoValueKind.Int64)
        {
            error = argument.Wrong(value);
            return false;
        }
        position = value.AsInt64;
        if (position < 0 || position > length)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IndexOutOfRange, $"The position {position} is not within the string, 0 to {length}.");
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>How many Unicode scalar values a string has before a UTF-16 index, which starts one.</summary>
    private static long ScalarsBefore(string text, int index)
    {
        var count = 0L;
        for (var i = 0; i < index; i++)
        {
            if (i + 1 < index && char.IsSurrogatePair(text[i], text[i + 1])) i++;
            count++;
        }
        return count;
    }

    private static bool Done(WebLingoValue value, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        (result, error) = (value, null);
        return true;
    }
}
