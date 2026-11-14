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
        Assert.Contains($"{code} at page.wlp {line},{column}:", diagnostics);
    }

    [Fact]
    public void ARuntimeErrorIsReportedAtItsPlaceInThePage()
    {
        var (_, written, error) = Render("<p>\n<%= [1][5] %>\n</p>");
        Assert.Equal("<p>\n", written);
        Assert.StartsWith("WL3005 at page.wlp 2,", error);
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

    [Theory]
    // An include is textual: the template is read in its place, and sees the variables there, as its text would.
    [InlineData("a<% include \"/h.wlp\"; %>c", "h.wlp=b", "abc")]
    [InlineData("<% var name = \"Ann\"; %><% include \"/greet.wlp\"; %>", "greet.wlp=Hello, <%= name %>!", "Hello, Ann!")]
    [InlineData("<% include \"/h.wlp\"; %><%= x * 3 %>", "h.wlp=<% var x = 2; %>", "6")]
    [InlineData("<% foreach (var i in [1, 2]) { include \"/item.wlp\"; } %>", "item.wlp=<li><%= i %></li>", "<li>1</li><li>2</li>")]
    [InlineData("<% include \"/h.wlp\"; include \"h\"; %>", "h.wlp=x", "xx")]
    // A path starting with / is from the application's root, any other from the including file's folder.
    [InlineData("<% include \"/inc/a.wlp\"; %>", "inc/a.wlp=a<% include \"b.wlp\"; %>|inc/b.wlp=b", "ab")]
    [InlineData("<% include \"inc/a\"; %>", "inc/a.wlp=a<% include \"/b.wlp\"; %>|b.wlp=root b", "aroot b")]
    // Each file's lines are its own: a line holding only an include writes nothing, the template its own lines.
    [InlineData("<html>\n<% include \"/head.wlp\"; %>\n</html>", "head.wlp=<% var t = 1; %>\n<head><%= t %></head>\n", "<html>\n<head>1</head>\n</html>")]
    // include is a name like any other where no string follows it.
    [InlineData("<% var include = 2; %><%= include %>", "", "2")]
    public void AnIncludeReadsTheTemplateInItsPlace(string page, string templates, string written)
    {
        Assert.Equal(("", written, ""), Render(page, Templates(templates)));
    }

    [Theory]
    [InlineData("<% include \"/nope.wlp\"; %>", "", "WL5002 at page.wlp 1,12: The template '/nope.wlp' cannot be included: there is no such file.")]
    [InlineData("<% include \"/h.wlp\" %>", "h.wlp=x", "WL1002 at page.wlp 1,20: A ';' is missing here.")]
    [InlineData("<% include \"/a.wlp\"; %>", "a.wlp=<% include \"b.wlp\"; %>|b.wlp=<% include \"a.wlp\"; %>",
        "WL5003 at b.wlp 1,12: Including 'a.wlp' makes a cycle: a.wlp includes b.wlp includes a.wlp.")]
    [InlineData("<% include \"/page.wlp\"; %>", "", "WL5003 at page.wlp 1,12: Including '/page.wlp' makes a cycle: page.wlp includes page.wlp.")]
    // An error in a template is reported in it, at its own line and column.
    [InlineData("<p>\n<% include \"/h.wlp\"; %>", "h.wlp=line\n<%= y %>", "WL2001 at h.wlp 2,5: 'y' is not defined.")]
    [InlineData("<% include \"/h.wlp\"; %>", "h.wlp=\n<% var x = 1;", "WL5001 at h.wlp 2,1: This block has no closing '%>'.")]
    public void AnIncludeThatFailsIsReportedWhereItIs(string page, string templates, string diagnostic)
    {
        Assert.Equal(diagnostic, Render(page, Templates(templates)).Diagnostics);
    }

    [Fact]
    public void ARuntimeErrorInATemplateIsReportedInIt()
    {
        var (_, written, error) = Render("<p>\n<% include \"/h.wlp\"; %>", Templates("h.wlp=before\n<%= [1][5] %>"));
        Assert.Equal("<p>\nbefore\n", written);
        Assert.StartsWith("WL3005 at h.wlp 2,", error);
    }

    [Fact]
    public void IncludesNestAtMost64Deep()
    {
        var chain = new List<string>();
        for (var i = 0; i < 70; i++) chain.Add($"t{i}.wlp=<% include \"t{i + 1}.wlp\"; %>");
        chain.Add("t70.wlp=end");
        var (diagnostics, _, _) = Render("<% include \"/t0.wlp\"; %>", Templates(string.Join("|", chain)));
        Assert.Equal("WL5004 at t62.wlp 1,12: Includes nest too deeply here: at most 64.", diagnostics);
    }

    [Theory]
    // A component renders where it is called, anew each time, given its parameters' values; its scope is its own.
    [InlineData("<% await renderComponent(\"/card.wlp\", { name: \"Ann\" }); %>", "card.wlp=<% param name; %>\n<b><%= name %></b>", "<b>Ann</b>")]
    [InlineData("<% await renderComponent(\"/c.wlp\"); await renderComponent(\"/c\"); %>", "c.wlp=x", "xx")]
    [InlineData("<% foreach (var n in [\"a\", \"b\"]) { await renderComponent(\"/item.wlp\", { n: n }); } %>",
        "item.wlp=<% param n; %><li><%= n %></li>", "<li>a</li><li>b</li>")]
    [InlineData("<% await renderComponent(\"/s.wlp\", {}); await renderComponent(\"/s.wlp\", { size: 2 }); %>",
        "s.wlp=<% param size = 10; %><%= size %>,", "10,2,")]
    [InlineData("<% renderComponent(\"/c.wlp\"); %>after", "c.wlp=x", "xafter")]
    [InlineData("<% var x = 1; await renderComponent(\"/c.wlp\", { x: 2 }); %><%= x %>", "c.wlp=<% param x; %><%= x %>", "21")]
    // A path starting with / is from the root, any other from the rendering file's folder; a component may include.
    [InlineData("<% await renderComponent(\"parts/a.wlp\"); %>", "parts/a.wlp=a<% await renderComponent(\"b.wlp\"); %>|parts/b.wlp=b", "ab")]
    [InlineData("<% await renderComponent(\"/c.wlp\"); %>", "c.wlp=[<% include \"/h.wlp\"; %>]|h.wlp=h", "[h]")]
    // A component may render itself.
    [InlineData("<% await renderComponent(\"/tree.wlp\", { n: 3 }); %>",
        "tree.wlp=<% param n; %><%= n %><% if (n > 0) { await renderComponent(\"tree.wlp\", { n: n - 1 }); } %>", "3210")]
    // A missing one is a runtime error, which a catch may catch.
    [InlineData("<% try { await renderComponent(\"/nope.wlp\"); } catch (e) { write(e.code); } %>", "", "WL5005")]
    public void AComponentRendersWhereItIsCalled(string page, string templates, string written)
    {
        Assert.Equal(("", written, ""), Render(page, Templates(templates)));
    }

    [Theory]
    [InlineData("<% await renderComponent(\"/nope.wlp\"); %>", "",
        "WL5005 at page.wlp 1,10: The component '/nope.wlp' cannot be rendered: there is no such file.")]
    [InlineData("<% await renderComponent(\"/card.wlp\"); %>", "card.wlp=<% param name; %><%= name %>",
        "WL5006 at page.wlp 1,10: 'card.wlp''s parameter 'name' is not given.")]
    [InlineData("<% await renderComponent(\"/card.wlp\", { name: 1 }); await renderComponent(\"/card.wlp\", { name: 2, size: 3 }); %>",
        "card.wlp=<% param name; %><%= name %>", "WL5006 at page.wlp 1,59: 'size' is not a parameter of 'card.wlp'.")]
    // A component does not see the variables where it is rendered.
    [InlineData("<% var secret = 1; await renderComponent(\"/c.wlp\"); %>", "c.wlp=<%= secret %>",
        "WL2001 at page.wlp 1,26: The component '/c.wlp' does not compile: 'secret' is not defined.")]
    // An error in a component, or in a template it includes, is reported where it is.
    [InlineData("<% await renderComponent(\"/c.wlp\"); %>", "c.wlp=x\n<%= [1][5] %>", "WL3005 at c.wlp 2,")]
    [InlineData("<% await renderComponent(\"/c.wlp\"); %>", "c.wlp=<% include \"/h.wlp\"; %>|h.wlp=\n<%= [1][5] %>", "WL3005 at h.wlp 2,")]
    public void AComponentThatFailsIsAnErrorWhereItIsRendered(string page, string templates, string error)
    {
        Assert.StartsWith(error, Render(page, Templates(templates)).Error);
    }

    [Theory]
    // A page's layout runs first, given the page's values, and the page's body runs where the layout calls renderBody().
    [InlineData("<% layout \"/main.wlp\", { title: \"Products\" }; %>\n<h1>Products</h1>\n",
        "main.wlp=<% param title; %>\n<html><title><%= title %></title>\n<% renderBody(); %>\n</html>\n",
        "<html><title>Products</title>\n<h1>Products</h1>\n</html>\n")]
    [InlineData("<% layout \"/l.wlp\"; %>body", "l.wlp=[<% renderBody(); %>]", "[body]")]
    [InlineData("<% layout \"l\"; %><% var n = 2; %><%= n * 2 %>", "l.wlp=[<% renderBody(); %>]", "[4]")]
    [InlineData("<% param who = \"x\"; %>\n<% layout \"/l.wlp\", { who: who, }; %>\nhi", "l.wlp=<% param who; %><%= who %>:<% renderBody(); %>", "x:hi")]
    [InlineData("<% layout \"/l.wlp\"; %>x", "l.wlp=<% param title = \"Home\"; %><%= title %>:<% renderBody(); %>", "Home:x")]
    [InlineData("<% layout \"/l.wlp\"; %>x", "l.wlp=<% renderBody(); renderBody(); %>", "xx")]
    // A layout may have a layout of its own.
    [InlineData("<% layout \"/l1.wlp\", { title: \"T\" }; %>body",
        "l1.wlp=<% param title; %><% layout \"l2.wlp\", { t: title }; %><main><% renderBody(); %></main>|l2.wlp=<% param t; %><html><%= t %><% renderBody(); %></html>",
        "<html>T<main>body</main></html>")]
    // The body streams where the layout has it, waiting included.
    [InlineData("<% layout \"/l.wlp\"; %>a<% await renderComponent(\"/c.wlp\"); %>b<% await delay(1); %>c", "l.wlp=[<% renderBody(); %>]|c.wlp=C",
        "[aCbc]")]
    public void ALayoutWrapsItsPage(string page, string templates, string written)
    {
        Assert.Equal(("", written, ""), Render(page, Templates(templates)));
    }

    [Theory]
    [InlineData("<% layout \"/nope.wlp\"; %>", "", "WL5002 at page.wlp 1,11: The layout '/nope.wlp' cannot be used: there is no such file.")]
    [InlineData("<% layout \"/a.wlp\"; %>", "a.wlp=<% layout \"b.wlp\"; %>A<% renderBody(); %>|b.wlp=<% layout \"a.wlp\"; %>B<% renderBody(); %>",
        "WL5003 at page.wlp 1,11: Using the layout '/a.wlp' makes a cycle: page.wlp uses a.wlp uses b.wlp uses a.wlp.")]
    [InlineData("<% layout \"/page.wlp\"; %>", "", "WL5003 at page.wlp 1,11: Using the layout '/page.wlp' makes a cycle: page.wlp uses page.wlp.")]
    [InlineData("<% layout \"/l.wlp\", { title: 1, x: 2 }; %>", "l.wlp=<% param title; %>", "WL5006 at page.wlp 1,33: 'x' is not a parameter of 'l.wlp'.")]
    [InlineData("<% layout \"/l.wlp\"; %>", "l.wlp=<% param title; %>", "WL5006 at page.wlp 1,11: 'l.wlp''s parameter 'title' is not given.")]
    [InlineData("<p>\n<% layout \"/l.wlp\"; %>", "l.wlp=", "WL5007 at page.wlp 2,4: A page's layout comes before anything else the page does or writes, after its parameters and imports.")]
    [InlineData("<% layout \"/l.wlp\"; layout \"/l.wlp\"; %>", "l.wlp=", "WL5007 at page.wlp 1,21: A page has at most one layout.")]
    [InlineData("<% include \"/h.wlp\"; %>", "h.wlp=<% layout \"/l.wlp\"; %>|l.wlp=", "WL5007 at h.wlp 1,4: A layout is a page's own: a template it includes cannot have one.")]
    [InlineData("<% layout \"/l.wlp\", x; %>", "l.wlp=", "WL1001 at page.wlp 1,21: Expected a layout's values, a map literal, found 'x'.")]
    [InlineData("<% layout \"/l.wlp\" %>", "l.wlp=", "WL1002 at page.wlp 1,19: A ';' is missing here.")]
    // renderBody() is a layout's; a layout does not see the page's variables.
    [InlineData("<% renderBody(); %>", "", "WL2001 at page.wlp 1,4: 'renderBody' is not defined.")]
    [InlineData("<% param secret = 1; %><% layout \"/l.wlp\"; %>", "l.wlp=<%= secret %>", "WL2001 at l.wlp 1,5: 'secret' is not defined.")]
    public void ALayoutThatFailsIsReportedWhereItIs(string page, string templates, string diagnostic)
    {
        Assert.StartsWith(diagnostic, Render(page, Templates(templates)).Diagnostics);
    }

    [Fact]
    public void ARuntimeErrorInALayoutIsReportedInIt()
    {
        var (_, written, error) = Render("<% layout \"/l.wlp\"; %>body", Templates("l.wlp=<% renderBody(); %>\n<%= [1][5] %>"));
        // The layout's first line holds only a statement block, and so writes no line break.
        Assert.Equal("body", written);
        Assert.StartsWith("WL3005 at l.wlp 2,", error);
    }

    [Fact]
    public void RenderComponentIsOnlyCalled()
    {
        Assert.Equal("WL3001 at page.wlp 1,12: 'renderComponent' renders a component, and can only be called.",
            Render("<% var r = renderComponent; %>").Diagnostics);
        Assert.StartsWith("WL2006 at page.wlp 1,4: 'renderComponent' takes 1 to 2 arguments, not 0.", Render("<% renderComponent(); %>").Diagnostics);
    }

    [Fact]
    public void APageParsedWithoutAResolverIncludesNothing()
    {
        var tree = WebLingoTemplateParser.Parse(new WebLingoSourceText("<% include \"/h.wlp\"; %>", "page.wlp"));
        Assert.Equal("The template '/h.wlp' cannot be included: this host finds no templates.", Assert.Single(tree.Diagnostics).Message);
    }

    /// <summary>
    /// Templates in memory, each <c>name=text</c>, separated by <c>|</c>: a path starting with <c>/</c> names one from the
    /// root, any other from the including file's folder, <c>.wlp</c> added to one without an extension.
    /// </summary>
    private sealed class MemoryTemplates(Dictionary<string, string> texts) : IWebLingoSourceResolver
    {
        public bool TryResolve(string importer, string path, out string name, out string source, out string? error)
        {
            var folder = importer.LastIndexOf('/') is var slash and >= 0 ? importer[..(slash + 1)] : "";
            name = path.StartsWith('/') ? path[1..] : folder + path;
            if (!name.EndsWith(".wlp", StringComparison.Ordinal)) name += ".wlp";
            error = texts.TryGetValue(name, out source!) ? null : "there is no such file";
            return error is null;
        }
    }

    private static MemoryTemplates Templates(string templates)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal) { ["page.wlp"] = "" };
        foreach (var entry in templates.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = entry.IndexOf('=');
            texts[entry[..equals]] = entry[(equals + 1)..];
        }
        return new MemoryTemplates(texts);
    }

    /// <summary>
    /// A page compiled and run on both VMs, which must give the same: its diagnostics (each as its code, its file, line
    /// and column, and its message), what it writes, and its runtime error, if any.
    /// </summary>
    private static (string Diagnostics, string Written, string Error) Render(string page, IWebLingoSourceResolver? templates = null)
    {
        var text = new WebLingoSourceText(page, "page.wlp");

        // Each VM with a compiler of its own, whose sources hold the page's files and those of the components it renders.
        var compiler = new WebLingoTemplateCompiler(Options, templates);
        var registerCompiled = compiler.Compile(text);
        var registerOutput = new WebLingoCollectedOutput();
        var registerResult = registerCompiled.Module is { } module
            ? new WebLingoVirtualMachine { Components = compiler.Components }.Execute(module, Context(registerOutput))
            : null;
        var register = (Describe(compiler.Sources, text, registerCompiled.Diagnostics), registerOutput.Text,
            Describe(compiler.Sources, text, registerResult?.Error));

        compiler = new WebLingoTemplateCompiler(Options, templates);
        var stackCompiled = compiler.CompileForStack(text);
        var stackOutput = new WebLingoCollectedOutput();
        var stackResult = stackCompiled.Module is { } stackModule
            ? new WebLingoStackVirtualMachine { Components = compiler.Components }.Execute(stackModule, Context(stackOutput))
            : null;
        var stack = (Describe(compiler.Sources, text, stackCompiled.Diagnostics), stackOutput.Text,
            Describe(compiler.Sources, text, stackResult?.Error));

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

    private static string Describe(WebLingoTemplateSources sources, WebLingoSourceText page, IReadOnlyList<WebLingoDiagnostic> diagnostics)
    {
        var lines = new List<string>();
        foreach (var diagnostic in diagnostics) lines.Add(Describe(sources, page, diagnostic));
        return string.Join(" | ", lines);
    }

    /// <summary>A diagnostic, a runtime error's located first, in the file it is in.</summary>
    private static string Describe(WebLingoTemplateSources sources, WebLingoSourceText page, WebLingoDiagnostic? diagnostic)
    {
        if (diagnostic is null) return "";
        diagnostic = sources.Locate(diagnostic);
        var text = diagnostic.Source is { } name ? sources.TextOf(name)! : page;
        var location = text.GetLocation(diagnostic.Span.Start);
        return $"{diagnostic.Code} at {text.Name} {location.Line},{location.Column}: {diagnostic.Message}";
    }
}
