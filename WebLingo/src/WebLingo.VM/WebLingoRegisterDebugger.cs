using WebLingo.Bytecode;
using WebLingo.Core;
namespace WebLingo.VM;

/// <summary>A source debugger attached to a register VM. Callbacks run on the execution's worker.</summary>
public interface IWebLingoRegisterDebugger
{
    void ModuleLoaded(WebLingoCompiledModule module);
    bool ShouldStop(WebLingoDebugPoint point);
    void Stopped(IReadOnlyList<WebLingoDebugFrame> frames);
}

public readonly record struct WebLingoDebugPoint(string Source, WebLingoSourceSpan Span, int Depth, long Activation, int Instruction);
public sealed record WebLingoDebugFrame(string Name, string Source, WebLingoSourceSpan Span, IReadOnlyList<WebLingoDebugVariable> Variables);
public sealed record WebLingoDebugVariable(string Name, string Value, string Type)
{
    public IReadOnlyList<WebLingoDebugVariable> Children { get; init; } = [];
}
