using System.Net.Sockets;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// The network on .NET (specification, section 7.9): HTTP through one <see cref="HttpClient"/> for the process, which
/// follows no redirect, so that every host it reaches is one the application may; TCP through <see cref="TcpClient"/>.
/// The hosts the application may reach are those given, or any when none are (<c>weblingo</c>'s).
/// </summary>
public sealed class DotNetNetwork(IReadOnlyCollection<string>? allowed) : IWebLingoNetwork
{
    /// <summary>The largest body a response may have, which is read whole.</summary>
    public const int MaxBody = 64 * 1024 * 1024;

    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(30)
    })
    {
        Timeout = TimeSpan.FromSeconds(100)
    };

    public bool Allows(string host)
    {
        if (allowed is null) return true;
        foreach (var name in allowed)
        {
            if (string.Equals(name, host, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public WebLingoHttpIncoming Send(WebLingoHttpOutgoing request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
        {
            throw new WebLingoNetworkException(WebLingoNetworkErrorKind.InvalidAddress, $"'{request.Url}' is not an http or https address");
        }
        Check(address.Host);
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), address);
        if (request.Body is { } body) message.Content = new ByteArrayContent(body);
        foreach (var (name, value) in request.Headers)
        {
            if (message.Headers.TryAddWithoutValidation(name, value)) continue;
            message.Content ??= new ByteArrayContent([]);
            if (!message.Content.Headers.TryAddWithoutValidation(name, value))
            {
                throw new WebLingoNetworkException(WebLingoNetworkErrorKind.InvalidAddress, $"'{name}' is not a header a request may have");
            }
        }
        try
        {
            using var response = Client.Send(message, HttpCompletionOption.ResponseHeadersRead);
            var headers = new List<KeyValuePair<string, string>>();
            foreach (var (name, values) in response.Headers) headers.Add(new(name.ToLowerInvariant(), string.Join(", ", values)));
            foreach (var (name, values) in response.Content.Headers) headers.Add(new(name.ToLowerInvariant(), string.Join(", ", values)));
            using var stream = response.Content.ReadAsStream();
            using var read = new MemoryStream();
            var buffer = new byte[81920];
            for (int count; (count = stream.Read(buffer, 0, buffer.Length)) > 0;)
            {
                if (read.Length + count > MaxBody)
                {
                    throw new WebLingoNetworkException(WebLingoNetworkErrorKind.Failed, $"'{request.Url}' gave a body larger than {MaxBody / (1024 * 1024)} MB");
                }
                read.Write(buffer, 0, count);
            }
            return new WebLingoHttpIncoming((int)response.StatusCode, headers, read.ToArray());
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
        {
            throw new WebLingoNetworkException(WebLingoNetworkErrorKind.Failed,
                exception is TaskCanceledException or OperationCanceledException ? $"'{request.Url}' did not answer in time" : $"'{request.Url}': {exception.Message}");
        }
    }

    public IWebLingoConnection Connect(string host, int port)
    {
        Check(host);
        var client = new TcpClient();
        try
        {
            client.Connect(host, port);
            return new Connection(client);
        }
        catch (Exception exception) when (exception is SocketException or IOException or ArgumentException)
        {
            client.Dispose();
            throw new WebLingoNetworkException(WebLingoNetworkErrorKind.Failed, $"{host}:{port}: {exception.Message}");
        }
    }

    private void Check(string host)
    {
        if (!Allows(host)) throw new WebLingoNetworkException(WebLingoNetworkErrorKind.NotAllowed, $"'{host}' is not a host this application may reach");
    }

    private sealed class Connection(TcpClient client) : IWebLingoConnection
    {
        private readonly NetworkStream _stream = client.GetStream();

        public byte[]? Read(int most)
        {
            var buffer = new byte[most];
            try
            {
                var count = _stream.Read(buffer, 0, most);
                return count == 0 ? null : buffer[..count];
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                throw new WebLingoNetworkException(WebLingoNetworkErrorKind.Failed, $"the connection failed: {exception.Message}");
            }
        }

        public void Write(byte[] data)
        {
            try
            {
                _stream.Write(data, 0, data.Length);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                throw new WebLingoNetworkException(WebLingoNetworkErrorKind.Failed, $"the connection failed: {exception.Message}");
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
            client.Dispose();
        }
    }
}
