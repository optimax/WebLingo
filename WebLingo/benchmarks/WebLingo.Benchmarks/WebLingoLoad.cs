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
public sealed record WebLingoServerSample(long WorkingSet, long PrivateBytes, int Handles, int Threads);

/// <summary>
/// WASP under load (specification, section 5.6, "Under load", part 1): <c>WebLingo.Benchmarks --load</c>. The load site,
/// its <c>LoadSite</c> folder, copied beside a SQLite database of its own, is served by <c>wasp serve</c>, or by
/// <c>wasp fastcgi</c> behind nginx, unpacked from the repository's <c>WebServers</c> folder as <c>WebServerTests</c>
/// unpacks it, or is found at a URL, as IIS serves it; clients ask for its pages at once, each with its own connection and
/// its own cookies, as browsers do, every answer checked, until the time measured ends. It writes each page's throughput and
/// latency at each number of clients, and its server's memory after; with <c>--reload</c>, a page replaced every quarter
/// of a second while clients ask for it, every answer one version's whole; with <c>--soak</c>, the pages asked for in turn
/// for hours, the server's memory and handles sampled as it goes. Exit codes: 0 every answer right, 1 one wrong or failed,
/// 2 a usage error.
/// </summary>
public static class WebLingoLoad
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int UsageError = 2;

    private static readonly byte[] Upload = MakeUpload();
    private static readonly long ComputedSum = ComputeSum();
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
        public List<string> Names { get; } = [];
        public int[] Clients { get; private set; } = [1, 4, 16, 64, 256];
        public int Seconds { get; private set; } = 10;
        public int WarmUp { get; private set; } = 3;
        public int? Reload { get; private set; }
        public double? Soak { get; private set; }
        public int Sample { get; private set; } = 10;

        public IReadOnlyList<WebLingoLoadPage> All => Site == "forum" ? ForumPages : WebLingoLoad.Pages;

        public IReadOnlyList<WebLingoLoadPage> Pages => Names.Count == 0 ? All : All.Where(page => Names.Contains(page.Name)).ToList();

        /// <summary>Whether what it was given goes together: the forum's pages at a URL, and pages its site has.</summary>
        public bool IsWhole => (Site != "forum" || Url is not null) && Names.All(name => All.Any(page => page.Name == name));

        public bool TryTake(string option, string value)
        {
            switch (option)
            {
                case "--host" when value is "serve" or "fastcgi":
                    Host = value;
                    return true;
                case "--url" when Uri.TryCreate(value, UriKind.Absolute, out var given) && given.Scheme is "http" or "https":
                    (Host, Url) = ("url", given);
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
                default:
                    return false;
            }
        }
    }

    private static async Task<int> RunAsync(Settings settings, TextWriter output, TextWriter error)
    {
        await using var site = settings.Url is { } url ? WebLingoLoadSite.At(url) : await WebLingoLoadSite.Start(settings.Host);
        output.WriteLine($"WASP under load: {site.Description}.");
        output.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription.Trim()}, {Environment.ProcessorCount} "
                         + "logical processors, the clients on the same machine.");
        output.WriteLine();
        if (settings.Reload is { } reload) return await Reload(site, settings.Clients.Max(), TimeSpan.FromSeconds(reload), output);
        if (settings.Soak is { } hours)
        {
            var sample = TimeSpan.FromMinutes(settings.Sample);
            return await Soak(site, settings.Pages, settings.Clients.Max(), TimeSpan.FromHours(hours), sample, output);
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
        string[] versions = ["<p>Hello, load.</p>", "<p>Hello again, load.</p>"];
        const string Name = "<%= request.query?[\"name\"] ?? \"load\" %>";
        var page = new WebLingoLoadPage("hello", () => Get("/hello"), body => versions.Any(version => Has(body, version)));
        var until = Stopwatch.GetTimestamp() + Ticks(time);
        var (replaced, refused) = (0, 0);
        var replacing = Task.Run(async () =>
        {
            while (Stopwatch.GetTimestamp() < until)
            {
                await Task.Delay(250);
                var text = versions[++replaced % 2].Replace("load", Name, StringComparison.Ordinal);
                refused += await site.Replace("hello.wasp", "<% param request; %>" + text + "\n");
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

    /// <summary>The pages asked for in turn by <paramref name="count"/> clients for <paramref name="time"/>, a row each sample.</summary>
    private static async Task<int> Soak(WebLingoLoadSite site, IReadOnlyList<WebLingoLoadPage> pages, int count, TimeSpan time,
        TimeSpan sample, TextWriter output)
    {
        var answered = new List<WebLingoLoadPage>();
        foreach (var page in pages)
        {
            if (await Fails(site, page) is { } failure) output.WriteLine($"{page.Name} left out: {failure}.");
            else answered.Add(page);
        }
        output.WriteLine($"{string.Join(", ", answered.Select(page => page.Name))}, asked for in turn by {count} clients for "
                         + $"{time.TotalHours:0.#} h.");
        output.WriteLine();
        output.WriteLine("| Elapsed | Requests | Requests/s | Median | 99% under | Errors | Working set | Private bytes | Handles "
                         + "| Threads |");
        output.WriteLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        var started = Stopwatch.GetTimestamp();
        var until = started + Ticks(time);
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
            output.WriteLine($"| {elapsed:hh\\:mm} | {requests:#,0} | {Number(rate)} | "
                             + $"{Milliseconds(Percentile(sorted, 0.5))} | {Milliseconds(Percentile(sorted, 0.99))} | {errors} | "
                             + $"{Megabytes(server?.WorkingSet)} | {Megabytes(server?.PrivateBytes)} | {Text(server?.Handles)} | "
                             + $"{Text(server?.Threads)} |");
            output.Flush();
            last = now;
        }
        await asking;
        var first = tallies.Select(tally => tally.FirstError).FirstOrDefault(failure => failure is not null);
        if (first is not null) output.WriteLine($"The first error: {first}.");
        return errors == 0 ? Success : Failed;
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

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Usage: WebLingo.Benchmarks --load [--host serve|fastcgi | --url <url>] [--site load|forum] [--page NAME]...");
        error.WriteLine("                             [--clients 1,4,16,64,256]");
        error.WriteLine("                             [--seconds N] [--warm-up N] [--reload SECONDS | --soak HOURS [--sample MINUTES]]");
        error.WriteLine($"  pages: {string.Join(", ", Pages.Select(page => page.Name))}");
        error.WriteLine($"  the forum's, --site forum, at --url: {string.Join(", ", ForumPages.Select(page => page.Name))}");
        return UsageError;
    }
}
