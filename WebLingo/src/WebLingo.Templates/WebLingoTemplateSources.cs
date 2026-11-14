using WebLingo.Core;
namespace WebLingo.Templates;

/// <summary>
/// The files of what a template compiler compiled (specification, section 13): a page's, and those of each component it
/// rendered while it ran, each with the templates it includes (<see cref="WebLingoTemplateFiles"/>). It says where a
/// diagnostic is, at compile time or at run time, and gives the text of a file a diagnostic names.
/// </summary>
public sealed class WebLingoTemplateSources
{
    private readonly List<WebLingoTemplateFiles> _files = [];

    internal void Add(WebLingoTemplateFiles files) => _files.Add(files);

    /// <summary>
    /// A diagnostic where it is: in the page, or a component, it names (none for the page), or in a template one of
    /// them includes, which it then names (<see cref="WebLingoTemplateFiles.Locate"/>); one in another file, a
    /// module's, as it was.
    /// </summary>
    public WebLingoDiagnostic Locate(WebLingoDiagnostic diagnostic)
    {
        for (var i = 0; i < _files.Count; i++)
        {
            var files = _files[i];
            var mine = diagnostic.Source is null ? i == 0 : diagnostic.Source == files.Page.Name;
            if (mine) return files.Locate(diagnostic);
        }
        return diagnostic;
    }

    /// <summary>The text of the file named <paramref name="name"/>: a page, a component, or a template one includes.</summary>
    public WebLingoSourceText? TextOf(string name)
    {
        foreach (var files in _files)
        {
            if (files.TextOf(name) is { } text) return text;
        }
        return null;
    }
}
