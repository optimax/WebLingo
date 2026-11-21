using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.StdLib;

/// <summary>
/// <c>write(value)</c>: writes the value as text (<see cref="WebLingoValueFormatter"/>) to the host's output, with no line
/// break, and gives <c>null</c>. <c>std:console</c>'s, a native module (<c>std/console.dll</c>, section 3.9), which a program
/// imports; a host may give it as its own built-in too (specification, sections 8.4 and 15.2), as a test's host does; it is
/// here so that every host and every test has the same one, each with its own output, or, given none, the execution's
/// (<see cref="WebLingoHeap.Output"/>).
/// </summary>
public sealed class WebLingoWriteFunction(IWebLingoTextOutput? output = null) : IWebLingoTextBuiltin
{
    public string Name => "write";
    public int Arity => 1;

    public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        WebLingoStandardBuiltins.TryWrite(output, WebLingoValueFormatter.Format(arguments[0], heap), heap, out result,
            out error);
}

/// <summary><c>writeLn(value)</c>: as <c>write</c>, then a line break, LF on every platform.</summary>
public sealed class WebLingoWriteLnFunction(IWebLingoTextOutput? output = null) : IWebLingoTextBuiltin
{
    public string Name => "writeLn";
    public int Arity => 1;

    public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        WebLingoStandardBuiltins.TryWrite(output, WebLingoValueFormatter.Format(arguments[0], heap) + "\n", heap, out result,
            out error);
}

/// <summary>
/// <c>writeError(value)</c> and <c>writeErrorLn(value)</c>, <c>std:console</c>'s: as <c>write</c> and <c>writeLn</c>, to the
/// host's errors, a script's standard error under <c>weblingo</c>, or, given none, the execution's
/// (<see cref="WebLingoHeap.ErrorOutput"/>) (the owner's answer, 20 November 2026, "Console Module", A9).
/// </summary>
public sealed class WebLingoWriteErrorFunction(bool line, IWebLingoTextOutput? output = null) : IWebLingoTextBuiltin
{
    public string Name => line ? "writeErrorLn" : "writeError";
    public int Arity => 1;

    public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        WebLingoStandardBuiltins.TryWriteError(output, WebLingoValueFormatter.Format(arguments[0], heap) + (line ? "\n" : ""), heap,
            out result, out error);
}

/// <summary>
/// The standard library's built-ins, for a host that gives them as its own, as a test's host does, each writing to the one
/// output: <c>std:console</c>'s writing and reading, which a program reaches by an import under <c>weblingo</c>, and
/// <c>std:system</c>'s <c>delay</c>.
/// </summary>
public static class WebLingoStandardBuiltins
{
    public static IWebLingoBuiltinFunction[] Create(IWebLingoTextOutput output) =>
        [new WebLingoWriteFunction(output), new WebLingoWriteLnFunction(output), new WebLingoDelayFunction(),
         new WebLingoReadLineFunction(false), new WebLingoReadLineFunction(true), new WebLingoPromptFunction(output),
         new WebLingoReadPasswordFunction(), new WebLingoWriteErrorFunction(false), new WebLingoWriteErrorFunction(true)];

    /// <summary>Text to <paramref name="output"/>, or, given none, to the execution's; an error if it has none.</summary>
    internal static bool TryWrite(IWebLingoTextOutput? output, string text, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if ((output ?? heap.Output) is not { } target)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                "This execution has no output to write to.");
            return false;
        }
        target.Write(text);
        error = null;
        return true;
    }

    /// <summary>Text to <paramref name="output"/>, or, given none, to the execution's errors; an error if it has none.</summary>
    internal static bool TryWriteError(IWebLingoTextOutput? output, string text, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if ((output ?? heap.ErrorOutput) is not { } target)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                "This execution has no error output to write to.");
            return false;
        }
        target.Write(text);
        error = null;
        return true;
    }
}

/// <summary>
/// <c>delay(milliseconds)</c>: a task that completes, with <c>null</c>, once that time has passed, on the host's clock,
/// or in virtual time (specification, section 3.8); for tests.
/// </summary>
public sealed class WebLingoDelayFunction : IWebLingoBuiltinFunction
{
    public string Name => "delay";
    public int Arity => 1;

    public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (arguments[0].Kind != WebLingoValueKind.Int64 || arguments[0].AsInt64 < 0)
        {
            error = new WebLingoArgument("delay", 0, 1, "the milliseconds", "a number of milliseconds, an integer, 0 or more").Wrong(arguments[0], heap);
            return false;
        }
        if (heap.Scheduler is not { } scheduler)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "'delay' needs a VM that runs tasks.");
            return false;
        }
        return scheduler.TryDelay(arguments[0].AsInt64, out result, out error);
    }
}

/// <summary>Collects output, for tests and for hosts that send output elsewhere afterwards.</summary>
public sealed class WebLingoCollectedOutput : IWebLingoTextOutput
{
    private readonly System.Text.StringBuilder _text = new();
    public string Text => _text.ToString();

    /// <summary>The output split at its line breaks; a final line break does not start another line.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            var text = Text;
            if (text.Length == 0) return [];
            if (text.EndsWith('\n')) text = text[..^1];
            return text.Split('\n');
        }
    }

    public void Write(string text) => _text.Append(text);
}
