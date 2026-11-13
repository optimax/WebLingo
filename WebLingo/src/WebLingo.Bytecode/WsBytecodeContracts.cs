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
    /// <summary>A = destination register; a new cell, without a value until one is stored.</summary>
    NewCell,
    /// <summary>A = register; its value moves into a new cell, which the register then holds.</summary>
    BoxRegister,
    /// <summary>A = register holding a cell; it then holds a new cell, with the old one's value.</summary>
    CopyCell,
    /// <summary>A = destination register, B = register holding a cell; its value (WS2004 if it has none yet).</summary>
    LoadCell,
    /// <summary>A = register holding a cell, B = source register; stores the value in the cell.</summary>
    StoreCell,
    /// <summary>A = destination register, B = capture index; the captured cell's value (WS2004 if it has none yet).</summary>
    LoadCapture,
    /// <summary>A = capture index, B = source register; stores the value in the captured cell.</summary>
    StoreCapture,
    /// <summary>A = destination register, B = function index; a closure, capturing as the function's captures say.</summary>
    MakeClosure,
    /// <summary>A = destination register, B = import index; the built-in as a value.</summary>
    LoadBuiltin,
    /// <summary>
    /// A = destination register, B = callee register, which must hold a function taking D arguments (WS2005, WS2006),
    /// C = first argument register, D = argument count.
    /// </summary>
    Call,
    /// <summary>A = register; ends the function, giving its value.</summary>
    ReturnValue,
    /// <summary>A = the index of the instruction to go to.</summary>
    Jump,
    /// <summary>Ends the function, giving <c>null</c>.</summary>
    Return,
    /// <summary>A = destination register, B = target register, C = constant index of the member's name, a string.</summary>
    GetMember,
    /// <summary>
    /// A = destination register, B = constant index of the method's name, a string, C = the target's register, with
    /// the D arguments in the registers after it, D = argument count.
    /// </summary>
    CallMethod,
    /// <summary>A = destination register, B = target register, C = index register.</summary>
    GetIndex,
    /// <summary>A = target register, B = index register, C = value register; nothing is written.</summary>
    SetIndex,
    /// <summary>A = destination register, B = first element register, C = element count; a new array of them.</summary>
    NewArray,
    /// <summary>
    /// A = destination register, B = first register, C = entry count; a new map of the C key-value pairs in the 2 × C
    /// registers from B on, each key before its value.
    /// </summary>
    NewMap,
    /// <summary>
    /// A = the instruction to go to when there is nothing more, B = the register holding the collection, with the
    /// loop's position (an int64, from 0) in the next one, and the items it writes in the C after that, C = 1 for an
    /// item, 2 for a key and a value (specification, section 3.6).
    /// </summary>
    ForEachNext,
    /// <summary>A = destination register, B = first value register, C = value count; a new set of them.</summary>
    NewSet,
}

public readonly record struct WsInstruction(WsOpCode OpCode, int A = 0, int B = 0, int C = 0, int D = 0);

public enum WsConstantKind : byte { Int64, Binary64, String }

/// <summary>
/// A constant; a binary64 is kept as its IEEE 754 bits, so that constants compare and copy exactly, and a string as
/// its <paramref name="Text"/>, which only a string has.
/// </summary>
public readonly record struct WsConstant(WsConstantKind Kind, long Bits, string? Text = null)
{
    public static WsConstant String(string text) => new(WsConstantKind.String, 0, text);
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

    /// <summary>How many arguments it takes, into its first registers.</summary>
    public int ParameterCount { get; init; }
    /// <summary>The cells a closure of it captures, from the function that makes the closure.</summary>
    public IReadOnlyList<WsCapture> Captures { get; init; } = [];
}

/// <summary>
/// Where a closure's capture comes from when <see cref="WsOpCode.MakeClosure"/> makes it: the cell in a register of
/// the making function (<paramref name="FromLocal"/>), or one of that function's own captures.
/// </summary>
public readonly record struct WsCapture(bool FromLocal, int Index);

/// <summary>An in-memory module: its constants, the built-ins it imports, the function it runs, and its other functions.</summary>
public sealed record WsCompiledModule(
    string Name, IReadOnlyList<WsConstant> Constants, IReadOnlyList<WsImport> Imports, WsFunction EntryPoint)
{
    /// <summary>The functions the module declares, besides its entry point, which closures are made of by index.</summary>
    public IReadOnlyList<WsFunction> Functions { get; init; } = [];

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
