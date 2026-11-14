using WebLingo.Compiler;
using WebLingo.Language;
using Xunit;
namespace WebLingo.Compiler.Tests;

public sealed class LowererTests
{
    [Fact]
    public void LowersTheSpecificationsExample()
    {
        var program = Lower("var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);");
        Assert.Equal(
            new[] {
                "init 0 = 10", "init 1 = 20", "init 2 = (load 0 + load 1)", "eval writeLn#0(load 2)"
            },
            program.Statements.Select(Show));
        Assert.Equal(new[] { "a", "b", "result" }, program.LocalNames);
    }

    [Fact]
    public void AnAssignmentStoresAndYieldsItsValue()
    {
        Assert.Equal(
            new[] { "init 0 = 1", "eval (store 0 = 5)", "eval writeLn#0(load 0)" },
            Lower("var x = 1; x = 5; writeLn(x);").Statements.Select(Show));
    }

    [Fact]
    public void AnAssignmentsValueIsLoweredBeforeItsStore()
    {
        Assert.Equal(
            new[] { "init 0 = 1", "eval (store 0 = (load 0 + 1))" },
            Lower("var x = 1; x = x + 1;").Statements.Select(Show));
    }

    [Fact]
    public void OperandsAreLoweredLeftToRight()
    {
        Assert.Equal(
            new[] { "init 0 = 0", "eval ((store 0 = 1) + load 0)" },
            Lower("var x = 0; (x = 1) + x;").Statements.Select(Show));
    }

    [Fact]
    public void ParenthesesDisappearAndOperatorsRemain()
    {
        Assert.Equal(new[] { "init 0 = (-((1 + 2) * 3) / 4)" }, Lower("var v = -((1 + 2) * 3) / 4;").Statements.Select(Show));
    }

    private static WebLingoIrProgram Lower(string source)
    {
        var compilation = BinderTests.Compile(source);
        Assert.Empty(compilation.Diagnostics);
        return compilation.LoweredProgram!;
    }

    private static string Show(WebLingoIrStatement statement) => statement switch
    {
        WebLingoIrInitializeLocal init => $"init {init.Local} = {Show(init.Value)}",
        WebLingoIrEvaluate evaluate => $"eval {Show(evaluate.Expression)}",
        _ => "?"
    };

    private static string Show(WebLingoIrExpression expression) => expression switch
    {
        WebLingoIrInt64Constant constant => constant.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        WebLingoIrLoadLocal load => "load " + load.Local,
        WebLingoIrStoreLocal store => $"(store {store.Local} = {Show(store.Value)})",
        WebLingoIrUnary unary => $"{(unary.Operator == WebLingoUnaryOperator.Negate ? "-" : "+")}{Show(unary.Operand)}",
        WebLingoIrBinary binary => $"({Show(binary.Left)} {Symbol(binary.Operator)} {Show(binary.Right)})",
        WebLingoIrCallBuiltin call => $"writeLn#{call.Builtin}({string.Join(", ", call.Arguments.Select(Show))})",
        _ => "?"
    };

    private static string Symbol(WebLingoBinaryOperator op) => op switch
    {
        WebLingoBinaryOperator.Add => "+",
        WebLingoBinaryOperator.Subtract => "-",
        WebLingoBinaryOperator.Multiply => "*",
        _ => "/"
    };
}
