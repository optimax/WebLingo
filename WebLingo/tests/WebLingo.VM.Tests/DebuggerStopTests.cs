using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// A debugger's stop that waits as at an <c>await</c> (specification, section 5.7, "Debugging WASP pages, parts", part 2): a
/// debugger whose <c>Stopped</c> gives false leaves its execution waiting, its frames on the VM's stack, and the execution
/// goes on where it stopped once it is resumed and the debugger no longer holds it, the point it stopped at not stopped at
/// again.
/// </summary>
public sealed class DebuggerStopTests
{
    private const string Program = "import { writeLn } from \"std:system\";\n"
                                   + "function twice(x) {\n"
                                   + "    var y = x * 2;\n"
                                   + "    return y + 1;\n"
                                   + "}\n"
                                   + "writeLn(\"before\");\n"
                                   + "writeLn(twice(20));\n"
                                   + "writeLn(\"after\");\n";

    [Fact]
    public void AStopThatWaitsGoesOnWhereItStoppedOnceResumed()
    {
        var debugger = new Holding(Program.IndexOf("return y", StringComparison.Ordinal));
        var output = new WebLingoCollectedOutput();
        var vm = new WebLingoVirtualMachine { Debugger = debugger };
        var result = vm.Execute(Compile(), Context(output));
        Assert.Equal((true, -1L, "before\n"), (result.Waiting, result.WaitMilliseconds, output.Text));
        Assert.Equal(2, debugger.Frames.Count);
        Assert.Equal("twice", debugger.Frames[0].Name);
        Assert.Contains(debugger.Frames[0].Variables, variable => variable is { Name: "y", Value: "40" });

        // Resumed while its debugger holds it, it waits still, where it stopped.
        result = vm.Resume();
        Assert.Equal((true, -1L, "before\n"), (result.Waiting, result.WaitMilliseconds, output.Text));
        debugger.Holds = false;
        result = vm.Resume();
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal("before\n41\nafter\n", output.Text);
        Assert.Equal(1, debugger.Stops);
    }

    [Fact]
    public void AStoppedExecutionCancelledEndsWhenResumed()
    {
        var debugger = new Holding(Program.IndexOf("return y", StringComparison.Ordinal));
        var cancellation = new WebLingoCancellationSignal();
        var vm = new WebLingoVirtualMachine { Debugger = debugger };
        Assert.True(vm.Execute(Compile(), Context(new WebLingoCollectedOutput()) with { Cancellation = cancellation }).Waiting);
        cancellation.Cancel();
        var result = vm.Resume();
        Assert.Equal("WL3004", result.Error?.Code);
        Assert.Throws<InvalidOperationException>(() => vm.Resume());
    }

    private static WebLingoCompiledModule Compile()
    {
        var options = new WebLingoCompilerOptions([new("write", 1), new("writeLn", 1)]) { Debugging = true };
        var compiled = new WebLingoCompiler(options).Compile(Program, "main.wlg");
        Assert.Empty(compiled.Diagnostics);
        return compiled.Module!;
    }

    private static WebLingoExecutionContext Context(WebLingoCollectedOutput output) =>
        new(new WebLingoWorkerId(0))
        {
            Services = new WebLingoBuiltinRegistry([.. WebLingoStandardBuiltins.Create(output)]),
            Suspends = true
        };

    /// <summary>A debugger that stops at every visit of the statement at <paramref name="start"/>, and holds it there while <see cref="Holds"/>.</summary>
    private sealed class Holding(int start) : IWebLingoRegisterDebugger
    {
        public bool Holds { get; set; } = true;
        public int Stops { get; private set; }
        public IReadOnlyList<WebLingoDebugFrame> Frames { get; private set; } = [];

        public void ModuleLoaded(WebLingoCompiledModule module) { }

        public bool ShouldStop(WebLingoDebugPoint point) => point.Span.Start == start;

        public bool Stopped(IReadOnlyList<WebLingoDebugFrame> frames)
        {
            (Frames, Stops) = (frames, Stops + 1);
            return false;
        }

        public bool Waits => Holds;
    }
}
