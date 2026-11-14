using WebLingo.Core;
namespace WebLingo.Runtime;

public enum WebLingoArithmeticOperator { Add, Subtract, Multiply, Divide }

public enum WebLingoComparisonOperator { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }

/// <summary>
/// The arithmetic of runtime values, the same for every VM and every host. Integer arithmetic is checked: a result
/// outside int64 is an error (WL3002), never a wrap (section 3.2). <c>/</c> gives a binary64 even for two integers
/// (section 8.6: 10 / 2 is 5.0). An int64 meeting a binary64 is converted to binary64, and binary64 arithmetic is
/// IEEE 754's, infinities and NaN included (both proposed, section 15.2). Any other operand is an error (WL3001).
/// </summary>
public static class WebLingoOperators
{
    public static bool TryBinary(WebLingoArithmeticOperator op, WebLingoValue left, WebLingoValue right, out WebLingoValue result,
        out WebLingoRuntimeError? error) => TryBinary(op, left, right, null, out result, out error);

    /// <summary>
    /// As the other overload; <c>+</c> of two strings also concatenates them, into <paramref name="heap"/>
    /// (specification, section 3.5). A string and anything else are not joined: nothing converts implicitly.
    /// </summary>
    public static bool TryBinary(WebLingoArithmeticOperator op, WebLingoValue left, WebLingoValue right, WebLingoHeap? heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (op == WebLingoArithmeticOperator.Add && left.Kind == WebLingoValueKind.String && right.Kind == WebLingoValueKind.String
            && heap is not null)
        {
            return heap.TryAllocateString(heap.GetString(left) + heap.GetString(right), out result, out error);
        }
        if ((left.Kind == WebLingoValueKind.Instance || right.Kind == WebLingoValueKind.Instance) && heap is not null
            && (heap.IsHolding(left) || heap.IsHolding(right)))
        {
            return TryHeld(op, left, right, heap, out result, out error);
        }
        if (op == WebLingoArithmeticOperator.Add && (left.Kind == WebLingoValueKind.String || right.Kind == WebLingoValueKind.String))
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'+' joins two strings or adds two numbers, not {WebLingoValueFormatter.Describe(left)} and {WebLingoValueFormatter.Describe(right)}.");
            return false;
        }
        error = CheckOperand(left, op) ?? CheckOperand(right, op);
        if (error is not null) return false;

