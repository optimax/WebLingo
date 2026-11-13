using WebLingo.Core;
namespace WebLingo.Bytecode;

// M0.6's in-memory bytecode (specification, sections 5.3 and 8.4): a register instruction model the VM executes after
// the verifier has checked it. It is not WBC, the serialized format, whose encoding is not decided yet, and nothing
// here is a frozen format.

/// <summary>
/// An instruction's operation. The comment on each says what its operands A, B, C, and D mean; an operand an
/// operation does not use must be 0. "Register" operands index the function's registers: its locals first, from 0,
/// then its temporaries.
/// </summary>
public enum WsOpCode : byte
{
    /// <summary>A = destination register, B = constant index.</summary>
    LoadConstant,
    /// <summary>A = destination register, B = source register.</summary>
    Move,
    /// <summary>A = destination register, B = operand register.</summary>
    Negate,
    /// <summary>A = destination register, B = operand register; the operand, which must be a number.</summary>
    Plus,
    /// <summary>A = destination register, B = left register, C = right register.</summary>
    Add,
    /// <summary>A = destination register, B = left register, C = right register.</summary>
    Subtract,
    /// <summary>A = destination register, B = left register, C = right register.</summary>
    Multiply,
    /// <summary>A = destination register, B = left register, C = right register.</summary>
    Divide,
    /// <summary>A = destination register, B = import index, C = first argument register, D = argument count.</summary>
    CallBuiltin,
    /// <summary>A = destination register; null.</summary>
    LoadNull,
    /// <summary>A = destination register, B = 0 for false or 1 for true.</summary>
    LoadBoolean,
    /// <summary>A = destination register, B = operand register, which must be a Boolean.</summary>
    Not,
    /// <summary>A = destination register, B = left register, C = right register; a Boolean, never an error.</summary>
    Equal,
    /// <summary>A = destination register, B = left register, C = right register; a Boolean, never an error.</summary>
    NotEqual,
    /// <summary>A = destination register, B = left register, C = right register, both numbers; a Boolean.</summary>
    Less,
    /// <summary>A = destination register, B = left register, C = right register, both numbers; a Boolean.</summary>
    LessOrEqual,
    /// <summary>A = destination register, B = left register, C = right register, both numbers; a Boolean.</summary>
    Greater,
    /// <summary>A = destination register, B = left register, C = right register, both numbers; a Boolean.</summary>
    GreaterOrEqual,
    /// <summary>A = condition register, which must be a Boolean, B = target instruction, taken if it is false.</summary>
    JumpIfFalse,
    /// <summary>A = condition register, which must be a Boolean, B = target instruction, taken if it is true.</summary>
    JumpIfTrue,
    /// <summary>A = register, which must be a Boolean; nothing is written.</summary>
    CheckBoolean,
    /// <summary>A = the index of the instruction to go to.</summary>
    Jump,
    /// <summary>Ends the function.</summary>
    Return,
}

public readonly record struct WsInstruction(WsOpCode OpCode, int A = 0, int B = 0, int C = 0, int D = 0);

public enum WsConstantKind : byte { Int64, Binary64 }

/// <summary>A constant; a binary64 is kept as its IEEE 754 bits, so that constants compare and copy exactly.</summary>
public readonly record struct WsConstant(WsConstantKind Kind, long Bits)
{
    public static WsConstant Int64(long value) => new(WsConstantKind.Int64, value);
    public static WsConstant Binary64(double value) => new(WsConstantKind.Binary64, BitConverter.DoubleToInt64Bits(value));
}

/// <summary>A host built-in the module calls, linked by name when the module runs.</summary>
public sealed record WsImport(string Name, int Arity);

/// <summary>
/// A function: its registers (<see cref="LocalCount"/> locals, then temporaries, <see cref="RegisterCount"/> in all)
/// and its code. <see cref="Spans"/> (one per instruction) and <see cref="LocalNames"/> (one per local) are
/// optional debugging information, for runtime error messages.
/// </summary>
public sealed record WsFunction(
    string Name, int RegisterCount, int LocalCount, IReadOnlyList<WsInstruction> Code,
    IReadOnlyList<WsSourceSpan>? Spans = null, IReadOnlyList<string>? LocalNames = null)
{
    /// <summary>The most registers a function may have (specification, section 5.3, proposed).</summary>
    public const int MaxRegisters = 65535;
}

/// <summary>An in-memory module: its constants, the built-ins it imports, and the function it runs.</summary>
public sealed record WsCompiledModule(
    string Name, IReadOnlyList<WsConstant> Constants, IReadOnlyList<WsImport> Imports, WsFunction EntryPoint)
{
    /// <summary>The smallest valid module: it returns at once.</summary>
    public static WsCompiledModule Empty(string name) =>
        new(name, [], [], new WsFunction("main", 0, 0, [new WsInstruction(WsOpCode.Return)]));
}

/// <summary>A verifier's verdict; <paramref name="InstructionIndex"/> is the faulty instruction's, or -1.</summary>
public sealed record WsVerificationResult(bool IsValid, string? Error, int InstructionIndex = -1)
{
    public static WsVerificationResult Valid { get; } = new(true, null);
}

public interface IWsBytecodeVerifier { WsVerificationResult Verify(WsCompiledModule module); }
