using System.Globalization;
using System.Text;
using System.Text.Json;
namespace WebLingo.Cli;

/// <summary>Debug Adapter Protocol messages on the adapter's standard streams, or on a connection to a site's own adapter.</summary>
internal sealed class WebLingoDebugProtocol(Stream input, Stream output)
{
    private readonly object _writes = new();
    private int _sequence;

    public JsonElement? Read()
    {
        int? length = null;
        var headerSize = 0;
        while (true)
        {
            var line = new StringBuilder();
            int next;
            while ((next = input.ReadByte()) >= 0 && next != '\n')
            {
                if (++headerSize > 8192) throw new InvalidDataException("The debug protocol header is too large.");
                line.Append((char)next);
            }
            if (next < 0)
            {
                if (headerSize == 0 && length is null) return null;
                throw new EndOfStreamException();
            }
            var header = line.ToString().TrimEnd('\r');
            if (header.Length == 0) break;
            const string prefix = "Content-Length:";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (length is not null || !int.TryParse(header.AsSpan(prefix.Length).Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > 1024 * 1024)
                throw new InvalidDataException("The debug protocol message has an invalid content length.");
            length = parsed;
        }
        if (length is not { } count) throw new InvalidDataException("The debug protocol message has no content length.");
        var payload = new byte[count];
        input.ReadExactly(payload);
        return JsonSerializer.Deserialize<JsonElement>(payload);
    }

    public void Respond(int request, string command, object? body = null, string? error = null)
    {
        var message = new Dictionary<string, object?>
        {
            ["type"] = "response", ["request_seq"] = request, ["command"] = command, ["success"] = error is null
        };
        if (body is not null) message["body"] = body;
        if (error is not null) message["message"] = error;
        Write(message);
    }

    public void Event(string name, object? body = null)
    {
        var message = new Dictionary<string, object?> { ["type"] = "event", ["event"] = name };
        if (body is not null) message["body"] = body;
        Write(message);
    }

    /// <summary>A request of its own, <paramref name="command"/> with <paramref name="arguments"/>, as a client sends one.</summary>
    public void Request(string command, object arguments) =>
        Write(new Dictionary<string, object?> { ["type"] = "request", ["command"] = command, ["arguments"] = arguments });

    /// <summary>
    /// A message another side sent, written as it is, its <c>seq</c> its own: a client's to the site a session passes them
    /// on to, and the site's back to it (specification, section 5.7, "Debugging a site where it runs, parts", part 2).
    /// </summary>
    public void Forward(JsonElement message)
    {
        lock (_writes) Send(JsonSerializer.SerializeToUtf8Bytes(message));
    }

    private void Write(Dictionary<string, object?> message)
    {
        lock (_writes)
        {
            message["seq"] = ++_sequence;
            Send(JsonSerializer.SerializeToUtf8Bytes(message));
        }
    }

    private void Send(byte[] payload)
    {
        output.Write(Encoding.ASCII.GetBytes(FormattableString.Invariant($"Content-Length: {payload.Length}\r\n\r\n")));
        output.Write(payload);
        output.Flush();
    }
}
