using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using WebLingo.Cli;
using WebLingo.Hosting;
using WebLingo.Hosting.DotNet;
using WebLingo.Hosting.FastCgi;
using WebLingo.Wasp.Cli;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// The FastCGI host (specification, sections 6.1 and 6.2; section 9, M0.19), each test the web server, speaking FastCGI's
/// records to it: a page, its request's method, path, query, headers, scheme and host, translated from CGI's variables; a
/// form posted in <c>FCGI_STDIN</c>; a status and a header; a 404 and a 500; a file sent as it is; a page larger than one
/// record, streamed; a connection kept for a second request; a second request on a busy connection refused; and
/// <c>FCGI_GET_VALUES</c>, an unknown record's type and an unknown role answered; over TCP, and on Windows a named pipe,
/// connected to anew for each request; and <c>wasp fastcgi</c> itself, on a port and on a Unix socket.
/// </summary>
public sealed class FastCgiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wasp-fastcgi-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _stop = new();
    private readonly WebLingoWorkerPool _workers = new(2);
    private readonly WebLingoFastCgiServer _server;
    private readonly Socket _listener;

    public FastCgiTests()
    {
        Write("index.wasp", "<% param request; %><p><%= request.method %> <%= request.path %> <%= request.query?[\"q\"] %> "
                            + "<%= request.headers?[\"Accept-Language\"] %> <%= request.scheme %> <%= request.host %></p>");
        Write("form.wasp", "<% param request; var form = await request.form(); %><%= form[\"tea\"] %>");
        Write("made.wasp", "<% param response; response.status = 201; response.headers[\"X-Tea\"] = \"Sencha\"; %>made");
        Write("big.wasp", "<% for (var i = 0; i < 20000; i++) { %>0123456789<% } %>");
        Write("broken.wasp", "<%= [1][2] %>");
        Write("css/site.css", "h1 { }");
        Write("deep.wasp", "<%= " + new string('(', 250) + "1" + new string(')', 250) + " %>");
        var application = WebLingoWaspServer.Load(_root, new Dictionary<string, string>(), WebLingoMode.Production, null, out _, out _, out var error);
        Assert.True(application is not null, error);
        _server = new WebLingoFastCgiServer(application, _workers);
        _listener = WebLingoWaspFastCgi.Listen(0, null, out _);
        _ = _server.ServeAsync(_listener, _stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        _workers.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private static Dictionary<string, string> Get(string uri, params (string, string)[] more)
    {
        var question = uri.IndexOf('?');
        var parameters = new Dictionary<string, string>
        {
            ["REQUEST_METHOD"] = "GET", ["REQUEST_URI"] = uri, ["QUERY_STRING"] = question >= 0 ? uri[(question + 1)..] : "",
            ["SCRIPT_NAME"] = question >= 0 ? uri[..question] : uri, ["SERVER_NAME"] = "localhost", ["SERVER_PROTOCOL"] = "HTTP/1.1"
        };
        foreach (var (name, value) in more) parameters[name] = value;
        return parameters;
    }

    [Fact]
    public async Task APageIsServedFromCgisVariables()
    {
        using var connection = await Connect();
        var (head, body, status) = await RequestAsync(connection, 1, Get("/?q=Sencha", ("HTTP_HOST", "tea.example"), ("HTTP_ACCEPT_LANGUAGE", "pl"),
            ("HTTPS", "on")), []);
        Assert.Equal(0, status);
        Assert.Equal("Status: 200 OK", head[0]);
        Assert.Contains("Content-Type: text/html; charset=utf-8", head);
        Assert.Equal("<p>GET / Sencha pl https tea.example</p>", body);
        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(body)}", head);
    }

    [Fact]
    public async Task AFormIsReadFromStdin()
    {
        using var connection = await Connect();
        var posted = Encoding.UTF8.GetBytes("tea=Sencha&cups=2");
        var parameters = Get("/form", ("CONTENT_TYPE", "application/x-www-form-urlencoded"), ("CONTENT_LENGTH", posted.Length.ToString()));
        parameters["REQUEST_METHOD"] = "POST";
        var (head, body, _) = await RequestAsync(connection, 1, parameters, posted);
        Assert.Equal(("Status: 200 OK", "Sencha"), (head[0], body));
    }

    /// <summary>
    /// A form as nginx sends one: its <c>Content-Type</c> and <c>Content-Length</c> both as CGI's <c>CONTENT_TYPE</c> and
    /// <c>CONTENT_LENGTH</c> and as headers, <c>HTTP_CONTENT_TYPE</c> and <c>HTTP_CONTENT_LENGTH</c>, which are read once.
    /// </summary>
    [Fact]
    public async Task AFormIsReadAsNginxSendsIt()
    {
        using var connection = await Connect();
        var posted = Encoding.UTF8.GetBytes("tea=Genmaicha");
        var length = posted.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var parameters = Get("/form", ("CONTENT_TYPE", "application/x-www-form-urlencoded"), ("CONTENT_LENGTH", length),
            ("HTTP_CONTENT_TYPE", "application/x-www-form-urlencoded"), ("HTTP_CONTENT_LENGTH", length));
        parameters["REQUEST_METHOD"] = "POST";
        var (head, body, _) = await RequestAsync(connection, 1, parameters, posted);
        Assert.Equal(("Status: 200 OK", "Genmaicha"), (head[0], body));
    }

    /// <summary>
    /// A request answered before the web server has sent all of it, as nginx's GET often is, its empty <c>FCGI_STDIN</c>
    /// coming after the response: the response is not lost, the connection ended only for writing until the web server has
    /// sent what it had, so that nothing is left unread, which would reset the connection.
    /// </summary>
    [Fact]
    public async Task AResponseIsNotLostToInputSentAfterIt()
    {
        using var connection = await Connect();
        var stream = connection.GetStream();
        await stream.WriteAsync(Begin(1, keep: false));
        await stream.WriteAsync(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.Params, 1, WebLingoFastCgiRecords.WritePairs(Get("/css/site.css"))));
        await stream.WriteAsync(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.Params, 1, []));
        await Task.Delay(500); // The response sent meanwhile, and the connection ended.
        await stream.WriteAsync(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.Stdin, 1, []));
        await Task.Delay(200);
        var (head, body, _) = await ReadResponseAsync(stream, 1);
        Assert.Equal(("Status: 200 OK", "h1 { }"), (head[0], body));
        Assert.Equal(0, await stream.ReadAsync(new byte[1]));
    }

    [Fact]
    public async Task AStatusAHeaderAndErrorsAreCgis()
    {
        using var connection = await Connect();
        var (head, body, _) = await RequestAsync(connection, 1, Get("/made"), [], keep: true);
        Assert.Equal(("Status: 201 Created", "made"), (head[0], body));
        Assert.Contains("X-Tea: Sencha", head);
        HasTheSecurityHeaders(head);
        (head, _, _) = await RequestAsync(connection, 2, Get("/nowhere"), [], keep: true);
        Assert.Equal("Status: 404 Not Found", head[0]);
        HasTheSecurityHeaders(head);
        (head, body, _) = await RequestAsync(connection, 3, Get("/broken"), [], keep: true);
        Assert.Equal("Status: 500 Internal Server Error", head[0]);
        Assert.DoesNotContain("broken.wasp", body, StringComparison.Ordinal); // Production shows no error's details.
        HasTheSecurityHeaders(head);
        (head, body, _) = await RequestAsync(connection, 4, Get("/css/site.css"), []);
        Assert.Equal(("Status: 200 OK", "h1 { }"), (head[0], body));
        Assert.Contains("Content-Type: text/css", head);
        Assert.Contains("Content-Length: 6", head);
        HasTheSecurityHeaders(head);
    }

    /// <summary>The headers a site's every response has by default, a file's too (section 6.12, "Security headers").</summary>
    private static void HasTheSecurityHeaders(IReadOnlyList<string> head)
    {
        Assert.Contains("X-Content-Type-Options: nosniff", head);
        Assert.Contains("Referrer-Policy: strict-origin-when-cross-origin", head);
        Assert.Contains("Content-Security-Policy: frame-ancestors 'self'", head);
        Assert.Contains("X-Frame-Options: SAMEORIGIN", head);
    }

    [Fact]
    public async Task ALargePageIsStreamedInSeveralRecords()
    {
        using var connection = await Connect();
        var (head, body, _) = await RequestAsync(connection, 1, Get("/big"), []);
        Assert.Equal("Status: 200 OK", head[0]);
        Assert.Equal(200_000, body.Length);
        Assert.DoesNotContain(head, line => line.StartsWith("Content-Length", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AConnectionCarriesOneRequestAtATime()
    {
        using var connection = await Connect();
        var stream = connection.GetStream();
        await stream.WriteAsync(Begin(1, keep: true));
        await stream.WriteAsync(Begin(2, keep: true));
        var refused = await ReadAsync(stream);
        Assert.Equal((WebLingoFastCgiType.EndRequest, 2, (byte)1), (refused!.Value.Type, refused.Value.RequestId, refused.Value.Content[4])); // FCGI_CANT_MPX_CONN
        await stream.WriteAsync(Records(1, Get("/"), []));
        var (head, _, status) = await ReadResponseAsync(stream, 1);
        Assert.Equal(("Status: 200 OK", 0), (head[0], status));
    }

    [Fact]
    public async Task AKeptConnectionsNextRequestMayBeginAsSoonAsTheLastHasEnded()
    {
        // As nginx with fastcgi_keep_conn sends it, the moment FCGI_END_REQUEST comes: never refused as a second request at
        // once, as it was when it came in before the last request had let the connection go (25 November 2026, "FastCGI
        // Behind nginx Under Load").
        using var connection = await Connect();
        var stream = connection.GetStream();
        for (var i = 0; i < 1000; i++)
        {
            await stream.WriteAsync(Begin(1, keep: true));
            await stream.WriteAsync(Records(1, Get("/made"), []));
            var (head, body, status) = await ReadResponseAsync(stream, 1);
            Assert.True((head.FirstOrDefault(), body, status) == ("Status: 201 Created", "made", 0), $"request {i}: {head.FirstOrDefault()}, {status}");
        }
    }

    /// <summary>
    /// A path the web server gives, decoded, found as it is among the routes, so that no <c>..</c>, however written, reaches a
    /// file outside them, nor a page by its own name (25 November 2026, "Security Review of WASP's Request Path").
    /// </summary>
    [Theory]
    [InlineData("/%2e%2e/wasp.config")]
    [InlineData("/css/%2e%2e/broken.wasp")]
    [InlineData("/..%2f..%2fwindows/win.ini")]
    [InlineData("/css/..%5c..%5cwasp.config")]
    [InlineData("/broken.wasp")]
    [InlineData("/BROKEN.WASP")]
    public async Task APathThatIsNoRouteIs404(string uri)
    {
        using var connection = await Connect();
        var (head, _, status) = await RequestAsync(connection, 1, Get(uri), []);
        Assert.Equal(("Status: 404 Not Found", 0), (head[0], status));
    }

    [Fact]
    public async Task ManagementRecordsAreAnswered()
    {
        using var connection = await Connect();
        var stream = connection.GetStream();
        await stream.WriteAsync(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.GetValues, 0,
            WebLingoFastCgiRecords.WritePairs([new("FCGI_MPXS_CONNS", ""), new("FCGI_MAX_REQS", ""), new("NOT_A_VALUE", "")])));
        var values = await ReadAsync(stream);
        Assert.Equal(WebLingoFastCgiType.GetValuesResult, values!.Value.Type);
        Assert.Equal([new("FCGI_MPXS_CONNS", "0"), new("FCGI_MAX_REQS", "1024")], WebLingoFastCgiRecords.ReadPairs(values.Value.Content));
        await stream.WriteAsync(WebLingoFastCgiRecords.Make((WebLingoFastCgiType)42, 0, []));
        var unknown = await ReadAsync(stream);
        Assert.Equal((WebLingoFastCgiType.UnknownType, (byte)42), (unknown!.Value.Type, unknown.Value.Content[0]));
        await stream.WriteAsync(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.BeginRequest, 7, [0, 2, 0, 0, 0, 0, 0, 0])); // FCGI_AUTHORIZER
        var role = await ReadAsync(stream);
        Assert.Equal((WebLingoFastCgiType.EndRequest, 7, (byte)3), (role!.Value.Type, role.Value.RequestId, role.Value.Content[4])); // FCGI_UNKNOWN_ROLE
    }

    [Fact]
    public async Task ARequestOnANamedPipeIsServed()
    {
        if (!OperatingSystem.IsWindows()) return; // A web server's pipe, on Windows, as IIS and mod_fcgid give one.
        var name = "wasp-fastcgi-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);
        _ = _server.ServePipeAsync(pipe.SafePipeHandle, _stop.Token);
        // One instance of the pipe, as mod_fcgid makes it, connected to anew for each request: the host disconnects it after a
        // request whose web server did not ask to keep it, and after a web server that kept it goes.
        foreach (var (query, keep) in new[] { ("one", false), ("two", true), ("three", false) })
        {
            await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
            await client.ConnectAsync(10_000);
            await client.WriteAsync(Begin(1, keep));
            await client.WriteAsync(Records(1, Get("/?q=" + query), []));
            var (head, body, _) = await ReadResponseAsync(client, 1);
            Assert.Equal(("Status: 200 OK", $"<p>GET / {query}  http localhost</p>"), (head[0], body));
            if (!keep) Assert.Equal(0, await client.ReadAsync(new byte[1])); // Closed by the host.
        }
    }

    /// <summary>
    /// A page nested as deep as the parser allows, compiled on the worker it is first requested on, whose stack is the
    /// compiler's (<see cref="WebLingo.Language.WebLingoParser.CompilerStackSize"/>), before .NET has optimized the compiler.
    /// </summary>
    [Fact]
    public async Task ADeepPageCompilesOnAWorker()
    {
        using var connection = await Connect();
        var (head, body, _) = await RequestAsync(connection, 1, Get("/deep"), []);
        Assert.Equal(("Status: 200 OK", "1"), (head[0], body));
    }

    /// <summary><c>wasp fastcgi --root site --port n</c> itself: it says where it listens, serves there, and stops when told.</summary>
    [Fact]
    public async Task WaspFastCgiServesARootOnAPort()
    {
        int port;
        using (var free = new TcpListener(IPAddress.Loopback, 0))
        {
            free.Start();
            port = ((IPEndPoint)free.LocalEndpoint).Port;
        }
        await ServesThroughWaspFastCgiAsync(["--port", port.ToString(CultureInfo.InvariantCulture)], $"127.0.0.1:{port}",
                                            new IPEndPoint(IPAddress.Loopback, port), AddressFamily.InterNetwork, ProtocolType.Tcp);
    }

    /// <summary><c>wasp fastcgi --socket path</c>, as PHP-FPM listens on Linux: a Unix socket made at the path, removed when it stops.</summary>
    [Fact]
    public async Task WaspFastCgiServesARootOnAUnixSocket()
    {
        var path = Path.Combine(Path.GetTempPath(), "wasp-" + Guid.NewGuid().ToString("N")[..12] + ".sock");
        await ServesThroughWaspFastCgiAsync(["--socket", path], path, new UnixDomainSocketEndPoint(path), AddressFamily.Unix, ProtocolType.Unspecified);
        Assert.False(File.Exists(path));
    }

    /// <summary><c>wasp fastcgi</c> run on the root, listening <paramref name="where"/>, which it names; a request; then stopped.</summary>
    private async Task ServesThroughWaspFastCgiAsync(string[] where, string named, EndPoint endPoint, AddressFamily family, ProtocolType protocol)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        using var stop = new CancellationTokenSource();
        var running = Task.Run(() => WebLingoWaspFastCgi.Run(["fastcgi", "--root", _root, .. where], output, error, stop.Token));
        for (var i = 0; i < 100 && !output.ToString().Contains("WASP serving FastCGI", StringComparison.Ordinal); i++) await Task.Delay(100);
        Assert.Contains($"WASP serving FastCGI on {named} (Production); Ctrl+C stops it.", output.ToString(), StringComparison.Ordinal);
        using (var socket = new Socket(family, SocketType.Stream, protocol))
        {
            await socket.ConnectAsync(endPoint);
            await using var stream = new NetworkStream(socket);
            await stream.WriteAsync(Begin(1, keep: false));
            await stream.WriteAsync(Records(1, Get("/?q=cli"), []));
            var (head, body, _) = await ReadResponseAsync(stream, 1);
            Assert.Equal(("Status: 200 OK", "<p>GET / cli  http localhost</p>"), (head[0], body));
        }
        stop.Cancel();
        Assert.Equal(WebLingoCommandLine.Success, await running);
        Assert.Equal("", error.ToString());
    }

    [Theory]
    [InlineData("--port", "x")]
    [InlineData("--port", "70000")]
    [InlineData("--nonsense", "1")]
    public void WaspFastCgiTakesWhereItListens(string option, string value)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoWaspCommandLine.Run(["fastcgi", option, value], output, error));
        Assert.Contains("is not --root <folder>, --port <port>, --socket <path>, --stdin or --scheme <name>=<folder>", error.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(WebLingoWaspFastCgi.Usage, output.ToString(), StringComparison.Ordinal);
        (output, error) = (new StringWriter(), new StringWriter());
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoWaspCommandLine.Run(["fastcgi", "--port", "9001", "--stdin"], output, error));
        Assert.Contains("wasp fastcgi listens on one of --port, --socket and --stdin.", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A request's time, kept by the FastCGI host as by ASP.NET Core's (section 6.12, "Limits"): a page that loops, stopped, a 503.</summary>
    [Fact]
    public async Task APageThatPassesItsTimeIsStopped()
    {
        var site = Path.Combine(_root, "limited");
        Directory.CreateDirectory(site);
        File.WriteAllText(Path.Combine(site, "wasp.config"), "{ \"limits\": { \"requestTimeout\": 0.3 } }");
        File.WriteAllText(Path.Combine(site, "loops.wasp"), "<% var i = 0; while (true) { i = i + 1; } %>never");
        var application = WebLingoWaspServer.Load(site, new Dictionary<string, string>(), WebLingoMode.Production, null, out _, out _, out var error);
        Assert.True(application is not null, error);
        using var stop = new CancellationTokenSource();
        using var listener = WebLingoWaspFastCgi.Listen(0, null, out _);
        _ = new WebLingoFastCgiServer(application, _workers).ServeAsync(listener, stop.Token);
        try
        {
            using var connection = new TcpClient();
            await connection.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndPoint!).Port);
            var (head, body, _) = await RequestAsync(connection, 1, Get("/loops"), []);
            Assert.Equal("Status: 503 Service Unavailable", head[0]);
            Assert.DoesNotContain("never", body, StringComparison.Ordinal);
        }
        finally
        {
            stop.Cancel();
        }
    }

    /// <summary>
    /// The drain, kept by the FastCGI host as by ASP.NET Core's (section 6.12, "Reloading"): a request with no time limit
    /// still running on an older generation once a reload has published a newer one, stopped when the drain has passed, a 503.
    /// </summary>
    [Fact]
    public async Task ARequestOnAnOlderGenerationIsStoppedWhenTheDrainHasPassed()
    {
        var site = Path.Combine(_root, "drained");
        Directory.CreateDirectory(site);
        File.WriteAllText(Path.Combine(site, "wasp.config"), "{ \"limits\": { \"requestTimeout\": null, \"drain\": 0.3 } }");
        File.WriteAllText(Path.Combine(site, "waits.wasp"), "<% import { delay } from \"std:system\"; while (true) { await delay(10); } %>never");
        var application = WebLingoWaspServer.Load(site, new Dictionary<string, string>(), WebLingoMode.Production, null, out _, out _, out var error);
        Assert.True(application is not null, error);
        Assert.Null(application.RequestTimeout);
        using var stop = new CancellationTokenSource();
        using var listener = WebLingoWaspFastCgi.Listen(0, null, out _);
        _ = new WebLingoFastCgiServer(application, _workers).ServeAsync(listener, stop.Token);
        try
        {
            using var connection = new TcpClient();
            await connection.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndPoint!).Port);
            var waiting = RequestAsync(connection, 1, Get("/waits"), []);
            var drain = WebLingoDrain.Of(application);
            for (var waited = 0; drain.Running == 0 && waited < 30_000; waited += 10) await Task.Delay(10);
            Assert.Equal(1, drain.Running);
            Assert.True(application.TryReload(out error), error);
            drain.Reloaded(application.GenerationNumber);
            var (head, body, _) = await waiting;
            Assert.Equal("Status: 503 Service Unavailable", head[0]);
            Assert.DoesNotContain("never", body, StringComparison.Ordinal);
        }
        finally
        {
            stop.Cancel();
        }
    }

    /// <summary>
    /// Old generations let go under the FastCGI host and <c>wasp</c>'s own loading of a site, its native modules among it
    /// (section 9, "M0.22 parts", part 2): the site reloaded by its watcher as its files change, while clients ask for its
    /// pages, drained requests and a page that does not compile among them; once the clients have stopped, every generation
    /// but the one served let go. A request's heap let go with it is <c>GenerationsLetGoTests</c>' (<c>WebLingo.Hosting.Tests</c>).
    /// </summary>
    [Fact]
    public async Task OldGenerationsAreLetGoUnderFastCgi()
    {
        var site = Path.Combine(_root, "reloaded");
        Directory.CreateDirectory(site);
        File.WriteAllText(Path.Combine(site, "wasp.config"), "{ \"mode\": \"development\", \"limits\": { \"drain\": 0.2 } }");
        File.WriteAllText(Path.Combine(site, "slow.wasp"), "<% import { delay } from \"std:system\"; await delay(2000); %>slow");
        File.WriteAllText(Path.Combine(site, "lib.wlg"), "export class Cup { constructor(n) { this.n = n; } size() { return this.n; } }");
        File.WriteAllText(Path.Combine(site, "cup.wasp"),
            "<% import { Cup } from \"/lib.wlg\"; import { Json } from \"std:json\"; %><%- Json.stringify({ n: new Cup(3).size() }) %>");
        Version(site, 0);
        var application = WebLingoWaspServer.Load(site, new Dictionary<string, string>(), WebLingoMode.Production, null, out _, out _, out var error);
        Assert.True(application is not null, error);
        var generations = new List<WeakReference> { Watch(application) };
        using var stop = new CancellationTokenSource();
        using var listener = WebLingoWaspFastCgi.Listen(0, null, out _);
        _ = new WebLingoFastCgiServer(application, _workers).ServeAsync(listener, stop.Token);
        var reloader = new WebLingoReloader(application, site, (_, why) =>
        {
            if (why is null) lock (generations) generations.Add(Watch(application));
        });
        try
        {
            using var asking = new CancellationTokenSource();
            var answers = new int[2];
            var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
            var clients = Enumerable.Range(0, 3).Select(client => Ask(port, client, asking.Token, answers)).ToArray();
            // 8 versions, and more while no request has been drained yet, as on a loaded machine, for 30 seconds at most.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var version = 1; version <= 8 || (Volatile.Read(ref answers[1]) == 0 && clock.Elapsed < TimeSpan.FromSeconds(30)); version++)
            {
                await Task.Delay(150);
                Version(site, version);
                for (var waited = 0; Count(generations) < version + 1 && waited < 30_000; waited += 10) await Task.Delay(10);
                Assert.Equal(version + 1, Count(generations));
            }
            await asking.CancelAsync();
            await Task.WhenAll(clients);
            var drain = WebLingoDrain.Of(application);
            for (var waited = 0; drain.Running > 0 && waited < 30_000; waited += 10) await Task.Delay(10);
            Assert.Equal(0, drain.Running);
            Assert.True(answers[0] > 0, "no page was served");
            Assert.True(answers[1] > 0, "no request was drained");
        }
        finally
        {
            reloader.Dispose();
            stop.Cancel();
        }
        var held = 0;
        for (var i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            lock (generations) held = generations.Count(generation => generation.IsAlive);
            if (held == 1) break;
            await Task.Delay(50);
        }
        Assert.Equal(1, held);
        Assert.True(generations[^1].IsAlive);
        GC.KeepAlive(application);

        static int Count(List<WeakReference> generations)
        {
            lock (generations) return generations.Count;
        }

        // A client, a connection of its own, asking for each page in turn until it is told to stop.
        static async Task Ask(int port, int client, CancellationToken asking, int[] answers)
        {
            using var connection = new TcpClient();
            await connection.ConnectAsync(IPAddress.Loopback, port);
            string[] pages = ["/", "/cup", "/slow", "/broken"];
            for (var turn = client; !asking.IsCancellationRequested; turn++)
            {
                var path = pages[turn % pages.Length];
                var (head, body, _) = await RequestAsync(connection, 1, Get(path), [], keep: true);
                if (head[0] == "Status: 503 Service Unavailable")
                {
                    Interlocked.Increment(ref answers[1]);
                    continue;
                }
                Assert.True(head[0] == "Status: 200 OK" || (path == "/broken" && head[0] == "Status: 500 Internal Server Error"), $"{path}: {head[0]} {body}");
                Assert.True(path != "/cup" || body == "{\"n\":3}", body);
                Interlocked.Increment(ref answers[0]);
            }
        }

        // The pages a reload changes: the front page's text, and a page that does not compile in every third version.
        static void Version(string site, int version)
        {
            File.WriteAllText(Path.Combine(site, "index.wasp"), $"<p>version {version}</p>");
            File.WriteAllText(Path.Combine(site, "broken.wasp"), version % 3 == 2 ? "<%= [1][2 %>" : $"<%= {version} %>");
        }
    }

    /// <summary>A weak reference to the generation served now, made in a method of its own, so that no local of the test's holds it.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Watch(WebLingoApplication application) => new(application.CurrentGeneration);

    /// <summary>
    /// A file a page sends, through FastCGI (specification, section 6.12, "Files sent"): with the page's status and headers,
    /// its tag, a 304 for a client that has it, and one range of it, as CGI's <c>HTTP_</c> variables ask; and a site's own
    /// file in part too.
    /// </summary>
    [Fact]
    public async Task AFileAPageSendsIsSentWithItsTagAndRanges()
    {
        var site = Path.Combine(_root, "sending");
        Directory.CreateDirectory(Path.Combine(site, "data"));
        File.WriteAllText(Path.Combine(site, "wasp.config"), "{ \"files\": { \"read\": [\"data\"] } }");
        File.WriteAllText(Path.Combine(site, "data", "teas.txt"), "Sencha, Gyokuro, Bancha");
        File.WriteAllText(Path.Combine(site, "teas.wasp"), "<% param response; response.headers[\"X-Tea\"] = \"green\"; %>not sent"
            + "<% await response.sendFile(\"data/teas.txt\", \"text/plain; charset=utf-8\", { download: true }); %>");
        File.WriteAllText(Path.Combine(site, "site.css"), "h1 { }");
        var application = WebLingoWaspServer.Load(site, new Dictionary<string, string>(), WebLingoMode.Production, null, out _, out _, out var error);
        Assert.True(application is not null, error);
        using var stop = new CancellationTokenSource();
        using var listener = WebLingoWaspFastCgi.Listen(0, null, out _);
        _ = new WebLingoFastCgiServer(application, _workers).ServeAsync(listener, stop.Token);
        try
        {
            using var connection = new TcpClient();
            await connection.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndPoint!).Port);
            var (head, body, _) = await RequestAsync(connection, 1, Get("/teas"), [], keep: true);
            Assert.Equal(("Status: 200 OK", "Sencha, Gyokuro, Bancha"), (head[0], body));
            Assert.Contains("Content-Type: text/plain; charset=utf-8", head);
            Assert.Contains("Content-Length: 23", head);
            Assert.Contains("X-Tea: green", head);
            Assert.Contains("Content-Disposition: attachment; filename=\"teas.txt\"", head);
            Assert.Contains("Accept-Ranges: bytes", head);
            HasTheSecurityHeaders(head);
            var tag = head.Single(line => line.StartsWith("ETag: ", StringComparison.Ordinal))["ETag: ".Length..];
            (head, body, _) = await RequestAsync(connection, 2, Get("/teas", ("HTTP_IF_NONE_MATCH", tag)), [], keep: true);
            Assert.Equal(("Status: 304 Not Modified", ""), (head[0], body));
            (head, body, _) = await RequestAsync(connection, 3, Get("/teas", ("HTTP_RANGE", "bytes=8-14")), [], keep: true);
            Assert.Equal(("Status: 206 Partial Content", "Gyokuro"), (head[0], body));
            Assert.Contains("Content-Range: bytes 8-14/23", head);
            (head, body, _) = await RequestAsync(connection, 4, Get("/site.css", ("HTTP_RANGE", "bytes=-3")), []);
            Assert.Equal(("Status: 206 Partial Content", "{ }"), (head[0], body));
        }
        finally
        {
            stop.Cancel();
        }
    }

    private async Task<TcpClient> Connect()
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndPoint!).Port);
        return client;
    }

    private static byte[] Begin(int id, bool keep) =>
        WebLingoFastCgiRecords.Make(WebLingoFastCgiType.BeginRequest, id, [0, 1, (byte)(keep ? 1 : 0), 0, 0, 0, 0, 0]);

    /// <summary>A request's parameters and body, as a web server sends them, each stream ended by an empty record.</summary>
    private static byte[] Records(int id, Dictionary<string, string> parameters, byte[] body)
    {
        var made = new MemoryStream();
        made.Write(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.Params, id, WebLingoFastCgiRecords.WritePairs(parameters)));
        made.Write(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.Params, id, []));
        if (body.Length > 0) made.Write(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.Stdin, id, body));
        made.Write(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.Stdin, id, []));
        return made.ToArray();
    }

    private static async Task<(List<string> Head, string Body, int Status)> RequestAsync(TcpClient connection, int id,
        Dictionary<string, string> parameters, byte[] body, bool keep = false)
    {
        var stream = connection.GetStream();
        await stream.WriteAsync(Begin(id, keep));
        await stream.WriteAsync(Records(id, parameters, body));
        return await ReadResponseAsync(stream, id);
    }

    /// <summary>The response's <c>FCGI_STDOUT</c>, its head's lines and its body, to its <c>FCGI_END_REQUEST</c>, whose protocol's status it gives.</summary>
    private static async Task<(List<string> Head, string Body, int Status)> ReadResponseAsync(Stream stream, int id)
    {
        var stdout = new MemoryStream();
        while (true)
        {
            var record = await ReadAsync(stream) ?? throw new IOException("the host closed the connection");
            Assert.Equal(id, record.RequestId);
            if (record.Type == WebLingoFastCgiType.Stdout)
            {
                stdout.Write(record.Content);
                continue;
            }
            Assert.Equal(WebLingoFastCgiType.EndRequest, record.Type);
            var text = Encoding.UTF8.GetString(stdout.ToArray());
            var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            return ([.. text[..split].Split("\r\n")], text[(split + 4)..], record.Content[4]);
        }
    }

    private static async Task<WebLingoFastCgiRecord?> ReadAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await WebLingoFastCgiRecords.ReadAsync(stream, timeout.Token);
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
