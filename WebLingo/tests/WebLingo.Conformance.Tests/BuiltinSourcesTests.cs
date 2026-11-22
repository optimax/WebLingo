using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// The built-ins written in WebLingo, <c>.wlg</c> files beside the code that uses them, which the build makes constants
/// (<c>Directory.Build.targets</c>; the owner's answer, 22 November 2026, "Built-ins Written in WebLingo, in .wlg Files",
/// A1 (a)): each checked by <c>weblingo check</c> where it can be, one that uses none of the compiler's own intrinsics
/// (<c>hold</c>, <c>toText</c>, <c>writtenText</c>, <c>toJson</c>, <c>checkCallback</c>), names no built-in class a script
/// imports, and is a whole file, not a native class's methods.
/// </summary>
public sealed class BuiltinSourcesTests
{
    private static readonly string Source = FindSource();

    [Theory]
    [InlineData("WebLingo.Compiler/Builtins/Error.wlg")]
    [InlineData("WebLingo.Compiler/Builtins/Iterator.wlg")]
    [InlineData("WebLingo.Compiler/Builtins/RuntimeError.wlg")]
    [InlineData("WebLingo.Compiler/Builtins/Spread.wlg")]
    [InlineData("WebLingo.Compiler/Builtins/Test.wlg")]
    public void ABuiltinWrittenInWebLingoChecksAsAScript(string file)
    {
        var path = Path.Combine(Source, file);
        Assert.True(File.Exists(path), $"{path} is not in the repository.");
        var error = new StringWriter();
        Assert.Equal((WebLingoCommandLine.Success, ""), (WebLingoCommandLine.Run(["check", path], new StringWriter(), error), error.ToString()));
    }

    /// <summary>The solution's <c>src</c> folder, found from the tests' own, as the examples' folder is.</summary>
    private static string FindSource()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "src", "WebLingo.Compiler", "Builtins")))
            folder = folder.Parent;
        return folder is null ? "" : Path.Combine(folder.FullName, "src");
    }
}
