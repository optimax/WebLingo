using System.Diagnostics;
using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:workers</c> (specification, section 9, "M0.26 parts", part 2; the owner's answer, A1 (a) of "M0.26, Part 2: A
/// Worker"), through <c>weblingo</c>: a module run on a worker of its own, which a <c>Worker</c> and the worker's
/// <c>parent</c> exchange copied values with, <c>post</c> and <c>receive</c> at both ends; a worker's uncaught exception
/// its parent's next <c>receive()</c>'s, <c>null</c> after; a worker going on while its tasks wait, ended when disposed or
/// when its program ends; and the module's errors, each message tested.
/// </summary>
public sealed class WorkersTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-workers-" + Guid.NewGuid().ToString("N"));

    private const string Imports = "import { writeLn } from \"std:console\"; import { Worker, parent } from \"std:workers\";\n";

    public WorkersTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AWorkerAnswersWhatItsParentPosts_EachValueCopied()
    {
        Write("echo.wlg", Imports + """
            writeLn(parent == null);
            while (true) {
                var value = await parent.receive();
                if (value == "stop") break;
                parent.post([value, value]);
            }
            """);
        var (output, error) = Run(Imports + """
            import { Bytes } from "std:bytes";
            writeLn(parent);
            var worker = new Worker("echo.wlg");
            var shared = [1];
            foreach (var value in [null, true, 7, 1.5, 2.50m, "tea", Bytes.from("hi"), [shared, shared], { "a": #{1, "b"} }]) {
                worker.post(value);
                writeLn(await worker.receive());
            }
            worker.post("stop");
            """);
        Assert.Equal("", error);
        Assert.Equal("""
            null
            false
            [null, null]
            [true, true]
            [7, 7]
            [1.5, 1.5]
            [2.5m, 2.5m]
            ["tea", "tea"]
            [Bytes:[104, 105], Bytes:[104, 105]]
            [[[1], [1]], [[1], [1]]]
            [{"a": #{1, "b"}}, {"a": #{1, "b"}}]

            """.ReplaceLineEndings("\n"), Sorted(output));
    }

    [Fact]
    public void AWorkersSharedReferencesArriveShared()
    {
        Write("same.wlg", Imports + "var pair = await parent.receive(); parent.post(pair[0] == pair[1]); pair[0].push(2); parent.post(pair);");
        var (output, error) = Run(Imports + """
            var worker = new Worker("same.wlg");
            var shared = [1];
            worker.post([shared, shared]);
            writeLn(await worker.receive());
            writeLn(await worker.receive());
            writeLn(shared);
            """);
        Assert.Equal(("true\n[[1, 2], [1, 2]]\n[1]\n", ""), (output, error));
    }

    [Fact]
    public void AWorkersUncaughtErrorIsItsParentsNextReceive_ThenNull()
    {
        Write("throws.wlg", Imports + """
            class NotFound extends Error { constructor(message) { super(message); this.code = "E42"; } }
            parent.post("first");
            throw new NotFound("no such tea");
            """);
        var (output, error) = Run(Imports + """
            var worker = new Worker("throws.wlg");
            writeLn(await worker.receive());
            try { await worker.receive(); }
            catch (e) { writeLn([e is Error, e.className, e.message, e.code, e.thrownAt.endsWith("throws.wlg(4,1)")]); }
            writeLn(await worker.receive());
            """);
        Assert.Equal(("first\n[true, \"NotFound\", \"no such tea\", \"E42\", true]\nnull\n", ""), (output, error));
    }

    [Fact]
    public void AWorkersRuntimeErrorArrivesAsAnErrorOfItsClass_AThrownValueAsItself()
    {
        Write("divides.wlg", "var zero = 0;\nvar x = 1 \\ zero;\n");
        Write("five.wlg", "throw 5;");
        var (output, error) = Run(Imports + """
            try { await new Worker("divides.wlg").receive(); }
            catch (e) { writeLn([e.className, e.message, e.code, e.thrownAt.endsWith("divides.wlg(2,9)")]); }
            try { await new Worker("five.wlg").receive(); } catch (e) { writeLn(e); }
            """);
        Assert.Equal(("[\"DivisionByZeroError\", \"1 \\\\ 0 divides by zero.\", \"WL3013\", true]\n5\n", ""), (output, error));
    }

    [Fact]
    public void AnErrorNothingCatchesStopsTheParentWhereItReceived()
    {
        Write("throws.wlg", "throw new Error(\"broken\");");
        var (exit, output, error) = Weblingo("run", Main(Imports + "var worker = new Worker(\"throws.wlg\");\nawait worker.receive();\n"));
        Assert.Equal((WebLingoCommandLine.RuntimeFailed, ""), (exit, output));
        Assert.Contains("main.wlg(3,1): ERROR WL3008 (Error): An error was thrown and not caught: broken", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AWorkerGoesOnWhileItsTasksWait_UntilItIsDisposed()
    {
        // Its top level ended, a task waiting for its parent's next message (A1 (a)).
        Write("serves.wlg", Imports + "async function serve() { while (true) { parent.post(10 * await parent.receive()); } }\nserve();");
        var (output, error) = Run(Imports + """
            var worker = new Worker("serves.wlg");
            worker.post(5);
            writeLn(await worker.receive());
            worker.post(6);
            writeLn(await worker.receive());
            worker.dispose();
            worker.dispose();
            try { await worker.receive(); } catch (e) { writeLn(e.message); }
            using (var other = new Worker("serves.wlg")) { other.post(1); writeLn(await other.receive()); }
            """);
        Assert.Equal(("50\n60\nThis Worker has been disposed.\n10\n", ""), (output, error));
    }

    [Fact]
    public void AWorkerThatHasEndedGivesNull_WhatItPostedFirst()
    {
        Write("ends.wlg", Imports + "parent.post(1); parent.post(2);");
        var (output, error) = Run(Imports + """
            var worker = new Worker("ends.wlg");
            writeLn([await worker.receive(), await worker.receive(), await worker.receive(), await worker.receive()]);
            worker.post("nobody");
            """);
        Assert.Equal(("[1, 2, null, null]\n", ""), (output, error));
    }

    [Fact]
    public void AWorkerEndsWithItsProgram()
    {
        Write("loops.wlg", "var i = 0;\nwhile (true) { i++; }");
        Write("waits.wlg", Imports + "await parent.receive();");
        var watch = Stopwatch.StartNew();
        var (output, error) = Run(Imports + "var loops = new Worker(\"loops.wlg\");\nvar waits = new Worker(\"waits.wlg\");\nwriteLn(\"done\");");
        Assert.Equal(("done\n", ""), (output, error));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"{watch.Elapsed}");
    }

    [Fact]
    public void AWorkerLetGoOfGoesOn_NoCollectionEndingIt()
    {
        // The worker waits for a file the program writes once it has made enough to collect, then answers with another; each
        // new in the loop, since the VM keeps what new made last, the Worker until then (a collection ending it failed this).
        var (go, done) = (Path.Combine(_folder, "go.txt").Replace('\\', '/'), Path.Combine(_folder, "done.txt").Replace('\\', '/'));
        Write("letgo.wlg", $$"""
            import fs from "std:fs";
            while (!fs.existsSync("{{go}}")) await delay(10);
            fs.writeTextSync("{{done}}", "still running");
            """);
        var (output, error) = Run(Imports + $$"""
            import fs from "std:fs";
            function start() { new Worker("letgo.wlg"); }
            start();
            var made = null;
            class Box { constructor(i) { this.i = [i]; } }
            for (var i = 0; i < 200000; i++) made = new Box(i);
            fs.writeTextSync("{{go}}", "go");
            for (var waited = 0; waited < 3000 && !fs.existsSync("{{done}}"); waited++) await delay(10);
            writeLn(fs.readTextSync("{{done}}"));
            """);
        Assert.Equal(("still running\n", ""), (output, error));
    }

    [Fact]
    public void WorkersStartWorkers_AndWriteWhereTheProgramWrites()
    {
        Write("middle.wlg", Imports + """
            writeLn("middle");
            var inner = new Worker("inner.wlg");
            inner.post(await parent.receive());
            parent.post(await inner.receive() + 1);
            """);
        Write("inner.wlg", Imports + "parent.post(10 * await parent.receive());");
        var (output, error) = Run(Imports + "var worker = new Worker(\"middle.wlg\");\nworker.post(3);\nwriteLn(await worker.receive());");
        Assert.Equal(("middle\n31\n", ""), (output, error));
    }

    [Fact]
    public void AWorkersModuleIsFoundFromTheProgramsFolder_ItsImportsFromItsOwn()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "lib"));
        Write("lib/work.wlg", Imports + "import { twice } from \"twice.wlg\";\nparent.post(twice(await parent.receive()));");
        Write("lib/twice.wlg", "export function twice(n) { return 2 * n; }");
        var (output, error) = Run(Imports + "var worker = new Worker(\"lib/work\");\nworker.post(21);\nwriteLn(await worker.receive());");
        Assert.Equal(("42\n", ""), (output, error));
    }

    [Fact]
    public void ErrorsAreTheSpecifiedOnes()
    {
        Write("serves.wlg", Imports + "async function serve() { while (true) { parent.post(await parent.receive()); } }\nserve();");
        Write("broken.wlg", "var x = ;");
        var (output, error) = Run(Imports + """
            class Point { constructor() { this.x = 1; } }
            var worker = new Worker("serves.wlg");
            try { worker.post([1, new Point()]); } catch (e) { writeLn($"{e.code} {e.message}"); }
            try { worker.post(writeLn); } catch (e) { writeLn($"{e.code} {e.message}"); }
            try { new Worker("missing"); } catch (e) { writeLn($"{e.code} {e is IOError} {e.message}"); }
            try { new Worker(5); } catch (e) { writeLn($"{e.code} {e.message}"); }
            try { new Worker("broken.wlg"); } catch (e) { writeLn($"{e.code} {e.message.startsWith("The worker's module 'broken.wlg' does not compile: ")}"); }
            """);
        Assert.Equal("", error);
        Assert.Equal("""
            WL3001 A message holds null, Booleans, numbers, decimals, strings, bytes, dates, arrays, maps and sets of them, and errors, not an instance of Point.
            WL3001 A message holds null, Booleans, numbers, decimals, strings, bytes, dates, arrays, maps and sets of them, and errors, not a function.
            WL3011 true The worker's module 'missing' is not there.
            WL3001 The argument of 'Worker' must be a module's path, a string, not the integer 5.
            WL3001 true

            """.ReplaceLineEndings("\n"), output);
    }

    [Fact]
    public void ACompiledProgramRunsWorkersToo()
    {
        Write("serves.wlg", Imports + "parent.post(await parent.receive() + 1);");
        var path = Main(Imports + "var worker = new Worker(\"serves.wlg\");\nworker.post(1);\nwriteLn(await worker.receive());");
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", path).Exit);
        Assert.Equal((WebLingoCommandLine.Success, "2\n", ""), Weblingo("run", Path.ChangeExtension(path, ".wlb")));
    }

    private const string Maths = """
        import { writeLn } from "std:console";
        writeLn("maths runs");
        export function square(n) { return n * n; }
        export async function later(n, wait = 10) { await delay(wait); return n + 1; }
        export function sum(...values) { var total = 0; foreach (var v in values) total += v; return total; }
        export function fails(message) { throw new Error(message); }
        export function pair(n) { return [n, { "n": n }]; }
        export const half = (n) => n / 2;
        """;

    [Fact]
    public void APoolRunsAModulesFunctions_TheirArgumentsAndResultsCopied()
    {
        // Section 9, "M0.26 parts", part 3: workers.run(path, name, arguments), the module's function run on a pool's worker.
        Write("maths.wlg", Maths);
        var (output, error) = Run("""
            import { writeLn } from "std:console";
            import workers from "std:workers";
            import { run } from "std:workers";
            writeLn([await workers.run("maths.wlg", "square", [7]), await run("maths", "sum", [1, 2, 3]), await run("maths.wlg", "sum", null)]);
            writeLn([await run("maths.wlg", "later", [1]), await run("maths.wlg", "half", [9]), await run("maths.wlg", "pair", [2])]);
            """);
        Assert.Equal(("maths runs\n[49, 6, 0]\n[2, 4.5, [2, {\"n\": 2}]]\n", ""), (output, error));
    }

    [Fact]
    public void APoolsWorkersRunCallsAtOnce_AsManyAsTheProcessors_TheModuleOnceOnEach()
    {
        Write("maths.wlg", Maths);
        var (output, error) = Run("""
            import { writeLn } from "std:console";
            import { run } from "std:workers";
            var calls = [];
            for (var i = 0; i < 40; i++) calls.push(run("maths.wlg", "later", [i, 20]));
            writeLn(await Task.all(calls));
            """);
        Assert.Equal("", error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("[" + string.Join(", ", Enumerable.Range(1, 40)) + "]", lines[^1]);
        var runs = lines.Count(line => line == "maths runs");
        Assert.InRange(runs, 1, Environment.ProcessorCount);
        Assert.Equal(lines.Length - 1, runs);
    }

    [Fact]
    public void APoolsExceptionIsItsCallsTask_ThePoolGoingOn()
    {
        Write("maths.wlg", Maths);
        var (output, error) = Run("""
            import { writeLn } from "std:console";
            import { run } from "std:workers";
            try { await run("maths.wlg", "fails", ["broken"]); }
            catch (e) { writeLn([e is Error, e.className, e.message, e.thrownAt.endsWith("maths.wlg(6,34)")]); }
            writeLn(await run("maths.wlg", "square", [3]));
            """);
        Assert.Equal(("maths runs\n[true, \"Error\", \"broken\", true]\nmaths runs\n9\n", ""), (output, error));
    }

    [Fact]
    public void APoolsErrorsAreTheSpecifiedOnes()
    {
        Write("maths.wlg", Maths);
        Write("broken.wlg", "export function f() { return ; ");
        var (output, error) = Run("""
            import { writeLn } from "std:console";
            import { run } from "std:workers";
            class Point {}
            foreach (var call in [() => run("maths.wlg", "cube", [3]), () => run("maths.wlg", "square", [3, 4]),
                                  () => run("maths.wlg", "later", []), () => run("maths.wlg", "sum", [new Point()]),
                                  () => run("nothere.wlg", "f"), () => run("maths.wlg", "square", 3), () => run(5, "f"),
                                  () => run("maths.wlg", null)]) {
                try { await call(); } catch (e) { writeLn($"{e.code} {e.message}"); }
            }
            try { await run("broken.wlg", "f"); } catch (e) { writeLn($"{e.code} {e.message.startsWith("The pool's module 'broken.wlg' does not compile: ")}"); }
            """);
        Assert.Equal("", error);
        Assert.Equal("""
            WL3006 The module 'maths.wlg' exports no function 'cube'.
            WL2006 'square' takes 1 argument, not 2.
            WL2006 'later' takes 1 to 2 arguments, not 0.
            WL3001 A message holds null, Booleans, numbers, decimals, strings, bytes, dates, arrays, maps and sets of them, and errors, not an instance of Point.
            WL3011 The pool's module 'nothere.wlg' is not there.
            WL3001 The third argument of 'run', the arguments, must be an array of the function's arguments, or null for none, not the integer 3.
            WL3001 The first argument of 'run', the path, must be a module's path, a string, not the integer 5.
            WL3001 The second argument of 'run', the name, must be the name of a function the module exports, a string, not null.
            WL3001 true

            """.ReplaceLineEndings("\n"), output);
    }

    [Fact]
    public void AsManyWorkersRunAsTheLimitsAllow_ThePoolWithinThem()
    {
        // Section 9, "M0.26 parts", part 4: "limits"' "workers", a Worker beyond them an error where it is made.
        Write("weblingo.config", "{ \"limits\": { \"workers\": 2 } }");
        Write("waits.wlg", Imports + "await parent.receive();");
        Write("maths.wlg", Maths);
        var (output, error) = Run("""
            import { writeLn } from "std:console";
            import { Worker, run } from "std:workers";
            var a = new Worker("waits.wlg");
            var b = new Worker("waits.wlg");
            try { new Worker("waits.wlg"); } catch (e) { writeLn($"{e.code} {e.message}"); }
            b.post(null);
            writeLn(await b.receive());
            // One worker left for the pool, which runs its calls on it one after another.
            var calls = [];
            for (var i = 0; i < 6; i++) calls.push(run("maths.wlg", "later", [i, 5]));
            writeLn(await Task.all(calls));
            """);
        Assert.Equal("", error);
        Assert.Equal("WL3001 This program runs 2 workers at once, the most \"limits\"' \"workers\" in weblingo.config allows: one must end "
                     + "before another starts.\nnull\nmaths runs\n[1, 2, 3, 4, 5, 6]\n", output);
    }

    [Fact]
    public void AWorkerPastItsLimitsIsStopped_ItsErrorItsParentsNextReceive()
    {
        Write("weblingo.config", "{ \"limits\": { \"workerTimeout\": 0.5, \"workerInstructions\": 5000000, \"workerMemory\": 1 } }");
        Write("waits.wlg", "await delay(100000);");
        Write("loops.wlg", "var i = 0;\nwhile (true) { i++; }");
        Write("grows.wlg", "var s = \"x\";\nwhile (true) s = s + s;");
        Write("maths.wlg", Maths + "\nexport function spin(n) { var i = 0; while (i < n) i++; return i; }");
        var watch = Stopwatch.StartNew();
        var (output, error) = Run("""
            import { writeLn } from "std:console";
            import { Worker, run } from "std:workers";
            foreach (var path in ["waits.wlg", "loops.wlg", "grows.wlg"]) {
                try { await new Worker(path).receive(); } catch (e) { writeLn($"{e.code} {e.message}"); }
            }
            // A pool's worker's time is each call's, its instructions and its memory its life's.
            try { await run("maths.wlg", "later", [1, 2000]); } catch (e) { writeLn($"{e.code} {e.message}"); }
            writeLn(await run("maths.wlg", "later", [1, 10]));
            try { await run("maths.wlg", "spin", [100000000]); } catch (e) { writeLn($"{e.code} {e.message}"); }
            writeLn(await run("maths.wlg", "spin", [1000]));
            """);
        Assert.Equal("", error);
        Assert.Equal("""
            WL3003 The worker took longer than 0.5 seconds, its limit, "limits"' "workerTimeout" in weblingo.config.
            WL3003 The program ran 5000000 instructions, its whole budget, and was stopped.
            WL3003 A string of 524288 characters does not fit in the memory this execution may use.
            maths runs
            WL3003 The call took longer than 0.5 seconds, its limit, "limits"' "workerTimeout" in weblingo.config.
            maths runs
            2
            WL3003 The program ran 5000000 instructions, its whole budget, and was stopped.
            maths runs
            1000

            """.ReplaceLineEndings("\n"), output);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"{watch.Elapsed}");
    }

    [Theory]
    [InlineData("{ \"workers\": 0 }")]
    [InlineData("{ \"workers\": 1025 }")]
    [InlineData("{ \"workerTimeout\": 0 }")]
    [InlineData("{ \"workerInstructions\": 0 }")]
    [InlineData("{ \"workerMemory\": 0 }")]
    [InlineData("{ \"workers\": \"8\" }")]
    public void WorkersLimitsAreChecked(string limits)
    {
        Write("weblingo.config", "{ \"limits\": " + limits + " }");
        var (exit, _, error) = Weblingo("run", Main("var x = 1;"));
        Assert.Equal(WebLingoCommandLine.FileUnreadable, exit);
        Assert.Contains("weblingo.config: \"limits\" is an object, { \"requestTimeout\": 60,", error, StringComparison.Ordinal);
        Assert.Contains("\"workers\" an integer from 1 to 1024", error, StringComparison.Ordinal);
    }

    /// <summary>The worker's first line, which it writes on a thread of its own, put first, so that the test does not depend on when it runs.</summary>
    private static string Sorted(string output)
    {
        var lines = output.Split('\n').ToList();
        var mine = lines.IndexOf("false");
        lines.RemoveAt(mine);
        lines.Insert(1, "false");
        return string.Join('\n', lines);
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(_folder, name), text);

    private string Main(string program)
    {
        var path = Path.Combine(_folder, "main.wlg");
        File.WriteAllText(path, program);
        return path;
    }

    private (string Output, string Error) Run(string program)
    {
        var (exit, output, error) = Weblingo("run", Main(program));
        Assert.True(exit == WebLingoCommandLine.Success, error);
        return (output, error);
    }

    private static (int Exit, string Output, string Error) Weblingo(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
