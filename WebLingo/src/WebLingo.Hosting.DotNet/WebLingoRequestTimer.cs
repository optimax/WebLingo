using WebLingo.Runtime;
namespace WebLingo.Hosting.DotNet;

/// <summary>
/// A request's time limit, its site's (specification, section 6.12, "Limits"), kept by its host, the portable core having
/// no timers: once the request has taken that long, its execution is asked to stop, its cancellation signal told why
/// (<see cref="WebLingoCancellationSignal.Exceeded"/>), and the request is woken where it waits, for a database, a client or
/// a timer, to see it. ASP.NET Core's host and the FastCGI host each start one for every request they serve.
/// </summary>
public static class WebLingoRequestTimer
{
    /// <summary>
    /// The request's timer, started now, which <paramref name="wake"/> wakes the request through; null where the
    /// application has no time limit. Disposed before it fires, it never does.
    /// </summary>
    public static IDisposable? Start(WebLingoApplication application, WebLingoCancellationSignal cancellation, CancellationTokenSource wake)
    {
        if (application.RequestTimeout is not { } limit) return null;
        var message = application.TimeLimitMessage;
        var timer = new CancellationTokenSource(limit);
        timer.Token.Register(() =>
        {
            cancellation.Exceeded(message);
            try
            {
                wake.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The request has ended already.
            }
        });
        return timer;
    }
}
