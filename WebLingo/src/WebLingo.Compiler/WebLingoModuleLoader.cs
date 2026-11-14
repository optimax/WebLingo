using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// The compiler as the loader a VM imports modules with while a program runs (section 3.9): it finds files with the
/// host's resolver, as the compiler does for static imports, and compiles them as imports.
/// </summary>
public sealed class WebLingoModuleLoader(WebLingoCompilerOptions options) : IWebLingoModuleLoader, IWebLingoStackModuleLoader
{
    private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);

    /// <summary>
    /// How a file is read, from its name and its text, when not by the WebLingo parser: a template compiler's components'
    /// (section 13), which are pages.
    /// </summary>
    public Func<string, string, WebLingoSyntaxTree>? Parse { get; init; }

    /// <summary>
    /// What finds the files it loads, when not the options' resolver: a template compiler's components' (section 13), whose
    /// own imports the options' resolver finds.
    /// </summary>
    public IWebLingoSourceResolver? Resolver { get; init; }

    public bool TryResolve(string importer, string path, out string name, out string? error)
    {
        name = "";
        if (path.StartsWith("std:", StringComparison.Ordinal) && !(Resolver is null && options.Native is { } host && host.Names(path)))
        {
            name = path;
            error = path == "std:system" ? null : "there is no such standard module";
            return error is null;
        }
        if (Resolver is null && options.Native is { } native && native.Names(path))
        {
            // A native module, by its scheme or its assembly's path, compiled as an import of its name (section 3.9).
            if (!native.TryResolve(importer, path, out var module, out error)) return false;
            name = module.Name;
            _sources[name] = "";
            return true;
        }
        if ((Resolver ?? options.Resolver) is not { } resolver)
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
        var compiler = new WebLingoCompiler(options);
        var result = Parse is { } parse
            ? compiler.CompileImport(parse(name, _sources.GetValueOrDefault(name, "")))
            : compiler.CompileImport(name, _sources.GetValueOrDefault(name, ""));
        error = FirstError(result.Diagnostics);
        return result.Module;
    }

    WebLingoStackModule? IWebLingoStackModuleLoader.Compile(string name, out WebLingoDiagnostic? error)
    {
        var compiler = new WebLingoStackCompiler(options);
        var result = Parse is { } parse
            ? compiler.CompileImport(parse(name, _sources.GetValueOrDefault(name, "")))
            : compiler.CompileImport(name, _sources.GetValueOrDefault(name, ""));
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
