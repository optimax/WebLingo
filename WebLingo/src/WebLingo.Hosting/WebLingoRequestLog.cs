using System.Globalization;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Hosting;

/// <summary>
/// A request's log (specification, section 6.12, "Logging"): the application's, each line for the request, as the
/// debugger names one, <c>GET /cart #12</c>, its number the log's next when the request first writes, so that every line of
/// one request has the same; used on the request's worker only, its number given by the host's log, which may count on
/// several threads.
/// </summary>
internal sealed class WebLingoRequestLog(IWebLingoLog log, string request) : IWebLingoLogWriter
{
    private string? _name;

    public WebLingoLogLevel Level => log.Level;

    /// <summary>The request's name in its lines, <c>GET /cart #12</c>.</summary>
    public string Name => _name ??= request + " #" + log.NextRequest().ToString(CultureInfo.InvariantCulture);

    public void Write(WebLingoLogLevel level, string text)
    {
        if (level >= log.Level) log.Write(level, Name, text);
    }
}
