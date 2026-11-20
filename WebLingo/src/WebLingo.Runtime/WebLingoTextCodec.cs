namespace WebLingo.Runtime;

/// <summary>A text encoding the portable core reads and writes itself (<see cref="WebLingoTextCodec"/>).</summary>
public enum WebLingoCharset { Utf8, Utf16LittleEndian, Utf16BigEndian, Ascii, Latin1 }

/// <summary>
/// The text encodings the portable core reads and writes itself, rather than with .NET's <c>System.Text.Encoding</c>,
/// which is adapter-only (<c>Documents/native-compatibility.md</c>): UTF-8, UTF-16, little-endian or big-endian, US-ASCII
/// and ISO-8859-1. A charset is found by the names the web gives it, without regard to case: <c>utf-8</c> and
/// <c>utf8</c>; <c>utf-16</c>, <c>utf-16le</c> and <c>utf16</c>; <c>utf-16be</c>; <c>us-ascii</c> and <c>ascii</c>;
/// <c>iso-8859-1</c>, <c>latin1</c> and <c>l1</c>. Decoding never fails: what is no character, a byte that UTF-8 or
/// US-ASCII cannot begin or go on with, or UTF-16's lone surrogate or odd last byte, is U+FFFD, each maximal piece of a
/// broken sequence one U+FFFD, as the web decodes; a byte order mark that begins UTF-16 named <c>utf-16</c> says which way
/// it is, and is dropped, and one that begins UTF-8 is dropped too. Encoding never fails: a character US-ASCII or
/// ISO-8859-1 cannot hold is <c>?</c>, and a lone surrogate U+FFFD.
/// </summary>
public static class WebLingoTextCodec
{
    private const char Replacement = '\uFFFD';

    /// <summary>The charset of a name, or false for one the core does not know.</summary>
    public static bool TryFind(string name, out WebLingoCharset charset)
    {
        var lower = new char[name.Length];
        for (var i = 0; i < name.Length; i++) lower[i] = name[i] is >= 'A' and <= 'Z' ? (char)(name[i] + ('a' - 'A')) : name[i];
        (var found, charset) = new string(lower) switch
        {
            "utf-8" or "utf8" => (true, WebLingoCharset.Utf8),
            "utf-16" or "utf-16le" or "utf16" => (true, WebLingoCharset.Utf16LittleEndian),
            "utf-16be" => (true, WebLingoCharset.Utf16BigEndian),
            "us-ascii" or "ascii" => (true, WebLingoCharset.Ascii),
            "iso-8859-1" or "latin1" or "l1" => (true, WebLingoCharset.Latin1),
            _ => (false, WebLingoCharset.Utf8)
        };
        return found;
    }

    /// <summary><paramref name="bytes"/> as text in <paramref name="charset"/>.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes, WebLingoCharset charset)
    {
        switch (charset)
        {
            case WebLingoCharset.Utf8:
                return DecodeUtf8(bytes);
            case WebLingoCharset.Ascii or WebLingoCharset.Latin1:
            {
                var chars = new char[bytes.Length];
                for (var i = 0; i < bytes.Length; i++) chars[i] = charset == WebLingoCharset.Ascii && bytes[i] > 0x7F ? Replacement : (char)bytes[i];
                return new string(chars);
            }
            default:
                return DecodeUtf16(bytes, charset == WebLingoCharset.Utf16BigEndian);
        }
    }

