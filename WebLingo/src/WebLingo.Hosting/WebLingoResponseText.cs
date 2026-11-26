using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting;

/// <summary>
/// A request's text output, as its page writes it (specification, section 6.12, "Streaming"; section 9, "M0.14.7 parts",
/// part 1): kept until it reaches <see cref="Kept"/> bytes of UTF-8, so that a runtime error before then can still be a
/// 500; then its response committed, its <c>Content-Type</c> <c>text/html; charset=utf-8</c> unless the page set another,
/// and the output sent as it is written, in pieces of about <see cref="Piece"/> bytes. Written as UTF-8, a surrogate
/// without its pair as U+FFFD. Once sent, it is full while its response holds more than its client has taken, and a page
/// waits for it (section 9, "M0.14.7 parts", part 3), for at most <paramref name="sendTimeout"/>, after which
/// <paramref name="timedOut"/> is called, to cancel the request, and the page goes on, to see it cancelled. A request that
/// its debugger, <paramref name="debugged"/>, stopped meanwhile waits the send timeout anew, the time it was stopped not its
/// client's (section 5.7, "Debugging WASP pages, parts", part 2).
/// </summary>
internal sealed class WebLingoResponseText(IWebLingoHttpResponse response, TimeSpan sendTimeout, Action timedOut,
    WebLingo.VM.IWebLingoDebuggerThread? debugged = null) : IWebLingoWaitingOutput
{
    /// <summary>How much output is kept before its response is committed and it is sent.</summary>
    public const int Kept = 64 * 1024;

    /// <summary>How much output, once it is being sent, makes a piece.</summary>
    public const int Piece = 16 * 1024;

    private readonly StringBuilder _text = new();
    private int _bytes;

    // The output kept before the text in _text, as bytes, where bytes were written: the text before them, encoded, and them.
    private List<byte[]>? _kept;

    /// <summary>
    /// The file the page or the handler sent (specification, section 6.12, "Files sent"), or null: what it wrote, before and
    /// after, never sent.
    /// </summary>
    public WebLingoSentFile? Sent { get; private set; }

    /// <summary>Whether the output is being sent: its response committed, and an error then too late for a 500.</summary>
    public bool Sending { get; private set; }

    /// <summary>
    /// Whether its client went longer than the send timeout without taking what waited for it. Set off the worker, before
    /// the page is told to go on, which its scheduler host posts to the worker, so that the worker sees it then.
    /// </summary>
    public bool TimedOut { get; private set; }

    public bool IsFull => Sending && response.Body.IsFull;

    /// <summary>Calls <paramref name="ready"/> once the response can take more, or once the send timeout has passed, the request then cancelled.</summary>
    public void WhenReady(Action ready) => WhenReady(ready, debugged?.Stops ?? 0);

    private void WhenReady(Action ready, int stops) => response.Body.WhenReady(sendTimeout, inTime =>
    {
        if (!inTime && debugged is { } thread && (thread.Stops != stops || (thread.Stops & 1) == 1))
        {
            WhenReady(ready, thread.Stops); // Stopped by its debugger since it began to wait: it waits anew.
            return;
        }
        if (!inTime)
        {
            TimedOut = true;
            timedOut();
        }
        ready();
    });

    public void Write(string text)
    {
        if (Sent is not null) return;
        _text.Append(text);
        _bytes += Encoded(text);
        if (_bytes >= (Sending ? Piece : Kept)) Send();
    }

    /// <summary>Writes bytes as they are, after the text before them (specification, section 6.12, "Files sent").</summary>
    public void WriteBytes(byte[] data)
    {
        if (Sent is not null || data.Length == 0) return;
        _kept ??= [];
        if (_text.Length > 0)
        {
            _kept.Add(Encode(_text.ToString()));
            _text.Clear();
        }
        _kept.Add(data);
        _bytes += data.Length;
        if (_bytes >= (Sending ? Piece : Kept)) Send();
    }

    /// <summary>
    /// The page's response to be a file, which its host sends: what was written, and what is written after, not sent; false,
    /// with why, where its output is being sent already.
    /// </summary>
    public bool TrySendFile(WebLingoSentFile file, out string? why)
    {
        if (Sending)
        {
            why = "the response's output is being sent already";
            return false;
        }
        why = null;
        Sent = file;
        _text.Clear();
        _kept = null;
        _bytes = 0;
        return true;
    }

    /// <summary>Sends the output kept, committing the response first if it is not.</summary>
    public void Send()
    {
        if (Sent is not null) return;
        if (!Sending)
        {
            if (response.Headers.Get("Content-Type") is null) response.Headers.Set("Content-Type", WebLingoErrorPages.HtmlType);
            response.Commit();
            Sending = true;
        }
        if (_kept is { Count: > 0 })
        {
            foreach (var piece in _kept) response.Body.Write(piece, 0, piece.Length);
            _kept.Clear();
        }
        // A surrogate whose pair a later write may give waits for it.
        var keep = _text.Length > 0 && char.IsHighSurrogate(_text[^1]) ? 1 : 0;
        WriteTo(response, _text.ToString(0, _text.Length - keep));
        _text.Remove(0, _text.Length - keep);
        _bytes = keep * 3;
    }

    /// <summary>Writes <paramref name="text"/> to <paramref name="response"/>'s body as UTF-8.</summary>
    public static void WriteTo(IWebLingoHttpResponse response, string text)
    {
        var bytes = Encode(text);
        if (bytes.Length > 0) response.Body.Write(bytes, 0, bytes.Length);
    }

    /// <summary><paramref name="text"/> as UTF-8, a surrogate without its pair as U+FFFD.</summary>
    private static byte[] Encode(string text)
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
        return bytes;
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
