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

public sealed class HeapTests
{
    [Fact]
    public void AStringKeepsItsTextAndIsWrittenAsIt()
    {
        var heap = new WsHeap();
        Assert.True(heap.TryAllocateString("héllo", out var value, out _));
        Assert.Equal(WsValueKind.String, value.Kind);
        Assert.Equal("héllo", heap.GetString(value));
        Assert.Equal("héllo", WsValueFormatter.Format(value, heap));
        Assert.Equal("a string", WsValueFormatter.Describe(value));
    }

    [Fact]
    public void ACollectionFreesWhatNoRootReachesAndKeepsTheRest()
    {
        var heap = new WsHeap();
        var roots = new WsValue[1];
        heap.AddRoots(roots);
        Assert.True(heap.TryAllocateString("kept", out roots[0], out _));
        Assert.True(heap.TryAllocateString("garbage", out _, out _));
        Assert.Equal(2, heap.ObjectCount);
        heap.Collect();
        Assert.Equal(1, heap.ObjectCount);
        Assert.Equal("kept", heap.GetString(roots[0]));
        Assert.Equal(4 + WsHeap.ObjectOverhead, heap.Units);
    }

    [Fact]
    public void GarbageIsCollectedBeforeTheBudgetRunsOut()
    {
        var heap = new WsHeap(maxUnits: 10_000);
        var roots = new WsValue[1];
        heap.AddRoots(roots);
        for (var i = 0; i < 10_000; i++) Assert.True(heap.TryAllocateString("0123456789", out roots[0], out _));
        Assert.True(heap.CollectionCount > 0);
        Assert.InRange(heap.Units, 0, 10_000);
    }

    [Fact]
    public void AClosureKeepsItsCellsAndTheirValuesAlive()
    {
        var heap = new WsHeap();
        var roots = new WsValue[1];
        heap.AddRoots(roots);
        heap.TryAllocateString("kept through a cell", out var text, out _);
        heap.TryAllocateCell(text, true, out var cell, out _);
        heap.TryAllocateClosure(0, [cell], out roots[0], out _);
        heap.TryAllocateString("garbage", out _, out _);
        heap.Collect();
        Assert.Equal(3, heap.ObjectCount);
        var (function, cells) = heap.GetClosure(roots[0]);
        Assert.Equal(0, function);
        Assert.True(heap.TryReadCell(cells[0], out var held));
        Assert.Equal("kept through a cell", heap.GetString(held));
    }

    [Fact]
    public void ACellHasNoValueUntilOneIsWritten()
    {
        var heap = new WsHeap();
        heap.TryAllocateCell(WsValue.Null, false, out var cell, out _);
        Assert.False(heap.TryReadCell(cell, out _));
        heap.WriteCell(cell, WsValue.FromInt64(7));
        Assert.True(heap.TryReadCell(cell, out var value));
        Assert.Equal(WsValue.FromInt64(7), value);
    }

    [Fact]
    public void RootsComeAndGoWithFrames()
    {
        var heap = new WsHeap();
        var frame = new WsValue[1];
        heap.AddRoots(frame);
        heap.TryAllocateString("frame's", out frame[0], out _);
        heap.RemoveLastRoots(frame);
        heap.Collect();
        Assert.Equal(0, heap.ObjectCount);
        Assert.Throws<InvalidOperationException>(() => heap.RemoveLastRoots(frame));
    }

    [Fact]
    public void AStringBeyondTheBudgetIsAnError()
    {
        var heap = new WsHeap(maxUnits: 100);
        Assert.False(heap.TryAllocateString(new string('x', 100), out _, out var error));
        Assert.Equal(WsDiagnosticCodes.BudgetExceeded, error!.Code);
    }

    [Fact]
    public void PlusJoinsTwoStringsAndNothingElse()
    {
        var heap = new WsHeap();
        heap.TryAllocateString("ab", out var a, out _);
        heap.TryAllocateString("cd", out var c, out _);
        Assert.True(WsOperators.TryBinary(WsArithmeticOperator.Add, a, c, heap, out var joined, out _));
        Assert.Equal("abcd", heap.GetString(joined));
        Assert.False(WsOperators.TryBinary(WsArithmeticOperator.Add, a, WsValue.FromInt64(1), heap, out _, out var error));
        Assert.Equal(WsDiagnosticCodes.InvalidOperation, error!.Code);
        Assert.False(WsOperators.TryBinary(WsArithmeticOperator.Multiply, a, c, heap, out _, out _));
    }

