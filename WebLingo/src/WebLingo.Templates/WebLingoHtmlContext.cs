using System.Text;
using WebLingo.Core;
namespace WebLingo.Templates;

/// <summary>Where a page's HTML stands, as Go's <c>html/template</c> names its states (section 13, "Context-aware escaping").</summary>
public enum WebLingoHtmlState : byte
{
    Text, Tag, AttributeName, AfterName, BeforeValue, Comment, RcData, Attribute, Url, Srcset,
    Script, ScriptDoubleString, ScriptSingleString, ScriptTemplate, ScriptRegex, ScriptBlockComment, ScriptLineComment,
    Style, StyleDoubleString, StyleSingleString, StyleDoubleUrl, StyleSingleUrl, StyleUrl, StyleBlockComment, StyleLineComment,
    Error
}

/// <summary>What ends the attribute's value a page's HTML stands in.</summary>
public enum WebLingoHtmlDelimiter : byte { None, DoubleQuote, SingleQuote, SpaceOrTagEnd }

/// <summary>Which part of a URL the HTML stands in: its beginning, before its query, its query or fragment, or no one knows.</summary>
public enum WebLingoUrlPart : byte { None, PreQuery, QueryOrFragment, Unknown }

/// <summary>What a <c>/</c> begins in a script: a regular expression, a division, or no one knows.</summary>
public enum WebLingoScriptSlash : byte { Regex, Division, Unknown }

/// <summary>The element whose content the HTML is in, if its content is not HTML's text.</summary>
public enum WebLingoHtmlElement : byte { None, Script, Style, Textarea, Title }

/// <summary>The kind of the attribute whose value the HTML is in, or is about to be.</summary>
public enum WebLingoHtmlAttribute : byte { None, Script, ScriptType, Style, Url, Srcset }

