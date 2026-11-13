using WebLingo.Core;
namespace WebLingo.Language;

public enum WsTokenKind
{
    EndOfFile, Bad, Identifier, IntegerLiteral, StringLiteral,
    Var, Const, Let, Plus, Minus, Star, Slash, Equals, Semicolon,
    OpenParen, CloseParen, Comma
}

/// <summary>
/// A token. <paramref name="IntegerValue"/> is the value of an <see cref="WsTokenKind.IntegerLiteral"/>, which the
/// lexer has checked to be in range; it is 0 for every other kind.
/// </summary>
public readonly record struct WsToken(WsTokenKind Kind, string Text, WsSourceSpan Span, long IntegerValue = 0);

public interface IWsLexer
{
    WsToken NextToken();
    IReadOnlyList<WsDiagnostic> Diagnostics { get; }
}
public interface IWsParser { WsSyntaxTree ParseCompilationUnit(); }
public sealed record WsSyntaxTree(IReadOnlyList<WsDiagnostic> Diagnostics);
