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
    public void AnOptionItDoesNotTakeIsAUsageError(params string[] args)
    {
        var error = new StringWriter();
        Assert.Equal(WebLingoLoad.UsageError, WebLingoBenchmarkCommand.Run(["--load", .. args], new StringWriter(), error));
        Assert.StartsWith("Usage: WebLingo.Benchmarks --load", error.ToString(), StringComparison.Ordinal);
    }

    private static readonly WebLingoSoakTrend.Measure Bytes = WebLingoSoakTrend.Measures.Single(measure => measure.Name == "private bytes");

    /// <summary>Samples, one a reload, ten reloads apart, their private bytes those given, their handles and threads steady.</summary>
    private static List<WebLingoSoakSample> Samples(params long[] bytes) =>
        [.. bytes.Select((value, i) => new WebLingoSoakSample(TimeSpan.FromMinutes(i), i * 10L, new WebLingoServerSample(value, value, 500, 30)))];

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
