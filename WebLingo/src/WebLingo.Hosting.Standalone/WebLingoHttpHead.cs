namespace WebLingo.Hosting.Standalone;

/// <summary>
/// A request's line and headers as the standalone server read them (<see cref="WebLingoHttpParser"/>): its method, its
/// path, decoded, and its query, as it came; its HTTP version's minor number, 0 or 1; its host; its headers, in order; and
/// how its body comes, by its <see cref="ContentLength"/>, <see cref="Chunked"/>, or not at all.
/// </summary>
public sealed record WebLingoHttpHead(string Method, string Path, string Query, int Minor, string Host, IReadOnlyList<(string Name, string Value)> Headers)
{
    /// <summary>Its body's length, where its <c>Content-Length</c> gives it; null for none, or a chunked body.</summary>
    public long? ContentLength { get; init; }

    /// <summary>Whether its body comes in chunks, its <c>Transfer-Encoding: chunked</c>.</summary>
    public bool Chunked { get; init; }

    /// <summary>Whether its connection may carry another request after it: HTTP/1.1's unless it says <c>Connection: close</c>, HTTP/1.0's only where it says <c>keep-alive</c>.</summary>
    public bool KeepAlive { get; init; }

    /// <summary>Whether it waits for <c>100 Continue</c> before it sends its body, <c>Expect: 100-continue</c>.</summary>
    public bool ExpectsContinue { get; init; }

    /// <summary>Whether it has a body at all.</summary>
    public bool HasBody => Chunked || ContentLength is > 0;
}
