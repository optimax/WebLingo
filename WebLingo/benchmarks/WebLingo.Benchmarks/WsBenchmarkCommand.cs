using System.Globalization;
using System.Runtime.InteropServices;
namespace WebLingo.Benchmarks;

/// <summary>
/// The benchmark's command line: <c>[--runs N] [--warm-ups N] [--workload NAME]…</c>. It measures every workload, or
/// the ones named, on each VM, and writes the results as a Markdown table. Exit codes: 0 success, 1 a workload that
/// failed or gave the wrong output, 2 a usage error.
/// </summary>
public static class WsBenchmarkCommand
{
    public const int Success = 0;
    public const int MeasurementFailed = 1;
    public const int UsageError = 2;

    public static IReadOnlyList<IWsBenchmarkEngine> Engines { get; } = [new WsRegisterEngine()];

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var runs = 7;
        var warmUps = 3;
        var names = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--runs" when TryCount(value, 1, out runs):
                case "--warm-ups" when TryCount(value, 0, out warmUps):
                    i++;
                    break;
                case "--workload" when WsWorkloads.All.Any(workload => workload.Name == value):
                    names.Add(value!);
                    i++;
                    break;
                default:
                    return Usage(error);
            }
        }
        var workloads = names.Count == 0 ? WsWorkloads.All : WsWorkloads.All.Where(w => names.Contains(w.Name)).ToList();

        output.WriteLine($"WebLingo benchmark: {runs} timed runs of each workload after {warmUps} warm-ups.");
        output.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription.Trim()}, " +
                         $"{RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical processors.");
        output.WriteLine();
        output.WriteLine("| Workload | VM | Median | Fastest | Instructions | Instructions/s | Allocated | Code | Code bytes |");
        output.WriteLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
        try
        {
            foreach (var workload in workloads)
            {
                foreach (var engine in Engines)
                {
                    var m = WsBenchmark.Measure(engine, workload, runs, warmUps);
                    output.WriteLine($"| {m.Workload} | {m.Engine} | {Milliseconds(m.Median)} | {Milliseconds(m.Fastest)} | " +
                                     $"{Number(m.Instructions)} | {Millions(m.InstructionsPerSecond)} | " +
                                     $"{Number(m.AllocatedBytes)} B | {Number(m.CodeInstructions)} | {Number(m.CodeBytes)} |");
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            output.Flush();
            error.WriteLine(exception.Message);
            return MeasurementFailed;
        }
        output.WriteLine();
        output.WriteLine("Median and fastest are a run's time; instructions are those one run executes; allocated is what one");
        output.WriteLine("run allocates in .NET's heap; code is the instructions of all the workload's functions, and code bytes");
        output.WriteLine("their size with a byte per operation and each operand it uses in LEB128.");
        return Success;
    }

    private static bool TryCount(string? text, int minimum, out int count) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out count) && count >= minimum;

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Usage: WebLingo.Benchmarks [--runs N] [--warm-ups N] [--workload NAME]...");
        error.WriteLine($"  workloads: {string.Join(", ", WsWorkloads.All.Select(workload => workload.Name))}");
        return UsageError;
    }

    private static string Milliseconds(TimeSpan time) => time.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";

    private static string Millions(double value) => (value / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " M";

    private static string Number(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);
}
