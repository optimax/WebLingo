namespace WebLingo.Core;

public enum WebLingoDiagnosticSeverity { Info, Warning, Error }
public readonly record struct WebLingoSourceSpan(int Start, int Length);
public readonly record struct WebLingoSourceLocation(int Offset, int Line, int Column);
public sealed record WebLingoDiagnostic(string Code, WebLingoDiagnosticSeverity Severity, string Message, WebLingoSourceSpan Span)
{
    /// <summary>The source the span is in, when it is not the program's own: a module it imports (section 3.9).</summary>
    public string? Source { get; init; }

    /// <summary>The actual class of an uncaught Error instance, when one was thrown.</summary>
    public string? ExceptionClass { get; init; }

    /// <summary>
    /// For a runtime error nothing caught, the calls it was raised in, the innermost first, where the VM knows them;
    /// null otherwise (specification, section 6.12, "Logging, parts", part 2).
    /// </summary>
    public IReadOnlyList<WebLingoStackFrame>? Stack { get; init; }
}

/// <summary>
/// One call an error was raised in: its function's name, as compiled, <c>function</c> for one without a name, null for a
/// file's top level, the program's or a module's; and where it was, its source, null for the program's own, as a
/// diagnostic's, and its span there.
/// </summary>
public sealed record WebLingoStackFrame(string? Function, string? Source, WebLingoSourceSpan Span);
