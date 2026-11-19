using System.Diagnostics.CodeAnalysis;
using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>Resolves compiler-generated imports and ordinary imports supplied by the host.</summary>
public static class WebLingoIntrinsicBuiltins
{
    private static readonly IWebLingoBuiltinFunction ArrayCallback = new CheckArrayCallback();

    public static bool TryResolve(IWebLingoRuntimeServices services, string name,
        [MaybeNullWhen(false)] out IWebLingoBuiltinFunction builtin)
    {
        if (name != WebLingoIntrinsicNames.CheckArrayCallback) return services.TryGetBuiltin(name, out builtin);
        builtin = ArrayCallback;
        return true;
    }

    private sealed class CheckArrayCallback : IWebLingoBuiltinFunction
    {
        public string Name => WebLingoIntrinsicNames.CheckArrayCallback;
        public int Arity => 2;

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result,
            out WebLingoRuntimeError? error)
        {
            result = WebLingoValue.Null;
            error = null;
            if (heap.IsCallable(arguments[1])) return true;

            // The compiler supplies the method name. A bytecode file can supply an arbitrary value here.
            var method = arguments[0].Kind == WebLingoValueKind.String ? heap.GetString(arguments[0]) : "Array method";
            var argument = new WebLingoArgument(method, 0, method == "reduce" ? 2 : 1, "the callback", "a function");
            error = argument.Wrong(arguments[1], heap);
            return false;
        }
    }
}
