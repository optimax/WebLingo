using WebLingo.Cli;
using WebLingo.Pages.Cli;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// <c>welp</c>, WebLingo Pages' command line (specification, section 13, "Contracts"), end to end: each page is written to
/// a file and run through it, as a user would, with its output, its diagnostics and its exit code checked.
/// </summary>
public sealed class WelpTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "welp-tests-" + Guid.NewGuid().ToString("N"));

    public WelpTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void RunWritesThePageToStandardOutput()
    {
        var page = "<html>\n<% foreach (var n in [1, 2]) { %>\n<p><%= n %> &lt; <%= \"3 & 4\" %></p>\n<% } %>\n</html>\n";
        Assert.Equal((WebLingoCommandLine.Success, "<html>\n<p>1 &lt; 3 &amp; 4</p>\n<p>2 &lt; 3 &amp; 4</p>\n</html>\n", ""),
            Welp("run", page));
    }

    [Fact]
    public void CheckCompilesThePageWithoutWritingIt()
    {
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Welp("check", "<p><%= 1 %></p>"));
    }

    [Fact]
    public void APageTakesItsParametersAsAScriptDoes()
    {
        var page = "<% param name; %>\nHello, <%= name %>!";
        Assert.Equal((WebLingoCommandLine.Success, "Hello, &lt;World&gt;!", ""), Welp("run", page, "name=<World>"));
    }

    [Fact]
    public void APageImportsModulesFromItsFolder()
    {
        File.WriteAllText(Path.Combine(_folder, "lib.wl"), "export function twice(x) { return x * 2; }");
        Assert.Equal((WebLingoCommandLine.Success, "<b>42</b>", ""), Welp("run", "<% import { twice } from \"lib.wl\"; %><b><%= twice(21) %></b>"));
    }

    [Fact]
    public void ErrorsNameThePageWithTheirLineAndColumn()
    {
        var (exit, output, error) = Welp("check", "<p>\n  <% var x = 1;\n");
        Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
        Assert.Equal("", output);
        Assert.Equal("page.wlp(2,3): error WL5001: This block has no closing '%>'.", error.TrimEnd());
    }

    [Fact]
    public void ARuntimeErrorStopsThePageWhereItIs()
    {
        var (exit, output, error) = Welp("run", "<p>before</p>\n<%= [1][5] %>\n<p>after</p>");
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Equal("<p>before</p>\n", output);
        Assert.StartsWith("page.wlp(2,", error);
        Assert.Contains("error WL3005", error);
    }

    [Fact]
    public void VersionAndUsageNameWelp()
    {
        var output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.Success, WebLingoPagesCommandLine.Run(["--version"], output, new StringWriter()));
        Assert.Equal(WebLingoPagesCommandLine.Version, output.ToString().TrimEnd());

        output = new StringWriter();
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoPagesCommandLine.Run(["page.wlp"], output, new StringWriter()));
        Assert.StartsWith("Usage: welp --version | run <file.wlp> [name=value …] | check <file.wlp>", output.ToString());
    }

    [Fact]
    public void APageThatCannotBeReadIsReported()
    {
        var path = Path.Combine(_folder, "missing.wlp");
        var error = new StringWriter();
        Assert.Equal(WebLingoCommandLine.FileUnreadable, WebLingoPagesCommandLine.Run(["run", path], new StringWriter(), error));
        Assert.StartsWith(path + ": cannot read the file:", error.ToString());
    }

    [Fact]
    public void WeblingoStillRunsScriptsOnly()
    {
        // weblingo compiles the language only: a page's block is not WebLingo.
        var path = Path.Combine(_folder, "page.wl");
        File.WriteAllText(path, "<p><%= 1 %></p>");
        Assert.Equal(WebLingoCommandLine.CompilationFailed, WebLingoCommandLine.Run(["check", path], new StringWriter(), new StringWriter()));
    }

    private (int Exit, string Output, string Error) Welp(string command, string page, params string[] parameters)
    {
        var path = Path.Combine(_folder, "page.wlp");
        File.WriteAllText(path, page);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoPagesCommandLine.Run([command, path, .. parameters], output, error);
        // Diagnostics name the file as it was given; show only its name, so the expectations hold on every machine.
        return (exit, output.ToString(), error.ToString().Replace(path, "page.wlp", StringComparison.Ordinal));
    }
}
