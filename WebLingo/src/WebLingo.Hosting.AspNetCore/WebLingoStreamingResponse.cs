using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using WebLingo.Hosting.Abstractions;
namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// What a response's pieces are sent to (<see cref="WebLingoStreamingResponse"/>): an HTTP server's response, ASP.NET Core's,
/// or FastCGI's records (section 6.2). Its status and headers come once, before its body, with the body's length when it
/// comes whole; then its body, piece by piece; or its end, unfinished, which the client sees.
/// </summary>
public interface IWebLingoResponseSink
{
    ValueTask StartAsync(int status, IWebLingoHeaderCollection headers, long? length);

    ValueTask WriteAsync(byte[] bytes);

    ValueTask AbortAsync();
}

/// <summary>
/// A response sent as it is written (specification, section 6.12, "Streaming"; section 9, "M0.14.7 parts", part 1), to
/// ASP.NET Core's, or to another host's sink (<see cref="IWebLingoResponseSink"/>): its worker commits it, writes its body
/// and completes or aborts it, each a piece passed to the request's own task, which sends its status and headers, then its
/// body, as they come, through <see cref="SendAsync(HttpContext)"/>. A body sent whole, in one piece, has its
/// <c>Content-Length</c>. A write never waits: once <see cref="Pieces"/> pieces of the body wait to
/// be sent, the body is full (<see cref="IWebLingoBodyWriter.IsFull"/>), and a page waits for it, its worker running other
/// requests meanwhile (backpressure, part 3), until the client has taken one, or has gone.
/// </summary>
public sealed class WebLingoStreamingResponse : IWebLingoHttpResponse
{
    /// <summary>How many pieces of the body may wait to be sent before it is full.</summary>
    public const int Pieces = 4;

    private readonly WebLingoHeaderList _headers = new();
    private readonly Channel<Piece> _pieces = Channel.CreateUnbounded<Piece>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly CancellationToken _gone;
    private readonly object _gate = new();
    private readonly List<Action> _ready = [];
    private int _waiting; // The body's pieces written and not yet sent.
    private bool _ended;

    /// <param name="gone">Signalled when the client has gone, after which nothing written is sent.</param>
    public WebLingoStreamingResponse(CancellationToken gone)
    {
        _gone = gone;
        Body = new Writer(this);
    }

    public WebLingoResponseState State { get; private set; }

    public int StatusCode { get; private set; } = 200;

    public IWebLingoHeaderCollection Headers => _headers;

    public IWebLingoBodyWriter Body { get; }

    public bool TrySetStatusCode(int code)
    {
        if (State != WebLingoResponseState.Uncommitted || code is < 100 or > 999) return false;
        StatusCode = code;
        return true;
    }

    public void Commit()
    {
        if (State != WebLingoResponseState.Uncommitted) return;
        _headers.MakeReadOnly();
        State = WebLingoResponseState.Committed;
        Send(new Piece(PieceKind.Commit, []));
    }

    public void Complete()
    {
        if (State == WebLingoResponseState.Completed) return;
        Commit();
        State = WebLingoResponseState.Completed;
        _pieces.Writer.TryComplete();
    }

    public void Abort()
    {
        if (State == WebLingoResponseState.Completed) return;
        State = WebLingoResponseState.Completed;
        Send(new Piece(PieceKind.Abort, []));
        _pieces.Writer.TryComplete();
    }

    /// <summary>
    /// Ends what its worker gives, completing nothing that it left uncommitted, as a file the host serves leaves it, so
    /// that <see cref="SendAsync"/> returns; its worker calls it last, whatever the request came to.
    /// </summary>
    public void End() => _pieces.Writer.TryComplete();

    /// <summary>A piece for the request's task, at once; dropped once its sending has ended.</summary>
    private void Send(Piece piece)
    {
        if (piece.Kind == PieceKind.Data) Interlocked.Increment(ref _waiting);
        _pieces.Writer.TryWrite(piece);
    }

    private bool IsFull => Volatile.Read(ref _waiting) >= Pieces && !Volatile.Read(ref _ended);

    /// <summary>
    /// Calls <paramref name="ready"/> with <c>true</c> once there is room, or the response has ended, at once if there is;
    /// with <c>false</c> once <paramref name="timeout"/> has passed without, whichever comes first, once.
    /// </summary>
    private void WhenReady(TimeSpan timeout, Action<bool> ready)
    {
        var fired = 0;
        Timer? timer = null;
        void Fire(bool inTime)
        {
            if (Interlocked.Exchange(ref fired, 1) != 0) return;
            timer?.Dispose();
            ready(inTime);
        }
        lock (_gate)
        {
            if (IsFull)
            {
                timer = new Timer(_ => Fire(false), null, timeout, Timeout.InfiniteTimeSpan);
                _ready.Add(() => Fire(true));
                return;
            }
        }
        ready(true);
    }

