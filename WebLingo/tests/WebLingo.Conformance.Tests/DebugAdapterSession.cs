using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// A client of the debug adapter's (specification, section 5.7), for tests: <c>weblingo --debug-adapter</c> or
/// <c>wasp --debug-adapter</c> started beside them, spoken to by the Debug Adapter Protocol, its events kept until asked for.
/// </summary>
public sealed class DebugAdapterSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly List<JsonElement> _events = [];
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
    private int _sequence;

    /// <summary>A session of <paramref name="adapter"/>'s, <c>weblingo</c> or <c>wasp</c>, beside the tests.</summary>
    public DebugAdapterSession(string adapter = "weblingo")
    {
        var binary = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? adapter + ".exe" : adapter);
        _process = Process.Start(new ProcessStartInfo(binary, ["--debug-adapter"])
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
        })!;
    }

    public async Task Start(string program, bool stopOnEntry = false, string[]? args = null, string? root = null)
    {
        await Request("initialize", new { adapterID = "weblingo", linesStartAt1 = true, columnsStartAt1 = true, pathFormat = "path" });
        if (root is null) await Request("launch", new { program, stopOnEntry, args = args ?? [] });
        else await Request("launch", new { program, stopOnEntry, args = args ?? [], root });
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
