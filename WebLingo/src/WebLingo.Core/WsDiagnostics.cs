namespace WebLingo.Core;

public enum WsDiagnosticSeverity { Info, Warning, Error }
public readonly record struct WsSourceSpan(int Start, int Length);
public readonly record struct WsSourceLocation(int Offset, int Line, int Column);
public sealed record WsDiagnostic(string Code, WsDiagnosticSeverity Severity, string Message, WsSourceSpan Span);
