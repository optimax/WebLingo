using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Primitives;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// The ASP.NET Core adapter (specification, section 6.1, WL-H009; section 9, "M0.14 parts", part 4), which serves a
/// <see cref="WebLingoApplication"/>, under IIS through the ASP.NET Core Module, or on Kestrel: each request translated to
/// WLHI's, its body read whole first, run on one of the pool's workers, and its response translated back; a file the
/// application leaves to its host sent as it is, with the content type its extension gives, and a 404 for an extension
/// ASP.NET Core does not know, as its own static files are served. Only HTTP is translated here: everything else is the
/// host's core's (section 6.12).
/// </summary>
public static class WebLingoAspNetCore
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>Serves <paramref name="application"/>, whose root is <paramref name="root"/>, on <paramref name="workers"/>, as the end of the pipeline.</summary>
    public static IApplicationBuilder UseWebLingo(this IApplicationBuilder builder, WebLingoApplication application, string root,
        WebLingoWorkerPool workers)
    {
        builder.Run(context => Serve(context, application, root, workers));
        return builder;
    }

    /// <summary>Serves one request.</summary>
    public static async Task Serve(HttpContext context, WebLingoApplication application, string root, WebLingoWorkerPool workers)
    {
        var request = await Translate(context.Request);
        var response = new WebLingoBufferedResponse();
        var cancellation = new WebLingoCancellationSignal();
        using var aborted = context.RequestAborted.Register(cancellation.Cancel);
        WebLingoRequestOutcome? outcome = null;
        await workers.Run(worker => outcome = application.Handle(request, response, worker, cancellation));
        if (outcome is { Kind: WebLingoRequestOutcomeKind.File, File: { } file })
        {
            await SendFile(context, root, file);
            return;
        }
        context.Response.StatusCode = response.StatusCode;
        foreach (var name in response.Headers.Names) context.Response.Headers[name] = new StringValues([.. response.Headers.GetValues(name)]);
        var body = response.BodyBytes;
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, context.RequestAborted);
    }

    /// <summary>ASP.NET Core's request as WLHI's: its path without the application's base, its query without the <c>?</c>.</summary>
    public static async Task<WebLingoBufferedRequest> Translate(HttpRequest request)
    {
        var headers = new List<(string, string)>();
        foreach (var (name, values) in request.Headers)
        {
            foreach (var value in values) headers.Add((name, value ?? ""));
        }
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body);
        var query = request.QueryString.Value ?? "";
        return new WebLingoBufferedRequest(request.Method, request.Path.HasValue ? request.Path.Value : "/",
            query.StartsWith('?') ? query[1..] : query, request.Scheme, request.Host.Value ?? "", headers, body.ToArray());
    }

    private static async Task SendFile(HttpContext context, string root, string file)
    {
        if (!ContentTypes.TryGetContentType(file, out var type))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var path = Path.Combine(root, file);
        context.Response.ContentType = type;
        context.Response.ContentLength = new FileInfo(path).Length;
        await context.Response.SendFileAsync(path, context.RequestAborted);
    }
}
