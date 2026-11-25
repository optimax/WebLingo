using System.Text.RegularExpressions;
using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:log</c> under <c>weblingo</c> (specification, section 6.12, "Logging"; the owner's answer, 24 November 2026,
/// "Logging for WASP Sites", A1 (c)): each line written to the standard error, every level, its time in UTC, its level and
/// its text, kept to one line, a script's output left as it is; run as a script and compiled, which must give the same.
/// </summary>
public sealed partial class LogModuleTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-log-" + Guid.NewGuid().ToString("N"));

    public LogModuleTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AScriptLogsToItsStandardError_EveryLevel()
    {
        var (exit, output, error) = Run("""
            import log from "std:log";
            import { writeLn } from "std:console";
            log.debug("one");
            log.info($"two {1 + 1}");
            writeLn("written");
            log.warn("three\nlines\r\n");
            log.error("four");
            """);
        Assert.Equal((WebLingoCommandLine.Success, "written\n"), (exit, output));
        var lines = error.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["DEBUG one", "INFO two 2", "WARN three\\nlines\\r\\n", "ERROR four"], lines.Select(line => Line().Match(line).Groups[1].Value));
        Assert.All(lines, line => Assert.Matches(Line(), line));
    }

    [Theory]
    [InlineData("log.info(5);", "WL3001 (TypeError): The argument of 'info' must be a string, the text to log, as $\"{value}\" makes one of a value, not the integer 5.")]
    [InlineData("log.error(null);", "WL3001 (TypeError): The argument of 'error' must be a string, the text to log, as $\"{value}\" makes one of a value, not null.")]
    [InlineData("log.debug();", "WL2006")]
    public void WhatIsNotTextIsAnError(string call, string message)
    {
        var (exit, _, error) = Run("import log from \"std:log\";\n" + call);
        Assert.NotEqual(WebLingoCommandLine.Success, exit);
        Assert.Contains(message, error);
    }

    // The time in UTC, to the millisecond, then the level and the text.
    [GeneratedRegex(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z ((?:DEBUG|INFO|WARN|ERROR) .*)$")]
    private static partial Regex Line();

    private (int Exit, string Output, string Error) Run(string program)
    {
        var path = Path.Combine(_folder, "program.wlg");
        File.WriteAllText(path, program);
        var source = Weblingo("run", path);
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", path).Exit);
        var compiled = Weblingo("run", Path.ChangeExtension(path, ".wlb"));
        Assert.Equal((source.Exit, source.Output, Times().Replace(source.Error, "")), (compiled.Exit, compiled.Output, Times().Replace(compiled.Error, "")));
        return source;
    }

    [GeneratedRegex(@"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z")]
    private static partial Regex Times();

    private static (int Exit, string Output, string Error) Weblingo(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(args, output, error);
        return (exit, output.ToString().Replace("\r\n", "\n"), error.ToString());
    }
}
