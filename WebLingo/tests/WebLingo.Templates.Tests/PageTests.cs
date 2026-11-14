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
        var compiler = new WebLingoTemplateCompiler(Options, templates);

        var registerCompiled = compiler.Compile(text, out var files);
        var registerOutput = new WebLingoCollectedOutput();
        var registerResult = registerCompiled.Module is { } module
            ? new WebLingoVirtualMachine().Execute(module, Context(registerOutput))
            : null;
        var register = (Describe(files, registerCompiled.Diagnostics), registerOutput.Text, Describe(files, registerResult?.Error));

        var stackCompiled = compiler.CompileForStack(text, out files);
        var stackOutput = new WebLingoCollectedOutput();
        var stackResult = stackCompiled.Module is { } stackModule
            ? new WebLingoStackVirtualMachine().Execute(stackModule, Context(stackOutput))
            : null;
        var stack = (Describe(files, stackCompiled.Diagnostics), stackOutput.Text, Describe(files, stackResult?.Error));

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

    private static string Describe(WebLingoTemplateFiles files, IReadOnlyList<WebLingoDiagnostic> diagnostics)
    {
        var lines = new List<string>();
        foreach (var diagnostic in diagnostics) lines.Add(Describe(files, diagnostic));
        return string.Join(" | ", lines);
    }

    /// <summary>A diagnostic, a runtime error's located first, in the file it is in.</summary>
    private static string Describe(WebLingoTemplateFiles files, WebLingoDiagnostic? diagnostic)
    {
        if (diagnostic is null) return "";
        diagnostic = files.Locate(diagnostic);
        var text = diagnostic.Source is { } name ? files.TextOf(name)! : files.Page;
        var location = text.GetLocation(diagnostic.Span.Start);
        return $"{diagnostic.Code} at {text.Name} {location.Line},{location.Column}: {diagnostic.Message}";
    }
}