    /// <summary><paramref name="text"/> as bytes in <paramref name="charset"/>, without a byte order mark.</summary>
    public static byte[] Encode(string text, WebLingoCharset charset)
    {
        switch (charset)
        {
            case WebLingoCharset.Utf8:
                return EncodeUtf8(text);
            case WebLingoCharset.Ascii or WebLingoCharset.Latin1:
            {
                var limit = charset == WebLingoCharset.Ascii ? 0x7F : 0xFF;
                var bytes = new List<byte>(text.Length);
                for (var i = 0; i < text.Length; i++)
                {
                    // A pair of surrogates is one character, which neither can hold.
                    if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                    bytes.Add(text[i] <= limit ? (byte)text[i] : (byte)'?');
                }
                return [.. bytes];
            }
            default:
            {
                var big = charset == WebLingoCharset.Utf16BigEndian;
                var bytes = new byte[2 * text.Length];
                for (var i = 0; i < text.Length; i++)
                {
                    var c = text[i];
                    var paired = char.IsHighSurrogate(c) ? i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                        : !char.IsLowSurrogate(c) || (i > 0 && char.IsHighSurrogate(text[i - 1]));
                    if (!paired) c = Replacement;
                    bytes[2 * i + (big ? 1 : 0)] = (byte)(c & 0xFF);
                    bytes[2 * i + (big ? 0 : 1)] = (byte)(c >> 8);
                }
                return bytes;
            }
        }
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        var text = new System.Text.StringBuilder(bytes.Length);
        var at = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        while (at < bytes.Length)
        {
            int b = bytes[at];
            if (b < 0x80)
            {
                text.Append((char)b);
                at++;
                continue;
            }
            // How many bytes follow, and the range the second may take, so that no overlong form, surrogate or value beyond
            // U+10FFFF is decoded.
            var (follow, low, high, code) = b switch
            {
                >= 0xC2 and <= 0xDF => (1, 0x80, 0xBF, b & 0x1F),
                0xE0 => (2, 0xA0, 0xBF, b & 0x0F),
                >= 0xE1 and <= 0xEC or 0xEE or 0xEF => (2, 0x80, 0xBF, b & 0x0F),
                0xED => (2, 0x80, 0x9F, b & 0x0F),
                0xF0 => (3, 0x90, 0xBF, b & 0x07),
                >= 0xF1 and <= 0xF3 => (3, 0x80, 0xBF, b & 0x07),
                0xF4 => (3, 0x80, 0x8F, b & 0x07),
                _ => (0, 0, 0, 0)
            };
            at++;
            if (follow == 0)
            {
                text.Append(Replacement);
                continue;
            }
            var complete = true;
            for (var i = 0; i < follow; i++)
            {
                if (at >= bytes.Length || bytes[at] < (i == 0 ? low : 0x80) || bytes[at] > (i == 0 ? high : 0xBF))
                {
                    complete = false;
                    break;
                }
                code = (code << 6) | (bytes[at] & 0x3F);
                at++;
            }
            if (!complete) text.Append(Replacement);
            else if (code < 0x10000) text.Append((char)code);
            else text.Append((char)(0xD800 + ((code - 0x10000) >> 10))).Append((char)(0xDC00 + ((code - 0x10000) & 0x3FF)));
        }
        return text.ToString();
    }

    private static string DecodeUtf16(ReadOnlySpan<byte> bytes, bool big)
    {
        var at = 0;
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            big = bytes[0] == 0xFE;
            at = 2;
        }
        var units = new char[(bytes.Length - at + 1) / 2];
        var count = 0;
        for (; at + 1 < bytes.Length; at += 2) units[count++] = (char)(big ? (bytes[at] << 8) | bytes[at + 1] : bytes[at] | (bytes[at + 1] << 8));
        if (at < bytes.Length) units[count++] = Replacement;
        // A surrogate without its pair is no character.
        for (var i = 0; i < count; i++)
        {
            if (char.IsHighSurrogate(units[i]) && i + 1 < count && char.IsLowSurrogate(units[i + 1])) i++;
            else if (char.IsSurrogate(units[i])) units[i] = Replacement;
        }
        return new string(units, 0, count);
    }

    private static byte[] EncodeUtf8(string text)
    {
        var bytes = new List<byte>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            int code = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                code = 0x10000 + ((code - 0xD800) << 10) + (text[++i] - 0xDC00);
            }
            else if (char.IsSurrogate(text[i]))
            {
                code = Replacement;
            }
            if (code < 0x80)
            {
                bytes.Add((byte)code);
            }
            else if (code < 0x800)
            {
                bytes.Add((byte)(0xC0 | (code >> 6)));
                bytes.Add((byte)(0x80 | (code & 0x3F)));
            }
            else if (code < 0x10000)
            {
                bytes.Add((byte)(0xE0 | (code >> 12)));
                bytes.Add((byte)(0x80 | ((code >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (code & 0x3F)));
            }
            else
            {
                bytes.Add((byte)(0xF0 | (code >> 18)));
                bytes.Add((byte)(0x80 | ((code >> 12) & 0x3F)));
                bytes.Add((byte)(0x80 | ((code >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (code & 0x3F)));
            }
        }
        return [.. bytes];
    }
}
