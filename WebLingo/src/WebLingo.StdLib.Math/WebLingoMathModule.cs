using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Math;

/// <summary>
/// <c>std:math</c>, a standard module, a native module installed with the runtime in the <c>std</c> scheme's folder as
/// <c>math.dll</c>, imported, <c>import math from "std:math";</c> (the owner's answers, 18 November 2026, "Mathematics"):
/// <list type="bullet">
/// <item><c>abs(x)</c>, <c>sign(x)</c>, <c>min(a, …)</c> and <c>max(a, …)</c>, each keeping its argument's kind, an
/// integer's an integer, a number's a number, a decimal's a decimal; <c>min</c> and <c>max</c> comparing as <c>&lt;</c>
/// does, a number and a decimal not compared, and giving <c>NaN</c> where a number is <c>NaN</c>;</item>
/// <item><c>floor(x)</c>, <c>ceiling(x)</c>, <c>round(x)</c>, halfway away from zero, and <c>truncate(x)</c>, each giving an
/// integer, an integer itself, a number or a decimal beyond 64 bits an <c>OverflowError</c>, as infinity is, and
/// <c>NaN</c> WL3001;</item>
/// <item><c>sqrt</c>, <c>pow</c>, <c>exp</c>, <c>log</c>, <c>log10</c>, <c>log2</c>, <c>sin</c>, <c>cos</c>, <c>tan</c>,
/// <c>asin</c>, <c>acos</c>, <c>atan</c> and <c>atan2(y, x)</c>, each taking integers and numbers and giving a number, as
/// IEEE 754 has it, <c>sqrt(-1.0)</c> <c>NaN</c>;</item>
/// <item>the constants <c>PI</c> and <c>E</c>, numbers.</item>
/// </list>
/// </summary>
public sealed class WebLingoMathModule : IWebLingoNativeModule
{
    private const string Numeric = "an integer, a number or a decimal";
    private const string Real = "an integer or a number";

