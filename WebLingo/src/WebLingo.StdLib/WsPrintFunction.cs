using WebLingo.Runtime;
namespace WebLingo.StdLib;

/// <summary>
/// <c>print(value)</c>: writes the value as text (<see cref="WsValueFormatter"/>) and a line break to the host's
/// output, and gives <c>null</c>. The specification has <c>print</c> registered by the CLI host (section 8.4); it is
/// here so that every host and every test registers the same one, each with its own output.
/// </summary>
public sealed class WsPrintFunction(IWsTextOutput output) : IWsBuiltinFunction
{
    public string Name => "print";
    public int Arity => 1;

    public bool TryInvoke(WsValue[] arguments, out WsValue result, out WsRuntimeError? error)
    {
        output.WriteLine(WsValueFormatter.Format(arguments[0]));
        result = WsValue.Null;
        error = null;
        return true;
    }
}

/// <summary>Collects output lines, for tests and for hosts that send output elsewhere afterwards.</summary>
public sealed class WsCollectedOutput : IWsTextOutput
{
    private readonly List<string> _lines = [];
    public IReadOnlyList<string> Lines => _lines;
    public void WriteLine(string text) => _lines.Add(text);
}
