using System.Diagnostics.CodeAnalysis;
namespace WebLingo.Runtime;

public interface IWsDisposable { void Dispose(); }
public enum WsOperationState { Created, Pending, Completed, Failed, Cancelled }
public readonly record struct WsWorkerId(int Value);

/// <summary>
/// What a VM executes a module in (specification, section 8.4, "VM"): the worker that owns the execution, a
/// cancellation signal, an instruction budget, a resource budget, and the runtime services that give it the host's
/// built-ins.
/// </summary>
public sealed record WsExecutionContext(WsWorkerId OwnerWorkerId)
{
    public IWsRuntimeServices Services { get; init; } = WsBuiltinRegistry.None;
    public WsCancellationSignal? Cancellation { get; init; }
    /// <summary>How many instructions the execution may run before it is stopped (WS3003).</summary>
    public long InstructionBudget { get; init; } = long.MaxValue;
    public WsResourceBudget Resources { get; init; } = WsResourceBudget.Default;
}

/// <summary>The resources an execution may take; a module that needs more is not run (WS3003).</summary>
public sealed record WsResourceBudget(int MaxRegisters)
{
    public static WsResourceBudget Default { get; } = new(65535);
}

/// <summary>
/// Asks an execution to stop. The VM looks at it as it runs (every so many instructions) and stops with WS3004.
/// WebLingo's own signal, not .NET's <c>CancellationToken</c>, so that the core does not depend on .NET's
/// asynchronous model (WS-P007).
/// </summary>
public sealed class WsCancellationSignal
{
    private volatile bool _requested;
    public bool IsCancellationRequested => _requested;
    public void Cancel() => _requested = true;
}

/// <summary>A host built-in function, such as <c>write</c>.</summary>
public interface IWsBuiltinFunction
{
    string Name { get; }
    int Arity { get; }
    /// <summary>Calls the function with exactly <see cref="Arity"/> script values.</summary>
    bool TryInvoke(WsValue[] arguments, out WsValue result, out WsRuntimeError? error);
}

/// <summary>The runtime services a host gives an execution; M0.6 has its built-ins.</summary>
public interface IWsRuntimeServices
{
    bool TryGetBuiltin(string name, [MaybeNullWhen(false)] out IWsBuiltinFunction builtin);
    IReadOnlyList<IWsBuiltinFunction> Builtins { get; }
}

/// <summary>The host's built-ins, by name.</summary>
public sealed class WsBuiltinRegistry : IWsRuntimeServices
{
    private readonly Dictionary<string, IWsBuiltinFunction> _byName = new(StringComparer.Ordinal);
    private readonly List<IWsBuiltinFunction> _inOrder = [];

    public WsBuiltinRegistry(params IWsBuiltinFunction[] builtins)
    {
        foreach (var builtin in builtins) Register(builtin);
    }

    public static WsBuiltinRegistry None { get; } = new();

    public IReadOnlyList<IWsBuiltinFunction> Builtins => _inOrder;

    public void Register(IWsBuiltinFunction builtin)
    {
        if (!_byName.TryAdd(builtin.Name, builtin))
        {
            throw new ArgumentException($"A built-in named '{builtin.Name}' is already registered.", nameof(builtin));
        }
        _inOrder.Add(builtin);
    }

    public bool TryGetBuiltin(string name, [MaybeNullWhen(false)] out IWsBuiltinFunction builtin) =>
        _byName.TryGetValue(name, out builtin);
}

/// <summary>Where a host sends a script's text output, as it is written; a line break in it is LF.</summary>
public interface IWsTextOutput
{
    void Write(string text);
}
