namespace WebLingo.Runtime;

/// <summary>
/// A native module's function (specification, section 3.9): the call's arguments, exactly as many as its arity, whose
/// objects are in <paramref name="heap"/>, where any it makes go too; its result, a value or a task for asynchronous work,
/// or its runtime error.
/// </summary>
public delegate bool WebLingoNativeFunction(WebLingoValue[] arguments, WebLingoHeap heap, out WebLingoValue result,
    out WebLingoRuntimeError? error);

/// <summary>What a native module gives its exports to, when it is loaded (section 3.9).</summary>
public interface IWebLingoNativeRegistry
{
    /// <summary>
    /// An exported function: its name, a name a script can write (ASCII letters, digits and <c>_</c>, not first a digit),
    /// given once; its arity, 0 or more; and the function.
    /// </summary>
    void Function(string name, int arity, WebLingoNativeFunction function);
}

/// <summary>
/// A native module (section 3.9): on .NET, an assembly whose one public class implementing this interface, with a public
/// constructor that takes nothing, registers the module's exports when the host loads it.
/// </summary>
public interface IWebLingoNativeModule
{
    void Register(IWebLingoNativeRegistry registry);
}
