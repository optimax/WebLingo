using System.Text.RegularExpressions;
using WebLingo.Cli;
using WebLingo.Language;
using Xunit;

namespace WebLingo.Conformance.Tests;

/// <summary>
/// The repository's <c>Examples.wl</c>, scripts that show the language (its <c>README.md</c>): each run through
/// <c>weblingo</c>, from a copy, writes what the block at its end says, <c>/* Output: … */</c> its output and
/// <c>/* Errors: … */</c> its errors, from its source on the register VM, and from its WLBC on the stack VM; each in
/// <c>does-not-compile</c> gives the errors its block says, and nothing runs. A script's <c>// Run:</c> line gives its
/// parameters, or has it only checked; one marked <c>// Windows only.</c> is left out elsewhere.
/// </summary>
public sealed class ExamplesTests : IDisposable
{
    private static readonly string Examples = FindExamples();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-examples-" + Guid.NewGuid().ToString("N"));

    public ExamplesTests()
    {
        foreach (var file in Directory.GetFiles(Examples, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(_folder, Path.GetRelativePath(Examples, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    public static TheoryData<string> Scripts()
    {
        var scripts = new TheoryData<string>();
        var files = Directory.GetFiles(Examples, "*.wl").Concat(Directory.GetFiles(Path.Combine(Examples, "does-not-compile"), "*.wl"));
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            if (!OperatingSystem.IsWindows() && Read(file).Contains("\n// Windows only.\n", StringComparison.Ordinal)) continue;
            scripts.Add(Path.GetRelativePath(Examples, file).Replace('\\', '/'));
        }
        return scripts;
    }

    [Fact]
    public void EveryExampleIsTested()
    {
        // Each script in the folder, and in does-not-compile, says what it writes, or that it is only checked.
        var count = Scripts().Count<object[]>();
        Assert.True(count >= 30, $"Only {count} examples were found in {Examples}.");
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void TheExampleWritesWhatItsBlockSays(string name)
    {
        var path = Path.GetFullPath(Path.Combine(_folder, name));
        var source = Read(path);
        var refused = name.StartsWith("does-not-compile/", StringComparison.Ordinal);
        var runLine = Regex.Match(source, @"^// Run: weblingo (run|check) \S+((?: \S+)*)$", RegexOptions.Multiline);
        var command = refused ? "check" : runLine.Success ? runLine.Groups[1].Value : "run";
        List<string> parameters = runLine.Success ? [.. runLine.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)] : [];
        if (Regex.IsMatch(source, @"^param folder\b", RegexOptions.Multiline)) parameters.Add("folder=" + Path.Combine(_folder, "scratch"));
        var output = Block(source, "Output");
        var errors = Block(source, "Errors");
        if (command == "check")
        {
            Assert.Null(output);
            Assert.True(refused ? errors is not null : errors is null, $"{name}'s blocks do not match what it is.");
        }
        else Assert.True(output is not null, $"{name} has no /* Output: */ block.");

        var expected = (Exit: errors is null ? WebLingoCommandLine.Success : refused ? WebLingoCommandLine.CompilationFailed
            : WebLingoCommandLine.RuntimeFailed, Output: output ?? "", Errors: errors ?? "");
        Assert.Equal(expected, Weblingo([command, path, .. parameters], path));
        if (command != "run") return;

        // The same from its WLBC, on the stack VM.
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Weblingo(["compile", "--stack", path], path));
        Assert.Equal(expected, Weblingo(["run", Path.ChangeExtension(path, ".wlbc"), .. parameters], path));
    }

    /// <summary>
    /// <c>weblingo</c> run in this process, on a thread whose stack is the compiler's, a single-threaded apartment's on
    /// Windows, as COM asks: its exit code, its output, and its errors, without the script's folder.
    /// </summary>
    private static (int Exit, string Output, string Errors) Weblingo(string[] args, string script)
    {
        (int, string, string) result = default;
        var thread = new Thread(() =>
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exit = WebLingoCommandLine.Run(args, output, error);
            var folder = Path.GetDirectoryName(script)! + Path.DirectorySeparatorChar;
            result = (exit, output.ToString().ReplaceLineEndings("\n"), error.ToString().Replace(folder, "", StringComparison.Ordinal)
                .ReplaceLineEndings("\n"));
        }, WebLingoParser.CompilerStackSize);
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>The text of a block at a script's end, <c>/* Output:</c> or <c>/* Errors:</c>, to its <c>*/</c>; null if none.</summary>
    private static string? Block(string source, string name)
    {
        var match = Regex.Match(source, @"^/\* " + name + @":\n(.*?)^\*/$", RegexOptions.Multiline | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Read(string path) => File.ReadAllText(path).ReplaceLineEndings("\n");

    private static string FindExamples()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "Examples.wl"))) folder = folder.Parent;
        return folder is null ? throw new DirectoryNotFoundException("Examples.wl is not in a folder above the tests.")
            : Path.Combine(folder.FullName, "Examples.wl");
    }
}
