using WebLingo.Core;
namespace WebLingo.Bytecode;

/// <summary>
/// What a host gives a VM so that a program may import a module while it runs (<c>Module.import(path)</c>, section
/// 3.9): it finds a module's file from the file importing it, and compiles that file, with the files it imports, as an
/// import (<see cref="WebLingoCompiledModule.IsImport"/>), which the VM then joins to the program.
/// </summary>
public interface IWebLingoModuleLoader
{
    /// <summary>The name of the file <paramref name="path"/> names, from <paramref name="importer"/>'s folder; false, with why, if there is none.</summary>
    bool TryResolve(string importer, string path, out string name, out string? error);

    /// <summary>The file <paramref name="name"/> compiled as an import; null, with its first error, if it does not compile.</summary>
    WebLingoCompiledModule? Compile(string name, out WebLingoDiagnostic? error);
}

/// <summary>A host's <see cref="IWebLingoModuleLoader"/> for the stack VM, whose modules are its own.</summary>
public interface IWebLingoStackModuleLoader
{
    bool TryResolve(string importer, string path, out string name, out string? error);

    WebLingoStackModule? Compile(string name, out WebLingoDiagnostic? error);
}
