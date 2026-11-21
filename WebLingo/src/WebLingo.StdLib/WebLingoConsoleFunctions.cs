using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib;

/// <summary>
/// <c>readLine()</c> and <c>readLineSync()</c>, <c>std:console</c>'s, beside <c>write</c> (the owner's answers, 19 November
/// 2026, "Console Input", and 20 November 2026, "Console Module", A1): the console's next line, without its line break,
/// <c>null</c> at the end of the input, as a task, its reading off the worker, or at once, blocking it.
/// </summary>
public sealed class WebLingoReadLineFunction(bool synchronous) : IWebLingoBuiltinFunction
{
    public string Name => synchronous ? "readLineSync" : "readLine";
    public int Arity => 0;

    public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        WebLingoConsoleFunctions.TryRead(Name, input => input.ReadLine(), synchronous, heap, out result, out error);
}

/// <summary>
/// <c>Console.read()</c> and <c>Console.readSync()</c>, <c>std:console</c>'s (the owner's answer, 20 November 2026, "Console
/// Module, Part 2", A3): the input's next character, a string of one, <c>null</c> at its end, as a task or at once.
/// </summary>
public sealed class WebLingoReadCharacterFunction(bool synchronous) : IWebLingoBuiltinFunction
{
    public string Name => synchronous ? "readSync" : "read";
    public int Arity => 0;

    public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        WebLingoConsoleFunctions.TryRead(Name, input => input.ReadCharacter(), synchronous, heap, out result, out error);
}

/// <summary><c>prompt(text)</c>: writes the text, as <c>write</c> does, then reads a line, as <c>readLine()</c> does, a task.</summary>
public sealed class WebLingoPromptFunction(IWebLingoTextOutput? output = null) : IWebLingoBuiltinFunction
{
    public string Name => "prompt";
    public int Arity => 1;

    public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (arguments[0].Kind != WebLingoValueKind.String)
        {
            error = new WebLingoArgument("prompt", 0, 1, "the text", "a string").Wrong(arguments[0], heap);
            return false;
        }
        if (heap.ConsoleInput is null) return WebLingoConsoleFunctions.TryRead(Name, input => input.ReadLine(), false, heap, out result, out error);
        return WebLingoStandardBuiltins.TryWrite(output, heap.GetString(arguments[0]), heap, out result, out error)
            && WebLingoConsoleFunctions.TryRead(Name, input => input.ReadLine(), false, heap, out result, out error);
    }
}

/// <summary><c>readPassword()</c>: a line, not shown as it is typed where the input is a console's, a task.</summary>
public sealed class WebLingoReadPasswordFunction : IWebLingoBuiltinFunction
{
    public string Name => "readPassword";
    public int Arity => 0;

    public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        WebLingoConsoleFunctions.TryRead(Name, input => input.ReadPassword(), false, heap, out result, out error);
}

/// <summary>What the console's reading shares.</summary>
internal static class WebLingoConsoleFunctions
{
    /// <summary>
    /// A line the console gives, a string, or <c>null</c> at the end of the input: at once, or as a task, read off the
    /// worker. Without a console, an execution its host gives none, an <c>IOError</c>, WL3011, saying so.
    /// </summary>
    internal static bool TryRead(string name, Func<IWebLingoConsoleInput, string?> read, bool synchronous, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (heap.ConsoleInput is not { } input)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.IOFailure,
                $"'{name}' reads a console, and this program has none.");
            return false;
        }
        if (synchronous)
        {
            string? line;
            try
            {
                line = read(input);
            }
            catch (IOException failure)
            {
                error = Failed(name, failure);
                return false;
            }
            return Line(line, heap, out result, out error);
        }
        if (heap.Scheduler is not { } scheduler)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{name}' needs a VM that runs tasks.");
            return false;
        }
        return scheduler.TryRunOffWorker(() => read(input), Line, exception => exception is IOException failure ? Failed(name, failure) : null,
            out result, out error);
    }

    private static bool Line(string? line, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        (result, error) = (WebLingoValue.Null, null);
        return line is null || heap.TryAllocateString(line, out result, out error);
    }

    private static WebLingoRuntimeError Failed(string name, IOException failure) =>
        new(WebLingoDiagnosticCodes.IOFailure, $"'{name}' could not read the console: {failure.Message}");
}
