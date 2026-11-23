using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A request's body in scripts (specification, section 6.12, "Streaming"; section 9, "M0.14.7 parts", part 4):
/// <c>await request.text()</c>, in the charset its <c>Content-Type</c> names, and <c>await request.form()</c>, an
/// <c>application/x-www-form-urlencoded</c> form's fields, each read once, off the worker, and kept.
/// </summary>
public sealed class RequestBodyTests
{
    [Fact]
    public void APageReadsItsBodyAsText()
    {
        var app = Load();
        Assert.Equal((200, "héllo wörld|true"),
            Post(app, "/text", Encoding.UTF8.GetBytes("héllo wörld"), "text/plain"));
        Assert.Equal((200, "café|true"),
            Post(app, "/text", [0x63, 0x61, 0x66, 0xE9], "text/plain; charset=\"ISO-8859-1\""));
        Assert.Equal((200, "|true"), Post(app, "/text", [], null));
    }

    [Fact]
    public void APageReadsAFormsFields()
    {
        var (status, body) = Post(Load(), "/form", Encoding.UTF8.GetBytes("a=1&b=tea+%26+caf%C3%A9&a=2&c"), "application/x-www-form-urlencoded");
        Assert.Equal((200, "2|tea &amp; café||a=1&amp;b=tea+%26+caf%C3%A9&amp;a=2&amp;c"), (status, body));
    }

    [Fact]
    public void AHandlerReadsAForm()
    {
        Assert.Equal((200, "Anna"), Post(Load(), "/api/names", Encoding.UTF8.GetBytes("name=Anna"), "application/x-www-form-urlencoded; charset=utf-8"));
    }

    [Fact]
    public void APageReadsItsBodyInPieces()
    {
        // 40,000 bytes, read 16 KiB at a time as they arrive, as bytes.
        var body = new byte[40_000];
        for (var i = 0; i < body.Length; i++) body[i] = (byte)'a';
        Assert.Equal((200, "3 pieces, 40000 bytes, first 97"), Post(Load(), "/pieces", body, "application/octet-stream"));
        Assert.Equal((200, "0 pieces, 0 bytes, first "), Post(Load(), "/pieces", [], null));
    }

    [Fact]
    public void ABodyReadWholeIsOnePieceToRead()
    {
        Assert.Equal((200, "true|"), Post(Load(), "/whole-then-read", Encoding.UTF8.GetBytes("tea"), "text/plain"));
    }

    [Theory]
    [InlineData("/read-then-text", "WL3001: The request&#39;s body is being read in pieces, with request.read(), and cannot be read whole too.")]
    [InlineData("/read-twice", "WL3001: request.read() is called again before the piece it gives has come: await each piece first.")]
    public void ABodyIsReadInPiecesOrWholeNotBoth(string path, string error)
    {
        var (status, body) = Post(Load(), path, Encoding.UTF8.GetBytes("tea"), "text/plain");
        Assert.Equal(500, status);
        Assert.Contains(error, body);
    }

    [Theory]
    [InlineData("/form", "application/json", "WL3001: request.form() reads a form sent as application/x-www-form-urlencoded or multipart/form-data, not as &#39;application/json&#39;.")]
    [InlineData("/form", null, "WL3001: request.form() reads a form sent as application/x-www-form-urlencoded or multipart/form-data, not as &#39;&#39;.")]
    [InlineData("/text", "text/plain; charset=klingon", "WL3001: The request&#39;s body is in the charset &#39;klingon&#39;, which this host cannot read.")]
    public void ABodyThatCannotBeReadSoIsAnErrorWhereItIsRead(string path, string? type, string error)
    {
        var (status, body) = Post(Load(), path, Encoding.UTF8.GetBytes("x"), type);
        Assert.Equal(500, status);
        Assert.Contains(error, body);
    }

    private static WebLingoApplication Load()
    {
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions("/site", new MemoryFileSystem()
            .Add("/site/text.wasp", "<% param request; var t = await request.text(); %><%= t %>|<%= await request.text() == t %>")
            .Add("/site/form.wasp", "<% param request; var f = await request.form(); %><%= f[\"a\"] %>|<%= f[\"b\"] %>|<%= f[\"c\"] %>|"
                                    + "<%= await request.text() %>")
            .Add("/site/api/names.wlg", "export async function post(request, response) { var f = await request.form(); response.write(f[\"name\"]); }")
            .Add("/site/pieces.wasp", "<% param request; var pieces = 0; var bytes = 0; var first = null; var piece = await request.read(); "
                                      + "while (piece != null) { if (first == null) first = piece[0]; pieces = pieces + 1; bytes = bytes + piece.length; "
                                      + "piece = await request.read(); } %><%= pieces %> pieces, <%= bytes %> bytes, first <%= first %>")
            .Add("/site/whole-then-read.wasp", "<% param request; var t = await request.text(); var p = await request.read(); "
                                               + "var q = await request.read(); %><%= p.text() == t %>|<%= q %>")
            .Add("/site/read-then-text.wasp", "<% param request; var p = await request.read(); var t = await request.text(); %>")
            .Add("/site/read-twice.wasp", "<% param request; var a = request.read(); var b = request.read(); %>"))
        {
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry()),
            Scheduler = () => new DotNetSchedulerHost(),
            Mode = WebLingoMode.Development
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }

    private static (int Status, string Body) Post(WebLingoApplication app, string path, byte[] body, string? type)
    {
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("POST", path, headers: type is null ? [] : [("Content-Type", type)], body: body), response,
            new WebLingoWorkerId(0));
        return (response.StatusCode, Encoding.UTF8.GetString(response.BodyBytes));
    }
}
