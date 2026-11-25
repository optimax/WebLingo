using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
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
    // And one given while the page runs (section 3.7).
    [InlineData("<% class T {} T.setMethod(\"toString\", function (this) { return \"<T>\"; }); %><%= new T() %><%= [new T()] %>",
        "&lt;T&gt;[&lt;T&gt;]")]
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
        Assert.Contains($"{code} at page.wasp {line},{column}:", diagnostics);
    }

    [Fact]
    public void AnUnclosedMultiLineStringNamesItsBlock()
    {
        // It runs on over the page's text after its block, so its diagnostic names the block's <% too (section 3.5).
        var (diagnostics, _, _) = Render("<p>\n  <% var s = \"\"\"\n  a %>\n<p>");
        Assert.Contains("WL1011 at page.wasp 2,14: This multi-line string has no closing \"\"\". It began in the block at line 2, "
            + "column 3.", diagnostics);
    }

    [Theory]
    [InlineData("<% var who = \"Anna\"; %><%= $\"<b>{who}</b>\" %>", "&lt;b&gt;Anna&lt;/b&gt;")]
    [InlineData("<% var price = 1234.5; %><td><%= $\"{price,10:N2}\" %></td>", "<td>  1,234.50</td>")]
    [InlineData("<%- \"\"\"\n  <ul>\n    <li>%></li>\n  </ul>\n  \"\"\" %>", "<ul>\n  <li>%></li>\n</ul>")]
    public void APagesBlocksTakeMultiLineAndInterpolatedStrings(string page, string written)
    {
        Assert.Equal(("", written, ""), Render(page));
    }

    [Fact]
    public void ARuntimeErrorIsReportedAtItsPlaceInThePage()
    {
        var (_, written, error) = Render("<p>\n<%= [1][5] %>\n</p>");
        Assert.Equal("<p>\n", written);
        Assert.StartsWith("WL3005 at page.wasp 2,", error);
    }

    [Fact]
    public void TheParsedPageIsAProgramThatWritesIt()
    {
        var tree = WebLingoTemplateParser.Parse(new WebLingoSourceText("a<%= b %>c", "page.wasp"));
        Assert.False(tree.HasErrors);
        Assert.Equal(3, tree.Root.Statements.Count);
        Assert.Equal("page.wasp", tree.Source.Name);
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
    [InlineData("a<% include \"/h.wasp\"; %>c", "h.wasp=b", "abc")]
    [InlineData("<% var name = \"Anna\"; %><% include \"/greet.wasp\"; %>", "greet.wasp=Hello, <%= name %>!", "Hello, Anna!")]
    [InlineData("<% include \"/h.wasp\"; %><%= x * 3 %>", "h.wasp=<% var x = 2; %>", "6")]
    [InlineData("<% foreach (var i in [1, 2]) { include \"/item.wasp\"; } %>", "item.wasp=<li><%= i %></li>", "<li>1</li><li>2</li>")]
    [InlineData("<% include \"/h.wasp\"; include \"h\"; %>", "h.wasp=x", "xx")]
    // A path starting with / is from the application's root, any other from the including file's folder.
    [InlineData("<% include \"/inc/a.wasp\"; %>", "inc/a.wasp=a<% include \"b.wasp\"; %>|inc/b.wasp=b", "ab")]
    [InlineData("<% include \"inc/a\"; %>", "inc/a.wasp=a<% include \"/b.wasp\"; %>|b.wasp=root b", "aroot b")]
    // Each file's lines are its own: a line holding only an include writes nothing, the template its own lines.
    [InlineData("<html>\n<% include \"/head.wasp\"; %>\n</html>", "head.wasp=<% var t = 1; %>\n<head><%= t %></head>\n", "<html>\n<head>1</head>\n</html>")]
    // include is a name like any other where no string follows it.
    [InlineData("<% var include = 2; %><%= include %>", "", "2")]
    // An interpolation's format is the template's own, whether the page is longer than it or shorter.
    [InlineData("<p>0123456789 0123456789</p><% include \"/h.wasp\"; %>", "h.wasp=<%= $\"{12.5:0.00}\" %>", "<p>0123456789 0123456789</p>12.50")]
    [InlineData("<% include \"/h.wasp\"; %>", "h.wasp=<p>A template longer than the page that includes it.</p><%= $\"{12.5:0.00}\" %>",
        "<p>A template longer than the page that includes it.</p>12.50")]
    public void AnIncludeReadsTheTemplateInItsPlace(string page, string templates, string written)
    {
        Assert.Equal(("", written, ""), Render(page, Templates(templates)));
    }

    [Theory]
    [InlineData("<% include \"/nope.wasp\"; %>", "", "WL5002 at page.wasp 1,12: The template '/nope.wasp' cannot be included: there is no such file.")]
    [InlineData("<% include \"/h.wasp\" %>", "h.wasp=x", "WL1002 at page.wasp 1,21: A ';' is missing here.")]
    [InlineData("<% include \"/a.wasp\"; %>", "a.wasp=<% include \"b.wasp\"; %>|b.wasp=<% include \"a.wasp\"; %>",
        "WL5003 at b.wasp 1,12: Including 'a.wasp' makes a cycle: a.wasp includes b.wasp includes a.wasp.")]
    [InlineData("<% include \"/page.wasp\"; %>", "", "WL5003 at page.wasp 1,12: Including '/page.wasp' makes a cycle: page.wasp includes page.wasp.")]
    // An error in a template is reported in it, at its own line and column.
    [InlineData("<p>\n<% include \"/h.wasp\"; %>", "h.wasp=line\n<%= y %>", "WL2001 at h.wasp 2,5: 'y' is not defined.")]
    [InlineData("<p>\n<% include \"/h.wasp\"; %>", "h.wasp=line\n<%= $\"{y}\" %>", "WL2001 at h.wasp 2,8: 'y' is not defined.")]
    [InlineData("<p>\n<% include \"/h.wasp\"; %>", "h.wasp=line\n<%= $\"{$\"{y}\"}\" %>", "WL2001 at h.wasp 2,11: 'y' is not defined.")]
    [InlineData("<% include \"/h.wasp\"; %>", "h.wasp=\n<% var x = 1;", "WL5001 at h.wasp 2,1: This block has no closing '%>'.")]
    public void AnIncludeThatFailsIsReportedWhereItIs(string page, string templates, string diagnostic)
    {
        Assert.Equal(diagnostic, Render(page, Templates(templates)).Diagnostics);
    }

    [Fact]
    public void ARuntimeErrorInATemplateIsReportedInIt()
    {
        var (_, written, error) = Render("<p>\n<% include \"/h.wasp\"; %>", Templates("h.wasp=before\n<%= [1][5] %>"));
        Assert.Equal("<p>\nbefore\n", written);
        Assert.StartsWith("WL3005 at h.wasp 2,", error);
    }

    [Fact]
    public void ARuntimeErrorInABuiltinCalledInATemplateIsReportedInIt()
    {
        // Iterator's next() is written in WebLingo, its positions its own source's, which would name the page.
        var (_, written, error) = Render("<p>\n<% include \"/h.wasp\"; %>", Templates("h.wasp=before\n<% var it = [].iterate(); it.next(); %>"));
        Assert.Equal("<p>\nbefore\n", written);
        Assert.StartsWith("WL3005 at h.wasp 2,27:", error);
    }

    [Fact]
    public void IncludesNestAtMost64Deep()
    {
        var chain = new List<string>();
        for (var i = 0; i < 70; i++) chain.Add($"t{i}.wasp=<% include \"t{i + 1}.wasp\"; %>");
        chain.Add("t70.wasp=end");
        var (diagnostics, _, _) = Render("<% include \"/t0.wasp\"; %>", Templates(string.Join("|", chain)));
        Assert.Equal("WL5004 at t62.wasp 1,12: Includes nest too deeply here: at most 64.", diagnostics);
    }

    [Theory]
    // A component renders where it is called, anew each time, given its parameters' values; its scope is its own.
    [InlineData("<% await renderComponent(\"/card.wasp\", { name: \"Anna\" }); %>", "card.wasp=<% param name; %>\n<b><%= name %></b>", "<b>Anna</b>")]
    [InlineData("<% await renderComponent(\"/c.wasp\"); await renderComponent(\"/c\"); %>", "c.wasp=x", "xx")]
    [InlineData("<% foreach (var n in [\"a\", \"b\"]) { await renderComponent(\"/item.wasp\", { n: n }); } %>",
        "item.wasp=<% param n; %><li><%= n %></li>", "<li>a</li><li>b</li>")]
    [InlineData("<% await renderComponent(\"/s.wasp\", {}); await renderComponent(\"/s.wasp\", { size: 2 }); %>",
        "s.wasp=<% param size = 10; %><%= size %>,", "10,2,")]
    [InlineData("<% renderComponent(\"/c.wasp\"); %>after", "c.wasp=x", "xafter")]
    [InlineData("<% var x = 1; await renderComponent(\"/c.wasp\", { x: 2 }); %><%= x %>", "c.wasp=<% param x; %><%= x %>", "21")]
    // A path starting with / is from the root, any other from the rendering file's folder; a component may include.
    [InlineData("<% await renderComponent(\"parts/a.wasp\"); %>", "parts/a.wasp=a<% await renderComponent(\"b.wasp\"); %>|parts/b.wasp=b", "ab")]
    [InlineData("<% await renderComponent(\"/c.wasp\"); %>", "c.wasp=[<% include \"/h.wasp\"; %>]|h.wasp=h", "[h]")]
    // A component may render itself.
    [InlineData("<% await renderComponent(\"/tree.wasp\", { n: 3 }); %>",
        "tree.wasp=<% param n; %><%= n %><% if (n > 0) { await renderComponent(\"tree.wasp\", { n: n - 1 }); } %>", "3210")]
    // A missing one is a runtime error, which a catch may catch.
    [InlineData("<% try { await renderComponent(\"/nope.wasp\"); } catch (e) { write(e.code); } %>", "", "WL5005")]
    public void AComponentRendersWhereItIsCalled(string page, string templates, string written)
    {
        Assert.Equal(("", written, ""), Render(page, Templates(templates)));
    }

    [Theory]
    [InlineData("<% await renderComponent(\"/nope.wasp\"); %>", "",
        "WL5005 at page.wasp 1,10: The component '/nope.wasp' cannot be rendered: there is no such file.")]
    [InlineData("<% await renderComponent(\"/card.wasp\"); %>", "card.wasp=<% param name; %><%= name %>",
        "WL5006 at page.wasp 1,10: 'card.wasp''s parameter 'name' is not given.")]
    [InlineData("<% await renderComponent(\"/card.wasp\", { name: 1 }); await renderComponent(\"/card.wasp\", { name: 2, size: 3 }); %>",
        "card.wasp=<% param name; %><%= name %>", "WL5006 at page.wasp 1,60: 'size' is not a parameter of 'card.wasp'.")]
    // A component does not see the variables where it is rendered.
    [InlineData("<% var secret = 1; await renderComponent(\"/c.wasp\"); %>", "c.wasp=<%= secret %>",
        "WL2001 at page.wasp 1,26: The component '/c.wasp' does not compile: 'secret' is not defined.")]
    // An error in a component, or in a template it includes, is reported where it is.
    [InlineData("<% await renderComponent(\"/c.wasp\"); %>", "c.wasp=x\n<%= [1][5] %>", "WL3005 at c.wasp 2,")]
    [InlineData("<% await renderComponent(\"/c.wasp\"); %>", "c.wasp=<% include \"/h.wasp\"; %>|h.wasp=\n<%= [1][5] %>", "WL3005 at h.wasp 2,")]
    public void AComponentThatFailsIsAnErrorWhereItIsRendered(string page, string templates, string error)
    {
        Assert.StartsWith(error, Render(page, Templates(templates)).Error);
    }

    [Theory]
    // A page's layout runs first, given the page's values, and the page's body runs where the layout calls renderBody().
    [InlineData("<% layout \"/main.wasp\", { title: \"Products\" }; %>\n<h1>Products</h1>\n",
        "main.wasp=<% param title; %>\n<html><title><%= title %></title>\n<% renderBody(); %>\n</html>\n",
        "<html><title>Products</title>\n<h1>Products</h1>\n</html>\n")]
    [InlineData("<% layout \"/l.wasp\"; %>body", "l.wasp=[<% renderBody(); %>]", "[body]")]
    [InlineData("<% layout \"l\"; %><% var n = 2; %><%= n * 2 %>", "l.wasp=[<% renderBody(); %>]", "[4]")]
    [InlineData("<% param who = \"x\"; %>\n<% layout \"/l.wasp\", { who: who, }; %>\nhi", "l.wasp=<% param who; %><%= who %>:<% renderBody(); %>", "x:hi")]
    [InlineData("<% layout \"/l.wasp\"; %>x", "l.wasp=<% param title = \"Home\"; %><%= title %>:<% renderBody(); %>", "Home:x")]
    [InlineData("<% layout \"/l.wasp\"; %>x", "l.wasp=<% renderBody(); renderBody(); %>", "xx")]
    // The body, which the layout's import runs, has the methods of arrays that take a function (section 3.6) and spreading.
    [InlineData("<% layout \"/l.wasp\"; %><%= [1, 2, 3].find(x => x > 1) %>,<%= [...[1, 2], 3].map(x => x * 2) %>",
        "l.wasp=[<% renderBody(); %>]", "[2,[2, 4, 6]]")]
    // A layout may have a layout of its own.
    [InlineData("<% layout \"/l1.wasp\", { title: \"T\" }; %>body",
        "l1.wasp=<% param title; %><% layout \"l2.wasp\", { t: title }; %><main><% renderBody(); %></main>|l2.wasp=<% param t; %><html><%= t %><% renderBody(); %></html>",
        "<html>T<main>body</main></html>")]
    // The body streams where the layout has it, waiting included.
    [InlineData("<% import { delay } from \"std:system\"; layout \"/l.wasp\"; %>a<% await renderComponent(\"/c.wasp\"); %>b<% await delay(1); %>c", "l.wasp=[<% renderBody(); %>]|c.wasp=C",
        "[aCbc]")]
    public void ALayoutWrapsItsPage(string page, string templates, string written)
    {
        Assert.Equal(("", written, ""), Render(page, Templates(templates)));
    }

    [Theory]
    [InlineData("<% layout \"/nope.wasp\"; %>", "", "WL5002 at page.wasp 1,11: The layout '/nope.wasp' cannot be used: there is no such file.")]
    [InlineData("<% layout \"/a.wasp\"; %>", "a.wasp=<% layout \"b.wasp\"; %>A<% renderBody(); %>|b.wasp=<% layout \"a.wasp\"; %>B<% renderBody(); %>",
        "WL5003 at page.wasp 1,11: Using the layout '/a.wasp' makes a cycle: page.wasp uses a.wasp uses b.wasp uses a.wasp.")]
    [InlineData("<% layout \"/page.wasp\"; %>", "", "WL5003 at page.wasp 1,11: Using the layout '/page.wasp' makes a cycle: page.wasp uses page.wasp.")]
    [InlineData("<% layout \"/l.wasp\", { title: 1, x: 2 }; %>", "l.wasp=<% param title; %>", "WL5006 at page.wasp 1,34: 'x' is not a parameter of 'l.wasp'.")]
    [InlineData("<% layout \"/l.wasp\"; %>", "l.wasp=<% param title; %>", "WL5006 at page.wasp 1,11: 'l.wasp''s parameter 'title' is not given.")]
    [InlineData("<p>\n<% layout \"/l.wasp\"; %>", "l.wasp=", "WL5007 at page.wasp 2,4: A page's layout comes before anything else the page does or writes, after its parameters and imports.")]
    [InlineData("<% layout \"/l.wasp\"; layout \"/l.wasp\"; %>", "l.wasp=", "WL5007 at page.wasp 1,22: A page has at most one layout.")]
    [InlineData("<% include \"/h.wasp\"; %>", "h.wasp=<% layout \"/l.wasp\"; %>|l.wasp=", "WL5007 at h.wasp 1,4: A layout is a page's own: a template it includes cannot have one.")]
    [InlineData("<% layout \"/l.wasp\", x; %>", "l.wasp=", "WL1001 at page.wasp 1,22: Expected a layout's values, a map literal, found 'x'.")]
    [InlineData("<% layout \"/l.wasp\" %>", "l.wasp=", "WL1002 at page.wasp 1,20: A ';' is missing here.")]
    // renderBody() is a layout's; a layout does not see the page's variables.
    [InlineData("<% renderBody(); %>", "", "WL2001 at page.wasp 1,4: 'renderBody' is not defined.")]
    [InlineData("<% param secret = 1; %><% layout \"/l.wasp\"; %>", "l.wasp=<%= secret %>", "WL2001 at l.wasp 1,5: 'secret' is not defined.")]
    public void ALayoutThatFailsIsReportedWhereItIs(string page, string templates, string diagnostic)
    {
        Assert.StartsWith(diagnostic, Render(page, Templates(templates)).Diagnostics);
    }

    /// <summary>
    /// <c>session readonly;</c> (specification, section 6.12, "Sessions and cookies", A5): a statement of its own at the top
    /// level of a page's own text, which is nothing in its program, and which the template compiler gives its host.
    /// </summary>
    [Fact]
    public void APageDeclaresThatItOnlyReadsItsSession()
    {
        Assert.Equal(("", "a b", ""), Render("<% session readonly; %>a <% var session = \"b\"; %><%= session %>"));
        var compiler = new WebLingoTemplateCompiler(Options, Templates("l.wasp=[<% renderBody(); %>]"));
        compiler.Compile(new WebLingoSourceText("<% param x = 1;\nsession readonly; layout \"/l.wasp\"; %>x", "page.wasp"));
        Assert.Equal(new WebLingoSourceSpan(16, 16), compiler.ReadOnlySession);
        compiler = new WebLingoTemplateCompiler(Options);
        compiler.Compile(new WebLingoSourceText("<% var session = 1; %>", "page.wasp"));
        Assert.Null(compiler.ReadOnlySession);
    }

    [Theory]
    [InlineData("<% include \"/h.wasp\"; %>", "h.wasp=<% session readonly; %>",
        "WL5009 at h.wasp 1,4: A template a page includes cannot declare how the session is used: it reads it as its page declares.")]
    [InlineData("<% layout \"/l.wasp\"; %>x", "l.wasp=<% session readonly; %><% renderBody(); %>",
        "WL5009 at l.wasp 1,4: A layout cannot declare how the session is used: it reads it as its page declares.")]
    [InlineData("<% if (true) { session readonly; } %>", "",
        "WL5009 at page.wasp 1,16: 'session readonly;' is a statement of its own, at the page's top level, not inside another.")]
    [InlineData("<% var x = (1 + %><% session readonly; %><% 2); %>", "",
        "WL5009 at page.wasp 1,22: 'session readonly;' is a statement of its own, at the page's top level, not inside another.")]
    [InlineData("<% session readonly; session readonly; %>", "", "WL5009 at page.wasp 1,22: A page declares that it only reads its session once.")]
    [InlineData("<% session readonly %>", "", "WL1002 at page.wasp 1,20: A ';' is missing here.")]
    public void ASessionDeclaredWhereItCannotBeIsAnError(string page, string templates, string diagnostic)
    {
        Assert.StartsWith(diagnostic, Render(page, Templates(templates)).Diagnostics);
    }

    /// <summary>
    /// <c>crossorigin allowed;</c> (specification, section 6.12, "Cross-site request forgery"): a statement of its own at the
    /// top level of a page's own text, which is nothing in its program, and which the template compiler gives its host.
    /// </summary>
    [Fact]
    public void APageDeclaresThatItTakesRequestsOtherSitesSend()
    {
        Assert.Equal(("", "a 1", ""), Render("<% crossorigin allowed; %>a <% var crossorigin = 1; %><%= crossorigin %>"));
        var compiler = new WebLingoTemplateCompiler(Options, Templates("l.wasp=[<% renderBody(); %>]"));
        compiler.Compile(new WebLingoSourceText("<% param x = 1;\ncrossorigin allowed; session readonly; layout \"/l.wasp\"; %>x", "page.wasp"));
        Assert.Equal((new WebLingoSourceSpan(16, 19), new WebLingoSourceSpan(37, 16)), (compiler.CrossOriginAllowed, compiler.ReadOnlySession));
        compiler = new WebLingoTemplateCompiler(Options);
        compiler.Compile(new WebLingoSourceText("<% session readonly; %>", "page.wasp"));
        Assert.Null(compiler.CrossOriginAllowed);
    }

    [Theory]
    [InlineData("<% include \"/h.wasp\"; %>", "h.wasp=<% crossorigin allowed; %>",
        "WL5010 at h.wasp 1,4: A template a page includes cannot declare which requests it takes: it takes those its page takes.")]
    [InlineData("<% layout \"/l.wasp\"; %>x", "l.wasp=<% crossorigin allowed; %><% renderBody(); %>",
        "WL5010 at l.wasp 1,4: A layout cannot declare which requests it takes: it takes those its page takes.")]
    [InlineData("<% if (true) { crossorigin allowed; } %>", "",
        "WL5010 at page.wasp 1,16: 'crossorigin allowed;' is a statement of its own, at the page's top level, not inside another.")]
    [InlineData("<% crossorigin allowed; crossorigin allowed; %>", "",
        "WL5010 at page.wasp 1,25: A page declares once that it takes requests other sites send.")]
    [InlineData("<% crossorigin allowed %>", "", "WL1002 at page.wasp 1,23: A ';' is missing here.")]
    public void ACrossOriginDeclaredWhereItCannotBeIsAnError(string page, string templates, string diagnostic)
    {
        Assert.StartsWith(diagnostic, Render(page, Templates(templates)).Diagnostics);
    }

    // A component is compiled when it is rendered, its errors the page's runtime error.
    [Fact]
    public void AComponentCannotDeclareWhichRequestsItTakes()
    {
        var error = Render("<% await renderComponent(\"/c.wasp\"); %>", Templates("c.wasp=<% crossorigin allowed; %>")).Error;
        Assert.StartsWith("WL5010 at page.wasp 1,10: The component '/c.wasp'", error);
        Assert.Contains("A component cannot declare which requests it takes: it takes those its page takes.", error);
    }

    [Fact]
    public void AComponentCannotDeclareHowTheSessionIsUsed()
    {
        Assert.Contains("A component cannot declare how the session is used: it reads it as its page declares.",
            Render("<% await renderComponent(\"/c.wasp\"); %>", Templates("c.wasp=<% session readonly; %>c")).Error);
    }

    [Fact]
    public void ARuntimeErrorInALayoutIsReportedInIt()
    {
        var (_, written, error) = Render("<% layout \"/l.wasp\"; %>body", Templates("l.wasp=<% renderBody(); %>\n<%= [1][5] %>"));
        // The layout's first line holds only a statement block, and so writes no line break.
        Assert.Equal("body", written);
        Assert.StartsWith("WL3005 at l.wasp 2,", error);
    }

    [Fact]
    public void RenderComponentIsOnlyCalled()
    {
        Assert.Equal("WL3001 at page.wasp 1,12: 'renderComponent' renders a component, and can only be called.",
            Render("<% var r = renderComponent; %>").Diagnostics);
        Assert.StartsWith("WL2006 at page.wasp 1,4: 'renderComponent' takes 1 to 2 arguments, not 0.", Render("<% renderComponent(); %>").Diagnostics);
    }

    [Fact]
    public void APageParsedWithoutAResolverIncludesNothing()
    {
        var tree = WebLingoTemplateParser.Parse(new WebLingoSourceText("<% include \"/h.wasp\"; %>", "page.wasp"));
        Assert.Equal("The template '/h.wasp' cannot be included: this host finds no templates.", Assert.Single(tree.Diagnostics).Message);
    }

    /// <summary>
    /// Templates in memory, each <c>name=text</c>, separated by <c>|</c>: a path starting with <c>/</c> names one from the
    /// root, any other from the including file's folder, <c>.wasp</c> added to one without an extension.
    /// </summary>
    internal sealed class MemoryTemplates(Dictionary<string, string> texts) : IWebLingoSourceResolver
    {
        public bool TryResolve(string importer, string path, out string name, out string source, out string? error)
        {
            var folder = importer.LastIndexOf('/') is var slash and >= 0 ? importer[..(slash + 1)] : "";
            name = path.StartsWith('/') ? path[1..] : folder + path;
            if (!name.EndsWith(".wasp", StringComparison.Ordinal)) name += ".wasp";
            error = texts.TryGetValue(name, out source!) ? null : "there is no such file";
            return error is null;
        }
    }

    internal static MemoryTemplates Templates(string templates)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal) { ["page.wasp"] = "" };
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
    internal static (string Diagnostics, string Written, string Error) Render(string page, IWebLingoSourceResolver? templates = null)
    {
        var text = new WebLingoSourceText(page, "page.wasp");

        // Each VM with a compiler of its own, whose sources hold the page's files and those of the components it renders.
        var compiler = new WebLingoTemplateCompiler(Options, templates);
        var registerCompiled = compiler.Compile(text);
        var registerOutput = new WebLingoCollectedOutput();
        var registerResult = registerCompiled.Module is { } module
            ? new WebLingoVirtualMachine { Components = compiler.Components }.Execute(module, Context(registerOutput))
            : null;
        return (Describe(compiler.Sources, text, registerCompiled.Diagnostics), registerOutput.Text,
            Describe(compiler.Sources, text, registerResult?.Error));
    }

    private static readonly WebLingoCompilerOptions Options =
        new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1), new WebLingoBuiltinDeclaration("delay", 1)]);

    private static WebLingoExecutionContext Context(IWebLingoTextOutput output)
    {
        var services = new WebLingoBuiltinRegistry(new WebLingoDelayFunction());
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
