using WebLingo.Compiler;
using WebLingo.Core;
namespace WebLingo.Templates;

/// <summary>
/// The template compiler (specification, section 13, "Contracts"): a page read by <see cref="WebLingoTemplateParser"/>,
/// then compiled by the WebLingo compiler, for either VM, as a program that writes it. It declares the built-ins a page
/// calls (<see cref="WebLingoTemplateWriters"/>) besides the host's, which the host that runs the page must register.
/// </summary>
public sealed class WebLingoTemplateCompiler(WebLingoCompilerOptions options)
{
    private readonly WebLingoCompilerOptions _options = WithWriters(options);

    public WebLingoCompilationResult Compile(WebLingoSourceText page) =>
        new WebLingoCompiler(_options).Compile(WebLingoTemplateParser.Parse(page));

    public WebLingoStackCompilationResult CompileForStack(WebLingoSourceText page) =>
        new WebLingoStackCompiler(_options).Compile(WebLingoTemplateParser.Parse(page));

    private static WebLingoCompilerOptions WithWriters(WebLingoCompilerOptions options)
    {
        var builtins = new List<WebLingoBuiltinDeclaration>(options.Builtins);
        foreach (var name in (string[])[WebLingoTemplateWriters.Escaped, WebLingoTemplateWriters.Raw])
        {
            var declared = false;
            foreach (var builtin in builtins) declared |= builtin.Name == name;
            if (!declared) builtins.Add(new WebLingoBuiltinDeclaration(name, 1));
        }
        return options with { Builtins = builtins };
    }
}
