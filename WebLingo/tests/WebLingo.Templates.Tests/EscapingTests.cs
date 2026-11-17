using WebLingo.Core;
using WebLingo.Runtime;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// Context-aware escaping (specification, section 13, "Context-aware escaping"; section 9, M0.15): a page's
/// <c>&lt;%= … %&gt;</c> escaped for where it stands in the page's HTML, as Go's <c>html/template</c> escapes an action, each
/// page run on both VMs; the HTML the escaping cannot follow, WL5008; and the escapers themselves.
/// </summary>
public sealed class EscapingTests
{
    private const string X = "<% var x = \"a'b\\\"c<d>&e f=g\"; %>";

    [Theory]
    // HTML's text, a title's, a text area's and a comment's, and a quoted attribute's plain value: the five as entities.
    [InlineData(X + "<p><%= x %></p>", "<p>a&#39;b&quot;c&lt;d&gt;&amp;e f=g</p>")]
    [InlineData(X + "<p title=\"<%= x %>\" lang='<%= x %>'>", "<p title=\"a&#39;b&quot;c&lt;d&gt;&amp;e f=g\" lang='a&#39;b&quot;c&lt;d&gt;&amp;e f=g'>")]
    [InlineData(X + "<title><%= x %></title><textarea><%= x %></textarea>",
        "<title>a&#39;b&quot;c&lt;d&gt;&amp;e f=g</title><textarea>a&#39;b&quot;c&lt;d&gt;&amp;e f=g</textarea>")]
    [InlineData(X + "<!-- <%= x %> -->", "<!-- a&#39;b&quot;c&lt;d&gt;&amp;e f=g -->")]
    // An unquoted attribute's value: spaces and = too; an empty one zWebLingoz, which would leave the value to what follows.
    [InlineData(X + "<p title=<%= x %>>", "<p title=a&#39;b&quot;c&lt;d&gt;&amp;e&#32;f&#61;g>")]
    [InlineData("<p title=<%= \"\" %> lang=en>", "<p title=zWebLingoz lang=en>")]
    // Where an attribute's name stands: a plain attribute's, in lower case; an event's, a URL's or a style's refused.
    [InlineData("<input <%= \"CHECKED\" %> <%= \"data-x\" %>=1>", "<input checked data-x=1>")]
    [InlineData("<input <%= \"onclick\" %>=\"f()\" <%= \"href\" %>=x <%= \"a b\" %>>", "<input zWebLingoz=\"f()\" zWebLingoz=x zWebLingoz>")]
    // A URL: a scheme that could run a script refused at its beginning; normalized in its path; encoded in its query.
    [InlineData("<a href=\"<%= \"javascript:alert(1)\" %>\">", "<a href=\"#zWebLingoz\">")]
    [InlineData("<a href=\"<%= \" JavaScript:alert(1)\" %>\"><a href=\"<%= \"data:text/html,x\" %>\">",
        "<a href=\"#zWebLingoz\"><a href=\"#zWebLingoz\">")]
    [InlineData("<a href=\"<%= \"HTTPS://ex.com/a b?c=d\" %>\">", "<a href=\"HTTPS://ex.com/a%20b?c=d\">")]
    [InlineData("<a href=\"<%= \"mailto:a@b.c\" %>\"><a href=\"<%= \"/x:y\" %>\">", "<a href=\"mailto:a@b.c\"><a href=\"/x:y\">")]
    [InlineData(X + "<a href=\"/p/<%= x %>?q=<%= x %>#<%= x %>\">",
        "<a href=\"/p/a%27b%22c%3Cd%3E&amp;e%20f=g?q=a%27b%22c%3Cd%3E%26e%20f%3Dg#a%27b%22c%3Cd%3E%26e%20f%3Dg\">")]
    [InlineData("<a href=\"/x?a=1&amp;b=<%= \"&\" %>\"><img src=<%= \"/a b\" %>>", "<a href=\"/x?a=1&amp;b=%26\"><img src=/a%20b>")]
    [InlineData("<a href=\"<%= \"/caf\u00e9/\U0001F600%41%zz\" %>\">", "<a href=\"/caf%C3%A9/%F0%9F%98%80%41%25zz\">")]
    // Attributes that hold URLs by their names, and a data- attribute's.
    [InlineData("<form action=\"<%= \"javascript:x\" %>\"><div data-href=\"<%= \"javascript:x\" %>\" fooUrl=\"<%= \"javascript:x\" %>\">",
        "<form action=\"#zWebLingoz\"><div data-href=\"#zWebLingoz\" fooUrl=\"#zWebLingoz\">")]
    [InlineData("<img srcset=\"<%= \"a.png 1x, javascript:x 2x, b.png 1x;x\" %>\">", "<img srcset=\"a.png 1x,#zWebLingoz,#zWebLingoz\">")]
    // A script's value: its JSON, safe in HTML, a number or a keyword between spaces; null as null.
    [InlineData("<script>var v = <%= { a: [1, 2.5, true, null], b: \"</script>\" } %>; var n = <%= 5 %>; var z = <%= null %>;</script>",
        "<script>var v = {\"a\":[1,2.5,true,null],\"b\":\"\\u003c/script\\u003e\"}; var n =  5 ; var z =  null ;</script>")]
    [InlineData(X + "<button onclick=\"show(<%= x %>, <%= [1] %>)\">",
        "<button onclick=\"show(&quot;a&#39;b\\&quot;c\\u003cd\\u003e\\u0026e f=g&quot;, [1])\">")]
    // A script's string, template literal and regular expression; its comments get nothing.
    [InlineData(X + "<script>var s = \"<%= x %>\", t = '<%= x %>';</script>",
        "<script>var s = \"a\\u0027b\\u0022c\\u003cd\\u003e\\u0026e f=g\", t = 'a\\u0027b\\u0022c\\u003cd\\u003e\\u0026e f=g';</script>")]
    [InlineData("<script>var t = `a${<%= 1 %>}<%= \"${x}`\" %>`; var r = /<%= \"a.b\" %>/g, e = <%= \"\" %>;</script>",
        "<script>var t = `a${ 1 }\\u0024\\u007bx\\u007d\\u0060`; var r = /a\\.b/g, e = \"\";</script>")]
    [InlineData("<script>var r = /<%= \"\" %>/; // <%= \"x\" %>\n/* <%= \"y\" %> */</script>", "<script>var r = /(?:)/; // \n/*  */</script>")]
    [InlineData(X + "<a onclick=\"f(&quot;<%= x %>&quot;)\">", "<a onclick=\"f(&quot;a\\u0027b\\u0022c\\u003cd\\u003e\\u0026e f=g&quot;)\">")]
    // A style's value refused where it could change what the style means; its string and its URL escaped.
    [InlineData("<style>p { color: <%= \"red\" %>; width: <%= \"expression(alert(1))\" %>; x: <%= \"a;b\" %> }</style>",
        "<style>p { color: red; width: zWebLingoz; x: zWebLingoz }</style>")]
    [InlineData(X + "<p style=\"font-family: '<%= x %>'; background: url(<%= \"javascript:x\" %>)\">",
        "<p style=\"font-family: 'a\\27 b\\22 c\\3c d\\3e\\26 e f=g'; background: url(#zWebLingoz)\">")]
    [InlineData("<style>a { background: url('/i/<%= \"a b(c)\" %>') }</style>", "<style>a { background: url('/i/a%20b%28c%29') }</style>")]
    // A script of another type, a template's, is HTML's text.
    [InlineData(X + "<script type=\"text/template\"><b><%= x %></b></script>",
        "<script type=\"text/template\"><b>a&#39;b&quot;c&lt;d&gt;&amp;e f=g</b></script>")]
    // Code: an if's branches and its else's end where each other's do; a loop's body where it began.
    [InlineData("<% var c = false; %><% if (c) { %><a href=\"/a<% } else if (!c) { %><a href=\"/b<% } else { %><a href=\"/c<% } %>/<%= \"x y\" %>\">",
        "<a href=\"/b/x%20y\">")]
    [InlineData("<% foreach (var i in [\"a b\", \"c\"]) { %><a href=\"/<%= i %>\"><%= i %></a><% } %>", "<a href=\"/a%20b\">a b</a><a href=\"/c\">c</a>")]
    [InlineData("<% if (true) %><b>x</b><% else %>y", "<b>x</b>")]
    // <%- %> leaves the HTML where it was.
    [InlineData("<a<%- \" class=\\\"c\\\"\" %> href=\"<%= \"javascript:x\" %>\">", "<a class=\"c\" href=\"#zWebLingoz\">")]
    public void AValueIsEscapedForWhereItStands(string page, string written)
    {
        Assert.Equal(("", written, ""), PageTests.Render(page));
    }

