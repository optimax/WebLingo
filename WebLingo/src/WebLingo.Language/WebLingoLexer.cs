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

    /// <summary>How deep interpolations may nest, an interpolation's code holding an interpolated string (WL1008).</summary>
    public const int MaxInterpolationDepth = 32;

    private readonly List<WebLingoDiagnostic> _diagnostics = [];
    private readonly string? _end;
    private readonly bool _interpolation;
    private readonly int _nesting;
    private int _depth;
    private int _position;

    /// <summary>
    /// A lexer of the code that starts at <paramref name="start"/> and ends where <paramref name="end"/> stands as a token
    /// would, outside strings and comments, such as a template's block, which <c>%&gt;</c> ends (specification, section 13).
    /// There it gives its <see cref="WebLingoTokenKind.EndOfFile"/>, spanning <paramref name="end"/>; at the end of the
    /// text, an empty one.
    /// </summary>
    public WebLingoLexer(string source, int start, string end) : this(source)
    {
        _position = start;
        _end = end;
    }

    /// <summary>
    /// A lexer of an interpolation's code, from <paramref name="start"/>, which ends at a <c>}</c>, or a <c>:</c>, outside
    /// its own brackets, giving its <see cref="WebLingoTokenKind.EndOfFile"/> there, empty.
    /// </summary>
    private WebLingoLexer(string source, int start, int nesting) : this(source)
    {
        _position = start;
        _nesting = nesting;
        _interpolation = true;
    }

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
        var token = Next();
        if (_interpolation)
        {
            _depth += token.Kind switch
            {
                WebLingoTokenKind.OpenParen or WebLingoTokenKind.OpenBrace or WebLingoTokenKind.OpenBracket
                    or WebLingoTokenKind.HashBrace or WebLingoTokenKind.QuestionBracket => 1,
                WebLingoTokenKind.CloseParen or WebLingoTokenKind.CloseBrace or WebLingoTokenKind.CloseBracket => -1,
                _ => 0
            };
        }
        return token;
    }

    private WebLingoToken Next()
    {
        SkipWhitespaceAndComments();
        var start = _position;
        if (start >= source.Length) return new WebLingoToken(WebLingoTokenKind.EndOfFile, "", new WebLingoSourceSpan(source.Length, 0));
        if (_end is not null && source.AsSpan(start).StartsWith(_end, StringComparison.Ordinal))
        {
            return new WebLingoToken(WebLingoTokenKind.EndOfFile, _end, new WebLingoSourceSpan(start, _end.Length));
        }
        var c = source[start];
        if (_interpolation && _depth == 0 && c is '}' or ':')
        {
            // An interpolation's code ends at its }, or at the : that begins its format.
            return new WebLingoToken(WebLingoTokenKind.EndOfFile, "", new WebLingoSourceSpan(start, 0));
        }
        if (IsIdentifierStart(c)) return ReadIdentifierOrKeyword(start);
        if (IsDigit(c)) return ReadNumericLiteral(start);
        if (c is '"' or '\'') return Run(start, c) >= 3 ? ReadRawString(start, 0) : ReadStringLiteral(start);
        if (c == '$' && Run(start, '$') is var dollars && CharAt(start + dollars) is '"' or '\'') return ReadInterpolated(start, dollars);
        var pair = (c, start + 1 < source.Length ? source[start + 1] : '\0') switch
        {
            ('=', '=') => WebLingoTokenKind.EqualEqual, ('!', '=') => WebLingoTokenKind.BangEqual,
            ('<', '=') => WebLingoTokenKind.LessEqual, ('>', '=') => WebLingoTokenKind.GreaterEqual,
            ('&', '&') => WebLingoTokenKind.AmpAmp, ('|', '|') => WebLingoTokenKind.PipePipe, ('#', '{') => WebLingoTokenKind.HashBrace,
            ('?', '[') => WebLingoTokenKind.QuestionBracket, ('?', '.') => WebLingoTokenKind.QuestionDot,
            ('=', '>') => WebLingoTokenKind.Arrow,
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
            "param" => WebLingoTokenKind.Param,
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
            valid &= ReadEscape(text, braces: false);
        }
        if (!valid) return Token(WebLingoTokenKind.Bad, start);
        return new WebLingoToken(WebLingoTokenKind.StringLiteral, source[start.._position], new WebLingoSourceSpan(start, _position - start),
            StringValue: text.ToString());
    }

    /// <summary>
    /// An escape in a string, at its <c>\</c>: appended, and true; or reported (WL1009), and false. In an interpolated
    /// string (<paramref name="braces"/>), <c>\{</c> and <c>\}</c> are braces.
    /// </summary>
    private bool ReadEscape(System.Text.StringBuilder text, bool braces)
    {
        var escapeStart = _position;
        _position++;
        var next = _position < source.Length ? source[_position] : '\0';
        var simple = next switch
        {
            'n' => "\n", 'r' => "\r", 't' => "\t", '0' => "\0", '\\' => "\\", '\'' => "'", '"' => "\"",
            '{' when braces => "{", '}' when braces => "}",
            _ => null
        };
        if (simple is not null)
        {
            text.Append(simple);
            _position++;
            return true;
        }
        if (next == 'u' && ReadUnicodeEscape(text)) return true;
        if (next is not ('\r' or '\n') && _position < source.Length) _position++;
        Report(WebLingoDiagnosticCodes.InvalidEscape,
            braces
                ? "This escape is not valid: use \\n, \\r, \\t, \\0, \\\\, \\', \\\", \\{, \\} or \\u{…} with a Unicode scalar value."
                : "This escape is not valid: use \\n, \\r, \\t, \\0, \\\\, \\', \\\" or \\u{…} with a Unicode scalar value.",
            new WebLingoSourceSpan(escapeStart, _position - escapeStart));
        return false;
    }

    /// <summary>
    /// An interpolated string (section 3.5), at its <paramref name="dollars"/> <c>$</c>: <c>$"…{x}…"</c>, on one line, with an
    /// ordinary string's escapes, a brace written <c>{{</c> or <c>\{</c>; or a multi-line one, <c>$"""…{x}…"""</c>, raw,
    /// as many <c>$</c> as the braces that open an interpolation.
    /// </summary>
    private WebLingoToken ReadInterpolated(int start, int dollars)
    {
        var open = start + dollars;
        var quote = source[open];
        if (Run(open, quote) >= 3) return ReadRawString(start, dollars);
        _position = open + 1;
        if (dollars > 1)
        {
            Report(WebLingoDiagnosticCodes.MalformedString, "A single-line interpolated string takes one '$'.",
                new WebLingoSourceSpan(start, dollars));
        }
        var parts = new List<WebLingoInterpolationPart>();
        var text = new System.Text.StringBuilder();
        var textStart = _position;
        var lineEnd = LineEnd(_position);
        var valid = dollars == 1;
        while (true)
        {
            if (_position >= lineEnd)
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
            if (c is '{' or '}' && CharAt(_position + 1) == c)
            {
                text.Append(c);
                _position += 2;
            }
            else if (c == '}')
            {
                Report(WebLingoDiagnosticCodes.MalformedString, "A '}' in an interpolated string is written '}}' or '\\}'.",
                    new WebLingoSourceSpan(_position, 1));
                _position++;
                valid = false;
            }
            else if (c == '{')
            {
                AddText(parts, text, textStart);
                _position++;
                valid &= ReadInterpolation(parts, 1, lineEnd);
                textStart = _position;
            }
            else if (c == '\\')
            {
                valid &= ReadEscape(text, braces: true);
            }
            else
            {
                text.Append(c);
                _position++;
            }
        }
        AddText(parts, text, textStart);
        if (!valid) return Token(WebLingoTokenKind.Bad, start);
        return new WebLingoToken(WebLingoTokenKind.InterpolatedString, source[start.._position], new WebLingoSourceSpan(start, _position - start),
            Parts: parts);
    }

    /// <summary>
    /// An interpolation, after its opening braces: its code, read by a lexer of its own up to its <c>}</c>, its format after
    /// a <c>:</c>, if it has one, then its <paramref name="braces"/> closing braces, all before <paramref name="limit"/>.
    /// </summary>
    private bool ReadInterpolation(List<WebLingoInterpolationPart> parts, int braces, int limit)
    {
        var start = _position;
        if (_nesting >= MaxInterpolationDepth)
        {
            Report(WebLingoDiagnosticCodes.NestingTooDeep, $"Interpolations nest too deeply here: at most {MaxInterpolationDepth}.",
                new WebLingoSourceSpan(start, 0));
            _position = limit;
            return false;
        }
        var lexer = new WebLingoLexer(source, start, _nesting + 1);
        var code = new List<WebLingoToken>();
        WebLingoToken token;
        while ((token = lexer.NextToken()).Kind != WebLingoTokenKind.EndOfFile) code.Add(token);
        _diagnostics.AddRange(lexer.Diagnostics);
        _position = token.Span.Start;
        WebLingoSourceSpan? format = null;
        if (_position < limit && source[_position] == ':')
        {
            var formatStart = _position;
            while (_position < limit && source[_position] != '}') _position++;
            format = new WebLingoSourceSpan(formatStart, _position - formatStart);
        }
        if (_position >= limit || Run(_position, '}') < braces)
        {
            Report(WebLingoDiagnosticCodes.UnterminatedString,
                $"This interpolation has no closing '{new string('}', braces)}' on its line.", new WebLingoSourceSpan(start - braces, braces));
            _position = Math.Min(Math.Max(_position, start), limit);
            return false;
        }
        parts.Add(new WebLingoInterpolationPart(null, code, new WebLingoSourceSpan(start, _position - start)) { Format = format });
        _position += braces;
        return true;
    }

    /// <summary>
    /// A raw string (section 3.5), at its <paramref name="dollars"/> <c>$</c>, or its quotes when it has none: three or more
    /// double or single quotes, and the same again to close it, with no escapes. On one line, <c>"""a "b" c"""</c>; or over
    /// several, its opening alone at the end of its line and its closing first on its own, each line then without the
    /// closing delimiter's indentation, its lines' breaks LF, and neither the break after its opening nor the one before
    /// its closing its own.
    /// </summary>
    private WebLingoToken ReadRawString(int start, int dollars)
    {
        var open = start + dollars;
        var quote = source[open];
        var count = Run(open, quote);
        var delimiter = new string(quote, count);
        var contentStart = open + count;
        var lineEnd = LineEnd(contentStart);
        var segments = new List<(int Start, int End)>();
        var valid = true;
        if (!IsBlank(contentStart, lineEnd))
        {
            var close = contentStart;
            while (close < lineEnd && !(source[close] == quote && Run(close, quote) >= count)) close++;
            if (close >= lineEnd)
            {
                Report(WebLingoDiagnosticCodes.UnterminatedString, $"This string has no closing {delimiter} on its line.",
                    new WebLingoSourceSpan(start, lineEnd - start));
                _position = lineEnd;
                return Token(WebLingoTokenKind.Bad, start);
            }
            valid &= CheckClose(close, quote, count);
            segments.Add((contentStart, close));
            _position = close + Run(close, quote);
            return Finish(start, dollars, segments, valid);
        }
        var lines = new List<(int Start, int End)>();
        var closeAt = -1;
        var lineStart = NextLine(lineEnd);
        while (lineStart >= 0)
        {
            var end = LineEnd(lineStart);
            var first = lineStart;
            while (first < end && source[first] is ' ' or '\t') first++;
            if (first < end && source[first] == quote && Run(first, quote) >= count)
            {
                closeAt = first;
                break;
            }
            lines.Add((lineStart, end));
            lineStart = NextLine(end);
        }
        if (closeAt < 0)
        {
            Report(WebLingoDiagnosticCodes.UnterminatedMultiLineString, $"This multi-line string has no closing {delimiter}.",
                new WebLingoSourceSpan(start, contentStart - start));
            _position = source.Length;
            return Token(WebLingoTokenKind.Bad, start);
        }
        valid &= CheckClose(closeAt, quote, count);
        var indentStart = LineStart(closeAt);
        var indent = source.AsSpan(indentStart, closeAt - indentStart);
        foreach (var (lineFrom, lineTo) in lines)
        {
            if (IsBlank(lineFrom, lineTo))
            {
                segments.Add((lineTo, lineTo));
            }
            else if (lineTo - lineFrom >= indent.Length && source.AsSpan(lineFrom, indent.Length).SequenceEqual(indent))
            {
                segments.Add((lineFrom + indent.Length, lineTo));
            }
            else
            {
                Report(WebLingoDiagnosticCodes.MalformedString,
                    "This line of a multi-line string does not begin with its closing delimiter's indentation.",
                    new WebLingoSourceSpan(lineFrom, 0));
                segments.Add((lineFrom, lineTo));
                valid = false;
            }
        }
        _position = closeAt + Run(closeAt, quote);
        return Finish(start, dollars, segments, valid);
    }

    /// <summary>A raw string's closing quotes, as many as its opening's, not more (WL1012).</summary>
    private bool CheckClose(int close, char quote, int count)
    {
        if (Run(close, quote) == count) return true;
        Report(WebLingoDiagnosticCodes.MalformedString, $"This string closes with more quotes than its {count} open it.",
            new WebLingoSourceSpan(close, Run(close, quote)));
        return false;
    }

    /// <summary>
    /// A raw string's token, from its lines' text, joined by LF: a string literal; or, with <paramref name="dollars"/>, an
    /// interpolated string, a run of that many <c>{</c> opening an interpolation, which as many <c>}</c> close, and a
    /// shorter run of either brace being text.
    /// </summary>
    private WebLingoToken Finish(int start, int dollars, List<(int Start, int End)> segments, bool valid)
    {
        var end = _position;
        var text = new System.Text.StringBuilder();
        var parts = new List<WebLingoInterpolationPart>();
        for (var s = 0; s < segments.Count; s++)
        {
            if (s > 0) text.Append('\n');
            var (from, to) = segments[s];
            var textStart = from;
            for (_position = from; _position < to;)
            {
                var c = source[_position];
                var run = c is '{' or '}' && dollars > 0 ? Math.Min(Run(_position, c), to - _position) : 0;
                if (dollars == 0 || run < dollars)
                {
                    text.Append(c);
                    _position++;
                }
                else if (c == '}' || run >= 2 * dollars)
                {
                    Report(WebLingoDiagnosticCodes.MalformedString,
                        c == '}'
                            ? $"A run of {run} '}}' stands outside an interpolation, which {dollars} '$' close with {dollars}."
                            : $"A run of {run} '{{' is more than {2 * dollars - 1}, which {dollars} '$' allow.",
                        new WebLingoSourceSpan(_position, run));
                    _position += run;
                    valid = false;
                }
                else
                {
                    text.Append(c, run - dollars);
                    AddText(parts, text, textStart);
                    _position += run;
                    valid &= ReadInterpolation(parts, dollars, to);
                    textStart = _position;
                }
            }
        }
        _position = end;
        if (!valid) return Token(WebLingoTokenKind.Bad, start);
        var span = new WebLingoSourceSpan(start, end - start);
        if (dollars == 0) return new WebLingoToken(WebLingoTokenKind.StringLiteral, source[start..end], span, StringValue: text.ToString());
        AddText(parts, text, end);
        return new WebLingoToken(WebLingoTokenKind.InterpolatedString, source[start..end], span, Parts: parts);
    }

    /// <summary>The text gathered so far, as a part, if there is any.</summary>
    private static void AddText(List<WebLingoInterpolationPart> parts, System.Text.StringBuilder text, int start)
    {
        if (text.Length == 0) return;
        parts.Add(new WebLingoInterpolationPart(text.ToString(), null, new WebLingoSourceSpan(start, 0)));
        text.Clear();
    }

    /// <summary>How many of <paramref name="c"/> stand in a row from <paramref name="at"/>.</summary>
    private int Run(int at, char c)
    {
        var end = at;
        while (end < source.Length && source[end] == c) end++;
        return end - at;
    }

    private char CharAt(int at) => at < source.Length ? source[at] : '\0';

    /// <summary>Where the line holding <paramref name="at"/> ends, at its break or at the end of the text.</summary>
    private int LineEnd(int at)
    {
        while (at < source.Length && source[at] is not ('\r' or '\n')) at++;
        return at;
    }

    private int LineStart(int at)
    {
        while (at > 0 && source[at - 1] is not ('\r' or '\n')) at--;
        return at;
    }

    /// <summary>Where the line after the break at <paramref name="lineEnd"/> begins (CR LF, LF or CR), or -1 at the end.</summary>
    private int NextLine(int lineEnd)
    {
        if (lineEnd >= source.Length) return -1;
        return source[lineEnd] == '\r' && CharAt(lineEnd + 1) == '\n' ? lineEnd + 2 : lineEnd + 1;
    }

    private bool IsBlank(int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (source[i] is not (' ' or '\t')) return false;
        }
        return true;
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
