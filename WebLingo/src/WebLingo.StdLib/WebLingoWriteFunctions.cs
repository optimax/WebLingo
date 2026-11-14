using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.StdLib;

/// <summary>
/// <c>write(value)</c>: writes the value as text (<see cref="WebLingoValueFormatter"/>) to the host's output, with no line
/// break, and gives <c>null</c>. The CLI host registers it (specification, sections 8.4 and 15.2); it is here so that
/// every host and every test registers the same one, each with its own output.
/// </summary>
public sealed class WebLingoWriteFunction(IWebLingoTextOutput output) : IWebLingoTextBuiltin
{
    public string Name => "write";
    public int Arity => 1;

    public bool TryInvoke(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        output.Write(WebLingoValueFormatter.Format(arguments[0], heap));
        result = WebLingoValue.Null;
        error = null;
        return true;
    }
}

/// <summary><c>writeLn(value)</c>: as <c>write</c>, then a line break, LF on every platform.</summary>
public sealed class WebLingoWriteLnFunction(IWebLingoTextOutput output) : IWebLingoTextBuiltin
{
    public string Name => "writeLn";
    public int Arity => 1;

    public bool TryInvoke(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        output.Write(WebLingoValueFormatter.Format(arguments[0], heap) + "\n");
        result = WebLingoValue.Null;
        error = null;
        return true;
    }
}

/// <summary>The standard library's built-ins, each writing to the one output.</summary>
public static class WebLingoStandardBuiltins
{
    public static IWebLingoBuiltinFunction[] Create(IWebLingoTextOutput output) =>
        [new WebLingoWriteFunction(output), new WebLingoWriteLnFunction(output), new WebLingoDelayFunction()];
}

/// <summary>
/// <c>delay(milliseconds)</c>: a task that completes, with <c>null</c>, once that time has passed, on the host's clock,
/// or in virtual time (specification, section 3.8); for tests.
/// </summary>
public sealed class WebLingoDelayFunction : IWebLingoBuiltinFunction
{
    public string Name => "delay";
    public int Arity => 1;

    public bool TryInvoke(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (arguments[0].Kind != WebLingoValueKind.Int64 || arguments[0].AsInt64 < 0)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                $"'delay' takes a number of milliseconds, an integer of 0 or more, not {WebLingoValueFormatter.Describe(arguments[0], heap)}.");
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
