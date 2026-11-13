using WebLingo.Core;
namespace WebLingo.Bytecode;

// The stack bytecode (specification, section 5.6, "The comparison's parts", part 3): the instruction model of the
// stack VM, which is compared with the register VM before the choice between them is made (WS-BC002, provisional).
// Like the register bytecode, it is an in-memory model, not WBC, and nothing here is a frozen format. A function's
// frame holds its locals, then its operand stack; an instruction takes its operands from the top of the stack and
// pushes its result there. Constants, imports and captures are the register bytecode's.

/// <summary>
/// A stack instruction's operation. The comment on each says what its operands A and B mean, what it pops and what it
/// pushes; an operand an operation does not use must be 0.
/// </summary>
public enum WsStackOpCode : byte
{
    /// <summary>A = constant index; pushes the constant.</summary>
    LoadConstant,
    /// <summary>Pushes null.</summary>
    LoadNull,
    /// <summary>A = 0 for false or 1 for true; pushes the Boolean.</summary>
    LoadBoolean,
    /// <summary>A = local; pushes its value.</summary>
    LoadLocal,
    /// <summary>A = local; pops a value into it.</summary>
    StoreLocal,
    /// <summary>Pushes the value on top of the stack again.</summary>
    Duplicate,
    /// <summary>Pops a value, which is dropped.</summary>
    Pop,
    /// <summary>Pops a number, pushes its negation.</summary>
    Negate,
    /// <summary>Pops a value, which must be a number, and pushes it back.</summary>
    Plus,
    /// <summary>Pops a Boolean, pushes its opposite.</summary>
    Not,
    /// <summary>Pops the right operand, then the left one; pushes the result.</summary>
    Add,
    /// <summary>Pops the right operand, then the left one; pushes the result.</summary>
    Subtract,
    /// <summary>Pops the right operand, then the left one; pushes the result.</summary>
    Multiply,
    /// <summary>Pops the right operand, then the left one; pushes the result.</summary>
    Divide,
    /// <summary>Pops the right operand, then the left one; pushes a Boolean, never an error.</summary>
    Equal,
    /// <summary>Pops the right operand, then the left one; pushes a Boolean, never an error.</summary>
    NotEqual,
    /// <summary>Pops the right operand, then the left one, both numbers; pushes a Boolean.</summary>
    Less,
    /// <summary>Pops the right operand, then the left one, both numbers; pushes a Boolean.</summary>
    LessOrEqual,
    /// <summary>Pops the right operand, then the left one, both numbers; pushes a Boolean.</summary>
    Greater,
    /// <summary>Pops the right operand, then the left one, both numbers; pushes a Boolean.</summary>
    GreaterOrEqual,
    /// <summary>A = the index of the instruction to go to.</summary>
    Jump,
    /// <summary>A = target instruction; pops a condition, which must be a Boolean, and goes there if it is false.</summary>
    JumpIfFalse,
    /// <summary>A = target instruction; pops a condition, which must be a Boolean, and goes there if it is true.</summary>
    JumpIfTrue,
    /// <summary>The value on top of the stack must be a Boolean; nothing is popped.</summary>
    CheckBoolean,
    /// <summary>A = local; it gets a new cell, without a value until one is stored.</summary>
    NewCell,
    /// <summary>A = local; its value moves into a new cell, which the local then holds.</summary>
    BoxLocal,
    /// <summary>A = local holding a cell; it then holds a new cell, with the old one's value.</summary>
    CopyCell,
    /// <summary>A = local holding a cell; pushes the cell's value (WS2004 if it has none yet).</summary>
    LoadCell,
    /// <summary>A = local holding a cell; pops a value into the cell.</summary>
    StoreCell,
    /// <summary>A = capture index; pushes the captured cell's value (WS2004 if it has none yet).</summary>
    LoadCapture,
    /// <summary>A = capture index; pops a value into the captured cell.</summary>
    StoreCapture,
    /// <summary>A = function index; pushes a closure, capturing as the function's captures say.</summary>
    MakeClosure,
    /// <summary>A = import index; pushes the built-in as a value.</summary>
    LoadBuiltin,
    /// <summary>
    /// A = argument count; pops the arguments, then the callee below them, which must be a function taking A arguments
    /// (WS2005, WS2006); pushes the result.
    /// </summary>
    Call,
    /// <summary>A = import index, B = argument count; pops the arguments, pushes the built-in's result.</summary>
    CallBuiltin,
    /// <summary>Pops a value, and ends the function, giving it.</summary>
    ReturnValue,
    /// <summary>Ends the function, giving <c>null</c>.</summary>
    Return,
    /// <summary>
    /// A = constant index of the member's name, a string, B = 1 for <c>?.</c>, else 0; pops the target, pushes its member.
    /// </summary>
    GetMember,
    /// <summary>
    /// A = constant index of the method's name, a string, B = argument count; pops the arguments, then the target
    /// below them, and pushes the method's result.
    /// </summary>
    CallMethod,
    /// <summary>
    /// A = 1 for <c>?[</c>, which gives <c>null</c> for an absent entry or a <c>null</c> target, else 0; pops the index,
    /// then the target, and pushes the target's element at the index.
    /// </summary>
    GetIndex,
    /// <summary>A = 1 to push the value back, 0 not to; pops the value, the index, then the target, and stores it.</summary>
    StoreIndex,
    /// <summary>A = element count; pops that many values, the last one on top, and pushes a new array of them.</summary>
    NewArray,
    /// <summary>
    /// A = entry count; pops that many key-value pairs, each key below its value, and pushes a new map of them.
    /// </summary>
    NewMap,
    /// <summary>
    /// A = the instruction to go to when there is nothing more, B = the local holding the collection, with the loop's
    /// position (an int64, from 0) in the next one; writes the next item into the local after that (section 3.6).
    /// </summary>
    ForEachNext,
    /// <summary>As <see cref="ForEachNext"/>, writing the next entry's key and value into the two locals after the position.</summary>
    ForEachNextEntry,
    /// <summary>A = value count; pops that many values and pushes a new set of them.</summary>
    NewSet,
    /// <summary>
    /// A = class index, B = 1 to pop the class it extends, else 0; pushes a class value, its methods' closures made as
    /// their captures say.
    /// </summary>
    MakeClass,
    /// <summary>
    /// A = argument count; pops the arguments, then the class below them (WS2005 if it is not one), and pushes a new
    /// instance, its constructor run with the arguments (section 3.7).
    /// </summary>
    New,
    /// <summary>
    /// A = constant index of the field's name, a string, B = 1 to push the value back, 0 not to; pops the value, then
    /// the target, and assigns the field.
    /// </summary>
    SetMember,
    /// <summary>
    /// A = constant index of a name, a string; pops <c>this</c>, then a class, and pushes the class's base's method of
    /// that name, or its constructor for <c>constructor</c>, bound to <c>this</c> (section 3.7).
    /// </summary>
    GetSuper,
    /// <summary>Pops a resource and disposes it, unless it is <c>null</c>, as the register bytecode's <c>Dispose</c> does.</summary>
    Dispose,
    /// <summary>Pops a value and throws it (section 3.7).</summary>
    Throw,
    /// <summary>Pops where an exception was first thrown, then the exception, and throws it again from there.</summary>
    Rethrow,
}

