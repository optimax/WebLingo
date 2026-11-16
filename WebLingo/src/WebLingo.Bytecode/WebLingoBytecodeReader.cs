namespace WebLingo.Bytecode;

/// <summary>
/// Reads one section of a WLBC file, as <see cref="WebLingoBytecodeWriter"/> writes it, from <paramref name="start"/> up
/// to <paramref name="end"/>. It never throws: the first thing wrong is kept as <see cref="Error"/>, and from then on
/// every read gives 0, <c>false</c> or an empty string, every count 0, so that whatever reads it stops at once, having
/// allocated nothing it was not given the bytes for.
/// </summary>
internal sealed class WebLingoBytecodeReader(byte[] bytes, int start, int end, string section)
{
    private int _at = start;

    /// <summary>Why the section is damaged, or null.</summary>
    public string? Error { get; private set; }

    public bool Failed => Error is not null;

    /// <summary>How many bytes are left of the section.</summary>
    public int Left => end - _at;

    /// <summary>Fails unless every byte of the section has been read.</summary>
    public void End()
    {
        if (!Failed && _at != end) Fail("has bytes left over");
    }

    public void Fail(string why)
    {
        Error ??= $"The file is damaged: its {section} section {why}.";
        _at = end;
    }

    public int Byte()
    {
        if (_at >= end)
        {
            Fail("ends too soon");
            return 0;
        }
        return bytes[_at++];
    }

    public bool Boolean()
    {
        var value = Byte();
        if (value > 1) Fail("has a Boolean that is neither 0 nor 1");
        return value == 1;
    }

    /// <summary>A byte of flags, failing if one is set that <paramref name="known"/> does not have.</summary>
    public int Flags(int known)
    {
        var value = Byte();
        if ((value & ~known) != 0) Fail("has an unknown flag");
        return Failed ? 0 : value;
    }

    public ulong Unsigned()
    {
        ulong value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var next = Byte();
            if (Failed) return 0;
            if (shift == 63 && (next & 0x7E) != 0)
            {
                Fail("has a number that does not fit");
                return 0;
            }
            value |= (ulong)(next & 0x7F) << shift;
            if ((next & 0x80) == 0) return value;
            if (shift == 63)
            {
                Fail("has a number that does not fit");
                return 0;
            }
        }
    }

    public long Signed()
    {
        long value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var next = Byte();
            if (Failed) return 0;
            if (shift == 63)
            {
                // The tenth byte holds the last bit and the sign: all clear or all set, and the number ends there.
                if (next is not (0x00 or 0x7F))
                {
                    Fail("has a number that does not fit");
                    return 0;
                }
                return value | ((long)(next & 1) << 63);
            }
            value |= (long)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
            {
                if ((next & 0x40) != 0 && shift + 7 < 64) value |= -1L << (shift + 7);
                return value;
            }
        }
    }

    /// <summary>A signed integer that an <c>int</c> holds.</summary>
    public int Int()
    {
        var value = Signed();
        if (value is < int.MinValue or > int.MaxValue)
        {
            Fail("has a number that does not fit");
            return 0;
        }
        return (int)value;
    }

    /// <summary>A count of things that take a byte or more each in this section: no more than the bytes left.</summary>
    public int Count()
    {
        var value = Unsigned();
        if (value > (ulong)Left)
        {
            Fail("has a count larger than what is left of it");
            return 0;
        }
        return (int)value;
    }

    /// <summary>A count of things in another section, which the reader of that section checks: no more than an <c>int</c> holds.</summary>
    public int Number()
    {
        var value = Unsigned();
        if (value > int.MaxValue)
        {
            Fail("has a number that does not fit");
            return 0;
        }
        return (int)value;
    }

    /// <summary>A fixed-size 64-bit field, little-endian.</summary>
    public long Fixed64()
    {
        if (Left < 8)
        {
            Fail("ends too soon");
            return 0;
        }
        ulong value = 0;
        for (var i = 0; i < 8; i++) value |= (ulong)bytes[_at + i] << (8 * i);
        _at += 8;
        return (long)value;
    }

    /// <summary>A string, its UTF-8 bytes' count, then the bytes; a surrogate without its pair as its three bytes (WTF-8).</summary>
    public string Text()
    {
        var count = Count();
        if (Failed || count == 0) return "";
        var chars = new char[count];
        var length = 0;
        var (at, stop) = (_at, _at + count);
        var afterHigh = false;
        while (at < stop)
        {
            var first = bytes[at];
            int code, size;
            if (first < 0x80) (code, size) = (first, 1);
            else if (first is >= 0xC2 and <= 0xDF) (code, size) = (first & 0x1F, 2);
            else if (first is >= 0xE0 and <= 0xEF) (code, size) = (first & 0x0F, 3);
            else if (first is >= 0xF0 and <= 0xF4) (code, size) = (first & 0x07, 4);
            else return NotUtf8();
            if (at + size > stop) return NotUtf8();
            for (var i = 1; i < size; i++)
            {
                if ((bytes[at + i] & 0xC0) != 0x80) return NotUtf8();
                code = (code << 6) | (bytes[at + i] & 0x3F);
            }
            // Overlong forms, and code points beyond Unicode's, are not UTF-8.
            if ((size == 3 && code < 0x800) || (size == 4 && (code < 0x10000 || code > 0x10FFFF))) return NotUtf8();
            if (code is >= 0xD800 and <= 0xDFFF)
            {
                // A surrogate stands alone: a low one just after a high one would have been their pair's four bytes.
                if (code >= 0xDC00 && afterHigh) return NotUtf8();
                chars[length++] = (char)code;
                afterHigh = code < 0xDC00;
            }
            else if (code >= 0x10000)
            {
                chars[length++] = (char)(0xD800 + ((code - 0x10000) >> 10));
                chars[length++] = (char)(0xDC00 + ((code - 0x10000) & 0x3FF));
                afterHigh = false;
            }
            else
            {
                chars[length++] = (char)code;
                afterHigh = false;
            }
            at += size;
        }
        _at = stop;
        return new string(chars, 0, length);
    }

    private string NotUtf8()
    {
        Fail("has a string that is not UTF-8");
        return "";
    }
}
