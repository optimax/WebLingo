namespace WebLingo.Hosting.Abstractions;

/// <summary>
/// A response held in memory (specification, section 6.7; section 9, "M0.14 parts", part 1): for tests, and for a host
/// that sends a response once it is complete. It goes from uncommitted to committed, its status and headers fixed, when it
/// is committed or its body is first written, and to completed when it is completed, or aborted; <see cref="BodyBytes"/> has
/// what was written.
/// </summary>
public sealed class WebLingoBufferedResponse : IWebLingoHttpResponse
{
    private readonly WebLingoHeaderList _headers = new();
    private byte[] _body = new byte[256];
    private int _length;

    public WebLingoBufferedResponse() => Body = new Writer(this);

    public WebLingoResponseState State { get; private set; }

    public int StatusCode { get; private set; } = 200;

    public IWebLingoHeaderCollection Headers => _headers;

    public IWebLingoBodyWriter Body { get; }

    /// <summary>A copy of the body's bytes, as far as they have been written.</summary>
    public byte[] BodyBytes
    {
        get
        {
            var bytes = new byte[_length];
            Array.Copy(_body, bytes, _length);
            return bytes;
        }
    }

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
    }

    public void Complete()
    {
        Commit();
        State = WebLingoResponseState.Completed;
    }

    /// <summary>Whether it was aborted, ended unfinished, rather than completed.</summary>
    public bool Aborted { get; private set; }

    public void Abort()
    {
        if (State == WebLingoResponseState.Completed) return;
        Aborted = true;
        State = WebLingoResponseState.Completed;
    }

    private sealed class Writer(WebLingoBufferedResponse response) : IWebLingoBodyWriter
    {
        /// <summary>Never: it holds whatever is written.</summary>
        public bool IsFull => false;

        public void WhenReady(Action ready) => ready();

        public bool Write(byte[] buffer, int offset, int count)
        {
            if (response.State == WebLingoResponseState.Completed || offset < 0 || count < 0 || offset > buffer.Length - count) return false;
            response.Commit();
            if (response._length + count > response._body.Length)
            {
                Array.Resize(ref response._body, Math.Max(response._body.Length * 2, response._length + count));
            }
            Array.Copy(buffer, offset, response._body, response._length, count);
            response._length += count;
            return true;
        }
    }
}
