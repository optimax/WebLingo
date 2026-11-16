using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using WebLingo.Cli;
using WebLingo.Hosting;
using WebLingo.Wasp.Cli;
using Xunit;
namespace WebLingo.Templates.Tests;

/// <summary>
/// <c>wasp serve</c> (specification, sections 6.12 and 8.4; section 9, "M0.14 parts", part 5), end to end: an application
/// on disk, served on Kestrel on a port of the system's choosing, and asked for its pages and files over HTTP.
/// </summary>
public sealed class WaspServeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wasp-serve-" + Guid.NewGuid().ToString("N"));

    public WaspServeTests()
    {
        Write("layouts/main.wasp", "<% param title; %><html><title><%= title %></title><body><% renderBody(); %></body></html>");
        Write("index.wasp", "<% layout \"/layouts/main.wasp\", { title: \"Home\" }; %><h1>Hello</h1><% writeLn(\"from std:system\"); %>");
        Write("broken.wasp", "<%= [1][2] %>");
        Write("css/site.css", "h1 { }");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task AnApplicationIsServedOverHttp()
    {
        var server = WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out var error, out var environment);
        Assert.True(server is not null, error);
        Assert.Equal(WebLingoEnvironment.Development, environment);
        await server.StartAsync();
        try
        {
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            var page = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal("text/html; charset=utf-8", page.Content.Headers.ContentType?.ToString());
            Assert.Equal("<html><title>Home</title><body><h1>Hello</h1>from std:system\n</body></html>", await page.Content.ReadAsStringAsync());

            var css = await client.GetAsync("/css/site.css");
            Assert.Equal((HttpStatusCode.OK, "text/css"), (css.StatusCode, css.Content.Headers.ContentType?.MediaType));
            Assert.Equal("h1 { }", await css.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/index.wasp")).StatusCode);
            var broken = await client.GetAsync("/broken");
            Assert.Equal(HttpStatusCode.InternalServerError, broken.StatusCode);
            Assert.Contains("broken.wasp(1,", await broken.Content.ReadAsStringAsync());
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task TheExampleApplicationServesItsPages()
    {
        // Examples/example1, at the repository's root: the owner's example of a website built with WASP (M0.14.5).
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "Examples", "example1", "site"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var server = WebLingoWaspServer.Create(Path.Combine(folder.FullName, "Examples", "example1", "site"), 0,
            new Dictionary<string, string>(), out var error, out _);
        Assert.True(server is not null, error);
        await server.StartAsync();
        try
        {
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            foreach (var (path, text) in new[]
                     {
                         ("/", "<h3>Darjeeling</h3>"), ("/teas", "Brew at 90 °C (194 °F) for 3 minutes."),
                         ("/brewing", "<tr><td>Assam</td><td>100 °C, 212 °F</td><td>4 minutes</td></tr>"),
                         ("/about", "Tea &amp; patience &lt;always&gt;"), ("/css/site.css", "--leaf")
                     })
            {
                var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains(text, await response.Content.ReadAsStringAsync());
            }
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/lib/teas.wl")).StatusCode);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public void TheConfigurationNamesTheEnvironment()
    {
        Write("weblingo.config", "{ \"environment\": \"production\" }");
        Assert.NotNull(WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out _, out var environment));
        Assert.Equal(WebLingoEnvironment.Production, environment);

        Write("weblingo.config", "{ \"environment\": \"staging\" }");
        Assert.Null(WebLingoWaspServer.Create(_root, 0, new Dictionary<string, string>(), out var error, out _));
        Assert.EndsWith("weblingo.config: \"environment\" is \"development\" or \"production\".", error);
    }

    [Fact]
    public void ServeTakesARootAPortAndSchemesOnly()
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoWaspCommandLine.Run(["serve", "--port", "http"], output, error));
        Assert.StartsWith("Usage: wasp serve [--root <folder>] [--port <port>]", output.ToString());
        Assert.Equal(WebLingoCommandLine.UsageError, WebLingoWaspCommandLine.Run(["serve", "--root", "a", "--root", "b"], output, error));
        Assert.Equal(WebLingoCommandLine.FileUnreadable,
            WebLingoWaspCommandLine.Run(["serve", "--root", Path.Combine(_root, "missing")], new StringWriter(), error = new StringWriter()));
        Assert.Contains("the application's root is not a folder", error.ToString());
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
