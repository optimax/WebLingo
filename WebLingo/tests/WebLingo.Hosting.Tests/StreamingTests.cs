using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A page's output sent as it is written (specification, section 6.12, "Streaming"; section 9, "M0.14.7 parts", part 1):
/// kept until it reaches 64 KB, so that a shorter page that fails is still a 500, then sent in pieces; and a page that fails
/// after that, its response ended unfinished, its error logged and, in development, at its output's end.
/// </summary>
public sealed class StreamingTests
{
    private const string Root = "/site";

    /// <summary>A page writing <paramref name="kilobytes"/> KB of <c>x</c>, a kilobyte at a time, then <paramref name="after"/>.</summary>
    private static string Page(int kilobytes, string after = "") =>
        "<% var i = 0; while (i < " + kilobytes + ") { %>" + new string('x', 1024) + "<% i = i + 1; } %>" + after;

    [Fact]
    public void AShortPageIsSentWholeOnceItHasRun()
    {
        var response = new RecordingResponse();
        Load(new MemoryFileSystem().Add("/site/index.wasp", Page(63))).Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        Assert.Equal([63 * 1024], response.Writes);
        Assert.Equal((200, false, "text/html; charset=utf-8"), (response.Inner.StatusCode, response.Inner.Aborted, response.Inner.Headers.Get("Content-Type")));
    }

    [Fact]
    public void ALongPageIsSentAsItIsWritten()
    {
        var response = new RecordingResponse();
        var app = Load(new MemoryFileSystem().Add("/site/index.wasp",
            "<% param response; response.headers[\"Content-Type\"] = \"text/plain\"; %>" + Page(100, "<% writeLn(response.status); %>")));
        app.Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        // Committed once 64 KB were written, then sent 16 KB at a time, the rest once the page has run.
        Assert.Equal([64 * 1024, 16 * 1024, 16 * 1024, 4 * 1024 + 4], response.Writes);
        Assert.Equal((200, false, "text/plain"), (response.Inner.StatusCode, response.Inner.Aborted, response.Inner.Headers.Get("Content-Type")));
        Assert.EndsWith("xx200\n", Encoding.UTF8.GetString(response.Inner.BodyBytes));
    }

    [Fact]
    public void APageThatFailsBeforeItsOutputIsSentIsA500()
    {
        var log = new List<string>();
        var response = new WebLingoBufferedResponse();
        Load(new MemoryFileSystem().Add("/site/index.wasp", Page(63, "<%= [1][5] %>")), log: log)
            .Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        Assert.Equal((500, false), (response.StatusCode, response.Aborted));
        Assert.DoesNotContain("xxx", Encoding.UTF8.GetString(response.BodyBytes));
        Assert.Empty(log);
    }

    [Theory]
    [InlineData(WebLingoEnvironment.Development)]
    [InlineData(WebLingoEnvironment.Production)]
    public void APageThatFailsAfterItsOutputIsSentEndsItUnfinished(WebLingoEnvironment environment)
    {
        var log = new List<string>();
        var response = new WebLingoBufferedResponse();
        var outcome = Load(new MemoryFileSystem().Add("/site/index.wasp", Page(70, "\n<%= [\"<a>\"][5] %>")), environment, log)
            .Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        Assert.Equal(WebLingoRequestOutcomeKind.Failed, outcome.Kind);
        Assert.Equal((200, true), (response.StatusCode, response.Aborted));
        var body = Encoding.UTF8.GetString(response.BodyBytes);
        Assert.StartsWith(new string('x', 70 * 1024), body);
        var error = Assert.Single(log);
        Assert.StartsWith("A page failed after its response was sent: index.wasp(2,", error);
        Assert.Contains("error WL3005:", error);
        if (environment == WebLingoEnvironment.Development)
        {
            Assert.EndsWith("\n<pre>" + error["A page failed after its response was sent: ".Length..].Replace("'", "&#39;", StringComparison.Ordinal) + "\n</pre>\n", body);
        }
        else
        {
            Assert.EndsWith(new string('x', 1024) + "\n", body);
        }
    }

    [Fact]
    public void AStatusOrAHeaderSetOnceTheOutputIsSentIsAnError()
    {
        var log = new List<string>();
        var response = new WebLingoBufferedResponse();
        Load(new MemoryFileSystem().Add("/site/index.wasp", "<% param response; %>" + Page(65, "<% response.status = 404; %>")), log: log)
            .Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        Assert.Equal((200, true), (response.StatusCode, response.Aborted));
        Assert.Contains("error WL3001: The response's status cannot be set: it has been sent.", Assert.Single(log));
    }

    private static WebLingoApplication Load(MemoryFileSystem files, WebLingoEnvironment environment = WebLingoEnvironment.Production,
        List<string>? log = null)
    {
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions(Root, files)
        {
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry(new WebLingoWriteFunction(), new WebLingoWriteLnFunction())),
            Environment = environment,
            Log = log is null ? null : log.Add
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }

    /// <summary>A response in memory that records each write's size.</summary>
    private sealed class RecordingResponse : IWebLingoHttpResponse, IWebLingoBodyWriter
    {
        public WebLingoBufferedResponse Inner { get; } = new();
        public List<int> Writes { get; } = [];

        public WebLingoResponseState State => Inner.State;
        public int StatusCode => Inner.StatusCode;
        public IWebLingoHeaderCollection Headers => Inner.Headers;
        public IWebLingoBodyWriter Body => this;
        public bool TrySetStatusCode(int code) => Inner.TrySetStatusCode(code);
        public void Complete() => Inner.Complete();
        public void Abort() => Inner.Abort();

        public void Commit() => Inner.Commit();

        public bool Write(byte[] buffer, int offset, int count)
        {
            Writes.Add(count);
            return Inner.Body.Write(buffer, offset, count);
        }
    }
}
