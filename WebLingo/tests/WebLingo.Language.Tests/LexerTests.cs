using WebLingo.Core;
using WebLingo.Language;
using Xunit;
namespace WebLingo.Language.Tests;

public sealed class LexerTests
{
    [Fact]
    public void RecognizesVarConstAndSemicolon()
    {
        var lexer = new WebLingoLexer("var a = 10; const b = 20;");
        var expected = new[] {
            WebLingoTokenKind.Var, WebLingoTokenKind.Identifier, WebLingoTokenKind.Equals,
            WebLingoTokenKind.IntegerLiteral, WebLingoTokenKind.Semicolon,
            WebLingoTokenKind.Const, WebLingoTokenKind.Identifier, WebLingoTokenKind.Equals,
            WebLingoTokenKind.IntegerLiteral, WebLingoTokenKind.Semicolon, WebLingoTokenKind.EndOfFile
        };
        foreach (var kind in expected) Assert.Equal(kind, lexer.NextToken().Kind);
    }

    [Fact]
    public void LetIsNotTreatedAsVarOrConst()
    {
        Assert.Equal(WebLingoTokenKind.Let, new WebLingoLexer("let").NextToken().Kind);
    }

    [Fact]
    public void RecognizesEveryOperatorAndPunctuation()
    {
        Assert.Equal(
            new[] {
                WebLingoTokenKind.Plus, WebLingoTokenKind.Minus, WebLingoTokenKind.Star, WebLingoTokenKind.Slash, WebLingoTokenKind.Equals,
                WebLingoTokenKind.Semicolon, WebLingoTokenKind.OpenParen, WebLingoTokenKind.CloseParen, WebLingoTokenKind.OpenBrace,
                WebLingoTokenKind.CloseBrace, WebLingoTokenKind.Comma, WebLingoTokenKind.EndOfFile
            },
            Kinds("+ - * / = ; ( ) { } ,", out var diagnostics));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void RecognizesComparisonAndLogicalOperators()
    {
        Assert.Equal(
            new[] {
                WebLingoTokenKind.EqualEqual, WebLingoTokenKind.BangEqual, WebLingoTokenKind.Less, WebLingoTokenKind.LessEqual, WebLingoTokenKind.Greater,
                WebLingoTokenKind.GreaterEqual, WebLingoTokenKind.Bang, WebLingoTokenKind.AmpAmp, WebLingoTokenKind.PipePipe, WebLingoTokenKind.Equals,
                WebLingoTokenKind.True, WebLingoTokenKind.False, WebLingoTokenKind.Null, WebLingoTokenKind.EndOfFile
            },
            Kinds("== != < <= > >= ! && || = true false null", out var diagnostics));
        Assert.Empty(diagnostics);
        Assert.Equal(new[] { WebLingoTokenKind.EqualEqual, WebLingoTokenKind.Equals, WebLingoTokenKind.EndOfFile }, Kinds("===", out _));
    }

    [Theory]
    [InlineData("&")]
    [InlineData("|")]
    public void ASingleAmpersandOrBarIsInvalid(string text)
    {
        Assert.Equal(new[] { WebLingoTokenKind.Bad, WebLingoTokenKind.EndOfFile }, Kinds(text, out var diagnostics));
        Assert.Equal(WebLingoDiagnosticCodes.InvalidCharacter, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public void TokensNeedNoSpaceBetweenThem()
    {
        Assert.Equal(
            new[] {
                WebLingoTokenKind.Identifier, WebLingoTokenKind.OpenParen, WebLingoTokenKind.Identifier, WebLingoTokenKind.Plus,
                WebLingoTokenKind.IntegerLiteral, WebLingoTokenKind.CloseParen, WebLingoTokenKind.Semicolon, WebLingoTokenKind.EndOfFile
            },
            Kinds("writeLn(a+10);", out _));
    }

    [Fact]
    public void TokensCarryTheirTextAndSpan()
    {
        var tokens = WebLingoLexer.Tokenize("var total = 42;", out _);
        Assert.Equal(("total", new WebLingoSourceSpan(4, 5)), (tokens[1].Text, tokens[1].Span));
        Assert.Equal(("42", new WebLingoSourceSpan(12, 2)), (tokens[3].Text, tokens[3].Span));
        Assert.Equal(new WebLingoSourceSpan(14, 1), tokens[4].Span);
    }

    [Fact]
    public void EndOfFileIsAnEmptySpanAtTheEndAndRepeats()
    {
        var lexer = new WebLingoLexer("a ");
        lexer.NextToken();
        var end = lexer.NextToken();
        Assert.Equal((WebLingoTokenKind.EndOfFile, new WebLingoSourceSpan(2, 0)), (end.Kind, end.Span));
        Assert.Equal(WebLingoTokenKind.EndOfFile, lexer.NextToken().Kind);
    }

    [Theory]
    [InlineData("varx")]
    [InlineData("Var")]
    [InlineData("CONST")]
    [InlineData("_")]
    [InlineData("_tmp2")]
    [InlineData("letter")]
    public void KeywordsAreWholeAndCaseSensitive(string text)
    {
        var token = new WebLingoLexer(text).NextToken();
        Assert.Equal((WebLingoTokenKind.Identifier, text), (token.Kind, token.Text));
    }

    [Fact]
    public void AnIdentifierMayHoldDigitsButNotStartWithOne()
    {
        Assert.Equal(new[] { WebLingoTokenKind.Identifier, WebLingoTokenKind.EndOfFile }, Kinds("a1b2", out _));
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("10", 10L)]
    [InlineData("007", 7L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void IntegerLiteralsCarryTheirValue(string text, long value)
    {
        var token = new WebLingoLexer(text).NextToken();
        Assert.Equal((WebLingoTokenKind.IntegerLiteral, value), (token.Kind, token.IntegerValue));
    }

    [Fact]
    public void AnIntegerLiteralTooLargeForInt64IsReported()
    {
        var tokens = WebLingoLexer.Tokenize("9223372036854775808;", out var diagnostics);
        Assert.Equal(WebLingoTokenKind.Bad, tokens[0].Kind);
        Assert.Equal(WebLingoTokenKind.Semicolon, tokens[1].Kind);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((WebLingoDiagnosticCodes.InvalidNumericLiteral, new WebLingoSourceSpan(0, 19)), (diagnostic.Code, diagnostic.Span));
        Assert.Equal(WebLingoDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("0.1", 0.1)]
    [InlineData("007.50", 7.5)]
    [InlineData("1e21", 1e21)]
    [InlineData("1E21", 1e21)]
    [InlineData("2.5e-3", 0.0025)]
    [InlineData("2.5E+3", 2500.0)]
    [InlineData("1.7976931348623157e308", double.MaxValue)]
    [InlineData("1e-400", 0.0)]
    public void Binary64LiteralsCarryTheNearestValue(string text, double value)
    {
        var tokens = WebLingoLexer.Tokenize(text, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Equal((WebLingoTokenKind.Binary64Literal, value, text), (tokens[0].Kind, tokens[0].Binary64Value, tokens[0].Text));
        Assert.Equal(WebLingoTokenKind.EndOfFile, tokens[1].Kind);
    }

    [Fact]
    public void ABinary64LiteralTooLargeIsReported()
    {
        var tokens = WebLingoLexer.Tokenize("1e309;", out var diagnostics);
        Assert.Equal(new[] { WebLingoTokenKind.Bad, WebLingoTokenKind.Semicolon, WebLingoTokenKind.EndOfFile }, tokens.Select(t => t.Kind));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((WebLingoDiagnosticCodes.InvalidNumericLiteral, new WebLingoSourceSpan(0, 5)), (diagnostic.Code, diagnostic.Span));
    }

    [Theory]
    [InlineData("1.", new[] { WebLingoTokenKind.IntegerLiteral, WebLingoTokenKind.Dot })]
    [InlineData(".5", new[] { WebLingoTokenKind.Dot, WebLingoTokenKind.IntegerLiteral })]
    [InlineData("1.e5", new[] { WebLingoTokenKind.IntegerLiteral, WebLingoTokenKind.Dot, WebLingoTokenKind.Identifier })]
    public void APointNeedsDigitsOnBothSides(string text, WebLingoTokenKind[] kinds)
    {
        // Without digits on both sides, a point is not part of a number but a member access's (section 3.6).
        var tokens = WebLingoLexer.Tokenize(text, out var diagnostics);
        Assert.Equal(kinds.Append(WebLingoTokenKind.EndOfFile), tokens.Select(t => t.Kind));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void OptionalAccessAndSetsHaveTheirTokens()
    {
        var tokens = WebLingoLexer.Tokenize("m?[0] #{}", out var diagnostics);
        Assert.Equal(
            new[]
            {
                WebLingoTokenKind.Identifier, WebLingoTokenKind.QuestionBracket, WebLingoTokenKind.IntegerLiteral, WebLingoTokenKind.CloseBracket,
                WebLingoTokenKind.HashBrace, WebLingoTokenKind.CloseBrace, WebLingoTokenKind.EndOfFile
            },
            tokens.Select(t => t.Kind));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MembersAndIndexesHaveTheirTokens()
    {
        var tokens = WebLingoLexer.Tokenize("a.length[0]", out var diagnostics);
        Assert.Equal(
            new[]
            {
                WebLingoTokenKind.Identifier, WebLingoTokenKind.Dot, WebLingoTokenKind.Identifier, WebLingoTokenKind.OpenBracket,
                WebLingoTokenKind.IntegerLiteral, WebLingoTokenKind.CloseBracket, WebLingoTokenKind.EndOfFile
            },
            tokens.Select(t => t.Kind));
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("\"abc\"", "abc")]
    [InlineData("'abc'", "abc")]
    [InlineData("\"\"", "")]
    [InlineData("'it\\'s'", "it's")]
    [InlineData("\"say \\\"hi\\\"\"", "say \"hi\"")]
    [InlineData("\"'\"", "'")]
    [InlineData("'\"'", "\"")]
    [InlineData("\"\\n\\r\\t\\0\\\\\"", "\n\r\t\0\\")]
    [InlineData("\"\\u{41}\\u{e9}\\u{1F600}\"", "A\u00e9\U0001F600")]
    [InlineData("\"\\u{10FFFF}\"", "\U0010FFFF")]
    [InlineData("\"é ✓\"", "é ✓")]
    public void StringLiteralsDecodeTheirEscapes(string text, string value)
    {
        var tokens = WebLingoLexer.Tokenize(text, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Equal((WebLingoTokenKind.StringLiteral, value, text), (tokens[0].Kind, tokens[0].StringValue, tokens[0].Text));
    }

    [Theory]
    [InlineData("\"a\\qb\"", 2, 2)]
    [InlineData("\"\\u{D800}\"", 1, 2)]
    [InlineData("\"\\u{110000}\"", 1, 2)]
    [InlineData("\"\\u{}\"", 1, 2)]
    [InlineData("\"\\u{1234567}\"", 1, 2)]
    [InlineData("\"\\u41\"", 1, 2)]
    [InlineData("\"\\x41\"", 1, 2)]
    public void AnInvalidEscapeIsReportedWhereItIs(string text, int start, int length)
    {
        var tokens = WebLingoLexer.Tokenize(text, out var diagnostics);
        Assert.Equal(new[] { WebLingoTokenKind.Bad, WebLingoTokenKind.EndOfFile }, tokens.Select(t => t.Kind));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((WebLingoDiagnosticCodes.InvalidEscape, new WebLingoSourceSpan(start, length)), (diagnostic.Code, diagnostic.Span));
    }

    [Theory]
    [InlineData("\"abc", 4)]
    [InlineData("'abc\nx'", 4)]
    [InlineData("\"abc\\", 5)]
    public void AStringNotClosedOnItsLineIsReported(string text, int length)
    {
        var tokens = WebLingoLexer.Tokenize(text, out var diagnostics);
        Assert.Equal(WebLingoTokenKind.Bad, tokens[0].Kind);
        var unterminated = diagnostics.First(d => d.Code == WebLingoDiagnosticCodes.UnterminatedString);
        Assert.Equal(new WebLingoSourceSpan(0, length), unterminated.Span);
    }

    [Fact]
    public void InfinityAndNaNAreKeywords()
    {
        Assert.Equal(new[] { WebLingoTokenKind.Infinity, WebLingoTokenKind.NaN, WebLingoTokenKind.Identifier, WebLingoTokenKind.Identifier,
                WebLingoTokenKind.EndOfFile },
            Kinds("Infinity NaN infinity nan", out var diagnostics));
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("10abc")]
    [InlineData("10m")]
    [InlineData("1_000")]
    [InlineData("1.5m")]
    [InlineData("1e")]
    [InlineData("2.5e3x")]
    public void DigitsRunningIntoLettersAreOneInvalidLiteral(string text)
    {
        var tokens = WebLingoLexer.Tokenize(text, out var diagnostics);
        Assert.Equal(new[] { WebLingoTokenKind.Bad, WebLingoTokenKind.EndOfFile }, tokens.Select(t => t.Kind));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((WebLingoDiagnosticCodes.InvalidNumericLiteral, new WebLingoSourceSpan(0, text.Length)),
            (diagnostic.Code, diagnostic.Span));
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    [InlineData("a\tb")]
    [InlineData("  a   b  ")]
    public void SpacesTabsAndEveryLineEndingSeparateTokens(string text)
    {
        Assert.Equal(new[] { WebLingoTokenKind.Identifier, WebLingoTokenKind.Identifier, WebLingoTokenKind.EndOfFile }, Kinds(text, out var d));
        Assert.Empty(d);
    }

    [Theory]
    [InlineData("a // comment\nb")]
    [InlineData("a // comment\r\nb")]
    [InlineData("a // comment\rb")]
    [InlineData("a /* comment */ b")]
    [InlineData("a /* several\nlines */ b")]
    [InlineData("a/**/b")]
    [InlineData("a /* not /* nested */ b")]
    public void CommentsAreSkipped(string text)
    {
        Assert.Equal(new[] { WebLingoTokenKind.Identifier, WebLingoTokenKind.Identifier, WebLingoTokenKind.EndOfFile }, Kinds(text, out var d));
        Assert.Empty(d);
    }

    [Fact]
    public void ALineCommentMayEndTheFile()
    {
        Assert.Equal(new[] { WebLingoTokenKind.Identifier, WebLingoTokenKind.EndOfFile }, Kinds("a // to the end", out var d));
        Assert.Empty(d);
    }

    [Fact]
    public void ASingleSlashIsDivision()
    {
        Assert.Equal(
            new[] { WebLingoTokenKind.Identifier, WebLingoTokenKind.Slash, WebLingoTokenKind.Identifier, WebLingoTokenKind.EndOfFile },
            Kinds("a / b", out _));
    }

    [Fact]
    public void AnUnterminatedCommentIsReportedFromItsStart()
    {
        var tokens = WebLingoLexer.Tokenize("a /* no end", out var diagnostics);
        Assert.Equal(new[] { WebLingoTokenKind.Identifier, WebLingoTokenKind.EndOfFile }, tokens.Select(t => t.Kind));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((WebLingoDiagnosticCodes.UnterminatedComment, new WebLingoSourceSpan(2, 9)), (diagnostic.Code, diagnostic.Span));
    }

    [Theory]
    [InlineData("@")]
    [InlineData("~")]
    [InlineData("é")]
    [InlineData(" ")]
    [InlineData("\u0001")]
    public void AnInvalidCharacterIsOneBadToken(string text)
    {
        var tokens = WebLingoLexer.Tokenize(text, out var diagnostics);
        Assert.Equal(new[] { WebLingoTokenKind.Bad, WebLingoTokenKind.EndOfFile }, tokens.Select(t => t.Kind));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((WebLingoDiagnosticCodes.InvalidCharacter, new WebLingoSourceSpan(0, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Fact]
    public void ASurrogatePairIsOneInvalidCharacter()
    {
        var tokens = WebLingoLexer.Tokenize("\U0001F600", out var diagnostics);
        Assert.Equal(new WebLingoSourceSpan(0, 2), tokens[0].Span);
        Assert.Contains("U+1F600", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void LexingGoesOnAfterAnErrorAndReportsEachOne()
    {
        var tokens = WebLingoLexer.Tokenize("var a = 1 @ 2 # 3;", out var diagnostics);
        Assert.Equal(
            new[] {
                WebLingoTokenKind.Var, WebLingoTokenKind.Identifier, WebLingoTokenKind.Equals, WebLingoTokenKind.IntegerLiteral, WebLingoTokenKind.Bad,
                WebLingoTokenKind.IntegerLiteral, WebLingoTokenKind.Bad, WebLingoTokenKind.IntegerLiteral, WebLingoTokenKind.Semicolon,
                WebLingoTokenKind.EndOfFile
            },
            tokens.Select(t => t.Kind));
        Assert.Equal(new[] { new WebLingoSourceSpan(10, 1), new WebLingoSourceSpan(14, 1) }, diagnostics.Select(d => d.Span));
    }

    [Fact]
    public void AnEmptySourceIsJustTheEndOfFile()
    {
        Assert.Equal(new[] { WebLingoTokenKind.EndOfFile }, Kinds("", out var d));
        Assert.Empty(d);
    }

    private static IEnumerable<WebLingoTokenKind> Kinds(string source, out IReadOnlyList<WebLingoDiagnostic> diagnostics) =>
        WebLingoLexer.Tokenize(source, out diagnostics).Select(t => t.Kind).ToArray();
}
