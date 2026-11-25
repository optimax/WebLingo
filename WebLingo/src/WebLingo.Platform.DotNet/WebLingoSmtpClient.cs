using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// SMTP's client, WebLingo's own, on .NET's sockets, with no package (specification, section 7.17): for sending to a
/// relay only (RFC 5321). TLS by .NET's <see cref="SslStream"/>, TLS 1.2 or later, by STARTTLS (RFC 3207), a server that
/// offers none refused, or from the start (RFC 8314), or none where the server's settings say so; the server's
/// certificate checked, its chain and its name, one that cannot be checked accepted only by its thumbprint, SHA-256 or
/// SHA-1, as the settings give it. Signing in by AUTH (RFC 4954): XOAUTH2 with a token, else PLAIN, else LOGIN; a password
/// never sent without TLS, but to a server on this machine. Each step blocks, for <see cref="Timeout"/> at most, the
/// message's end for ten minutes, as RFC 5321 allows a server: synchronously, the connection and TLS's handshake too, since
/// a send runs on a thread that may block (the scheduler host's <c>RunBlocking</c>, one of the thread pool's), where an
/// asynchronous operation waited for needs another of the pool's threads to complete it, which a busy pool may not give
/// for longer than the server waits (24 November 2026, "Flaky Tests Found Under Load": a handshake waited for so took more
/// than 10 s on a loaded machine).
/// </summary>
public sealed class WebLingoSmtpClient : IDisposable
{
    /// <summary>How long a reply is waited for, but the one after the message's end.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DataTimeout = TimeSpan.FromMinutes(10);
    private const int MaxLine = 4096;

    private readonly WebLingoMailServer _server;
    private readonly TcpClient _tcp;
    private Stream _stream;
    private readonly byte[] _buffer = new byte[MaxLine];
    private int _start, _end;

