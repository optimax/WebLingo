using System.Text;
using System.Text.RegularExpressions;
using WebLingo.Hosting.Abstractions;
using WebLingo.Platform.DotNet;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// Cross-site request forgery (specification, section 6.12, "Cross-site request forgery"; the owner's answer, 24 November
/// 2026, A1 (b)): a request that may change things, any method but GET, HEAD and OPTIONS, refused where another site sent
/// it, by its <c>Sec-Fetch-Site</c>, or its <c>Origin</c> where it has none, as Go's <c>CrossOriginProtection</c> refuses
/// one; a page or a handler opting out by a declaration; and a token besides, <c>request.csrfToken</c> and
/// <c>request.checkCsrf()</c>, which a page calls where it wants one.
/// </summary>
public sealed partial class CsrfTests
{
    [Theory]
    [InlineData("POST", "Sec-Fetch-Site", "cross-site", 403)]
    [InlineData("POST", "Sec-Fetch-Site", "same-site", 403)]
    [InlineData("POST", "Sec-Fetch-Site", "same-origin", 200)]
    [InlineData("POST", "Sec-Fetch-Site", "none", 200)]
    [InlineData("POST", "Origin", "http://attacker.example", 403)]
    [InlineData("POST", "Origin", "http://localhost.attacker.example", 403)]
    [InlineData("POST", "Origin", "null", 403)]
    [InlineData("POST", "Origin", "http://localhost", 200)]
    [InlineData("POST", "Origin", "https://LocalHost", 200)]
    [InlineData("POST", null, null, 200)]
    [InlineData("PUT", "Sec-Fetch-Site", "cross-site", 403)]
    [InlineData("PATCH", "Origin", "http://attacker.example", 403)]
    [InlineData("DELETE", "Sec-Fetch-Site", "cross-site", 403)]
    [InlineData("GET", "Sec-Fetch-Site", "cross-site", 200)]
    [InlineData("HEAD", "Origin", "http://attacker.example", 200)]
    [InlineData("OPTIONS", "Sec-Fetch-Site", "cross-site", 200)]
    public void ARequestAnotherSiteSentThatMayChangeThingsIsRefused(string method, string? header, string? value, int status)
    {
        var app = Load(Site);
        Assert.Equal(status, Send(app, method, "/api/item", header is null ? [] : [(header, value!)]).Status);
        if (method is "POST") Assert.Equal(status, Send(app, method, "/page", header is null ? [] : [(header, value!)]).Status);
    }

    [Fact]
    public void ARequestsSecFetchSiteIsTakenBeforeItsOrigin()
    {
        var app = Load(Site);
        Assert.Equal(200, Send(app, "POST", "/page", [("Sec-Fetch-Site", "same-origin"), ("Origin", "http://attacker.example")]).Status);
        Assert.Equal(403, Send(app, "POST", "/page", [("Sec-Fetch-Site", "cross-site"), ("Origin", "http://localhost")]).Status);
    }

    [Fact]
    public void ARefusedRequestRunsNothingAndSaysWhyInDevelopmentOnly()
    {
        var (status, body, _) = Send(Load(Site), "POST", "/page", [("Origin", "http://attacker.example")]);
        Assert.Equal(403, status);
        Assert.DoesNotContain("ran", body);
        Assert.Contains("A POST from another site was refused, as a cross-site request forgery may be: its Origin, "
                        + "&#39;http://attacker.example&#39;, is not this site&#39;s, &#39;localhost&#39;; "
                        + "&#39;crossorigin allowed;&#39; lets a page take one, and "
                        + "&#39;export const crossorigin = &quot;allowed&quot;;&#39; a handler.", body);
        (status, body, _) = Send(Load(Site, WebLingoMode.Production), "POST", "/page", [("Sec-Fetch-Site", "cross-site")]);
        Assert.Equal(403, status);
        Assert.DoesNotContain("refused", body);
        Assert.Contains("Forbidden", body);
    }

