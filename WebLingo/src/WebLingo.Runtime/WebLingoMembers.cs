using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// The members that values have (specification, section 3.6), by name. A VM resolves a member's name to one of these
/// once, when it loads a module, so that it does not compare names as it runs; a name that is no member of any value
/// is <see cref="Unknown"/>, and is kept for the message of its error.
/// </summary>
public enum WebLingoMember : byte { Unknown, Length, Push, Pop, Has, Remove, Add, Dispose, ToString, Equal, Forget }

/// <summary>A member's name as a module's constant gives it, and the member it names.</summary>
public readonly record struct WebLingoMemberName(WebLingoMember Member, string Text);

/// <summary>
/// Members and indexing, the same for every VM (specification, section 3.6): <c>x.name</c>, <c>x.name(…)</c>,
/// <c>x[i]</c> and <c>x[i] = v</c>. A string's <c>length</c> counts its Unicode scalar values, and <c>s[i]</c> is its
/// <c>i</c>-th, as a string of one. A member a value does not have is WL3006, an index outside it WL3005, and an index
/// of the wrong kind, or a value that cannot be indexed, WL3001.
/// </summary>
public static class WebLingoMembers
{
    public static WebLingoMember Find(string name) => name switch
    {
        "length" => WebLingoMember.Length,
        "push" => WebLingoMember.Push,
        "pop" => WebLingoMember.Pop,
        "has" => WebLingoMember.Has,
        "remove" => WebLingoMember.Remove,
        "add" => WebLingoMember.Add,
        "dispose" => WebLingoMember.Dispose,
        "toString" => WebLingoMember.ToString,
        "equal" => WebLingoMember.Equal,
        "forget" => WebLingoMember.Forget,
        _ => WebLingoMember.Unknown
    };