/// <summary>
/// Where a page's HTML stands at a point in its text (specification, section 13, "Context-aware escaping"), as Go's
/// <c>html/template</c> follows it: its state, the attribute's delimiter, the URL's part, what a script's <c>/</c> begins,
/// the element, the attribute, and in a script's template literal how deep its <c>${…}</c> substitutions' braces are, one
/// character a level; and an error's message, in <see cref="WebLingoHtmlState.Error"/>.
/// </summary>
public readonly record struct WebLingoHtmlContext(
    WebLingoHtmlState State, WebLingoHtmlDelimiter Delimiter = WebLingoHtmlDelimiter.None, WebLingoUrlPart UrlPart = WebLingoUrlPart.None,
    WebLingoScriptSlash Slash = WebLingoScriptSlash.Regex, WebLingoHtmlElement Element = WebLingoHtmlElement.None,
    WebLingoHtmlAttribute Attribute = WebLingoHtmlAttribute.None, string Braces = "", string? Error = null)
{
    public static WebLingoHtmlContext Text => new(WebLingoHtmlState.Text);

    public static WebLingoHtmlContext Failed(string message) => new(WebLingoHtmlState.Error, Error: message);

    public bool IsError => State == WebLingoHtmlState.Error;

    /// <summary>Where it stands, in words, for a diagnostic.</summary>
    public string Describe() => State switch
    {
        WebLingoHtmlState.Text => "HTML's text",
        WebLingoHtmlState.Tag or WebLingoHtmlState.AttributeName or WebLingoHtmlState.AfterName or WebLingoHtmlState.BeforeValue => "a tag",
        WebLingoHtmlState.Comment => "an HTML comment",
        WebLingoHtmlState.RcData => Element == WebLingoHtmlElement.Title ? "a title" : "a text area",
        WebLingoHtmlState.Attribute => "an attribute's value",
        WebLingoHtmlState.Url => UrlPart switch
        {
            WebLingoUrlPart.None => "a URL's beginning",
            WebLingoUrlPart.PreQuery => "a URL's path",
            WebLingoUrlPart.QueryOrFragment => "a URL's query",
            _ => "a URL"
        },
        WebLingoHtmlState.Srcset => "an image's sources",
        WebLingoHtmlState.Script => "a script",
        WebLingoHtmlState.ScriptDoubleString or WebLingoHtmlState.ScriptSingleString => "a script's string",
        WebLingoHtmlState.ScriptTemplate => "a script's template literal",
        WebLingoHtmlState.ScriptRegex => "a script's regular expression",
        WebLingoHtmlState.ScriptBlockComment or WebLingoHtmlState.ScriptLineComment => "a script's comment",
        WebLingoHtmlState.Style => "a style",
        WebLingoHtmlState.StyleDoubleString or WebLingoHtmlState.StyleSingleString => "a style's string",
        WebLingoHtmlState.StyleDoubleUrl or WebLingoHtmlState.StyleSingleUrl or WebLingoHtmlState.StyleUrl => "a style's URL",
        WebLingoHtmlState.StyleBlockComment or WebLingoHtmlState.StyleLineComment => "a style's comment",
        _ => "HTML that cannot be read"
    };

    /// <summary>
    /// Where a value stands, as Go's <c>nudge</c> moves it: in a tag, or after an attribute's name, a value is an
    /// attribute's name; after <c>=</c>, an unquoted value of its attribute's kind.
    /// </summary>
    public WebLingoHtmlContext Nudge() => State switch
    {
        WebLingoHtmlState.Tag => this with { State = WebLingoHtmlState.AttributeName },
        WebLingoHtmlState.BeforeValue => this with
        {
            State = AttributeStart(Attribute), Delimiter = WebLingoHtmlDelimiter.SpaceOrTagEnd, Attribute = WebLingoHtmlAttribute.None
        },
        WebLingoHtmlState.AfterName => this with { State = WebLingoHtmlState.AttributeName, Attribute = WebLingoHtmlAttribute.None },
        _ => this
    };

    internal static WebLingoHtmlState AttributeStart(WebLingoHtmlAttribute attribute) => attribute switch
    {
        WebLingoHtmlAttribute.Script => WebLingoHtmlState.Script,
        WebLingoHtmlAttribute.Style => WebLingoHtmlState.Style,
        WebLingoHtmlAttribute.Url => WebLingoHtmlState.Url,
        WebLingoHtmlAttribute.Srcset => WebLingoHtmlState.Srcset,
        _ => WebLingoHtmlState.Attribute
    };

    /// <summary>
    /// Where two ways through a page meet, as Go's <c>join</c> has it: where they stand alike; where they differ only in a
    /// URL's part or in what a script's <c>/</c> begins, that unknown; else an error.
    /// </summary>
    public static WebLingoHtmlContext Join(WebLingoHtmlContext a, WebLingoHtmlContext b, string what)
    {
        if (a.IsError) return a;
        if (b.IsError) return b;
        if (a == b) return a;
        if ((a with { UrlPart = b.UrlPart }) == b) return a with { UrlPart = WebLingoUrlPart.Unknown };
        if ((a with { Slash = b.Slash }) == b) return a with { Slash = WebLingoScriptSlash.Unknown };
        var (na, nb) = (a.Nudge(), b.Nudge());
        if (na != a || nb != b)
        {
            var joined = Join(na, nb, what);
            if (!joined.IsError) return joined;
        }
        return Failed($"{what} in different places in the page's HTML, {a.Describe()} and {b.Describe()}");
    }

    /// <summary>
    /// The escaping of a value that stands here, as Go's <c>escapeAction</c> chooses it, and where the HTML stands after
    /// it; an error where no escaping is safe, a URL whose part no one knows.
    /// </summary>
    public bool TryEscape(out WebLingoHtmlEscape escape, out WebLingoHtmlQuoting quoting, out WebLingoHtmlContext after)
    {
        var c = Nudge();
        quoting = c.Delimiter switch
        {
            WebLingoHtmlDelimiter.None => WebLingoHtmlQuoting.None,
            WebLingoHtmlDelimiter.SpaceOrTagEnd => WebLingoHtmlQuoting.Unquoted,
            _ => WebLingoHtmlQuoting.Quoted
        };
        after = c;
        escape = WebLingoHtmlEscape.Text;
        switch (c.State)
        {
            case WebLingoHtmlState.Url or WebLingoHtmlState.StyleDoubleUrl or WebLingoHtmlState.StyleSingleUrl or WebLingoHtmlState.StyleUrl
                or WebLingoHtmlState.StyleDoubleString or WebLingoHtmlState.StyleSingleString:
                var inString = c.State is WebLingoHtmlState.StyleDoubleString or WebLingoHtmlState.StyleSingleString;
                switch (c.UrlPart)
                {
                    case WebLingoUrlPart.None:
                        escape = inString ? WebLingoHtmlEscape.StyleString : WebLingoHtmlEscape.Url;
                        break;
                    case WebLingoUrlPart.PreQuery:
                        escape = inString ? WebLingoHtmlEscape.StyleStringPart : WebLingoHtmlEscape.UrlNormalized;
                        break;
                    case WebLingoUrlPart.QueryOrFragment:
                        escape = WebLingoHtmlEscape.UrlPart;
                        break;
                    default:
                        after = Failed($"a value stands where it is not known which part of a URL it is in, {c.Describe()}");
                        return false;
                }
                return true;
            case WebLingoHtmlState.Script:
                escape = WebLingoHtmlEscape.Script;
                after = c with { Slash = WebLingoScriptSlash.Division }; // A slash after a value divides it.
                return true;
            case WebLingoHtmlState.ScriptDoubleString or WebLingoHtmlState.ScriptSingleString:
                escape = WebLingoHtmlEscape.ScriptString;
                return true;
            case WebLingoHtmlState.ScriptTemplate:
                escape = WebLingoHtmlEscape.ScriptTemplate;
                return true;
            case WebLingoHtmlState.ScriptRegex:
                escape = WebLingoHtmlEscape.ScriptRegex;
                return true;
            case WebLingoHtmlState.Style:
                escape = WebLingoHtmlEscape.Style;
                return true;
            case WebLingoHtmlState.Text or WebLingoHtmlState.RcData or WebLingoHtmlState.Comment:
                escape = WebLingoHtmlEscape.Text;
                return true;
            case WebLingoHtmlState.Attribute:
                escape = WebLingoHtmlEscape.Plain;
                return true;
            case WebLingoHtmlState.AttributeName:
                escape = WebLingoHtmlEscape.AttributeName;
                quoting = WebLingoHtmlQuoting.None;
                return true;
            case WebLingoHtmlState.Srcset:
                escape = WebLingoHtmlEscape.Srcset;
                return true;
            case WebLingoHtmlState.ScriptBlockComment or WebLingoHtmlState.ScriptLineComment or WebLingoHtmlState.StyleBlockComment
                or WebLingoHtmlState.StyleLineComment:
                escape = WebLingoHtmlEscape.Omitted;
                return true;
            default:
                after = c;
                return false;
        }
    }

    /// <summary>Where the HTML stands after <paramref name="text"/>, read from here, as Go's <c>escapeText</c> reads a template's text.</summary>
    public WebLingoHtmlContext After(string text)
    {
        var c = this;
        var i = 0;
        while (i < text.Length && !c.IsError)
        {
            var (next, read) = WebLingoHtmlReader.AfterText(c, text, i);
            if (read == 0 && next == c) break; // Nothing more can be read; never the case, as Go's checks too.
            (c, i) = (next, i + read);
        }
        return c;
    }
}

