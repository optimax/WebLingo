using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;
using WebLingo.Hosting.Abstractions;
using WebLingo.Hosting.AspNetCore;
using WebLingo.Runtime;
namespace WebLingo.Hosting.FastCgi;

/// <summary>
/// The FastCGI host (specification, sections 6.1 and 6.2; section 9, M0.19), beside the ASP.NET Core adapter, so that a web
/// server other than IIS, nginx or Apache, runs WASP's sites, as it runs PHP's through PHP-FPM: FastCGI's requests, the
/// responder's role, translated to WLHI's (<see cref="Translate"/>), each run on the pool's workers and its response sent
/// back as its worker writes it, in <c>FCGI_STDOUT</c> records, CGI's <c>Status</c> and headers first; a file the
/// application leaves to its host sent as it is. A connection carries one request at a time (<c>FCGI_MPXS_CONNS</c> 0, a
/// second one's <c>FCGI_CANT_MPX_CONN</c>), and is kept for the next when the web server asks (<c>FCGI_KEEP_CONN</c>). A
/// request whose web server sends <c>FCGI_ABORT_REQUEST</c>, or closes its connection, is cancelled, as one whose client
/// goes is; a response a page ends unfinished closes its connection, so that the web server ends its client's unfinished
/// too. On a named pipe, whose handle a web server may have made for synchronous use only, which cannot be read and
/// written at once, a request is read whole, its body included, before it runs, and nothing more is read until it has
/// been answered, as php-cgi reads one. Only FastCGI is translated here: everything else is the host's core's (section
/// 6.12).
/// </summary>
public sealed class WebLingoFastCgiServer(WebLingoApplication application, string root, WebLingoWorkerPool workers, Action<string>? log = null)
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>The most a request's <c>FCGI_PARAMS</c> may hold, so that a web server's mistake cannot take the host's memory.</summary>
    public const int MaxParams = 1024 * 1024;

    /// <summary>Accepts the web server's connections on <paramref name="listener"/>, each served on its own, until <paramref name="stop"/>.</summary>
    public async Task ServeAsync(Socket listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            Socket accepted;
            try
            {
                accepted = await listener.AcceptAsync(stop);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
                                                  || (exception is SocketException && stop.IsCancellationRequested))
            {
                return;
            }
            _ = ServeConnectionAsync(new NetworkStream(accepted, ownsSocket: true), stop);
        }
    }

    /// <summary>
    /// Serves the connections of a named pipe the web server made, as IIS's and Apache's mod_fcgid give one on Windows, a
    /// process's standard input: one at a time, each until the web server disconnects, or until its request ends and the web
    /// server did not ask to keep it, until <paramref name="stop"/>; the pipe then waits for the next, as php-cgi's does.
    /// </summary>
    public async Task ServePipeAsync(SafePipeHandle pipe, CancellationToken stop)
    {
        using var server = new NamedPipeServerStream(PipeDirection.InOut, isAsync: false, isConnected: false, pipe);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Run(server.WaitForConnection, stop);
                await ServeConnectionAsync(server, stop, ownsStream: false, sequential: true); // Which disconnects it.
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
                if (stop.IsCancellationRequested) return;
                Disconnect(server);
            }
        }
    }

    /// <summary>
    /// A pipe's connection ended: what was written to it read by the web server first, then the pipe disconnected, ready for
    /// the next, as php-cgi ends one; a client already gone leaves nothing to read.
    /// </summary>
    private static void Disconnect(NamedPipeServerStream pipe)
    {
        try
        {
            if (pipe.IsConnected && OperatingSystem.IsWindows()) pipe.WaitForPipeDrain();
        }
        catch (IOException)
        {
        }
        try
        {
            pipe.Disconnect();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Serves one connection's requests, one at a time, until the web server closes it, or a request ends it; with
    /// <paramref name="sequential"/>, each read whole before it runs, and answered before anything more is read.
    /// </summary>
    public async Task ServeConnectionAsync(Stream stream, CancellationToken stop, bool ownsStream = true, bool sequential = false)
    {
        var connection = new Connection(this, stream, ownsStream, sequential);
        try
        {
            await connection.RunAsync(stop);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ObjectDisposedException or OperationCanceledException
                                              or SocketException)
        {
            if (exception is InvalidDataException) log?.Invoke($"WASP's FastCGI host closed a connection: {exception.Message}.");
        }
        finally
        {
            connection.Close();
            if (ownsStream) await stream.DisposeAsync();
        }
    }

    /// <summary>
    /// FastCGI's request as WLHI's (section 6.7), from CGI's variables: its method, <c>REQUEST_METHOD</c>; its path,
    /// <c>REQUEST_URI</c>'s before any <c>?</c>, decoded, else <c>SCRIPT_NAME</c> and <c>PATH_INFO</c>; its query,
    /// <c>QUERY_STRING</c>; its scheme, <c>https</c> where <c>HTTPS</c> is on, else <c>REQUEST_SCHEME</c>'s or <c>http</c>; its
    /// host, <c>HTTP_HOST</c>'s, else <c>SERVER_NAME</c>'s; its headers, each <c>HTTP_</c> variable's, its name's words
    /// joined by <c>-</c>, and <c>CONTENT_TYPE</c> and <c>CONTENT_LENGTH</c>, which nginx sends as headers too,
    /// <c>HTTP_CONTENT_TYPE</c> and <c>HTTP_CONTENT_LENGTH</c>, read once; and its body, <paramref name="body"/>.
    /// </summary>
    public static WebLingoBufferedRequest Translate(IReadOnlyList<KeyValuePair<string, string>> parameters, IWebLingoBodyReader body)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters) variables[name] = value;
        string Get(string name) => variables.TryGetValue(name, out var value) ? value : "";
        var uri = Get("REQUEST_URI");
        var question = uri.IndexOf('?');
        var path = uri.Length > 0 ? Uri.UnescapeDataString(question >= 0 ? uri[..question] : uri) : Get("SCRIPT_NAME") + Get("PATH_INFO");
        if (path.Length == 0) path = "/";
        var query = variables.TryGetValue("QUERY_STRING", out var given) ? given : question >= 0 ? uri[(question + 1)..] : "";
        var https = Get("HTTPS");
        var scheme = https.Equals("on", StringComparison.OrdinalIgnoreCase) || https == "1" ? "https"
            : Get("REQUEST_SCHEME") is { Length: > 0 } named ? named : "http";
        var host = Get("HTTP_HOST") is { Length: > 0 } hostHeader ? hostHeader : Get("SERVER_NAME");
        var headers = new List<(string, string)>();
        foreach (var (name, value) in parameters)
        {
            if (name.StartsWith("HTTP_", StringComparison.Ordinal) && name.Length > 5)
            {
                if (name is not ("HTTP_CONTENT_TYPE" or "HTTP_CONTENT_LENGTH")) headers.Add((HeaderName(name[5..]), value));
            }
            else if (name == "CONTENT_TYPE" && value.Length > 0) headers.Add(("Content-Type", value));
            else if (name == "CONTENT_LENGTH" && value.Length > 0) headers.Add(("Content-Length", value));
        }
        return new WebLingoBufferedRequest(Get("REQUEST_METHOD") is { Length: > 0 } method ? method : "GET", path, query, scheme, host, headers, body);
    }

    /// <summary><c>ACCEPT_LANGUAGE</c> as <c>Accept-Language</c>: each word's first letter a capital, the rest small, joined by <c>-</c>.</summary>
    private static string HeaderName(string variable)
    {
        var words = variable.Split('_');
        for (var i = 0; i < words.Length; i++) words[i] = words[i].Length == 0 ? "" : words[i][..1] + words[i][1..].ToLowerInvariant();
        return string.Join('-', words);
    }

    /// <summary>A connection: its records read in turn, its one request at a time, and what is written to it, one write at a time.</summary>
    private sealed class Connection(WebLingoFastCgiServer server, Stream stream, bool ownsStream, bool sequential)
    {
        /// <summary>How long a connection closed for writing is read, and what comes dropped, before it is closed whole.</summary>
        private static readonly TimeSpan Draining = TimeSpan.FromSeconds(30);

        private readonly SemaphoreSlim _writing = new(1, 1);
        private readonly CancellationTokenSource _reading = new();
        private readonly object _gate = new();
        private Request? _current;
        private bool _closed;

        private bool IsClosed
        {
            get
            {
                lock (_gate) return _closed;
            }
        }

        /// <summary>
        /// Reads the web server's records until it closes the connection; once the connection is closed for writing, what
        /// still comes is read and dropped (<see cref="Close"/>), for at most <see cref="Draining"/>, a pipe's not at all.
        /// </summary>
        public async Task RunAsync(CancellationToken stop)
        {
            try
            {
                using var reading = CancellationTokenSource.CreateLinkedTokenSource(stop, _reading.Token);
                while (!(IsClosed && !ownsStream) && await WebLingoFastCgiRecords.ReadAsync(stream, reading.Token) is { } record)
                {
                    if (IsClosed) continue;
                    switch (record.Type)
                    {
                        case WebLingoFastCgiType.GetValues:
                            await WriteAsync(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.GetValuesResult, 0, WebLingoFastCgiRecords.WritePairs(Values(record.Content))));
                            break;
                        case WebLingoFastCgiType.BeginRequest:
                            await BeginAsync(record);
                            break;
                        case WebLingoFastCgiType.Params when Current(record.RequestId) is { Started: false, HasParams: false } request:
                            if (record.Content.Length == 0)
                            {
                                request.HasParams = true;
                                if (!sequential)
                                {
                                    request.Started = true;
                                    _ = server.HandleAsync(this, request);
                                }
                            }
                            else if (request.Params.Length + record.Content.Length > MaxParams)
                            {
                                throw new InvalidDataException($"a request's parameters beyond {MaxParams} bytes");
                            }
                            else
                            {
                                request.Params.Write(record.Content);
                            }
                            break;
                        case WebLingoFastCgiType.Stdin when Current(record.RequestId) is { } request:
                            if (record.Content.Length > 0)
                            {
                                request.Body.Add(record.Content);
                                break;
                            }
                            request.Body.End();
                            if (sequential && request is { HasParams: true, Started: false })
                            {
                                // Read whole: it runs, and is answered, before anything more is read.
                                request.Started = true;
                                await server.HandleAsync(this, request);
                            }
                            break;
                        case WebLingoFastCgiType.AbortRequest when Current(record.RequestId) is { } request:
                            request.Gone();
                            break;
                        case WebLingoFastCgiType.Params or WebLingoFastCgiType.Stdin or WebLingoFastCgiType.Data or WebLingoFastCgiType.AbortRequest:
                            break; // A request that is not this connection's, or not any more.
                        default:
                            await WriteAsync(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.UnknownType, 0, [(byte)record.Type, 0, 0, 0, 0, 0, 0, 0]));
                            break;
                    }
                }
            }
            finally
            {
                _reading.Dispose(); // Its timer, once CancelAfter has run, gone with it.
            }
        }

        private Request? Current(int id)
        {
            lock (_gate) return _current is { } request && request.Id == id ? request : null;
        }

        private async Task BeginAsync(WebLingoFastCgiRecord record)
        {
            var content = record.Content;
            var role = content.Length >= 3 ? content[0] << 8 | content[1] : 0;
            Request? begun = null;
            lock (_gate)
            {
                if (_current is null && role == WebLingoFastCgiRecords.Responder)
                {
                    begun = _current = new Request(record.RequestId, (content[2] & WebLingoFastCgiRecords.KeepConnection) != 0);
                }
            }
            if (begun is not null) return;
            var status = role == WebLingoFastCgiRecords.Responder ? WebLingoFastCgiRecords.CannotMultiplex : WebLingoFastCgiRecords.UnknownRole;
            await WriteAsync(WebLingoFastCgiRecords.EndRequest(record.RequestId, status));
        }

        /// <summary><c>FCGI_GET_VALUES</c>' answer: one connection at a time per request, no multiplexing.</summary>
        private Dictionary<string, string> Values(byte[] content)
        {
            var known = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FCGI_MAX_CONNS"] = "1024", ["FCGI_MAX_REQS"] = "1024", ["FCGI_MPXS_CONNS"] = "0"
            };
            var answer = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, _) in WebLingoFastCgiRecords.ReadPairs(content))
            {
                if (known.TryGetValue(name, out var value)) answer[name] = value;
            }
            return answer;
        }

        /// <summary>Writes records, one write at a time, nothing once the connection is closed.</summary>
        public async Task WriteAsync(byte[] records)
        {
            await _writing.WaitAsync();
            try
            {
                if (_closed) throw new IOException("the connection is closed");
                await stream.WriteAsync(records);
                await stream.FlushAsync();
            }
            finally
            {
                _writing.Release();
            }
        }

        /// <summary>A request ended: the next may begin, or, if the web server did not ask to keep the connection, it is closed.</summary>
        public void Ended(Request request)
        {
            lock (_gate)
            {
                if (_current == request) _current = null;
            }
            if (!request.Keep) Close();
        }

        /// <summary>
        /// Closes the connection, its request gone, as a client that goes is: a socket for writing only, once what was
        /// written has been sent, its reader going on until the web server closes it too, as PHP-FPM closes one, since a
        /// socket closed whole with input unread, a request's last <c>FCGI_STDIN</c> that came after its response, is reset,
        /// and its response's end may be lost; a web server's pipe, which the server keeps for the next connection,
        /// disconnected; any other stream closed.
        /// </summary>
        public void Close()
        {
            Request? going;
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                going = _current;
            }
            going?.Gone();
            if (!ownsStream)
            {
                if (stream is NamedPipeServerStream pipe) Disconnect(pipe);
                return;
            }
            try
            {
                if (stream is NetworkStream { Socket: var socket })
                {
                    socket.Shutdown(SocketShutdown.Send);
                    _reading.CancelAfter(Draining);
                }
                else
                {
                    stream.Close();
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException)
            {
            }
        }
    }

    /// <summary>A request on a connection: its id, whether its connection is kept after it, its parameters, its body, and whether it has gone.</summary>
    private sealed class Request(int id, bool keep)
    {
        public int Id { get; } = id;
        public bool Keep { get; } = keep;
        public bool Started { get; set; }
        public bool HasParams { get; set; }
        public MemoryStream Params { get; } = new();
        public Body Body { get; } = new();
        public CancellationTokenSource Going { get; } = new();

        public void Gone()
        {
            Body.Fail();
            try
            {
                Going.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>
    /// Runs a request on a worker and sends its response as it is written; then its end, <c>FCGI_STDOUT</c>'s empty record and
    /// <c>FCGI_END_REQUEST</c>, unless the page ended it unfinished, which closes the connection.
    /// </summary>
    private async Task HandleAsync(Connection connection, Request request)
    {
        List<KeyValuePair<string, string>> parameters;
        try
        {
            parameters = WebLingoFastCgiRecords.ReadPairs(request.Params.ToArray());
        }
        catch (InvalidDataException exception)
        {
            log?.Invoke($"WASP's FastCGI host closed a connection: {exception.Message}.");
            connection.Close();
            return;
        }
        var translated = Translate(parameters, request.Body);
        var response = new WebLingoStreamingResponse(request.Going.Token);
        var cancellation = new WebLingoCancellationSignal();
        using var cancelled = request.Going.Token.Register(cancellation.Cancel);
        // Woken, where it waits, by the web server's going, or by its time limit, which stops it too (section 6.12, "Limits").
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(request.Going.Token);
        using var timer = WebLingoRequestTimer.Start(application, cancellation, wake);
        var run = workers.Serve((worker, scheduler) => application.Handle(translated, response, worker, cancellation, scheduler), wake.Token,
            response.End);
        var sink = new Sink(connection, request.Id);
        try
        {
            await response.SendAsync(sink);
            var outcome = await run;
            if (sink.Aborted)
            {
                connection.Close();
                return;
            }
            if (outcome is { Kind: WebLingoRequestOutcomeKind.File, File: { } file }) await SendFileAsync(sink, file);
            await connection.WriteAsync(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.Stdout, request.Id, []));
            await connection.WriteAsync(WebLingoFastCgiRecords.EndRequest(request.Id, WebLingoFastCgiRecords.RequestComplete));
            connection.Ended(request);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
        {
            // The web server has gone: the page stops, and the connection closes.
            request.Gone();
            await run;
            connection.Close();
        }
    }

    /// <summary>A file the application leaves to its host, sent as it is, with the content type its extension gives, a 404 for one with none.</summary>
    private async Task SendFileAsync(Sink sink, string file)
    {
        var headers = new WebLingoHeaderList();
        if (!ContentTypes.TryGetContentType(file, out var type))
        {
            await sink.StartAsync(404, headers, 0);
            return;
        }
        var path = Path.Combine(root, file);
        headers.Set("Content-Type", type);
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, useAsync: true);
        await sink.StartAsync(200, headers, source.Length);
        var buffer = new byte[WebLingoFastCgiRecords.MaxContent];
        for (int read; (read = await source.ReadAsync(buffer)) > 0;) await sink.WriteAsync(buffer[..read]);
    }

    /// <summary>
    /// A request's response as FastCGI's <c>FCGI_STDOUT</c> records: CGI's <c>Status</c> line and its headers, its
    /// <c>Content-Length</c> when its body comes whole, then its body; an end unfinished closes the connection.
    /// </summary>
    private sealed class Sink(Connection connection, int requestId) : IWebLingoResponseSink
    {
        public bool Aborted { get; private set; }

        public async ValueTask StartAsync(int status, IWebLingoHeaderCollection headers, long? length)
        {
            var head = new StringBuilder();
            head.Append("Status: ").Append(status.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var reason = ReasonPhrases.GetReasonPhrase(status);
            if (reason.Length > 0) head.Append(' ').Append(reason);
            head.Append("\r\n");
            var hasLength = false;
            foreach (var name in headers.Names)
            {
                hasLength |= name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);
                foreach (var value in headers.GetValues(name)) head.Append(name).Append(": ").Append(value).Append("\r\n");
            }
            if (length is { } bytes && !hasLength) head.Append("Content-Length: ").Append(bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
            head.Append("\r\n");
            await connection.WriteAsync(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.Stdout, requestId, Encoding.UTF8.GetBytes(head.ToString())));
        }

        public async ValueTask WriteAsync(byte[] bytes)
        {
            if (bytes.Length > 0) await connection.WriteAsync(WebLingoFastCgiRecords.Make(WebLingoFastCgiType.Stdout, requestId, bytes));
        }

        public ValueTask AbortAsync()
        {
            Aborted = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A request's body as its web server sends it, in <c>FCGI_STDIN</c> records, read as it arrives (section 6.12): a read
    /// waits for the next piece, 0 at its end, -1 once the request has gone.
    /// </summary>
    private sealed class Body : IWebLingoBodyReader
    {
        private readonly object _gate = new();
        private readonly Queue<byte[]> _pieces = new();
        private readonly Queue<(byte[] Buffer, int Offset, int Count, Action<int> Done)> _reads = new();
        private int _at; // How much of the first piece has been read.
        private bool _ended;
        private bool _failed;

        public void Add(byte[] piece)
        {
            lock (_gate) _pieces.Enqueue(piece);
            Serve();
        }

        public void End()
        {
            lock (_gate) _ended = true;
            Serve();
        }

        public void Fail()
        {
            lock (_gate) _failed = true;
            Serve();
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            var read = -1;
            using var done = new ManualResetEventSlim();
            ReadAsync(buffer, offset, count, n =>
            {
                read = n;
                done.Set();
            });
            done.Wait();
            return read;
        }

        public void ReadAsync(byte[] buffer, int offset, int count, Action<int> done)
        {
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
            {
                ThreadPool.QueueUserWorkItem(_ => done(0));
                return;
            }
            lock (_gate) _reads.Enqueue((buffer, offset, count, done));
            Serve();
        }

        /// <summary>Gives the reads waiting what has arrived, or their end, each told on the thread pool, never where it waits.</summary>
        private void Serve()
        {
            var answers = new List<(Action<int> Done, int Read)>();
            lock (_gate)
            {
                while (_reads.Count > 0)
                {
                    var (buffer, offset, count, done) = _reads.Peek();
                    int read;
                    if (_failed)
                    {
                        read = -1;
                    }
                    else if (_pieces.Count > 0)
                    {
                        var piece = _pieces.Peek();
                        read = Math.Min(count, piece.Length - _at);
                        Array.Copy(piece, _at, buffer, offset, read);
                        _at += read;
                        if (_at == piece.Length)
                        {
                            _pieces.Dequeue();
                            _at = 0;
                        }
                    }
                    else if (_ended || count == 0)
                    {
                        read = 0;
                    }
                    else
                    {
                        break;
                    }
                    _reads.Dequeue();
                    answers.Add((done, read));
                }
            }
            foreach (var (done, read) in answers) ThreadPool.QueueUserWorkItem(_ => done(read));
        }
    }
}
