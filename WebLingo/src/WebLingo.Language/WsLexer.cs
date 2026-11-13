using WebLingo.Core;
namespace WebLingo.Language;

/// <summary>
/// The M0.6 lexer (specification, section 8.4, "Lexer"; its choices are in section 15.2). It reads identifiers,
/// the keywords <c>var</c> and <c>const</c> and the reserved <c>let</c>, decimal integer literals, operators, and
/// punctuation, and skips whitespace and comments. Malformed input gives a <see cref="WsTokenKind.Bad"/> token
/// and a diagnostic, and lexing goes on, so that one pass reports every lexical error.
/// </summary>
public sealed class WsLexer(string source) : IWsLexer
{
    private const string LargestInteger = "9223372036854775807";

    private readonly List<WsDiagnostic> _diagnostics = [];
    private int _position;

    public IReadOnlyList<WsDiagnostic> Diagnostics => _diagnostics;

    /// <summary>All the tokens of a source text, ending with its <see cref="WsTokenKind.EndOfFile"/>.</summary>
    public static IReadOnlyList<WsToken> Tokenize(string source, out IReadOnlyList<WsDiagnostic> diagnostics)
    {
        var lexer = new WsLexer(source);
        var tokens = new List<WsToken>();
        WsToken token;
        do
        {
            token = lexer.NextToken();
            tokens.Add(token);
        }
        while (token.Kind != WsTokenKind.EndOfFile);
        diagnostics = lexer.Diagnostics;
        return tokens;
    }

    public WsToken NextToken()
    {
        SkipWhitespaceAndComments();
        var start = _position;
        if (start >= source.Length) return new WsToken(WsTokenKind.EndOfFile, "", new WsSourceSpan(source.Length, 0));
        var c = source[start];
        if (IsIdentifierStart(c)) return ReadIdentifierOrKeyword(start);
        if (IsDigit(c)) return ReadIntegerLiteral(start);
        var kind = c switch
        {
            '+' => WsTokenKind.Plus, '-' => WsTokenKind.Minus,
            '*' => WsTokenKind.Star, '/' => WsTokenKind.Slash,
            '=' => WsTokenKind.Equals, ';' => WsTokenKind.Semicolon,
            '(' => WsTokenKind.OpenParen, ')' => WsTokenKind.CloseParen,
            ',' => WsTokenKind.Comma,
            _ => WsTokenKind.Bad
        };
        if (kind == WsTokenKind.Bad) return ReadInvalidCharacter(start);
        _position++;
        return Token(kind, start);
    }

    private void SkipWhitespaceAndComments()
    {
        while (_position < source.Length)
        {
            var c = source[_position];
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                _position++;
            }
            else if (c == '/' && Peek(1) == '/')
            {
                _position += 2;
                while (_position < source.Length && source[_position] is not ('\r' or '\n')) _position++;
            }
            else if (c == '/' && Peek(1) == '*')
            {
                var start = _position;
                _position += 2;
                while (_position < source.Length && !(source[_position] == '*' && Peek(1) == '/')) _position++;
                if (_position < source.Length)
                {
                    _position += 2;
                }
                else
                {
                    Report(WsDiagnosticCodes.UnterminatedComment, "The comment has no closing '*/'.",
                        new WsSourceSpan(start, _position - start));
                }
            }
            else
            {
                return;
            }
        }
    }

    private WsToken ReadIdentifierOrKeyword(int start)
    {
        _position++;
        while (_position < source.Length && IsIdentifierPart(source[_position])) _position++;
        var text = source[start.._position];
        var kind = text switch
        {
            "var" => WsTokenKind.Var,
            "const" => WsTokenKind.Const,
            "let" => WsTokenKind.Let, // Reserved: never a declaration keyword (WS-002.3).
            _ => WsTokenKind.Identifier
        };
        return new WsToken(kind, text, new WsSourceSpan(start, _position - start));
    }

    private WsToken ReadIntegerLiteral(int start)
    {
        while (_position < source.Length && IsDigit(source[_position])) _position++;
        if (_position < source.Length && IsIdentifierPart(source[_position]))
        {
            // Digits run into letters (10abc, or the decimal suffix of 10m, which M0.6 does not read yet).
            while (_position < source.Length && IsIdentifierPart(source[_position])) _position++;
            var text = source[start.._position];
            Report(WsDiagnosticCodes.InvalidNumericLiteral, $"'{text}' is not a valid number.",
                new WsSourceSpan(start, _position - start));
            return Token(WsTokenKind.Bad, start);
        }

        long value = 0;
        for (var i = start; i < _position; i++)
        {
            var digit = source[i] - '0';
            if (value > (long.MaxValue - digit) / 10)
            {
                Report(WsDiagnosticCodes.InvalidNumericLiteral,
                    $"The integer {source[start.._position]} is too large; the largest is {LargestInteger}.",
                    new WsSourceSpan(start, _position - start));
                return Token(WsTokenKind.Bad, start);
            }
            value = value * 10 + digit;
        }
        return new WsToken(WsTokenKind.IntegerLiteral, source[start.._position], new WsSourceSpan(start, _position - start),
            value);
    }

    private WsToken ReadInvalidCharacter(int start)
    {
        var c = source[start];
        int codePoint = c;
        _position++;
        if (char.IsHighSurrogate(c) && _position < source.Length && char.IsLowSurrogate(source[_position]))
        {
            codePoint = ((c - 0xD800) << 10) + (source[_position] - 0xDC00) + 0x10000;
            _position++;
        }
        var text = source[start.._position];
        // Control characters and lone surrogates are shown by their code only.
        var shown = codePoint < 0x20 || codePoint == 0x7F || (char.IsSurrogate(c) && text.Length == 1)
            ? ""
            : $"'{text}' ";
        Report(WsDiagnosticCodes.InvalidCharacter, $"The character {shown}(U+{codePoint:X4}) is not valid here.",
            new WsSourceSpan(start, _position - start));
        return Token(WsTokenKind.Bad, start);
    }

    private char Peek(int ahead) => _position + ahead < source.Length ? source[_position + ahead] : '\0';

    private WsToken Token(WsTokenKind kind, int start) =>
        new(kind, source[start.._position], new WsSourceSpan(start, _position - start));

    private void Report(string code, string message, WsSourceSpan span) =>
        _diagnostics.Add(new WsDiagnostic(code, WsDiagnosticSeverity.Error, message, span));

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
    private static bool IsIdentifierStart(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || IsDigit(c);
}
