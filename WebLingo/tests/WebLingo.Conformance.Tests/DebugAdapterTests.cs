using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit;
namespace WebLingo.Conformance.Tests;

public sealed class DebugAdapterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-debug-" + Guid.NewGuid().ToString("N"));

    public DebugAdapterTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task BreakpointsAndStepsUseRealFramesAndVariables()
    {
        var math = Write("math.wlg", """
            export function twice(value) {
                var total = value * 2;
                return total;
            }
            """);
        var program = Write("example.wlg", """
            param label = "default";
            import { twice } from "./math.wlg"; import { writeLn } from "std:console";
            var count = 2;
            var result = twice(count);
            writeLn($"{label}: {result}");
            // A comment is not executable.
            """);

        await using var dap = new DebugAdapterSession();
        await dap.Start(program, args: ["label=ś"]);
        var breakpoints = (await dap.Request("setBreakpoints", new
        {
            source = new { path = program },
            breakpoints = new[] { new { line = 4 }, new { line = 6 } }
        })).GetProperty("breakpoints");
        var id = breakpoints[0].GetProperty("id").GetInt32();
        Assert.False(breakpoints[1].GetProperty("verified").GetBoolean());
        await dap.Request("setExceptionBreakpoints", new { filters = Array.Empty<string>() });
        await dap.Request("configurationDone");
        var verified = (await dap.Event("breakpoint")).GetProperty("breakpoint");
        Assert.True(verified.GetProperty("verified").GetBoolean());
        Assert.Equal(id, verified.GetProperty("id").GetInt32());
        var stopped = await dap.Event("stopped");
        Assert.Equal("breakpoint", stopped.GetProperty("reason").GetString());
        Assert.Equal(id, stopped.GetProperty("hitBreakpointIds")[0].GetInt32());

        var frames = await dap.Frames();
        AssertFrame(frames[0], "main", program, 4);
        var local = await dap.Locals(frames[0]);
        Assert.Equal("2", local["count"]);
        Assert.Equal("function", local["twice"]);
        Assert.Equal("\"ś\"", local["label"]);
        Assert.DoesNotContain("result", local.Keys);
        await dap.Request("stepIn", new { threadId = 1 });
        Assert.Equal("step", (await dap.Event("stopped")).GetProperty("reason").GetString());
        frames = await dap.Frames();
        AssertFrame(frames[0], "twice", math, 2);
        AssertFrame(frames[1], "main", program, 4);
        Assert.Equal("2", (await dap.Locals(frames[0]))["value"]);
        Assert.DoesNotContain("result", (await dap.Locals(frames[1])).Keys);

        await dap.Request("next", new { threadId = 1 });
        await dap.Event("stopped");
        frames = await dap.Frames();
        AssertFrame(frames[0], "twice", math, 3);
        Assert.Equal("4", (await dap.Locals(frames[0]))["total"]);
        await dap.Request("stepOut", new { threadId = 1 });
        await dap.Event("stopped");
        frames = await dap.Frames();
        AssertFrame(frames[0], "main", program, 5);
        Assert.Equal("4", (await dap.Locals(frames[0]))["result"]);

        await dap.Request("continue", new { threadId = 1 });
        Assert.Equal("ś: 4\n", (await dap.Event("output")).GetProperty("output").GetString());
        Assert.Equal(0, (await dap.Event("exited")).GetProperty("exitCode").GetInt32());
        await dap.Event("terminated");
        await dap.Request("disconnect");
    }

    [Fact]
    public async Task EntryStopAndScopesRespectShadowing()
    {
        var program = Write("scopes.wlg", """
            import { writeLn } from "std:console"; var item = 1;
            {
                var item = [2, 3];
                writeLn(item);
            }
            writeLn(item);
            """);
        await using var dap = new DebugAdapterSession();
        await dap.Start(program, stopOnEntry: true);
        await dap.Request("setBreakpoints", new
        {
            source = new { path = program },
            breakpoints = new[] { new { line = 4 }, new { line = 6 } }
        });
        await dap.Request("configurationDone");
        Assert.Equal("entry", (await dap.Event("stopped")).GetProperty("reason").GetString());
        AssertFrame((await dap.Frames())[0], "main", program, 1);
        await dap.Request("continue", new { threadId = 1 });
        await dap.Event("stopped");
        var variables = await dap.Variables((await dap.Frames())[0]);
        var item = variables.EnumerateArray().Single(variable => variable.GetProperty("name").GetString() == "item");
        var children = (await dap.Request("variables", new
        {
            variablesReference = item.GetProperty("variablesReference").GetInt32()
        })).GetProperty("variables");
        Assert.Equal("2", children[0].GetProperty("value").GetString());
        await dap.Request("continue", new { threadId = 1 });
        await dap.Event("stopped");
        Assert.Equal("1", (await dap.Locals((await dap.Frames())[0]))["item"]);
        await dap.Request("terminate");
        Assert.Equal(0, (await dap.Event("exited")).GetProperty("exitCode").GetInt32());
        await dap.Event("terminated");
        await dap.Request("disconnect");
    }

    [Fact]
    public async Task NamedImportsShowTheirLiveExports()
    {
        Write("teas.wlg", """
            export class Tea {}
            export var served = 0;
            export var optional = null;
            export function serve() { served += 1; }
            """);
        var program = Write("example.wlg", """
            import { Tea, serve, served, optional } from "./teas.wlg";
            import { writeLn } from "std:console";
            function pour() { serve(); }
            var sencha = new Tea();
            pour();
            writeLn(served);
            """);

        await using var dap = new DebugAdapterSession();
        await dap.Start(program);
        await dap.Request("setBreakpoints", new
        {
            source = new { path = program },
            breakpoints = new[] { new { line = 5 }, new { line = 6 } }
        });
        await dap.Request("configurationDone");
        await dap.Event("stopped");
        var frame = (await dap.Frames())[0];
        AssertFrame(frame, "main", program, 5);
        var locals = await dap.Locals(frame);
        Assert.Equal("class Tea", locals["Tea"]);
        Assert.Equal("function", locals["serve"]);
        Assert.Equal("0", locals["served"]);
        Assert.Equal("null", locals["optional"]);
        Assert.Equal("function", locals["writeLn"]);

        await dap.Request("continue", new { threadId = 1 });
        await dap.Event("stopped");
        frame = (await dap.Frames())[0];
        AssertFrame(frame, "main", program, 6);
        Assert.Equal("1", (await dap.Locals(frame))["served"]);
        await dap.Request("continue", new { threadId = 1 });
        Assert.Equal(0, (await dap.Event("exited")).GetProperty("exitCode").GetInt32());
        await dap.Request("disconnect");
    }

    /// <summary>
    /// Conditional breakpoints (specification, section 5.7, "Debugging WASP pages, parts", part 3): a condition stops only
    /// where it gives true, read without running the program's code, the built-in classes' methods that only read allowed;
    /// one that fails stops, saying why; and one that cannot be a condition, or reads a name not in scope on its line, leaves
    /// the breakpoint unverified, saying why, before the program runs.
    /// </summary>
    [Fact]
    public async Task ABreakpointsConditionStopsOnlyWhereItHolds()
    {
        var program = Write("loop.wlg", """
            import { writeLn } from "std:console";
            class Cup { constructor(size) { this.size = size; } property doubled { get { return this.size * 2; } } }
            var names = ["Ann", "Bob", "Ava"];
            var total = 0;
            for (var i = 0; i < names.length; i++) {
                var name = names[i];
                total = total + i;
            }
            var cup = new Cup(3);
            writeLn(total);
            """);

        await using var dap = new DebugAdapterSession();
        await dap.Start(program);
        var set = (await dap.Request("setBreakpoints", new
        {
            source = new { path = program },
            breakpoints = new object[]
            {
                new { line = 7, condition = "name.startsWith(\"A\") && i > 0" },
                new { line = 10, condition = "cup.doubled == 6" },
                new { line = 4, condition = "names.push(\"Eve\")" },
                new { line = 4, condition = "i ==" },
                new { line = 10, condition = "nothing == 1" }
            }
        })).GetProperty("breakpoints");
        // What cannot be a condition is refused where it is set, before the program runs.
        Assert.False(set[2].GetProperty("verified").GetBoolean());
        Assert.Contains("does not run the program's code: 'push' is not such a method.", set[2].GetProperty("message").GetString());
        Assert.StartsWith("The condition does not parse:", set[3].GetProperty("message").GetString());
        await dap.Request("configurationDone");

        // Once the program is compiled, a name not in scope on its line is refused too.
        var changes = new List<JsonElement>();
        for (var i = 0; i < 3; i++) changes.Add((await dap.Event("breakpoint")).GetProperty("breakpoint"));
        var unknown = Assert.Single(changes, change => change.GetProperty("id").GetInt32() == set[4].GetProperty("id").GetInt32());
        Assert.Equal((false, "The condition reads 'nothing', which is not in scope on this line."),
            (unknown.GetProperty("verified").GetBoolean(), unknown.GetProperty("message").GetString()));

        // It stops only where it holds: at Ava, not at Ann (i is 0) nor at Bob.
        Assert.Equal("breakpoint", (await dap.Event("stopped")).GetProperty("reason").GetString());
        var locals = await dap.Locals((await dap.Frames())[0]);
        Assert.Equal(("\"Ava\"", "2"), (locals["name"], locals["i"]));

        // A property written in WebLingo is not run: the condition fails, and the breakpoint stops, saying why.
        await dap.Request("continue", new { threadId = 1 });
        var failed = await dap.Event("stopped");
        Assert.Equal("breakpoint", failed.GetProperty("reason").GetString());
        Assert.Contains("is a property written in WebLingo, and a condition does not run the program's code.", failed.GetProperty("text").GetString());
        AssertFrame((await dap.Frames())[0], "main", program, 10);
        await dap.Request("continue", new { threadId = 1 });
        Assert.Equal(0, (await dap.Event("exited")).GetProperty("exitCode").GetInt32());
        await dap.Request("disconnect");
    }

    private string Write(string name, string source)
    {
        var file = Path.Combine(_folder, name);
        File.WriteAllText(file, source);
        return file;
    }

    private static void AssertFrame(JsonElement frame, string name, string path, int line)
    {
        Assert.Equal(name, frame.GetProperty("name").GetString());
        Assert.Equal(path, frame.GetProperty("source").GetProperty("path").GetString());
        Assert.Equal(line, frame.GetProperty("line").GetInt32());
    }
}
