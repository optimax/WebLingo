using System.Text.RegularExpressions;
using WebLingo.Core;
using WebLingo.Runtime;
using Kind = WebLingo.Runtime.WebLingoValueKind;
namespace WebLingo.StdLib.Test;

/// <summary>Value comparisons and error reports for the standard testing module.</summary>
public sealed partial class WebLingoTestModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        RegisterAssertion("assert", 1, Check);
        RegisterAssertion("requireEqual", 3, Equal);
        RegisterAssertion("requireNotEqual", 3, NotEqual);
        RegisterAssertion("assertDeepEqual", 2, DeepEqual);
        RegisterAssertion("assertContains", 2, Contains);
        RegisterAssertion("assertHasKey", 2, HasKey);
        RegisterAssertion("assertMatches", 2, Matches);
        RegisterAssertion("assertClose", 3, Close);
        registry.Function("checkAction", 2, CheckAction);
        registry.Function("matchError", 2, MatchError);
        registry.Function("describeError", 1, DescribeError);
        registry.Function("isAssertion", 1, IsAssertion);
        registry.Function("fail", 1, Fail);
        registry.Function("failAll", 3, FailAll);

        void RegisterAssertion(string name, int required, Assertion assertion) =>
            registry.Function(name, required + 1, required,
                (ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
                {
                    result = WebLingoValue.Null;
                    var message = args[required];
                    if (message.Kind is not (Kind.Null or Kind.String))
                    {
                        error = AssertionFailure($"{name} expects its message to be a String or null.");
                        return false;
                    }
                    var failure = assertion(args, heap);
                    if (failure is not null && message.Kind == Kind.String) failure += " " + heap.GetString(message);
                    error = failure is null ? null : AssertionFailure(failure);
                    return error is null;
                });
    }

    private delegate string? Assertion(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap);

    private static WebLingoRuntimeError AssertionFailure(string message) => new(WebLingoDiagnosticCodes.TestAssertionFailed, message);

    private static string Display(WebLingoValue value, WebLingoHeap heap) =>
        value.Kind == Kind.String ? WebLingoValueFormatter.Quote(heap.GetString(value)) : WebLingoValueFormatter.Format(value, heap);

    private static string Expected(WebLingoValue actual, WebLingoValue expected, WebLingoHeap heap, string detail = "") =>
        $"Expected {Display(expected, heap)}{detail}, actual {Display(actual, heap)}.";

    private static bool Same(WebLingoValue actual, WebLingoValue expected, WebLingoHeap heap) =>
        WebLingoOperators.TryCompare(WebLingoComparisonOperator.Equal, actual, expected, heap, out var result, out _) && result.AsBoolean;

    private static string? Check(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap) =>
        heap.Held(args[0]) == WebLingoValue.True ? null : Expected(args[0], WebLingoValue.True, heap, " (Boolean)");

    private static string? Equal(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap) =>
        args[2] == WebLingoValue.True ? null : Expected(args[0], args[1], heap);

    private static string? NotEqual(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap) =>
        args[2] == WebLingoValue.False ? null : $"Expected a value different from {Display(args[1], heap)}, actual {Display(args[0], heap)}.";

    private static string? Contains(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap)
    {
        var collection = heap.Held(args[0]);
        var item = args[1];
        var found = collection.Kind switch
        {
            Kind.String when heap.Held(item).Kind == Kind.String =>
                heap.GetString(collection).Contains(heap.GetString(heap.Held(item)), StringComparison.Ordinal),
            Kind.Array => heap.GetArray(collection).Exists(value => Same(value, item, heap)),
            Kind.Set => heap.TryFindInSet(collection, item, out var hasItem, out _) && hasItem,
            _ => false
        };
        return found ? null : $"Expected a String, Array or Set containing {Display(item, heap)}, actual {Display(args[0], heap)}.";
    }

    private static string? HasKey(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap)
    {
        var map = heap.Held(args[0]);
        return map.Kind == Kind.Map && heap.TryFindInMap(map, args[1], out _, out var found, out _) && found
            ? null : $"Expected a Map with key {Display(args[1], heap)}, actual {Display(args[0], heap)}.";
    }

    private static string? Matches(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap)
    {
        var text = heap.Held(args[0]);
        var pattern = heap.Held(args[1]);
        if (text.Kind == Kind.String && pattern.Kind == Kind.Regex)
        {
            try
            {
                if (heap.GetRegex(pattern).Regex.IsMatch(heap.GetString(text))) return null;
            }
            catch (RegexMatchTimeoutException)
            {
                return $"Matching {Display(args[0], heap)} against {Display(args[1], heap)} timed out.";
            }
        }
        return $"Expected text matching a Regex {Display(args[1], heap)}, actual {Display(args[0], heap)}.";
    }

    private static string? Close(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap)
    {
        var actual = heap.Held(args[0]);
        var expected = heap.Held(args[1]);
        var tolerance = heap.Held(args[2]);
        if (tolerance.Kind != Kind.Map || heap.MapCount(tolerance) == 0)
            return "Expected a tolerance map with absolute or relative numeric limits.";
        var absolute = WebLingoValue.FromInt64(0);
        var relative = WebLingoValue.FromInt64(0);
        for (var i = 0; i < heap.MapCount(tolerance); i++)
        {
            var (key, value) = heap.MapEntryAt(tolerance, i);
            value = heap.Held(value);
            if (key.Kind != Kind.String || !IsNumeric(value) || !double.IsFinite(AsDouble(value, heap)) || AsDouble(value, heap) < 0)
                return "Expected finite, nonnegative numeric tolerances.";
            switch (heap.GetString(key))
            {
                case "absolute": absolute = value; break;
                case "relative": relative = value; break;
                default: return "Expected tolerance keys absolute or relative.";
            }
        }
        if (IsNumeric(actual) && IsNumeric(expected))
        {
            var useDouble = actual.Kind == Kind.Binary64 || expected.Kind == Kind.Binary64
                || absolute.Kind == Kind.Binary64 || relative.Kind == Kind.Binary64;
            var close = useDouble
                ? CloseDouble(AsDouble(actual, heap), AsDouble(expected, heap), AsDouble(absolute, heap), AsDouble(relative, heap))
                : CloseDecimal(AsDecimal(actual, heap), AsDecimal(expected, heap), AsDecimal(absolute, heap), AsDecimal(relative, heap));
            if (close) return null;
        }
        return Expected(args[0], args[1], heap, $" within tolerance {Display(args[2], heap)}");
    }

    private static bool CloseDouble(double actual, double expected, double absolute, double relative)
    {
        if (actual == expected) return true;
        if (!double.IsFinite(actual) || !double.IsFinite(expected)) return false;
        var scale = Math.Max(Math.Abs(actual), Math.Abs(expected));
        var difference = Math.Abs(actual - expected);
        if (difference <= absolute) return true;
        return relative > 0
            && (double.IsFinite(difference) ? difference / scale : Math.Abs(actual / scale - expected / scale)) <= relative;
    }

    private static bool CloseDecimal(decimal actual, decimal expected, decimal absolute, decimal relative)
    {
        if (actual == expected) return true;
        var scale = Math.Max(Math.Abs(actual), Math.Abs(expected));
        try
        {
            var difference = Math.Abs(actual - expected);
            if (difference <= absolute) return true;
            return relative > 0 && difference / scale <= relative;
        }
        catch (OverflowException)
        {
            return Math.Abs(actual / scale - expected / scale) <= relative;
        }
    }

    private static bool IsNumeric(WebLingoValue value) => value.Kind is Kind.Int64 or Kind.Binary64 or Kind.Decimal;

    private static decimal AsDecimal(WebLingoValue value, WebLingoHeap heap) =>
        value.Kind == Kind.Decimal ? heap.GetDecimal(value) : value.AsInt64;

    private static double AsDouble(WebLingoValue value, WebLingoHeap heap) =>
        value.Kind == Kind.Binary64 ? value.AsBinary64 : (double)AsDecimal(value, heap);
}
