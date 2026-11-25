using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
namespace WebLingo.Conformance.Tests;

/// <summary>How the tests' SMTP server is reached: STARTTLS offered, TLS from the start, or neither.</summary>
public enum SmtpTestTls { StartTls, Implicit, None }

/// <summary>
/// The tests' SMTP server, on the loopback address, for <c>std:mail</c>'s tests (specification, section 7.17): never a
/// real one. It offers STARTTLS, or TLS from the start, with a self-signed certificate, the sign-ins it is given, and
/// takes a message, recording each command and the message's lines, a dot-stuffed line unstuffed; it refuses one
/// recipient where it is told to, and a sign-in that does not match. It serves on a thread of its own, never the thread
/// pool's, which tests running beside it may keep busy for longer than a client waits for its greeting: until 24 November
/// 2026 it accepted through the pool, and a client waited a minute for it on a loaded machine ("Flaky Tests Found Under
/// Load").
/// </summary>
public sealed class SmtpTestServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lock = new();
    private readonly List<string> _commands = [];
    private readonly List<string> _messages = [];

    public SmtpTestTls Tls { get; init; } = SmtpTestTls.StartTls;
    public X509Certificate2? Certificate { get; init; }
    public string[] Auth { get; init; } = ["PLAIN", "LOGIN"];
    public string User { get; init; } = "ann";
    public string Password { get; init; } = "secret";
    public string Token { get; init; } = "token";
    public string? RefusedRecipient { get; init; }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>The commands it was given, an AUTH's own as it was sent.</summary>
    public List<string> Commands
    {
        get { lock (_lock) return [.. _commands]; }
    }

    /// <summary>The messages it took, each its lines joined by CRLF, unstuffed.</summary>
    public List<string> Messages
    {
        get { lock (_lock) return [.. _messages]; }
    }

    public SmtpTestServer Start()
    {
        _listener.Start();
        new Thread(Serve) { IsBackground = true, Name = "The SMTP test server" }.Start();
        return this;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    /// <summary>A certificate for <c>localhost</c> and 127.0.0.1, made for a test, which no machine trusts.</summary>
    public static X509Certificate2 MakeCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Loaded from PKCS #12, so that Windows's TLS has its key; removed with the certificate.
        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pkcs12), null);
    }

    private void Serve()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                // Stopping the listener ends a wait here.
                client = _listener.AcceptTcpClient();
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            try
            {
                using (client) Converse(client);
            }
            catch (Exception exception) when (exception is IOException or SocketException or AuthenticationException
                                                  or ObjectDisposedException or InvalidOperationException)
            {
                // A client that gave up, as one refusing the certificate does.
            }
        }
    }

    private void Converse(TcpClient client)
    {
        client.ReceiveTimeout = client.SendTimeout = 10_000;
        Stream stream = client.GetStream();
        var secured = false;
        if (Tls == SmtpTestTls.Implicit) (stream, secured) = (Secure(stream), true);
        Write(stream, "220 test.example ESMTP");
        while (ReadLine(stream) is { } line)
        {
            lock (_lock) _commands.Add(line);
            var verb = (line.Contains(' ') ? line[..line.IndexOf(' ')] : line).ToUpperInvariant();
            var rest = line.Contains(' ') ? line[(line.IndexOf(' ') + 1)..] : "";
            switch (verb)
            {
                case "EHLO":
                    var lines = new List<string> { "test.example" };
                    if (Tls == SmtpTestTls.StartTls && !secured && Certificate is not null) lines.Add("STARTTLS");
                    if (Auth.Length > 0) lines.Add("AUTH " + string.Join(" ", Auth));
                    lines.Add("8BITMIME");
                    for (var i = 0; i < lines.Count; i++) Write(stream, $"250{(i < lines.Count - 1 ? "-" : " ")}{lines[i]}");
                    break;
                case "HELO":
                    Write(stream, "250 test.example");
                    break;
                case "STARTTLS":
                    Write(stream, "220 2.0.0 Ready to start TLS");
                    (stream, secured) = (Secure(stream), true);
                    break;
                case "AUTH":
                    SignIn(stream, rest);
                    break;
                case "MAIL":
                case "RSET":
                case "NOOP":
                    Write(stream, "250 2.1.0 Ok");
                    break;
                case "RCPT":
                    Write(stream, RefusedRecipient is { } refused && rest.Contains($"<{refused}>", StringComparison.OrdinalIgnoreCase)
                        ? "550 5.1.1 No such user here"
                        : "250 2.1.5 Ok");
                    break;
                case "DATA":
                    Write(stream, "354 End data with <CR><LF>.<CR><LF>");
                    var message = new List<string>();
                    while (ReadLine(stream) is { } data && data != ".") message.Add(data.StartsWith('.') ? data[1..] : data);
                    lock (_lock) _messages.Add(string.Join("\r\n", message));
                    Write(stream, "250 2.0.0 Ok: queued");
                    break;
                case "QUIT":
                    Write(stream, "221 2.0.0 Bye");
                    return;
                default:
                    Write(stream, "500 5.5.2 Error: command not recognized");
                    break;
            }
        }
    }

    private void SignIn(Stream stream, string rest)
    {
        var parts = rest.Split(' ', 2);
        string Decoded(string text) => Encoding.UTF8.GetString(Convert.FromBase64String(text));
        switch (parts[0].ToUpperInvariant())
        {
            case "PLAIN":
                Write(stream, Decoded(parts[1]) == $"\0{User}\0{Password}" ? "235 2.7.0 Authentication successful" : "535 5.7.8 Authentication failed");
                return;
            case "LOGIN":
                Write(stream, "334 VXNlcm5hbWU6");
                var user = Decoded(ReadLine(stream) ?? "");
                Write(stream, "334 UGFzc3dvcmQ6");
                var password = Decoded(ReadLine(stream) ?? "");
                Write(stream, user == User && password == Password ? "235 2.7.0 Authentication successful" : "535 5.7.8 Authentication failed");
                return;
            case "XOAUTH2":
                if (Decoded(parts[1]) == $"user={User}\u0001auth=Bearer {Token}\u0001\u0001")
                {
                    Write(stream, "235 2.7.0 Accepted");
                    return;
                }
                Write(stream, "334 eyJzdGF0dXMiOiI0MDEifQ==");
                ReadLine(stream);
                Write(stream, "535 5.7.8 Username and Password not accepted");
                return;
            default:
                Write(stream, "504 5.5.4 Unrecognized authentication type");
                return;
        }
    }

    private SslStream Secure(Stream stream)
    {
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
        ssl.AuthenticateAsServer(new SslServerAuthenticationOptions
        {
            ServerCertificate = Certificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        });
        return ssl;
    }

    private static void Write(Stream stream, string line)
    {
        stream.Write(Encoding.ASCII.GetBytes(line + "\r\n"));
        stream.Flush();
    }

    private static string? ReadLine(Stream stream)
    {
        var line = new List<byte>();
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0) return null;
            if (b == '\n') return Encoding.UTF8.GetString([.. line]).TrimEnd('\r');
            line.Add((byte)b);
        }
    }
}
