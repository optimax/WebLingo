namespace WebLingo.Hosting.Abstractions;

// WLHI's HTTP request and response (specification, section 6.7; section 9, "M0.14 parts", part 1): what a host gives the
// runtime of a request, and what the runtime gives back, contracts free of .NET's HTTP types, which every adapter
// implements, IIS's through ASP.NET Core first, FastCGI's and a standalone server's later. Errors are results, not
// exceptions. Nothing here is frozen.

/// <summary>A request, as its host received it.</summary>
public interface IWebLingoHttpRequest
{
    /// <summary>Its method as the client sent it: <c>GET</c>, <c>POST</c>, and so on, compared with regard to case.</summary>
    string Method { get; }

    /// <summary>Its path, from <c>/</c>, as its host decoded it, without its query.</summary>
    string Path { get; }

    /// <summary>Its query, after the <c>?</c>, as the client sent it, not decoded; empty if it has none.</summary>
    string QueryString { get; }

    /// <summary><c>http</c> or <c>https</c>.</summary>
    string Scheme { get; }

    /// <summary>The host the client named, with the port if it named one: <c>example.com:8080</c>.</summary>
    string Host { get; }

    /// <summary>Its headers, which cannot be changed.</summary>
    IWebLingoHeaderCollection Headers { get; }

    /// <summary>Its body, read as it is wanted.</summary>
    IWebLingoBodyReader Body { get; }
}

/// <summary>
/// Where a response stands: uncommitted, its status and headers changeable; committed, they have been sent and are fixed,
/// and its body is being written; completed, nothing more can be written.
/// </summary>
public enum WebLingoResponseState { Uncommitted, Committed, Completed }

/// <summary>A response, as the runtime gives it back to its host.</summary>
public interface IWebLingoHttpResponse
{
    WebLingoResponseState State { get; }

    /// <summary>Its status: 200 until it is set.</summary>
    int StatusCode { get; }

    /// <summary>Sets its status, from 100 to 999, while it is uncommitted; false otherwise, its status unchanged.</summary>
    bool TrySetStatusCode(int code);

    /// <summary>Its headers, which can be changed while it is uncommitted.</summary>
    IWebLingoHeaderCollection Headers { get; }

    /// <summary>Its body, whose first bytes commit it.</summary>
    IWebLingoBodyWriter Body { get; }

    /// <summary>Commits it, its status and headers fixed from then on; nothing if it is committed already.</summary>
    void Commit();

    /// <summary>Completes it, committing it first if it is not: nothing more can be written to it.</summary>
    void Complete();

    /// <summary>
    /// Ends it unfinished, as a broken connection would, so that its client sees its body incomplete, rather than whole:
    /// nothing more can be written to it. Nothing if it is completed already.
    /// </summary>
    void Abort();
}

/// <summary>What a change to headers did: it was made, or why not.</summary>
public enum WebLingoHeaderChange
{
    Done,
    /// <summary>The headers cannot be changed: a request's, or a committed response's.</summary>
    ReadOnly,
    /// <summary>The name is not an HTTP token: empty, or with a character a token cannot have (RFC 9110, section 5.6.2).</summary>
    InvalidName,
    /// <summary>The value has a character a header's value cannot: a control character but tab, or one beyond ASCII.</summary>
    InvalidValue
}

/// <summary>
/// A request's or a response's headers: each a name, compared without regard to ASCII case, and one or more values, in
/// the order they were given.
/// </summary>
public interface IWebLingoHeaderCollection
{
    /// <summary>Whether they cannot be changed: a request's never can, a response's only until it is committed.</summary>
    bool IsReadOnly { get; }

    /// <summary>The headers' names, each once, as first given, in the order first given.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>A header's values; none if it has none.</summary>
    IReadOnlyList<string> GetValues(string name);

    /// <summary>A header's values joined with <c>", "</c>, or null if it has none.</summary>
    string? Get(string name);

    /// <summary>Gives a header this one value in place of any it had.</summary>
    WebLingoHeaderChange Set(string name, string value);

    /// <summary>Gives a header one more value.</summary>
    WebLingoHeaderChange Add(string name, string value);

    /// <summary>Removes a header's values, done even if it had none.</summary>
    WebLingoHeaderChange Remove(string name);
}

/// <summary>A request's body, read in pieces.</summary>
public interface IWebLingoBodyReader
{
    /// <summary>
    /// Reads up to <paramref name="count"/> bytes into <paramref name="buffer"/> from <paramref name="offset"/>: how many it
    /// read, 0 at the body's end, or for a range outside the buffer.
    /// </summary>
    int Read(byte[] buffer, int offset, int count);
}

/// <summary>A response's body, written in pieces.</summary>
public interface IWebLingoBodyWriter
{
    /// <summary>
    /// Writes <paramref name="count"/> bytes of <paramref name="buffer"/> from <paramref name="offset"/>, committing the
    /// response first; false, nothing written, once it is completed, or for a range outside the buffer. It never waits.
    /// </summary>
    bool Write(byte[] buffer, int offset, int count);

    /// <summary>
    /// Whether what was written waits to be sent beyond what the response holds, so that a writer that can wait should,
    /// before writing more (backpressure; specification, section 6.12, "Streaming").
    /// </summary>
    bool IsFull { get; }

    /// <summary>Calls <paramref name="ready"/> once, from any thread, when the response can take more, or has ended; at once if it can now.</summary>
    void WhenReady(Action ready);
}
