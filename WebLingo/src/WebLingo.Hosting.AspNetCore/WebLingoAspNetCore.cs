using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using WebLingo.Hosting.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// The ASP.NET Core adapter (specification, section 6.1, WL-H009; section 9, "M0.14 parts", part 4), which serves a
/// <see cref="WebLingoApplication"/>, under IIS through the ASP.NET Core Module, or on Kestrel: each request translated to
/// WLHI's, its body read as it arrives, as a script asks for it, run on one of the pool's workers, and its response sent
/// back as the worker writes it; a file the application leaves to its host, a site's own or one a page sent, sent as it is
/// (<see cref="WebLingoFileSending"/>), a site's with the content type its extension gives, and a 404 for an extension
/// ASP.NET Core does not know, as its own static files are served. Only HTTP is translated here: everything else is the
/// host's core's (section 6.12).
/// </summary>
public static class WebLingoAspNetCore
{
    /// <summary>Serves <paramref name="application"/> on <paramref name="workers"/>, as the end of the pipeline.</summary>
    public static IApplicationBuilder UseWebLingo(this IApplicationBuilder builder, WebLingoApplication application, WebLingoWorkerPool workers)
    {
        builder.Run(context => Serve(context, application, workers));
        return builder;
    }

    /// <summary>
    /// Serves one request, its response sent as its worker writes it (<see cref="WebLingoStreamingResponse"/>), on a worker
    /// that runs other requests while it waits (section 6.6).
    /// </summary>
    public static async Task Serve(HttpContext context, WebLingoApplication application, WebLingoWorkerPool workers)
    {
        // The site's limit on a body is its own (section 6.12, "Uploaded files"), not Kestrel's 30,000,000 bytes.
        if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = application.MaxBodySize;
        }
        var request = Translate(context.Request, context.RequestAborted);
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
        // Its time limit, which stops it too, resumed if it waits (section 6.12, "Limits").
        using var timer = WebLingoRequestTimer.Start(application, cancellation, gone);
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
        if (await run is not { Kind: WebLingoRequestOutcomeKind.File, File: { } file } outcome) return;
        var sink = new WebLingoStreamingResponse.AspNetCoreSink(context, context.RequestAborted);
        try
        {
            if (outcome.Sent is { } sent)
            {
                await WebLingoFileSending.SendAsync(sink, request.Method, request.Headers, sent, response.StatusCode, response.Headers);
            }
            else
            {
                await WebLingoFileSending.SendSiteFileAsync(sink, request.Method, request.Headers, application.PathOf(file), application.Headers);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client has gone: nothing more is sent.
        }
    }

    /// <summary>
    /// ASP.NET Core's request as WLHI's: its path without the application's base, its query without the <c>?</c>, and its
    /// body read as it arrives, as a script asks for it, never before the page runs (section 9, "M0.14.7 parts", part 4);
    /// <paramref name="gone"/> signalled when its client has gone.
    /// </summary>
    public static WebLingoBufferedRequest Translate(HttpRequest request, CancellationToken gone = default)
    {
        var headers = new List<(string, string)>();
        foreach (var (name, values) in request.Headers)
        {
            foreach (var value in values) headers.Add((name, value ?? ""));
        }
        var query = request.QueryString.Value ?? "";
        return new WebLingoBufferedRequest(request.Method, request.Path.HasValue ? request.Path.Value : "/",
            query.StartsWith('?') ? query[1..] : query, request.Scheme, request.Host.Value ?? "", headers, new BodyReader(request.Body, gone));
    }

    /// <summary>A request's body as ASP.NET Core gives it, read as it arrives.</summary>
    private sealed class BodyReader(Stream body, CancellationToken gone) : IWebLingoBodyReader
    {
        public int Read(byte[] buffer, int offset, int count)
        {
            var read = -1;
            using var done = new ManualResetEventSlim();
            ReadAsync(buffer, offset, count, n =>
            {
                read = n;
                done.Set();
            });
            done.Wait();
            return read;
        }

        public void ReadAsync(byte[] buffer, int offset, int count, Action<int> done)
        {
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
            {
                done(0);
                return;
            }
            body.ReadAsync(buffer.AsMemory(offset, count), gone).AsTask().ContinueWith(read => done(read.IsCompletedSuccessfully ? read.Result : -1),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }
}
