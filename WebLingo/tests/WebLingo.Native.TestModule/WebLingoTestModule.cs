using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.Native.TestModule;

/// <summary>
/// A native module for the tests (specification, section 3.9, "Native modules"): <c>twice(x)</c>, an integer doubled;
/// <c>greet(name)</c>, a string it makes; <c>fail()</c>, a runtime error it gives; <c>throws()</c>, an exception it throws;
/// <c>ready(x)</c>, a task already complete with <c>x</c>; and <c>count()</c>, how many times it has been called, so that
/// the tests see the module is loaded once. Its classes (section 9, "Native modules", part 2): <c>Counter</c>, whose
/// instances each hold a <see cref="Tally"/>, a host object, <c>new Counter(start)</c>, <c>c.add(n)</c>, <c>c.close()</c>
/// and <c>c.dispose()</c>, which release it, <c>c.toString()</c>, and <c>Counter.describe()</c>; and <c>Other</c>, whose
/// <c>peek()</c> asks for a <c>Counter</c>'s host object on its own instance. <c>released()</c> is how many tallies have
/// been disposed, by a program or by the runtime.
/// </summary>
public sealed class WebLingoTestModule : IWebLingoNativeModule
{
    private static long _count;
    private static long _released;
    private static WebLingoNativeClass? _counter;

    public void Register(IWebLingoNativeRegistry registry)
    {
        registry.Function("twice", 1, Twice);
        registry.Function("greet", 1, Greet);
        registry.Function("fail", 0, Fail);
        registry.Function("throws", 0, Throws);
        registry.Function("ready", 1, Ready);
        registry.Function("count", 0, Count);
        registry.Function("released", 0, Released);
        _counter = registry.Class("Counter")
            .DeclareConstructor(1, MakeCounter)
            .DeclareMethod("add", 1, Add)
            .DeclareMethod("close", 0, Close)
            .DeclareMethod("dispose", 0, Close)
            .DeclareMethod("toString", 0, CounterText)
            .DeclareStaticMethod("describe", 0, Describe);
        registry.Class("Other").DeclareMethod("peek", 0, Peek);
    }

    /// <summary>A counter's host object, which no script sees: its value, and its disposal, which <c>released()</c> counts.</summary>
    private sealed class Tally(long value) : IDisposable
    {
        public long Value { get; set; } = value;

        public void Dispose() => Interlocked.Increment(ref _released);
    }

    private static bool MakeCounter(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (arguments[1].Kind != WebLingoValueKind.Int64)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "A Counter starts from an integer.");
            return false;
        }
        return _counter!.TrySetHostObject(arguments[0], new Tally(arguments[1].AsInt64), heap, out error);
    }

    private static bool Add(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_counter!.TryGetHostObject<Tally>(arguments[0], heap, out var tally, out error)) return false;
        tally.Value += arguments[1].AsInt64;
        result = WebLingoValue.FromInt64(tally.Value);
        return true;
    }

    private static bool Close(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (!_counter!.TryReleaseHostObject(arguments[0], heap, out var released, out error)) return false;
        (released as IDisposable)?.Dispose();
        return true;
    }

    private static bool CounterText(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        heap.TryAllocateString(_counter!.TryGetHostObject<Tally>(arguments[0], heap, out var tally, out _)
            ? $"Counter({tally.Value})"
            : "Counter(disposed)", out result, out error);

    private static bool Describe(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        heap.TryAllocateString("a counter", out result, out error);

    private static bool Peek(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        return _counter!.TryGetHostObject<Tally>(arguments[0], heap, out _, out error);
    }

    private static bool Released(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.FromInt64(Interlocked.Read(ref _released));
        error = null;
        return true;
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
