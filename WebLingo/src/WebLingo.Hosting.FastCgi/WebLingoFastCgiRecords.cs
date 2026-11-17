namespace WebLingo.Hosting.FastCgi;

/// <summary>A FastCGI record's type (the FastCGI specification, 1.0, section 8).</summary>
public enum WebLingoFastCgiType : byte
{
    BeginRequest = 1, AbortRequest = 2, EndRequest = 3, Params = 4, Stdin = 5, Stdout = 6, Stderr = 7, Data = 8, GetValues = 9,
    GetValuesResult = 10, UnknownType = 11
}

/// <summary>A FastCGI record: its type, its request's id, and its content, without its padding.</summary>
public readonly record struct WebLingoFastCgiRecord(WebLingoFastCgiType Type, int RequestId, byte[] Content);

/// <summary>
/// FastCGI's records, read from and written to a connection (the FastCGI specification, 1.0): an 8-byte header, its version
/// 1, its type, its request's id and its content's length, big-endian, and its padding's length; then its content and its
/// padding. A record's content holds at most 65,535 bytes; a stream's, <c>FCGI_PARAMS</c>' or <c>FCGI_STDOUT</c>'s, ends
/// with an empty one. Name-value pairs, as <c>FCGI_PARAMS</c> and <c>FCGI_GET_VALUES</c> hold them, have each length in
/// one byte below 128, else in four, its top bit set.
/// </summary>
public static class WebLingoFastCgiRecords
{
    /// <summary>The most a record's content holds.</summary>
    public const int MaxContent = 65_535;

    public const int Responder = 1;
    public const byte KeepConnection = 1;
    public const byte RequestComplete = 0;
    public const byte CannotMultiplex = 1;
    public const byte UnknownRole = 3;

    /// <summary>The next record, or null at the connection's end; a version other than 1, or a record cut short, an <see cref="InvalidDataException"/>.</summary>
    public static async Task<WebLingoFastCgiRecord?> ReadAsync(Stream stream, CancellationToken cancel)
    {
        var header = new byte[8];
        if (!await FillAsync(stream, header, cancel)) return null;
        if (header[0] != 1) throw new InvalidDataException($"a FastCGI record of version {header[0]}, not 1");
        var length = header[4] << 8 | header[5];
        var content = new byte[length];
        var padding = new byte[header[6]];
        if (!await FillAsync(stream, content, cancel) || !await FillAsync(stream, padding, cancel))
        {
            throw new InvalidDataException("a FastCGI record cut short");
        }
        return new WebLingoFastCgiRecord((WebLingoFastCgiType)header[1], header[2] << 8 | header[3], content);
    }

    /// <summary>Reads exactly enough to fill <paramref name="buffer"/>: false if the connection ends first, before a byte of it.</summary>
    private static async Task<bool> FillAsync(Stream stream, byte[] buffer, CancellationToken cancel)
    {
        var at = 0;
        while (at < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(at), cancel);
            if (read == 0)
            {
                if (at == 0) return false;
                throw new InvalidDataException("a FastCGI record cut short");
            }
            at += read;
        }
        return true;
    }

    /// <summary>A record, or, for content beyond <see cref="MaxContent"/>, several, each padded to 8 bytes.</summary>
    public static byte[] Make(WebLingoFastCgiType type, int requestId, ReadOnlySpan<byte> content)
    {
        var made = new MemoryStream(content.Length + 16);
        var at = 0;
        do
        {
            var length = Math.Min(MaxContent, content.Length - at);
            var padding = (8 - length % 8) % 8;
            made.Write([1, (byte)type, (byte)(requestId >> 8), (byte)requestId, (byte)(length >> 8), (byte)length, (byte)padding, 0]);
            made.Write(content.Slice(at, length));
            made.Write(new byte[padding]);
            at += length;
        }
        while (at < content.Length);
        return made.ToArray();
    }

    /// <summary><c>FCGI_END_REQUEST</c>: the application's status, 0, and the protocol's.</summary>
    public static byte[] EndRequest(int requestId, byte protocolStatus) =>
        Make(WebLingoFastCgiType.EndRequest, requestId, [0, 0, 0, 0, protocolStatus, 0, 0, 0]);

    /// <summary>Name-value pairs read from <paramref name="content"/>, in their order; an <see cref="InvalidDataException"/> for pairs cut short.</summary>
    public static List<KeyValuePair<string, string>> ReadPairs(ReadOnlySpan<byte> content)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        var at = 0;
        while (at < content.Length)
        {
            var nameLength = ReadLength(content, ref at);
            var valueLength = ReadLength(content, ref at);
            if (nameLength < 0 || valueLength < 0 || at + nameLength + valueLength > content.Length)
            {
                throw new InvalidDataException("FastCGI's name-value pairs cut short");
            }
            var name = System.Text.Encoding.UTF8.GetString(content.Slice(at, nameLength));
            var value = System.Text.Encoding.UTF8.GetString(content.Slice(at + nameLength, valueLength));
            pairs.Add(new(name, value));
            at += nameLength + valueLength;
        }
        return pairs;
    }

    private static int ReadLength(ReadOnlySpan<byte> content, ref int at)
    {
        if (at >= content.Length) return -1;
        if (content[at] < 0x80) return content[at++];
        if (at + 4 > content.Length) return -1;
        var length = (content[at] & 0x7F) << 24 | content[at + 1] << 16 | content[at + 2] << 8 | content[at + 3];
        at += 4;
        return length;
    }

    /// <summary>Name-value pairs written as FastCGI writes them.</summary>
    public static byte[] WritePairs(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var made = new MemoryStream();
        foreach (var (name, value) in pairs)
        {
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
            var valueBytes = System.Text.Encoding.UTF8.GetBytes(value);
            WriteLength(made, nameBytes.Length);
            WriteLength(made, valueBytes.Length);
            made.Write(nameBytes);
            made.Write(valueBytes);
        }
        return made.ToArray();
    }

    private static void WriteLength(Stream into, int length)
    {
        if (length < 0x80) into.WriteByte((byte)length);
        else into.Write([(byte)(0x80 | length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length]);
    }
}
