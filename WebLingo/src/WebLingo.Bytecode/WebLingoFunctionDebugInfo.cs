using WebLingo.Core;
namespace WebLingo.Bytecode;

/// <summary>Source positions and names for a function compiled for a debugger.</summary>
public sealed record WebLingoFunctionDebugInfo(
    string Source,
    IReadOnlyDictionary<int, WebLingoSourceSpan> Points,
    IReadOnlyList<WebLingoDebugLocal> Locals,
    IReadOnlyList<string> Captures);

public readonly record struct WebLingoDebugLocal(string Name, int Register, int VisibleFrom, int VisibleUntil);
