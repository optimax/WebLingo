using System.Net;
using System.Net.Sockets;
using System.Text;
using WebLingo.Hosting;
using WebLingo.Hosting.DotNet;
using WebLingo.Hosting.Standalone;
using WebLingo.Wasp.Cli;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// WebLingo's own HTTP server (specification, section 9, "M0.23 parts", part 2): a request's head read strictly, as RFC
/// 9112 has it, and refused where it is malformed, both a length and a transfer coding among it, against request smuggling;
/// its path decoded as ASP.NET Core decodes it; its parser fuzzed; and the server spoken to byte by byte, as a client
/// speaks to it: kept connections, requests sent together, a body by its length and in chunks, <c>100 Continue</c>,
/// <c>HEAD</c>, a response in chunks, HTTP/1.0, and a client that goes. <c>wasp serve</c>'s own tests run on it too
/// (<see cref="StandaloneWaspServeTests"/>).
/// </summary>
public sealed class StandaloneHttpTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wasp-standalone-" + Guid.NewGuid().ToString("N"));
    private readonly WebLingoWorkerPool _workers = new(2);
    private readonly CancellationTokenSource _stop = new();
    private readonly Socket _listener;
    private readonly WebLingoApplication _application;

    public StandaloneHttpTests()
    {
        Write("index.wasp", "<% param request; %><p><%= request.method %> <%= request.path %> <%= request.query?[\"q\"] %> <%= request.host %></p>");
        Write("echo.wasp", "<% param request; var form = await request.form(); %><%= form[\"tea\"] %>");
        Write("big.wasp", "<% for (var i = 0; i < 20000; i++) { %>0123456789<% } %>");
        Write("waits.wasp", "<% import { delay } from \"std:system\"; await delay(60000); %>never");
        Write("css/site.css", "h1 { }");
        _application = WebLingoWaspServer.Load(_root, new Dictionary<string, string>(), WebLingoMode.Production, null, out _, out _, out var error)!;
        Assert.True(_application is not null, error);
        _listener = WebLingoHttpServer.Listen(IPAddress.Loopback, 0);
        _ = new WebLingoHttpServer(_application, _workers).ServeAsync(_listener, _stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        _workers.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private static WebLingoHttpParsed Parse(string text, out WebLingoHttpHead? head, out int status, out string reason) =>
        WebLingoHttpParser.Parse(Encoding.UTF8.GetBytes(text), WebLingoHttpLimits.Default, out head, out _, out status, out reason);

    [Fact]
    public void AHeadIsReadWhole_ItsPathDecodedAndItsQueryAsItCame()
    {
        const string text = "\r\nGET /a%20b/../c?q=1%202 HTTP/1.1\r\nHost: example.com\r\nX-Tea:  green tea \r\nAccept: */*\r\n\r\nrest";
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.Equal(WebLingoHttpParsed.Done, WebLingoHttpParser.Parse(bytes, WebLingoHttpLimits.Default, out var head, out var consumed, out _, out _));
        Assert.Equal(("GET", "/c", "q=1%202", 1, "example.com"), (head!.Method, head.Path, head.Query, head.Minor, head.Host));
        Assert.Equal([("Host", "example.com"), ("X-Tea", "green tea"), ("Accept", "*/*")], head.Headers);
        Assert.Equal(text.Length - 4, consumed);
        Assert.True(head.KeepAlive);
        Assert.False(head.HasBody);
        // Every head cut short is incomplete, never refused.
        for (var length = 0; length < text.Length - 4; length++)
        {
            Assert.Equal(WebLingoHttpParsed.Incomplete, WebLingoHttpParser.Parse(bytes.AsSpan(0, length), WebLingoHttpLimits.Default, out _, out _, out _, out _));
        }
    }

    [Theory]
    [InlineData("/a%2Fb", "/a%2Fb")]
    [InlineData("/a%2fb/../c", "/c")]
    [InlineData("/%2e%2e/%2E%2E/x", "/x")]
    [InlineData("/../..", "/")]
    [InlineData("/a/./b/.", "/a/b/")]
    [InlineData("/caf%C3%A9", "/café")]
    [InlineData("/100%", "/100%")]
    [InlineData("/%zz", "/%zz")]
    public void APathIsDecodedAsAspNetCoreDecodesIt(string raw, string path)
    {
        Assert.True(WebLingoHttpParser.TryDecodePath(raw, out var decoded));
        Assert.Equal(path, decoded);
    }

    [Theory]
    [InlineData("GET /%ff HTTP/1.1\r\nHost: a\r\n\r\n", 400, "not UTF-8")]
    [InlineData("POST / HTTP/1.1\r\nHost: a\r\nContent-Length: 3\r\nTransfer-Encoding: chunked\r\n\r\n", 400, "both a Content-Length and a Transfer-Encoding")]
    [InlineData("POST / HTTP/1.1\r\nHost: a\r\nContent-Length: 3\r\nContent-Length: 4\r\n\r\n", 400, "not one length")]
    [InlineData("POST / HTTP/1.1\r\nHost: a\r\nContent-Length: 3, 4\r\n\r\n", 400, "not one length")]
    [InlineData("POST / HTTP/1.1\r\nHost: a\r\nContent-Length: -3\r\n\r\n", 400, "not one length")]
    [InlineData("POST / HTTP/1.1\r\nHost: a\r\nContent-Length: 0x10\r\n\r\n", 400, "not one length")]
    [InlineData("POST / HTTP/1.1\r\nHost: a\r\nTransfer-Encoding: gzip, chunked\r\n\r\n", 501, "not one this server reads")]
    [InlineData("POST / HTTP/1.1\r\nHost: a\r\nTransfer-Encoding: chunked, chunked\r\n\r\n", 501, "not one this server reads")]
    [InlineData("POST / HTTP/1.0\r\nTransfer-Encoding: chunked\r\n\r\n", 400, "HTTP/1.0 request cannot")]
    [InlineData("GET / HTTP/1.1\r\n\r\n", 400, "has a Host")]
    [InlineData("GET / HTTP/1.1\r\nHost: a\r\nHost: b\r\n\r\n", 400, "more than one Host")]
    [InlineData("GET / HTTP/1.1\nHost: a\r\n\r\n", 400, "without its CR")]
    [InlineData("GET / HTTP/1.1\r\nHost: a\rX: b\r\n\r\n", 400, "without its CR")]
    [InlineData("GET / HTTP/1.1\r\nHost: a\r\nX-Tea: green\r\n tea\r\n\r\n", 400, "folded")]
    [InlineData("GET / HTTP/1.1\r\nHost : a\r\n\r\n", 400, "space before its colon")]
    [InlineData("GET / HTTP/1.1\r\nHost: a\r\nX-Tea: a\u0001b\r\n\r\n", 400, "control character")]
    [InlineData("GET / HTTP/2.0\r\nHost: a\r\n\r\n", 505, "not 1.1 or 1.0")]
    [InlineData("GET / HTTP/1.1 \r\nHost: a\r\n\r\n", 400, "does not end with its HTTP version")]
    [InlineData("GET  / HTTP/1.1\r\nHost: a\r\n\r\n", 400, "not a method, a target and a version")]
    [InlineData("G@T / HTTP/1.1\r\nHost: a\r\n\r\n", 400, "not a token")]
    [InlineData("GET a.wasp HTTP/1.1\r\nHost: a\r\n\r\n", 400, "not a path, an absolute URL")]
    [InlineData("CONNECT example.com:443 HTTP/1.1\r\nHost: a\r\n\r\n", 400, "not a path, an absolute URL")]
    public void AMalformedHeadIsRefused(string text, int status, string reason)
    {
        Assert.Equal(WebLingoHttpParsed.Refused, Parse(text, out _, out var refused, out var why));
        Assert.Equal(status, refused);
        Assert.Contains(reason, why, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeadTooLargeIsRefused_AndOneWithinItsLimitsIsNot()
    {
        var limits = WebLingoHttpLimits.Default;
        Assert.Equal(414, Refused("GET /" + new string('a', limits.RequestLine) + " HTTP/1.1\r\nHost: a\r\n\r\n"));
        Assert.Equal(414, Refused("GET /" + new string('a', limits.RequestLine + 10)));
        Assert.Equal(431, Refused("GET / HTTP/1.1\r\nHost: a\r\n" + string.Concat(Enumerable.Range(0, limits.HeaderCount).Select(i => $"X-{i}: {i}\r\n")) + "\r\n"));
        Assert.Equal(431, Refused("GET / HTTP/1.1\r\nHost: a\r\nX-Big: " + new string('a', limits.RequestHeaders) + "\r\n\r\n"));
        Assert.Equal(WebLingoHttpParsed.Done, Parse("GET /" + new string('a', 8000) + " HTTP/1.1\r\nHost: a\r\n"
                                                    + string.Concat(Enumerable.Range(0, limits.HeaderCount - 1).Select(i => $"X-{i}: {i}\r\n")) + "\r\n", out _, out _, out _));

        static int Refused(string text) => Parse(text, out _, out var status, out _) == WebLingoHttpParsed.Refused ? status : 0;
    }

    [Fact]
    public void ABodysFramingIsRead_AndAnAbsoluteTargetsAuthorityIsTheHost()
    {
        Assert.Equal(WebLingoHttpParsed.Done, Parse("POST /x HTTP/1.1\r\nHost: a\r\nContent-Length: 12, 12\r\nExpect: 100-continue\r\n\r\n", out var head, out _, out _));
        Assert.Equal((12L, false, true, true), (head!.ContentLength, head.Chunked, head.ExpectsContinue, head.HasBody));
        Assert.Equal(WebLingoHttpParsed.Done, Parse("POST /x HTTP/1.1\r\nHost: a\r\nTransfer-Encoding: Chunked\r\nConnection: close\r\n\r\n", out head, out _, out _));
        Assert.Equal((null, true, false), (head!.ContentLength, head.Chunked, head.KeepAlive));
        Assert.Equal(WebLingoHttpParsed.Done, Parse("GET http://example.com:8080?q=1 HTTP/1.1\r\nHost: other\r\n\r\n", out head, out _, out _));
        Assert.Equal(("example.com:8080", "/", "q=1"), (head!.Host, head.Path, head.Query));
        Assert.Equal(WebLingoHttpParsed.Done, Parse("GET / HTTP/1.0\r\n\r\n", out head, out _, out _));
        Assert.Equal((0, "localhost", false), (head!.Minor, head.Host, head.KeepAlive));
        Assert.Equal(WebLingoHttpParsed.Done, Parse("OPTIONS * HTTP/1.0\r\nConnection: Keep-Alive\r\n\r\n", out head, out _, out _));
        Assert.Equal(("*", true), (head!.Path, head.KeepAlive));
    }

    /// <summary>Random bytes, and requests changed at random, read without an exception, whatever they come to.</summary>
    [Fact]
    public void TheParserNeverThrows()
    {
        var random = new Random(23);
        var seeds = new[]
        {
            "GET /a?b=c HTTP/1.1\r\nHost: x\r\nContent-Length: 5\r\n\r\nhello",
            "POST /p HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\nExpect: 100-continue\r\n\r\n5\r\nhello\r\n0\r\n\r\n",
            "OPTIONS * HTTP/1.0\r\n\r\n"
        };
        var alphabet = "\r\n :/?%.-_aZ09\t\u0000\u007Fÿ,;"u8.ToArray();
        for (var i = 0; i < 20000; i++)
        {
            byte[] bytes;
            if (i % 2 == 0)
            {
                bytes = new byte[random.Next(0, 200)];
                for (var j = 0; j < bytes.Length; j++) bytes[j] = random.Next(3) == 0 ? (byte)random.Next(256) : alphabet[random.Next(alphabet.Length)];
            }
            else
            {
                bytes = Encoding.UTF8.GetBytes(seeds[random.Next(seeds.Length)]);
                for (var changes = random.Next(1, 4); changes > 0; changes--) bytes[random.Next(bytes.Length)] = alphabet[random.Next(alphabet.Length)];
            }
            var parsed = WebLingoHttpParser.Parse(bytes, WebLingoHttpLimits.Default, out var head, out var consumed, out var status, out _);
            Assert.True(parsed switch
            {
                WebLingoHttpParsed.Done => head is not null && consumed > 0 && consumed <= bytes.Length,
                WebLingoHttpParsed.Refused => status is 400 or 414 or 431 or 501 or 505,
                _ => head is null
            });
        }
    }

    [Fact]
    public async Task AConnectionIsKept_ItsRequestsSentTogetherAnsweredInTurn()
    {
        using var client = await Connect();
        var stream = client.GetStream();
        await Send(stream, "GET /?q=tea HTTP/1.1\r\nHost: example.com\r\n\r\n");
        var (status, headers, body) = await ReadResponse(stream);
        Assert.Equal((200, "<p>GET / tea example.com</p>"), (status, body));
        Assert.Contains("Content-Length: 28", headers);
        Assert.Contains(headers, header => header.StartsWith("Date: ", StringComparison.Ordinal));
        Assert.Contains("X-Content-Type-Options: nosniff", headers);
        // Two at once, on the same connection, answered in their order.
        await Send(stream, "GET /?q=one HTTP/1.1\r\nHost: a\r\n\r\nHEAD /?q=two HTTP/1.1\r\nHost: a\r\n\r\nGET /css/site.css HTTP/1.1\r\nHost: a\r\n\r\n");
        Assert.Equal((200, "<p>GET / one a</p>"), Pick(await ReadResponse(stream)));
        (status, headers, body) = await ReadResponse(stream, head: true);
        Assert.Equal((200, ""), (status, body));
        Assert.Contains("Content-Length: 19", headers); // "<p>HEAD / two a</p>", which a HEAD does not send.
        (status, headers, body) = await ReadResponse(stream);
        Assert.Equal((200, "h1 { }"), (status, body));
        Assert.Contains("Content-Type: text/css", headers);
        // A page that is not there, and the connection kept after it.
        Assert.Equal(404, (await ReadAfter(stream, "GET /nowhere HTTP/1.1\r\nHost: a\r\n\r\n")).Status);
        Assert.Equal(200, (await ReadAfter(stream, "GET / HTTP/1.1\r\nHost: a\r\nConnection: close\r\n\r\n")).Status);
        Assert.Equal(0, await stream.ReadAsync(new byte[1]));
    }

    [Fact]
    public async Task ABodyIsReadByItsLengthAndInChunks_AContinueSentForOneThatWaits()
    {
        using var client = await Connect();
        var stream = client.GetStream();
        Assert.Equal((200, "green"), Pick(await ReadAfter(stream,
            "POST /echo HTTP/1.1\r\nHost: a\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: 9\r\n\r\ntea=green")));
        Assert.Equal((200, "sencha tea"), Pick(await ReadAfter(stream,
            "POST /echo HTTP/1.1\r\nHost: a\r\nContent-Type: application/x-www-form-urlencoded\r\nTransfer-Encoding: chunked\r\n\r\n"
            + "4;ext=1\r\ntea=\r\n6\r\nsencha\r\n4\r\n+tea\r\n0\r\nX-Trailer: dropped\r\n\r\n")));
        // A client that waits for 100 Continue before its body sends it once told.
        await Send(stream, "POST /echo HTTP/1.1\r\nHost: a\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: 10\r\nExpect: 100-continue\r\n\r\n");
        Assert.Equal("HTTP/1.1 100 Continue\r\n\r\n", await ReadText(stream, 25));
        await Send(stream, "tea=oolong");
        Assert.Equal((200, "oolong"), Pick(await ReadResponse(stream)));
    }

    [Fact]
    public async Task ALargeResponseIsSentInChunks_HttpOnePointZerosToTheConnectionsEnd()
    {
        using (var client = await Connect())
        {
            var (status, headers, body) = await ReadAfter(client.GetStream(), "GET /big HTTP/1.1\r\nHost: a\r\n\r\n");
            Assert.Equal((200, 200000), (status, body.Length));
            Assert.Contains("Transfer-Encoding: chunked", headers);
        }
        using (var client = await Connect())
        {
            var stream = client.GetStream();
            var (status, headers, body) = await ReadAfter(stream, "GET /big HTTP/1.0\r\n\r\n");
            Assert.Equal((200, 200000), (status, body.Length));
            Assert.Contains("Connection: close", headers);
            Assert.DoesNotContain("Transfer-Encoding: chunked", headers);
        }
        using (var client = await Connect())
        {
            var stream = client.GetStream();
            var (_, headers, _) = await ReadAfter(stream, "GET / HTTP/1.0\r\nConnection: keep-alive\r\n\r\n");
            Assert.Contains("Connection: keep-alive", headers);
            Assert.Equal(200, (await ReadAfter(stream, "GET / HTTP/1.0\r\n\r\n")).Status);
        }
    }

    [Fact]
    public async Task AMalformedRequestIsRefusedAndItsConnectionClosed()
    {
        using var client = await Connect();
        var stream = client.GetStream();
        var (status, headers, body) = await ReadAfter(stream,
            "POST / HTTP/1.1\r\nHost: a\r\nContent-Length: 4\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\nGET /smuggled HTTP/1.1\r\nHost: a\r\n\r\n");
        Assert.Equal(400, status);
        Assert.Contains("Connection: close", headers);
        Assert.Contains("request smuggling", body, StringComparison.Ordinal);
        Assert.Equal(0, await stream.ReadAsync(new byte[1]));
    }

    /// <summary>
    /// A client that goes at once, or a moment after, while its request begins, waits, or is woken: its request ends each
    /// time. Until 26 November 2026 the request was woken by its client's going before it was cancelled, and, seeing no
    /// cancellation, waited again, for its minute: within 200 tries, the eighth.
    /// </summary>
    [Fact]
    public async Task AClientThatGoesWhileItsRequestBeginsEndsItEachTime()
    {
        var drain = WebLingoDrain.Of(_application);
        for (var i = 0; i < 80; i++)
        {
            var after = (i % 4) switch { 0 => 0, 1 => 1, 2 => 5, _ => 20 };
            using (var client = await Connect())
            {
                await Send(client.GetStream(), "GET /waits HTTP/1.1\r\nHost: a\r\n\r\n");
                if (after > 0) await Task.Delay(after);
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (drain.Running > 0 && clock.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(5);
            Assert.True(drain.Running == 0, $"try {i}, its client gone after {after} ms: {drain.Running} running");
        }
    }

    /// <summary>
    /// A request sent behind another, both in one write, whose client goes while it runs: it ends, the read that watches for
    /// the client's going, begun for the first, telling the second. Until 26 November 2026 it told the first alone, the
    /// second then waiting for its minute.
    /// </summary>
    [Fact]
    public async Task AClientThatGoesEndsARequestSentBehindAnother()
    {
        var drain = WebLingoDrain.Of(_application);
        using (var client = await Connect())
        {
            var stream = client.GetStream();
            await Send(stream, "GET / HTTP/1.1\r\nHost: a\r\n\r\nGET /waits HTTP/1.1\r\nHost: a\r\n\r\n");
            Assert.Equal(200, (await ReadResponse(stream)).Status);
            var started = System.Diagnostics.Stopwatch.StartNew();
            while (drain.Running == 0 && started.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(5);
            Assert.Equal(1, drain.Running);
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (drain.Running > 0 && clock.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(5);
        Assert.Equal(0, drain.Running);
    }

    [Fact]
    public async Task AClientThatGoesEndsItsRequest()
    {
        var drain = WebLingoDrain.Of(_application);
        using (var client = await Connect())
        {
            await Send(client.GetStream(), "GET /waits HTTP/1.1\r\nHost: a\r\n\r\n");
            for (var waited = 0; drain.Running == 0 && waited < 30_000; waited += 10) await Task.Delay(10);
            Assert.Equal(1, drain.Running);
        }
        for (var waited = 0; drain.Running > 0 && waited < 30_000; waited += 10) await Task.Delay(10);
        Assert.Equal(0, drain.Running);
    }

    /// <summary>
    /// A site's limits on what a client sends (section 9, "M0.23 parts", part 3): a head slower than its
    /// <c>"requestArrival"</c> a 408, a kept connection idle past its <c>"keepAlive"</c> closed, and a body slower than 240
    /// bytes a second once its first 5 seconds are over refused, its connection closed.
    /// </summary>
    [Fact]
    public async Task AClientThatSendsTooSlowlyIsRefused()
    {
        var site = Path.Combine(_root, "slow");
        Directory.CreateDirectory(site);
        File.WriteAllText(Path.Combine(site, "wasp.config"), "{ \"limits\": { \"requestArrival\": 0.5, \"keepAlive\": 0.5 } }");
        File.WriteAllText(Path.Combine(site, "index.wasp"), "index");
        File.WriteAllText(Path.Combine(site, "echo.wasp"), "<% param request; var form = await request.form(); %><%= form[\"tea\"] %>");
        var application = WebLingoWaspServer.Load(site, new Dictionary<string, string>(), WebLingoMode.Production, null, out _, out _, out var error);
        Assert.True(application is not null, error);
        Assert.Equal((TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5)), (application.HttpLimits.RequestArrival, application.HttpLimits.KeepAlive));
        using var stop = new CancellationTokenSource();
        using var listener = WebLingoHttpServer.Listen(IPAddress.Loopback, 0);
        _ = new WebLingoHttpServer(application, _workers).ServeAsync(listener, stop.Token);
        try
        {
            var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();
                await Send(stream, "GET / HTTP/1.1\r\nHost: a\r\n");
                var (status, headers, body) = await ReadResponse(stream);
                Assert.Equal(408, status);
                Assert.Contains("Connection: close", headers);
                Assert.Contains("took too long to arrive", body, StringComparison.Ordinal);
            }
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();
                Assert.Equal(200, (await ReadAfter(stream, "GET / HTTP/1.1\r\nHost: a\r\n\r\n")).Status);
                // Kept, then idle past its half a second: closed.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                Assert.Equal(0, await stream.ReadAsync(new byte[1], timeout.Token));
            }
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();
                // 10 bytes of 10,000, then nothing: once the body's 5 seconds are over, it has come too slowly.
                await Send(stream, "POST /echo HTTP/1.1\r\nHost: a\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: 10000\r\n\r\ntea=sench");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var (status, _, _) = await ReadResponse(stream);
                Assert.NotEqual(200, status);
                Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(30));
            }
        }
        finally
        {
            await stop.CancelAsync();
        }
    }

    [Fact]
    public void WaspServeTakesTheHostsItKnows()
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        Assert.Equal(WebLingo.Cli.WebLingoCommandLine.UsageError, WebLingoWaspCommandLine.Run(["serve", "--host", "iis"], output, error));
        Assert.Contains("is not --root <folder>, --port <port>, --host aspnetcore|standalone or --scheme <name>=<folder>", error.ToString(),
            StringComparison.Ordinal);
        Assert.Contains("Usage: wasp serve [--root <folder>] [--port <port>] [--host aspnetcore|standalone]", output.ToString(), StringComparison.Ordinal);
        Assert.Null(WebLingoWaspStandalone.Create("standalone", Path.Combine(_root, "nowhere"), 0, new Dictionary<string, string>(), out var why, out _));
        Assert.NotNull(why);
    }

    /// <summary>
    /// <c>wasp serve</c>'s server (section 9, "M0.23 parts", part 6): WebLingo's own by default, ASP.NET Core's with
    /// <c>--host aspnetcore</c>, each saying which as it starts; ASP.NET Core's answers name Kestrel, WebLingo's name no server.
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("standalone", false)]
    [InlineData("aspnetcore", true)]
    public async Task WaspServeRunsWebLingosOwnServerUnlessAskedForAspNetCores(string? host, bool aspNetCore)
    {
        int port;
        using (var free = new TcpListener(IPAddress.Loopback, 0))
        {
            free.Start();
            port = ((IPEndPoint)free.LocalEndpoint).Port;
        }
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "wasp.exe" : "wasp"))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        List<string> arguments = ["serve", "--root", _root, "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        if (host is not null) arguments.AddRange(["--host", host]);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var wasp = System.Diagnostics.Process.Start(start)!;
        try
        {
            var line = await wasp.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal($"WASP serving http://localhost:{port}/ (Development){(aspNetCore ? ", on ASP.NET Core's server" : "")}; Ctrl+C stops it.",
                line);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var (status, headers, _) = await ReadAfter(client.GetStream(), "GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
            Assert.Equal(200, status);
            Assert.Equal(aspNetCore, headers.Contains("Server: Kestrel"));
        }
        finally
        {
            wasp.Kill(entireProcessTree: true);
            await wasp.WaitForExitAsync();
        }
    }

    private async Task<TcpClient> Connect()
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndPoint!).Port);
        return client;
    }

    private static Task Send(NetworkStream stream, string text) => stream.WriteAsync(Encoding.UTF8.GetBytes(text)).AsTask();

    private static async Task<(int Status, List<string> Headers, string Body)> ReadAfter(NetworkStream stream, string request)
    {
        await Send(stream, request);
        return await ReadResponse(stream);
    }

    private static (int, string) Pick((int Status, List<string> Headers, string Body) response) => (response.Status, response.Body);

    /// <summary>Exactly <paramref name="count"/> bytes, as text.</summary>
    private static async Task<string> ReadText(NetworkStream stream, int count)
    {
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>A response read byte by byte: its status, its headers, and its body by its length, in chunks, or to the connection's end.</summary>
    private static async Task<(int Status, List<string> Headers, string Body)> ReadResponse(NetworkStream stream, bool head = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<string> Line()
        {
            var line = new List<byte>();
            var one = new byte[1];
            while (true)
            {
                if (await stream.ReadAsync(one, timeout.Token) == 0) throw new IOException("closed");
                if (one[0] == '\n' && line.Count > 0 && line[^1] == '\r') return Encoding.UTF8.GetString([.. line.Take(line.Count - 1)]);
                line.Add(one[0]);
            }
        }
        var status = int.Parse((await Line()).Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        var headers = new List<string>();
        for (var line = await Line(); line.Length > 0; line = await Line()) headers.Add(line);
        string? Header(string name) => headers.FirstOrDefault(header => header.StartsWith(name + ": ", StringComparison.OrdinalIgnoreCase))?[(name.Length + 2)..];
        var body = new MemoryStream();
        if (head || status is 204 or 304)
        {
        }
        else if (Header("Content-Length") is { } length)
        {
            var bytes = new byte[int.Parse(length, System.Globalization.CultureInfo.InvariantCulture)];
            await stream.ReadExactlyAsync(bytes, timeout.Token);
            body.Write(bytes);
        }
        else if (Header("Transfer-Encoding") is "chunked")
        {
            while (true)
            {
                var size = Convert.ToInt32(await Line(), 16);
                if (size == 0)
                {
                    Assert.Equal("", await Line());
                    break;
                }
                var bytes = new byte[size];
                await stream.ReadExactlyAsync(bytes, timeout.Token);
                body.Write(bytes);
                Assert.Equal("", await Line());
            }
        }
        else
        {
            await stream.CopyToAsync(body, timeout.Token);
        }
        return (status, headers, Encoding.UTF8.GetString(body.ToArray()));
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
