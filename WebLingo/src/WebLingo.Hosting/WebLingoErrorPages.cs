using WebLingo.Hosting.Abstractions;
using WebLingo.StdLib;
namespace WebLingo.Hosting;

/// <summary>
/// The responses an application gives when it cannot give a page's (specification, section 6.12, "Errors"): 404 for a path
/// with no route, 405 for a method a route handler does not answer, and 500 for a page that does not compile or fails while
/// it runs, its errors, each <c>file(line,column): error CODE: message</c>, shown only in development, every one escaped as
/// HTML. Each has only the headers it sets, any a page set before it failed removed.
/// </summary>
internal static class WebLingoErrorPages
{
    public const string HtmlType = "text/html; charset=utf-8";

    public static void NotFound(IWebLingoHttpResponse response) => Write(response, 404, "Not Found", []);

    /// <summary>A 405, its <c>Allow</c> the methods the handler answers, as <c>GET, HEAD, POST</c>.</summary>
    public static void MethodNotAllowed(IWebLingoHttpResponse response, string allow) => Write(response, 405, "Method Not Allowed", [], allow);

    /// <summary>A 413, a request whose body is larger than its site takes (section 6.12, "Uploaded files").</summary>
    public static void TooLarge(IWebLingoHttpResponse response) => Write(response, 413, "Content Too Large", []);

    public static void Failed(IWebLingoHttpResponse response, WebLingoMode mode, IReadOnlyList<string> errors) =>
        Write(response, 500, "Internal Server Error", mode == WebLingoMode.Development ? errors : []);

    private static void Write(IWebLingoHttpResponse response, int status, string reason, IReadOnlyList<string> errors, string? allow = null)
    {
        response.TrySetStatusCode(status);
        foreach (var name in new List<string>(response.Headers.Names)) response.Headers.Remove(name);
        response.Headers.Set("Content-Type", HtmlType);
        if (allow is not null) response.Headers.Set("Allow", allow);
        var title = $"{status} {reason}";
        var page = $"<!doctype html>\n<html><head><meta charset=\"utf-8\"><title>{title}</title></head>\n<body><h1>{title}</h1>\n";
        if (errors.Count > 0)
        {
            page += "<pre>";
            foreach (var error in errors) page += WebLingoTemplateWriteFunction.Escape(error) + "\n";
            page += "</pre>\n";
        }
        WebLingoResponseText.WriteTo(response, page + "</body></html>\n");
        response.Complete();
    }
}