    [Fact]
    public void APageOrAHandlerThatDeclaresItTakesRequestsOtherSitesSendTakesThem()
    {
        var app = Load(Site);
        Assert.Equal((200, "open"), Pick(Send(app, "POST", "/open", [("Sec-Fetch-Site", "cross-site")])));
        Assert.Equal((200, "open"), Pick(Send(app, "POST", "/api/open", [("Origin", "http://attacker.example")])));
        Assert.Equal((200, "open"), Pick(Send(app, "DELETE", "/api/open", [("Sec-Fetch-Site", "cross-site")])));
    }

    [Theory]
    [InlineData("export const crossorigin = \"yes\";")]
    [InlineData("export var crossorigin = \"allowed\";")]
    [InlineData("export function crossorigin() { }")]
    public void AHandlersCrossOriginExportThatIsNotTheDeclarationIsAnError(string export)
    {
        var app = Load(("/site/api/h.wlg", export + "\nexport function post(request, response) { response.write(1); }"));
        var (status, body, _) = Send(app, "POST", "/api/h", []);
        Assert.Equal(500, status);
        Assert.Contains("api/h.wlg(1,1): error WL5010: A handler&#39;s &#39;crossorigin&#39; export declares that it takes requests other "
                        + "sites send, &#39;export const crossorigin = &quot;allowed&quot;;&#39;, and is nothing else.", body);
    }

