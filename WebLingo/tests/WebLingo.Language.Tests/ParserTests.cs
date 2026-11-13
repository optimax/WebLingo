using WebLingo.Core;
using WebLingo.Language;
using Xunit;
namespace WebLingo.Language.Tests;

public sealed class ParserTests
{
    [Fact]
    public void ParsesTheSpecificationsExample()
    {
        var tree = Parse("var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);\n");
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(4, tree.Root.Statements.Count);
        var result = Assert.IsType<WsVariableDeclarationSyntax>(tree.Root.Statements[2]);
        Assert.Equal((WsDeclarationKind.Var, "result"), (result.Kind, result.Name));
        Assert.Equal("(a + b)", Show(result.Initializer!));
        var write = Assert.IsType<WsExpressionStatementSyntax>(tree.Root.Statements[3]);
        Assert.Equal("writeLn(result)", Show(write.Expression));
    }

    [Theory]
    [InlineData("1 + 2 * 3", "(1 + (2 * 3))")]
    [InlineData("1 * 2 + 3", "((1 * 2) + 3)")]
    [InlineData("(1 + 2) * 3", "([(1 + 2)] * 3)")]
    [InlineData("1 - 2 - 3", "((1 - 2) - 3)")]
    [InlineData("8 / 4 / 2", "((8 / 4) / 2)")]
    [InlineData("-2 * 3", "((-2) * 3)")]
    [InlineData("- -2", "(-(-2))")]
    [InlineData("a - -b", "(a - (-b))")]
    [InlineData("a = b = 3", "(a = (b = 3))")]
    [InlineData("a = 1 + 2", "(a = (1 + 2))")]
    [InlineData("f(1, 2 + 3)", "f(1, (2 + 3))")]
    [InlineData("f()", "f()")]
    [InlineData("f(1)(2)", "f(1)(2)")]
    [InlineData("-f(1)", "(-f(1))")]
    [InlineData("+2 * 3", "((+2) * 3)")]
    [InlineData("- +a", "(-(+a))")]
    [InlineData("1 + +2", "(1 + (+2))")]
    public void OperatorsHaveTheirPrecedenceAndAssociativity(string expression, string expected)
    {
        var tree = Parse(expression + ";");
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(expected, Show(Assert.IsType<WsExpressionStatementSyntax>(Assert.Single(tree.Root.Statements)).Expression));
    }

    [Fact]
    public void ConstantsAreDeclaredWithConst()
    {
        var tree = Parse("const limit = 10;");
        Assert.Empty(tree.Diagnostics);
        var declaration = Assert.IsType<WsVariableDeclarationSyntax>(Assert.Single(tree.Root.Statements));
        Assert.Equal((WsDeclarationKind.Const, "limit", new WsSourceSpan(6, 5)),
            (declaration.Kind, declaration.Name, declaration.NameSpan));
    }

    [Theory]
    [InlineData("const x;", 6)]
    [InlineData("var x;", 4)]
    public void ADeclarationWithoutAValueIsAnError(string source, int nameStart)
    {
        var tree = Parse(source);
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal((WsDiagnosticCodes.MissingInitializer, new WsSourceSpan(nameStart, 1)), (diagnostic.Code, diagnostic.Span));
        Assert.Null(Assert.IsType<WsVariableDeclarationSyntax>(Assert.Single(tree.Root.Statements)).Initializer);
    }

    [Fact]
    public void AMissingSemicolonIsReportedWhereItBelongs()
    {
        var diagnostic = Assert.Single(Parse("var x = 10").Diagnostics);
        Assert.Equal((WsDiagnosticCodes.MissingSemicolon, new WsSourceSpan(10, 0)), (diagnostic.Code, diagnostic.Span));
    }

    [Fact]
    public void AMissingSemicolonBeforeTheNextDeclarationLeavesThatDeclarationIntact()
    {
        var tree = Parse("var x = 10\nvar y = 20;");
        Assert.Equal(WsDiagnosticCodes.MissingSemicolon, Assert.Single(tree.Diagnostics).Code);
        Assert.Equal(2, tree.Root.Statements.Count);
        Assert.Equal("y", Assert.IsType<WsVariableDeclarationSyntax>(tree.Root.Statements[1]).Name);
    }

    [Fact]
    public void ABlockHoldsStatementsAndNests()
    {
        var tree = Parse("{ var a = 1; { a; } {} }");
        Assert.Empty(tree.Diagnostics);
        var block = Assert.IsType<WsBlockSyntax>(Assert.Single(tree.Root.Statements));
        Assert.Equal(new WsSourceSpan(0, 24), block.Span);
        Assert.Equal(3, block.Statements.Count);
        Assert.IsType<WsExpressionStatementSyntax>(Assert.Single(Assert.IsType<WsBlockSyntax>(block.Statements[1]).Statements));
        Assert.Empty(Assert.IsType<WsBlockSyntax>(block.Statements[2]).Statements);
    }

