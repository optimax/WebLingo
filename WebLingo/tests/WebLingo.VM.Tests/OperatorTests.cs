using WebLingo.Compiler;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM.Stack;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// Each VM does the arithmetic and comparisons of two int64s, or of two binary64s, itself, for speed, and leaves the
/// rest to <see cref="WebLingoOperators"/>. Every operator, on pairs of edge values of both kinds, must give what
/// <see cref="WebLingoOperators"/> gives: the same value, or the same error.
/// </summary>
public sealed class OperatorTests
{
    private static readonly (string Source, WebLingoValue Value)[] Operands =
    [
        ("0", WebLingoValue.FromInt64(0)),
        ("1", WebLingoValue.FromInt64(1)),
        ("-1", WebLingoValue.FromInt64(-1)),
        ("7", WebLingoValue.FromInt64(7)),
        ("3037000500", WebLingoValue.FromInt64(3037000500)), // Its square is just above the largest int64.
        ("-3037000500", WebLingoValue.FromInt64(-3037000500)),
        ("4294967296", WebLingoValue.FromInt64(4294967296)),
        ("9223372036854775807", WebLingoValue.FromInt64(long.MaxValue)),
        ("(-9223372036854775807 - 1)", WebLingoValue.FromInt64(long.MinValue)),
        ("0.0", WebLingoValue.FromBinary64(0.0)),
        ("-0.0", WebLingoValue.FromBinary64(-0.0)),
        ("1.5", WebLingoValue.FromBinary64(1.5)),
        ("7.0", WebLingoValue.FromBinary64(7.0)),
        ("9223372036854775808.0", WebLingoValue.FromBinary64(9223372036854775808.0)),
        ("NaN", WebLingoValue.FromBinary64(double.NaN)),
        ("Infinity", WebLingoValue.FromBinary64(double.PositiveInfinity)),
        ("-Infinity", WebLingoValue.FromBinary64(double.NegativeInfinity)),
        ("true", WebLingoValue.True),
        ("null", WebLingoValue.Null),
    ];

    [Theory]
    [InlineData("+", WebLingoArithmeticOperator.Add)]
    [InlineData("-", WebLingoArithmeticOperator.Subtract)]
    [InlineData("*", WebLingoArithmeticOperator.Multiply)]
    [InlineData("/", WebLingoArithmeticOperator.Divide)]
    public void ArithmeticIsTheRuntimes(string symbol, WebLingoArithmeticOperator op)
    {
        foreach (var (left, a) in Operands)
        {
            foreach (var (right, b) in Operands)
            {
                WebLingoOperators.TryBinary(op, a, b, out var value, out var error);
                AssertTheVmGives(left, symbol, right, value, error);
            }
        }
    }

    [Theory]
    [InlineData("==", WebLingoComparisonOperator.Equal)]
    [InlineData("!=", WebLingoComparisonOperator.NotEqual)]
    [InlineData("<", WebLingoComparisonOperator.Less)]
    [InlineData("<=", WebLingoComparisonOperator.LessOrEqual)]
    [InlineData(">", WebLingoComparisonOperator.Greater)]
    [InlineData(">=", WebLingoComparisonOperator.GreaterOrEqual)]
    public void ComparisonsAreTheRuntimes(string symbol, WebLingoComparisonOperator op)
    {
        foreach (var (left, a) in Operands)
        {
            foreach (var (right, b) in Operands)
            {
                WebLingoOperators.TryCompare(op, a, b, out var value, out var error);
                AssertTheVmGives(left, symbol, right, value, error);
            }
        }
    }

    private static readonly WebLingoCompilerOptions WithWriteLn = new([new WebLingoBuiltinDeclaration("writeLn", 1)]);

    /// <summary>
    /// Runs the expression on operands held in variables, so that the VM, not the compiler, evaluates it, on the
    /// register VM and on the stack VM.
    /// </summary>
    private static void AssertTheVmGives(string left, string symbol, string right, WebLingoValue value, WebLingoRuntimeError? error)
    {
        var source = $"var a = {left}; var b = {right}; writeLn(a {symbol} b);";
        var registerModule = new WebLingoCompiler(WithWriteLn).Compile(source, "operators.wl").Module;
        var stackModule = new WebLingoStackCompiler(WithWriteLn).Compile(source, "operators.wl").Module;
        Assert.True(registerModule is not null && stackModule is not null, source);
        foreach (var vm in new[] { "register", "stack" })
        {
            var output = new WebLingoCollectedOutput();
            var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
            {
                Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output))
            };
            var result = vm == "register"
                ? new WebLingoVirtualMachine().Execute(registerModule, context)
                : new WebLingoStackVirtualMachine().Execute(stackModule, context);
            var what = $"{source} on the {vm} VM";
            if (error is null)
            {
                Assert.True(result.Succeeded, $"{what}: {result.Error?.Message}");
                Assert.Equal((what, WebLingoValueFormatter.Format(value) + "\n"), (what, output.Text));
            }
            else
            {
                Assert.Equal((what, error.Code, error.Message), (what, result.Error?.Code, result.Error?.Message));
            }
        }
    }
}
