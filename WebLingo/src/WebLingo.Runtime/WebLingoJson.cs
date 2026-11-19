using System.Globalization;
using System.Text;
using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// A text a VM builds while it calls the program's methods on the way, as an instance's <c>toString()</c> or
/// <c>toJson()</c> asks: <c>write</c>'s (<see cref="WebLingoTextBuilder"/>) and JSON's (<see cref="WebLingoJsonBuilder"/>).
/// </summary>
public interface IWebLingoTextBuilder
{
    /// <summary>The values it holds, which its VM keeps as roots while a method it waits for runs.</summary>
    List<WebLingoValue> Roots { get; }

    /// <summary>The text, once <see cref="TryBuild"/> is done.</summary>
    string Text { get; }

    /// <summary>Why it cannot go on, once <see cref="TryBuild"/> has given false for it; null otherwise.</summary>
    WebLingoRuntimeError? Error { get; }

    /// <summary>
    /// Builds on: true when the text is done; false when <paramref name="method"/> of <paramref name="instance"/> must
    /// be called first, whose result <see cref="Supply"/> takes, or when <see cref="Error"/> says why it stopped.
    /// </summary>
    bool TryBuild(out WebLingoValue instance, out WebLingoValue method);

    /// <summary>What the method <see cref="TryBuild"/> waited for gave; the error, if it gave what the text cannot take.</summary>
    WebLingoRuntimeError? Supply(WebLingoValue returned);
}

/// <summary>
/// <c>Json.stringify(value, indent)</c>'s text (specification, section 3.6, "JSON"; the owner's answers, 16 November 2026):
/// a map as an object, an array or a set as an array, a string, an integer, a number, a decimal, <c>true</c>,
/// <c>false</c> and <c>null</c> as themselves, a date as its ISO 8601 text, and an instance as its fields, as
/// <c>write</c> writes them, unless its class has <c>toJson()</c>, whose result is written in its place, which its VM
/// calls, as it calls a <c>toString()</c> for <c>write</c>. Anything else, a value that contains itself, a key that is
/// neither a string nor an integer, and a number that is not finite are WL3001. With an indent, from 1 to 10 spaces,
/// each element and entry is on a line of its own.
/// </summary>
public sealed class WebLingoJsonBuilder : IWebLingoTextBuilder
{
    private readonly WebLingoHeap _heap;
    private readonly StringBuilder _text = new();
    private readonly HashSet<int> _open = [];
    private readonly Stack<Container> _pending = new();
    private readonly int _indent;
    private WebLingoValue _next;
    private bool _hasNext;
    private int _depth;

    public WebLingoJsonBuilder(WebLingoHeap heap, WebLingoValue value, WebLingoValue indent)
    {
        _heap = heap;
        (_next, _hasNext) = (value, true);
        Roots.Add(value);
        if (indent.Kind == WebLingoValueKind.Int64 && indent.AsInt64 is >= 0 and <= 10)
        {
            _indent = (int)indent.AsInt64;
            return;
        }
        Error = new WebLingoArgument("stringify", 1, 2, "the indent", "a number of spaces, an integer from 0 to 10").Wrong(indent, heap);
    }

    public List<WebLingoValue> Roots { get; } = [];

    public string Text => _text.ToString();

    public WebLingoRuntimeError? Error { get; private set; }

