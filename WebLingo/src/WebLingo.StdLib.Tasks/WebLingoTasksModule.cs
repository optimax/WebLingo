using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Tasks;

/// <summary>
/// <c>std:tasks</c>, a standard module (specification, sections 3.8 and 3.9), a native module installed with the runtime in
/// the <c>std</c> scheme's folder as <c>tasks.dll</c>: <c>all(values)</c>, <c>any(values)</c> and
/// <c>timeout(task, milliseconds)</c>, as <c>Task.all</c>, <c>Task.any</c> and <c>Task.timeout</c> are, each a task the
/// execution's scheduler completes.
/// </summary>
public sealed class WebLingoTasksModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Function("all", 1, All);
        registry.Function("any", 1, Any);
        registry.Function("timeout", 2, Timeout);
    }

    private static bool All(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        TryCombine("all", arguments[0], heap, out result, out error);

    private static bool Any(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        TryCombine("any", arguments[0], heap, out result, out error);

    private static bool Timeout(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryScheduler(heap, out var scheduler, out error)) return false;
        return scheduler.TryTimeout(arguments[0], arguments[1], out result, out error);
    }

    /// <summary><c>all</c> or <c>any</c>: the values, an array (WL3001 otherwise), as <c>Task.all</c> and <c>Task.any</c> take them.</summary>
    private static bool TryCombine(string name, WebLingoValue values, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!TryScheduler(heap, out var scheduler, out error)) return false;
        if (values.Kind != WebLingoValueKind.Array)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'{name}' takes an array of values, not {WebLingoValueFormatter.Describe(values, heap)}.");
            return false;
        }
        var given = new List<WebLingoValue>(heap.GetArray(values));
        return name == "all" ? scheduler.TryAll(given, out result, out error) : scheduler.TryAny(given, out result, out error);
    }

    private static bool TryScheduler(WebLingoHeap heap, out WebLingoScheduler scheduler, out WebLingoRuntimeError? error)
    {
        scheduler = heap.Scheduler!;
        error = heap.Scheduler is null
            ? new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "std:tasks needs a VM that runs tasks.")
            : null;
        return error is null;
    }
}
