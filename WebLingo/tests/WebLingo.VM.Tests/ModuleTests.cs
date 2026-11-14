using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM.Stack;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// Programs of several files (specification, section 3.9): imports, exports, the order modules run in, cycles, and
/// the files errors name, on both VMs, their files found by a resolver of the test's own.
/// </summary>
public sealed class ModuleTests
{
    [Fact]
    public void ImportsRunEachModuleOnceAndFirst()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wl"] = "import db from \"lib/db\"; import { twice, Point } from \"lib/util.wl\"; writeLn(\"main\"); " +
                          "writeLn(db.count); db.bump(); writeLn(db.count); writeLn(twice(21)); writeLn(new Point(1, 2)); " +
                          "writeLn([db is Module, db]);",
            ["lib/db.wl"] = "import { twice } from \"util.wl\"; writeLn(\"db\"); export var count = twice(1); " +
                            "export function bump() { count = count + 1; }",
            ["lib/util.wl"] = "writeLn(\"util\"); export function twice(x) { return x * 2; } " +
                              "export class Point { constructor(x, y) { this.x = x; this.y = y; } }",
        };
        AssertBoth(files, "util\ndb\nmain\n2\n3\n42\nPoint {x: 1, y: 2}\n[true, module lib/db.wl]\n");
    }

    [Fact]
    public void ACycleGetsTheInstanceAsItIsSoFar()
    {
        var files = new Dictionary<string, string>
        {
            ["a.wl"] = "import b from \"b\"; writeLn(\"a: \" + b.fromB); export var fromA = \"A\"; writeLn(b.later());",
            ["b.wl"] = "import a from \"a\"; export var fromB = \"B\"; export function later() { return a.fromA; } " +
                       "try { writeLn(a.fromA); } catch (e) { writeLn(e.code); }",
            ["self.wl"] = "import me from \"self\"; export var x = 1; writeLn(me.x);",
        };
        AssertBoth(files, "WL2004\na: B\nA\n", "a.wl");
        AssertBoth(files, "1\n", "self.wl");
    }

    [Fact]
    public void AnExportIsItsModulesToChange()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wl"] = "import m from \"m\"; import { n } from \"m\"; try { m.n = 2; } catch (e) { writeLn(e.code); } " +
                          "writeLn([n, m.n, m?.missing]); try { writeLn(m.missing); } catch (e) { writeLn(e.code); }",
            ["m.wl"] = "export var n = 1; var hidden = 2;",
        };
        AssertBoth(files, "WL3001\n[1, 1, null]\nWL3006\n");
    }

    [Fact]
    public void AnErrorNamesTheFileItIsIn()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wl"] = "import r from \"lib/r\";\nr.f();",
            ["lib/r.wl"] = "export function f() {\n    return [1][5];\n}",
            ["broken.wl"] = "import b from \"lib/b\";",
            ["lib/b.wl"] = "export var y = 1 +;",
            ["missing.wl"] = "writeLn(1);\nimport x from \"nowhere\";",
        };
        foreach (var run in BothVms)
        {
            var (diagnostics, _, error) = run(files, "main.wl");
            Assert.Empty(diagnostics);
            Assert.Equal((WebLingoDiagnosticCodes.IndexOutOfRange, "lib/r.wl"), (error!.Code, error.Source));
            Assert.Equal(2, new WebLingoSourceText(files["lib/r.wl"]).GetLocation(error.Span.Start).Line);
            (diagnostics, _, _) = run(files, "broken.wl");
            Assert.Equal((WebLingoDiagnosticCodes.UnexpectedToken, "lib/b.wl"), (diagnostics[0].Code, diagnostics[0].Source));
            (diagnostics, _, _) = run(files, "missing.wl");
            Assert.Equal((WebLingoDiagnosticCodes.ModuleNotFound, null), (diagnostics[0].Code, diagnostics[0].Source));
        }
    }

    [Fact]
    public void TheStandardModuleSystemHoldsTheBuiltIns()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wl"] = "import sys from \"std:system\"; import { writeLn } from \"std:system\"; import m from \"m\"; " +
                          "sys.writeLn(1); writeLn([sys, sys.write == write, m.f()]); try { sys.writeLn = 1; } catch (e) { writeLn(e.code); }",
            ["m.wl"] = "import { write } from \"std:system\"; export function f() { write(\"m: \"); return 2; }",
            ["nope.wl"] = "import x from \"std:nope\";",
        };
        AssertBoth(files, "1\nm: [module std:system, true, 2]\nWL3001\n");
        foreach (var run in BothVms)
        {
            Assert.Equal(WebLingoDiagnosticCodes.ModuleNotFound, run(files, "nope.wl").Diagnostics[0].Code);
        }
    }

    [Fact]
    public void AModuleRunsOnceForEachSetOfValuesItsParametersAreGiven()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wl"] = "import a from \"box\" { size: 1 }; import b from \"box\" { size: 2, label: \"big\" }; " +
                          "import c from \"box\" { size: 1 }; import d, { describe } from \"box\" { \"size\": 1, label: \"box\" }; " +
                          "import n from \"nested\" { n: 3 }; " +
                          "writeLn([a.describe(), b.describe(), a == c, a == d, b.made, describe(), n.inner.describe()]);",
            ["box.wl"] = "param size; param label = \"box\"; writeLn(\"running \" + label + \" \" + size.toString()); " +
                         "export function describe() { return label + \":\" + size.toString(); } export var made = size * 2;",
            ["nested.wl"] = "param n; import box from \"box\" { size: n * 10 }; export var inner = box;",
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
    [InlineData("writeLn(1); param x = 1;", WebLingoDiagnosticCodes.UnexpectedToken)]
    [InlineData("param n = 5; n = 6;", WebLingoDiagnosticCodes.AssignmentToConstant)]
    [InlineData("import a from \"setter\" { size: 1 };", WebLingoDiagnosticCodes.AssignmentToConstant)]
    public void AModulesParametersAreCheckedWhereItIsImported(string main, string code)
    {
        var files = new Dictionary<string, string>
        {
            ["main.wl"] = main,
            ["box.wl"] = "param size; param label = \"box\";",
            ["plain.wl"] = "export var n = 1;",
            ["setter.wl"] = "param size; size = 2;",
        };
        foreach (var run in BothVms) Assert.Equal(code, run(files, "main.wl").Diagnostics[0].Code);
    }

    [Fact]
    public void AModuleImportedWhileTheProgramRunsJoinsIt()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wl"] = "import { twice } from \"lib/util\"; writeLn(\"main \" + twice(2).toString()); " +
                          "var g = await Module.import(\"lib/greet\"); writeLn([g.hello(), g.n, new g.Point(3), new g.Point(3) is Object, g]); " +
                          "var h = await Module.import(\"lib/greet\", { who: \"Ann\" }); var g2 = await Module.import(\"lib/greet\"); " +
                          "writeLn([h.hello(), g2 == g, h == g, (await Module.import(\"lib/util\")).twice(5), (await g.later()).n]); " +
                          "var s = await Module.import(\"std:system\"); s.writeLn(\"via std:system\");",
            ["lib/greet.wl"] = "param who = \"world\"; import { twice } from \"util\"; writeLn(\"greet runs for \" + who); " +
                               "export function hello() { return \"hello, \" + who; } export class Point { constructor(x) { this.x = x; } } " +
                               "export var n = twice(21); export async function later() { return await Module.import(\"late\"); }",
            ["lib/util.wl"] = "writeLn(\"util runs\"); export function twice(x) { return x * 2; }",
            ["lib/late.wl"] = "export var n = 7;",
        };
        AssertBoth(files, "util runs\nmain 4\ngreet runs for world\n[\"hello, world\", 42, Point {x: 3}, true, module lib/greet.wl]\n" +
                          "greet runs for Ann\n[\"hello, Ann\", true, false, 10, 7]\nvia std:system\n");
    }

    [Theory]
    [InlineData("await Module.import(\"nowhere\");", WebLingoDiagnosticCodes.ModuleNotFound)]
    [InlineData("await Module.import(\"box\", { colour: 1 });", WebLingoDiagnosticCodes.ModuleParameter)]
    [InlineData("await Module.import(\"box\");", WebLingoDiagnosticCodes.ModuleParameter)]
    [InlineData("await Module.import(\"box\", 5);", WebLingoDiagnosticCodes.InvalidOperation)]
    [InlineData("await Module.import(\"broken\");", WebLingoDiagnosticCodes.UnexpectedToken)]
    [InlineData("await Module.import(5);", WebLingoDiagnosticCodes.InvalidOperation)]
    [InlineData("await Module.import(\"thrower\");", WebLingoDiagnosticCodes.UncaughtException)]
    [InlineData("param n = 5; param m; writeLn(n);", WebLingoDiagnosticCodes.ModuleParameter)]
    public void AModuleImportedWhileTheProgramRunsIsCheckedThen(string main, string code)
    {
        var files = new Dictionary<string, string>
        {
            ["main.wl"] = main,
            ["box.wl"] = "param size;",
            ["broken.wl"] = "export var x = 1 +;",
            ["thrower.wl"] = "throw \"no\";",
        };
        foreach (var run in BothVms)
        {
            var (diagnostics, _, error) = run(files, "main.wl");
            Assert.Empty(diagnostics);
            Assert.True(code == error?.Code, error?.Code + ": " + error?.Message);
        }
    }

    [Fact]
    public void AProgramsOwnModuleClassHidesTheBuiltIn()
    {
        var files = new Dictionary<string, string>
        {
            ["main.wl"] = "class Module {} Module.import = function (path) { return \"mine: \" + path; }; writeLn(Module.import(\"x\"));",
        };
        AssertBoth(files, "mine: x\n");
    }

    [Fact]
    public void AProgramsOwnParametersAreItsDefaultsForNow()
    {
        var files = new Dictionary<string, string> { ["main.wl"] = "param n = 5; param s = \"x\"; writeLn([n, s]);" };
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
        var files = new Dictionary<string, string> { ["main.wl"] = main, ["m.wl"] = "export var n = 1;", ["r.wl"] = "return 1;" };
        foreach (var run in BothVms) Assert.Equal(code, run(files, "main.wl").Diagnostics[0].Code);
    }

    private static void AssertBoth(Dictionary<string, string> files, string expected, string main = "main.wl")
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
        (files, main) =>
        {
            var output = new WebLingoCollectedOutput();
            var compiled = new WebLingoStackCompiler(Options(files)).Compile(files[main], main);
            var result = compiled.Module is { } module
                ? new WebLingoStackVirtualMachine { Loader = new WebLingoModuleLoader(Options(files)) }.Execute(module, Context(output))
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

    /// <summary>Files by their paths from one folder, an import's path from its importer's folder, <c>.wl</c> optional.</summary>
    private sealed class MemoryResolver(Dictionary<string, string> files) : IWebLingoSourceResolver
    {
        public bool TryResolve(string importer, string path, out string name, out string source, out string? error)
        {
            var folder = importer.Contains('/') ? importer[..importer.LastIndexOf('/')] + "/" : "";
            name = folder + path.Replace("./", "", StringComparison.Ordinal);
            if (!name.EndsWith(".wl", StringComparison.Ordinal)) name += ".wl";
            error = files.TryGetValue(name, out source!) ? null : "there is no such file";
            source ??= "";
            return error is null;
        }
    }
}