    public bool TryBuild(out WebLingoValue instance, out WebLingoValue method)
    {
        instance = WebLingoValue.Null;
        method = WebLingoValue.Null;
        if (Error is not null) return false;
        while (true)
        {
            if (_hasNext)
            {
                _hasNext = false;
                if (!TryStart(_next, out instance, out method)) return false;
                continue;
            }
            if (_pending.Count == 0) return true;
            var top = _pending.Peek();
            if (top.Next >= top.Count)
            {
                Close(top);
                continue;
            }
            if (top.Kind == Kind.Converted)
            {
                // A toJson()'s result, written in its instance's place.
                top.Next++;
                (_next, _hasNext) = (top.Value, true);
                continue;
            }
            if (top.Next > 0) _text.Append(',');
            NewLine(_depth);
            WebLingoValue item;
            switch (top.Kind)
            {
                case Kind.Map:
                {
                    var (key, value) = _heap.MapEntryAt(top.Value, top.Next);
                    if (key.Kind is not (WebLingoValueKind.String or WebLingoValueKind.Int64))
                    {
                        return Fail($"An object's keys in JSON are strings, written from strings or integers, not {Describe(key)}.");
                    }
                    Key(key.Kind == WebLingoValueKind.String ? _heap.GetString(key) : key.AsInt64.ToString(CultureInfo.InvariantCulture));
                    item = value;
                    break;
                }
                case Kind.Fields:
                {
                    var (name, value) = _heap.FieldAt(top.Value, top.Next);
                    Key(name.StartsWith("field:", StringComparison.Ordinal) ? name[6..] : name);
                    item = value;
                    break;
                }
                case Kind.Set:
                    item = _heap.SetValueAt(top.Value, top.Next);
                    break;
                default:
                    item = _heap.GetArray(top.Value)[top.Next];
                    break;
            }
            top.Next++;
            (_next, _hasNext) = (item, true);
        }
    }

    public WebLingoRuntimeError? Supply(WebLingoValue returned)
    {
        // toJson()'s result, written in its instance's place, its instance open meanwhile, so that a result that holds it is
        // WL3001, not a call without end.
        var instance = _pending.Peek();
        instance.Value = returned;
        instance.Count = 1;
        Roots.Add(returned);
        return null;
    }

    /// <summary>Writes a value, or opens it; false for a call of its <c>toJson()</c> its VM makes first, or with <see cref="Error"/>.</summary>
    private bool TryStart(WebLingoValue value, out WebLingoValue instance, out WebLingoValue method)
    {
        instance = WebLingoValue.Null;
        method = WebLingoValue.Null;
        switch (value.Kind)
        {
            case WebLingoValueKind.Null:
                _text.Append("null");
                return true;
            case WebLingoValueKind.Boolean:
                _text.Append(value.AsBoolean ? "true" : "false");
                return true;
            case WebLingoValueKind.Int64:
                _text.Append(value.AsInt64.ToString(CultureInfo.InvariantCulture));
                return true;
            case WebLingoValueKind.Binary64:
                if (!double.IsFinite(value.AsBinary64)) return Fail($"{Capitalized(Describe(value))} cannot be written as JSON, whose numbers are finite.");
                _text.Append(WebLingoValueFormatter.Format(value)); // As write writes it, 5.0 a number, so that it reads back as one.
                return true;
            case WebLingoValueKind.Decimal:
            {
                var digits = WebLingoValueFormatter.FormatDecimal(_heap.GetDecimal(value));
                _text.Append(digits, 0, digits.Length - 1); // Without its m.
                return true;
            }
            case WebLingoValueKind.String:
                Quote(_heap.GetString(value));
                return true;
            case WebLingoValueKind.DateTime:
                Quote(WebLingoMembers.IsoText(value));
                return true;
            case WebLingoValueKind.Array or WebLingoValueKind.Map or WebLingoValueKind.Set or WebLingoValueKind.Instance:
            {
                if (_open.Contains(value.Handle)) return Fail($"{Capitalized(Describe(value))} cannot be written as JSON: it contains itself.");
                var classValue = _heap.ClassOfValue(value); // An instance's, or a collection's of a class extending one.
                if (classValue.Kind == WebLingoValueKind.Class && _heap.TryGetMethod(classValue, "toJson", out method)
                    && method.Kind == WebLingoValueKind.Function)
                {
                    // Its class's toJson(), which its VM calls, as a call of its own, the instance open until its result is written.
                    _open.Add(value.Handle);
                    _pending.Push(new Container(value, Kind.Converted, 0));
                    Roots.Add(value);
                    instance = value;
                    return false;
                }
                method = WebLingoValue.Null;
                if (_heap.IsHolding(value))
                {
                    (_next, _hasNext) = (_heap.Held(value), true); // A class of values' instance, as the value it holds.
                    return true;
                }
                var (kind, count, open, close) = value.Kind switch
                {
                    WebLingoValueKind.Map => (Kind.Map, _heap.MapCount(value), '{', '}'),
                    WebLingoValueKind.Set => (Kind.Set, _heap.SetCount(value), '[', ']'),
                    WebLingoValueKind.Instance => (Kind.Fields, _heap.FieldCount(value), '{', '}'),
                    _ => (Kind.Array, _heap.GetArray(value).Count, '[', ']')
                };
                _text.Append(open);
                _open.Add(value.Handle);
                _pending.Push(new Container(value, kind, count) { Closing = close });
                Roots.Add(value);
                _depth++;
                return true;
            }
            default:
                return Fail($"{Capitalized(Describe(value))} cannot be written as JSON.");
        }
    }

