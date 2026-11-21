using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// The request and the response in scripts (specification, section 6.12; section 9, "M0.14 parts", part 3): a page's
/// <c>param request;</c> and <c>param response;</c>, instances of <c>Request</c> and <c>Response</c>, read and changed
/// through their members; and a <c>.wlg</c> route handler, a module exporting a function for each method it answers.
/// </summary>
public sealed class RequestAndResponseTests
{
    private const string Root = "/site";

    [Fact]
    public void APageReadsItsRequest()
    {
        var app = Load(new MemoryFileSystem().Add("/site/info.wasp",
            "<% param request; %><%= request.method %> <%= request.path %> <%= request.scheme %>://<%= request.host %>\n"
            + "<%= request.query[\"id\"] %> <%= request.query[\"name\"] %> <%= request.query.has(\"none\") %> <%= request.query?[\"none\"] %>|"
            + "<%= request.headers[\"accept\"] %> <%= request.headers.has(\"Accept\") %> <%= request.headers?[\"X-None\"] %>|"
            + "<%= request.query == request.query %> <%= request.headers == request.headers %>"));
        var request = new WebLingoBufferedRequest("POST", "/info", "id=7&name=tea+%26+caf%C3%A9&id=8", "https", "example.com:8443",
            [("Accept", "text/html"), ("accept", "text/plain")]);
        var response = new WebLingoBufferedResponse();
        Assert.Equal(WebLingoRequestOutcomeKind.Page, app.Handle(request, response, new WebLingoWorkerId(0)).Kind);
        Assert.Equal("POST /info https://example.com:8443\n8 tea &amp; café false |text/html, text/plain true |true true", Body(response));
    }

