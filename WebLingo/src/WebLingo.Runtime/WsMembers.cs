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

    /// <summary>
    /// Reads a member that is not called: <c>x.name</c>, or with <paramref name="optional"/> <c>x?.name</c>, which gives
    /// <c>null</c> for a <c>null</c> target or a member it does not have (section 3.7). An instance's field is its
    /// value, and its method a function bound to it.
    /// </summary>
    public static bool TryGet(WsValue target, WsMember member, string name, WsHeap heap, bool optional, out WsValue result,
        out WsRuntimeError? error)
    {
        if (optional && target.Kind == WsValueKind.Null)
        {
            result = WsValue.Null;
            error = null;
            return true;
        }
        if (target.Kind == WsValueKind.Instance)
        {
            error = null;
            if (heap.TryGetField(target, name, out result)) return true;
            if (heap.TryGetMethod(heap.ClassOf(target), name, out var method))
            {
                return heap.TryAllocateBoundMethod(target, method, out result, out error);
            }
            if (optional) return true;
            error = NoSuchMember(target, name, heap);
            return false;
        }
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
        if (target.Kind == WsValueKind.Map && member == WsMember.Length)
        {
            result = WsValue.FromInt64(heap.MapCount(target));
            error = null;
            return true;
        }
        if (target.Kind == WsValueKind.Set && member == WsMember.Length)
        {
            result = WsValue.FromInt64(heap.SetCount(target));
            error = null;
            return true;
        }
        result = WsValue.Null;
        if (IsMethod(target, member))
        {
            error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                $"'{name}' is a method of {WsValueFormatter.Describe(target)}, and can only be called.");
            return false;
        }
        error = optional ? null : NoSuchMember(target, name, heap);
        return optional;
    }

    /// <summary>
    /// Assigns a field of an instance, <c>x.name = v</c>, which exists from then on (section 3.7). A method's name is not
    /// a field's, and nothing else has fields to assign (WS3001).
    /// </summary>
    public static bool TrySet(WsValue target, string name, WsValue value, WsHeap heap, out WsRuntimeError? error)
    {
        if (target.Kind != WsValueKind.Instance)
        {
            error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                $"Only an instance's fields can be assigned, not a member of {WsValueFormatter.Describe(target, heap)}.");
            return false;
        }
        if (heap.TryGetMethod(heap.ClassOf(target), name, out _))
        {
            error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                $"'{name}' is a method of {WsValueFormatter.Describe(target, heap)}, and cannot be assigned.");
            return false;
        }
        return heap.TrySetField(target, name, value, out error);
    }

    /// <summary>
    /// What <c>super</c> names, in a method of <paramref name="classValue"/>: its base's method <paramref name="name"/>, or
    /// its constructor for <c>constructor</c>, bound to <paramref name="self"/> (section 3.7); a method the base does not
    /// have is WS3006.
    /// </summary>
    public static bool TryGetSuper(WsValue classValue, WsValue self, string name, WsHeap heap, out WsValue bound,
        out WsRuntimeError? error)
    {
        bound = WsValue.Null;
        var baseClass = heap.ClassBase(classValue);
        if (baseClass.Kind != WsValueKind.Class) throw new InvalidOperationException("The class extends no other.");
        WsValue closure;
        if (name == "constructor")
        {
            closure = heap.ClassConstructor(baseClass);
            if (closure.Kind == WsValueKind.Null) throw new InvalidOperationException("The base class has no constructor.");
        }
        else if (!heap.TryGetMethod(baseClass, name, out closure))
        {
            error = new WsRuntimeError(WsDiagnosticCodes.NoSuchMember,
                $"{Capitalized(WsValueFormatter.Describe(baseClass, heap))} has no method '{name}'.");
            return false;
        }
        return heap.TryAllocateBoundMethod(self, closure, out bound, out error);
    }

    /// <summary>
    /// What <c>x.name(…)</c> calls when <c>x</c> is an instance: a field's value, as any function value is called, or
    /// a method, which runs on the instance (<paramref name="isMethod"/>); WS3006 if it has neither.
    /// </summary>
    public static bool TryFindCallee(WsValue instance, string name, WsHeap heap, out WsValue callee, out bool isMethod,
        out WsRuntimeError? error)
    {
        error = null;
        isMethod = false;
        if (heap.TryGetField(instance, name, out callee)) return true;
        isMethod = heap.TryGetMethod(heap.ClassOf(instance), name, out callee);
        if (isMethod) return true;
        error = NoSuchMember(instance, name, heap);
        return false;
    }

    /// <summary>Whether the member is a method of the value: an array's <c>push</c> and <c>pop</c>, a map's <c>has</c> and <c>remove</c>.</summary>
    private static bool IsMethod(WsValue target, WsMember member) =>
        (target.Kind == WsValueKind.Array && member is WsMember.Push or WsMember.Pop)
        || (target.Kind == WsValueKind.Map && member is WsMember.Has or WsMember.Remove)
        || (target.Kind == WsValueKind.Set && member is WsMember.Add or WsMember.Has or WsMember.Remove);

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
        if (target.Kind == WsValueKind.Map && member == WsMember.Has)
        {
            if (count != 1) return WrongArgumentCount(name, 1, count, out error);
            if (!heap.TryFindInMap(target, values[first], out _, out var found, out error)) return false;
            result = WsValue.FromBoolean(found);
            return true;
        }
        if (target.Kind == WsValueKind.Map && member == WsMember.Remove)
        {
            if (count != 1) return WrongArgumentCount(name, 1, count, out error);
            if (!heap.TryRemoveFromMap(target, values[first], out var removed, out error)) return false;
            result = WsValue.FromBoolean(removed);
            return true;
        }
        if (target.Kind == WsValueKind.Set && member is WsMember.Add or WsMember.Has or WsMember.Remove)
        {
            if (count != 1) return WrongArgumentCount(name, 1, count, out error);
            bool answer;
            var succeeded = member switch
            {
                WsMember.Add => heap.TryAddToSet(target, values[first], out answer, out error),
                WsMember.Has => heap.TryFindInSet(target, values[first], out answer, out error),
                _ => heap.TryRemoveFromSet(target, values[first], out answer, out error)
            };
            if (succeeded) result = WsValue.FromBoolean(answer);
            return succeeded;
        }
        error = target.Kind is WsValueKind.String or WsValueKind.Array or WsValueKind.Map or WsValueKind.Set
                && member == WsMember.Length
            ? new WsRuntimeError(WsDiagnosticCodes.NotAFunction,
                $"'{name}' of {WsValueFormatter.Describe(target)} is not a method, and cannot be called.")
            : NoSuchMember(target, name, heap);
        return false;
    }

    /// <summary>
    /// <c>target[index]</c>, or with <paramref name="optional"/> <c>target?[index]</c>, which gives <c>null</c> where
    /// the other would raise a missing key (WS3007) or an index out of range (WS3005), and for a <c>null</c> target, but
    /// raises every other error as the other does (section 3.6).
    /// </summary>
    public static bool TryGetIndex(WsValue target, WsValue index, WsHeap heap, bool optional, out WsValue result,
        out WsRuntimeError? error)
    {
        result = WsValue.Null;
        error = null;
        if (optional && target.Kind == WsValueKind.Null) return true;
        if (target.Kind == WsValueKind.Array)
        {
            var elements = heap.GetArray(target);
            if (!TryIndex(index, elements.Count, out var at, out error)) return Absent(optional, ref error);
            result = elements[(int)at];
            return true;
        }
        if (target.Kind == WsValueKind.Map)
        {
            if (!heap.TryFindInMap(target, index, out result, out var found, out error)) return false;
            if (found || optional) return true;
            error = new WsRuntimeError(WsDiagnosticCodes.NoSuchKey, $"The map has no key {KeyText(index, heap)}.");
            return false;
        }
        if (target.Kind != WsValueKind.String)
        {
            error = CannotBeIndexed(target);
            return false;
        }
        var text = heap.GetString(target);
        if (!TryIndex(index, ScalarCount(text), out var position, out error)) return Absent(optional, ref error);
        var start = ScalarStart(text, position);
        var length = start + 1 < text.Length && char.IsSurrogatePair(text[start], text[start + 1]) ? 2 : 1;
        return heap.TryAllocateString(text.Substring(start, length), out result, out error);
    }

    /// <summary>An index out of range, which <c>?[</c> answers with <c>null</c>: true then, and the error cleared.</summary>
    private static bool Absent(bool optional, ref WsRuntimeError? error)
    {
        if (!optional || error?.Code != WsDiagnosticCodes.IndexOutOfRange) return false;
        error = null;
        return true;
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
        if (target.Kind == WsValueKind.Map) return heap.TrySetInMap(target, index, value, out error);
        error = target.Kind == WsValueKind.String
            ? new WsRuntimeError(WsDiagnosticCodes.InvalidOperation, "A string cannot be changed: strings are immutable.")
            : CannotBeIndexed(target);
        return false;
    }

    /// <summary>
    /// The next step of <c>foreach</c> (section 3.6): from <paramref name="position"/> (an int64, from 0), the next
    /// element of an array, or Unicode scalar value of a string (as a string of one), into
    /// <paramref name="values"/>[<paramref name="first"/>], or the next entry of a map, its key and its value, into it
    /// and the one after, with the position after it; <paramref name="done"/> when there is nothing more. A map is
    /// looped over with a key and a value, anything else with one item, and anything else again not at all (WS3001).
    /// </summary>
    public static bool TryNext(WsValue collection, WsValue position, int items, WsValue[] values, int first, WsHeap heap,
        out WsValue next, out bool done, out WsRuntimeError? error)
    {
        next = position;
        done = false;
        error = null;
        if (position.Kind != WsValueKind.Int64) throw new InvalidOperationException("A loop's position is not an integer.");
        var at = position.AsInt64;
        switch (collection.Kind)
        {
            case WsValueKind.Array when items == 1:
            {
                var elements = heap.GetArray(collection);
                done = at >= elements.Count;
                if (done) return true;
                values[first] = elements[(int)at];
                next = WsValue.FromInt64(at + 1);
                return true;
            }
            case WsValueKind.Set when items == 1:
            {
                done = at >= heap.SetCount(collection);
                if (done) return true;
                values[first] = heap.SetValueAt(collection, (int)at);
                next = WsValue.FromInt64(at + 1);
                return true;
            }
            case WsValueKind.Map when items == 2:
            {
                done = at >= heap.MapCount(collection);
                if (done) return true;
                (values[first], values[first + 1]) = heap.MapEntryAt(collection, (int)at);
                next = WsValue.FromInt64(at + 1);
                return true;
            }
            case WsValueKind.String when items == 1:
            {
                var text = heap.GetString(collection);
                done = at >= text.Length;
                if (done) return true;
                var start = (int)at;
                var length = start + 1 < text.Length && char.IsSurrogatePair(text[start], text[start + 1]) ? 2 : 1;
                if (!heap.TryAllocateString(text.Substring(start, length), out values[first], out error)) return false;
                next = WsValue.FromInt64(at + length);
                return true;
            }
            case WsValueKind.Map:
                error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                    "A map's loop takes a key and a value: 'foreach (var (key, value) in map)'.");
                return false;
            case WsValueKind.Array or WsValueKind.String or WsValueKind.Set:
                error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                    $"Only a map's loop takes a key and a value, not {WsValueFormatter.Describe(collection)}'s.");
                return false;
            default:
                error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                    $"{Capitalized(WsValueFormatter.Describe(collection))} cannot be looped over with 'foreach'.");
                return false;
        }
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

    /// <summary>A key as a message shows it: a string quoted, anything else as <c>write</c> writes it.</summary>
    private static string KeyText(WsValue key, WsHeap heap) =>
        key.Kind == WsValueKind.String ? WsValueFormatter.Quote(heap.GetString(key)) : WsValueFormatter.Format(key);

    private static WsRuntimeError CannotBeIndexed(WsValue target) =>
        new(WsDiagnosticCodes.InvalidOperation, $"{Capitalized(WsValueFormatter.Describe(target))} cannot be indexed.");

    private static WsRuntimeError NoSuchMember(WsValue target, string name, WsHeap heap) =>
        new(WsDiagnosticCodes.NoSuchMember, $"{Capitalized(WsValueFormatter.Describe(target, heap))} has no member '{name}'.");

    private static string Invariant(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
