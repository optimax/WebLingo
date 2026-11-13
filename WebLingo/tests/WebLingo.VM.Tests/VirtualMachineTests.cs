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
        Assert.Equal(WsDiagnosticCodes.IntegerOverflow, result.Error!.Code);
        Assert.Equal(source.IndexOf("big + 1", StringComparison.Ordinal), result.Error.Span.Start);
    }

    [Fact]
    public void AnInstructionBudgetStopsTheProgram()
    {
        var module = Compile("writeLn(1); writeLn(2); writeLn(3);");
        var output = new WsCollectedOutput();
        var result = new WsVirtualMachine().Execute(module, Context(output) with { InstructionBudget = 4 });
        Assert.Equal(WsDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        Assert.Equal(new[] { "1", "2" }, output.Lines);
    }

    [Fact]
    public void ARegisterBudgetKeepsAModuleFromRunning()
    {
        var module = Compile("var a = 1; var b = 2; writeLn(a + b);");
        var output = new WsCollectedOutput();
        var result = new WsVirtualMachine().Execute(module, Context(output) with { Resources = new WsResourceBudget(2) });
        Assert.Equal(WsDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        Assert.Empty(output.Lines);
    }

    [Fact]
    public void CancellationStopsAnEndlessProgram()
    {
        // An endless loop, which the compiler cannot write yet: 0: r0 = 1; 1: r0 = -r0; 2: jump 1.
        var module = new WsCompiledModule("loop", [WsConstant.Int64(1)], [],
            new WsFunction("main", 1, 0, [
                new WsInstruction(WsOpCode.LoadConstant, 0, 0), new WsInstruction(WsOpCode.Negate, 0, 0),
                new WsInstruction(WsOpCode.Jump, 1)
            ]));
        var signal = new WsCancellationSignal();
        signal.Cancel();
        var result = new WsVirtualMachine().Execute(module, new WsExecutionContext(new WsWorkerId(1)) { Cancellation = signal });
        Assert.Equal(WsDiagnosticCodes.Cancelled, result.Error!.Code);

        var budgeted = new WsVirtualMachine().Execute(module, new WsExecutionContext(new WsWorkerId(1)) { InstructionBudget = 100_000 });
        Assert.Equal(WsDiagnosticCodes.BudgetExceeded, budgeted.Error!.Code);
    }

    [Fact]
    public void AModuleThatFailsVerificationIsNotRun()
    {
        var module = new WsCompiledModule("bad", [], [new WsImport("writeLn", 1)],
            new WsFunction("main", 1, 0, [new WsInstruction(WsOpCode.CallBuiltin, 0, 0, 0, 1), new WsInstruction(WsOpCode.Return)]));
        var output = new WsCollectedOutput();
        var result = new WsVirtualMachine().Execute(module, Context(output));
        Assert.Equal(WsDiagnosticCodes.BytecodeVerificationFailure, result.Error!.Code);
        Assert.Empty(output.Lines);
    }

    [Fact]
    public void AnImportTheHostDoesNotOfferIsNotRun()
    {
        var module = Compile("writeLn(1);");
        var result = new WsVirtualMachine().Execute(module, new WsExecutionContext(new WsWorkerId(0)));
        Assert.Equal(WsDiagnosticCodes.UndefinedIdentifier, result.Error!.Code);
    }

    [Fact]
    public void AModuleReadingARegisterBeforeWritingItIsNotRun()
    {
        // 0: r1 = r0; 1: writeLn(r1). r0 is never written: the verifier rejects the module, so print never sees it.
        var module = new WsCompiledModule("leak", [], [new WsImport("writeLn", 1)],
            new WsFunction("main", 2, 1, [
                new WsInstruction(WsOpCode.Move, 1, 0), new WsInstruction(WsOpCode.CallBuiltin, 1, 0, 1, 1),
                new WsInstruction(WsOpCode.Return)
            ]));
        var output = new WsCollectedOutput();
        var result = new WsVirtualMachine().Execute(module, Context(output));
        Assert.Equal(WsDiagnosticCodes.BytecodeVerificationFailure, result.Error!.Code);
        Assert.Empty(output.Lines);
    }

    [Fact]
    public void ABuiltinThatThrowsIsARuntimeErrorNotACrash()
    {
        var module = Compile("writeLn(1);");
        var context = new WsExecutionContext(new WsWorkerId(0)) { Services = new WsBuiltinRegistry(new ThrowingPrint()) };
        var result = new WsVirtualMachine().Execute(module, context);
        Assert.Equal(WsDiagnosticCodes.InvalidOperation, result.Error!.Code);
        Assert.Contains("broken", result.Error.Message);
    }

    [Fact]
    public void AnEmptyProgramSucceeds()
    {
        var (result, lines) = Run("");
        Assert.True(result.Succeeded);
        Assert.Empty(lines);
    }

    private sealed class ThrowingPrint : IWsBuiltinFunction
    {
        public string Name => "writeLn";
        public int Arity => 1;
        public bool TryInvoke(WsValue[] arguments, out WsValue result, out WsRuntimeError? error) =>
            throw new InvalidOperationException("broken output");
    }

    private static readonly WsCompilerOptions WithWrite = new([new WsBuiltinDeclaration("write", 1), new WsBuiltinDeclaration("writeLn", 1)]);

    private static WsCompiledModule Compile(string source)
    {
        var compiled = new WsCompiler(WithWrite).Compile(source, "test.ws");
        Assert.Empty(compiled.Diagnostics);
        return compiled.Module!;
    }

    private static WsExecutionContext Context(IWsTextOutput output) =>
        new(new WsWorkerId(0)) { Services = new WsBuiltinRegistry(WsStandardBuiltins.Create(output)) };

    private static (WsExecutionResult Result, IReadOnlyList<string> Lines) Run(string source)
    {
        var output = new WsCollectedOutput();
        var result = new WsVirtualMachine().Execute(Compile(source), Context(output));
        return (result, output.Lines);
    }
}
