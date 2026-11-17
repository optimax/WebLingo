namespace WebLingo.Core;

/// <summary>What an HTML attribute's value is (specification, section 13, "Context-aware escaping").</summary>
public enum WebLingoHtmlAttributeKind : byte
{
    /// <summary>Text, which only HTML reads.</summary>
    Plain,
    /// <summary>Text that changes what a page does, a form's <c>method</c> or a link's <c>rel</c>: plain as a value, refused as a name a value gives.</summary>
    Unsafe,
    /// <summary>A URL.</summary>
    Url,
    /// <summary>A script, an event's handler.</summary>
    Script,
    /// <summary>A style.</summary>
    Style,
    /// <summary>An image's sources.</summary>
    Srcset,
    /// <summary>HTML, <c>srcdoc</c>'s.</summary>
    Html
}

/// <summary>
/// The kinds of HTML's attributes, as Go's <c>html/template</c> reads them (its <c>attrType</c>): the attributes it lists,
/// an <c>on…</c> attribute a script, and an attribute whose name holds <c>src</c>, <c>uri</c> or <c>url</c> a URL, after a
/// <c>data-</c> prefix, or a namespace's, is taken off; <c>xmlns:…</c> a URL. Shared by the template compiler, which reads
/// a page's attributes, and the runtime, which checks a name a value gives.
/// </summary>
public static class WebLingoHtmlAttributes
{
    private static readonly Dictionary<string, WebLingoHtmlAttributeKind> Kinds = new(StringComparer.Ordinal)
    {
        ["accept-charset"] = WebLingoHtmlAttributeKind.Unsafe, ["action"] = WebLingoHtmlAttributeKind.Url,
        ["archive"] = WebLingoHtmlAttributeKind.Url, ["async"] = WebLingoHtmlAttributeKind.Unsafe,
        ["background"] = WebLingoHtmlAttributeKind.Url, ["challenge"] = WebLingoHtmlAttributeKind.Unsafe,
        ["charset"] = WebLingoHtmlAttributeKind.Unsafe, ["cite"] = WebLingoHtmlAttributeKind.Url,
        ["classid"] = WebLingoHtmlAttributeKind.Url, ["codebase"] = WebLingoHtmlAttributeKind.Url,
        ["content"] = WebLingoHtmlAttributeKind.Unsafe, ["crossorigin"] = WebLingoHtmlAttributeKind.Unsafe,
        ["data"] = WebLingoHtmlAttributeKind.Url, ["defer"] = WebLingoHtmlAttributeKind.Unsafe,
        ["enctype"] = WebLingoHtmlAttributeKind.Unsafe, ["form"] = WebLingoHtmlAttributeKind.Unsafe,
        ["formaction"] = WebLingoHtmlAttributeKind.Url, ["formenctype"] = WebLingoHtmlAttributeKind.Unsafe,
        ["formmethod"] = WebLingoHtmlAttributeKind.Unsafe, ["formnovalidate"] = WebLingoHtmlAttributeKind.Unsafe,
        ["href"] = WebLingoHtmlAttributeKind.Url, ["http-equiv"] = WebLingoHtmlAttributeKind.Unsafe,
        ["icon"] = WebLingoHtmlAttributeKind.Url, ["keytype"] = WebLingoHtmlAttributeKind.Unsafe,
        ["language"] = WebLingoHtmlAttributeKind.Unsafe, ["longdesc"] = WebLingoHtmlAttributeKind.Url,
        ["manifest"] = WebLingoHtmlAttributeKind.Url, ["method"] = WebLingoHtmlAttributeKind.Unsafe,
        ["novalidate"] = WebLingoHtmlAttributeKind.Unsafe, ["pattern"] = WebLingoHtmlAttributeKind.Unsafe,
        ["poster"] = WebLingoHtmlAttributeKind.Url, ["profile"] = WebLingoHtmlAttributeKind.Url,
        ["rel"] = WebLingoHtmlAttributeKind.Unsafe, ["sandbox"] = WebLingoHtmlAttributeKind.Unsafe,
        ["src"] = WebLingoHtmlAttributeKind.Url, ["srcdoc"] = WebLingoHtmlAttributeKind.Html,
        ["srcset"] = WebLingoHtmlAttributeKind.Srcset, ["style"] = WebLingoHtmlAttributeKind.Style,
        ["type"] = WebLingoHtmlAttributeKind.Unsafe, ["usemap"] = WebLingoHtmlAttributeKind.Url,
        ["value"] = WebLingoHtmlAttributeKind.Unsafe, ["xmlns"] = WebLingoHtmlAttributeKind.Url
    };

    /// <summary>The kind of the attribute named <paramref name="name"/>, which is in lower case.</summary>
    public static WebLingoHtmlAttributeKind KindOf(string name)
    {
        if (name.StartsWith("data-", StringComparison.Ordinal))
        {
            name = name[5..];
        }
        else if (name.IndexOf(':') is var colon and >= 0)
        {
            if (name[..colon] == "xmlns") return WebLingoHtmlAttributeKind.Url;
            name = name[(colon + 1)..]; // svg:href and xlink:href are href.
        }
        if (Kinds.TryGetValue(name, out var kind)) return kind;
        if (name.StartsWith("on", StringComparison.Ordinal)) return WebLingoHtmlAttributeKind.Script;
        return name.Contains("src", StringComparison.Ordinal) || name.Contains("uri", StringComparison.Ordinal)
            || name.Contains("url", StringComparison.Ordinal)
            ? WebLingoHtmlAttributeKind.Url
            : WebLingoHtmlAttributeKind.Plain;
    }
}
