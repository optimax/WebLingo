namespace WebLingo.Hosting.DotNet;

/// <summary>
/// What a file is sent as (<see cref="WebLingoFileSending.Plan"/>): its status, the length its <c>Content-Length</c> gives,
/// or null for none, the first of its bytes sent and how many, and whether a body is sent.
/// </summary>
public sealed record WebLingoFilePlan(int Status, long? Length, long Offset, long Count, bool Body);
