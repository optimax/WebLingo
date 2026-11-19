namespace WebLingo.Platform.Abstractions;

/// <summary>A host that can run an executable and capture its output.</summary>
public interface IWebLingoProcessRunner
{
    WebLingoProcessResult Run(string program, IReadOnlyList<string> arguments, int timeoutMs, int maxOutputBytes);
}

public sealed record WebLingoProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class WebLingoProcessException(string message, bool timedOut = false) : Exception(message)
{
    public bool TimedOut { get; } = timedOut;
}
