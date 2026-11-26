using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using WebLingo.Hosting.Abstractions;
using WebLingo.Hosting.DotNet;
namespace WebLingo.Hosting.AspNetCore;

/// <summary>
/// ASP.NET Core's response as a sink of a streaming response's pieces (<see cref="IWebLingoResponseSink"/>): its status and
/// headers set, its body written, an abort its connection's.
/// </summary>
public sealed class WebLingoAspNetCoreSink(HttpContext context, CancellationToken gone) : IWebLingoResponseSink
{
    public ValueTask StartAsync(int status, IWebLingoHeaderCollection headers, long? length)
    {
        var response = context.Response;
        response.StatusCode = status;
        foreach (var name in headers.Names) response.Headers[name] = new StringValues([.. headers.GetValues(name)]);
        if (length is { } bytes) response.ContentLength = bytes;
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteAsync(byte[] bytes) => context.Response.Body.WriteAsync(bytes, gone);

    public async ValueTask AbortAsync()
    {
        await context.Response.Body.FlushAsync(gone);
        context.Abort();
    }
}
