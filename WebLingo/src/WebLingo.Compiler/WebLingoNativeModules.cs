using System.Diagnostics.CodeAnalysis;
namespace WebLingo.Compiler;

/// <summary>
/// A native module as the compiler sees it (specification, section 3.9): its name, <c>scheme:module</c>, which identifies
/// it, and its exports, each a function's name, the built-in the host registers it as, and its arity.
/// </summary>
public sealed record WebLingoNativeModuleInfo(string Name, IReadOnlyList<WebLingoNativeExport> Exports);

/// <summary>A native module's exported function: the name it exports it by, the built-in that is it, and its arity.</summary>
public sealed record WebLingoNativeExport(string Name, string Builtin, int Arity);

/// <summary>
/// The host's way to find native modules (section 3.9): which import paths name one, a scheme's (<c>native:sqlite</c>) or
/// an assembly's path; and the module one names, from the importing file's name, loaded, its functions registered as the
/// host's built-ins; or why it cannot be.
/// </summary>
public interface IWebLingoNativeResolver
{
    bool Names(string path);

    bool TryResolve(string importer, string path, [NotNullWhen(true)] out WebLingoNativeModuleInfo? module, out string? error);
}