    [Theory]
    // An include is textual: the HTML goes on into it, and back.
    [InlineData("<a href=\"<% include \"/h.wasp\"; %>\">", "h.wasp=<%= \"javascript:x\" %>", "<a href=\"#zWebLingoz\">")]
    // A component writes HTML's text, from its start.
    [InlineData("<p><% await renderComponent(\"/c.wasp\", {}); %></p>", "c.wasp=<a href=\"<%= \"javascript:x\" %>\">", "<p><a href=\"#zWebLingoz\"></p>")]
    public void IncludesAndComponentsAreEscapedForWhereTheyStand(string page, string templates, string written)
    {
        Assert.Equal(("", written, ""), PageTests.Render(page, PageTests.Templates(templates)));
    }

    [Theory]
    [InlineData("<% var c = true; %><% if (c) { %><a href=\"/x<% } %>?q=<%= 1 %>\">", 1, 52,
        "An 'if' without an 'else' and its branches end in different places in the page's HTML, HTML's text and a URL's path.")]
    [InlineData("<% var c = true; %><% if (c) { %><script><% } else { %><b><% } %>", 1, 62,
        "The branches of an 'if' end in different places in the page's HTML, a script and HTML's text.")]
    [InlineData("<% foreach (var i in [1]) { %><a href=\"<% } %>\">", 1, 43,
        "A block of code begins and ends in different places in the page's HTML, HTML's text and a URL's beginning.")]
    [InlineData("<% if (true) %><a href=\"", 1, 16,
        "This text, a statement's body without braces, moves the page's HTML from HTML's text to a URL's beginning; put the statement's body in braces.")]
    [InlineData("<a title=x\"y>", 1, 10,
        "The page's HTML cannot be read here: '\"' stands in an unquoted attribute's value, which browsers read in different ways.")]
    [InlineData("<a b\"c=d>", 1, 3, "The page's HTML cannot be read here: '\"' stands in an attribute's name.")]
    [InlineData("<p>\n<script>var x = 1;", 2, 19,
        "The page ends in a script, not in HTML's text: every tag, comment, script and style it begins must end in it.")]
    [InlineData("<a href=\"x\"", 1, 12, "The page ends in a tag, not in HTML's text: every tag, comment, script and style it begins must end in it.")]
    [InlineData("<% var c = true; %><% if (c) { %><a href=\"/x<% } else { %><a href=\"/y?<% } %><%= 1 %>\">", 1, 78,
        "A value cannot be escaped here: a value stands where it is not known which part of a URL it is in, a URL.")]
    [InlineData("<% var c = true; %><script><% if (c) { %>x = 1<% } else { %>y = (<% } %> / 2</script>", 1, 73,
        "The page's HTML cannot be read here: a script's '/' could begin a division or a regular expression.")]
    [InlineData("<a href=\"<% await renderComponent(\"/c.wasp\", {}); %>\">", 1, 19,
        "A component is rendered here, in a URL's beginning; it writes HTML's text, where it must stand.")]
    public void HtmlTheEscapingCannotFollowIsAnError(string page, int line, int column, string message)
    {
        var (diagnostics, written, _) = PageTests.Render(page);
        Assert.Equal($"WL5008 at page.wasp {line},{column}: {message}", diagnostics);
        Assert.Equal("", written);
    }

