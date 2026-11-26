namespace WebLingo.Hosting.Standalone;

/// <summary>
/// What the standalone server takes of a client before it refuses it (specification, section 9, "M0.23 parts", parts 2 and
/// 3): a request's line, its headers, how long they may take to arrive, and how long a kept connection may wait for its
/// next request; Kestrel's defaults, each a site's own once part 3 reads them from its configuration. A request's body's
/// largest is the site's (<see cref="WebLingoApplication.MaxBodySize"/>).
/// </summary>
public sealed record WebLingoHttpLimits
{
    /// <summary>The most bytes a request's line may have, its method, its target and its version: past it, a 414.</summary>
    public int RequestLine { get; init; } = 8192;

    /// <summary>The most bytes a request's headers may have in all, their line breaks included: past it, a 431.</summary>
    public int RequestHeaders { get; init; } = 32768;

    /// <summary>The most headers a request may have: past it, a 431.</summary>
    public int HeaderCount { get; init; } = 100;

    /// <summary>How long a request's line and headers may take to arrive, from their first byte: past it, a 408.</summary>
    public TimeSpan RequestArrival { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a kept connection may wait for its next request's first byte before it is closed.</summary>
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(130);

    /// <summary>Kestrel's defaults.</summary>
    public static WebLingoHttpLimits Default { get; } = new();
}
