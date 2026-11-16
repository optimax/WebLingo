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
/// <paramref name="timedOut"/> is called, to cancel the request, and the page goes on, to see it cancelled.
/// </summary>
internal sealed class WebLingoResponseText(IWebLingoHttpResponse response, TimeSpan sendTimeout, Action timedOut) : IWebLingoWaitingOutput
{
    /// <summary>How much output is kept before its response is committed and it is sent.</summary>
    public const int Kept = 64 * 1024;

    /// <summary>How much output, once it is being sent, makes a piece.</summary>
    public const int Piece = 16 * 1024;

    private readonly StringBuilder _text = new();
    private int _bytes;

    /// <summary>Whether the output is being sent: its response committed, and an error then too late for a 500.</summary>
    public bool Sending { get; private set; }

    /// <summary>Whether its client has gone longer than the send timeout without taking what waited for it.</summary>
    public bool TimedOut => Volatile.Read(ref _timedOut) == 1;

    private int _timedOut;

    public bool IsFull => Sending && response.Body.IsFull;

    public void WhenReady(Action ready)
    {
        var fired = 0;
        Timer? timer = null;
        void Fire(bool late)
        {
            if (Interlocked.Exchange(ref fired, 1) != 0) return;
            timer?.Dispose();
            if (late)
            {
                Volatile.Write(ref _timedOut, 1);
                timedOut();
            }
            ready();
        }
        timer = new Timer(_ => Fire(true), null, sendTimeout, Timeout.InfiniteTimeSpan);
        response.Body.WhenReady(() => Fire(false));
    }

    public void Write(string text)
    {
        _text.Append(text);
        _bytes += Encoded(text);
        if (_bytes >= (Sending ? Piece : Kept)) Send();
    }

    /// <summary>Sends the output kept, committing the response first if it is not.</summary>
    public void Send()
    {
        if (!Sending)
        {
            if (response.Headers.Get("Content-Type") is null) response.Headers.Set("Content-Type", WebLingoErrorPages.HtmlType);
            response.Commit();
            Sending = true;
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
