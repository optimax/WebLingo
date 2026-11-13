using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM.Stack;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// The stack VM's own behavior: its budgets, cancellation, the heap's collector, and the modules it refuses to run.
/// That it gives every program what the register VM gives is <c>StackVmTests</c>' (<c>WebLingo.Conformance.Tests</c>).
/// </summary>
public sealed class StackVirtualMachineTests
{
    [Fact]
    public void RunsTheSpecificationsExampleAndPrints30()
    {
        var output = new WsCollectedOutput();
        var module = Compile("var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);");
        var result = new WsStackVirtualMachine().Execute(module, Context(output));
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new[] { "30" }, output.Lines);
        // a: LoadConstant, StoreLocal; b: the same; result: LoadLocal twice, Add, StoreLocal; writeLn: LoadLocal,
        // CallBuiltin, Pop; then Return.
        Assert.Equal(12, result.Instructions);
    }

    [Fact]
    public void AnInstructionBudgetStopsTheProgram()
    {
        var output = new WsCollectedOutput();
        // Each writeLn is LoadConstant, CallBuiltin and Pop.
        var module = Compile("writeLn(1); writeLn(2); writeLn(3);");
        var result = new WsStackVirtualMachine().Execute(module, Context(output) with { InstructionBudget = 6 });
        Assert.Equal(WsDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        Assert.Equal(new[] { "1", "2" }, output.Lines);
        Assert.Equal(6, result.Instructions);
    }

    [Fact]
    public void ASlotBudgetKeepsAModuleFromRunning()
    {
        var output = new WsCollectedOutput();
        // Two locals and a stack of two: four slots.
        var module = Compile("var a = 1; var b = 2; writeLn(a + b);");
        var result = new WsStackVirtualMachine().Execute(module, Context(output) with { Resources = new WsResourceBudget(3) });
        Assert.Equal(WsDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        Assert.Contains("4 slots", result.Error.Message);
        Assert.Empty(output.Lines);
    }

    [Fact]
    public void CancellationStopsAnEndlessProgram()
    {
        var module = Compile("var x = 1; while (true) x = -x;");
        var signal = new WsCancellationSignal();
        signal.Cancel();
        var cancelled = new WsExecutionContext(new WsWorkerId(1)) { Cancellation = signal };
        var result = new WsStackVirtualMachine().Execute(module, cancelled);
        Assert.Equal(WsDiagnosticCodes.Cancelled, result.Error!.Code);

        var limited = new WsExecutionContext(new WsWorkerId(1)) { InstructionBudget = 100_000 };
        var budgeted = new WsStackVirtualMachine().Execute(module, limited);
        Assert.Equal(WsDiagnosticCodes.BudgetExceeded, budgeted.Error!.Code);
    }

    [Fact]
    public void GarbageStringsAreCollectedAsTheProgramRuns()
    {
        var output = new WsCollectedOutput();
        var module = Compile(
            "var t = \"\"; for (var i = 0; i < 20000; i = i + 1) { t = \"abcdefghij\" + \"klmnopqrst\"; } writeLn(t);");
        var context = Context(output) with { Resources = new WsResourceBudget(65535, MaxHeapUnits: 10_000) };
        var result = new WsStackVirtualMachine().Execute(module, context);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new[] { "abcdefghijklmnopqrst" }, output.Lines);
    }

    [Fact]
    public void AStringGrowingPastTheBudgetStopsTheProgram()
    {
        var context = new WsExecutionContext(new WsWorkerId(0))
        {
            Resources = new WsResourceBudget(65535, MaxHeapUnits: 100_000)
        };
        var result = new WsStackVirtualMachine().Execute(Compile("var t = \"x\"; while (true) t = t + t;"), context);
        Assert.Equal(WsDiagnosticCodes.BudgetExceeded, result.Error!.Code);
    }

    [Fact]
    public void GarbageArraysAreCollectedOnBothVms()
    {
        const string source = "var t = []; for (var i = 0; i < 20000; i = i + 1) { t = [i, i, i, i]; } writeLn(t.length);";
        foreach (var run in BothVms)
        {
            var output = new WsCollectedOutput();
            var result = run(source, Context(output) with { Resources = new WsResourceBudget(65535, MaxHeapUnits: 10_000) });
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(new[] { "4" }, output.Lines);
        }
    }

    [Fact]
    public void AnArrayGrowingPastTheBudgetStopsTheProgramOnBothVms()
    {
        foreach (var run in BothVms)
        {
            var result = run("var a = []; while (true) a.push(1);",
                new WsExecutionContext(new WsWorkerId(0)) { Resources = new WsResourceBudget(65535, MaxHeapUnits: 100_000) });
            Assert.Equal(WsDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        }
    }

    [Fact]
    public void GarbageMapsAreCollectedAndAGrowingOneIsBoundedOnBothVms()
    {
        const string garbage = "var t = {}; for (var i = 0; i < 20000; i = i + 1) { t = {a: i, b: [i]}; } writeLn(t[\"a\"]);";
        const string growing = "var m = {}; var i = 0; while (true) { m[i] = i; i = i + 1; }";
        foreach (var run in BothVms)
        {
            var output = new WsCollectedOutput();
            var result = run(garbage, Context(output) with { Resources = new WsResourceBudget(65535, MaxHeapUnits: 10_000) });
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(new[] { "19999" }, output.Lines);

            var bounded = run(growing, Context(output) with { Resources = new WsResourceBudget(65535, MaxHeapUnits: 100_000) });
            Assert.Equal(WsDiagnosticCodes.BudgetExceeded, bounded.Error!.Code);
        }
    }

    [Fact]
    public void ADeeplyNestedArrayIsWrittenWithoutOverflowingTheStack()
    {
        const string source = "var a = []; for (var i = 0; i < 100000; i = i + 1) { a = [a]; } write(a);";
        foreach (var run in BothVms)
        {
            var output = new WsCollectedOutput();
            var result = run(source, Context(output));
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(200_002, output.Text.Length);
            Assert.StartsWith("[[[", output.Text);
        }
    }

    /// <summary>A program compiled for each VM and run on it.</summary>
    private static Func<string, WsExecutionContext, WsExecutionResult>[] BothVms =>
    [
        (source, context) => new WsVirtualMachine().Execute(new WsCompiler(WithWrite).Compile(source, "test.ws").Module!, context),
        (source, context) => new WsStackVirtualMachine().Execute(Compile(source), context),
    ];

    [Fact]
    public void AModuleThatFailsVerificationIsNotRun()
    {
        // writeLn with nothing on the stack to give it.
        var module = new WsStackModule("bad", [], [new WsImport("writeLn", 1)],
            new WsStackFunction("main", 0, 1, [
                new WsStackInstruction(WsStackOpCode.CallBuiltin, 0, 1), new WsStackInstruction(WsStackOpCode.Return)
            ]));
        var output = new WsCollectedOutput();
        var result = new WsStackVirtualMachine().Execute(module, Context(output));
        Assert.Equal(WsDiagnosticCodes.BytecodeVerificationFailure, result.Error!.Code);
        Assert.Empty(output.Lines);
    }

    [Fact]
    public void AnImportTheHostDoesNotOfferIsNotRun()
    {
        var result = new WsStackVirtualMachine().Execute(Compile("writeLn(1);"), new WsExecutionContext(new WsWorkerId(0)));
        Assert.Equal(WsDiagnosticCodes.UndefinedIdentifier, result.Error!.Code);
    }

    [Fact]
    public void ALocalOfTheWrongKindIsAnInvalidModuleNotACrash()
    {
        // The verifier cannot tell that local 0 holds an integer, not a cell: the VM finds it out, and reports it.
        var module = new WsStackModule("kind", [WsConstant.Int64(1)], [],
            new WsStackFunction("main", 1, 1, [
                new WsStackInstruction(WsStackOpCode.LoadConstant, 0), new WsStackInstruction(WsStackOpCode.StoreLocal, 0),
                new WsStackInstruction(WsStackOpCode.LoadCell, 0), new WsStackInstruction(WsStackOpCode.ReturnValue)
            ]));
        var result = new WsStackVirtualMachine().Execute(module, new WsExecutionContext(new WsWorkerId(0)));
        Assert.Equal(WsDiagnosticCodes.BytecodeVerificationFailure, result.Error!.Code);
    }

    [Fact]
    public void ABuiltinThatThrowsIsARuntimeErrorNotACrash()
    {
        var context = new WsExecutionContext(new WsWorkerId(0)) { Services = new WsBuiltinRegistry(new ThrowingWriteLn()) };
        var result = new WsStackVirtualMachine().Execute(Compile("writeLn(1);"), context);
        Assert.Equal(WsDiagnosticCodes.InvalidOperation, result.Error!.Code);
        Assert.Contains("broken", result.Error.Message);
    }

    [Fact]
    public void DeepRecursionIsBoundedByTheCallDepth()
    {
        var output = new WsCollectedOutput();
        var module = Compile("function deep(n) { if (n == 0) return 0; return 1 + deep(n - 1); } writeLn(deep(50));");
        var context = Context(output) with { Resources = new WsResourceBudget(65535, MaxCallDepth: 20) };
        var result = new WsStackVirtualMachine().Execute(module, context);
        Assert.Equal(WsDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        Assert.Contains("nest more than 20 deep", result.Error.Message);
    }

    private sealed class ThrowingWriteLn : IWsBuiltinFunction
    {
        public string Name => "writeLn";
        public int Arity => 1;
        public bool TryInvoke(WsValue[] arguments, WsHeap heap, out WsValue result, out WsRuntimeError? error) =>
            throw new InvalidOperationException("broken output");
    }

    private static readonly WsCompilerOptions WithWrite =
        new([new WsBuiltinDeclaration("write", 1), new WsBuiltinDeclaration("writeLn", 1)]);

    private static WsStackModule Compile(string source)
    {
        var compiled = new WsStackCompiler(WithWrite).Compile(source, "test.ws");
        Assert.Empty(compiled.Diagnostics);
        return compiled.Module!;
    }

    private static WsExecutionContext Context(IWsTextOutput output) =>
        new(new WsWorkerId(0)) { Services = new WsBuiltinRegistry(WsStandardBuiltins.Create(output)) };
}
