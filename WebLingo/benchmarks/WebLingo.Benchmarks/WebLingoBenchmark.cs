using System.Diagnostics;
using WebLingo.Compiler;
using WebLingo.Runtime;
using WebLingo.StdLib;
namespace WebLingo.Benchmarks;

/// <summary>
/// One workload measured on one VM: the median and the fastest of its timed runs, the instructions one run executes
/// (the dispatches), what one run allocates in .NET's heap, and its code's size.
/// </summary>
public sealed record WebLingoMeasurement(
    string Workload, string Engine, TimeSpan Median, TimeSpan Fastest, long Instructions, long AllocatedBytes,
    int CodeInstructions, long CodeBytes)
{
    /// <summary>Instructions executed per second, at the median time.</summary>
    public double InstructionsPerSecond => Median.TotalSeconds > 0 ? Instructions / Median.TotalSeconds : 0;
}

/// <summary>
/// Measures workloads with <see cref="Stopwatch"/> (specification, section 5.6): each is compiled once, run a few
/// times to warm up (so that .NET's tiered compiler has optimized the VM), then run and timed, with a collection of
/// .NET's heap before each timed run. Every run's output must be the workload's expected one, or the measurement
/// fails: a fast VM that computes the wrong result measures nothing.
/// </summary>
public static class WebLingoBenchmark
{
    private static readonly WebLingoCompilerOptions Options =
        new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1)]);

    public static WebLingoMeasurement Measure(IWebLingoBenchmarkEngine engine, WebLingoWorkload workload, int runs, int warmUps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(runs, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(warmUps);
        var program = engine.Compile(workload.Source, workload.Name + ".wl", Options);
        for (var i = 0; i < warmUps; i++) Run(engine, workload, program);

        var times = new TimeSpan[runs];
        var instructions = 0L;
        var allocated = 0L;
        for (var i = 0; i < runs; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            instructions = Run(engine, workload, program);
            watch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            times[i] = watch.Elapsed;
        }
        Array.Sort(times);
        return new WebLingoMeasurement(workload.Name, engine.Name, times[runs / 2], times[0], instructions, allocated,
            program.CodeInstructions, program.CodeBytes);
    }

    /// <summary>Runs the program once, checks its output, and returns how many instructions it executed.</summary>
    private static long Run(IWebLingoBenchmarkEngine engine, WebLingoWorkload workload, IWebLingoBenchmarkProgram program)
    {
        var output = new WebLingoCollectedOutput();
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
        {
            Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output))
        };
        var result = program.Run(context);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{workload.Name} failed on the {engine.Name} VM: {result.Error}");
        }
        if (output.Text != workload.ExpectedOutput)
        {
            throw new InvalidOperationException(
                $"{workload.Name} wrote \"{output.Text}\" on the {engine.Name} VM, not \"{workload.ExpectedOutput}\".");
        }
        return result.Instructions;
    }
}