    /// <summary>A piece of the body sent: those waiting for room told, once there is some.</summary>
    private void Sent()
    {
        if (Interlocked.Decrement(ref _waiting) < Pieces) Release();
    }

    /// <summary>Tells those waiting for room that there is some, or that nothing more will be sent.</summary>
    private void Release()
    {
        Action[] ready;
        lock (_gate)
        {
            if (_ready.Count == 0 || IsFull) return;
            ready = [.. _ready];
            _ready.Clear();
        }
        foreach (var action in ready) action();
    }

    /// <summary>
    /// On the request's task: sends the pieces to <paramref name="context"/>'s response as they come, until its worker ends
    /// the response; whether it was committed, so that a response left uncommitted, a file's, is the caller's to send. If
    /// it fails, it ends the response, so that its worker never waits for it again.
    /// </summary>
    public Task<bool> SendAsync(HttpContext context) => SendAsync(new AspNetCoreSink(context, _gone));

    /// <summary>As the other, the pieces sent to <paramref name="sink"/>.</summary>
    public async Task<bool> SendAsync(IWebLingoResponseSink sink)
    {
        try
        {
            return await SendPiecesAsync(sink);
        }
        finally
        {
            _pieces.Writer.TryComplete();
            Volatile.Write(ref _ended, true);
            Release();
        }
    }

    private async Task<bool> SendPiecesAsync(IWebLingoResponseSink sink)
    {
        var committed = false;
        var started = false;
        byte[]? held = null; // The first piece of the body, held until the next shows whether it is the whole body.
        var sentAny = false;
        async ValueTask Start(long? length)
        {
            if (started) return;
            started = true;
            await sink.StartAsync(StatusCode, _headers, length);
        }
        await foreach (var piece in _pieces.Reader.ReadAllAsync(CancellationToken.None))
        {
            switch (piece.Kind)
            {
                case PieceKind.Commit:
                    committed = true;
                    break;
                case PieceKind.Data when !sentAny && held is null:
                    held = piece.Bytes;
                    break;
                case PieceKind.Data:
                    await Start(null);
                    if (held is not null)
                    {
                        await sink.WriteAsync(held);
                        Sent();
                    }
                    held = null;
                    sentAny = true;
                    await sink.WriteAsync(piece.Bytes);
                    Sent();
                    break;
                case PieceKind.Abort:
                    if (committed) await Start(null);
                    if (held is not null) await sink.WriteAsync(held);
                    await sink.AbortAsync();
                    return committed;
            }
        }
        if (held is not null)
        {
            await Start(sentAny ? null : held.Length);
            await sink.WriteAsync(held);
        }
        else if (committed && !sentAny)
        {
            await Start(0);
        }
        return committed;
    }

    /// <summary>ASP.NET Core's response as a sink: its status and headers set, its body written, an abort the connection's.</summary>
    internal sealed class AspNetCoreSink(HttpContext context, CancellationToken gone) : IWebLingoResponseSink
    {
        public ValueTask StartAsync(int status, IWebLingoHeaderCollection headers, long? length)
        {
            var response = context.Response;
            response.StatusCode = status;
            foreach (var name in headers.Names) response.Headers[name] = new StringValues([.. headers.GetValues(name)]);
            if (length is { } bytes) response.ContentLength = bytes;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteAsync(byte[] bytes) => context.Response.Body.WriteAsync(bytes, gone);

        public async ValueTask AbortAsync()
        {
            await context.Response.Body.FlushAsync(gone);
            context.Abort();
        }
    }

    private enum PieceKind { Commit, Data, Abort }

    private readonly record struct Piece(PieceKind Kind, byte[] Bytes);

    private sealed class Writer(WebLingoStreamingResponse response) : IWebLingoBodyWriter
    {
        public bool IsFull => response.IsFull;

        public void WhenReady(TimeSpan timeout, Action<bool> ready) => response.WhenReady(timeout, ready);

        public bool Write(byte[] buffer, int offset, int count)
        {
            if (response.State == WebLingoResponseState.Completed || offset < 0 || count < 0 || offset > buffer.Length - count) return false;
            response.Commit();
            if (count == 0) return true;
            var bytes = new byte[count];
            Array.Copy(buffer, offset, bytes, 0, count);
            response.Send(new Piece(PieceKind.Data, bytes));
            return true;
        }
    }
}
