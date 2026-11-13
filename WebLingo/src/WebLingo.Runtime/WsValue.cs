namespace WebLingo.Runtime;

/// <summary>
/// The kinds of runtime value. A String, a Function (a closure), a Cell and an Array refer to an object of a heap by
/// handle; a Builtin is a host built-in, by its import index; a Cell is never a script's value, only a frame's.
/// </summary>
public enum WsValueKind : byte { Null, Boolean, Int64, Binary64, String, Function, Builtin, Cell, Array }

/// <summary>
/// A runtime value (specification, section 4.1): a kind and a 64-bit payload, 16 bytes in all. M0.6 has null,
/// Boolean, int64 and binary64 (section 8.4, "Runtime values"); a binary64 is kept as its IEEE 754 bits.
/// </summary>
public readonly struct WsValue : IEquatable<WsValue>
{
    private readonly long _bits;

    private WsValue(WsValueKind kind, long bits)
    {
        Kind = kind;
        _bits = bits;
    }

    public WsValueKind Kind { get; }

    public static WsValue Null => default;
    public static WsValue True => new(WsValueKind.Boolean, 1);
    public static WsValue False => new(WsValueKind.Boolean, 0);

    public static WsValue FromBoolean(bool value) => value ? True : False;
    public static WsValue FromInt64(long value) => new(WsValueKind.Int64, value);
    public static WsValue FromBinary64(double value) => new(WsValueKind.Binary64, BitConverter.DoubleToInt64Bits(value));
    public static WsValue FromBinary64Bits(long bits) => new(WsValueKind.Binary64, bits);
    /// <summary>A value referring to an object of a <see cref="WsHeap"/> by its handle.</summary>
    public static WsValue FromHandle(WsValueKind kind, int handle) => new(kind, handle);

    public bool AsBoolean => _bits != 0;
    public long AsInt64 => _bits;
    public double AsBinary64 => BitConverter.Int64BitsToDouble(_bits);
    /// <summary>The heap handle of a string.</summary>
    public int Handle => (int)_bits;

    public bool Equals(WsValue other) => Kind == other.Kind && _bits == other._bits;
    public override bool Equals(object? obj) => obj is WsValue other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, _bits);
    public static bool operator ==(WsValue left, WsValue right) => left.Equals(right);
    public static bool operator !=(WsValue left, WsValue right) => !left.Equals(right);
    public override string ToString() => WsValueFormatter.Format(this);
}

/// <summary>A runtime error: its diagnostic code and message; the VM adds where it happened.</summary>
public sealed record WsRuntimeError(string Code, string Message);
