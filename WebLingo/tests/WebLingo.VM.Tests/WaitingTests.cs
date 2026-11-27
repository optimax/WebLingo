using WebLingo.Compiler;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// Executions that wait without a thread (specification, section 6.6; section 9, "M0.14.7 parts", part 2), on both VMs:
/// with <see cref="WebLingoExecutionContext.Suspends"/>, an execution that has nothing to run but waits for work off its
/// worker, or for a timer's time, returns to its host, waiting, and goes on where it was when resumed.
/// </summary>
public sealed class WaitingTests
{
    [Fact]
    public void AnExecutionWaitingForATimerReturnsAndGoesOnWhenResumed()
    {
        const string program = "import { write, writeLn, delay } from \"std:system\"; var t = delay(1000); writeLn(\"before\"); await t; writeLn(\"after\"); await delay(500); writeLn(\"end\");";
        foreach (var stack in new[] { false })
        {
            var clock = new ManualClock();
            var output = new WebLingoCollectedOutput();
            var vm = Vm(stack);
            var result = vm.Execute(Compile(program, stack), Context(output, clock, null));
            Assert.Equal((true, 1000L, "before\n"), (result.Waiting, result.WaitMilliseconds, output.Text));
            clock.Milliseconds = 1000;
            result = vm.Resume();
            Assert.Equal((true, 500L, "before\nafter\n"), (result.Waiting, result.WaitMilliseconds, output.Text));
            clock.Milliseconds = 1500;
            result = vm.Resume();
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal("before\nafter\nend\n", output.Text);
            Assert.Equal(0, clock.Waits);
        }
    }

    [Fact]
    public void AnExecutionWaitingForWorkOffItsWorkerReturnsUntilTheWorkIsPosted()
    {
        const string program = "import { write, writeLn } from \"std:system\"; writeLn(await offWorker(20)); writeLn(await offWorker(22));";
        foreach (var stack in new[] { false })
        {
            var host = new QueuedHost();
            var output = new WebLingoCollectedOutput();
            var vm = Vm(stack);
            var result = vm.Execute(Compile(program, stack), Context(output, null, host));
            Assert.Equal((true, -1L, ""), (result.Waiting, result.WaitMilliseconds, output.Text));
            // Resumed before the work is done, it waits again; once the work is posted back, it goes on.
            Assert.True(vm.Resume().Waiting);
            host.RunBlockingWork();
            result = vm.Resume();
            Assert.Equal((true, "40\n"), (result.Waiting, output.Text));
            host.RunBlockingWork();
            result = vm.Resume();
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal("40\n44\n", output.Text);
        }
    }

    [Fact]
    public void WithoutSuspendsTheVmWaitsItself()
    {
        foreach (var stack in new[] { false })
        {
            var clock = new ManualClock();
            var output = new WebLingoCollectedOutput();
            var context = Context(output, clock, null) with { Suspends = false };
            var result = Vm(stack).Execute(Compile("import { write, writeLn, delay } from \"std:system\"; await delay(1000); writeLn(\"after\");", stack), context);
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal((1, "after\n"), (clock.Waits, output.Text));
        }
    }

    [Fact]
    public void ACancelledExecutionEndsWhenResumed()
    {
        foreach (var stack in new[] { false })
        {
            var cancellation = new WebLingoCancellationSignal();
            var vm = Vm(stack);
            var context = Context(new WebLingoCollectedOutput(), new ManualClock(), null) with { Cancellation = cancellation };
            Assert.True(vm.Execute(Compile("import { delay } from \"std:system\"; await delay(1000);", stack), context).Waiting);
            cancellation.Cancel();
            var result = vm.Resume();
            Assert.Equal("WL3004", result.Error?.Code);
            Assert.Throws<InvalidOperationException>(() => vm.Resume());
        }
    }

    private static IRunner Vm(bool stack) => new RegisterRunner();

    private static object Compile(string program, bool stack)
    {
        var options = new WebLingoCompilerOptions([new("write", 1), new("writeLn", 1), new("delay", 1), new("offWorker", 1)]);
        var compiled = new WebLingoCompiler(options).Compile(program, "main.wlg");
        Assert.Empty(compiled.Diagnostics);
        return compiled.Module!;
    }

    private static WebLingoExecutionContext Context(WebLingoCollectedOutput output, IWebLingoExecutionClock? clock, IWebLingoSchedulerHost? host) =>
        new(new WebLingoWorkerId(0))
        {
            Services = new WebLingoBuiltinRegistry([.. WebLingoStandardBuiltins.Create(output), new OffWorker()]),
            Clock = clock,
            SchedulerHost = host,
            Suspends = true
        };

    /// <summary>A VM either kind, the module the one compiled for it.</summary>
    private interface IRunner
    {
        WebLingoExecutionResult Execute(object module, WebLingoExecutionContext context);
        WebLingoExecutionResult Resume();
    }

    private sealed class RegisterRunner : IRunner
    {
        private readonly WebLingoVirtualMachine _vm = new();
        public WebLingoExecutionResult Execute(object module, WebLingoExecutionContext context) => _vm.Execute((Bytecode.WebLingoCompiledModule)module, context);
        public WebLingoExecutionResult Resume() => _vm.Resume();
    }

    /// <summary>A clock whose time the test sets, counting the waits a VM asks of it.</summary>
    private sealed class ManualClock : IWebLingoExecutionClock
    {
        public long Milliseconds { get; set; }
        public int Waits { get; private set; }

        public void WaitUntil(long milliseconds)
        {
            Waits++;
            Milliseconds = Math.Max(Milliseconds, milliseconds);
        }
    }

    /// <summary>A scheduler host whose blocking work the test runs, and whose posted work waits for the worker.</summary>
    private sealed class QueuedHost : IWebLingoSchedulerHost
    {
        private readonly Queue<Action> _blocking = new();
        private readonly Queue<Action> _posted = new();

        public void RunBlocking(Action work, bool opensConnection) => _blocking.Enqueue(work);
        public void Post(Action work) => _posted.Enqueue(work);

        public bool RunPosted(int milliseconds)
        {
            var ran = _posted.Count > 0;
            while (_posted.Count > 0) _posted.Dequeue()();
            return ran;
        }

        public void RunBlockingWork()
        {
            while (_blocking.Count > 0) _blocking.Dequeue()();
        }
    }

    /// <summary><c>offWorker(n)</c>: a task that completes with twice <c>n</c>, its work run off the worker.</summary>
    private sealed class OffWorker : IWebLingoBuiltinFunction
    {
        public string Name => "offWorker";
        public int Arity => 1;

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
        {
            var n = arguments[0].AsInt64;
            return heap.Scheduler!.TryRunOffWorker(() => 2 * n, (long value, WebLingoHeap _, out WebLingoValue made, out WebLingoRuntimeError? failed) =>
            {
                (made, failed) = (WebLingoValue.FromInt64(value), null);
                return true;
            }, _ => null, out result, out error);
        }
    }
}
