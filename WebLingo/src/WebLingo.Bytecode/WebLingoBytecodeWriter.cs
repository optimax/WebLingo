namespace WebLingo.Bytecode;

/// <summary>
/// The bytes of a WLBC file or of one of its sections, as they are written (specification, section 5.3, "As built";
/// section 15.2, "WLBC's layout"): fixed-size fields little-endian, counts and lengths as unsigned LEB128, every other
/// integer as signed LEB128, and a string as its UTF-8 bytes' count and the bytes, a lone surrogate as its three bytes
/// (WTF-8), so that every string reads back as it was.
/// </summary>
internal sealed class WebLingoBytecodeWriter(int capacity = 256)
{
    private byte[] _bytes = new byte[Math.Max(capacity, 16)];

    public int Length { get; private set; }

    /// <summary>The bytes written, a copy of exactly <see cref="Length"/> of them.</summary>
    public byte[] ToArray()
    {
        var bytes = new byte[Length];
        Array.Copy(_bytes, bytes, Length);
        return bytes;
    }

    /// <summary>Copies the bytes written into <paramref name="target"/> at <paramref name="at"/>.</summary>
    public void CopyTo(byte[] target, int at) => Array.Copy(_bytes, 0, target, at, Length);

    /// <summary>The CRC-32 of the bytes written.</summary>
    public uint Crc32() => WebLingoCrc32.Compute(_bytes, 0, Length);

    public void Byte(int value)
    {
        Ensure(1);
        _bytes[Length++] = (byte)value;
    }

    public void Boolean(bool value) => Byte(value ? 1 : 0);

    /// <summary>A fixed-size field of <paramref name="size"/> bytes, little-endian.</summary>
    public void Fixed(ulong value, int size)
    {
        Ensure(size);
        for (var i = 0; i < size; i++) _bytes[Length++] = (byte)(value >> (8 * i));
    }

    public void Unsigned(ulong value)
    {
        while (value >= 0x80)
        {
            Byte((int)(value & 0x7F) | 0x80);
            value >>= 7;
        }
        Byte((int)value);
    }

    public void Count(int count) => Unsigned((ulong)count);

    public void Signed(long value)
    {
        while (true)
        {
            var low = (int)(value & 0x7F);
            value >>= 7;
            if ((value == 0 && (low & 0x40) == 0) || (value == -1 && (low & 0x40) != 0))
            {
                Byte(low);
                return;
            }
            Byte(low | 0x80);
        }
    }

    /// <summary>A string: its UTF-8 bytes' count, then the bytes; a surrogate without its pair as its three bytes (WTF-8).</summary>
    public void Text(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var code = CodeAt(text, ref i);
            count += code < 0x80 ? 1 : code < 0x800 ? 2 : code < 0x10000 ? 3 : 4;
        }
        Count(count);
        Ensure(count);
        for (var i = 0; i < text.Length; i++)
        {
            var code = CodeAt(text, ref i);
            if (code < 0x80)
            {
                _bytes[Length++] = (byte)code;
            }
            else if (code < 0x800)
            {
                _bytes[Length++] = (byte)(0xC0 | (code >> 6));
                _bytes[Length++] = (byte)(0x80 | (code & 0x3F));
            }
            else if (code < 0x10000)
            {
                _bytes[Length++] = (byte)(0xE0 | (code >> 12));
                _bytes[Length++] = (byte)(0x80 | ((code >> 6) & 0x3F));
                _bytes[Length++] = (byte)(0x80 | (code & 0x3F));
            }
            else
            {
                _bytes[Length++] = (byte)(0xF0 | (code >> 18));
                _bytes[Length++] = (byte)(0x80 | ((code >> 12) & 0x3F));
                _bytes[Length++] = (byte)(0x80 | ((code >> 6) & 0x3F));
                _bytes[Length++] = (byte)(0x80 | (code & 0x3F));
            }
        }
    }

    /// <summary>The code point at <paramref name="i"/>, a surrogate pair's whole (<paramref name="i"/> then at its second half).</summary>
    private static int CodeAt(string text, ref int i)
    {
        int code = text[i];
        if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
        {
            code = 0x10000 + ((code - 0xD800) << 10) + (text[i + 1] - 0xDC00);
            i++;
        }
        return code;
    }

    private void Ensure(int more)
    {
        if (Length + more > _bytes.Length) Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, Length + more));
    }
}
