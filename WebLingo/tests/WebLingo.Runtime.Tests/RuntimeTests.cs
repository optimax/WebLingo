using System.Runtime.CompilerServices;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Runtime.Tests;

public sealed class ValueTests
{
    [Fact]
    public void AValueIsSixteenBytes()
    {
        Assert.Equal(16, Unsafe.SizeOf<WsValue>()); // The specification's target, section 4.1.
    }

    [Fact]
    public void ValuesKeepWhatTheyHold()
    {
        Assert.Equal(long.MinValue, WsValue.FromInt64(long.MinValue).AsInt64);
        Assert.Equal(-0.0, WsValue.FromBinary64(-0.0).AsBinary64);
        Assert.True(double.IsNegative(WsValue.FromBinary64(-0.0).AsBinary64));
        Assert.True(WsValue.True.AsBoolean);
        Assert.Equal(WsValueKind.Null, default(WsValue).Kind);
    }

    [Theory]
    [InlineData(30L, "30")]
    [InlineData(-7L, "-7")]
    [InlineData(long.MinValue, "-9223372036854775808")]
    public void IntegersAreShownInDigits(long value, string text)
    {
        Assert.Equal(text, WsValueFormatter.Format(WsValue.FromInt64(value)));
    }

    [Theory]
    [InlineData(5.0, "5.0")]
    [InlineData(3.5, "3.5")]
    [InlineData(0.1, "0.1")]
    [InlineData(-0.0, "-0.0")]
    [InlineData(1e21, "1E+21")]
    [InlineData(1.5e-7, "1.5E-07")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void NumbersAreShownSoTheyReadBackAndAreNotMistakenForIntegers(double value, string text)
    {
        Assert.Equal(text, WsValueFormatter.Format(WsValue.FromBinary64(value)));
    }

    [Fact]
    public void FormattingDoesNotDependOnTheCulture()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("3.5", WsValueFormatter.Format(WsValue.FromBinary64(3.5)));
            Assert.Equal("-1234567", WsValueFormatter.Format(WsValue.FromInt64(-1234567)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void NullAndBooleansAreShownByName()
    {
        Assert.Equal(new[] { "null", "true", "false" },
            new[] { WsValue.Null, WsValue.True, WsValue.False }.Select(WsValueFormatter.Format));
    }
}

public sealed class OperatorTests
{
    [Theory]
    [InlineData(WsArithmeticOperator.Add, 10L, 20L, 30L)]
    [InlineData(WsArithmeticOperator.Subtract, 10L, 20L, -10L)]
    [InlineData(WsArithmeticOperator.Multiply, -4L, 5L, -20L)]
    [InlineData(WsArithmeticOperator.Add, long.MaxValue, long.MinValue, -1L)]
    [InlineData(WsArithmeticOperator.Subtract, -1L, long.MaxValue, long.MinValue)]
    [InlineData(WsArithmeticOperator.Multiply, long.MinValue, 1L, long.MinValue)]
    [InlineData(WsArithmeticOperator.Multiply, 3037000499L, 3037000499L, 9223372030926249001L)]
    public void IntegerArithmeticIsExactWithinInt64(WsArithmeticOperator op, long a, long b, long expected)
    {
        Assert.True(WsOperators.TryBinary(op, Int(a), Int(b), out var result, out _));
        Assert.Equal(WsValue.FromInt64(expected), result);
    }

    [Theory]
    [InlineData(WsArithmeticOperator.Add, long.MaxValue, 1L)]
    [InlineData(WsArithmeticOperator.Add, long.MinValue, -1L)]
    [InlineData(WsArithmeticOperator.Subtract, long.MinValue, 1L)]
    [InlineData(WsArithmeticOperator.Subtract, 0L, long.MinValue)]
    [InlineData(WsArithmeticOperator.Multiply, long.MinValue, -1L)]
    [InlineData(WsArithmeticOperator.Multiply, -1L, long.MinValue)]
    [InlineData(WsArithmeticOperator.Multiply, 3037000500L, 3037000500L)]
    [InlineData(WsArithmeticOperator.Multiply, long.MaxValue, 2L)]
    public void IntegerOverflowIsAnError(WsArithmeticOperator op, long a, long b)
    {
        Assert.False(WsOperators.TryBinary(op, Int(a), Int(b), out _, out var error));
        Assert.Equal(WsDiagnosticCodes.IntegerOverflow, error!.Code);
    }

    [Fact]
    public void NegatingTheSmallestIntegerOverflows()
    {
        Assert.False(WsOperators.TryNegate(Int(long.MinValue), out _, out var error));
        Assert.Equal(WsDiagnosticCodes.IntegerOverflow, error!.Code);
        Assert.True(WsOperators.TryNegate(Int(long.MaxValue), out var result, out _));
        Assert.Equal(-long.MaxValue, result.AsInt64);
    }

    [Theory]
    [InlineData(10L, 2L, 5.0)]
    [InlineData(7L, 2L, 3.5)]
    [InlineData(1L, 3L, 1.0 / 3.0)]
    [InlineData(1L, 0L, double.PositiveInfinity)]
    [InlineData(-1L, 0L, double.NegativeInfinity)]
    public void DividingIntegersGivesABinary64(long a, long b, double expected)
    {
        Assert.True(WsOperators.TryBinary(WsArithmeticOperator.Divide, Int(a), Int(b), out var result, out _));
        Assert.Equal(WsValueKind.Binary64, result.Kind);
        Assert.Equal(expected, result.AsBinary64);
    }

    [Fact]
    public void ZeroDividedByZeroIsNaN()
    {
        Assert.True(WsOperators.TryBinary(WsArithmeticOperator.Divide, Int(0), Int(0), out var result, out _));
        Assert.True(double.IsNaN(result.AsBinary64));
    }

    [Fact]
    public void AnIntegerMeetingABinary64BecomesOne()
    {
        Assert.True(WsOperators.TryBinary(WsArithmeticOperator.Add, Int(1), WsValue.FromBinary64(0.5), out var sum, out _));
        Assert.Equal(WsValue.FromBinary64(1.5), sum);
        Assert.True(WsOperators.TryBinary(WsArithmeticOperator.Multiply, WsValue.FromBinary64(2.5), Int(2), out var product, out _));
        Assert.Equal(WsValue.FromBinary64(5.0), product);
    }

    [Fact]
    public void Binary64ArithmeticDoesNotOverflowIntoAnError()
    {
        Assert.True(WsOperators.TryBinary(WsArithmeticOperator.Multiply, WsValue.FromBinary64(double.MaxValue),
            WsValue.FromBinary64(2), out var result, out _));
        Assert.Equal(double.PositiveInfinity, result.AsBinary64);
    }

    [Fact]
    public void OnlyNumbersTakePartInArithmetic()
    {
        Assert.False(WsOperators.TryBinary(WsArithmeticOperator.Add, WsValue.Null, Int(1), out _, out var nullError));
        Assert.Equal(WsDiagnosticCodes.InvalidOperation, nullError!.Code);
        Assert.False(WsOperators.TryNegate(WsValue.True, out _, out var booleanError));
        Assert.Equal(WsDiagnosticCodes.InvalidOperation, booleanError!.Code);
    }

    private static WsValue Int(long value) => WsValue.FromInt64(value);
}

public sealed class PrintTests
{
    [Fact]
    public void PrintWritesTheValueAsALineAndGivesNull()
    {
        var output = new WsCollectedOutput();
        var print = new WsPrintFunction(output);
        Assert.Equal(("print", 1), (print.Name, print.Arity));
        Assert.True(print.TryInvoke([WsValue.FromBinary64(5.0)], out var result, out _));
        Assert.Equal(WsValue.Null, result);
        Assert.Equal(new[] { "5.0" }, output.Lines);
    }

    [Fact]
    public void ARegistryRefusesTwoBuiltinsOfOneName()
    {
        var registry = new WsBuiltinRegistry(new WsPrintFunction(new WsCollectedOutput()));
        Assert.Throws<ArgumentException>(() => registry.Register(new WsPrintFunction(new WsCollectedOutput())));
        Assert.True(registry.TryGetBuiltin("print", out _));
        Assert.False(registry.TryGetBuiltin("Print", out _));
    }
}
