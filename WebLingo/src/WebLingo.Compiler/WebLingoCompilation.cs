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
        var modules = new List<WebLingoModuleSource>();
        var found = new List<WebLingoDiagnostic>(syntaxTree.Diagnostics);
        Gather(syntaxTree, sourceName, options, modules, found);
        if (syntaxTree.HasErrors || found.Count > syntaxTree.Diagnostics.Count) return new WebLingoCompilation(syntaxTree, null, null, found);

        var binder = new WebLingoBinder(options);
        var boundProgram = binder.Bind(modules);
        var diagnostics = new List<WebLingoDiagnostic>(syntaxTree.Diagnostics);
        diagnostics.AddRange(binder.Diagnostics);
        if (binder.Diagnostics.Count > 0) return new WebLingoCompilation(syntaxTree, boundProgram, null, diagnostics);

        return new WebLingoCompilation(syntaxTree, boundProgram, WebLingoLowerer.Lower(boundProgram), diagnostics);
    }

    /// <summary>
    /// The program's files (section 3.9): its own, then each file an import names at a file's top level, found by the
    /// host's resolver, parsed once however many import it, in the order they are first named. A file that cannot be
    /// found is WL2015, and a file's syntax errors are reported, each naming its file.
    /// </summary>
    private static void Gather(WebLingoSyntaxTree main, string mainName, WebLingoCompilerOptions options,
        List<WebLingoModuleSource> modules, List<WebLingoDiagnostic> diagnostics)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal) { [mainName] = 0 };
        var roots = new List<(string Name, WebLingoCompilationUnitSyntax Root, Dictionary<WebLingoImportSyntax, int> Imports)>
        {
            (mainName, main.Root, new Dictionary<WebLingoImportSyntax, int>())
        };
        for (var i = 0; i < roots.Count; i++)
        {
            var (name, root, imports) = roots[i];
            var source = i == 0 ? null : name;
            foreach (var statement in root.Statements)
            {
                if (statement is not WebLingoImportSyntax import || import.Path.Length == 0) continue;
                string? error;
                if (import.Path.StartsWith("std:", StringComparison.Ordinal))
                {
                    // A standard module, named by the scheme std:, never a path (section 3.9).
                    if (StandardModules.Contains(import.Path))
                    {
                        if (!indexes.TryGetValue(import.Path, out var standard))
                        {
                            standard = roots.Count;
                            indexes.Add(import.Path, standard);
                            roots.Add((import.Path, new WebLingoCompilationUnitSyntax([], default), new Dictionary<WebLingoImportSyntax, int>()));
                        }
                        imports[import] = standard;
                        continue;
                    }
                    error = "there is no such standard module";
                }
                else if (options.Resolver is not { } resolver)
                {
                    error = "this host finds no modules";
                }
                else if (resolver.TryResolve(name, import.Path, out var resolved, out var text, out error))
                {
                    if (!indexes.TryGetValue(resolved, out var index))
                    {
                        index = roots.Count;
                        indexes.Add(resolved, index);
                        var tree = new WebLingoParser(text, resolved).ParseCompilationUnit();
                        foreach (var diagnostic in tree.Diagnostics) diagnostics.Add(diagnostic with { Source = resolved });
                        roots.Add((resolved, tree.Root, new Dictionary<WebLingoImportSyntax, int>()));
                    }
                    imports[import] = index;
                    continue;
                }
                diagnostics.Add(new WebLingoDiagnostic(WebLingoDiagnosticCodes.ModuleNotFound, WebLingoDiagnosticSeverity.Error,
                    $"The module '{import.Path}' cannot be imported: {error}.", import.PathSpan) { Source = source });
            }
        }
        foreach (var (name, root, imports) in roots)
        {
            modules.Add(new WebLingoModuleSource(name, root, imports) { IsStandard = StandardModules.Contains(name) });
        }
    }

    /// <summary>The standard modules there are (section 3.9): <c>std:system</c>, the host's built-ins.</summary>
    private static readonly HashSet<string> StandardModules = new(StringComparer.Ordinal) { "std:system" };
}
