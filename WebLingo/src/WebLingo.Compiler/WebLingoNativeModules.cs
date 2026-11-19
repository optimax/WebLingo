using System.Diagnostics.CodeAnalysis;
using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// A native module as the compiler sees it (specification, section 3.9): its name, <c>scheme:module</c>, which identifies
/// it, and its exports, each a function's name, the built-in the host registers it as, and its arity.
/// </summary>
public sealed record WebLingoNativeModuleInfo(string Name, IReadOnlyList<WebLingoNativeExport> Exports)
{
    /// <summary>Its exported classes (section 3.9).</summary>
    public IReadOnlyList<WebLingoNativeClassInfo> Classes { get; init; } = [];
}

/// <summary>
/// A native module's class as the compiler sees it: its name, and the built-ins that are its constructor, its methods and
/// its static methods, each export's arity its built-in's, which is one more than the member's for a constructor and a
/// method, whose built-in is given the instance first.
/// </summary>
public sealed record WebLingoNativeClassInfo(string Name, WebLingoNativeExport Constructor, IReadOnlyList<WebLingoNativeExport> Methods,
    IReadOnlyList<WebLingoNativeExport> StaticMethods)
{
    /// <summary>Its properties, each its getter's built-in, given the instance, and its setter's, or null (section 3.7).</summary>
    public IReadOnlyList<WebLingoNativePropertyInfo> Properties { get; init; } = [];

    /// <summary>Its static properties, each its getter's built-in, given nothing, and its setter's, given the value, or null.</summary>
    public IReadOnlyList<WebLingoNativePropertyInfo> StaticProperties { get; init; } = [];

    /// <summary>Its indexer's getter's built-in, given the instance and the index, or null for a class without one.</summary>
    public WebLingoNativeExport? IndexGetter { get; init; }

    /// <summary>Its indexer's setter's built-in, given the instance, the index and the value, or null.</summary>
    public WebLingoNativeExport? IndexSetter { get; init; }

    /// <summary>
    /// Its indexer's optional getter's built-in, given the instance and the index, which <c>x?[k]</c> calls, <c>null</c>
    /// where the getter raises a missing key or an index out of range; or null for a class without an indexer.
    /// </summary>
    public WebLingoNativeExport? IndexOptional { get; init; }

    /// <summary>Its methods written in WebLingo (section 3.9), as <see cref="TryParseMethods"/> read them.</summary>
    public IReadOnlyList<WebLingoMethodSyntax> WebLingoMethods { get; init; } = [];

    /// <summary>
    /// For a class its module's functions make instances of, the built-in the program calls when it begins, given the
    /// class, so that the module knows it (section 3.10); null for any other.
    /// </summary>
    public WebLingoNativeExport? Registration { get; init; }

    /// <summary>
    /// A native class's methods written in WebLingo, as a class's body declares them, read; false, with why, for a source
    /// that does not parse, that declares anything but methods, or a method whose name <paramref name="taken"/> holds or
    /// another of them has.
    /// </summary>
    public static bool TryParseMethods(string source, ISet<string> taken, out IReadOnlyList<WebLingoMethodSyntax> methods, out string? error)
    {
        methods = [];
        var tree = new WebLingoParser("class Native {\n" + source + "\n}", "native").ParseCompilationUnit();
        foreach (var diagnostic in tree.Diagnostics)
        {
            if (diagnostic.Severity != WebLingoDiagnosticSeverity.Error) continue;
            error = $"its methods written in WebLingo do not parse: {diagnostic.Message}";
            return false;
        }
        if (tree.Root.Statements is not [WebLingoClassDeclarationSyntax { StaticFields.Count: 0 } declaration])
        {
            error = "its methods written in WebLingo are not methods alone";
            return false;
        }
        foreach (var method in declaration.Methods)
        {
            if (method.Name == "constructor" || !taken.Add(method.Name))
            {
                error = $"its method written in WebLingo '{method.Name}' has a name another member has";
                return false;
            }
        }
        (methods, error) = (declaration.Methods, null);
        return true;
    }
}

/// <summary>A native class's property: its name, its getter's built-in, and its setter's, or null.</summary>
public sealed record WebLingoNativePropertyInfo(string Name, WebLingoNativeExport Getter, WebLingoNativeExport? Setter);

/// <summary>A native module's exported function: the name it exports it by, the built-in that is it, and its arity.</summary>
public sealed record WebLingoNativeExport(string Name, string Builtin, int Arity)
{
    /// <summary>
    /// How many of its last parameters a call may leave out, each then <c>null</c> to its built-in, which a function written
    /// in WebLingo, with those defaults, calls in its place (section 3.9).
    /// </summary>
    public int Optional { get; init; }

    /// <summary>
    /// For a native class's static method, whether it is a factory, written in WebLingo, shorthand for <c>new this(…)</c>
    /// and its instance's <see cref="FactoryMethod"/> (section 3.7, "No sealed classes").
    /// </summary>
    public bool Factory { get; init; }

    /// <summary>A factory's instance method, which opens the instance it makes.</summary>
    public string? FactoryMethod { get; init; }

    /// <summary>Whether a factory awaits its method, and is asynchronous.</summary>
    public bool FactoryAsync { get; init; }

    /// <summary>Whether a factory's arguments go to <c>new</c>, not to its method.</summary>
    public bool FactoryToConstructor { get; init; }
}

/// <summary>
/// The host's way to find native modules (section 3.9): which import paths name one, a scheme's (<c>native:sqlite</c>) or
/// an assembly's path; and the module one names, from the importing file's name, loaded, its functions registered as the
/// host's built-ins; or why it cannot be.
/// </summary>
public interface IWebLingoNativeResolver
{
    bool Names(string path);

    bool TryResolve(string importer, string path, [NotNullWhen(true)] out WebLingoNativeModuleInfo? module, out string? error);

    /// <summary>
    /// The standard modules the host has, each loaded, but those whose names every program has, for the compiler to
    /// suggest the import a name it does not know needs (section 3.9); asked only of a program that fails to compile.
    /// </summary>
    IReadOnlyList<WebLingoNativeModuleInfo> StandardModules();
}
