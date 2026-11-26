using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Hosting.DotNet;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// Files sent (specification, section 6.12, "Files sent"): <c>response.write(bytes)</c>, written as they are, in order with
/// the text around them; <c>response.sendFile(path, type, { download })</c>, which leaves its host the file, its path
/// checked through the script's files, the page's status and headers kept; and the plan both hosts send a file by
/// (<see cref="WebLingoFileSending"/>), its <c>ETag</c>, 304, ranges and <c>Content-Disposition</c>.
/// </summary>
public sealed class SentFileTests
{
    private static readonly DateTimeOffset Modified = new(2026, 11, 25, 10, 30, 15, 250, TimeSpan.Zero);

    [Fact]
    public void AResponseWritesBytesAsTheyAreInOrderWithItsText()
    {
        var app = Load(new MemoryFileSystem().Add("/site/data.wlg", "import { Bytes } from \"std:bytes\";\n"
            + "export function get(request, response) { response.write(\"a\"); response.write(Bytes.fromHex(\"00ff\")); response.writeLn(\"b\");\n"
            + "  response.writeLn(Bytes.from(\"é\")); response.write(null); }"));
        var response = new WebLingoBufferedResponse();
        Assert.Equal(WebLingoRequestOutcomeKind.Handler, app.Handle(new WebLingoBufferedRequest("GET", "/data"), response, new WebLingoWorkerId(0)).Kind);
        Assert.Equal(new byte[] { 0x61, 0x00, 0xFF, 0x62, 0x0A, 0xC3, 0xA9, 0x0A, 0x6E, 0x75, 0x6C, 0x6C }, response.BodyBytes);
    }

