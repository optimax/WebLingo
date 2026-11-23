namespace WebLingo.Platform.Abstractions;

/// <summary>
/// The platform's mail (specification, section 7.17), which <c>std:mail</c> reaches: a message sent by SMTP to a relay, a
/// server the configuration names, <c>"mail": { "default": { "host": "smtp.example.com", "port": 587, … } }</c>, or one
/// a script gives. Each send blocks, and <c>std:mail</c> runs it off the worker. Safe on several threads at once.
/// </summary>
public interface IWebLingoMail
{
    /// <summary>
    /// Sends <paramref name="message"/> through the server the configuration names <paramref name="server"/>, or, where it
    /// is null, its <c>"default"</c>, or the one server it names; <paramref name="token"/>, where given, an OAuth 2.0
    /// token, signing in by XOAUTH2. Gives the message's <c>Message-ID</c>, without its angle brackets; a
    /// <see cref="WebLingoMailException"/> where it cannot be sent.
    /// </summary>
    string Send(string? server, string? token, WebLingoMailMessage message);

    /// <summary>As the other, through a server a script gives, its password its own.</summary>
    string Send(WebLingoMailServer server, string? token, WebLingoMailMessage message);
}

/// <summary>How a mail server is reached: STARTTLS, TLS from the start, as on port 465, or without TLS.</summary>
public enum WebLingoMailTls { StartTls, Implicit, None }

/// <summary>
/// A mail server: its host, its port, how TLS reaches it, the user it signs in as and the password, if any, and the
/// SHA-256 or SHA-1 thumbprint of a certificate accepted though it cannot be checked, if any.
/// </summary>
public sealed record WebLingoMailServer(string Host, int Port, WebLingoMailTls Tls, string? User, WebLingoSecret? Password,
    string? Thumbprint)
{
    /// <summary>The port a server listens on where none is given: 587 for STARTTLS, 465 for TLS from the start, 25 without.</summary>
    public static int DefaultPort(WebLingoMailTls tls) => tls switch
    {
        WebLingoMailTls.Implicit => 465,
        WebLingoMailTls.None => 25,
        _ => 587
    };
}

/// <summary>An address of a message: its display name, or null, and the address itself, ASCII, <c>ann@example.com</c>.</summary>
public sealed record WebLingoMailAddress(string? Name, string Address);

/// <summary>A message's attachment: its file's name, its <c>Content-Type</c> and its bytes.</summary>
public sealed record WebLingoMailAttachment(string Name, string Type, byte[] Content);

/// <summary>
/// A message, checked: its sender, its recipients, those it is addressed to, copied to and copied to unseen, at least one
/// among them, those a reply goes to, its subject, its text and its HTML, either or both or neither, its other headers,
/// each a name and a value, and its attachments.
/// </summary>
public sealed record WebLingoMailMessage(
    WebLingoMailAddress From,
    IReadOnlyList<WebLingoMailAddress> To,
    IReadOnlyList<WebLingoMailAddress> Cc,
    IReadOnlyList<WebLingoMailAddress> Bcc,
    IReadOnlyList<WebLingoMailAddress> ReplyTo,
    string Subject,
    string? Text,
    string? Html,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    IReadOnlyList<WebLingoMailAttachment> Attachments);

/// <summary>
/// What a send failed of: something the script asked for that cannot be, a server the configuration does not name
/// (WL3001); or the mail's own failure, a server that cannot be reached, offers no TLS, refuses the sign-in, a recipient or
/// the message, or a secret the host cannot read (WL3023, <c>MailError</c>).
/// </summary>
public enum WebLingoMailErrorKind { Invalid, Failed }

/// <summary>
/// A send's failure, its message for a script to read, without a capital or a full stop, never holding a password; and
/// the server's reply code, where it answered, <c>550</c>.
/// </summary>
public sealed class WebLingoMailException(WebLingoMailErrorKind kind, string message, int? reply = null) : Exception(message)
{
    public WebLingoMailErrorKind Kind { get; } = kind;

    public int? Reply { get; } = reply;
}
