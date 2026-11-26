using WebLingo.Core;
using Xunit;
namespace WebLingo.Runtime.Tests;

/// <summary>
/// Values copied between heaps (specification, section 9, "M0.26 parts", part 1): copied out whole into a message, which
/// holds no handle of any heap, and made anew in another heap, cycles and shared references kept; an error arriving as an
/// <c>Error</c>, its class's name and where it was thrown its own fields; anything else refused, WL3001 naming it.
/// </summary>
public sealed class MessageTests
{
    private readonly WebLingoHeap _from = new();
    private readonly WebLingoHeap _to = new();
    private readonly WebLingoValue[] _roots = new WebLingoValue[8];
    private readonly WebLingoValue _fromError;
    private readonly WebLingoValue _toError;

    public MessageTests()
    {
        _from.AddRoots(_roots);
        Assert.True(_from.TryMakeClass("Error", WebLingoValue.Null, out _fromError, out _));
        Assert.True(_to.TryMakeClass("Error", WebLingoValue.Null, out _toError, out _));
        (_roots[0], _roots[1]) = (_fromError, WebLingoValue.Null);
        _to.AddRoots(new[] { _toError });
    }

    private WebLingoValue Copied(WebLingoValue value, string? thrownAt = null)
    {
        Assert.True(WebLingoMessage.TryCopy(_from, value, _fromError, thrownAt, out var message, out var error), error?.Message);
        Assert.True(message!.TryRead(_to, _toError, out var read, out error), error?.Message);
        return read;
    }

    private WebLingoValue Text(WebLingoHeap heap, string text)
    {
        Assert.True(heap.TryAllocateString(text, out var value, out _));
        return value;
    }

    [Fact]
    public void PlainValuesArriveAsThemselves()
    {
        foreach (var value in new[] { WebLingoValue.Null, WebLingoValue.True, WebLingoValue.FromInt64(long.MinValue), WebLingoValue.FromBinary64(1.5),
                                      WebLingoValue.FromDateTime(1_795_000_000_000, 60) })
        {
            Assert.Equal(value, Copied(value));
        }
        Assert.True(double.IsNaN(Copied(WebLingoValue.FromBinary64(double.NaN)).AsBinary64));
        Assert.Equal("café ☕", _to.GetString(Copied(Text(_from, "café ☕"))));
        Assert.True(_from.TryAllocateDecimal(1.50m, out var number, out _));
        Assert.Equal(1.50m, _to.GetDecimal(Copied(number)));
        Assert.True(_from.TryAllocateBytes([1, 2, 255], out var bytes, out _));
        Assert.Equal(new byte[] { 1, 2, 255 }, _to.GetBytes(Copied(bytes)));
    }

    [Fact]
    public void CollectionsArriveWhole_TheirCyclesAndSharedReferencesKept()
    {
        // [shared, shared, self, { "tea": #{1, "two"}, 3: null, true: [] }]
        Assert.True(_from.TryAllocateArray([WebLingoValue.FromInt64(7)], 0, 1, out _roots[1], out _));
        var shared = _roots[1];
        Assert.True(_from.TryAllocateArray([], 0, 0, out _roots[2], out _));
        var outer = _roots[2];
        Assert.True(_from.TryAllocateSet([WebLingoValue.FromInt64(1), Text(_from, "two")], 0, 2, out _roots[3], out _));
        Assert.True(_from.TryAllocateMap([], 0, 0, out _roots[4], out _));
        var map = _roots[4];
        Assert.True(_from.TrySetInMap(map, Text(_from, "tea"), _roots[3], out _));
        Assert.True(_from.TrySetInMap(map, WebLingoValue.FromInt64(3), WebLingoValue.Null, out _));
        Assert.True(_from.TryAllocateArray([], 0, 0, out _roots[5], out _));
        Assert.True(_from.TrySetInMap(map, WebLingoValue.True, _roots[5], out _));
        foreach (var element in new[] { shared, shared, outer, map }) Assert.True(_from.TryAddToArray(outer, element, out _));

        var read = Copied(outer);
        var elements = _to.GetArray(read);
        Assert.Equal(4, elements.Count);
        Assert.Equal(elements[0], elements[1]); // Shared, as it was.
        Assert.Equal(read, elements[2]); // Itself.
        Assert.Equal(WebLingoValue.FromInt64(7), _to.GetArray(elements[0])[0]);
        Assert.Equal(3, _to.MapCount(elements[3]));
        Assert.True(_to.TryFindInMap(elements[3], Text(_to, "tea"), out var set, out var found, out _) && found);
        Assert.Equal(2, _to.SetCount(set));
        Assert.True(_to.TryFindInSet(set, Text(_to, "two"), out found, out _) && found);
        Assert.True(_to.TryFindInMap(elements[3], WebLingoValue.FromInt64(3), out var none, out found, out _) && found && none.Kind == WebLingoValueKind.Null);
        Assert.True(_to.TryFindInMap(elements[3], WebLingoValue.True, out var empty, out found, out _) && found && _to.GetArray(empty).Count == 0);
        Assert.NotEqual(_to.GetArray(empty), _to.GetArray(elements[0]));
    }

