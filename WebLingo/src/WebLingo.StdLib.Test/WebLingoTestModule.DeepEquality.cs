using WebLingo.Runtime;
using Kind = WebLingo.Runtime.WebLingoValueKind;
namespace WebLingo.StdLib.Test;

public sealed partial class WebLingoTestModule
{
    private sealed record Path(Path? Parent, string Segment)
    {
        public override string ToString()
        {
            var segments = new Stack<string>();
            for (Path? path = this; path is not null; path = path.Parent) segments.Push(path.Segment);
            return string.Concat(segments);
        }
    }

    private readonly record struct Comparison(WebLingoValue Actual, WebLingoValue Expected, Path Path);

    private static string? DeepEqual(ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap)
    {
        var seen = new HashSet<(WebLingoValue, WebLingoValue)>();
        // A new pair uses -1. A collection already visited resumes at its next child.
        var pending = new Stack<(Comparison Comparison, int NextChild)>();
        pending.Push((new Comparison(args[0], args[1], new Path(null, "$")), -1));
        while (pending.TryPop(out var step))
        {
            if (step.NextChild >= 0)
            {
                var failure = ChildAt(step.Comparison, step.NextChild, heap, out var child);
                if (failure is not null) return failure;
                if (child is { } next)
                {
                    pending.Push((step.Comparison, step.NextChild + 1));
                    pending.Push((next, -1));
                }
                continue;
            }
            var (actual, expected, path) = step.Comparison;
            if (Same(actual, expected, heap)) continue;
            actual = heap.Held(actual);
            expected = heap.Held(expected);
            if (actual.Kind != expected.Kind) return $"At {path}: {Expected(actual, expected, heap)}";
            switch (actual.Kind)
            {
                case Kind.Array:
                case Kind.Map:
                    if (seen.Add((actual, expected))) pending.Push((new Comparison(actual, expected, path), 0));
                    break;
                case Kind.Set:
                    for (var i = 0; i < heap.SetCount(expected); i++)
                    {
                        var item = heap.SetValueAt(expected, i);
                        if (!heap.TryFindInSet(actual, item, out var found, out _) || !found)
                            return $"At {path}: Expected set member {Display(item, heap)}, actual set {Display(actual, heap)}.";
                    }
                    if (heap.SetCount(actual) != heap.SetCount(expected))
                        return $"At {path}: {Expected(actual, expected, heap)}";
                    break;
                case Kind.Bytes:
                    var actualBytes = heap.GetBytes(actual);
                    var expectedBytes = heap.GetBytes(expected);
                    for (var i = 0; i < Math.Min(actualBytes.Length, expectedBytes.Length); i++)
                    {
                        if (actualBytes[i] != expectedBytes[i])
                            return $"At {path}[{i}]: Expected byte {expectedBytes[i]}, actual byte {actualBytes[i]}.";
                    }
                    if (actualBytes.Length != expectedBytes.Length)
                        return $"At {path}.length: Expected {expectedBytes.Length}, actual {actualBytes.Length}.";
                    break;
                default:
                    return $"At {path}: {Expected(actual, expected, heap)}";
            }
        }
        return null;
    }

    private static string? ChildAt(Comparison parent, int index, WebLingoHeap heap, out Comparison? child)
    {
        var (actual, expected, path) = parent;
        child = null;
        if (actual.Kind == Kind.Array)
        {
            var actualItems = heap.GetArray(actual);
            var expectedItems = heap.GetArray(expected);
            if (index < Math.Min(actualItems.Count, expectedItems.Count))
                child = new Comparison(actualItems[index], expectedItems[index], new Path(path, $"[{index}]"));
            else if (actualItems.Count != expectedItems.Count)
                return $"At {path}.length: Expected {expectedItems.Count}, actual {actualItems.Count}.";
            return null;
        }

        if (index < heap.MapCount(expected))
        {
            var (key, value) = heap.MapEntryAt(expected, index);
            var atKey = new Path(path, $"[{Display(key, heap)}]");
            if (heap.TryFindInMap(actual, key, out var foundValue, out var found, out _) && found)
                child = new Comparison(foundValue, value, atKey);
            else
                return $"At {atKey}: Expected {Display(value, heap)}, actual key missing.";
            return null;
        }
        if (heap.MapCount(actual) == heap.MapCount(expected)) return null;
        for (var i = 0; i < heap.MapCount(actual); i++)
        {
            var (key, value) = heap.MapEntryAt(actual, i);
            if (!heap.TryFindInMap(expected, key, out _, out var found, out _) || !found)
                return $"At {path}[{Display(key, heap)}]: Expected key absent, actual {Display(value, heap)}.";
        }
        return null;
    }
}
