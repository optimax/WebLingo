using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// One source text through the compiler's front end: parsed, bound and lowered. A stage runs only if the stages
/// before it found no error (section 15.2), so a program with syntax errors reports those alone, and one that does
/// not bind is not lowered.
/// </summary>
public sealed class WsCompilation
{
    private WsCompilation(WsSyntaxTree syntaxTree, WsBoundProgram? boundProgram, WsIrProgram? loweredProgram,
        IReadOnlyList<WsDiagnostic> diagnostics)
    {
        SyntaxTree = syntaxTree;
        BoundProgram = boundProgram;
        LoweredProgram = loweredProgram;
        Diagnostics = diagnostics;
    }

    public WsSyntaxTree SyntaxTree { get; }
    /// <summary>The bound program, or null if the source has syntax errors.</summary>
    public WsBoundProgram? BoundProgram { get; }
    /// <summary>The lowered program, or null if the source has errors.</summary>
    public WsIrProgram? LoweredProgram { get; }
    public IReadOnlyList<WsDiagnostic> Diagnostics { get; }
    public bool HasErrors => LoweredProgram is null;

    public static WsCompilation Create(string source, string sourceName, WsCompilerOptions options)
    {
        var syntaxTree = new WsParser(source, sourceName).ParseCompilationUnit();
        if (syntaxTree.HasErrors) return new WsCompilation(syntaxTree, null, null, syntaxTree.Diagnostics);

        var binder = new WsBinder(options);
        var boundProgram = binder.Bind(syntaxTree.Root);
        var diagnostics = new List<WsDiagnostic>(syntaxTree.Diagnostics);
        diagnostics.AddRange(binder.Diagnostics);
        if (binder.Diagnostics.Count > 0) return new WsCompilation(syntaxTree, boundProgram, null, diagnostics);

        return new WsCompilation(syntaxTree, boundProgram, WsLowerer.Lower(boundProgram), diagnostics);
    }
}
