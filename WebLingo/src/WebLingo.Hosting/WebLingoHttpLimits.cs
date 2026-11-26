namespace WebLingo.Hosting;

/// <summary>
/// What a site's server takes of a client before it refuses it (specification, section 9, "M0.23 parts", part 3; the
/// owner's answer, A1 (a) of "M0.23, Part 3: Its Limits"): a request's line, its headers, how long they may take to arrive and
/// how fast its body must then come, and how long a kept connection may wait for its next request; Kestrel's defaults, each
/// but the count of headers and the body's pace <c>wasp.config</c>'s <c>"limits"</c>' own. WebLingo's own server keeps them
/// (<c>WebLingo.Hosting.Standalone</c>), and ASP.NET Core's, Kestrel's own limits set from them, so that a site refuses alike
/// whichever server serves it; a web server in front of FastCGI keeps its own. A request's body's largest is the site's
/// (<see cref="WebLingoApplicationOptions.Uploads"/>).
/// </summary>
public sealed record WebLingoHttpLimits
{
    /// <summary>The most bytes a request's line may have, its method, its target and its version, <c>"requestLine"</c>: past it, a 414.</summary>
    public int RequestLine { get; init; } = 8192;

    /// <summary>The most bytes a request's headers may have in all, their line breaks included, <c>"requestHeaders"</c>: past it, a 431.</summary>
    public int RequestHeaders { get; init; } = 32768;

    /// <summary>The most headers a request may have: past it, a 431.</summary>
    public int HeaderCount { get; init; } = 100;

    /// <summary>How long a request's line and headers may take to arrive, from their first byte, <c>"requestArrival"</c>: past it, a 408.</summary>
    public TimeSpan RequestArrival { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How many bytes a second a request's body must come at, on average, once <see cref="BodyGrace"/> has passed: slower, it is refused.</summary>
    public double BodyRate { get; init; } = 240;

    /// <summary>How long a request's body may come at any pace before <see cref="BodyRate"/> applies.</summary>
    public TimeSpan BodyGrace { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a kept connection may wait for its next request's first byte before it is closed, <c>"keepAlive"</c>.</summary>
    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(130);

    /// <summary>Kestrel's defaults.</summary>
    public static WebLingoHttpLimits Default { get; } = new();
}
