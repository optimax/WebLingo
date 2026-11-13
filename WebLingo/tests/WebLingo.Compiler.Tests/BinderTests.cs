using WebLingo.Compiler;
using WebLingo.Core;
using Xunit;
namespace WebLingo.Compiler.Tests;

public sealed class BinderTests
{
    internal static readonly WsCompilerOptions WithWrite = new([new WsBuiltinDeclaration("write", 1), new WsBuiltinDeclaration("writeLn", 1)]);

    [Fact]
    public void BindsTheSpecificationsExample()
    {
        var compilation = Compile("var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);");
        Assert.Empty(compilation.Diagnostics);
        var program = compilation.BoundProgram!;
        Assert.Equal(new[] { "a", "b", "result" }, program.Locals.Select(l => l.Name));
        Assert.Equal(new[] { 0, 1, 2 }, program.Locals.Select(l => l.Index));
        var call = Assert.IsType<WsBoundBuiltinCall>(Assert.IsType<WsBoundExpressionStatement>(program.Statements[3]).Expression);
        Assert.Same(Assert.Single(program.Builtins), call.Builtin);
        Assert.Same(program.Locals[2], Assert.IsType<WsBoundLocalReference>(Assert.Single(call.Arguments)).Local);
    }

    [Fact]
    public void AssigningAnUndeclaredNameIsAnError()
    {
        var diagnostic = Assert.Single(Compile("x = 10;").Diagnostics);
        Assert.Equal((WsDiagnosticCodes.UndefinedIdentifier, new WsSourceSpan(0, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Fact]
    public void ReadingAnUndeclaredNameIsAnError()
    {
        var diagnostic = Assert.Single(Compile("writeLn(y + 1);").Diagnostics);
        Assert.Equal((WsDiagnosticCodes.UndefinedIdentifier, new WsSourceSpan(8, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Theory]
    [InlineData("var x = 1; var x = 2;")]
    [InlineData("var x = 1; const x = 2;")]
    [InlineData("const x = 1; var x = 3;")]
    public void ADuplicateDeclarationIsReportedAtTheLaterName(string source)
    {
        var diagnostic = Assert.Single(Compile(source).Diagnostics);
        Assert.Equal(WsDiagnosticCodes.DuplicateDeclaration, diagnostic.Code);
        Assert.Equal(source.LastIndexOf('x'), diagnostic.Span.Start);
    }

    [Fact]
    public void AssigningAConstantIsAnError()
    {
        var diagnostic = Assert.Single(Compile("const c = 1; c = 2;").Diagnostics);
        Assert.Equal((WsDiagnosticCodes.AssignmentToConstant, new WsSourceSpan(13, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Fact]
    public void AnUnknownFunctionIsUndefined()
    {
        Assert.Equal(WsDiagnosticCodes.UndefinedIdentifier, Assert.Single(Compile("show(1);").Diagnostics).Code);
    }

    [Fact]
    public void ABuiltinIsUndefinedWhereTheHostDoesNotOfferIt()
    {
        var compilation = WsCompilation.Create("writeLn(1);", "test.ws", WsCompilerOptions.Empty);
        Assert.Equal(WsDiagnosticCodes.UndefinedIdentifier, Assert.Single(compilation.Diagnostics).Code);
    }

    [Theory]
    [InlineData("var x = 1; x(2);", WsDiagnosticCodes.NotAFunction)]
    [InlineData("var x = 1; (x)(2);", WsDiagnosticCodes.NotAFunction)]
    [InlineData("writeLn(1)(2);", WsDiagnosticCodes.NotAFunction)]
    [InlineData("(1)(2);", WsDiagnosticCodes.NotAFunction)]
    [InlineData("writeLn();", WsDiagnosticCodes.WrongArgumentCount)]
    [InlineData("writeLn(1, 2);", WsDiagnosticCodes.WrongArgumentCount)]
    [InlineData("var p = write;", WsDiagnosticCodes.InvalidOperation)]
    [InlineData("write = 1;", WsDiagnosticCodes.InvalidOperation)]
    public void CallsAndBuiltinsAreChecked(string source, string code)
    {
        var diagnostic = Assert.Single(Compile(source).Diagnostics);
        Assert.Equal(code, diagnostic.Code);
    }

    [Fact]
    public void AParenthesizedBuiltinNameIsCalledLikeTheName()
    {
        var compilation = Compile("((write))(1);");
        Assert.Empty(compilation.Diagnostics);
        Assert.IsType<WsBoundBuiltinCall>(((WsBoundExpressionStatement)compilation.BoundProgram!.Statements[0]).Expression);
    }

    [Fact]
    public void ADeclarationMayShadowABuiltin()
    {
        var compilation = Compile("var write = 1;");
        Assert.Empty(compilation.Diagnostics);
        Assert.Equal(WsDiagnosticCodes.NotAFunction, Assert.Single(Compile("var write = 1; write(2);").Diagnostics).Code);
    }

    [Theory]
    [InlineData("writeLn(x); var x = 1;", 8)]
    [InlineData("x = 2; var x = 1;", 0)]
    [InlineData("var x = x + 1;", 8)]
    public void ANameDoesNotExistBeforeItsDeclaration(string source, int start)
    {
        var diagnostic = Assert.Single(Compile(source).Diagnostics);
        Assert.Equal((WsDiagnosticCodes.UndefinedIdentifier, new WsSourceSpan(start, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Fact]
    public void BeforeItsDeclarationANameMeansWhatItMeansOutside()
    {
        // The host's write is outside the program's scope: it is write until the program declares its own.
        var compilation = Compile("write(1); var write = 2;");
        Assert.Empty(compilation.Diagnostics);
        Assert.IsType<WsBoundBuiltinCall>(((WsBoundExpressionStatement)compilation.BoundProgram!.Statements[0]).Expression);
    }

    [Fact]
    public void EveryBindingErrorIsReported()
    {
        var compilation = Compile("a = 1;\nconst c = 2;\nc = 3;\nvar c = 4;\nwriteLn(b);");
        Assert.Equal(
            new[] {
                WsDiagnosticCodes.UndefinedIdentifier, WsDiagnosticCodes.AssignmentToConstant,
                WsDiagnosticCodes.DuplicateDeclaration, WsDiagnosticCodes.UndefinedIdentifier
            },
            compilation.Diagnostics.Select(d => d.Code));
        Assert.Null(compilation.LoweredProgram);
    }

    [Fact]
    public void SyntaxErrorsStopCompilationBeforeBinding()
    {
        var compilation = Compile("var x = 1\nwriteLn(undefinedName);");
        Assert.Equal(WsDiagnosticCodes.MissingSemicolon, Assert.Single(compilation.Diagnostics).Code);
        Assert.Null(compilation.BoundProgram);
        Assert.True(compilation.HasErrors);
    }

    [Fact]
    public void BuiltinsAreNumberedInTheOrderTheProgramUsesThem()
    {
        var options = new WsCompilerOptions([new WsBuiltinDeclaration("first", 0), new WsBuiltinDeclaration("writeLn", 1)]);
        var compilation = WsCompilation.Create("writeLn(1); first(); writeLn(2);", "test.ws", options);
        Assert.Empty(compilation.Diagnostics);
        var builtins = compilation.BoundProgram!.Builtins;
        Assert.Equal(new[] { ("writeLn", 0), ("first", 1) }, builtins.Select(b => (b.Name, b.Index)));
    }

    internal static WsCompilation Compile(string source) => WsCompilation.Create(source, "test.ws", WithWrite);
}