/// <summary>
/// The transitions of HTML's contexts, a port of Go's <c>html/template</c> (its <c>transition.go</c> and
/// <c>contextAfterText</c>): each reads text from a position in one state and gives the state after it and how much it
/// read. Unlike Go's, it rewrites none of the page's text, and an element's end tag, <c>&lt;/script</c>, ends a script
/// wherever it stands, as browsers end one.
/// </summary>
internal static class WebLingoHtmlReader
{
    /// <summary>Reads from <paramref name="at"/> in <paramref name="c"/>: the context after, and how many characters were read.</summary>
    public static (WebLingoHtmlContext, int) AfterText(WebLingoHtmlContext c, string s, int at)
    {
        if (c.Delimiter == WebLingoHtmlDelimiter.None)
        {
            // The text up to an element's end tag, which ends its script, style, title or text area.
            var end = c.Element != WebLingoHtmlElement.None ? IndexOfEndTag(s, at, c.Element) : -1;
            if (end == at) return (WebLingoHtmlContext.Text, 0);
            var limit = end < 0 ? s.Length : end;
            return Transition(c, s[at..limit], 0);
        }

        // In an attribute's value: up to its delimiter, its entities decoded for the language it holds.
        var close = c.Delimiter switch
        {
            WebLingoHtmlDelimiter.DoubleQuote => s.IndexOf('"', at),
            WebLingoHtmlDelimiter.SingleQuote => s.IndexOf('\'', at),
            _ => s.IndexOfAny([' ', '\t', '\n', '\f', '\r', '>'], at)
        };
        var stop = close < 0 ? s.Length : close;
        if (c.Delimiter == WebLingoHtmlDelimiter.SpaceOrTagEnd)
        {
            var bad = s.IndexOfAny(['"', '\'', '<', '=', '`'], at, stop - at);
            if (bad >= 0)
            {
                return (WebLingoHtmlContext.Failed($"'{s[bad]}' stands in an unquoted attribute's value, which browsers read in different ways"),
                    s.Length - at);
            }
        }
        if (close < 0)
        {
            var value = DecodeEntities(s[at..]);
            for (var i = 0; i < value.Length && !c.IsError;)
            {
                var (next, read) = Transition(c, value, i);
                if (read == 0 && next == c) break;
                (c, i) = (next, i + read);
            }
            return (c, s.Length - at);
        }
        var element = c.Element;
        if (c.State == WebLingoHtmlState.Attribute && c.Element == WebLingoHtmlElement.Script && c.Attribute == WebLingoHtmlAttribute.ScriptType
            && !IsScriptType(s[at..close]))
        {
            element = WebLingoHtmlElement.None; // A script of another type, a template's, is not read as a script.
        }
        var consumed = close - at + (c.Delimiter != WebLingoHtmlDelimiter.SpaceOrTagEnd ? 1 : 0);
        return (new WebLingoHtmlContext(WebLingoHtmlState.Tag, Element: element), consumed);
    }

