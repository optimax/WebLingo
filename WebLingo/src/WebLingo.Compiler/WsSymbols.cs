using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>A built-in function the host offers to scripts (<c>write</c>, registered by the CLI), by name and arity.</summary>
public sealed record WsBuiltinDeclaration(string Name, int Arity);

/// <summary>What the compiler needs from its host: the built-in functions scripts may call.</summary>
public sealed record WsCompilerOptions(IReadOnlyList<WsBuiltinDeclaration> Builtins)
{
    public static WsCompilerOptions Empty { get; } = new([]);
}

/// <summary>A name the binder resolved. Symbols are compared by identity.</summary>
public abstract class WsSymbol(string name)
{
    public string Name { get; } = name;
    public override string ToString() => Name;
}

/// <summary>
/// A variable, constant, function or parameter the script declares. <see cref="Index"/> numbers the locals of the
/// function that owns it (<see cref="Owner"/>; the program is the outermost function) from 0, parameters first.
/// </summary>
public sealed class WsLocalSymbol(string name, WsDeclarationKind kind, int index, WsSourceSpan declarationSpan, object owner)
    : WsSymbol(name)
{
    public WsDeclarationKind Kind { get; } = kind;
    public int Index { get; } = index;
    public WsSourceSpan DeclarationSpan { get; } = declarationSpan;
    /// <summary>The function whose frame holds the local.</summary>
    public object Owner { get; } = owner;
    /// <summary>Whether an inner function uses it, so that it lives in a cell the closures share (section 3.5).</summary>
    public bool IsCaptured { get; internal set; }
    /// <summary>A declared function's parameter count, which a direct call is checked against.</summary>
    public int? FunctionArity { get; init; }
    public bool IsConstant => Kind is WsDeclarationKind.Const or WsDeclarationKind.Function;
}

/// <summary>A host built-in function the script calls; <see cref="Index"/> numbers the built-ins it uses from 0.</summary>
public sealed class WsBuiltinSymbol(string name, int arity, int index) : WsSymbol(name)
{
    public int Arity { get; } = arity;
    public int Index { get; } = index;
}
