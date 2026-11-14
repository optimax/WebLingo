using System.Diagnostics.CodeAnalysis;
namespace WebLingo.Runtime;

public interface IWebLingoDisposable { void Dispose(); }
public enum WebLingoOperationState { Created, Pending, Completed, Failed, Cancelled }
public readonly record struct WebLingoWorkerId(int Value);

/// <summary>
/// What a VM executes a module in (specification, section 8.4, "VM"): the worker that owns the execution, a
/// cancellation signal, an instruction budget, a resource budget, and the runtime services that give it the host's
/// built-ins.
/// </summary>
public sealed record WebLingoExecutionContext(WebLingoWorkerId OwnerWorkerId)
{
    public IWebLingoRuntimeServices Services { get; init; } = WebLingoBuiltinRegistry.None;
    public WebLingoCancellationSignal? Cancellation { get; init; }
    /// <summary>How many instructions the execution may run before it is stopped (WL3003).</summary>
    public long InstructionBudget { get; init; } = long.MaxValue;
    public WebLingoResourceBudget Resources { get; init; } = WebLingoResourceBudget.Default;
    /// <summary>The host's clock, which <c>delay</c>'s timers wait on; without one, the execution runs in virtual time (section 3.8).</summary>
    public IWebLingoExecutionClock? Clock { get; init; }

    /// <summary>
    /// The values its host gives the program's parameters (<c>param name;</c>, section 3.9), each a string, as
    /// <c>weblingo run script.wl name=value</c> gives them; none by default.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// The resources an execution may take: registers (a module that needs more is not run, WL3003), and its heap's
/// units (<see cref="WebLingoHeap"/>; an allocation beyond them is WL3003), and how deep calls may nest (WL3003). The
/// defaults are arbitrary, and easy to change.
/// </summary>
public sealed record WebLingoResourceBudget(int MaxRegisters, long MaxHeapUnits = WebLingoHeap.DefaultMaxUnits, int MaxCallDepth = 10_000)
{
    public static WebLingoResourceBudget Default { get; } = new(65535);
}

/// <summary>
/// Asks an execution to stop. The VM looks at it as it runs (every so many instructions) and stops with WL3004.
/// WebLingo's own signal, not .NET's <c>CancellationToken</c>, so that the core does not depend on .NET's
/// asynchronous model (WL-P007).
/// </summary>
public sealed class WebLingoCancellationSignal
{
    private volatile bool _requested;
    public bool IsCancellationRequested => _requested;
    public void Cancel() => _requested = true;
}

/// <summary>A host built-in function, such as <c>write</c>.</summary>
public interface IWebLingoBuiltinFunction
{
    string Name { get; }
    int Arity { get; }
    /// <summary>
    /// Calls the function with exactly <see cref="Arity"/> script values, whose objects are in <paramref name="heap"/>,
    /// where any it makes go too.
    /// </summary>
    bool TryInvoke(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error);
}

/// <summary>
/// A built-in that writes its argument as text, as <c>write</c> does: a VM gives it the text of its argument, as a
/// string, when the argument holds an instance whose class overrides <c>toString()</c>, having run that method
/// (specification, section 3.7); otherwise the argument itself, whose text <see cref="WebLingoValueFormatter"/> gives.
/// </summary>
public interface IWebLingoTextBuiltin : IWebLingoBuiltinFunction;

/// <summary>The runtime services a host gives an execution; M0.6 has its built-ins.</summary>
public interface IWebLingoRuntimeServices
{
    bool TryGetBuiltin(string name, [MaybeNullWhen(false)] out IWebLingoBuiltinFunction builtin);
    IReadOnlyList<IWebLingoBuiltinFunction> Builtins { get; }
}

/// <summary>The host's built-ins, by name.</summary>
public sealed class WebLingoBuiltinRegistry : IWebLingoRuntimeServices
{
    private readonly Dictionary<string, IWebLingoBuiltinFunction> _byName = new(StringComparer.Ordinal);
    private readonly List<IWebLingoBuiltinFunction> _inOrder = [];

    public WebLingoBuiltinRegistry(params IWebLingoBuiltinFunction[] builtins)
    {
        foreach (var builtin in builtins) Register(builtin);
    }

    public static WebLingoBuiltinRegistry None { get; } = new();

    public IReadOnlyList<IWebLingoBuiltinFunction> Builtins => _inOrder;

    public void Register(IWebLingoBuiltinFunction builtin)
    {
        if (!_byName.TryAdd(builtin.Name, builtin))
        {
            throw new ArgumentException($"A built-in named '{builtin.Name}' is already registered.", nameof(builtin));
        }
        _inOrder.Add(builtin);
    }

    public bool TryGetBuiltin(string name, [MaybeNullWhen(false)] out IWebLingoBuiltinFunction builtin) =>
        _byName.TryGetValue(name, out builtin);
}

/// <summary>Where a host sends a script's text output, as it is written; a line break in it is LF.</summary>
public interface IWebLingoTextOutput
{
    void Write(string text);
}
