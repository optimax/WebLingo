namespace WebLingo.Runtime;

/// <summary>
/// <c>sort(compare)</c>'s work (specification, section 3.6, "Array methods"; section 5.6, "Where the time goes", cause 6):
/// the merge sort the array methods wrote in WebLingo until 23 November 2026, the same, comparing the same elements in the
/// same order, so that it gives what that gave, stable, and fails where that failed. Bottom-up: runs of a width merged
/// from one half of <see cref="WebLingoCalls.Values"/> into the other, the width doubled, its elements copied first, as
/// they were when it began. Two elements are compared as <c>compare(x, y) &lt; 0</c>, a call its VM makes, or, without
/// a function, as <c>x &lt; y</c>, each as <c>&lt;</c> compares (section 3.5), a class's operator <c>&lt;</c>, where
/// <c>&lt;</c> finds one, called too, what it gives taken as a Boolean, as <c>||</c> takes one.
/// </summary>
public sealed class WebLingoSort : WebLingoCalls
{
    private enum Waits { Nothing, Comparison, Operator }

    private readonly int _n;
    private readonly WebLingoValue _compare;
    private long _width = 1;
    private int _from;
    private int _to;
    private int _middle;
    private int _high;
    private int _i;
    private int _j;
    private int _k;
    private Waits _waits;
    private WebLingoValue _returned;

    /// <summary>The work of sorting <paramref name="elements"/>, by <paramref name="compare"/>, or by <c>&lt;</c> where it is null.</summary>
    public WebLingoSort(List<WebLingoValue> elements, WebLingoValue compare) : base(2 * elements.Count + 3)
    {
        _n = elements.Count;
        elements.CopyTo(Values, 0);
        _compare = compare;
        _to = _n;
        FirstArgument = 2 * _n + 1;
        ArgumentCount = 2;
        Begin(0);
    }

    public override bool TryGoOn(WebLingoHeap heap, out bool calls, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        calls = false;
        result = WebLingoValue.Null;
        error = null;
        if (_waits != Waits.Nothing && !TryTake(heap, out calls, out error)) return false;
        var values = Values;
        while (_width < _n)
        {
            while (_k < _high)
            {
                bool right;
                if (_j >= _high)
                {
                    right = false;
                }
                else if (_i >= _middle)
                {
                    right = true;
                }
                else
                {
                    var x = values[_from + _j];
                    var y = values[_from + _i];
                    if (_compare.Kind != WebLingoValueKind.Null)
                    {
                        Call(_compare, WebLingoValue.Missing, x, y, Waits.Comparison);
                        calls = true;
                        return false;
                    }
                    if (x.Kind == WebLingoValueKind.Int64 && y.Kind == WebLingoValueKind.Int64) right = x.AsInt64 < y.AsInt64;
                    else if (!TryLess(x, y, heap, out right, out calls, out error)) return false;
                }
                Place(right);
            }
            if (_high < _n)
            {
                Begin(_high); // The next two runs.
                continue;
            }
            (_from, _to) = (_to, _from);
            _width *= 2;
            Begin(0);
        }
        return heap.TryAllocateArray(values, _from, _n, out result, out error);
    }

    public override void Supply(WebLingoValue returned) => _returned = returned;

    /// <summary>The two runs from <paramref name="low"/>, each of the width or what is left, to be merged.</summary>
    private void Begin(int low)
    {
        _middle = (int)Math.Min(low + _width, _n);
        _high = (int)Math.Min(low + 2 * _width, _n);
        _i = low;
        _j = _middle;
        _k = low;
    }

    /// <summary>The next element merged: the right run's, or the left's, which a tie keeps first.</summary>
    private void Place(bool right)
    {
        var values = Values;
        if (right)
        {
            values[_to + _k] = values[_from + _j];
            _j++;
        }
        else
        {
            values[_to + _k] = values[_from + _i];
            _i++;
        }
        _k++;
    }

    /// <summary>What the call it waited for gave, which decides the element it waited to merge.</summary>
    private bool TryTake(WebLingoHeap heap, out bool calls, out WebLingoRuntimeError? error)
    {
        calls = false;
        error = null;
        var given = _returned;
        _returned = WebLingoValue.Null;
        var waited = _waits;
        _waits = Waits.Nothing;
        bool right;
        if (waited == Waits.Comparison)
        {
            if (given.Kind == WebLingoValueKind.Int64) right = given.AsInt64 < 0;
            else if (!TryLess(given, WebLingoValue.FromInt64(0), heap, out right, out calls, out error)) return false;
        }
        else
        {
            // A class's operator's result, which || takes as a Boolean, or as one an instance holds.
            if (given.Kind != WebLingoValueKind.Boolean) given = heap.Held(given);
            if (given.Kind != WebLingoValueKind.Boolean)
            {
                error = WebLingoOperators.CheckBoolean(given, "'&&' and '||' need Booleans:");
                return false;
            }
            right = given.AsBoolean;
        }
        Place(right);
        return true;
    }

    /// <summary>
    /// <c>left &lt; right</c>, as a VM compares: two integers or two numbers at once; a class's operator, where an
    /// instance's or a classed value's class has one, as a call it then waits for; anything else as the runtime compares.
    /// </summary>
    private bool TryLess(WebLingoValue left, WebLingoValue right, WebLingoHeap heap, out bool less, out bool calls,
        out WebLingoRuntimeError? error)
    {
        less = false;
        calls = false;
        error = null;
        if (left.Kind == WebLingoValueKind.Int64 && right.Kind == WebLingoValueKind.Int64)
        {
            less = left.AsInt64 < right.AsInt64;
            return true;
        }
        if (left.Kind == WebLingoValueKind.Binary64 && right.Kind == WebLingoValueKind.Binary64)
        {
            less = left.AsBinary64 < right.AsBinary64;
            return true;
        }
        if ((left.Kind == WebLingoValueKind.Instance || right.Kind == WebLingoValueKind.Instance || left.HasClass || right.HasClass)
            && WebLingoMembers.TryFindOperator("operator<", left, right, heap, out var method))
        {
            Call(method, WebLingoValue.Null, left, right, Waits.Operator);
            calls = true;
            return false;
        }
        if (!WebLingoOperators.TryCompare(WebLingoComparisonOperator.Less, left, right, heap, out var value, out error)) return false;
        less = value.AsBoolean;
        return true;
    }

    private void Call(WebLingoValue callee, WebLingoValue self, WebLingoValue x, WebLingoValue y, Waits waits)
    {
        Callee = callee;
        Self = self;
        Values[2 * _n] = callee;
        Values[2 * _n + 1] = x;
        Values[2 * _n + 2] = y;
        _waits = waits;
    }
}
