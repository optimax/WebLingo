using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Hosting;

/// <summary>
/// A form posted as <c>multipart/form-data</c> (RFC 7578; specification, section 6.12, "Uploaded files"), read as its
/// body arrives, piece by piece, off the worker: each part a field, its value text in UTF-8, or a file, a part whose
/// <c>Content-Disposition</c> names a file, kept in memory up to <see cref="_inMemory"/> bytes, then in a temporary file of
/// the server's, the files' bytes in all at most <see cref="_maxFiles"/>. A file input left empty, its file's name empty and
/// its content none, is no file. A name given twice is a field's last value, and a file's names each an array, in order.
/// Its errors are messages for a script, which it keeps, reading nothing more; whoever made it removes its temporary
/// files, <see cref="Dispose"/>, when its request ends.
/// </summary>
internal sealed class WebLingoMultipartReader : IDisposable
{
    /// <summary>The most a part's headers may take, so that a body of headers without end holds no memory.</summary>
    private const int MaxHeaders = 16 * 1024;

    private enum State { Preamble, AfterBoundary, Headers, Content, Done }

    private readonly byte[] _delimiter;
    private readonly int _inMemory;
    private readonly long _maxFiles;
    private readonly IWebLingoTemporaryFiles? _temporary;
    private byte[] _buffer = new byte[16 * 1024];
    private int _length;
    private State _state = State.Preamble;
    private Part? _part;
    private long _fileBytes;

    /// <summary>
    /// A reader of a form whose parts <paramref name="boundary"/> divides, a file kept in memory up to
    /// <paramref name="inMemory"/> bytes, the files' bytes at most <paramref name="maxFiles"/>.
    /// </summary>
    public WebLingoMultipartReader(string boundary, int inMemory, long maxFiles, IWebLingoTemporaryFiles? temporary)
    {
        _delimiter = WebLingoTextCodec.Encode("\r\n--" + boundary, WebLingoCharset.Utf8);
        (_inMemory, _maxFiles, _temporary) = (inMemory, maxFiles, temporary);
        // The body's first boundary, at its start, is read as any other, after a line break.
        _buffer[0] = (byte)'\r';
        _buffer[1] = (byte)'\n';
        _length = 2;
    }

