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
    /// <summary>A declaration without an initializer: <c>var x;</c> or <c>const x;</c>.</summary>
    public const string MissingInitializer = "WS1006";
    /// <summary>The left side of <c>=</c> is not a variable (proposed).</summary>
    public const string InvalidAssignmentTarget = "WS1007";
    /// <summary>Source nested too deeply to compile safely (proposed).</summary>
    public const string NestingTooDeep = "WS1008";
    /// <summary>An invalid escape in a string literal.</summary>
    public const string InvalidEscape = "WS1009";
    /// <summary>A string literal not closed on its line.</summary>
    public const string UnterminatedString = "WS1010";

    /// <summary>Undefined identifier.</summary>
    public const string UndefinedIdentifier = "WS2001";
    /// <summary>Duplicate declaration.</summary>
    public const string DuplicateDeclaration = "WS2002";
    /// <summary>Assignment to constant.</summary>
    public const string AssignmentToConstant = "WS2003";
    /// <summary>Read before initialization.</summary>
    public const string ReadBeforeInitialization = "WS2004";
    /// <summary>A call of something that is not a function.</summary>
    public const string NotAFunction = "WS2005";
    /// <summary>A call with the wrong number of arguments.</summary>
    public const string WrongArgumentCount = "WS2006";
    /// <summary>A <c>break</c> or <c>continue</c> outside a loop (proposed).</summary>
    public const string NotInLoop = "WS2007";
    /// <summary>A <c>return</c> outside a function (proposed).</summary>
    public const string ReturnOutsideFunction = "WS2008";
    /// <summary><c>this</c> outside a method or a constructor (approved, section 3.7).</summary>
    public const string ThisOutsideMethod = "WS2009";
    /// <summary><c>throw;</c> alone outside a <c>catch</c> block (proposed, section 3.7).</summary>
    public const string ThrowOutsideCatch = "WS2010";
    /// <summary>A class declaring both <c>dispose()</c> and <c>destructor()</c> (approved, section 3.7).</summary>
    public const string DisposeAndDestructor = "WS2011";
    /// <summary>A <c>break</c>, <c>continue</c> or <c>return</c> that would leave a <c>finally</c> block (approved, section 3.7).</summary>
    public const string JumpOutOfFinally = "WS2012";
    /// <summary>A class that extends itself, directly or through others (proposed).</summary>
    public const string ClassExtendsItself = "WS2013";
    /// <summary><c>await</c> outside an <c>async</c> function and a script's top level (proposed, section 3.8).</summary>
    public const string AwaitOutsideAsync = "WS2014";

    /// <summary>Invalid operation.</summary>
    public const string InvalidOperation = "WS3001";
    /// <summary>An integer result outside int64 (proposed).</summary>
    public const string IntegerOverflow = "WS3002";
    /// <summary>An execution stopped for exceeding its instruction or resource budget (proposed).</summary>
    public const string BudgetExceeded = "WS3003";
    /// <summary>An execution stopped because its host cancelled it (proposed).</summary>
    public const string Cancelled = "WS3004";
    /// <summary>An index outside an array or a string (approved, section 3.6).</summary>
    public const string IndexOutOfRange = "WS3005";
    /// <summary>A member a value does not have (approved, section 3.6).</summary>
    public const string NoSuchMember = "WS3006";
    /// <summary>A key a map does not have (approved, section 3.6).</summary>
    public const string NoSuchKey = "WS3007";
    /// <summary>An exception a program threw, which nothing caught (approved, section 3.7).</summary>
    public const string UncaughtException = "WS3008";
    /// <summary>A program waiting for a task that nothing will complete, such as two that await each other (proposed, section 3.8).</summary>
    public const string Deadlock = "WS3009";

    /// <summary>Bytecode verification failure.</summary>
    public const string BytecodeVerificationFailure = "WS4001";
    /// <summary>A program too large for the bytecode, such as one needing more registers than a function has (proposed).</summary>
    public const string ProgramTooLarge = "WS4002";
}
