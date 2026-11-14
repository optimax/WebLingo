using System.Text;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.StdLib;

/// <summary>
/// The built-ins a template's page calls (specification, section 13, "Contracts"; <see cref="WebLingoTemplateWriters"/>),
/// writing where <c>write</c> writes: <c>&lt;%= value %&gt;</c> the text <c>write</c> would write, an instance's
/// <c>toString()</c> included, escaped (<see cref="Escape"/>); <c>&lt;%- value %&gt;</c>, and the page's own text, that
/// text as it stands. Each writes nothing for <c>null</c>, so that an optional read that finds nothing is blank.
/// </summary>
public sealed class WebLingoTemplateWriteFunction(bool escapes, IWebLingoTextOutput output) : IWebLingoTextBuiltin
{
    public string Name => escapes ? WebLingoTemplateWriters.Escaped : WebLingoTemplateWriters.Raw;
    public int Arity => 1;

    public bool TryInvoke(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        if (arguments[0].Kind != WebLingoValueKind.Null)
        {
            var text = WebLingoValueFormatter.Format(arguments[0], heap);
            output.Write(escapes ? Escape(text) : text);
        }
        result = WebLingoValue.Null;
        error = null;
        return true;
    }

    /// <summary>
    /// Text as HTML's text and attribute values both take it: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and
    /// <c>'</c> as <c>&amp;amp;</c>, <c>&amp;lt;</c>, <c>&amp;gt;</c>, <c>&amp;quot;</c> and <c>&amp;#39;</c>.
    /// </summary>
    public static string Escape(string text)
    {
        StringBuilder? escaped = null;
        var from = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var entity = text[i] switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", '\'' => "&#39;", _ => null };
            if (entity is null) continue;
            escaped ??= new StringBuilder(text.Length + 16);
            escaped.Append(text, from, i - from).Append(entity);
            from = i + 1;
        }
        return escaped is null ? text : escaped.Append(text, from, text.Length - from).ToString();
    }
}

/// <summary>The built-ins a host that runs pages registers besides the standard library's, each writing to the one output.</summary>
public static class WebLingoTemplateBuiltins
{
    public static IWebLingoBuiltinFunction[] Create(IWebLingoTextOutput output) =>
        [new WebLingoTemplateWriteFunction(true, output), new WebLingoTemplateWriteFunction(false, output)];
}