    private void Close(Container container)
    {
        _pending.Pop();
        _open.Remove(container.Opened.Handle);
        Roots.RemoveAt(Roots.Count - 1);
        if (container.Kind == Kind.Converted)
        {
            if (container.Count == 1) Roots.RemoveAt(Roots.Count - 1); // Its toJson()'s result, written.
            return;
        }
        _depth--;
        if (container.Count > 0) NewLine(_depth);
        _text.Append(container.Closing);
    }

    private void Key(string key)
    {
        Quote(key);
        _text.Append(_indent > 0 ? ": " : ":");
    }

    private void NewLine(int depth)
    {
        if (_indent == 0) return;
        _text.Append('\n').Append(' ', _indent * depth);
    }

    /// <summary>A string as JSON writes it: quoted, its quotes, backslashes and control characters escaped.</summary>
    private void Quote(string text)
    {
        _text.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    _text.Append("\\\"");
                    break;
                case '\\':
                    _text.Append("\\\\");
                    break;
                case '\n':
                    _text.Append("\\n");
                    break;
                case '\r':
                    _text.Append("\\r");
                    break;
                case '\t':
                    _text.Append("\\t");
                    break;
                case '\b':
                    _text.Append("\\b");
                    break;
                case '\f':
                    _text.Append("\\f");
                    break;
                default:
                    if (c < ' ') _text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else _text.Append(c);
                    break;
            }
        }
        _text.Append('"');
    }

    private bool Fail(string message)
    {
        Error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, message);
        return false;
    }

    private string Describe(WebLingoValue value) => WebLingoValueFormatter.Describe(value, _heap);

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private enum Kind { Array, Set, Map, Fields, Converted }

    /// <summary>
    /// A value being written: a collection or an instance's fields, its next element or entry; or an instance whose
    /// <c>toJson()</c> runs, then its result, written in its place.
    /// </summary>
    private sealed class Container(WebLingoValue value, Kind kind, int count)
    {
        public WebLingoValue Opened { get; } = value;
        public WebLingoValue Value { get; set; } = value;
        public Kind Kind { get; } = kind;
        public int Count { get; set; } = count;
        public int Next { get; set; }
        public char Closing { get; init; }
    }
}

/// <summary>
/// <c>Json.parse(text)</c> (specification, section 3.6, "JSON"; the owner's answers, 16 November 2026): RFC 8259's JSON,
/// an object as a map, its keys strings, a later key's value replacing an earlier one's; an array as an array; a string; a
/// number as an integer when it has no fraction or exponent and fits int64, else as a binary64; and <c>true</c>,
/// <c>false</c> and <c>null</c>. Text that is not JSON is WL3001, naming where, by line and column, as diagnostics count
/// them; so is JSON nested more than <see cref="MaxDepth"/> deep.
/// </summary>
public static class WebLingoJsonParser
{
    public const int MaxDepth = 512;

