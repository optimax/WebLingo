using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Data.DotNet;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
namespace WebLingo.Cli;

/// <summary>
/// The <c>weblingo</c> command line (specification, section 8.4, "CLI"): <c>run</c>, <c>check</c>, <c>compile</c>,
/// <c>disasm</c> and <c>--version</c>. It is the host: it registers <c>write</c> and <c>writeLn</c>, writing to standard
/// output, compiles a file, and runs it in the VM; or it writes a WLBC file, <c>.wlb</c> (section 5.3), which <c>run</c>
/// runs without compiling anything. Diagnostics go to standard error as
/// <c>file(line,column): ERROR CODE (ExceptionClass): message</c>, with the class if available.
/// Exit codes (section 15.2): 0 success, 1 compilation errors or a
/// WLBC file that cannot be used, 2 a usage error, 3 a runtime error, 4 a file that cannot be read or written.
/// </summary>
public static partial class WebLingoCommandLine
{
    public const int Success = 0;
    public const int CompilationFailed = 1;
    public const int UsageError = 2;
    public const int RuntimeFailed = 3;
    public const int FileUnreadable = 4;

    /// <summary>
    /// The solution's version, every assembly's, as <c>Directory.Build.props</c> sets it, <c>0.20.0</c>: the owner's
    /// answer, 18 November 2026, in place of the milestone each command line named, <c>WebLingo M0.6 (pre-alpha)</c>,
    /// without its <c>-pre-alpha</c> since 21 November 2026 (the owner's request, "Drop Pre-Alpha").
    /// </summary>
    public static string ProductVersion { get; } =
        typeof(WebLingoCommandLine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>
    /// The build number, the build's local time, <c>2026.11.21.2330</c>, as MaxPad makes it, every assembly's file version
    /// (<c>Directory.Build.props</c>; the owner's request, 21 November 2026).
    /// </summary>
    public static string BuildNumber { get; } =
        typeof(WebLingoCommandLine).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "0";

    /// <summary>
    /// The version, then the build number in parentheses, as MaxPad's About box has them: <c>0.20.0 (2026.11.21.2330)</c>.
    /// </summary>
    public static string VersionAndBuild { get; } = $"{ProductVersion} ({BuildNumber})";

    /// <summary>
    /// What <c>weblingo --version</c> prints, and <c>weblingo</c> first without arguments:
    /// <c>WebLingo 0.20.0 (2026.11.21.2330)</c>.
    /// </summary>
    public static string Version { get; } = "WebLingo " + VersionAndBuild;

    /// <summary>A WLBC file's extension (section 5.3).</summary>
    public const string BytecodeExtension = WebLingoBytecodeFile.Extension;

    /// <summary><c>weblingo</c>: scripts, <c>.wlg</c> files, compiled by the WebLingo compiler, and their WLBC files.</summary>
    public static WebLingoCommandLineProgram Scripts { get; } = new("weblingo", Version, ".wlg", _ => [],
        (file, options) => new WebLingoCommandLineCompilation(new WebLingoCompiler(options).Compile(file.Text, file.Name)))
    {
        Bytecode = true,
        RunsFileNamedFirst = true,
        AllowsProcesses = true
    };

    public static int Run(string[] args, TextWriter output, TextWriter error) => Run(Scripts, args, output, error);

    /// <summary>
    /// <c>weblingo</c> with a console's input, which its scripts read, <c>readLine</c> and the others (the owner's answers,
    /// 19 November 2026, "Console Input"): its program gives the console's, a test any other; without one, a read is an
    /// <c>IOError</c>, as a site's is.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error, IWebLingoConsoleInput? input) =>
        Run(Scripts, args, output, error, input);

    /// <summary>
    /// <c>weblingo</c>, given what makes a console for a script that imports <c>std:console</c> where the process has none,
    /// which it calls when it first loads the module to run a script (<see cref="WebLingoConsoleSession.Ensure"/>; the
    /// owner's answers, 20 November 2026, "Console Module", A7), never to check or compile one.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error, IWebLingoConsoleInput? input, Action? console) =>
        Run(Scripts, args, output, error, input, console);

    public static int Run(WebLingoCommandLineProgram program, string[] args, TextWriter output, TextWriter error,
        IWebLingoConsoleInput? input = null, Action? console = null)
    {
        // A file named first is run, weblingo script.wlg name=Anna as weblingo run script.wlg name=Anna (the owner's answer,
        // 18 November 2026, "Minimal WebLingo Installer: Linux", Q1).
        if (program.RunsFileNamedFirst && args.Length >= 1 && (args[0].EndsWith(program.Extension, StringComparison.OrdinalIgnoreCase)
            || program.Bytecode && WebLingoBytecodeFile.IsBytecodeExtension(Path.GetExtension(args[0]))))
        {
            args = ["run", .. args];
        }
        if (args.Length == 1 && args[0] is "--version" or "version")
        {
            output.WriteLine(program.Version);
            return Success;
        }
        if (args.Length >= 1 && args[0] == "test" && ReferenceEquals(program, Scripts))
            return RunTests(args, output, error, input, console);
        if (args.Length == 2 && args[0] == "disasm" && program.Bytecode) return DisassembleFile(args[1], output, error);
        // What was wrong with the command line, written before how the program is used (the owner's request, 21 November
        // 2026, "Enhance Errors About Command Line").
        string? wrong = null;
        if (args.Length >= 2 && (args[0] is "run" or "check" || (args[0] == "compile" && program.Bytecode)))
        {
            // run|check|compile [--scheme name=folder]… [--debug, for compile] [--timeout ms, for run] file
            // [name=value …, for run].
            var schemes = new Dictionary<string, string>(StringComparer.Ordinal);
            var run = args[0] == "run";
            int? regexTimeout = null;
            var debug = false;
            var at = 1;
            while (wrong is null && at < args.Length
                   && (args[at] == "--scheme" || (args[at] == "--timeout" && run)
                       || (args[at] == "--debug" && args[0] == "compile")))
            {
                // The debugger's metadata written into the file, for disasm and debuggers (the owner's request, 22 November
                // 2026, "Debug Info").
                if (args[at] == "--debug")
                {
                    if (debug)
                    {
                        wrong = "--debug may be given only once.";
                        break;
                    }
                    debug = true;
                    at++;
                    continue;
                }
                if (args[at] == "--timeout")
                {
                    if (regexTimeout is not null)
                    {
                        wrong = "--timeout may be given only once.";
                        break;
                    }
                    if (at + 1 == args.Length || !int.TryParse(args[at + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
                        || ms is < 1 or > WebLingoHeap.MaxRegexTimeoutMilliseconds)
                    {
                        wrong = "--timeout requires a whole number of milliseconds from 1 to 2147483646.";
                        break;
                    }
                    regexTimeout = ms;
                    at += 2;
                    continue;
                }
                var equals = at + 1 < args.Length ? args[at + 1].IndexOf('=', StringComparison.Ordinal) : -1;
                if (equals > 0 && WebLingoSchemes.IsName(args[at + 1][..equals]) && equals + 1 < args[at + 1].Length
                    && schemes.TryAdd(args[at + 1][..equals], args[at + 1][(equals + 1)..]))
                {
                    at += 2;
                    continue;
                }
                wrong = at + 1 == args.Length ? "--scheme requires a scheme, name=folder."
                    : $"'{args[at + 1]}' is not a scheme, name=folder, or is given twice.";
            }
            if (wrong is null && at == args.Length) wrong = $"{args[0]} requires the file to {args[0]} after its options.";
            if (wrong is null)
            {
                var path = args[at];
                var bytecode = program.Bytecode && WebLingoBytecodeFile.IsBytecodeExtension(Path.GetExtension(path));
                if (args[0] is "check" or "compile")
                {
                    if (at + 1 == args.Length)
                    {
                        var none = new Dictionary<string, string>();
                        return args[0] == "compile" ? CompileFile(program, path, debug, schemes, error)
                            : bytecode ? RunBytecode(program, false, path, none, schemes, output, error, input)
                            : RunFile(program, false, path, none, schemes, output, error, input);
                    }
                    wrong = path.StartsWith("--", StringComparison.Ordinal) ? NotAnOption(program, path, args[0])
                        : $"{args[0]} takes one file, not '{args[at + 1]}' after it.";
                }
                else if (TryParameters(args, at + 1, out var parameters, out var value))
                {
                    var timeout = regexTimeout ?? WebLingoHeap.DefaultRegexTimeoutMilliseconds;
                    return bytecode ? RunBytecode(program, true, path, parameters, schemes, output, error, input, timeout, console)
                        : RunFile(program, true, path, parameters, schemes, output, error, input, timeout, console);
                }
                else
                {
                    wrong = path.StartsWith("--", StringComparison.Ordinal) ? NotAnOption(program, path, "run")
                        : $"'{value}' is not a parameter's value, name=value, or is given twice.";
                }
            }
        }
        else if (args.Length >= 1)
        {
            wrong = Misuse(program, args);
        }
        return Usage(program, args.Length == 0, wrong, output, error);
    }

    /// <summary>
    /// What was wrong, <paramref name="wrong"/>, written to the standard error, then how the program is used, to its output,
    /// as for any usage error: for a program's own commands, such as <c>wasp</c>'s <c>--root</c>.
    /// </summary>
    public static int ReportMisuse(WebLingoCommandLineProgram program, string wrong, TextWriter output, TextWriter error) =>
        Usage(program, false, wrong, output, error);

    /// <summary>
    /// What was wrong with a command line that names no command the program has, or one without what it takes; the commands
    /// with their files and options are checked where they are read.
    /// </summary>
    private static string Misuse(WebLingoCommandLineProgram program, string[] args)
    {
        var command = args[0];
        if (command is "--version" or "version") return $"{command} takes nothing after it.";
        if (command is "run" or "check" || (command == "compile" && program.Bytecode)) return $"{command} requires the file to {command}.";
        if (command == "disasm" && program.Bytecode)
            return args.Length == 1 ? "disasm requires the bytecode file to show." : $"disasm takes one file, not '{args[2]}' after it.";
        List<string> commands = ["run", "check"];
        if (program.Bytecode) commands.AddRange(["compile", "disasm"]);
        if (ReferenceEquals(program, Scripts)) commands.Add("test");
        commands.AddRange(program.MoreCommands);
        commands.Add("--version");
        var named = "";
        if (program.RunsFileNamedFirst && command.IndexOfAny(['.', '/', '\\']) >= 0)
        {
            named = program.Bytecode
                ? $"; a file named first is run when it is a {program.Extension} or {WebLingoBytecodeFile.Extension} file"
                : $"; a file named first is run when it is a {program.Extension} file";
        }
        return $"'{command}' is not a command: {string.Join(", ", commands[..^1])} or {commands[^1]}{named}.";
    }

    /// <summary>An option the command does not take, and the command that takes it, where one does.</summary>
    private static string NotAnOption(WebLingoCommandLineProgram program, string option, string command)
    {
        var whose = option == "--timeout" ? "run"
            : option == "--debug" && program.Bytecode ? "compile"
            : null;
        return whose is null || whose == command ? $"'{option}' is not an option of {command}."
            : $"'{option}' is not an option of {command}, but of {whose}.";
    }

    /// <summary>
    /// How the program is used, to its output, after what was wrong, if anything, to the standard error; without arguments,
    /// its version first (the owner's answer, 18 November 2026), and not an error.
    /// </summary>
    private static int Usage(WebLingoCommandLineProgram program, bool noArguments, string? wrong, TextWriter output, TextWriter error)
    {
        if (wrong is not null) error.WriteLine(wrong);
        if (noArguments) output.WriteLine(program.Version);
        var file = $"<file{program.Extension}>";
        var options = program.Options + "[--scheme <name>=<folder> …]";
        var bytecodeUsage = program.Bytecode
            ? $" | compile [--debug] [--scheme <name>=<folder> …] <file{program.Extension}>"
              + " | disasm <bytecode-file>"
            : "";
        output.WriteLine($"Usage: {program.Name} --version | run {options} [--timeout <ms>] {file} [name=value …]"
            + $" | check {options} {file}{bytecodeUsage}" + (ReferenceEquals(program, Scripts) ? $" | {TestUsage}" : ""));
        output.WriteLine("  run       compiles the file and runs it, giving its parameters the values after it, each a string");
        if (program.RunsFileNamedFirst) output.WriteLine($"            {program.Name} <file{program.Extension}> [name=value …] is the same as run");
        output.WriteLine("  check     compiles the file and reports its errors, without running it");
        if (ReferenceEquals(program, Scripts))
            output.WriteLine("  test      runs or lists tests discovered in exported test classes");
        if (program.Bytecode)
        {
            output.WriteLine("  compile   writes the file's bytecode, a .wlb file beside it");
            output.WriteLine("            --debug also writes the debugger's names and positions, which disasm shows");
            output.WriteLine("  disasm    shows compiled bytecode as text");
            output.WriteLine("            run, check and disasm accept .wlb files, and earlier builds' .wlbr and .wlbc files");
        }
        output.WriteLine($"  --scheme  names a folder of native modules, over {program.Configuration}'s, in the file's folder");
        output.WriteLine("  --timeout sets the regular expression match timeout for run, in milliseconds (default 1000)");
        foreach (var line in program.OptionsHelp) output.WriteLine(line);
        return noArguments ? Success : UsageError;
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

    internal static int RunDebugFile(WebLingoCommandLineProgram program, string path, string[] args, TextWriter output, TextWriter error,
        WebLingoDebugSession debugger)
    {
        if (!TryParameters(args, 0, out var parameters, out var wrong))
        {
            error.WriteLine($"'{wrong}' is not a parameter's value, name=value, or is given twice.");
            return UsageError;
        }
        var source = PrepareSource(program, path, new Dictionary<string, string>(), output, error, null, true, out var exit);
        if (source is null) return exit;
        // A position located as a runtime error is, in the file it is in, a page's include's in the include (section 5.7).
        debugger.SetSources((name, offset) =>
        {
            var at = source.Compilation.Locate(new WebLingoDiagnostic("", WebLingoDiagnosticSeverity.Error, "", new WebLingoSourceSpan(offset, 0))
            {
                Source = name
            });
            return (source.TextOf(at), at.Span.Start);
        }, () => { output.Flush(); error.Flush(); });
        var context = Context(source.Host, parameters, source.LineOf, WebLingoHeap.DefaultRegexTimeoutMilliseconds) with
        {
            Cancellation = debugger.Cancellation,
            SchedulerHost = debugger.Scheduler
        };
        var vm = new WebLingoVirtualMachine
        {
            Loader = new WebLingoModuleLoader(source.Options), Components = source.Compilation.Components, Debugger = debugger.Program
        };
        var result = vm.Execute(source.Compilation.Result.Module!, context);
        output.Flush();
        return debugger.Cancellation.IsCancellationRequested ? Success : source.Report(result, error);
    }

    private static int RunFile(WebLingoCommandLineProgram program, bool run, string path, IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> givenSchemes, TextWriter output, TextWriter error, IWebLingoConsoleInput? input,
        int regexTimeout = WebLingoHeap.DefaultRegexTimeoutMilliseconds, Action? console = null)
    {
        var source = PrepareSource(program, path, givenSchemes, output, error, input, false, out var exit, run ? console : null);
        if (source is null || !run) return exit;
        // The VM imports modules with the same resolver the compiler used (section 3.9).
        var context = Context(source.Host, parameters, source.LineOf, regexTimeout);
        var loader = new WebLingoModuleLoader(source.Options);
        var result = OnVm(source.Compilation.Result.Module!, loader, source.Compilation.Components, context);
        output.Flush();
        return source.Report(result, error);
    }

    private static SourceRun? PrepareSource(WebLingoCommandLineProgram program, string path,
        IReadOnlyDictionary<string, string> givenSchemes, TextWriter output, TextWriter error, IWebLingoConsoleInput? input,
        bool debugging, out int exit, Action? console = null)
    {
        exit = FileUnreadable;
        if (!TryReadText(path, error, out var source)) return null;
        var text = new WebLingoSourceText(source, path);
        if (TryHost(program, path, path, givenSchemes, output, error, input, console) is not { } host) return null;
        var options = debugging ? host.Options with { Debugging = true } : host.Options;
        var compilation = program.Compile(text, options);
        var prepared = new SourceRun(text, host, options, compilation);
        foreach (var diagnostic in compilation.Result.Diagnostics) WriteDiagnostic(error, prepared.TextOf(diagnostic), diagnostic);
        exit = compilation.Result.Module is null ? CompilationFailed : Success;
        return exit == Success ? prepared : null;
    }

    private sealed record SourceRun(WebLingoSourceText Text, Host Host, WebLingoCompilerOptions Options,
        WebLingoCommandLineCompilation Compilation)
    {
        public int Report(WebLingoExecutionResult result, TextWriter error)
        {
            if (result.Succeeded) return Success;
            var located = Compilation.Locate(result.Error!);
            WriteDiagnostic(error, TextOf(located), located);
            return RuntimeFailed;
        }

        // A diagnostic's file: a module the program imports, a file the program's compiler read itself (a page's include,
        // section 13), or the program's own (section 3.9).
        public WebLingoSourceText TextOf(WebLingoDiagnostic diagnostic) =>
            diagnostic.Source is not { } name ? Text
            : Host.Resolver.Texts.TryGetValue(name, out var imported) ? imported
            : Compilation.TextOf(name) ?? Text;

        // A position's line, in the file it is in, located as an error is, for a message that names it (section 3.8).
        public int LineOf(string? source, int offset)
        {
            var at = Compilation.Locate(new WebLingoDiagnostic("", WebLingoDiagnosticSeverity.Error, "", new WebLingoSourceSpan(offset, 0))
            {
                Source = source
            });
            return TextOf(at).GetLocation(at.Span.Start).Line;
        }
    }

    /// <summary>
    /// <c>compile</c>: the file compiled, written beside it (section 5.3), its files
    /// named from its folder, the program's own by its name alone, so that the file holds none of the host's paths; with
    /// <paramref name="debug"/>, compiled for a debugger, the debugger's metadata in its debugging section.
    /// </summary>
    private static int CompileFile(WebLingoCommandLineProgram program, string path, bool debug,
        IReadOnlyDictionary<string, string> givenSchemes, TextWriter error)
    {
        if (!TryReadText(path, error, out var source)) return FileUnreadable;
        var text = new WebLingoSourceText(source, path);
        if (TryHost(program, path, path, givenSchemes, TextWriter.Null, error, null) is not { } host) return FileUnreadable;
        var options = debug ? host.Options with { Debugging = true } : host.Options;
        var (register, diagnostics) = new WebLingoCompiler(options).Compile(source, path);
        foreach (var diagnostic in diagnostics)
        {
            var file = diagnostic.Source is { } name && host.Resolver.Texts.TryGetValue(name, out var imported) ? imported : text;
            WriteDiagnostic(error, file, diagnostic);
        }
        if (register is null) return CompilationFailed;

        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        string FromFolder(string name)
        {
            var renamed = name == path ? Path.GetFileName(path) : IsScheme(name) ? name : Relative(folder, name);
            originals.TryAdd(renamed, name);
            return renamed;
        }
        WebLingoSourceText? TextOf(string? name) =>
            name is null ? text
            : originals.TryGetValue(name, out var original) && host.Resolver.Texts.TryGetValue(original, out var imported) ? imported
            : null;
        var bytes = WebLingoBytecodeFile.Write(Renamed(register, FromFolder), TextOf);
        var target = Path.ChangeExtension(path, WebLingoBytecodeFile.Extension);
        try
        {
            File.WriteAllBytes(target, bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            error.WriteLine($"{target}: cannot write the file: {exception.Message}");
            return FileUnreadable;
        }
        return Success;
    }

    /// <summary>
    /// <c>run</c> or <c>check</c> of WLBC: the file read and verified (section 5.3), its files named again from its
    /// folder, the program's own as the file beside it, and, for <c>run</c>, the native modules it names loaded and it run,
    /// a runtime error located from the file's source positions.
    /// </summary>
    private static int RunBytecode(WebLingoCommandLineProgram program, bool run, string path, IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> givenSchemes, TextWriter output, TextWriter error, IWebLingoConsoleInput? input,
        int regexTimeout = WebLingoHeap.DefaultRegexTimeoutMilliseconds, Action? console = null)
    {
        if (!TryRead(path, error, out var bytes, out var register, out var positions)) return Failed(bytes);
        if (!run)
        {
            // Its configuration checked, as for a source file.
            if (!WebLingoSchemes.TryLoad(path, program.Configuration, givenSchemes, out _, out _, out _, out _, out _,
                out _, out _, out var wrongConfiguration))
            {
                error.WriteLine(wrongConfiguration);
                return FileUnreadable;
            }
            return Success;
        }
        var main = register.Name;
        var mainName = path[..^Path.GetFileName(path).Length] + main;
        if (TryHost(program, path, mainName, givenSchemes, output, error, input, console) is not { } host) return FileUnreadable;
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        string FromHere(string name) =>
            name == main ? mainName : IsScheme(name) ? name : Relative(Directory.GetCurrentDirectory(), Path.Combine(folder, name));
        register = Renamed(register, FromHere);
        var located = new List<WebLingoSourcePositions>();
        foreach (var table in positions) located.Add(table with { Source = table.Source is { } source ? FromHere(source) : null });
        // The native modules it names, which its compiler had loaded, so that their built-ins are the host's (section 3.9).
        foreach (var unit in register.Units)
        {
            if (unit.IsNative && !host.Native.TryResolve(mainName, unit.Name, out _, out var why))
            {
                error.WriteLine($"{unit.Name} cannot be loaded: {why}.");
                return FileUnreadable;
            }
        }
        var loader = new WebLingoModuleLoader(host.Options);
        var context = Context(host, parameters, LineOf, regexTimeout);
        var result = OnVm(register, loader, null, context);
        output.Flush();
        if (result.Succeeded) return Success;
        var diagnostic = result.Error!;
        var file = diagnostic.Source ?? mainName;
        var location = located.Find(table => table.Source == diagnostic.Source) is { } positionsOf
            ? positionsOf.GetLocation(diagnostic.Span.Start)
            : host.Resolver.Texts.TryGetValue(file, out var imported) ? imported.GetLocation(diagnostic.Span.Start)
            : new WebLingoSourceLocation(diagnostic.Span.Start, 1, 1);
        WriteDiagnostic(error, file, location, diagnostic);
        return RuntimeFailed;

        // A file that cannot be read is one thing, and one that is not WLBC a reader can use is another.
        static int Failed(byte[]? bytes) => bytes is null ? FileUnreadable : CompilationFailed;

        // A position's line, from the file's source positions, as an error is located, for a message that names it (section 3.8).
        int LineOf(string? source, int offset) =>
            (located.Find(table => table.Source == source) is { } positionsOf ? positionsOf.GetLocation(offset)
                : host.Resolver.Texts.TryGetValue(source ?? mainName, out var imported) ? imported.GetLocation(offset)
                : new WebLingoSourceLocation(offset, 1, 1)).Line;
    }

    /// <summary>
    /// A module run on the VM, named only in a method of its own, which .NET does not inline: it loads the VM's assembly
    /// when it compiles a method naming it, so that <c>compile</c>, <c>check</c> and <c>disasm</c> load none.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WebLingoExecutionResult OnVm(WebLingoCompiledModule module, IWebLingoModuleLoader loader,
        IWebLingoModuleLoader? components, WebLingoExecutionContext context) =>
        new WebLingoVirtualMachine { Loader = loader, Components = components }.Execute(module, context);

    /// <summary><c>disasm</c>: compiled bytecode as text.</summary>
    private static int DisassembleFile(string path, TextWriter output, TextWriter error)
    {
        if (!TryRead(path, error, out var bytes, out var register, out var positions))
            return bytes is null ? FileUnreadable : CompilationFailed;
        // Its line and column tables, for the debugging section's positions, where the file has one.
        output.Write(WebLingoDisassembler.Format(register, positions));
        return Success;
    }

    /// <summary>
    /// A WLBC file read and verified, its module; false, the reason written, and <paramref name="bytes"/> null if the file
    /// cannot be read. A file of the stack VM's, which WebLingo had until 23 November 2026, is one it cannot use.
    /// </summary>
    private static bool TryRead(string path, TextWriter error, out byte[]? bytes, [NotNullWhen(true)] out WebLingoCompiledModule? register,
        out IReadOnlyList<WebLingoSourcePositions> positions)
    {
        (register, positions) = (null, []);
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            error.WriteLine($"{path}: cannot read the file: {exception.Message}");
            bytes = null;
            return false;
        }
        if (WebLingoBytecodeFile.TryReadKind(bytes, out var kind, out var why))
        {
            // A file of the stack VM's is refused as such, whatever its name; a register VM's named as a stack VM's file, too.
            if (kind == WebLingoBytecodeKind.Register && WebLingoBytecodeFile.KindForExtension(Path.GetExtension(path)) is { } expected
                && kind != expected)
                why = $"The file extension is for the {VmName(expected)} VM, but its header names the {VmName(kind)} VM.";
            else if (WebLingoBytecodeFile.TryReadRegister(bytes, out register, out positions, out why))
                return true;
        }
        WebLingoConsoleText.WriteErrorLine(error, path, $"{WebLingoDiagnosticCodes.BytecodeVerificationFailure}: {why}");
        return false;
    }

    private static string VmName(WebLingoBytecodeKind kind) => kind == WebLingoBytecodeKind.Register ? "register" : "stack";

    private static bool TryReadText(string path, TextWriter error, out string source)
    {
        try
        {
            source = File.ReadAllText(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            error.WriteLine($"{path}: cannot read the file: {exception.Message}");
            source = "";
            return false;
        }
    }

    /// <summary>What a command line gives the programs it compiles and runs: its built-ins, its native modules and its files.</summary>
    private sealed record Host(WebLingoBuiltinRegistry Services, WebLingoNativeModuleLoader Native, WebLingoCompilerOptions Options,
        FileResolver Resolver, IWebLingoTextOutput Output, IWebLingoTextOutput ErrorOutput, IWebLingoNetwork Network, IWebLingoDatabases Databases,
        IWebLingoMail Mail, IWebLingoFileSystem Files,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Allowed, IWebLingoEnvironment Environment, IWebLingoConsoleInput? ConsoleInput,
        IWebLingoConsole? Console,
        IWebLingoProcessRunner? Processes);

    /// <summary>
    /// The host for the file <paramref name="path"/>, whose program's own file is named <paramref name="mainName"/>: its
    /// schemes, from its program's configuration in its folder and those given, <c>std:system</c> loaded, and the program's built-ins;
    /// null, the reason written, if they cannot be had.
    /// </summary>
    private static Host? TryHost(WebLingoCommandLineProgram program, string path, string mainName, IReadOnlyDictionary<string, string> givenSchemes,
        TextWriter output, TextWriter error, IWebLingoConsoleInput? input, Action? console = null)
    {
        if (!WebLingoSchemes.TryLoad(path, program.Configuration, givenSchemes, out var schemes, out var allowedHosts, out var databases,
            out var folders, out var allowed, out var env, out var mail, out var wrongConfiguration))
        {
            error.WriteLine(wrongConfiguration);
            return null;
        }
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        if (program.Root is { } root && Path.GetFullPath(root) != folder
            && !WebLingoSchemes.TryLoadFolder(root, program.Configuration, givenSchemes, out _, out _, out _, out _, out _,
                out _, out _, out _, out _, out _, out wrongConfiguration))
        {
            error.WriteLine(wrongConfiguration);
            return null;
        }
        var textOutput = new TextWriterOutput(output);
        var services = new WebLingoBuiltinRegistry();
        // std:console a script's, but not a site's, which has no console (section 3.9, "The console: std:console").
        var native = new WebLingoNativeModuleLoader(schemes, services)
        {
            Refused = program.HasConsole ? new Dictionary<string, string>() : WebLingoNativeModuleLoader.WithoutConsole,
            // A script that imports std:console, run, is a console program: given a console where it has none (section 3.9).
            Loaded = console is null ? null : name => { if (name == "std:console") console(); }
        };
        // std:system, a native module installed with the runtime, in the std scheme's folder: its functions the host's
        // built-ins, which every module sees, and which std:system exports (section 3.9).
        if (!native.TryResolve(path, "std:system", out var system, out var why))
        {
            error.WriteLine($"std:system cannot be loaded from {schemes["std"]}: {why}.");
            return null;
        }
        var hostBuiltins = new List<IWebLingoBuiltinFunction>(native.FunctionsOf(system));
        hostBuiltins.AddRange(program.Builtins(textOutput));
        var declarations = new List<WebLingoBuiltinDeclaration>();
        foreach (var builtin in hostBuiltins)
        {
            services.Register(builtin);
            declarations.Add(new WebLingoBuiltinDeclaration(builtin.Name, builtin.Arity));
        }
        var resolver = new FileResolver(mainName);
        // Any host and any file for a script's, only those its configuration allows for a page's (sections 7.9 and 7.13); the
        // databases it names, their files from its folder (section 7.11), a script's own checked as its files are.
        var files = program.ReachesAnyFile ? null : new DotNetFileCapabilities(program.Root ?? folder, folders.Read, folders.Write);
        return new Host(services, native, new WebLingoCompilerOptions(declarations) { Resolver = resolver, Native = native }, resolver,
            textOutput, new TextWriterOutput(error), new DotNetNetwork(program.ReachesAnyHost ? null : allowedHosts),
            new DotNetDatabases(databases, folder, new DotNetSecrets()) { Files = files },
            // The mail servers its configuration names, their passwords read where it keeps them (section 7.17).
            new DotNetMail(mail, new DotNetSecrets()),
            new DotNetFileSystem(files), allowed,
            // All of the machine's environment for a script's, what its configuration's "env" allows for a page's (section 7.15).
            program.ReachesAllEnvironment ? new DotNetEnvironment() : new DotNetEnvironment(env.MachineName, env.Variables, env.Platform),
            // The console itself, which std:console's Console controls: a script's, never a site's (section 3.9).
            input, program.HasConsole ? new DotNetConsole() : null, program.AllowsProcesses ? new DotNetProcessRunner() : null);
    }

    private static WebLingoExecutionContext Context(Host host, IReadOnlyDictionary<string, string> parameters,
        Func<string?, int, int> lines, int regexTimeout) =>
        new(new WebLingoWorkerId(0))
        {
            Lines = lines,
            RegexTimeoutMilliseconds = regexTimeout,
            Services = host.Services,
            Clock = new SystemClock(new DotNetClock()),
            WallClock = new DotNetClock(),
            TimeZone = new DotNetTimeZone(),
            Output = host.Output,
            ErrorOutput = host.ErrorOutput,
            Parameters = parameters,
            FileSystem = host.Files,
            Network = host.Network,
            Allowed = host.Allowed,
            Crypto = new DotNetCrypto(),
            Environment = host.Environment,
            ConsoleInput = host.ConsoleInput,
            Console = host.Console,
            Databases = host.Databases,
            Mail = host.Mail,
            Processes = host.Processes,
            SchedulerHost = new DotNetSchedulerHost()
        };

    /// <summary><c>scheme:module</c>, a native module's name (section 3.9), which is not a file's, a scheme being two or more letters.</summary>
    private static bool IsScheme(string name)
    {
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        return colon > 1 && WebLingoSchemes.IsName(name[..colon]);
    }

    /// <summary><paramref name="name"/>, a file's path, from <paramref name="folder"/>, with <c>/</c> between its folders; as it is if it has none.</summary>
    private static string Relative(string folder, string name)
    {
        try
        {
            return Path.GetRelativePath(folder, Path.GetFullPath(name)).Replace('\\', '/');
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return name;
        }
    }

    /// <summary>
    /// A module with its files renamed (section 3.9): its own name, its files', its functions' sources, and the names of the
    /// functions that run its files, which are their files'.
    /// </summary>
    private static WebLingoCompiledModule Renamed(WebLingoCompiledModule module, Func<string, string> rename)
    {
        var files = Files(module.Name, module.Units);
        return module with
        {
            Name = rename(module.Name),
            EntryPoint = Renamed(module.EntryPoint, files, rename),
            Functions = [.. module.Functions.Select(function => Renamed(function, files, rename))],
            Units = [.. module.Units.Select(unit => unit.IsNative ? unit : unit with { Name = rename(unit.Name) })]
        };
    }

    private static WebLingoFunction Renamed(WebLingoFunction function, HashSet<string> files, Func<string, string> rename) =>
        function with
        {
            Name = files.Contains(function.Name) ? rename(function.Name) : function.Name,
            Source = function.Source is { } source ? rename(source) : null
        };

    /// <summary>The names of a program's files, its own and those it imports, but native modules'.</summary>
    private static HashSet<string> Files(string name, IReadOnlyList<WebLingoUnitInfo> units)
    {
        var files = new HashSet<string>(StringComparer.Ordinal) { name };
        foreach (var unit in units)
        {
            if (!unit.IsNative) files.Add(unit.Name);
        }
        return files;
    }

    /// <summary>
    /// The files a program imports (specification, section 3.9): a path from the importing file's folder, <c>./</c>
    /// being that folder and <c>/</c> the drive's root, <c>.wlg</c> added to one without an extension. A file is named
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
                if (Path.GetExtension(full).Length == 0) full += ".wlg";
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

    /// <summary>A diagnostic as plain text, with the source location.</summary>
    public static string Format(WebLingoSourceText source, WebLingoDiagnostic diagnostic) =>
        Format(source.Name, source.GetLocation(diagnostic.Span.Start), diagnostic);

    /// <summary>A diagnostic as a line, in the file <paramref name="name"/>, at <paramref name="location"/>.</summary>
    public static string Format(string name, WebLingoSourceLocation location, WebLingoDiagnostic diagnostic) =>
        $"{name}({location.Line},{location.Column}): {SeverityOf(diagnostic)} {Describe(diagnostic)}";

    private static string SeverityOf(WebLingoDiagnostic diagnostic) => diagnostic.Severity switch
    {
        WebLingoDiagnosticSeverity.Error => "ERROR",
        WebLingoDiagnosticSeverity.Warning => "warning",
        _ => "info"
    };

    private static string Describe(WebLingoDiagnostic diagnostic)
    {
        var exception = diagnostic.Severity == WebLingoDiagnosticSeverity.Error
            ? diagnostic.ExceptionClass ?? WebLingoErrorClasses.NameOf(diagnostic.Code) : null;
        return exception is null ? $"{diagnostic.Code}: {diagnostic.Message}"
            : $"{diagnostic.Code} ({exception}): {diagnostic.Message}";
    }

    private static void WriteDiagnostic(TextWriter error, WebLingoSourceText source, WebLingoDiagnostic diagnostic) =>
        WriteDiagnostic(error, source.Name, source.GetLocation(diagnostic.Span.Start), diagnostic);

    private static void WriteDiagnostic(TextWriter error, string name, WebLingoSourceLocation location, WebLingoDiagnostic diagnostic)
    {
        if (diagnostic.Severity == WebLingoDiagnosticSeverity.Error)
            WebLingoConsoleText.WriteErrorLine(error, $"{name}({location.Line},{location.Column})", Describe(diagnostic));
        else error.WriteLine(Format(name, location, diagnostic));
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
