using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// The members that values have (specification, section 3.6), by name. A VM resolves a member's name to one of these
/// once, when it loads a module, so that it does not compare names as it runs; a name that is no member of any value
/// is <see cref="Unknown"/>, and is kept for the message of its error.
/// </summary>
public enum WsMember : byte { Unknown, Length, Push, Pop, Has, Remove, Add }

/// <summary>A member's name as a module's constant gives it, and the member it names.</summary>
public readonly record struct WsMemberName(WsMember Member, string Text);

/// <summary>
/// Members and indexing, the same for every VM (specification, section 3.6): <c>x.name</c>, <c>x.name(…)</c>,
/// <c>x[i]</c> and <c>x[i] = v</c>. A string's <c>length</c> counts its Unicode scalar values, and <c>s[i]</c> is its
/// <c>i</c>-th, as a string of one. A member a value does not have is WS3006, an index outside it WS3005, and an index
/// of the wrong kind, or a value that cannot be indexed, WS3001.
/// </summary>
public static class WsMembers
{
    public static WsMember Find(string name) => name switch
    {
        "length" => WsMember.Length,
        "push" => WsMember.Push,
        "pop" => WsMember.Pop,
        "has" => WsMember.Has,
        "remove" => WsMember.Remove,
        "add" => WsMember.Add,
        _ => WsMember.Unknown
    };

