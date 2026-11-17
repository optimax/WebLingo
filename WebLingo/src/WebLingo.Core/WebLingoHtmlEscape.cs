namespace WebLingo.Core;

/// <summary>
/// How a page's <c>&lt;%= … %&gt;</c> escapes its value for where it stands in the page's HTML (specification, section 13,
/// "Context-aware escaping"), as Go's <c>html/template</c> escapes an action: the template compiler reads the HTML around
/// each one and calls the writer of its escaping (<see cref="WebLingoTemplateWriters.EscapedAs"/>), whose text the
/// runtime's escapers make (<c>WebLingoHtmlEscaping</c>). This is what the value's own language takes; what the attribute
/// it stands in takes is <see cref="WebLingoHtmlQuoting"/>.
/// </summary>
public enum WebLingoHtmlEscape : byte
{
    /// <summary>HTML's text, a <c>&lt;title&gt;</c>'s or a <c>&lt;textarea&gt;</c>'s, and a comment's: the five characters as entities.</summary>
    Text,
    /// <summary>An attribute's value that is only text: nothing of its own, its quoting escaping it.</summary>
    Plain,
    /// <summary>Where an attribute's name stands: a plain attribute's name, in lower case, else <c>zWebLingoz</c>.</summary>
    AttributeName,
    /// <summary>A URL's beginning: a scheme other than <c>http</c>, <c>https</c> and <c>mailto</c> refused, the URL then normalized.</summary>
    Url,
    /// <summary>A URL after its beginning, before its query: normalized, its reserved characters kept.</summary>
    UrlNormalized,
    /// <summary>A URL's query or fragment: every character but the unreserved percent-encoded.</summary>
    UrlPart,
    /// <summary>An image's <c>srcset</c>: each URL's scheme checked and normalized, each descriptor checked.</summary>
    Srcset,
    /// <summary>A script's value: JSON made safe in HTML, which the compiler has <c>Html.js</c> give the writer.</summary>
    Script,
    /// <summary>In a script's string: JavaScript's escapes, HTML's specials among them.</summary>
    ScriptString,
    /// <summary>In a script's template literal: as in a string, and <c>$</c>, <c>{</c> and <c>}</c>.</summary>
    ScriptTemplate,
    /// <summary>In a script's regular expression: as in a string, and the expression's specials.</summary>
    ScriptRegex,
    /// <summary>A style's value: a value that could change what the style means refused.</summary>
    Style,
    /// <summary>In a style's string, at its beginning: its scheme checked, as a URL's is, then CSS's escapes.</summary>
    StyleString,
    /// <summary>In a style's string, after its beginning: CSS's escapes.</summary>
    StyleStringPart,
    /// <summary>In a script's or a style's comment: nothing written.</summary>
    Omitted
}

/// <summary>How the attribute a value stands in is quoted, which escapes its text after its own language does.</summary>
public enum WebLingoHtmlQuoting : byte
{
    /// <summary>Not in an attribute: HTML's text, or an element's script or style.</summary>
    None,
    /// <summary>In quotes: the five characters as entities.</summary>
    Quoted,
    /// <summary>Unquoted: those, and whatever else ends an unquoted value, as entities; an empty value <c>zWebLingoz</c>.</summary>
    Unquoted
}
