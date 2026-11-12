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
}