    /// <summary>Reads a member that is not called: <c>x.name</c>.</summary>
    public static bool TryGet(WsValue target, WsMember member, string name, WsHeap heap, out WsValue result,
        out WsRuntimeError? error)
    {
        if (target.Kind == WsValueKind.String && member == WsMember.Length)
        {
            result = WsValue.FromInt64(ScalarCount(heap.GetString(target)));
            error = null;
            return true;
        }
        if (target.Kind == WsValueKind.Array && member == WsMember.Length)
        {
            result = WsValue.FromInt64(heap.GetArray(target).Count);
            error = null;
            return true;
        }
        result = WsValue.Null;
        error = IsMethod(target, member)
            ? new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                $"'{name}' is a method of {WsValueFormatter.Describe(target)}, and can only be called.")
            : NoSuchMember(target, name);
        return false;
    }

    /// <summary>Whether the member is a method of the value: an array's <c>push</c> and <c>pop</c>.</summary>
    private static bool IsMethod(WsValue target, WsMember member) =>
        target.Kind == WsValueKind.Array && member is WsMember.Push or WsMember.Pop;

    /// <summary>
    /// Calls a method, <c>x.name(…)</c>, with the <paramref name="count"/> arguments from
    /// <paramref name="values"/>[<paramref name="first"/>] on. A member that is not a method is not a function (WS2005).
    /// </summary>
    public static bool TryCall(WsValue target, WsMember member, string name, WsValue[] values, int first, int count,
        WsHeap heap, out WsValue result, out WsRuntimeError? error)
    {
        result = WsValue.Null;
        if (target.Kind == WsValueKind.Array && member == WsMember.Push)
        {
            if (count != 1) return WrongArgumentCount(name, 1, count, out error);
            if (!heap.TryAddToArray(target, values[first], out error)) return false;
            return true;
        }
        if (target.Kind == WsValueKind.Array && member == WsMember.Pop)
        {
            if (count != 0) return WrongArgumentCount(name, 0, count, out error);
            if (heap.GetArray(target).Count == 0)
            {
                error = new WsRuntimeError(WsDiagnosticCodes.IndexOutOfRange, "'pop' cannot remove an element from an empty array.");
                return false;
            }
            result = heap.RemoveLastOfArray(target);
            error = null;
            return true;
        }
        error = target.Kind is WsValueKind.String or WsValueKind.Array && member == WsMember.Length
            ? new WsRuntimeError(WsDiagnosticCodes.NotAFunction,
                $"'{name}' of {WsValueFormatter.Describe(target)} is not a method, and cannot be called.")
            : NoSuchMember(target, name);
        return false;
    }

    /// <summary><c>target[index]</c>.</summary>
    public static bool TryGetIndex(WsValue target, WsValue index, WsHeap heap, out WsValue result, out WsRuntimeError? error)
    {
        result = WsValue.Null;
        if (target.Kind == WsValueKind.Array)
        {
            var elements = heap.GetArray(target);
            if (!TryIndex(index, elements.Count, out var at, out error)) return false;
            result = elements[(int)at];
            return true;
        }
        if (target.Kind != WsValueKind.String)
        {
            error = CannotBeIndexed(target);
            return false;
        }
        var text = heap.GetString(target);
        if (!TryIndex(index, ScalarCount(text), out var position, out error)) return false;
        var start = ScalarStart(text, position);
        var length = start + 1 < text.Length && char.IsSurrogatePair(text[start], text[start + 1]) ? 2 : 1;
        return heap.TryAllocateString(text.Substring(start, length), out result, out error);
    }

    /// <summary><c>target[index] = value</c>.</summary>
    public static bool TrySetIndex(WsValue target, WsValue index, WsValue value, WsHeap heap, out WsRuntimeError? error)
    {
        if (target.Kind == WsValueKind.Array)
        {
            var elements = heap.GetArray(target);
            if (!TryIndex(index, elements.Count, out var at, out error)) return false;
            elements[(int)at] = value;
            return true;
        }
        error = target.Kind == WsValueKind.String
            ? new WsRuntimeError(WsDiagnosticCodes.InvalidOperation, "A string cannot be changed: strings are immutable.")
            : CannotBeIndexed(target);
        return false;
    }

    /// <summary>The number of Unicode scalar values in a string: a surrogate pair counts once.</summary>
    public static long ScalarCount(string text)
    {
        var count = 0L;
        for (var i = 0; i < text.Length; i++)
        {
            if (i + 1 < text.Length && char.IsSurrogatePair(text[i], text[i + 1])) i++;
            count++;
        }
        return count;
    }

    /// <summary>The UTF-16 index at which the string's <paramref name="scalar"/>-th Unicode scalar value starts.</summary>
    private static int ScalarStart(string text, long scalar)
    {
        var i = 0;
        for (var seen = 0L; seen < scalar; seen++)
        {
            i += i + 1 < text.Length && char.IsSurrogatePair(text[i], text[i + 1]) ? 2 : 1;
        }
        return i;
    }

    /// <summary>An index, which must be an int64 from 0 to <paramref name="length"/> − 1.</summary>
    private static bool TryIndex(WsValue index, long length, out long position, out WsRuntimeError? error)
    {
        position = 0;
        if (index.Kind != WsValueKind.Int64)
        {
            error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                $"An index must be an integer, not {WsValueFormatter.Describe(index)}.");
            return false;
        }
        position = index.AsInt64;
        if (position < 0 || position >= length)
        {
            error = new WsRuntimeError(WsDiagnosticCodes.IndexOutOfRange, length == 0
                ? $"There is no index {Invariant(position)} in something empty."
                : $"The index {Invariant(position)} is outside 0 to {Invariant(length - 1)}.");
            return false;
        }
        error = null;
        return true;
    }

    private static bool WrongArgumentCount(string name, int arity, int count, out WsRuntimeError? error)
    {
        error = new WsRuntimeError(WsDiagnosticCodes.WrongArgumentCount,
            $"'{name}' takes {arity} {(arity == 1 ? "argument" : "arguments")}, not {count}.");
        return false;
    }

    private static WsRuntimeError CannotBeIndexed(WsValue target) =>
        new(WsDiagnosticCodes.InvalidOperation, $"{Capitalized(WsValueFormatter.Describe(target))} cannot be indexed.");

    private static WsRuntimeError NoSuchMember(WsValue target, string name) =>
        new(WsDiagnosticCodes.NoSuchMember, $"{Capitalized(WsValueFormatter.Describe(target))} has no member '{name}'.");

    private static string Invariant(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
