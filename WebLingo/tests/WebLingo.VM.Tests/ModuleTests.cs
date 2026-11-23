using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// Programs of several files (specification, section 3.9): imports, exports, the order modules run in, cycles, and
/// the files errors name, on both VMs, their files found by a resolver of the test's own.
/// </summary>
public sealed class ModuleTests
{
    [Fact]
    public void AFileImportedThatNothingMakesIsAnError()
    {
        // A native module's file with neither its instance, made where the program begins, nor a function that makes it,
        // imported: bytecode no compiler writes, which each VM reports, rather than failing as it calls nothing.
        var units = new[]
        {
            new WebLingo.Bytecode.WebLingoUnitInfo("main.wlg", -1, []),
            new WebLingo.Bytecode.WebLingoUnitInfo("native:none", -1, []) { IsNative = true }
        };
        var register = new WebLingo.Bytecode.WebLingoCompiledModule("main.wlg", [], [],
            new WebLingo.Bytecode.WebLingoFunction("main", 1, 0,
                [new(WebLingo.Bytecode.WebLingoOpCode.Import, 0, 1), new(WebLingo.Bytecode.WebLingoOpCode.Return)])) { Units = units };
        var result = new WebLingoVirtualMachine().Execute(register, new WebLingoExecutionContext(new WebLingoWorkerId(0)));
        Assert.Equal((WebLingoDiagnosticCodes.BytecodeVerificationFailure,
            "The bytecode is invalid: file 1 ('native:none') is imported, but nothing makes its instance."),
            (result.Error?.Code, result.Error?.Message));
    }