    public static bool TryParse(string text, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var made = new List<WebLingoValue>(); // Every value made, kept where the collector sees it until the whole is.
        heap.AddRoots(made);
        try
        {
            var reader = new Reader(text, heap, made);
            reader.SkipWhitespace();
            if (!reader.TryValue(0, out result) || !reader.AtEnd(out _))
            {
                error = reader.Error ?? reader.Failed("expected the text's end");
                return false;
            }
            error = null;
            return true;
        }
        finally
        {
            heap.RemoveLastRoots(made);
        }
    }

    private sealed class Reader(string text, WebLingoHeap heap, List<WebLingoValue> made)
    {
        private int _at;

        public WebLingoRuntimeError? Error { get; private set; }

        public bool AtEnd(out WebLingoRuntimeError? error)
        {
            SkipWhitespace();
            error = _at == text.Length ? null : Failed("expected the text's end");
            return error is null;
        }

        public void SkipWhitespace()
        {
            while (_at < text.Length && text[_at] is ' ' or '\t' or '\n' or '\r') _at++;
        }

        public bool TryValue(int depth, out WebLingoValue value)
        {
            value = WebLingoValue.Null;
            if (depth > MaxDepth) return Fail($"nested more than {MaxDepth} deep");
            if (_at >= text.Length) return Fail("expected a value");
            switch (text[_at])
            {
                case '{':
                    return TryObject(depth, out value);
                case '[':
                    return TryArray(depth, out value);
                case '"':
                    return TryString(out value);
                case 't':
                    return TryWord("true", WebLingoValue.FromBoolean(true), out value);
                case 'f':
                    return TryWord("false", WebLingoValue.FromBoolean(false), out value);
                case 'n':
                    return TryWord("null", WebLingoValue.Null, out value);
                default:
                    return text[_at] is '-' or (>= '0' and <= '9') ? TryNumber(out value) : Fail("expected a value");
            }
        }

        private bool TryObject(int depth, out WebLingoValue value)
        {
            value = WebLingoValue.Null;
            _at++;
            if (!Made(heap.TryAllocateMap([], 0, 0, out value, out var error), value, error)) return false;
            SkipWhitespace();
            if (Take('}')) return true;
            while (true)
            {
                SkipWhitespace();
                if (_at >= text.Length || text[_at] != '"') return Fail("expected a key, a string");
                if (!TryString(out var key)) return false;
                SkipWhitespace();
                if (!Take(':')) return Fail("expected ':'");
                SkipWhitespace();
                if (!TryValue(depth + 1, out var entry)) return false;
                if (!heap.TrySetInMap(value, key, entry, out error)) return Stop(error);
                SkipWhitespace();
                if (Take('}')) return true;
                if (!Take(',')) return Fail("expected ',' or '}'");
            }
        }

        private bool TryArray(int depth, out WebLingoValue value)
        {
            value = WebLingoValue.Null;
            _at++;
            var elements = new List<WebLingoValue>();
            SkipWhitespace();
            if (!Take(']'))
            {
                while (true)
                {
                    SkipWhitespace();
                    if (!TryValue(depth + 1, out var element)) return false;
                    elements.Add(element);
                    SkipWhitespace();
                    if (Take(']')) break;
                    if (!Take(',')) return Fail("expected ',' or ']'");
                }
            }
            return Made(heap.TryAllocateArray([.. elements], 0, elements.Count, out value, out var error), value, error);
        }