    [Fact]
    public void ALayoutsBodyIsWrittenInHtmlsText()
    {
        var (diagnostics, _, _) = PageTests.Render("<% layout \"/l.wasp\"; %>x", PageTests.Templates("l.wasp=<a title=\"<% renderBody(); %>\">"));
        Assert.Equal("WL5008 at l.wasp 1,14: A layout's body is written here, in an attribute's value; it writes HTML's text, where it must stand.",
            diagnostics);
    }

    [Theory]
    [InlineData("https://a.b/c", "https://a.b/c")]
    [InlineData("HTTP://a", "HTTP://a")]
    [InlineData("/a:b", "/a:b")]
    [InlineData("?a:b", "?a:b")]
    [InlineData("#a:b", "#a:b")]
    [InlineData("a:b", "#zWebLingoz")]
    [InlineData("java\nscript:x", "#zWebLingoz")]
    [InlineData("vbscript:x", "#zWebLingoz")]
    public void AUrlsSchemeIsChecked(string url, string filtered)
    {
        Assert.Equal(filtered, WebLingoHtmlEscaping.FilterUrl(url));
    }

    [Theory]
    [InlineData(WebLingoHtmlEscape.Text, WebLingoHtmlQuoting.None, "&<>\"'", "&amp;&lt;&gt;&quot;&#39;")]
    [InlineData(WebLingoHtmlEscape.Plain, WebLingoHtmlQuoting.Unquoted, "a b\t=`", "a&#32;b&#9;&#61;&#96;")]
    [InlineData(WebLingoHtmlEscape.Plain, WebLingoHtmlQuoting.Unquoted, "", "zWebLingoz")]
    [InlineData(WebLingoHtmlEscape.UrlNormalized, WebLingoHtmlQuoting.None, "a'b(c)%2F%g", "a%27b%28c%29%2F%25g")]
    [InlineData(WebLingoHtmlEscape.UrlPart, WebLingoHtmlQuoting.None, "a/b?c=d&e", "a%2Fb%3Fc%3Dd%26e")]
    [InlineData(WebLingoHtmlEscape.Style, WebLingoHtmlQuoting.None, "#ff0", "#ff0")]
    [InlineData(WebLingoHtmlEscape.Style, WebLingoHtmlQuoting.None, "\\72 ed", "red")]
    [InlineData(WebLingoHtmlEscape.Style, WebLingoHtmlQuoting.None, "e\\78pression(x)", "zWebLingoz")]
    [InlineData(WebLingoHtmlEscape.Style, WebLingoHtmlQuoting.None, "-moz-binding", "zWebLingoz")]
    [InlineData(WebLingoHtmlEscape.Style, WebLingoHtmlQuoting.None, "a--b", "zWebLingoz")]
    [InlineData(WebLingoHtmlEscape.StyleStringPart, WebLingoHtmlQuoting.None, "a\"b\\c1", "a\\22 b\\\\c1")]
    [InlineData(WebLingoHtmlEscape.ScriptString, WebLingoHtmlQuoting.None, "\u2028\n\u0001/", "\\u2028\\n\\u0001\\/")]
    [InlineData(WebLingoHtmlEscape.Omitted, WebLingoHtmlQuoting.Unquoted, "x", "")]
    public void EachEscapeEscapesForItsPlace(WebLingoHtmlEscape escape, WebLingoHtmlQuoting quoting, string text, string escaped)
    {
        Assert.Equal(escaped, WebLingoHtmlEscaping.Escape(text, escape, quoting));
    }

    [Fact]
    public void EachEscapingHasAWriterOfItsOwn()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (escape, quoting) in WebLingoTemplateWriters.Escapings)
        {
            var name = WebLingoTemplateWriters.EscapedAs(escape, quoting);
            Assert.True(names.Add(name), name);
            Assert.True(WebLingoTemplateWriters.TryEscapedAs(name, out var found, out var foundQuoting));
            Assert.Equal((escape, quoting), (found, foundQuoting));
        }
        Assert.Equal(WebLingoTemplateWriters.Escaped, WebLingoTemplateWriters.EscapedAs(WebLingoHtmlEscape.Plain, WebLingoHtmlQuoting.Quoted));
        Assert.Equal("<%=url,quoted>", WebLingoTemplateWriters.EscapedAs(WebLingoHtmlEscape.Url, WebLingoHtmlQuoting.Quoted));
        Assert.False(WebLingoTemplateWriters.TryEscapedAs("<%=nothing>", out _, out _));
    }
}
