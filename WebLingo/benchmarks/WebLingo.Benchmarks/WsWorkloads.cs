namespace WebLingo.Benchmarks;

/// <summary>A fixed program the benchmark runs, what it exercises, and the output it must give.</summary>
public sealed record WsWorkload(string Name, string Exercises, string Source, string ExpectedOutput);

/// <summary>
/// The benchmark's workloads (specification, section 5.6): loops, calls, closures, strings and collections. Each is a
/// <c>.ws</c> file in <c>Workloads/</c>, embedded in the assembly, that <c>weblingo run</c> also runs, and each writes
/// one result, which every VM must give.
/// </summary>
public static class WsWorkloads
{
    public static IReadOnlyList<WsWorkload> All { get; } =
    [
        Load("loops", "nested for loops, int64 arithmetic, comparisons and if", "165666500\n"),
        Load("calls", "recursive calls of a declared function", "75025\n"),
        Load("closures", "closures made and called, sharing captured cells", "3300000\n"),
        Load("strings", "string concatenation and comparison, the heap and its collector", "20000\n"),
        Load("collections", "arrays and maps: elements and entries read, written and visited with foreach", "200124998\n"),
    ];

    private static WsWorkload Load(string name, string exercises, string expectedOutput)
    {
        using var stream = typeof(WsWorkloads).Assembly.GetManifestResourceStream($"Workloads/{name}.ws")
            ?? throw new InvalidOperationException($"The workload '{name}' is not embedded in the benchmark.");
        using var reader = new StreamReader(stream);
        return new WsWorkload(name, exercises, reader.ReadToEnd(), expectedOutput);
    }
}
