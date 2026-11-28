using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// A client of the debug adapter's (specification, section 5.7), for tests: <c>weblingo --debug-adapter</c> or
/// <c>wasp --debug-adapter</c> started beside them, spoken to by the Debug Adapter Protocol, its events kept until asked for.
/// Each request's answer, and each event, is waited for 30 s at most from when it is asked for. Its requests are written,
/// and the adapter's messages read, by threads of its own, a thread of its own reading them: never by the thread pool's,
/// which tests running beside it may keep busy for longer than that. Until 24 November 2026 the whole session had 30 s from
/// its start, and its pipes were read and written through the pool, so that it failed on a loaded machine ("Flaky Tests
/// Found Under Load").
/// </summary>
public sealed class DebugAdapterSession : IAsyncDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private readonly Process? _process;
    private readonly Stream _toAdapter;
    private readonly Stream _fromAdapter;
    private readonly Thread? _adapter;
    private readonly TcpClient? _connection;
    private readonly BlockingCollection<JsonElement> _messages = [];
    private readonly Thread _reader;
    private readonly List<JsonElement> _events = [];
    private int _sequence;

    /// <summary>A session of <paramref name="adapter"/>'s, <c>weblingo</c> or <c>wasp</c>, beside the tests.</summary>
    public DebugAdapterSession(string adapter = "weblingo")
    {
        var binary = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? adapter + ".exe" : adapter);
        _process = Process.Start(new ProcessStartInfo(binary, ["--debug-adapter"])
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
        })!;
        (_toAdapter, _fromAdapter) = (_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream);
        _reader = new Thread(ReadAll) { IsBackground = true, Name = "The debug adapter's messages" };
        _reader.Start();
    }

    /// <summary>
    /// A session of an adapter run in the tests' own process, on a thread of its own, by <paramref name="run"/>, given the
    /// stream it reads the session's requests from and writes its messages to, a connection of this machine's, as a site's
    /// own process runs the adapter a debugger attaches to (section 5.7, "Debugging a site where it runs, parts", part 1).
    /// </summary>
    public DebugAdapterSession(Action<Stream> run)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        client.Connect((IPEndPoint)listener.LocalEndpoint);
        var adapter = listener.AcceptTcpClient();
        _connection = client;
        _toAdapter = _fromAdapter = client.GetStream();
        _adapter = new Thread(() =>
        {
            using var connection = adapter;
            // Its messages end with it, so that the session's reader does.
            run(connection.GetStream());
        }) { IsBackground = true, Name = "The debug adapter in the tests' process" };
        _adapter.Start();
        _reader = new Thread(ReadAll) { IsBackground = true, Name = "The debug adapter's messages" };
        _reader.Start();
    }

    /// <summary>Attaches to the site its adapter runs beside (section 5.7, "Debugging a site where it runs, parts", part 1).</summary>
    public async Task Attach()
    {
        await Request("initialize", new { adapterID = "wasp", linesStartAt1 = true, columnsStartAt1 = true, pathFormat = "path" });
        await Request("attach", new { });
        await Event("initialized");
    }

    public async Task Start(string program, bool stopOnEntry = false, string[]? args = null, string? root = null)
    {
        await Request("initialize", new { adapterID = "weblingo", linesStartAt1 = true, columnsStartAt1 = true, pathFormat = "path" });
        if (root is null) await Request("launch", new { program, stopOnEntry, args = args ?? [] });
        else await Request("launch", new { program, stopOnEntry, args = args ?? [], root });
        await Event("initialized");
    }

    /// <summary>Launches a site, served at <paramref name="root"/> on a port the system chooses (section 5.7, part 2).</summary>
    public async Task Serve(string root)
    {
        await Request("initialize", new { adapterID = "wasp", linesStartAt1 = true, columnsStartAt1 = true, pathFormat = "path" });
        await Request("launch", new { root, port = 0 });
        await Event("initialized");
    }

    public Task<JsonElement> Request(string command, object? arguments = null)
    {
        var deadline = Environment.TickCount64 + (long)Deadline.TotalMilliseconds;
        var seq = ++_sequence;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { seq, type = "request", command, arguments });
        var stream = _toAdapter;
        stream.Write(Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n"));
        stream.Write(payload);
        stream.Flush();
        while (true)
        {
            var message = Next(deadline, $"the answer to '{command}'");
            if (message.GetProperty("type").GetString() == "event")
            {
                _events.Add(message);
                continue;
            }
            Assert.Equal(seq, message.GetProperty("request_seq").GetInt32());
            Assert.True(message.GetProperty("success").GetBoolean(), message.GetRawText());
            return Task.FromResult(message.TryGetProperty("body", out var body) ? body : default);
        }
    }

    public Task<JsonElement> Event(string name)
    {
        var deadline = Environment.TickCount64 + (long)Deadline.TotalMilliseconds;
        while (true)
        {
            var index = _events.FindIndex(message => message.GetProperty("event").GetString() == name);
            if (index >= 0)
            {
                var message = _events[index];
                _events.RemoveAt(index);
                return Task.FromResult(message.TryGetProperty("body", out var body) ? body : default);
            }
            var next = Next(deadline, $"the event '{name}'");
            Assert.Equal("event", next.GetProperty("type").GetString());
            _events.Add(next);
        }
    }

    public async Task<JsonElement> Frames(int threadId = 1) =>
        (await Request("stackTrace", new { threadId })).GetProperty("stackFrames");

    public async Task<JsonElement> Variables(JsonElement frame)
    {
        var scopes = (await Request("scopes", new { frameId = frame.GetProperty("id").GetInt32() })).GetProperty("scopes");
        return (await Request("variables", new { variablesReference = scopes[0].GetProperty("variablesReference").GetInt32() }))
            .GetProperty("variables");
    }

    public async Task<Dictionary<string, string>> Locals(JsonElement frame) =>
        (await Variables(frame)).EnumerateArray().ToDictionary(variable => variable.GetProperty("name").GetString()!,
            variable => variable.GetProperty("value").GetString()!);

    /// <summary>The adapter's next message, by <paramref name="deadline"/>, or a failure naming what was awaited.</summary>
    private JsonElement Next(long deadline, string awaited)
    {
        var left = Math.Max(0, deadline - Environment.TickCount64);
        if (_messages.TryTake(out var message, TimeSpan.FromMilliseconds(left))) return message;
        throw new TimeoutException(_messages.IsAddingCompleted
            ? $"The debug adapter ended before {awaited}."
            : $"{awaited} did not come within {Deadline.TotalSeconds} s.");
    }

    /// <summary>The adapter's messages, read on the session's own thread until its output ends.</summary>
    private void ReadAll()
    {
        try
        {
            var stream = _fromAdapter;
            while (Read(stream) is { } message) _messages.Add(message);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The adapter's output closed under the session as it was disposed.
        }
        finally
        {
            // Once disposed, the session has done with its messages: a reader slower to end than its wait finds the
            // collection gone, which it must not fail on, a thread's exception ending the test host.
            try { _messages.CompleteAdding(); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>A message, its header and its payload, read synchronously, or null where the output ends before one.</summary>
    private static JsonElement? Read(Stream stream)
    {
        var header = new StringBuilder();
        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            var b = stream.ReadByte();
            if (b < 0) return null;
            header.Append((char)b);
        }
        const string prefix = "Content-Length: ";
        Assert.StartsWith(prefix, header.ToString());
        var count = int.Parse(header.ToString().AsSpan(prefix.Length).Trim(), CultureInfo.InvariantCulture);
        var payload = new byte[count];
        stream.ReadExactly(payload);
        return JsonSerializer.Deserialize<JsonElement>(payload);
    }

    public ValueTask DisposeAsync()
    {
        if (_process is null)
        {
            // Its requests end, so that the adapter in the tests' process ends, and its messages with it.
            _connection!.Client.Shutdown(SocketShutdown.Send);
            _adapter!.Join(TimeSpan.FromSeconds(10));
            _connection.Dispose();
        }
        else if (!_process.HasExited)
        {
            _process.StandardInput.Close();
            if (!_process.WaitForExit(2_000))
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }
        }
        var read = _reader.Join(TimeSpan.FromSeconds(10));
        _process?.Dispose();
        _fromAdapter.Dispose();
        if (read) _messages.Dispose();
        return ValueTask.CompletedTask;
    }
}
