using System.Text;
using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Language;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
namespace WebLingo.Cli;

public static partial class WebLingoCommandLine
{
    private const string TestUsage = "test [--list] [--filter <text>] <file.wlg> [more files]";
    private const string ReportTest = "__weblingoReportTest";
    private const string HasTestHook = "__weblingoHasTestHook";
    private const int TestError = 0;
    private const int CleanupError = 1;
    private const int CompletedTest = 2;

    private sealed record TestCase(string Class, WebLingoMethodSyntax Method)
    {
        public string Label(string file) => $"{file}::{Class}.{Method.Name}";
    }

    private static int RunTests(string[] args, TextWriter output, TextWriter error, IWebLingoConsoleInput? input, Action? console = null)
    {
        var listOnly = false;
        string? filter = null;
        var first = 1;
        for (; first < args.Length && args[first].StartsWith('-'); first++)
        {
            if (args[first] == "--")
            {
                first++;
                break;
            }
            switch (args[first])
            {
                case "--list" when !listOnly:
                    listOnly = true;
                    break;
                case "--filter" when filter is null && first + 1 < args.Length && args[first + 1].Length > 0:
                    filter = args[++first];
                    break;
                default:
                    // What was wrong, then how test is used (the owner's request, 21 November 2026, "Enhance Errors About
                    // Command Line").
                    error.WriteLine(args[first] switch
                    {
                        "--list" => "--list may be given only once.",
                        "--filter" when filter is not null => "--filter may be given only once.",
                        "--filter" => "--filter requires the text that the labels of the tests to run contain.",
                        var option => $"'{option}' is not an option of test: --list or --filter <text>."
                    });
                    error.WriteLine($"Usage: weblingo {TestUsage}");
                    return UsageError;
            }
        }
        var notTests = args.Skip(first).FirstOrDefault(path => !path.EndsWith(Scripts.Extension, StringComparison.OrdinalIgnoreCase));
        if (first == args.Length || notTests is not null)
        {
            error.WriteLine(notTests is null ? $"test requires the {Scripts.Extension} files whose tests it runs."
                : $"'{notTests}' is not a {Scripts.Extension} file of tests.");
            error.WriteLine($"Usage: weblingo {TestUsage}");
            return UsageError;
        }

        var selected = 0;
        var passed = 0;
        var failed = 0;
        var exit = Success;
        foreach (var path in args.AsSpan(first))
        {
            if (!TryReadText(path, error, out var source))
            {
                exit = Math.Max(exit, FileUnreadable);
                continue;
            }
            var text = new WebLingoSourceText(source, path);
            var (cases, valid) = DiscoverTests(text, error);
            if (!valid) exit = Math.Max(exit, CompilationFailed);
            var matches = cases.Where(test => filter is null || test.Label(path).Contains(filter, StringComparison.Ordinal)).ToArray();
            selected += matches.Length;
            if (listOnly)
            {
                foreach (var test in matches) output.WriteLine(test.Label(path));
            }
            else if (matches.Length > 0)
            {
                exit = Math.Max(exit, RunTestFile(text, matches, output, error, input, console, ref passed, ref failed));
            }
        }
        if (selected == 0 && exit == Success)
        {
            error.WriteLine($"No tests match the filter {WebLingoValueFormatter.Quote(filter ?? "")}.");
            exit = CompilationFailed;
        }
        if (!listOnly) output.WriteLine($"{passed} passed, {failed} failed.");
        output.Flush();
        return exit;
    }

    private static (List<TestCase> Cases, bool Valid) DiscoverTests(WebLingoSourceText text, TextWriter error)
    {
        var tree = new WebLingoParser(text.Text, text.Name).ParseCompilationUnit();
        var cases = new List<TestCase>();
        foreach (var diagnostic in tree.Diagnostics) WriteDiagnostic(error, text, diagnostic);
        if (tree.Diagnostics.Any(diagnostic => diagnostic.Severity == WebLingoDiagnosticSeverity.Error)) return (cases, false);
        var valid = true;
        foreach (var statement in tree.Root.Statements)
        {
            if (statement is not WebLingoExportSyntax { Declaration: WebLingoClassDeclarationSyntax suite }
                || !suite.Name.EndsWith("Tests", StringComparison.Ordinal)) continue;
            foreach (var method in suite.Methods)
            {
                if (method.IsStatic || method.Name.Length <= 4 || !method.Name.StartsWith("test", StringComparison.Ordinal)
                    || !char.IsUpper(method.Name, 4)) continue;
                var test = new TestCase(suite.Name, method);
                if (method.Parameters.Count == 0)
                {
                    cases.Add(test);
                    continue;
                }
                var at = text.GetLocation(method.NameSpan.Start);
                error.WriteLine($"{text.Name}({at.Line},{at.Column}): Test {test.Label(text.Name)} must declare zero parameters.");
                valid = false;
            }
        }
        if (cases.Count != 0 || !valid) return (cases, valid);
        error.WriteLine($"{text.Name}: No tests found. Export a class ending in Tests with an instance method such as testExample.");
        return (cases, false);
    }

