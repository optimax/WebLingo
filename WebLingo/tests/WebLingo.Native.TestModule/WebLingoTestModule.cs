using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.Native.TestModule;

/// <summary>
/// A native module for the tests (specification, section 3.9, "Native modules"): <c>twice(x)</c>, an integer doubled;
/// <c>greet(name)</c>, a string it makes; <c>fail()</c>, a runtime error it gives; <c>throws()</c>, an exception it throws;
/// <c>ready(x)</c>, a task already complete with <c>x</c>; and <c>count()</c>, how many times it has been called, so that
/// the tests see the module is loaded once.
/// </summary>
public sealed class WebLingoTestModule : IWebLingoNativeModule
{
    private static long _count;

    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Function("twice", 1, Twice);
        registry.Function("greet", 1, Greet);
        registry.Function("fail", 0, Fail);
        registry.Function("throws", 0, Throws);
        registry.Function("ready", 1, Ready);
        registry.Function("count", 0, Count);
    }

    private static bool Twice(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = null;
        if (arguments[0].Kind != WebLingoValueKind.Int64)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "'twice' takes an integer.");
            return false;
        }
        result = WebLingoValue.FromInt64(arguments[0].AsInt64 * 2);
        return true;
    }

    private static bool Greet(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        heap.TryAllocateString("Hello, " + WebLingoValueFormatter.Format(arguments[0], heap) + "!", out result, out error);

    private static bool Fail(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "The test module fails, as asked.");
        return false;
    }

    private static bool Throws(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        throw new InvalidOperationException("thrown by the test module");

    private static bool Ready(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        if (heap.Scheduler is not { } scheduler || !heap.TryAllocateTask(out result, out error))
        {
            result = WebLingoValue.Null;
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "'ready' needs a VM that runs tasks.");
            return false;
        }
        scheduler.Complete(result, arguments[0]);
        return true;
    }

    private static bool Count(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.FromInt64(Interlocked.Increment(ref _count));
        error = null;
        return true;
    }
}
