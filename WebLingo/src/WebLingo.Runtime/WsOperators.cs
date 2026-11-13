using WebLingo.Core;
namespace WebLingo.Runtime;

public enum WsArithmeticOperator { Add, Subtract, Multiply, Divide }

public enum WsComparisonOperator { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }

/// <summary>
/// The arithmetic of runtime values, the same for every VM and every host. Integer arithmetic is checked: a result
/// outside int64 is an error (WS3002), never a wrap (section 3.2). <c>/</c> gives a binary64 even for two integers
/// (section 8.6: 10 / 2 is 5.0). An int64 meeting a binary64 is converted to binary64, and binary64 arithmetic is
/// IEEE 754's, infinities and NaN included (both proposed, section 15.2). Any other operand is an error (WS3001).
/// </summary>
public static class WsOperators
{
    public static bool TryBinary(WsArithmeticOperator op, WsValue left, WsValue right, out WsValue result,
        out WsRuntimeError? error) => TryBinary(op, left, right, null, out result, out error);

    /// <summary>
    /// As the other overload; <c>+</c> of two strings also concatenates them, into <paramref name="heap"/>
    /// (specification, section 3.5). A string and anything else are not joined: nothing converts implicitly.
    /// </summary>
    public static bool TryBinary(WsArithmeticOperator op, WsValue left, WsValue right, WsHeap? heap, out WsValue result,
        out WsRuntimeError? error)
    {
        result = WsValue.Null;
        if (op == WsArithmeticOperator.Add && left.Kind == WsValueKind.String && right.Kind == WsValueKind.String
            && heap is not null)
        {
            return heap.TryAllocateString(heap.GetString(left) + heap.GetString(right), out result, out error);
        }
        if (op == WsArithmeticOperator.Add && (left.Kind == WsValueKind.String || right.Kind == WsValueKind.String))
        {
            error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                $"'+' joins two strings or adds two numbers, not {WsValueFormatter.Describe(left)} and {WsValueFormatter.Describe(right)}.");
            return false;
        }
        error = CheckOperand(left, op) ?? CheckOperand(right, op);
        if (error is not null) return false;

        if (left.Kind == WsValueKind.Int64 && right.Kind == WsValueKind.Int64 && op != WsArithmeticOperator.Divide)
        {
            var (a, b) = (left.AsInt64, right.AsInt64);
            var fits = op switch
            {
                WsArithmeticOperator.Add => TryAdd(a, b, out var sum) && Set(out result, sum),
                WsArithmeticOperator.Subtract => TrySubtract(a, b, out var difference) && Set(out result, difference),
                _ => TryMultiply(a, b, out var product) && Set(out result, product)
            };
            if (fits) return true;
            error = new WsRuntimeError(WsDiagnosticCodes.IntegerOverflow,
                $"{Invariant(a)} {Symbol(op)} {Invariant(b)} does not fit in a 64-bit integer.");
            return false;
        }

