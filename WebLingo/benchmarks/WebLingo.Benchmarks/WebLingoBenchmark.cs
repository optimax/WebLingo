using System.Diagnostics;
using WebLingo.Cli;
using WebLingo.Compiler;
using WebLingo.Platform.DotNet;
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
    /// <summary>The standard modules' built-ins, which their loader registers as it loads them (section 3.9).</summary>
    private static readonly WebLingoBuiltinRegistry NativeBuiltins = new();

    /// <summary>
    /// The workloads' built-ins, and the standard modules installed beside the benchmark in std/, as beside the command
    /// lines, <c>std:fs</c> among them (section 3.9).
    /// </summary>
    private static readonly WebLingoCompilerOptions Options =
        new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1)])
        {
            Native = new WebLingoNativeModuleLoader(new Dictionary<string, string> { ["std"] = Path.Combine(AppContext.BaseDirectory, "std") },
                NativeBuiltins)
        };

    public static WebLingoMeasurement Measure(IWebLingoBenchmarkEngine engine, WebLingoWorkload workload, int runs, int warmUps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(runs, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(warmUps);
        var program = engine.Compile(workload.Source, workload.FileName, Options);
        // A workload's files, in a folder of its own, which its runs write anew, removed at the end.
        var folder = workload.UsesFolder ? Path.Combine(Path.GetTempPath(), "weblingo-benchmark-" + Guid.NewGuid().ToString("N")) : null;
        if (folder is not null) Directory.CreateDirectory(folder);
        try
        {
            for (var i = 0; i < warmUps; i++) Run(engine, workload, program, folder);

            var times = new TimeSpan[runs];
            var instructions = 0L;
            var allocated = 0L;
            for (var i = 0; i < runs; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var before = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                instructions = Run(engine, workload, program, folder);
                watch.Stop();
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                times[i] = watch.Elapsed;
            }
            Array.Sort(times);
            return new WebLingoMeasurement(workload.Name, engine.Name, times[runs / 2], times[0], instructions, allocated,
                program.CodeInstructions, program.CodeBytes);
        }
        finally
        {
            if (folder is not null) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// Runs the program once, checks its output, and returns how many instructions it executed. Allocated is the worker's
    /// own: what the host's threads allocate, an I/O workload's files read and written, is not counted.
    /// </summary>
    private static long Run(IWebLingoBenchmarkEngine engine, WebLingoWorkload workload, IWebLingoBenchmarkProgram program, string? folder)
    {
        var output = new WebLingoCollectedOutput();
        // The built-ins a page calls besides (section 13), which a script does not.
        var services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output));
        foreach (var builtin in WebLingoTemplateBuiltins.Create(output)) services.Register(builtin);
        foreach (var builtin in NativeBuiltins.Builtins) services.Register(builtin);
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0)) { Services = services };
        if (folder is not null)
        {
            context = context with
            {
                Parameters = new Dictionary<string, string> { ["folder"] = folder.Replace('\\', '/') },
                FileSystem = new DotNetFileSystem(),
                SchedulerHost = new DotNetSchedulerHost()
            };
        }
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
