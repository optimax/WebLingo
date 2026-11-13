namespace WebLingo.Core;

/// <summary>
/// The diagnostic codes. A code, once assigned, keeps its meaning. The codes the specification suggests are in
/// its section 8.4; the others are proposed (section 15.2).
/// </summary>
public static class WsDiagnosticCodes
{
    /// <summary>Unexpected token.</summary>
    public const string UnexpectedToken = "WS1001";
    /// <summary>Missing semicolon.</summary>
    public const string MissingSemicolon = "WS1002";
    /// <summary>A character that cannot begin any token (proposed).</summary>
    public const string InvalidCharacter = "WS1003";
    /// <summary>A malformed numeric literal, or one out of range (proposed).</summary>
    public const string InvalidNumericLiteral = "WS1004";
    /// <summary>A <c>/*</c> comment without its <c>*/</c> (proposed).</summary>
    public const string UnterminatedComment = "WS1005";
    /// <summary>A <c>const</c> declaration without an initializer (proposed).</summary>
    public const string MissingConstantInitializer = "WS1006";
    /// <summary>The left side of <c>=</c> is not a variable (proposed).</summary>
    public const string InvalidAssignmentTarget = "WS1007";
    /// <summary>Source nested too deeply to compile safely (proposed).</summary>
    public const string NestingTooDeep = "WS1008";

    /// <summary>Undefined identifier.</summary>
    public const string UndefinedIdentifier = "WS2001";
    /// <summary>Duplicate declaration.</summary>
    public const string DuplicateDeclaration = "WS2002";
    /// <summary>Assignment to constant.</summary>
    public const string AssignmentToConstant = "WS2003";
    /// <summary>Read before initialization.</summary>
    public const string ReadBeforeInitialization = "WS2004";

    /// <summary>Invalid operation.</summary>
    public const string InvalidOperation = "WS3001";

    /// <summary>Bytecode verification failure.</summary>
    public const string BytecodeVerificationFailure = "WS4001";
}