    private WebLingoSmtpClient(WebLingoMailServer server, TcpClient tcp)
    {
        (_server, _tcp) = (server, tcp);
        _stream = tcp.GetStream();
    }

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }

    /// <summary>
    /// Sends <paramref name="data"/>, a message as MIME writes it, from <paramref name="from"/> to each of
    /// <paramref name="recipients"/>, through <paramref name="server"/>, signing in with <paramref name="password"/> or
    /// <paramref name="token"/> where its settings name a user; a <see cref="WebLingoMailException"/> where it cannot.
    /// </summary>
    public static void Send(WebLingoMailServer server, string? password, string? token, string from, IReadOnlyList<string> recipients,
        byte[] data)
    {
        using var client = Connect(server);
        try
        {
            client.Converse(password, token, from, recipients, data);
        }
        catch (IOException exception)
        {
            throw Failed($"the connection to the mail server {server.Host} failed: {exception.Message.TrimEnd('.')}");
        }
        catch (SocketException exception)
        {
            throw Failed($"the connection to the mail server {server.Host} failed: {exception.Message.TrimEnd('.')}");
        }
    }

    private static WebLingoSmtpClient Connect(WebLingoMailServer server)
    {
        Socket socket;
        try
        {
            socket = ConnectWithin(server.Host, server.Port, Timeout);
        }
        catch (Exception exception) when (exception is SocketException or TimeoutException)
        {
            var why = exception is TimeoutException ? "it did not answer in time" : exception.Message.TrimEnd('.');
            throw Failed($"the mail server {server.Host}:{server.Port} cannot be reached: {why}");
        }
        var tcp = new TcpClient { Client = socket };
        tcp.ReceiveTimeout = tcp.SendTimeout = (int)Timeout.TotalMilliseconds;
        return new WebLingoSmtpClient(server, tcp);
    }

    /// <summary>
    /// A connection to <paramref name="host"/>, made synchronously within <paramref name="timeout"/>, each of its addresses
    /// tried in turn, as <see cref="TcpClient"/>'s own connect tries them: a socket that does not block connecting, its
    /// end waited for by <see cref="Socket.Select"/>, which no thread of the pool completes.
    /// </summary>
    private static Socket ConnectWithin(string host, int port, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        var addresses = IPAddress.TryParse(host, out var given) ? [given] : Dns.GetHostAddresses(host);
        Exception failure = new SocketException((int)SocketError.HostNotFound);
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
            try
            {
                try
                {
                    socket.Connect(address, port);
                }
                catch (SocketException exception) when (exception.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress)
                {
                    // Being made: Windows says so by WouldBlock, Linux by InProgress. A connection made is writable; one
                    // refused is writable on Linux, and an error on Windows, either way its error in SO_ERROR.
                    List<Socket> writable = [socket], failed = [socket];
                    var left = Math.Max(0, deadline - Environment.TickCount64);
                    Socket.Select(null, writable, failed, (int)Math.Min(left * 1000, int.MaxValue));
                    if (writable.Count == 0 && failed.Count == 0) throw new TimeoutException();
                    var error = (SocketError)(int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)!;
                    if (error != SocketError.Success) throw new SocketException((int)error);
                }
                socket.Blocking = true;
                return socket;
            }
            catch (Exception exception) when (exception is SocketException or TimeoutException)
            {
                socket.Dispose();
                if (exception is TimeoutException || Environment.TickCount64 >= deadline) throw new TimeoutException();
                failure = exception;
            }
        }
        throw failure;
    }

    private void Converse(string? password, string? token, string from, IReadOnlyList<string> recipients, byte[] data)
    {
        var host = _server.Host;
        if (_server.Tls == WebLingoMailTls.Implicit) StartTls();
        var greeting = Reply();
        if (greeting.Code != 220) throw Refused("the connection", greeting);
        var extensions = Hello();
        if (_server.Tls == WebLingoMailTls.StartTls)
        {
            if (!extensions.Contains("STARTTLS"))
            {
                throw Failed($"the mail server {host} does not offer STARTTLS, so that the message would go unencrypted; "
                             + "\"tls\": \"none\" sends it so");
            }
            Expect(Command("STARTTLS"), 220, "STARTTLS");
            // Nothing the server sent before TLS may be read as sent within it (RFC 3207's injection).
            if (_start != _end) throw Failed($"the mail server {host} sent more than STARTTLS's reply before TLS began");
            StartTls();
            extensions = Hello();
        }
        if (_server.User is { } user)
        {
            if (_server.Tls == WebLingoMailTls.None && !IsThisMachine())
            {
                throw Failed($"a password is never sent without TLS but to a server on this machine, and {host} is not one");
            }
            SignIn(user, password, token, extensions);
        }
        Expect(Command($"MAIL FROM:<{from}>"), 250, $"the sender {from}");
        foreach (var recipient in recipients)
        {
            var reply = Command($"RCPT TO:<{recipient}>");
            if (reply.Code is not (250 or 251)) throw Refused($"the recipient {recipient}", reply);
        }
        Expect(Command("DATA"), 354, "the message");
        WriteBytes(Stuffed(data));
        _tcp.ReceiveTimeout = (int)DataTimeout.TotalMilliseconds;
        Expect(Reply(), 250, "the message");
        _tcp.ReceiveTimeout = (int)Timeout.TotalMilliseconds;
        try
        {
            Command("QUIT");
        }
        catch (Exception exception) when (exception is IOException or SocketException or WebLingoMailException)
        {
            // Sent: a server that does not answer QUIT changes nothing.
        }
    }

    /// <summary>EHLO, or HELO where the server does not take it; its extensions, each a line of EHLO's reply in capitals.</summary>
    private HashSet<string> Hello()
    {
        var name = ClientName();
        var reply = Command($"EHLO {name}");
        var extensions = new HashSet<string>(StringComparer.Ordinal);
        if (reply.Code == 250)
        {
            for (var i = 1; i < reply.Lines.Count; i++) extensions.Add(reply.Lines[i].Trim().ToUpperInvariant());
            return extensions;
        }
        Expect(Command($"HELO {name}"), 250, "HELO");
        return extensions;
    }

    /// <summary>The address this end of the connection has, as an address literal, <c>[192.0.2.1]</c>, which EHLO names.</summary>
    private string ClientName()
    {
        if (_tcp.Client.LocalEndPoint is not IPEndPoint { Address: var address }) return "[127.0.0.1]";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[IPv6:{address}]" : $"[{address}]";
    }

    private bool IsThisMachine() => _tcp.Client.RemoteEndPoint is IPEndPoint { Address: var address } && IPAddress.IsLoopback(address);

    private void SignIn(string user, string? password, string? token, HashSet<string> extensions)
    {
        var mechanisms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in extensions)
        {
            if (!extension.StartsWith("AUTH ", StringComparison.Ordinal) && !extension.StartsWith("AUTH=", StringComparison.Ordinal)) continue;
            foreach (var mechanism in extension[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries)) mechanisms.Add(mechanism);
        }
        var host = _server.Host;
        if (token is not null)
        {
            if (!mechanisms.Contains("XOAUTH2")) throw Failed($"the mail server {host} does not offer XOAUTH2, which a token signs in by");
            var reply = Command("AUTH XOAUTH2 " + Base64($"user={user}\u0001auth=Bearer {token}\u0001\u0001"));
            // A refusal comes first as a challenge, with what was wrong, which an empty line answers (Google's XOAUTH2).
            if (reply.Code == 334) reply = Command("");
            Expect(reply, 235, "the sign-in");
            return;
        }
        if (password is null) throw Failed($"the mail server {host} signs in {user}, and is given no password");
        if (mechanisms.Contains("PLAIN"))
        {
            Expect(Command("AUTH PLAIN " + Base64($"\0{user}\0{password}")), 235, "the sign-in");
            return;
        }
        if (!mechanisms.Contains("LOGIN")) throw Failed($"the mail server {host} offers no sign-in WebLingo has, PLAIN or LOGIN");
        Expect(Command("AUTH LOGIN"), 334, "the sign-in");
        Expect(Command(Base64(user)), 334, "the sign-in");
        Expect(Command(Base64(password)), 235, "the sign-in");
    }

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private void StartTls()
    {
        var host = _server.Host;
        var ssl = new SslStream(_stream, leaveInnerStreamOpen: false);
        string? seen = null;
        var problems = SslPolicyErrors.None;
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;
                problems = errors;
                if (certificate is null) return false;
                seen = certificate.GetCertHashString(HashAlgorithmName.SHA256);
                return _server.Thumbprint is { } thumbprint
                       && (Same(thumbprint, seen) || Same(thumbprint, certificate.GetCertHashString(HashAlgorithmName.SHA1)));
            }
        };
        try
        {
            // Synchronous, its reads and writes the socket's own, each within its timeout, Timeout.
            ssl.AuthenticateAsClient(options);
        }
        catch (Exception exception) when (exception is AuthenticationException or IOException)
        {
            ssl.Dispose();
            if (seen is not null)
            {
                throw Failed($"the mail server {host}'s certificate cannot be checked ({problems}); a \"thumbprint\" of {seen} "
                             + "in its settings accepts it");
            }
            throw Failed($"TLS with the mail server {host} failed: {exception.Message.TrimEnd('.')}");
        }
        _stream = ssl;
        (_start, _end) = (0, 0);
    }

    /// <summary>Whether two thumbprints are the same, their colons, spaces and hyphens, and their case, aside.</summary>
    private static bool Same(string given, string thumbprint)
    {
        var bare = new StringBuilder();
        foreach (var c in given)
        {
            if (c is not (':' or ' ' or '-')) bare.Append(char.ToUpperInvariant(c));
        }
        return bare.ToString() == thumbprint.ToUpperInvariant();
    }

    /// <summary>A reply: its code and its lines' text, each after the code and its separator.</summary>
    private sealed record SmtpReply(int Code, List<string> Lines)
    {
        public override string ToString() => $"{Code} {string.Join(" ", Lines).Trim()}";
    }

    private SmtpReply Command(string command)
    {
        WriteBytes(Encoding.ASCII.GetBytes(command + "\r\n"));
        return Reply();
    }

    private void Expect(SmtpReply reply, int code, string what)
    {
        if (reply.Code != code) throw Refused(what, reply);
    }

    private WebLingoMailException Refused(string what, SmtpReply reply) =>
        new(WebLingoMailErrorKind.Failed, $"the mail server {_server.Host} refused {what}: {reply}", reply.Code);

    private static WebLingoMailException Failed(string message) => new(WebLingoMailErrorKind.Failed, message);

    private SmtpReply Reply()
    {
        var lines = new List<string>();
        while (true)
        {
            var line = ReadLine();
            if (line.Length < 3 || !char.IsAsciiDigit(line[0]) || !char.IsAsciiDigit(line[1]) || !char.IsAsciiDigit(line[2])
                || line.Length > 3 && line[3] is not (' ' or '-'))
            {
                throw Failed($"the mail server {_server.Host} does not speak SMTP");
            }
            lines.Add(line.Length > 4 ? line[4..] : "");
            if (line.Length == 3 || line[3] == ' ') return new SmtpReply((line[0] - '0') * 100 + (line[1] - '0') * 10 + line[2] - '0', lines);
            if (lines.Count > 1000) throw Failed($"the mail server {_server.Host} gave a reply without an end");
        }
    }

    /// <summary>A line the server sent, without its line break, CRLF or LF, at most 4,096 bytes.</summary>
    private string ReadLine()
    {
        var line = new List<byte>();
        while (true)
        {
            if (_start == _end)
            {
                _end = _stream.Read(_buffer, 0, _buffer.Length);
                _start = 0;
                if (_end == 0) throw Failed($"the mail server {_server.Host} closed the connection");
            }
            var b = _buffer[_start++];
            if (b == (byte)'\n')
            {
                if (line.Count > 0 && line[^1] == (byte)'\r') line.RemoveAt(line.Count - 1);
                return Encoding.UTF8.GetString([.. line]);
            }
            line.Add(b);
            if (line.Count > MaxLine) throw Failed($"the mail server {_server.Host} sent a line too long for SMTP");
        }
    }

    private void WriteBytes(byte[] bytes)
    {
        _stream.Write(bytes);
        _stream.Flush();
    }

    /// <summary>
    /// The message as DATA sends it (RFC 5321, section 4.5.2): a line beginning with a dot given another, and the message
    /// ended by a line of a dot alone.
    /// </summary>
    public static byte[] Stuffed(byte[] data)
    {
        var stuffed = new List<byte>(data.Length + 64);
        var lineStart = true;
        foreach (var b in data)
        {
            if (lineStart && b == (byte)'.') stuffed.Add((byte)'.');
            stuffed.Add(b);
            lineStart = b == (byte)'\n';
        }
        if (!lineStart) stuffed.AddRange("\r\n"u8.ToArray());
        stuffed.AddRange(".\r\n"u8.ToArray());
        return [.. stuffed];
    }
}
