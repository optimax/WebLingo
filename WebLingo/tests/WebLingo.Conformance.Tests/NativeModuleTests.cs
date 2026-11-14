using WebLingo.Cli;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using WebLingo.VM.Stack;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// Native modules' functions (specification, section 3.9, "Native modules"; section 9, "Native modules", part 1): a test
/// module, <c>WebLingo.Native.TestModule</c>, installed as <c>test.dll</c> in a scheme's folder, <c>native/</c>, imported by
/// programs run on both VMs, which must give the same; and through <c>weblingo</c>, its schemes given by
/// <c>weblingo.json</c> and <c>--scheme</c>.
/// </summary>
public sealed class NativeModuleTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-native-" + Guid.NewGuid().ToString("N"));
    private readonly string _native;

    public NativeModuleTests()
    {
        _native = Path.Combine(_folder, "native");
        Directory.CreateDirectory(_native);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "WebLingo.Native.TestModule.dll"), Path.Combine(_native, "test.dll"));
    }

    public void Dispose()
    {
        // The loaded assembly stays loaded, and on Windows its file locked: leave what cannot be deleted.
        try { Directory.Delete(_folder, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData("import t from \"native:test\"; writeLn([t.twice(21), t.greet(\"Ann\")]);", "[42, \"Hello, Ann!\"]")]
    [InlineData("import { twice, greet } from \"native:test\"; writeLn([twice(2), greet(1)]);", "[4, \"Hello, 1!\"]")]
    [InlineData("import t from \"native/test.dll\"; writeLn(t.twice(5));", "10")]
    [InlineData("var t = await Module.import(\"native:test\"); writeLn(t.twice(3));", "6")]
    [InlineData("import t from \"native:test\"; writeLn(await t.ready(7));", "7")]
    [InlineData("import t from \"native:test\"; try { t.fail(); } catch (e) { writeLn(e.code); }", "WL3001")]
    [InlineData("import t from \"native:test\"; import u from \"native/test.dll\"; writeLn([t == u, t]);", "[true, module native:test]")]
    [InlineData("import t from \"native:test\"; var u = await Module.import(\"native:test\"); writeLn(t == u);", "true")]
    public void ANativeModulesFunctionsAreItsExports(string source, string printed)
    {
        Assert.Equal(("", printed + "\n", ""), Run(source));
    }

    [Theory]
    [InlineData("import t from \"native:nope\";", "WL2015", "The module 'native:nope' cannot be imported: there is no such native module.")]
    [InlineData("import t from \"native:../test\";", "WL2015",
        "The module 'native:../test' cannot be imported: a native module's name is a file's name in its scheme's folder.")]
    [InlineData("import t from \"elsewhere/test.dll\";", "WL2015",
        "The module 'elsewhere/test.dll' cannot be imported: it is not in a configured scheme's folder.")]
    public void ANativeModuleThatCannotBeFoundIsReported(string source, string code, string message)
    {
        Directory.CreateDirectory(Path.Combine(_folder, "elsewhere"));
        File.Copy(Path.Combine(_native, "test.dll"), Path.Combine(_folder, "elsewhere", "test.dll"));
        Assert.Equal($"{code}: {message}", Run(source).Diagnostics);
    }

    [Theory]
    [InlineData("import t from \"native:test\"; t.throws();", "WL3001: 'native:test.throws' failed: thrown by the test module")]
    [InlineData("import t from \"native:test\"; t.twice(\"x\");", "WL3001: 'twice' takes an integer.")]
    [InlineData("import t from \"native:test\"; t.twice(1, 2);", "WL2006")]
    [InlineData("import t from \"native:test\"; t.nothing();", "WL3006")]
    public void ANativeFunctionsFailureIsARuntimeError(string source, string error)
    {
        Assert.StartsWith(error, Run(source).Error);
    }

    [Fact]
    public void WeblingoTakesItsSchemesFromItsCommandLineAndFromWeblingoJson()
    {
        var script = Path.Combine(_folder, "script.wl");
        File.WriteAllText(script, "import t from \"lib:test\"; writeLn(t.twice(4));");
        var (exit, output, error) = Weblingo(["run", "--scheme", "lib=" + _native, script]);
        Assert.Equal((WebLingoCommandLine.Success, "8\n", ""), (exit, output, error));

        // weblingo.json, in the script's folder, its folder from the file's; the command line over it.
        File.WriteAllText(Path.Combine(_folder, WebLingoSchemes.ConfigurationFile), "{ \"schemes\": { \"lib\": \"native\" } }");
        Assert.Equal((WebLingoCommandLine.Success, "8\n", ""), Weblingo(["run", script]));
        Assert.Equal(WebLingoCommandLine.CompilationFailed, Weblingo(["check", "--scheme", "lib=" + _folder, script]).Exit);

        File.WriteAllText(Path.Combine(_folder, WebLingoSchemes.ConfigurationFile), "{ \"schemes\": { \"l\": \"native\" } }");
        (exit, _, error) = Weblingo(["run", script]);
        Assert.Equal(WebLingoCommandLine.FileUnreadable, exit);
        Assert.Contains("'l' is not a scheme's name, given a folder.", error);
        File.Delete(Path.Combine(_folder, WebLingoSchemes.ConfigurationFile));

        (exit, _, error) = Weblingo(["run", "--scheme", "lib", script]);
        Assert.Equal(WebLingoCommandLine.UsageError, exit);
        Assert.StartsWith("'lib' is not a scheme, name=folder, or is given twice.", error);
    }

    [Theory]
    // std:tasks, a standard module installed as std/tasks.dll: Task's combinators as its exports (sections 3.8 and 3.9).
    [InlineData("import { all, any, timeout } from \"std:tasks\"; writeLn([await all([1, delay(1), 3]), await any([delay(5), 2]), "
        + "await timeout(7, 100)]);", "[[1, null, 3], 2, 7]")]
    [InlineData("import tasks from \"std:tasks\"; writeLn([tasks, await tasks.all([])]);", "[module std:tasks, []]")]
    [InlineData("import { all } from \"std:tasks\"; var messages = []; try { all(5); } catch (e) { messages.push(e.message); } "
        + "try { Task.all(5); } catch (e) { messages.push(e.message); } writeLn(messages[0] == messages[1]);", "true")]
    [InlineData("import { timeout } from \"std:tasks\"; try { await timeout(delay(50), 1); } catch (e) { writeLn(e.code); }", "WL3010")]
    public void StdTasksGivesTasksCombinators(string source, string printed)
    {
        Assert.Equal(("", printed + "\n", ""), Run(source));
    }

    [Fact]
    public void AStandardModuleThatIsNotInstalledIsReported()
    {
        Assert.Equal("WL2015: The module 'std:nothing' cannot be imported: there is no such standard module.",
            Run("import n from \"std:nothing\";").Diagnostics);
    }

    [Fact]
    public void StdSystemIsANativeModuleThatAnotherImplementationMayReplace()
    {
        // weblingo loads std:system from std/system.dll, beside it unless the std scheme names another folder (section 3.9).
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "std", "system.dll")));
        var script = Path.Combine(_folder, "script.wl");
        File.WriteAllText(script, "import s from \"std:system\"; writeLn([s, s.writeLn == writeLn]);");
        Assert.Equal((WebLingoCommandLine.Success, "[module std:system, true]\n", ""), Weblingo(["run", script]));

        // A std folder without system.dll: the command stops before it compiles anything.
        var empty = Path.Combine(_folder, "empty");
        Directory.CreateDirectory(empty);
        var (exit, _, error) = Weblingo(["run", "--scheme", "std=" + empty, script]);
        Assert.Equal(WebLingoCommandLine.FileUnreadable, exit);
        Assert.StartsWith($"std:system cannot be loaded from {empty}: there is no such standard module.", error);

        // Another implementation of std:system: its functions are the built-ins every module sees, and std:system's exports.
        var other = Path.Combine(_folder, "other");
        Directory.CreateDirectory(other);
        File.Copy(Path.Combine(_native, "test.dll"), Path.Combine(other, "system.dll"));
        File.WriteAllText(script, "writeLn(1);");
        Assert.Equal(WebLingoCommandLine.CompilationFailed, Weblingo(["check", "--scheme", "std=" + other, script]).Exit);
        File.WriteAllText(script, "import { twice } from \"std:system\"; if (twice(2) == 4) { throws(); }");
        (exit, _, error) = Weblingo(["run", "--scheme", "std=" + other, script]);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains("error WL3001: 'throws' failed: thrown by the test module", error);
    }

    private static (int Exit, string Output, string Error) Weblingo(string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>
    /// A program, named as a file in the test's folder, compiled and run on both VMs, which must give the same: its
    /// diagnostics (each its code and message), what it writes, and its runtime error.
    /// </summary>
    private (string Diagnostics, string Output, string Error) Run(string source)
    {
        var name = Path.Combine(_folder, "program.wl");
        var schemes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["native"] = _native, ["std"] = Path.Combine(AppContext.BaseDirectory, "std")
        };

        var registerOutput = new WebLingoCollectedOutput();
        var (registerOptions, registerServices) = Options(schemes, registerOutput);
        var registerCompiled = new WebLingoCompiler(registerOptions).Compile(source, name);
        var registerResult = registerCompiled.Module is { } module
            ? new WebLingoVirtualMachine { Loader = new WebLingoModuleLoader(registerOptions) }.Execute(module, Context(registerServices))
            : null;
        var register = (Describe(registerCompiled.Diagnostics), registerOutput.Text, Describe(registerResult?.Error));

        var stackOutput = new WebLingoCollectedOutput();
        var (stackOptions, stackServices) = Options(schemes, stackOutput);
        var stackCompiled = new WebLingoStackCompiler(stackOptions).Compile(source, name);
        var stackResult = stackCompiled.Module is { } stackModule
            ? new WebLingoStackVirtualMachine { Loader = new WebLingoModuleLoader(stackOptions) }.Execute(stackModule, Context(stackServices))
            : null;
        var stack = (Describe(stackCompiled.Diagnostics), stackOutput.Text, Describe(stackResult?.Error));

        Assert.Equal(register, stack);
        return register;
    }

    private static (WebLingoCompilerOptions, WebLingoBuiltinRegistry) Options(Dictionary<string, string> schemes, IWebLingoTextOutput output)
    {
        var services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output));
        var declarations = new List<WebLingoBuiltinDeclaration>();
        foreach (var builtin in services.Builtins) declarations.Add(new WebLingoBuiltinDeclaration(builtin.Name, builtin.Arity));
        return (new WebLingoCompilerOptions(declarations) { Native = new WebLingoNativeModuleLoader(schemes, services) }, services);
    }

    private static WebLingoExecutionContext Context(WebLingoBuiltinRegistry services) =>
        new(new WebLingoWorkerId(0)) { Services = services, InstructionBudget = 1_000_000 };

    private static string Describe(IReadOnlyList<WebLingoDiagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(d => Describe(d)));

    private static string Describe(WebLingoDiagnostic? diagnostic) => diagnostic is null ? "" : $"{diagnostic.Code}: {diagnostic.Message}";
}