    [Fact]
    public void ATokenIsMadeOnceItsCookieGivenWhereTheRequestHasNone()
    {
        var app = Load(Site);
        var (status, body, response) = Send(app, "GET", "/token", []);
        Assert.Equal(200, status);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Matches("^WASPCSRF=[0-9a-f]{32}; Path=/; HttpOnly; SameSite=Lax$", cookie);
        var token = Token().Match(cookie).Groups[1].Value;
        Assert.Equal($"{token}|{token}", body);
        Assert.NotEqual(token, Token().Match(Assert.Single(Send(app, "GET", "/token", []).Response.Headers.GetValues("Set-Cookie"))).Groups[1].Value);

        // A token the browser sends back is the request's, and no cookie is set again.
        (status, body, response) = Send(app, "GET", "/token", [("Cookie", "other=1; WASPCSRF=" + token)]);
        Assert.Equal((200, $"{token}|{token}"), (status, body));
        Assert.Empty(response.Headers.GetValues("Set-Cookie"));

        // One that is not a token csrfToken makes is replaced.
        response = Send(app, "GET", "/token", [("Cookie", "WASPCSRF=0123456789ABCDEF0123456789ABCDEF")]).Response;
        Assert.Matches("^WASPCSRF=[0-9a-f]{32}; ", Assert.Single(response.Headers.GetValues("Set-Cookie")));

        // Over HTTPS, its cookie is secure.
        var secure = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest("GET", "/token", scheme: "https"), secure, new WebLingoWorkerId(0));
        Assert.Matches("^WASPCSRF=[0-9a-f]{32}; Path=/; Secure; HttpOnly; SameSite=Lax$", Assert.Single(secure.Headers.GetValues("Set-Cookie")));
    }

    [Theory]
    [InlineData("csrfToken=TOKEN", "application/x-www-form-urlencoded", null, "true")]
    [InlineData("csrfToken=0123456789abcdef0123456789abcdef", "application/x-www-form-urlencoded", null, "false")]
    [InlineData("other=1", "application/x-www-form-urlencoded", null, "false")]
    [InlineData("{}", "application/json", "TOKEN", "true")]
    [InlineData("{}", "application/json", "0123456789abcdef0123456789abcdef", "false")]
    [InlineData("{}", "application/json", null, "false")]
    [InlineData("csrfToken=0123456789abcdef0123456789abcdef", "application/x-www-form-urlencoded", "TOKEN", "true")]
    public void ATokenIsCheckedAgainstTheRequestsCookie(string form, string type, string? header, string expected)
    {
        var app = Load(Site);
        var token = Token().Match(Assert.Single(Send(app, "GET", "/token", []).Response.Headers.GetValues("Set-Cookie"))).Groups[1].Value;
        var headers = new List<(string, string)> { ("Cookie", "WASPCSRF=" + token), ("Content-Type", type) };
        if (header is not null) headers.Add(("X-CSRF-Token", header.Replace("TOKEN", token)));
        Assert.Equal((200, expected), Pick(Send(app, "POST", "/check", headers, form.Replace("TOKEN", token))));

        // Without its cookie, no token is the request's.
        headers.RemoveAt(0);
        Assert.Equal((200, "false"), Pick(Send(app, "POST", "/check", headers, form.Replace("TOKEN", token))));
    }

    [Fact]
    public void ATokenGivenIsChecked_AndTheFormStillRead()
    {
        var app = Load(Site);
        var token = Token().Match(Assert.Single(Send(app, "GET", "/token", []).Response.Headers.GetValues("Set-Cookie"))).Groups[1].Value;
        (string, string)[] headers = [("Cookie", "WASPCSRF=" + token), ("Content-Type", "application/x-www-form-urlencoded")];
        Assert.Equal((200, "true|tea"), Pick(Send(app, "POST", "/given", headers, $"t={token}&name=tea")));
        Assert.Equal((200, "false|tea"), Pick(Send(app, "POST", "/given", headers, "t=x&name=tea")));
        var (status, body, _) = Send(app, "POST", "/wrong", headers, "");
        Assert.Equal(500, status);
        Assert.Contains("WL3001: The argument of &#39;checkCsrf&#39; must be a string, or null, not the integer 5.", body);
    }

    private static readonly (string, string)[] Site =
    [
        ("/site/page.wasp", "<% param request; %>ran"),
        ("/site/open.wasp", "<% crossorigin allowed; %>open"),
        ("/site/api/item.wlg", "export function get(request, response) { response.write(\"item\"); }\n"
                               + "export function head(request, response) { }\n"
                               + "export function options(request, response) { }\n"
                               + "export function post(request, response) { response.write(\"ran\"); }\n"
                               + "export function put(request, response) { response.write(\"ran\"); }\n"
                               + "export function patch(request, response) { response.write(\"ran\"); }\n"
                               + "export function delete(request, response) { response.write(\"ran\"); }"),
        ("/site/api/open.wlg", "export const crossorigin = \"allowed\";\n"
                               + "export function post(request, response) { response.write(\"open\"); }\n"
                               + "export function delete(request, response) { response.write(\"open\"); }"),
        ("/site/token.wasp", "<% param request; %><%= request.csrfToken %>|<%= request.csrfToken %>"),
        ("/site/check.wasp", "<% param request; %><%= await request.checkCsrf() %>"),
        ("/site/given.wasp", "<% param request; var f = await request.form(); %><%= await request.checkCsrf(f[\"t\"]) %>|<%= f[\"name\"] %>"),
        ("/site/wrong.wasp", "<% param request; await request.checkCsrf(5); %>")
    ];

    [GeneratedRegex("^WASPCSRF=([0-9a-f]{32});")]
    private static partial Regex Token();

    private static (int, string) Pick((int Status, string Body, WebLingoBufferedResponse Response) sent) => (sent.Status, sent.Body);

    private static (int Status, string Body, WebLingoBufferedResponse Response) Send(WebLingoApplication app, string method, string path,
        IEnumerable<(string, string)> headers, string? body = null)
    {
        var response = new WebLingoBufferedResponse();
        app.Handle(new WebLingoBufferedRequest(method, path, headers: headers, body: body is null ? null : Encoding.UTF8.GetBytes(body)), response,
            new WebLingoWorkerId(0));
        return (response.StatusCode, Encoding.UTF8.GetString(response.BodyBytes), response);
    }

    private static WebLingoApplication Load(params (string Path, string Text)[] files) => Load(files, WebLingoMode.Development);

    private static WebLingoApplication Load((string Path, string Text)[] files, WebLingoMode mode)
    {
        var site = new MemoryFileSystem();
        foreach (var (path, text) in files) site.Add(path, text);
        var app = WebLingoApplication.Load(new WebLingoApplicationOptions("/site", site)
        {
            WorkerServices = _ => new WebLingoWorkerServices(new WebLingoBuiltinRegistry()),
            Scheduler = () => new DotNetSchedulerHost(),
            Mode = mode,
            Crypto = new DotNetCrypto()
        }, out var error);
        Assert.True(app is not null, error);
        return app;
    }
}
