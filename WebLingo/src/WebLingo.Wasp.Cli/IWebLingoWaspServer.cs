namespace WebLingo.Wasp.Cli;

/// <summary>
/// A site's server under <c>wasp serve</c>, whichever host serves it (specification, section 9, "M0.23 parts", part 2):
/// ASP.NET Core's, <c>--host aspnetcore</c>, or WebLingo's own, <c>--host standalone</c>; started, then stopped.
/// </summary>
public interface IWebLingoWaspServer : IAsyncDisposable
{
    /// <summary>Where it serves, once started, <c>http://127.0.0.1:8080</c>, its port the system's where it chose one.</summary>
    string Address { get; }

    /// <summary>Begins serving.</summary>
    Task StartAsync();

    /// <summary>Stops serving, its site's requests ended.</summary>
    Task StopAsync();
}
