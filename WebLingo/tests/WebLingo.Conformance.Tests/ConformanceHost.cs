using System.Diagnostics.CodeAnalysis;
using WebLingo.Cli;
using WebLingo.Compiler;
using WebLingo.Runtime;
using WebLingo.StdLib;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// The host of the tests that compile and run the conformance programs themselves, rather than through <c>weblingo</c>:
/// the standard library's built-ins as its own, <c>write</c>, <c>writeLn</c> and <c>delay</c>, and the standard modules
/// installed beside the tests, <c>std:console</c> among them, which the conformance programs import (specification,
/// section 3.9), each module's writing to the execution's output. One for each test class, whose tests run one at a time,
/// its loader keeping the modules it has loaded.
/// </summary>
internal sealed class ConformanceHost
{
    private readonly WebLingoBuiltinRegistry _native = new();

    public ConformanceHost()
    {
        var schemes = new Dictionary<string, string>(StringComparer.Ordinal) { ["std"] = Path.Combine(AppContext.BaseDirectory, "std") };
        Options = new WebLingoCompilerOptions(
            [new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1), new WebLingoBuiltinDeclaration("delay", 1)])
        {
            Native = new WebLingoNativeModuleLoader(schemes, _native)
        };
    }

    /// <summary>The compiler's options: the host's built-ins, and the native modules' loader.</summary>
    public WebLingoCompilerOptions Options { get; }

    /// <summary>An execution writing to <paramref name="output"/>, the host's built-ins and the loaded modules' functions its services.</summary>
    public WebLingoExecutionContext Context(IWebLingoTextOutput output, long budget) =>
        new(new WebLingoWorkerId(0))
        {
            Services = new Services(new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output)), _native),
            Output = output,
            InstructionBudget = budget,
        };

    /// <summary>The host's own built-ins, then the native modules' functions.</summary>
    private sealed class Services(WebLingoBuiltinRegistry own, WebLingoBuiltinRegistry native) : IWebLingoRuntimeServices
    {
        public IReadOnlyList<IWebLingoBuiltinFunction> Builtins { get; } = [.. own.Builtins, .. native.Builtins];

        public bool TryGetBuiltin(string name, [MaybeNullWhen(false)] out IWebLingoBuiltinFunction builtin) =>
            own.TryGetBuiltin(name, out builtin) || native.TryGetBuiltin(name, out builtin);
    }
}