    [Fact]
    public void BytesBeyondWhatIsKeptAreSentInOrder()
    {
        // 70,000 bytes, more than the 64 KB a response keeps, then text: sent as written, the text after them.
        var app = Load(new MemoryFileSystem().Add("/site/data.wlg", "import { Bytes } from \"std:bytes\";\n"
            + "export function get(request, response) { response.write(\"<\"); response.write(Bytes.from(\"x\".repeat(70000))); response.write(\">\"); }"));
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("GET", "/data"), response, new WebLingoWorkerId(0));
        Assert.Equal("<" + new string('x', 70000) + ">", Encoding.UTF8.GetString(response.BodyBytes));
    }

    [Fact]
    public void APageSendsAFileWithItsStatusAndHeaders()
    {
        var app = Load(Site().Add("/site/report.wasp", "<% param response; response.status = 201; response.headers[\"X-Kind\"] = \"report\"; %>before"
            + "<% await response.sendFile(\"files/report.pdf\", null, { download: true }); %>after<% response.write(\"and more\"); %>"));
        var response = new WebLingoBufferedResponse();
        var outcome = app.Handle(new WebLingoBufferedRequest("GET", "/report"), response, new WebLingoWorkerId(0));
        Assert.Equal((WebLingoRequestOutcomeKind.File, "/memory/files/report.pdf"), (outcome.Kind, outcome.File));
        Assert.Equal(new WebLingoSentFile("/memory/files/report.pdf", null, "report.pdf"), outcome.Sent);
        // Left uncommitted, for the host to send the file with the page's status and headers; what it wrote never sent.
        Assert.Equal(WebLingoResponseState.Uncommitted, response.State);
        Assert.Equal((201, "report"), (response.StatusCode, response.Headers.Get("X-Kind")));
        Assert.Empty(response.BodyBytes);
    }

    [Fact]
    public void AHandlerSendsAFileWithItsTypeAndName()
    {
        var outcome = Send("files/report.pdf", "application/pdf", "named", out _);
        Assert.Equal(new WebLingoSentFile("/memory/files/report.pdf", "application/pdf", "Raport roczny żółty.pdf"), outcome.Sent);
        Assert.Equal(new WebLingoSentFile("/memory/files/report.pdf", "text/plain; charset=utf-8", null),
            Send("files/report.pdf", "text/plain; charset=utf-8", "show", out _).Sent);
    }

    [Fact]
    public void AFileThatCannotBeSentIsAnErrorThatSaysWhy()
    {
        Assert.Equal("WL3016 FilePermissionError 'secret/key.txt' cannot be sent: the site may not read it.", Failure("secret/key.txt"));
        Assert.Equal("WL3011 IOError 'files/none.pdf' cannot be sent: it is not there.", Failure("files/none.pdf"));
        Assert.Equal("WL3011 IOError 'files/sub' cannot be sent: it is a folder.", Failure("files/sub"));
        Assert.Equal("WL3001 TypeError The first argument of 'sendFile', the path, must be a file's path, a string, not the integer 5.",
            Failure("5"));
        Assert.Equal("WL3001 TypeError The second argument of 'sendFile', the type, must be a media type, \"type/subtype\", or null for the one "
            + "its extension gives, not \"pdf\".", Failure("files/report.pdf", "pdf"));
        Assert.Equal("WL3001 TypeError The third argument of 'sendFile', the options, has 'downlaod', which is not an option of 'sendFile'; "
            + "did you mean download?", Failure("files/report.pdf", options: "typo"));
        Assert.Equal("WL3001 TypeError The third argument of 'sendFile', the options, must give download a file's name, a string without "
            + "control characters, true for the file's own, or false, not a string.", Failure("files/report.pdf", options: "path"));
        Assert.Equal("WL3001 TypeError The third argument of 'sendFile', the options, must be a map of the file's options, not the integer 5.",
            Failure("files/report.pdf", options: "number"));
    }

    [Fact]
    public void AFileCannotBeSentOnceTheOutputIsBeingSent()
    {
        var app = Load(Site().Add("/site/late.wlg", "export async function get(request, response) { response.write(\"x\".repeat(70000));\n"
            + "  try { await response.sendFile(\"files/report.pdf\"); } catch (e) { response.write($\"|{e.code} {e.message}\"); } }"));
        var response = new WebLingoBufferedResponse();
        var outcome = app.Handle(new WebLingoBufferedRequest("GET", "/late"), response, new WebLingoWorkerId(0));
        Assert.Null(outcome.Sent);
        Assert.EndsWith("x|WL3001 'files/report.pdf' cannot be sent: the response's output is being sent already.",
            Encoding.UTF8.GetString(response.BodyBytes));
    }

    [Fact]
    public void AFileIsSentWholeWithItsTagAndTime()
    {
        var headers = new WebLingoHeaderList();
        var plan = WebLingoFileSending.Plan("GET", new WebLingoHeaderList(), 200, headers, 1000, Modified, "application/pdf", null);
        Assert.Equal(new WebLingoFilePlan(200, 1000, 0, 1000, true), plan);
        Assert.Equal("application/pdf", headers.Get("Content-Type"));
        Assert.Equal("\"3e8-" + Modified.UtcTicks.ToString("x") + "\"", headers.Get("ETag"));
        Assert.Equal("Wed, 25 Nov 2026 10:30:15 GMT", headers.Get("Last-Modified"));
        Assert.Equal("bytes", headers.Get("Accept-Ranges"));
        Assert.Null(headers.Get("Content-Disposition"));
        // A HEAD's the same, without its body.
        Assert.Equal(new WebLingoFilePlan(200, 1000, 0, 1000, false),
            WebLingoFileSending.Plan("HEAD", new WebLingoHeaderList(), 200, new WebLingoHeaderList(), 1000, Modified, "application/pdf", null));
    }

    [Fact]
    public void AFileTheClientHasIsNotModified()
    {
        var tag = "\"3e8-" + Modified.UtcTicks.ToString("x") + "\"";
        Assert.Equal(304, Plan(("If-None-Match", tag)).Status);
        Assert.Equal(304, Plan(("If-None-Match", "\"other\", W/" + tag)).Status);
        Assert.Equal(304, Plan(("If-None-Match", "*")).Status);
        Assert.Equal(200, Plan(("If-None-Match", "\"other\"")).Status);
        Assert.Equal(new WebLingoFilePlan(304, null, 0, 0, false), Plan(("If-Modified-Since", "Wed, 25 Nov 2026 10:30:15 GMT")));
        Assert.Equal(304, Plan(("If-Modified-Since", "Thu, 26 Nov 2026 00:00:00 GMT")).Status);
        Assert.Equal(200, Plan(("If-Modified-Since", "Wed, 25 Nov 2026 10:30:14 GMT")).Status);
        Assert.Equal(200, Plan(("If-Modified-Since", "yesterday")).Status);
        // If-None-Match, where there is one, decides alone.
        Assert.Equal(200, Plan(("If-None-Match", "\"other\""), ("If-Modified-Since", "Thu, 26 Nov 2026 00:00:00 GMT")).Status);
        // Only a GET's or a HEAD's 200.
        Assert.Equal(200, Plan("POST", 200, ("If-None-Match", "*")).Status);
        Assert.Equal(404, Plan("GET", 404, ("If-None-Match", "*")).Status);
    }

    [Fact]
    public void ARangeOfAFileIsSentInPart()
    {
        var headers = new WebLingoHeaderList();
        Assert.Equal(new WebLingoFilePlan(206, 10, 0, 10, true), WebLingoFileSending.Plan("GET", Request(("Range", "bytes=0-9")), 200, headers,
            1000, Modified, "application/pdf", null));
        Assert.Equal("bytes 0-9/1000", headers.Get("Content-Range"));
        Assert.Equal(new WebLingoFilePlan(206, 100, 900, 100, true), Plan(("Range", "bytes=900-")));
        Assert.Equal(new WebLingoFilePlan(206, 100, 900, 100, true), Plan(("Range", "bytes=-100")));
        Assert.Equal(new WebLingoFilePlan(206, 1000, 0, 1000, true), Plan(("Range", "bytes=-5000")));
        Assert.Equal(new WebLingoFilePlan(206, 10, 990, 10, true), Plan(("Range", "bytes=990-5000")));
        Assert.Equal(new WebLingoFilePlan(206, 1, 5, 1, false), Plan("HEAD", 200, ("Range", "bytes=5-5")));
        // Beyond the file, or none of its end: a 416, which says how long it is.
        headers = new WebLingoHeaderList();
        Assert.Equal(new WebLingoFilePlan(416, 0, 0, 0, false), WebLingoFileSending.Plan("GET", Request(("Range", "bytes=1000-")), 200, headers,
            1000, Modified, "application/pdf", "a.pdf"));
        Assert.Equal(("bytes */1000", null), (headers.Get("Content-Range"), headers.Get("Content-Disposition")));
        Assert.Equal(416, Plan(("Range", "bytes=-0")).Status);
        // Several ranges, or what is not a range, the file whole.
        foreach (var whole in new[] { "bytes=0-1,5-6", "bytes=9-1", "items=0-1", "bytes=a-b", "bytes=0x1-2", "bytes=99999999999999999999-" })
        {
            Assert.Equal(new WebLingoFilePlan(200, 1000, 0, 1000, true), Plan(("Range", whole)));
        }
    }

    [Fact]
    public void IfRangeAllowsARangeOnlyOfTheSameFile()
    {
        var tag = "\"3e8-" + Modified.UtcTicks.ToString("x") + "\"";
        Assert.Equal(206, Plan(("Range", "bytes=0-9"), ("If-Range", tag)).Status);
        Assert.Equal(200, Plan(("Range", "bytes=0-9"), ("If-Range", "\"other\"")).Status);
        Assert.Equal(200, Plan(("Range", "bytes=0-9"), ("If-Range", "W/" + tag)).Status);
        Assert.Equal(206, Plan(("Range", "bytes=0-9"), ("If-Range", "Wed, 25 Nov 2026 10:30:15 GMT")).Status);
        Assert.Equal(200, Plan(("Range", "bytes=0-9"), ("If-Range", "Thu, 26 Nov 2026 00:00:00 GMT")).Status);
    }

    [Fact]
    public void APagesOwnTagAndTimeAreKept()
    {
        var headers = new WebLingoHeaderList();
        headers.Set("ETag", "\"v7\"");
        headers.Set("Last-Modified", "Mon, 02 Nov 2026 00:00:00 GMT");
        Assert.Equal(304, WebLingoFileSending.Plan("GET", Request(("If-None-Match", "\"v7\"")), 200, headers, 1000, Modified, "image/png", null).Status);
        Assert.Equal(("\"v7\"", "Mon, 02 Nov 2026 00:00:00 GMT"), (headers.Get("ETag"), headers.Get("Last-Modified")));
    }

    [Fact]
    public void AFileToBeSavedIsNamedInAsciiAndInUtf8()
    {
        Assert.Equal("attachment; filename=\"report.pdf\"", WebLingoFileSending.Disposition("report.pdf"));
        Assert.Equal("attachment; filename=\"Raport ___ty _.pdf\"; filename*=UTF-8''Raport%20%C5%BC%C3%B3%C5%82ty%20%22.pdf",
            WebLingoFileSending.Disposition("Raport żółty \".pdf"));
        Assert.Equal("attachment; filename=\"a_b.txt\"; filename*=UTF-8''a%5Cb.txt", WebLingoFileSending.Disposition("a\\b.txt"));
    }

    private static WebLingoFilePlan Plan(params (string, string)[] request) => Plan("GET", 200, request);

    private static WebLingoFilePlan Plan(string method, int status, params (string, string)[] request) =>
        WebLingoFileSending.Plan(method, Request(request), status, new WebLingoHeaderList(), 1000, Modified, "application/pdf", null);

    private static WebLingoHeaderList Request(params (string Name, string Value)[] headers)
    {
        var list = new WebLingoHeaderList();
        foreach (var (name, value) in headers) list.Add(name, value);
        return list;
    }

    private static MemoryFileSystem Site() => new MemoryFileSystem()
        .Add("files/report.pdf", "%PDF")
        .Add("files/sub/a.txt", "a")
        .Add("secret/key.txt", "key")
        .Add("/site/send.wlg", "const optionsOf = { none: null, show: { download: false }, own: { download: true },\n"
            + "  named: { download: \"Raport roczny żółty.pdf\" }, typo: { downlaod: true }, path: { download: \"a/b\" }, number: 5 };\n"
            + "export async function get(request, response) {\n"
            + "  var path = request.query[\"path\"]; if (path == \"5\") path = 5;\n"
            + "  try { await response.sendFile(path, request.query?[\"type\"], optionsOf[request.query?[\"options\"] ?? \"none\"]); }\n"
            + "  catch (e) { response.write($\"{e.code} {e.__instanceOf.__className} {e.message}\"); } }");

    private static WebLingoRequestOutcome Send(string path, string? type, string options, out WebLingoBufferedResponse response)
    {
        var query = "path=" + Uri.EscapeDataString(path) + (type is null ? "" : "&type=" + Uri.EscapeDataString(type)) + "&options=" + options;
        response = new WebLingoBufferedResponse();
        return Load(Site()).Handle(new WebLingoBufferedRequest("GET", "/send", query), response, new WebLingoWorkerId(0));
    }

    private static string Failure(string path, string? type = null, string options = "none")
    {
        var outcome = Send(path, type, options, out var response);
        Assert.Null(outcome.Sent);
        return Encoding.UTF8.GetString(response.BodyBytes);
    }

    private static WebLingoApplication Load(MemoryFileSystem files)
    {
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions("/site", files)
        {
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry()),
            Scheduler = () => new DotNetSchedulerHost(),
            Mode = WebLingoMode.Development
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }
}
