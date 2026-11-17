namespace WebLingo.Platform.Abstractions;

/// <summary>
/// The platform's network (specification, sections 7.9 and 7.13, WL-P009): HTTP requests and TCP connections, each
/// blocking, which <c>std:http</c> and <c>std:net</c> run off the worker; and the hosts the application may reach, which
/// it checks where each connection is made, any host under <c>weblingo</c>, only those <c>wasp.config</c> allows under
/// <c>wasp</c>.
/// </summary>
public interface IWebLingoNetwork
{
    /// <summary>Whether the application may reach a host, named as an address names it, compared ignoring case.</summary>
    bool Allows(string host);

    /// <summary>
    /// An HTTP request, its response read whole, a redirect not followed but given as it is; a
    /// <see cref="WebLingoNetworkException"/> for an address that is not an <c>http</c> or <c>https</c> one, a host the
    /// application may not reach, or a request that fails.
    /// </summary>
    WebLingoHttpIncoming Send(WebLingoHttpOutgoing request);

    /// <summary>A TCP connection to a host's port; a <see cref="WebLingoNetworkException"/> as <see cref="Send"/> throws one.</summary>
    IWebLingoConnection Connect(string host, int port);
}

/// <summary>An HTTP request: its method, its address, its headers, and its body, if it has one.</summary>
public sealed record WebLingoHttpOutgoing(string Method, string Url, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[]? Body);

/// <summary>An HTTP response: its status, its headers, each name in lower case, and its body.</summary>
public sealed record WebLingoHttpIncoming(int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body);

/// <summary>A TCP connection, closed by <see cref="IDisposable.Dispose"/>.</summary>
public interface IWebLingoConnection : IDisposable
{
    /// <summary>The bytes that have come, waiting for at least one, at most <paramref name="most"/>; null at the end.</summary>
    byte[]? Read(int most);

    /// <summary>Sends the bytes, all of them.</summary>
    void Write(byte[] data);
}

public enum WebLingoNetworkErrorKind
{
    /// <summary>A host the application may not reach.</summary>
    NotAllowed,
    /// <summary>An address that is not one: not <c>http</c> or <c>https</c>, or not written as one.</summary>
    InvalidAddress,
    /// <summary>A request or a connection that failed: a host not found, refused, gone, or too slow.</summary>
    Failed
}

/// <summary>A network operation's failure, its message naming what the script gave, never the host's own.</summary>
public sealed class WebLingoNetworkException(WebLingoNetworkErrorKind kind, string message) : Exception(message)
{
    public WebLingoNetworkErrorKind Kind { get; } = kind;
}
