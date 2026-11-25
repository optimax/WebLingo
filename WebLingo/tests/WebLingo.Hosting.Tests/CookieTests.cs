using System.Text;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// Cookies (specification, section 6.12, "Sessions and cookies"; "Sessions and cookies, parts", part 1): a request's
/// <c>cookies</c>, a map of their names to their values, decoded; and a response's <c>setCookie(name, value, options)</c>
/// and <c>deleteCookie(name, options)</c>, secure by default, <c>HttpOnly</c>, <c>SameSite=Lax</c>, <c>Path=/</c>, and
/// <c>Secure</c> over HTTPS, each changed by its option, a value encoded where it must be.
/// </summary>
public sealed class CookieTests
{
    [Fact]
    public void APageReadsItsRequestsCookies()
    {
        var app = Load(("/site/index.wasp",
            "<% param request; var c = request.cookies; %><%= c[\"tea\"] %>|<%= c[\"name\"] %>|<%= c[\"quoted\"] %>|<%= c[\"empty\"] %>|"
            + "<%= c.has(\"bad name\") %> <%= c?[\"none\"] %> <%= c.length %> <%= c == request.cookies %>"));
        var request = new WebLingoBufferedRequest("GET", "/", headers:
        [
            ("Cookie", "tea=green; name=caf%C3%A9%3B%20au%20lait; quoted=\"q\"; empty=; bad name=1; =x; novalue"),
            ("Cookie", "tea=black")
        ]);
        var response = new WebLingoBufferedResponse();
        app.Handle(request, response, new WebLingoWorkerId(0));
        // A name given twice keeps its first value, as a browser sends the most particular first.
        Assert.Equal("green|café; au lait|q||false  4 true", Body(response));
    }

