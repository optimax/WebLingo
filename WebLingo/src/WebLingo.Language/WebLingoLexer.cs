using WebLingo.Core;
namespace WebLingo.Language;

/// <summary>
/// The M0.6 lexer (specification, section 8.4, "Lexer"; its choices are in section 15.2). It reads identifiers,
/// the keywords <c>var</c>, <c>const</c>, <c>Infinity</c> and <c>NaN</c> and the reserved <c>let</c>, decimal integer
/// and binary64 literals (<c>1.5</c>, <c>1e21</c>, <c>2.5E-3</c>), operators, and
/// punctuation, and skips whitespace and comments. Malformed input gives a <see cref="WebLingoTokenKind.Bad"/> token
/// and a diagnostic, and lexing goes on, so that one pass reports every lexical error.
/// </summary>
public sealed class WebLingoLexer(string source) : IWebLingoLexer
{
    private const string LargestInteger = "9223372036854775807";

    private readonly List<WebLingoDiagnostic> _diagnostics = [];
    private int _position;

    public IReadOnlyList<WebLingoDiagnostic> Diagnostics => _diagnostics;

    /// <summary>All the tokens of a source text, ending with its <see cref="WebLingoTokenKind.EndOfFile"/>.</summary>
    public static IReadOnlyList<WebLingoToken> Tokenize(string source, out IReadOnlyList<WebLingoDiagnostic> diagnostics)
    {
        var lexer = new WebLingoLexer(source);
        var tokens = new List<WebLingoToken>();
        WebLingoToken token;
        do
        {
            token = lexer.NextToken();
            tokens.Add(token);
        }
        while (token.Kind != WebLingoTokenKind.EndOfFile);
        diagnostics = lexer.Diagnostics;
        return tokens;
    }

