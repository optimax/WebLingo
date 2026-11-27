using WebLingo.Core;
namespace WebLingo.Bytecode;

// M0.6's in-memory bytecode (specification, sections 5.3 and 8.4): a register instruction model the VM executes after
// the verifier has checked it. It is not WLBC, the serialized format, whose encoding is not decided yet, and nothing
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
    /// <c>toString()</c>; section 3.7). With C = 1, its text as <c>write</c> writes it, an instance whose class overrides
    /// <c>toString()</c> as that gives too, a string or WL3001: an interpolation's (section 3.5).
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
    /// <summary>A = destination register, B = register of a module's path, C = register of the values its parameters are given, D = 1 if there are values, plus 2 for a component's render, which runs the file anew (section 13); a task, which completes with the module's instance once it has run (section 3.9).</summary>
    ImportDynamic,
    /// <summary>
    /// <c>foreach</c> of one item, before its loop (section 3.6): A = the instruction to go to when the VMs visit the
    /// collection themselves, an array, a set, a map or a string, B = the register holding it; else the next, which calls
    /// its <c>iterate()</c>; WL3001 for a value that has none.
    /// </summary>
    ForEachStart,
    /// <summary>
    /// <c>foreach</c> of one item, after its loop's body: A = the instruction to go to when the VMs visit the collection
    /// themselves, their loop over, B = the register holding it; else the next, the protocol's step, its iterator's
    /// <c>hasNext()</c> and <c>next()</c> (section 3.6).
    /// </summary>
    ForEachVisits,
    /// <summary>A = destination register, B = left register, C = right register: <c>%</c> (section 3.5).</summary>
    Remainder,
    /// <summary>
    /// A = the rest parameter's register, the one after the parameters: the arguments after them, which the call gathered,
    /// else a new empty array; a function with a rest parameter's first instruction, and only there (section 3.5).
    /// </summary>
    TakeRest,
    /// <summary>
    /// A = destination, B = callee, C = an array: <c>f(...values)</c> (section 3.5), the array's elements put in the
    /// registers from C on, the frame's registers grown where they are too few, then called as <see cref="Call"/> is.
    /// </summary>
    CallSpread,
    /// <summary>
    /// A = destination, B = the method's name, a constant, C = target, D = an array: <c>x.m(...values)</c>, the array's
    /// elements put in the registers from C + 1 on, then called as <see cref="CallMethod"/> is (section 3.5).
    /// </summary>
    CallMethodSpread,
    /// <summary>
    /// A = destination, B = class, C = an array: <c>new Class(...values)</c>, the array's elements put in the registers
    /// from C on, then made as <see cref="New"/> makes one (section 3.5).
    /// </summary>
    NewSpread,
    /// <summary>
    /// A = destination, B = a value, C = an indent: the value's JSON text, a toJson() its instances' classes have called
    /// on the way, as <see cref="ToText"/> calls a toString() (section 3.6, "JSON").
    /// </summary>
    ToJson,
    /// <summary><c>A = B \ C</c>, two integers' quotient (section 3.5).</summary>
    IntegerDivide,
    /// <summary>
    /// A = destination, B = left, C = a constant, the right operand: <see cref="Add"/>'s <c>A = B + C</c>, its constant
    /// read from the module's constants, not loaded into a register first (specification, section 5.6, "Where the time
    /// goes"); and so for the others below.
    /// </summary>
    AddConstant,
    SubtractConstant,
    MultiplyConstant,
    RemainderConstant,
    EqualConstant,
    NotEqualConstant,
    LessConstant,
    LessOrEqualConstant,
    GreaterConstant,
    GreaterOrEqualConstant,
    /// <summary>
    /// A = destination register, B = target register, C = constant index of the member's name, a string, D = the field's
    /// place in its class's instances (<see cref="WebLingoClassInfo.Fields"/>): <see cref="GetMember"/>'s <c>A = B.C</c>,
    /// read from that place where B is an instance whose class has the field there and it has been assigned, and as
    /// <see cref="GetMember"/> reads it otherwise (specification, section 9, "M0.21 parts", part 2).
    /// </summary>
    GetField,
    /// <summary>
    /// A = target register, B = constant index of the field's name, a string, C = value register, D = the field's place:
    /// <see cref="SetMember"/>'s <c>A.B = C</c>, written to that place where A is an instance whose class has the field
    /// there and it has been assigned before, and as <see cref="SetMember"/> assigns it otherwise.
    /// </summary>
    SetField
}

public readonly record struct WebLingoInstruction(WebLingoOpCode OpCode, int A = 0, int B = 0, int C = 0, int D = 0);

public enum WebLingoConstantKind : byte { Int64, Binary64, String, Decimal }

