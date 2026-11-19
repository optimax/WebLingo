using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Process;

/// <summary>Runs local executables through a host that provides a subprocess runner.</summary>
public sealed class WebLingoProcessModule : IWebLingoNativeModule
{
    private const int DefaultTimeoutMs = 30_000;
    private const int DefaultOutputBytes = 1024 * 1024;
    private const int MaximumOutputBytes = 16 * DefaultOutputBytes;

    public void Register(IWebLingoNativeRegistry registry) => registry.Function("run", 3, 2, Run);

    private static bool Run(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (heap.Processes is not { } runner || heap.Scheduler is not { } scheduler)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IOFailure,
                "Subprocess execution is available only to the WebLingo CLI.");
            return false;
        }
        var executable = heap.Held(args[0]);
        var program = executable.Kind == WebLingoValueKind.String ? heap.GetString(executable) : "";
        if (string.IsNullOrWhiteSpace(program) || program.Contains('\0'))
        {
            error = new WebLingoArgument("run", 0, 3, "the executable", "a nonempty program name without NUL").Wrong(args[0], heap);
            return false;
        }
        if (!TryArguments(args[1], heap, out var arguments, out error)
            || !TryOptions(args[2], heap, out var timeout, out var maxBytes, out error)) return false;
        return scheduler.TryRunOffWorker(() => runner.Run(program, arguments, timeout, maxBytes), MakeResult, Failed,
            out result, out error);
    }

    private static bool TryArguments(WebLingoValue argument, WebLingoHeap heap, out string[] arguments,
        out WebLingoRuntimeError? error)
    {
        arguments = [];
        error = null;
        var value = heap.Held(argument);
        if (value.Kind == WebLingoValueKind.Null) return true;
        var expected = new WebLingoArgument("run", 1, 3, "the arguments", "an array of strings without NUL or null");
        if (value.Kind != WebLingoValueKind.Array)
        {
            error = expected.Wrong(argument, heap);
            return false;
        }
        var items = heap.GetArray(value);
        arguments = new string[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var item = heap.Held(items[index]);
            if (item.Kind == WebLingoValueKind.String)
            {
                var text = heap.GetString(item);
                if (!text.Contains('\0'))
                {
                    arguments[index] = text;
                    continue;
                }
            }
            error = expected.Wrong($"an array with an invalid argument at index {index}");
            return false;
        }
        return true;
    }

    private static bool TryOptions(WebLingoValue argument, WebLingoHeap heap, out int timeout, out int maxBytes,
        out WebLingoRuntimeError? error)
    {
        timeout = DefaultTimeoutMs;
        maxBytes = DefaultOutputBytes;
        error = null;
        var value = heap.Held(argument);
        if (value.Kind == WebLingoValueKind.Null) return true;
        var expected = new WebLingoArgument("run", 2, 3, "the options", "a map with timeoutMs and maxOutputBytes options or null");
        if (value.Kind != WebLingoValueKind.Map)
        {
            error = expected.Wrong(argument, heap);
            return false;
        }
        for (var index = 0; index < heap.MapCount(value); index++)
        {
            var (key, option) = heap.MapEntryAt(value, index);
            var name = heap.Held(key);
            var setting = name.Kind == WebLingoValueKind.String ? heap.GetString(name) : "";
            var number = heap.Held(option);
            var count = number.Kind == WebLingoValueKind.Int64 ? number.AsInt64 : -1;
            switch (setting)
            {
                case "timeoutMs" when count is >= 1 and <= int.MaxValue:
                    timeout = (int)count;
                    break;
                case "maxOutputBytes" when count is >= 0 and <= MaximumOutputBytes:
                    maxBytes = (int)count;
                    break;
                default:
                    error = expected.Wrong("an unknown option or a value outside its allowed integer range");
                    return false;
            }
        }
        return true;
    }

    private static WebLingoRuntimeError? Failed(Exception failure) =>
        failure is WebLingoProcessException process
            ? new WebLingoRuntimeError(process.TimedOut ? WebLingoDiagnosticCodes.Timeout : WebLingoDiagnosticCodes.IOFailure,
                process.Message)
            : null;

    private static bool MakeResult(WebLingoProcessResult process, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var entries = new WebLingoValue[6];
        heap.AddRoots(entries);
        try
        {
            entries[1] = WebLingoValue.FromInt64(process.ExitCode);
            return heap.TryAllocateString("exitCode", out entries[0], out error)
                && heap.TryAllocateString("stdout", out entries[2], out error)
                && heap.TryAllocateString(process.StandardOutput, out entries[3], out error)
                && heap.TryAllocateString("stderr", out entries[4], out error)
                && heap.TryAllocateString(process.StandardError, out entries[5], out error)
                && heap.TryAllocateMap(entries, 0, 3, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(entries);
        }
    }
}
