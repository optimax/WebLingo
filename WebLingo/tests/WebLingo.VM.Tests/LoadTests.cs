using WebLingo.Benchmarks;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// WASP under load (specification, section 5.6, "Under load"): the load site has a page for each of the load command's,
/// and the command takes only what it knows. Serving the site under load is the benchmark's, run by hand, not a test's;
/// what a soak's samples say is tested here (section 9, "M0.22 parts", part 3).
/// </summary>
public sealed class LoadTests
{
    [Fact]
    public void EveryPageIsInTheLoadSite()
    {
        foreach (var page in WebLingoLoad.Pages)
        {
            Assert.True(File.Exists(Path.Combine(WebLingoLoad.SiteFolder, page.Name + ".wasp")), page.Name);
        }
        Assert.True(File.Exists(Path.Combine(WebLingoLoad.SiteFolder, "wasp.config")));
        // The page --fair asks for beside hello (section 9, "M0.24 parts", part 3).
        Assert.True(File.Exists(Path.Combine(WebLingoLoad.SiteFolder, "slow.wasp")));
    }

    [Theory]
    [InlineData("--page", "nowhere")]
    [InlineData("--clients", "1,0")]
    [InlineData("--host", "iis")]
    [InlineData("--url", "ftp://example.com/")]
    [InlineData("--soak", "0")]
    [InlineData("--seconds")]
    [InlineData("--site", "shop")]
    [InlineData("--site", "forum")]
    [InlineData("--site", "forum", "--url", "http://localhost:8080/", "--page", "hello")]
    [InlineData("--page", "topic")]
    [InlineData("--fair", "0")]
    [InlineData("--fair", "none,fast")]
    [InlineData("--fair", "")]
    [InlineData("--reload-every", "5")]
    [InlineData("--reload-every", "0", "--soak", "1")]
    [InlineData("--soak", "1", "--reload-every", "5", "--url", "http://localhost:8080/")]
    [InlineData("--prepare")]
    [InlineData("--prepare", "C:/WWW/wasp.load", "--seconds", "1")]
    public void AnOptionItDoesNotTakeIsAUsageError(params string[] args)
    {
        var error = new StringWriter();
        Assert.Equal(WebLingoLoad.UsageError, WebLingoBenchmarkCommand.Run(["--load", .. args], new StringWriter(), error));
        Assert.StartsWith("Usage: WebLingo.Benchmarks --load", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--prepare</c> makes the load site for IIS: its pages, a <c>web.config</c> that runs the installed <c>wasp</c>, and
    /// its database where its <c>wasp.config</c> names it; made again, its pages are replaced and its database kept
    /// (section 5.6, "Under load, parts", part 3).
    /// </summary>
    [Fact]
    public void PrepareMakesTheLoadSiteForIis_KeepingItsDatabaseWhenMadeAgain()
    {
        var folder = Path.Combine(Path.GetTempPath(), "weblingo-prepare-" + Guid.NewGuid().ToString("N"));
        try
        {
            var output = new StringWriter();
            Assert.Equal(WebLingoLoad.Success, WebLingoBenchmarkCommand.Run(["--load", "--prepare", folder], output, new StringWriter()));
            var site = Path.Combine(folder, "site");
            foreach (var page in WebLingoLoad.Pages) Assert.True(File.Exists(Path.Combine(site, page.Name + ".wasp")), page.Name);
            Assert.Contains(@"processPath=""C:\Program Files\WebLingo\wasp.exe""", File.ReadAllText(Path.Combine(site, "web.config")));
            var database = Path.Combine(folder, "load-data", "load.db");
            Assert.True(File.Exists(database));
            Assert.Contains($"writable={Path.Combine(folder, "load-data")}", output.ToString());

            File.WriteAllText(Path.Combine(site, "hello.wasp"), "changed");
            var made = File.GetLastWriteTimeUtc(database);
            Assert.Equal(WebLingoLoad.Success,
                WebLingoBenchmarkCommand.Run(["--load", "--prepare", folder], new StringWriter(), new StringWriter()));
            Assert.NotEqual("changed", File.ReadAllText(Path.Combine(site, "hello.wasp")));
            Assert.Equal(made, File.GetLastWriteTimeUtc(database));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static readonly WebLingoSoakTrend.Measure Bytes = WebLingoSoakTrend.Measures.Single(measure => measure.Name == "private bytes");

    /// <summary>Samples, one a reload, ten reloads apart, their private bytes those given, their .NET heap, handles and threads steady.</summary>
    private static List<WebLingoSoakSample> Samples(params long[] bytes) =>
        [.. bytes.Select((value, i) => new WebLingoSoakSample(TimeSpan.FromMinutes(i), i * 10L,
            new WebLingoServerSample(value, value, 500, 30, 40_000_000)))];

    /// <summary>A measure some sample lacks, the .NET heap where its counters could not be read, gives no growth and no verdict.</summary>
    [Fact]
    public void AMeasureSomeSampleLacksGivesNoGrowthAndNoVerdict()
    {
        var heap = WebLingoSoakTrend.Measures.Single(measure => measure.Name == "GC heap");
        var samples = Samples([.. Enumerable.Range(0, 16).Select(i => 100_000_000L + i * 1_000_000)]);
        samples[5] = samples[5] with { Server = samples[5].Server with { GcHeap = null } };
        var after = WebLingoSoakTrend.AfterWarmUp(samples);
        Assert.False(WebLingoSoakTrend.Read(after, heap));
        Assert.Null(WebLingoSoakTrend.Growth(after, heap, out _));
        Assert.Null(WebLingoSoakTrend.KeepsRising(after, heap));
        Assert.True(WebLingoSoakTrend.KeepsRising(after, Bytes));
    }

    [Fact]
    public void AMeasureThatRisesAllTheWayAfterTheWarmUpKeepsRising_ItsGrowthPerReloadItsSlope()
    {
        // 100 MB, then 1 MB more every ten reloads, 100 KB a reload, with the collector's noise.
        const long Megabyte = 1024 * 1024;
        var samples = Samples([.. Enumerable.Range(0, 16).Select(i => 100 * Megabyte + i * Megabyte + (i % 2 == 0 ? 200_000 : 0))]);
        var after = WebLingoSoakTrend.AfterWarmUp(samples);
        Assert.Equal(12, after.Count);
        Assert.True(WebLingoSoakTrend.KeepsRising(after, Bytes));
        Assert.InRange(WebLingoSoakTrend.Growth(after, Bytes, out var perReload)!.Value, 100 * 1024 - 5000, 100 * 1024 + 5000);
        Assert.True(perReload);
        // Handles and threads steady: neither rises, nor grows.
        foreach (var measure in WebLingoSoakTrend.Measures.Where(measure => measure != Bytes))
        {
            Assert.False(WebLingoSoakTrend.KeepsRising(after, measure));
            Assert.Equal(0, WebLingoSoakTrend.Growth(after, measure, out _));
        }
    }

    [Fact]
    public void AMeasureThatOnlyWobbles_OrRisesWhileWarmingUp_OrRisesByLessThanItsNoise_DoesNotKeepRising()
    {
        const long Megabyte = 1024 * 1024;
        // Up and down around 120 MB.
        Assert.False(WebLingoSoakTrend.KeepsRising(WebLingoSoakTrend.AfterWarmUp(
            Samples([.. Enumerable.Range(0, 16).Select(i => 120 * Megabyte + (i % 3) * 3 * Megabyte)])), Bytes));
        // Rising from 60 MB to 120 MB in its first quarter, then steady.
        Assert.False(WebLingoSoakTrend.KeepsRising(WebLingoSoakTrend.AfterWarmUp(
            Samples([.. Enumerable.Range(0, 16).Select(i => i < 4 ? 60 * Megabyte + i * 20 * Megabyte : 120 * Megabyte)])), Bytes));
        // Rising all the way, by 1,000 bytes in all, below the margin of a megabyte or 5 percent.
        Assert.False(WebLingoSoakTrend.KeepsRising(WebLingoSoakTrend.AfterWarmUp(
            Samples([.. Enumerable.Range(0, 16).Select(i => 120 * Megabyte + i * 100L)])), Bytes));
    }

    [Fact]
    public void TooFewSamplesSayNothing_AndWithoutReloadsTheGrowthIsAnHours()
    {
        var samples = Samples(1, 2, 3, 4, 5, 6, 7, 8);
        var after = WebLingoSoakTrend.AfterWarmUp(samples);
        Assert.Equal(6, after.Count);
        Assert.Null(WebLingoSoakTrend.KeepsRising(after, Bytes));
        var steady = samples.Select(sample => sample with { Reloads = 0 }).ToList();
        Assert.Equal(60, WebLingoSoakTrend.Growth(steady, Bytes, out var perReload)!.Value, 6);
        Assert.False(perReload);
        Assert.Null(WebLingoSoakTrend.Growth(steady.Take(1).ToList(), Bytes, out _));
    }
}