    /// <summary>One transition in <paramref name="c"/>'s state, reading <paramref name="s"/> from <paramref name="i"/>.</summary>
    private static (WebLingoHtmlContext, int) Transition(WebLingoHtmlContext c, string s, int i) => c.State switch
    {
        WebLingoHtmlState.Text => InText(c, s, i),
        WebLingoHtmlState.Tag => InTag(c, s, i),
        WebLingoHtmlState.AttributeName => InAttributeName(c, s, i),
        WebLingoHtmlState.AfterName => AfterName(c, s, i),
        WebLingoHtmlState.BeforeValue => BeforeValue(c, s, i),
        WebLingoHtmlState.Comment => InComment(c, s, i),
        WebLingoHtmlState.RcData or WebLingoHtmlState.Attribute => (c, s.Length - i),
        WebLingoHtmlState.Url or WebLingoHtmlState.Srcset => InUrl(c, s, i, s.Length),
        WebLingoHtmlState.Script => InScript(c, s, i),
        WebLingoHtmlState.ScriptDoubleString or WebLingoHtmlState.ScriptSingleString or WebLingoHtmlState.ScriptRegex => InScriptDelimited(c, s, i),
        WebLingoHtmlState.ScriptTemplate => InScriptTemplate(c, s, i),
        WebLingoHtmlState.ScriptBlockComment or WebLingoHtmlState.StyleBlockComment => InBlockComment(c, s, i),
        WebLingoHtmlState.ScriptLineComment or WebLingoHtmlState.StyleLineComment => InLineComment(c, s, i),
        WebLingoHtmlState.Style => InStyle(c, s, i),
        WebLingoHtmlState.StyleDoubleString or WebLingoHtmlState.StyleSingleString or WebLingoHtmlState.StyleDoubleUrl
            or WebLingoHtmlState.StyleSingleUrl or WebLingoHtmlState.StyleUrl => InStyleString(c, s, i),
        _ => (c, s.Length - i)
    };

    private static (WebLingoHtmlContext, int) InText(WebLingoHtmlContext c, string s, int start)
    {
        var k = start;
        while (true)
        {
            var i = s.IndexOf('<', k);
            if (i < 0 || i + 1 == s.Length) return (c, s.Length - start);
            if (string.CompareOrdinal(s, i, "<!--", 0, 4) == 0) return (new WebLingoHtmlContext(WebLingoHtmlState.Comment), i + 4 - start);
            i++;
            var end = false;
            if (s[i] == '/')
            {
                if (i + 1 == s.Length) return (c, s.Length - start);
                (end, i) = (true, i + 1);
            }
            var (j, element) = TagName(s, i);
            if (j != i) return (new WebLingoHtmlContext(WebLingoHtmlState.Tag, Element: end ? WebLingoHtmlElement.None : element), j - start);
            k = j;
        }
    }

    private static (WebLingoHtmlContext, int) InTag(WebLingoHtmlContext c, string s, int start)
    {
        var i = SkipSpace(s, start);
        if (i == s.Length) return (c, s.Length - start);
        if (s[i] == '>')
        {
            var state = c.Element switch
            {
                WebLingoHtmlElement.Script => WebLingoHtmlState.Script,
                WebLingoHtmlElement.Style => WebLingoHtmlState.Style,
                WebLingoHtmlElement.Textarea or WebLingoHtmlElement.Title => WebLingoHtmlState.RcData,
                _ => WebLingoHtmlState.Text
            };
            return (new WebLingoHtmlContext(state, Element: c.Element), i + 1 - start);
        }
        if (AttributeNameEnd(s, i) is not { } j) return (BadName(s, i), s.Length - start);
        if (i == j)
        {
            return (WebLingoHtmlContext.Failed($"a tag holds '{s[i]}' where a space, an attribute's name or the tag's end should be"),
                s.Length - start);
        }
        var name = s[i..j].ToLowerInvariant();
        var attribute = c.Element == WebLingoHtmlElement.Script && name == "type"
            ? WebLingoHtmlAttribute.ScriptType
            : WebLingoHtmlAttributes.KindOf(name) switch
            {
                WebLingoHtmlAttributeKind.Url => WebLingoHtmlAttribute.Url,
                WebLingoHtmlAttributeKind.Style => WebLingoHtmlAttribute.Style,
                WebLingoHtmlAttributeKind.Script => WebLingoHtmlAttribute.Script,
                WebLingoHtmlAttributeKind.Srcset => WebLingoHtmlAttribute.Srcset,
                _ => WebLingoHtmlAttribute.None
            };
        var after = j == s.Length ? WebLingoHtmlState.AttributeName : WebLingoHtmlState.AfterName;
        return (new WebLingoHtmlContext(after, Element: c.Element, Attribute: attribute), j - start);
    }

    private static WebLingoHtmlContext BadName(string s, int i)
    {
        var at = s.IndexOfAny(['\'', '"', '<'], i);
        return WebLingoHtmlContext.Failed($"'{s[at]}' stands in an attribute's name");
    }

    private static (WebLingoHtmlContext, int) InAttributeName(WebLingoHtmlContext c, string s, int start)
    {
        if (AttributeNameEnd(s, start) is not { } i) return (BadName(s, start), s.Length - start);
        return (i != s.Length ? c with { State = WebLingoHtmlState.AfterName } : c, i - start);
    }

    private static (WebLingoHtmlContext, int) AfterName(WebLingoHtmlContext c, string s, int start)
    {
        var i = SkipSpace(s, start);
        if (i == s.Length) return (c, s.Length - start);
        // A tag's end, or an attribute without a value.
        if (s[i] != '=') return (c with { State = WebLingoHtmlState.Tag }, i - start);
        return (c with { State = WebLingoHtmlState.BeforeValue }, i + 1 - start);
    }

