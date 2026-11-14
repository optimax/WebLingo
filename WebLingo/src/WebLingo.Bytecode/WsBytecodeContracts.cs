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
    /// <summary>
    /// A = destination register, B = target register, C = constant index of the member's name, a string, D = 1 for
    /// <c>?.</c>, which gives <c>null</c> for a <c>null</c> target or a missing member, else 0.
    /// </summary>
    GetMember,
    /// <summary>
    /// A = destination register, B = constant index of the method's name, a string, C = the target's register, with
    /// the D arguments in the registers after it, D = argument count.
    /// </summary>
    CallMethod,
    /// <summary>
    /// A = destination register, B = target register, C = index register, D = 1 for <c>?[</c>, which gives <c>null</c>
    /// for an absent entry or a <c>null</c> target, else 0.
    /// </summary>
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
    /// <summary>
    /// A = destination register, B = class index, C = register holding the class it extends if D is 1, else 0; a class
    /// value, its methods' closures made as their captures say.
    /// </summary>
    MakeClass,
    /// <summary>
    /// A = destination register, B = register holding a class (WS2005 otherwise), C = first argument register, D =
    /// argument count; a new instance, its constructor run with the arguments (section 3.7).
    /// </summary>
    New,
    /// <summary>A = target register, B = constant index of the field's name, a string, C = value register; assigns the field.</summary>
    SetMember,
    /// <summary>
    /// A = destination register, B = register holding a class, C = register holding <c>this</c>, D = constant index of
    /// a name, a string; the class's base's method of that name, or its constructor for <c>constructor</c>, bound to
    /// <c>this</c>: what <c>super</c> calls (section 3.7).
    /// </summary>
    GetSuper,
    /// <summary>
    /// A = register holding a resource; disposes it, unless it is <c>null</c>: its class's disposal, then its bases',
    /// at most once (WS3006 for a value with nothing to dispose; section 3.7).
    /// </summary>
    Dispose,
    /// <summary>A = register holding a value; throws it (section 3.7).</summary>
    Throw,
    /// <summary>
    /// A = register holding an exception, B = register holding where it was first thrown, as a handler gives them;
    /// throws it again from there (<c>throw;</c>, and the end of a <c>finally</c>'s handler; section 3.7).
    /// </summary>
    Rethrow,
    /// <summary>
    /// A = destination, B = a value: its text, as <c>write</c> writes it, an instance as its class's name and its
    /// fields, the instances in it whose class overrides <c>toString()</c> as that gives (<c>Object</c>'s
    /// <c>toString()</c>; section 3.7).
    /// </summary>
    ToText,
    /// <summary>
    /// A = destination, B = a value, C = a class: whether the value is an instance of the class, or of a class extending
    /// it, its kind's class for a value that is not an instance; a C that is not a class, whether the value is equal to
    /// it (<c>is</c>, and typed catches; section 3.7).
    /// </summary>
    IsInstance,
    /// <summary>
    /// A = a parameter's register, B = target instruction: jumps there if the caller gave the parameter, which, then, is
    /// not the missing value the VM put in its place, nor a cell holding it (default values; section 3.5).
    /// </summary>
    JumpIfGiven,
    /// <summary>
    /// A = destination, B = a value: suspends the <c>async</c> function, letting other tasks run, until the value, or a
    /// task's result once it completes, goes to A, or the task's exception is thrown here (section 3.8).
    /// </summary>
    Await,
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
    /// <summary>A method's or a constructor's: its first parameter is <c>this</c>, which its caller gives (section 3.7).</summary>
    public bool HasThis { get; init; }
    /// <summary>
    /// How many of its last parameters may be left out, having default values, which its code gives them (section 3.5);
    /// the VM puts a missing value in their place, which <see cref="WsOpCode.JumpIfGiven"/> tells.
    /// </summary>
    public int OptionalCount { get; init; }
    /// <summary>Whether it is <c>async</c>: its call gives a task, and it may <c>Await</c> (section 3.8).</summary>
    public bool IsAsync { get; init; }
    /// <summary>
    /// Its protected regions, innermost first, which an exception raised in it, or in a call it makes, looks through for
    /// the first that holds the instruction it was raised at (section 3.7; section 5.4's table-driven regions).
    /// </summary>
    public IReadOnlyList<WsHandler> Handlers { get; init; } = [];
}

/// <summary>
/// A protected region: an exception raised by an instruction from <paramref name="Start"/> to
/// <paramref name="End"/> − 1, or in a call one of them makes, jumps to <paramref name="Target"/>, the exception in
/// register <paramref name="Value"/> and where it was first raised, which <see cref="WsOpCode.Rethrow"/> takes, in
/// register <paramref name="Origin"/>.
/// </summary>
public readonly record struct WsHandler(int Start, int End, int Target, int Value, int Origin);

/// <summary>A class of a module: its name, its constructor's function, or -1 if it has none, and its methods' (section 3.7).</summary>
public sealed record WsClassInfo(string Name, int Constructor, IReadOnlyList<WsClassMethod> Methods)
{
    /// <summary>Its static methods, functions without <c>this</c>, by name (section 3.7).</summary>
    public IReadOnlyList<WsClassMethod> StaticMethods { get; init; } = [];

    /// <summary>The names of its static fields, which exist, as <c>null</c>, from when it is made.</summary>
    public IReadOnlyList<string> StaticFields { get; init; } = [];
}

/// <summary>A class of a module's that a runtime error of a code becomes, when a handler catches it (section 3.7).</summary>
public readonly record struct WsErrorClass(string Code, int Class);

/// <summary>A method of a class: its name, and the module's function that is its body.</summary>
public readonly record struct WsClassMethod(string Name, int Function);

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
    /// <summary>The classes the module declares, which <see cref="WsOpCode.MakeClass"/> refers to by index.</summary>
    public IReadOnlyList<WsClassInfo> Classes { get; init; } = [];
    /// <summary>
    /// The class of <see cref="Classes"/> that is <c>Error</c>, whose instances a runtime error a handler catches
    /// becomes (section 3.7), or -1 if the module has none.
    /// </summary>
    public int ErrorClass { get; init; } = -1;
    /// <summary>The class of <see cref="Classes"/> that is <c>Object</c>, whose <c>toString()</c> every value has, or -1.</summary>
    public int ObjectClass { get; init; } = -1;
    /// <summary>
    /// The classes of <see cref="Classes"/> that are the kinds of values', by <see cref="WsValueClass"/>, -1 for one the
    /// module has not; empty if it has none of them (section 3.7, "Classes of values").
    /// </summary>
    public IReadOnlyList<int> ValueClasses { get; init; } = [];
    /// <summary>The classes of <see cref="Classes"/> that runtime errors become, by their codes (section 3.7).</summary>
    public IReadOnlyList<WsErrorClass> ErrorClasses { get; init; } = [];

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
