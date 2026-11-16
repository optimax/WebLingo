using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// The ASP.NET Core adapter (specification, section 6.1, WL-H009; section 9, "M0.14 parts", part 4), which serves a
/// <see cref="WebLingoApplication"/>, under IIS through the ASP.NET Core Module, or on Kestrel: each request translated to
/// WLHI's, its body read whole first, run on one of the pool's workers, and its response sent back as the worker writes
/// it; a file the
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

    /// <summary>
    /// Serves one request, its response sent as its worker writes it (<see cref="WebLingoStreamingResponse"/>), on a worker
    /// that runs other requests while it waits (section 6.6).
    /// </summary>
    public static async Task Serve(HttpContext context, WebLingoApplication application, string root, WebLingoWorkerPool workers)
    {
        var request = await Translate(context.Request);
        var response = new WebLingoStreamingResponse(context.RequestAborted);
        var cancellation = new WebLingoCancellationSignal();
        // The client gone: the request cancelled first, then, if it waits, resumed to see it.
        using var gone = new CancellationTokenSource();
        void Cancel()
        {
            cancellation.Cancel();
            gone.Cancel();
        }
        using var aborted = context.RequestAborted.Register(Cancel);
        var run = workers.Serve((worker, scheduler) => application.Handle(request, response, worker, cancellation, scheduler), gone.Token,
            response.End);
        try
        {
            await response.SendAsync(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client has gone: nothing more is sent, and the page stops.
            Cancel();
            await run;
            return;
        }
        catch
        {
            // What the client cannot take any more: the page stops too.
            Cancel();
            await run;
            throw;
        }
        if (await run is { Kind: WebLingoRequestOutcomeKind.File, File: { } file }) await SendFile(context, root, file);
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
