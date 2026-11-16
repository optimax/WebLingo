using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting;

/// <summary>
/// A request's text output, as its page writes it, kept until the page has run (specification, section 6.12), so that a
/// runtime error can still be a 500; then written to the response as UTF-8, a surrogate without its pair as U+FFFD.
/// Streaming it as it is written, with backpressure, is M0.14.7's.
/// </summary>
internal sealed class WebLingoResponseText : IWebLingoTextOutput
{
    private readonly StringBuilder _text = new();

    public void Write(string text) => _text.Append(text);

    /// <summary>Writes the text kept to <paramref name="response"/>'s body.</summary>
    public void WriteTo(IWebLingoHttpResponse response) => WriteTo(response, _text.ToString());

    /// <summary>Writes <paramref name="text"/> to <paramref name="response"/>'s body as UTF-8.</summary>
    public static void WriteTo(IWebLingoHttpResponse response, string text)
    {
        var bytes = new byte[Encoded(text)];
        var at = 0;
        for (var i = 0; i < text.Length; i++)
        {
            int code = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                code = 0x10000 + ((code - 0xD800) << 10) + (text[++i] - 0xDC00);
            }
            else if (char.IsSurrogate(text[i]))
            {
                code = 0xFFFD;
            }
            if (code < 0x80)
            {
                bytes[at++] = (byte)code;
            }
            else if (code < 0x800)
            {
                bytes[at++] = (byte)(0xC0 | (code >> 6));
                bytes[at++] = (byte)(0x80 | (code & 0x3F));
            }
            else if (code < 0x10000)
            {
                bytes[at++] = (byte)(0xE0 | (code >> 12));
                bytes[at++] = (byte)(0x80 | ((code >> 6) & 0x3F));
                bytes[at++] = (byte)(0x80 | (code & 0x3F));
            }
            else
            {
                bytes[at++] = (byte)(0xF0 | (code >> 18));
                bytes[at++] = (byte)(0x80 | ((code >> 12) & 0x3F));
                bytes[at++] = (byte)(0x80 | ((code >> 6) & 0x3F));
                bytes[at++] = (byte)(0x80 | (code & 0x3F));
            }
        }
        if (bytes.Length > 0) response.Body.Write(bytes, 0, bytes.Length);
    }

    private static int Encoded(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                count += 4;
                i++;
            }
            else
            {
                count += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
            }
        }
        return count;
    }
}
