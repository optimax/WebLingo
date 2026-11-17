using System.Text;
using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// A value's text escaped for where it stands in a page's HTML (specification, section 13, "Context-aware escaping"), as Go's
/// <c>html/template</c> escapes an action's: its own language's escaping (<see cref="WebLingoHtmlEscape"/>), then its
/// attribute's (<see cref="WebLingoHtmlQuoting"/>). A value that cannot stand somewhere safely, a URL whose scheme could run
/// a script, a style that could, an attribute's name that is not a plain attribute's, is replaced there by
/// <see cref="Unsafe"/>, as Go replaces it by <c>ZgotmplZ</c>. <c>Html</c>'s members, <c>Html.url(x)</c> and the like,
/// escape with the same methods. Text is UTF-16, as .NET's strings are; a URL is percent-encoded as UTF-8.
/// </summary>
public static class WebLingoHtmlEscaping
{
    /// <summary>What stands in place of a value that cannot stand somewhere safely.</summary>
    public const string Unsafe = "zWebLingoz";

    public static string Escape(string text, WebLingoHtmlEscape escape, WebLingoHtmlQuoting quoting)
    {
        var escaped = escape switch
        {
            WebLingoHtmlEscape.Text => Html(text),
            WebLingoHtmlEscape.AttributeName => AttributeName(text),
            WebLingoHtmlEscape.Url => NormalizeUrl(FilterUrl(text)),
            WebLingoHtmlEscape.UrlNormalized => NormalizeUrl(text),
            WebLingoHtmlEscape.UrlPart => EscapeUrl(text),
            WebLingoHtmlEscape.Srcset => Srcset(text),
            WebLingoHtmlEscape.ScriptString => ScriptString(text, ScriptKind.String),
            WebLingoHtmlEscape.ScriptTemplate => ScriptString(text, ScriptKind.Template),
            WebLingoHtmlEscape.ScriptRegex => ScriptString(text, ScriptKind.Regex),
            WebLingoHtmlEscape.Style => FilterStyle(text),
            WebLingoHtmlEscape.StyleString => StyleString(FilterUrl(text)),
            WebLingoHtmlEscape.StyleStringPart => StyleString(text),
            WebLingoHtmlEscape.Omitted => null,
            _ => text // Plain, and Script, whose text Html.js has made.
        };
        if (escaped is null) return "";
        return quoting switch
        {
            WebLingoHtmlQuoting.Quoted => Html(escaped),
            WebLingoHtmlQuoting.Unquoted => Unquoted(escaped),
            _ => escaped
        };
    }

