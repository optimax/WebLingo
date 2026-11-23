namespace WebLingo.Platform.Abstractions;

/// <summary>Where a secret is kept: in the configuration itself, in an environment variable, or in a file.</summary>
public enum WebLingoSecretSource { Given, Variable, File }

/// <summary>
/// A secret a configuration gives, a password or a connection string (specification, section 7.17, "Secrets"): its text,
/// <c>"password": "…"</c>; or where it is kept, an environment variable, <c>{ "env": "SMTP_PASSWORD" }</c>, or a file
/// outside the site, <c>{ "file": "../secrets/smtp.txt" }</c>, its full path, which the host reads when it is used, never
/// a script. <paramref name="Written"/> is a file's path as the configuration writes it, which a message names, never the
/// host's own path. Its text appears in no message, nor in <see cref="ToString"/>.
/// </summary>
public sealed record WebLingoSecret(WebLingoSecretSource Source, string Text, string? Written = null)
{
    /// <summary>A secret given as itself.</summary>
    public static WebLingoSecret Given(string text) => new(WebLingoSecretSource.Given, text);

    /// <summary>Where it is kept, for a message: never its text.</summary>
    public string Where => Source switch
    {
        WebLingoSecretSource.Variable => $"the environment variable {Text}",
        WebLingoSecretSource.File => Written is null ? "a file" : $"the file {Written}",
        _ => "the configuration"
    };

    /// <summary>Where it is kept, so that nothing that writes a record writes the secret.</summary>
    public override string ToString() => Where;
}

/// <summary>Reads a secret where its configuration keeps it, each time it is used, so that one changed since is read anew.</summary>
public interface IWebLingoSecrets
{
    /// <summary>The secret's text; a <see cref="WebLingoSecretException"/> where it cannot be read, or is empty.</summary>
    string Read(WebLingoSecret secret);
}

/// <summary>A secret that cannot be read, its message naming where it is kept, without a capital or a full stop.</summary>
public sealed class WebLingoSecretException(string message) : Exception(message);
