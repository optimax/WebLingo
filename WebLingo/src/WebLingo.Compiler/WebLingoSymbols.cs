using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>A built-in function the host offers to scripts (<c>write</c>, registered by the CLI), by name and arity.</summary>
public sealed record WebLingoBuiltinDeclaration(string Name, int Arity)
{
    /// <summary>
    /// Whether a call of it renders a component (specification, section 13): <c>name(path, values)</c> runs the file the path
    /// names anew, with the values given its parameters, found and compiled while the program runs, as <c>Module.import</c>
    /// finds one (section 3.9), its call giving the task of running it; it has no function the host gives, and is only
    /// called. A template compiler declares <c>renderComponent</c> so.
    /// </summary>
    public bool RendersComponent { get; init; }
}

/// <summary>What the compiler needs from its host: the built-in functions scripts may call.</summary>
/// <summary>
/// The host's way to find the modules a program imports (section 3.9): from the importing file's name and a path as the
/// import writes it, the module's name, which identifies it, and its source; or why it cannot.
/// </summary>
public interface IWebLingoSourceResolver
{
    bool TryResolve(string importer, string path, out string name, out string source, out string? error);
}

/// <summary>A file of a program, parsed, and the module each of its imports names, by its index among the program's.</summary>
public sealed record WebLingoModuleSource(string Name, WebLingoCompilationUnitSyntax Root, IReadOnlyDictionary<WebLingoImportSyntax, int> Imports)
{
    /// <summary>Whether it is a standard module, which the compiler makes itself, without a file (section 3.9).</summary>
    public bool IsStandard { get; init; }

    /// <summary>A native module, whose exports are the host's built-ins, made without a file (section 3.9); or null.</summary>
    public WebLingoNativeModuleInfo? Native { get; init; }
}

public sealed record WebLingoCompilerOptions(IReadOnlyList<WebLingoBuiltinDeclaration> Builtins)
{
    public static WebLingoCompilerOptions Empty { get; } = new([]);

    /// <summary>How the host finds the files a program imports, or null for a host that finds none (section 3.9).</summary>
    public IWebLingoSourceResolver? Resolver { get; init; }

    /// <summary>
    /// How a file a program imports is read, from its name and its text, when not by the WebLingo parser alone: a template
    /// compiler's, which reads a layout as a page (section 13); null for the WebLingo parser.
    /// </summary>
    public Func<string, string, WebLingoSyntaxTree>? Parse { get; init; }

    /// <summary>How the host finds the native modules a program imports, or null for a host that has none (section 3.9).</summary>
    public IWebLingoNativeResolver? Native { get; init; }
}

/// <summary>A name the binder resolved. Symbols are compared by identity.</summary>
public abstract class WebLingoSymbol(string name)
{
    public string Name { get; } = name;
    public override string ToString() => Name;
}

/// <summary>
/// A variable, constant, function or parameter the script declares. <see cref="Index"/> numbers the locals of the
/// function that owns it (<see cref="Owner"/>; the program is the outermost function) from 0, parameters first.
/// </summary>
public sealed class WebLingoLocalSymbol(string name, WebLingoDeclarationKind kind, int index, WebLingoSourceSpan declarationSpan, object owner)
    : WebLingoSymbol(name)
{
    public WebLingoDeclarationKind Kind { get; } = kind;
    public int Index { get; } = index;
    public WebLingoSourceSpan DeclarationSpan { get; } = declarationSpan;
    /// <summary>The function whose frame holds the local.</summary>
    public object Owner { get; } = owner;
    /// <summary>Whether an inner function uses it, so that it lives in a cell the closures share (section 3.5).</summary>
    public bool IsCaptured { get; internal set; }
    /// <summary>A declared function's parameter count, which a direct call is checked against.</summary>
    public int? FunctionArity { get; init; }
    /// <summary>For a function declared by name: how many of its parameters a call must give, the others having defaults.</summary>
    public int? FunctionMinArity { get; set; }
    /// <summary>For a name an import takes (<c>import { f } from …</c>): the local holding the module, read as its member.</summary>
    public WebLingoLocalSymbol? ImportOf { get; set; }
    public string? ImportName { get; set; }
    public bool IsConstant => Kind is WebLingoDeclarationKind.Const or WebLingoDeclarationKind.Function or WebLingoDeclarationKind.Class;
}

/// <summary>A host built-in function the script calls; <see cref="Index"/> numbers the built-ins it uses from 0.</summary>
public sealed class WebLingoBuiltinSymbol(string name, int arity, int index) : WebLingoSymbol(name)
{
    public int Arity { get; } = arity;
    public int Index { get; } = index;
}