    private static (WebLingoHtmlContext, int) BeforeValue(WebLingoHtmlContext c, string s, int start)
    {
        var i = SkipSpace(s, start);
        if (i == s.Length) return (c, s.Length - start);
        var delimiter = WebLingoHtmlDelimiter.SpaceOrTagEnd;
        if (s[i] == '\'') (delimiter, i) = (WebLingoHtmlDelimiter.SingleQuote, i + 1);
        else if (s[i] == '"') (delimiter, i) = (WebLingoHtmlDelimiter.DoubleQuote, i + 1);
        return (c with { State = WebLingoHtmlContext.AttributeStart(c.Attribute), Delimiter = delimiter }, i - start);
    }

    private static (WebLingoHtmlContext, int) InComment(WebLingoHtmlContext c, string s, int start)
    {
        var i = s.IndexOf("-->", start, StringComparison.Ordinal);
        return i < 0 ? (c, s.Length - start) : (WebLingoHtmlContext.Text, i + 3 - start);
    }

    private static (WebLingoHtmlContext, int) InUrl(WebLingoHtmlContext c, string s, int start, int end)
    {
        if (s.IndexOfAny(['#', '?'], start, end - start) >= 0) c = c with { UrlPart = WebLingoUrlPart.QueryOrFragment };
        else if (SkipSpace(s, start, end) != end && c.UrlPart == WebLingoUrlPart.None) c = c with { UrlPart = WebLingoUrlPart.PreQuery };
        return (c, end - start);
    }

    private static (WebLingoHtmlContext, int) InScript(WebLingoHtmlContext c, string s, int start)
    {
        var i = s.IndexOfAny(['"', '`', '\'', '/', '{', '}', '<', '-', '#'], start);
        if (i < 0) return (c with { Slash = NextSlash(s, start, s.Length, c.Slash) }, s.Length - start);
        c = c with { Slash = NextSlash(s, start, i, c.Slash) };
        switch (s[i])
        {
            case '"':
                c = c with { State = WebLingoHtmlState.ScriptDoubleString, Slash = WebLingoScriptSlash.Regex };
                break;
            case '\'':
                c = c with { State = WebLingoHtmlState.ScriptSingleString, Slash = WebLingoScriptSlash.Regex };
                break;
            case '`':
                c = c with { State = WebLingoHtmlState.ScriptTemplate, Slash = WebLingoScriptSlash.Regex };
                break;
            case '/':
                if (i + 1 < s.Length && s[i + 1] == '/') (c, i) = (c with { State = WebLingoHtmlState.ScriptLineComment }, i + 1);
                else if (i + 1 < s.Length && s[i + 1] == '*') (c, i) = (c with { State = WebLingoHtmlState.ScriptBlockComment }, i + 1);
                else if (c.Slash == WebLingoScriptSlash.Regex) c = c with { State = WebLingoHtmlState.ScriptRegex };
                else if (c.Slash == WebLingoScriptSlash.Division) c = c with { Slash = WebLingoScriptSlash.Regex };
                else return (WebLingoHtmlContext.Failed("a script's '/' could begin a division or a regular expression"), s.Length - start);
                break;
            case '<':
                // JavaScript reads a line after <!-- or --> as a comment's.
                if (string.CompareOrdinal(s, i, "<!--", 0, 4) == 0) (c, i) = (c with { State = WebLingoHtmlState.ScriptLineComment }, i + 3);
                break;
            case '-':
                if (string.CompareOrdinal(s, i, "-->", 0, 3) == 0) (c, i) = (c with { State = WebLingoHtmlState.ScriptLineComment }, i + 2);
                break;
            case '#':
                if (i + 1 < s.Length && s[i + 1] == '!') (c, i) = (c with { State = WebLingoHtmlState.ScriptLineComment }, i + 1);
                break;
            case '{':
                if (c.Braces.Length != 0) c = c with { Braces = c.Braces[..^1] + (char)(c.Braces[^1] + 1) };
                break;
            case '}':
                if (c.Braces.Length == 0) break;
                if (c.Braces[^1] > 0)
                {
                    c = c with { Braces = c.Braces[..^1] + (char)(c.Braces[^1] - 1) };
                    break;
                }
                c = c with { Braces = c.Braces[..^1], State = WebLingoHtmlState.ScriptTemplate }; // The substitution's end.
                break;
        }
        return (c, i + 1 - start);
    }

