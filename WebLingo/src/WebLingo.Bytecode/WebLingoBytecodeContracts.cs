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
public enum WebLingoOpCode : byte
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
    /// <summary>A = destination register, B = register holding a cell; its value (WL2004 if it has none yet).</summary>
    LoadCell,
    /// <summary>A = register holding a cell, B = source register; stores the value in the cell.</summary>
    StoreCell,
    /// <summary>A = destination register, B = capture index; the captured cell's value (WL2004 if it has none yet).</summary>
    LoadCapture,
    /// <summary>A = capture index, B = source register; stores the value in the captured cell.</summary>
    StoreCapture,
    /// <summary>A = destination register, B = function index; a closure, capturing as the function's captures say.</summary>
    MakeClosure,
    /// <summary>A = destination register, B = import index; the built-in as a value.</summary>
    LoadBuiltin,
    /// <summary>
    /// A = destination register, B = callee register, which must hold a function taking D arguments (WL2005, WL2006),
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
    /// A = destination register, B = register holding a class (WL2005 otherwise), C = first argument register, D =
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
    /// at most once (WL3006 for a value with nothing to dispose; section 3.7).
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
    /// <summary>
    /// A = register holding a class (WL3001 otherwise), B = class index: the class given that class's methods and static
    /// members, their closures made as their captures say, an extension's (section 3.7).
    /// </summary>
    Extend,
    /// <summary>
    /// A = destination, given null, B = an instance of a class that holds a value, C = the value, which it holds, of its
    /// class's kind (WL3001 otherwise): the built-in classes' constructors' (section 3.7).
    /// </summary>
    Hold,
    /// <summary>
    /// A = destination, B = a module of the program's: its instance, if it has begun to run, or else what calling its
    /// function gives, a task, which the code then awaits (section 3.9).
    /// </summary>
    Import,
    /// <summary>
    /// A = destination, B = a module of the program's, C = first register, D = count: the module's instance, its exports
    /// the cells in those registers, from then on its instance (section 3.9).
    /// </summary>
    MakeModule,
    /// <summary>A = a module the program imports, B = register holding the closure of its function, which runs it.</summary>
    RegisterUnit,
}

public readonly record struct WebLingoInstruction(WebLingoOpCode OpCode, int A = 0, int B = 0, int C = 0, int D = 0);

public enum WebLingoConstantKind : byte { Int64, Binary64, String }

/// <summary>
/// A constant; a binary64 is kept as its IEEE 754 bits, so that constants compare and copy exactly, and a string as
/// its <paramref name="Text"/>, which only a string has.
/// </summary>
public readonly record struct WebLingoConstant(WebLingoConstantKind Kind, long Bits, string? Text = null)
{
    public static WebLingoConstant String(string text) => new(WebLingoConstantKind.String, 0, text);
    public static WebLingoConstant Int64(long value) => new(WebLingoConstantKind.Int64, value);
    public static WebLingoConstant Binary64(double value) => new(WebLingoConstantKind.Binary64, BitConverter.DoubleToInt64Bits(value));
}

/// <summary>A host built-in the module calls, linked by name when the module runs.</summary>
public sealed record WebLingoImport(string Name, int Arity);

/// <summary>
/// A function: its registers (<see cref="LocalCount"/> locals, then temporaries, <see cref="RegisterCount"/> in all)
/// and its code. <see cref="Spans"/> (one per instruction) and <see cref="LocalNames"/> (one per local) are
/// optional debugging information, for runtime error messages.
/// </summary>
public sealed record WebLingoFunction(
    string Name, int RegisterCount, int LocalCount, IReadOnlyList<WebLingoInstruction> Code,
    IReadOnlyList<WebLingoSourceSpan>? Spans = null, IReadOnlyList<string>? LocalNames = null)
{
    /// <summary>The most registers a function may have (specification, section 5.3, proposed).</summary>
    public const int MaxRegisters = 65535;

    /// <summary>How many arguments it takes, into its first registers.</summary>
    public int ParameterCount { get; init; }
    /// <summary>The file its spans are in, when it is not the program's own: a module it imports (section 3.9).</summary>
    public string? Source { get; init; }
    /// <summary>The cells a closure of it captures, from the function that makes the closure.</summary>
    public IReadOnlyList<WebLingoCapture> Captures { get; init; } = [];
    /// <summary>A method's or a constructor's: its first parameter is <c>this</c>, which its caller gives (section 3.7).</summary>
    public bool HasThis { get; init; }
    /// <summary>
    /// How many of its last parameters may be left out, having default values, which its code gives them (section 3.5);
    /// the VM puts a missing value in their place, which <see cref="WebLingoOpCode.JumpIfGiven"/> tells.
    /// </summary>
    public int OptionalCount { get; init; }
    /// <summary>Whether it is <c>async</c>: its call gives a task, and it may <c>Await</c> (section 3.8).</summary>
    public bool IsAsync { get; init; }
    /// <summary>
    /// Its protected regions, innermost first, which an exception raised in it, or in a call it makes, looks through for
    /// the first that holds the instruction it was raised at (section 3.7; section 5.4's table-driven regions).
    /// </summary>
    public IReadOnlyList<WebLingoHandler> Handlers { get; init; } = [];
}

