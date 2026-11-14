using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using WebLingo.VM.Stack;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// Pages (specification, section 13, "Contracts"; section 9, M0.12.1): each compiled by the template compiler and run on
/// both VMs, which must write the same page and report the same errors.
/// </summary>
public sealed class PageTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("Hello, world.", "Hello, world.")]
    [InlineData("<p><%= 1 + 2 %></p>", "<p>3</p>")]
    // <%= %> escapes the text write would write, in text and attribute values alike.
    [InlineData("<%= \"<a href=\\\"x\\\">Tom & 'Jerry'</a>\" %>", "&lt;a href=&quot;x&quot;&gt;Tom &amp; &#39;Jerry&#39;&lt;/a&gt;")]
    [InlineData("<%= [1, \"a\"] %>", "[1, &quot;a&quot;]")]
    [InlineData("<% class T { toString() { return \"<T>\"; } } %><%= new T() %>", "&lt;T&gt;")]
    // <%- %> writes it as it stands.
    [InlineData("<%- \"<b>bold</b>\" %>", "<b>bold</b>")]
    // null writes nothing, so that an optional read that finds nothing is blank; write(null) writes null.
    [InlineData("[<%= null %>][<%- null %>]", "[][]")]
    [InlineData("<% var p = null; %>[<%= p?.name %>]<% write(null); %>", "[]null")]
    // <%# %> is nothing, and ends at the first %>.
    [InlineData("a<%# a \"note %>b", "ab")]
    // A statement may span several blocks, the text between them in its body.
    [InlineData("<% foreach (var p in [\"a\", \"b\"]) { %><li><%= p %></li><% } %>", "<li>a</li><li>b</li>")]
    [InlineData("<% if (1 > 2) { %>yes<% } else { %>no<% } %>", "no")]
    [InlineData("a<% write(\"b\"); %>c", "abc")]
    // A block's code is WebLingo's tokens: %> in a string or a comment does not end it.
    [InlineData("<% var end = \"%>\"; %><%= end %>", "%&gt;")]
    [InlineData("<% /* %> */ var x = 1; %><%= x %>", "1")]
    [InlineData("<%= \"%>\" + '%>' %>", "%&gt;%&gt;")]
    // A line holding only statement blocks, and spaces, writes nothing, not even its line break.
    [InlineData("<ul>\n<% foreach (var i in [1, 2]) { %>\n  <li><%= i %></li>\n<% } %>\n</ul>\n", "<ul>\n  <li>1</li>\n  <li>2</li>\n</ul>\n")]
    [InlineData("a\n    <% var x = 1; %>  \t\nb", "a\nb")]
    [InlineData("a\r\n<% var x = 1; %>\r\nb\r\n", "a\r\nb\r\n")]
    [InlineData("a\r<% var x = 1; %>\rb", "a\rb")]
    [InlineData("<% if (true) { %> <% } %>\nend", "end")]
    [InlineData("a\n<%\n  var x = 1;\n%>\nb<%= x %>", "a\nb1")]
    [InlineData("a\n<%# a note %>\nb", "a\nb")]
    [InlineData("a\n<% var x = 1; %>", "a\n")]
    // A line that writes something keeps its line break.
    [InlineData("<%= 1 %>\n", "1\n")]
    [InlineData("<%- null %>\n", "\n")]
    [InlineData("x <% var y = 1; %>\n", "x \n")]
    [InlineData("\n\n", "\n\n")]
    public void APageWritesItsText(string page, string written)
    {
        Assert.Equal(("", written, ""), Render(page));
    }

    [Theory]
    // A block with no closing %>.
    [InlineData("a<% var x = 1;", "WL5001", 1, 2)]
    [InlineData("a\n  <%= 1", "WL5001", 2, 3)]
    [InlineData("<%# a note", "WL5001", 1, 1)]
    // A // comment runs to the end of its line, %> included, so the page's text after it is read as code.
    [InlineData("<% // a note %>\n<p>", "WL5001", 1, 1)]
    // An output block needs an expression.
    [InlineData("<%= %>", "WL1001", 1, 5)]
    [InlineData("<p>\n<%- %>", "WL1001", 2, 5)]
    [InlineData("<%= 1; %>", "WL1001", 1, 6)]
    // A block's code is checked as a script's is, at its place in the page.
    [InlineData("<%= y %>", "WL2001", 1, 5)]
    [InlineData("line\n<% var x = ; %>", "WL1001", 2, 12)]
    [InlineData("<p>\n  <% var s = \"a; %>\n", "WL1010", 2, 14)]
    public void APageThatDoesNotCompileIsReportedWhereItIsWrong(string page, string code, int line, int column)
    {
        var (diagnostics, _, _) = Render(page);
        Assert.Contains($"{code} at {line},{column}:", diagnostics);
    }

    [Fact]
    public void ARuntimeErrorIsReportedAtItsPlaceInThePage()
    {
        var (_, written, error) = Render("<p>\n<%= [1][5] %>\n</p>");
        Assert.Equal("<p>\n", written);
        Assert.StartsWith("WL3005 at 2,", error);
    }

    [Fact]
    public void TheParsedPageIsAProgramThatWritesIt()
    {
        var tree = WebLingoTemplateParser.Parse(new WebLingoSourceText("a<%= b %>c", "page.wlp"));
        Assert.False(tree.HasErrors);
        Assert.Equal(3, tree.Root.Statements.Count);
        Assert.Equal("page.wlp", tree.Source.Name);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("&<>\"'", "&amp;&lt;&gt;&quot;&#39;")]
    [InlineData("a & b", "a &amp; b")]
    public void EscapingReplacesWhatHtmlGivesAMeaning(string text, string escaped)
    {
        Assert.Equal(escaped, WebLingoTemplateWriteFunction.Escape(text));
    }

    /// <summary>
    /// A page compiled and run on both VMs, which must give the same: its diagnostics (each as its code, its line and
    /// column, and its message), what it writes, and its runtime error, if any.
    /// </summary>
    private static (string Diagnostics, string Written, string Error) Render(string page)
    {
        var text = new WebLingoSourceText(page, "page.wlp");
        var compiler = new WebLingoTemplateCompiler(Options);

        var registerCompiled = compiler.Compile(text);
        var registerOutput = new WebLingoCollectedOutput();
        var registerResult = registerCompiled.Module is { } module
            ? new WebLingoVirtualMachine().Execute(module, Context(registerOutput))
            : null;
        var register = (Describe(text, registerCompiled.Diagnostics), registerOutput.Text, Describe(text, registerResult?.Error));

        var stackCompiled = compiler.CompileForStack(text);
        var stackOutput = new WebLingoCollectedOutput();
        var stackResult = stackCompiled.Module is { } stackModule
            ? new WebLingoStackVirtualMachine().Execute(stackModule, Context(stackOutput))
            : null;
        var stack = (Describe(text, stackCompiled.Diagnostics), stackOutput.Text, Describe(text, stackResult?.Error));

        Assert.Equal(register, stack);
        return register;
    }

    private static readonly WebLingoCompilerOptions Options =
        new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1), new WebLingoBuiltinDeclaration("delay", 1)]);

    private static WebLingoExecutionContext Context(IWebLingoTextOutput output)
    {
        var services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output));
        foreach (var builtin in WebLingoTemplateBuiltins.Create(output)) services.Register(builtin);
        return new WebLingoExecutionContext(new WebLingoWorkerId(0)) { Services = services, InstructionBudget = 1_000_000 };
    }

    private static string Describe(WebLingoSourceText text, IReadOnlyList<WebLingoDiagnostic> diagnostics)
    {
        var lines = new List<string>();
        foreach (var diagnostic in diagnostics) lines.Add(Describe(text, diagnostic));
        return string.Join(" | ", lines);
    }

    private static string Describe(WebLingoSourceText text, WebLingoDiagnostic? diagnostic)
    {
        if (diagnostic is null) return "";
        var location = text.GetLocation(diagnostic.Span.Start);
        return $"{diagnostic.Code} at {location.Line},{location.Column}: {diagnostic.Message}";
    }
}