    /// <summary>
    /// Text as HTML's text and a quoted attribute's value both take it (section 13, "Contracts", approved 13 November
    /// 2026): <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and <c>'</c> as <c>&amp;amp;</c>, <c>&amp;lt;</c>,
    /// <c>&amp;gt;</c>, <c>&amp;quot;</c> and <c>&amp;#39;</c>.
    /// </summary>
    public static string Html(string text)
    {
        StringBuilder? escaped = null;
        var from = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var entity = text[i] switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", '\'' => "&#39;", _ => null };
            if (entity is null) continue;
            escaped ??= new StringBuilder(text.Length + 16);
            escaped.Append(text, from, i - from).Append(entity);
            from = i + 1;
        }
        return escaped is null ? text : escaped.Append(text, from, text.Length - from).ToString();
    }

    /// <summary>
    /// Text as an unquoted attribute's value takes it: as <see cref="Html"/>, and what else ends or upsets such a value,
    /// spaces, <c>=</c> and <c>`</c>, as numeric entities; an empty value, which would leave the attribute's value to
    /// what follows it, <see cref="Unsafe"/>.
    /// </summary>
    public static string Unquoted(string text) => text.Length == 0 ? Unsafe : Replace(text, static c => c switch
    {
        '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", '\'' => "&#39;", '\0' => "&#xfffd;", '\t' => "&#9;",
        '\n' => "&#10;", '\v' => "&#11;", '\f' => "&#12;", '\r' => "&#13;", ' ' => "&#32;", '=' => "&#61;", '`' => "&#96;",
        '+' => "&#43;", _ => null
    });

    /// <summary>
    /// A value where an attribute's name stands: the name in lower case, if it is a plain attribute's (not an event's, a
    /// URL's, a style's or one that changes what a page does) of letters, digits and <c>-</c>; <see cref="Unsafe"/> else.
    /// </summary>
    public static string AttributeName(string text)
    {
        var name = text.ToLowerInvariant();
        if (name.Length == 0 || WebLingoHtmlAttributes.KindOf(name) != WebLingoHtmlAttributeKind.Plain) return Unsafe;
        foreach (var c in name)
        {
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) return Unsafe;
        }
        return name;
    }

    /// <summary>
    /// A URL whose scheme, the text before its first <c>:</c> if no <c>/</c>, <c>?</c> or <c>#</c> comes before it, is not
    /// <c>http</c>, <c>https</c> or <c>mailto</c>, in any case, replaced by <c>#zWebLingoz</c>, so that it cannot run a
    /// script (<c>javascript:</c>) or carry a page (<c>data:</c>); any other as it is.
    /// </summary>
    public static string FilterUrl(string text) => IsSafeUrl(text) ? text : "#" + Unsafe;

    private static bool IsSafeUrl(string text)
    {
        var colon = text.IndexOf(':');
        if (colon < 0 || text.IndexOfAny(['/', '?', '#'], 0, colon) >= 0) return true;
        var scheme = text[..colon].ToLowerInvariant();
        return scheme is "http" or "https" or "mailto";
    }

    /// <summary>
    /// A URL with every character that a URL does not hold as it stands percent-encoded, as UTF-8, its reserved characters
    /// (<c>!#$&amp;*+,/:;=?@[]</c>) and its escapes (<c>%</c> and two hexadecimal digits) kept; <c>'</c>, <c>(</c> and
    /// <c>)</c> encoded too, so that it stands in a quoted attribute or in CSS's <c>url(…)</c>.
    /// </summary>
    public static string NormalizeUrl(string text) => ProcessUrl(text, true);

    /// <summary>A part of a URL, its query's or its fragment's: every character but letters, digits and <c>-._~</c> percent-encoded, as UTF-8.</summary>
    public static string EscapeUrl(string text) => ProcessUrl(text, false);

    private static string ProcessUrl(string text, bool normalize)
    {
        StringBuilder? built = null;
        var written = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '.' or '_' or '~') continue;
            if (normalize && c is '!' or '#' or '$' or '&' or '*' or '+' or ',' or '/' or ':' or ';' or '=' or '?' or '@' or '[' or ']') continue;
            if (normalize && c == '%' && i + 2 < text.Length && IsHex(text[i + 1]) && IsHex(text[i + 2])) continue;
            built ??= new StringBuilder(text.Length + 16);
            built.Append(text, written, i - written);
            var scalar = (int)c;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                scalar = char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                scalar = 0xFFFD;
            }
            AppendUtf8(built, scalar);
            written = i + 1;
        }
        return built is null ? text : built.Append(text, written, text.Length - written).ToString();
    }

    /// <summary>A Unicode scalar value as UTF-8, each byte percent-encoded.</summary>
    private static void AppendUtf8(StringBuilder built, int scalar)
    {
        Span<byte> bytes = stackalloc byte[4];
        var count = scalar switch
        {
            < 0x80 => Write(bytes, (byte)scalar),
            < 0x800 => Write(bytes, (byte)(0xC0 | (scalar >> 6)), (byte)(0x80 | (scalar & 0x3F))),
            < 0x10000 => Write(bytes, (byte)(0xE0 | (scalar >> 12)), (byte)(0x80 | ((scalar >> 6) & 0x3F)), (byte)(0x80 | (scalar & 0x3F))),
            _ => Write(bytes, (byte)(0xF0 | (scalar >> 18)), (byte)(0x80 | ((scalar >> 12) & 0x3F)), (byte)(0x80 | ((scalar >> 6) & 0x3F)),
                (byte)(0x80 | (scalar & 0x3F)))
        };
        for (var i = 0; i < count; i++) built.Append('%').Append(Hex[bytes[i] >> 4]).Append(Hex[bytes[i] & 0xF]);
    }

    private static int Write(Span<byte> bytes, byte a, byte b = 0, byte c = 0, byte d = 0)
    {
        (bytes[0], bytes[1], bytes[2], bytes[3]) = (a, b, c, d);
        return d != 0 ? 4 : c != 0 ? 3 : b != 0 ? 2 : 1;
    }

    private const string Hex = "0123456789ABCDEF";

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    /// <summary>
    /// An image's sources, <c>url descriptor, …</c>: each URL checked as <see cref="FilterUrl"/> does and normalized, its
    /// descriptor (<c>2x</c>, <c>1.5x</c>, <c>640w</c>) only letters, digits, <c>.</c> and spaces; a source that is not so
    /// <c>#zWebLingoz</c>.
    /// </summary>
    public static string Srcset(string text)
    {
        var built = new StringBuilder(text.Length + 16);
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != ',') continue;
            if (start > 0) built.Append(',');
            AppendSource(built, text, start, i);
            start = i + 1;
        }
        return built.ToString();
    }

    private static void AppendSource(StringBuilder built, string text, int left, int right)
    {
        var start = left;
        while (start < right && IsHtmlSpace(text[start])) start++;
        var end = start;
        while (end < right && !IsHtmlSpace(text[end])) end++;
        var url = text[start..end];
        var descriptorOk = true;
        for (var i = end; i < right && descriptorOk; i++)
        {
            descriptorOk = IsHtmlSpace(text[i]) || text[i] is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.';
        }
        if (IsSafeUrl(url) && descriptorOk)
        {
            built.Append(text, left, start - left).Append(NormalizeUrl(url)).Append(text, end, right - end);
            return;
        }
        built.Append('#').Append(Unsafe);
    }

    private static bool IsHtmlSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    private enum ScriptKind { String, Template, Regex }

    /// <summary>
    /// Text as a script's string takes it, between quotes: JavaScript's escapes for <c>\</c>, the quotes, line breaks and
    /// other control characters, U+2028 and U+2029, and HTML's specials as <c>\u00XX</c>, so that it neither ends its string,
    /// nor its script, nor its attribute; in a template literal <c>$</c>, <c>{</c> and <c>}</c> too, and in a regular
    /// expression its specials, an empty one <c>(?:)</c>.
    /// </summary>
    public static string ScriptString(string text) => ScriptString(text, ScriptKind.String);

    private static string ScriptString(string text, ScriptKind kind)
    {
        if (kind == ScriptKind.Regex && text.Length == 0) return "(?:)";
        StringBuilder? built = null;
        var written = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var replacement = c switch
            {
                '\t' => "\\t", '\n' => "\\n", '\f' => "\\f", '\r' => "\\r", '"' => "\\u0022", '`' => "\\u0060", '&' => "\\u0026",
                '\'' => "\\u0027", '+' => "\\u002b", '/' => "\\/", '<' => "\\u003c", '>' => "\\u003e", '\\' => "\\\\",
                '\u2028' => "\\u2028", '\u2029' => "\\u2029",
                < ' ' => "\\u00" + Hex[c >> 4] + Hex[c & 0xF],
                '$' or '{' or '}' when kind == ScriptKind.Template => c switch { '$' => "\\u0024", '{' => "\\u007b", _ => "\\u007d" },
                '$' or '(' or ')' or '*' or '-' or '.' or '?' or '[' or ']' or '^' or '{' or '|' or '}' when kind == ScriptKind.Regex => "\\" + c,
                _ => null
            };
            if (replacement is null) continue;
            built ??= new StringBuilder(text.Length + 16);
            built.Append(text, written, i - written).Append(replacement);
            written = i + 1;
        }
        return built is null ? text : built.Append(text, written, text.Length - written).ToString();
    }

    /// <summary>
    /// A style's value: its CSS escapes decoded, then refused, as <see cref="Unsafe"/>, if it holds anything that could end
    /// the value or change what the style means (<c>"'()/;@[\]`{}&lt;&gt;</c>, a NUL, <c>--</c>), or names
    /// <c>expression</c> or <c>-moz-binding</c>; else the decoded value.
    /// </summary>
    public static string FilterStyle(string text)
    {
        var decoded = DecodeStyle(text);
        var name = new StringBuilder(decoded.Length);
        for (var i = 0; i < decoded.Length; i++)
        {
            var c = decoded[i];
            switch (c)
            {
                case '\0' or '"' or '\'' or '(' or ')' or '/' or ';' or '@' or '[' or '\\' or ']' or '`' or '{' or '}' or '<' or '>':
                    return Unsafe;
                case '-' when i != 0 && decoded[i - 1] == '-':
                    return Unsafe;
                default:
                    if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-') name.Append(char.ToLowerInvariant(c));
                    break;
            }
        }
        var names = name.ToString();
        return names.Contains("expression", StringComparison.Ordinal) || names.Contains("mozbinding", StringComparison.Ordinal)
            || names.Contains("moz-binding", StringComparison.Ordinal)
            ? Unsafe
            : decoded;
    }

    /// <summary>CSS's escapes decoded: <c>\</c> and one to six hexadecimal digits, a space after them taken with them, or any other character.</summary>
    private static string DecodeStyle(string text)
    {
        if (!text.Contains('\\')) return text;
        var decoded = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 == text.Length)
            {
                decoded.Append(text[i]);
                continue;
            }
            var j = i + 1;
            var value = 0;
            while (j < text.Length && j < i + 7 && IsHex(text[j])) value = value * 16 + HexValue(text[j++]);
            if (j == i + 1)
            {
                decoded.Append(text[++i]);
                continue;
            }
            if (j < text.Length && text[j] is ' ' or '\t' or '\n' or '\f' or '\r') j++;
            decoded.Append(value is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF) ? "\uFFFD" : char.ConvertFromUtf32(value));
            i = j - 1;
        }
        return decoded.ToString();
    }

    /// <summary>
    /// Text as a style's string takes it: CSS's escapes, <c>\</c> and hexadecimal digits, for what could end the string or
    /// its attribute, or start a comment or a URL (<c>"&amp;'()+/:;&lt;&gt;{}</c>, control characters), <c>\\</c> for a
    /// <c>\</c>, a space after an escape that a hexadecimal digit or a space follows.
    /// </summary>
    public static string StyleString(string text)
    {
        StringBuilder? built = null;
        var written = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var replacement = c switch
            {
                '\0' => "\\0", '\t' => "\\9", '\n' => "\\a", '\f' => "\\c", '\r' => "\\d", '"' => "\\22", '&' => "\\26", '\'' => "\\27",
                '(' => "\\28", ')' => "\\29", '+' => "\\2b", '/' => "\\2f", ':' => "\\3a", ';' => "\\3b", '<' => "\\3c", '>' => "\\3e",
                '\\' => "\\\\", '{' => "\\7b", '}' => "\\7d",
                < ' ' => (c < 0x10 ? "\\" : "\\1") + char.ToLowerInvariant(Hex[c & 0xF]),
                _ => null
            };
            if (replacement is null) continue;
            built ??= new StringBuilder(text.Length + 16);
            built.Append(text, written, i - written).Append(replacement);
            written = i + 1;
            if (replacement != "\\\\" && (written == text.Length || IsHex(text[written]) || text[written] is ' ' or '\t' or '\n' or '\f' or '\r'))
            {
                built.Append(' ');
            }
        }
        return built is null ? text : built.Append(text, written, text.Length - written).ToString();
    }

    /// <summary>Text with each character <paramref name="replacement"/> gives a replacement for replaced, the same string if none is.</summary>
    private static string Replace(string text, Func<char, string?> replacement)
    {
        StringBuilder? built = null;
        var written = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (replacement(text[i]) is not { } replaced) continue;
            built ??= new StringBuilder(text.Length + 16);
            built.Append(text, written, i - written).Append(replaced);
            written = i + 1;
        }
        return built is null ? text : built.Append(text, written, text.Length - written).ToString();
    }
}
