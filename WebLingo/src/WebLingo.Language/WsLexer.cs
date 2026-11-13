using WebLingo.Core;
namespace WebLingo.Language;

/// <summary>
/// The M0.6 lexer (specification, section 8.4, "Lexer"; its choices are in section 15.2). It reads identifiers,
/// the keywords <c>var</c>, <c>const</c>, <c>Infinity</c> and <c>NaN</c> and the reserved <c>let</c>, decimal integer
/// and binary64 literals (<c>1.5</c>, <c>1e21</c>, <c>2.5E-3</c>), operators, and
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
        if (IsDigit(c)) return ReadNumericLiteral(start);
        var pair = (c, start + 1 < source.Length ? source[start + 1] : '\0') switch
        {
            ('=', '=') => WsTokenKind.EqualEqual, ('!', '=') => WsTokenKind.BangEqual,
            ('<', '=') => WsTokenKind.LessEqual, ('>', '=') => WsTokenKind.GreaterEqual,
            ('&', '&') => WsTokenKind.AmpAmp, ('|', '|') => WsTokenKind.PipePipe,
            _ => WsTokenKind.Bad
        };
        if (pair != WsTokenKind.Bad)
        {
            _position += 2;
            return Token(pair, start);
        }
        var kind = c switch
        {
            '<' => WsTokenKind.Less, '>' => WsTokenKind.Greater, '!' => WsTokenKind.Bang,
            '{' => WsTokenKind.OpenBrace, '}' => WsTokenKind.CloseBrace, '+' => WsTokenKind.Plus, '-' => WsTokenKind.Minus,
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
            "Infinity" => WsTokenKind.Infinity,
            "NaN" => WsTokenKind.NaN,
            "true" => WsTokenKind.True,
            "false" => WsTokenKind.False,
            "null" => WsTokenKind.Null,
            _ => WsTokenKind.Identifier
        };
        return new WsToken(kind, text, new WsSourceSpan(start, _position - start));
    }

    /// <summary>
    /// Digits, then optionally a fraction (<c>.</c> and digits) and an exponent (<c>e</c> or <c>E</c>, an optional sign,
    /// and digits). With either, the literal is a binary64, rounded to the nearest; otherwise an int64.
    /// </summary>
    private WsToken ReadNumericLiteral(int start)
    {
        SkipDigits();
        var isBinary64 = false;
        if (At(0) == '.' && IsDigit(At(1)))
        {
            _position++;
            SkipDigits();
            isBinary64 = true;
        }
        if (At(0) is 'e' or 'E' && (IsDigit(At(1)) || (At(1) is '+' or '-' && IsDigit(At(2)))))
        {
            _position += IsDigit(At(1)) ? 1 : 2;
            SkipDigits();
            isBinary64 = true;
        }
        if (_position < source.Length && IsIdentifierPart(source[_position]))
        {
            // Digits run into letters (10abc, or the decimal suffix of 10m, which M0.6 does not read yet).
            while (_position < source.Length && IsIdentifierPart(source[_position])) _position++;
            var text = source[start.._position];
            Report(WsDiagnosticCodes.InvalidNumericLiteral, $"'{text}' is not a valid number.",
                new WsSourceSpan(start, _position - start));
            return Token(WsTokenKind.Bad, start);
        }
        if (isBinary64) return ReadBinary64(start);

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

    private WsToken ReadBinary64(int start)
    {
        var text = source[start.._position];
        var value = double.Parse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        var span = new WsSourceSpan(start, _position - start);
        if (double.IsInfinity(value))
        {
            Report(WsDiagnosticCodes.InvalidNumericLiteral,
                $"The number {text} is too large for a binary64; write Infinity for infinity.", span);
            return Token(WsTokenKind.Bad, start);
        }
        return new WsToken(WsTokenKind.Binary64Literal, text, span, BitConverter.DoubleToInt64Bits(value));
    }

    private void SkipDigits()
    {
        while (_position < source.Length && IsDigit(source[_position])) _position++;
    }

    private char At(int offset) => _position + offset < source.Length ? source[_position + offset] : '\0';

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