/// <summary>
/// A constant; a binary64 is kept as its IEEE 754 bits, so that constants compare and copy exactly, and a string as
/// its <paramref name="Text"/>, which only a string has.
/// </summary>
public readonly record struct WebLingoConstant(WebLingoConstantKind Kind, long Bits, string? Text = null)
{
    public static WebLingoConstant String(string text) => new(WebLingoConstantKind.String, 0, text);
    /// <summary>A decimal, its digits as its literal writes them (section 3.2), which a VM reads as .NET's decimal.</summary>
    public static WebLingoConstant Decimal(string digits) => new(WebLingoConstantKind.Decimal, 0, digits);

    /// <summary>Whether text is a decimal's digits, as a decimal constant holds them, within a decimal's range.</summary>
    public static bool IsDecimal(string digits) =>
        decimal.TryParse(digits, System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowExponent,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    public static WebLingoConstant Int64(long value) => new(WebLingoConstantKind.Int64, value);
    public static WebLingoConstant Binary64(double value) => new(WebLingoConstantKind.Binary64, BitConverter.DoubleToInt64Bits(value));
}

/// <summary>A host built-in the module calls, linked by name when the module runs.</summary>
public sealed record WebLingoImport(string Name, int Arity);

/// <summary>
/// A function's member caches (specification, section 9, "M0.21 parts", part 3): a slot for each of its instructions,
/// holding what a VM made of what that instruction found last, an object of the VM's, immutable, replaced whole by one
/// reference's write, so that executions on any worker share them and none sees one half written. No part of a
/// function's equality, nor of its file.
/// </summary>
public sealed class WebLingoMemberSites(int count)
{
    private readonly object?[] _entries = count <= 0 ? [] : new object?[count];

    /// <summary>The entry of the instruction at <paramref name="instruction"/>; none beyond its code.</summary>
    public object? this[int instruction]
    {
        get => (uint)instruction < (uint)_entries.Length ? _entries[instruction] : null;
        set
        {
            if ((uint)instruction < (uint)_entries.Length) _entries[instruction] = value;
        }
    }

    public override bool Equals(object? obj) => obj is WebLingoMemberSites;
    public override int GetHashCode() => 0;
}

/// <summary>
/// A function: its registers (<see cref="LocalCount"/> locals, then temporaries, <see cref="RegisterCount"/> in all)
/// and its code. <see cref="Spans"/> (one per instruction) and <see cref="LocalNames"/> (one per local) are
/// optional debugging information, for runtime error messages.
/// </summary>
public sealed record WebLingoFunction(
    string Name, int RegisterCount, int LocalCount, IReadOnlyList<WebLingoInstruction> Code,
    IReadOnlyList<WebLingoSourceSpan>? Spans = null, IReadOnlyList<string>? LocalNames = null)
{
    /// <summary>
    /// What its instructions that reach a member found last, beside its code, made with it (specification, section 9,
    /// "M0.21 parts", part 3): every execution of it shares them, and none is written to a file.
    /// </summary>
    public WebLingoMemberSites MemberSites { get; init; } = new(Code.Count);

    /// <summary>
    /// Source debugger metadata, which a compilation for a debugger gives, and a file has where <c>weblingo compile --debug</c>
    /// wrote it, its debugging section (<see cref="WebLingoBytecodeFile"/>).
    /// </summary>
    public WebLingoFunctionDebugInfo? DebugInfo { get; init; }
    /// <summary>The most registers a function may have (specification, section 5.3, proposed).</summary>
    public const int MaxRegisters = 65535;

    /// <summary>How many arguments it takes, into its first registers.</summary>
    public int ParameterCount { get; init; }
    /// <summary>The file its spans are in, when it is not the program's own: a module it imports (section 3.9).</summary>
    public string? Source { get; init; }
    /// <summary>Whether it is a native class's member, which only calls its built-in, an error in it reported where it was called (section 3.9).</summary>
    public bool IsNative { get; init; }
    /// <summary>
    /// For a native member that only calls one built-in with its parameters, in order: that built-in's import, which the VM
    /// calls in its place, without its frame, and what the call gives; -1 for any other function (section 3.9).
    /// </summary>
    public int NativeBuiltin { get; init; } = -1;
    public WebLingoNativeResult NativeResult { get; init; }
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
    /// Whether it has a rest parameter after its parameters, which it does not count (section 3.5): the arguments after
    /// them, an array, empty when there are none, which its first instruction, <see cref="WebLingoOpCode.TakeRest"/>,
    /// takes; a call then gives at least its parameters but the optional ones, and any number more.
    /// </summary>
    public bool HasRest { get; init; }
    /// <summary>
    /// Its protected regions, innermost first, which an exception raised in it, or in a call it makes, looks through for
    /// the first that holds the instruction it was raised at (section 3.7; section 5.4's table-driven regions).
    /// </summary>
    public IReadOnlyList<WebLingoHandler> Handlers { get; init; } = [];
    /// <summary>
    /// The cleanups it runs on their way out with no exception, a <c>finally</c>'s body and a <c>using</c>'s disposal
    /// written inline after a <c>try</c>'s body and before a <c>break</c>, a <c>continue</c> or a <c>return</c> out of it,
    /// which no handler marks: a cancellation that comes while one runs waits until it has left it (section 9, "A
    /// cancelled request's cleanup, parts", and "A Cancellation in a Cleanup on Its Normal Way Out").
    /// </summary>
    public IReadOnlyList<WebLingoWayOut> WaysOut { get; init; } = [];
}

/// <summary>A cleanup's code on its way out with no exception, from <paramref name="Start"/> to <paramref name="End"/> − 1.</summary>
public readonly record struct WebLingoWayOut(int Start, int End);

/// <summary>
/// A protected region: an exception raised by an instruction from <paramref name="Start"/> to
/// <paramref name="End"/> − 1, or in a call one of them makes, jumps to <paramref name="Target"/>, the exception in
/// register <paramref name="Value"/> and where it was first raised, which <see cref="WebLingoOpCode.Rethrow"/> takes, in
/// register <paramref name="Origin"/>. A cleanup's, a <c>finally</c>'s or a <c>using</c>'s, as <paramref name="Kind"/>
/// says, has its code from <paramref name="Target"/> to <paramref name="CleanupEnd"/> − 1, the last its
/// <see cref="WebLingoOpCode.Rethrow"/> of those registers, so that a cancellation can run a function's cleanups alone,
/// and tell which handlers are within one (section 9, "A cancelled request's cleanup, parts", part 1); a <c>catch</c>'s
/// <paramref name="CleanupEnd"/> is 0.
/// </summary>
public readonly record struct WebLingoHandler(int Start, int End, int Target, int Value, int Origin,
    WebLingoHandlerKind Kind = WebLingoHandlerKind.Catch, int CleanupEnd = 0)
{
    /// <summary>Whether it is a cleanup's, a <c>finally</c>'s or a <c>using</c>'s.</summary>
    public bool IsCleanup => Kind != WebLingoHandlerKind.Catch;
}

/// <summary>What a handler is: a <c>catch</c>'s, or a cleanup's, a <c>finally</c>'s or a <c>using</c>'s.</summary>
public enum WebLingoHandlerKind : byte
{
    Catch,
    Finally,
    Using
}

/// <summary>A class of a module: its name, its constructor's function, or -1 if it has none, and its methods' (section 3.7).</summary>
public sealed record WebLingoClassInfo(string Name, int Constructor, IReadOnlyList<WebLingoClassMethod> Methods)
{
    /// <summary>Its static methods, functions without <c>this</c>, by name (section 3.7).</summary>
    public IReadOnlyList<WebLingoClassMethod> StaticMethods { get; init; } = [];

    /// <summary>The names of its static fields, which exist, as <c>null</c>, from when it is made.</summary>
    public IReadOnlyList<string> StaticFields { get; init; } = [];

    /// <summary>
    /// The fields its instances have places for, by place: its base's first, then those its constructor and its other
    /// methods assign to <c>this</c>; none where its base is not a class of the program's, whose fields the compiler
    /// knows, its instances then having its base's places (specification, section 9, "M0.21 parts", part 2).
    /// </summary>
    public IReadOnlyList<string> Fields { get; init; } = [];

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
    /// <summary>Its parameters (section 3.9), whose values its function takes as one map; none for the program's own file, which the module lists.</summary>
    public IReadOnlyList<WebLingoModuleParameter> Parameters { get; init; } = [];

    /// <summary>
    /// Whether it is a native module (section 3.9), which, as a standard module, has no function and no parameters, its
    /// exports the built-ins its <c>MakeModule</c> is given.
    /// </summary>
    public bool IsNative { get; init; }
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

    /// <summary>
    /// Whether it is an import's (section 3.9): a module compiled while a program runs, its file the second of its table
    /// of files, whose entry point takes the values its parameters are given, imports it, and gives its instance.
    /// </summary>
    public bool IsImport { get; init; }

    /// <summary>
    /// The program's own parameters (section 3.9), which its host gives values; with some, its entry point takes one
    /// parameter, the map of their values.
    /// </summary>
    public IReadOnlyList<WebLingoModuleParameter> Parameters { get; init; } = [];

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
