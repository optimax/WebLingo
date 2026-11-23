using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Platform.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// Files uploaded by a form (specification, section 6.12, "Uploaded files"): a <c>multipart/form-data</c> body read as it
/// arrives, <c>await request.form()</c> its fields, <c>await request.files()</c> its files, each input's name an array of
/// <c>UploadedFile</c>s; a file past the site's threshold kept in a temporary file, removed when the request ends; the
/// body's limits, 413 before it is read where its <c>Content-Length</c> passes them, an error where it is read otherwise;
/// and <c>file.save(path)</c> within the site's file capabilities.
/// </summary>
public sealed class UploadTests : IDisposable
{
    private const string Boundary = "----WebLingoTest7MA4YWxkTrZu0gW";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-uploads-" + Guid.NewGuid().ToString("N"));
    private readonly Temporary _temporary = new();

    public UploadTests() => Directory.CreateDirectory(Path.Combine(_folder, "uploads"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A multipart body: each part its headers' lines and its content.</summary>
    private static byte[] Multipart(params (string Headers, string Content)[] parts)
    {
        var body = new StringBuilder();
        foreach (var (headers, content) in parts) body.Append("--").Append(Boundary).Append("\r\n").Append(headers).Append("\r\n\r\n").Append(content).Append("\r\n");
        body.Append("--").Append(Boundary).Append("--\r\n");
        return Encoding.UTF8.GetBytes(body.ToString());
    }

    private static string Field(string name) => $"Content-Disposition: form-data; name=\"{name}\"";

    private static string File(string name, string fileName, string type = "text/plain") =>
        $"Content-Disposition: form-data; name=\"{name}\"; filename=\"{fileName}\"\r\nContent-Type: {type}";

    private const string Type = "multipart/form-data; boundary=" + Boundary;

    [Fact]
    public void AFormsFieldsAndFilesAreRead()
    {
        var body = Multipart((Field("a"), "1"), (Field("b"), "tea & café"), (Field("a"), "2"),
            (File("photos", "one.txt"), "first"), (File("photos", "two.txt"), "second file"),
            (File("avatar", "me.png", "image/png"), "PNG"), (File("empty", ""), ""),
            ("Content-Disposition: form-data; name=\"x\"; filename*=utf-8''r%C3%A9sum%C3%A9.txt", "résumé"));
        var (status, page) = Post(Load(), "/both", body, Type);
        Assert.Equal((200, "2|tea &amp; café|avatar,photos,x|one.txt text/plain 5 first; two.txt text/plain 11 second file; "
                           + "|me.png image/png 3 PNG|résumé.txt application/octet-stream 8 résumé|photos|true"), (status, page));
        Assert.Equal(0, _temporary.Made);
    }

    [Fact]
    public void AFileLargerThanTheThresholdIsKeptInATemporaryFileUntilItsRequestEnds()
    {
        var large = new string('x', 100) + "end";
        var (status, page) = Post(Load(new WebLingoUploads(1_000_000, 16, 1_000_000, _temporary)), "/one", Multipart((File("doc", "large.txt"), large)), Type);
        Assert.Equal((200, "large.txt 103 " + large), (status, page));
        Assert.Equal((1, 0), (_temporary.Made, _temporary.Live)); // Made, then removed as the request ended.
    }

    [Fact]
    public void ABodyLargerThanTheSiteTakesIsRefused()
    {
        var body = Multipart((File("doc", "a.txt"), new string('x', 200)));
        var app = Load(new WebLingoUploads(100, 16, 100, _temporary));
        // Its Content-Length too large: 413, before the page runs.
        var response = new WebLingoBufferedResponse();
        var outcome = app.Handle(new WebLingoBufferedRequest("POST", "/one", headers: [("Content-Type", Type), ("Content-Length", body.Length.ToString())],
            body: body), response, new WebLingoWorkerId(0));
        Assert.Equal((WebLingoRequestOutcomeKind.TooLarge, 413), (outcome.Kind, response.StatusCode));
        // Sent without one: an error where it is read, once it passes the limit.
        var (status, page) = Post(app, "/one", body, Type);
        Assert.Equal(500, status);
        Assert.Contains("WL3001: The request&#39;s body is larger than the 100 bytes this site takes.", page);
        // Its files larger than "uploads"' "maxSize", the body not.
        (status, page) = Post(Load(new WebLingoUploads(10_000, 16, 50, _temporary)), "/one", body, Type);
        Assert.Equal(500, status);
        Assert.Contains("WL3001: The form&#39;s files are larger than the 50 bytes this site takes.", page);
        Assert.Equal(0, _temporary.Live);
    }

    [Fact]
    public void AFileIsSavedWhereTheSiteMayWrite()
    {
        var body = Multipart((File("doc", "notes.txt"), "Sencha and Genmaicha"));
        var app = Load(new WebLingoUploads(1_000_000, 4, 1_000_000, _temporary), saving: true);
        Assert.Equal((200, "saved"), Post(app, "/save", body, Type));
        Assert.Equal("Sencha and Genmaicha", System.IO.File.ReadAllText(Path.Combine(_folder, "uploads", "notes.txt")));
        Assert.Equal((200, "WL3016"), Post(app, "/save-outside", body, Type));
        Assert.Equal(0, _temporary.Live);
    }

    [Theory]
    [InlineData("/read-then-files", Type, "WL3001: The request&#39;s body is being read in pieces, with request.read(), and cannot be read as a form too.")]
    [InlineData("/files-then-read", Type, "WL3001: The request&#39;s body has been read as a form, with request.form() or request.files(), and cannot be read again.")]
    [InlineData("/files-then-text", Type, "WL3001: The request&#39;s body has been read as a form, with request.form() or request.files(), and cannot be read again.")]
    [InlineData("/one", "application/json", "WL3001: request.files() reads a form sent as multipart/form-data, not as &#39;application/json&#39;.")]
    [InlineData("/one", "multipart/form-data", "WL3001: The request&#39;s form is sent as multipart/form-data without a boundary.")]
    public void AFormIsReadOneWay(string path, string type, string error)
    {
        var (status, page) = Post(Load(), path, Multipart((File("doc", "a.txt"), "x")), type);
        Assert.Equal(500, status);
        Assert.Contains(error, page);
    }

    [Fact]
    public void AnUrlEncodedFormHasNoFiles()
    {
        Assert.Equal((200, "0"), Post(Load(), "/count", Encoding.UTF8.GetBytes("a=1"), "application/x-www-form-urlencoded"));
    }

    [Fact]
    public void ABodyThatIsNotTheFormItsTypeSaysIsAnError()
    {
        var whole = Multipart((File("doc", "a.txt"), "x"));
        var (status, page) = Post(Load(), "/one", whole[..(whole.Length - 20)], Type);
        Assert.Equal(500, status);
        Assert.Contains("WL3001: The form&#39;s body ended before its last part did.", page);
        (status, page) = Post(Load(), "/one", Encoding.UTF8.GetBytes("no parts here"), Type);
        Assert.Contains("WL3001: The form&#39;s body has no part its boundary begins.", page);
    }

    private WebLingoApplication Load(WebLingoUploads? uploads = null, bool saving = false)
    {
        var files = saving ? new DotNetFileSystem(new DotNetFileCapabilities(_folder, [], [Path.Combine(_folder, "uploads")])) : null;
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions("/site", new MemoryFileSystem()
            .Add("/site/both.wasp", "<% param request; var f = await request.form(); var files = await request.files(); %>"
                                    + "<% var names = []; foreach (var (k, v) in files) names.push(k); %>"
                                    + "<%= f[\"a\"] %>|<%= f[\"b\"] %>|<%= \",\".join(names.sort()) %>|"
                                    + "<% foreach (var p in files[\"photos\"]) { %><%= p.fileName %> <%= p.type %> <%= p.size %> <%= await p.text() %>; <% } %>"
                                    + "|<%= files[\"avatar\"][0].fileName %> <%= files[\"avatar\"][0].type %> <%= files[\"avatar\"][0].size %> "
                                    + "<%= (await files[\"avatar\"][0].bytes()).text() %>|<% var x = files[\"x\"][0]; %><%= x.fileName %> <%= x.type %> "
                                    + "<%= x.size %> <%= await x.text() %>|<%= files[\"photos\"][1].name %>|<%= files == await request.files() %>")
            .Add("/site/one.wasp", "<% param request; var d = (await request.files())[\"doc\"][0]; %><%= d.fileName %> <%= d.size %> <%= await d.text() %>")
            .Add("/site/count.wasp", "<% param request; %><%= (await request.files()).length %>")
            .Add("/site/save.wasp", "<% param request; var d = (await request.files())[\"doc\"][0]; await d.save(\"uploads/\" + d.fileName); %>saved")
            .Add("/site/save-outside.wasp", "<% param request; var d = (await request.files())[\"doc\"][0]; "
                                            + "try { await d.save(\"elsewhere.txt\"); } catch (FilePermissionError e) { write(e.code); } %>")
            .Add("/site/read-then-files.wasp", "<% param request; var p = await request.read(); var f = await request.files(); %>")
            .Add("/site/files-then-read.wasp", "<% param request; var f = await request.files(); var p = await request.read(); %>")
            .Add("/site/files-then-text.wasp", "<% param request; var f = await request.files(); var t = await request.text(); %>"))
        {
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry()),
            Scheduler = () => new DotNetSchedulerHost(),
            Mode = WebLingoMode.Development,
            Uploads = uploads ?? new WebLingoUploads(1_000_000, 64 * 1024, 1_000_000, _temporary),
            ScriptFileSystem = files
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }

    private static (int Status, string Body) Post(WebLingoApplication app, string path, byte[] body, string type)
    {
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("POST", path, headers: [("Content-Type", type)], body: body), response, new WebLingoWorkerId(0));
        return (response.StatusCode, Encoding.UTF8.GetString(response.BodyBytes));
    }

    /// <summary>Temporary files in memory, counted as they are made and removed.</summary>
    private sealed class Temporary : IWebLingoTemporaryFiles
    {
        private int _made, _live;

        public int Made => Volatile.Read(ref _made);
        public int Live => Volatile.Read(ref _live);

        public IWebLingoTemporaryFile Create()
        {
            Interlocked.Increment(ref _made);
            Interlocked.Increment(ref _live);
            return new Kept(this);
        }

        private sealed class Kept(Temporary owner) : IWebLingoTemporaryFile
        {
            private readonly List<byte> _bytes = [];
            private int _disposed;

            public long Length => _bytes.Count;

            public void Write(byte[] buffer, int offset, int count) => _bytes.AddRange(buffer.AsSpan(offset, count).ToArray());

            public int Read(long position, byte[] buffer, int offset, int count)
            {
                var read = (int)Math.Max(0, Math.Min(count, _bytes.Count - position));
                _bytes.CopyTo((int)position, buffer, offset, read);
                return read;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref owner._live);
            }
        }
    }
}
