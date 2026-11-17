using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.StdLib;

/// <summary>
/// The built-ins a template's page calls (specification, section 13, "Contracts"; <see cref="WebLingoTemplateWriters"/>),
/// writing where <c>write</c> writes: <c>&lt;%= value %&gt;</c> the text <c>write</c> would write, an instance's
/// <c>toString()</c> included, escaped for where it stands in the page's HTML, which its name says (section 13,
/// "Context-aware escaping"; <see cref="WebLingoHtmlEscaping"/>); <c>&lt;%- value %&gt;</c>, and the page's own text, that
/// text as it stands. Each writes nothing for <c>null</c>, so that an optional read that finds nothing is blank, and gives
/// whether the output is full (<see cref="IWebLingoWaitingOutput"/>), which a page then waits for (specification, section
/// 6.12, "Streaming"), so that a write needs no second call to ask.
/// </summary>
public sealed class WebLingoTemplateWriteFunction : IWebLingoTextBuiltin
{
    private readonly IWebLingoTextOutput _output;
    private readonly bool _escapes;
    private readonly WebLingoHtmlEscape _escape;
    private readonly WebLingoHtmlQuoting _quoting;

    /// <summary><c>&lt;%= … %&gt;</c>'s writer for HTML's text, or the raw writer.</summary>
    public WebLingoTemplateWriteFunction(bool escapes, IWebLingoTextOutput output)
        : this(escapes ? WebLingoTemplateWriters.Escaped : WebLingoTemplateWriters.Raw, output)
    {
    }

    /// <summary>The writer named <paramref name="name"/>, the raw writer or one of <c>&lt;%= … %&gt;</c>'s.</summary>
    public WebLingoTemplateWriteFunction(string name, IWebLingoTextOutput output)
    {
        Name = name;
        _output = output;
        _escapes = WebLingoTemplateWriters.TryEscapedAs(name, out _escape, out _quoting);
        if (!_escapes && name != WebLingoTemplateWriters.Raw) throw new ArgumentException($"'{name}' is not a template's writer.", nameof(name));
    }

    public string Name { get; }
    public int Arity => 1;

    public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        if (arguments[0].Kind != WebLingoValueKind.Null)
        {
            var text = WebLingoValueFormatter.Format(arguments[0], heap);
            _output.Write(!_escapes ? text
                : _escape == WebLingoHtmlEscape.Text ? WebLingoHtmlEscaping.Html(text)
                : WebLingoHtmlEscaping.Escape(text, _escape, _quoting));
        }
        result = WebLingoValue.FromBoolean(_output is IWebLingoWaitingOutput { IsFull: true });
        error = null;
        return true;
    }

    /// <summary>
    /// Text as HTML's text and a quoted attribute's value both take it: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and
    /// <c>'</c> as <c>&amp;amp;</c>, <c>&amp;lt;</c>, <c>&amp;gt;</c>, <c>&amp;quot;</c> and <c>&amp;#39;</c>.
    /// </summary>
    public static string Escape(string text) => WebLingoHtmlEscaping.Html(text);
}

/// <summary>
/// The wait for a page's output (specification, section 6.12, "Streaming"; <see cref="WebLingoTemplateWriters.Full"/> and
/// <see cref="WebLingoTemplateWriters.Drained"/>): whether its output holds more than its reader has taken, which only an
/// output that waits (<see cref="IWebLingoWaitingOutput"/>) ever does; and a task that completes once it can take more.
/// </summary>
public sealed class WebLingoOutputWaitFunction(bool drained, IWebLingoTextOutput output) : IWebLingoBuiltinFunction
{
    public string Name => drained ? WebLingoTemplateWriters.Drained : WebLingoTemplateWriters.Full;
    public int Arity => 0;

    public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        (result, error) = (WebLingoValue.Null, null);
        if (output is not IWebLingoWaitingOutput { IsFull: true } waiting)
        {
            if (!drained) result = WebLingoValue.False;
            return true;
        }
        if (!drained)
        {
            result = WebLingoValue.True;
            return true;
        }
        return heap.Scheduler is { } scheduler
            ? scheduler.TryWaitForHost(waiting.WhenReady, out result, out error)
            : true;
    }
}

/// <summary>The built-ins a host that runs pages registers besides the standard library's, each writing to the one output.</summary>
public static class WebLingoTemplateBuiltins
{
    /// <summary>All of them: the raw writer, every writer of <c>&lt;%= … %&gt;</c>, and the wait for the output.</summary>
    public static IWebLingoBuiltinFunction[] Create(IWebLingoTextOutput output)
    {
        var builtins = new List<IWebLingoBuiltinFunction>(Common(output));
        foreach (var (escape, quoting) in WebLingoTemplateWriters.Escapings)
        {
            var name = WebLingoTemplateWriters.EscapedAs(escape, quoting);
            if (name != WebLingoTemplateWriters.Escaped) builtins.Add(new WebLingoTemplateWriteFunction(name, output));
        }
        return [.. builtins];
    }

    /// <summary>
    /// Those most pages call: the raw writer, <c>&lt;%= … %&gt;</c>'s for HTML's text, and the wait for the output; a host
    /// that makes the others when a page names them (<see cref="TryCreateWriter"/>) makes fewer for each request.
    /// </summary>
    public static IWebLingoBuiltinFunction[] Common(IWebLingoTextOutput output) =>
    [
        new WebLingoTemplateWriteFunction(true, output), new WebLingoTemplateWriteFunction(false, output),
        new WebLingoOutputWaitFunction(false, output), new WebLingoOutputWaitFunction(true, output)
    ];

    /// <summary>The writer of <c>&lt;%= … %&gt;</c> named <paramref name="name"/>, if it is one.</summary>
    public static bool TryCreateWriter(string name, IWebLingoTextOutput output, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IWebLingoBuiltinFunction? writer)
    {
        writer = WebLingoTemplateWriters.TryEscapedAs(name, out _, out _) ? new WebLingoTemplateWriteFunction(name, output) : null;
        return writer is not null;
    }
}
