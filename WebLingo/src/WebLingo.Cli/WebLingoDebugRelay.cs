using System.Net;
using System.Net.Sockets;
using System.Text.Json;
namespace WebLingo.Cli;

/// <summary>
/// A debug session's way to a site another process serves, one that lets a debugger in on a port of this machine's, on
/// 127.0.0.1 (specification, section 5.7, "Debugging a site where it runs, parts", part 2), so that any client of the Debug
/// Adapter Protocol that starts <c>wasp --debug-adapter</c> attaches to it: the site's own adapter initialized as the client
/// initialized this one, and given the client's <c>attach</c>, its token among its arguments; then each message the client
/// sends passed on to the site as it is, and each the site sends passed back, until either side ends.
/// </summary>
internal sealed class WebLingoDebugRelay
{
    private readonly TcpClient _connection;
    private readonly WebLingoDebugProtocol _site;

    private WebLingoDebugRelay(TcpClient connection, WebLingoDebugProtocol site) => (_connection, _site) = (connection, site);

    /// <summary>
    /// Connected to the site's adapter on <paramref name="port"/>, initialized, and given <paramref name="attach"/>, whose
    /// answer the site sends back through it; an <see cref="ArgumentException"/>, saying why, where it cannot be.
    /// </summary>
    public static WebLingoDebugRelay Connect(int port, JsonElement attach, bool linesStartAt1, bool columnsStartAt1)
    {
        var connection = new TcpClient();
        try
        {
            connection.Connect(IPAddress.Loopback, port);
            var stream = connection.GetStream();
            var site = new WebLingoDebugProtocol(stream, stream);
            site.Request("initialize", new { clientID = "wasp", adapterID = "wasp", linesStartAt1, columnsStartAt1, pathFormat = "path" });
            // Its answer, which the client had from this adapter already, not passed on.
            while (true)
            {
                if (site.Read() is not { } message) throw new IOException("the site's adapter closed the connection.");
                if (Text(message, "type") != "response") continue;
                if (message.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                    throw new IOException(Text(message, "message") ?? "the site's adapter refused to initialize.");
                break;
            }
            site.Forward(attach);
            return new WebLingoDebugRelay(connection, site);
        }
        catch (Exception failure) when (failure is SocketException or IOException or InvalidDataException or JsonException)
        {
            connection.Dispose();
            throw new ArgumentException($"No site lets a debugger in on 127.0.0.1:{port}: {failure.Message}");
        }
    }

    /// <summary>
    /// The client's messages, read from <paramref name="client"/>, passed on to the site, and the site's passed back, until
    /// the client's end; where the site ends first, its connection closed, the client told the session has ended, once.
    /// </summary>
    public int Run(WebLingoDebugProtocol client)
    {
        var ended = false;
        var back = new Thread(() =>
        {
            try
            {
                while (_site.Read() is { } message)
                {
                    if (Text(message, "type") == "event" && Text(message, "event") == "terminated") ended = true;
                    client.Forward(message);
                }
            }
            catch (Exception failure) when (failure is IOException or ObjectDisposedException or InvalidDataException or JsonException
                                                or EndOfStreamException)
            {
                // The site's connection closed under it.
            }
            try
            {
                if (!ended) client.Event("terminated");
            }
            catch (IOException)
            {
                // The client has gone too.
            }
        }) { IsBackground = true, Name = "The site's debugger's messages" };
        back.Start();
        try
        {
            while (client.Read() is { } message) _site.Forward(message);
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException or InvalidDataException or JsonException
                                            or EndOfStreamException)
        {
            // The client's end, or the site's connection closed under it.
        }
        finally
        {
            try
            {
                _connection.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception failure) when (failure is SocketException or ObjectDisposedException)
            {
                // Closed already.
            }
            back.Join(TimeSpan.FromSeconds(5));
            _connection.Dispose();
        }
        return 0;
    }

    private static string? Text(JsonElement message, string key) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
