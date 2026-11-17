namespace WebLingo.Runtime;

/// <summary>
/// Bytes as text, and text as bytes again (specification, section 3.2, "Bytes"): hexadecimal, two lower-case digits for
/// each byte, read in either case; and Base64, RFC 4648's, its standard alphabet with its padding, or its URL-safe one,
/// <c>-</c> and <c>_</c> for <c>+</c> and <c>/</c>, without the padding, <c>=</c>, which a URL would have to escape, read
/// with or without it. Written here rather than by .NET's <c>Convert</c>, which the portable core does not use, so that
/// every toolchain gives the same text, and refuses the same.
/// </summary>
public static class WebLingoBinaryText
{
    private const string HexDigits = "0123456789abcdef";
    private const string StandardAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    private const string UrlSafeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <summary><paramref name="bytes"/> as hexadecimal, two lower-case digits for each byte.</summary>
    public static string ToHex(ReadOnlySpan<byte> bytes)
    {
        var text = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            text[2 * i] = HexDigits[bytes[i] >> 4];
            text[2 * i + 1] = HexDigits[bytes[i] & 15];
        }
        return new string(text);
    }

    /// <summary>Hexadecimal as bytes: two digits for each byte, in either case, and nothing else; <c>""</c> no bytes.</summary>
    public static bool TryFromHex(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length % 2 != 0) return false;
        var made = new byte[text.Length / 2];
        for (var i = 0; i < made.Length; i++)
        {
            var (high, low) = (HexDigit(text[2 * i]), HexDigit(text[2 * i + 1]));
            if (high < 0 || low < 0) return false;
            made[i] = (byte)(high << 4 | low);
        }
        bytes = made;
        return true;
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    /// <summary>
    /// <paramref name="bytes"/> in Base64: the standard alphabet, padded with <c>=</c> to a multiple of four characters; or,
    /// <paramref name="urlSafe"/>, the URL-safe one, unpadded.
    /// </summary>
    public static string ToBase64(ReadOnlySpan<byte> bytes, bool urlSafe = false)
    {
        var alphabet = urlSafe ? UrlSafeAlphabet : StandardAlphabet;
        var (whole, rest) = (bytes.Length / 3, bytes.Length % 3);
        var text = new char[whole * 4 + (rest == 0 ? 0 : urlSafe ? rest + 1 : 4)];
        var at = 0;
        for (var i = 0; i < whole * 3; i += 3)
        {
            var chunk = bytes[i] << 16 | bytes[i + 1] << 8 | bytes[i + 2];
            for (var shift = 18; shift >= 0; shift -= 6) text[at++] = alphabet[chunk >> shift & 63];
        }
        if (rest > 0)
        {
            var chunk = bytes[whole * 3] << 16 | (rest == 2 ? bytes[whole * 3 + 1] << 8 : 0);
            for (var shift = 18; shift >= 18 - 6 * rest; shift -= 6) text[at++] = alphabet[chunk >> shift & 63];
            while (at < text.Length) text[at++] = '=';
        }
        return new string(text);
    }

    /// <summary>
    /// Base64 as bytes: the standard alphabet, its padding required, its length a multiple of four; or,
    /// <paramref name="urlSafe"/>, the URL-safe alphabet, padded or not. Nothing else, white space included; <c>""</c> no
    /// bytes.
    /// </summary>
    public static bool TryFromBase64(string text, bool urlSafe, out byte[] bytes)
    {
        bytes = [];
        var (length, padding) = (text.Length, 0);
        while (padding < 2 && length > 0 && text[length - 1] == '=')
        {
            length--;
            padding++;
        }
        if ((!urlSafe || padding > 0) && text.Length % 4 != 0) return false;
        if (length % 4 == 1 || (padding > 0 && padding != 4 - length % 4)) return false;
        var alphabet = urlSafe ? UrlSafeAlphabet : StandardAlphabet;
        var made = new byte[length / 4 * 3 + (length % 4 == 0 ? 0 : length % 4 - 1)];
        var (at, chunk, bits) = (0, 0, 0);
        for (var i = 0; i < length; i++)
        {
            var value = alphabet.IndexOf(text[i], StringComparison.Ordinal);
            if (value < 0) return false;
            chunk = chunk << 6 | value;
            bits += 6;
            if (bits < 8) continue;
            bits -= 8;
            made[at++] = (byte)(chunk >> bits);
            chunk &= (1 << bits) - 1;
        }
        bytes = made;
        return true;
    }
}
