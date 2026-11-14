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

    /// <summary><c>weblingo</c>: scripts, <c>.wl</c> files, compiled by the WebLingo compiler.</summary>
    public static WebLingoCommandLineProgram Scripts { get; } = new("weblingo", Version, ".wl", _ => [],
        (file, options) => new WebLingoCommandLineCompilation(new WebLingoCompiler(options).Compile(file.Text, file.Name)));

    public static int Run(string[] args, TextWriter output, TextWriter error) => Run(Scripts, args, output, error);

    public static int Run(WebLingoCommandLineProgram program, string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 1 && args[0] is "--version" or "version")
        {
            output.WriteLine(program.Version);
            return Success;
        }
        if (args.Length >= 2 && args[0] is "run" or "check")
        {
            // run|check [--scheme name=folder]… file [name=value …]: the schemes over weblingo.json's (section 3.9).
            var schemes = new Dictionary<string, string>(StringComparer.Ordinal);
            var at = 1;
            string? wrong = null;
            while (wrong is null && at < args.Length && args[at] == "--scheme")
            {
                var equals = at + 1 < args.Length ? args[at + 1].IndexOf('=', StringComparison.Ordinal) : -1;
                if (equals > 0 && WebLingoSchemes.IsName(args[at + 1][..equals]) && equals + 1 < args[at + 1].Length
                    && schemes.TryAdd(args[at + 1][..equals], args[at + 1][(equals + 1)..]))
                {
                    at += 2;
                    continue;
                }
                wrong = $"'{(at + 1 < args.Length ? args[at + 1] : "")}' is not a scheme, name=folder, or is given twice.";
            }
            if (wrong is null && at < args.Length)
            {
                if (args[0] == "check")
                {
                    if (at + 1 == args.Length) return RunFile(program, false, args[at], new Dictionary<string, string>(), schemes, output, error);
                }
                else if (TryParameters(args, at + 1, out var parameters, out var value))
                {
                    return RunFile(program, true, args[at], parameters, schemes, output, error);
                }
                else
                {
                    wrong = $"'{value}' is not a parameter's value, name=value, or is given twice.";
                }
            }
            if (wrong is not null) error.WriteLine(wrong);
        }

        var file = program.Options + "[--scheme <name>=<folder> …] <file" + program.Extension + ">";
        output.WriteLine($"Usage: {program.Name} --version | run {file} [name=value …] | check {file}");
        output.WriteLine("  run       compiles the file and runs it, giving its parameters the values after it, each a string");
        output.WriteLine("  check     compiles the file and reports its errors, without running it");
        output.WriteLine("  --scheme  names a folder of native modules, over weblingo.json's, in the file's folder");
        foreach (var line in program.OptionsHelp) output.WriteLine(line);
        return args.Length == 0 ? Success : UsageError;
    }

    /// <summary>
    /// A script's parameters, after its path: each <c>name=value</c>, its name a name, its value a string, every name given
    /// once (section 3.9); false, with the first argument that is not one, otherwise.
    /// </summary>
    private static bool TryParameters(string[] args, int first, out Dictionary<string, string> parameters, out string wrong)
    {
        parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        wrong = "";
        for (var i = first; i < args.Length; i++)
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

    private static int RunFile(WebLingoCommandLineProgram program, bool run, string path, IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> givenSchemes, TextWriter output, TextWriter error)
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
        if (!WebLingoSchemes.TryLoad(path, givenSchemes, out var schemes, out var wrongConfiguration))
        {
            error.WriteLine(wrongConfiguration);
            return FileUnreadable;
        }
        var textOutput = new TextWriterOutput(output);
        var services = new WebLingoBuiltinRegistry();
        var native = new WebLingoNativeModuleLoader(schemes, services);
        // std:system, a native module installed with the runtime, in the std scheme's folder: its functions the host's
        // built-ins, which every module sees, and which std:system exports (section 3.9).
        if (!native.TryResolve(path, "std:system", out var system, out var why))
        {
            error.WriteLine($"std:system cannot be loaded from {schemes["std"]}: {why}.");
            return FileUnreadable;
        }
        var hostBuiltins = new List<IWebLingoBuiltinFunction>(native.FunctionsOf(system));
        hostBuiltins.AddRange(program.Builtins(textOutput));
        var declarations = new List<WebLingoBuiltinDeclaration>();
        foreach (var builtin in hostBuiltins)
        {
            services.Register(builtin);
            declarations.Add(new WebLingoBuiltinDeclaration(builtin.Name, builtin.Arity));
        }
        var resolver = new FileResolver(path);
        var options = new WebLingoCompilerOptions(declarations) { Resolver = resolver, Native = native };
        var compilation = program.Compile(text, options);
        var compiled = compilation.Result;
        foreach (var diagnostic in compiled.Diagnostics) error.WriteLine(Format(TextOf(diagnostic), diagnostic));
        if (compiled.Module is null) return CompilationFailed;
        if (!run) return Success;

        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
        {
            Services = services,
            Clock = new SystemClock(new DotNetClock()),
            Output = textOutput,
            Parameters = parameters
        };
        // The VM imports modules while the program runs as the compiler imports them, with the same resolver (section 3.9).
        var vm = new WebLingoVirtualMachine { Loader = new WebLingoModuleLoader(options), Components = compilation.Components };
        var result = vm.Execute(compiled.Module, context);
        output.Flush();
        if (result.Succeeded) return Success;
        var located = compilation.Locate(result.Error!);
        error.WriteLine(Format(TextOf(located), located));
        return RuntimeFailed;

        // A diagnostic's file: a module the program imports, a file the program's compiler read itself (a page's include,
        // section 13), or the program's own (section 3.9).
        WebLingoSourceText TextOf(WebLingoDiagnostic diagnostic) =>
            diagnostic.Source is not { } name ? text
            : resolver.Texts.TryGetValue(name, out var imported) ? imported
            : compilation.TextOf(name) ?? text;
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