        if (left.Kind == WebLingoValueKind.Int64 && right.Kind == WebLingoValueKind.Int64 && op != WebLingoArithmeticOperator.Divide)
        {
            var (a, b) = (left.AsInt64, right.AsInt64);
            var fits = op switch
            {
                WebLingoArithmeticOperator.Add => TryAdd(a, b, out var sum) && Set(out result, sum),
                WebLingoArithmeticOperator.Subtract => TrySubtract(a, b, out var difference) && Set(out result, difference),
                _ => TryMultiply(a, b, out var product) && Set(out result, product)
            };
            if (fits) return true;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IntegerOverflow,
                $"{Invariant(a)} {Symbol(op)} {Invariant(b)} does not fit in a 64-bit integer.");
            return false;
        }

        var (x, y) = (ToBinary64(left), ToBinary64(right));
        result = WebLingoValue.FromBinary64(op switch
        {
            WebLingoArithmeticOperator.Add => x + y,
            WebLingoArithmeticOperator.Subtract => x - y,
            WebLingoArithmeticOperator.Multiply => x * y,
            _ => x / y
        });
        return true;
    }

    /// <summary>
    /// <c>==</c> and <c>!=</c> never fail and never convert, except that an int64 and a binary64 compare as numbers,
    /// exactly; <c>&lt; &lt;= &gt; &gt;=</c> take numbers only (specification, section 3.5). NaN is unordered, and
    /// unequal even to itself.
    /// </summary>
    public static bool TryCompare(WebLingoComparisonOperator op, WebLingoValue left, WebLingoValue right, out WebLingoValue result,
        out WebLingoRuntimeError? error) => TryCompare(op, left, right, null, out result, out error);

    /// <summary>As the other overload; two strings are equal when their texts are, which their heap knows.</summary>
    public static bool TryCompare(WebLingoComparisonOperator op, WebLingoValue left, WebLingoValue right, WebLingoHeap? heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        if ((left.Kind == WebLingoValueKind.Instance || right.Kind == WebLingoValueKind.Instance) && heap is not null)
        {
            // An instance holding a value is compared as the value it holds (section 3.7).
            left = heap.Held(left);
            right = heap.Held(right);
        }
        error = null;
        if (op is WebLingoComparisonOperator.Equal or WebLingoComparisonOperator.NotEqual)
        {
            var equal = AreEqual(left, right, heap);
            result = WebLingoValue.FromBoolean(op == WebLingoComparisonOperator.Equal ? equal : !equal);
            return true;
        }
        result = WebLingoValue.Null;
        if (!IsNumber(left) || !IsNumber(right))
        {
            var bad = IsNumber(left) ? right : left;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'{Symbol(op)}' compares numbers, not {WebLingoValueFormatter.Describe(bad)}.");
            return false;
        }
        var order = Compare(left, right);
        result = WebLingoValue.FromBoolean(order is { } o && op switch
        {
            WebLingoComparisonOperator.Less => o < 0,
            WebLingoComparisonOperator.LessOrEqual => o <= 0,
            WebLingoComparisonOperator.Greater => o > 0,
            _ => o >= 0
        });
        return true;
    }

    /// <summary>
    /// Arithmetic on an instance holding a value (section 3.7): on the values held, its result an instance of its left
    /// operand's class, if that holds one, else of its right's, with that operand's fields; or a plain value, when it is
    /// not of the kind that class holds (an integer's class divided, giving a binary64).
    /// </summary>
    private static bool TryHeld(WebLingoArithmeticOperator op, WebLingoValue left, WebLingoValue right, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        var source = heap.IsHolding(left) ? left : right;
        if (!TryBinary(op, heap.Held(left), heap.Held(right), heap, out var value, out error))
        {
            result = WebLingoValue.Null;
            return false;
        }
        if (value.Kind != heap.HeldKind(source))
        {
            result = value;
            return true;
        }
        return heap.TryHoldAnew(source, value, out result, out error);
    }

    /// <summary>Unary <c>-</c> on any value, an instance holding a number giving one of its class (section 3.7).</summary>
    public static bool TryNegate(WebLingoValue operand, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        if (!heap.IsHolding(operand)) return TryNegate(operand, out result, out error);
        if (!TryNegate(heap.Held(operand), out var value, out error))
        {
            result = WebLingoValue.Null;
            return false;
        }
        return heap.TryHoldAnew(operand, value, out result, out error);
    }

    /// <summary>Unary <c>+</c> on any value, an instance holding a number giving one of its class (section 3.7).</summary>
    public static bool TryPlus(WebLingoValue operand, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        if (!heap.IsHolding(operand)) return TryPlus(operand, out result, out error);
        if (!TryPlus(heap.Held(operand), out var value, out error))
        {
            result = WebLingoValue.Null;
            return false;
        }
        return heap.TryHoldAnew(operand, value, out result, out error);
    }

    /// <summary><c>!</c>: the opposite of a Boolean; anything else is an error.</summary>
    public static bool TryNot(WebLingoValue operand, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        error = CheckBoolean(operand, "'!'");
        result = error is null ? WebLingoValue.FromBoolean(!operand.AsBoolean) : WebLingoValue.Null;
        return error is null;
    }

    /// <summary>An error unless the value is a Boolean: conditions and logical operands have no truthiness.</summary>
    public static WebLingoRuntimeError? CheckBoolean(WebLingoValue value, string what) =>
        value.Kind == WebLingoValueKind.Boolean
            ? null
            : new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"{what} needs a Boolean, not {WebLingoValueFormatter.Describe(value)}.");

    private static bool AreEqual(WebLingoValue left, WebLingoValue right, WebLingoHeap? heap)
    {
        if (IsNumber(left) && IsNumber(right)) return Compare(left, right) == 0;
        if (left.Kind == WebLingoValueKind.String && right.Kind == WebLingoValueKind.String)
        {
            return heap is null
                ? left.Handle == right.Handle
                : string.Equals(heap.GetString(left), heap.GetString(right), StringComparison.Ordinal);
        }
        return left == right; // The same kind and payload: null, a Boolean, or a function by identity.
    }

    private static bool IsNumber(WebLingoValue value) => value.Kind is WebLingoValueKind.Int64 or WebLingoValueKind.Binary64;

    /// <summary>The order of two numbers, exact even between an int64 and a binary64; null if either is NaN.</summary>
    private static int? Compare(WebLingoValue left, WebLingoValue right)
    {
        if (left.Kind == WebLingoValueKind.Int64 && right.Kind == WebLingoValueKind.Int64) return left.AsInt64.CompareTo(right.AsInt64);
        if (left.Kind == WebLingoValueKind.Binary64 && right.Kind == WebLingoValueKind.Binary64)
        {
            var (x, y) = (left.AsBinary64, right.AsBinary64);
            if (double.IsNaN(x) || double.IsNaN(y)) return null;
            return x < y ? -1 : x > y ? 1 : 0;
        }
        return left.Kind == WebLingoValueKind.Int64
            ? CompareInt64WithBinary64(left.AsInt64, right.AsBinary64)
            : -CompareInt64WithBinary64(right.AsInt64, left.AsBinary64);
    }

    private static int? CompareInt64WithBinary64(long a, double b)
    {
        if (double.IsNaN(b)) return null;
        if (b >= 9223372036854775808.0) return -1; // 2^63: above every int64.
        if (b < -9223372036854775808.0) return 1;
        var whole = Math.Truncate(b);
        var integral = (long)whole;
        if (a != integral) return a < integral ? -1 : 1;
        return b > whole ? -1 : b < whole ? 1 : 0;
    }

    private static string Symbol(WebLingoComparisonOperator op) => op switch
    {
        WebLingoComparisonOperator.Equal => "==",
        WebLingoComparisonOperator.NotEqual => "!=",
        WebLingoComparisonOperator.Less => "<",
        WebLingoComparisonOperator.LessOrEqual => "<=",
        WebLingoComparisonOperator.Greater => ">",
        _ => ">="
    };

    /// <summary>Unary <c>+</c>: the operand itself, which must be a number.</summary>
    public static bool TryPlus(WebLingoValue operand, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        error = CheckOperand(operand, null, "+");
        result = error is null ? operand : WebLingoValue.Null;
        return error is null;
    }

    public static bool TryNegate(WebLingoValue operand, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = CheckOperand(operand, null);
        if (error is not null) return false;
        if (operand.Kind == WebLingoValueKind.Binary64)
        {
            result = WebLingoValue.FromBinary64(-operand.AsBinary64);
            return true;
        }
        if (operand.AsInt64 == long.MinValue)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IntegerOverflow,
                $"-({Invariant(long.MinValue)}) does not fit in a 64-bit integer.");
            return false;
        }
        result = WebLingoValue.FromInt64(-operand.AsInt64);
        return true;
    }

    private static WebLingoRuntimeError? CheckOperand(WebLingoValue value, WebLingoArithmeticOperator? op, string unary = "-")
    {
        if (value.Kind is WebLingoValueKind.Int64 or WebLingoValueKind.Binary64) return null;
        var what = op is { } binary ? $"'{Symbol(binary)}'" : $"'{unary}'";
        var needs = op == WebLingoArithmeticOperator.Add ? "two numbers or two strings" : "numbers";
        return new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
            $"{what} needs {needs}, not {WebLingoValueFormatter.Describe(value)}.");
    }

    private static double ToBinary64(WebLingoValue value) => value.Kind == WebLingoValueKind.Int64 ? value.AsInt64 : value.AsBinary64;

    private static bool TryAdd(long a, long b, out long sum)
    {
        sum = unchecked(a + b);
        return ((a ^ sum) & (b ^ sum)) >= 0; // Overflow only when both operands' signs differ from the sum's.
    }

    private static bool TrySubtract(long a, long b, out long difference)
    {
        difference = unchecked(a - b);
        return ((a ^ b) & (a ^ difference)) >= 0; // Overflow only when a's and b's signs differ and the result's is not a's.
    }

    private static bool TryMultiply(long a, long b, out long product)
    {
        product = unchecked(a * b);
        if (a == 0 || b == 0) return true;
        if ((a == -1 && b == long.MinValue) || (b == -1 && a == long.MinValue)) return false;
        return product / b == a;
    }

    private static bool Set(out WebLingoValue result, long value)
    {
        result = WebLingoValue.FromInt64(value);
        return true;
    }

    private static string Symbol(WebLingoArithmeticOperator op) => op switch
    {
        WebLingoArithmeticOperator.Add => "+",
        WebLingoArithmeticOperator.Subtract => "-",
        WebLingoArithmeticOperator.Multiply => "*",
        _ => "/"
    };

    private static string Invariant(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
