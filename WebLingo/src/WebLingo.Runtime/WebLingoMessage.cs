using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// A value copied out of its heap, whole, into a message of its own, and from the message into another heap (specification,
/// section 9, "M0.26 parts", part 1; the owner's answers, A2 (a) of "M0.26: Parallel Work" and A1 (a) of "M0.26, Part 1:
/// Values Copied Between Heaps"), so that workers with heaps of their own exchange values and share none: <c>null</c>,
/// Booleans, integers, numbers, decimals, strings, bytes, dates, and arrays, maps and sets of them, their cycles and the
/// references they share kept as they were; and an error, an instance of <c>Error</c> or of a class extending it, which
/// arrives as an <c>Error</c>, its <c>message</c> and its <c>code</c> as they were, and two fields of its own, its class's name,
/// <c>className</c>, and where it was thrown, <c>thrownAt</c>. Anything else, an instance of another class, a function, a
/// class, a task, a module, a value of a class extending another kind of value, cannot be copied, WL3001 naming it. A
/// message holds no handle of any heap and never changes, so that any thread may read it; copying out and in never
/// recurses, however deep the value.
/// </summary>
public sealed class WebLingoMessage
{
    private enum Kind : byte { Plain, String, Bytes, Decimal, Array, Map, Set, Error }

    /// <summary>A value of the message: a plain one as it is, a string, bytes or a decimal its content, a collection or an error its parts' nodes.</summary>
    private readonly record struct Node(Kind Kind, WebLingoValue Plain, object? Payload);

    private sealed record ErrorParts(string ClassName, int Message, int Code, string? ThrownAt);

    private readonly Node[] _nodes;

    private WebLingoMessage(Node[] nodes) => _nodes = nodes;

    /// <summary>How many values it holds, each element, key and value counted once, however many times it is referred to.</summary>
    public int Count => _nodes.Length;

    /// <summary>
    /// An error, as a message: an <c>Error</c> of the class named <paramref name="className"/>, its <c>message</c> and its
    /// <c>code</c>, thrown at <paramref name="thrownAt"/>, where that is given: what a worker's end is copied as where its
    /// exception was not, its limits' or one a message cannot hold (section 9, "M0.26 parts", part 2).
    /// </summary>
    public static WebLingoMessage OfError(string className, string message, string? code, string? thrownAt) =>
        new([
            new Node(Kind.Error, default, new ErrorParts(className, 1, 2, thrownAt)),
            new Node(Kind.String, default, message),
            code is null ? new Node(Kind.Plain, WebLingoValue.Null, null) : new Node(Kind.String, default, code)
        ]);

    /// <summary>
    /// The message, where it is an error, thrown at <paramref name="thrownAt"/>, in place of where it says it was: a worker's
    /// uncaught exception, which its host locates (section 9, "M0.26 parts", part 2); else the message itself.
    /// </summary>
    public WebLingoMessage ThrownAt(string thrownAt)
    {
        if (_nodes[0].Kind != Kind.Error) return this;
        var nodes = (Node[])_nodes.Clone();
        nodes[0] = nodes[0] with { Payload = (ErrorParts)nodes[0].Payload! with { ThrownAt = thrownAt } };
        return new WebLingoMessage(nodes);
    }

    /// <summary>Whether it holds an error, an <c>Error</c> once it is read (<see cref="TryRead"/>).</summary>
    public bool IsError => _nodes[0].Kind == Kind.Error;

    /// <summary>
    /// Where it is an error, what a report says of it: its class's name, its message, where a string, its code and where it
    /// was thrown, where they are known; null otherwise (section 9, "M0.26 parts", part 2).
    /// </summary>
    public (string ClassName, string Message, string? Code, string? ThrownAt)? ErrorReport()
    {
        if (_nodes[0] is not { Kind: Kind.Error, Payload: ErrorParts parts }) return null;
        var message = _nodes[parts.Message] is { Kind: Kind.String, Payload: string text } ? text : "";
        var code = _nodes[parts.Code] is { Kind: Kind.String, Payload: string given } ? given : null;
        return (parts.ClassName, message, code, parts.ThrownAt);
    }

