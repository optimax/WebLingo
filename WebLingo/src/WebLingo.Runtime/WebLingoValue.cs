namespace WebLingo.Runtime;

/// <summary>
/// The kinds of runtime value. A String, a Function (a closure), a Cell, an Array, a Map, a Set, a Class, an Instance and
/// a BoundMethod (a method and the instance it runs on) refer to an object of a heap by
/// handle; a Builtin is a host built-in, by its import index; a Cell is never a script's value, only a frame's.
/// </summary>
public enum WebLingoValueKind : byte { Null, Boolean, Int64, Binary64, String, Function, Builtin, Cell, Array, Map, Set, Class, Instance, BoundMethod, Missing, Task }

/// <summary>
/// A runtime value (specification, section 4.1): a kind and a 64-bit payload, 16 bytes in all. M0.6 has null,
/// Boolean, int64 and binary64 (section 8.4, "Runtime values"); a binary64 is kept as its IEEE 754 bits.
/// </summary>
public readonly struct WebLingoValue : IEquatable<WebLingoValue>
{
    private readonly long _bits;

    private WebLingoValue(WebLingoValueKind kind, long bits)
    {
        Kind = kind;
        _bits = bits;
    }

    public WebLingoValueKind Kind { get; }

    public static WebLingoValue Null => default;

    /// <summary>
    /// What a VM puts in the place of a parameter its caller left out, which the function's code replaces with its
    /// default value before anything else reads it (section 3.5); never a program's value.
    /// </summary>
    public static WebLingoValue Missing => new(WebLingoValueKind.Missing, 0);
    public static WebLingoValue True => new(WebLingoValueKind.Boolean, 1);
    public static WebLingoValue False => new(WebLingoValueKind.Boolean, 0);

    public static WebLingoValue FromBoolean(bool value) => value ? True : False;
    public static WebLingoValue FromInt64(long value) => new(WebLingoValueKind.Int64, value);
    public static WebLingoValue FromBinary64(double value) => new(WebLingoValueKind.Binary64, BitConverter.DoubleToInt64Bits(value));
    public static WebLingoValue FromBinary64Bits(long bits) => new(WebLingoValueKind.Binary64, bits);
    /// <summary>A value referring to an object of a <see cref="WebLingoHeap"/> by its handle.</summary>
    public static WebLingoValue FromHandle(WebLingoValueKind kind, int handle) => new(kind, handle);

    /// <summary>
    /// A collection's handle, marked, with <paramref name="hasClass"/>, as one a class extending Array, Map or Set made,
    /// so that its members are found without looking at the heap for plain ones (section 3.7).
    /// </summary>
    public static WebLingoValue FromHandle(WebLingoValueKind kind, int handle, bool hasClass) =>
        new(kind, hasClass ? (uint)handle | (1L << 32) : handle);

    /// <summary>For an array, a map or a set: whether a class extending Array, Map or Set made it.</summary>
    public bool HasClass => (_bits >> 32) == 1;

    public bool AsBoolean => _bits != 0;
    public long AsInt64 => _bits;
    public double AsBinary64 => BitConverter.Int64BitsToDouble(_bits);
    /// <summary>The heap handle of a string.</summary>
    public int Handle => (int)_bits;

    public bool Equals(WebLingoValue other) => Kind == other.Kind && _bits == other._bits;
    public override bool Equals(object? obj) => obj is WebLingoValue other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, _bits);
    public static bool operator ==(WebLingoValue left, WebLingoValue right) => left.Equals(right);
    public static bool operator !=(WebLingoValue left, WebLingoValue right) => !left.Equals(right);
    public override string ToString() => WebLingoValueFormatter.Format(this);
}

/// <summary>A runtime error: its diagnostic code and message; the VM adds where it happened.</summary>
public sealed record WebLingoRuntimeError(string Code, string Message);