    [Fact]
    public void APageSetsAndDeletesCookies()
    {
        var app = Load(("/site/index.wasp",
            "<% param response; import { DateTime } from \"std:datetime\";\n"
            + "response.setCookie(\"tea\", \"green\");\n"
            + "response.setCookie(\"name\", \"café; au lait\", { httpOnly: false, sameSite: \"strict\", path: \"/shop\", domain: \"example.com\", maxAge: 3600 });\n"
            + "response.setCookie(\"until\", \"x\", { expires: DateTime.utc(2026, 12, 24, 18, 30), secure: true, sameSite: \"none\" });\n"
            + "response.setCookie(\"tea\", \"black\");\n"
            + "response.deleteCookie(\"old\", { path: \"/shop\" }); %>done"));
        var response = new WebLingoBufferedResponse();
        Assert.Equal(WebLingoRequestOutcomeKind.Page, app.Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0)).Kind);
        Assert.Equal("done", Body(response));
        Assert.Equal(
        [
            "name=caf%C3%A9%3B%20au%20lait; Path=/shop; Domain=example.com; Max-Age=3600; SameSite=Strict",
            "until=x; Path=/; Expires=Thu, 24 Dec 2026 18:30:00 GMT; Secure; HttpOnly; SameSite=None",
            // Set again, of the same name, path and domain, it replaces the cookie set before.
            "tea=black; Path=/; HttpOnly; SameSite=Lax",
            "old=; Path=/shop; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT; HttpOnly; SameSite=Lax"
        ], response.Headers.GetValues("Set-Cookie"));
    }

    [Fact]
    public void ACookieOverHttpsIsSecureByDefault()
    {
        var app = Load(("/site/index.wasp", "<% param response; response.setCookie(\"a\", \"1\"); response.setCookie(\"b\", \"2\", { secure: false }); %>"));
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("GET", "/", "", "https", "example.com"), response, new WebLingoWorkerId(0));
        Assert.Equal(["a=1; Path=/; Secure; HttpOnly; SameSite=Lax", "b=2; Path=/; HttpOnly; SameSite=Lax"], response.Headers.GetValues("Set-Cookie"));
    }

    [Theory]
    [InlineData("response.setCookie(\"bad name\", \"x\");",
        "The first argument of &#39;setCookie&#39;, the name, must be a cookie&#39;s name, a string of letters, digits and")]
    [InlineData("response.setCookie(5, \"x\");", "the name, must be a cookie&#39;s name, a string of letters, digits and !#$%&amp;&#39;*+-.^_`|~, not the integer 5.")]
    [InlineData("response.setCookie(\"a\", 5);", "The second argument of &#39;setCookie&#39;, the value, must be a string, not the integer 5.")]
    [InlineData("response.setCookie(\"a\", \"x\", 5);", "The third argument of &#39;setCookie&#39;, the options, must be a map of the cookie&#39;s options, not the integer 5.")]
    [InlineData("response.setCookie(\"a\", \"x\", { httponly: false });",
        "The third argument of &#39;setCookie&#39;, the options, has &#39;httponly&#39;, which is not an option of &#39;setCookie&#39;; did you mean httpOnly?")]
    [InlineData("response.setCookie(\"a\", \"x\", { colour: 1 });",
        "which is not an option of &#39;setCookie&#39;, whose options are httpOnly, secure, sameSite, path, domain, maxAge and expires.")]
    [InlineData("response.setCookie(\"a\", \"x\", { maxAge: -1 });", "must give maxAge a number of seconds, an integer, 0 or more, not the integer -1.")]
    [InlineData("response.setCookie(\"a\", \"x\", { sameSite: \"loose\" });", "must give sameSite &quot;lax&quot;, &quot;strict&quot; or &quot;none&quot;, not a string.")]
    [InlineData("response.setCookie(\"a\", \"x\", { path: \"shop\" });", "must give path a path from /, without ; or a character outside ASCII, not a string.")]
    [InlineData("response.setCookie(\"a\", \"x\", { sameSite: \"none\" });", "A cookie whose sameSite is &quot;none&quot; must be secure")]
    [InlineData("response.deleteCookie(\"a\", { maxAge: 1 });",
        "The second argument of &#39;deleteCookie&#39;, the options, has &#39;maxAge&#39;, which is not an option of &#39;deleteCookie&#39;, whose options are path and domain.")]
    public void AWrongCookieIsAnErrorWhereItIsSet(string statement, string error)
    {
        var app = Load(("/site/wrong.wasp", "<% param response;\n" + statement + " %>"));
        var response = new WebLingoBufferedResponse();
        Assert.Equal(WebLingoRequestOutcomeKind.Failed, app.Handle(new WebLingoBufferedRequest("GET", "/wrong"), response, new WebLingoWorkerId(0)).Kind);
        Assert.Contains("<pre>wrong.wasp(2,", Body(response));
        Assert.True(Body(response).Contains("WL3001: " + error, StringComparison.Ordinal) || Body(response).Contains(error, StringComparison.Ordinal),
            Body(response));
    }

    [Fact]
    public void ACookieSetOnceTheResponseIsSentIsAnError()
    {
        var log = new List<string>();
        var app = Load(("/site/index.wasp", "<% param response; %>" + new string('x', 70 * 1024) + "<% response.setCookie(\"late\", \"1\"); %>"), log);
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("GET", "/"), response, new WebLingoWorkerId(0));
        Assert.Contains("error WL3001: The cookie 'late' cannot be set: the response has been sent.", ListLog.Error(log));
        Assert.Empty(response.Headers.GetValues("Set-Cookie"));
    }

    private static WebLingoApplication Load((string Path, string Text) file, List<string>? log = null)
    {
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions("/site", new MemoryFileSystem().Add(file.Path, file.Text))
        {
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry()),
            Mode = WebLingoMode.Development,
            Log = log is null ? null : new ListLog(log)
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }

    private static string Body(WebLingoBufferedResponse response) => Encoding.UTF8.GetString(response.BodyBytes);
}