    /// <summary>
    /// <paramref name="value"/>, of <paramref name="heap"/>, copied into a message; an error <paramref name="errorClass"/>'s
    /// instance or its subclasses', where it is the value itself, thrown at <paramref name="thrownAt"/>, where that is given;
    /// false, with WL3001, where the value holds anything a message cannot.
    /// </summary>
    public static bool TryCopy(WebLingoHeap heap, WebLingoValue value, WebLingoValue errorClass, string? thrownAt, out WebLingoMessage? message,
        out WebLingoRuntimeError? error)
    {
        (message, error) = (null, null);
        var nodes = new List<Node>();
        var seen = new Dictionary<WebLingoValue, int>();
        var pending = new Stack<(WebLingoValue Value, int Index)>();

        bool TryVisit(WebLingoValue current, out int index, out WebLingoRuntimeError? wrong)
        {
            (index, wrong) = (nodes.Count, null);
            switch (current.Kind)
            {
                case WebLingoValueKind.Null or WebLingoValueKind.Boolean or WebLingoValueKind.Int64 or WebLingoValueKind.Binary64
                    or WebLingoValueKind.DateTime:
                    nodes.Add(new Node(Kind.Plain, current, null));
                    return true;
                case WebLingoValueKind.String:
                    nodes.Add(new Node(Kind.String, default, heap.GetString(current)));
                    return true;
                case WebLingoValueKind.Bytes:
                    nodes.Add(new Node(Kind.Bytes, default, (byte[])heap.GetBytes(current).Clone()));
                    return true;
                case WebLingoValueKind.Decimal:
                    nodes.Add(new Node(Kind.Decimal, default, heap.GetDecimal(current)));
                    return true;
                case WebLingoValueKind.Array or WebLingoValueKind.Map or WebLingoValueKind.Set when !current.HasClass:
                case WebLingoValueKind.Instance when WebLingoExceptions.IsError(current, errorClass, heap):
                    if (seen.TryGetValue(current, out index)) return true;
                    index = nodes.Count;
                    nodes.Add(default); // Filled once its parts are copied.
                    seen.Add(current, index);
                    pending.Push((current, index));
                    return true;
                default:
                    wrong = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                        "A message holds null, Booleans, numbers, decimals, strings, bytes, dates, arrays, maps and sets of them, and errors, "
                        + $"not {WebLingoValueFormatter.Describe(current, heap)}.");
                    return false;
            }
        }

