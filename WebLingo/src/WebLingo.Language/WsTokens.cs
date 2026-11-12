using WebLingo.Core;
namespace WebLingo.Language;

public enum WsTokenKind
{
    EndOfFile, Bad, Identifier, IntegerLiteral, StringLiteral,
    Var, Const, Let, Plus, Minus, Star, Slash, Equals, Semicolon,
    OpenParen, CloseParen
}
public readonly record struct WsToken(WsTokenKind Kind, string Text, WsSourceSpan Span);
public interface IWsLexer { WsToken NextToken(); }
public interface IWsParser { WsSyntaxTree ParseCompilationUnit(); }
public sealed record WsSyntaxTree(IReadOnlyList<WsDiagnostic> Diagnostics);
