using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// Reads a configuration's secrets on .NET (specification, section 7.17, "Secrets"): one given as itself, an environment
/// variable of the process, or a file's text, its line breaks at its end left out, each read when it is used.
/// </summary>
public sealed class DotNetSecrets : IWebLingoSecrets
{
    public string Read(WebLingoSecret secret)
    {
        var text = secret.Source switch
        {
            WebLingoSecretSource.Variable => Environment.GetEnvironmentVariable(secret.Text)
                                             ?? throw new WebLingoSecretException($"{secret.Where} is not set"),
            WebLingoSecretSource.File => FromFile(secret),
            _ => secret.Text
        };
        if (text.Length == 0) throw new WebLingoSecretException($"{secret.Where} is empty");
        return text;
    }

    private static string FromFile(WebLingoSecret secret)
    {
        try
        {
            return File.ReadAllText(secret.Text).TrimEnd('\r', '\n');
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            throw new WebLingoSecretException($"{secret.Where} cannot be read");
        }
    }
}
