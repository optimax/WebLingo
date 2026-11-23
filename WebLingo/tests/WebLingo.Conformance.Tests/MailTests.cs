using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Data.Sqlite;
using WebLingo.Cli;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:mail</c> (specification, section 7.17), through <c>weblingo</c>, against an SMTP server of the tests' own on the
/// loopback address (<see cref="SmtpTestServer"/>), never a real one: a message written as MIME, sent by STARTTLS or TLS
/// from the start, signed in by PLAIN, LOGIN or XOAUTH2, through a configured server or one a script gives; a certificate
/// no machine trusts accepted by its thumbprint alone; every key and value of a message checked; and secrets, a password
/// or a connection string, read where the configuration keeps them, never shown.
/// </summary>
public sealed class MailTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-mail-" + Guid.NewGuid().ToString("N"));
    private readonly X509Certificate2 _certificate = SmtpTestServer.MakeCertificate();
    private readonly List<SmtpTestServer> _servers = [];

    private const string Imports = "import { writeLn } from \"std:console\"; import mail from \"std:mail\"; ";
    private const string Message = "{ from: \"ann@example.com\", to: \"bob@example.com\", subject: \"Tea\", text: \"Sencha.\" }";

    public MailTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        foreach (var server in _servers) server.Dispose();
        _certificate.Dispose();
        SqliteConnection.ClearAllPools(); // The provider's pool keeps a file open after its connection is closed.
        try { Directory.Delete(_folder, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private string Thumbprint => _certificate.GetCertHashString(HashAlgorithmName.SHA256);

    private SmtpTestServer Server(SmtpTestTls tls = SmtpTestTls.StartTls, string[]? auth = null, string? refused = null)
    {
        var server = new SmtpTestServer { Tls = tls, Certificate = _certificate, Auth = auth ?? ["PLAIN", "LOGIN"], RefusedRecipient = refused };
        _servers.Add(server);
        return server.Start();
    }

    /// <summary>A configuration whose "default" server is the test's, with <paramref name="more"/> of its settings.</summary>
    private void Configure(SmtpTestServer server, string more = "\"user\": \"ann\", \"password\": \"secret\"", bool thumbprint = true) =>
        File.WriteAllText(Path.Combine(_folder, "weblingo.config"),
            $"{{ \"mail\": {{ \"default\": {{ \"host\": \"127.0.0.1\", \"port\": {server.Port}"
            + (thumbprint ? $", \"thumbprint\": \"{Thumbprint}\"" : "") + (more.Length > 0 ? ", " + more : "") + " } } }");

    private (int Exit, string Output, string Error) Weblingo(string source)
    {
        var path = Path.Combine(_folder, "program.wlg");
        File.WriteAllText(path, Imports + source);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(["run", path], output, error);
        return (exit, output.ToString(), error.ToString().Replace(path, "program.wlg", StringComparison.Ordinal));
    }

    private static string Encoded(string text) => "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "?=";

    [Fact]
    public void AMessageGoesThroughTheConfiguredServerByStartTls()
    {
        var server = Server();
        Configure(server);
        var (exit, output, error) = Weblingo(
            "var id = await mail.send({ from: \"Zofia Żak <ann@example.com>\", to: [\"bob@example.com\", \"Carl <carl@example.com>\"], "
            + "cc: \"dana@example.com\", bcc: \"eve@example.com\", replyTo: \"help@example.com\", subject: \"Zażółć gęślą jaźń\", "
            + "text: \"Dzień dobry.\\n.A line with a dot.\", html: \"<p>Dzień dobry.</p>\", headers: { \"X-Shop\": \"Tea\" } }); "
            + "writeLn([id.endsWith(\"@example.com\"), id.length]);");
        Assert.Equal((0, "[true, 44]\n", ""), (exit, output, error));
        var commands = server.Commands;
        Assert.Equal(new[] { "EHLO [127.0.0.1]", "STARTTLS", "EHLO [127.0.0.1]" }, commands[..3]);
        Assert.StartsWith("AUTH PLAIN ", commands[3], StringComparison.Ordinal);
        Assert.Equal(new[] { "MAIL FROM:<ann@example.com>", "RCPT TO:<bob@example.com>", "RCPT TO:<carl@example.com>",
            "RCPT TO:<dana@example.com>", "RCPT TO:<eve@example.com>", "DATA", "QUIT" }, commands[4..]);
        var message = Assert.Single(server.Messages);
        Assert.Contains($"From: {Encoded("Zofia Żak")} <ann@example.com>\r\n", message, StringComparison.Ordinal);
        Assert.Contains("\r\nTo: bob@example.com, Carl <carl@example.com>\r\n", message, StringComparison.Ordinal);
        Assert.Contains("\r\nCc: dana@example.com\r\n", message, StringComparison.Ordinal);
        Assert.Contains("\r\nReply-To: help@example.com\r\n", message, StringComparison.Ordinal);
        Assert.Contains($"\r\nSubject: {Encoded("Zażółć gęślą jaźń")}\r\n", message, StringComparison.Ordinal);
        Assert.Contains("\r\nX-Shop: Tea\r\n", message, StringComparison.Ordinal);
        Assert.Contains("\r\nMIME-Version: 1.0\r\n", message, StringComparison.Ordinal);
        Assert.Contains("\r\nContent-Type: multipart/alternative;\r\n boundary=", message, StringComparison.Ordinal);
        Assert.Contains("\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: quoted-printable\r\n\r\n"
                        + "Dzie=C5=84 dobry.\r\n.A line with a dot.\r\n", message, StringComparison.Ordinal);
        Assert.Contains("\r\nContent-Type: text/html; charset=utf-8\r\n", message, StringComparison.Ordinal);
        Assert.DoesNotContain("eve@", message, StringComparison.Ordinal); // A Bcc is never written.
        Assert.Matches(@"\r\nMessage-ID: <[0-9a-f]{32}@example\.com>\r\n", message);
        Assert.Matches(@"^Date: [A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} [+-]\d{4}\r\n", message);
        Assert.All(message.Split("\r\n"), line => Assert.True(line.Length <= 78, line));
    }

    [Fact]
    public void ACertificateThatCannotBeCheckedIsAcceptedOnlyByItsThumbprint()
    {
        var server = Server();
        Configure(server, thumbprint: false);
        var (exit, output, error) = Weblingo($"try {{ await mail.send({Message}); }} catch (MailError e) {{ writeLn([e.code, e.reply]); writeLn(e.message); }}");
        Assert.Equal((0, ""), (exit, error));
        Assert.StartsWith("[\"WL3023\", null]\nThe message cannot be sent: the mail server 127.0.0.1's certificate cannot be checked (", output,
            StringComparison.Ordinal);
        Assert.EndsWith($"); a \"thumbprint\" of {Thumbprint} in its settings accepts it.\n", output, StringComparison.Ordinal);
        Assert.Empty(server.Messages);
        Configure(server, thumbprint: true);
        Assert.Equal((0, "sent\n", ""), Weblingo($"await mail.send({Message}); writeLn(\"sent\");"));
        Assert.Single(server.Messages);
    }

    [Fact]
    public void AServerThatOffersNoTlsIsRefusedUnlessItsSettingsSayNone()
    {
        var server = Server(SmtpTestTls.None);
        Configure(server);
        Assert.Equal((0, "The message cannot be sent: the mail server 127.0.0.1 does not offer STARTTLS, so that the message would go "
                         + "unencrypted; \"tls\": \"none\" sends it so.\n", ""),
            Weblingo($"try {{ await mail.send({Message}); }} catch (MailError e) {{ writeLn(e.message); }}"));
        // A password without TLS only to a server on this machine, which this one is.
        Configure(server, "\"user\": \"ann\", \"password\": \"secret\", \"tls\": \"none\"");
        Assert.Equal((0, "sent\n", ""), Weblingo($"await mail.send({Message}); writeLn(\"sent\");"));
        Assert.Equal(new[] { "EHLO [127.0.0.1]", "AUTH PLAIN AGFubgBzZWNyZXQ=", "MAIL FROM:<ann@example.com>" }, server.Commands[^6..^3]);
    }

    [Fact]
    public void TlsFromTheStartReachesAServerOnItsOwnPort()
    {
        var server = Server(SmtpTestTls.Implicit, auth: ["LOGIN"]);
        Configure(server, "\"user\": \"ann\", \"password\": \"secret\", \"tls\": \"implicit\"");
        Assert.Equal((0, "sent\n", ""), Weblingo($"await mail.send({Message}); writeLn(\"sent\");"));
        Assert.Equal(new[] { "EHLO [127.0.0.1]", "AUTH LOGIN", "MAIL FROM:<ann@example.com>" }, server.Commands[..3]);
        Assert.Single(server.Messages);
    }

    [Fact]
    public void AScriptsTokenSignsInByXOAuth2()
    {
        var server = Server(auth: ["XOAUTH2"]);
        Configure(server, "\"user\": \"ann\"");
        Assert.Equal((0, "sent\n", ""), Weblingo($"await mail.send({Message}, {{ token: \"token\" }}); writeLn(\"sent\");"));
        Assert.Equal((0, "sent\n", ""), Weblingo($"await mail.send({Message}, {{ server: \"default\", token: \"token\" }}); writeLn(\"sent\");"));
        Assert.Equal(2, server.Messages.Count);
        Assert.Equal((0, "[\"WL3023\", 535, \"The message cannot be sent: the mail server 127.0.0.1 refused the sign-in: 535 5.7.8 Username "
                         + "and Password not accepted.\"]\n", ""),
            Weblingo($"try {{ await mail.send({Message}, {{ token: \"stale\" }}); }} catch (MailError e) {{ writeLn([e.code, e.reply, e.message]); }}"));
        Assert.Equal((0, "[\"WL3001\", \"The message cannot be sent: the mail server 127.0.0.1 signs in as a user, and is given neither a "
                         + "password nor a token.\"]\n", ""),
            Weblingo($"try {{ await mail.send({Message}); }} catch (e) {{ writeLn([e.code, e.message]); }}"));
    }

    [Fact]
    public void ARefusalIsAMailErrorWithTheServersReply()
    {
        var server = Server(refused: "bob@example.com");
        Configure(server, "\"user\": \"ann\", \"password\": \"wrong\"");
        Assert.Equal((0, "[535, \"The message cannot be sent: the mail server 127.0.0.1 refused the sign-in: 535 5.7.8 Authentication "
                         + "failed.\"]\n", ""),
            Weblingo($"try {{ await mail.send({Message}); }} catch (MailError e) {{ writeLn([e.reply, e.message]); }}"));
        Configure(server);
        Assert.Equal((0, "[550, \"The message cannot be sent: the mail server 127.0.0.1 refused the recipient bob@example.com: 550 5.1.1 "
                         + "No such user here.\"]\n", ""),
            Weblingo($"try {{ await mail.send({Message}); }} catch (MailError e) {{ writeLn([e.reply, e.message]); }}"));
        Assert.Empty(server.Messages);
        Assert.Equal((0, "[\"WL3023\", null]\n", ""),
            Weblingo($"try {{ await mail.send({Message}, {{ host: \"127.0.0.1\", port: 1, tls: \"none\" }}); }} "
                     + "catch (MailError e) { writeLn([e.code, e.reply]); }"));
    }

    [Fact]
    public void AScriptGivesAServerOfItsOwn()
    {
        var server = Server(SmtpTestTls.None);
        Assert.Equal((0, "sent\n", ""),
            Weblingo($"await mail.send({Message}, {{ host: \"127.0.0.1\", port: {server.Port}, tls: \"none\" }}); writeLn(\"sent\");"));
        var secured = Server();
        Assert.Equal((0, "sent\n", ""),
            Weblingo($"await mail.send({Message}, {{ host: \"127.0.0.1\", port: {secured.Port}, user: \"ann\", password: \"secret\", "
                     + $"thumbprint: \"{Thumbprint}\" }}); writeLn(\"sent\");"));
        Assert.Single(server.Messages);
        Assert.Equal("STARTTLS", secured.Commands[1]);
        Assert.Single(secured.Messages);
    }

    [Fact]
    public void AttachmentsAreAFilesOrTheScriptsOwnContent()
    {
        var server = Server();
        Configure(server);
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "Tea notes");
        var notes = Path.Combine(_folder, "notes.txt").Replace('\\', '/'); // weblingo's relative paths are from the current folder.
        var (exit, output, error) = Weblingo("import { Bytes } from \"std:bytes\"; "
            + "await mail.send({ from: \"ann@example.com\", to: \"bob@example.com\", subject: \"Files\", text: \"See.\", attachments: ["
            + $"{{ path: \"{notes}\" }}, " + "{ name: \"résumé.pdf\", content: Bytes.from(\"PDF\") }, { name: \"a.csv\", content: \"x,y\" }, "
            + "{ name: \"data\", content: \"z\", type: \"application/x-tea\" }] }); writeLn(\"sent\"); "
            + "try { await mail.send({ from: \"ann@example.com\", to: \"bob@example.com\", attachments: [{ path: \"missing.txt\" }] }); } "
            + "catch (IOError e) { writeLn(e.message.startsWith(\"'missing.txt' cannot be attached: \")); }");
        Assert.Equal((0, "sent\ntrue\n", ""), (exit, output, error));
        var message = Assert.Single(server.Messages);
        Assert.Contains("\r\nContent-Type: multipart/mixed;\r\n boundary=", message, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/plain; name=\"notes.txt\"\r\nContent-Transfer-Encoding: base64\r\n"
                        + "Content-Disposition: attachment; filename=\"notes.txt\"\r\n\r\nVGVhIG5vdGVz\r\n", message, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/pdf; name*=utf-8''r%C3%A9sum%C3%A9.pdf\r\nContent-Transfer-Encoding: base64\r\n"
                        + "Content-Disposition: attachment; filename*=utf-8''r%C3%A9sum%C3%A9.pdf\r\n\r\nUERG\r\n", message, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/csv; charset=utf-8; name=\"a.csv\"\r\n", message, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/x-tea; name=\"data\"\r\n", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ from: \"ann@example.com\", to: \"bob@example.com\", subjet: \"Hi\" }", "'subjet' is not a field of a message; did you mean subject?")]
    [InlineData("{ from: \"ann@example.com\", to: \"bob@example.com\", x: 1 }",
        "'x' is not a field of a message, whose fields are from, to, cc, bcc, replyTo, subject, text, html, headers and attachments.")]
    [InlineData("{ from: \"ann@example.com\" }", "A message must have at least one recipient, in to, cc or bcc.")]
    [InlineData("{ to: \"bob@example.com\" }", "A message must have from, its sender's address.")]
    [InlineData("{ from: \"Ann <ann@>\", to: \"bob@example.com\" }",
        "The message's from has 'Ann <ann@>', which is not an address, \"ann@example.com\" or \"Ann <ann@example.com>\".")]
    [InlineData("{ from: \"ann@example.com\", to: \"józef@example.com\" }",
        "The message's to has 'józef@example.com', an address that is not ASCII, which mail cannot use without SMTPUTF8, not built yet.")]
    [InlineData("{ from: \"ann@example.com\", to: [\"bob@example.com\", 7] }", "The message's to must hold addresses, each a string, not the integer 7.")]
    [InlineData("{ from: \"ann@example.com\", to: \"bob@example.com\", subject: \"a\\nb\" }", "The message's subject must hold no line break.")]
    [InlineData("{ from: \"ann@example.com\", to: \"bob@example.com\", text: 5 }",
        "The first argument of 'send', the message, must give text a string, not the integer 5.")]
    [InlineData("{ from: \"ann@example.com\", to: \"bob@example.com\", headers: { \"Subject\": \"x\" } }",
        "The message's headers may not give 'Subject', which the message itself gives.")]
    [InlineData("{ from: \"ann@example.com\", to: \"bob@example.com\", headers: { \"X-Tag\": \"a\\r\\nBcc: eve@example.com\" } }",
        "The message's header 'X-Tag' must hold no line break.")]
    [InlineData("{ from: \"ann@example.com\", to: \"bob@example.com\", attachments: [{ nme: \"a.txt\", content: \"x\" }] }",
        "'nme' is not a field of an attachment; did you mean name?")]
    [InlineData("{ from: \"ann@example.com\", to: \"bob@example.com\", attachments: [{ content: \"x\" }] }", "An attachment with content must have a name.")]
    [InlineData("{ from: \"ann@example.com\", to: \"bob@example.com\", attachments: [{ path: \"a.txt\", content: \"x\" }] }",
        "An attachment has a path or content, not both.")]
    [InlineData("{ from: \"ann@example.com\", to: \"bob@example.com\", attachments: [{ name: \"../a.txt\", content: \"x\" }] }",
        "An attachment's name must be a file's name, without a folder: '../a.txt'.")]
    [InlineData("5", "The first argument of 'send', the message, must be a map, { from: \"…\", to: \"…\", subject: \"…\", text: \"…\" }, not the integer 5.")]
    public void EveryKeyAndValueOfAMessageIsChecked(string message, string printed)
    {
        Assert.Equal((0, "WL3001 " + printed + "\n", ""), Weblingo($"try {{ await mail.send({message}); }} catch (e) {{ writeLn(e.code + \" \" + e.message); }}"));
    }

    [Theory]
    [InlineData("{ hots: \"x\" }", "'hots' is not a field of a server; did you mean host?")]
    [InlineData("{ port: 25 }", "A server that gives its port must give its host too; a configured server's settings are its configuration's.")]
    [InlineData("{ host: \"x\", tls: \"ssl\" }", "A server's tls must be \"starttls\", \"implicit\" or \"none\", not 'ssl'.")]
    [InlineData("{ host: \"x\", port: 70000 }", "A server's port must be an integer from 1 to 65535, not the integer 70000.")]
    [InlineData("{ host: \"x\", password: \"p\" }", "A server's password needs its user.")]
    [InlineData("{ server: \"default\", host: \"x\" }", "A server is a configured server, by its name, or a server of its own, by its host, not both.")]
    [InlineData("\"newsletter\"", "The message cannot be sent: 'newsletter' is not a mail server the configuration names.")]
    [InlineData("5", "The second argument of 'send', the server, must be a configured server's name, \"newsletter\", or a map, "
                     + "{ host: \"smtp.example.com\", … }, not the integer 5.")]
    public void EveryKeyAndValueOfAServerIsChecked(string server, string printed)
    {
        Assert.Equal((0, "WL3001 " + printed + "\n", ""),
            Weblingo($"try {{ await mail.send({Message}, {server}); }} catch (e) {{ writeLn(e.code + \" \" + e.message); }}"));
    }

    [Fact]
    public void APasswordMayBeKeptInAnEnvironmentVariableOrAFile()
    {
        var server = Server();
        var variable = "WEBLINGO_TEST_SMTP_" + Guid.NewGuid().ToString("N");
        Configure(server, $"\"user\": \"ann\", \"password\": {{ \"env\": \"{variable}\" }}");
        Assert.Equal((0, $"The message cannot be sent: the mail server 'default''s password cannot be read: the environment variable {variable} is "
                         + "not set.\n", ""),
            Weblingo($"try {{ await mail.send({Message}); }} catch (MailError e) {{ writeLn(e.message); }}"));
        Environment.SetEnvironmentVariable(variable, "secret");
        try
        {
            Assert.Equal((0, "sent\n", ""), Weblingo($"await mail.send({Message}); writeLn(\"sent\");"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
        Directory.CreateDirectory(Path.Combine(_folder, "secrets"));
        File.WriteAllText(Path.Combine(_folder, "secrets", "smtp.txt"), "secret\r\n");
        Configure(server, "\"user\": \"ann\", \"password\": { \"file\": \"secrets/smtp.txt\" }");
        Assert.Equal((0, "sent\n", ""), Weblingo($"await mail.send({Message}); writeLn(\"sent\");"));
        Assert.Equal(2, server.Messages.Count);
        Configure(server, "\"user\": \"ann\", \"password\": { \"file\": \"secrets/none.txt\" }");
        Assert.Equal((0, "The message cannot be sent: the mail server 'default''s password cannot be read: the file secrets/none.txt cannot be "
                         + "read.\n", ""),
            Weblingo($"try {{ await mail.send({Message}); }} catch (MailError e) {{ writeLn(e.message); }}"));
    }

    [Fact]
    public void ADatabasesConnectionStringMayBeKeptInAnEnvironmentVariable()
    {
        var variable = "WEBLINGO_TEST_DB_" + Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(_folder, "weblingo.config"), $"{{ \"databases\": {{ \"shop\": {{ \"env\": \"{variable}\" }} }} }}");
        const string open = "import { Database } from \"std:db\"; try { var db = await Database.open(\"shop\"); "
                            + "writeLn((await db.query(\"select 1 as one\"))[0][\"one\"]); db.dispose(); } catch (e) { writeLn([e.code, e.message]); }";
        Assert.Equal((0, $"[\"WL3015\", \"The database 'shop''s connection string cannot be read: the environment variable {variable} is not set.\"]\n", ""),
            Weblingo(open));
        Environment.SetEnvironmentVariable(variable, "sqlite:shop.db");
        try
        {
            Assert.Equal((0, "1\n", ""), Weblingo(open));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData("{ \"mail\": [] }", "\"mail\" is an object, each server's name and its settings")]
    [InlineData("{ \"mail\": { \"default\": { \"port\": 25 } } }", "\"mail\"'s \"default\" gives its \"host\".")]
    [InlineData("{ \"mail\": { \"default\": { \"host\": \"x\", \"password\": \"hunter2\" } } }", "\"mail\"'s \"default\"'s \"password\" needs its \"user\".")]
    [InlineData("{ \"mail\": { \"default\": { \"host\": \"x\", \"colour\": 1 } } }", "'colour' is not one, or not given as one.")]
    [InlineData("{ \"mail\": { \"default\": { \"host\": \"x\", \"tls\": \"ssl\" } } }", "'tls' is not one, or not given as one.")]
    [InlineData("{ \"mail\": { \"default\": { \"host\": \"x\", \"user\": \"u\", \"password\": { \"vault\": \"x\" } } } }",
        "\"mail\"'s \"default\"'s \"password\" is a string, or where it is kept, { \"env\": \"NAME\" } or { \"file\": \"../secrets/name.txt\" }.")]
    [InlineData("{ \"databases\": { \"shop\": { \"env\": \"\" } } }", "\"databases\"'s \"shop\" is a string, or where it is kept")]
    public void AConfigurationsMailServersAreChecked(string configuration, string expected)
    {
        File.WriteAllText(Path.Combine(_folder, "weblingo.config"), configuration);
        var (exit, output, error) = Weblingo("writeLn(1);");
        Assert.Equal((WebLingoCommandLine.FileUnreadable, ""), (exit, output));
        Assert.Contains(expected, error, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", error, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotedPrintableKeepsItsLinesShortAndItsSpacesAtTheirEnds()
    {
        var encoded = WebLingoMimeWriter.QuotedPrintable(new string('a', 100) + " \nend\t\r\nx=y");
        Assert.All(encoded.Split("\r\n"), line => Assert.True(line.Length <= 76, line));
        Assert.Equal(new string('a', 75) + "=\r\n" + new string('a', 25) + "=20\r\nend=09\r\nx=3Dy", encoded);
    }

    [Fact]
    public void AMessageWritesAPasswordNowhere()
    {
        var secret = new WebLingoSecret(WebLingoSecretSource.Given, "hunter2");
        var server = new WebLingoMailServer("smtp.example.com", 587, WebLingoMailTls.StartTls, "ann", secret, null);
        Assert.DoesNotContain("hunter2", server.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", secret.ToString(), StringComparison.Ordinal);
    }
}
