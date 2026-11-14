using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>
/// Exceptions, the same for every VM (specification, section 3.7): which runtime errors a handler may catch, the
/// <c>Error</c> a caught one becomes, where an exception was first thrown, and how one that nothing caught is reported.
/// </summary>
public static class WsExceptions
{
    /// <summary>
    /// Whether a disposal goes on after one of its dispose methods throws: its next methods run, each once, and the
    /// exception is thrown again after the last, a later one replacing it. Section 3.4 proposes it; the owner has been
    /// asked, and this is the one place to change if the answer is that the disposal stops there.
    /// </summary>
    public static readonly bool DisposalGoesOn = true;

    /// <summary>
    /// Whether a handler may catch a runtime error of this code: every one but the execution's limits (WS3003), its
    /// cancellation (WS3004) and bytecode the VM cannot run (WS4001), which stop the program.
    /// </summary>
    public static bool IsCatchable(string code) =>
        code is not (WsDiagnosticCodes.BudgetExceeded or WsDiagnosticCodes.Cancelled
            or WsDiagnosticCodes.BytecodeVerificationFailure);

    /// <summary>
    /// A runtime error as an instance of <paramref name="errorClass"/>, <c>Error</c>, with its <c>message</c> and its
    /// <c>code</c>; false if there is no such class, or no room for it in the heap.
    /// </summary>
    public static bool TryMakeError(WsHeap heap, WsValue errorClass, WsRuntimeError error, out WsValue exception)
    {
        exception = WsValue.Null;
        if (errorClass.Kind != WsValueKind.Class || !heap.TryAllocateInstance(errorClass, out var instance, out _)) return false;
        var roots = new[] { instance };
        heap.AddRoots(roots); // While its strings are made, which may collect.
        try
        {
            if (!heap.TryAllocateString(error.Message, out var message, out _) || !heap.TrySetField(instance, "message", message, out _)
                || !heap.TryAllocateString(error.Code, out var code, out _) || !heap.TrySetField(instance, "code", code, out _))
            {
                return false;
            }
        }
        finally
        {
            heap.RemoveLastRoots(roots);
        }
        exception = instance;
        return true;
    }

    /// <summary>Whether a value is an instance of <c>Error</c>, or of a class that extends it.</summary>
    public static bool IsError(WsValue value, WsValue errorClass, WsHeap heap)
    {
        if (value.Kind != WsValueKind.Instance || errorClass.Kind != WsValueKind.Class) return false;
        for (var current = heap.ClassOf(value); current.Kind == WsValueKind.Class; current = heap.ClassBase(current))
        {
            if (current == errorClass) return true;
        }
        return false;
    }

    /// <summary>
    /// What an exception that nothing caught is reported as: an <c>Error</c> whose code is a string, as a runtime
    /// error becomes, as that code and its message, as the runtime error itself would be; any other value as WS3008.
    /// </summary>
    public static (string Code, string Message) Uncaught(WsValue exception, WsValue errorClass, WsHeap heap)
    {
        if (!IsError(exception, errorClass, heap))
        {
            return (WsDiagnosticCodes.UncaughtException,
                $"An exception was thrown and not caught: {WsValueFormatter.Format(exception, heap)}");
        }
        heap.TryGetField(exception, "message", out var message);
        var text = message.Kind == WsValueKind.String ? heap.GetString(message) : WsValueFormatter.Format(message, heap);
        if (heap.TryGetField(exception, "code", out var code) && code.Kind == WsValueKind.String) return (heap.GetString(code), text);
        return (WsDiagnosticCodes.UncaughtException, $"An error was thrown and not caught: {text}");
    }

    /// <summary>
    /// Where an exception was first thrown, as a handler gives it and <c>Rethrow</c> takes it: a function (0 the entry
    /// point, <c>i</c> + 1 the module's function <c>i</c>) and an instruction of it.
    /// </summary>
    public static long Origin(int function, int instruction) => ((long)function << 32) | (uint)instruction;

    public static (int Function, int Instruction) FromOrigin(long origin) => ((int)(origin >> 32), (int)(origin & 0xFFFFFFFF));
}
