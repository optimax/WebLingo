using System.Text;
using WebLingo.Core;
using WebLingo.Runtime;
using Kind = WebLingo.Runtime.WebLingoValueKind;
namespace WebLingo.StdLib.Test;

public sealed partial class WebLingoTestModule
{
    private static bool CheckAction(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = !heap.IsCallable(args[0]) ? AssertionFailure("Expected a callable action.")
            : args[1].Kind != Kind.Class ? AssertionFailure("Expected an error class.") : null;
        return error is null;
    }

    private static bool MatchError(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = args[0];
        error = WebLingoExceptions.IsError(result, args[1], heap)
            ? null : AssertionFailure($"Expected {heap.ClassName(args[1])}, actual {DescribeError(result, heap)}.");
        return error is null;
    }

    private static bool DescribeError(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error) =>
        heap.TryAllocateString(DescribeError(args[0], heap), out result, out error);

    private static string DescribeError(WebLingoValue value, WebLingoHeap heap)
    {
        if (value.Kind != Kind.Instance || !heap.TryGetField(value, "message", out var message)) return Display(value, heap);
        var name = heap.ClassName(heap.ClassOf(value));
        var code = heap.TryGetField(value, "code", out var field) && field.Kind == Kind.String ? $" {heap.GetString(field)}" : "";
        return $"{name}{code}: {(message.Kind == Kind.String ? heap.GetString(message) : Display(message, heap))}";
    }

    private static bool IsAssertion(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.FromBoolean(args[0].Kind == Kind.Instance && heap.TryGetField(args[0], "code", out var code)
            && code.Kind == Kind.String && heap.GetString(code) == WebLingoDiagnosticCodes.TestAssertionFailed);
        error = null;
        return true;
    }

    private static bool Fail(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        error = AssertionFailure(args[0].Kind == Kind.String ? heap.GetString(args[0]) : Display(args[0], heap));
        return false;
    }

    private static bool FailAll(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        if (args[0].Kind != Kind.Array || args[1].Kind != Kind.Boolean)
        {
            error = AssertionFailure("Expected collected failures and whether the check block was interrupted.");
            return false;
        }
        var failures = heap.GetArray(args[0]);
        var report = new StringBuilder($"{failures.Count} assertion failure(s) in checkAll.");
        foreach (var failure in failures)
        {
            report.Append('\n').Append("  ");
            if (failure.Kind == Kind.Instance && heap.TryGetField(failure, "sourceFile", out var file) && file.Kind == Kind.String)
            {
                report.Append(heap.GetString(file));
                if (heap.TryGetField(failure, "sourceLine", out var line) && line.Kind == Kind.Int64)
                    report.Append('(').Append(line.AsInt64).Append(')');
                report.Append(": ");
            }
            report.Append(DescribeError(failure, heap));
        }
        if (args[1].AsBoolean) report.Append('\n').Append("  Unexpected error: ").Append(DescribeError(args[2], heap));
        error = AssertionFailure(report.ToString());
        return false;
    }
}