    /// <summary>The form's fields, each a name and its last value.</summary>
    public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);

    /// <summary>The form's fields' names, in the order first given.</summary>
    public List<string> FieldNames { get; } = [];

    /// <summary>The form's files, each name's in the order given.</summary>
    public Dictionary<string, List<UploadedPart>> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>The files' names, in the order first given.</summary>
    public List<string> FileNames { get; } = [];

    /// <summary>Why the form cannot be read, a message for a script, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// The boundary a <c>multipart/form-data</c> <c>Content-Type</c> names, <c>boundary=----x</c>, or null where it names
    /// none, or one RFC 2046 does not allow.
    /// </summary>
    public static string? Boundary(string contentType)
    {
        foreach (var (name, value) in Parameters(contentType))
        {
            if (name.Equals("boundary", StringComparison.OrdinalIgnoreCase)) return value.Length is > 0 and <= 70 ? value : null;
        }
        return null;
    }

    /// <summary>Reads the body's next <paramref name="count"/> bytes: false, with <see cref="Error"/>, once it cannot.</summary>
    public bool Feed(byte[] piece, int count)
    {
        if (Error is not null) return false;
        if (_state == State.Done) return true; // What follows the last part, its epilogue, is nothing.
        if (_length + count > _buffer.Length) Array.Resize(ref _buffer, Math.Max(_length + count, _buffer.Length * 2));
        Array.Copy(piece, 0, _buffer, _length, count);
        _length += count;
        while (Error is null && Step())
        {
        }
        return Error is null;
    }

    /// <summary>The body's end: false, with <see cref="Error"/>, if its last part has not ended.</summary>
    public bool End()
    {
        if (Error is null && _state != State.Done) Fail(_state == State.Preamble
            ? "The form's body has no part its boundary begins."
            : "The form's body ended before its last part did.");
        return Error is null;
    }

    public void Dispose()
    {
        _part?.Dispose();
        foreach (var files in Files.Values)
        {
            foreach (var file in files) file.Dispose();
        }
    }

    /// <summary>One step on, as far as what has arrived allows: false when it needs more.</summary>
    private bool Step()
    {
        var held = _buffer.AsSpan(0, _length);
        switch (_state)
        {
            case State.Preamble:
            {
                var at = held.IndexOf(_delimiter);
                if (at < 0)
                {
                    Consume(Math.Max(0, _length - (_delimiter.Length - 1)));
                    return false;
                }
                Consume(at + _delimiter.Length);
                _state = State.AfterBoundary;
                return true;
            }
            case State.AfterBoundary:
            {
                // "--" ends the form; else a line break, after any spaces or tabs, begins a part's headers.
                if (_length < 2) return false;
                if (held[0] == (byte)'-' && held[1] == (byte)'-')
                {
                    _state = State.Done;
                    Consume(_length);
                    return false;
                }
                var skipped = 0;
                while (skipped < _length && held[skipped] is (byte)' ' or (byte)'\t') skipped++;
                if (_length - skipped < 2) return false;
                if (held[skipped] != (byte)'\r' || held[skipped + 1] != (byte)'\n') return Fail("The form's body is not multipart/form-data as its boundary says.");
                Consume(skipped + 2);
                _state = State.Headers;
                return true;
            }
            case State.Headers:
            {
                var at = held.IndexOf("\r\n\r\n"u8);
                var empty = _length >= 2 && held[0] == (byte)'\r' && held[1] == (byte)'\n';
                if (!empty && at < 0)
                {
                    return _length > MaxHeaders ? Fail("A part of the form has headers too long to read.") : false;
                }
                var text = empty ? "" : WebLingoTextCodec.Decode(held[..at].ToArray(), WebLingoCharset.Utf8);
                Consume(empty ? 2 : at + 4);
                if (!TryBegin(text)) return false;
                _state = State.Content;
                return true;
            }
            case State.Content:
            {
                var at = held.IndexOf(_delimiter);
                if (at < 0)
                {
                    var safe = _length - (_delimiter.Length - 1);
                    if (safe <= 0) return false;
                    if (!Append(held[..safe])) return false;
                    Consume(safe);
                    return false;
                }
                if (!Append(held[..at]) || !Finish()) return false;
                Consume(at + _delimiter.Length);
                _state = State.AfterBoundary;
                return true;
            }
            default:
                return false;
        }
    }

    private void Consume(int count)
    {
        Array.Copy(_buffer, count, _buffer, 0, _length - count);
        _length -= count;
    }

    private bool Fail(string message)
    {
        Error ??= message;
        return false;
    }

    /// <summary>A part's headers read: a field, or a file, by its <c>Content-Disposition</c>.</summary>
    private bool TryBegin(string headers)
    {
        string? disposition = null, type = null;
        foreach (var line in headers.Split("\r\n"))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            if (name.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase)) disposition = line[(colon + 1)..].Trim();
            else if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) type = line[(colon + 1)..].Trim();
        }
        var semicolon = disposition?.IndexOf(';', StringComparison.Ordinal) ?? -1;
        if (disposition is null || !(semicolon < 0 ? disposition : disposition[..semicolon]).Trim().Equals("form-data", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("A part of the form has no Content-Disposition of form-data.");
        }
        string? fieldName = null, fileName = null, encodedName = null;
        foreach (var (key, value) in Parameters(disposition))
        {
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase)) fieldName = value;
            else if (key.Equals("filename", StringComparison.OrdinalIgnoreCase)) fileName = value;
            else if (key.Equals("filename*", StringComparison.OrdinalIgnoreCase)) encodedName = Extended(value);
        }
        if (fieldName is null) return Fail("A part of the form has no name.");
        _part = new Part(fieldName, encodedName ?? fileName, type ?? "application/octet-stream");
        return true;
    }

    /// <summary>A part's next bytes: a field's held, a file's held in memory, or written to its temporary file past the threshold.</summary>
    private bool Append(ReadOnlySpan<byte> bytes)
    {
        var part = _part!;
        if (bytes.Length == 0) return true;
        if (part.FileName is null)
        {
            part.Memory.Write(bytes);
            return true;
        }
        _fileBytes += bytes.Length;
        if (_fileBytes > _maxFiles) return Fail($"The form's files are larger than the {_maxFiles} bytes this site takes.");
        if (part.File is null && part.Memory.Length + bytes.Length <= _inMemory)
        {
            part.Memory.Write(bytes);
            return true;
        }
        try
        {
            if (part.File is null)
            {
                if (_temporary is null) return Fail("This host keeps no temporary files, which a file this large needs.");
                part.File = _temporary.Create();
                part.File.Write(part.Memory.Buffer, 0, part.Memory.Length);
                part.Memory = new Bytes();
            }
            var copy = bytes.ToArray();
            part.File.Write(copy, 0, copy.Length);
            return true;
        }
        catch (WebLingoFileSystemException failure)
        {
            return Fail($"A file of the form cannot be kept: {failure.Message}.");
        }
    }

    /// <summary>A part's end: a field's value, or a file, kept, but a file input left empty.</summary>
    private bool Finish()
    {
        var part = _part!;
        _part = null;
        if (part.FileName is null)
        {
            if (!Fields.ContainsKey(part.Name)) FieldNames.Add(part.Name);
            Fields[part.Name] = WebLingoTextCodec.Decode(part.Memory.Buffer.AsSpan(0, part.Memory.Length).ToArray(), WebLingoCharset.Utf8);
            return true;
        }
        if (part.FileName.Length == 0 && part.File is null && part.Memory.Length == 0) return true; // No file chosen.
        var uploaded = part.File is { } file
            ? new UploadedPart(part.Name, part.FileName, part.Type, file.Length, null, file)
            : new UploadedPart(part.Name, part.FileName, part.Type, part.Memory.Length, part.Memory.Buffer.AsSpan(0, part.Memory.Length).ToArray(), null);
        if (!Files.TryGetValue(part.Name, out var named))
        {
            Files[part.Name] = named = [];
            FileNames.Add(part.Name);
        }
        named.Add(uploaded);
        return true;
    }

    /// <summary>
    /// A header's parameters after its first <c>;</c>, each a name and its value, a quoted one unquoted, its backslashes'
    /// escapes undone: <c>form-data; name="photo"; filename="a b.png"</c>'s <c>name</c> and <c>filename</c>.
    /// </summary>
    private static List<(string Name, string Value)> Parameters(string header)
    {
        var found = new List<(string, string)>();
        var at = header.IndexOf(';', StringComparison.Ordinal);
        while (at >= 0 && at < header.Length)
        {
            at++;
            while (at < header.Length && header[at] is ' ' or '\t') at++;
            var equals = header.IndexOf('=', at);
            if (equals < 0) break;
            var name = header[at..equals].Trim();
            at = equals + 1;
            while (at < header.Length && header[at] is ' ' or '\t') at++;
            var value = new System.Text.StringBuilder();
            if (at < header.Length && header[at] == '"')
            {
                at++;
                while (at < header.Length && header[at] != '"')
                {
                    if (header[at] == '\\' && at + 1 < header.Length) at++;
                    value.Append(header[at++]);
                }
                at = header.IndexOf(';', Math.Min(at + 1, header.Length));
            }
            else
            {
                var end = header.IndexOf(';', at);
                value.Append(header[at..(end < 0 ? header.Length : end)].Trim());
                at = end;
            }
            found.Add((name, value.ToString()));
            if (at < 0) break;
        }
        return found;
    }

    /// <summary>An extended parameter's value (RFC 8187), <c>utf-8''r%C3%A9sum%C3%A9.pdf</c>, decoded; null for another charset.</summary>
    private static string? Extended(string value)
    {
        var first = value.IndexOf('\'', StringComparison.Ordinal);
        var second = first < 0 ? -1 : value.IndexOf('\'', first + 1);
        if (second < 0 || !value[..first].Equals("utf-8", StringComparison.OrdinalIgnoreCase)) return null;
        var encoded = value[(second + 1)..];
        var bytes = new List<byte>(encoded.Length);
        for (var i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] == '%' && i + 2 < encoded.Length && IsHex(encoded[i + 1]) && IsHex(encoded[i + 2]))
            {
                bytes.Add((byte)(Hex(encoded[i + 1]) * 16 + Hex(encoded[i + 2])));
                i += 2;
            }
            else if (encoded[i] < 0x80)
            {
                bytes.Add((byte)encoded[i]);
            }
            else
            {
                return null;
            }
        }
        return WebLingoTextCodec.Decode([.. bytes], WebLingoCharset.Utf8);
    }

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int Hex(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    /// <summary>A growing array of bytes, a part's in memory.</summary>
    private sealed class Bytes
    {
        public byte[] Buffer { get; private set; } = [];
        public int Length { get; private set; }

        public void Write(ReadOnlySpan<byte> bytes)
        {
            if (Length + bytes.Length > Buffer.Length)
            {
                var grown = new byte[Math.Max(Length + bytes.Length, Math.Max(256, Buffer.Length * 2))];
                Array.Copy(Buffer, grown, Length);
                Buffer = grown;
            }
            bytes.CopyTo(Buffer.AsSpan(Length));
            Length += bytes.Length;
        }
    }

    /// <summary>The part being read: its name, its file's name, for a file, its type, and its content so far.</summary>
    private sealed class Part(string name, string? fileName, string type) : IDisposable
    {
        public string Name { get; } = name;
        public string? FileName { get; } = fileName;
        public string Type { get; } = type;
        public Bytes Memory { get; set; } = new();
        public IWebLingoTemporaryFile? File { get; set; }

        public void Dispose() => File?.Dispose();
    }
}

/// <summary>
/// A file a form uploaded: its input's name, its file's name as the browser gave it, never a path, its type, its size in
/// bytes, and its content, in memory, or in a temporary file, removed with it.
/// </summary>
internal sealed record UploadedPart(string Name, string FileName, string Type, long Size, byte[]? Bytes, IWebLingoTemporaryFile? File)
    : IDisposable
{
    public void Dispose() => File?.Dispose();
}