        private bool TryString(out WebLingoValue value)
        {
            value = WebLingoValue.Null;
            _at++;
            var built = new StringBuilder();
            while (true)
            {
                if (_at >= text.Length) return Fail("expected a string's closing '\"'");
                var c = text[_at++];
                if (c == '"') break;
                if (c < ' ') return Fail("expected a control character in a string escaped");
                if (c != '\\')
                {
                    built.Append(c);
                    continue;
                }
                if (_at >= text.Length) return Fail("expected an escape");
                var escape = text[_at++];
                switch (escape)
                {
                    case '"' or '\\' or '/':
                        built.Append(escape);
                        break;
                    case 'b':
                        built.Append('\b');
                        break;
                    case 'f':
                        built.Append('\f');
                        break;
                    case 'n':
                        built.Append('\n');
                        break;
                    case 'r':
                        built.Append('\r');
                        break;
                    case 't':
                        built.Append('\t');
                        break;
                    case 'u':
                    {
                        if (!TryHex(out var unit)) return false;
                        if (char.IsHighSurrogate(unit) && _at + 1 < text.Length && text[_at] == '\\' && text[_at + 1] == 'u')
                        {
                            _at += 2;
                            if (!TryHex(out var low)) return false;
                            if (!char.IsLowSurrogate(low)) return Fail("expected a surrogate pair's second half");
                            built.Append(unit).Append(low);
                            break;
                        }
                        if (char.IsSurrogate(unit)) return Fail("expected a Unicode scalar value, not half a surrogate pair");
                        built.Append(unit);
                        break;
                    }
                    default:
                        _at--;
                        return Fail("expected an escape: \\\", \\\\, \\/, \\b, \\f, \\n, \\r, \\t or \\u and four hexadecimal digits");
                }
            }
            return Made(heap.TryAllocateString(built.ToString(), out value, out var error), value, error);
        }

        private bool TryHex(out char unit)
        {
            unit = '\0';
            if (_at + 4 > text.Length || !int.TryParse(text.AsSpan(_at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
            {
                return Fail("expected four hexadecimal digits");
            }
            _at += 4;
            unit = (char)code;
            return true;
        }

        private bool TryNumber(out WebLingoValue value)
        {
            value = WebLingoValue.Null;
            var start = _at;
            if (text[_at] == '-') _at++;
            if (_at < text.Length && text[_at] == '0') _at++;
            else if (!Digits()) return Fail("expected a digit");
            var whole = true;
            if (_at < text.Length && text[_at] == '.')
            {
                _at++;
                whole = false;
                if (!Digits()) return Fail("expected a digit after '.'");
            }
            if (_at < text.Length && text[_at] is 'e' or 'E')
            {
                _at++;
                whole = false;
                if (_at < text.Length && text[_at] is '+' or '-') _at++;
                if (!Digits()) return Fail("expected an exponent's digits");
            }
            var written = text.AsSpan(start, _at - start);
            if (whole && long.TryParse(written, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            {
                value = WebLingoValue.FromInt64(integer);
                return true;
            }
            value = WebLingoValue.FromBinary64(double.Parse(written, NumberStyles.Float, CultureInfo.InvariantCulture));
            return true;
        }

        private bool Digits()
        {
            var start = _at;
            while (_at < text.Length && text[_at] is >= '0' and <= '9') _at++;
            return _at > start;
        }

        private bool TryWord(string word, WebLingoValue meaning, out WebLingoValue value)
        {
            value = meaning;
            if (string.CompareOrdinal(text, _at, word, 0, word.Length) != 0) return Fail("expected a value");
            _at += word.Length;
            return true;
        }

        private bool Take(char c)
        {
            if (_at >= text.Length || text[_at] != c) return false;
            _at++;
            return true;
        }

        private bool Made(bool succeeded, WebLingoValue value, WebLingoRuntimeError? error)
        {
            if (!succeeded) return Stop(error);
            made.Add(value);
            return true;
        }

        private bool Stop(WebLingoRuntimeError? error)
        {
            Error = error;
            return false;
        }

        private bool Fail(string expected)
        {
            Error = Failed(expected);
            return false;
        }

        /// <summary>WL3001 at where the reading stopped, by line and column, counted as diagnostics count them.</summary>
        public WebLingoRuntimeError Failed(string expected)
        {
            var source = new WebLingoSourceText(text, "json");
            var location = source.GetLocation(Math.Min(_at, text.Length));
            return new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"The text is not JSON: at line {location.Line}, column {location.Column}, {expected}.");
        }
    }
}
