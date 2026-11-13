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
        if (c is '"' or '\'') return ReadStringLiteral(start);
        var pair = (c, start + 1 < source.Length ? source[start + 1] : '\0') switch
        {
            ('=', '=') => WsTokenKind.EqualEqual, ('!', '=') => WsTokenKind.BangEqual,
            ('<', '=') => WsTokenKind.LessEqual, ('>', '=') => WsTokenKind.GreaterEqual,
            ('&', '&') => WsTokenKind.AmpAmp, ('|', '|') => WsTokenKind.PipePipe, ('#', '{') => WsTokenKind.HashBrace,
            ('?', '[') => WsTokenKind.QuestionBracket, ('?', '.') => WsTokenKind.QuestionDot,
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
            ',' => WsTokenKind.Comma, '.' => WsTokenKind.Dot, '[' => WsTokenKind.OpenBracket,
            ']' => WsTokenKind.CloseBracket, ':' => WsTokenKind.Colon,
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
            "if" => WsTokenKind.If,
            "else" => WsTokenKind.Else,
            "while" => WsTokenKind.While,
            "for" => WsTokenKind.For,
            "foreach" => WsTokenKind.Foreach,
            "do" => WsTokenKind.Do,
            "break" => WsTokenKind.Break,
            "continue" => WsTokenKind.Continue,
            "function" => WsTokenKind.Function,
            "class" => WsTokenKind.Class,
            "new" => WsTokenKind.New,
            "this" => WsTokenKind.This,
            "extends" => WsTokenKind.Extends,
            "super" => WsTokenKind.Super,
            "using" => WsTokenKind.Using,
            "static" => WsTokenKind.Static,
            "try" => WsTokenKind.Try,
            "catch" => WsTokenKind.Catch,
            "finally" => WsTokenKind.Finally,
            "throw" => WsTokenKind.Throw,
            "return" => WsTokenKind.Return,
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

    /// <summary>
    /// A string literal in either quote, on one line (specification, section 3.5). An invalid escape (WS1009) is
    /// reported and the literal read on; a literal not closed on its line is WS1010. Either makes it a bad token.
    /// </summary>
    private WsToken ReadStringLiteral(int start)
    {
        var quote = source[start];
        _position++;
        var text = new System.Text.StringBuilder();
        var valid = true;
        while (true)
        {
            if (_position >= source.Length || source[_position] is '\r' or '\n')
            {
                Report(WsDiagnosticCodes.UnterminatedString, $"This string has no closing {quote} on its line.",
                    new WsSourceSpan(start, _position - start));
                return Token(WsTokenKind.Bad, start);
            }
            var c = source[_position];
            if (c == quote)
            {
                _position++;
                break;
            }
            if (c != '\\')
            {
                text.Append(c);
                _position++;
                continue;
            }
            var escapeStart = _position;
            _position++;
            var next = _position < source.Length ? source[_position] : '\0';
            var simple = next switch
            {
                'n' => "\n", 'r' => "\r", 't' => "\t", '0' => "\0", '\\' => "\\", '\'' => "'", '"' => "\"",
                _ => null
            };
            if (simple is not null)
            {
                text.Append(simple);
                _position++;
                continue;
            }
            if (next == 'u' && ReadUnicodeEscape(text)) continue;
            if (next is not ('\r' or '\n') && _position < source.Length) _position++;
            Report(WsDiagnosticCodes.InvalidEscape,
                "This escape is not valid: use \\n, \\r, \\t, \\0, \\\\, \\', \\\" or \\u{…} with a Unicode scalar value.",
                new WsSourceSpan(escapeStart, _position - escapeStart));
            valid = false;
        }
        if (!valid) return Token(WsTokenKind.Bad, start);
        return new WsToken(WsTokenKind.StringLiteral, source[start.._position], new WsSourceSpan(start, _position - start),
            StringValue: text.ToString());
    }

    /// <summary>
    /// <c>\u{…}</c>, at the <c>u</c>: one to six hex digits naming a Unicode scalar value (not a surrogate). On
    /// success it appends the character and moves past the <c>}</c>; otherwise it moves nothing.
    /// </summary>
    private bool ReadUnicodeEscape(System.Text.StringBuilder text)
    {
        var at = _position + 1;
        if (at >= source.Length || source[at] != '{') return false;
        at++;
        var value = 0;
        var digits = 0;
        while (at < source.Length && digits < 7 && HexValue(source[at]) is int digit)
        {
            value = value * 16 + digit;
            digits++;
            at++;
        }
        if (digits is 0 or > 6 || at >= source.Length || source[at] != '}') return false;
        if (value > 0x10FFFF || value is >= 0xD800 and <= 0xDFFF) return false;
        if (value < 0x10000)
        {
            text.Append((char)value);
        }
        else
        {
            text.Append((char)(0xD800 + ((value - 0x10000) >> 10)));
            text.Append((char)(0xDC00 + ((value - 0x10000) & 0x3FF)));
        }
        _position = at + 1;
        return true;
    }

    private static int? HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => null
    };

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
