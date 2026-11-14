using WebLingo.Core;
namespace WebLingo.Templates;

/// <summary>
/// The files a page's program is compiled from (specification, section 13): the page, and the templates it includes, each
/// at positions of its own among the program's, the page's first, as they stand in it, and each template's after the one
/// before, so that a position of the program names one place in one file. <see cref="Locate"/> turns a diagnostic of the
/// program, at compile time or at run time, into one in the file it is in.
/// </summary>
public sealed class WebLingoTemplateFiles
{
    private readonly List<(int Start, WebLingoSourceText Text)> _files = [];
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    public WebLingoTemplateFiles(WebLingoSourceText page)
    {
        _files.Add((0, page));
        _byName.Add(page.Name, 0);
    }

    public WebLingoSourceText Page => _files[0].Text;

    /// <summary>Where a template's positions start among the program's: its own, if it has some already, or new ones after the last.</summary>
    internal int StartOf(WebLingoSourceText template)
    {
        if (_byName.TryGetValue(template.Name, out var index)) return _files[index].Start;
        var (lastStart, lastText) = _files[^1];
        // One position between files, so that the end of one is not the start of the next.
        var start = lastStart + lastText.Text.Length + 1;
        _byName.Add(template.Name, _files.Count);
        _files.Add((start, template));
        return start;
    }

    /// <summary>
    /// A diagnostic of the page's program where it is: its span in the template it is in, which it then names
    /// (<see cref="WebLingoDiagnostic.Source"/>). One in the page itself, whether it names the page (a component's runtime
    /// error does) or nothing, or in another file, which it names already, is as it was.
    /// </summary>
    public WebLingoDiagnostic Locate(WebLingoDiagnostic diagnostic)
    {
        if (diagnostic.Source is not null && diagnostic.Source != Page.Name) return diagnostic;
        var index = 0;
        for (var i = 1; i < _files.Count && _files[i].Start <= diagnostic.Span.Start; i++) index = i;
        if (index == 0) return diagnostic;
        var (start, text) = _files[index];
        return diagnostic with { Source = text.Name, Span = new WebLingoSourceSpan(diagnostic.Span.Start - start, diagnostic.Span.Length) };
    }

    /// <summary>The text of the file named <paramref name="name"/>, the page or a template it includes, if it is one.</summary>
    public WebLingoSourceText? TextOf(string name) => _byName.TryGetValue(name, out var index) ? _files[index].Text : null;
}
