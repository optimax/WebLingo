namespace WebLingo.Core;

/// <summary>
/// The built-ins a template's page is compiled to call (specification, section 13, "Contracts"): <see cref="Escaped"/>
/// writes a value's text escaped, for <c>&lt;%= … %&gt;</c>, and <see cref="Raw"/> as it stands, for <c>&lt;%- … %&gt;</c>
/// and the page's own text; each writes nothing for <c>null</c>. Neither name is one a program can write, so that no
/// program calls, hides or replaces them. The template compiler (<c>WebLingo.Templates</c>) declares them, and a host
/// that runs pages registers them (<c>WebLingo.StdLib</c>). Each gives whether the page's output holds more than its client
/// has taken; after another write where the page can <c>await</c>, <c>write</c>'s or <c>writeLn</c>'s, it calls
/// <see cref="Full"/>, which says so; and, if so, it awaits the task <see cref="Drained"/> gives, which completes once the
/// client has taken enough (section 6.12, "Streaming").
/// </summary>
public static class WebLingoTemplateWriters
{
    public const string Escaped = "<%=>";
    public const string Raw = "<%->";
    public const string Full = "<%full>";
    public const string Drained = "<%drained>";
}
