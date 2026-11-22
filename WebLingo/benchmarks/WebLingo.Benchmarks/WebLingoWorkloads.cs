namespace WebLingo.Benchmarks;

/// <summary>
/// A fixed program the benchmark runs, what it exercises, and the output it must give; its file's name, a script's
/// (<c>.wlg</c>) or a page's (<c>.wasp</c>, section 13), which the template compiler compiles.
/// </summary>
public sealed record WebLingoWorkload(string Name, string Exercises, string Source, string ExpectedOutput)
{
    public string FileName { get; init; } = Name + ".wlg";

    /// <summary>Whether it takes a folder of its own, its parameter <c>folder</c>, for the files it writes (section 3.9, <c>std:fs</c>).</summary>
    public bool UsesFolder { get; init; }
}

/// <summary>
/// The benchmark's workloads (specification, section 5.6): loops, calls, imports (a native module's functions imported by
/// name), closures, strings, collections, objects (property
/// access), async (asynchronous suspension), templates (template output) and io (I/O-bound work, in a folder of its own,
/// which the benchmark makes and removes). Each is a <c>.wlg</c> file in
/// <c>Workloads/</c>, embedded in the assembly, that <c>weblingo run</c> also runs, and each writes one result, which
/// every VM must give; or, for templates, a <c>.wasp</c> page, which <c>wasp run</c> runs, and which writes its page. A
/// file's line breaks are read as LF, whatever the checkout gave it, so that a page writes the same on every platform.
/// </summary>
public static class WebLingoWorkloads
{
    public static IReadOnlyList<WebLingoWorkload> All { get; } =
    [
        Load("loops", "nested for loops, int64 arithmetic, comparisons and if", "165666500\n"),
        Load("calls", "recursive calls of a declared function", "75025\n"),
        Load("imports", "a native module's functions, imported by name, called in a loop", "10000199985\n"),
        Load("closures", "closures made and called, sharing captured cells", "3300000\n"),
        Load("strings", "string concatenation and comparison, the heap and its collector", "20000\n"),
        Load("bytes", "bytes built from many small pieces by BytesBuilders, and read", "600300\n"),
        Load("collections", "arrays and maps: elements and entries read, written and visited with foreach", "200124998\n"),
        Load("objects", "instances made, their fields read and written, their methods called", "1666691666750000\n"),
        Load("async", "async functions suspended at await, and resumed by the scheduler", "49950000\n"),
        Load("templates", "a page's text, and values written escaped and as they stand, in a loop", TemplatesOutput(), ".wasp"),
        Load("io", "files written and read through std:fs, one at a time, each awaited, then all at once", "15780\n") with { UsesFolder = true },
    ];

    private static WebLingoWorkload Load(string name, string exercises, string expectedOutput, string extension = ".wlg")
    {
        using var stream = typeof(WebLingoWorkloads).Assembly.GetManifestResourceStream($"Workloads/{name}{extension}")
            ?? throw new InvalidOperationException($"The workload '{name}' is not embedded in the benchmark.");
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        return new WebLingoWorkload(name, exercises, source, expectedOutput) { FileName = name + extension };
    }

    /// <summary>What <c>templates.wasp</c> writes: a table of 10,000 rows, each with an escaped value and a raw one.</summary>
    private static string TemplatesOutput()
    {
        var text = new System.Text.StringBuilder("<table>\n");
        for (var i = 0; i < 10000; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"<tr><td>{i}</td><td>&lt;item &amp; {i}&gt;</td><td>{i * 2}</td></tr>\n");
        }
        return text.Append("</table>\n").ToString();
    }
}
