using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// One source text through the compiler's front end: parsed, bound and lowered. A stage runs only if the stages
/// before it found no error (section 15.2), so a program with syntax errors reports those alone, and one that does
/// not bind is not lowered.
/// </summary>
public sealed class WebLingoCompilation
{
    private WebLingoCompilation(WebLingoSyntaxTree syntaxTree, WebLingoBoundProgram? boundProgram, WebLingoIrProgram? loweredProgram,
        IReadOnlyList<WebLingoDiagnostic> diagnostics)
    {
        SyntaxTree = syntaxTree;
        BoundProgram = boundProgram;
        LoweredProgram = loweredProgram;
        Diagnostics = diagnostics;
    }

    public WebLingoSyntaxTree SyntaxTree { get; }
    /// <summary>The bound program, or null if the source has syntax errors.</summary>
    public WebLingoBoundProgram? BoundProgram { get; }
    /// <summary>The lowered program, or null if the source has errors.</summary>
    public WebLingoIrProgram? LoweredProgram { get; }
    public IReadOnlyList<WebLingoDiagnostic> Diagnostics { get; }
    public bool HasErrors => LoweredProgram is null;

    public static WebLingoCompilation Create(string source, string sourceName, WebLingoCompilerOptions options)
    {
        var syntaxTree = new WebLingoParser(source, sourceName).ParseCompilationUnit();
        if (syntaxTree.HasErrors) return new WebLingoCompilation(syntaxTree, null, null, syntaxTree.Diagnostics);

        var binder = new WebLingoBinder(options);
        var boundProgram = binder.Bind(syntaxTree.Root);
        var diagnostics = new List<WebLingoDiagnostic>(syntaxTree.Diagnostics);
        diagnostics.AddRange(binder.Diagnostics);
        if (binder.Diagnostics.Count > 0) return new WebLingoCompilation(syntaxTree, boundProgram, null, diagnostics);

        return new WebLingoCompilation(syntaxTree, boundProgram, WebLingoLowerer.Lower(boundProgram), diagnostics);
    }
}
