using System.Diagnostics.CodeAnalysis;
using WebLingo.Core;
namespace WebLingo.Runtime;

/// <summary>Resolves compiler-generated imports and ordinary imports supplied by the host.</summary>
public static class WebLingoIntrinsicBuiltins
{
    private static readonly IWebLingoBuiltinFunction ArrayCallback = new CheckArrayCallback();
    private static readonly IWebLingoBuiltinFunction Sort = new SortArray();

    public static bool TryResolve(IWebLingoRuntimeServices services, string name,
        [MaybeNullWhen(false)] out IWebLingoBuiltinFunction builtin)
    {
        builtin = name switch
        {
            WebLingoIntrinsicNames.CheckArrayCallback => ArrayCallback,
            WebLingoIntrinsicNames.SortArray => Sort,
            _ => null
        };
        return builtin is not null || services.TryGetBuiltin(name, out builtin);
    }

    /// <summary>The error of an array method given a callback that is not a function, by the method's name.</summary>
    private static WebLingoRuntimeError CallbackError(string method, WebLingoValue callback, WebLingoHeap heap)
    {
        var argument = new WebLingoArgument(method, 0, method == "reduce" ? 2 : 1, "the callback", "a function");
        return argument.Wrong(callback, heap);
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
            error = CallbackError(method, arguments[1], heap);
            return false;
        }
    }

    /// <summary>
    /// <c>sortArray(array, compare)</c>, <c>sort</c>'s (specification, section 3.6, "Array methods"): a new array of its
    /// elements, in order, sorted by <see cref="WebLingoSort"/>, which calls the comparison, if there is one, through its VM.
    /// </summary>
    private sealed class SortArray : IWebLingoCallingBuiltin
    {
        public string Name => WebLingoIntrinsicNames.SortArray;
        public int Arity => 2;

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result,
            out WebLingoRuntimeError? error)
        {
            // Only a VM that runs its work sorts.
            result = WebLingoValue.Null;
            error = null;
            return false;
        }

        public WebLingoCalls? Begin(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoRuntimeError? error)
        {
            error = null;
            var items = heap.Held(arguments[0]);
            if (items.Kind != WebLingoValueKind.Array)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'sort' sorts an array, not {WebLingoValueFormatter.Describe(arguments[0], heap)}.");
                return null;
            }
            var compare = arguments[1];
            if (compare.Kind != WebLingoValueKind.Null && !heap.IsCallable(compare))
            {
                error = CallbackError("sort", compare, heap);
                return null;
            }
            return new WebLingoSort(heap.GetArray(items), compare);
        }
    }
}
