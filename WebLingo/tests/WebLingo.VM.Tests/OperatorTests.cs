using WebLingo.Compiler;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// The VM does the arithmetic and comparisons of two int64s, or of two binary64s, itself, for speed, and leaves the
/// rest to <see cref="WsOperators"/>. Every operator, on pairs of edge values of both kinds, must give what
/// <see cref="WsOperators"/> gives: the same value, or the same error.
/// </summary>
public sealed class OperatorTests
{
    private static readonly (string Source, WsValue Value)[] Operands =
    [
        ("0", WsValue.FromInt64(0)),
        ("1", WsValue.FromInt64(1)),
        ("-1", WsValue.FromInt64(-1)),
        ("7", WsValue.FromInt64(7)),
        ("3037000500", WsValue.FromInt64(3037000500)), // Its square is just above the largest int64.
        ("-3037000500", WsValue.FromInt64(-3037000500)),
        ("4294967296", WsValue.FromInt64(4294967296)),
        ("9223372036854775807", WsValue.FromInt64(long.MaxValue)),
        ("(-9223372036854775807 - 1)", WsValue.FromInt64(long.MinValue)),
        ("0.0", WsValue.FromBinary64(0.0)),
        ("-0.0", WsValue.FromBinary64(-0.0)),
        ("1.5", WsValue.FromBinary64(1.5)),
        ("7.0", WsValue.FromBinary64(7.0)),
        ("9223372036854775808.0", WsValue.FromBinary64(9223372036854775808.0)),
        ("NaN", WsValue.FromBinary64(double.NaN)),
        ("Infinity", WsValue.FromBinary64(double.PositiveInfinity)),
        ("-Infinity", WsValue.FromBinary64(double.NegativeInfinity)),
        ("true", WsValue.True),
        ("null", WsValue.Null),
    ];

    [Theory]
    [InlineData("+", WsArithmeticOperator.Add)]
    [InlineData("-", WsArithmeticOperator.Subtract)]
    [InlineData("*", WsArithmeticOperator.Multiply)]
    [InlineData("/", WsArithmeticOperator.Divide)]
    public void ArithmeticIsTheRuntimes(string symbol, WsArithmeticOperator op)
    {
        foreach (var (left, a) in Operands)
        {
            foreach (var (right, b) in Operands)
            {
                WsOperators.TryBinary(op, a, b, out var value, out var error);
                AssertTheVmGives(left, symbol, right, value, error);
            }
        }
    }

    [Theory]
    [InlineData("==", WsComparisonOperator.Equal)]
    [InlineData("!=", WsComparisonOperator.NotEqual)]
    [InlineData("<", WsComparisonOperator.Less)]
    [InlineData("<=", WsComparisonOperator.LessOrEqual)]
    [InlineData(">", WsComparisonOperator.Greater)]
    [InlineData(">=", WsComparisonOperator.GreaterOrEqual)]
    public void ComparisonsAreTheRuntimes(string symbol, WsComparisonOperator op)
    {
        foreach (var (left, a) in Operands)
        {
            foreach (var (right, b) in Operands)
            {
                WsOperators.TryCompare(op, a, b, out var value, out var error);
                AssertTheVmGives(left, symbol, right, value, error);
            }
        }
    }

    private static readonly WsCompilerOptions WithWriteLn = new([new WsBuiltinDeclaration("writeLn", 1)]);

    /// <summary>Runs the expression on operands held in variables, so that the VM, not the compiler, evaluates it.</summary>
    private static void AssertTheVmGives(string left, string symbol, string right, WsValue value, WsRuntimeError? error)
    {
        var source = $"var a = {left}; var b = {right}; writeLn(a {symbol} b);";
        var compiled = new WsCompiler(WithWriteLn).Compile(source, "operators.ws");
        Assert.True(compiled.Module is not null, source);
        var output = new WsCollectedOutput();
        var context = new WsExecutionContext(new WsWorkerId(0)) { Services = new WsBuiltinRegistry(WsStandardBuiltins.Create(output)) };
        var result = new WsVirtualMachine().Execute(compiled.Module, context);
        if (error is null)
        {
            Assert.True(result.Succeeded, $"{source}: {result.Error?.Message}");
            Assert.Equal((source, WsValueFormatter.Format(value) + "\n"), (source, output.Text));
        }
        else
        {
            Assert.Equal((source, error.Code, error.Message), (source, result.Error?.Code, result.Error?.Message));
        }
    }
}
