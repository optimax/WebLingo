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
