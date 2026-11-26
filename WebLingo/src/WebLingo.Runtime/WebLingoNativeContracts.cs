namespace WebLingo.Runtime;

/// <summary>
/// A native module's function (specification, section 3.9): the call's arguments, exactly as many as its arity, whose
/// objects are in <paramref name="heap"/>, where any it makes go too; its result, a value or a task for asynchronous work,
/// or its runtime error.
/// </summary>
public delegate bool WebLingoNativeFunction(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result,
    out WebLingoRuntimeError? error);

/// <summary>What a native module gives its exports to, when it is loaded (section 3.9).</summary>
public interface IWebLingoNativeRegistry
{
    /// <summary>
    /// An exported function: its name, a name a script can write (ASCII letters, digits and <c>_</c>, not first a digit),
    /// given once; its arity, 0 or more; and the function.
    /// </summary>
    void Function(string name, int arity, WebLingoNativeFunction function);

    /// <summary>
    /// An exported function whose last <paramref name="optional"/> parameters a call may leave out, each then <c>null</c>
    /// to the function, which is always given <paramref name="arity"/> arguments (section 3.9).
    /// </summary>
    void Function(string name, int arity, int optional, WebLingoNativeFunction function);

    /// <summary>
    /// An exported function that writes its arguments as text, as <c>write</c> does: a VM gives it an argument's text, as a
    /// string, when the argument holds an instance whose class overrides <c>toString()</c>, having run that method
    /// (<see cref="IWebLingoTextBuiltin"/>, section 3.7).
    /// </summary>
    void TextFunction(string name, int arity, WebLingoNativeFunction function);

    /// <summary>
    /// An exported function whose last parameter is a rest parameter, as a function written in WebLingo has one,
    /// <c>function f(a, ...rest)</c> (section 3.5): a call gives it at least <paramref name="arity"/> - 1 arguments, and the
    /// function is given those, then an array of the others, <c>[]</c> where there are none, its last argument.
    /// </summary>
    void RestFunction(string name, int arity, WebLingoNativeFunction function);

    /// <summary>
    /// An exported constant, read as its module's member, <c>math.PI</c>, which no importer may assign (section 3.9): a
    /// value that needs no heap, <c>null</c>, a Boolean, an integer or a number.
    /// </summary>
    void Constant(string name, WebLingoValue value);

    /// <summary>
    /// An exported value made for each program that imports it, where the program begins, after its module's classes are
    /// made: what <paramref name="make"/>, given no argument, gives in that program's heap, an instance of a class the module
    /// makes instances of among what it may give; read as its module's member, which no importer may assign, as a constant
    /// is (section 9, "M0.26 parts", part 2: <c>std:workers</c>' <c>parent</c>, its worker's parent where the program runs as
    /// a worker, <c>null</c> otherwise).
    /// </summary>
    void Value(string name, WebLingoNativeFunction make);

    /// <summary>
    /// An exported class, by a name a script can write, given once, and not a function's too: its members are declared on
    /// what this gives (<see cref="WebLingoNativeClass"/>), whose host objects its functions reach through it.
    /// </summary>
    WebLingoNativeClass Class(string name);
}

/// <summary>
/// A native module (section 3.9): on .NET, an assembly whose one public class implementing this interface, with a public
/// constructor that takes nothing, registers the module's exports when the host loads it.
/// </summary>
public interface IWebLingoNativeModule
{
    void Register(IWebLingoNativeRegistry registry);
}
