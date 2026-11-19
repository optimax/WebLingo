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
/// Native modules' functions and classes (specification, section 3.9, "Native modules"; section 9, "Native modules", parts 1
/// and 2): a test
/// module, <c>WebLingo.Native.TestModule</c>, installed as <c>test.dll</c> in a scheme's folder, <c>native/</c>, imported by
/// programs run on both VMs, which must give the same; and through <c>weblingo</c>, its schemes given by
/// <c>weblingo.config</c> and <c>--scheme</c>.
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
    [InlineData("import { writeLn } from \"std:system\"; import t from \"native:test\"; writeLn([t.twice(21), t.greet(\"Ann\")]);", "[42, \"Hello, Ann!\"]")]
    [InlineData("import { writeLn } from \"std:system\"; import { twice, greet } from \"native:test\"; writeLn([twice(2), greet(1)]);", "[4, \"Hello, 1!\"]")]
    [InlineData("import { writeLn } from \"std:system\"; import t from \"native/test.dll\"; writeLn(t.twice(5));", "10")]
    [InlineData("import { writeLn } from \"std:system\"; var t = await Module.import(\"native:test\"); writeLn(t.twice(3));", "6")]
    [InlineData("import { writeLn } from \"std:system\"; import t from \"native:test\"; writeLn(await t.ready(7));", "7")]
    [InlineData("import { writeLn } from \"std:system\"; import t from \"native:test\"; try { t.fail(); } catch (e) { writeLn(e.code); }", "WL3001")]
    [InlineData("import { writeLn } from \"std:system\"; import t from \"native:test\"; import u from \"native/test.dll\"; writeLn([t == u, t]);", "[true, module native:test]")]
    [InlineData("import { writeLn } from \"std:system\"; import t from \"native:test\"; var u = await Module.import(\"native:test\"); writeLn(t == u);", "true")]
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

    [Theory]
    [InlineData("import { writeLn } from \"std:system\"; import { Counter } from \"native:test\"; var c = new Counter(5); writeLn([c.add(2), c.add(3), Counter.describe(), c, c.__instanceOf]);", "[7, 10, \"a counter\", Counter(10), class Counter]")]
    [InlineData("import { writeLn } from \"std:system\"; import { Counter } from \"native:test\"; class Twice extends Counter { add(n) { return super.add(n * 2); } } var t = new Twice(1); writeLn([t.add(2), t is Counter, t is Twice, t]);", "[5, true, true, Counter(5)]")]
    [InlineData("import { writeLn } from \"std:system\"; import t from \"native:test\"; writeLn([t, t.Counter, new t.Counter(3)]);", "[module native:test, class Counter, Counter(3)]")]
    [InlineData("import { writeLn } from \"std:system\"; import t from \"native:test\"; var u = await Module.import(\"native:test\"); writeLn([t.Counter == u.Counter, new u.Counter(2) is t.Counter]);", "[true, true]")]
    [InlineData("import { writeLn } from \"std:system\"; import { Counter } from \"native:test\"; var C = new Class(\"C\", Counter); var c = new C(4); writeLn([c.add(1), c is Counter]);", "[5, true]")]
    [InlineData("import { writeLn } from \"std:system\"; import { Counter } from \"native:test\"; extend Counter { twice() { return this.add(this.add(0)); } } writeLn(new Counter(3).twice());", "6")]
    [InlineData("import { writeLn } from \"std:system\"; import { Counter } from \"native:test\"; var c = new Counter(1); using (var d = c) { d.add(1); } try { c.add(1); } catch (e) { writeLn([e.code, e.message, c]); }", "[\"WL3001\", \"This Counter has been disposed.\", Counter(disposed)]")]
    [InlineData("import { writeLn } from \"std:system\"; import { Counter } from \"native:test\"; var c = new Counter(1); c.close(); c.close(); c.dispose(); writeLn(c);", "Counter(disposed)")]
    [InlineData("import { writeLn } from \"std:system\"; import { Other } from \"native:test\"; try { new Other().peek(); } catch (e) { writeLn(e.message); }", "An instance of Other is not an instance that the native class Counter's constructor made.")]
    [InlineData("import { writeLn } from \"std:system\"; import { Counter } from \"native:test\"; Counter.setMethod(\"constructor\", function (this, n) {}); var c = new Counter(1); try { c.add(1); } catch (e) { writeLn(e.message); }", "'add' of the native class Counter runs only on an instance that its constructor made, not on an instance of Counter.")]
    [InlineData("import { writeLn } from \"std:system\"; import { Counter } from \"native:test\"; try { new Counter(\"x\"); } catch (e) { writeLn(e.message); } try { new Counter(); } catch (e) { writeLn(e.code); }", "A Counter starts from an integer.\nWL2006")]
    [InlineData("import { writeLn } from \"std:system\"; import { Counter, released } from \"native:test\"; var before = released(); for (var i = 0; i < 100; i = i + 1) { new Counter(i); } var big = \"0123456789\"; for (var i = 0; i < 17; i = i + 1) { big = big + big; } writeLn(released() - before >= 99);", "true")]
    public void ANativeModulesClassesAreItsExports(string source, string printed)
    {
        // Classes as any others, extended, made and disposed alike, their instances' host objects only theirs (part 2).
        Assert.Equal(("", printed + "\n", ""), Run(source));
    }

    [Theory]
    [InlineData("import { writeLn } from \"std:system\"; var c = new Counter(5); c.value = 7; writeLn([c.value, c.initial, c.add(1), c.value, c?.initial]);", "[7, 5, 8, 8, 5]")]
    [InlineData("import { writeLn } from \"std:system\"; var c = new Counter(5); try { c.initial = 1; } catch (e) { writeLn(e.code); } try { c.value = \"x\"; } catch (e) { writeLn(e.message); }", "WL3001\nA Counter's value is an integer.")]
    [InlineData("import { writeLn } from \"std:system\"; var c = new Counter(1); c[\"a\"] = 3; c[\"b\"] = c[\"a\"] + 1; var y = (c[\"x\"] = 9); writeLn([c[\"a\"], c[\"b\"], y, c[\"x\"]]);", "[3, 4, 9, 9]")]
    [InlineData("import { writeLn } from \"std:system\"; var c = new Counter(1); try { writeLn(c[\"z\"]); } catch (e) { writeLn([e.code, e.message]); }", "[\"WL3007\", \"This Counter has no mark 'z'.\"]")]
    [InlineData("import { writeLn } from \"std:system\"; var c = new Counter(1); try { c[\"a\"] = \"x\"; } catch (e) { writeLn(e.message); }", "A Counter's mark is an integer.")]
    [InlineData("import { writeLn } from \"std:system\"; var c = new Counter(1); c[\"a\"] = 2; var n = null; writeLn([n?[\"z\"], c?[\"z\"], c?[\"a\"], c?[\"z\"]?[\"y\"]]);", "[null, null, 2, null]")]
    [InlineData("import { writeLn } from \"std:system\"; var c = new Counter(1); c.dispose(); try { writeLn(c?[\"a\"]); } catch (e) { writeLn([e.code, e.message]); }", "[\"WL3001\", \"This Counter has been disposed.\"]")]
    [InlineData("import { writeLn } from \"std:system\"; class D extends Counter {} var d = new D(1); d[\"k\"] = 5; writeLn([d?[\"k\"], d?[\"q\"]]);", "[5, null]")]
    [InlineData("import { writeLn } from \"std:system\"; class D extends Counter { property twice { get { return this.value * 2; } } } var d = new D(2); d[\"k\"] = 1; d.value = d.value + 1; writeLn([d[\"k\"], d.twice, d.value]);", "[1, 6, 3]")]
    [InlineData("import { writeLn } from \"std:system\"; extend Counter { bump() { this.value = this.value + 1; return this[\"m\"] = this.value; } } var c = new Counter(4); writeLn([c.bump(), c[\"m\"]]);", "[5, 5]")]
    [InlineData("import { writeLn } from \"std:system\"; var c = new Counter(1); c.dispose(); try { writeLn(c.value); } catch (e) { writeLn(e.message); } try { c[\"a\"] = 1; } catch (e) { writeLn(e.message); }", "This Counter has been disposed.\nThis Counter has been disposed.")]
    public void ANativeClassesPropertiesAndIndexerAreItsInstancesMembers(string source, string printed)
    {
        // Native classes' properties and indexers (section 9, "Native modules", part 4), on both VMs.
        Assert.Equal(("", printed + "\n", ""), Run("import { Counter } from \"native:test\"; " + source));
    }

    [Theory]
    [InlineData("import { writeLn } from \"std:system\"; Counter.limit = 7; writeLn([Counter.limit, Counter.kind]); Counter.limit = 8; writeLn(Counter.limit);", "[7, \"Counter\"]\n8")]
    [InlineData("import { write, writeLn } from \"std:system\"; try { Counter.kind = \"x\"; } catch (e) { writeLn(e.code); } try { Counter.limit = \"x\"; } catch (e) { writeLn(e.message); }",
        "WL3001\nA Counter's limit is an integer.")]
    [InlineData("import { writeLn } from \"std:system\"; class D extends Counter {} writeLn([D.kind, Counter.kind == D.kind]);", "[\"Counter\", true]")]
    public void ANativeClassesStaticPropertiesAreItsClasssMembers(string source, string printed)
    {
        // Native classes' static properties (section 3.7), as std:env's Environment.machineName is (section 7.15), on both VMs.
        Assert.Equal(("", printed + "\n", ""), Run("import { Counter } from \"native:test\"; " + source));
    }

    [Fact]
    public void ANativeClassesHostObjectsAreReleasedWhenTheExecutionEnds()
    {
        const string released = "import { write, writeLn } from \"std:system\"; import t from \"native:test\"; writeLn(t.released());";
        var before = long.Parse(Run(released).Output, System.Globalization.CultureInfo.InvariantCulture);
        Run("import { Counter } from \"native:test\"; var kept = new Counter(1);"); // On both VMs: two tallies.
        Assert.Equal((before + 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n", Run(released).Output);
    }

    [Fact]
    public void ANativeMethodsErrorIsReportedWhereItWasCalled()
    {
        // Not in the class's members, which the compiler wrote, but at the call, in the file that made it (section 3.9).
        File.WriteAllText(Path.Combine(_folder, "lib.wlg"), "export function use(c) {\n  return c.add(1);\n}");
        var script = Path.Combine(_folder, "script.wlg");
        File.WriteAllText(script,
            "import { Counter } from \"lib:test\";\nimport { use } from \"./lib.wlg\";\nvar c = new Counter(1);\nc.close();\n  c.add(1);");
        var (exit, _, error) = Weblingo(["run", "--scheme", "lib=" + _native, script]);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains("script.wlg(5,3): ERROR WL3001 (TypeError): This Counter has been disposed.", error);
        File.WriteAllText(script,
            "import { Counter } from \"lib:test\";\nimport { use } from \"./lib.wlg\";\nvar c = new Counter(1);\nc.dispose();\nuse(c);");
        (exit, _, error) = Weblingo(["run", "--scheme", "lib=" + _native, script]);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains("lib.wlg(2,10): ERROR WL3001 (TypeError): This Counter has been disposed.", error);
    }

    [Fact]
    public void WeblingoTakesItsSchemesFromItsCommandLineAndFromWeblingoJson()
    {
        var script = Path.Combine(_folder, "script.wlg");
        File.WriteAllText(script, "import { write, writeLn } from \"std:system\"; import t from \"lib:test\"; writeLn(t.twice(4));");
        var (exit, output, error) = Weblingo(["run", "--scheme", "lib=" + _native, script]);
        Assert.Equal((WebLingoCommandLine.Success, "8\n", ""), (exit, output, error));

        // weblingo.config, in the script's folder, its folder from the file's; the command line over it.
        File.WriteAllText(Path.Combine(_folder, WebLingoSchemes.ConfigurationFile), "{ \"schemes\": { \"lib\": \"native\" } }");
        Assert.Equal((WebLingoCommandLine.Success, "8\n", ""), Weblingo(["run", script]));
        Assert.Equal(WebLingoCommandLine.CompilationFailed, Weblingo(["check", "--scheme", "lib=" + _folder, script]).Exit);

        File.WriteAllText(Path.Combine(_folder, WebLingoSchemes.ConfigurationFile), "{ \"schemes\": { \"l\": \"native\" } }");
        (exit, _, error) = Weblingo(["run", script]);
        Assert.Equal(WebLingoCommandLine.FileUnreadable, exit);
        Assert.Contains("'l' is not a scheme's name, given a folder.", error);
        File.Delete(Path.Combine(_folder, WebLingoSchemes.ConfigurationFile));

        // The configuration is weblingo.config (16 November 2026): a weblingo.json is not read.
        Assert.Equal("weblingo.config", WebLingoSchemes.ConfigurationFile);
        File.WriteAllText(Path.Combine(_folder, "weblingo.json"), "{ \"schemes\": { \"lib\": \"native\" } }");
        Assert.Equal(WebLingoCommandLine.CompilationFailed, Weblingo(["run", script]).Exit);
        File.Delete(Path.Combine(_folder, "weblingo.json"));

        // A script's configuration stays weblingo.config, a site's being wasp.config (the owner's decision, 16 November 2026),
        // which weblingo does not read.
        File.WriteAllText(Path.Combine(_folder, "wasp.config"), "{ \"schemes\": { \"lib\": \"native\" } }");
        Assert.Equal(WebLingoCommandLine.CompilationFailed, Weblingo(["run", script]).Exit);
        File.Delete(Path.Combine(_folder, "wasp.config"));

        (exit, _, error) = Weblingo(["run", "--scheme", "lib", script]);
        Assert.Equal(WebLingoCommandLine.UsageError, exit);
        Assert.StartsWith("'lib' is not a scheme, name=folder, or is given twice.", error);
    }

    [Theory]
    // std:tasks, a standard module installed as std/tasks.dll: Task's combinators as its exports (sections 3.8 and 3.9).
    [InlineData("import { write, writeLn, delay } from \"std:system\"; import { all, any, timeout } from \"std:tasks\"; writeLn([await all([1, delay(1), 3]), await any([delay(5), 2]), "
        + "await timeout(7, 100)]);", "[[1, null, 3], 2, 7]")]
    [InlineData("import { writeLn } from \"std:system\"; import tasks from \"std:tasks\"; writeLn([tasks, await tasks.all([])]);", "[module std:tasks, []]")]
    [InlineData("import { write, writeLn } from \"std:system\"; import { all } from \"std:tasks\"; var messages = []; try { all(5); } catch (e) { messages.push(e.message); } "
        + "try { Task.all(5); } catch (e) { messages.push(e.message); } writeLn(messages[0] == messages[1]);", "true")]
    [InlineData("import { writeLn, delay } from \"std:system\"; import { timeout } from \"std:tasks\"; try { await timeout(delay(50), 1); } catch (e) { writeLn(e.code); }", "WL3010")]
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
        var script = Path.Combine(_folder, "script.wlg");
        File.WriteAllText(script, "import { writeLn, delay } from \"std:system\"; import s from \"std:system\"; writeLn([s, s.delay == delay]);");
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
        File.WriteAllText(script, "delay(1);"); // The test module offers no delay, which every program has from std:system.
        Assert.Equal(WebLingoCommandLine.CompilationFailed, Weblingo(["check", "--scheme", "std=" + other, script]).Exit);
        File.WriteAllText(script, "import { twice } from \"std:system\"; if (twice(2) == 4) { throws(); }");
        (exit, _, error) = Weblingo(["run", "--scheme", "std=" + other, script]);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains("ERROR WL3001 (TypeError): 'throws' failed: thrown by the test module", error);
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
        var name = Path.Combine(_folder, "program.wlg");
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
