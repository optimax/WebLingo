using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// The members that values have (specification, section 3.6), by name. A VM resolves a member's name to one of these
/// once, when it loads a module, so that it does not compare names as it runs; a name that is no member of any value
/// is <see cref="Unknown"/>, and is kept for the message of its error.
/// </summary>
public enum WsMember : byte { Unknown, Length, Push, Pop, Has, Remove, Add, Dispose, ToString, Equal, Forget }

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
        "dispose" => WsMember.Dispose,
        "toString" => WsMember.ToString,
        "equal" => WsMember.Equal,
        "forget" => WsMember.Forget,
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
        if (IsClassedCollection(target, heap, out var collectionClass))
        {
            // A collection of a class extending Array, Map or Set: its fields, then its class's methods, then its own
            // members, below (section 3.7).
            error = null;
            if (heap.TryGetField(target, name, out result)) return true;
            if (heap.TryGetMethod(collectionClass, name, out var classMethod))
            {
                return heap.TryAllocateBoundMethod(target, classMethod, out result, out error);
            }
            if (heap.TryGetStatic(collectionClass, name, out result)) return true;
        }
        if (target.Kind == WsValueKind.Instance)
        {
            error = null;
            if (heap.TryGetField(target, name, out result)) return true;
            if (heap.TryGetMethod(heap.ClassOf(target), name, out var method))
            {
                return heap.TryAllocateBoundMethod(target, method, out result, out error, member == WsMember.Dispose);
            }
            // Its class's static member, which its own members shadow (section 3.7).
            if (heap.TryGetStatic(heap.ClassOf(target), name, out result)) return true;
            // An instance holding a value has that value's members too, its string's length (section 3.7).
            if (heap.IsHolding(target)) return TryGet(heap.Held(target), member, name, heap, optional, out result, out error);
            if (optional) return true;
            error = NoSuchMember(target, name, heap);
            return false;
        }
        if (target.Kind == WsValueKind.Class)
        {
            // A static member, the class's own or its base's (section 3.7).
            error = null;
            if (heap.TryGetStatic(target, name, out result)) return true;
            if (TryGetValueMethod(target, member, name, heap, out var objectToString))
            {
                return heap.TryAllocateBoundMethod(target, objectToString, out result, out error);
            }
            if (optional) return true;
            error = NoSuchMember(target, name, heap);
            return false;
        }
        if (TryGetValueMethod(target, member, name, heap, out var objectMethod))
        {
            // Every value has Object's toString() and equal(), and its kind's class's extensions (section 3.7).
            return heap.TryAllocateBoundMethod(target, objectMethod, out result, out error);
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
    /// Assigns a field of an instance, <c>x.name = v</c>, which exists from then on, or a static field of a class, its
    /// own or its base's, which it declares (WS3006 otherwise; section 3.7). A method's name is not a field's, and
    /// nothing else has fields to assign (WS3001).
    /// </summary>
    public static bool TrySet(WsValue target, string name, WsValue value, WsHeap heap, out WsRuntimeError? error)
    {
        if (target.Kind == WsValueKind.Class)
        {
            error = null;
            if (heap.TrySetStatic(target, name, value, out var isMethod)) return true;
            if (!isMethod) return heap.TryAddStatic(target, name, value, out error); // A new static member (section 3.7).
            error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                $"'{name}' is a static method of {WsValueFormatter.Describe(target, heap)}, and cannot be assigned.");
            return false;
        }
        if (IsClassedCollection(target, heap, out var collectionClass))
        {
            if (heap.TryGetMethod(collectionClass, name, out _))
            {
                error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                    $"'{name}' is a method of {WsValueFormatter.Describe(target, heap)}, and cannot be assigned.");
                return false;
            }
            return heap.TrySetField(target, name, value, out error);
        }
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
            closure = heap.ConstructorFor(baseClass);
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

    /// <summary>Whether a value is a collection that a class extending Array, Map or Set made, and its class.</summary>
    public static bool IsClassedCollection(WsValue value, WsHeap heap, out WsValue classValue)
    {
        classValue = value.Kind is WsValueKind.Array or WsValueKind.Map or WsValueKind.Set && value.HasClass
            ? heap.ClassOfValue(value)
            : WsValue.Null;
        return classValue.Kind == WsValueKind.Class;
    }

    /// <summary>
    /// What <c>x.name(…)</c> calls when <c>x</c> is a collection of a class extending Array, Map or Set: a field's
    /// value, or a method of its class (<paramref name="isMethod"/>); false for anything else, its own members included.
    /// </summary>
    public static bool TryFindCollectionCallee(WsValue target, string name, WsHeap heap, out WsValue callee, out bool isMethod)
    {
        callee = WsValue.Null;
        isMethod = false;
        if (!IsClassedCollection(target, heap, out var classValue)) return false;
        if (heap.TryGetField(target, name, out callee)) return true;
        isMethod = heap.TryGetMethod(classValue, name, out callee);
        return isMethod || heap.TryGetStatic(classValue, name, out callee);
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
        // Its class's static member, called as any function is, which its own members shadow (section 3.7).
        if (heap.TryGetStatic(heap.ClassOf(instance), name, out callee)) return true;
        error = NoSuchMember(instance, name, heap);
        return false;
    }

    /// <summary>
    /// Begins disposing an instance, for <c>x.dispose()</c> and <c>using</c> (section 3.7): gives the dispose methods to
    /// run on it, its class's own first, then its bases', or <c>null</c> if its disposal has begun already, which then
    /// does nothing. A value whose class has no dispose method, or that is no instance, is WS3006.
    /// </summary>
    public static bool TryBeginDisposal(WsValue target, WsHeap heap, out WsValue[]? chain, out WsRuntimeError? error)
    {
        chain = null;
        error = null;
        if (target.Kind == WsValueKind.Instance)
        {
            var methods = heap.DisposeChain(heap.ClassOf(target));
            if (methods.Length > 0)
            {
                if (heap.TryMarkDisposed(target)) chain = methods;
                return true;
            }
        }
        error = new WsRuntimeError(WsDiagnosticCodes.NoSuchMember,
            $"{Capitalized(WsValueFormatter.Describe(target, heap))} has no method 'dispose', and cannot be disposed.");
        return false;
    }

    /// <summary>
    /// A method of a value that is not an instance: its kind's class's, an extension's, once an <c>extend</c> has run, or
    /// <c>Object</c>'s if the program has not made that class; or <c>Object</c>'s <c>toString()</c> or <c>equal()</c>,
    /// which every value has, <c>null</c> included, which has no class (section 3.7).
    /// </summary>
    public static bool TryGetValueMethod(WsValue target, WsMember member, string name, WsHeap heap, out WsValue method)
    {
        if (heap.Extended && target.Kind != WsValueKind.Null)
        {
            var index = ValueClassOf(target.Kind);
            var classValue = index >= 0 && heap.ValueClasses is { } classes && classes[index].Kind == WsValueKind.Class
                ? classes[index]
                : heap.ObjectClass;
            if (classValue.Kind == WsValueKind.Class && heap.TryGetMethod(classValue, name, out method)) return true;
        }
        return TryGetObjectMethod(member, name, heap, out method);
    }

    /// <summary>
    /// The operator a class declares (<c>static operator +(a, b)</c>, a static method named <c>operator+</c>), that an
    /// operation on two values calls: its left operand's class's, its own or a base's, else its right's; false when
    /// neither is an instance, or a collection, of a class that declares it (section 3.7).
    /// </summary>
    public static bool TryFindOperator(string name, WsValue left, WsValue right, WsHeap heap, out WsValue method) =>
        TryFindOperator(name, left, heap, out method) || TryFindOperator(name, right, heap, out method);

    private static bool TryFindOperator(string name, WsValue operand, WsHeap heap, out WsValue method)
    {
        method = WsValue.Null;
        var classValue = heap.ClassOfValue(operand);
        return classValue.Kind == WsValueKind.Class && heap.TryGetStatic(classValue, name, out method)
            && method.Kind == WsValueKind.Function;
    }

    /// <summary>
    /// <c>Object</c>'s <c>toString()</c> or <c>equal()</c>, which every value has (section 3.7); false for another
    /// member, or before the program has made <c>Object</c>.
    /// </summary>
    public static bool TryGetObjectMethod(WsMember member, string name, WsHeap heap, out WsValue method)
    {
        method = WsValue.Null;
        return member is WsMember.ToString or WsMember.Equal && heap.ObjectClass.Kind == WsValueKind.Class
            && heap.TryGetMethod(heap.ObjectClass, name, out method);
    }

    /// <summary>
    /// <c>value is type</c> (section 3.7): an instance's class, or a value's kind's class (<paramref name="valueClasses"/>,
    /// by <see cref="WsValueClass"/>), is <paramref name="type"/> or extends it; <c>null</c> is of no class, and every
    /// other value is an <c>Object</c>, its kind's class made or not. A type that is not a class is compared with
    /// <c>==</c> here; a VM calls the type's <c>equal()</c> instead, once the program has <c>Object</c>.
    /// </summary>
    public static bool IsInstance(WsValue value, WsValue type, WsHeap heap, WsValue[] valueClasses)
    {
        if (type.Kind != WsValueKind.Class)
        {
            WsOperators.TryCompare(WsComparisonOperator.Equal, value, type, heap, out var equal, out _);
            return equal.AsBoolean;
        }
        if (value.Kind == WsValueKind.Null) return false;
        var kind = ValueClassOf(value.Kind);
        var current = heap.ClassOfValue(value);
        if (current.Kind != WsValueKind.Class && kind >= 0 && kind < valueClasses.Length) current = valueClasses[kind];
        if (current.Kind != WsValueKind.Class) return type == heap.ObjectClass;
        for (; current.Kind == WsValueKind.Class; current = heap.ClassBase(current))
        {
            if (current == type) return true;
        }
        return false;
    }

    /// <summary>A kind of value's class, as <see cref="WsValueClass"/> numbers it, or -1 for an instance's or <c>null</c>.</summary>
    public static int ValueClassOf(WsValueKind kind) => kind switch
    {
        WsValueKind.Boolean => (int)WsValueClass.Boolean,
        WsValueKind.Int64 => (int)WsValueClass.Integer,
        WsValueKind.Binary64 => (int)WsValueClass.Number,
        WsValueKind.String => (int)WsValueClass.String,
        WsValueKind.Array => (int)WsValueClass.Array,
        WsValueKind.Map => (int)WsValueClass.Map,
        WsValueKind.Set => (int)WsValueClass.Set,
        WsValueKind.Function or WsValueKind.Builtin or WsValueKind.BoundMethod => (int)WsValueClass.Function,
        WsValueKind.Class => (int)WsValueClass.Class,
        WsValueKind.Task => (int)WsValueClass.Task,
        _ => -1
    };

    /// <summary>Whether the member is a method of the value: an array's <c>push</c> and <c>pop</c>, a map's <c>has</c> and <c>remove</c>.</summary>
    private static bool IsMethod(WsValue target, WsMember member) =>
        (target.Kind == WsValueKind.Array && member is WsMember.Push or WsMember.Pop)
        || (target.Kind == WsValueKind.Map && member is WsMember.Has or WsMember.Remove)
        || (target.Kind == WsValueKind.Set && member is WsMember.Add or WsMember.Has or WsMember.Remove)
        || (target.Kind == WsValueKind.Task && member == WsMember.Forget);

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
        if (target.Kind == WsValueKind.Task && member == WsMember.Forget)
        {
            // t.forget(): nothing reports its exception, if it faults (section 3.8).
            if (count != 0) return WrongArgumentCount(name, 0, count, out error);
            heap.GetTask(target).Forgotten = true;
            error = null;
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
        target = heap.Held(target); // An instance holding a string is indexed as its string (section 3.7).
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
        target = heap.Held(target);
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
        collection = heap.Held(collection); // An instance holding a string is visited as its string (section 3.7).
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
