using WebLingo.Hosting.Abstractions;
using WebLingo.StdLib;
namespace WebLingo.Hosting;

/// <summary>
/// The responses an application gives when it cannot give a page's (specification, section 6.12, "Errors"): 404 for a path
/// with no route, and 500 for a page that does not compile or fails while it runs, its errors, each
/// <c>file(line,column): error CODE: message</c>, shown only in development, every one escaped as HTML.
/// </summary>
internal static class WebLingoErrorPages
{
    public const string HtmlType = "text/html; charset=utf-8";

    public static void NotFound(IWebLingoHttpResponse response) => Write(response, 404, "Not Found", []);

    public static void Failed(IWebLingoHttpResponse response, WebLingoEnvironment environment, IReadOnlyList<string> errors) =>
        Write(response, 500, "Internal Server Error", environment == WebLingoEnvironment.Development ? errors : []);

    private static void Write(IWebLingoHttpResponse response, int status, string reason, IReadOnlyList<string> errors)
    {
        response.TrySetStatusCode(status);
        response.Headers.Set("Content-Type", HtmlType);
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
