namespace WebLingo.Hosting.Abstractions;

public interface IWsHttpRequest { string Method { get; } string Path { get; } }
public interface IWsHttpResponse { int StatusCode { get; set; } }
