using WebLingo.Bytecode;
using WebLingo.Core;
namespace WebLingo.Compiler;

/// <summary>
/// The compiler as the loader a VM imports modules with while a program runs (section 3.9): it finds files with the
/// host's resolver, as the compiler does for static imports, and compiles them as imports.
/// </summary>
public sealed class WebLingoModuleLoader(WebLingoCompilerOptions options) : IWebLingoModuleLoader, IWebLingoStackModuleLoader
{
    private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);

    public bool TryResolve(string importer, string path, out string name, out string? error)
    {
        name = "";
        if (path.StartsWith("std:", StringComparison.Ordinal))
        {
            name = path;
            error = path == "std:system" ? null : "there is no such standard module";
            return error is null;
        }
        if (options.Resolver is not { } resolver)
        {
            error = "this host finds no modules";
            return false;
        }
        if (!resolver.TryResolve(importer, path, out name, out var source, out error)) return false;
        _sources[name] = source;
        return true;
    }

    WebLingoCompiledModule? IWebLingoModuleLoader.Compile(string name, out WebLingoDiagnostic? error)
    {
        var result = new WebLingoCompiler(options).CompileImport(name, _sources.GetValueOrDefault(name, ""));
        error = FirstError(result.Diagnostics);
        return result.Module;
    }

    WebLingoStackModule? IWebLingoStackModuleLoader.Compile(string name, out WebLingoDiagnostic? error)
    {
        var result = new WebLingoStackCompiler(options).CompileImport(name, _sources.GetValueOrDefault(name, ""));
        error = FirstError(result.Diagnostics);
        return result.Module;
    }

    private static WebLingoDiagnostic? FirstError(IReadOnlyList<WebLingoDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == WebLingoDiagnosticSeverity.Error) return diagnostic;
        }
        return null;
    }
}
