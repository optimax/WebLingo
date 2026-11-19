using WebLingo.Core;
namespace WebLingo.Runtime;

// Arrays' methods that take no function (specification, section 3.6, "Array methods"; the owner's answers, 16 November
// 2026): insert(i, v) and removeAt(i), which change the array, as push and pop do; indexOf(v) and contains(v) or
// includes(v), by ==; slice(start, end), concat(other), join(separator) and reverse(), which give a new array, or a string,
// the array unchanged. Those that take one, map, filter, reduce, find, any, all and sort, are written in WebLingo, an
// extension of Array the compiler adds to a program that names one (WebLingoBinder), so that they call it as the VMs call
// any function.
public static partial class WebLingoMembers
{
    private static bool IsArrayMethod(WebLingoMember member) =>
        member is WebLingoMember.Insert or WebLingoMember.RemoveAt or WebLingoMember.IndexOf or WebLingoMember.Contains
            or WebLingoMember.Slice or WebLingoMember.Concat or WebLingoMember.Join or WebLingoMember.Reverse;

    private static bool TryCallArray(WebLingoValue target, WebLingoMember member, string name, WebLingoValue[] values, int first, int count,
        WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var elements = heap.GetArray(target);
        switch (member)
        {
            case WebLingoMember.Insert:
            {
                // insert(i, v): v at i, from 0 to the length, the elements from i on moved up.
                if (count != 2) return WrongArgumentCount(name, 2, count, out error);
                if (!TryArrayPosition(new(name, 0, 2, "the position", "an integer"), values[first], elements.Count, out var at, out error)) return false;
                return heap.TryInsertIntoArray(target, at, values[first + 1], out error);
            }
            case WebLingoMember.RemoveAt:
            {
                // removeAt(i): the element at i, removed, the elements after it moved down.
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (!TryArrayPosition(new(name, 0, 1, "the position", "a position, an integer"), values[first], elements.Count - 1, out var at, out error))
                {
                    return false;
                }
                return Done(heap.RemoveFromArray(target, at), out result, out error);
            }
            case WebLingoMember.IndexOf or WebLingoMember.Contains:
            {
                // By ==, as it compares two values (section 3.5): 1 == 1.0, an instance only itself.
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                var found = -1;
                for (var i = 0; i < elements.Count && found < 0; i++)
                {
                    if (!WebLingoOperators.TryCompare(WebLingoComparisonOperator.Equal, elements[i], values[first], heap, out var equal, out error))
                    {
                        return false;
                    }
                    if (equal.AsBoolean) found = i;
                }
                return Done(member == WebLingoMember.IndexOf ? WebLingoValue.FromInt64(found) : WebLingoValue.FromBoolean(found >= 0),
                    out result, out error);
            }
            case WebLingoMember.Slice:
            {
                // slice(start, end): as a string's and bytes' are, WL3005 for a range outside the array.
                if (count is < 1 or > 2) return WrongArgumentCount(name, 1, 2, count, out error);
                var start = values[first];
                var end = count == 2 ? values[first + 1] : WebLingoValue.FromInt64(elements.Count);
                if (start.Kind != WebLingoValueKind.Int64 || end.Kind != WebLingoValueKind.Int64)
                {
                    error = SliceArgument(name, start).Wrong(start.Kind != WebLingoValueKind.Int64 ? start : end, heap);
                    return false;
                }
                var (from, to) = (start.AsInt64, end.AsInt64);
                if (from < 0 || to > elements.Count || from > to)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IndexOutOfRange,
                        $"The range {from} to {to} is not within the array, 0 to {elements.Count}.");
                    return false;
                }
                return heap.TryAllocateArray([.. elements], (int)from, (int)(to - from), out result, out error);
            }
            case WebLingoMember.Concat:
            {
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (values[first].Kind != WebLingoValueKind.Array)
                {
                    error = new WebLingoArgument(name, 0, 1, "the other array", "an array").Wrong(values[first], heap);
                    return false;
                }
                WebLingoValue[] both = [.. elements, .. heap.GetArray(values[first])];
                return heap.TryAllocateArray(both, 0, both.Length, out result, out error);
            }
            case WebLingoMember.Join:
            {
                // join(separator): a comma unless one is given, as JavaScript's; the elements' text as a string's join writes it.
                if (count > 1) return WrongArgumentCount(name, 0, 1, count, out error);
                var separator = ",";
                if (count == 1 && !TryText(new(name, 0, 1, "the separator", "a string"), values[first], heap, out separator, out error)) return false;
                return TryJoin(separator, name, [target], 0, 1, false, heap, out result, out error);
            }
            default:
            {
                // reverse(): a new array, its elements the other way round.
                if (count != 0) return WrongArgumentCount(name, 0, count, out error);
                var reversed = new WebLingoValue[elements.Count];
                for (var i = 0; i < reversed.Length; i++) reversed[i] = elements[elements.Count - 1 - i];
                return heap.TryAllocateArray(reversed, 0, reversed.Length, out result, out error);
            }
        }
    }

    /// <summary>A position in an array, an integer from 0 to <paramref name="last"/>; WL3005 outside it.</summary>
    private static bool TryArrayPosition(WebLingoArgument argument, WebLingoValue value, int last, out int position, out WebLingoRuntimeError? error)
    {
        position = 0;
        var name = argument.Member;
        if (value.Kind != WebLingoValueKind.Int64)
        {
            error = argument.Wrong(value);
            return false;
        }
        if (value.AsInt64 < 0 || value.AsInt64 > last)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IndexOutOfRange,
                last < 0 ? $"'{name}' has no element to remove from an empty array."
                    : $"The position {value.AsInt64} is not within the array, 0 to {last}.");
            return false;
        }
        position = (int)value.AsInt64;
        error = null;
        return true;
    }
}
