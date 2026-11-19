namespace WebLingo.Platform.Abstractions;

/// <summary>
/// A script's console input, its standard input, which a host that has one gives, <c>weblingo</c>, interactive or piped;
/// a site's pages and handlers, under <c>wasp</c>, have none (the owner's answers, 19 November 2026, "Console Input").
/// Each read blocks until a line has come, so that a script's asynchronous reads run off its worker.
/// </summary>
public interface IWebLingoConsoleInput
{
    /// <summary>The next line, without its line break; null at the end of the input.</summary>
    string? ReadLine();

    /// <summary>
    /// The next line, as <see cref="ReadLine"/> reads it, but not shown as it is typed where the input is a console's,
    /// a password's; null at the end of the input.
    /// </summary>
    string? ReadPassword();
}
