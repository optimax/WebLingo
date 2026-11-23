using System.Security.Cryptography;
using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// The platform's mail on .NET (specification, section 7.17): the servers a configuration names, each password read where
/// the configuration keeps it when it is used (<see cref="IWebLingoSecrets"/>), and a message written by
/// <see cref="WebLingoMimeWriter"/>, dated now on the server's clock, sent by <see cref="WebLingoSmtpClient"/> to every
/// address it is sent to, copied to and copied to unseen, each once. Its <c>Message-ID</c> is 128 random bits and the
/// sender's domain.
/// </summary>
public sealed class DotNetMail(IReadOnlyDictionary<string, WebLingoMailServer> configured, IWebLingoSecrets secrets) : IWebLingoMail
{
    /// <summary>The server a send that names none uses, where the configuration names more than one.</summary>
    public const string DefaultServer = "default";

    public string Send(string? server, string? token, WebLingoMailMessage message)
    {
        var name = server ?? (configured.ContainsKey(DefaultServer) || configured.Count != 1 ? DefaultServer : configured.Keys.First());
        if (!configured.TryGetValue(name, out var chosen))
        {
            throw new WebLingoMailException(WebLingoMailErrorKind.Invalid, server is not null
                ? $"'{server}' is not a mail server the configuration names"
                : configured.Count == 0
                    ? "the configuration names no mail server, \"mail\": { \"default\": { \"host\": \"smtp.example.com\" } }"
                    : "the configuration names several mail servers, none of them \"default\", so that a send names one");
        }
        string? password = null;
        if (chosen.Password is { } secret)
        {
            try
            {
                password = secrets.Read(secret);
            }
            catch (WebLingoSecretException failure)
            {
                throw new WebLingoMailException(WebLingoMailErrorKind.Failed, $"the mail server '{name}''s password cannot be read: {failure.Message}");
            }
        }
        return Deliver(chosen, password, token, message);
    }

    public string Send(WebLingoMailServer server, string? token, WebLingoMailMessage message) =>
        Deliver(server, server.Password?.Text, token, message);

    private static string Deliver(WebLingoMailServer server, string? password, string? token, WebLingoMailMessage message)
    {
        if (server.User is not null && password is null && token is null)
        {
            throw new WebLingoMailException(WebLingoMailErrorKind.Invalid,
                $"the mail server {server.Host} signs in as a user, and is given neither a password nor a token");
        }
        var at = message.From.Address.LastIndexOf('@');
        var messageId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + message.From.Address[at..];
        var data = WebLingoMimeWriter.Write(message, DateTimeOffset.Now, messageId);
        var recipients = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in new[] { message.To, message.Cc, message.Bcc })
        {
            foreach (var address in list)
            {
                if (seen.Add(address.Address)) recipients.Add(address.Address);
            }
        }
        WebLingoSmtpClient.Send(server, password, token, message.From.Address, recipients, data);
        return messageId;
    }
}