    private delegate double Function1(double x);

    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Function("abs", 1, Abs);
        registry.Function("sign", 1, Sign);
        registry.RestFunction("min", 2, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            Extreme("min", WebLingoComparisonOperator.Greater, a, heap, out result, out error));
        registry.RestFunction("max", 2, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            Extreme("max", WebLingoComparisonOperator.Less, a, heap, out result, out error));
        Integer(registry, "floor", System.Math.Floor, decimal.Floor);
        Integer(registry, "ceiling", System.Math.Ceiling, decimal.Ceiling);
        Integer(registry, "round", x => System.Math.Round(x, MidpointRounding.AwayFromZero),
            x => decimal.Round(x, MidpointRounding.AwayFromZero));
        Integer(registry, "truncate", System.Math.Truncate, decimal.Truncate);
        Number(registry, "sqrt", System.Math.Sqrt);
        Number(registry, "exp", System.Math.Exp);
        Number(registry, "log", System.Math.Log);
        Number(registry, "log10", System.Math.Log10);
        Number(registry, "log2", System.Math.Log2);
        Number(registry, "sin", System.Math.Sin);
        Number(registry, "cos", System.Math.Cos);
        Number(registry, "tan", System.Math.Tan);
        Number(registry, "asin", System.Math.Asin);
        Number(registry, "acos", System.Math.Acos);
        Number(registry, "atan", System.Math.Atan);
        registry.Function("pow", 2, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            Number2("pow", "the base", "the exponent", System.Math.Pow, a, heap, out result, out error));
        registry.Function("atan2", 2, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            Number2("atan2", "y", "x", System.Math.Atan2, a, heap, out result, out error));
        registry.Constant("PI", WebLingoValue.FromBinary64(System.Math.PI));
        registry.Constant("E", WebLingoValue.FromBinary64(System.Math.E));
    }

    private static bool Abs(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = null;
        switch (a[0].Kind)
        {
            case WebLingoValueKind.Int64:
                if (a[0].AsInt64 == long.MinValue)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IntegerOverflow,
                        $"abs({long.MinValue}) does not fit in a 64-bit integer.");
                    return false;
                }
                result = WebLingoValue.FromInt64(System.Math.Abs(a[0].AsInt64));
                return true;
            case WebLingoValueKind.Binary64:
                result = WebLingoValue.FromBinary64(System.Math.Abs(a[0].AsBinary64));
                return true;
            case WebLingoValueKind.Decimal:
                return heap.TryAllocateDecimal(decimal.Abs(heap.GetDecimal(a[0])), out result, out error);
            default:
                error = new WebLingoArgument("abs", 0, 1, "the value", Numeric).Wrong(a[0], heap);
                return false;
        }
    }

    private static bool Sign(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = null;
        switch (a[0].Kind)
        {
            case WebLingoValueKind.Int64:
                result = WebLingoValue.FromInt64(System.Math.Sign(a[0].AsInt64));
                return true;
            case WebLingoValueKind.Binary64:
                var x = a[0].AsBinary64;
                result = WebLingoValue.FromBinary64(double.IsNaN(x) ? double.NaN : System.Math.Sign(x));
                return true;
            case WebLingoValueKind.Decimal:
                return heap.TryAllocateDecimal(System.Math.Sign(heap.GetDecimal(a[0])), out result, out error);
            default:
                error = new WebLingoArgument("sign", 0, 1, "the value", Numeric).Wrong(a[0], heap);
                return false;
        }
    }

    /// <summary>
    /// <c>min(first, ...others)</c> or <c>max</c>: the first, then each other that the best so far is greater than, or less
    /// than, as <c>&gt;</c> and <c>&lt;</c> compare them, given as it was, its kind kept; <c>NaN</c> where any number is <c>NaN</c>.
    /// </summary>
    private static bool Extreme(string name, WebLingoComparisonOperator worse, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var others = heap.GetArray(a[1]);
        var values = new WebLingoValue[others.Count + 1];
        values[0] = a[0];
        for (var i = 0; i < others.Count; i++) values[i + 1] = heap.Held(others[i]);
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (value.Kind is not (WebLingoValueKind.Int64 or WebLingoValueKind.Binary64 or WebLingoValueKind.Decimal))
            {
                error = new WebLingoArgument(name, i, values.Length, "a value", Numeric).Wrong(value, heap);
                return false;
            }
        }
        error = null;
        var best = values[0];
        foreach (var value in values)
        {
            if (value.Kind == WebLingoValueKind.Binary64 && double.IsNaN(value.AsBinary64))
            {
                result = value;
                return true;
            }
            if (!WebLingoOperators.TryCompare(worse, best, value, heap, out var beaten, out error)) return false;
            if (beaten.AsBoolean) best = value;
        }
        result = best;
        return true;
    }

    /// <summary>A function giving an integer: an integer itself; a number or a decimal made one as the function says.</summary>
    private static void Integer(IWebLingoNativeRegistry registry, string name, Function1 ofNumber, Func<decimal, decimal> ofDecimal) =>
        registry.Function(name, 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            error = null;
            switch (a[0].Kind)
            {
                case WebLingoValueKind.Int64:
                    result = a[0];
                    return true;
                case WebLingoValueKind.Binary64:
                    var x = a[0].AsBinary64;
                    if (double.IsNaN(x))
                    {
                        error = new WebLingoArgument(name, 0, 1, "the value", "an integer, a decimal or a number that is not NaN")
                            .Wrong("NaN");
                        return false;
                    }
                    var made = ofNumber(x);
                    // 2^63 is the first binary64 beyond int64; -2^63 is its least.
                    if (made is < -9223372036854775808.0 or >= 9223372036854775808.0)
                    {
                        error = Beyond(name, WebLingoValueFormatter.Format(a[0]));
                        return false;
                    }
                    result = WebLingoValue.FromInt64((long)made);
                    return true;
                case WebLingoValueKind.Decimal:
                    var d = ofDecimal(heap.GetDecimal(a[0]));
                    if (d is < long.MinValue or > long.MaxValue)
                    {
                        error = Beyond(name, WebLingoValueFormatter.Format(a[0], heap));
                        return false;
                    }
                    result = WebLingoValue.FromInt64((long)d);
                    return true;
                default:
                    error = new WebLingoArgument(name, 0, 1, "the value", Numeric).Wrong(a[0], heap);
                    return false;
            }
        });

    private static WebLingoRuntimeError Beyond(string name, string given) =>
        new(WebLingoDiagnosticCodes.IntegerOverflow, $"{name}({given}) does not fit in a 64-bit integer.");

    /// <summary>A function of one integer or number, giving a number.</summary>
    private static void Number(IWebLingoNativeRegistry registry, string name, Function1 function) =>
        registry.Function(name, 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!TryReal(new WebLingoArgument(name, 0, 1, "the value", Real), a[0], heap, out var x, out error)) return false;
            result = WebLingoValue.FromBinary64(function(x));
            return true;
        });

    private static bool Number2(string name, string first, string second, Func<double, double, double> function, ReadOnlySpan<WebLingoValue> a,
        WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryReal(new WebLingoArgument(name, 0, 2, first, Real), a[0], heap, out var x, out error)
            || !TryReal(new WebLingoArgument(name, 1, 2, second, Real), a[1], heap, out var y, out error))
        {
            return false;
        }
        result = WebLingoValue.FromBinary64(function(x, y));
        return true;
    }

    /// <summary>An integer or a number, as a number.</summary>
    private static bool TryReal(WebLingoArgument argument, WebLingoValue value, WebLingoHeap heap, out double x, out WebLingoRuntimeError? error)
    {
        (x, error) = (0, null);
        switch (value.Kind)
        {
            case WebLingoValueKind.Int64:
                x = value.AsInt64;
                return true;
            case WebLingoValueKind.Binary64:
                x = value.AsBinary64;
                return true;
            default:
                error = argument.Wrong(value, heap);
                return false;
        }
    }
}