    private static (WebLingoHtmlContext, int) InScriptTemplate(WebLingoHtmlContext c, string s, int start)
    {
        for (var i = start; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '\\':
                    if (++i == s.Length) return (WebLingoHtmlContext.Failed("a script's template literal ends in an unfinished escape"), s.Length - start);
                    break;
                case '$' when i + 1 < s.Length && s[i + 1] == '{':
                    return (c with { State = WebLingoHtmlState.Script, Braces = c.Braces + (char)0 }, i + 2 - start);
                case '`':
                    return (c with { State = WebLingoHtmlState.Script }, i + 1 - start);
            }
        }
        return (c, s.Length - start);
    }

    private static (WebLingoHtmlContext, int) InScriptDelimited(WebLingoHtmlContext c, string s, int start)
    {
        var quote = c.State switch { WebLingoHtmlState.ScriptDoubleString => '"', WebLingoHtmlState.ScriptSingleString => '\'', _ => '/' };
        var inCharset = false;
        for (var i = start; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '\\')
            {
                if (++i == s.Length) return (WebLingoHtmlContext.Failed("a script's string ends in an unfinished escape"), s.Length - start);
                continue;
            }
            if (quote == '/' && ch == '[') inCharset = true;
            else if (quote == '/' && ch == ']') inCharset = false;
            else if (ch == quote && !inCharset) return (c with { State = WebLingoHtmlState.Script, Slash = WebLingoScriptSlash.Division }, i + 1 - start);
        }
        if (inCharset) return (WebLingoHtmlContext.Failed("a script's regular expression ends in an unfinished set, '['"), s.Length - start);
        return (c, s.Length - start);
    }

    private static (WebLingoHtmlContext, int) InBlockComment(WebLingoHtmlContext c, string s, int start)
    {
        var i = s.IndexOf("*/", start, StringComparison.Ordinal);
        if (i < 0) return (c, s.Length - start);
        var state = c.State == WebLingoHtmlState.ScriptBlockComment ? WebLingoHtmlState.Script : WebLingoHtmlState.Style;
        return (c with { State = state }, i + 2 - start);
    }

    private static (WebLingoHtmlContext, int) InLineComment(WebLingoHtmlContext c, string s, int start)
    {
        var script = c.State == WebLingoHtmlState.ScriptLineComment;
        var i = script ? s.IndexOfAny(['\n', '\r', '\u2028', '\u2029'], start) : s.IndexOfAny(['\n', '\f', '\r'], start);
        if (i < 0) return (c, s.Length - start);
        return (c with { State = script ? WebLingoHtmlState.Script : WebLingoHtmlState.Style }, i - start);
    }

    private static (WebLingoHtmlContext, int) InStyle(WebLingoHtmlContext c, string s, int start)
    {
        for (var i = start; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '(':
                    var before = i;
                    while (before > start && s[before - 1] is '\t' or '\n' or '\f' or '\r' or ' ') before--;
                    if (before - start >= 3 && string.Compare(s, before - 3, "url", 0, 3, StringComparison.OrdinalIgnoreCase) == 0
                        && (before - 3 == start || !IsStyleNameChar(s[before - 4])))
                    {
                        var j = SkipSpace(s, i + 1);
                        if (j < s.Length && s[j] == '"') return (c with { State = WebLingoHtmlState.StyleDoubleUrl, UrlPart = WebLingoUrlPart.None }, j + 1 - start);
                        if (j < s.Length && s[j] == '\'') return (c with { State = WebLingoHtmlState.StyleSingleUrl, UrlPart = WebLingoUrlPart.None }, j + 1 - start);
                        return (c with { State = WebLingoHtmlState.StyleUrl, UrlPart = WebLingoUrlPart.None }, j - start);
                    }
                    break;
                case '/' when i + 1 < s.Length && s[i + 1] == '/':
                    return (c with { State = WebLingoHtmlState.StyleLineComment }, i + 2 - start);
                case '/' when i + 1 < s.Length && s[i + 1] == '*':
                    return (c with { State = WebLingoHtmlState.StyleBlockComment }, i + 2 - start);
                case '"':
                    return (c with { State = WebLingoHtmlState.StyleDoubleString, UrlPart = WebLingoUrlPart.None }, i + 1 - start);
                case '\'':
                    return (c with { State = WebLingoHtmlState.StyleSingleString, UrlPart = WebLingoUrlPart.None }, i + 1 - start);
            }
        }
        return (c, s.Length - start);
    }

    private static (WebLingoHtmlContext, int) InStyleString(WebLingoHtmlContext c, string s, int start)
    {
        char[] ends = c.State switch
        {
            WebLingoHtmlState.StyleDoubleString or WebLingoHtmlState.StyleDoubleUrl => ['\\', '"'],
            WebLingoHtmlState.StyleSingleString or WebLingoHtmlState.StyleSingleUrl => ['\\', '\''],
            _ => ['\\', '\t', '\n', '\f', '\r', ' ', ')']
        };
        var k = start;
        while (true)
        {
            var i = s.IndexOfAny(ends, k);
            if (i < 0)
            {
                var decoded = DecodeStyle(s[k..]);
                var (inUrl, _) = InUrl(c, decoded, 0, decoded.Length);
                return (inUrl, s.Length - start);
            }
            if (s[i] == '\\')
            {
                if (++i == s.Length) return (WebLingoHtmlContext.Failed("a style's string ends in an unfinished escape"), s.Length - start);
            }
            else
            {
                return (c with { State = WebLingoHtmlState.Style, UrlPart = WebLingoUrlPart.None }, i + 1 - start);
            }
            var part = DecodeStyle(s[k..(i + 1)]);
            (c, _) = InUrl(c, part, 0, part.Length);
            k = i + 1;
        }
    }

    private static bool IsStyleNameChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or >= (char)0x80;

    /// <summary>CSS's escapes decoded, enough to see a URL's <c>?</c> and <c>#</c> in a style's string.</summary>
    private static string DecodeStyle(string s)
    {
        if (!s.Contains('\\')) return s;
        var decoded = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 == s.Length)
            {
                decoded.Append(s[i]);
                continue;
            }
            var j = i + 1;
            var value = 0;
            while (j < s.Length && j < i + 7 && IsHex(s[j])) value = value * 16 + HexValue(s[j++]);
            if (j == i + 1)
            {
                decoded.Append(s[++i]);
                continue;
            }
            if (j < s.Length && s[j] is ' ' or '\t' or '\n' or '\f' or '\r') j++;
            decoded.Append(value is > 0 and <= 0xFFFF and not (>= 0xD800 and <= 0xDFFF) ? (char)value : '\uFFFD');
            i = j - 1;
        }
        return decoded.ToString();
    }

    /// <summary>
    /// What a script's <c>/</c> begins after <paramref name="s"/> from <paramref name="start"/> to <paramref name="end"/>,
    /// as Go's <c>nextJSCtx</c> guesses it: a regular expression after an operator, an open bracket or a keyword that
    /// comes before one, a division after a value.
    /// </summary>
    private static WebLingoScriptSlash NextSlash(string s, int start, int end, WebLingoScriptSlash preceding)
    {
        while (end > start && s[end - 1] is '\t' or '\n' or '\f' or '\r' or ' ' or '\u2028' or '\u2029') end--;
        if (end == start) return preceding;
        var c = s[end - 1];
        switch (c)
        {
            case '+' or '-':
                var first = end - 1;
                while (first > start && s[first - 1] == c) first--;
                // ++ and -- end a value; + and -, and ---, an operator's operand.
                return (end - first) % 2 == 1 ? WebLingoScriptSlash.Regex : WebLingoScriptSlash.Division;
            case '.':
                return end - start > 1 && s[end - 2] is >= '0' and <= '9' ? WebLingoScriptSlash.Division : WebLingoScriptSlash.Regex;
            case ',' or '<' or '>' or '=' or '*' or '%' or '&' or '|' or '^' or '?' or '!' or '~' or '(' or '[' or ':' or ';' or '{' or '}':
                return WebLingoScriptSlash.Regex;
        }
        var j = end;
        while (j > start && IsScriptNameChar(s[j - 1])) j--;
        return s[j..end] is "break" or "case" or "continue" or "delete" or "do" or "else" or "finally" or "in" or "instanceof" or "return"
            or "throw" or "try" or "typeof" or "void"
            ? WebLingoScriptSlash.Regex
            : WebLingoScriptSlash.Division;
    }

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private static bool IsScriptNameChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '$';

    /// <summary>Where an attribute's name ends from <paramref name="i"/>; null where a quote or a <c>&lt;</c> stands in it.</summary>
    private static int? AttributeNameEnd(string s, int i)
    {
        for (var j = i; j < s.Length; j++)
        {
            switch (s[j])
            {
                case ' ' or '\t' or '\n' or '\f' or '\r' or '=' or '>':
                    return j;
                case '\'' or '"' or '<':
                    return null;
            }
        }
        return s.Length;
    }

    /// <summary>The end of the tag's name from <paramref name="i"/>, and the element it names, if its content is not HTML's text.</summary>
    private static (int, WebLingoHtmlElement) TagName(string s, int i)
    {
        if (i == s.Length || !char.IsAsciiLetter(s[i])) return (i, WebLingoHtmlElement.None);
        var j = i + 1;
        while (j < s.Length)
        {
            if (char.IsAsciiLetterOrDigit(s[j]))
            {
                j++;
                continue;
            }
            // x-y and x:y, but not x-, -y or x--y.
            if (s[j] is ':' or '-' && j + 1 < s.Length && char.IsAsciiLetterOrDigit(s[j + 1]))
            {
                j += 2;
                continue;
            }
            break;
        }
        var element = s[i..j].ToLowerInvariant() switch
        {
            "script" => WebLingoHtmlElement.Script,
            "style" => WebLingoHtmlElement.Style,
            "textarea" => WebLingoHtmlElement.Textarea,
            "title" => WebLingoHtmlElement.Title,
            _ => WebLingoHtmlElement.None
        };
        return (j, element);
    }

    /// <summary>Where the element's end tag, <c>&lt;/script</c> and a space, a <c>/</c> or a <c>&gt;</c>, begins from <paramref name="at"/>; -1 if nowhere.</summary>
    private static int IndexOfEndTag(string s, int at, WebLingoHtmlElement element)
    {
        var tag = element switch
        {
            WebLingoHtmlElement.Script => "script",
            WebLingoHtmlElement.Style => "style",
            WebLingoHtmlElement.Textarea => "textarea",
            _ => "title"
        };
        for (var i = s.IndexOf("</", at, StringComparison.Ordinal); i >= 0; i = s.IndexOf("</", i + 2, StringComparison.Ordinal))
        {
            var after = i + 2 + tag.Length;
            if (after <= s.Length && string.Compare(s, i + 2, tag, 0, tag.Length, StringComparison.OrdinalIgnoreCase) == 0
                && after < s.Length && s[after] is '>' or ' ' or '\t' or '\n' or '\f' or '/')
            {
                return i;
            }
        }
        return -1;
    }

    private static int SkipSpace(string s, int i, int end = -1)
    {
        if (end < 0) end = s.Length;
        while (i < end && s[i] is ' ' or '\t' or '\n' or '\f' or '\r') i++;
        return i;
    }

    /// <summary>Whether a script's <c>type</c> is JavaScript's, or JSON's, which a script is read as.</summary>
    private static bool IsScriptType(string type)
    {
        var semicolon = type.IndexOf(';');
        if (semicolon >= 0) type = type[..semicolon];
        return type.Trim().ToLowerInvariant() is "" or "application/ecmascript" or "application/javascript" or "application/json"
            or "application/ld+json" or "application/x-ecmascript" or "application/x-javascript" or "module" or "text/ecmascript"
            or "text/javascript" or "text/javascript1.0" or "text/javascript1.1" or "text/javascript1.2" or "text/javascript1.3"
            or "text/javascript1.4" or "text/javascript1.5" or "text/jscript" or "text/livescript" or "text/x-ecmascript"
            or "text/x-javascript";
    }

    /// <summary>
    /// An attribute's value with its character references decoded, as a browser decodes them before its script, style or
    /// URL is read: numeric ones, and the named ones of ASCII's punctuation, with or without their <c>;</c> for the few
    /// HTML allows so; any other named one as it stands.
    /// </summary>
    internal static string DecodeEntities(string s)
    {
        if (!s.Contains('&')) return s;
        var decoded = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '&')
            {
                decoded.Append(s[i]);
                continue;
            }
            if (i + 1 < s.Length && s[i + 1] == '#')
            {
                var hex = i + 2 < s.Length && s[i + 2] is 'x' or 'X';
                var j = i + (hex ? 3 : 2);
                var value = 0L;
                var digits = j;
                while (j < s.Length && (hex ? IsHex(s[j]) : char.IsAsciiDigit(s[j])))
                {
                    if (value <= 0x10FFFF) value = value * (hex ? 16 : 10) + (hex ? HexValue(s[j]) : s[j] - '0');
                    j++;
                }
                if (j == digits)
                {
                    decoded.Append('&');
                    continue;
                }
                if (j < s.Length && s[j] == ';') j++;
                decoded.Append(value is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? char.ConvertFromUtf32((int)value) : "\uFFFD");
                i = j - 1;
                continue;
            }
            var end = i + 1;
            while (end < s.Length && char.IsAsciiLetterOrDigit(s[end])) end++;
            var name = s[(i + 1)..end];
            var semicolon = end < s.Length && s[end] == ';';
            if (Entities.TryGetValue(name, out var character) && (semicolon || (name is "amp" or "lt" or "gt" or "quot" or "AMP" or "LT" or "GT" or "QUOT"
                    && !(end < s.Length && s[end] == '='))))
            {
                decoded.Append(character);
                i = semicolon ? end : end - 1;
                continue;
            }
            decoded.Append('&');
        }
        return decoded.ToString();
    }

    private static readonly Dictionary<string, char> Entities = new(StringComparer.Ordinal)
    {
        ["Tab"] = '\t', ["NewLine"] = '\n', ["excl"] = '!', ["quot"] = '"', ["QUOT"] = '"', ["num"] = '#', ["dollar"] = '$',
        ["percnt"] = '%', ["amp"] = '&', ["AMP"] = '&', ["apos"] = '\'', ["lpar"] = '(', ["rpar"] = ')', ["ast"] = '*',
        ["midast"] = '*', ["plus"] = '+', ["comma"] = ',', ["period"] = '.', ["sol"] = '/', ["colon"] = ':', ["semi"] = ';',
        ["lt"] = '<', ["LT"] = '<', ["equals"] = '=', ["gt"] = '>', ["GT"] = '>', ["quest"] = '?', ["commat"] = '@',
        ["lsqb"] = '[', ["lbrack"] = '[', ["bsol"] = '\\', ["rsqb"] = ']', ["rbrack"] = ']', ["Hat"] = '^', ["lowbar"] = '_',
        ["UnderBar"] = '_', ["grave"] = '`', ["DiacriticalGrave"] = '`', ["lcub"] = '{', ["lbrace"] = '{', ["verbar"] = '|',
        ["vert"] = '|', ["VerticalLine"] = '|', ["rcub"] = '}', ["rbrace"] = '}'
    };
}