    [Fact]
    public void AnUnclosedBlockIsReportedAtItsBrace()
    {
        var tree = Parse("var a = 1;\n{ a;");
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal((WsDiagnosticCodes.UnexpectedToken, new WsSourceSpan(11, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Fact]
    public void AnErrorInABlockDoesNotSwallowItsBrace()
    {
        var tree = Parse("{ var a = ; } var b = 2;");
        Assert.Equal(WsDiagnosticCodes.UnexpectedToken, Assert.Single(tree.Diagnostics).Code);
        Assert.Equal(2, tree.Root.Statements.Count);
        Assert.Equal("b", Assert.IsType<WsVariableDeclarationSyntax>(tree.Root.Statements[1]).Name);
    }

    [Fact]
    public void AStrayClosingBraceIsAnError()
    {
        var tree = Parse("var a = 1; } var b = 2;");
        Assert.Equal(WsDiagnosticCodes.UnexpectedToken, Assert.Single(tree.Diagnostics).Code);
        Assert.Equal("b", Assert.IsType<WsVariableDeclarationSyntax>(tree.Root.Statements[^1]).Name);
    }

    [Fact]
    public void BlocksNestOnlySoDeep()
    {
        var source = new string('{', WsParser.MaxRecursionDepth + 1) + new string('}', WsParser.MaxRecursionDepth + 1);
        Assert.Contains(Parse(source).Diagnostics, d => d.Code == WsDiagnosticCodes.NestingTooDeep);
    }

    [Fact]
    public void SeveralErrorsAreReportedInOnePass()
    {
        var tree = Parse("var a = ;\nvar b = 1\nconst c;\nvar d = 4;");
        Assert.Equal(
            new[] { WsDiagnosticCodes.UnexpectedToken, WsDiagnosticCodes.MissingSemicolon, WsDiagnosticCodes.MissingInitializer },
            tree.Diagnostics.Select(d => d.Code));
        Assert.Equal(4, tree.Root.Statements.Count);
        Assert.Equal("d", Assert.IsType<WsVariableDeclarationSyntax>(tree.Root.Statements[3]).Name);
    }

    [Fact]
    public void LetIsReportedAsNotAWebLingoKeyword()
    {
        var diagnostic = Assert.Single(Parse("let x = 1;").Diagnostics);
        Assert.Equal((WsDiagnosticCodes.UnexpectedToken, new WsSourceSpan(0, 3)), (diagnostic.Code, diagnostic.Span));
        Assert.Contains("'var'", diagnostic.Message);
    }

    [Theory]
    [InlineData("1 = 2;", 0, 1)]
    [InlineData("(a) = 2;", 0, 3)]
    [InlineData("a + b = 2;", 0, 5)]
    [InlineData("f() = 2;", 0, 3)]
    public void OnlyAVariableCanBeAssigned(string source, int start, int length)
    {
        var diagnostic = Assert.Single(Parse(source).Diagnostics);
        Assert.Equal((WsDiagnosticCodes.InvalidAssignmentTarget, new WsSourceSpan(start, length)), (diagnostic.Code, diagnostic.Span));
    }

    [Theory]
    [InlineData(";")]
    [InlineData(")")]
    [InlineData("var = 5;")]
    [InlineData("var 5 = 5;")]
    [InlineData("f(1,);")]
    [InlineData("f(1;")]
    [InlineData("(1 + 2;")]
    [InlineData("1 +;")]
    [InlineData("var x = 1 2;")]
    public void MalformedStatementsGiveOneDiagnosticEach(string source)
    {
        var diagnostic = Assert.Single(Parse(source).Diagnostics);
        Assert.True(diagnostic.Code is WsDiagnosticCodes.UnexpectedToken or WsDiagnosticCodes.MissingSemicolon,
            diagnostic.Code + ": " + diagnostic.Message);
        Assert.Equal(WsDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void AnExpressionCutShortByTheEndOfTheFileIsReported()
    {
        var diagnostic = Assert.Single(Parse("var x = 1 +").Diagnostics);
        Assert.Equal(WsDiagnosticCodes.UnexpectedToken, diagnostic.Code);
        Assert.Contains("end of the file", diagnostic.Message);
    }

    [Fact]
    public void ALexicalErrorIsNotReportedAgainByTheParser()
    {
        var tree = Parse("var a = 1 @ 2;\nvar b = 10abc;\nvar c = 3;");
        Assert.Equal(new[] { WsDiagnosticCodes.InvalidCharacter, WsDiagnosticCodes.InvalidNumericLiteral },
            tree.Diagnostics.Select(d => d.Code));
        Assert.Equal(3, tree.Root.Statements.Count);
    }

    [Fact]
    public void DiagnosticsAreInSourceOrder()
    {
        var tree = Parse("var a = ;\nvar b = 1 # 2;\nconst c;");
        var starts = tree.Diagnostics.Select(d => d.Span.Start).ToArray();
        Assert.Equal(3, starts.Length);
        Assert.Equal(starts.OrderBy(s => s), starts);
    }

    [Fact]
    public void SpansCoverWhatWasParsed()
    {
        var tree = Parse("var total = (1 + 2) * x;  writeLn(total);");
        var declaration = Assert.IsType<WsVariableDeclarationSyntax>(tree.Root.Statements[0]);
        Assert.Equal(new WsSourceSpan(0, 24), declaration.Span);
        var product = Assert.IsType<WsBinaryExpressionSyntax>(declaration.Initializer);
        Assert.Equal((new WsSourceSpan(12, 11), new WsSourceSpan(20, 1)), (product.Span, product.OperatorSpan));
        Assert.Equal(new WsSourceSpan(12, 7), product.Left.Span);
        var call = Assert.IsType<WsExpressionStatementSyntax>(tree.Root.Statements[1]);
        Assert.Equal((new WsSourceSpan(26, 15), new WsSourceSpan(26, 14)), (call.Span, call.Expression.Span));
    }

    [Fact]
    public void AnEmptySourceHasNoStatements()
    {
        var tree = Parse("  // nothing here\n");
        Assert.Empty(tree.Root.Statements);
        Assert.Empty(tree.Diagnostics);
        Assert.False(tree.HasErrors);
    }

    [Theory]
    [InlineData("(", ")")]
    [InlineData("-", "")]
    [InlineData("f(", ")")]
    [InlineData("a = ", "")]
    public void DeepNestingIsADiagnosticNotACrash(string open, string close)
    {
        var source = "var x = " + string.Concat(Enumerable.Repeat(open, 100_000)) + "1"
            + string.Concat(Enumerable.Repeat(close, 100_000)) + ";\nvar y = 2;";
        var tree = Parse(source);
        Assert.Equal(WsDiagnosticCodes.NestingTooDeep, Assert.Single(tree.Diagnostics).Code);
        Assert.Equal("y", Assert.IsType<WsVariableDeclarationSyntax>(tree.Root.Statements[^1]).Name);
    }

    [Fact]
    public void AVeryLongChainOfOperationsIsADiagnosticNotACrash()
    {
        var tree = Parse("var x = 1" + string.Concat(Enumerable.Repeat(" + 1", 100_000)) + ";");
        Assert.Equal(WsDiagnosticCodes.NestingTooDeep, Assert.Single(tree.Diagnostics).Code);
    }

    [Fact]
    public void NestingWithinTheLimitsIsFine()
    {
        var parenthesized = "var x = " + new string('(', 200) + "1" + new string(')', 200) + ";";
        var chain = "var y = 1" + string.Concat(Enumerable.Repeat(" + 1", 900)) + ";";
        Assert.Empty(Parse(parenthesized).Diagnostics);
        Assert.Empty(Parse(chain).Diagnostics);
    }

    private static WsSyntaxTree Parse(string source) => new WsParser(source).ParseCompilationUnit();

    private static string Show(WsExpressionSyntax expression) => expression switch
    {
        WsIntegerLiteralSyntax literal => literal.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        WsNameSyntax name => name.Name,
        WsUnaryExpressionSyntax unary => $"({(unary.Operator == WsUnaryOperator.Negate ? "-" : "+")}{Show(unary.Operand)})",
        WsBinaryExpressionSyntax binary => $"({Show(binary.Left)} {Symbol(binary.Operator)} {Show(binary.Right)})",
        WsParenthesizedExpressionSyntax parenthesized => $"[{Show(parenthesized.Expression)}]",
        WsAssignmentExpressionSyntax assignment => $"({assignment.Target.Name} = {Show(assignment.Value)})",
        WsCallExpressionSyntax call => $"{Show(call.Callee)}({string.Join(", ", call.Arguments.Select(Show))})",
        _ => "?"
    };

    private static string Symbol(WsBinaryOperator op) => op switch
    {
        WsBinaryOperator.Add => "+",
        WsBinaryOperator.Subtract => "-",
        WsBinaryOperator.Multiply => "*",
        _ => "/"
    };
}