        var (x, y) = (ToBinary64(left), ToBinary64(right));
        result = WsValue.FromBinary64(op switch
        {
            WsArithmeticOperator.Add => x + y,
            WsArithmeticOperator.Subtract => x - y,
            WsArithmeticOperator.Multiply => x * y,
            _ => x / y
        });
        return true;
    }

    /// <summary>
    /// <c>==</c> and <c>!=</c> never fail and never convert, except that an int64 and a binary64 compare as numbers,
    /// exactly; <c>&lt; &lt;= &gt; &gt;=</c> take numbers only (specification, section 3.5). NaN is unordered, and
    /// unequal even to itself.
    /// </summary>
    public static bool TryCompare(WsComparisonOperator op, WsValue left, WsValue right, out WsValue result,
        out WsRuntimeError? error) => TryCompare(op, left, right, null, out result, out error);

    /// <summary>As the other overload; two strings are equal when their texts are, which their heap knows.</summary>
    public static bool TryCompare(WsComparisonOperator op, WsValue left, WsValue right, WsHeap? heap, out WsValue result,
        out WsRuntimeError? error)
    {
        error = null;
        if (op is WsComparisonOperator.Equal or WsComparisonOperator.NotEqual)
        {
            var equal = AreEqual(left, right, heap);
            result = WsValue.FromBoolean(op == WsComparisonOperator.Equal ? equal : !equal);
            return true;
        }
        result = WsValue.Null;
        if (!IsNumber(left) || !IsNumber(right))
        {
            var bad = IsNumber(left) ? right : left;
            error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                $"'{Symbol(op)}' compares numbers, not {WsValueFormatter.Describe(bad)}.");
            return false;
        }
        var order = Compare(left, right);
        result = WsValue.FromBoolean(order is { } o && op switch
        {
            WsComparisonOperator.Less => o < 0,
            WsComparisonOperator.LessOrEqual => o <= 0,
            WsComparisonOperator.Greater => o > 0,
            _ => o >= 0
        });
        return true;
    }

    /// <summary><c>!</c>: the opposite of a Boolean; anything else is an error.</summary>
    public static bool TryNot(WsValue operand, out WsValue result, out WsRuntimeError? error)
    {
        error = CheckBoolean(operand, "'!'");
        result = error is null ? WsValue.FromBoolean(!operand.AsBoolean) : WsValue.Null;
        return error is null;
    }

    /// <summary>An error unless the value is a Boolean: conditions and logical operands have no truthiness.</summary>
    public static WsRuntimeError? CheckBoolean(WsValue value, string what) =>
        value.Kind == WsValueKind.Boolean
            ? null
            : new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                $"{what} needs a Boolean, not {WsValueFormatter.Describe(value)}.");

    private static bool AreEqual(WsValue left, WsValue right, WsHeap? heap)
    {
        if (IsNumber(left) && IsNumber(right)) return Compare(left, right) == 0;
        if (left.Kind == WsValueKind.String && right.Kind == WsValueKind.String)
        {
            return heap is null
                ? left.Handle == right.Handle
                : string.Equals(heap.GetString(left), heap.GetString(right), StringComparison.Ordinal);
        }
        return left == right; // The same kind and payload: null, a Boolean, or a function by identity.
    }

    private static bool IsNumber(WsValue value) => value.Kind is WsValueKind.Int64 or WsValueKind.Binary64;

    /// <summary>The order of two numbers, exact even between an int64 and a binary64; null if either is NaN.</summary>
    private static int? Compare(WsValue left, WsValue right)
    {
        if (left.Kind == WsValueKind.Int64 && right.Kind == WsValueKind.Int64) return left.AsInt64.CompareTo(right.AsInt64);
        if (left.Kind == WsValueKind.Binary64 && right.Kind == WsValueKind.Binary64)
        {
            var (x, y) = (left.AsBinary64, right.AsBinary64);
            if (double.IsNaN(x) || double.IsNaN(y)) return null;
            return x < y ? -1 : x > y ? 1 : 0;
        }
        return left.Kind == WsValueKind.Int64
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

    private static string Symbol(WsComparisonOperator op) => op switch
    {
        WsComparisonOperator.Equal => "==",
        WsComparisonOperator.NotEqual => "!=",
        WsComparisonOperator.Less => "<",
        WsComparisonOperator.LessOrEqual => "<=",
        WsComparisonOperator.Greater => ">",
        _ => ">="
    };

    /// <summary>Unary <c>+</c>: the operand itself, which must be a number.</summary>
    public static bool TryPlus(WsValue operand, out WsValue result, out WsRuntimeError? error)
    {
        error = CheckOperand(operand, null, "+");
        result = error is null ? operand : WsValue.Null;
        return error is null;
    }

    public static bool TryNegate(WsValue operand, out WsValue result, out WsRuntimeError? error)
    {
        result = WsValue.Null;
        error = CheckOperand(operand, null);
        if (error is not null) return false;
        if (operand.Kind == WsValueKind.Binary64)
        {
            result = WsValue.FromBinary64(-operand.AsBinary64);
            return true;
        }
        if (operand.AsInt64 == long.MinValue)
        {
            error = new WsRuntimeError(WsDiagnosticCodes.IntegerOverflow,
                $"-({Invariant(long.MinValue)}) does not fit in a 64-bit integer.");
            return false;
        }
        result = WsValue.FromInt64(-operand.AsInt64);
        return true;
    }

    private static WsRuntimeError? CheckOperand(WsValue value, WsArithmeticOperator? op, string unary = "-")
    {
        if (value.Kind is WsValueKind.Int64 or WsValueKind.Binary64) return null;
        var what = op is { } binary ? $"'{Symbol(binary)}'" : $"'{unary}'";
        var needs = op == WsArithmeticOperator.Add ? "two numbers or two strings" : "numbers";
        return new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
            $"{what} needs {needs}, not {WsValueFormatter.Describe(value)}.");
    }

    private static double ToBinary64(WsValue value) => value.Kind == WsValueKind.Int64 ? value.AsInt64 : value.AsBinary64;

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

    private static bool Set(out WsValue result, long value)
    {
        result = WsValue.FromInt64(value);
        return true;
    }

    private static string Symbol(WsArithmeticOperator op) => op switch
    {
        WsArithmeticOperator.Add => "+",
        WsArithmeticOperator.Subtract => "-",
        WsArithmeticOperator.Multiply => "*",
        _ => "/"
    };

    private static string Invariant(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