        if (!TryVisit(value, out _, out error)) return false;
        while (pending.Count > 0)
        {
            var (current, index) = pending.Pop();
            switch (current.Kind)
            {
                case WebLingoValueKind.Array:
                {
                    var elements = heap.GetArray(current);
                    var parts = new int[elements.Count];
                    for (var i = 0; i < parts.Length; i++)
                    {
                        if (!TryVisit(elements[i], out parts[i], out error)) return false;
                    }
                    nodes[index] = new Node(Kind.Array, default, parts);
                    break;
                }
                case WebLingoValueKind.Map:
                {
                    var parts = new (int Key, int Value)[heap.MapCount(current)];
                    for (var i = 0; i < parts.Length; i++)
                    {
                        var (key, entry) = heap.MapEntryAt(current, i);
                        if (!TryVisit(key, out parts[i].Key, out error) || !TryVisit(entry, out parts[i].Value, out error)) return false;
                    }
                    nodes[index] = new Node(Kind.Map, default, parts);
                    break;
                }
                case WebLingoValueKind.Set:
                {
                    var parts = new int[heap.SetCount(current)];
                    for (var i = 0; i < parts.Length; i++)
                    {
                        if (!TryVisit(heap.SetValueAt(current, i), out parts[i], out error)) return false;
                    }
                    nodes[index] = new Node(Kind.Set, default, parts);
                    break;
                }
                default:
                {
                    // An error: its class's name, an Error copied before keeping the one it came with; its message and its
                    // code; and where it was thrown, the one given for the value itself, else the one it came with.
                    var className = heap.ClassName(heap.ClassOf(current));
                    if (heap.ClassOf(current) == errorClass && heap.TryGetField(current, "className", out var given) && given.Kind == WebLingoValueKind.String)
                    {
                        className = heap.GetString(given);
                    }
                    var at = index == 0 ? thrownAt : null;
                    if (at is null && heap.TryGetField(current, "thrownAt", out var where) && where.Kind == WebLingoValueKind.String) at = heap.GetString(where);
                    if (!TryVisit(heap.TryGetField(current, "message", out var text) ? text : WebLingoValue.Null, out var messageIndex, out error)
                        || !TryVisit(heap.TryGetField(current, "code", out var code) ? code : WebLingoValue.Null, out var codeIndex, out error))
                    {
                        return false;
                    }
                    nodes[index] = new Node(Kind.Error, default, new ErrorParts(className, messageIndex, codeIndex, at));
                    break;
                }
            }
        }
        message = new WebLingoMessage(nodes.ToArray());
        return true;
    }

    /// <summary>
    /// The value it holds, made anew in <paramref name="heap"/>, an error an instance of <paramref name="errorClass"/>; false,
    /// with why, where the heap has no room for it.
    /// </summary>
    public bool TryRead(WebLingoHeap heap, WebLingoValue errorClass, out WebLingoValue value, out WebLingoRuntimeError? error)
    {
        (value, error) = (WebLingoValue.Null, null);
        var values = new WebLingoValue[_nodes.Length];
        heap.AddRoots(values); // What it has made, while what it makes next may collect.
        try
        {
            // Each value made first, its collections empty, so that a collection may hold one made after it, or itself.
            for (var i = 0; i < _nodes.Length; i++)
            {
                var node = _nodes[i];
                var made = node.Kind switch
                {
                    Kind.Plain => Plain(node.Plain, out values[i], out error),
                    Kind.String => heap.TryAllocateString((string)node.Payload!, out values[i], out error),
                    Kind.Bytes => heap.TryAllocateBytes((byte[])((byte[])node.Payload!).Clone(), out values[i], out error),
                    Kind.Decimal => heap.TryAllocateDecimal((decimal)node.Payload!, out values[i], out error),
                    Kind.Array => heap.TryAllocateArray([], 0, 0, out values[i], out error),
                    Kind.Map => heap.TryAllocateMap([], 0, 0, out values[i], out error),
                    Kind.Set => heap.TryAllocateSet([], 0, 0, out values[i], out error),
                    _ => errorClass.Kind == WebLingoValueKind.Class ? heap.TryAllocateInstance(errorClass, out values[i], out error) : NoErrorClass(out error)
                };
                if (!made) return false;
            }
            for (var i = 0; i < _nodes.Length; i++)
            {
                var node = _nodes[i];
                switch (node.Kind)
                {
                    case Kind.Array:
                        foreach (var part in (int[])node.Payload!)
                        {
                            if (!heap.TryAddToArray(values[i], values[part], out error)) return false;
                        }
                        break;
                    case Kind.Map:
                        foreach (var (key, entry) in ((int Key, int Value)[])node.Payload!)
                        {
                            if (!heap.TrySetInMap(values[i], values[key], values[entry], out error)) return false;
                        }
                        break;
                    case Kind.Set:
                        foreach (var part in (int[])node.Payload!)
                        {
                            if (!heap.TryAddToSet(values[i], values[part], out _, out error)) return false;
                        }
                        break;
                    case Kind.Error:
                    {
                        var parts = (ErrorParts)node.Payload!;
                        if (!heap.TrySetField(values[i], "message", values[parts.Message], out error)
                            || !heap.TrySetField(values[i], "code", values[parts.Code], out error)
                            || !heap.TryAllocateString(parts.ClassName, out var className, out error)
                            || !heap.TrySetField(values[i], "className", className, out error))
                        {
                            return false;
                        }
                        var thrownAt = WebLingoValue.Null;
                        if (parts.ThrownAt is { } at && !heap.TryAllocateString(at, out thrownAt, out error)) return false;
                        if (!heap.TrySetField(values[i], "thrownAt", thrownAt, out error)) return false;
                        break;
                    }
                }
            }
            value = values[0];
            return true;
        }
        finally
        {
            heap.RemoveLastRoots(values);
        }

        static bool Plain(WebLingoValue plain, out WebLingoValue made, out WebLingoRuntimeError? error)
        {
            (made, error) = (plain, null);
            return true;
        }

        static bool NoErrorClass(out WebLingoRuntimeError? error)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "A message's error cannot be made: its heap has no Error class.");
            return false;
        }
    }
}
