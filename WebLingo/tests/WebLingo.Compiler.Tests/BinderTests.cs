using WebLingo.Compiler;
using WebLingo.Core;
using Xunit;
namespace WebLingo.Compiler.Tests;

public sealed class BinderTests
{
    internal static readonly WebLingoCompilerOptions WithWrite = new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1)]);

    [Fact]
    public void BindsTheSpecificationsExample()
    {
        var compilation = Compile("var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);");
        Assert.Empty(compilation.Diagnostics);
        var program = compilation.BoundProgram!;
        Assert.Equal(new[] { "a", "b", "result" }, program.Locals.Select(l => l.Name));
        Assert.Equal(new[] { 0, 1, 2 }, program.Locals.Select(l => l.Index));
        var call = Assert.IsType<WebLingoBoundBuiltinCall>(Assert.IsType<WebLingoBoundExpressionStatement>(program.Statements[3]).Expression);
        Assert.Same(Assert.Single(program.Builtins), call.Builtin);
        Assert.Same(program.Locals[2], Assert.IsType<WebLingoBoundLocalReference>(Assert.Single(call.Arguments)).Local);
    }

    [Fact]
    public void AssigningAnUndeclaredNameIsAnError()
    {
        var diagnostic = Assert.Single(Compile("x = 10;").Diagnostics);
        Assert.Equal((WebLingoDiagnosticCodes.UndefinedIdentifier, new WebLingoSourceSpan(0, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Fact]
    public void ReadingAnUndeclaredNameIsAnError()
    {
        var diagnostic = Assert.Single(Compile("writeLn(y + 1);").Diagnostics);
        Assert.Equal((WebLingoDiagnosticCodes.UndefinedIdentifier, new WebLingoSourceSpan(8, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Theory]
    [InlineData("var x = 1; var x = 2;")]
    [InlineData("var x = 1; const x = 2;")]
    [InlineData("const x = 1; var x = 3;")]
    public void ADuplicateDeclarationIsReportedAtTheLaterName(string source)
    {
        var diagnostic = Assert.Single(Compile(source).Diagnostics);
        Assert.Equal(WebLingoDiagnosticCodes.DuplicateDeclaration, diagnostic.Code);
        Assert.Equal(source.LastIndexOf('x'), diagnostic.Span.Start);
    }

    [Fact]
    public void AssigningAConstantIsAnError()
    {
        var diagnostic = Assert.Single(Compile("const c = 1; c = 2;").Diagnostics);
        Assert.Equal((WebLingoDiagnosticCodes.AssignmentToConstant, new WebLingoSourceSpan(13, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Fact]
    public void AnUnknownFunctionIsUndefined()
    {
        Assert.Equal(WebLingoDiagnosticCodes.UndefinedIdentifier, Assert.Single(Compile("show(1);").Diagnostics).Code);
    }

    [Fact]
    public void ABuiltinIsUndefinedWhereTheHostDoesNotOfferIt()
    {
        var compilation = WebLingoCompilation.Create("writeLn(1);", "test.wl", WebLingoCompilerOptions.Empty);
        Assert.Equal(WebLingoDiagnosticCodes.UndefinedIdentifier, Assert.Single(compilation.Diagnostics).Code);
    }

    [Theory]
    [InlineData("(1)(2);", WebLingoDiagnosticCodes.NotAFunction)]
    [InlineData("\"f\"();", WebLingoDiagnosticCodes.NotAFunction)]
    [InlineData("writeLn();", WebLingoDiagnosticCodes.WrongArgumentCount)]
    [InlineData("writeLn(1, 2);", WebLingoDiagnosticCodes.WrongArgumentCount)]
    [InlineData("function f(a) {} f();", WebLingoDiagnosticCodes.WrongArgumentCount)]
    [InlineData("write = 1;", WebLingoDiagnosticCodes.InvalidOperation)]
    [InlineData("function f() {} f = 1;", WebLingoDiagnosticCodes.AssignmentToConstant)]
    [InlineData("function f(a, a) {}", WebLingoDiagnosticCodes.DuplicateDeclaration)]
    [InlineData("function f() {} function f() {}", WebLingoDiagnosticCodes.DuplicateDeclaration)]
    [InlineData("return 1;", WebLingoDiagnosticCodes.ReturnOutsideFunction)]
    [InlineData("function f() { break; }", WebLingoDiagnosticCodes.NotInLoop)]
    [InlineData("while (true) { function f() { continue; } }", WebLingoDiagnosticCodes.NotInLoop)]
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
        Assert.IsType<WebLingoBoundBuiltinCall>(((WebLingoBoundExpressionStatement)compilation.BoundProgram!.Statements[0]).Expression);
    }

    [Fact]
    public void ADeclarationMayShadowABuiltin()
    {
        var compilation = Compile("var write = 1;");
        Assert.Empty(compilation.Diagnostics);
        // Now a variable, which is called as any value is, and found not to be a function at run time.
        var call = Compile("var write = 1; write(2);");
        Assert.Empty(call.Diagnostics);
        Assert.IsType<WebLingoBoundCall>(((WebLingoBoundExpressionStatement)call.BoundProgram!.Statements[1]).Expression);
    }

    [Fact]
    public void AFunctionIsInScopeThroughoutItsBlock()
    {
        Assert.Empty(Compile("writeLn(f(1)); function f(a) { return g(a); } function g(b) { return b; }").Diagnostics);
        Assert.Equal(WebLingoDiagnosticCodes.UndefinedIdentifier, Assert.Single(Compile("{ function f() {} } f();").Diagnostics).Code);
    }

    [Fact]
    public void ANameOfAnOuterFunctionIsCaptured()
    {
        var compilation = Compile("var a = 1; var b = 2; function f() { return a; }");
        Assert.Empty(compilation.Diagnostics);
        var locals = compilation.BoundProgram!.Locals;
        // The function is declared first, since it is in scope throughout the block.
        Assert.Equal(new[] { ("f", false), ("a", true), ("b", false) }, locals.Select(l => (l.Name, l.IsCaptured)));
    }

    [Theory]
    [InlineData("writeLn(x); var x = 1;", 8)]
    [InlineData("x = 2; var x = 1;", 0)]
    [InlineData("var x = x + 1;", 8)]
    public void ANameDoesNotExistBeforeItsDeclaration(string source, int start)
    {
        var diagnostic = Assert.Single(Compile(source).Diagnostics);
        Assert.Equal((WebLingoDiagnosticCodes.UndefinedIdentifier, new WebLingoSourceSpan(start, 1)), (diagnostic.Code, diagnostic.Span));
    }

    [Fact]
    public void BeforeItsDeclarationANameMeansWhatItMeansOutside()
    {
        // The host's write is outside the program's scope: it is write until the program declares its own.
        var compilation = Compile("write(1); var write = 2;");
        Assert.Empty(compilation.Diagnostics);
        Assert.IsType<WebLingoBoundBuiltinCall>(((WebLingoBoundExpressionStatement)compilation.BoundProgram!.Statements[0]).Expression);
    }

    [Fact]
    public void EveryBindingErrorIsReported()
    {
        var compilation = Compile("a = 1;\nconst c = 2;\nc = 3;\nvar c = 4;\nwriteLn(b);");
        Assert.Equal(
            new[] {
                WebLingoDiagnosticCodes.UndefinedIdentifier, WebLingoDiagnosticCodes.AssignmentToConstant,
                WebLingoDiagnosticCodes.DuplicateDeclaration, WebLingoDiagnosticCodes.UndefinedIdentifier
            },
            compilation.Diagnostics.Select(d => d.Code));
        Assert.Null(compilation.LoweredProgram);
    }

    [Fact]
    public void SyntaxErrorsStopCompilationBeforeBinding()
    {
        var compilation = Compile("var x = 1\nwriteLn(undefinedName);");
        Assert.Equal(WebLingoDiagnosticCodes.MissingSemicolon, Assert.Single(compilation.Diagnostics).Code);
        Assert.Null(compilation.BoundProgram);
        Assert.True(compilation.HasErrors);
    }

    [Fact]
    public void BuiltinsAreNumberedInTheOrderTheProgramUsesThem()
    {
        var options = new WebLingoCompilerOptions([new WebLingoBuiltinDeclaration("first", 0), new WebLingoBuiltinDeclaration("writeLn", 1)]);
        var compilation = WebLingoCompilation.Create("writeLn(1); first(); writeLn(2);", "test.wl", options);
        Assert.Empty(compilation.Diagnostics);
        var builtins = compilation.BoundProgram!.Builtins;
        Assert.Equal(new[] { ("writeLn", 0), ("first", 1) }, builtins.Select(b => (b.Name, b.Index)));
    }

    internal static WebLingoCompilation Compile(string source) => WebLingoCompilation.Create(source, "test.wl", WithWrite);
}
