using WebLingo.Runtime;
namespace WebLingo.StdLib;

/// <summary>
/// <c>write(value)</c>: writes the value as text (<see cref="WsValueFormatter"/>) to the host's output, with no line
/// break, and gives <c>null</c>. The CLI host registers it (specification, sections 8.4 and 15.2); it is here so that
/// every host and every test registers the same one, each with its own output.
/// </summary>
public sealed class WsWriteFunction(IWsTextOutput output) : IWsBuiltinFunction
{
    public string Name => "write";
    public int Arity => 1;

    public bool TryInvoke(WsValue[] arguments, out WsValue result, out WsRuntimeError? error)
    {
        output.Write(WsValueFormatter.Format(arguments[0]));
        result = WsValue.Null;
        error = null;
        return true;
    }
}

/// <summary><c>writeLn(value)</c>: as <c>write</c>, then a line break, LF on every platform.</summary>
public sealed class WsWriteLnFunction(IWsTextOutput output) : IWsBuiltinFunction
{
    public string Name => "writeLn";
    public int Arity => 1;

    public bool TryInvoke(WsValue[] arguments, out WsValue result, out WsRuntimeError? error)
    {
        output.Write(WsValueFormatter.Format(arguments[0]) + "\n");
        result = WsValue.Null;
        error = null;
        return true;
    }
}

/// <summary>The standard library's built-ins, each writing to the one output.</summary>
public static class WsStandardBuiltins
{
    public static IWsBuiltinFunction[] Create(IWsTextOutput output) => [new WsWriteFunction(output), new WsWriteLnFunction(output)];
}

/// <summary>Collects output, for tests and for hosts that send output elsewhere afterwards.</summary>
public sealed class WsCollectedOutput : IWsTextOutput
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