    [Fact]
    public void AnErrorArrivesAsAnError_ItsClassesNameAndWhereItWasThrownItsOwn()
    {
        Assert.True(_from.TryMakeClass("NotFound", _fromError, out _roots[1], out _));
        Assert.True(_from.TryAllocateInstance(_roots[1], out _roots[2], out _));
        var thrown = _roots[2];
        Assert.True(_from.TrySetField(thrown, "message", Text(_from, "not there"), out _));
        Assert.True(_from.TrySetField(thrown, "code", WebLingoValue.Null, out _));
        Assert.True(_from.TrySetField(thrown, "extra", WebLingoValue.FromInt64(5), out _));

        var read = Copied(thrown, "work.wlg(12,5)");
        Assert.Equal(_toError, _to.ClassOf(read));
        Assert.Equal("NotFound", Field(read, "className"));
        Assert.Equal("not there", Field(read, "message"));
        Assert.Equal("work.wlg(12,5)", Field(read, "thrownAt"));
        Assert.True(_to.TryGetField(read, "code", out var code) && code.Kind == WebLingoValueKind.Null);
        Assert.False(_to.TryGetField(read, "extra", out _)); // Only what A1 (a) names.

        // Within an array, where it was thrown is its own field, none here; an error copied before keeps its class's name.
        Assert.True(_from.TryAllocateArray([thrown], 0, 1, out _roots[3], out _));
        var inside = _to.GetArray(Copied(_roots[3], "ignored(1,1)"))[0];
        Assert.True(_to.TryGetField(inside, "thrownAt", out var at) && at.Kind == WebLingoValueKind.Null);
        Assert.True(WebLingoMessage.TryCopy(_to, read, _toError, null, out var again, out _));
        Assert.True(again!.TryRead(_from, _fromError, out var back, out _));
        Assert.Equal(("NotFound", "work.wlg(12,5)"), (_from.GetString(Get(_from, back, "className")), _from.GetString(Get(_from, back, "thrownAt"))));
    }

    [Fact]
    public void AnythingElseIsRefused_NamedInWL3001()
    {
        Assert.True(_from.TryMakeClass("Point", WebLingoValue.Null, out _roots[1], out _));
        Assert.True(_from.TryAllocateInstance(_roots[1], out _roots[2], out _));
        Assert.True(_from.TryAllocateArray([WebLingoValue.FromInt64(1), _roots[2]], 0, 2, out _roots[3], out _));
        foreach (var (value, described) in new[] { (_roots[3], "an instance of Point"), (_roots[1], "the class Point") })
        {
            Assert.False(WebLingoMessage.TryCopy(_from, value, _fromError, null, out var message, out var error));
            Assert.Null(message);
            Assert.Equal(WebLingoDiagnosticCodes.InvalidOperation, error!.Code);
            Assert.Equal("A message holds null, Booleans, numbers, decimals, strings, bytes, dates, arrays, maps and sets of them, and errors, "
                         + $"not {described}.", error.Message);
        }
    }

    [Fact]
    public void AValueOfAnyDepthIsCopied_WithoutRecursion()
    {
        Assert.True(_from.TryAllocateArray([], 0, 0, out _roots[1], out _));
        var current = _roots[1];
        for (var i = 0; i < 100_000; i++)
        {
            Assert.True(_from.TryAllocateArray([], 0, 0, out var inner, out _));
            Assert.True(_from.TryAddToArray(current, inner, out _));
            current = inner;
        }
        var read = Copied(_roots[1]);
        var depth = 0;
        for (var at = read; _to.GetArray(at).Count == 1; at = _to.GetArray(at)[0]) depth++;
        Assert.Equal(100_000, depth);
    }

    [Fact]
    public void AHeapTooSmallForAMessageSaysSo()
    {
        Assert.True(WebLingoMessage.TryCopy(_from, Text(_from, new string('a', 100_000)), _fromError, null, out var message, out _));
        var small = new WebLingoHeap(maxUnits: 10_000);
        Assert.False(message!.TryRead(small, WebLingoValue.Null, out _, out var error));
        Assert.NotNull(error);
        Assert.Equal(1, message.Count);
    }

    private string Field(WebLingoValue instance, string name) => _to.GetString(Get(_to, instance, name));

    private static WebLingoValue Get(WebLingoHeap heap, WebLingoValue instance, string name)
    {
        Assert.True(heap.TryGetField(instance, name, out var value));
        return value;
    }
}
