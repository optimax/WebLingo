namespace WebLingo.Hosting.Abstractions;

public interface IWebLingoHttpRequest { string Method { get; } string Path { get; } }
public interface IWebLingoHttpResponse { int StatusCode { get; set; } }
