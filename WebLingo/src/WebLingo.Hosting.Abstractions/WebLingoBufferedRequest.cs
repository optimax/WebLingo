namespace WebLingo.Hosting.Abstractions;

/// <summary>
/// A request held in memory, its body whole (specification, section 6.7; section 9, "M0.14 parts", part 1): for tests, and
/// for a host that has the whole request before it runs it. Its headers are given when it is made, and fixed.
/// </summary>
public sealed class WebLingoBufferedRequest : IWebLingoHttpRequest
{
    private readonly WebLingoHeaderList _headers = new();

    /// <param name="headers">Its headers, each a name and a value; one that is not a header's is left out, as a host refuses it.</param>
    /// <param name="body">Its body's bytes, which it keeps; none if null.</param>
    public WebLingoBufferedRequest(string method, string path, string queryString = "", string scheme = "http", string host = "localhost",
        IEnumerable<(string Name, string Value)>? headers = null, byte[]? body = null)
    {
        (Method, Path, QueryString, Scheme, Host) = (method, path, queryString, scheme, host);
        foreach (var (name, value) in headers ?? []) _headers.Add(name, value);
        _headers.MakeReadOnly();
        Body = new Reader(body ?? []);
    }

    public string Method { get; }
    public string Path { get; }
    public string QueryString { get; }
    public string Scheme { get; }
    public string Host { get; }
    public IWebLingoHeaderCollection Headers => _headers;
    public IWebLingoBodyReader Body { get; }

    private sealed class Reader(byte[] body) : IWebLingoBodyReader
    {
        private int _at;

        public int Read(byte[] buffer, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > buffer.Length - count) return 0;
            var read = Math.Min(count, body.Length - _at);
            Array.Copy(body, _at, buffer, offset, read);
            _at += read;
            return read;
        }
    }
}
