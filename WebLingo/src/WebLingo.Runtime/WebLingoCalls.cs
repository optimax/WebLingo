namespace WebLingo.Runtime;

/// <summary>
/// A built-in's work that calls the program's functions on the way, as <c>sort</c> calls its comparison (specification,
/// section 5.6, "Where the time goes", cause 6; Proposed): its VM runs each call as it runs any, in a frame of its own on
/// its own stack, never .NET's, then gives the work what the call gave, so that the call may throw, as any may, an
/// <c>async</c> function's giving its task. A built-in it calls is called at once.
/// </summary>
public abstract class WebLingoCalls
{
    protected WebLingoCalls(int count) => Values = new WebLingoValue[count];

    /// <summary>The values it holds, which its VM keeps as roots from its beginning to its end; its calls' among them.</summary>
    public WebLingoValue[] Values { get; }

    /// <summary>The function of the call it waits for, once <see cref="TryGoOn"/> has said it waits for one.</summary>
    public WebLingoValue Callee { get; protected set; }

    /// <summary>What the call runs on: <see cref="WebLingoValue.Missing"/> for a function's call, as <c>f(x)</c> has none.</summary>
    public WebLingoValue Self { get; protected set; } = WebLingoValue.Missing;

    /// <summary>Where in <see cref="Values"/> the call's arguments begin, and how many there are.</summary>
    public int FirstArgument { get; protected set; }

    public int ArgumentCount { get; protected set; }

    /// <summary>
    /// Goes on: true when it is done, its result in <paramref name="result"/>; false when it waits for a call,
    /// <paramref name="calls"/> true, or when it cannot go on, <paramref name="error"/> saying why.
    /// </summary>
    public abstract bool TryGoOn(WebLingoHeap heap, out bool calls, out WebLingoValue result, out WebLingoRuntimeError? error);

    /// <summary>What the call it waited for gave.</summary>
    public abstract void Supply(WebLingoValue returned);
}

/// <summary>
/// A built-in whose work calls the program's functions (<see cref="WebLingoCalls"/>): a VM that knows it begins its work,
/// which it then runs, where its <see cref="IWebLingoBuiltinFunction.TryInvoke"/> only fails.
/// </summary>
public interface IWebLingoCallingBuiltin : IWebLingoBuiltinFunction
{
    /// <summary>Its work, given its arguments; null, with the error, where it cannot begin.</summary>
    WebLingoCalls? Begin(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoRuntimeError? error);
}
