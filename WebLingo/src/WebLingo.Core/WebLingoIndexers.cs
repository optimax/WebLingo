namespace WebLingo.Core;

/// <summary>
/// The names a class's indexer has among its methods (specification, section 3.9): its getter's, which <c>x[i]</c> calls
/// with the index, and its setter's, which <c>x[i] = v</c> calls with the index and the value; names no program can write,
/// which only a native class declares.
/// </summary>
public static class WebLingoIndexers
{
    public const string Getter = "[]";
    public const string Setter = "[]=";
}
