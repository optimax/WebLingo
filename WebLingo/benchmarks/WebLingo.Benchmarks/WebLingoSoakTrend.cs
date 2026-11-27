namespace WebLingo.Benchmarks;

/// <summary>One of a soak's samples: when it was taken, how many reloads there had been by then, and the server's process.</summary>
public sealed record WebLingoSoakSample(TimeSpan Elapsed, long Reloads, WebLingoServerSample Server);

/// <summary>
/// What a soak's samples say (specification, section 9, "M0.22 parts", part 3): after its warm-up, the first quarter of its
/// samples, each measure's growth for each reload, or for each hour where nothing reloaded, by least squares; and whether
/// it keeps rising, a failure: the samples after the warm-up in four quarters, each quarter's lowest above the one before,
/// so that it rose all the way, and the last's above the first's by more than the measure's margin, so that it rose by
/// more than its noise. Its lowest, since a process's memory is at its lowest just after its collector has run, which is
/// what a leak raises.
/// </summary>
public static class WebLingoSoakTrend
{
    /// <summary>The fewest samples after the warm-up for a verdict: two in each quarter.</summary>
    public const int Fewest = 8;

    /// <summary>A measure of the server's: its name, how it is read from a sample, and its margin, a fraction and a least.</summary>
    public sealed record Measure(string Name, Func<WebLingoServerSample, double?> Read, double Fraction, double Least, string Unit);

    /// <summary>
    /// The measures a soak looks at: private bytes, the .NET heap, where its counters are read (<see cref="WebLingoServerCounters"/>),
    /// handles and threads.
    /// </summary>
    public static IReadOnlyList<Measure> Measures { get; } =
    [
        new("private bytes", server => server.PrivateBytes, 0.05, 1024 * 1024, "bytes"),
        new("GC heap", server => server.GcHeap, 0.05, 1024 * 1024, "bytes"),
        new("handles", server => server.Handles, 0.05, 20, "handles"),
        new("threads", server => server.Threads, 0.05, 4, "threads")
    ];

    /// <summary>Whether every sample of <paramref name="samples"/> has <paramref name="measure"/>, which a growth and a verdict need.</summary>
    public static bool Read(IReadOnlyList<WebLingoSoakSample> samples, Measure measure) =>
        samples.All(sample => measure.Read(sample.Server) is not null);

    /// <summary>The samples after the warm-up: all but the first quarter of them, the first one at least.</summary>
    public static IReadOnlyList<WebLingoSoakSample> AfterWarmUp(IReadOnlyList<WebLingoSoakSample> samples) =>
        samples.Skip(Math.Max(1, samples.Count / 4)).ToList();

    /// <summary>
    /// A measure's growth for each reload, the slope of its least-squares line against the reloads; where the samples span
    /// no reload, for each hour, against their time. Null for fewer than two samples.
    /// </summary>
    public static double? Growth(IReadOnlyList<WebLingoSoakSample> samples, Measure measure, out bool perReload)
    {
        perReload = samples.Count > 0 && samples[^1].Reloads > samples[0].Reloads;
        if (samples.Count < 2 || !Read(samples, measure)) return null;
        var byReload = perReload;
        var xs = samples.Select(sample => byReload ? sample.Reloads : sample.Elapsed.TotalHours).ToArray();
        var ys = samples.Select(sample => measure.Read(sample.Server)!.Value).ToArray();
        var (meanX, meanY) = (xs.Average(), ys.Average());
        double across = 0, spread = 0;
        for (var i = 0; i < xs.Length; i++)
        {
            across += (xs[i] - meanX) * (ys[i] - meanY);
            spread += (xs[i] - meanX) * (xs[i] - meanX);
        }
        return spread == 0 ? null : across / spread;
    }

    /// <summary>
    /// Whether a measure keeps rising across <paramref name="samples"/>, those after the warm-up: null where there are fewer
    /// than <see cref="Fewest"/>, too few to say.
    /// </summary>
    public static bool? KeepsRising(IReadOnlyList<WebLingoSoakSample> samples, Measure measure)
    {
        if (samples.Count < Fewest || !Read(samples, measure)) return null;
        var lowest = new double[4];
        for (var quarter = 0; quarter < 4; quarter++)
        {
            var (from, to) = (samples.Count * quarter / 4, samples.Count * (quarter + 1) / 4);
            lowest[quarter] = samples.Skip(from).Take(to - from).Min(sample => measure.Read(sample.Server)!.Value);
        }
        for (var quarter = 1; quarter < 4; quarter++)
        {
            if (lowest[quarter] <= lowest[quarter - 1]) return false;
        }
        return lowest[3] - lowest[0] > Math.Max(lowest[0] * measure.Fraction, measure.Least);
    }
}