    [Fact]
    public void StringsAreEqualByTheirText()
    {
        var heap = new WsHeap();
        heap.TryAllocateString("x", out var first, out _);
        heap.TryAllocateString("x", out var second, out _);
        Assert.True(WsOperators.TryCompare(WsComparisonOperator.Equal, first, second, heap, out var equal, out _));
        Assert.True(equal.AsBoolean);
        Assert.False(WsOperators.TryCompare(WsComparisonOperator.Less, first, second, heap, out _, out _));
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

    [Fact]
    public void UnaryPlusGivesTheNumberItself()
    {
        Assert.True(WsOperators.TryPlus(Int(-7), out var integer, out _));
        Assert.Equal(Int(-7), integer);
        Assert.True(WsOperators.TryPlus(WsValue.FromBinary64(-0.0), out var number, out _));
        Assert.True(double.IsNegative(number.AsBinary64));
        Assert.False(WsOperators.TryPlus(WsValue.Null, out _, out var error));
        Assert.Equal(WsDiagnosticCodes.InvalidOperation, error!.Code);
        Assert.Contains("'+'", error.Message);
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

public sealed class WriteTests
{
    [Fact]
    public void WriteWritesTheValueAsTextWithNoLineBreakAndGivesNull()
    {
        var output = new WsCollectedOutput();
        var write = new WsWriteFunction(output);
        Assert.Equal(("write", 1), (write.Name, write.Arity));
        Assert.True(write.TryInvoke([WsValue.FromBinary64(5.0)], new WsHeap(), out var result, out _));
        Assert.True(write.TryInvoke([WsValue.FromInt64(6)], new WsHeap(), out _, out _));
        Assert.Equal(WsValue.Null, result);
        Assert.Equal("5.06", output.Text);
    }

    [Fact]
    public void WriteLnEndsTheValueWithALineFeed()
    {
        var output = new WsCollectedOutput();
        var writeLn = new WsWriteLnFunction(output);
        Assert.Equal(("writeLn", 1), (writeLn.Name, writeLn.Arity));
        Assert.True(writeLn.TryInvoke([WsValue.FromInt64(1)], new WsHeap(), out var result, out _));
        Assert.True(writeLn.TryInvoke([WsValue.Null], new WsHeap(), out _, out _));
        Assert.Equal(WsValue.Null, result);
        Assert.Equal("1\nnull\n", output.Text);
        Assert.Equal(new[] { "1", "null" }, output.Lines);
    }

    [Fact]
    public void ARegistryRefusesTwoBuiltinsOfOneName()
    {
        var registry = new WsBuiltinRegistry(WsStandardBuiltins.Create(new WsCollectedOutput()));
        Assert.Throws<ArgumentException>(() => registry.Register(new WsWriteFunction(new WsCollectedOutput())));
        Assert.True(registry.TryGetBuiltin("write", out _));
        Assert.True(registry.TryGetBuiltin("writeLn", out _));
        Assert.False(registry.TryGetBuiltin("Write", out _));
    }
}

public sealed class QuoteTests
{
    [Theory]
    [InlineData("abc", "\"abc\"")]
    [InlineData("", "\"\"")]
    [InlineData("a\"b\\c", "\"a\\\"b\\\\c\"")]
    [InlineData("\n\r\t\0", "\"\\n\\r\\t\\0\"")]
    [InlineData("\u0001\u007F", "\"\\u{1}\\u{7F}\"")]
    [InlineData("é\U0001F600'", "\"é\U0001F600'\"")]
    public void AStringIsQuotedAsALiteralWouldWriteIt(string text, string quoted) =>
        Assert.Equal(quoted, WebLingo.Runtime.WsValueFormatter.Quote(text));
}
