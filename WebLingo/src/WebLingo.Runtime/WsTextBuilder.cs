using System.Text;
namespace WebLingo.Runtime;

/// <summary>
/// The text of a value, as <c>write</c> writes it (specification, section 3.7), built with a stack of its own, as
/// <see cref="WsValueFormatter"/> builds it, but stopping where an instance whose class overrides <c>toString()</c> is
/// to be written, so that its VM can call that method, as a call of its own, and going on with the text it gives. A
/// VM that runs no such method needs none of this: the formatter gives the same text.
/// </summary>
public sealed class WsTextBuilder
{
    private readonly WsHeap _heap;
    private readonly WsValue _value;
    private readonly bool _asFields;
    private readonly StringBuilder _text = new();
    private readonly HashSet<int> _open = [];
    private readonly Stack<(WsValue Collection, int Next)> _pending = new();
    private bool _started;

    /// <summary>
    /// The text of <paramref name="value"/>; with <paramref name="asFields"/>, an instance is written as its class's
    /// name and its fields even if its class overrides <c>toString()</c>, as <c>Object</c>'s <c>toString()</c> writes
    /// the instance it runs on.
    /// </summary>
    public WsTextBuilder(WsHeap heap, WsValue value, bool asFields)
    {
        _heap = heap;
        _value = value;
        _asFields = asFields;
        Roots.Add(value);
    }

    /// <summary>
    /// The values it holds, the value first, then the collections it is writing; its VM keeps them as roots while a
    /// <c>toString()</c> it waits for runs, which may change, or let go of, what it was writing.
    /// </summary>
    public List<WsValue> Roots { get; } = [];

    /// <summary>The text, once <see cref="TryBuild"/> is done.</summary>
    public string Text => _text.ToString();

    /// <summary>
    /// Builds on: true when the text is done; false when the <paramref name="method"/>, <c>toString()</c>, of
    /// <paramref name="instance"/> must be called first, whose text <see cref="Supply"/> takes.
    /// </summary>
    public bool TryBuild(out WsValue instance, out WsValue method)
    {
        instance = WsValue.Null;
        method = WsValue.Null;
        if (!_started)
        {
            _started = true;
            switch (_value.Kind)
            {
                case WsValueKind.Array or WsValueKind.Map or WsValueKind.Set:
                    Open(_value);
                    break;
                case WsValueKind.Instance:
                    if (!_asFields && Overrides(_value, out method))
                    {
                        instance = _value;
                        return false;
                    }
                    Open(_value);
                    break;
                default:
                    _text.Append(WsValueFormatter.Format(_value, _heap));
                    return true;
            }
        }
        while (_pending.Count > 0)
        {
            var (collection, next) = _pending.Pop();
            var count = collection.Kind switch
            {
                WsValueKind.Map => _heap.MapCount(collection),
                WsValueKind.Set => _heap.SetCount(collection),
                WsValueKind.Instance => _heap.FieldCount(collection),
                _ => _heap.GetArray(collection).Count
            };
            if (next >= count)
            {
                _text.Append(collection.Kind == WsValueKind.Array ? ']' : '}');
                _open.Remove(collection.Handle);
                Roots.RemoveAt(Roots.Count - 1);
                continue;
            }
            if (next > 0) _text.Append(", ");
            _pending.Push((collection, next + 1));
            WsValue item;
            if (collection.Kind == WsValueKind.Map)
            {
                var (key, value) = _heap.MapEntryAt(collection, next);
                _text.Append(Scalar(key)).Append(": ");
                item = value;
            }
            else if (collection.Kind == WsValueKind.Instance)
            {
                var (name, value) = _heap.FieldAt(collection, next);
                _text.Append(name).Append(": ");
                item = value;
            }
            else
            {
                item = collection.Kind == WsValueKind.Set ? _heap.SetValueAt(collection, next) : _heap.GetArray(collection)[next];
            }
            if (item.Kind is not (WsValueKind.Array or WsValueKind.Map or WsValueKind.Set or WsValueKind.Instance))
            {
                _text.Append(Scalar(item));
                continue;
            }
            if (_open.Contains(item.Handle))
            {
                _text.Append(item.Kind switch
                {
                    WsValueKind.Map => "{…}",
                    WsValueKind.Set => "#{…}",
                    WsValueKind.Instance => _heap.ClassName(_heap.ClassOf(item)) + " {…}",
                    _ => "[…]"
                });
                continue;
            }
            if (item.Kind == WsValueKind.Instance && Overrides(item, out method))
            {
                instance = item;
                return false;
            }
            Open(item);
        }
        return true;
    }

    /// <summary>The text the <c>toString()</c> that <see cref="TryBuild"/> waited for gave, written as it is.</summary>
    public void Supply(string text) => _text.Append(text);

    /// <summary>Whether an instance's class overrides <c>Object</c>'s <c>toString()</c>, and with which method.</summary>
    private bool Overrides(WsValue instance, out WsValue method) =>
        _heap.TryGetMethod(_heap.ClassOf(instance), "toString", out method) && method.Kind == WsValueKind.Function
        && method != _heap.DefaultToString;

    private void Open(WsValue collection)
    {
        _text.Append(collection.Kind switch
        {
            WsValueKind.Map => "{",
            WsValueKind.Set => "#{",
            WsValueKind.Instance => _heap.ClassName(_heap.ClassOf(collection)) + " {",
            _ => "["
        });
        _open.Add(collection.Handle);
        _pending.Push((collection, 0));
        Roots.Add(collection);
    }

    private string Scalar(WsValue value) =>
        value.Kind == WsValueKind.String ? WsValueFormatter.Quote(_heap.GetString(value)) : WsValueFormatter.Format(value, _heap);
}