public readonly record struct WsStackInstruction(WsStackOpCode OpCode, int A = 0, int B = 0);

/// <summary>
/// A function of the stack bytecode: its locals (<see cref="LocalCount"/>, its parameters first), the most values its
/// operand stack holds (<see cref="MaxStack"/>), and its code. Its frame has a slot for each local and each stack
/// value, at most <see cref="WsFunction.MaxRegisters"/> in all. <see cref="Spans"/> (one per instruction) and
/// <see cref="LocalNames"/> (one per local) are optional debugging information, for runtime error messages.
/// </summary>
public sealed record WsStackFunction(
    string Name, int LocalCount, int MaxStack, IReadOnlyList<WsStackInstruction> Code,
    IReadOnlyList<WsSourceSpan>? Spans = null, IReadOnlyList<string>? LocalNames = null)
{
    /// <summary>How many arguments it takes, into its first locals.</summary>
    public int ParameterCount { get; init; }
    /// <summary>The cells a closure of it captures, from the function that makes the closure.</summary>
    public IReadOnlyList<WsCapture> Captures { get; init; } = [];
    /// <summary>The slots its frame needs: its locals and its operand stack.</summary>
    public int SlotCount => LocalCount + MaxStack;
    /// <summary>A method's or a constructor's: its first parameter is <c>this</c>, which its caller gives (section 3.7).</summary>
    public bool HasThis { get; init; }
    /// <summary>Its protected regions, innermost first, as the register bytecode's (section 3.7).</summary>
    public IReadOnlyList<WsStackHandler> Handlers { get; init; } = [];
}

/// <summary>
/// A protected region, as <see cref="WsHandler"/> is: the exception goes to local <paramref name="Value"/>, where it
/// was first raised to local <paramref name="Origin"/>, and the operand stack is cut to <paramref name="Depth"/>.
/// </summary>
public readonly record struct WsStackHandler(int Start, int End, int Target, int Value, int Origin, int Depth);

/// <summary>A stack bytecode module: its constants, its imports, the function it runs, and its other functions.</summary>
public sealed record WsStackModule(
    string Name, IReadOnlyList<WsConstant> Constants, IReadOnlyList<WsImport> Imports, WsStackFunction EntryPoint)
{
    /// <summary>The functions the module declares, besides its entry point, which closures are made of by index.</summary>
    public IReadOnlyList<WsStackFunction> Functions { get; init; } = [];
    /// <summary>The classes the module declares, which <see cref="WsStackOpCode.MakeClass"/> refers to by index.</summary>
    public IReadOnlyList<WsClassInfo> Classes { get; init; } = [];
    /// <summary>The class of <see cref="Classes"/> that is <c>Error</c>, or -1 (section 3.7).</summary>
    public int ErrorClass { get; init; } = -1;
}
