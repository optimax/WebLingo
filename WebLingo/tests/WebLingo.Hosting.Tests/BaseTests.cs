using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// A site over a base (specification, section 6.12, "A site over a base"): its pages, includes, layouts, components,
/// modules and other files read from its own folder where it has them, else from its base's, its routes those of both,
/// a file its host sends found where it is, and a file added to the base after it loaded routed once it reloads.
/// </summary>
public sealed class BaseTests
{
    private static MemoryFileSystem Files() => new MemoryFileSystem()
        .Add("/forum/index.wasp", "<% layout \"/layouts/main.wasp\", {}; %><% include \"/includes/head.wasp\"; %>the forum's index")
        .Add("/forum/layouts/main.wasp", "[<% await renderBody(); %>]")
        .Add("/forum/includes/head.wasp", "the forum's head|")
        .Add("/forum/about.wasp", "<% import { name } from \"/lib/names.wlg\"; %><% await renderComponent(\"/components/card.wasp\", { who: name() }); %>")
        .Add("/forum/components/card.wasp", "<% param who; %>card of <%= who %>")
        .Add("/forum/lib/names.wlg", "export function name() { return \"the forum\"; }")
        .Add("/forum/css/site.css", "h1 { color: blue; }")
        .Add("/forum/wasp.config", "{ }")
        .Add("/site/includes/head.wasp", "Acme's head|")
        .Add("/site/lib/names.wlg", "export function name() { return \"Acme\"; }")
        .Add("/site/extra.wasp", "Acme's own page")
        .Add("/site/css/logo.svg", "<svg/>");

    [Fact]
    public void ASitesFilesComeBeforeItsBases()
    {
        var app = Load(Files());
        Assert.Equal("/forum", app.Base);
        Assert.Equal((200, "[Acme's head|the forum's index]"), Get(app, "/"));
        Assert.Equal((200, "card of Acme"), Get(app, "/about"));
        Assert.Equal((200, "Acme's own page"), Get(app, "/extra"));
        Assert.Equal(404, Get(app, "/wasp.config").Status);
        Assert.Equal(404, Get(app, "/missing").Status);
    }

    [Fact]
    public void AFileItsHostSendsIsFoundWhereItIs()
    {
        var app = Load(Files());
        var response = new WebLingoBufferedResponse();
        Assert.Equal((WebLingoRequestOutcomeKind.File, "css/site.css"),
            (app.Handle(new WebLingoBufferedRequest("GET", "/css/site.css"), response, new WebLingoWorkerId(0)) is var outcome ? outcome.Kind : default,
             outcome.File));
        Assert.Equal("/memory//forum/css/site.css", app.PathOf("css/site.css"));
        Assert.Equal("/memory//site/css/logo.svg", app.PathOf("css/logo.svg"));
    }

    [Fact]
    public void AFileAddedToTheBaseIsRoutedOnceTheSiteReloads()
    {
        var files = Files();
        var app = Load(files);
        Assert.Equal(404, Get(app, "/news").Status);
        files.Add("/forum/news.wasp", "the forum's news");
        files.Add("/site/about.wasp", "Acme's about");
        Assert.True(app.TryReload(out var error), error);
        Assert.Equal((200, "the forum's news"), Get(app, "/news"));
        Assert.Equal((200, "Acme's about"), Get(app, "/about"));
    }

    [Fact]
    public void BaseFilesListBothFoldersOnce()
    {
        var files = new WebLingoBaseFiles(Files(), "/site", "/forum");
        // The site's names first, then the base's it has not.
        Assert.Equal(["css", "extra.wasp", "includes", "lib", "about.wasp", "components", "index.wasp", "layouts", "wasp.config"], files.List("/site"));
        Assert.Equal(["head.wasp"], files.List("/site/includes"));
        Assert.True(files.IsFolder("/site/layouts"));
        Assert.True(files.Exists("/site/about.wasp"));
        Assert.False(files.Exists("/site/none.wasp"));
        Assert.Equal("Acme's head|", files.ReadText("/site/includes/head.wasp", "utf-8"));
        Assert.Equal("[<% await renderBody(); %>]", files.ReadText("/site/layouts/main.wasp", "utf-8"));
        // Outside the root, a path is the files' own.
        Assert.False(files.Exists("/other/about.wasp"));
        Assert.Equal("{ }", files.ReadText("/forum/wasp.config", "utf-8"));
    }

    private static WebLingoApplication Load(MemoryFileSystem files)
    {
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions("/site", files)
        {
            Base = "/forum",
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry(new WebLingoDelayFunction())),
            Mode = WebLingoMode.Development
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
