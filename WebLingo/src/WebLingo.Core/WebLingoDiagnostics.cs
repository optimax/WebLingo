namespace WebLingo.Core;

public enum WebLingoDiagnosticSeverity { Info, Warning, Error }
public readonly record struct WebLingoSourceSpan(int Start, int Length);
public readonly record struct WebLingoSourceLocation(int Offset, int Line, int Column);
public sealed record WebLingoDiagnostic(string Code, WebLingoDiagnosticSeverity Severity, string Message, WebLingoSourceSpan Span);
