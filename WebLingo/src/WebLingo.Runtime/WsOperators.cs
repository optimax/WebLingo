using WebLingo.Core;
namespace WebLingo.Runtime;

public enum WsArithmeticOperator { Add, Subtract, Multiply, Divide }

/// <summary>
/// The arithmetic of runtime values, the same for every VM and every host. Integer arithmetic is checked: a result
/// outside int64 is an error (WS3002), never a wrap (section 3.2). <c>/</c> gives a binary64 even for two integers
/// (section 8.6: 10 / 2 is 5.0). An int64 meeting a binary64 is converted to binary64, and binary64 arithmetic is
/// IEEE 754's, infinities and NaN included (both proposed, section 15.2). Any other operand is an error (WS3001).
/// </summary>
public static class WsOperators
{
    public static bool TryBinary(WsArithmeticOperator op, WsValue left, WsValue right, out WsValue result,
        out WsRuntimeError? error)
    {
        result = WsValue.Null;
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

    private static WsRuntimeError? CheckOperand(WsValue value, WsArithmeticOperator? op)
    {
        if (value.Kind is WsValueKind.Int64 or WsValueKind.Binary64) return null;
        if (!value.IsScriptValue)
        {
            return new WsRuntimeError(WsDiagnosticCodes.ReadBeforeInitialization, "A variable is read before it has a value.");
        }
        var what = op is { } binary ? $"'{Symbol(binary)}'" : "'-'";
        return new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
            $"{what} needs numbers, not {WsValueFormatter.Describe(value)}.");
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
