using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Templates;

/// <summary>
/// The template compiler (specification, section 13, "Contracts"): a page read by <see cref="WebLingoTemplateParser"/>,
/// the templates it includes found by <paramref name="templates"/>, then compiled by the WebLingo compiler, for either VM,
/// as a program that writes it, its diagnostics each in the file it is in (<see cref="WebLingoTemplateFiles.Locate"/>). It
/// declares the built-ins a page calls (<see cref="WebLingoTemplateWriters"/>) besides the host's, which the host that runs
/// the page must register, and <c>renderComponent(path, values)</c>, a component's render, which the VM runs, with the
/// loader <see cref="Components"/> gives: the same resolver finds a component, and this compiler compiles it.
/// </summary>
public sealed class WebLingoTemplateCompiler(WebLingoCompilerOptions options, IWebLingoSourceResolver? templates = null)
{
    /// <summary>The built-in that renders a component (section 13).</summary>
    public const string RenderComponent = "renderComponent";

    private readonly WebLingoCompilerOptions _options = WithBuiltins(options);

    /// <summary>The files of what it compiled, the page's and its components', which say where a diagnostic is.</summary>
    public WebLingoTemplateSources Sources { get; } = new();

    public WebLingoCompilationResult Compile(WebLingoSourceText page) => Compile(page, out _);

    /// <summary>A page compiled for the register VM, and its files, which say where a runtime error's position is.</summary>
    public WebLingoCompilationResult Compile(WebLingoSourceText page, out WebLingoTemplateFiles files)
    {
        var result = new WebLingoCompiler(_options).Compile(Parse(page, out files));
        return result with { Diagnostics = Locate(result.Diagnostics, files) };
    }

    public WebLingoStackCompilationResult CompileForStack(WebLingoSourceText page) => CompileForStack(page, out _);

    /// <summary>A page compiled for the stack VM, and its files, which say where a runtime error's position is.</summary>
    public WebLingoStackCompilationResult CompileForStack(WebLingoSourceText page, out WebLingoTemplateFiles files)
    {
        var result = new WebLingoStackCompiler(_options).Compile(Parse(page, out files));
        return result with { Diagnostics = Locate(result.Diagnostics, files) };
    }

    /// <summary>
    /// What finds and compiles the components a page renders while it runs, for either VM (section 13): each found by the
    /// templates' resolver, from the rendering file's folder, or the root for a path starting with <c>/</c>, and compiled as
    /// an import, a page whose parameters a render gives; its files added to <see cref="Sources"/>.
    /// </summary>
    public WebLingoModuleLoader Components =>
        new(_options with { Resolver = templates }) { Parse = (name, text) => Parse(new WebLingoSourceText(text, name), out _) };

    private WebLingoSyntaxTree Parse(WebLingoSourceText page, out WebLingoTemplateFiles files)
    {
        var tree = WebLingoTemplateParser.Parse(page, templates, out files);
        Sources.Add(files);
        return tree;
    }

    private static List<WebLingoDiagnostic> Locate(IReadOnlyList<WebLingoDiagnostic> diagnostics, WebLingoTemplateFiles files)
    {
        var located = new List<WebLingoDiagnostic>(diagnostics.Count);
        foreach (var diagnostic in diagnostics) located.Add(files.Locate(diagnostic));
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
}
