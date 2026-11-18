using System.Net;
using System.Net.Sockets;
using System.Text;
using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:http</c> and <c>std:net</c> (specification, section 7.9), through <c>weblingo</c>, whose scripts reach any host:
/// requests to a server of the test's own, on the loopback address, which answers <c>/echo</c> with what it was sent,
/// <c>/moved</c> with a redirect, and <c>/json</c> with JSON.
/// </summary>
public sealed class NetworkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-network-" + Guid.NewGuid().ToString("N"));
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public NetworkTests()
    {
        Directory.CreateDirectory(_folder);
        _listener.Start();
        _ = Task.Run(Serve);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try { Directory.Delete(_folder, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    [Theory]
    [InlineData("var r = await http.get(\"URL/echo\", { \"X-Test\": \"yes\" }); writeLn([r[\"status\"], r[\"headers\"][\"content-type\"], r[\"body\"]]);",
        "[200, \"text/plain; charset=utf-8\", \"GET /echo x-test=yes \"]")]
    [InlineData("var r = await http.post(\"URL/echo\", \"héllo\"); writeLn([r[\"status\"], r[\"body\"], r[\"bytes\"].length]);",
        "[200, \"POST /echo x-test= héllo\", 25]")]
    [InlineData("var r = await http.request(\"put\", \"URL/echo\", Bytes.from(\"hi\")); writeLn(r[\"body\"]);", "PUT /echo x-test= hi")]
    [InlineData("var r = await http.get(\"URL/moved\"); writeLn([r[\"status\"], r[\"headers\"][\"location\"]]);", "[302, \"http://elsewhere.test/\"]")]
    [InlineData("import { Json } from \"std:json\"; var r = await http.get(\"URL/json\"); writeLn(Json.parse(r[\"body\"])[\"tea\"]);", "Sencha")]
    [InlineData("var all = await Task.all([http.get(\"URL/echo\"), http.get(\"URL/json\")]); writeLn([all[0][\"status\"], all[1][\"status\"]]);",
        "[200, 200]")]
    public void HttpRequestsGiveTheResponse(string source, string printed)
    {
        var (exit, output, error) = Weblingo("import http from \"std:http\"; " + source.Replace("URL", $"http://127.0.0.1:{Port}", StringComparison.Ordinal));
        Assert.Equal((WebLingoCommandLine.Success, printed + "\n", ""), (exit, output, error));
    }

    [Fact]
    public void ASocketTalksToTheServer()
    {
        var source = "import { Socket } from \"std:net\"; var s = await Socket.connect(\"127.0.0.1\", PORT); "
                     + "await s.write(\"GET /echo HTTP/1.1\\r\\nHost: t\\r\\nConnection: close\\r\\n\\r\\n\"); var all = \"\"; "
                     + "var piece = await s.read(); while (piece != null) { all += piece.text(); piece = await s.read(); } s.dispose(); s.dispose(); "
                     + "writeLn([all.startsWith(\"HTTP/1.1 200\"), all.endsWith(\"GET /echo x-test= \"), s is Socket]);";
        var (exit, output, error) = Weblingo(source.Replace("PORT", Port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.Equal((WebLingoCommandLine.Success, "[true, true, true]\n", ""), (exit, output, error));
    }

    [Theory]
    [InlineData("import http from \"std:http\"; try { await http.get(\"http://127.0.0.1:CLOSED/\"); } catch (NetworkError e) { writeLn(e.code); }", "WL3014")]
    [InlineData("import { Socket } from \"std:net\"; try { await Socket.connect(\"127.0.0.1\", CLOSED); } catch (NetworkError e) { writeLn(e.code); }", "WL3014")]
    [InlineData("import http from \"std:http\"; try { await http.get(\"ftp://127.0.0.1/\"); } catch (e) { writeLn(e.code); }", "WL3001")]
    [InlineData("import http from \"std:http\"; try { http.get(1); } catch (e) { writeLn([e.code, e.message]); }",
        "[\"WL3001\", \"An address is a string, \\\"https://example.com/\\\", not the integer 1.\"]")]
    [InlineData("import http from \"std:http\"; try { http.get(\"http://x/\", [1]); } catch (e) { writeLn(e.code); }", "WL3001")]
    [InlineData("import { Socket } from \"std:net\"; try { Socket.connect(\"127.0.0.1\", 70000); } catch (e) { writeLn(e.code); }", "WL3001")]
    public void FailuresAreErrors(string source, string printed)
    {
        // A port nothing listens on: one the system gave, then let go of.
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        var (exit, output, error) = Weblingo(source.Replace("CLOSED", port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.Equal((WebLingoCommandLine.Success, printed + "\n", ""), (exit, output, error));
    }

    private (int Exit, string Output, string Error) Weblingo(string source)
    {
        var path = Path.Combine(_folder, "program.wl");
        File.WriteAllText(path, source);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(["run", path], output, error);
        return (exit, output.ToString(), error.ToString().Replace(path, "program.wl", StringComparison.Ordinal));
    }

    /// <summary>The test's server: each connection one request, answered and closed.</summary>
    private async Task Serve()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            _ = Task.Run(() => Answer(client));
        }
    }

    private static async Task Answer(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        var received = new List<byte>();
        var buffer = new byte[4096];
        int end;
        while ((end = HeadersEnd(received)) < 0)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0) return;
            received.AddRange(buffer.AsSpan(0, count).ToArray());
        }
        var head = Encoding.ASCII.GetString([.. received.GetRange(0, end)]);
        var lines = head.Split("\r\n");
        var (method, path) = (lines[0].Split(' ')[0], lines[0].Split(' ')[1]);
        var length = 0;
        var test = "";
        foreach (var line in lines)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line[15..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            if (line.StartsWith("X-Test:", StringComparison.OrdinalIgnoreCase)) test = line[7..].Trim();
        }
        while (received.Count < end + 4 + length)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0) break;
            received.AddRange(buffer.AsSpan(0, count).ToArray());
        }
        var body = Encoding.UTF8.GetString([.. received.GetRange(end + 4, Math.Min(length, received.Count - end - 4))]);
        var (status, type, text, extra) = path switch
        {
            "/moved" => ("302 Found", "text/plain; charset=utf-8", "", "Location: http://elsewhere.test/\r\n"),
            "/json" => ("200 OK", "application/json", "{\"tea\": \"Sencha\"}", ""),
            _ => ("200 OK", "text/plain; charset=utf-8", $"{method} {path} x-test={test} {body}", "")
        };
        var content = Encoding.UTF8.GetBytes(text);
        var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {content.Length}\r\n{extra}Connection: close\r\n\r\n");
        await stream.WriteAsync(response);
        await stream.WriteAsync(content);
    }

    private static int HeadersEnd(List<byte> received)
    {
        for (var i = 0; i + 3 < received.Count; i++)
        {
            if (received[i] == '\r' && received[i + 1] == '\n' && received[i + 2] == '\r' && received[i + 3] == '\n') return i;
        }
        return -1;
    }
}
