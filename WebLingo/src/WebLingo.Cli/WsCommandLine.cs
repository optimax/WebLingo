using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
namespace WebLingo.Cli;

/// <summary>
/// The <c>weblingo</c> command line (specification, section 8.4, "CLI"): <c>run</c>, <c>check</c> and
/// <c>--version</c>. It is the host: it registers <c>print</c>, writing to standard output, compiles a file, and runs
/// it in the VM. Diagnostics go to standard error as <c>file(line,column): error CODE: message</c>. Exit codes
/// (proposed, section 15.2): 0 success, 1 compilation errors, 2 a usage error or a file that cannot be read, 3 a
/// runtime error.
/// </summary>
public static class WsCommandLine
{
    public const int Success = 0;
    public const int CompilationFailed = 1;
    public const int UsageError = 2;
    public const int RuntimeFailed = 3;

    public const string Version = "WebLingo M0.6 (pre-alpha)";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 1 && args[0] is "--version" or "version")
        {
            output.WriteLine(Version);
            return Success;
        }
        if (args.Length == 2 && args[0] is "run" or "check") return RunFile(args[0] == "run", args[1], output, error);

        output.WriteLine("Usage: weblingo --version | run <file.ws> | check <file.ws>");
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
            return UsageError;
        }

        var text = new WsSourceText(source, path);
        var services = new WsBuiltinRegistry(new WsPrintFunction(new TextWriterOutput(output)));
        var declarations = new List<WsBuiltinDeclaration>();
        foreach (var builtin in services.Builtins) declarations.Add(new WsBuiltinDeclaration(builtin.Name, builtin.Arity));

        var compiled = new WsCompiler(new WsCompilerOptions(declarations)).Compile(source, path);
        foreach (var diagnostic in compiled.Diagnostics) error.WriteLine(Format(text, diagnostic));
        if (compiled.Module is null) return CompilationFailed;
        if (!run) return Success;

        var context = new WsExecutionContext(new WsWorkerId(0)) { Services = services };
        var result = new WsVirtualMachine().Execute(compiled.Module, context);
        output.Flush();
        if (result.Succeeded) return Success;
        error.WriteLine(Format(text, result.Error!));
        return RuntimeFailed;
    }

    /// <summary>A diagnostic as a line: <c>file(line,column): error CODE: message</c>.</summary>
    public static string Format(WsSourceText source, WsDiagnostic diagnostic)
    {
        var location = source.GetLocation(diagnostic.Span.Start);
        var severity = diagnostic.Severity switch
        {
            WsDiagnosticSeverity.Error => "error",
            WsDiagnosticSeverity.Warning => "warning",
            _ => "info"
        };
        return $"{source.Name}({location.Line},{location.Column}): {severity} {diagnostic.Code}: {diagnostic.Message}";
    }

    /// <summary>Script output to a text writer, each line ended by LF on every platform.</summary>
    private sealed class TextWriterOutput(TextWriter writer) : IWsTextOutput
    {
        public void WriteLine(string text)
        {
            writer.Write(text);
            writer.Write('\n');
        }
    }
}
