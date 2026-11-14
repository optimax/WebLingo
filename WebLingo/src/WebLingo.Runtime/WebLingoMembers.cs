using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// The members that values have (specification, section 3.6), by name. A VM resolves a member's name to one of these
/// once, when it loads a module, so that it does not compare names as it runs; a name that is no member of any value
/// is <see cref="Unknown"/>, and is kept for the message of its error.
/// </summary>
public enum WebLingoMember : byte
{
    Unknown, Length, Push, Pop, Has, Remove, Add, Dispose, ToString, Equal, Forget, SetMethod, SetProperty, SetStaticProperty, SetConstant,
    RemoveMethod,
    All, Any, Timeout, Parse,
    /// <summary>The names every value, or every class, has (section 3.7), last, so that one comparison finds them.</summary>
    ClassName, InstanceOf, Parent
}

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
        "setMethod" => WebLingoMember.SetMethod,
        "removeMethod" => WebLingoMember.RemoveMethod,
        "setProperty" => WebLingoMember.SetProperty,
        "setStaticProperty" => WebLingoMember.SetStaticProperty,
        "setConstant" or "static:const" => WebLingoMember.SetConstant,
        "all" => WebLingoMember.All,
        "any" => WebLingoMember.Any,
        "timeout" => WebLingoMember.Timeout,
        "parse" => WebLingoMember.Parse,
        "__className" => WebLingoMember.ClassName,
        "__instanceOf" => WebLingoMember.InstanceOf,
        "__parent" => WebLingoMember.Parent,
        _ => WebLingoMember.Unknown
    };

    /// <summary>
    /// Reads a member that is not called: <c>x.name</c>, or with <paramref name="optional"/> <c>x?.name</c>, which gives
    /// <c>null</c> for a <c>null</c> target or a member it does not have (section 3.7). An instance's field is its
    /// value, and its method a function bound to it.
    /// </summary>
    /// <summary>
    /// What <see cref="TryGet"/> fails with when the member is a property's getter, given as its result, which the VM
    /// calls on the target (section 3.7); <see cref="Setter"/>, what <see cref="TrySet"/> fails with for its setter.
    /// </summary>
    public static readonly WebLingoRuntimeError Getter = new(WebLingoDiagnosticCodes.InvalidOperation, "A property's getter, to call.");
    public static readonly WebLingoRuntimeError Setter = new(WebLingoDiagnosticCodes.InvalidOperation, "A property's setter, to call.");

    /// <summary>A property read on a class's instance: its getter, or WL3006 for one that has only a setter (section 3.7).</summary>
    private static bool TryGetProperty(WebLingoValue target, WebLingoValue classValue, string name, WebLingoHeap heap,
        out WebLingoValue getter, out WebLingoRuntimeError? error)
    {
        error = null;
        if (heap.TryGetAccessor(classValue, false, name, out getter))
        {
            error = Getter;
            return true;
        }
        if (!heap.TryGetAccessor(classValue, true, name, out _)) return false;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NoSuchMember,
            $"'{name}' of {WebLingoValueFormatter.Describe(target, heap)} can only be assigned: its property has no getter.");
        return true;
    }

    public static bool TryGet(WebLingoValue target, WebLingoMember member, string name, WebLingoHeap heap, bool optional, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        if (optional && target.Kind == WebLingoValueKind.Null)
        {
            result = WebLingoValue.Null;
            error = null;
            return true;
        }
        if (member >= WebLingoMember.ClassName && TryGetName(target, member, heap, out result, out error)) return error is null;
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
            if (TryGetProperty(target, collectionClass, name, heap, out result, out error)) return false;
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
            if (TryGetProperty(target, heap.ClassOf(target), name, heap, out result, out error)) return false;
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
            if (heap.TryGetStatic(target, "get:" + name, out result) && result.Kind == WebLingoValueKind.Function)
            {
                // A static property's getter, which the VM calls (section 3.7).
                error = Getter;
                return false;
            }
            if (TryGetValueMethod(target, member, name, heap, out var objectToString))
            {
                return heap.TryAllocateBoundMethod(target, objectToString, out result, out error);
            }
            if (IsClassMethod(target, member, heap))
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'{name}' is a method of {WebLingoValueFormatter.Describe(target, heap)}, and can only be called.");
                return false;
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
        if (ExtendedClassOf(target, heap) is { Kind: WebLingoValueKind.Class } valueClass
            && TryGetProperty(target, valueClass, name, heap, out result, out error))
        {
            return false; // Its kind's class's property, which an extension gave it (section 3.7).
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
    public static bool TrySet(WebLingoValue target, string name, WebLingoValue value, WebLingoHeap heap, out WebLingoRuntimeError? error) =>
        TrySet(target, name, value, heap, out error, out _);

    /// <summary>
    /// <see cref="TrySet"/>, failing with <see cref="Setter"/> for a property's setter, given as <paramref name="setter"/>,
    /// which the VM calls on the target with the value (section 3.7).
    /// </summary>
    public static bool TrySet(WebLingoValue target, string name, WebLingoValue value, WebLingoHeap heap, out WebLingoRuntimeError? error,
        out WebLingoValue setter)
    {
        setter = WebLingoValue.Null;
        if (name.Length > 2 && name[0] == '_' && name[1] == '_' && TrySetName(target, name, value, heap, out error)) return error is null;
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
            // A static field, or a static method, which the value replaces (section 3.7); not a constant.
            if (heap.TrySetStatic(target, name, value, out var isConstant)) return true;
            if (isConstant)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'{name}' is a constant of {WebLingoValueFormatter.Describe(target, heap)}, and cannot be assigned.");
                return false;
            }
            // A static property's setter, which the VM calls with the value (section 3.7); else a new static member.
            if (heap.TryGetStatic(target, "set:" + name, out setter) && setter.Kind == WebLingoValueKind.Function)
            {
                error = Setter;
                return false;
            }
            setter = WebLingoValue.Null;
            if (heap.TryGetStatic(target, "get:" + name, out _))
            {
                error = NoSetter(target, name, heap);
                return false;
            }
            return heap.TryAddStatic(target, name, value, out error);
        }
        if (IsClassedCollection(target, heap, out var collectionClass))
        {
            var collectionAssignee = heap.FindAssignee(collectionClass, name, out setter);
            if (collectionAssignee == WebLingoHeap.Assignee.Field) return heap.TrySetField(target, name, value, out error, collectionClass);
            error = NotAField(collectionAssignee, target, name, heap);
            return false;
        }
        if (target.Kind != WebLingoValueKind.Instance)
        {
            if (ExtendedClassOf(target, heap) is { Kind: WebLingoValueKind.Class } valueClass
                && heap.FindAssignee(valueClass, name, out setter) is var valueAssignee
                    and (WebLingoHeap.Assignee.Setter or WebLingoHeap.Assignee.ReadOnly))
            {
                // Its kind's class's property, which an extension gave it (section 3.7).
                error = NotAField(valueAssignee, target, name, heap);
                return false;
            }
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"Only an instance's fields can be assigned, not a member of {WebLingoValueFormatter.Describe(target, heap)}.");
            return false;
        }
        var classValue = heap.ClassOf(target);
        var assignee = heap.FindAssignee(classValue, name, out setter);
        if (assignee == WebLingoHeap.Assignee.Field) return heap.TrySetField(target, name, value, out error, classValue);
        error = NotAField(assignee, target, name, heap);
        return false;
    }

    /// <summary>
    /// Why assigning <c>x.name</c> on a class's value assigns no field: a method, which cannot be assigned (WL3001), a
    /// property's setter, which the VM calls (<see cref="Setter"/>), or a property without one (WL3001; section 3.7).
    /// </summary>
    private static WebLingoRuntimeError NotAField(WebLingoHeap.Assignee assignee, WebLingoValue target, string name, WebLingoHeap heap) =>
        assignee switch
        {
            WebLingoHeap.Assignee.Setter => Setter,
            WebLingoHeap.Assignee.ReadOnly => NoSetter(target, name, heap),
            _ => new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'{name}' is a method of {WebLingoValueFormatter.Describe(target, heap)}, and cannot be assigned.")
        };

    private static WebLingoRuntimeError NoSetter(WebLingoValue target, string name, WebLingoHeap heap) =>
        new(WebLingoDiagnosticCodes.InvalidOperation,
            $"'{name}' of {WebLingoValueFormatter.Describe(target, heap)} cannot be assigned: its property has no setter.");

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
            // A base made while the program runs, and given no constructor, nor its bases, takes none, as Object's does.
            if (closure.Kind == WebLingoValueKind.Null) closure = heap.ConstructorFor(heap.ObjectClass);
            if (closure.Kind == WebLingoValueKind.Null) throw new InvalidOperationException("The base class has no constructor.");
        }
        else if (!heap.TryGetMethod(baseClass, name, out closure))
        {
            if (heap.TryGetAccessor(baseClass, false, name, out closure))
            {
                // super.name of the base's property: its getter, which the VM calls on this (section 3.7).
                bound = closure;
                error = Getter;
                return false;
            }
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
        if (heap.TryGetAccessor(heap.ClassOf(instance), false, name, out _))
        {
            error = IsProperty(instance, name, heap);
            return false;
        }
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
        var classValue = ExtendedClassOf(target, heap);
        if (classValue.Kind == WebLingoValueKind.Class && heap.TryGetMethod(classValue, name, out method)) return true;
        return TryGetObjectMethod(member, name, heap, out method);
    }

    /// <summary>
    /// The class whose extensions a value that is not an instance has, once an <c>extend</c> has run: its kind's, or
    /// <c>Object</c> if the program has not made that one; <c>null</c> before, and for <c>null</c> (section 3.7).
    /// </summary>
    private static WebLingoValue ExtendedClassOf(WebLingoValue target, WebLingoHeap heap)
    {
        // A class a metaclass made has its metaclass's methods (section 3.7).
        if (target.Kind == WebLingoValueKind.Class && heap.ClassOfValue(target) is { Kind: WebLingoValueKind.Class } metaclass) return metaclass;
        if (!heap.Extended || target.Kind == WebLingoValueKind.Null) return WebLingoValue.Null;
        var index = ValueClassOf(target.Kind);
        return index >= 0 && heap.ValueClasses is { } classes && classes[index].Kind == WebLingoValueKind.Class
            ? classes[index]
            : heap.ObjectClass;
    }

    /// <summary>
    /// What <c>C.name(…)</c> calls on a class, or a module: its static member, or its export (section 3.7); a static
    /// property is WL3001, read before it is called.
    /// </summary>
    public static bool TryGetStaticCallee(WebLingoValue target, WebLingoMember member, string name, WebLingoHeap heap, out WebLingoValue callee,
        out WebLingoRuntimeError? error)
    {
        if (TryGet(target, member, name, heap, false, out callee, out error)) return true;
        if (ReferenceEquals(error, Getter)) error = IsProperty(target, name, heap);
        return false;
    }

    private static WebLingoRuntimeError IsProperty(WebLingoValue target, string name, WebLingoHeap heap) =>
        new(WebLingoDiagnosticCodes.InvalidOperation,
            $"'{name}' of {WebLingoValueFormatter.Describe(target, heap)} is a property: read it, then call what it gives.");

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
    /// <summary>
    /// <c>x.__instanceOf</c>, a value's class, <c>null</c> for <c>null</c>; and a class's <c>__className</c>, its name,
    /// <c>null</c> for an unnamed one, and <c>__parent</c>, its base, <c>null</c> for <c>Object</c>. They come before a
    /// program's own members of those names (section 3.7). False for a member that is none of them there.
    /// </summary>
    private static bool TryGetName(WebLingoValue target, WebLingoMember member, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = null;
        if (member == WebLingoMember.InstanceOf)
        {
            result = ClassOfAny(target, heap);
            return true;
        }
        if (target.Kind != WebLingoValueKind.Class) return false;
        if (member == WebLingoMember.Parent)
        {
            result = heap.ClassBase(target);
            return true;
        }
        if (heap.IsNamed(target)) heap.TryAllocateString(heap.ClassName(target), out result, out error);
        return true;
    }

    /// <summary>
    /// A value's class: an instance's, or a classed collection's, its own; any other value's, its kind's, or <c>Object</c>
    /// for a kind whose class the program has not made; none for <c>null</c> (section 3.7).
    /// </summary>
    private static WebLingoValue ClassOfAny(WebLingoValue value, WebLingoHeap heap)
    {
        if (value.Kind == WebLingoValueKind.Null) return WebLingoValue.Null;
        var own = heap.ClassOfValue(value);
        if (own.Kind == WebLingoValueKind.Class) return own;
        var index = ValueClassOf(value.Kind);
        return index >= 0 && heap.ValueClasses is { } classes && classes[index].Kind == WebLingoValueKind.Class
            ? classes[index]
            : heap.ObjectClass;
    }

    /// <summary>
    /// <c>x.name = v</c> for a name every value, or every class, has (section 3.7): a class's <c>__className</c> takes a
    /// string, or <c>null</c> to unname it (WL3001 otherwise); <c>__instanceOf</c> and <c>__parent</c> cannot be
    /// assigned (WL3001). False for a name that is none of them there.
    /// </summary>
    private static bool TrySetName(WebLingoValue target, string name, WebLingoValue value, WebLingoHeap heap, out WebLingoRuntimeError? error)
    {
        error = null;
        var member = Find(name);
        if (member < WebLingoMember.ClassName || (member != WebLingoMember.InstanceOf && target.Kind != WebLingoValueKind.Class)) return false;
        if (member == WebLingoMember.ClassName && value.Kind is WebLingoValueKind.String or WebLingoValueKind.Null)
        {
            heap.RenameClass(target, value.Kind == WebLingoValueKind.String ? heap.GetString(value) : "");
            return true;
        }
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, member == WebLingoMember.ClassName
            ? $"A class's name is a string, not {WebLingoValueFormatter.Describe(value, heap)}."
            : $"'{name}' of {WebLingoValueFormatter.Describe(target, heap)} cannot be assigned.");
        return true;
    }

    /// <summary>
    /// A class's built-in method (section 3.7): <c>setMethod(name, f)</c>, <c>setProperty(name, getter, setter)</c>,
    /// <c>setStaticProperty(name, getter, setter)</c> and <c>setConstant(name, value)</c>, each giving <c>null</c>. A name
    /// is a string that is a name; a method a function, and a getter or a setter a function or <c>null</c> (WL3001
    /// otherwise).
    /// </summary>
    private static bool TryCallClassMethod(WebLingoValue target, WebLingoMember member, string name, WebLingoValue[] values, int first,
        int count, WebLingoHeap heap, out WebLingoRuntimeError? error)
    {
        var arity = member switch
        {
            WebLingoMember.SetProperty or WebLingoMember.SetStaticProperty => 3,
            _ => 2
        };
        if (count != arity) return WrongArgumentCount(name, arity, count, out error);
        if (values[first].Kind != WebLingoValueKind.String || !IsName(heap.GetString(values[first])))
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"A member's name is a name, letters, digits and '_', not {WebLingoValueFormatter.Describe(values[first], heap)}.");
            return false;
        }
        var memberName = heap.GetString(values[first]);
        if (member == WebLingoMember.SetConstant) return heap.TrySetConstant(target, memberName, values[first + 1], out error);
        for (var i = 1; i < arity; i++)
        {
            var function = values[first + i];
            if (function.Kind == WebLingoValueKind.Function || (function.Kind == WebLingoValueKind.Null && member != WebLingoMember.SetMethod))
            {
                continue;
            }
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, member == WebLingoMember.SetMethod
                ? $"A method is a function, not {WebLingoValueFormatter.Describe(function, heap)}."
                : $"A property's getter or setter is a function, or null, not {WebLingoValueFormatter.Describe(function, heap)}.");
            return false;
        }
        return member == WebLingoMember.SetMethod
            ? heap.TrySetMethod(target, memberName, values[first + 1], out error)
            : heap.TrySetProperty(target, memberName, values[first + 1], values[first + 2], member == WebLingoMember.SetStaticProperty, out error);
    }

    /// <summary>
    /// <c>Task.all(values)</c>, <c>Task.any(values)</c>, each given an array (WL3001 otherwise), and
    /// <c>Task.timeout(task, milliseconds)</c>: a task, which the execution's scheduler completes (section 3.8).
    /// </summary>
    private static bool TryCombine(WebLingoMember member, string name, WebLingoValue[] values, int first, int count, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var arity = member == WebLingoMember.Timeout ? 2 : 1;
        if (count != arity) return WrongArgumentCount(name, arity, count, out error);
        var scheduler = heap.Scheduler ?? throw new InvalidOperationException("The execution has no scheduler.");
        if (member == WebLingoMember.Timeout) return scheduler.TryTimeout(values[first], values[first + 1], out result, out error);
        if (values[first].Kind != WebLingoValueKind.Array)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'{name}' takes an array of values, not {WebLingoValueFormatter.Describe(values[first], heap)}.");
            return false;
        }
        var given = new List<WebLingoValue>(heap.GetArray(values[first]));
        return member == WebLingoMember.All ? scheduler.TryAll(given, out result, out error) : scheduler.TryAny(given, out result, out error);
    }

    /// <summary>Whether a string is a name, as a program writes one: ASCII letters, digits and <c>_</c>, not first a digit.</summary>
    private static bool IsName(string text)
    {
        if (text.Length == 0 || char.IsAsciiDigit(text[0])) return false;
        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    private static bool IsMethod(WebLingoValue target, WebLingoMember member) =>
        (target.Kind == WebLingoValueKind.Array && member is WebLingoMember.Push or WebLingoMember.Pop)
        || (target.Kind == WebLingoValueKind.Map && member is WebLingoMember.Has or WebLingoMember.Remove)
        || (target.Kind == WebLingoValueKind.Set && member is WebLingoMember.Add or WebLingoMember.Has or WebLingoMember.Remove)
        || (target.Kind == WebLingoValueKind.Task && member == WebLingoMember.Forget);

    /// <summary>
    /// Whether <c>x.name(…)</c> calls a class's built-in method, <c>setMethod</c>, <c>setProperty</c>,
    /// <c>setStaticProperty</c> or <c>setConstant</c>, which a class has when it has no static member of that name; a
    /// <c>static const</c> calls <c>setConstant</c> by a name no program can declare (section 3.7).
    /// </summary>
    /// <remarks><c>Task</c> has <c>all</c>, <c>any</c> and <c>timeout</c> besides (section 3.8).</remarks>
    public static bool IsClassMethod(WebLingoValue target, WebLingoMember member, WebLingoHeap heap) =>
        target.Kind == WebLingoValueKind.Class
        && (member is WebLingoMember.SetMethod or WebLingoMember.SetProperty or WebLingoMember.SetStaticProperty or WebLingoMember.SetConstant
                or WebLingoMember.RemoveMethod
            || (member is WebLingoMember.All or WebLingoMember.Any or WebLingoMember.Timeout && heap.ValueClasses is { } classes
                && classes[(int)WebLingoValueClass.Task] == target)
            || (member == WebLingoMember.Parse && ParsedKind(target, heap) != WebLingoValueKind.Null));

    /// <summary>The kind <c>parse</c> reads a string as, for <c>Integer</c>, <c>Number</c> and <c>Boolean</c>; null's for any other class.</summary>
    private static WebLingoValueKind ParsedKind(WebLingoValue target, WebLingoHeap heap)
    {
        if (heap.ValueClasses is not { } classes) return WebLingoValueKind.Null;
        if (classes[(int)WebLingoValueClass.Integer] == target) return WebLingoValueKind.Int64;
        if (classes[(int)WebLingoValueClass.Number] == target) return WebLingoValueKind.Binary64;
        return classes[(int)WebLingoValueClass.Boolean] == target ? WebLingoValueKind.Boolean : WebLingoValueKind.Null;
    }

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
        if (IsClassMethod(target, member, heap))
        {
            if (member is WebLingoMember.All or WebLingoMember.Any or WebLingoMember.Timeout)
            {
                return TryCombine(member, name, values, first, count, heap, out result, out error);
            }
            if (member == WebLingoMember.Parse)
            {
                // Integer.parse("10"), Number.parse("1.5") and Boolean.parse("true") (section 3.7).
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (values[first].Kind == WebLingoValueKind.String)
                {
                    return WebLingoConversions.TryParse(ParsedKind(target, heap), heap.GetString(values[first]), out result, out error);
                }
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'parse' reads a string, not {WebLingoValueFormatter.Describe(values[first], heap)}.");
                return false;
            }
            if (member == WebLingoMember.RemoveMethod)
            {
                // Point.removeMethod(name): whether the class had that method of its own (section 3.7).
                if (count != 1) return WrongArgumentCount(name, 1, count, out error);
                if (values[first].Kind != WebLingoValueKind.String || !IsName(heap.GetString(values[first])))
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                        $"A member's name is a name, letters, digits and '_', not {WebLingoValueFormatter.Describe(values[first], heap)}.");
                    return false;
                }
                result = WebLingoValue.FromBoolean(heap.RemoveMethod(target, heap.GetString(values[first])));
                error = null;
                return true;
            }
            return TryCallClassMethod(target, member, name, values, first, count, heap, out error);
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
