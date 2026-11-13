using WebLingo.Core;
using WebLingo.Language;
using Xunit;
namespace WebLingo.Language.Tests;

public sealed class LexerTests
{
    [Fact]
    public void RecognizesVarConstAndSemicolon()
    {
        var lexer = new WsLexer("var a = 10; const b = 20;");
        var expected = new[] {
            WsTokenKind.Var, WsTokenKind.Identifier, WsTokenKind.Equals,
            WsTokenKind.IntegerLiteral, WsTokenKind.Semicolon,
            WsTokenKind.Const, WsTokenKind.Identifier, WsTokenKind.Equals,
            WsTokenKind.IntegerLiteral, WsTokenKind.Semicolon, WsTokenKind.EndOfFile
        };
        foreach (var kind in expected) Assert.Equal(kind, lexer.NextToken().Kind);
    }

    [Fact]
    public void LetIsNotTreatedAsVarOrConst()
    {
        Assert.Equal(WsTokenKind.Let, new WsLexer("let").NextToken().Kind);
    }

    [Fact]
    public void RecognizesEveryOperatorAndPunctuation()
    {
        Assert.Equal(
            new[] {
                WsTokenKind.Plus, WsTokenKind.Minus, WsTokenKind.Star, WsTokenKind.Slash, WsTokenKind.Equals,
                WsTokenKind.Semicolon, WsTokenKind.OpenParen, WsTokenKind.CloseParen, WsTokenKind.Comma,
                WsTokenKind.EndOfFile
            },
            Kinds("+ - * / = ; ( ) ,", out var diagnostics));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TokensNeedNoSpaceBetweenThem()
    {
        Assert.Equal(
            new[] {
                WsTokenKind.Identifier, WsTokenKind.OpenParen, WsTokenKind.Identifier, WsTokenKind.Plus,
                WsTokenKind.IntegerLiteral, WsTokenKind.CloseParen, WsTokenKind.Semicolon, WsTokenKind.EndOfFile
            },
            Kinds("writeLn(a+10);", out _));
    }

    [Fact]
    public void TokensCarryTheirTextAndSpan()
    {
        var tokens = WsLexer.Tokenize("var total = 42;", out _);
        Assert.Equal(("total", new WsSourceSpan(4, 5)), (tokens[1].Text, tokens[1].Span));
        Assert.Equal(("42", new WsSourceSpan(12, 2)), (tokens[3].Text, tokens[3].Span));
        Assert.Equal(new WsSourceSpan(14, 1), tokens[4].Span);
    }

    [Fact]
    public void EndOfFileIsAnEmptySpanAtTheEndAndRepeats()
    {
        var lexer = new WsLexer("a ");
        lexer.NextToken();
        var end = lexer.NextToken();
        Assert.Equal((WsTokenKind.EndOfFile, new WsSourceSpan(2, 0)), (end.Kind, end.Span));
        Assert.Equal(WsTokenKind.EndOfFile, lexer.NextToken().Kind);
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
        var token = new WsLexer(text).NextToken();
        Assert.Equal((WsTokenKind.Identifier, text), (token.Kind, token.Text));
    }

    [Fact]
    public void AnIdentifierMayHoldDigitsButNotStartWithOne()
    {
        Assert.Equal(new[] { WsTokenKind.Identifier, WsTokenKind.EndOfFile }, Kinds("a1b2", out _));
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("10", 10L)]
    [InlineData("007", 7L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void IntegerLiteralsCarryTheirValue(string text, long value)
    {
        var token = new WsLexer(text).NextToken();
        Assert.Equal((WsTokenKind.IntegerLiteral, value), (token.Kind, token.IntegerValue));
    }

    [Fact]
    public void AnIntegerLiteralTooLargeForInt64IsReported()
    {
        var tokens = WsLexer.Tokenize("9223372036854775808;", out var diagnostics);
        Assert.Equal(WsTokenKind.Bad, tokens[0].Kind);
        Assert.Equal(WsTokenKind.Semicolon, tokens[1].Kind);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((WsDiagnosticCodes.InvalidNumericLiteral, new WsSourceSpan(0, 19)), (diagnostic.Code, diagnostic.Span));
        Assert.Equal(WsDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("10abc")]
    [InlineData("10m")]
    [InlineData("1_000")]
    public void DigitsRunningIntoLettersAreOneInvalidLiteral(string text)
    {
        var tokens = WsLexer.Tokenize(text, out var diagnostics);
        Assert.Equal(new[] { WsTokenKind.Bad, WsTokenKind.EndOfFile }, tokens.Select(t => t.Kind));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((WsDiagnosticCodes.InvalidNumericLiteral, new WsSourceSpan(0, text.Length)),
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
        Assert.Equal(new[] { WsTokenKind.Identifier, WsTokenKind.Identifier, WsTokenKind.EndOfFile }, Kinds(text, out var d));
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
        Assert.Equal(new[] { WsTokenKind.Identifier, WsTokenKind.Identifier, WsTokenKind.EndOfFile }, Kinds(text, out var d));
        Assert.Empty(d);
    }

    [Fact]
    public void ALineCommentMayEndTheFile()
    {
        Assert.Equal(new[] { WsTokenKind.Identifier, WsTokenKind.EndOfFile }, Kinds("a // to the end", out var d));
        Assert.Empty(d);
    }

    [Fact]
    public void ASingleSlashIsDivision()
    {
        Assert.Equal(
            new[] { WsTokenKind.Identifier, WsTokenKind.Slash, WsTokenKind.Identifier, WsTokenKind.EndOfFile },
            Kinds("a / b", out _));
    }

    [Fact]
    public void AnUnterminatedCommentIsReportedFromItsStart()
    {
        var tokens = WsLexer.Tokenize("a /* no end", out var diagnostics);
        Assert.Equal(new[] { WsTokenKind.Identifier, WsTokenKind.EndOfFile }, tokens.Select(t => t.Kind));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((WsDiagnosticCodes.UnterminatedComment, new WsSourceSpan(2, 9)), (diagnostic.Code, diagnostic.Span));
    }

    [Theory]
    [InlineData("@")]
    [InlineData(".")]
    [InlineData("\"")]
    [InlineData("é")]
    [InlineData(" ")]
    [InlineData("\u0001")]
    public void AnInvalidCharacterIsOneBadToken(string text)
    {
        var tokens = WsLexer.Tokenize(text, out var diagnostics);
        Assert.Equal(new[] { WsTokenKind.Bad, WsTokenKind.EndOfFile }, tokens.Select(t => t.Kind));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((WsDiagnosticCodes.InvalidCharacter, new WsSourceSpan(0, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Fact]
    public void ASurrogatePairIsOneInvalidCharacter()
    {
        var tokens = WsLexer.Tokenize("\U0001F600", out var diagnostics);
        Assert.Equal(new WsSourceSpan(0, 2), tokens[0].Span);
        Assert.Contains("U+1F600", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void LexingGoesOnAfterAnErrorAndReportsEachOne()
    {
        var tokens = WsLexer.Tokenize("var a = 1 @ 2 # 3;", out var diagnostics);
        Assert.Equal(
            new[] {
                WsTokenKind.Var, WsTokenKind.Identifier, WsTokenKind.Equals, WsTokenKind.IntegerLiteral, WsTokenKind.Bad,
                WsTokenKind.IntegerLiteral, WsTokenKind.Bad, WsTokenKind.IntegerLiteral, WsTokenKind.Semicolon,
                WsTokenKind.EndOfFile
            },
            tokens.Select(t => t.Kind));
        Assert.Equal(new[] { new WsSourceSpan(10, 1), new WsSourceSpan(14, 1) }, diagnostics.Select(d => d.Span));
    }

    [Fact]
    public void AnEmptySourceIsJustTheEndOfFile()
    {
        Assert.Equal(new[] { WsTokenKind.EndOfFile }, Kinds("", out var d));
        Assert.Empty(d);
    }

    private static IEnumerable<WsTokenKind> Kinds(string source, out IReadOnlyList<WsDiagnostic> diagnostics) =>
        WsLexer.Tokenize(source, out diagnostics).Select(t => t.Kind).ToArray();
}
