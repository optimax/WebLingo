using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Templates;

/// <summary>
/// The template compiler (specification, section 13, "Contracts"): a page read by <see cref="WebLingoTemplateParser"/>,
/// the templates it includes found by <paramref name="templates"/>, then compiled by the WebLingo compiler, for either VM,
/// as a program that writes it, its diagnostics each in the file it is in (<see cref="Sources"/>). It declares the
/// built-ins a page calls (<see cref="WebLingoTemplateWriters"/>) besides the host's, which the host that runs the page
/// must register, and <c>renderComponent(path, values)</c>, a component's render, which the VM runs, with the loader
/// <see cref="Components"/> gives: the same resolver finds a component, and this compiler compiles it. A page's layout is
/// an import, which the same resolver finds, and this compiler reads as a layout; the page's other imports the host's
/// resolver finds, as a script's.
/// </summary>
public sealed class WebLingoTemplateCompiler
{
    /// <summary>The built-in that renders a component (section 13).</summary>
    public const string RenderComponent = "renderComponent";

    private readonly IWebLingoSourceResolver? _templates;
    private readonly WebLingoCompilerOptions _options;
    private readonly HashSet<string> _layouts = new(StringComparer.Ordinal);

    public WebLingoTemplateCompiler(WebLingoCompilerOptions options, IWebLingoSourceResolver? templates = null)
    {
        _templates = templates;
        _options = WithBuiltins(options) with { Resolver = new Imports(options.Resolver, templates, _layouts), Parse = ParseImport };
    }

    /// <summary>The files of what it compiled, the page's, its layouts' and its components', which say where a diagnostic is.</summary>
    public WebLingoTemplateSources Sources { get; } = new();

    public WebLingoCompilationResult Compile(WebLingoSourceText page)
    {
        var result = new WebLingoCompiler(_options).Compile(Parse(page, WebLingoTemplateKind.Page));
        return result with { Diagnostics = Locate(result.Diagnostics) };
    }

    public WebLingoStackCompilationResult CompileForStack(WebLingoSourceText page)
    {
        var result = new WebLingoStackCompiler(_options).Compile(Parse(page, WebLingoTemplateKind.Page));
        return result with { Diagnostics = Locate(result.Diagnostics) };
    }

    /// <summary>
    /// What finds and compiles the components a page renders while it runs, for either VM (section 13): each found by the
    /// templates' resolver, from the rendering file's folder, or the root for a path starting with <c>/</c>, and compiled as
    /// an import, a page whose parameters a render gives; its files added to <see cref="Sources"/>.
    /// </summary>
    public WebLingoModuleLoader Components =>
        new(_options) { Resolver = _templates, Parse = (name, text) => Parse(new WebLingoSourceText(text, name), WebLingoTemplateKind.Page) };

    private WebLingoSyntaxTree Parse(WebLingoSourceText page, WebLingoTemplateKind kind)
    {
        var tree = WebLingoTemplateParser.Parse(page, _templates, out var files, kind);
        Sources.Add(files);
        return tree;
    }

    /// <summary>A file a page imports: its layout, a template read as one, or a module, which the WebLingo parser reads.</summary>
    private WebLingoSyntaxTree ParseImport(string name, string text) =>
        _layouts.Contains(name)
            ? Parse(new WebLingoSourceText(text, name), WebLingoTemplateKind.Layout)
            : new WebLingoParser(text, name).ParseCompilationUnit();

    private List<WebLingoDiagnostic> Locate(IReadOnlyList<WebLingoDiagnostic> diagnostics)
    {
        var located = new List<WebLingoDiagnostic>(diagnostics.Count);
        foreach (var diagnostic in diagnostics) located.Add(Sources.Locate(diagnostic));
        return located;
    }

    private static WebLingoCompilerOptions WithBuiltins(WebLingoCompilerOptions options)
    {
        var builtins = new List<WebLingoBuiltinDeclaration>(options.Builtins);
        foreach (var declaration in (WebLingoBuiltinDeclaration[])[
                     new(WebLingoTemplateWriters.Escaped, 1), new(WebLingoTemplateWriters.Raw, 1),
                     new(RenderComponent, 2) { RendersComponent = true }])
        {
            var declared = false;
            foreach (var builtin in builtins) declared |= builtin.Name == declaration.Name;
            if (!declared) builtins.Add(declaration);
        }
        return options with { Builtins = builtins };
    }

    /// <summary>
    /// The files a page's program imports: a layout, its path marked so (<see cref="WebLingoTemplateParser.LayoutScheme"/>),
    /// found by the templates' resolver, and noted as one; any other by the host's.
    /// </summary>
    private sealed class Imports(IWebLingoSourceResolver? modules, IWebLingoSourceResolver? templates, HashSet<string> layouts)
        : IWebLingoSourceResolver
    {
        public bool TryResolve(string importer, string path, out string name, out string source, out string? error)
        {
            (name, source, error) = ("", "", null);
            if (path.StartsWith(WebLingoTemplateParser.LayoutScheme, StringComparison.Ordinal))
            {
                if (templates is null)
                {
                    error = "this host finds no templates";
                    return false;
                }
                if (!templates.TryResolve(importer, path[WebLingoTemplateParser.LayoutScheme.Length..], out name, out source, out error)) return false;
                layouts.Add(name);
                return true;
            }
            if (modules is null)
            {
                error = "this host finds no modules";
                return false;
            }
            return modules.TryResolve(importer, path, out name, out source, out error);
        }
    }
}
