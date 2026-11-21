namespace WebLingo.Core;

/// <summary>
/// The built-ins a template's page is compiled to call (specification, section 13, "Contracts"): <see cref="Escaped"/>
/// writes a value's text escaped, for <c>&lt;%= … %&gt;</c> in HTML's text and in a quoted attribute's plain value, and
/// <see cref="Raw"/> as it stands, for <c>&lt;%- … %&gt;</c> and the page's own text; each writes nothing for
/// <c>null</c>. Where <c>&lt;%= … %&gt;</c> stands elsewhere in the page's HTML, the template compiler calls the writer
/// that escapes for there (<see cref="EscapedAs"/>; section 13, "Context-aware escaping"). No name of theirs is one a
/// program can write, so that no program calls, hides or replaces them. The template compiler declares them, and a host
/// that runs pages registers them (<c>WebLingo.StdLib</c>). Each gives whether the page's output holds more than its
/// client has taken; after another write where the page can <c>await</c>, <c>write</c>'s or <c>writeLn</c>'s, it calls
/// <see cref="Full"/>, which says so; and, if so, it awaits the task <see cref="Drained"/> gives, which completes once the
/// client has taken enough (section 6.12, "Streaming").
/// </summary>
public static class WebLingoTemplateWriters
{
    public const string Escaped = "<%=>";
    public const string Raw = "<%->";
    public const string Full = "<%full>";
    public const string Drained = "<%drained>";

    /// <summary>
    /// A page's own <c>write</c> and <c>writeLn</c>, which its templates' code calls without an import, writing where its
    /// text goes, as <c>std:console</c>'s write a script's console (the owner's answer, 20 November 2026, "Console Module",
    /// A2); names a program writes, which only a template's code sees.
    /// </summary>
    public const string Write = "write";

    /// <inheritdoc cref="Write"/>
    public const string WriteLine = "writeLn";

    private const string EscapedStart = "<%=";

    /// <summary>
    /// The escaping each writer of <c>&lt;%= … %&gt;</c> gives, <see cref="Escaped"/>'s first: one for each escaping a page's
    /// HTML can ask for, a value's language's and its attribute's quoting.
    /// </summary>
    public static IReadOnlyList<(WebLingoHtmlEscape Escape, WebLingoHtmlQuoting Quoting)> Escapings { get; } = MakeEscapings();

    private static List<(WebLingoHtmlEscape, WebLingoHtmlQuoting)> MakeEscapings()
    {
        var escapings = new List<(WebLingoHtmlEscape, WebLingoHtmlQuoting)>
        {
            (WebLingoHtmlEscape.Text, WebLingoHtmlQuoting.None), (WebLingoHtmlEscape.Plain, WebLingoHtmlQuoting.Unquoted),
            (WebLingoHtmlEscape.AttributeName, WebLingoHtmlQuoting.None), (WebLingoHtmlEscape.Omitted, WebLingoHtmlQuoting.None),
            (WebLingoHtmlEscape.Srcset, WebLingoHtmlQuoting.Quoted), (WebLingoHtmlEscape.Srcset, WebLingoHtmlQuoting.Unquoted)
        };
        foreach (var escape in (WebLingoHtmlEscape[])[
                     WebLingoHtmlEscape.Url, WebLingoHtmlEscape.UrlNormalized, WebLingoHtmlEscape.UrlPart, WebLingoHtmlEscape.Script,
                     WebLingoHtmlEscape.ScriptString, WebLingoHtmlEscape.ScriptTemplate, WebLingoHtmlEscape.ScriptRegex,
                     WebLingoHtmlEscape.Style, WebLingoHtmlEscape.StyleString, WebLingoHtmlEscape.StyleStringPart])
        {
            foreach (var quoting in (WebLingoHtmlQuoting[])[WebLingoHtmlQuoting.None, WebLingoHtmlQuoting.Quoted, WebLingoHtmlQuoting.Unquoted])
            {
                escapings.Add((escape, quoting));
            }
        }
        return escapings;
    }

    /// <summary>
    /// The writer of <c>&lt;%= … %&gt;</c> that escapes so: <see cref="Escaped"/> for HTML's text and a quoted attribute's
    /// plain value, which escape alike; any other <c>&lt;%=escape&gt;</c>, and <c>,quoted</c> or <c>,unquoted</c> after
    /// the escape in an attribute, such as <c>&lt;%=url,quoted&gt;</c>.
    /// </summary>
    public static string EscapedAs(WebLingoHtmlEscape escape, WebLingoHtmlQuoting quoting)
    {
        if (escape == WebLingoHtmlEscape.Text || (escape == WebLingoHtmlEscape.Plain && quoting == WebLingoHtmlQuoting.Quoted)) return Escaped;
        if (escape is WebLingoHtmlEscape.AttributeName or WebLingoHtmlEscape.Omitted) quoting = WebLingoHtmlQuoting.None;
        var name = escape.ToString();
        var suffix = quoting switch { WebLingoHtmlQuoting.Quoted => ",quoted", WebLingoHtmlQuoting.Unquoted => ",unquoted", _ => "" };
        return EscapedStart + char.ToLowerInvariant(name[0]) + name[1..] + suffix + ">";
    }

    /// <summary>Whether a built-in's name is one of the writers of <c>&lt;%= … %&gt;</c>, and how it escapes.</summary>
    public static bool TryEscapedAs(string name, out WebLingoHtmlEscape escape, out WebLingoHtmlQuoting quoting)
    {
        if (name.StartsWith(EscapedStart, StringComparison.Ordinal) && ByName.TryGetValue(name, out var found))
        {
            (escape, quoting) = found;
            return true;
        }
        (escape, quoting) = (WebLingoHtmlEscape.Text, WebLingoHtmlQuoting.None);
        return false;
    }

    private static readonly Dictionary<string, (WebLingoHtmlEscape, WebLingoHtmlQuoting)> ByName = MakeNames();

    private static Dictionary<string, (WebLingoHtmlEscape, WebLingoHtmlQuoting)> MakeNames()
    {
        var names = new Dictionary<string, (WebLingoHtmlEscape, WebLingoHtmlQuoting)>(StringComparer.Ordinal);
        foreach (var escaping in MakeEscapings()) names.Add(EscapedAs(escaping.Item1, escaping.Item2), escaping);
        return names;
    }

    /// <summary>Whether a built-in's name is a template's writer of a value, raw or escaped.</summary>
    public static bool IsWriter(string name) => name == Raw || TryEscapedAs(name, out _, out _);
}
