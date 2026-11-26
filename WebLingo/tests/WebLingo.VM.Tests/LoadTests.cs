using WebLingo.Benchmarks;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// WASP under load (specification, section 5.6, "Under load"): the load site has a page for each of the load command's,
/// and the command takes only what it knows. Serving the site under load is the benchmark's, run by hand, not a test's.
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
    public void AnOptionItDoesNotTakeIsAUsageError(params string[] args)
    {
        var error = new StringWriter();
        Assert.Equal(WebLingoLoad.UsageError, WebLingoBenchmarkCommand.Run(["--load", .. args], new StringWriter(), error));
        Assert.StartsWith("Usage: WebLingo.Benchmarks --load", error.ToString(), StringComparison.Ordinal);
    }
}
