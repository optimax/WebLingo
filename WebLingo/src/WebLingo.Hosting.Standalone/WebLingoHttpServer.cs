using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Hosting.DotNet;
using WebLingo.Runtime;
namespace WebLingo.Hosting.Standalone;

/// <summary>
/// The standalone HTTP host (specification, section 9, "M0.23 parts", part 2): a site served without ASP.NET Core, HTTP/1.1
/// written in WebLingo's own C# on .NET's sockets, with no package, as the FastCGI host is, TLS and HTTP/2 left to a reverse
/// proxy in front of it (the owner's answer, A1 (a) of "M0.23: A Standalone HTTP Host"). Each connection carries its
/// requests one after another, kept while both ends keep it, HTTP/1.0's where it asks; each request's head read by
/// <see cref="WebLingoHttpParser"/>, and refused where it is malformed or too large, its connection then closed; its body
/// read as the page asks for it, by its length or in chunks, <c>100 Continue</c> sent first where the client waits for it;
/// its response sent as the page writes it, by its length where it comes whole, in chunks otherwise; each request run on the
/// hosts' own workers, its time, its drain and its client's going kept as the other hosts keep them.
/// </summary>
public sealed class WebLingoHttpServer(WebLingoApplication application, WebLingoWorkerPool workers)
{
    /// <summary>What it takes of a client before it refuses it: its site's, <c>wasp.config</c>'s <c>"limits"</c>' (part 3).</summary>
    public WebLingoHttpLimits Limits { get; init; } = application.HttpLimits;

