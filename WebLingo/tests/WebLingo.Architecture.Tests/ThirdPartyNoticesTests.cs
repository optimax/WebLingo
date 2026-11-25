using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
namespace WebLingo.Architecture.Tests;

/// <summary>
/// The repository's <c>THIRD_PARTY_NOTICES.md</c> lists the packages WebLingo's programs ship, each at its version
/// (<c>Documents/implementation.md</c>, section 10): the packages <c>weblingo</c>, <c>weblingow</c> and <c>wasp</c> are
/// built with, as their <c>.deps.json</c> files name them, are exactly those its table "Packages" lists, so that a package
/// added, upgraded or removed, a dependency's own among them, fails here until the notices say so.
/// </summary>
public sealed class ThirdPartyNoticesTests
{
    [Fact]
    public void TheNoticesListEveryPackageTheProgramsShipAtItsVersion()
    {
        var solution = new DirectoryInfo(AppContext.BaseDirectory);
        while (solution is not null && !File.Exists(Path.Combine(solution.FullName, "WebLingo.slnx"))) solution = solution.Parent;
        Assert.NotNull(solution);
        // The programs as the solution's build made them, in the configuration and the framework these tests were built for.
        var built = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var (framework, configuration) = (built.Name, built.Parent!.Name);
        var shipped = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (project, program) in new[] { ("WebLingo.Cli", "weblingo"), ("WebLingo.Windowed.Cli", "weblingow"), ("WebLingo.Wasp.Cli", "wasp") })
        {
            var deps = Path.Combine(solution.FullName, "src", project, "bin", configuration, framework, program + ".deps.json");
            Assert.True(File.Exists(deps), $"'{deps}' is not there: build the solution first.");
            using var document = JsonDocument.Parse(File.ReadAllText(deps));
            foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
            {
                if (library.Value.GetProperty("type").GetString() == "package") shipped.Add(library.Name);
            }
        }

        var notices = File.ReadAllText(Path.Combine(solution.Parent!.FullName, "THIRD_PARTY_NOTICES.md"));
        var start = notices.IndexOf("\n## Packages", StringComparison.Ordinal);
        var end = notices.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "THIRD_PARTY_NOTICES.md has no section \"Packages\" followed by another.");
        var listed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match row in Regex.Matches(notices[start..end], @"^\| ([A-Za-z0-9_.]+) \| ([0-9][^ |]*) \|", RegexOptions.Multiline))
        {
            listed.Add(row.Groups[1].Value + "/" + row.Groups[2].Value);
        }
        Assert.Equal(shipped, listed);
    }
}
