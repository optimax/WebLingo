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
            import { twice } from "./math.wlg";
            var count = 2;
            var result = twice(count);
            writeLn($"{label}: {result}");
            // A comment is not executable.
            """);

        await using var dap = new Session();
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
            var item = 1;
            {
                var item = [2, 3];
                writeLn(item);
            }
            writeLn(item);
            """);
        await using var dap = new Session();
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

    private sealed class Session : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly List<JsonElement> _events = [];
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        private int _sequence;

        public Session()
        {
            var binary = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "weblingo.exe" : "weblingo");
            _process = Process.Start(new ProcessStartInfo(binary, ["--debug-adapter"])
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
            })!;
        }

        public async Task Start(string program, bool stopOnEntry = false, string[]? args = null)
        {
            await Request("initialize", new { adapterID = "weblingo", linesStartAt1 = true, columnsStartAt1 = true, pathFormat = "path" });
            await Request("launch", new { program, stopOnEntry, args = args ?? [] });
            await Event("initialized");
        }

        public async Task<JsonElement> Request(string command, object? arguments = null)
        {
            var seq = ++_sequence;
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { seq, type = "request", command, arguments });
            var stream = _process.StandardInput.BaseStream;
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n"), _timeout.Token);
            await stream.WriteAsync(payload, _timeout.Token);
            await stream.FlushAsync(_timeout.Token);
            while (true)
            {
                var message = await Read();
                if (message.GetProperty("type").GetString() == "event")
                {
                    _events.Add(message);
                    continue;
                }
                Assert.Equal(seq, message.GetProperty("request_seq").GetInt32());
                Assert.True(message.GetProperty("success").GetBoolean(), message.GetRawText());
                return message.TryGetProperty("body", out var body) ? body : default;
            }
        }

        public async Task<JsonElement> Event(string name)
        {
            while (true)
            {
                var index = _events.FindIndex(message => message.GetProperty("event").GetString() == name);
                if (index >= 0)
                {
                    var message = _events[index];
                    _events.RemoveAt(index);
                    return message.TryGetProperty("body", out var body) ? body : default;
                }
                var next = await Read();
                Assert.Equal("event", next.GetProperty("type").GetString());
                _events.Add(next);
            }
        }

        public async Task<JsonElement> Frames() =>
            (await Request("stackTrace", new { threadId = 1 })).GetProperty("stackFrames");

        public async Task<JsonElement> Variables(JsonElement frame)
        {
            var scopes = (await Request("scopes", new { frameId = frame.GetProperty("id").GetInt32() })).GetProperty("scopes");
            return (await Request("variables", new { variablesReference = scopes[0].GetProperty("variablesReference").GetInt32() }))
                .GetProperty("variables");
        }

        public async Task<Dictionary<string, string>> Locals(JsonElement frame) =>
            (await Variables(frame)).EnumerateArray().ToDictionary(variable => variable.GetProperty("name").GetString()!,
                variable => variable.GetProperty("value").GetString()!);

        private async Task<JsonElement> Read()
        {
            var stream = _process.StandardOutput.BaseStream;
            var header = new StringBuilder();
            var buffer = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                await stream.ReadExactlyAsync(buffer, _timeout.Token);
                header.Append((char)buffer[0]);
            }
            const string prefix = "Content-Length: ";
            Assert.StartsWith(prefix, header.ToString());
            var count = int.Parse(header.ToString().AsSpan(prefix.Length).Trim(), CultureInfo.InvariantCulture);
            var payload = new byte[count];
            await stream.ReadExactlyAsync(payload, _timeout.Token);
            return JsonSerializer.Deserialize<JsonElement>(payload);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await _process.WaitForExitAsync(deadline.Token); }
                catch (OperationCanceledException)
                {
                    if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
            _process.Dispose();
            _timeout.Dispose();
        }
    }
}