    /// <summary>A socket listening on <paramref name="address"/>'s <paramref name="port"/>, 0 for one the system chooses.</summary>
    public static Socket Listen(IPAddress address, int port)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6) socket.DualMode = false;
            socket.Bind(new IPEndPoint(address, port));
            socket.Listen(512);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Serves the connections <paramref name="listener"/> accepts, each on its own, until <paramref name="stop"/>.</summary>
    public async Task ServeAsync(Socket listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            Socket accepted;
            try
            {
                accepted = await listener.AcceptAsync(stop);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            accepted.NoDelay = true;
            _ = Task.Run(() => new Connection(this, accepted, stop).ServeAsync(), CancellationToken.None);
        }
    }

    /// <summary>A client's connection: its bytes read into a buffer, its requests served in turn, its writes one at a time.</summary>
    private sealed class Connection(WebLingoHttpServer server, Socket socket, CancellationToken stop)
    {
        /// <summary>The most a connection buffers at once: a request's line and headers, and what comes after them.</summary>
        private int MaxBuffer => server.Limits.RequestLine + server.Limits.RequestHeaders + 16 * 1024;

        private readonly NetworkStream _stream = new(socket, ownsSocket: true);
        private readonly SemaphoreSlim _writing = new(1, 1);
        private byte[] _buffer = new byte[8192];
        private int _start;
        private int _end;

        /// <summary>A read begun while a request runs, to see its client go, whose bytes, if any, are the next request's.</summary>
        private Task<int>? _pending;
        private CancellationTokenSource? _watching;

        public async Task ServeAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var head = await ReadHeadAsync();
                    if (head is null || !await HandleAsync(head)) return;
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
                // The client has gone, or the server stops: the connection closes.
            }
            finally
            {
                _watching?.Cancel();
                await _stream.DisposeAsync();
            }
        }

        private enum Filled { Bytes, Ended, TimedOut }

        /// <summary>
        /// The next request's head: its first byte within the time a kept connection may wait, the rest within the time a
        /// head may take to arrive; null where the connection ends first, or the request is refused, its answer sent.
        /// </summary>
        private async Task<WebLingoHttpHead?> ReadHeadAsync()
        {
            var limits = server.Limits;
            if (_start == _end && await FillAsync(limits.KeepAlive) != Filled.Bytes) return null;
            var clock = Stopwatch.StartNew();
            while (true)
            {
                var parsed = WebLingoHttpParser.Parse(_buffer.AsSpan(_start, _end - _start), limits, out var head, out var consumed, out var status,
                    out var reason);
                if (parsed == WebLingoHttpParsed.Done)
                {
                    _start += consumed;
                    return head;
                }
                if (parsed == WebLingoHttpParsed.Refused)
                {
                    await RefuseAsync(status, reason);
                    return null;
                }
                var left = limits.RequestArrival - clock.Elapsed;
                var filled = left <= TimeSpan.Zero ? Filled.TimedOut : await FillAsync(left);
                if (filled == Filled.TimedOut) await RefuseAsync(408, "The request's line and headers took too long to arrive.");
                if (filled != Filled.Bytes) return null;
            }
        }

        /// <summary>
        /// More bytes read into the buffer, within <paramref name="time"/>: the watching read's where one was begun, a new
        /// read's otherwise.
        /// </summary>
        private async Task<Filled> FillAsync(TimeSpan time)
        {
            if (_pending is null)
            {
                MakeRoom();
                _watching = new CancellationTokenSource();
                _pending = _stream.ReadAsync(_buffer.AsMemory(_end), _watching.Token).AsTask();
            }
            var reading = _pending;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var waited = await Task.WhenAny(reading, Task.Delay(time, timeout.Token));
            if (waited != reading)
            {
                _watching?.Cancel();
                return Filled.TimedOut;
            }
            await timeout.CancelAsync();
            _pending = null;
            int read;
            try
            {
                read = await reading;
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                return Filled.Ended;
            }
            if (read <= 0) return Filled.Ended;
            _end += read;
            return Filled.Bytes;
        }

        /// <summary>Room after the buffer's unread bytes: moved to its start, and the buffer made larger where they fill it, up to its most.</summary>
        private void MakeRoom()
        {
            if (_start > 0)
            {
                Array.Copy(_buffer, _start, _buffer, 0, _end - _start);
                (_end, _start) = (_end - _start, 0);
            }
            if (_end == _buffer.Length && _buffer.Length < MaxBuffer) Array.Resize(ref _buffer, Math.Min(_buffer.Length * 2, MaxBuffer));
            if (_end == _buffer.Length) throw new IOException("The connection's buffer is full.");
        }

        /// <summary>
        /// Once a request's body has been read, or where it has none: a read begun, which ends where the client goes, so that its
        /// request is cancelled, and whose bytes are otherwise the next request's.
        /// </summary>
        public void Watch(CancellationTokenSource gone)
        {
            if (_pending is not null) return;
            try
            {
                MakeRoom();
            }
            catch (IOException)
            {
                return;
            }
            _watching = new CancellationTokenSource();
            _pending = _stream.ReadAsync(_buffer.AsMemory(_end), _watching.Token).AsTask();
            _pending.ContinueWith(read =>
            {
                if (read.IsCompletedSuccessfully && read.Result > 0) return;
                try
                {
                    gone.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Its request has ended.
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        /// <summary>
        /// Up to <paramref name="into"/>'s length of the request's bytes: the buffer's first, then the client's, within
        /// <paramref name="time"/>, past which it throws <see cref="OperationCanceledException"/>; 0 at the connection's end.
        /// </summary>
        public async ValueTask<int> ReadSomeAsync(Memory<byte> into, TimeSpan time)
        {
            if (_start < _end)
            {
                var count = Math.Min(into.Length, _end - _start);
                _buffer.AsMemory(_start, count).CopyTo(into);
                _start += count;
                return count;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(time);
            return await _stream.ReadAsync(into, timeout.Token);
        }

        /// <summary>
        /// A line of the request's body's framing, a chunk's size or a trailer, without its CR LF, within <paramref name="time"/>;
        /// null where it is longer than <paramref name="most"/> or the connection ends.
        /// </summary>
        public async ValueTask<string?> ReadLineAsync(int most, TimeSpan time)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(time);
            while (true)
            {
                var span = _buffer.AsSpan(_start, _end - _start);
                var lf = span.IndexOf((byte)'\n');
                if (lf >= 0)
                {
                    if (lf == 0 || span[lf - 1] != '\r' || lf - 1 > most) return null;
                    var line = Encoding.ASCII.GetString(span[..(lf - 1)]);
                    _start += lf + 1;
                    return line;
                }
                if (span.Length > most + 1) return null; // Its CR may have come, its LF not yet.
                MakeRoom();
                var read = await _stream.ReadAsync(_buffer.AsMemory(_end), timeout.Token);
                if (read <= 0) return null;
                _end += read;
            }
        }

        /// <summary>Writes <paramref name="bytes"/> to the client, one write at a time.</summary>
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes)
        {
            await _writing.WaitAsync(stop);
            try
            {
                await _stream.WriteAsync(bytes, stop);
            }
            finally
            {
                _writing.Release();
            }
        }

        /// <summary>A request refused: its status, why in plain text, and the connection closed after it.</summary>
        private async Task RefuseAsync(int status, string reason)
        {
            var body = Encoding.UTF8.GetBytes(reason);
            var head = $"HTTP/1.1 {status.ToString(CultureInfo.InvariantCulture)} {WebLingoReasonPhrases.Of(status)}\r\nDate: {Date()}\r\n"
                       + $"Content-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n"
                       + "Connection: close\r\n\r\n";
            try
            {
                await WriteAsync((byte[])[.. Encoding.ASCII.GetBytes(head), .. body]);
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
                // The client has gone already.
            }
        }

        /// <summary>
        /// A request run, and its response sent; whether its connection may carry the next: both ends keep it, its body was read
        /// whole, and its response ended as it should.
        /// </summary>
        private async Task<bool> HandleAsync(WebLingoHttpHead head)
        {
            var application = server._application;
            var gone = new CancellationTokenSource();
            var sink = new Sink(this, head);
            var body = head.HasBody ? new Body(this, head, application.MaxBodySize, sink, gone, server.Limits) : null;
            if (body is null) Watch(gone);
            // Its client its connection's, or, from a proxy the site trusts, the proxy's word, with its scheme (part 4).
            var (client, scheme) = WebLingoProxies.Of(application).Resolve((socket.RemoteEndPoint as IPEndPoint)?.Address, "http",
                name => head.Headers.Where(header => header.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(header => header.Value));
            var request = new WebLingoBufferedRequest(head.Method, head.Path, head.Query, scheme, head.Host, head.Headers, (IWebLingoBodyReader?)body ?? Empty.Body)
            {
                ClientAddress = client
            };
            var response = new WebLingoStreamingResponse(gone.Token);
            var cancellation = new WebLingoCancellationSignal();
            using var cancelled = gone.Token.Register(cancellation.Cancel);
            // Woken, where it waits, by its client's going, by its time limit, or by a reload's drain (section 6.12).
            using var wake = CancellationTokenSource.CreateLinkedTokenSource(gone.Token);
            using var timer = WebLingoRequestTimer.Start(application, cancellation, wake);
            using var drained = WebLingoDrain.Of(application).Begin(cancellation, wake, timer);
            var run = server._workers.Serve((worker, scheduler) => application.Handle(request, response, worker, cancellation, scheduler, drained),
                wake.Token, response.End);
            try
            {
                await response.SendAsync(sink);
                var outcome = await run;
                if (sink.Aborted) return false;
                if (outcome is { Kind: WebLingoRequestOutcomeKind.File, File: { } file })
                {
                    if (outcome.Sent is { } sent)
                    {
                        await WebLingoFileSending.SendAsync(sink, head.Method, request.Headers, sent, response.StatusCode, response.Headers);
                    }
                    else
                    {
                        await WebLingoFileSending.SendSiteFileAsync(sink, head.Method, request.Headers, application.PathOf(file), application.Headers);
                    }
                }
                if (sink.Aborted) return false;
                await sink.EndAsync();
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
                // The client has gone, or the server stops: the page stops, and the connection closes.
                await gone.CancelAsync();
                await run;
                return false;
            }
            return head.KeepAlive && !sink.Closes && (body is null || body.Whole) && !gone.IsCancellationRequested;
        }
    }

    private readonly WebLingoApplication _application = application;
    private readonly WebLingoWorkerPool _workers = workers;

    /// <summary>Now as a <c>Date</c> header gives it, RFC 9110's IMF-fixdate.</summary>
    private static string Date() => DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);

    /// <summary>
    /// A request's response as HTTP/1.1's: its status line and headers, a <c>Date</c>, and its body by its
    /// <c>Content-Length</c> where it comes whole, in chunks where it does not, to the connection's end for HTTP/1.0's;
    /// no body for a <c>HEAD</c>'s, a 204's, a 304's; an end unfinished closes the connection.
    /// </summary>
    private sealed class Sink(Connection connection, WebLingoHttpHead head) : IWebLingoResponseSink
    {
        private bool _chunked;
        private bool _bodiless;

        /// <summary>Whether its head has been sent, after which <c>100 Continue</c> is not.</summary>
        public bool Started { get; private set; }

        /// <summary>Whether it ended unfinished.</summary>
        public bool Aborted { get; private set; }

        /// <summary>Whether its connection closes after it.</summary>
        public bool Closes { get; private set; }

        public async ValueTask StartAsync(int status, IWebLingoHeaderCollection headers, long? length)
        {
            Started = true;
            Closes = !head.KeepAlive;
            _bodiless = head.Method == "HEAD" || status is 204 or 304 or (>= 100 and < 200);
            var text = new StringBuilder("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(WebLingoReasonPhrases.Of(status)).Append("\r\n");
            var (hasLength, hasDate) = (false, false);
            foreach (var name in headers.Names)
            {
                // The framing is the server's own, and a page's Connection only says whether it closes.
                if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                {
                    Closes |= headers.GetValues(name).Any(value => value.Contains("close", StringComparison.OrdinalIgnoreCase));
                    continue;
                }
                hasLength |= name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);
                hasDate |= name.Equals("Date", StringComparison.OrdinalIgnoreCase);
                foreach (var value in headers.GetValues(name))
                {
                    if (name.AsSpan().IndexOfAny('\r', '\n') >= 0 || value.AsSpan().IndexOfAny('\r', '\n') >= 0) continue;
                    text.Append(name).Append(": ").Append(value).Append("\r\n");
                }
            }
            if (!hasDate) text.Append("Date: ").Append(Date()).Append("\r\n");
            if (length is { } bytes && !hasLength && status is not (204 or 304) and not (>= 100 and < 200))
            {
                text.Append("Content-Length: ").Append(bytes.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }
            else if (!hasLength && !_bodiless)
            {
                if (head.Minor == 1)
                {
                    _chunked = true;
                    text.Append("Transfer-Encoding: chunked\r\n");
                }
                else
                {
                    Closes = true; // HTTP/1.0's body ends where its connection does.
                }
            }
            if (Closes) text.Append("Connection: close\r\n");
            else if (head.Minor == 0) text.Append("Connection: keep-alive\r\n");
            text.Append("\r\n");
            await connection.WriteAsync(Encoding.UTF8.GetBytes(text.ToString()));
        }

        public async ValueTask WriteAsync(byte[] bytes)
        {
            if (_bodiless || bytes.Length == 0) return;
            if (!_chunked)
            {
                await connection.WriteAsync(bytes);
                return;
            }
            var size = Encoding.ASCII.GetBytes(bytes.Length.ToString("X", CultureInfo.InvariantCulture) + "\r\n");
            await connection.WriteAsync((byte[])[.. size, .. bytes, (byte)'\r', (byte)'\n']);
        }

        public ValueTask AbortAsync()
        {
            Aborted = true;
            return ValueTask.CompletedTask;
        }

        /// <summary>Its end: the last chunk, where it was sent in chunks.</summary>
        public async ValueTask EndAsync()
        {
            if (Started && _chunked && !Aborted) await connection.WriteAsync("0\r\n\r\n"u8.ToArray());
        }

        /// <summary><c>100 Continue</c>, where its request waits for it and nothing else has been sent.</summary>
        public async ValueTask ContinueAsync()
        {
            if (!Started) await connection.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray());
        }
    }

    /// <summary>
    /// A request's body, read as its page asks for it (specification, section 6.12, "Streaming"): by its length, or in chunks,
    /// their sizes and trailers read and dropped, at most the site's largest body; a read 0 at its end, -1 where its client
    /// has gone, or it is malformed or too large, its connection then closed. Its pace kept as Kestrel keeps it: once its
    /// waits for its client have taken <see cref="WebLingoHttpLimits.BodyGrace"/>, it must have come at
    /// <see cref="WebLingoHttpLimits.BodyRate"/> bytes a second of them, or a read fails, only the time spent waiting for the
    /// client counted, not the page's between its reads (section 9, "M0.23 parts", part 3).
    /// </summary>
    private sealed class Body(Connection connection, WebLingoHttpHead head, long largest, Sink sink, CancellationTokenSource gone,
        WebLingoHttpLimits limits) : IWebLingoBodyReader
    {
        private TimeSpan _waited;

        /// <summary>How long its next wait for the client may take, its pace kept; nothing where it is too slow already.</summary>
        private TimeSpan Allowed()
        {
            var due = Math.Max(limits.BodyGrace.TotalSeconds, (_read + 1) / limits.BodyRate);
            return TimeSpan.FromSeconds(due) - _waited;
        }

        /// <summary>A wait for the client, its time counted towards the body's pace.</summary>
        private async ValueTask<T> Waiting<T>(Func<TimeSpan, ValueTask<T>> wait)
        {
            var allowed = Allowed();
            if (allowed <= TimeSpan.Zero) throw new OperationCanceledException("The request's body came too slowly.");
            var clock = Stopwatch.StartNew();
            try
            {
                return await wait(allowed);
            }
            finally
            {
                _waited += clock.Elapsed;
            }
        }

        private readonly SemaphoreSlim _reading = new(1, 1);
        private long _left = head.ContentLength ?? 0;
        private long _chunkLeft;
        private long _read;
        private bool _ended;
        private bool _failed;
        private bool _continued;

        /// <summary>Whether it was read to its end, so that its connection may carry the next request.</summary>
        public bool Whole => _ended && !_failed;

        public int Read(byte[] buffer, int offset, int count) =>
            offset < 0 || count < 0 || offset > buffer.Length - count ? 0 : ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public void ReadAsync(byte[] buffer, int offset, int count, Action<int> done)
        {
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
            {
                done(0);
                return;
            }
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().ContinueWith(read => done(read.IsCompletedSuccessfully ? read.Result : -1),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        private async ValueTask<int> ReadAsync(Memory<byte> into)
        {
            await _reading.WaitAsync();
            try
            {
                if (_failed) return -1;
                if (_ended || into.Length == 0) return 0;
                if (head.ExpectsContinue && !_continued)
                {
                    _continued = true;
                    await sink.ContinueAsync();
                }
                var read = head.Chunked ? await ReadChunkedAsync(into) : await ReadLengthAsync(into);
                if (read > 0 && (_read += read) > largest) return Fail();
                if (read == 0) connection.Watch(gone);
                return read;
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
                return Fail();
            }
            finally
            {
                _reading.Release();
            }
        }

        private int Fail()
        {
            _failed = true;
            return -1;
        }

        private async ValueTask<int> ReadLengthAsync(Memory<byte> into)
        {
            if (_left == 0)
            {
                _ended = true;
                return 0;
            }
            var read = await Waiting(time => connection.ReadSomeAsync(into[..(int)Math.Min(into.Length, _left)], time));
            if (read <= 0) return Fail();
            _left -= read;
            if (_left == 0) _ended = true;
            if (_ended) connection.Watch(gone);
            return read;
        }

        private async ValueTask<int> ReadChunkedAsync(Memory<byte> into)
        {
            if (_chunkLeft == 0)
            {
                // A chunk's size, in hexadecimal, its extensions dropped; 0 the last, its trailers read to the empty line and dropped.
                var line = await Waiting(time => connection.ReadLineAsync(4096, time));
                if (line is null) return Fail();
                var semicolon = line.IndexOf(';');
                var size = (semicolon >= 0 ? line[..semicolon] : line).Trim();
                if (size.Length is 0 or > 15 || !long.TryParse(size, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _chunkLeft))
                {
                    return Fail();
                }
                if (_chunkLeft == 0)
                {
                    for (var trailers = 0; ; trailers++)
                    {
                        var trailer = await Waiting(time => connection.ReadLineAsync(8192, time));
                        if (trailer is null || trailers > 100) return Fail();
                        if (trailer.Length == 0) break;
                    }
                    _ended = true;
                    return 0;
                }
            }
            var read = await Waiting(time => connection.ReadSomeAsync(into[..(int)Math.Min(into.Length, _chunkLeft)], time));
            if (read <= 0) return Fail();
            _chunkLeft -= read;
            if (_chunkLeft == 0 && await Waiting(time => connection.ReadLineAsync(0, time)) is not "") return Fail();
            return read;
        }
    }

    /// <summary>The body of a request that has none.</summary>
    private sealed class Empty : IWebLingoBodyReader
    {
        public static Empty Body { get; } = new();

        public int Read(byte[] buffer, int offset, int count) => 0;

        public void ReadAsync(byte[] buffer, int offset, int count, Action<int> done) => done(0);
    }
}
