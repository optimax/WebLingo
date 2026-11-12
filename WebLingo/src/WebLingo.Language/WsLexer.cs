using WebLingo.Core;
namespace WebLingo.Language;

/// <summary>Minimal M0.6.1 lexer seed; not yet the complete WebLingo grammar.</summary>
public sealed class WsLexer(string source) : IWsLexer
{
    private int _position;
    public WsToken NextToken()
    {
        while (_position < source.Length && char.IsWhiteSpace(source[_position])) _position++;
        var start = _position;
        if (start == source.Length) return Token(WsTokenKind.EndOfFile, start);
        var c = source[_position];
        if (char.IsAsciiLetter(c) || c == '_')
        {
            _position++;
            while (_position < source.Length && (char.IsAsciiLetterOrDigit(source[_position]) || source[_position] == '_')) _position++;
            var text = source[start.._position];
            return new WsToken(text switch
            {
                "var" => WsTokenKind.Var,
                "const" => WsTokenKind.Const,
                "let" => WsTokenKind.Let, // Reserved/unsupported: never a declaration keyword.
                _ => WsTokenKind.Identifier
            }, text, new(start, _position - start));
        }
        if (char.IsAsciiDigit(c))
        {
            _position++;
            while (_position < source.Length && char.IsAsciiDigit(source[_position])) _position++;
            return Token(WsTokenKind.IntegerLiteral, start);
        }
        _position++;
        return Token(c switch
        {
            '+' => WsTokenKind.Plus, '-' => WsTokenKind.Minus,
            '*' => WsTokenKind.Star, '/' => WsTokenKind.Slash,
            '=' => WsTokenKind.Equals, ';' => WsTokenKind.Semicolon,
            '(' => WsTokenKind.OpenParen, ')' => WsTokenKind.CloseParen,
            _ => WsTokenKind.Bad
        }, start);
    }
    private WsToken Token(WsTokenKind kind, int start) =>
        new(kind, source[start.._position], new(start, _position - start));
}
