using WebLingo.Core;
namespace WebLingo.Language;

public enum WebLingoTokenKind
{
    EndOfFile, Bad, Identifier, IntegerLiteral, Binary64Literal, StringLiteral,
    Var, Const, Let, Infinity, NaN, True, False, Null, If, Else, While, For, Do, Break, Continue, Function, Return, Plus, Minus, Star, Slash, Equals, Semicolon,
    EqualEqual, BangEqual, Less, LessEqual, Greater, GreaterEqual, Bang, AmpAmp, PipePipe,
    OpenParen, CloseParen, OpenBrace, CloseBrace, Comma, Dot, OpenBracket, CloseBracket, Colon, Foreach, HashBrace, QuestionBracket, Class, New, This, QuestionDot, Extends, Super, Using, Static, Try, Catch, Finally, Throw, Is, Async, Await, Extend, Import, Export, Param, Arrow, InterpolatedString
}

/// <summary>
/// A token. <paramref name="IntegerValue"/> is the value of an <see cref="WebLingoTokenKind.IntegerLiteral"/>, which the
/// lexer has checked to be in range, or the IEEE 754 bits of a <see cref="WebLingoTokenKind.Binary64Literal"/>, which it has
/// checked to be finite; it is 0 for every other kind. <paramref name="StringValue"/> is the text a
/// <see cref="WebLingoTokenKind.StringLiteral"/> means, its escapes decoded. <paramref name="Parts"/> are a
/// <see cref="WebLingoTokenKind.InterpolatedString"/>'s, in order.
/// </summary>
public readonly record struct WebLingoToken(
    WebLingoTokenKind Kind, string Text, WebLingoSourceSpan Span, long IntegerValue = 0, string? StringValue = null,
    IReadOnlyList<WebLingoInterpolationPart>? Parts = null)
{
    public double Binary64Value => BitConverter.Int64BitsToDouble(IntegerValue);
}

/// <summary>
/// A part of an interpolated string (specification, section 3.5): its text, as it means it, or an interpolation's code,
/// its tokens, and the span of the format after its <c>:</c>, if it has one.
/// </summary>
public sealed record WebLingoInterpolationPart(string? Text, IReadOnlyList<WebLingoToken>? Code, WebLingoSourceSpan Span)
{
    public WebLingoSourceSpan? Format { get; init; }
}

public interface IWebLingoLexer
{
    WebLingoToken NextToken();
    IReadOnlyList<WebLingoDiagnostic> Diagnostics { get; }
}
public interface IWebLingoParser { WebLingoSyntaxTree ParseCompilationUnit(); }

/// <summary>A parsed source text: its tree, and the lexer's and the parser's diagnostics in source order.</summary>
public sealed record WebLingoSyntaxTree(WebLingoSourceText Source, WebLingoCompilationUnitSyntax Root, IReadOnlyList<WebLingoDiagnostic> Diagnostics)
{
    public bool HasErrors
    {
        get
        {
            foreach (var diagnostic in Diagnostics)
            {
                if (diagnostic.Severity == WebLingoDiagnosticSeverity.Error) return true;
            }
            return false;
        }
    }
}