    [Fact]
    public void TheRequestsObjectsOutliveACollection()
    {
        // The headers and the query, which only the request's instance reaches, kept while the page makes and drops enough
        // to collect its heap's garbage (its first collection at 1 MiB units).
        var elements = string.Join(", ", Enumerable.Range(0, 100));
        var app = Load(new MemoryFileSystem().Add("/site/index.wasp",
            "<% param request; param response; var had = request.query.has(\"a\");\n"
            + "var i = 0; while (i < 20000) { var a = [" + elements + "]; i = i + 1; } %>"
            + "<%= had %> <%= request.query[\"a\"] %> <%= request.headers[\"accept\"] %> <%= response.headers.has(\"X\") %>"));
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("GET", "/", "a=1", headers: [("Accept", "text/html")]), response, new WebLingoWorkerId(0));
        Assert.Equal("true 1 text/html false", Body(response));
    }

    [Fact]
    public void APageSetsItsStatusAndHeaders()
    {
        var app = Load(new MemoryFileSystem().Add("/site/gone.wasp",
            "<% param request; param response; response.status = 404; response.headers[\"Content-Type\"] = \"text/plain\";\n"
            + "response.headers.add(\"Set-Cookie\", \"a=1\"); response.headers.add(\"Set-Cookie\", \"b=2\");\n"
            + "response.headers[\"X-Gone\"] = \"yes\"; var removed = response.headers.remove(\"x-gone\"); %>"
            + "gone <%= response.status %> <%= response.headers[\"content-type\"] %> <%= removed %> <%= response.headers.remove(\"X-Gone\") %>"));
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("GET", "/gone"), response, new WebLingoWorkerId(0));
        Assert.Equal((404, "text/plain"), (response.StatusCode, response.Headers.Get("Content-Type")));
        Assert.Equal(["a=1", "b=2"], response.Headers.GetValues("Set-Cookie"));
        Assert.Null(response.Headers.Get("X-Gone"));
        Assert.Equal("gone 404 text/plain true false", Body(response));
    }

    [Theory]
    [InlineData("response.status = 1000;", "WL3001: A response&#39;s status is an integer from 100 to 999, not the integer 1000.")]
    [InlineData("response.status = \"200\";", "WL3001: A response&#39;s status is an integer from 100 to 999, not a string.")]
    [InlineData("response.headers[\"bad name\"] = \"x\";", "WL3001: &#39;bad name&#39; is not a header&#39;s name.")]
    [InlineData("response.headers[\"X-Line\"] = \"a\\nb\";", "WL3001: The value given the header &#39;X-Line&#39; has a character")]
    [InlineData("response.headers[\"X-Count\"] = 5;", "WL3001: A header&#39;s value is a string, not the integer 5.")]
    [InlineData("response.headers[5] = \"x\";", "WL3001: A header&#39;s name is a string, not the integer 5.")]
    [InlineData("request.headers[\"Accept\"] = \"x\";", "WL3001: These headers cannot be changed: a request&#39;s never can")]
    [InlineData("write(request.headers[\"X-None\"]);", "WL3007: There is no header &#39;X-None&#39;.")]
    [InlineData("request.method = \"PUT\";", "WL3001:")]
    public void AWrongStatusOrHeaderIsAnErrorWhereItIsSet(string statement, string error)
    {
        var app = Load(new MemoryFileSystem().Add("/site/wrong.wasp",
            "<% param request; param response; response.headers[\"X-Before\"] = \"set\";\n" + statement + " %>"), WebLingoMode.Development);
        var response = new WebLingoBufferedResponse();
        Assert.Equal(WebLingoRequestOutcomeKind.Failed, app.Handle(new WebLingoBufferedRequest("GET", "/wrong"), response, new WebLingoWorkerId(0)).Kind);
        Assert.Equal(500, response.StatusCode);
        Assert.Contains("<pre>wrong.wasp(2,", Body(response));
        Assert.True(Body(response).Contains(error, StringComparison.Ordinal), Body(response));
        // The 500 has only its own headers, none the page set before it failed.
        Assert.Equal(["Content-Type"], response.Headers.Names);
    }

    [Fact]
    public void AComponentsParametersAreTheHostsUnlessItsCallerGivesThem()
    {
        // A component's parameter of that name is the host's, unless its caller gives another (the owner's answer, 16
        // November 2026); a page that declares none has neither.
        var app = Load(new MemoryFileSystem()
            .Add("/site/index.wasp", "<% param request; %><% await renderComponent(\"/card.wasp\", { request: \"given\" }); %> <%= request.path %>")
            .Add("/site/card.wasp", "<% param request; %><b><%= request %></b>")
            .Add("/site/path.wasp", "<% await renderComponent(\"/path-card.wasp\", {}); %>")
            .Add("/site/path-card.wasp", "<% param request; param response; %><% response.status = 202; %><b><%= request.path %></b>")
            .Add("/site/plain.wasp", "<% var request = 1; %><%= request %>"));
        var given = Get(app, "GET", "/");
        Assert.True(given.Status == 200, given.Body);
        Assert.Equal((200, "<b>given</b> /"), given);
        Assert.Equal((202, "<b>/path</b>"), Get(app, "GET", "/path"));
        Assert.Equal((200, "1"), Get(app, "GET", "/plain"));
    }

    [Fact]
    public void ALayoutHasTheHostsRequest_OrThePagesGiven_AndWhatItIncludesSeesIt()
    {
        // A layout's request and response are the host's, unless its page gives them; a page that gives its own request to
        // its layout made bytecode that failed verification (WL4001), or read its request before it was made (WL2004).
        var app = Load(new MemoryFileSystem()
            .Add("/site/layouts/main.wasp", "<% param title; param request; %><h1><%= title %></h1><% renderBody(); %>"
                + "<% include \"/footer.wasp\"; %>")
            .Add("/site/footer.wasp", "<footer><%= request.path %></footer>")
            .Add("/site/hosted.wasp", "<% layout \"/layouts/main.wasp\", { title: \"Hosted\" }; %><p>body</p>")
            .Add("/site/passed.wasp", "<% param request; %><% layout \"/layouts/main.wasp\", { title: \"Passed\", request: request }; %><p>body</p>")
            .Add("/site/both.wasp", "<% param request; param response; %><% layout \"/layouts/main.wasp\", { title: \"Both\", request: request }; %>"
                + "<% response.status = 201; %><p><%= request.method %></p>"));
        Assert.Equal((200, "<h1>Hosted</h1><p>body</p><footer>/hosted</footer>"), Get(app, "GET", "/hosted"));
        Assert.Equal((200, "<h1>Passed</h1><p>body</p><footer>/passed</footer>"), Get(app, "GET", "/passed"));
        Assert.Equal((201, "<h1>Both</h1><p>GET</p><footer>/both</footer>"), Get(app, "GET", "/both"));
    }

    [Fact]
    public void AHandlerAnswersTheMethodsItExports()
    {
        var app = Load(new MemoryFileSystem()
            .Add("/site/api/teas.wlg",
                "export function get(request, response) {\n  response.headers[\"Content-Type\"] = \"application/json\";\n"
                + "  response.write(\"[\\\"\" + request.query[\"kind\"] + \"\\\"]\");\n}\n"
                + "export async function post(request, response) { response.status = 201; response.write(\"made \" + request.method); }\n"
                + "export function twice(x) { return x * 2; }")
            .Add("/site/lib/util.wlg", "export function twice(x) { return x * 2; }"));
        var response = new WebLingoBufferedResponse();
        var outcome = app.Handle(new WebLingoBufferedRequest("GET", "/api/teas", "kind=green"), response, new WebLingoWorkerId(0));
        Assert.True(outcome.Kind == WebLingoRequestOutcomeKind.Handler, Body(response));
        Assert.Equal((200, "application/json", "[\"green\"]"), (response.StatusCode, response.Headers.Get("Content-Type"), Body(response)));
        Assert.Equal((201, "made POST"), Get(app, "POST", "/api/teas"));
        Assert.Equal((200, "[\"black\"]"), Get(app, "HEAD", "/api/teas", "kind=black"));

        response = new WebLingoBufferedResponse();
        Assert.Equal(WebLingoRequestOutcomeKind.MethodNotAllowed,
            app.Handle(new WebLingoBufferedRequest("PUT", "/api/teas"), response, new WebLingoWorkerId(1)).Kind);
        Assert.Equal((405, "GET, HEAD, POST"), (response.StatusCode, response.Headers.Get("Allow")));
        Assert.Contains("<h1>405 Method Not Allowed</h1>", Body(response));
        Assert.Equal(405, Get(app, "get", "/api/teas").Status);

        // A .wlg exporting no method's function is a module, not a handler.
        Assert.Equal(404, Get(app, "GET", "/lib/util").Status);
    }

    [Fact]
    public void APageOrAHandlerIsRoutedOnlyByANamePortableFilesHave()
    {
        // A file's name on Linux may hold \, : and line breaks, which no portable name has, and which an include or an
        // import cannot name: a page or a handler named so is not routed, a 404, where it was a route that always failed,
        // a 500. A name with " and %> is one, the handler's page naming it in a string literal as it is; any other file
        // named so is sent as it is.
        var app = Load(new MemoryFileSystem()
            .Add("/site/api/tea\\notes.wlg", "export function get(request, response) { response.write(\"notes\"); }")
            .Add("/site/api/two\nlines.wlg", "export function get(request, response) { response.write(\"lines\"); }")
            .Add("/site/tea:time.wasp", "<p>tea</p>")
            .Add("/site/back\\slash/index.wasp", "<p>index</p>")
            .Add("/site/api/say \"hi\" %>.wlg", "export function get(request, response) { response.write(\"quoted\"); }")
            .Add("/site/css/odd\\name.css", "p { }"));
        Assert.Equal(404, Get(app, "GET", "/api/tea\\notes").Status);
        Assert.Equal(404, Get(app, "GET", "/api/two\nlines").Status);
        Assert.Equal(404, Get(app, "GET", "/tea:time").Status);
        Assert.Equal(404, Get(app, "GET", "/back\\slash/").Status);
        Assert.Equal((200, "quoted"), Get(app, "GET", "/api/say \"hi\" %>"));
        var response = new WebLingoBufferedResponse();
        Assert.Equal(WebLingoRequestOutcomeKind.File, app.Handle(new WebLingoBufferedRequest("GET", "/css/odd\\name.css"), response, new WebLingoWorkerId(0)).Kind);
    }

    [Fact]
    public void AHandlerWritesItsResponseWithResponseWrite()
    {
        // response.write and response.writeLn write a value's text as write does, an instance's toString() run, null as
        // null (the owner's answer, 20 November 2026, "Console Module", A2); a page writes so too, beside its own write.
        var app = Load(new MemoryFileSystem()
            .Add("/site/api/text.wlg", "class P { toString() { return \"P!\"; } } export function get(request, response) { "
                                       + "response.write(new P()); response.writeLn(null); response.write([1, \"a\"]); response.writeLn(2.5); }")
            .Add("/site/index.wasp", "<% param response; response.write(\"a\"); write(\"b\"); response.writeLn(\"c\"); writeLn(\"d\"); %>e"));
        Assert.Equal((200, "P!null\n[1, \"a\"]2.5\n"), Get(app, "GET", "/api/text"));
        Assert.Equal((200, "abc\nd\ne"), Get(app, "GET", "/"));
    }

    [Fact]
    public void AHandlerThatFailsIsA500_ItsErrorsInItsFile()
    {
        var app = Load(new MemoryFileSystem()
            .Add("/site/api/fails.wlg", "export function get(request, response) {\n  response.write([1][5]);\n}")
            .Add("/site/api/bad.wlg", "export function get(request, response) {\n  response.write(nope);\n}")
            .Add("/site/api/broken.wlg", "export function get(request, response) {"), WebLingoMode.Development);
        var (status, body) = Get(app, "GET", "/api/fails");
        Assert.Equal(500, status);
        Assert.True(body.Contains("<pre>api/fails.wlg(2,", StringComparison.Ordinal), body);
        Assert.Contains("error WL3005:", body);
        Assert.Contains("<pre>api/bad.wlg(2,18): error WL2001: &#39;nope&#39; is not defined.", Get(app, "GET", "/api/bad").Body);
        (status, body) = Get(app, "GET", "/api/broken");
        Assert.Equal(500, status);
        Assert.Contains("<pre>api/broken.wlg(1,", body);
    }

    private static WebLingoApplication Load(MemoryFileSystem files, WebLingoMode environment = WebLingoMode.Production)
    {
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions(Root, files)
        {
            Workers = 2,
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry()),
            Mode = environment
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }

    private static (int Status, string Body) Get(WebLingoApplication app, string method, string path, string query = "")
    {
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest(method, path, query), response, new WebLingoWorkerId(0));
        return (response.StatusCode, Body(response));
    }

    private static string Body(WebLingoBufferedResponse response) => Encoding.UTF8.GetString(response.BodyBytes);
}
