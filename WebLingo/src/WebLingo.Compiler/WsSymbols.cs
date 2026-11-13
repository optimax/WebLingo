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
/// A variable or constant declared by the script. <see cref="Index"/> numbers the locals of the program from 0, in
/// declaration order.
/// </summary>
public sealed class WsLocalSymbol(string name, WsDeclarationKind kind, int index, WsSourceSpan declarationSpan)
    : WsSymbol(name)
{
    public WsDeclarationKind Kind { get; } = kind;
    public int Index { get; } = index;
    public WsSourceSpan DeclarationSpan { get; } = declarationSpan;
    public bool IsConstant => Kind == WsDeclarationKind.Const;
}

/// <summary>A host built-in function the script calls; <see cref="Index"/> numbers the built-ins it uses from 0.</summary>
public sealed class WsBuiltinSymbol(string name, int arity, int index) : WsSymbol(name)
{
    public int Arity { get; } = arity;
    public int Index { get; } = index;
}
