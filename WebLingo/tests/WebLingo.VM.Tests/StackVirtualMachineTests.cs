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
        var output = new WebLingoCollectedOutput();
        var module = Compile("var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);");
        var result = new WebLingoStackVirtualMachine().Execute(module, Context(output));
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new[] { "30" }, output.Lines);
        // a: LoadConstant, StoreLocal; b: the same; result: LoadLocal twice, Add, StoreLocal; writeLn: LoadLocal,
        // CallBuiltin, Pop; then Return.
        Assert.Equal(12, result.Instructions);
    }

    [Fact]
    public void AnInstructionBudgetStopsTheProgram()
    {
        var output = new WebLingoCollectedOutput();
        // Each writeLn is LoadConstant, CallBuiltin and Pop.
        var module = Compile("writeLn(1); writeLn(2); writeLn(3);");
        var result = new WebLingoStackVirtualMachine().Execute(module, Context(output) with { InstructionBudget = 6 });
        Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        Assert.Equal(new[] { "1", "2" }, output.Lines);
        Assert.Equal(6, result.Instructions);
    }

    [Fact]
    public void ASlotBudgetKeepsAModuleFromRunning()
    {
        var output = new WebLingoCollectedOutput();
        // Two locals and a stack of two: four slots.
        var module = Compile("var a = 1; var b = 2; writeLn(a + b);");
        var result = new WebLingoStackVirtualMachine().Execute(module, Context(output) with { Resources = new WebLingoResourceBudget(3) });
        Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        Assert.Contains("4 slots", result.Error.Message);
        Assert.Empty(output.Lines);
    }

    [Fact]
    public void CancellationStopsAnEndlessProgram()
    {
        var module = Compile("var x = 1; while (true) x = -x;");
        var signal = new WebLingoCancellationSignal();
        signal.Cancel();
        var cancelled = new WebLingoExecutionContext(new WebLingoWorkerId(1)) { Cancellation = signal };
        var result = new WebLingoStackVirtualMachine().Execute(module, cancelled);
        Assert.Equal(WebLingoDiagnosticCodes.Cancelled, result.Error!.Code);

        var limited = new WebLingoExecutionContext(new WebLingoWorkerId(1)) { InstructionBudget = 100_000 };
        var budgeted = new WebLingoStackVirtualMachine().Execute(module, limited);
        Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, budgeted.Error!.Code);
    }

    [Fact]
    public void GarbageStringsAreCollectedAsTheProgramRuns()
    {
        var output = new WebLingoCollectedOutput();
        var module = Compile(
            "var t = \"\"; for (var i = 0; i < 20000; i = i + 1) { t = \"abcdefghij\" + \"klmnopqrst\"; } writeLn(t);");
        var context = Context(output) with { Resources = new WebLingoResourceBudget(65535, MaxHeapUnits: 10_000) };
        var result = new WebLingoStackVirtualMachine().Execute(module, context);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new[] { "abcdefghijklmnopqrst" }, output.Lines);
    }

    [Fact]
    public void AStringGrowingPastTheBudgetStopsTheProgram()
    {
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
        {
            Resources = new WebLingoResourceBudget(65535, MaxHeapUnits: 100_000)
        };
        var result = new WebLingoStackVirtualMachine().Execute(Compile("var t = \"x\"; while (true) t = t + t;"), context);
        Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, result.Error!.Code);
    }

    [Fact]
    public void GarbageArraysAreCollectedOnBothVms()
    {
        const string source = "var t = []; for (var i = 0; i < 20000; i = i + 1) { t = [i, i, i, i]; } writeLn(t.length);";
        foreach (var run in BothVms)
        {
            var output = new WebLingoCollectedOutput();
            var result = run(source, Context(output) with { Resources = new WebLingoResourceBudget(65535, MaxHeapUnits: 10_000) });
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
                new WebLingoExecutionContext(new WebLingoWorkerId(0)) { Resources = new WebLingoResourceBudget(65535, MaxHeapUnits: 100_000) });
            Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        }
    }

    [Fact]
    public void GarbageMapsAreCollectedAndAGrowingOneIsBoundedOnBothVms()
    {
        const string garbage = "var t = {}; for (var i = 0; i < 20000; i = i + 1) { t = {a: i, b: [i], c: #{i}}; } writeLn(t[\"a\"]);";
        const string growing = "var m = {}; var s = #{}; var i = 0; while (true) { m[i] = i; s.add(i); i = i + 1; }";
        foreach (var run in BothVms)
        {
            var output = new WebLingoCollectedOutput();
            var result = run(garbage, Context(output) with { Resources = new WebLingoResourceBudget(65535, MaxHeapUnits: 10_000) });
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(new[] { "19999" }, output.Lines);

            var bounded = run(growing, Context(output) with { Resources = new WebLingoResourceBudget(65535, MaxHeapUnits: 100_000) });
            Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, bounded.Error!.Code);
        }
    }

    [Fact]
    public void ADeeplyNestedArrayIsWrittenWithoutOverflowingTheStack()
    {
        const string source = "var a = []; for (var i = 0; i < 100000; i = i + 1) { a = [a]; } write(a);";
        foreach (var run in BothVms)
        {
            var output = new WebLingoCollectedOutput();
            var result = run(source, Context(output));
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(200_002, output.Text.Length);
            Assert.StartsWith("[[[", output.Text);
        }
    }

    /// <summary>A program compiled for each VM and run on it.</summary>
    private static Func<string, WebLingoExecutionContext, WebLingoExecutionResult>[] BothVms =>
    [
        (source, context) => new WebLingoVirtualMachine().Execute(new WebLingoCompiler(WithWrite).Compile(source, "test.wl").Module!, context),
        (source, context) => new WebLingoStackVirtualMachine().Execute(Compile(source), context),
    ];

    /// <summary>A host's clock that only moves when the execution waits on it, recording each wait.</summary>
    private sealed class SteppedClock(Action? onWait = null) : IWebLingoExecutionClock
    {
        public long Milliseconds { get; private set; }
        public List<long> Waits { get; } = [];

        public void WaitUntil(long milliseconds)
        {
            Waits.Add(milliseconds);
            Milliseconds = milliseconds;
            onWait?.Invoke();
        }
    }

    [Fact]
    public void DelaysWaitOnTheHostsClock()
    {
        foreach (var vm in BothVms)
        {
            var output = new WebLingoCollectedOutput();
            var clock = new SteppedClock();
            var result = vm("writeLn(0); await delay(30); writeLn(1); var a = delay(10); var b = delay(5); await a; writeLn(2);",
                Context(output) with { Clock = clock });
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(new[] { "0", "1", "2" }, output.Lines);
            Assert.Equal(new long[] { 30, 35, 40 }, clock.Waits);
        }
    }

    [Fact]
    public void CancellationEndsAnExecutionWhoseTasksWait()
    {
        foreach (var vm in BothVms)
        {
            var output = new WebLingoCollectedOutput();
            var signal = new WebLingoCancellationSignal();
            var context = Context(output) with { Clock = new SteppedClock(signal.Cancel), Cancellation = signal };
            var result = vm("writeLn(0); await delay(5); writeLn(1);", context);
            Assert.Equal(WebLingoDiagnosticCodes.Cancelled, result.Error!.Code);
            Assert.Equal(new[] { "0" }, output.Lines);
        }
    }

    [Fact]
    public void WaitingFramesKeepTheirValuesThroughCollections()
    {
        const string source =
            "async function keep(tag) { var mine = tag + \"!\"; var held = [mine, {k: mine}]; await delay(10); " +
            "return held[0] + held[1][\"k\"] + mine; } " +
            "async function churn() { var s = \"\"; for (var i = 0; i < 40; i = i + 1) { " +
            "for (var j = 0; j < 500; j = j + 1) s = \"abcdefghij\" + \"klmnopqrst\"; await null; } return s.length; } " +
            "var k = keep(\"a\"); var c = churn(); writeLn(await c); writeLn(await k);";
        foreach (var vm in BothVms)
        {
            var output = new WebLingoCollectedOutput();
            var context = Context(output) with { Resources = new WebLingoResourceBudget(65535, MaxHeapUnits: 20_000) };
            var result = vm(source, context);
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(new[] { "20", "a!a!a!" }, output.Lines);
        }
    }

    [Fact]
    public void AWaitingDisposalKeepsItsInstanceThroughCollections()
    {
        const string source =
            "class R { constructor(tag) { this.tag = tag + \"!\"; } async dispose() { await delay(10); writeLn(this.tag); } } " +
            "async function churn() { var s = \"\"; for (var i = 0; i < 40; i = i + 1) { " +
            "for (var j = 0; j < 500; j = j + 1) s = \"abcdefghij\" + \"klmnopqrst\"; await null; } return s.length; } " +
            "async function use() { using (var r = new R(\"a\")) {} } " +
            "var u = use(); var c = churn(); writeLn(await c); await u;";
        foreach (var vm in BothVms)
        {
            var output = new WebLingoCollectedOutput();
            var context = Context(output) with { Resources = new WebLingoResourceBudget(65535, MaxHeapUnits: 20_000) };
            var result = vm(source, context);
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(new[] { "20", "a!" }, output.Lines);
        }
    }

    [Fact]
    public void ManyTasksWaitAtOnce()
    {
        const string source =
            "var total = 0; async function add(n) { await delay(n); total = total + n; } var tasks = []; " +
            "for (var i = 0; i < 2000; i = i + 1) tasks.push(add(i)); foreach (var t in tasks) await t; writeLn(total);";
        foreach (var vm in BothVms)
        {
            var output = new WebLingoCollectedOutput();
            var result = vm(source, Context(output));
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(new[] { "1999000" }, output.Lines);
        }
    }

    [Fact]
    public void AnAwaitInAFunctionThatIsNotAsyncIsNotRun()
    {
        var module = new WebLingoStackModule("await", [], [],
            new WebLingoStackFunction("main", 0, 1, [
                new WebLingoStackInstruction(WebLingoStackOpCode.LoadNull), new WebLingoStackInstruction(WebLingoStackOpCode.Await),
                new WebLingoStackInstruction(WebLingoStackOpCode.ReturnValue)
            ]));
        var result = new WebLingoStackVirtualMachine().Execute(module, new WebLingoExecutionContext(new WebLingoWorkerId(0)));
        Assert.Equal(WebLingoDiagnosticCodes.BytecodeVerificationFailure, result.Error!.Code);
        Assert.Contains("not async", result.Error.Message);
        var accepted = new WebLingoStackVirtualMachine().Execute(module with { EntryPoint = module.EntryPoint with { IsAsync = true } },
            new WebLingoExecutionContext(new WebLingoWorkerId(0)));
        Assert.True(accepted.Succeeded, accepted.Error?.Message);
    }

    [Fact]
    public void AModuleThatFailsVerificationIsNotRun()
    {
        // writeLn with nothing on the stack to give it.
        var module = new WebLingoStackModule("bad", [], [new WebLingoImport("writeLn", 1)],
            new WebLingoStackFunction("main", 0, 1, [
                new WebLingoStackInstruction(WebLingoStackOpCode.CallBuiltin, 0, 1), new WebLingoStackInstruction(WebLingoStackOpCode.Return)
            ]));
        var output = new WebLingoCollectedOutput();
        var result = new WebLingoStackVirtualMachine().Execute(module, Context(output));
        Assert.Equal(WebLingoDiagnosticCodes.BytecodeVerificationFailure, result.Error!.Code);
        Assert.Empty(output.Lines);
    }

    [Fact]
    public void AnImportTheHostDoesNotOfferIsNotRun()
    {
        var result = new WebLingoStackVirtualMachine().Execute(Compile("writeLn(1);"), new WebLingoExecutionContext(new WebLingoWorkerId(0)));
        Assert.Equal(WebLingoDiagnosticCodes.UndefinedIdentifier, result.Error!.Code);
    }

    [Fact]
    public void ALocalOfTheWrongKindIsAnInvalidModuleNotACrash()
    {
        // The verifier cannot tell that local 0 holds an integer, not a cell: the VM finds it out, and reports it.
        var module = new WebLingoStackModule("kind", [WebLingoConstant.Int64(1)], [],
            new WebLingoStackFunction("main", 1, 1, [
                new WebLingoStackInstruction(WebLingoStackOpCode.LoadConstant, 0), new WebLingoStackInstruction(WebLingoStackOpCode.StoreLocal, 0),
                new WebLingoStackInstruction(WebLingoStackOpCode.LoadCell, 0), new WebLingoStackInstruction(WebLingoStackOpCode.ReturnValue)
            ]));
        var result = new WebLingoStackVirtualMachine().Execute(module, new WebLingoExecutionContext(new WebLingoWorkerId(0)));
        Assert.Equal(WebLingoDiagnosticCodes.BytecodeVerificationFailure, result.Error!.Code);
    }

    [Fact]
    public void ABuiltinThatThrowsIsARuntimeErrorNotACrash()
    {
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0)) { Services = new WebLingoBuiltinRegistry(new ThrowingWriteLn()) };
        var result = new WebLingoStackVirtualMachine().Execute(Compile("writeLn(1);"), context);
        Assert.Equal(WebLingoDiagnosticCodes.InvalidOperation, result.Error!.Code);
        Assert.Contains("broken", result.Error.Message);
    }

    [Fact]
    public void DeepRecursionIsBoundedByTheCallDepth()
    {
        var output = new WebLingoCollectedOutput();
        var module = Compile("function deep(n) { if (n == 0) return 0; return 1 + deep(n - 1); } writeLn(deep(50));");
        var context = Context(output) with { Resources = new WebLingoResourceBudget(65535, MaxCallDepth: 20) };
        var result = new WebLingoStackVirtualMachine().Execute(module, context);
        Assert.Equal(WebLingoDiagnosticCodes.BudgetExceeded, result.Error!.Code);
        Assert.Contains("nest more than 20 deep", result.Error.Message);
    }

    private sealed class ThrowingWriteLn : IWebLingoBuiltinFunction
    {
        public string Name => "writeLn";
        public int Arity => 1;
        public bool TryInvoke(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            throw new InvalidOperationException("broken output");
    }

    private static readonly WebLingoCompilerOptions WithWrite =
        new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1), new WebLingoBuiltinDeclaration("delay", 1)]);

    private static WebLingoStackModule Compile(string source)
    {
        var compiled = new WebLingoStackCompiler(WithWrite).Compile(source, "test.wl");
        Assert.Empty(compiled.Diagnostics);
        return compiled.Module!;
    }

    private static WebLingoExecutionContext Context(IWebLingoTextOutput output) =>
        new(new WebLingoWorkerId(0)) { Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output)) };
}
