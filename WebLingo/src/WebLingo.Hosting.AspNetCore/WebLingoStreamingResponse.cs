using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using WebLingo.Hosting.Abstractions;
namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// A response sent to ASP.NET Core's as it is written (specification, section 6.12, "Streaming"; section 9, "M0.14.7 parts",
/// part 1): its worker commits it, writes its body and completes or aborts it, each a piece passed to the request's own
/// task, which sends its status and headers, then its body, as they come, through <see cref="SendAsync"/>. A body sent whole,
/// in one piece, has its <c>Content-Length</c>. At most <see cref="Pieces"/> pieces wait to be sent; a write beyond them
/// keeps its worker waiting until the client has taken one, or has gone (backpressure without a thread comes with part 3).
/// </summary>
public sealed class WebLingoStreamingResponse : IWebLingoHttpResponse
{
    /// <summary>How many pieces may wait to be sent before a write waits.</summary>
    public const int Pieces = 4;

    private readonly WebLingoHeaderList _headers = new();
    private readonly Channel<Piece> _pieces = Channel.CreateBounded<Piece>(new BoundedChannelOptions(Pieces)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly CancellationToken _gone;

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

    /// <summary>A piece for the request's task, waited for while <see cref="Pieces"/> wait already; dropped once the client has gone.</summary>
    private void Send(Piece piece)
    {
        if (_pieces.Writer.TryWrite(piece)) return;
        try
        {
            _pieces.Writer.WriteAsync(piece, _gone).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // The client has gone: what it would have been sent is dropped, and the request is cancelled.
        }
        catch (ChannelClosedException)
        {
            // Ended already.
        }
    }

    /// <summary>
    /// On the request's task: sends the pieces to <paramref name="context"/>'s response as they come, until its worker ends
    /// the response; whether it was committed, so that a response left uncommitted, a file's, is the caller's to send. If
    /// it fails, it ends the response, so that its worker never waits for it again.
    /// </summary>
    public async Task<bool> SendAsync(HttpContext context)
    {
        try
        {
            return await SendPiecesAsync(context);
        }
        finally
        {
            _pieces.Writer.TryComplete();
        }
    }

    private async Task<bool> SendPiecesAsync(HttpContext context)
    {
        var response = context.Response;
        var committed = false;
        byte[]? held = null; // The first piece of the body, held until the next shows whether it is the whole body.
        var sentAny = false;
        await foreach (var piece in _pieces.Reader.ReadAllAsync(CancellationToken.None))
        {
            switch (piece.Kind)
            {
                case PieceKind.Commit:
                    committed = true;
                    response.StatusCode = StatusCode;
                    foreach (var name in _headers.Names) response.Headers[name] = new StringValues([.. _headers.GetValues(name)]);
                    break;
                case PieceKind.Data when !sentAny && held is null:
                    held = piece.Bytes;
                    break;
                case PieceKind.Data:
                    if (held is not null) await response.Body.WriteAsync(held, _gone);
                    held = null;
                    sentAny = true;
                    await response.Body.WriteAsync(piece.Bytes, _gone);
                    break;
                case PieceKind.Abort:
                    if (held is not null) await response.Body.WriteAsync(held, _gone);
                    await response.Body.FlushAsync(_gone);
                    context.Abort();
                    return committed;
            }
        }
        if (held is not null)
        {
            if (!sentAny) response.ContentLength = held.Length;
            await response.Body.WriteAsync(held, _gone);
        }
        else if (committed && !sentAny)
        {
            response.ContentLength = 0;
        }
        return committed;
    }

    private enum PieceKind { Commit, Data, Abort }

    private readonly record struct Piece(PieceKind Kind, byte[] Bytes);

    private sealed class Writer(WebLingoStreamingResponse response) : IWebLingoBodyWriter
    {
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
