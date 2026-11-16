using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// The host's core (specification, sections 6.3 to 6.6 and 6.12; section 9, "M0.14 parts", part 2), an application whose
/// files are in memory: its routes, its pages run with what they include, are laid out by, render and import, each compiled
/// once on each worker, and the responses of a path with no route and of a page that fails.
/// </summary>
public sealed class ApplicationTests
{
    private const string Root = "/site";

    [Fact]
    public void APageIsFoundByItsPathWithoutItsExtension()
    {
        var app = Load(new MemoryFileSystem()
            .Add("/site/index.wasp", "<h1>Home</h1>")
            .Add("/site/products.wasp", "<% var n = 2; %><p><%= n %> products: café, 😀</p>")
            .Add("/site/docs/index.wasp", "<p>Docs</p>")
            .Add("/site/docs/intro.wasp", "<p>Intro</p>"));
        Assert.Equal((200, "<h1>Home</h1>"), Get(app, "/"));
        Assert.Equal((200, "<p>2 products: café, 😀</p>"), Get(app, "/products"));
        Assert.Equal((200, "<p>Docs</p>"), Get(app, "/docs/"));
        Assert.Equal((200, "<p>Docs</p>"), Get(app, "/docs"));
        Assert.Equal((200, "<p>Intro</p>"), Get(app, "/docs/intro"));

        var response = new WebLingoBufferedResponse();
        Assert.Equal(WebLingoRequestOutcomeKind.Page, app.Handle(Request("/"), response, new WebLingoWorkerId(0)).Kind);
        Assert.Equal((WebLingoResponseState.Completed, "text/html; charset=utf-8"), (response.State, response.Headers.Get("Content-Type")));
    }

    [Theory]
    [InlineData("/missing")]
    [InlineData("/products.wasp")]
    [InlineData("/Products")]
    [InlineData("/index")]
    [InlineData("/lib/util")]
    [InlineData("/lib/util.wl")]
    [InlineData("/app.wlbc")]
    [InlineData("/weblingo.config")]
    [InlineData("/web.config")]
    [InlineData("/.git/config")]
    [InlineData("/../site/index")]
    public void APathWithoutARouteIs404(string path)
    {
        var app = Load(Site());
        var response = new WebLingoBufferedResponse();
        Assert.Equal(WebLingoRequestOutcomeKind.NotFound, app.Handle(Request(path), response, new WebLingoWorkerId(0)).Kind);
        Assert.Equal(404, response.StatusCode);
        Assert.Equal("<!doctype html>\n<html><head><meta charset=\"utf-8\"><title>404 Not Found</title></head>\n<body><h1>404 Not Found"
                     + "</h1>\n</body></html>\n", Encoding.UTF8.GetString(response.BodyBytes));
    }

    [Fact]
    public void AnyOtherFileIsTheHostsToServe()
    {
        var app = Load(Site());
        var response = new WebLingoBufferedResponse();
        Assert.Equal(new WebLingoRequestOutcome(WebLingoRequestOutcomeKind.File, "css/site.css"),
            app.Handle(Request("/css/site.css"), response, new WebLingoWorkerId(0)));
        Assert.Equal(WebLingoResponseState.Uncommitted, response.State);
    }

    [Fact]
    public void TwoFilesOfOneRouteAreAnError()
    {
        var files = new MemoryFileSystem().Add("/site/products.wasp", "a").Add("/site/products.wl", "b");
        Assert.Null(WebLingoApplication.Load(new WebLingoApplicationOptions(Root, files), out var error));
        Assert.Equal("'products.wasp' and 'products.wl' are both the route /products", error);

        Assert.Null(WebLingoApplication.Load(new WebLingoApplicationOptions("/nowhere", files), out error));
        Assert.Equal("the application's root is not a folder", error);
        Assert.Null(WebLingoApplication.Load(new WebLingoApplicationOptions(Root, files) { Workers = 0 }, out error));
    }

