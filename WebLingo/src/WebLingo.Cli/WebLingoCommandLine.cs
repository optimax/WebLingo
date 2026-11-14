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
        if (args.Length == 2 && args[0] == "check") return RunFile(false, args[1], new Dictionary<string, string>(), output, error);
        if (args.Length >= 2 && args[0] == "run")
        {
            if (TryParameters(args, out var parameters, out var wrong)) return RunFile(true, args[1], parameters, output, error);
            error.WriteLine($"'{wrong}' is not a parameter's value, name=value, or is given twice.");
        }

        output.WriteLine("Usage: weblingo --version | run <file.wl> [name=value …] | check <file.wl>");
        output.WriteLine("  run    compiles the file and runs it, giving its parameters the values after it, each a string");
        output.WriteLine("  check  compiles the file and reports its errors, without running it");
        return args.Length == 0 ? Success : UsageError;
    }

    /// <summary>
    /// A script's parameters, after its path: each <c>name=value</c>, its name a name, its value a string, every name given
    /// once (section 3.9); false, with the first argument that is not one, otherwise.
    /// </summary>
    private static bool TryParameters(string[] args, out Dictionary<string, string> parameters, out string wrong)
    {
        parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        wrong = "";
        for (var i = 2; i < args.Length; i++)
        {
            var equals = args[i].IndexOf('=', StringComparison.Ordinal);
            var name = equals > 0 ? args[i][..equals] : "";
            if (name.Length == 0 || !IsName(name) || !parameters.TryAdd(name, args[i][(equals + 1)..]))
            {
                wrong = args[i];
                return false;
            }
        }
        return true;
    }

    private static bool IsName(string text)
    {
        if (char.IsAsciiDigit(text[0])) return false;
        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    private static int RunFile(bool run, string path, IReadOnlyDictionary<string, string> parameters, TextWriter output, TextWriter error)
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

        var resolver = new FileResolver(path);
        var options = new WebLingoCompilerOptions(declarations) { Resolver = resolver };
        var compiled = new WebLingoCompiler(options).Compile(source, path);
        foreach (var diagnostic in compiled.Diagnostics) error.WriteLine(Format(TextOf(diagnostic), diagnostic));
        if (compiled.Module is null) return CompilationFailed;
        if (!run) return Success;

        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
        {
            Services = services,
            Clock = new SystemClock(new DotNetClock()),
            Parameters = parameters
        };
        // The VM imports modules while the program runs as the compiler imports them, with the same resolver (section 3.9).
        var result = new WebLingoVirtualMachine { Loader = new WebLingoModuleLoader(options) }.Execute(compiled.Module, context);
        output.Flush();
        if (result.Succeeded) return Success;
        error.WriteLine(Format(TextOf(result.Error!), result.Error!));
        return RuntimeFailed;

        // A diagnostic's file: a module the program imports, or the program's own (section 3.9).
        WebLingoSourceText TextOf(WebLingoDiagnostic diagnostic) =>
            diagnostic.Source is { } name && resolver.Texts.TryGetValue(name, out var imported) ? imported : text;
    }

    /// <summary>
    /// The files a program imports (specification, section 3.9): a path from the importing file's folder, <c>./</c>
    /// being that folder and <c>/</c> the drive's root, <c>.wl</c> added to one without an extension. A file is named
    /// by its path from the current folder, with <c>/</c> between its folders, and the program's own by the path it was
    /// given, so that each is one module however many import it.
    /// </summary>
    private sealed class FileResolver(string mainPath) : IWebLingoSourceResolver
    {
        private readonly string _mainFull = Path.GetFullPath(mainPath);

        /// <summary>The imported files' texts, by name, for their diagnostics.</summary>
        public Dictionary<string, WebLingoSourceText> Texts { get; } = new(StringComparer.Ordinal);

        public bool TryResolve(string importer, string path, out string name, out string source, out string? error)
        {
            name = "";
            source = "";
            error = null;
            try
            {
                var importerFull = importer == mainPath ? _mainFull : Path.GetFullPath(importer);
                var full = path.StartsWith('/')
                    ? Path.Combine(Path.GetPathRoot(importerFull) ?? "", path.TrimStart('/'))
                    : Path.Combine(Path.GetDirectoryName(importerFull) ?? "", path);
                full = Path.GetFullPath(full);
                if (Path.GetExtension(full).Length == 0) full += ".wl";
                if (full == _mainFull)
                {
                    name = mainPath;
                    return true;
                }
                name = Path.GetRelativePath(Directory.GetCurrentDirectory(), full).Replace('\\', '/');
                if (Texts.TryGetValue(name, out var known))
                {
                    source = known.Text;
                    return true;
                }
                source = File.ReadAllText(full);
                Texts.Add(name, new WebLingoSourceText(source, name));
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                                  or NotSupportedException)
            {
                error = exception is FileNotFoundException or DirectoryNotFoundException ? "there is no such file" : exception.Message;
                return false;
            }
        }
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