/// <summary>
/// A protected region: an exception raised by an instruction from <paramref name="Start"/> to
/// <paramref name="End"/> − 1, or in a call one of them makes, jumps to <paramref name="Target"/>, the exception in
/// register <paramref name="Value"/> and where it was first raised, which <see cref="WebLingoOpCode.Rethrow"/> takes, in
/// register <paramref name="Origin"/>.
/// </summary>
public readonly record struct WebLingoHandler(int Start, int End, int Target, int Value, int Origin);

/// <summary>A class of a module: its name, its constructor's function, or -1 if it has none, and its methods' (section 3.7).</summary>
public sealed record WebLingoClassInfo(string Name, int Constructor, IReadOnlyList<WebLingoClassMethod> Methods)
{
    /// <summary>Its static methods, functions without <c>this</c>, by name (section 3.7).</summary>
    public IReadOnlyList<WebLingoClassMethod> StaticMethods { get; init; } = [];

    /// <summary>The names of its static fields, which exist, as <c>null</c>, from when it is made.</summary>
    public IReadOnlyList<string> StaticFields { get; init; } = [];

    /// <summary>
    /// Whether its constructor is the one a class that declares none gets, which only runs its base's, and which a
    /// class holding a value passes over, its arguments going on to its base's (section 3.7).
    /// </summary>
    public bool DefaultConstructor { get; init; }
}

/// <summary>A class of a module's that a runtime error of a code becomes, when a handler catches it (section 3.7).</summary>
public readonly record struct WebLingoErrorClass(string Code, int Class);

/// <summary>A method of a class: its name, and the module's function that is its body.</summary>
public readonly record struct WebLingoClassMethod(string Name, int Function);

/// <summary>
/// Where a closure's capture comes from when <see cref="WebLingoOpCode.MakeClosure"/> makes it: the cell in a register of
/// the making function (<paramref name="FromLocal"/>), or one of that function's own captures.
/// </summary>
public readonly record struct WebLingoCapture(bool FromLocal, int Index);

/// <summary>An in-memory module: its constants, the built-ins it imports, the function it runs, and its other functions.</summary>
/// <summary>
/// A module of a program, a file (section 3.9): its name, the function that runs it, its top level (-1 for the program
/// itself, the entry point), and the names it exports, in the order its instance's cells are given.
/// </summary>
public sealed record WebLingoUnitInfo(string Name, int Function, IReadOnlyList<string> Exports)
{
    /// <summary>Its parameters (section 3.9), whose values its function takes as one map; none for the program's own file.</summary>
    public IReadOnlyList<WebLingoModuleParameter> Parameters { get; init; } = [];
}

public sealed record WebLingoCompiledModule(
    string Name, IReadOnlyList<WebLingoConstant> Constants, IReadOnlyList<WebLingoImport> Imports, WebLingoFunction EntryPoint)
{
    /// <summary>The functions the module declares, besides its entry point, which closures are made of by index.</summary>
    public IReadOnlyList<WebLingoFunction> Functions { get; init; } = [];
    /// <summary>The classes the module declares, which <see cref="WebLingoOpCode.MakeClass"/> refers to by index.</summary>
    public IReadOnlyList<WebLingoClassInfo> Classes { get; init; } = [];
    /// <summary>The files of a program that imports others, the program's first; empty for one that imports none (section 3.9).</summary>
    public IReadOnlyList<WebLingoUnitInfo> Units { get; init; } = [];
    /// <summary>
    /// The class of <see cref="Classes"/> that is <c>Error</c>, whose instances a runtime error a handler catches
    /// becomes (section 3.7), or -1 if the module has none.
    /// </summary>
    public int ErrorClass { get; init; } = -1;
    /// <summary>The class of <see cref="Classes"/> that is <c>Object</c>, whose <c>toString()</c> every value has, or -1.</summary>
    public int ObjectClass { get; init; } = -1;
    /// <summary>
    /// The classes of <see cref="Classes"/> that are the kinds of values', by <see cref="WebLingoValueClass"/>, -1 for one the
    /// module has not; empty if it has none of them (section 3.7, "Classes of values").
    /// </summary>
    public IReadOnlyList<int> ValueClasses { get; init; } = [];
    /// <summary>The classes of <see cref="Classes"/> that runtime errors become, by their codes (section 3.7).</summary>
    public IReadOnlyList<WebLingoErrorClass> ErrorClasses { get; init; } = [];

    /// <summary>The smallest valid module: it returns at once.</summary>
    public static WebLingoCompiledModule Empty(string name) =>
        new(name, [], [], new WebLingoFunction("main", 0, 0, [new WebLingoInstruction(WebLingoOpCode.Return)]));
}

/// <summary>A verifier's verdict; <paramref name="InstructionIndex"/> is the faulty instruction's, or -1.</summary>
public sealed record WebLingoVerificationResult(bool IsValid, string? Error, int InstructionIndex = -1)
{
    public static WebLingoVerificationResult Valid { get; } = new(true, null);
}

public interface IWebLingoBytecodeVerifier { WebLingoVerificationResult Verify(WebLingoCompiledModule module); }