    [Fact]
    public void APageHasItsIncludesLayoutComponentsAndImports()
    {
        var files = Site()
            .Add("/site/layouts/main.wasp", "<% param title; %><html><title><%= title %></title><% renderBody(); %></html>")
            .Add("/site/includes/nav.wasp", "<nav><%= title %></nav>")
            .Add("/site/components/card.wasp", "<% param n; import { twice } from \"/lib/util.wl\"; %><b><%= twice(n) %></b>")
            .Add("/site/shop/index.wasp",
                "<% import { twice } from \"../lib/util.wl\"; %><% layout \"/layouts/main.wasp\", { title: \"Shop\" }; %>"
                + "<% var title = \"Menu\"; %><% include \"/includes/nav.wasp\"; %><% writeLn(twice(2)); %>"
                + "<% foreach (var n in [1, 2]) { await renderComponent(\"/components/card.wasp\", { n: n }); } %>");
        var app = Load(files, WebLingoEnvironment.Development);
        var first = Get(app, "/shop");
        Assert.True(first.Status == 200, first.Body);
        Assert.Equal((200, "<html><title>Shop</title><nav>Menu</nav>4\n<b>2</b><b>4</b></html>"), first);

        // Each page, and each component, is compiled once on each worker, and kept: read once more for a second worker only.
        Assert.Equal((200, "<html><title>Shop</title><nav>Menu</nav>4\n<b>2</b><b>4</b></html>"), Get(app, "/shop"));
        Assert.Equal(1, files.Reads["/site/shop/index.wasp"]);
        Assert.Equal(1, files.Reads["/site/components/card.wasp"]);
        Assert.Equal((200, "<html><title>Shop</title><nav>Menu</nav>4\n<b>2</b><b>4</b></html>"), Get(app, "/shop", worker: 1));
        Assert.Equal(2, files.Reads["/site/shop/index.wasp"]);
        Assert.Equal(2, files.Reads["/site/components/card.wasp"]);
    }

    [Fact]
    public void AFailingPageIsA500_ItsErrorShownInDevelopmentOnly()
    {
        var files = Site().Add("/site/fails.wasp", "<p>before</p>\n<%= [\"<a>\"][5] %>");
        var development = Load(files, WebLingoEnvironment.Development);
        var (status, body) = Get(development, "/fails");
        Assert.Equal(500, status);
        Assert.Contains("<h1>500 Internal Server Error</h1>", body);
        Assert.Contains("<pre>fails.wasp(2,", body);
        Assert.Contains("error WL3005:", body);
        Assert.DoesNotContain("before", body);

        (status, body) = Get(Load(files), "/fails");
        Assert.Equal(500, status);
        Assert.DoesNotContain("WL3005", body);
        Assert.DoesNotContain("<pre>", body);
    }

    [Fact]
    public void APageThatDoesNotCompileIsA500_ItsErrorsInTheFilesTheyAreIn()
    {
        var files = Site()
            .Add("/site/bad.wasp", "<% include \"/includes/broken.wasp\"; %>")
            .Add("/site/includes/broken.wasp", "<p>\n<%= nope %></p>")
            .Add("/site/escapes.wasp", "<% include \"../../secret.wasp\"; %>")
            .Add("/site/imports.wasp", "<% import { x } from \"../outside.wl\"; %>");
        var app = Load(files, WebLingoEnvironment.Development);
        var (status, body) = Get(app, "/bad");
        Assert.Equal(500, status);
        Assert.Contains("<pre>includes/broken.wasp(2,5): error WL2001: &#39;nope&#39; is not defined.\n</pre>", body);
        Assert.Contains("cannot be included: it is outside the application&#39;s root.", Get(app, "/escapes").Body);
        Assert.Contains("cannot be imported: it is outside the application&#39;s root.", Get(app, "/imports").Body);
    }

    private static MemoryFileSystem Site() => new MemoryFileSystem()
        .Add("/site/index.wasp", "<h1>Home</h1>")
        .Add("/site/products.wasp", "<p>Products</p>")
        .Add("/site/lib/util.wl", "export function twice(x) { return x * 2; }")
        .Add("/site/css/site.css", "body { }")
        .Add("/site/app.wlbc", "WLBC")
        .Add("/site/weblingo.config", "{ }")
        .Add("/site/web.config", "<configuration/>")
        .Add("/site/.git/config", "secret");

    private static WebLingoApplication Load(MemoryFileSystem files, WebLingoEnvironment environment = WebLingoEnvironment.Production)
    {
        var services = new WebLingoBuiltinRegistry(new WebLingoWriteFunction(), new WebLingoWriteLnFunction(), new WebLingoDelayFunction());
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions(Root, files) { Workers = 2, Services = services, Environment = environment },
            out var error);
        Assert.True(app is not null, error);
        return app;
    }

    private static WebLingoBufferedRequest Request(string path) => new("GET", path);

    private static (int Status, string Body) Get(WebLingoApplication app, string path, int worker = 0)
    {
        var response = new WebLingoBufferedResponse();
        app.Handle(Request(path), response, new WebLingoWorkerId(worker));
        return (response.StatusCode, Encoding.UTF8.GetString(response.BodyBytes));
    }
}
