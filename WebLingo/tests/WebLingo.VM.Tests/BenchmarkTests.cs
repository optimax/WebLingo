using WebLingo.Benchmarks;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>The benchmark's workloads run on every VM it measures, with their expected output (specification, section 5.6).</summary>
public sealed class BenchmarkTests
{
    public static TheoryData<string, string> WorkloadsAndEngines
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var workload in WsWorkloads.All)
            {
                foreach (var engine in WsBenchmarkCommand.Engines) data.Add(workload.Name, engine.Name);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(WorkloadsAndEngines))]
    public void AWorkloadGivesItsExpectedOutput(string workloadName, string engineName)
    {
        var workload = WsWorkloads.All.Single(w => w.Name == workloadName);
        var engine = WsBenchmarkCommand.Engines.Single(e => e.Name == engineName);
        var measurement = WsBenchmark.Measure(engine, workload, runs: 1, warmUps: 0); // Throws if the output is wrong.
        Assert.True(measurement.Instructions > 0);
        Assert.True(measurement.CodeInstructions > 0);
        Assert.True(measurement.CodeBytes > measurement.CodeInstructions);
    }

    [Fact]
    public void TheCommandWritesATableRowPerWorkloadAndVm()
    {
        var output = new StringWriter();
        var exit = WsBenchmarkCommand.Run(["--runs", "1", "--warm-ups", "0", "--workload", "calls"], output, new StringWriter());
        Assert.Equal(WsBenchmarkCommand.Success, exit);
        Assert.Equal(WsBenchmarkCommand.Engines.Count, output.ToString().Split('\n').Count(line => line.StartsWith("| calls |")));
    }

    [Theory]
    [InlineData("--runs", "0")]
    [InlineData("--runs")]
    [InlineData("--warm-ups", "-1")]
    [InlineData("--workload", "nothing")]
    [InlineData("--unknown")]
    public void ABadArgumentIsAUsageError(params string[] args)
    {
        var error = new StringWriter();
        Assert.Equal(WsBenchmarkCommand.UsageError, WsBenchmarkCommand.Run(args, new StringWriter(), error));
        Assert.StartsWith("Usage: WebLingo.Benchmarks", error.ToString());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 2)]
    [InlineData(65535, 3)]
    public void AnOperandsSizeIsItsLeb128Size(int value, int size) => Assert.Equal(size, WsEncoding.Leb128Size(value));
}
