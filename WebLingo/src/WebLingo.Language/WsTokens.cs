using WebLingo.Core;
namespace WebLingo.Language;

public enum WsTokenKind
{
    EndOfFile, Bad, Identifier, IntegerLiteral, Binary64Literal, StringLiteral,
    Var, Const, Let, Infinity, NaN, True, False, Null, Plus, Minus, Star, Slash, Equals, Semicolon,
    EqualEqual, BangEqual, Less, LessEqual, Greater, GreaterEqual, Bang, AmpAmp, PipePipe,
    OpenParen, CloseParen, OpenBrace, CloseBrace, Comma
}

/// <summary>
/// A token. <paramref name="IntegerValue"/> is the value of an <see cref="WsTokenKind.IntegerLiteral"/>, which the
/// lexer has checked to be in range, or the IEEE 754 bits of a <see cref="WsTokenKind.Binary64Literal"/>, which it has
/// checked to be finite; it is 0 for every other kind.
/// </summary>
public readonly record struct WsToken(WsTokenKind Kind, string Text, WsSourceSpan Span, long IntegerValue = 0)
{
    public double Binary64Value => BitConverter.Int64BitsToDouble(IntegerValue);
}

public interface IWsLexer
{
    WsToken NextToken();
    IReadOnlyList<WsDiagnostic> Diagnostics { get; }
}
public interface IWsParser { WsSyntaxTree ParseCompilationUnit(); }

/// <summary>A parsed source text: its tree, and the lexer's and the parser's diagnostics in source order.</summary>
public sealed record WsSyntaxTree(WsSourceText Source, WsCompilationUnitSyntax Root, IReadOnlyList<WsDiagnostic> Diagnostics)
{
    public bool HasErrors
    {
        get
        {
            foreach (var diagnostic in Diagnostics)
            {
                if (diagnostic.Severity == WsDiagnosticSeverity.Error) return true;
            }
            return false;
        }
    }
}
