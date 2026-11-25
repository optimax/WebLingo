using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Log;

/// <summary>
/// <c>std:log</c>, a standard module (specification, section 6.12, "Logging"; the owner's answer, 24 November 2026,
/// "Logging for WASP Sites", A1 (c)), a native module installed with the runtime in the <c>std</c> scheme's folder as
/// <c>log.dll</c>, through the execution's log (<see cref="WebLingoHeap.Log"/>), so that the portable core writes no file
/// of its own: <c>log.debug(text)</c>, <c>log.info(text)</c>, <c>log.warn(text)</c> and <c>log.error(text)</c>, each a line
/// of its level, written where the level is at least the log's, its text kept to one line; under <c>wasp</c> the host's
/// log, for the request, or the file a day <c>wasp.config</c>'s <c>"log"</c> names, under <c>weblingo</c> the standard
/// error. The text is a string, anything else WL3001; and a host without a log writes it nowhere, a page never failing
/// for its log.
/// </summary>
public sealed class WebLingoLogModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        foreach (var level in (ReadOnlySpan<WebLingoLogLevel>)[WebLingoLogLevel.Debug, WebLingoLogLevel.Info, WebLingoLogLevel.Warn, WebLingoLogLevel.Error])
        {
            var name = WebLingoLogText.Name(level);
            registry.Function(name, 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
                Write(name, level, a, heap, out result, out error));
        }
    }

    private static bool Write(string name, WebLingoLogLevel level, ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        (result, error) = (WebLingoValue.Null, null);
        var text = heap.Held(a[0]);
        if (text.Kind != WebLingoValueKind.String)
        {
            error = new WebLingoArgument(name, 0, 1, "the text", "a string, the text to log, as $\"{value}\" makes one of a value").Wrong(text, heap);
            return false;
        }
        if (heap.Log is { } log && level >= log.Level) log.Write(level, WebLingoLogText.OneLine(heap.GetString(text)));
        return true;
    }
}
