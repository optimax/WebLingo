using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

public sealed class TestingCommandLineTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-tests-" + Guid.NewGuid().ToString("N"));

    public TestingCommandLineTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("register")]
    [InlineData("stack")]
    public void DiscoveryUsesSourceOrderAndIsolatedInstances(string vm)
    {
        Configure(vm);
        Write("base.wlg", """
            export class ImportedTests {
                testInherited() { throw new Error("inherited method ran"); }
                async beforeEach() { await delay(1); this.setUp = true; }
            }
            """);
        var file = Write("cases.wlg", """
            import { ImportedTests } from "./base";
            import { assert, assertEqual } from "std:test"; import { writeLn } from "std:console";
            writeLn("initializing");
            var order = [];
            class UnexportedTests { testHidden() { throw new Error("unexported method ran"); } }
            export class Other { testHidden() { throw new Error("wrong class name"); } }
            export class ExampleTests extends ImportedTests {
                constructor() { super(); this.value = 1; }
                static testStatic() { throw new Error("static method ran"); }
                testlower() { throw new Error("wrong method name"); }
                testZulu() { assert(this.setUp); assertEqual(this.value, 1); this.value = 2; order.push("z"); }
                async testAlpha() { await delay(1); assertEqual(this.value, 1); order.push("a"); }
            }
            export class LastTests {
                testOrder() { assertEqual(order.join(""), "za"); }
            }
            """);
        var second = Write("second.wlg", """import { writeLn } from "std:console"; export class OtherTests { testLast() { writeLn("ran second"); } }""");
        var list = Run("test", "--list", file, second);
        Assert.Equal(WebLingoCommandLine.Success, list.Exit);
        Assert.Equal("", list.Error);
        Assert.Equal([$"{file}::ExampleTests.testZulu", $"{file}::ExampleTests.testAlpha", $"{file}::LastTests.testOrder",
            $"{second}::OtherTests.testLast"], list.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal((WebLingoCommandLine.Success, $"{file}::ExampleTests.testAlpha\n", ""), Run("test", "--list", "--filter", "Alpha", file));
        Assert.Equal((WebLingoCommandLine.Success, "initializing\n1 passed, 0 failed.\n", ""), Run("test", "--filter", "Alpha", file, second));
        Assert.Equal((WebLingoCommandLine.Success, "initializing\nran second\n4 passed, 0 failed.\n", ""), Run("test", file, second));
    }

    [Theory]
    [InlineData("register")]
    [InlineData("stack")]
    public void ReportsHardAssertionAndCleanupAndContinues(string vm)
    {
        Configure(vm);
        var helper = Write("assertions.wlg", """
            import { assertEqual } from "std:test";
            export function check() {
                assertEqual(2, 3, "from another file");
            }
            """);
        var file = Write("failures.wlg", """
            import { check } from "./assertions";
            import { assert, assertEqual } from "std:test"; import { writeLn } from "std:console";
            export class ExampleTests {
                async beforeEach() { await delay(1); this.cleanupFails = true; }
                testFails() {
                    check();
                    writeLn("unreachable");
                }
                testPasses() { this.cleanupFails = false; writeLn("continued"); }
                async afterEach() {
                    await delay(1);
                    assert(!this.cleanupFails, "cleanup failed");
                }
            }
            """);
        var (exit, output, error) = Run("test", file);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains($"FAIL {file}::ExampleTests.testFails", error);
        Assert.Contains($"{Path.GetFileName(helper)}(3,", error);
        Assert.Contains("Expected 3, actual 2. from another file", error);
        Assert.Contains($"{file}(12,", error);
        Assert.Contains("afterEach: WL3021", error);
        Assert.Contains("cleanup failed", error);
        Assert.Equal("continued\n1 passed, 1 failed.\n", output);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("stack")]
    public void ChecksReportLocationsAndKeepAnUnexpectedException(string vm)
    {
        Configure(vm);
        var file = Write("checks.wlg", """
            import { checkAll } from "std:test";
            export class ExampleTests {
                async testChecks() {
                    await checkAll(async check => {
                        check.assertEqual(1, 2, "first");
                        await check.assertRejects(async () => { await delay(1); });
                        check.assertDeepEqual([{word: "hi"}], [{word: "bye"}]);
                        throw new Error("unexpected from callback");
                    });
                }
            }
            """);
        var (exit, output, error) = Run("test", file);
        Assert.Equal(WebLingoCommandLine.RuntimeFailed, exit);
        Assert.Contains($"FAIL {file}::ExampleTests.testChecks", error);
        Assert.Contains("3 assertion failure(s)", error);
        Assert.Contains("(5): Error WL3021: Expected 2, actual 1. first", error);
        Assert.Contains("(6): Error WL3021: Expected a rejected task", error);
        Assert.Contains("(7): Error WL3021: At $[0][\"word\"]: Expected \"bye\", actual \"hi\".", error);
        Assert.Contains("Unexpected error: Error: unexpected from callback", error);
        Assert.Equal("0 passed, 1 failed.\n", output);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("stack")]
    public void ValueAndErrorAssertionsWorkInSourceAndBytecode(string vm)
    {
        var source = Write("assertions.wlg", """
            import { assert, assertEqual, assertNotEqual, assertDeepEqual, assertContains, assertHasKey,
                     assertMatches, assertClose, assertThrows, assertRejects } from "std:test";
            import { Bytes } from "std:bytes";
            import { Regex } from "std:regex";
            class Failure extends Error {}
            var a = [];
            var b = [];
            a.push(a);
            b.push(b);
            assert(true);
            assertEqual(a, a);
            assertNotEqual(a, b);
            assertDeepEqual(a, b);
            assertDeepEqual({a: a, b: 2}, {b: 2, a: b});
            assertDeepEqual(#{1, 2}, #{2, 1});
            assertDeepEqual(new Bytes([104, 105]), new Bytes("hi"));
            assertContains("there", "he");
            assertContains([3, 4], 4);
            assertContains(#{1, 2}, 1);
            assertHasKey({a: null}, "a");
            assertMatches("abc", new Regex("^a"));
            assertClose(0.1 + 0.2, 0.3, {absolute: 0.000001});
            assertClose(101, 100, {relative: 0.01});
            const sync = assertThrows(() => { throw new Failure("sync"); }, Failure);
            assertEqual(sync.message, "sync");
            const asyncError = await assertRejects(async () => { await delay(1); throw new Failure("async"); });
            assertEqual(asyncError.message, "async");
            """);
        Configure(vm);
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Run("run", source));
        var dynamicSource = Write("dynamic.wlg", """
            const testing = await Module.import("std:test");
            testing.assertEqual(2 + 2, 4);
            """);
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Run("run", dynamicSource));
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Run("compile", "--" + vm, source));
        var bytecode = Path.ChangeExtension(source, vm == "stack" ? ".wlbs" : ".wlbr");
        File.Delete(source);
        Assert.Equal((WebLingoCommandLine.Success, "", ""), Run("run", bytecode));
    }

    [Fact]
    public void DiscoveryErrorsReturnNonzeroAndOtherFilesStillRun()
    {
        var mixed = Write("arity.wlg", "export class ATests { testBad(n = 2) {} testGood() {} }");
        var empty = Write("empty.wlg", "export class NotASuite { testOops() {} }");
        var good = Write("good.wlg", "export class GoodTests { testGood() {} }");
        var (exit, output, error) = Run("test", mixed, empty, good);
        Assert.Equal(WebLingoCommandLine.CompilationFailed, exit);
        Assert.Contains("ATests.testBad must declare zero parameters", error);
        Assert.Contains($"{empty}: No tests found", error);
        Assert.Equal("2 passed, 0 failed.\n", output);
        var noMatches = Run("test", "--filter", "TESTGOOD", good);
        Assert.Equal(WebLingoCommandLine.CompilationFailed, noMatches.Exit);
        Assert.Contains("No tests match", noMatches.Error);
        Assert.Equal(WebLingoCommandLine.UsageError, Run("test", "--filter").Exit);
    }

    private void Configure(string vm) => Write("weblingo.config", $$"""{ "vm": "{{vm}}" }""");

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static (int Exit, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter { NewLine = "\n" };
        using var error = new StringWriter { NewLine = "\n" };
        var exit = WebLingoCommandLine.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
