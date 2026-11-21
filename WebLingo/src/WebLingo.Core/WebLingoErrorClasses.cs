namespace WebLingo.Core;

/// <summary>The diagnostic codes with a corresponding built-in class that extends Error.</summary>
public static class WebLingoErrorClasses
{
    private static readonly (string Name, string Code)[] Classes =
    [
        ("InitializationError", WebLingoDiagnosticCodes.ReadBeforeInitialization),
        ("CallError", WebLingoDiagnosticCodes.NotAFunction),
        ("ArgumentError", WebLingoDiagnosticCodes.WrongArgumentCount),
        ("TypeError", WebLingoDiagnosticCodes.InvalidOperation),
        ("OverflowError", WebLingoDiagnosticCodes.IntegerOverflow),
        ("IndexError", WebLingoDiagnosticCodes.IndexOutOfRange),
        ("MemberError", WebLingoDiagnosticCodes.NoSuchMember),
        ("KeyError", WebLingoDiagnosticCodes.NoSuchKey),
        ("TimeoutError", WebLingoDiagnosticCodes.Timeout),
        ("IOError", WebLingoDiagnosticCodes.IOFailure),
        ("DivisionByZeroError", WebLingoDiagnosticCodes.DivisionByZero),
        ("NetworkError", WebLingoDiagnosticCodes.NetworkFailure),
        ("DatabaseError", WebLingoDiagnosticCodes.DatabaseFailure),
        ("FilePermissionError", WebLingoDiagnosticCodes.FilePermissionDenied),
        ("ComError", WebLingoDiagnosticCodes.ComFailure),
        ("NetworkPermissionError", WebLingoDiagnosticCodes.NetworkPermissionDenied),
        ("ComPermissionError", WebLingoDiagnosticCodes.ComPermissionDenied),
        ("EnvironmentPermissionError", WebLingoDiagnosticCodes.EnvironmentPermissionDenied),
        ("DeadlockError", WebLingoDiagnosticCodes.WaitsForever),
        ("PlatformError", WebLingoDiagnosticCodes.PlatformNotSupported)
    ];

    public static ReadOnlySpan<(string Name, string Code)> BuiltIn => Classes;

    /// <summary>The built-in exception class for a diagnostic code, if one exists.</summary>
    public static string? NameOf(string code)
    {
        foreach (var (name, assignedCode) in Classes)
        {
            if (assignedCode == code) return name;
        }
        return null;
    }
}
