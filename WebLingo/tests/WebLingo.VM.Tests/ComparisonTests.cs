using WebLingo.Benchmarks;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// The comparison with other languages (specification, section 5.6, "Against other languages"): its programs, each in
/// every language, and WebLingo's on every VM, with their expected output; and its command, run on WebLingo alone, so
/// that no test reaches another language's runtime installed on the machine.
/// </summary>
public sealed class ComparisonTests
{
    public static TheoryData<string> ProgramNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var program in WebLingoComparison.Programs) data.Add(program.Name);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ProgramNames))]
    public void EveryProgramIsWrittenInEveryLanguage(string name)
    {
        foreach (var extension in WebLingoComparison.Runtimes.Select(runtime => runtime.Extension).Where(e => e != ".wlb").Distinct())
            Assert.True(File.Exists(Path.Combine(WebLingoComparison.ProgramsFolder, name + extension)), name + extension);
    }

    [Theory]
    [MemberData(nameof(ProgramNames))]
    public void AWebLingoProgramWritesItsResultOnEveryVm(string name)
    {
        var program = WebLingoComparison.Programs.Single(p => p.Name == name);
        var source = File.ReadAllText(Path.Combine(WebLingoComparison.ProgramsFolder, name + ".wlg")).ReplaceLineEndings("\n");
        var workload = new WebLingoWorkload(name, program.Exercises, source, program.ExpectedOutput);
        foreach (var engine in WebLingoBenchmarkCommand.Engines)
            WebLingoBenchmark.Measure(engine, workload, runs: 1, warmUps: 0); // Throws if the output is wrong.
    }

    [Fact]
    public void TheComparisonWritesARowPerProgramAndAColumnPerRuntime()
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = WebLingoBenchmarkCommand.Run(["--compare", "--runs", "1", "--warm-ups", "0", "--program", "calls",
            "--runtime", "weblingo", "--runtime", "weblingo-compiled"], output, error);
        Assert.Equal((WebLingoBenchmarkCommand.Success, ""), (exit, error.ToString()));
        var lines = output.ToString().ReplaceLineEndings("\n").Split('\n');
        Assert.Contains("| Program | WebLingo | WebLingo, compiled |", lines);
        // empty's row in the first table only, which the second takes away; calls's in both.
        Assert.Single(lines, line => line.StartsWith("| empty | ", StringComparison.Ordinal));
        Assert.Equal(2, lines.Count(line => line.StartsWith("| calls | ", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnUnknownRuntimeIsAUsageError()
    {
        var error = new StringWriter();
        Assert.Equal(WebLingoBenchmarkCommand.UsageError,
            WebLingoBenchmarkCommand.Run(["--compare", "--runtime", "perl"], new StringWriter(), error));
        Assert.Contains("runtimes: weblingo, weblingo-compiled,", error.ToString());
    }
}