    /// <summary>
    /// Reads a member that is not called: <c>x.name</c>, or with <paramref name="optional"/> <c>x?.name</c>, which gives
    /// <c>null</c> for a <c>null</c> target or a member it does not have (section 3.7). An instance's field is its
    /// value, and its method a function bound to it.
    /// </summary>
    public static bool TryGet(WebLingoValue target, WebLingoMember member, string name, WebLingoHeap heap, bool optional, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        if (optional && target.Kind == WebLingoValueKind.Null)
        {
            result = WebLingoValue.Null;
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
        if (target.Kind == WebLingoValueKind.Instance)
        {
            error = null;
            if (heap.TryGetField(target, name, out result)) return true;
            if (heap.TryGetMethod(heap.ClassOf(target), name, out var method))
            {
                return heap.TryAllocateBoundMethod(target, method, out result, out error, member == WebLingoMember.Dispose);
            }
            // Its class's static member, which its own members shadow (section 3.7).
            if (heap.TryGetStatic(heap.ClassOf(target), name, out result)) return true;
            // An instance holding a value has that value's members too, its string's length (section 3.7).
            if (heap.IsHolding(target)) return TryGet(heap.Held(target), member, name, heap, optional, out result, out error);
            if (optional) return true;
            error = NoSuchMember(target, name, heap);
            return false;
        }
        if (target.Kind == WebLingoValueKind.Module && heap.TryGetExport(target, name, out var cell))
        {
            // An export, as it is when read; one its module has not set yet is read before its initialization; a standard
            // module's, its value (section 3.9).
            if (cell.Kind != WebLingoValueKind.Cell)
            {
                result = cell;
                error = null;
                return true;
            }
            if (heap.TryReadCell(cell, out result))
            {
                error = null;
                return true;
            }
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.ReadBeforeInitialization,
                $"'{name}' of {WebLingoValueFormatter.Describe(target, heap)} is read before its module has set it.");
            return false;
        }
        if (target.Kind == WebLingoValueKind.Class)
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
        if (target.Kind == WebLingoValueKind.String && member == WebLingoMember.Length)
        {
            result = WebLingoValue.FromInt64(ScalarCount(heap.GetString(target)));
            error = null;
            return true;
        }
        if (target.Kind == WebLingoValueKind.Array && member == WebLingoMember.Length)
        {
            result = WebLingoValue.FromInt64(heap.GetArray(target).Count);
            error = null;
            return true;
        }
        if (target.Kind == WebLingoValueKind.Map && member == WebLingoMember.Length)
        {
            result = WebLingoValue.FromInt64(heap.MapCount(target));
            error = null;
            return true;
        }
        if (target.Kind == WebLingoValueKind.Set && member == WebLingoMember.Length)
        {
            result = WebLingoValue.FromInt64(heap.SetCount(target));
            error = null;
            return true;
        }
        result = WebLingoValue.Null;
        if (IsMethod(target, member))
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'{name}' is a method of {WebLingoValueFormatter.Describe(target)}, and can only be called.");
            return false;
        }
        error = optional ? null : NoSuchMember(target, name, heap);
        return optional;
    }

    /// <summary>
    /// Assigns a field of an instance, <c>x.name = v</c>, which exists from then on, or a static field of a class, its
    /// own or its base's, which it declares (WL3006 otherwise; section 3.7). A method's name is not a field's, and
    /// nothing else has fields to assign (WL3001).
    /// </summary>
    public static bool TrySet(WebLingoValue target, string name, WebLingoValue value, WebLingoHeap heap, out WebLingoRuntimeError? error)
    {
        if (target.Kind == WebLingoValueKind.Module)
        {
            // An export is its module's to change (section 3.9).
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'{name}' of {WebLingoValueFormatter.Describe(target, heap)} cannot be assigned: an export is its module's to change.");
            return false;
        }
        if (target.Kind == WebLingoValueKind.Class)
        {
            error = null;
            if (heap.TrySetStatic(target, name, value, out var isMethod)) return true;
            if (!isMethod) return heap.TryAddStatic(target, name, value, out error); // A new static member (section 3.7).
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'{name}' is a static method of {WebLingoValueFormatter.Describe(target, heap)}, and cannot be assigned.");
            return false;
        }
        if (IsClassedCollection(target, heap, out var collectionClass))
        {
            if (heap.TryGetMethod(collectionClass, name, out _))
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'{name}' is a method of {WebLingoValueFormatter.Describe(target, heap)}, and cannot be assigned.");
                return false;
            }
            return heap.TrySetField(target, name, value, out error);
        }
        if (target.Kind != WebLingoValueKind.Instance)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"Only an instance's fields can be assigned, not a member of {WebLingoValueFormatter.Describe(target, heap)}.");
            return false;
        }
        if (heap.TryGetMethod(heap.ClassOf(target), name, out _))
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'{name}' is a method of {WebLingoValueFormatter.Describe(target, heap)}, and cannot be assigned.");
            return false;
        }
        return heap.TrySetField(target, name, value, out error);
    }

    /// <summary>
    /// What <c>super</c> names, in a method of <paramref name="classValue"/>: its base's method <paramref name="name"/>, or
    /// its constructor for <c>constructor</c>, bound to <paramref name="self"/> (section 3.7); a method the base does not
    /// have is WL3006.
    /// </summary>
    public static bool TryGetSuper(WebLingoValue classValue, WebLingoValue self, string name, WebLingoHeap heap, out WebLingoValue bound,
        out WebLingoRuntimeError? error)
    {
        bound = WebLingoValue.Null;
        var baseClass = heap.ClassBase(classValue);
        if (baseClass.Kind != WebLingoValueKind.Class) throw new InvalidOperationException("The class extends no other.");
        WebLingoValue closure;
        if (name == "constructor")
        {
            closure = heap.ConstructorFor(baseClass);
            if (closure.Kind == WebLingoValueKind.Null) throw new InvalidOperationException("The base class has no constructor.");
        }
        else if (!heap.TryGetMethod(baseClass, name, out closure))
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NoSuchMember,
                $"{Capitalized(WebLingoValueFormatter.Describe(baseClass, heap))} has no method '{name}'.");
            return false;
        }
        return heap.TryAllocateBoundMethod(self, closure, out bound, out error);
    }

    /// <summary>Whether a value is a collection that a class extending Array, Map or Set made, and its class.</summary>
    public static bool IsClassedCollection(WebLingoValue value, WebLingoHeap heap, out WebLingoValue classValue)
    {
        classValue = value.Kind is WebLingoValueKind.Array or WebLingoValueKind.Map or WebLingoValueKind.Set && value.HasClass
            ? heap.ClassOfValue(value)
            : WebLingoValue.Null;
        return classValue.Kind == WebLingoValueKind.Class;
    }

    /// <summary>
    /// What <c>x.name(…)</c> calls when <c>x</c> is a collection of a class extending Array, Map or Set: a field's
    /// value, or a method of its class (<paramref name="isMethod"/>); false for anything else, its own members included.
    /// </summary>
    public static bool TryFindCollectionCallee(WebLingoValue target, string name, WebLingoHeap heap, out WebLingoValue callee, out bool isMethod)
    {
        callee = WebLingoValue.Null;
        isMethod = false;
        if (!IsClassedCollection(target, heap, out var classValue)) return false;
        if (heap.TryGetField(target, name, out callee)) return true;
        isMethod = heap.TryGetMethod(classValue, name, out callee);
        return isMethod || heap.TryGetStatic(classValue, name, out callee);
    }

    /// <summary>
    /// What <c>x.name(…)</c> calls when <c>x</c> is an instance: a field's value, as any function value is called, or
    /// a method, which runs on the instance (<paramref name="isMethod"/>); WL3006 if it has neither.
    /// </summary>
    public static bool TryFindCallee(WebLingoValue instance, string name, WebLingoHeap heap, out WebLingoValue callee, out bool isMethod,
        out WebLingoRuntimeError? error)
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
    /// does nothing. A value whose class has no dispose method, or that is no instance, is WL3006.
    /// </summary>
    public static bool TryBeginDisposal(WebLingoValue target, WebLingoHeap heap, out WebLingoValue[]? chain, out WebLingoRuntimeError? error)
    {
        chain = null;
        error = null;
        if (target.Kind == WebLingoValueKind.Instance)
        {
            var methods = heap.DisposeChain(heap.ClassOf(target));
            if (methods.Length > 0)
            {
                if (heap.TryMarkDisposed(target)) chain = methods;
                return true;
            }
        }
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NoSuchMember,
            $"{Capitalized(WebLingoValueFormatter.Describe(target, heap))} has no method 'dispose', and cannot be disposed.");
        return false;
    }

    /// <summary>
    /// A method of a value that is not an instance: its kind's class's, an extension's, once an <c>extend</c> has run, or
    /// <c>Object</c>'s if the program has not made that class; or <c>Object</c>'s <c>toString()</c> or <c>equal()</c>,
    /// which every value has, <c>null</c> included, which has no class (section 3.7).
    /// </summary>
    public static bool TryGetValueMethod(WebLingoValue target, WebLingoMember member, string name, WebLingoHeap heap, out WebLingoValue method)
    {
        if (heap.Extended && target.Kind != WebLingoValueKind.Null)
        {
            var index = ValueClassOf(target.Kind);
            var classValue = index >= 0 && heap.ValueClasses is { } classes && classes[index].Kind == WebLingoValueKind.Class
                ? classes[index]
                : heap.ObjectClass;
            if (classValue.Kind == WebLingoValueKind.Class && heap.TryGetMethod(classValue, name, out method)) return true;
        }
        return TryGetObjectMethod(member, name, heap, out method);
    }

    /// <summary>
    /// The operator a class declares (<c>static operator +(a, b)</c>, a static method named <c>operator+</c>), that an
    /// operation on two values calls: its left operand's class's, its own or a base's, else its right's; false when
    /// neither is an instance, or a collection, of a class that declares it (section 3.7).
    /// </summary>
    public static bool TryFindOperator(string name, WebLingoValue left, WebLingoValue right, WebLingoHeap heap, out WebLingoValue method) =>
        TryFindOperator(name, left, heap, out method) || TryFindOperator(name, right, heap, out method);

    private static bool TryFindOperator(string name, WebLingoValue operand, WebLingoHeap heap, out WebLingoValue method)
    {
        method = WebLingoValue.Null;
        var classValue = heap.ClassOfValue(operand);
        return classValue.Kind == WebLingoValueKind.Class && heap.TryGetStatic(classValue, name, out method)
            && method.Kind == WebLingoValueKind.Function;
    }

    /// <summary>
    /// <c>Object</c>'s <c>toString()</c> or <c>equal()</c>, which every value has (section 3.7); false for another
    /// member, or before the program has made <c>Object</c>.
    /// </summary>
    public static bool TryGetObjectMethod(WebLingoMember member, string name, WebLingoHeap heap, out WebLingoValue method)
    {
        method = WebLingoValue.Null;
        return member is WebLingoMember.ToString or WebLingoMember.Equal && heap.ObjectClass.Kind == WebLingoValueKind.Class
            && heap.TryGetMethod(heap.ObjectClass, name, out method);
    }

    /// <summary>
    /// <c>value is type</c> (section 3.7): an instance's class, or a value's kind's class (<paramref name="valueClasses"/>,
    /// by <see cref="WebLingoValueClass"/>), is <paramref name="type"/> or extends it; <c>null</c> is of no class, and every
    /// other value is an <c>Object</c>, its kind's class made or not. A type that is not a class is compared with
    /// <c>==</c> here; a VM calls the type's <c>equal()</c> instead, once the program has <c>Object</c>.
    /// </summary>
    public static bool IsInstance(WebLingoValue value, WebLingoValue type, WebLingoHeap heap, WebLingoValue[] valueClasses)
    {
        if (type.Kind != WebLingoValueKind.Class)
        {
            WebLingoOperators.TryCompare(WebLingoComparisonOperator.Equal, value, type, heap, out var equal, out _);
            return equal.AsBoolean;
        }
        if (value.Kind == WebLingoValueKind.Null) return false;
        var kind = ValueClassOf(value.Kind);
        var current = heap.ClassOfValue(value);
        if (current.Kind != WebLingoValueKind.Class && kind >= 0 && kind < valueClasses.Length) current = valueClasses[kind];
        if (current.Kind != WebLingoValueKind.Class) return type == heap.ObjectClass;
        for (; current.Kind == WebLingoValueKind.Class; current = heap.ClassBase(current))
        {
            if (current == type) return true;
        }
        return false;
    }

    /// <summary>A kind of value's class, as <see cref="WebLingoValueClass"/> numbers it, or -1 for an instance's or <c>null</c>.</summary>
    public static int ValueClassOf(WebLingoValueKind kind) => kind switch
    {
        WebLingoValueKind.Boolean => (int)WebLingoValueClass.Boolean,
        WebLingoValueKind.Int64 => (int)WebLingoValueClass.Integer,
        WebLingoValueKind.Binary64 => (int)WebLingoValueClass.Number,
        WebLingoValueKind.String => (int)WebLingoValueClass.String,
        WebLingoValueKind.Array => (int)WebLingoValueClass.Array,
        WebLingoValueKind.Map => (int)WebLingoValueClass.Map,
        WebLingoValueKind.Set => (int)WebLingoValueClass.Set,
        WebLingoValueKind.Function or WebLingoValueKind.Builtin or WebLingoValueKind.BoundMethod => (int)WebLingoValueClass.Function,
        WebLingoValueKind.Class => (int)WebLingoValueClass.Class,
        WebLingoValueKind.Task => (int)WebLingoValueClass.Task,
        WebLingoValueKind.Module => (int)WebLingoValueClass.Module,
        _ => -1
    };

    /// <summary>Whether the member is a method of the value: an array's <c>push</c> and <c>pop</c>, a map's <c>has</c> and <c>remove</c>.</summary>
    private static bool IsMethod(WebLingoValue target, WebLingoMember member) =>
        (target.Kind == WebLingoValueKind.Array && member is WebLingoMember.Push or WebLingoMember.Pop)
        || (target.Kind == WebLingoValueKind.Map && member is WebLingoMember.Has or WebLingoMember.Remove)
        || (target.Kind == WebLingoValueKind.Set && member is WebLingoMember.Add or WebLingoMember.Has or WebLingoMember.Remove)
        || (target.Kind == WebLingoValueKind.Task && member == WebLingoMember.Forget);

    /// <summary>
    /// Calls a method, <c>x.name(…)</c>, with the <paramref name="count"/> arguments from
    /// <paramref name="values"/>[<paramref name="first"/>] on. A member that is not a method is not a function (WL2005).
    /// </summary>
    public static bool TryCall(WebLingoValue target, WebLingoMember member, string name, WebLingoValue[] values, int first, int count,
        WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (target.Kind == WebLingoValueKind.Array && member == WebLingoMember.Push)
        {
            if (count != 1) return WrongArgumentCount(name, 1, count, out error);
            if (!heap.TryAddToArray(target, values[first], out error)) return false;
            return true;
        }
        if (target.Kind == WebLingoValueKind.Array && member == WebLingoMember.Pop)
        {
            if (count != 0) return WrongArgumentCount(name, 0, count, out error);
            if (heap.GetArray(target).Count == 0)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IndexOutOfRange, "'pop' cannot remove an element from an empty array.");
                return false;
            }
            result = heap.RemoveLastOfArray(target);
            error = null;
            return true;
        }
        if (target.Kind == WebLingoValueKind.Map && member == WebLingoMember.Has)
        {
            if (count != 1) return WrongArgumentCount(name, 1, count, out error);
            if (!heap.TryFindInMap(target, values[first], out _, out var found, out error)) return false;
            result = WebLingoValue.FromBoolean(found);
            return true;
        }
        if (target.Kind == WebLingoValueKind.Map && member == WebLingoMember.Remove)
        {
            if (count != 1) return WrongArgumentCount(name, 1, count, out error);
            if (!heap.TryRemoveFromMap(target, values[first], out var removed, out error)) return false;
            result = WebLingoValue.FromBoolean(removed);
            return true;
        }
        if (target.Kind == WebLingoValueKind.Task && member == WebLingoMember.Forget)
        {
            // t.forget(): nothing reports its exception, if it faults (section 3.8).
            if (count != 0) return WrongArgumentCount(name, 0, count, out error);
            heap.GetTask(target).Forgotten = true;
            error = null;
            return true;
        }
        if (target.Kind == WebLingoValueKind.Set && member is WebLingoMember.Add or WebLingoMember.Has or WebLingoMember.Remove)
        {
            if (count != 1) return WrongArgumentCount(name, 1, count, out error);
            bool answer;
            var succeeded = member switch
            {
                WebLingoMember.Add => heap.TryAddToSet(target, values[first], out answer, out error),
                WebLingoMember.Has => heap.TryFindInSet(target, values[first], out answer, out error),
                _ => heap.TryRemoveFromSet(target, values[first], out answer, out error)
            };
            if (succeeded) result = WebLingoValue.FromBoolean(answer);
            return succeeded;
        }
        error = target.Kind is WebLingoValueKind.String or WebLingoValueKind.Array or WebLingoValueKind.Map or WebLingoValueKind.Set
                && member == WebLingoMember.Length
            ? new WebLingoRuntimeError(WebLingoDiagnosticCodes.NotAFunction,
                $"'{name}' of {WebLingoValueFormatter.Describe(target)} is not a method, and cannot be called.")
            : NoSuchMember(target, name, heap);
        return false;
    }

    /// <summary>
    /// <c>target[index]</c>, or with <paramref name="optional"/> <c>target?[index]</c>, which gives <c>null</c> where
    /// the other would raise a missing key (WL3007) or an index out of range (WL3005), and for a <c>null</c> target, but
    /// raises every other error as the other does (section 3.6).
    /// </summary>
    public static bool TryGetIndex(WebLingoValue target, WebLingoValue index, WebLingoHeap heap, bool optional, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = null;
        target = heap.Held(target); // An instance holding a string is indexed as its string (section 3.7).
        if (optional && target.Kind == WebLingoValueKind.Null) return true;
        if (target.Kind == WebLingoValueKind.Array)
        {
            var elements = heap.GetArray(target);
            if (!TryIndex(index, elements.Count, out var at, out error)) return Absent(optional, ref error);
            result = elements[(int)at];
            return true;
        }
        if (target.Kind == WebLingoValueKind.Map)
        {
            if (!heap.TryFindInMap(target, index, out result, out var found, out error)) return false;
            if (found || optional) return true;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NoSuchKey, $"The map has no key {KeyText(index, heap)}.");
            return false;
        }
        if (target.Kind != WebLingoValueKind.String)
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
    private static bool Absent(bool optional, ref WebLingoRuntimeError? error)
    {
        if (!optional || error?.Code != WebLingoDiagnosticCodes.IndexOutOfRange) return false;
        error = null;
        return true;
    }

    /// <summary><c>target[index] = value</c>.</summary>
    public static bool TrySetIndex(WebLingoValue target, WebLingoValue index, WebLingoValue value, WebLingoHeap heap, out WebLingoRuntimeError? error)
    {
        target = heap.Held(target);
        if (target.Kind == WebLingoValueKind.Array)
        {
            var elements = heap.GetArray(target);
            if (!TryIndex(index, elements.Count, out var at, out error)) return false;
            elements[(int)at] = value;
            return true;
        }
        if (target.Kind == WebLingoValueKind.Map) return heap.TrySetInMap(target, index, value, out error);
        error = target.Kind == WebLingoValueKind.String
            ? new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "A string cannot be changed: strings are immutable.")
            : CannotBeIndexed(target);
        return false;
    }

    /// <summary>
    /// The next step of <c>foreach</c> (section 3.6): from <paramref name="position"/> (an int64, from 0), the next
    /// element of an array, or Unicode scalar value of a string (as a string of one), into
    /// <paramref name="values"/>[<paramref name="first"/>], or the next entry of a map, its key and its value, into it
    /// and the one after, with the position after it; <paramref name="done"/> when there is nothing more. A map is
    /// looped over with a key and a value, anything else with one item, and anything else again not at all (WL3001).
    /// </summary>
    public static bool TryNext(WebLingoValue collection, WebLingoValue position, int items, WebLingoValue[] values, int first, WebLingoHeap heap,
        out WebLingoValue next, out bool done, out WebLingoRuntimeError? error)
    {
        next = position;
        done = false;
        collection = heap.Held(collection); // An instance holding a string is visited as its string (section 3.7).
        error = null;
        if (position.Kind != WebLingoValueKind.Int64) throw new InvalidOperationException("A loop's position is not an integer.");
        var at = position.AsInt64;
        switch (collection.Kind)
        {
            case WebLingoValueKind.Array when items == 1:
            {
                var elements = heap.GetArray(collection);
                done = at >= elements.Count;
                if (done) return true;
                values[first] = elements[(int)at];
                next = WebLingoValue.FromInt64(at + 1);
                return true;
            }
            case WebLingoValueKind.Set when items == 1:
            {
                done = at >= heap.SetCount(collection);
                if (done) return true;
                values[first] = heap.SetValueAt(collection, (int)at);
                next = WebLingoValue.FromInt64(at + 1);
                return true;
            }
            case WebLingoValueKind.Map when items == 2:
            {
                done = at >= heap.MapCount(collection);
                if (done) return true;
                (values[first], values[first + 1]) = heap.MapEntryAt(collection, (int)at);
                next = WebLingoValue.FromInt64(at + 1);
                return true;
            }
            case WebLingoValueKind.String when items == 1:
            {
                var text = heap.GetString(collection);
                done = at >= text.Length;
                if (done) return true;
                var start = (int)at;
                var length = start + 1 < text.Length && char.IsSurrogatePair(text[start], text[start + 1]) ? 2 : 1;
                if (!heap.TryAllocateString(text.Substring(start, length), out values[first], out error)) return false;
                next = WebLingoValue.FromInt64(at + length);
                return true;
            }
            case WebLingoValueKind.Map:
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    "A map's loop takes a key and a value: 'foreach (var (key, value) in map)'.");
                return false;
            case WebLingoValueKind.Array or WebLingoValueKind.String or WebLingoValueKind.Set:
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"Only a map's loop takes a key and a value, not {WebLingoValueFormatter.Describe(collection)}'s.");
                return false;
            default:
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"{Capitalized(WebLingoValueFormatter.Describe(collection))} cannot be looped over with 'foreach'.");
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
    private static bool TryIndex(WebLingoValue index, long length, out long position, out WebLingoRuntimeError? error)
    {
        position = 0;
        if (index.Kind != WebLingoValueKind.Int64)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"An index must be an integer, not {WebLingoValueFormatter.Describe(index)}.");
            return false;
        }
        position = index.AsInt64;
        if (position < 0 || position >= length)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IndexOutOfRange, length == 0
                ? $"There is no index {Invariant(position)} in something empty."
                : $"The index {Invariant(position)} is outside 0 to {Invariant(length - 1)}.");
            return false;
        }
        error = null;
        return true;
    }

    private static bool WrongArgumentCount(string name, int arity, int count, out WebLingoRuntimeError? error)
    {
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.WrongArgumentCount,
            $"'{name}' takes {arity} {(arity == 1 ? "argument" : "arguments")}, not {count}.");
        return false;
    }

    /// <summary>A key as a message shows it: a string quoted, anything else as <c>write</c> writes it.</summary>
    private static string KeyText(WebLingoValue key, WebLingoHeap heap) =>
        key.Kind == WebLingoValueKind.String ? WebLingoValueFormatter.Quote(heap.GetString(key)) : WebLingoValueFormatter.Format(key);

    private static WebLingoRuntimeError CannotBeIndexed(WebLingoValue target) =>
        new(WebLingoDiagnosticCodes.InvalidOperation, $"{Capitalized(WebLingoValueFormatter.Describe(target))} cannot be indexed.");

    private static WebLingoRuntimeError NoSuchMember(WebLingoValue target, string name, WebLingoHeap heap) =>
        new(WebLingoDiagnosticCodes.NoSuchMember, $"{Capitalized(WebLingoValueFormatter.Describe(target, heap))} has no member '{name}'.");

    private static string Invariant(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
