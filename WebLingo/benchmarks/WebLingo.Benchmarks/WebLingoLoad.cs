using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
namespace WebLingo.Benchmarks;

/// <summary>A page of the load site: its name, how it is asked for, and whether an answer is the page's.</summary>
public sealed record WebLingoLoadPage(string Name, Func<HttpRequestMessage> Request, Func<string, bool> Answered);

/// <summary>
/// What one page gave at one number of clients: the requests that ended within the time measured, a second's of them, the
/// latencies and the errors of every request that ended after the warm-up, those the time's end left to end after it
/// among them, and its server's memory.
/// </summary>
public sealed record WebLingoLoadResult(string Page, int Clients, int Requests, int Errors, double PerSecond, TimeSpan Median,
    TimeSpan Slowest95, TimeSpan Slowest99, string? FirstError, WebLingoServerSample? Server);

/// <summary>The server's process as sampled: its memory, its handles and its threads.</summary>
public sealed record WebLingoServerSample(long WorkingSet, long PrivateBytes, int Handles, int Threads, long? GcHeap = null);

/// <summary>
/// WASP under load (specification, section 5.6, "Under load", part 1): <c>WebLingo.Benchmarks --load</c>. The load site,
/// its <c>LoadSite</c> folder, copied beside a SQLite database of its own, is served by <c>wasp serve</c>, or by
/// <c>wasp fastcgi</c> behind nginx, unpacked from the repository's <c>WebServers</c> folder as <c>WebServerTests</c>
/// unpacks it, or is found at a URL, as IIS serves it; clients ask for its pages at once, each with its own connection and
/// its own cookies, as browsers do, every answer checked, until the time measured ends. It writes each page's throughput and
/// latency at each number of clients, and its server's memory after; with <c>--reload</c>, a page replaced every quarter
/// of a second while clients ask for it, every answer one version's whole; with <c>--soak</c>, the pages asked for in turn
/// for hours, the server's memory, handles and threads sampled as it goes, and, with <c>--reload-every</c>, a page replaced
/// every few seconds, which reloads the site, the growth of each for each reload given after the warm-up, one that keeps
/// rising a failure (specification, section 9, "M0.22 parts", part 3; <see cref="WebLingoSoakTrend"/>); with
/// <c>--fair</c>, a page that computes for long asked for by a client for each of the server's workers while others ask for
/// <c>hello</c>, under each quantum given (section 9, "M0.24 parts", part 3); with <c>--prepare</c>, the load site made in
/// a folder for IIS to serve, and nothing measured (section 5.6, "Under load, parts", part 3). Exit codes: 0 every answer
/// right, 1 one wrong or failed, or a soak's measure rising, 2 a usage error.
/// </summary>
public static class WebLingoLoad
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int UsageError = 2;

    private static readonly byte[] Upload = MakeUpload();
    private static readonly long ComputedSum = ComputeSum();

    /// <summary><c>slow.wasp</c>'s sum, 20,000,000 numbers' remainders by 7, which <c>--fair</c> asks for beside <c>hello</c>.</summary>
    private static readonly long SlowSum = SlowSumOf(20_000_000);

    /// <summary>The page that computes for long, a second or so, which <c>--fair</c> asks for; not among the pages measured otherwise.</summary>
    private static readonly WebLingoLoadPage Slow = new("slow", () => Get("/slow"), body => Has(body, $"<p>Slow: {SlowSum}.</p>"));
    private static readonly Regex Visits = new(@"<p>Visits: (\d+)\.</p>", RegexOptions.CultureInvariant);

    /// <summary>The load site's pages, each a kind of work a site does (the specification, section 5.6, "Under load").</summary>
    public static IReadOnlyList<WebLingoLoadPage> Pages { get; } =
    [
        new("hello", () => Get("/hello"), body => Has(body, "<p>Hello, load.</p>")),
        new("compute", () => Get("/compute"), body => Has(body, $"<p>Sum: {ComputedSum}.</p>")),
        new("sqlite", () => Get("/sqlite"), body => Count(body, "<li>") == 10 && Has(body, "<li>41 Tea 41</li>")),
        new("sqlserver", () => Get("/sqlserver"), body => Count(body, "<li>") == 10),
        new("large", () => Get("/large"), body => body.Length > 900_000 && Has(body[^20..], "<p>End.</p>")),
        new("session", () => Get("/session"), body => Visits.IsMatch(body)),
        new("upload", Post, body => Has(body, $"<p>Received: {Upload.Length} {Upload.Length}.</p>"))
    ];

    /// <summary>
    /// WebLingo Forum's pages as a guest reads them (<c>Examples.wasp/WebLingoForum</c>; <c>Documents/weblingo-forum.md</c>,
    /// section 6, part 10), <c>--site forum</c>, at <c>--url</c>, where the forum is served, its database made by its
    /// <c>setup.wlg</c>: its front page, its first board, its first topic, and a search.
    /// </summary>
    public static IReadOnlyList<WebLingoLoadPage> ForumPages { get; } =
    [
        new("home", () => Get("/"), body => Has(body, "<title>Boards · ")),
        new("board", () => Get("/board?id=1"), body => Has(body, "Welcome to the forum")),
        new("topic", () => Get("/topic?id=1"), body => Has(body, "first post. Introduce yourself here")),
        new("search", () => Get("/search?q=forum"), body => Has(body, "<p class=\"note\">"))
    ];

    /// <summary>The site's folder beside the benchmark, copied for each run.</summary>
    public static string SiteFolder { get; } = Path.Combine(AppContext.BaseDirectory, "LoadSite");

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is ["--prepare", var folder]) return Prepare(folder, output, error);
        var settings = new Settings();
        for (var i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length || !settings.TryTake(args[i], args[i + 1])) return Usage(error);
            i++;
        }
        if (!settings.IsWhole) return Usage(error);
        try
        {
            return RunAsync(settings, output, error).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException exception)
        {
            output.Flush();
            error.WriteLine(exception.Message);
            return Failed;
        }
    }

    /// <summary>What the command line asks for, each option with its value.</summary>
    private sealed class Settings
    {
        public string Host { get; private set; } = "serve";
        public string Site { get; private set; } = "load";
        public Uri? Url { get; private set; }

        /// <summary>The folder <c>--prepare</c> made the site at <see cref="Url"/> in, whose page <c>--reload</c> replaces.</summary>
        public string? Folder { get; private set; }
        public List<string> Names { get; } = [];
        public int[] Clients { get; private set; } = [1, 4, 16, 64, 256];
        public int Seconds { get; private set; } = 10;
        public int WarmUp { get; private set; } = 3;
        public int? Reload { get; private set; }
        public double? Soak { get; private set; }

        /// <summary>The quanta <c>--fair</c> tries, null for none, each in turn; null where it is not asked for.</summary>
        public List<long?>? Fair { get; private set; }
        public int Sample { get; private set; } = 10;
        public int? ReloadEvery { get; private set; }

        public IReadOnlyList<WebLingoLoadPage> All => Site == "forum" ? ForumPages : WebLingoLoad.Pages;

        public IReadOnlyList<WebLingoLoadPage> Pages => Names.Count == 0 ? All : All.Where(page => Names.Contains(page.Name)).ToList();

        /// <summary>
        /// Whether what it was given goes together: the forum's pages at a URL, pages its site has, and a soak's reloads on a
        /// site the benchmark serves itself, whose page it replaces; and a folder only for a site at a URL.
        /// </summary>
        public bool IsWhole => (Site != "forum" || Url is not null) && Names.All(name => All.Any(page => page.Name == name))
                               && (ReloadEvery is null || (Soak is not null && Url is null)) && (Folder is null || Url is not null);

        public bool TryTake(string option, string value)
        {
            switch (option)
            {
                case "--host" when value is "serve" or "standalone" or "aspnetcore" or "fastcgi":
                    Host = value;
                    return true;
                case "--url" when Uri.TryCreate(value, UriKind.Absolute, out var given) && given.Scheme is "http" or "https":
                    (Host, Url) = ("url", given);
                    return true;
                case "--folder" when Directory.Exists(Path.Combine(value, "site")):
                    Folder = Path.GetFullPath(value);
                    return true;
                case "--site" when value is "load" or "forum":
                    Site = value;
                    return true;
                case "--page" when WebLingoLoad.Pages.Concat(ForumPages).Any(page => page.Name == value):
                    Names.Add(value);
                    return true;
                case "--clients" when TryCounts(value, out var counts):
                    Clients = counts;
                    return true;
                case "--seconds" when TryCount(value, 1, out var seconds):
                    Seconds = seconds;
                    return true;
                case "--warm-up" when TryCount(value, 0, out var warmUp):
                    WarmUp = warmUp;
                    return true;
                case "--sample" when TryCount(value, 1, out var sample):
                    Sample = sample;
                    return true;
                case "--reload" when TryCount(value, 1, out var reload):
                    Reload = reload;
                    return true;
                case "--soak" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) && hours > 0:
                    Soak = hours;
                    return true;
                case "--fair" when TryQuanta(value, out var quanta):
                    Fair = quanta;
                    return true;
                case "--reload-every" when TryCount(value, 1, out var every):
                    ReloadEvery = every;
                    return true;
                default:
                    return false;
            }
        }
    }

    private static async Task<int> RunAsync(Settings settings, TextWriter output, TextWriter error)
    {
        if (settings.Fair is { } fair) return await Fair(fair, settings, output);
        await using var site = settings.Url is { } url ? WebLingoLoadSite.At(url, settings.Folder) : await WebLingoLoadSite.Start(settings.Host);
        output.WriteLine($"WASP under load: {site.Description}.");
        output.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription.Trim()}, {Environment.ProcessorCount} "
                         + "logical processors, the clients on the same machine.");
        output.WriteLine();
        if (settings.Reload is { } reload) return await Reload(site, settings.Clients.Max(), TimeSpan.FromSeconds(reload), output);
        if (settings.Soak is { } hours)
        {
            var sample = TimeSpan.FromMinutes(settings.Sample);
            var every = settings.ReloadEvery is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
            return await Soak(site, settings.Pages, settings.Clients.Max(), TimeSpan.FromHours(hours), sample, every, output);
        }

        var (time, warmUp) = (TimeSpan.FromSeconds(settings.Seconds), TimeSpan.FromSeconds(settings.WarmUp));
        output.WriteLine($"Each page asked for by that many clients at once, for {settings.Seconds} s after {settings.WarmUp} s.");
        output.WriteLine();
        output.WriteLine("| Page | Clients | Requests/s | Median | 95% under | 99% under | Errors | Server's working set | Handles |");
        output.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        var wrong = 0;
        foreach (var page in settings.Pages)
        {
            if (await Fails(site, page) is { } failure)
            {
                output.WriteLine($"| {page.Name} | – | – | – | – | – | not answered: {failure} | – | – |");
                continue;
            }
            foreach (var count in settings.Clients)
            {
                var result = await Measure(site, page, count, warmUp, time);
                wrong += result.Errors;
                var errors = result.Errors + (result.FirstError is null ? "" : ": " + result.FirstError);
                var handles = result.Server?.Handles.ToString(CultureInfo.InvariantCulture) ?? "–";
                output.WriteLine($"| {page.Name} | {count} | {Number(result.PerSecond)} | {Milliseconds(result.Median)} | "
                                 + $"{Milliseconds(result.Slowest95)} | {Milliseconds(result.Slowest99)} | {errors} | "
                                 + $"{Megabytes(result.Server?.WorkingSet)} | {handles} |");
                output.Flush();
            }
        }
        if (wrong > 0) error.WriteLine($"{wrong} answers were wrong or failed.");
        return wrong == 0 ? Success : Failed;
    }

    /// <summary>Why one request for the page failed, or null where it was answered: a page the site cannot serve here.</summary>
    private static async Task<string?> Fails(WebLingoLoadSite site, WebLingoLoadPage page)
    {
        using var client = site.Client();
        return (await Answer(client, page)).Failure;
    }

    /// <summary>The page asked for by <paramref name="count"/> clients at once, each in turn, for the time given.</summary>
    public static async Task<WebLingoLoadResult> Measure(WebLingoLoadSite site, WebLingoLoadPage page, int count, TimeSpan warmUp,
        TimeSpan time)
    {
        var from = Stopwatch.GetTimestamp() + Ticks(warmUp);
        var until = from + Ticks(time);
        var tallies = Enumerable.Range(0, count).Select(_ => new Tally()).ToArray();
        await Task.WhenAll(tallies.Select(tally => Ask(site, page, from, until, tally)));
        var latencies = tallies.SelectMany(tally => tally.Latencies).Order().ToArray();
        var errors = tallies.Sum(tally => tally.Errors);
        var within = tallies.Sum(tally => tally.Within);
        var first = tallies.Select(tally => tally.FirstError).FirstOrDefault(failure => failure is not null);
        return new WebLingoLoadResult(page.Name, count, within, errors, within / time.TotalSeconds,
            Percentile(latencies, 0.5), Percentile(latencies, 0.95), Percentile(latencies, 0.99), first, site.Sample());
    }

    /// <summary>
    /// One client's requests, one after another, until <paramref name="until"/>: each that ends after <paramref name="from"/>
    /// counted, its latency its whole, one begun in the warm-up that ends after it among them, so that a slow page's are not
    /// lost; one that ends by <paramref name="until"/> counted as within the time, for the throughput.
    /// </summary>
    private static async Task Ask(WebLingoLoadSite site, WebLingoLoadPage page, long from, long until, Tally tally)
    {
        using var client = site.Client();
        while (Stopwatch.GetTimestamp() < until)
        {
            var sent = Stopwatch.GetTimestamp();
            var (ok, failure) = await Answer(client, page);
            var ended = Stopwatch.GetTimestamp();
            if (ended < from) continue;
            tally.Count(ended - sent, ok, failure, within: ended <= until);
        }
    }

    private static async Task<(bool Ok, string? Failure)> Answer(HttpClient client, WebLingoLoadPage page)
    {
        try
        {
            using var response = await client.SendAsync(page.Request());
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.OK) return (false, $"{(int)response.StatusCode} {response.ReasonPhrase}");
            if (page.Answered(body)) return (true, null);
            var shown = body.Length > 120 ? body[..120] + "…" : body;
            return (false, $"an answer that is not the page's, {shown.ReplaceLineEndings(" ")}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return (false, exception.Message);
        }
    }

    /// <summary>
    /// The page <c>hello</c> asked for while it is replaced, whole, every quarter of a second, by the other of two versions,
    /// as a deployment replaces a file: each answer must be one version's, never a mixture or an error.
    /// </summary>
    private static async Task<int> Reload(WebLingoLoadSite site, int count, TimeSpan time, TextWriter output)
    {
        var page = Hellos;
        var until = Stopwatch.GetTimestamp() + Ticks(time);
        var (replaced, refused) = (0, 0);
        var replacing = Task.Run(async () =>
        {
            while (Stopwatch.GetTimestamp() < until)
            {
                await Task.Delay(250);
                refused += await site.Replace("hello.wasp", Hello(++replaced));
            }
        });
        var tallies = Enumerable.Range(0, count).Select(_ => new Tally()).ToArray();
        await Task.WhenAll(tallies.Select(tally => Ask(site, page, 0, until, tally)));
        await replacing;
        var latencies = tallies.SelectMany(tally => tally.Latencies).Order().ToArray();
        var errors = tallies.Sum(tally => tally.Errors);
        var first = tallies.Select(tally => tally.FirstError).FirstOrDefault(failure => failure is not null);
        output.WriteLine($"`hello.wasp` replaced {replaced} times in {time.TotalSeconds:0} s while {count} clients asked for it, "
                         + $"refused {refused} times while the server read it, and tried again: {latencies.Length:#,0} requests, {errors} errors{(first is null ? "" : ", the first " + first)}, a median of "
                         + $"{Milliseconds(Percentile(latencies, 0.5))}, 99% under {Milliseconds(Percentile(latencies, 0.99))}; the "
                         + $"server's working set after, {Megabytes(site.Sample()?.WorkingSet)}.");
        return errors == 0 ? Success : Failed;
    }

    /// <summary>The two versions of <c>hello.wasp</c> a reload replaces it with, in turn.</summary>
    private static readonly string[] HelloVersions = ["<p>Hello, load.</p>", "<p>Hello again, load.</p>"];

    /// <summary><c>hello</c> as either of its versions answers it, while it is replaced.</summary>
    private static WebLingoLoadPage Hellos { get; } =
        new("hello", () => Get("/hello"), body => HelloVersions.Any(version => Has(body, version)));

    /// <summary><c>hello.wasp</c>'s text for its <paramref name="version"/>th replacement, the other of its two versions each time.</summary>
    private static string Hello(long version) =>
        "<% param request; %>" + HelloVersions[version % 2].Replace("load", "<%= request.query?[\"name\"] ?? \"load\" %>", StringComparison.Ordinal)
        + "\n";

    /// <summary>
    /// The pages asked for in turn by <paramref name="count"/> clients for <paramref name="time"/>, a row each sample; with
    /// <paramref name="reloadEvery"/>, <c>hello.wasp</c> replaced that often, as a deployment replaces it, which reloads the
    /// site; then what the samples after the warm-up say (<see cref="WebLingoSoakTrend"/>), a measure that keeps rising a
    /// failure.
    /// </summary>
    private static async Task<int> Soak(WebLingoLoadSite site, IReadOnlyList<WebLingoLoadPage> pages, int count, TimeSpan time,
        TimeSpan sample, TimeSpan? reloadEvery, TextWriter output)
    {
        // While it is replaced, either of hello's versions is its answer.
        if (reloadEvery is not null) pages = [.. pages.Select(page => page.Name == "hello" ? Hellos : page)];
        var answered = new List<WebLingoLoadPage>();
        foreach (var page in pages)
        {
            if (await Fails(site, page) is { } failure) output.WriteLine($"{page.Name} left out: {failure}.");
            else answered.Add(page);
        }
        output.WriteLine($"{string.Join(", ", answered.Select(page => page.Name))}, asked for in turn by {count} clients for "
                         + $"{time.TotalHours:0.#} h" + (reloadEvery is { } each
                             ? $", `hello.wasp` replaced every {each.TotalSeconds:0} s, which reloads the site." : "."));
        output.WriteLine();
        // The server's .NET heap, read from outside it, its System.Runtime counters (WebLingoServerCounters).
        // Its counters every 10 seconds at most, so that the first sample has them.
        if (site.WatchCounters(Math.Clamp((int)(sample.TotalSeconds / 6), 1, 10)) is { } unread)
        {
            output.WriteLine($"The server's .NET heap is not read: {unread}");
            output.WriteLine();
        }
        output.WriteLine("| Elapsed | Requests | Requests/s | Median | 99% under | Errors | Reloads | Working set | Private bytes "
                         + "| GC heap | Handles | Threads |");
        output.WriteLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        var started = Stopwatch.GetTimestamp();
        var until = started + Ticks(time);
        long reloads = 0;
        var refused = 0;
        var reloading = reloadEvery is not { } every ? Task.CompletedTask : Task.Run(async () =>
        {
            while (Stopwatch.GetTimestamp() + Ticks(every) < until)
            {
                await Task.Delay(every);
                refused += await site.Replace("hello.wasp", Hello(Interlocked.Read(ref reloads) + 1));
                Interlocked.Increment(ref reloads);
            }
        });
        var samples = new List<WebLingoSoakSample>();
        var tallies = Enumerable.Range(0, count).Select(_ => new Tally()).ToArray();
        var asking = Task.WhenAll(tallies.Select((tally, i) => Task.Run(async () =>
        {
            using var client = site.Client();
            for (var next = i; Stopwatch.GetTimestamp() < until; next++)
            {
                var sent = Stopwatch.GetTimestamp();
                var (ok, failure) = await Answer(client, answered[next % answered.Count]);
                tally.Count(Stopwatch.GetTimestamp() - sent, ok, failure);
            }
        })));
        long requests = 0, errors = 0;
        var last = started;
        while (!asking.IsCompleted)
        {
            await Task.WhenAny(asking, Task.Delay(sample));
            var now = Stopwatch.GetTimestamp();
            var latencies = new List<long>();
            foreach (var tally in tallies) errors += tally.Take(latencies);
            requests += latencies.Count;
            var sorted = latencies.Order().ToArray();
            var server = site.Sample();
            var elapsed = TimeSpan.FromSeconds((now - started) / (double)Stopwatch.Frequency);
            var rate = sorted.Length / ((now - last) / (double)Stopwatch.Frequency);
            var reloaded = Interlocked.Read(ref reloads);
            if (server is not null) samples.Add(new WebLingoSoakSample(elapsed, reloaded, server));
            output.WriteLine($"| {elapsed:hh\\:mm} | {requests:#,0} | {Number(rate)} | "
                             + $"{Milliseconds(Percentile(sorted, 0.5))} | {Milliseconds(Percentile(sorted, 0.99))} | {errors} | "
                             + $"{reloaded:#,0} | {Megabytes(server?.WorkingSet)} | {Megabytes(server?.PrivateBytes)} | "
                             + $"{Megabytes(server?.GcHeap)} | {Text(server?.Handles)} | {Text(server?.Threads)} |");
            output.Flush();
            last = now;
        }
        await asking;
        await reloading;
        var first = tallies.Select(tally => tally.FirstError).FirstOrDefault(failure => failure is not null);
        if (first is not null) output.WriteLine($"The first error: {first}.");
        if (reloadEvery is not null) output.WriteLine($"`hello.wasp` replaced {reloads:#,0} times, refused {refused} times while the server read it, and tried again.");
        var rising = Verdict(samples, output);
        return errors == 0 && !rising ? Success : Failed;
    }

    /// <summary>
    /// What a soak's samples say after its warm-up (<see cref="WebLingoSoakTrend"/>): each measure's growth for each reload, or
    /// each hour, and which keep rising; true where one does.
    /// </summary>
    private static bool Verdict(IReadOnlyList<WebLingoSoakSample> samples, TextWriter output)
    {
        if (samples.Count == 0)
        {
            output.WriteLine("The server's process was not sampled: its growth is not known.");
            return false;
        }
        var after = WebLingoSoakTrend.AfterWarmUp(samples);
        var growths = new List<string>();
        var rising = new List<string>();
        var unread = new List<string>();
        var unsure = false;
        foreach (var measure in WebLingoSoakTrend.Measures)
        {
            // A measure some sample lacks, the .NET heap where its counters could not be read, judged not at all.
            if (!WebLingoSoakTrend.Read(after, measure))
            {
                unread.Add(measure.Name);
                continue;
            }
            if (WebLingoSoakTrend.Growth(after, measure, out var perReload) is { } growth)
            {
                var shown = measure.Unit == "bytes"
                    ? (growth / 1024).ToString("+#,0.0;-#,0.0;0.0", CultureInfo.InvariantCulture) + " KB"
                    : growth.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
                growths.Add($"{measure.Name} {shown} a {(perReload ? "reload" : "hour")}");
            }
            switch (WebLingoSoakTrend.KeepsRising(after, measure))
            {
                case true:
                    rising.Add(measure.Name);
                    break;
                case null:
                    unsure = true;
                    break;
            }
        }
        output.WriteLine($"After the warm-up, the first quarter of the samples, {after.Count} samples: "
                         + (growths.Count == 0 ? "too few to give a growth" : string.Join(", ", growths)) + ".");
        if (unread.Count > 0) output.WriteLine($"Not read in every sample, and so not judged: {string.Join(", ", unread)}.");
        output.WriteLine(rising.Count > 0 ? $"Rising all the way, by more than its noise: {string.Join(", ", rising)}."
            : unsure ? $"Too few samples after the warm-up to say whether any keeps rising: {WebLingoSoakTrend.Fewest} are needed."
            : "None keeps rising.");
        return rising.Count > 0;
    }

    /// <summary>One client's latencies and errors, which a soak takes from another thread as it samples.</summary>
    private sealed class Tally
    {
        private readonly object _lock = new();
        private readonly List<long> _latencies = [];
        private int _errors;

        public IEnumerable<long> Latencies
        {
            get { lock (_lock) return [.. _latencies]; }
        }

        public int Errors
        {
            get { lock (_lock) return _errors; }
        }

        public string? FirstError { get; private set; }

        /// <summary>How many of its requests ended within the time measured.</summary>
        public int Within { get; private set; }

        public void Count(long latency, bool ok, string? failure, bool within = true)
        {
            lock (_lock)
            {
                _latencies.Add(latency);
                if (within) Within++;
                if (ok) return;
                _errors++;
                FirstError ??= failure;
            }
        }

        /// <summary>
        /// Its latencies so far added to <paramref name="into"/>, and its errors, each taken, so that the next sample counts
        /// anew.
        /// </summary>
        public int Take(List<long> into)
        {
            lock (_lock)
            {
                into.AddRange(_latencies);
                _latencies.Clear();
                var errors = _errors;
                _errors = 0;
                return errors;
            }
        }
    }

    private static HttpRequestMessage Get(string path) => new(HttpMethod.Get, path);

    private static HttpRequestMessage Post()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Upload);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "load.bin");
        return new HttpRequestMessage(HttpMethod.Post, "/upload") { Content = form };
    }

    private static byte[] MakeUpload()
    {
        var bytes = new byte[100 * 1024];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 31 % 251);
        return bytes;
    }

    /// <summary>What <c>compute.wasp</c> sums, computed as it computes it.</summary>
    /// <summary>
    /// <c>--fair</c> (section 9, "M0.24 parts", part 3): under each quantum, the site served anew, its <c>wasp.config</c>
    /// given it, which a server reads as it starts, two
    /// clients for each of the server's workers, the machine's processors, asking for <c>slow</c>, so that every worker
    /// computes and slow requests wait for one, and the most <c>--clients</c> gives asking for <c>hello</c> at once: <c>hello</c>'s latencies, which a worker held by a
    /// slow page delays, and both pages' throughput.
    /// </summary>
    private static async Task<int> Fair(IReadOnlyList<long?> quanta, Settings settings, TextWriter output)
    {
        if (settings.Url is not null) throw new InvalidOperationException("--fair needs a site the benchmark serves itself.");
        output.WriteLine($"WASP under load: {(settings.Host == "serve" ? "`wasp serve`" : settings.Host)}, served anew under each quantum.");
        output.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription.Trim()}, {Environment.ProcessorCount} "
                         + "logical processors, the clients on the same machine.");
        output.WriteLine();
        var (time, warmUp) = (TimeSpan.FromSeconds(settings.Seconds), TimeSpan.FromSeconds(settings.WarmUp));
        var (slow, quick) = (2 * Environment.ProcessorCount, settings.Clients.Max());
        output.WriteLine($"`slow` asked for by {slow} clients, two for each of the server's workers, and `hello` by {quick} at once, "
                         + $"for {settings.Seconds} s after {settings.WarmUp} s, under each quantum.");
        output.WriteLine();
        output.WriteLine("| Quantum | hello: requests/s | Median | 95% under | 99% under | slow: requests/s | Median | Errors |");
        output.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        var wrong = 0;
        foreach (var quantum in quanta)
        {
            var limit = quantum is { } given ? given.ToString(CultureInfo.InvariantCulture) : "null";
            await using var site = await WebLingoLoadSite.Start(settings.Host, $"\"limits\": {{ \"quantum\": {limit} }}");
            var hello = Pages[0];
            var measured = await Task.WhenAll(Measure(site, Slow, slow, warmUp, time), Measure(site, hello, quick, warmUp, time));
            var (slowResult, helloResult) = (measured[0], measured[1]);
            var errors = slowResult.Errors + helloResult.Errors;
            wrong += errors;
            var first = helloResult.FirstError ?? slowResult.FirstError;
            output.WriteLine($"| {(quantum is { } q ? Number(q) : "none")} | {Number(helloResult.PerSecond)} | {Milliseconds(helloResult.Median)} | "
                             + $"{Milliseconds(helloResult.Slowest95)} | {Milliseconds(helloResult.Slowest99)} | "
                             + $"{slowResult.PerSecond.ToString("0.0", CultureInfo.InvariantCulture)} | {Milliseconds(slowResult.Median)} | "
                             + $"{errors}{(first is null ? "" : ": " + first)} |");
            output.Flush();
        }
        return wrong == 0 ? Success : Failed;
    }

    /// <summary>The quanta <c>--fair</c> takes, <c>none,1000000,100000,10000</c>, each <c>none</c> or an integer more than 0.</summary>
    private static bool TryQuanta(string text, out List<long?> quanta)
    {
        quanta = [];
        foreach (var part in text.Split(','))
        {
            if (part == "none") quanta.Add(null);
            else if (long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var quantum) && quantum > 0) quanta.Add(quantum);
            else return false;
        }
        return quanta.Count > 0;
    }

    private static long SlowSumOf(long count)
    {
        long sum = 0;
        for (long i = 0; i < count; i++) sum += i % 7;
        return sum;
    }

    private static long ComputeSum()
    {
        long sum = 0;
        for (long i = 0; i < 20_000; i++) sum += i * i % 7;
        return sum;
    }

    private static bool Has(string text, string part) => text.Contains(part, StringComparison.Ordinal);

    private static int Count(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static long Ticks(TimeSpan time) => (long)(time.TotalSeconds * Stopwatch.Frequency);

    private static TimeSpan Percentile(long[] sorted, double fraction)
    {
        if (sorted.Length == 0) return TimeSpan.Zero;
        var at = Math.Min(sorted.Length - 1, Math.Max(0, (int)Math.Ceiling(fraction * sorted.Length) - 1));
        return TimeSpan.FromSeconds(sorted[at] / (double)Stopwatch.Frequency);
    }

    private static bool TryCount(string? text, int minimum, out int count) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out count) && count >= minimum;

    private static bool TryCounts(string? text, out int[] counts)
    {
        counts = [];
        if (text is null) return false;
        var parts = text.Split(',');
        var parsed = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!TryCount(parts[i], 1, out parsed[i])) return false;
        }
        counts = parsed;
        return true;
    }

    private static string Number(double value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    private static string Text(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "–";

    private static string Milliseconds(TimeSpan time) => time.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";

    private static string Megabytes(long? bytes) =>
        bytes is { } value ? (value / 1024.0 / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " MB" : "–";

    /// <summary>
    /// <c>--prepare</c>: the load site made in <paramref name="folder"/> for IIS to serve (specification, section 5.6, "Under
    /// load, parts", part 3; <see cref="WebLingoLoadSite.Prepare"/>), and what then adds it to IIS and measures it written.
    /// </summary>
    private static int Prepare(string folder, TextWriter output, TextWriter error)
    {
        string site, data;
        try
        {
            var full = Path.GetFullPath(folder);
            (site, data) = (Path.Combine(full, "site"), Path.Combine(full, "load-data"));
            WebLingoLoadSite.Prepare(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or Microsoft.Data.Sqlite.SqliteException)
        {
            error.WriteLine($"The load site could not be made in {folder}: {exception.Message}");
            return Failed;
        }
        output.WriteLine($"The load site is in {site}, its database in {data}.");
        output.WriteLine("Add it to IIS from the repository's Scripts, as administrator:");
        output.WriteLine();
        output.WriteLine($"  weblingo run add-wasp-site.wlg name=wasp.load folder={site} host=load.wasp.localhost writable={data}");
        output.WriteLine("  weblingo run add-sql-server-login.wlg name=wasp.load database=master");
        output.WriteLine();
        output.WriteLine($"Then measure it: WebLingo.Benchmarks --load --url https://load.wasp.localhost/, and --reload with --folder {folder}");
        return Success;
    }

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Usage: WebLingo.Benchmarks --load [--host serve|standalone|aspnetcore|fastcgi | --url <url> [--folder FOLDER]]");
        error.WriteLine("                             [--site load|forum]");
        error.WriteLine("                             [--page NAME]...");
        error.WriteLine("                             [--clients 1,4,16,64,256]");
        error.WriteLine("                             [--seconds N] [--warm-up N]");
        error.WriteLine("                             [--reload SECONDS | --soak HOURS [--sample MINUTES] [--reload-every SECONDS]");
        error.WriteLine("                             | --fair none,1000000,100000,10000]");
        error.WriteLine("       WebLingo.Benchmarks --load --prepare FOLDER");
        error.WriteLine($"  pages: {string.Join(", ", Pages.Select(page => page.Name))}");
        error.WriteLine($"  the forum's, --site forum, at --url: {string.Join(", ", ForumPages.Select(page => page.Name))}");
        return UsageError;
    }
}
