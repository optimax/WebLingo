using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
namespace WebLingo.Cli;

/// <summary>
/// The <c>weblingo</c> command line (specification, section 8.4, "CLI"): <c>run</c>, <c>check</c> and
/// <c>--version</c>. It is the host: it registers <c>write</c> and <c>writeLn</c>, writing to standard output, compiles a file, and runs
/// it in the VM. Diagnostics go to standard error as <c>file(line,column): error CODE: message</c>. Exit codes
/// (section 15.2): 0 success, 1 compilation errors, 2 a usage error, 3 a runtime error, 4 a file that cannot be
/// read.
/// </summary>
public static class WebLingoCommandLine
{
    public const int Success = 0;
    public const int CompilationFailed = 1;
    public const int UsageError = 2;
    public const int RuntimeFailed = 3;
    public const int FileUnreadable = 4;

    public const string Version = "WebLingo M0.6 (pre-alpha)";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 1 && args[0] is "--version" or "version")
        {
            output.WriteLine(Version);
            return Success;
        }
        if (args.Length == 2 && args[0] is "run" or "check") return RunFile(args[0] == "run", args[1], output, error);

        output.WriteLine("Usage: weblingo --version | run <file.wl> | check <file.wl>");
        output.WriteLine("  run    compiles the file and runs it");
        output.WriteLine("  check  compiles the file and reports its errors, without running it");
        return args.Length == 0 ? Success : UsageError;
    }

    private static int RunFile(bool run, string path, TextWriter output, TextWriter error)
    {
        string source;
        try
        {
            source = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            error.WriteLine($"{path}: cannot read the file: {exception.Message}");
            return FileUnreadable;
        }

        var text = new WebLingoSourceText(source, path);
        var services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(new TextWriterOutput(output)));
        var declarations = new List<WebLingoBuiltinDeclaration>();
        foreach (var builtin in services.Builtins) declarations.Add(new WebLingoBuiltinDeclaration(builtin.Name, builtin.Arity));

        var compiled = new WebLingoCompiler(new WebLingoCompilerOptions(declarations)).Compile(source, path);
        foreach (var diagnostic in compiled.Diagnostics) error.WriteLine(Format(text, diagnostic));
        if (compiled.Module is null) return CompilationFailed;
        if (!run) return Success;

        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0)) { Services = services, Clock = new SystemClock(new DotNetClock()) };
        var result = new WebLingoVirtualMachine().Execute(compiled.Module, context);
        output.Flush();
        if (result.Succeeded) return Success;
        error.WriteLine(Format(text, result.Error!));
        return RuntimeFailed;
    }

    /// <summary>A diagnostic as a line: <c>file(line,column): error CODE: message</c>.</summary>
    public static string Format(WebLingoSourceText source, WebLingoDiagnostic diagnostic)
    {
        var location = source.GetLocation(diagnostic.Span.Start);
        var severity = diagnostic.Severity switch
        {
            WebLingoDiagnosticSeverity.Error => "error",
            WebLingoDiagnosticSeverity.Warning => "warning",
            _ => "info"
        };
        return $"{source.Name}({location.Line},{location.Column}): {severity} {diagnostic.Code}: {diagnostic.Message}";
    }

    /// <summary>
    /// The clock an execution's timers (<c>delay</c>) wait on (specification, section 3.8): the platform's monotonic clock,
    /// in milliseconds, the thread sleeping while the execution has nothing else to run.
    /// </summary>
    private sealed class SystemClock(IWebLingoClock clock) : IWebLingoExecutionClock
    {
        public long Milliseconds
        {
            get
            {
                var (ticks, frequency) = (clock.GetMonotonicTicks(), clock.MonotonicFrequency);
                return ticks / frequency * 1000 + ticks % frequency * 1000 / frequency;
            }
        }

        public void WaitUntil(long milliseconds)
        {
            for (var now = Milliseconds; now < milliseconds; now = Milliseconds)
            {
                Thread.Sleep((int)Math.Min(milliseconds - now, int.MaxValue));
            }
        }
    }

    /// <summary>Script output to a text writer as it is written, line breaks LF on every platform.</summary>
    private sealed class TextWriterOutput(TextWriter writer) : IWebLingoTextOutput
    {
        public void Write(string text) => writer.Write(text);
    }
}