    public WebLingoToken NextToken()
    {
        SkipWhitespaceAndComments();
        var start = _position;
        if (start >= source.Length) return new WebLingoToken(WebLingoTokenKind.EndOfFile, "", new WebLingoSourceSpan(source.Length, 0));
        var c = source[start];
        if (IsIdentifierStart(c)) return ReadIdentifierOrKeyword(start);
        if (IsDigit(c)) return ReadNumericLiteral(start);
        if (c is '"' or '\'') return ReadStringLiteral(start);
        var pair = (c, start + 1 < source.Length ? source[start + 1] : '\0') switch
        {
            ('=', '=') => WebLingoTokenKind.EqualEqual, ('!', '=') => WebLingoTokenKind.BangEqual,
            ('<', '=') => WebLingoTokenKind.LessEqual, ('>', '=') => WebLingoTokenKind.GreaterEqual,
            ('&', '&') => WebLingoTokenKind.AmpAmp, ('|', '|') => WebLingoTokenKind.PipePipe, ('#', '{') => WebLingoTokenKind.HashBrace,
            ('?', '[') => WebLingoTokenKind.QuestionBracket, ('?', '.') => WebLingoTokenKind.QuestionDot,
            _ => WebLingoTokenKind.Bad
        };
        if (pair != WebLingoTokenKind.Bad)
        {
            _position += 2;
            return Token(pair, start);
        }
        var kind = c switch
        {
            '<' => WebLingoTokenKind.Less, '>' => WebLingoTokenKind.Greater, '!' => WebLingoTokenKind.Bang,
            '{' => WebLingoTokenKind.OpenBrace, '}' => WebLingoTokenKind.CloseBrace, '+' => WebLingoTokenKind.Plus, '-' => WebLingoTokenKind.Minus,
            '*' => WebLingoTokenKind.Star, '/' => WebLingoTokenKind.Slash,
            '=' => WebLingoTokenKind.Equals, ';' => WebLingoTokenKind.Semicolon,
            '(' => WebLingoTokenKind.OpenParen, ')' => WebLingoTokenKind.CloseParen,
            ',' => WebLingoTokenKind.Comma, '.' => WebLingoTokenKind.Dot, '[' => WebLingoTokenKind.OpenBracket,
            ']' => WebLingoTokenKind.CloseBracket, ':' => WebLingoTokenKind.Colon,
            _ => WebLingoTokenKind.Bad
        };
        if (kind == WebLingoTokenKind.Bad) return ReadInvalidCharacter(start);
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
                    Report(WebLingoDiagnosticCodes.UnterminatedComment, "The comment has no closing '*/'.",
                        new WebLingoSourceSpan(start, _position - start));
                }
            }
            else
            {
                return;
            }
        }
    }

    private WebLingoToken ReadIdentifierOrKeyword(int start)
    {
        _position++;
        while (_position < source.Length && IsIdentifierPart(source[_position])) _position++;
        var text = source[start.._position];
        var kind = text switch
        {
            "var" => WebLingoTokenKind.Var,
            "const" => WebLingoTokenKind.Const,
            "let" => WebLingoTokenKind.Let, // Reserved: never a declaration keyword (WL-002.3).
            "Infinity" => WebLingoTokenKind.Infinity,
            "NaN" => WebLingoTokenKind.NaN,
            "true" => WebLingoTokenKind.True,
            "false" => WebLingoTokenKind.False,
            "null" => WebLingoTokenKind.Null,
            "if" => WebLingoTokenKind.If,
            "else" => WebLingoTokenKind.Else,
            "while" => WebLingoTokenKind.While,
            "for" => WebLingoTokenKind.For,
            "foreach" => WebLingoTokenKind.Foreach,
            "do" => WebLingoTokenKind.Do,
            "break" => WebLingoTokenKind.Break,
            "continue" => WebLingoTokenKind.Continue,
            "function" => WebLingoTokenKind.Function,
            "class" => WebLingoTokenKind.Class,
            "new" => WebLingoTokenKind.New,
            "this" => WebLingoTokenKind.This,
            "extends" => WebLingoTokenKind.Extends,
            "super" => WebLingoTokenKind.Super,
            "using" => WebLingoTokenKind.Using,
            "static" => WebLingoTokenKind.Static,
            "try" => WebLingoTokenKind.Try,
            "catch" => WebLingoTokenKind.Catch,
            "finally" => WebLingoTokenKind.Finally,
            "throw" => WebLingoTokenKind.Throw,
            "is" => WebLingoTokenKind.Is,
            "async" => WebLingoTokenKind.Async,
            "await" => WebLingoTokenKind.Await,
            "extend" => WebLingoTokenKind.Extend,
            "import" => WebLingoTokenKind.Import,
            "export" => WebLingoTokenKind.Export,
            "return" => WebLingoTokenKind.Return,
            _ => WebLingoTokenKind.Identifier
        };
        return new WebLingoToken(kind, text, new WebLingoSourceSpan(start, _position - start));
    }

    /// <summary>
    /// Digits, then optionally a fraction (<c>.</c> and digits) and an exponent (<c>e</c> or <c>E</c>, an optional sign,
    /// and digits). With either, the literal is a binary64, rounded to the nearest; otherwise an int64.
    /// </summary>
    private WebLingoToken ReadNumericLiteral(int start)
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
            Report(WebLingoDiagnosticCodes.InvalidNumericLiteral, $"'{text}' is not a valid number.",
                new WebLingoSourceSpan(start, _position - start));
            return Token(WebLingoTokenKind.Bad, start);
        }
        if (isBinary64) return ReadBinary64(start);

        long value = 0;
        for (var i = start; i < _position; i++)
        {
            var digit = source[i] - '0';
            if (value > (long.MaxValue - digit) / 10)
            {
                Report(WebLingoDiagnosticCodes.InvalidNumericLiteral,
                    $"The integer {source[start.._position]} is too large; the largest is {LargestInteger}.",
                    new WebLingoSourceSpan(start, _position - start));
                return Token(WebLingoTokenKind.Bad, start);
            }
            value = value * 10 + digit;
        }
        return new WebLingoToken(WebLingoTokenKind.IntegerLiteral, source[start.._position], new WebLingoSourceSpan(start, _position - start),
            value);
    }

    private WebLingoToken ReadBinary64(int start)
    {
        var text = source[start.._position];
        var value = double.Parse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        var span = new WebLingoSourceSpan(start, _position - start);
        if (double.IsInfinity(value))
        {
            Report(WebLingoDiagnosticCodes.InvalidNumericLiteral,
                $"The number {text} is too large for a binary64; write Infinity for infinity.", span);
            return Token(WebLingoTokenKind.Bad, start);
        }
        return new WebLingoToken(WebLingoTokenKind.Binary64Literal, text, span, BitConverter.DoubleToInt64Bits(value));
    }

    /// <summary>
    /// A string literal in either quote, on one line (specification, section 3.5). An invalid escape (WL1009) is
    /// reported and the literal read on; a literal not closed on its line is WL1010. Either makes it a bad token.
    /// </summary>
    private WebLingoToken ReadStringLiteral(int start)
    {
        var quote = source[start];
        _position++;
        var text = new System.Text.StringBuilder();
        var valid = true;
        while (true)
        {
            if (_position >= source.Length || source[_position] is '\r' or '\n')
            {
                Report(WebLingoDiagnosticCodes.UnterminatedString, $"This string has no closing {quote} on its line.",
                    new WebLingoSourceSpan(start, _position - start));
                return Token(WebLingoTokenKind.Bad, start);
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
            Report(WebLingoDiagnosticCodes.InvalidEscape,
                "This escape is not valid: use \\n, \\r, \\t, \\0, \\\\, \\', \\\" or \\u{…} with a Unicode scalar value.",
                new WebLingoSourceSpan(escapeStart, _position - escapeStart));
            valid = false;
        }
        if (!valid) return Token(WebLingoTokenKind.Bad, start);
        return new WebLingoToken(WebLingoTokenKind.StringLiteral, source[start.._position], new WebLingoSourceSpan(start, _position - start),
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

    private WebLingoToken ReadInvalidCharacter(int start)
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
        Report(WebLingoDiagnosticCodes.InvalidCharacter, $"The character {shown}(U+{codePoint:X4}) is not valid here.",
            new WebLingoSourceSpan(start, _position - start));
        return Token(WebLingoTokenKind.Bad, start);
    }

    private char Peek(int ahead) => _position + ahead < source.Length ? source[_position + ahead] : '\0';

    private WebLingoToken Token(WebLingoTokenKind kind, int start) =>
        new(kind, source[start.._position], new WebLingoSourceSpan(start, _position - start));

    private void Report(string code, string message, WebLingoSourceSpan span) =>
        _diagnostics.Add(new WebLingoDiagnostic(code, WebLingoDiagnosticSeverity.Error, message, span));

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
    private static bool IsIdentifierStart(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || IsDigit(c);
}
