using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// A message as MIME writes it (RFC 5322, RFC 2045 to 2047 and RFC 2231; specification, section 7.17), WebLingo's own,
/// with no MIME parser: its headers in ASCII, a text that is not as MIME's encoded words, <c>=?utf-8?B?…?=</c>, folded
/// at 78 characters where they can be; its text and its HTML in UTF-8, quoted-printable, alternatives of each other where
/// it has both; its attachments in Base64, a file's name that is not ASCII as RFC 2231 writes it; every line ending CRLF,
/// and every byte 7-bit, so that a server needs no 8BITMIME. A <c>Bcc</c> is never written.
/// </summary>
public static class WebLingoMimeWriter
{
    private const string CrLf = "\r\n";
    private const int Folded = 78;

    /// <summary>The message's bytes, dated <paramref name="date"/>, its <c>Message-ID</c> <paramref name="messageId"/>.</summary>
    public static byte[] Write(WebLingoMailMessage message, DateTimeOffset date, string messageId)
    {
        var text = new StringBuilder();
        Header(text, "Date", [Date(date)]);
        Header(text, "From", Addresses([message.From]));
        if (message.ReplyTo.Count > 0) Header(text, "Reply-To", Addresses(message.ReplyTo));
        if (message.To.Count > 0) Header(text, "To", Addresses(message.To));
        if (message.Cc.Count > 0) Header(text, "Cc", Addresses(message.Cc));
        Header(text, "Subject", Unstructured(message.Subject));
        Header(text, "Message-ID", [$"<{messageId}>"]);
        Header(text, "MIME-Version", ["1.0"]);
        foreach (var (name, value) in message.Headers) Header(text, name, Unstructured(value));
        var entity = Body(message);
        foreach (var line in entity.Headers) text.Append(line).Append(CrLf);
        text.Append(CrLf).Append(entity.Body).Append(CrLf);
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>A part's headers, each a whole header, folded, and its body, without the line break after it.</summary>
    private sealed record Entity(List<string> Headers, string Body);

    private static Entity Body(WebLingoMailMessage message)
    {
        Entity body = (message.Text, message.Html) switch
        {
            ({ } text, { } html) => Multipart("alternative", [Text(text, "plain"), Text(html, "html")]),
            (null, { } html) => Text(html, "html"),
            _ => Text(message.Text ?? "", "plain")
        };
        if (message.Attachments.Count == 0) return body;
        var parts = new List<Entity> { body };
        foreach (var attachment in message.Attachments) parts.Add(Attachment(attachment));
        return Multipart("mixed", parts);
    }

    private static Entity Text(string text, string subtype) => new(
        [$"Content-Type: text/{subtype}; charset=utf-8", "Content-Transfer-Encoding: quoted-printable"], QuotedPrintable(text));

    private static Entity Attachment(WebLingoMailAttachment attachment)
    {
        var type = new StringBuilder();
        Header(type, "Content-Type", [attachment.Type + ";", Parameter("name", attachment.Name)]);
        var disposition = new StringBuilder();
        Header(disposition, "Content-Disposition", ["attachment;", Parameter("filename", attachment.Name)]);
        return new Entity([type.ToString().TrimEnd(), "Content-Transfer-Encoding: base64", disposition.ToString().TrimEnd()],
            Base64(attachment.Content));
    }

    private static Entity Multipart(string subtype, List<Entity> parts)
    {
        // "=_" never stands in quoted-printable text, nor in Base64, so that no part's body holds its boundary.
        var boundary = "=_WebLingo_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        var body = new StringBuilder();
        foreach (var part in parts)
        {
            body.Append("--").Append(boundary).Append(CrLf);
            foreach (var line in part.Headers) body.Append(line).Append(CrLf);
            body.Append(CrLf).Append(part.Body).Append(CrLf);
        }
        body.Append("--").Append(boundary).Append("--");
        // Its boundary folded onto a line of its own.
        return new Entity([$"Content-Type: multipart/{subtype};{CrLf} boundary=\"{boundary}\""], body.ToString());
    }

    /// <summary>
    /// A header, its words separated by spaces, a line folded before a word that would take it past 78 characters, where
    /// the line has a word already, each folded line beginning with a space.
    /// </summary>
    private static void Header(StringBuilder text, string name, IReadOnlyList<string> words)
    {
        var length = name.Length + 1;
        text.Append(name).Append(':');
        var first = true;
        foreach (var word in words)
        {
            if (!first && word.Length > 0 && length + 1 + word.Length > Folded)
            {
                text.Append(CrLf);
                length = 0;
            }
            text.Append(' ').Append(word);
            length += 1 + word.Length;
            first = false;
        }
        text.Append(CrLf);
    }

    /// <summary>RFC 5322's date, <c>Mon, 23 Nov 2026 14:30:00 -0600</c>.</summary>
    private static string Date(DateTimeOffset date)
    {
        var offset = date.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        offset = offset.Duration();
        return date.ToString("ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture)
               + $"{sign}{offset.Hours:00}{offset.Minutes:00}";
    }

    /// <summary>An address list's words, each address's display name and address, a comma after each but the last.</summary>
    private static List<string> Addresses(IReadOnlyList<WebLingoMailAddress> addresses)
    {
        var words = new List<string>();
        for (var i = 0; i < addresses.Count; i++)
        {
            var (name, address) = (addresses[i].Name, addresses[i].Address);
            if (string.IsNullOrEmpty(name)) words.Add(address);
            else
            {
                words.AddRange(Phrase(name));
                words.Add($"<{address}>");
            }
            if (i < addresses.Count - 1) words[^1] += ",";
        }
        return words;
    }

    /// <summary>
    /// A display name's words: as it is where it is atoms, quoted where it has a character of RFC 5322's specials, and as
    /// encoded words where it is not printable ASCII.
    /// </summary>
    private static List<string> Phrase(string name)
    {
        if (!IsPrintableAscii(name) || name.Contains("=?", StringComparison.Ordinal)) return EncodedWords(name);
        foreach (var c in name)
        {
            if ("()<>[]:;@\\,.\"".Contains(c))
                return ["\"" + name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\""];
        }
        return [.. name.Split(' ')];
    }

    /// <summary>An unstructured header's words: its own where it is printable ASCII, encoded words otherwise.</summary>
    private static List<string> Unstructured(string text)
    {
        if (!IsPrintableAscii(text) || text.Contains("=?", StringComparison.Ordinal)) return EncodedWords(text);
        var words = text.Split(' ');
        foreach (var word in words)
        {
            if (word.Length > 900) return EncodedWords(text); // No line may pass 998 characters.
        }
        return [.. words];
    }

    /// <summary>
    /// The text as MIME's encoded words (RFC 2047), <c>=?utf-8?B?…?=</c>, each of at most 39 bytes, 64 characters, so
    /// that a header's line, folded, keeps within 78 of them, no character's bytes divided between two.
    /// </summary>
    private static List<string> EncodedWords(string text)
    {
        var words = new List<string>();
        var bytes = new List<byte>();
        Span<byte> one = stackalloc byte[4];
        foreach (var rune in text.EnumerateRunes())
        {
            var length = rune.EncodeToUtf8(one);
            if (bytes.Count + length > 39)
            {
                words.Add(EncodedWord(bytes));
                bytes.Clear();
            }
            for (var i = 0; i < length; i++) bytes.Add(one[i]);
        }
        if (bytes.Count > 0 || words.Count == 0) words.Add(EncodedWord(bytes));
        return words;
    }

    private static string EncodedWord(List<byte> bytes) => "=?utf-8?B?" + Convert.ToBase64String([.. bytes]) + "?=";

    /// <summary>
    /// A parameter, <c>name="report.pdf"</c>, quoted where its value is printable ASCII, and as RFC 2231 writes it
    /// otherwise, <c>name*=utf-8''r%C3%A9sum%C3%A9.pdf</c>.
    /// </summary>
    private static string Parameter(string name, string value)
    {
        if (IsPrintableAscii(value) && !value.Contains('"') && !value.Contains('\\')) return $"{name}=\"{value}\"";
        var encoded = new StringBuilder($"{name}*=utf-8''");
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if (b < 0x80 && (char.IsAsciiLetterOrDigit((char)b) || "!#$&+-.^_`|~".Contains((char)b))) encoded.Append((char)b);
            else encoded.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return encoded.ToString();
    }

    private static bool IsPrintableAscii(string text)
    {
        foreach (var c in text)
        {
            if (c is < ' ' or > '~') return false;
        }
        return true;
    }

    /// <summary>
    /// Text as quoted-printable (RFC 2045), in UTF-8: its line breaks, CRLF, LF or CR, each CRLF; each line at most 76
    /// characters, a longer one broken by a soft line break, <c>=</c>; a byte outside printable ASCII, an <c>=</c>, and a
    /// space or a tab ending a line written as <c>=XX</c>.
    /// </summary>
    public static string QuotedPrintable(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var encoded = new StringBuilder();
        for (var l = 0; l < lines.Length; l++)
        {
            if (l > 0) encoded.Append(CrLf);
            var bytes = Encoding.UTF8.GetBytes(lines[l]);
            var length = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                var last = i == bytes.Length - 1;
                var token = (b is >= 33 and <= 126 && b != (byte)'=') || (b is (byte)' ' or (byte)'\t' && !last)
                    ? ((char)b).ToString()
                    : "=" + b.ToString("X2", CultureInfo.InvariantCulture);
                // Room is kept for the soft line break's "=", unless this is the line's last token.
                if (length + token.Length > (last ? 76 : 75))
                {
                    encoded.Append('=').Append(CrLf);
                    length = 0;
                }
                encoded.Append(token);
                length += token.Length;
            }
        }
        return encoded.ToString();
    }

    /// <summary>Bytes as Base64, in lines of 76 characters.</summary>
    private static string Base64(byte[] content)
    {
        var all = Convert.ToBase64String(content);
        var lines = new StringBuilder();
        for (var i = 0; i < all.Length; i += 76)
        {
            if (i > 0) lines.Append(CrLf);
            lines.Append(all, i, Math.Min(76, all.Length - i));
        }
        return lines.ToString();
    }
}