    private static int RunTestFile(WebLingoSourceText text, TestCase[] cases, TextWriter output, TextWriter error,
        IWebLingoConsoleInput? input, Action? console, ref int passed, ref int failed)
    {
        // The wrapper is compiled in memory. Placing it beside the test file preserves the file's import paths.
        var path = text.Name;
        var fullPath = Path.GetFullPath(path);
        var driver = Path.Combine(Path.GetDirectoryName(fullPath)!, $".weblingo-test-{Guid.NewGuid():N}.wlg");
        var mainName = Relative(Directory.GetCurrentDirectory(), driver);
        var none = new Dictionary<string, string>();
        if (TryHost(Scripts, path, mainName, none, output, error, input, console) is not { } host) return FileUnreadable;
        var resolved = Relative(Directory.GetCurrentDirectory(), fullPath);
        host.Resolver.Texts.Add(resolved, new WebLingoSourceText(text.Text, path));
        var reporter = new TestReporter(cases, text, host.Resolver, error);
        var hooks = new TestHooks();
        host.Services.Register(reporter);
        host.Services.Register(hooks);
        var options = host.Options with
        {
            Builtins = [.. host.Options.Builtins, new(reporter.Name, reporter.Arity), new(hooks.Name, hooks.Arity)]
        };
        var wrapper = new WebLingoSourceText(BuildTestScript(Path.GetFileName(fullPath), cases), mainName);
        var compilation = Scripts.Compile(wrapper, options);
        var prepared = new SourceRun(wrapper, host, options, compilation);
        foreach (var diagnostic in compilation.Result.Diagnostics) WriteDiagnostic(error, prepared.TextOf(diagnostic), diagnostic);
        if (compilation.Result.Module is null) return CompilationFailed;

        var loader = new WebLingoModuleLoader(options);
        // A store of std:shared's for each test file's run, its tests sharing it, as a program's run has (section 6.12).
        var context = Context(host, none, prepared.LineOf, WebLingoHeap.DefaultRegexTimeoutMilliseconds) with
        {
            Shared = new DotNetSharedStore(Limits(Scripts, path, none).SharedBytes)
        };
        var result = OnVm(compilation.Result.Module, loader, null, context);
        passed += reporter.Passed;
        failed += reporter.Failed;
        var exit = prepared.Report(result, error);
        return exit == Success && reporter.Failed != 0 ? RuntimeFailed : exit;
    }

    private static string BuildTestScript(string fileName, TestCase[] cases)
    {
        var script = new StringBuilder("import tested from ")
            .Append(WebLingoValueFormatter.Quote("./" + fileName)).AppendLine(";");
        for (var i = 0; i < cases.Length; i++)
        {
            var test = cases[i];
            script.AppendLine($$"""
                {
                    var instance = null;
                    try {
                        instance = new tested.{{test.Class}}();
                        if ({{HasTestHook}}(instance, "beforeEach")) await instance.beforeEach();
                        await instance.{{test.Method.Name}}();
                    } catch (e) {
                        {{ReportTest}}({{i}}, {{TestError}}, e, Error);
                    } finally {
                        if (instance != null && {{HasTestHook}}(instance, "afterEach")) {
                            try {
                                await instance.afterEach();
                            } catch (e) {
                                {{ReportTest}}({{i}}, {{CleanupError}}, e, Error);
                            }
                        }
                    }
                    {{ReportTest}}({{i}}, {{CompletedTest}}, null, Error);
                }
                """);
        }
        return script.ToString();
    }

    private sealed class TestHooks : IWebLingoBuiltinFunction
    {
        public string Name => HasTestHook;
        public int Arity => 2;

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
            out WebLingoValue result, out WebLingoRuntimeError? error)
        {
            var type = heap.ClassOfValue(args[0]);
            result = WebLingoValue.FromBoolean(type.Kind == WebLingoValueKind.Class && args[1].Kind == WebLingoValueKind.String
                && heap.TryGetMethod(type, heap.GetString(args[1]), out _));
            error = null;
            return true;
        }
    }

    private sealed class TestReporter(TestCase[] cases, WebLingoSourceText text, FileResolver resolver, TextWriter output)
        : IWebLingoBuiltinFunction
    {
        private readonly bool[] _failed = new bool[cases.Length];
        public string Name => ReportTest;
        public int Arity => 4;
        public int Passed { get; private set; }
        public int Failed { get; private set; }

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
            out WebLingoValue result, out WebLingoRuntimeError? error)
        {
            result = WebLingoValue.Null;
            error = null;
            if (args[0].Kind != WebLingoValueKind.Int64 || args[0].AsInt64 < 0 || args[0].AsInt64 >= cases.Length
                || args[1].Kind != WebLingoValueKind.Int64 || args[1].AsInt64 is < TestError or > CompletedTest)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "Invalid internal test report.");
                return false;
            }
            var index = (int)args[0].AsInt64;
            var test = cases[index];
            if (args[1].AsInt64 == CompletedTest)
            {
                if (!_failed[index]) Passed++;
                return true;
            }
            if (!_failed[index])
            {
                _failed[index] = true;
                Failed++;
                output.WriteLine($"FAIL {test.Label(text.Name)}");
            }
            var (code, message) = WebLingoExceptions.Uncaught(args[2], args[3], heap);
            var file = text;
            var offset = test.Method.NameSpan.Start;
            if (code == WebLingoDiagnosticCodes.TestAssertionFailed && heap.TryGetField(args[2], "sourceFile", out var source)
                && source.Kind == WebLingoValueKind.String && resolver.Texts.TryGetValue(heap.GetString(source), out var origin)
                && heap.TryGetField(args[2], "sourceOffset", out var position) && position.Kind == WebLingoValueKind.Int64)
            {
                file = origin;
                offset = (int)position.AsInt64;
            }
            var at = file.GetLocation(offset);
            var phase = args[1].AsInt64 == CleanupError ? "afterEach: " : "";
            output.WriteLine($"  {file.Name}({at.Line},{at.Column}): {phase}{code}: {message}");
            return true;
        }
    }
}
