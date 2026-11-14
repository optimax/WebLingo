using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using Xunit;
namespace WebLingo.VM.Tests;

public sealed class VirtualMachineTests
{
    [Fact]
    public void RunsTheSpecificationsExampleAndPrints30()
    {
        var (result, lines) = Run("var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);");
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "30" }, lines);
    }

    [Theory]
    [InlineData("writeLn(10 / 2);", "5.0")]
    [InlineData("writeLn(7 / 2);", "3.5")]
    [InlineData("writeLn(2 + 3 * 4);", "14")]
    [InlineData("writeLn((2 + 3) * 4);", "20")]
    [InlineData("writeLn(-(1 - 4));", "3")]
    [InlineData("writeLn(10 / 4 + 1);", "3.5")]
    [InlineData("var x = 1; x = 5; writeLn(x * x);", "25")]
    [InlineData("var a = 1; var b = a = 7; writeLn(a + b);", "14")]
    [InlineData("var x = 1; writeLn(x + (x = 5));", "6")]
    [InlineData("writeLn(writeLn(1));", "1\nnull")]
    [InlineData("writeLn(9223372036854775807);", "9223372036854775807")]
    [InlineData("writeLn(+(1 - 4) + +2);", "-1")]
    [InlineData("writeLn(1.5 + 1);", "2.5")]
    [InlineData("writeLn(1e21);", "1E+21")]
    [InlineData("writeLn(-0.0);", "-0.0")]
    [InlineData("writeLn(Infinity); writeLn(-Infinity); writeLn(NaN);", "Infinity\n-Infinity\nNaN")]
    [InlineData("write(1); write(2); writeLn(3); write(4);", "123\n4")]
    public void ComputesWhatTheProgramSays(string source, string expected)
    {
        var (result, lines) = Run(source);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(expected.Split('\n'), lines);
    }

    [Fact]
    public void IntegerOverflowStopsTheProgramWhereItHappens()
    {
        var source = "var big = 9223372036854775807;\nwriteLn(1);\nvar bigger = big + 1;\nwriteLn(2);";
        var (result, lines) = Run(source);
        Assert.Equal(new[] { "1" }, lines);
        Assert.Equal(WebLingoDiagnosticCodes.IntegerOverflow, result.Error!.Code);
        Assert.Equal(source.IndexOf("big + 1", StringComparison.Ordinal), result.Error.Span.Start);
    }

    [Fact]
    public void AnInstructionBudgetStopsTheProgram()
    {
        var module = Compile("writeLn(1); writeLn(2); writeLn(3);");
        var output = new WebLingoCollectedOutput();
        var result = new WebLingoVirtualMachine().Execute(module, Context(output) with { InstructionBudget = 4 });
        Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        Assert.Equal(new[] { "1", "2" }, output.Lines);
        Assert.Equal(4, result.Instructions);
    }

    [Fact]
    public void TheResultCountsTheInstructionsRun()
    {
        // Three times LoadConstant and CallBuiltin, then Return.
        var result = new WebLingoVirtualMachine().Execute(Compile("writeLn(1); writeLn(2); writeLn(3);"), Context(new WebLingoCollectedOutput()));
        Assert.True(result.Succeeded);
        Assert.Equal(7, result.Instructions);
    }

    [Fact]
    public void ARegisterBudgetKeepsAModuleFromRunning()
    {
        var module = Compile("var a = 1; var b = 2; writeLn(a + b);");
        var output = new WebLingoCollectedOutput();
        var result = new WebLingoVirtualMachine().Execute(module, Context(output) with { Resources = new WebLingoResourceBudget(2) });
        Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        Assert.Empty(output.Lines);
    }

    [Fact]
    public void CancellationStopsAnEndlessProgram()
    {
        // An endless loop, which the compiler cannot write yet: 0: r0 = 1; 1: r0 = -r0; 2: jump 1.
        var module = new WebLingoCompiledModule("loop", [WebLingoConstant.Int64(1)], [],
            new WebLingoFunction("main", 1, 0, [
                new WebLingoInstruction(WebLingoOpCode.LoadConstant, 0, 0), new WebLingoInstruction(WebLingoOpCode.Negate, 0, 0),
                new WebLingoInstruction(WebLingoOpCode.Jump, 1)
            ]));
        var signal = new WebLingoCancellationSignal();
        signal.Cancel();
        var result = new WebLingoVirtualMachine().Execute(module, new WebLingoExecutionContext(new WebLingoWorkerId(1)) { Cancellation = signal });
        Assert.Equal(WebLingoDiagnosticCodes.Cancelled, result.Error!.Code);

        var budgeted = new WebLingoVirtualMachine().Execute(module, new WebLingoExecutionContext(new WebLingoWorkerId(1)) { InstructionBudget = 100_000 });
        Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, budgeted.Error!.Code);
    }

    [Fact]
    public void GarbageStringsAreCollectedAsTheProgramRuns()
    {
        var output = new WebLingoCollectedOutput();
        var compiled = new WebLingoCompiler(WithWrite).Compile(
            "var t = \"\"; for (var i = 0; i < 20000; i = i + 1) { t = \"abcdefghij\" + \"klmnopqrst\"; } writeLn(t);",
            "test.wl");
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
        {
            Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output)),
            Resources = new WebLingoResourceBudget(65535, MaxHeapUnits: 10_000)
        };
        var result = new WebLingoVirtualMachine().Execute(compiled.Module!, context);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new[] { "abcdefghijklmnopqrst" }, output.Lines);
    }

    [Fact]
    public void AStringGrowingPastTheBudgetStopsTheProgram()
    {
        var compiled = new WebLingoCompiler(WithWrite).Compile(
            "var t = \"x\"; while (true) t = t + t;", "test.wl");
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0)) { Resources = new WebLingoResourceBudget(65535, MaxHeapUnits: 100_000) };
        var result = new WebLingoVirtualMachine().Execute(compiled.Module!, context);
        Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, result.Error!.Code);
    }

    [Fact]
    public void AModuleThatFailsVerificationIsNotRun()
    {
        var module = new WebLingoCompiledModule("bad", [], [new WebLingoImport("writeLn", 1)],
            new WebLingoFunction("main", 1, 0, [new WebLingoInstruction(WebLingoOpCode.CallBuiltin, 0, 0, 0, 1), new WebLingoInstruction(WebLingoOpCode.Return)]));
        var output = new WebLingoCollectedOutput();
        var result = new WebLingoVirtualMachine().Execute(module, Context(output));
        Assert.Equal(WebLingoDiagnosticCodes.BytecodeVerificationFailure, result.Error!.Code);
        Assert.Empty(output.Lines);
    }

    [Fact]
    public void AnImportTheHostDoesNotOfferIsNotRun()
    {
        var module = Compile("writeLn(1);");
        var result = new WebLingoVirtualMachine().Execute(module, new WebLingoExecutionContext(new WebLingoWorkerId(0)));
        Assert.Equal(WebLingoDiagnosticCodes.UndefinedIdentifier, result.Error!.Code);
    }

    [Fact]
    public void AModuleReadingARegisterBeforeWritingItIsNotRun()
    {
        // 0: r1 = r0; 1: writeLn(r1). r0 is never written: the verifier rejects the module, so print never sees it.
        var module = new WebLingoCompiledModule("leak", [], [new WebLingoImport("writeLn", 1)],
            new WebLingoFunction("main", 2, 1, [
                new WebLingoInstruction(WebLingoOpCode.Move, 1, 0), new WebLingoInstruction(WebLingoOpCode.CallBuiltin, 1, 0, 1, 1),
                new WebLingoInstruction(WebLingoOpCode.Return)
            ]));
        var output = new WebLingoCollectedOutput();
        var result = new WebLingoVirtualMachine().Execute(module, Context(output));
        Assert.Equal(WebLingoDiagnosticCodes.BytecodeVerificationFailure, result.Error!.Code);
        Assert.Empty(output.Lines);
    }

    [Fact]
    public void ABuiltinThatThrowsIsARuntimeErrorNotACrash()
    {
        var module = Compile("writeLn(1);");
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0)) { Services = new WebLingoBuiltinRegistry(new ThrowingPrint()) };
        var result = new WebLingoVirtualMachine().Execute(module, context);
        Assert.Equal(WebLingoDiagnosticCodes.InvalidOperation, result.Error!.Code);
        Assert.Contains("broken", result.Error.Message);
    }

    [Fact]
    public void AnEmptyProgramSucceeds()
    {
        var (result, lines) = Run("");
        Assert.True(result.Succeeded);
        Assert.Empty(lines);
    }

    private sealed class ThrowingPrint : IWebLingoBuiltinFunction
    {
        public string Name => "writeLn";
        public int Arity => 1;
        public bool TryInvoke(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            throw new InvalidOperationException("broken output");
    }

    private static readonly WebLingoCompilerOptions WithWrite = new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1)]);

    private static WebLingoCompiledModule Compile(string source)
    {
        var compiled = new WebLingoCompiler(WithWrite).Compile(source, "test.wl");
        Assert.Empty(compiled.Diagnostics);
        return compiled.Module!;
    }

    private static WebLingoExecutionContext Context(IWebLingoTextOutput output) =>
        new(new WebLingoWorkerId(0)) { Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output)) };

    private static (WebLingoExecutionResult Result, IReadOnlyList<string> Lines) Run(string source)
    {
        var output = new WebLingoCollectedOutput();
        var result = new WebLingoVirtualMachine().Execute(Compile(source), Context(output));
        return (result, output.Lines);
    }
}