    [Fact]
    public void ImportsRunEachModuleOnceAndFirst()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wlg"] = "import { write, writeLn } from \"std:system\"; import db from \"lib/db\"; import { twice, Point } from \"lib/util.wlg\"; writeLn(\"main\"); " +
                          "writeLn(db.count); db.bump(); writeLn(db.count); writeLn(twice(21)); writeLn(new Point(1, 2)); " +
                          "writeLn([db is Module, db]);",
            ["lib/db.wlg"] = "import { write, writeLn } from \"std:system\"; import { twice } from \"util.wlg\"; writeLn(\"db\"); export var count = twice(1); " +
                            "export function bump() { count = count + 1; }",
            ["lib/util.wlg"] = "import { write, writeLn } from \"std:system\"; writeLn(\"util\"); export function twice(x) { return x * 2; } " +
                              "export class Point { constructor(x, y) { this.x = x; this.y = y; } }",
        };
        AssertBoth(files, "util\ndb\nmain\n2\n3\n42\nPoint {x: 1, y: 2}\n[true, module lib/db.wlg]\n");
    }

    [Fact]
    public void ACycleGetsTheInstanceAsItIsSoFar()
    {
        var files = new Dictionary<string, string>
        {
            ["a.wlg"] = "import { write, writeLn } from \"std:system\"; import b from \"b\"; writeLn(\"a: \" + b.fromB); export var fromA = \"A\"; writeLn(b.later());",
            ["b.wlg"] = "import { write, writeLn } from \"std:system\"; import a from \"a\"; export var fromB = \"B\"; export function later() { return a.fromA; } " +
                       "try { writeLn(a.fromA); } catch (e) { writeLn(e.code); }",
            ["self.wlg"] = "import { write, writeLn } from \"std:system\"; import me from \"self\"; export var x = 1; writeLn(me.x);",
        };
        AssertBoth(files, "WL2004\na: B\nA\n", "a.wlg");
        AssertBoth(files, "1\n", "self.wlg");
    }

    [Fact]
    public void AnExportIsItsModulesToChange()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wlg"] = "import { write, writeLn } from \"std:system\"; import m from \"m\"; import { n } from \"m\"; try { m.n = 2; } catch (e) { writeLn(e.code); } " +
                          "writeLn([n, m.n, m?.missing]); try { writeLn(m.missing); } catch (e) { writeLn(e.code); }",
            ["m.wlg"] = "export var n = 1; var hidden = 2;",
        };
        AssertBoth(files, "WL3001\n[1, 1, null]\nWL3006\n");
    }

    [Fact]
    public void AnErrorNamesTheFileItIsIn()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wlg"] = "import r from \"lib/r\";\nr.f();",
            ["lib/r.wlg"] = "export function f() {\n    return [1][5];\n}",
            ["broken.wlg"] = "import b from \"lib/b\";",
            ["lib/b.wlg"] = "export var y = 1 +;",
            ["missing.wlg"] = "import { write, writeLn } from \"std:system\"; writeLn(1);\nimport x from \"nowhere\";",
        };
        foreach (var run in BothVms)
        {
            var (diagnostics, _, error) = run(files, "main.wlg");
            Assert.Empty(diagnostics);
            Assert.Equal((WebLingoDiagnosticCodes.IndexOutOfRange, "lib/r.wlg"), (error!.Code, error.Source));
            Assert.Equal(2, new WebLingoSourceText(files["lib/r.wlg"]).GetLocation(error.Span.Start).Line);
            (diagnostics, _, _) = run(files, "broken.wlg");
            Assert.Equal((WebLingoDiagnosticCodes.UnexpectedToken, "lib/b.wlg"), (diagnostics[0].Code, diagnostics[0].Source));
            (diagnostics, _, _) = run(files, "missing.wlg");
            Assert.Equal((WebLingoDiagnosticCodes.ModuleNotFound, null), (diagnostics[0].Code, diagnostics[0].Source));
        }
    }

    [Fact]
    public void TheStandardModuleSystemHoldsTheBuiltIns()
    {
        var files = new Dictionary<string, string>
        {
            // std:system holds write and writeLn (section 3.9, M0.20.3), its members and its imports' names the same built-ins.
            ["main.wlg"] = "import io from \"std:system\"; import { writeLn } from \"std:system\"; import m from \"m\"; " +
                          "io.writeLn(1); writeLn([io, io.write == m.w, io.writeLn == writeLn, m.f()]); try { io.writeLn = 1; } catch (e) { writeLn(e.code); }",
            ["m.wlg"] = "import { write } from \"std:system\"; export var w = write; export function f() { write(\"m: \"); return 2; }",
            ["nope.wlg"] = "import x from \"std:nope\";",
        };
        AssertBoth(files, "1\nm: [module std:system, true, true, 2]\nWL3001\n");
        foreach (var run in BothVms)
        {
            Assert.Equal(WebLingoDiagnosticCodes.ModuleNotFound, run(files, "nope.wlg").Diagnostics[0].Code);
        }
    }

    [Fact]
    public void AModuleRunsOnceForEachSetOfValuesItsParametersAreGiven()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wlg"] = "import { write, writeLn } from \"std:system\"; import a from \"box\" { size: 1 }; import b from \"box\" { size: 2, label: \"big\" }; " +
                          "import c from \"box\" { size: 1 }; import d, { describe } from \"box\" { \"size\": 1, label: \"box\" }; " +
                          "import n from \"nested\" { n: 3 }; " +
                          "writeLn([a.describe(), b.describe(), a == c, a == d, b.made, describe(), n.inner.describe()]);",
            ["box.wlg"] = "param size; param label = \"box\"; import { write, writeLn } from \"std:system\"; writeLn(\"running \" + label + \" \" + size.toString()); " +
                         "export function describe() { return label + \":\" + size.toString(); } export var made = size * 2;",
            ["nested.wlg"] = "param n; import box from \"box\" { size: n * 10 }; export var inner = box;",
        };
        AssertBoth(files, "running box 1\nrunning big 2\nrunning box 1\nrunning box 30\n" +
                          "[\"box:1\", \"big:2\", true, false, 4, \"box:1\", \"box:30\"]\n");
    }

    [Theory]
    [InlineData("import a from \"box\";", WebLingoDiagnosticCodes.ModuleParameter)]
    [InlineData("import a from \"box\" { size: 1, colour: \"red\" };", WebLingoDiagnosticCodes.ModuleParameter)]
    [InlineData("import a from \"box\" { size: 1, size: 2 };", WebLingoDiagnosticCodes.DuplicateDeclaration)]
    [InlineData("import a from \"plain\" { size: 1 };", WebLingoDiagnosticCodes.ModuleParameter)]
    [InlineData("function f() { param x; }", WebLingoDiagnosticCodes.UnexpectedToken)]
    [InlineData("import { writeLn } from \"std:system\"; writeLn(1); param x = 1;", WebLingoDiagnosticCodes.UnexpectedToken)]
    [InlineData("param n = 5; n = 6;", WebLingoDiagnosticCodes.AssignmentToConstant)]
    [InlineData("import a from \"setter\" { size: 1 };", WebLingoDiagnosticCodes.AssignmentToConstant)]
    public void AModulesParametersAreCheckedWhereItIsImported(string main, string code)
    {
        var files = new Dictionary<string, string>
        {
            ["main.wlg"] = main,
            ["box.wlg"] = "param size; param label = \"box\";",
            ["plain.wlg"] = "export var n = 1;",
            ["setter.wlg"] = "param size; size = 2;",
        };
        foreach (var run in BothVms) Assert.Equal(code, run(files, "main.wlg").Diagnostics[0].Code);
    }

    [Fact]
    public void AModuleImportedWhileTheProgramRunsJoinsIt()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wlg"] = "import { write, writeLn } from \"std:system\"; import { twice } from \"lib/util\"; writeLn(\"main \" + twice(2).toString()); " +
                          "var g = await Module.import(\"lib/greet\"); writeLn([g.hello(), g.n, new g.Point(3), new g.Point(3) is Object, g]); " +
                          "var h = await Module.import(\"lib/greet\", { who: \"Anna\" }); var g2 = await Module.import(\"lib/greet\"); " +
                          "writeLn([h.hello(), g2 == g, h == g, (await Module.import(\"lib/util\")).twice(5), (await g.later()).n]); " +
                          "var s = await Module.import(\"std:system\"); s.writeLn(\"via std:system\");",
            ["lib/greet.wlg"] = "param who = \"world\"; import { write, writeLn } from \"std:system\"; import { twice } from \"util\"; writeLn(\"greet runs for \" + who); " +
                               "export function hello() { return \"hello, \" + who; } export class Point { constructor(x) { this.x = x; } } " +
                               "export var n = twice(21); export async function later() { return await Module.import(\"late\"); }",
            ["lib/util.wlg"] = "import { write, writeLn } from \"std:system\"; writeLn(\"util runs\"); export function twice(x) { return x * 2; }",
            ["lib/late.wlg"] = "export var n = 7;",
        };
        AssertBoth(files, "util runs\nmain 4\ngreet runs for world\n[\"hello, world\", 42, Point {x: 3}, true, module lib/greet.wlg]\n" +
                          "greet runs for Anna\n[\"hello, Anna\", true, false, 10, 7]\nvia std:system\n");
    }

    [Theory]
    [InlineData("await Module.import(\"nowhere\");", WebLingoDiagnosticCodes.ModuleNotFound)]
    [InlineData("await Module.import(\"box\", { colour: 1 });", WebLingoDiagnosticCodes.ModuleParameter)]
    [InlineData("await Module.import(\"box\");", WebLingoDiagnosticCodes.ModuleParameter)]
    [InlineData("await Module.import(\"box\", 5);", WebLingoDiagnosticCodes.InvalidOperation)]
    [InlineData("await Module.import(\"broken\");", WebLingoDiagnosticCodes.UnexpectedToken)]
    [InlineData("await Module.import(5);", WebLingoDiagnosticCodes.InvalidOperation)]
    [InlineData("await Module.import(\"thrower\");", WebLingoDiagnosticCodes.UncaughtException)]
    [InlineData("param n = 5; param m; import { writeLn } from \"std:system\"; writeLn(n);", WebLingoDiagnosticCodes.ModuleParameter)]
    public void AModuleImportedWhileTheProgramRunsIsCheckedThen(string main, string code)
    {
        var files = new Dictionary<string, string>
        {
            ["main.wlg"] = main,
            ["box.wlg"] = "param size;",
            ["broken.wlg"] = "export var x = 1 +;",
            ["thrower.wlg"] = "throw \"no\";",
        };
        foreach (var run in BothVms)
        {
            var (diagnostics, _, error) = run(files, "main.wlg");
            Assert.Empty(diagnostics);
            Assert.True(code == error?.Code, error?.Code + ": " + error?.Message);
        }
    }

    [Fact]
    public void AProgramsOwnModuleClassHidesTheBuiltIn()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wlg"] = "import { write, writeLn } from \"std:system\"; class Module {} Module.import = function (path) { return \"mine: \" + path; }; writeLn(Module.import(\"x\"));",
        };
        AssertBoth(files, "mine: x\n");
    }

    [Fact]
    public void AProgramsOwnParametersAreItsDefaultsForNow()
    {
        var files = new Dictionary<string, string> { ["main.wlg"] = "param n = 5; param s = \"x\"; import { write, writeLn } from \"std:system\"; writeLn([n, s]);" };
        AssertBoth(files, "[5, \"x\"]\n");
    }

    [Theory]
    [InlineData("{ import m from \"m\"; }", WebLingoDiagnosticCodes.UnexpectedToken)]
    [InlineData("function f() { export var x = 1; }", WebLingoDiagnosticCodes.UnexpectedToken)]
    [InlineData("import { n } from \"m\"; n = 2;", WebLingoDiagnosticCodes.AssignmentToConstant)]
    [InlineData("import m from \"m\"; import m from \"m\";", WebLingoDiagnosticCodes.DuplicateDeclaration)]
    [InlineData("import m from \"r\";", WebLingoDiagnosticCodes.ReturnOutsideFunction)]
    public void ImportsAndExportsStandAtTheTopLevel(string main, string code)
    {
        var files = new Dictionary<string, string> { ["main.wlg"] = main, ["m.wlg"] = "export var n = 1;", ["r.wlg"] = "return 1;" };
        foreach (var run in BothVms) Assert.Equal(code, run(files, "main.wlg").Diagnostics[0].Code);
    }

    private static void AssertBoth(Dictionary<string, string> files, string expected, string main = "main.wlg")
    {
        foreach (var run in BothVms)
        {
            var (diagnostics, output, error) = run(files, main);
            Assert.Empty(diagnostics);
            Assert.True(error is null, error?.Message);
            Assert.Equal(expected, output);
        }
    }

    private delegate (IReadOnlyList<WebLingoDiagnostic> Diagnostics, string Output, WebLingoDiagnostic? Error) Runner(
        Dictionary<string, string> files, string main);

    private static Runner[] BothVms =>
    [
        (files, main) =>
        {
            var output = new WebLingoCollectedOutput();
            var compiled = new WebLingoCompiler(Options(files)).Compile(files[main], main);
            var result = compiled.Module is { } module
                ? new WebLingoVirtualMachine { Loader = new WebLingoModuleLoader(Options(files)) }.Execute(module, Context(output))
                : null;
            return (compiled.Diagnostics, output.Text, result?.Error);
        },
    ];

    private static WebLingoCompilerOptions Options(Dictionary<string, string> files) =>
        new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1)])
        {
            Resolver = new MemoryResolver(files)
        };

    private static WebLingoExecutionContext Context(IWebLingoTextOutput output) =>
        new(new WebLingoWorkerId(0)) { Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output)) };

    /// <summary>Files by their paths from one folder, an import's path from its importer's folder, <c>.wlg</c> optional.</summary>
    private sealed class MemoryResolver(Dictionary<string, string> files) : IWebLingoSourceResolver
    {
        public bool TryResolve(string importer, string path, out string name, out string source, out string? error)
        {
            var folder = importer.Contains('/') ? importer[..importer.LastIndexOf('/')] + "/" : "";
            name = folder + path.Replace("./", "", StringComparison.Ordinal);
            if (!name.EndsWith(".wlg", StringComparison.Ordinal)) name += ".wlg";
            error = files.TryGetValue(name, out source!) ? null : "there is no such file";
            source ??= "";
            return error is null;
        }
    }
}
