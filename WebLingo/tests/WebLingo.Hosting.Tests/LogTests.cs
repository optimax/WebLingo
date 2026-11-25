using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A site's log (specification, section 6.12, "Logging"; the owner's answer, 24 November 2026, "Logging for WASP Sites",
/// A1 (c)): a request's failures logged at <c>error</c> with its request, as the debugger names one, <c>GET /fail #1</c>,
/// whatever the mode, a request refused as another site's at <c>warn</c>; and the host's log, a writer's or a file a day,
/// each line its time in UTC, its level, its request and its text, kept to one line.
/// </summary>
public sealed class LogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void ARequestsFailureIsLoggedWithItsRequest_InProductionToo()
    {
        var log = new ListLog();
        var app = Load(log, WebLingoMode.Production);
        var (status, body) = Get(app, "/fail");
        Assert.Equal(500, status);
        Assert.DoesNotContain("WL3008", body);
        var (level, request, text) = Assert.Single(log.Entries);
        Assert.Equal((WebLingoLogLevel.Error, "GET /fail #1"), (level, request));
        Assert.Equal("fail.wasp(1,4): error WL3008: An exception was thrown and not caught: no tea", text);

        // A page that does not compile, its errors each a line of the request's; a request that fails nothing not logged.
        Assert.Equal(500, Get(app, "/broken").Status);
        Assert.Equal(200, Get(app, "/fine").Status);
        Assert.True(log.Entries.Count > 2);
        Assert.All(log.Entries.Skip(1), entry => Assert.Equal((WebLingoLogLevel.Error, "GET /broken #2"), (entry.Level, entry.Request)));
        Assert.All(log.Entries.Skip(1), entry => Assert.StartsWith("broken.wasp(1,", entry.Text));
    }

    [Fact]
    public void AHandlerThatCannotRunIsLogged_AndARequestFromAnotherSiteWarned()
    {
        var log = new ListLog();
        var app = Load(log, WebLingoMode.Production);
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("POST", "/fine", headers: [("Origin", "http://attacker.example")]), response,
            new WebLingoWorkerId(0));
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(500, Get(app, "/api/wrong").Status);
        Assert.Equal([WebLingoLogLevel.Warn, WebLingoLogLevel.Error], log.Entries.Select(entry => entry.Level));
        Assert.Equal("A POST from another site was refused, as a cross-site request forgery may be: its Origin, "
                     + "'http://attacker.example', is not this site's, 'localhost'; 'crossorigin allowed;' lets a page take one, "
                     + "and 'export const crossorigin = \"allowed\";' a handler.", log.Entries[0].Text);
        Assert.StartsWith("api/wrong.wlg(1,1): error WL5010:", log.Entries[1].Text);
    }

    [Fact]
    public void ALogsLevelLeavesOutWhatIsBelowIt()
    {
        var log = new ListLog(WebLingoLogLevel.Error);
        var app = Load(log, WebLingoMode.Production);
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("POST", "/fine", headers: [("Sec-Fetch-Site", "cross-site")]), response, new WebLingoWorkerId(0));
        Assert.Equal(500, Get(app, "/fail").Status);
        Assert.Equal([(WebLingoLogLevel.Error, "GET /fail #1")], log.Entries.Select(entry => (entry.Level, entry.Request)));
    }

    [Fact]
    public void ALineIsItsTimeItsLevelItsRequestAndItsText()
    {
        var writer = new StringWriter();
        var log = new DotNetLog(writer, WebLingoLogLevel.Info, () => new DateTimeOffset(2026, 11, 25, 9, 12, 3, 45, TimeSpan.FromHours(1)));
        log.Write(WebLingoLogLevel.Debug, "GET /cart #1", "left out");
        log.Write(WebLingoLogLevel.Info, "GET /cart #1", "The cart is empty.");
        log.Write(WebLingoLogLevel.Warn, null, "No request.");
        log.Write(WebLingoLogLevel.Error, "POST /pay #2", "Declined.");
        Assert.Equal("2026-11-25T08:12:03.045Z INFO GET /cart #1 The cart is empty.\n"
                     + "2026-11-25T08:12:03.045Z WARN No request.\n"
                     + "2026-11-25T08:12:03.045Z ERROR POST /pay #2 Declined.\n", writer.ToString().Replace("\r\n", "\n"));
        Assert.Equal((1, 2), (log.NextRequest(), log.NextRequest()));
    }

    [Fact]
    public void AFileIsWrittenADay_ItsFolderMade()
    {
        var now = new DateTimeOffset(2026, 11, 25, 23, 59, 59, TimeSpan.Zero);
        var file = Path.Combine(_folder, "logs", "shop.log");
        using (var log = new DotNetLog(file, WebLingoLogLevel.Debug, null, () => now))
        {
            log.Write(WebLingoLogLevel.Debug, "GET / #1", "first");
            now = now.AddSeconds(2);
            log.Write(WebLingoLogLevel.Info, "GET / #2", "second");
        }
        Assert.Equal("2026-11-25T23:59:59.000Z DEBUG GET / #1 first\n",
            File.ReadAllText(Path.Combine(_folder, "logs", "shop-2026-11-25.log")).Replace("\r\n", "\n"));
        Assert.Equal("2026-11-26T00:00:01.000Z INFO GET / #2 second\n",
            File.ReadAllText(Path.Combine(_folder, "logs", "shop-2026-11-26.log")).Replace("\r\n", "\n"));

        // Appended to, and readable while it is written.
        using (var log = new DotNetLog(file, WebLingoLogLevel.Debug, null, () => now))
        {
            log.Write(WebLingoLogLevel.Info, null, "third");
            using var reading = new FileStream(Path.Combine(_folder, "logs", "shop-2026-11-26.log"), FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            Assert.Equal(2, new StreamReader(reading).ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        }
    }

    [Fact]
    public void ALineTheFileCannotTakeIsWrittenToTheHostsLog()
    {
        // A folder where the file's folder would be: nothing can be written there.
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "logs"), "a file, not a folder");
        var writer = new StringWriter();
        using var log = new DotNetLog(Path.Combine(_folder, "logs", "shop.log"), WebLingoLogLevel.Info, writer,
            () => new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero));
        log.Write(WebLingoLogLevel.Info, null, "one");
        log.Write(WebLingoLogLevel.Info, null, "two");
        var lines = writer.ToString().Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("WASP cannot write its log, ", lines[0]);
        Assert.Equal(["2026-11-25T08:00:00.000Z INFO one", "2026-11-25T08:00:00.000Z INFO two"], lines[1..]);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a\r\nb", "a\\r\\nb")]
    [InlineData("tab\tkept", "tab\tkept")]
    [InlineData("bell\u0007 and \u2028", "bell\\u{7} and \\u{2028}")]
    public void ALinesTextIsKeptToOneLine(string text, string line)
    {
        Assert.Equal(line, WebLingoLogText.OneLine(text));
    }

    private static WebLingoApplication Load(IWebLingoLog log, WebLingoMode mode)
    {
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions("/site", new MemoryFileSystem()
            .Add("/site/fail.wasp", "<% throw \"no tea\"; %>")
            .Add("/site/broken.wasp", "<% var = ; %><% if %>")
            .Add("/site/fine.wasp", "fine")
            .Add("/site/api/wrong.wlg", "export const crossorigin = 1;\nexport function get(request, response) { }"))
        {
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry()),
            Scheduler = () => new DotNetSchedulerHost(),
            Mode = mode,
            Log = log
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }

    private static (int Status, string Body) Get(WebLingoApplication app, string path)
    {
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("GET", path), response, new WebLingoWorkerId(0));
        return (response.StatusCode, Encoding.UTF8.GetString(response.BodyBytes));
    }
}
