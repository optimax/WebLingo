using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.VM;

/// <summary>
/// The register VM (specification, sections 5.2 and 8.4, "VM"). It verifies a module before running it, links the
/// module's imports to the host's built-ins by name and arity, and runs it with a <c>switch</c> over the operations
/// (section 5.6), synchronously, on the calling thread, which stands for the context's owner worker. A call pushes a
/// frame onto a stack of the VM's own, not .NET's, so that deep recursion is bounded by the budget (WL3003), not by
/// the thread's stack. Each frame's registers, and its closure, are roots of the execution's heap. A runtime error
/// stops the execution and is returned with the source span of the instruction that raised it. Nothing a module or
/// a built-in does can crash the VM: a failure is a result.
/// With its context's <c>Suspends</c>, an execution that has nothing to run but waits returns, waiting, and
/// <see cref="Resume"/> goes on with it (section 6.6).
/// <para>
/// Its dispatch is tuned for speed (section 5.6): one loop runs every instruction, with the running frame's code,
/// registers and position in locals; the budget and the cancellation signal are looked at only when a count of
/// instructions comes due; integer and binary64 arithmetic and comparisons are done in the loop, and anything else
/// (strings, mixed operands, every error) by <see cref="WebLingoOperators"/>, so that the results and the messages are the
/// runtime's; and a call reuses the frame, and the registers, that the last call at its depth left.
/// </para>
/// </summary>
public sealed class WebLingoVirtualMachine : IWebLingoVirtualMachine
{
    /// <summary>How many instructions run between two looks at the cancellation signal.</summary>
    public const int CancellationInterval = WebLingoExecutionContext.CancellationInterval;

    private readonly IWebLingoBytecodeVerifier _verifier;

    /// <summary>What finds and compiles a module a program imports while it runs (section 3.9); none, and it can import none.</summary>
    public IWebLingoModuleLoader? Loader { get; init; }

    /// <summary>
    /// What finds and compiles a component a program renders (section 13), which runs anew each time; none, and it can
    /// render none.
    /// </summary>
    public IWebLingoModuleLoader? Components { get; init; }

    public IWebLingoRegisterDebugger? Debugger { get; init; }

    public WebLingoVirtualMachine() : this(new WebLingoBytecodeVerifier()) { }

    public WebLingoVirtualMachine(IWebLingoBytecodeVerifier verifier) => _verifier = verifier;

    public WebLingoExecutionResult Execute(WebLingoCompiledModule module, WebLingoExecutionContext context)
    {
        var verification = _verifier.Verify(module);
        if (!verification.IsValid)
        {
            return Failure(WebLingoDiagnosticCodes.BytecodeVerificationFailure,
                $"The module failed verification: {verification.Error}", new WebLingoSourceSpan(0, 0));
        }

        var largest = module.EntryPoint;
        foreach (var function in module.Functions)
        {
            if (function.RegisterCount > largest.RegisterCount) largest = function;
        }
        if (largest.RegisterCount > context.Resources.MaxRegisters)
        {
            return Failure(WebLingoDiagnosticCodes.BudgetExceeded,
                $"'{largest.Name}' needs {largest.RegisterCount} registers; this execution allows {context.Resources.MaxRegisters}.",
                new WebLingoSourceSpan(0, 0));
        }

        var builtins = new IWebLingoBuiltinFunction[module.Imports.Count];
        for (var i = 0; i < builtins.Length; i++)
        {
            var import = module.Imports[i];
            if (!WebLingoIntrinsicBuiltins.TryResolve(context.Services, import.Name, out var builtin) || builtin.Arity != import.Arity)
            {
                return Failure(WebLingoDiagnosticCodes.UndefinedIdentifier,
                    $"This host has no built-in '{import.Name}' taking {import.Arity} arguments.", new WebLingoSourceSpan(0, 0));
            }
            builtins[i] = builtin;
        }

        // The execution's own heap (worker-local, section 4.3), whose roots are the constants and the frames.
        var heap = new WebLingoHeap(context.Resources.MaxHeapUnits, context.RegexTimeoutMilliseconds);
        heap.FileSystem = context.FileSystem;
        heap.Network = context.Network;
        heap.Databases = context.Databases;
        heap.Mail = context.Mail;
        heap.Processes = context.Processes;
        heap.Workers = context.Workers;
        heap.Allowed = context.Allowed;
        heap.Crypto = context.Crypto;
        heap.Environment = context.Environment;
        heap.Log = context.Log;
        heap.ConsoleInput = context.ConsoleInput;
        heap.ErrorOutput = context.ErrorOutput;
        heap.Console = context.Console;
        heap.WallClock = context.WallClock;
        heap.TimeZone = context.TimeZone;
        heap.SchedulerHost = context.SchedulerHost;
        var constants = new WebLingoValue[module.Constants.Count];
        var names = new WebLingoMemberName[module.Constants.Count]; // Each string constant as a member's name.
        heap.AddRoots(constants);
        for (var i = 0; i < constants.Length; i++)
        {
            var constant = module.Constants[i];
            if (constant.Kind == WebLingoConstantKind.String)
            {
                names[i] = new WebLingoMemberName(WebLingoMembers.Find(constant.Text!), constant.Text!);
                if (!heap.TryAllocateString(constant.Text!, out constants[i], out var error))
                {
                    return Failure(error!.Code, error.Message, new WebLingoSourceSpan(0, 0));
                }
                continue;
            }
            if (constant.Kind == WebLingoConstantKind.Decimal)
            {
                if (!heap.TryAllocateDecimal(WebLingoValue.ParseDecimal(constant.Text!), out constants[i], out var decimalError))
                {
                    return Failure(decimalError!.Code, decimalError.Message, new WebLingoSourceSpan(0, 0));
                }
                continue;
            }
            constants[i] = constant.Kind == WebLingoConstantKind.Int64
                ? WebLingoValue.FromInt64(constant.Bits)
                : WebLingoValue.FromBinary64Bits(constant.Bits);
        }

        // Each function's code as an array, which the loop indexes directly.
        var codes = new WebLingoInstruction[module.Functions.Count][];
        for (var i = 0; i < codes.Length; i++) codes[i] = ToArray(module.Functions[i].Code);

        var execution = new Execution(module, ToArray(module.EntryPoint.Code), codes, constants, names, builtins, context, heap)
        {
            Loader = Loader,
            Components = Components,
            Debugger = Debugger
        };
        Debugger?.ModuleLoaded(module);
        return Run(execution, heap, resuming: false);
    }

    /// <summary>The execution that waits (<see cref="WebLingoExecutionResult.Waiting"/>), and its heap, until it is resumed.</summary>
    private (Execution Execution, WebLingoHeap Heap)? _waiting;

    /// <summary>
    /// Goes on with the execution that its last <see cref="Execute"/>, or <see cref="Resume"/>, left waiting, once its
    /// scheduler host has been posted work, or its next timer's time has come (section 6.6), on the same worker.
    /// </summary>
    public WebLingoExecutionResult Resume()
    {
        if (_waiting is not { } waiting) throw new InvalidOperationException("No execution of this VM waits.");
        _waiting = null;
        return Run(waiting.Execution, waiting.Heap, resuming: true);
    }

    /// <summary>Runs an execution, or goes on with one that waited, keeping it if it waits, and releasing its heap's host objects once it ends.</summary>
    private WebLingoExecutionResult Run(Execution execution, WebLingoHeap heap, bool resuming)
    {
        WebLingoExecutionResult? result = null;
        try
        {
            result = execution.Run(resuming);
            // A worker's execution goes on after its top level while its tasks have work (section 9, "M0.26 parts", part 2).
            while (result.Succeeded && execution.GoesOn(result)) result = execution.Run(resuming: true);
            return result;
        }
        finally
        {
            if (result is { Waiting: true }) _waiting = (execution, heap);
            else heap.ReleaseHostObjects(); // What native classes' instances still hold (section 3.9).
        }
    }

    /// <summary>A copy of the code, without LINQ, which the portable core does not use.</summary>
    private static WebLingoInstruction[] ToArray(IReadOnlyList<WebLingoInstruction> code)
    {
        var array = new WebLingoInstruction[code.Count];
        for (var i = 0; i < array.Length; i++) array[i] = code[i];
        return array;
    }

    /// <summary>
    /// A function running, at one depth of the VM's stack: its code, its registers, the closure it runs as (a root,
    /// which keeps its captured cells alive), and where its caller goes on. A frame, and its registers, are reused by
    /// the next call at the same depth; its registers may be more than the function's, and are null when unused.
    /// </summary>
    private sealed class Frame
    {
        public WebLingoFunction Function = null!;
        public WebLingoInstruction[] Code = [];
        public WebLingoValue[] Registers = [];
        /// <summary>The closure it runs as, and, for a disposal, the instance disposed: the frame's roots.</summary>
        /// <summary>
        /// The frame's roots: the closure it runs as; for a disposal, the instance disposed; an exception a disposal keeps
        /// while its next methods run (section 3.7); and an <c>async</c> function's task (section 3.8).
        /// </summary>
        public readonly WebLingoValue[] Closure = new WebLingoValue[4];
        public WebLingoValue[] Cells = [];
        /// <summary>The next instruction, while the frame waits for a call it made to return.</summary>
        public int Pc;
        public long DebugActivation;
        /// <summary>The caller's register that the call's result goes to.</summary>
        public int ResultRegister;
        /// <summary>For a constructor run by <c>new</c>: the instance, which is the call's result (section 3.7).</summary>
        public WebLingoValue Constructed;
        /// <summary>
        /// For a disposal: the instance's dispose methods, which run on it in turn in this frame, and the next of them;
        /// the instance is the frame's second root (section 3.7).
        /// </summary>
        public WebLingoValue[]? DisposeChain;
        public int DisposeNext;
        /// <summary>Whether a disposal keeps an exception one of its methods threw, its runtime error if it is one, and where it was thrown.</summary>
        public bool Holds;
        public WebLingoRuntimeError? HeldError;
        public long HeldOrigin;
        /// <summary>
        /// For a native function's frame (section 3.9): where the program called it, set when its first run ends, at which an
        /// error it raises once the scheduler has resumed it, with no caller below it, is reported.
        /// </summary>
        public long CalledFrom = WebLingoExceptions.Nowhere;
        /// <summary>
        /// For a toString() that a text being built waits for, that text (section 3.7); for a call that a built-in's work
        /// waits for, that work (<see cref="WebLingoCalls"/>).
        /// </summary>
        public Waiter? Waiter;
        /// <summary>
        /// For an <c>async</c> function's frame: its task, its fourth root too; whether it still runs on its caller's stack,
        /// before its first <c>await</c>; and, while it waits, where its <c>await</c>'s value goes (section 3.8).
        /// </summary>
        public WebLingoValue Task;
        public bool FirstRun;
        /// <summary>The roots a waiting frame's task holds: its registers and its closure, made once (section 3.8).</summary>
        public WebLingoValue[][]? WaitingRoots;
        public int AwaitDestination; // -1 drops what it gives, as a using's await of its disposal does.
    }

    /// <summary>
    /// A function as a call reads it: its code, and what its call checks, as fields. The dispatch method reads them without
    /// a call, where a property's getter, which .NET inlines into it until its budget of inlining is spent, is a call of its
    /// own once it is, as it is by its label <c>call:</c>, where every call goes (specification, section 5.6).
    /// </summary>
    private sealed class Callable(WebLingoFunction function, WebLingoInstruction[] code)
    {
        public readonly WebLingoFunction Function = function;
        public readonly WebLingoInstruction[] Code = code;
        public readonly int ParameterCount = function.ParameterCount;
        public readonly int OptionalCount = function.OptionalCount;
        public readonly int RegisterCount = function.RegisterCount;
        public readonly int NativeBuiltin = function.NativeBuiltin;
        public readonly bool HasThis = function.HasThis;
        public readonly bool IsAsync = function.IsAsync;
    }

    /// <summary>What a frame's caller waits for it to give: a text being built, or a built-in's work.</summary>
    private abstract class Waiter
    {
        /// <summary>Lets go of the values it holds, as roots, where its frame ends with an exception, or is never pushed.</summary>
        public abstract void Unroot(WebLingoHeap heap);
    }

    /// <summary>
    /// A built-in's work that calls the program's functions (<see cref="WebLingoCalls"/>), its values roots from its
    /// beginning to its end, its result going to the register <see cref="Destination"/> of the frame that called it.
    /// </summary>
    private sealed class CallsWaiting(WebLingoCalls work, int destination) : Waiter
    {
        public WebLingoCalls Work { get; } = work;
        public int Destination { get; } = destination;
        public override void Unroot(WebLingoHeap heap) => heap.RemoveLastRoots(Work.Values);
    }

    /// <summary>
    /// A text being built (section 3.7), and what it is for: a <c>ToText</c>'s result, or a built-in that writes it,
    /// whose result goes where <see cref="Destination"/> says; and whether its VM keeps its values as roots, as it
    /// does while a toString() it waits for runs.
    /// </summary>
    private sealed class TextCall(IWebLingoTextBuilder builder, IWebLingoBuiltinFunction? builtin, int destination) : Waiter
    {
        public override void Unroot(WebLingoHeap heap)
        {
            if (Rooted) heap.RemoveLastRoots(Builder.Roots);
        }

        public IWebLingoTextBuilder Builder { get; } = builder;
        public IWebLingoBuiltinFunction? Builtin { get; } = builtin;
        /// <summary>The register its result goes to.</summary>
        public int Destination { get; } = destination;
        public bool Rooted { get; set; }
    }

    private sealed class Execution(WebLingoCompiledModule module, WebLingoInstruction[] entryCode, WebLingoInstruction[][] codes,
        WebLingoValue[] constants, WebLingoMemberName[] names, IWebLingoBuiltinFunction[] builtins, WebLingoExecutionContext context, WebLingoHeap heap)
        : IWebLingoRootSource, IWebLingoDebugScope
    {
        /// <summary>
        /// The module's functions, by their indices, as a call reads them (<see cref="Callable"/>); grown with the module
        /// when a module is joined to it.
        /// </summary>
        private Callable[] _callables = Callables(module.Functions, codes, 0, []);

        private static Callable[] Callables(IReadOnlyList<WebLingoFunction> functions, WebLingoInstruction[][] codes, int first,
            Callable[] made)
        {
            var array = new Callable[functions.Count];
            Array.Copy(made, array, first);
            for (var i = first; i < array.Length; i++) array[i] = new Callable(functions[i], codes[i]);
            return array;
        }

        /// <summary>
        /// The depth of the frame that runs, which <c>Run</c> writes where its own changes, and below which every frame of
        /// <see cref="_frames"/> runs, or waits for a call it made, or is null, its task holding it while it awaits.
        /// </summary>
        private int _depth;

        /// <summary>
        /// The frames on the VM's stack, for the heap's collector: their registers and closures, the roots that each call
        /// added and each return removed until 23 November 2026 (specification, section 5.6, "Where the time goes", cause 4).
        /// </summary>
        public void AddRootArrays(List<WebLingoValue[]> arrays)
        {
            var frames = _frames;
            for (var depth = 0; depth <= _depth && depth < frames.Length; depth++)
            {
                if (frames[depth] is not { } frame) continue;
                arrays.Add(frame.Registers);
                arrays.Add(frame.Closure);
            }
        }

        /// <summary>The frames, twice as many, which <see cref="_frames"/> then holds too.</summary>
        private Frame[] Deeper(Frame[] frames)
        {
            Array.Resize(ref frames, frames.Length * 2);
            return _frames = frames;
        }

        /// <summary>
        /// Whether <c>Run</c> resumes a task the scheduler holds, which its loop's first look at its budget sees: a field,
        /// not a local, so that the loop keeps no register for what it reads once (Documents/implementation.md, "A VM's
        /// Dispatch Method Is Sensitive to Its Locals").
        /// </summary>
        private bool _resuming;

        public WebLingoExecutionResult Run(bool resuming)
        {
            Frame[] frames;
            var depth = 0;
            Frame frame;
            if (resuming)
            {
                frames = _frames;
                if (_stopDepth < 0)
                {
                    // It waited with nothing on the VM's stack: it goes on where the scheduler resumes a task (section 6.6).
                    frame = new Frame();
                }
                else
                {
                    // Stopped by its debugger as at an await: it goes on where it stopped, once the debugger lets it.
                    if ((depth = GoesOn(_executed)) < 0) return _left!;
                    frame = frames[depth];
                    resuming = false;
                }
            }
            else
            {
                frames = _frames = new Frame[16];
                frame = frames[0] = new Frame
                {
                    Function = module.EntryPoint,
                    Code = entryCode,
                    Registers = new WebLingoValue[module.EntryPoint.RegisterCount]
                };
                heap.AddRoots(_errorClass);
                heap.AddRoots(_objectClass);
                heap.AddRoots(_valueClasses);
                heap.AddRoots(_errorClasses);
                heap.AddRoots(_main);
                heap.AddRoots(_rest);
                heap.AddRoots(_made);
                heap.AddRoots(_operands); // A pending call's arguments, a module's values among them, until its frame holds them.
                _modules = new WebLingoModuleTable(module.Units.Count, heap);
                for (var i = 0; i < module.Units.Count; i++) _unitClosures.Add(WebLingoValue.Null);
                heap.AddRoots(_unitClosures);
                heap.AddRoots(_linked);
                _depth = 0;
                heap.Frames = this;
                _scheduler = new WebLingoScheduler(heap, context.Clock) { Suspends = context.Suspends };
                heap.Scheduler = _scheduler;
                heap.Output = context.Output;
                heap.ValueClasses = _valueClasses;
                if (module.Parameters.Count > 0 && !TryGiveParameters(frame.Registers)) return _ended!;
                if (module.EntryPoint.IsAsync && !TryStartMain(frame)) return _ended!;
            }

            var code = frame.Code;
            var registers = frame.Registers;
            var cells = frame.Cells;
            var pc = frame.Pc; // 0 but where its debugger stopped it.
            var budget = context.InstructionBudget;
            var cancellation = context.Cancellation;
            var debugger = Debugger;
            var maxCallDepth = context.Resources.MaxCallDepth;
            var executed = _executed;
            // Its quantum, a suspending execution's, counted from where it begins or goes on (section 9, "M0.24 parts", part 2).
            _quantumDue = context.Suspends && context.Quantum > 0 ? executed + context.Quantum : long.MaxValue;
            var dueAt = executed; // The count of instructions at which the budget and the cancellation signal are looked at.
            _resuming = resuming;
            WebLingoRuntimeError? error = null;
            // A call being made, which the operations that call set up, and the code after their switch carries out: the
            // value called, the instance a method runs on, where the arguments are, where the result goes, and, for
            // new, the instance made. A result register of -1, a using's disposal's, drops the result.
            var callee = WebLingoValue.Null;
            var self = WebLingoValue.Null;
            var arguments = registers;
            var firstArgument = 0;
            var argumentCount = 0;
            var resultRegister = 0;
            var constructed = WebLingoValue.Null;
            try
            {
                while (true)
                {
                    if (executed == dueAt)
                    {
                        if (_resuming)
                        {
                            _resuming = false;
                            dueAt = Math.Min(budget, (executed / CancellationInterval + 1) * CancellationInterval);
                            goto schedule;
                        }
                        if (executed >= budget)
                        {
                            return Failure(WebLingoDiagnosticCodes.BudgetExceeded,
                                    $"The program ran {executed} instructions, its whole budget, and was stopped.", frame.Function, pc)
                                with { Instructions = executed };
                        }
                        if (cancellation is not null && executed % CancellationInterval == 0
                            && cancellation.IsCancellationRequested)
                        {
                            return Failure(WebLingoDiagnosticCodes.Cancelled, "The program was cancelled.", frame.Function, pc)
                                with { Instructions = executed };
                        }
                        if (executed >= _quantumDue) return Yield(frames, frame, depth, pc, executed);
                        dueAt = Math.Min(budget, (executed / CancellationInterval + 1) * CancellationInterval);
                    }
                    if (debugger is not null && DebugInstruction(debugger, frames, frame, depth, pc, executed)) return _left!;
                    executed++;

                    ref readonly var ins = ref code[pc];
                    switch (ins.OpCode)
                    {
                        case WebLingoOpCode.LoadConstant:
                            registers[ins.A] = constants[ins.B];
                            pc++;
                            continue;
                        case WebLingoOpCode.Move:
                            registers[ins.A] = registers[ins.B];
                            pc++;
                            continue;
                        case WebLingoOpCode.Negate:
                        {
                            if (!WebLingoOperators.TryNegate(registers[ins.B], heap, out var negated, out error)) break;
                            registers[ins.A] = negated;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.Plus:
                        {
                            if (!WebLingoOperators.TryPlus(registers[ins.B], heap, out var same, out error)) break;
                            registers[ins.A] = same;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.Add:
                            if (!Arithmetic(WebLingoOpCode.Add, registers, ins, registers[ins.C], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Subtract:
                            if (!Arithmetic(WebLingoOpCode.Subtract, registers, ins, registers[ins.C], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Multiply:
                            if (!Arithmetic(WebLingoOpCode.Multiply, registers, ins, registers[ins.C], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Divide:
                            if (!Arithmetic(WebLingoOpCode.Divide, registers, ins, registers[ins.C], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Remainder:
                            if (!Arithmetic(WebLingoOpCode.Remainder, registers, ins, registers[ins.C], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.IntegerDivide:
                            if (!IntegerDivide(registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Equal:
                            if (!Compare(WebLingoOpCode.Equal, registers, ins, registers[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.NotEqual:
                            if (!Compare(WebLingoOpCode.NotEqual, registers, ins, registers[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.Less:
                            if (!Compare(WebLingoOpCode.Less, registers, ins, registers[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.LessOrEqual:
                            if (!Compare(WebLingoOpCode.LessOrEqual, registers, ins, registers[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.Greater:
                            if (!Compare(WebLingoOpCode.Greater, registers, ins, registers[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.GreaterOrEqual:
                            if (!Compare(WebLingoOpCode.GreaterOrEqual, registers, ins, registers[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.AddConstant:
                            if (!Arithmetic(WebLingoOpCode.Add, registers, ins, constants[ins.C], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.SubtractConstant:
                            if (!Arithmetic(WebLingoOpCode.Subtract, registers, ins, constants[ins.C], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.MultiplyConstant:
                            if (!Arithmetic(WebLingoOpCode.Multiply, registers, ins, constants[ins.C], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.RemainderConstant:
                            if (!Arithmetic(WebLingoOpCode.Remainder, registers, ins, constants[ins.C], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.EqualConstant:
                            if (!Compare(WebLingoOpCode.Equal, registers, ins, constants[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.NotEqualConstant:
                            if (!Compare(WebLingoOpCode.NotEqual, registers, ins, constants[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.LessConstant:
                            if (!Compare(WebLingoOpCode.Less, registers, ins, constants[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.LessOrEqualConstant:
                            if (!Compare(WebLingoOpCode.LessOrEqual, registers, ins, constants[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.GreaterConstant:
                            if (!Compare(WebLingoOpCode.Greater, registers, ins, constants[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.GreaterOrEqualConstant:
                            if (!Compare(WebLingoOpCode.GreaterOrEqual, registers, ins, constants[ins.C], out error)) break;
                            goto compared;
                        case WebLingoOpCode.LoadNull:
                            registers[ins.A] = WebLingoValue.Null;
                            pc++;
                            continue;
                        case WebLingoOpCode.LoadBoolean:
                            registers[ins.A] = WebLingoValue.FromBoolean(ins.B != 0);
                            pc++;
                            continue;
                        case WebLingoOpCode.Not:
                        {
                            if (!WebLingoOperators.TryNot(heap.Held(registers[ins.B]), out var not, out error)) break;
                            registers[ins.A] = not;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.JumpIfFalse:
                        {
                            var condition = registers[ins.A];
                            if (condition.Kind != WebLingoValueKind.Boolean)
                            {
                                // A Boolean an instance holds is its condition (section 3.7).
                                condition = heap.Held(condition);
                                if (condition.Kind != WebLingoValueKind.Boolean)
                                {
                                    error = WebLingoOperators.CheckBoolean(condition, "A condition");
                                    break;
                                }
                            }
                            pc = condition.AsBoolean ? pc + 1 : ins.B;
                            continue;
                        }
                        case WebLingoOpCode.JumpIfGiven:
                        {
                            // Whether the caller gave a parameter, which, captured, is in a cell (section 3.5).
                            var parameter = registers[ins.A];
                            if (parameter.Kind == WebLingoValueKind.Cell) heap.TryReadCell(parameter, out parameter);
                            pc = parameter.Kind == WebLingoValueKind.Missing ? pc + 1 : ins.B;
                            continue;
                        }
                        case WebLingoOpCode.JumpIfTrue:
                        {
                            var condition = registers[ins.A];
                            if (condition.Kind != WebLingoValueKind.Boolean)
                            {
                                condition = heap.Held(condition);
                                if (condition.Kind != WebLingoValueKind.Boolean)
                                {
                                    error = WebLingoOperators.CheckBoolean(condition, "A condition");
                                    break;
                                }
                            }
                            pc = condition.AsBoolean ? ins.B : pc + 1;
                            continue;
                        }
                        case WebLingoOpCode.CheckBoolean:
                            if (registers[ins.A].Kind != WebLingoValueKind.Boolean && heap.Held(registers[ins.A]).Kind != WebLingoValueKind.Boolean)
                            {
                                error = WebLingoOperators.CheckBoolean(heap.Held(registers[ins.A]), "'&&' and '||' need Booleans:");
                                break;
                            }
                            pc++;
                            continue;
                        case WebLingoOpCode.Jump:
                            pc = ins.A;
                            continue;
                        case WebLingoOpCode.NewCell:
                            if (!heap.TryAllocateCell(WebLingoValue.Null, false, out registers[ins.A], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.TakeRest:
                            if (!TryTakeRest(out registers[ins.A], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.BoxRegister:
                        {
                            if (!heap.TryAllocateCell(registers[ins.A], true, out var boxed, out error)) break;
                            registers[ins.A] = boxed;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.CopyCell:
                        {
                            var hasValue = heap.TryReadCell(registers[ins.A], out var held);
                            if (!heap.TryAllocateCell(held, hasValue, out var copy, out error)) break;
                            registers[ins.A] = copy;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.LoadCell:
                            error = ReadCell(registers[ins.B], frame.Function, ins.B, out registers[ins.A]);
                            if (error is not null) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.StoreCell:
                            heap.WriteCell(registers[ins.A], registers[ins.B]);
                            pc++;
                            continue;
                        case WebLingoOpCode.LoadCapture:
                            error = ReadCell(cells[ins.B], null, ins.B, out registers[ins.A]);
                            if (error is not null) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.StoreCapture:
                            heap.WriteCell(cells[ins.A], registers[ins.B]);
                            pc++;
                            continue;
                        case WebLingoOpCode.MakeClosure:
                        {
                            if (!TryMakeClosure(ins.B, registers, cells, out var closure, out error)) break;
                            registers[ins.A] = closure;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.LoadBuiltin:
                            registers[ins.A] = WebLingoValue.FromHandle(WebLingoValueKind.Builtin, ins.B);
                            pc++;
                            continue;
                        case WebLingoOpCode.CallBuiltin:
                            if (heap.ToStringGiven && _texts[ins.B] && ins.D == 1 && NeedsText(registers[ins.C]))
                            {
                                // A built-in that writes text, of a value that may hold an instance whose toString() must run.
                                _text = new TextCall(new WebLingoTextBuilder(heap, registers[ins.C], false), builtins[ins.B], ins.A);
                                goto text;
                            }
                            error = CallBuiltin(builtins[ins.B], registers, ins);
                            if (error is not null) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.CallSpread:
                            // f(...values), new C(...values) and x.m(...values) (section 3.5): the array's elements where
                            // the call takes its arguments, then the call itself, as its own instruction would run it.
                            if (!TrySpread(frame, in ins, out error)) break;
                            registers = frame.Registers;
                            ins = ref _spread[0];
                            goto case WebLingoOpCode.Call;
                        case WebLingoOpCode.NewSpread:
                            if (!TrySpread(frame, in ins, out error)) break;
                            registers = frame.Registers;
                            ins = ref _spread[0];
                            goto case WebLingoOpCode.New;
                        case WebLingoOpCode.CallMethodSpread:
                            if (!TrySpread(frame, in ins, out error)) break;
                            registers = frame.Registers;
                            ins = ref _spread[0];
                            goto case WebLingoOpCode.CallMethod;
                        case WebLingoOpCode.Call:
                            // Register B must hold a function taking D arguments; the compiler checks what it can, and
                            // the call, after this switch, the rest (WL2005, WL2006).
                            callee = registers[ins.B];
                            self = WebLingoValue.Missing; // No receiver: a function with one takes it as its first argument.
                            arguments = registers;
                            firstArgument = ins.C;
                            argumentCount = ins.D;
                            resultRegister = ins.A;
                            constructed = WebLingoValue.Null;
                            goto call;
                        case WebLingoOpCode.GetMember:
                        {
                            // An instance's member by a helper .NET does not inline, at the place its cache found it last
                            // (specification, section 9, "M0.21 parts", part 3), so that this case spends little of .NET's
                            // budget of inlining (Documents/implementation.md, "A VM's Dispatch Method Is Sensitive to Its Locals").
                            var name = names[ins.C];
                            if (registers[ins.B].Kind == WebLingoValueKind.Instance
                                    ? !TryGetInstanceMember(frame.Function, code, pc, registers, out var member, out error)
                                    : !WebLingoMembers.TryGet(registers[ins.B], name.Member, name.Text, heap, ins.D != 0, out member, out error))
                            {
                                PendAccessor(error, member, registers[ins.B], default, ins.A);
                                break;
                            }
                            registers[ins.A] = member;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.SetMember:
                            if (registers[ins.A].Kind == WebLingoValueKind.Instance
                                    ? !TrySetInstanceMember(frame.Function, code, pc, registers, out error, out callee)
                                    : !WebLingoMembers.TrySet(registers[ins.A], names[ins.B].Text, registers[ins.C], heap, out error, out callee))
                            {
                                // A setter gives the value assigned, which its result, written where the value is, keeps.
                                PendAccessor(error, callee, registers[ins.A], registers[ins.C], ins.C);
                                break;
                            }
                            pc++;
                            continue;
                        case WebLingoOpCode.CallMethod:
                        {
                            var name = names[ins.B];
                            var target = registers[ins.C];
                            if (target.HasClass && target.Kind is WebLingoValueKind.Array or WebLingoValueKind.Map or WebLingoValueKind.Set
                                && WebLingoMembers.TryFindCollectionCallee(target, name.Text, heap, out callee, out self))
                            {
                                // A collection of a class extending Array, Map or Set: its field's function, or its class's
                                // method, before its own members (section 3.7).
                                arguments = registers;
                                firstArgument = ins.C + 1;
                                argumentCount = ins.D;
                                resultRegister = ins.A;
                                constructed = WebLingoValue.Null;
                                goto call;
                            }
                            if (target.Kind is WebLingoValueKind.Class or WebLingoValueKind.Module)
                            {
                                // A static member, called as any function is (section 3.7).
                                if (WebLingoMembers.TryGetStaticCallee(target, name.Member, name.Text, heap, out callee, out error))
                                {
                                    self = WebLingoValue.Missing;
                                    arguments = registers;
                                    firstArgument = ins.C + 1;
                                    argumentCount = ins.D;
                                    resultRegister = ins.A;
                                    constructed = WebLingoValue.Null;
                                    goto call;
                                }
                                // Else the class's built-in setMethod, below, which a static member of its name hides.
                                if (!WebLingoMembers.IsClassMethod(target, name.Member, heap)) break;
                            }
                            if (target.Kind == WebLingoValueKind.Instance)
                            {
                                // A field's function is called as any function is; a method runs on the instance; a native
                                // class's member it does not declare its fallback calls, below (section 3.10). A method
                                // where its cache found it last, else found and remembered (section 9, "M0.21 parts", part 3).
                                bool isMethod;
                                if (TryGetCachedMethod(frame.Function, pc, target, out callee)) isMethod = true;
                                else
                                {
                                    if (!WebLingoMembers.TryFindCallee(target, name.Text, heap, out callee, out isMethod, out error))
                                    {
                                        if (ReferenceEquals(error, WebLingoMembers.Fallback)) goto memberFallback;
                                        break;
                                    }
                                    if (isMethod) RememberMethod(frame.Function, code, pc, target, callee);
                                }
                                self = isMethod ? target : WebLingoValue.Missing;
                                arguments = registers;
                                firstArgument = ins.C + 1;
                                argumentCount = ins.D;
                                resultRegister = ins.A;
                                constructed = WebLingoValue.Null;
                                if (isMethod && name.Member == WebLingoMember.Dispose) goto dispose;
                                goto call;
                            }
                            if (TryValueToString(target, name, ins.D, heap, registers, ins.A, out error))
                            {
                                if (error is not null) break;
                                pc++;
                                continue;
                            }
                            if (WebLingoMembers.TryGetValueMethod(target, name.Member, name.Text, heap, out var objectMethod))
                            {
                                // Every value has Object's toString() and equal() (section 3.7).
                                callee = objectMethod;
                                self = target;
                                arguments = registers;
                                firstArgument = ins.C + 1;
                                argumentCount = ins.D;
                                resultRegister = ins.A;
                                constructed = WebLingoValue.Null;
                                goto call;
                            }
                            memberFallback:
                            if (!WebLingoMembers.TryCall(target, name.Member, name.Text, registers, ins.C + 1, ins.D, heap,
                                    out var result, out error))
                            {
                                break;
                            }
                            registers[ins.A] = result;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.MakeClass:
                        {
                            var baseClass = ins.D == 1 ? registers[ins.C] : WebLingoValue.Null;
                            if (!TryMakeClass(module.Classes[ins.B], baseClass, registers, cells, out var made, out error)) break;
                            registers[ins.A] = made;
                            if (_builtin[ins.B]) Remember(ins.B, made);
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.RegisterUnit:
                            // A file a module imported while the program runs shares with the program keeps the program's.
                            if (_unitClosures[ins.A].Kind == WebLingoValueKind.Null) _unitClosures[ins.A] = registers[ins.B];
                            pc++;
                            continue;
                        case WebLingoOpCode.MakeModule:
                            if (!TryMakeModule(ins.B, registers, ins.C, ins.D, out registers[ins.A], out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.ImportDynamic:
                            // Module.import(path, values), or a component's render: a task, at once, or the call that gives one
                            // (sections 3.9 and 13).
                            registers[ins.A] = ImportedWhileRunning(registers[ins.B],
                                (ins.D & 1) == 1 ? registers[ins.C] : WebLingoValue.Null, ins.D >= 2, ins.A, frame.Function, out error);
                            if (registers[ins.A].Kind == WebLingoValueKind.Task)
                            {
                                pc++;
                                continue;
                            }
                            break;
                        case WebLingoOpCode.Import:
                            // A file's instance, for the values given its parameters; else its function runs, as a call, whose
                            // task the code awaits, or the values are wrong (section 3.9).
                            registers[ins.A] = Imported(ins.B, ins.D == 1 ? registers[ins.C] : WebLingoValue.Null, ins.A, out error);
                            if (registers[ins.A].Kind == WebLingoValueKind.Module)
                            {
                                pc++;
                                continue;
                            }
                            break;
                        case WebLingoOpCode.Hold:
                            // hold(this, value): the built-in classes' constructors' (section 3.7).
                            if (!heap.TrySetHeld(registers[ins.B], registers[ins.C], out error)) break;
                            registers[ins.A] = WebLingoValue.Null;
                            pc++;
                            continue;
                        case WebLingoOpCode.Extend:
                            // extend: the class given its extension's methods and static members (section 3.7).
                            if (!TryExtendClass(module.Classes[ins.B], registers[ins.A], registers, cells, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.GetSuper:
                        {
                            if (!WebLingoMembers.TryGetSuper(registers[ins.B], registers[ins.C], names[ins.D].Text, heap, out var bound,
                                    out error))
                            {
                                PendAccessor(error, bound, registers[ins.C], default, ins.A);
                                break;
                            }
                            registers[ins.A] = bound;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.New:
                        {
                            // A new instance; its constructor, if its class has one, runs on it as a call, whose result
                            // is the instance (section 3.7).
                            var classValue = registers[ins.B];
                            if (classValue.Kind != WebLingoValueKind.Class)
                            {
                                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NotAFunction,
                                    $"{Capitalized(WebLingoValueFormatter.Describe(classValue, heap))} is not a class, and cannot be made with 'new'.");
                                break;
                            }
                            if (heap.MakesPlainValue(classValue, ins.D))
                            {
                                // new Integer(42) is 42, and new Integer() 0 (section 3.7).
                                if (!TryMakeValue(classValue, registers, ins.C, ins.D, ins.A, frame.Function, pc, out error)) break;
                                pc++;
                                continue;
                            }
                            if (!heap.TryAllocateInstance(classValue, out var instance, out error)) break;
                            callee = heap.ConstructorFor(classValue);
                            if (callee.Kind == WebLingoValueKind.Null)
                            {
                                if (ins.D != 0)
                                {
                                    error = WrongArgumentCount(heap.ClassName(classValue), 0, ins.D);
                                    break;
                                }
                                registers[ins.A] = instance;
                                pc++;
                                continue;
                            }
                            self = _made[0] = instance;
                            constructed = instance;
                            arguments = registers;
                            firstArgument = ins.C;
                            argumentCount = ins.D;
                            resultRegister = ins.A;
                            goto call;
                        }
                        case WebLingoOpCode.NewArray:
                        {
                            if (!heap.TryAllocateArray(registers, ins.B, ins.C, out var array, out error)) break;
                            registers[ins.A] = array;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.NewSet:
                        {
                            if (!heap.TryAllocateSet(registers, ins.B, ins.C, out var set, out error)) break;
                            registers[ins.A] = set;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.NewMap:
                        {
                            if (!heap.TryAllocateMap(registers, ins.B, ins.C, out var map, out error)) break;
                            registers[ins.A] = map;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.ForEachNext:
                        {
                            if (!WebLingoMembers.TryNext(registers[ins.B], registers[ins.B + 1], ins.C, registers, ins.B + 2, heap,
                                    out var next, out var done, out error))
                            {
                                break;
                            }
                            registers[ins.B + 1] = next;
                            pc = done ? ins.A : pc + 1;
                            continue;
                        }
                        case WebLingoOpCode.ForEachStart:
                        case WebLingoOpCode.ForEachVisits:
                        {
                            // Whether the VMs visit the collection themselves, or its iterate() does (section 3.6).
                            if (!WebLingoMembers.TryVisit(registers[ins.B], ins.OpCode == WebLingoOpCode.ForEachStart, heap, out var own,
                                    out error))
                            {
                                break;
                            }
                            pc = own ? ins.A : pc + 1;
                            continue;
                        }
                        case WebLingoOpCode.GetIndex:
                        {
                            if (!WebLingoMembers.TryGetIndex(registers[ins.B], registers[ins.C], heap, ins.D != 0, out var element,
                                    out error))
                            {
                                PendIndexer(error, element, registers[ins.B], registers[ins.C], default, ins.A);
                                break;
                            }
                            registers[ins.A] = element;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.SetIndex:
                            if (!WebLingoMembers.TrySetIndex(registers[ins.A], registers[ins.B], registers[ins.C], heap, out error, out callee))
                            {
                                // An indexer's setter gives the value assigned, which its result, written where the value is, keeps.
                                PendIndexer(error, callee, registers[ins.A], registers[ins.B], registers[ins.C], ins.C);
                                break;
                            }
                            pc++;
                            continue;
                        case WebLingoOpCode.Dispose:
                        {
                            // A using's end: its resource disposed, unless it is null, as x.dispose() disposes it, the
                            // result dropped (section 3.7).
                            var resource = registers[ins.A];
                            if (resource.Kind == WebLingoValueKind.Null)
                            {
                                pc++;
                                continue;
                            }
                            error = BeginDisposal(resource, 0, frame.Function, out var chain);
                            if (error is not null) break;
                            if (chain is null)
                            {
                                pc++;
                                continue;
                            }
                            if (_asyncDisposal && !heap.TryAllocateTask(out _newTask, out error)) break;
                            if (depth + 1 == frames.Length) frames = Deeper(frames);
                            Disposing(frames, depth + 1, chain, resource);
                            callee = chain[0];
                            self = resource;
                            arguments = registers;
                            firstArgument = 0;
                            argumentCount = 0;
                            resultRegister = -1;
                            constructed = WebLingoValue.Null;
                            goto call;
                        }
                        case WebLingoOpCode.IsInstance:
                        {
                            var type = registers[ins.C];
                            if (type.Kind != WebLingoValueKind.Class && TryGetEqual(type, out var equalMethod))
                            {
                                // A name that is not a class: type.equal(value) gives the answer (section 3.7).
                                callee = equalMethod;
                                self = type;
                                arguments = registers;
                                firstArgument = ins.B;
                                argumentCount = 1;
                                resultRegister = ins.A;
                                constructed = WebLingoValue.Null;
                                goto call;
                            }
                            registers[ins.A] = WebLingoValue.FromBoolean(WebLingoMembers.IsInstance(registers[ins.B], type, heap, _valueClasses));
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.ToText:
                            // C = 1: as write writes it, for an interpolation (section 3.5); else as Object's toString().
                            if (TryPlainText(registers[ins.B], heap, registers, ins.A, out error))
                            {
                                if (error is not null) break;
                                pc++;
                                continue;
                            }
                            _text = new TextCall(new WebLingoTextBuilder(heap, registers[ins.B], ins.C != 1), null, ins.A);
                            goto text;
                        case WebLingoOpCode.ToJson:
                            // Json.stringify(value, indent): its text, built as a value's is, a toJson() called on the way (section 3.6).
                            _text = new TextCall(new WebLingoJsonBuilder(heap, registers[ins.B], registers[ins.C]), null, ins.A);
                            goto text;
                        case WebLingoOpCode.Await:
                            _awaited = registers[ins.B];
                            frame.AwaitDestination = ins.A;
                            frame.Pc = pc + 1;
                            goto awaiting;
                        case WebLingoOpCode.Throw:
                            _thrown = registers[ins.A];
                            _thrownError = null;
                            _origin = Thrown(frames, depth, pc);
                            goto throwing;
                        case WebLingoOpCode.Rethrow:
                            // 'throw;', or a finally's end: the exception again, from where it was first thrown.
                            _thrown = registers[ins.A];
                            _thrownError = null;
                            _origin = registers[ins.B].Kind == WebLingoValueKind.Int64 ? registers[ins.B].AsInt64 : OriginOf(frame.Function, pc);
                            goto throwing;
                        case WebLingoOpCode.Return:
                        case WebLingoOpCode.ReturnValue:
                        {
                            var result = ins.OpCode == WebLingoOpCode.ReturnValue ? registers[ins.A] : WebLingoValue.Null;
                            if (depth == 0 && frame.Task.Kind != WebLingoValueKind.Task) return Finish(executed);
                            for (var i = frame.Function.RegisterCount - 1; i >= 0; i--) registers[i] = default; // So that nothing stays reachable.
                            if (frame.DisposeChain is not null) goto disposed;
                            if (frame.Task.Kind == WebLingoValueKind.Task)
                            {
                                _returned = result;
                                goto asyncReturned;
                            }
                            if (frame.Waiter is not null)
                            {
                                _returned = result;
                                goto texted;
                            }
                            frame.Closure[0] = WebLingoValue.Null;
                            if (frame.Constructed.Kind != WebLingoValueKind.Null) result = frame.Constructed; // What new made: an instance, or a collection.
                            frame.Constructed = WebLingoValue.Null;
                            var returnTo = frame.ResultRegister;

                            frame = frames[_depth = --depth];
                            code = frame.Code;
                            registers = frame.Registers;
                            cells = frame.Cells;
                            pc = frame.Pc;
                            registers[returnTo] = result;
                            continue;
                        }
                        case WebLingoOpCode.GetField:
                            // this.name in a method of its class: the field at its place, where the instance has it there and
                            // has assigned it; else read as GetMember reads it (specification, section 9, "M0.21 parts", part 2).
                            // Each calls helpers .NET does not inline, which read the instruction from the code themselves, and
                            // reads no property, so that it spends none of .NET's budget of inlining into this method, and keeps
                            // ins live across no call, which would keep it in memory on every instruction (Documents/
                            // implementation.md, "A VM's Dispatch Method Is Sensitive to Its Locals").
                            if (TryGetField(registers, code, pc))
                            {
                                pc++;
                                continue;
                            }
                            ins = ref ByName(code, pc);
                            goto case WebLingoOpCode.GetMember;
                        case WebLingoOpCode.SetField:
                            // this.name = value: the field at its place, where the instance has assigned it there already; else
                            // assigned as SetMember assigns it, the first time among them.
                            if (TrySetField(registers, code, pc))
                            {
                                pc++;
                                continue;
                            }
                            ins = ref ByName(code, pc);
                            goto case WebLingoOpCode.SetMember;
                        default:
                            // The verifier admits no other operation; this only guards against a verifier that did.
                            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BytecodeVerificationFailure,
                                $"The operation {(int)ins.OpCode} is unknown.");
                            break;
                    }
                    goto failed;

                compared:
                    // A comparison whose result the next instruction branches on, as an if's or a loop's test does: the branch
                    // taken at once, without the jump's own dispatch, the result in its register too (specification, section
                    // 5.6, "Where the time goes"). A class's operator, which Compare leaves to a call, is branched on after it.
                    if (pc + 1 < code.Length && code[pc + 1].OpCode is WebLingoOpCode.JumpIfFalse or WebLingoOpCode.JumpIfTrue
                        && code[pc + 1].A == ins.A)
                    {
                        pc = registers[ins.A].AsBoolean == (code[pc + 1].OpCode == WebLingoOpCode.JumpIfTrue) ? code[pc + 1].B : pc + 2;
                        continue;
                    }
                    pc++;
                    continue;

                call:
                    // The call the operations above set up: a built-in at once, a closure in a new frame, its arguments
                    // in its first registers, after the instance it runs on if it is a method (section 3.7).
                    if (callee.Kind == WebLingoValueKind.BoundMethod)
                    {
                        if (heap.IsDisposal(callee))
                        {
                            self = heap.GetBoundMethod(callee).Instance;
                            goto dispose;
                        }
                        (self, callee) = heap.GetBoundMethod(callee);
                    }
                    if (callee.Kind == WebLingoValueKind.Builtin)
                    {
                        var builtin = builtins[callee.Handle];
                        if (builtin.Arity != argumentCount)
                        {
                            error = WrongArgumentCount(builtin.Name, builtin.Arity, argumentCount);
                            goto failed;
                        }
                        if (heap.ToStringGiven && _texts[callee.Handle] && argumentCount == 1 && NeedsText(arguments[firstArgument]))
                        {
                            _text = new TextCall(new WebLingoTextBuilder(heap, arguments[firstArgument], false), builtin, resultRegister);
                            goto text;
                        }
                        error = CallBuiltin(builtin, arguments, firstArgument, argumentCount, out var builtinResult);
                        if (error is not null) goto failed;
                        registers[resultRegister] = builtinResult;
                        pc++;
                        continue;
                    }
                    if (callee.Kind != WebLingoValueKind.Function)
                    {
                        if (callee.Kind == WebLingoValueKind.Instance && heap.IsCallable(callee))
                        {
                            // An instance of a class extending Function is called as the function it holds (section 3.7).
                            callee = heap.Held(callee);
                            goto call;
                        }
                        var what = Capitalized(WebLingoValueFormatter.Describe(callee, heap));
                        error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NotAFunction, callee.Kind == WebLingoValueKind.Class
                            ? $"{what} is not a function: make an instance with 'new'."
                            : $"{what} is not a function, and cannot be called.");
                        goto failed;
                    }
                    {
                        var (index, closureCells) = heap.GetClosure(callee);
                        var callable = _callables[index];
                        var function = callable.Function;
                        var withThis = callable.HasThis && self.Kind != WebLingoValueKind.Missing ? 1 : 0; // A receiver, if the call has one.
                        if (callable.ParameterCount != argumentCount + withThis
                            && (argumentCount + withThis > callable.ParameterCount
                                || argumentCount + withThis < callable.ParameterCount - callable.OptionalCount))
                        {
                            // Too many arguments for any function but one with a rest parameter, which gathers those after
                            // its parameters into an array before the frame is made (section 3.5).
                            if (!function.HasRest || argumentCount + withThis < function.ParameterCount)
                            {
                                error = WrongArgumentCountOf(function.Name, function.ParameterCount - withThis, function.OptionalCount,
                                    function.HasRest, argumentCount);
                                goto failed;
                            }
                            if (!TryGatherRest(function, withThis, arguments, firstArgument, argumentCount, out error)) goto failed;
                            argumentCount = function.ParameterCount - withThis;
                        }
                        if (callable.NativeBuiltin >= 0 && TryCallNative(function, self, withThis, arguments, firstArgument, argumentCount,
                                registers, resultRegister, constructed, frames, depth, out error))
                        {
                            // A native member that only calls its built-in: the built-in at once, without its frame (section 3.9).
                            if (error is not null) goto failed;
                            pc++;
                            continue;
                        }
                        if (depth + 1 >= maxCallDepth)
                        {
                            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BudgetExceeded,
                                $"The calls nest more than {maxCallDepth} deep, which this execution allows.");
                            goto failed;
                        }

                        if (callable.IsAsync && !TryMakeTask(frames, depth, out error)) goto failed;
                        frame.Pc = pc + 1;
                        if ((_depth = ++depth) == frames.Length) frames = Deeper(frames);
                        var called = frames[depth] ??= new Frame();
                        if (called.Registers.Length < callable.RegisterCount)
                        {
                            called.Registers = new WebLingoValue[callable.RegisterCount];
                        }
                        var calledRegisters = called.Registers;
                        if (withThis == 1) calledRegisters[0] = self;
                        for (var i = 0; i < argumentCount; i++) calledRegisters[withThis + i] = arguments[firstArgument + i];
                        // The parameters left out are missing, until the function's code gives them their defaults.
                        for (var i = withThis + argumentCount; i < callable.ParameterCount; i++) calledRegisters[i] = WebLingoValue.Missing;
                        if (!ReferenceEquals(called.Function, function))
                        {
                            // A frame that ran the same function last keeps its code: no write to it, nor .NET's barrier.
                            called.Function = function;
                            called.Code = callable.Code;
                        }
                        if (debugger is not null) called.DebugActivation = 0;
                        if (!ReferenceEquals(called.Cells, closureCells)) called.Cells = closureCells;
                        called.Closure[0] = callee;
                        called.ResultRegister = resultRegister;
                        called.Constructed = constructed;
                        if (callable.IsAsync) Started(called);

                        frame = called;
                        code = called.Code;
                        registers = calledRegisters;
                        cells = closureCells;
                        pc = 0;
                        continue;
                    }

                dispose:
                    // x.dispose(): the instance's dispose methods run on it in turn, once, in the frame of one call, which
                    // gives null (section 3.7).
                    {
                        error = BeginDisposal(self, argumentCount, null, out var chain);
                        if (error is not null) goto failed;
                        if (_asyncDisposal && !heap.TryAllocateTask(out _newTask, out error)) goto failed;
                        if (chain is null)
                        {
                            registers[resultRegister] = WebLingoValue.Null;
                            pc++;
                            continue;
                        }
                        if (depth + 1 == frames.Length) frames = Deeper(frames);
                        Disposing(frames, depth + 1, chain, self);
                        callee = chain[0];
                        goto call;
                    }

                disposed:
                    // A dispose method has returned: the instance's next one runs in the same frame, or the disposal is
                    // done, and gives null, or nothing to a using; or, if one of its methods threw, that exception is
                    // thrown again, from where the disposal was called (section 3.7).
                    {
                        if (frame.DisposeNext < frame.DisposeChain!.Length) goto nextDisposal;
                        frame.DisposeChain = null;
                        frame.Closure[0] = WebLingoValue.Null;
                        frame.Closure[1] = WebLingoValue.Null;
                        if (frame.Task.Kind == WebLingoValueKind.Task) goto asyncDisposed;
                        var disposedTo = frame.ResultRegister;
                        var held = frame.Holds;
                        if (held) TakeHeld(frame);
                        frame = frames[_depth = --depth];
                        code = frame.Code;
                        registers = frame.Registers;
                        cells = frame.Cells;
                        if (held)
                        {
                            pc = frame.Pc - 1;
                            goto throwing;
                        }
                        pc = frame.Pc;
                        if (disposedTo >= 0) registers[disposedTo] = WebLingoValue.Null;
                        continue;
                    }

                nextDisposal:
                    // The instance's next dispose method, in the same frame, whose roots were let go and registers cleared;
                    // its result is dropped.
                    {
                        var next = frame.DisposeChain![frame.DisposeNext++];
                        var (nextIndex, nextCells) = heap.GetClosure(next);
                        var nextFunction = _callables[nextIndex].Function;
                        if (registers.Length < nextFunction.RegisterCount)
                        {
                            frame.Registers = registers = new WebLingoValue[nextFunction.RegisterCount];
                        }
                        registers[0] = frame.Closure[1];
                        frame.Function = nextFunction;
                        if (debugger is not null) frame.DebugActivation = 0;
                        frame.Code = code = codes[nextIndex];
                        frame.Cells = cells = nextCells;
                        frame.Closure[0] = next;
                        pc = 0;
                        continue;
                    }

                text:
                    // A text being built, for ToText or a built-in that writes text, at pc: it goes on until it is done,
                    // or until an instance's toString() must run, as a call this instruction makes (section 3.7).
                    {
                        var building = _text!;
                        _text = null;
                        if (!building.Builder.TryBuild(out var instance, out var method))
                        {
                            error = building.Builder.Error; // JSON's: a value it cannot write (section 3.6).
                            if (error is not null)
                            {
                                if (building.Rooted) heap.RemoveLastRoots(building.Builder.Roots);
                                goto failed;
                            }
                            if (!building.Rooted)
                            {
                                heap.AddRoots(building.Builder.Roots);
                                building.Rooted = true;
                            }
                            if (depth + 1 == frames.Length) frames = Deeper(frames);
                            (frames[depth + 1] ??= new Frame()).Waiter = building;
                            callee = method;
                            self = instance;
                            arguments = registers;
                            firstArgument = 0;
                            argumentCount = 0;
                            resultRegister = 0;
                            constructed = WebLingoValue.Null;
                            goto call;
                        }
                        if (building.Rooted) heap.RemoveLastRoots(building.Builder.Roots);
                        if (!heap.TryAllocateString(building.Builder.Text, out var written, out error)) goto failed;
                        if (building.Builtin is { } writer)
                        {
                            error = CallBuiltin(writer, [written], 0, 1, out written);
                            if (error is not null) goto failed;
                        }
                        registers[building.Destination] = written;
                        pc++;
                        continue;
                    }

                texted:
                    // A toString() that a text waits for has returned, its frame let go of: its string goes into the
                    // text, which goes on, at the instruction it is for; anything but a string is WL3001 (section 3.7).
                    {
                        if (frame.Waiter is CallsWaiting) goto called;
                        _text = (TextCall)frame.Waiter!;
                        frame.Waiter = null;
                        frame.Closure[0] = WebLingoValue.Null;
                        frame = frames[_depth = --depth];
                        code = frame.Code;
                        registers = frame.Registers;
                        cells = frame.Cells;
                    }

                textGiven:
                    // What a toString() gave (_returned), for the text (_text) of the instruction that called it.
                    {
                        var building = _text!;
                        _text = null;
                        pc = frame.Pc - 1;
                        error = building.Builder.Supply(_returned);
                        _returned = WebLingoValue.Null;
                        if (error is not null)
                        {
                            if (building.Rooted) heap.RemoveLastRoots(building.Builder.Roots);
                            goto failed;
                        }
                        _text = building;
                        goto text;
                    }

                called:
                    // A call that a built-in's work waits for has returned, its frame let go of: what it gave goes to the
                    // work, which goes on, at the instruction that began it (specification, section 5.6, cause 6).
                    {
                        _calls = (CallsWaiting)frame.Waiter!;
                        frame.Waiter = null;
                        frame.Closure[0] = WebLingoValue.Null;
                        frame = frames[_depth = --depth];
                        code = frame.Code;
                        registers = frame.Registers;
                        cells = frame.Cells;
                    }

                callGiven:
                    // What the call gave (_returned), for the work (_calls) of the instruction that began it.
                    pc = frame.Pc - 1;
                    _calls!.Work.Supply(_returned);
                    _returned = WebLingoValue.Null;

                calls:
                    // A built-in's work, begun at pc: it goes on until it is done, its result where the instruction's goes,
                    // or until it waits for a call, which this instruction makes, as any call, in a frame of its own; a
                    // built-in it calls is called at once (specification, section 5.6, "Where the time goes", cause 6).
                    if (!GoOn(registers, out error)) goto failed;
                    if (_calls is null)
                    {
                        pc++;
                        continue;
                    }
                    if (depth + 1 == frames.Length) frames = Deeper(frames);
                    (frames[depth + 1] ??= new Frame()).Waiter = _calls;
                    callee = _calls.Work.Callee;
                    self = _calls.Work.Self;
                    arguments = _calls.Work.Values;
                    firstArgument = _calls.Work.FirstArgument;
                    argumentCount = _calls.Work.ArgumentCount;
                    resultRegister = 0;
                    constructed = WebLingoValue.Null;
                    _calls = null;
                    goto call;

                asyncDisposed:
                    // An async disposal's last method has returned: its task completes, or faults with what one of its
                    // methods threw. Still on its caller's stack, the disposal gives the task, which a using awaits.
                    DisposalEnded(frame);
                    if (!frame.FirstRun) goto schedule;
                    goto asyncGave;

                asyncReturned:
                    // An async function has returned: its task is done, with its result. Still on its caller's stack, its call
                    // gives the task; resumed by the scheduler, nothing runs below it (section 3.8).
                    Completed(frame);
                    if (!frame.FirstRun) goto schedule;
                    goto asyncGave;

                faulted:
                    // An exception leaves an async function: its task faults with it, for whatever awaits it. Still on its
                    // caller's stack, its call gives the task; resumed by the scheduler, nothing runs below it (section 3.8).
                    Faulted(frame);
                    if (!frame.FirstRun) goto schedule;
                    goto asyncGave;

                awaiting:
                    // await: the async function's frame leaves the stack, kept by its task until the scheduler resumes it
                    // with what the await gives. Still on its caller's stack, its call gives the task (section 3.8).
                    Suspend(frame);
                    frames[depth] = null!; // The frame is the task's now; this depth's next call gets a new one.
                    if (!frame.FirstRun) goto schedule;

                asyncGave:
                    // An async function's first run has ended, at its first await, its return or its exception: its call
                    // gives its task, to where the call's result goes, or to the text whose toString() it is.
                    Gave(frame, frames, depth);
                    frame = frames[_depth = --depth];
                    code = frame.Code;
                    registers = frame.Registers;
                    cells = frame.Cells;
                    if (_text is not null) goto textGiven;
                    if (_calls is not null) goto callGiven;
                    if (_usingAwaits)
                    {
                        // A using awaits its async disposal, as await would, whatever it gives dropped (section 3.8).
                        _usingAwaits = false;
                        frame.AwaitDestination = -1;
                        goto awaiting;
                    }
                    pc = frame.Pc;
                    continue;

                schedule:
                    // Nothing runs on the VM's stack: the next ready task resumes where it awaited, or the execution ends.
                    if (!TryResume(frames, cancellation, executed))
                    {
                        if (!_waits) return _ended!;
                        _waits = false;
                        // It waits for work off its worker, or a timer's time: kept, for its host to resume (section 6.6).
                        (_frames, _executed) = (frames, executed);
                        return WebLingoExecutionResult.Waits(_scheduler.WaitMilliseconds) with { Instructions = executed };
                    }
                    depth = _depth = 0;
                    frame = frames[0];
                    code = frame.Code;
                    registers = frame.Registers;
                    cells = frame.Cells;
                    if (_resumedThrowing)
                    {
                        pc = frame.Pc - 1;
                        goto throwing;
                    }
                    pc = frame.Pc;
                    continue;

                failed:
                    if (_operator.Kind == WebLingoValueKind.Function)
                    {
                        // A class's operator, which the operation found: called with its two operands (section 3.7); or
                        // another call it left here, a property's getter or setter on its target.
                        callee = _operator;
                        _operator = WebLingoValue.Null;
                        self = _pendingSelf;
                        _pendingSelf = WebLingoValue.Null;
                        arguments = _operands;
                        firstArgument = 0;
                        argumentCount = _operatorCount;
                        resultRegister = _operatorResult;
                        constructed = WebLingoValue.Null;
                        goto call;
                    }
                    if (ReferenceEquals(error, CallsBegun)) goto calls; // A built-in's work, which calls the program's functions.
                    // A call that failed before its frame was pushed leaves nothing in that frame: no disposal, no text.
                    if (depth + 1 < frames.Length && frames[depth + 1] is { } unpushed)
                    {
                        unpushed.DisposeChain = null;
                        unpushed.Task = WebLingoValue.Null;
                        unpushed.Closure[3] = WebLingoValue.Null;
                        unpushed.FirstRun = false;
                        unpushed.Waiter?.Unroot(heap);
                        unpushed.Waiter = null;
                    }
                    // A runtime error: one that cannot be caught stops the program, and any other is thrown (section 3.7).
                    if (!WebLingoExceptions.IsCatchable(error!.Code))
                    {
                        return Failure(error.Code, error.Message, frame.Function, pc) with { Instructions = executed };
                    }
                    _thrown = WebLingoValue.Null;
                    _thrownError = error;
                    _origin = Raised(frames, depth, pc);

                throwing:
                    // An exception, raised at pc: the first handler around it in this frame catches it; a disposal it
                    // leaves goes on with its next method; otherwise the frame ends, and its caller's call is where it
                    // goes on. One that nothing catches stops the program (section 3.7).
                    {
                        var handlers = frame.Function.Handlers;
                        var found = FindHandler(handlers, pc);
                        if (found >= 0)
                        {
                            if (!TryTakeException(out var exception)) return Uncaught(executed);
                            var handler = handlers[found];
                            registers[handler.Value] = exception;
                            registers[handler.Origin] = WebLingoValue.FromInt64(_origin);
                            pc = handler.Target;
                            continue;
                        }
                        if (WebLingoExceptions.DisposalGoesOn && frame.DisposeChain is { } chain && frame.DisposeNext < chain.Length)
                        {
                            // Its next dispose method runs; the exception waits for the last, a later one replacing it.
                            Hold(frame);
                            Array.Clear(registers, 0, frame.Function.RegisterCount);
                            goto nextDisposal;
                        }
                        if (frame.Task.Kind == WebLingoValueKind.Task) goto faulted;
                        if (depth == 0) return Uncaught(executed);
                        Array.Clear(registers, 0, frame.Function.RegisterCount);
                        frame.Waiter?.Unroot(heap);
                        Release(frame);
                        frame = frames[_depth = --depth];
                        code = frame.Code;
                        registers = frame.Registers;
                        cells = frame.Cells;
                        pc = frame.Pc - 1; // The call it made.
                        goto throwing;
                    }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                                  or IndexOutOfRangeException)
            {
                // Only bytecode the verifier cannot judge (a register of the wrong kind) gets here.
                return Failure(WebLingoDiagnosticCodes.BytecodeVerificationFailure, $"The bytecode is invalid: {exception.Message}", frame.Function, pc) with { Instructions = executed };
            }
        }

        private long _debugActivation;
        public IWebLingoRegisterDebugger? Debugger { get; init; }

        /// <summary>
        /// The depth of the frame its debugger stopped it in, where it waits as at an await (section 5.7, "Debugging WASP
        /// pages, parts", part 2), that frame's <c>Pc</c> the instruction it stopped at; -1 where it is not so stopped.
        /// </summary>
        private int _stopDepth = -1;

        /// <summary>Whether it has just gone on from such a stop, which the point it stopped at is not stopped at again.</summary>
        private bool _wentOn;

        /// <summary>The count of instructions at which it yields its worker, its quantum's; <see cref="long.MaxValue"/> for none.</summary>
        private long _quantumDue = long.MaxValue;

        /// <summary>Whether its stop at <see cref="_stopDepth"/> is a yield of its quantum's, not its debugger's.</summary>
        private bool _yielded;

        /// <summary>
        /// Its quantum passed (section 9, "M0.24 parts", part 2): it waits where it is, before the instruction at
        /// <paramref name="pc"/>, its frames on the VM's stack, as its debugger's stop leaves it, for no time, so that its
        /// host runs the other requests' ready work, then resumes it there.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private WebLingoExecutionResult Yield(Frame[] frames, Frame frame, int depth, int pc, long executed)
        {
            frame.Pc = pc;
            (_frames, _executed, _depth, _stopDepth, _yielded) = (frames, executed, depth, depth, true);
            return WebLingoExecutionResult.Waits(0) with { Instructions = executed };
        }

        /// <summary>What <c>Run</c> returns where a debugger's stop leaves it: waiting, or cancelled.</summary>
        private WebLingoExecutionResult? _left;

        /// <summary>
        /// At a point of the debugger's, a statement's first instruction: whether it stops there, its frames given to it, and
        /// leaves <c>Run</c>, with <see cref="_left"/>: cancelled while the debugger held it, or waiting, as at an await, where
        /// the debugger lets it wait (<see cref="IWebLingoRegisterDebugger.Stopped"/>).
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool DebugInstruction(IWebLingoRegisterDebugger debugger, Frame[] frames, Frame frame, int depth, int pc, long executed)
        {
            if (_wentOn)
            {
                _wentOn = false; // The point it stopped at, gone on from.
                return false;
            }
            if (frame.Function.DebugInfo is not { } info || !info.Points.TryGetValue(pc, out var span)) return false;
            if (frame.DebugActivation == 0) frame.DebugActivation = ++_debugActivation;
            (_scopeFrame, _scopeInfo, _scopeOffset) = (frame, info, span.Start);
            var stops = debugger.ShouldStop(new(info.Source, span, depth, frame.DebugActivation, pc), this);
            (_scopeFrame, _scopeInfo) = (null, null);
            if (!stops) return false;

            var stack = new List<WebLingoDebugFrame>();
            for (var level = depth; level >= 0; level--)
            {
                var current = frames[level];
                if (current.Function.DebugInfo is not { } source) continue;
                var callerPc = current.Pc - 1;
                var position = level == depth ? span : SpanOf(current.Function, callerPc);
                var visibility = level == depth ? span.Start : StatementStart(source, callerPc, position.Start);
                stack.Add(new(current.Function.Name, source.Source, position, Inspect(current, source, visibility)));
            }
            if (debugger.Stopped(stack))
            {
                if (context.Cancellation is not { IsCancellationRequested: true }) return false;
                _left = Failure(WebLingoDiagnosticCodes.Cancelled, "The program was cancelled.", frame.Function, pc) with { Instructions = executed };
                return true;
            }
            // It waits where it stopped, its frames on the VM's stack, until its host resumes it and its debugger lets it go on.
            frame.Pc = pc;
            (_frames, _executed, _depth, _stopDepth) = (frames, executed, depth, depth);
            _left = WebLingoExecutionResult.Waits(-1) with { Instructions = executed };
            return true;
        }

        /// <summary>
        /// Resumed while its debugger stopped it: the depth it goes on at, or -1, with <see cref="_left"/>, where it is
        /// cancelled, or waits still.
        /// </summary>
        private int GoesOn(long executed)
        {
            var frame = _frames[_stopDepth];
            if (context.Cancellation is { IsCancellationRequested: true })
            {
                _stopDepth = -1;
                _left = Failure(WebLingoDiagnosticCodes.Cancelled, "The program was cancelled.", frame.Function, frame.Pc)
                    with { Instructions = executed };
                return -1;
            }
            if (_yielded)
            {
                // A yield, gone on from at once, before the instruction it waited at, which no debugger has seen yet.
                _yielded = false;
                var resumed = _depth = _stopDepth;
                _stopDepth = -1;
                return resumed;
            }
            if (Debugger!.Waits)
            {
                _left = WebLingoExecutionResult.Waits(-1) with { Instructions = executed };
                return -1;
            }
            var depth = _depth = _stopDepth;
            _stopDepth = -1;
            _wentOn = true;
            return depth;
        }

        private static int StatementStart(WebLingoFunctionDebugInfo info, int pc, int offset)
        {
            var best = -1;
            var start = offset;
            foreach (var (instruction, span) in info.Points)
            {
                if (instruction <= pc && instruction > best && span.Start <= offset && offset - span.Start < span.Length)
                    (best, start) = (instruction, span.Start);
            }
            return start;
        }

        private IReadOnlyList<WebLingoDebugVariable> Inspect(Frame frame, WebLingoFunctionDebugInfo info, int offset)
        {
            var variables = new List<WebLingoDebugVariable>();
            foreach (var (name, value) in Visible(frame, info, offset)) variables.Add(DescribeVariable(name, value, expand: true));
            return variables;
        }

        /// <summary>The variables a frame's code sees at an offset, its captures and its locals, a named import as its export.</summary>
        private Dictionary<string, WebLingoValue> Visible(Frame frame, WebLingoFunctionDebugInfo info, int offset)
        {
            var visible = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
            for (var i = 0; i < info.Captures.Count && i < frame.Cells.Length; i++)
            {
                if (info.Captures[i].Length > 0) visible[info.Captures[i]] = frame.Cells[i];
            }
            foreach (var local in info.Locals)
            {
                if (offset >= local.VisibleFrom && offset < local.VisibleUntil && (uint)local.Register < (uint)frame.Function.LocalCount)
                    visible[local.Name] = DebugLocalValue(frame.Registers[local.Register], local.ExportName);
            }
            return visible;
        }

        /// <summary>The frame, and the offset in it, a debugger's <see cref="TryTest"/> reads its variables in, while it decides whether to stop.</summary>
        private Frame? _scopeFrame;
        private WebLingoFunctionDebugInfo? _scopeInfo;
        private int _scopeOffset;

        /// <summary>
        /// A breakpoint's condition, evaluated in the scope of the frame a debugger decides whether to stop in, without running
        /// the program's code (section 5.7, "Debugging WASP pages, parts", part 3): true, with whether it holds, where it gives
        /// a Boolean; false, with why, where it fails, or gives anything else. What it makes is kept from the collector while it
        /// is evaluated.
        /// </summary>
        public bool TryTest(WebLingoDebugExpression condition, out bool holds, out string? why)
        {
            holds = false;
            if (_scopeFrame is not { } frame || _scopeInfo is not { } info)
            {
                why = "There is no frame to evaluate the condition in.";
                return false;
            }
            var kept = new List<WebLingoValue>();
            heap.AddRoots(kept);
            try
            {
                if (!TryEvaluate(condition, Visible(frame, info, _scopeOffset), kept, out var value, out why)) return false;
                value = heap.Held(value);
                if (value.Kind != WebLingoValueKind.Boolean)
                {
                    why = $"The condition gives {WebLingoValueFormatter.Describe(value, heap)}, not a Boolean.";
                    return false;
                }
                holds = value.AsBoolean;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
            {
                why = "The condition cannot be evaluated: " + exception.Message;
                return false;
            }
            finally
            {
                heap.RemoveLastRoots(kept);
            }
        }

        private const string RunsCode = "a condition does not run the program's code";

        /// <summary>One expression of a condition, its value kept in <paramref name="kept"/>; false, with why, where it fails.</summary>
        private bool TryEvaluate(WebLingoDebugExpression expression, Dictionary<string, WebLingoValue> visible, List<WebLingoValue> kept,
            out WebLingoValue value, out string? why)
        {
            value = WebLingoValue.Null;
            why = null;
            WebLingoRuntimeError? error = null;
            switch (expression)
            {
                case WebLingoDebugExpression.Literal literal:
                    switch (literal.Value)
                    {
                        case null: break;
                        case bool flag: value = WebLingoValue.FromBoolean(flag); break;
                        case long integer: value = WebLingoValue.FromInt64(integer); break;
                        case double number: value = WebLingoValue.FromBinary64Bits(BitConverter.DoubleToInt64Bits(number)); break;
                        case decimal exact:
                            if (!heap.TryAllocateDecimal(exact, out value, out error)) return Failed(error, out why);
                            break;
                        case string text:
                            if (!heap.TryAllocateString(text, out value, out error)) return Failed(error, out why);
                            break;
                    }
                    break;
                case WebLingoDebugExpression.Name name:
                    if (!visible.TryGetValue(name.Identifier, out value))
                    {
                        why = $"'{name.Identifier}' is not in scope here.";
                        return false;
                    }
                    if (value.Kind == WebLingoValueKind.Cell && !heap.TryReadCell(value, out value))
                    {
                        why = $"'{name.Identifier}' is read before it is set.";
                        return false;
                    }
                    if (value.Kind == WebLingoValueKind.Missing)
                    {
                        why = $"'{name.Identifier}' has no value here.";
                        return false;
                    }
                    break;
                case WebLingoDebugExpression.Member member:
                {
                    if (!TryEvaluate(member.Target, visible, kept, out var target, out why)) return false;
                    if (WebLingoMembers.TryGet(target, WebLingoMembers.Find(member.Identifier), member.Identifier, heap, member.Optional, out value, out error)) break;
                    if (!ReferenceEquals(error, WebLingoMembers.Getter)) return Failed(error, out why);
                    // A property's getter: a native class's, called, its built-in; one written in WebLingo, refused.
                    if (!TryNative(value, out var getter))
                    {
                        why = $"'{member.Identifier}' of {WebLingoValueFormatter.Describe(target, heap)} is a property written in WebLingo, and {RunsCode}.";
                        return false;
                    }
                    if (!TryInvoke(getter, [target], out value, out why)) return false;
                    break;
                }
                case WebLingoDebugExpression.Index index:
                {
                    if (!TryEvaluate(index.Target, visible, kept, out var target, out why)) return false;
                    if (!TryEvaluate(index.Key, visible, kept, out var key, out why)) return false;
                    if (WebLingoMembers.TryGetIndex(target, key, heap, index.Optional, out value, out error)) break;
                    if (!ReferenceEquals(error, WebLingoMembers.Getter)) return Failed(error, out why);
                    if (!TryNative(value, out var getter))
                    {
                        why = $"The indexer of {WebLingoValueFormatter.Describe(target, heap)} is written in WebLingo, and {RunsCode}.";
                        return false;
                    }
                    if (!TryInvoke(getter, [target, key], out value, out why)) return false;
                    break;
                }
                case WebLingoDebugExpression.Call call:
                {
                    if (!TryEvaluate(call.Target, visible, kept, out var target, out why)) return false;
                    if (call.Optional && target.Kind == WebLingoValueKind.Null) break;
                    var member = WebLingoMembers.Find(call.Method);
                    if (target.HasClass || !WebLingoDebugExpression.ReadsOnly(target.Kind, call.Method)
                        || WebLingoMembers.TryGetValueMethod(target, member, call.Method, heap, out _))
                    {
                        why = $"'{call.Method}' of {WebLingoValueFormatter.Describe(target, heap)} is not a method of the built-in classes that only "
                              + $"reads, a string's, an array's, a map's or a set's, and {RunsCode}.";
                        return false;
                    }
                    var arguments = new WebLingoValue[call.Arguments.Count];
                    for (var i = 0; i < arguments.Length; i++)
                    {
                        if (!TryEvaluate(call.Arguments[i], visible, kept, out arguments[i], out why)) return false;
                        if (arguments[i].Kind is WebLingoValueKind.Function or WebLingoValueKind.Class)
                        {
                            why = $"'{call.Method}' is given {WebLingoValueFormatter.Describe(arguments[i], heap)}, which it would call, and {RunsCode}.";
                            return false;
                        }
                    }
                    if (!WebLingoMembers.TryCall(target, member, call.Method, arguments, 0, arguments.Length, heap, out value, out error)) return Failed(error, out why);
                    break;
                }
                case WebLingoDebugExpression.Unary unary:
                {
                    if (!TryEvaluate(unary.Operand, visible, kept, out var operand, out why)) return false;
                    var done = unary.Operator switch
                    {
                        WebLingoDebugOperator.Not => WebLingoOperators.TryNot(heap.Held(operand), out value, out error),
                        WebLingoDebugOperator.Negate => WebLingoOperators.TryNegate(operand, heap, out value, out error),
                        _ => WebLingoOperators.TryPlus(operand, heap, out value, out error)
                    };
                    if (!done) return Failed(error, out why);
                    break;
                }
                case WebLingoDebugExpression.Binary { Operator: WebLingoDebugOperator.And or WebLingoDebugOperator.Or } logic:
                {
                    if (!TryEvaluate(logic.Left, visible, kept, out var left, out why) || !TryBoolean(left, logic.Operator, out var first, out why)) return false;
                    if (first == (logic.Operator == WebLingoDebugOperator.Or))
                    {
                        value = WebLingoValue.FromBoolean(first);
                        break;
                    }
                    if (!TryEvaluate(logic.Right, visible, kept, out var right, out why) || !TryBoolean(right, logic.Operator, out var second, out why)) return false;
                    value = WebLingoValue.FromBoolean(second);
                    break;
                }
                case WebLingoDebugExpression.Binary { Operator: WebLingoDebugOperator.Coalesce } coalesce:
                    if (!TryEvaluate(coalesce.Left, visible, kept, out value, out why)) return false;
                    if (value.Kind == WebLingoValueKind.Null && !TryEvaluate(coalesce.Right, visible, kept, out value, out why)) return false;
                    break;
                case WebLingoDebugExpression.Binary binary:
                {
                    if (!TryEvaluate(binary.Left, visible, kept, out var left, out why) || !TryEvaluate(binary.Right, visible, kept, out var right, out why)) return false;
                    if (OperatorNameOf(binary.Operator) is { } operatorName && WebLingoMembers.TryFindOperator(operatorName, left, right, heap, out _))
                    {
                        why = $"'{operatorName["operator".Length..]}' on {WebLingoValueFormatter.Describe(left, heap)} and {WebLingoValueFormatter.Describe(right, heap)} "
                              + $"is a class's operator, written in WebLingo, and {RunsCode}.";
                        return false;
                    }
                    var done = binary.Operator switch
                    {
                        WebLingoDebugOperator.Add => WebLingoOperators.TryBinary(WebLingoArithmeticOperator.Add, heap.Held(left), heap.Held(right), heap, out value, out error),
                        WebLingoDebugOperator.Subtract => WebLingoOperators.TryBinary(WebLingoArithmeticOperator.Subtract, heap.Held(left), heap.Held(right), heap, out value, out error),
                        WebLingoDebugOperator.Multiply => WebLingoOperators.TryBinary(WebLingoArithmeticOperator.Multiply, heap.Held(left), heap.Held(right), heap, out value, out error),
                        WebLingoDebugOperator.Divide => WebLingoOperators.TryBinary(WebLingoArithmeticOperator.Divide, heap.Held(left), heap.Held(right), heap, out value, out error),
                        WebLingoDebugOperator.Remainder => WebLingoOperators.TryBinary(WebLingoArithmeticOperator.Remainder, heap.Held(left), heap.Held(right), heap, out value, out error),
                        WebLingoDebugOperator.IntegerDivide => WebLingoOperators.TryBinary(WebLingoArithmeticOperator.IntegerDivide, heap.Held(left), heap.Held(right), heap, out value, out error),
                        WebLingoDebugOperator.Equal => WebLingoOperators.TryCompare(WebLingoComparisonOperator.Equal, left, right, heap, out value, out error),
                        WebLingoDebugOperator.NotEqual => WebLingoOperators.TryCompare(WebLingoComparisonOperator.NotEqual, left, right, heap, out value, out error),
                        WebLingoDebugOperator.Less => WebLingoOperators.TryCompare(WebLingoComparisonOperator.Less, left, right, heap, out value, out error),
                        WebLingoDebugOperator.LessOrEqual => WebLingoOperators.TryCompare(WebLingoComparisonOperator.LessOrEqual, left, right, heap, out value, out error),
                        WebLingoDebugOperator.Greater => WebLingoOperators.TryCompare(WebLingoComparisonOperator.Greater, left, right, heap, out value, out error),
                        _ => WebLingoOperators.TryCompare(WebLingoComparisonOperator.GreaterOrEqual, left, right, heap, out value, out error)
                    };
                    if (!done) return Failed(error, out why);
                    break;
                }
                case WebLingoDebugExpression.Conditional conditional:
                {
                    if (!TryEvaluate(conditional.Test, visible, kept, out var test, out why)) return false;
                    test = heap.Held(test);
                    if (test.Kind != WebLingoValueKind.Boolean)
                    {
                        why = $"'?' chooses by a Boolean, not {WebLingoValueFormatter.Describe(test, heap)}.";
                        return false;
                    }
                    if (!TryEvaluate(test.AsBoolean ? conditional.WhenTrue : conditional.WhenFalse, visible, kept, out value, out why)) return false;
                    break;
                }
                default:
                    why = $"The condition holds what a condition cannot: {RunsCode}.";
                    return false;
            }
            kept.Add(value);
            return true;
        }

        private static bool Failed(WebLingoRuntimeError? error, out string? why)
        {
            why = error is null ? "The condition cannot be evaluated." : $"{error.Message} ({error.Code})";
            return false;
        }

        private bool TryBoolean(WebLingoValue value, WebLingoDebugOperator logic, out bool flag, out string? why)
        {
            value = heap.Held(value);
            (flag, why) = (value.Kind == WebLingoValueKind.Boolean && value.AsBoolean, null);
            if (value.Kind == WebLingoValueKind.Boolean) return true;
            why = $"'{(logic == WebLingoDebugOperator.And ? "&&" : "||")}' takes Booleans, not {WebLingoValueFormatter.Describe(value, heap)}.";
            return false;
        }

        /// <summary>The name a class's operator for an arithmetic operator or an ordering has; null for equality, which has none.</summary>
        private static string? OperatorNameOf(WebLingoDebugOperator op) => op switch
        {
            WebLingoDebugOperator.Add => "operator+",
            WebLingoDebugOperator.Subtract => "operator-",
            WebLingoDebugOperator.Multiply => "operator*",
            WebLingoDebugOperator.Divide => "operator/",
            WebLingoDebugOperator.Remainder => "operator%",
            WebLingoDebugOperator.IntegerDivide => "operator\\",
            WebLingoDebugOperator.Less => "operator<",
            WebLingoDebugOperator.LessOrEqual => "operator<=",
            WebLingoDebugOperator.Greater => "operator>",
            WebLingoDebugOperator.GreaterOrEqual => "operator>=",
            _ => null
        };

        /// <summary>Whether a function is a native member that only calls its built-in, that built-in; false for one written in WebLingo.</summary>
        private bool TryNative(WebLingoValue function, out IWebLingoBuiltinFunction builtin)
        {
            builtin = null!;
            if (function.Kind != WebLingoValueKind.Function) return false;
            var called = module.Functions[heap.GetClosure(function).Function];
            if (called.NativeBuiltin < 0 || called.NativeResult != WebLingoNativeResult.Result) return false;
            builtin = builtins[called.NativeBuiltin];
            return true;
        }

        private bool TryInvoke(IWebLingoBuiltinFunction builtin, WebLingoValue[] arguments, out WebLingoValue value, out string? why)
        {
            why = null;
            try
            {
                if (builtin.TryInvoke(arguments, heap, out value, out var error)) return true;
                return Failed(error, out why);
            }
            catch (Exception exception)
            {
                value = WebLingoValue.Null;
                why = $"'{builtin.Name}' failed: {exception.Message}";
                return false;
            }
        }

        private WebLingoValue DebugLocalValue(WebLingoValue value, string? exportName)
        {
            if (exportName is null) return value;
            // A function using this import captures the module in a cell. Inspect the live export without running script code.
            if (value.Kind == WebLingoValueKind.Cell)
            {
                if (!heap.TryReadCell(value, out var module)) return value;
                value = module;
            }
            return value.Kind == WebLingoValueKind.Module && heap.TryGetExport(value, exportName, out var export)
                ? export : WebLingoValue.Missing;
        }

        private WebLingoDebugVariable DescribeVariable(string name, WebLingoValue value, bool expand)
        {
            if (value.Kind == WebLingoValueKind.Cell && !heap.TryReadCell(value, out value))
                return new(name, "<uninitialized>", "");
            if (value.Kind == WebLingoValueKind.Missing) return new(name, "<missing>", "");

            var held = heap.Held(value);
            var kind = held.Kind;
            var count = kind switch
            {
                WebLingoValueKind.Array => heap.GetArray(held).Count,
                WebLingoValueKind.Map => heap.MapCount(held),
                WebLingoValueKind.Set => heap.SetCount(held),
                WebLingoValueKind.Instance => heap.FieldCount(held),
                WebLingoValueKind.Bytes => heap.GetBytes(held).Length,
                WebLingoValueKind.BytesBuilder => heap.BuilderBytes(held).Length,
                _ => -1
            };
            var type = value.Kind == WebLingoValueKind.Instance ? heap.ClassName(heap.ClassOf(value)) : kind.ToString();
            var preview = count >= 0 ? $"{type} ({count})" : kind switch
            {
                WebLingoValueKind.String => WebLingoValueFormatter.Quote(Abbreviate(heap.GetString(held))),
                WebLingoValueKind.Regex => "/" + Abbreviate(heap.GetRegex(held).Pattern) + "/",
                _ => Abbreviate(WebLingoValueFormatter.Format(held, heap))
            };

            List<WebLingoDebugVariable> children = [];
            if (expand && kind is WebLingoValueKind.Array or WebLingoValueKind.Map or WebLingoValueKind.Instance or WebLingoValueKind.Set)
            {
                for (var i = 0; i < Math.Min(count, 32); i++)
                {
                    var (label, child) = kind switch
                    {
                        WebLingoValueKind.Map => MapItem(held, i),
                        WebLingoValueKind.Instance => heap.FieldAt(held, i),
                        WebLingoValueKind.Set => ($"[{i}]", heap.SetValueAt(held, i)),
                        _ => ($"[{i}]", heap.GetArray(held)[i])
                    };
                    children.Add(DescribeVariable(label, child, expand: false));
                }
                if (count > 32) children.Add(new("…", $"{count - 32} more", ""));
            }
            return new(name, preview, type) { Children = children };
        }

        private (string Name, WebLingoValue Value) MapItem(WebLingoValue map, int index)
        {
            var (key, value) = heap.MapEntryAt(map, index);
            return (DescribeVariable("", key, expand: false).Value, value);
        }

        private static string Abbreviate(string value) =>
            value.Length <= 160 ? value : value[..(char.IsHighSurrogate(value[159]) ? 159 : 160)] + "…";

        /// <summary>A cell's value; WL2004 if it has none yet, which only a hoisted function can meet (section 3.5).</summary>
        private WebLingoRuntimeError? ReadCell(WebLingoValue cell, WebLingoFunction? owner, int register, out WebLingoValue value)
        {
            if (heap.TryReadCell(cell, out value)) return null;
            var name = owner?.LocalNames is { } names && register < names.Count ? $"'{names[register]}'" : "A variable";
            return new WebLingoRuntimeError(WebLingoDiagnosticCodes.ReadBeforeInitialization,
                $"{name} is used by a function called before its declaration has run.");
        }

        /// <summary>
        /// Arithmetic: two int64s, without overflow, or two binary64s, here; anything else, and every error, by
        /// <see cref="WebLingoOperators"/>, which gives the same results.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Arithmetic(WebLingoOpCode opCode, WebLingoValue[] registers, in WebLingoInstruction ins, WebLingoValue right,
            out WebLingoRuntimeError? error)
        {
            var left = registers[ins.B];
            error = null;
            if (left.Kind == WebLingoValueKind.Int64 && right.Kind == WebLingoValueKind.Int64)
            {
                var (a, b) = (left.AsInt64, right.AsInt64);
                switch (opCode)
                {
                    case WebLingoOpCode.Add:
                    {
                        var sum = unchecked(a + b);
                        if (((a ^ sum) & (b ^ sum)) >= 0) return Done(WebLingoValue.FromInt64(sum), registers, ins);
                        break;
                    }
                    case WebLingoOpCode.Subtract:
                    {
                        var difference = unchecked(a - b);
                        if (((a ^ b) & (a ^ difference)) >= 0) return Done(WebLingoValue.FromInt64(difference), registers, ins);
                        break;
                    }
                    case WebLingoOpCode.Multiply:
                    {
                        var high = Math.BigMul(a, b, out var low);
                        if (high == low >> 63) return Done(WebLingoValue.FromInt64(low), registers, ins);
                        break;
                    }
                    case WebLingoOpCode.Remainder:
                        if (b is 0 or -1) break;
                        return Done(WebLingoValue.FromInt64(a % b), registers, ins);
                    case WebLingoOpCode.IntegerDivide:
                        if (b is 0 or -1) break;
                        return Done(WebLingoValue.FromInt64(a / b), registers, ins);
                    default:
                        return Done(WebLingoValue.FromBinary64((double)a / b), registers, ins);
                }
            }
            else if (opCode != WebLingoOpCode.IntegerDivide && left.Kind == WebLingoValueKind.Binary64 && right.Kind == WebLingoValueKind.Binary64)
            {
                var (x, y) = (left.AsBinary64, right.AsBinary64);
                return Done(WebLingoValue.FromBinary64(opCode switch
                {
                    WebLingoOpCode.Add => x + y,
                    WebLingoOpCode.Subtract => x - y,
                    WebLingoOpCode.Multiply => x * y,
                    WebLingoOpCode.Remainder => x % y,
                    _ => x / y
                }), registers, ins);
            }
            return ArithmeticOtherwise(opCode, registers, ins, left, right, out error);
        }

        /// <summary>
        /// Arithmetic that is not two int64s' without overflow, or two binary64s': a class's operator, as a call; anything
        /// else, and every error, by <see cref="WebLingoOperators"/>. A helper that .NET does not inline, so that each of the
        /// dispatch method's operations inlines only <see cref="Arithmetic"/>'s fast paths (specification, section 5.6,
        /// "After the remedies"; <c>Documents/implementation.md</c>, "A VM's Dispatch Method Is Sensitive to Its Locals").
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool ArithmeticOtherwise(WebLingoOpCode opCode, WebLingoValue[] registers, in WebLingoInstruction ins,
            WebLingoValue left, WebLingoValue right, out WebLingoRuntimeError? error)
        {
            error = null;
            if ((left.Kind == WebLingoValueKind.Instance || right.Kind == WebLingoValueKind.Instance || left.HasClass || right.HasClass)
                && TryOperator(opCode, left, right))
            {
                _operatorResult = ins.A;
                return false;
            }
            if (!WebLingoOperators.TryBinary(ArithmeticOf(opCode), left, right, heap, out var value, out error)) return false;
            registers[ins.A] = value;
            return true;
        }

        /// <summary>
        /// Integer division, <c>\</c>, rarer than the other operations, the arithmetic above in a helper of its own, which
        /// .NET does not inline into the dispatch method.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool IntegerDivide(WebLingoValue[] registers, in WebLingoInstruction ins, out WebLingoRuntimeError? error) =>
            Arithmetic(WebLingoOpCode.IntegerDivide, registers, ins, registers[ins.C], out error);

        /// <summary>
        /// A comparison: two int64s, or two binary64s (IEEE 754's, so that NaN is unordered and unequal to itself),
        /// here; anything else by <see cref="WebLingoOperators"/>, which gives the same results.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Compare(WebLingoOpCode opCode, WebLingoValue[] registers, in WebLingoInstruction ins, WebLingoValue right,
            out WebLingoRuntimeError? error)
        {
            var left = registers[ins.B];
            error = null;
            if (left.Kind == WebLingoValueKind.Int64 && right.Kind == WebLingoValueKind.Int64)
            {
                var (a, b) = (left.AsInt64, right.AsInt64);
                return Done(WebLingoValue.FromBoolean(opCode switch
                {
                    WebLingoOpCode.Equal => a == b,
                    WebLingoOpCode.NotEqual => a != b,
                    WebLingoOpCode.Less => a < b,
                    WebLingoOpCode.LessOrEqual => a <= b,
                    WebLingoOpCode.Greater => a > b,
                    _ => a >= b
                }), registers, ins);
            }
            if (left.Kind == WebLingoValueKind.Binary64 && right.Kind == WebLingoValueKind.Binary64)
            {
                var (x, y) = (left.AsBinary64, right.AsBinary64);
                return Done(WebLingoValue.FromBoolean(opCode switch
                {
                    WebLingoOpCode.Equal => x == y,
                    WebLingoOpCode.NotEqual => x != y,
                    WebLingoOpCode.Less => x < y,
                    WebLingoOpCode.LessOrEqual => x <= y,
                    WebLingoOpCode.Greater => x > y,
                    _ => x >= y
                }), registers, ins);
            }
            return CompareOtherwise(opCode, registers, ins, left, right, out error);
        }

        /// <summary>
        /// A comparison that is not two int64s' or two binary64s': a class's operator, as a call; anything else by
        /// <see cref="WebLingoOperators"/>; in a helper .NET does not inline, as <see cref="ArithmeticOtherwise"/> is.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool CompareOtherwise(WebLingoOpCode opCode, WebLingoValue[] registers, in WebLingoInstruction ins,
            WebLingoValue left, WebLingoValue right, out WebLingoRuntimeError? error)
        {
            error = null;
            if ((left.Kind == WebLingoValueKind.Instance || right.Kind == WebLingoValueKind.Instance || left.HasClass || right.HasClass)
                && TryOperator(opCode, left, right))
            {
                _operatorResult = ins.A;
                return false;
            }
            if (!WebLingoOperators.TryCompare(ComparisonOf(opCode), left, right, heap, out var value, out error)) return false;
            registers[ins.A] = value;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Done(WebLingoValue value, WebLingoValue[] registers, in WebLingoInstruction ins)
        {
            registers[ins.A] = value;
            return true;
        }

        /// <summary>
        /// Begins disposing an instance, for <c>x.dispose()</c> or a using's end (section 3.7): its dispose methods, to run
        /// on it in turn, or <c>null</c> if its disposal has begun already. Disposal gives them no arguments, so a call
        /// with some, or a dispose method that takes some, is WL2006.
        /// </summary>
        private WebLingoRuntimeError? BeginDisposal(WebLingoValue instance, int argumentCount, WebLingoFunction? usingIn, out WebLingoValue[]? chain)
        {
            chain = null;
            _asyncDisposal = false;
            if (argumentCount != 0)
            {
                return new WebLingoRuntimeError(WebLingoDiagnosticCodes.WrongArgumentCount, $"'dispose' takes no arguments, not {argumentCount}.");
            }
            if (instance.Kind == WebLingoValueKind.Instance)
            {
                // An async dispose method makes the disposal async, which a using awaits; one in a function that is not
                // async cannot, and is WL3001 before the disposal begins (section 3.8).
                foreach (var method in heap.DisposeChain(heap.ClassOf(instance)))
                {
                    var function = module.Functions[heap.GetClosure(method).Function];
                    if (!function.IsAsync) continue;
                    _asyncDisposal = true;
                    if (usingIn is { IsAsync: false })
                    {
                        return new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                            $"'{function.Name}' is async, and a function that is not async cannot await it at the end of its using.");
                    }
                }
            }
            if (!WebLingoMembers.TryBeginDisposal(instance, heap, out chain, out var error)) return error;
            if (chain is null)
            {
                _asyncDisposal = false;
                return null;
            }
            foreach (var method in chain)
            {
                var (index, _) = heap.GetClosure(method);
                var function = module.Functions[index];
                if (function.ParameterCount != 1) return WrongArgumentCount(function.Name, function.ParameterCount - 1, 0);
            }
            return null;
        }

        /// <summary>
        /// Makes the frame at <paramref name="depth"/> ready to run an instance's dispose methods in turn, the first by the
        /// call about to be made, the rest as each returns (section 3.7).
        /// </summary>
        private Frame Disposing(Frame[] frames, int depth, WebLingoValue[] chain, WebLingoValue instance)
        {
            var frame = frames[depth] ??= new Frame();
            frame.DisposeChain = chain;
            frame.DisposeNext = 1;
            frame.Closure[1] = instance;
            if (_asyncDisposal)
            {
                // An async disposal is an async function's frame, whose task is the disposal's (section 3.8).
                frame.Task = frame.Closure[3] = _newTask;
                frame.FirstRun = true;
                _newTask = WebLingoValue.Null;
            }
            return frame;
        }

        /// <summary>
        /// An async function's call makes its task (section 3.8), but for a disposal's frame, which has the disposal's,
        /// if it is async.
        /// </summary>
        private bool TryMakeTask(Frame[] frames, int depth, out WebLingoRuntimeError? error)
        {
            error = null;
            if (depth + 1 < frames.Length && frames[depth + 1] is { DisposeChain: not null }) return true;
            return heap.TryAllocateTask(out _newTask, out error);
        }

        /// <summary>
        /// An async disposal's last method has returned: its task completes, with <c>null</c>, or faults with the
        /// exception one of its methods threw, and is what the disposal gives (section 3.8).
        /// </summary>
        private void DisposalEnded(Frame frame)
        {
            var task = frame.Task;
            frame.Task = WebLingoValue.Null;
            frame.Closure[3] = WebLingoValue.Null;
            if (frame.Holds)
            {
                TakeHeld(frame);
                _scheduler.Fault(task, _thrown, _thrownError, _origin);
                _thrown = WebLingoValue.Null;
                _thrownError = null;
            }
            else
            {
                _scheduler.Complete(task, WebLingoValue.Null);
            }
            _returned = task;
        }

        // The exception being thrown, while the frames are searched for its handler: a value, or a runtime error not
        // made a value yet, and where it was first thrown (WebLingoExceptions.Origin).
        private WebLingoValue _thrown;
        private WebLingoRuntimeError? _thrownError;
        private long _origin;

        // The calls the last exception was raised or thrown in, each an origin, the innermost first, and that exception's
        // origin, so that one nothing catches is reported with them (section 6.12, "Logging, parts", part 2).
        private long[]? _raisedStack;
        private long _raisedOrigin = WebLingoExceptions.Nowhere;

        /// <summary>The class Error, once the module has made it, which a caught runtime error becomes (section 3.7); a root.</summary>
        private readonly WebLingoValue[] _errorClass = new WebLingoValue[1];

        /// <summary>The class Object, once the module has made it, whose toString() every value has (section 3.7); a root.</summary>
        private readonly WebLingoValue[] _objectClass = new WebLingoValue[1];

        /// <summary>
        /// For each built-in, whether it writes text (<see cref="IWebLingoTextBuiltin"/>), and so is given a value's text with
        /// the toString() its instances override run, once a class has been given one (<c>heap.ToStringGiven</c>), as until
        /// then the formatter alone gives every text (section 3.7).
        /// </summary>
        private bool[] _texts = TextBuiltins(builtins);

        /// <summary>The execution's tasks (section 3.8), made when it starts; the program's own task, if its top level awaits, a root.</summary>
        private WebLingoScheduler _scheduler = null!;
        private readonly WebLingoValue[] _main = new WebLingoValue[1];
        /// <summary>A call's rest parameter, gathered before its frame is made, a root until its function takes it (section 3.5).</summary>
        private readonly WebLingoValue[] _rest = [WebLingoValue.Null];
        /// <summary>
        /// The instance <c>new</c> made last, a root until its constructor's frame holds it, since what the call makes
        /// before that frame, a rest parameter's array or an async constructor's task, may collect; no program's code
        /// runs in between, so that one is enough.
        /// </summary>
        private readonly WebLingoValue[] _made = [WebLingoValue.Null];
        /// <summary>The call a spread instruction is, once its array's elements are in place (section 3.5).</summary>
        private readonly WebLingoInstruction[] _spread = new WebLingoInstruction[1];

        /// <summary>
        /// A class's operator that an arithmetic or a comparison found (section 3.7), with its operands, and, on the
        /// register VM, where its result goes: called as a function, from where the operation's failure leads.
        /// </summary>
        private WebLingoValue _operator;
        private readonly WebLingoValue[] _operands = new WebLingoValue[2];
        private int _operatorResult;
        private int _operatorCount = 2;

        /// <summary>A call that an operation leaves to where its failure leads: a file's function, which an import runs.</summary>
        private void Pend(WebLingoValue callee, int count, int result)
        {
            _operator = callee;
            _operatorCount = count;
            _operatorResult = result;
        }

        /// <summary>What a pending call runs on: a property's target (section 3.7).</summary>
        private WebLingoValue _pendingSelf;

        /// <summary>
        /// A property's getter, or its setter, with <paramref name="value"/>, that a member's read or assignment failed
        /// with (<see cref="WebLingoMembers.Getter"/>, <see cref="WebLingoMembers.Setter"/>): pending, called on the
        /// property's target, its result going to <paramref name="result"/> (section 3.7); nothing for another failure.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void PendAccessor(WebLingoRuntimeError? error, WebLingoValue accessor, WebLingoValue self, WebLingoValue value, int result)
        {
            var setter = ReferenceEquals(error, WebLingoMembers.Setter);
            if (!setter && !ReferenceEquals(error, WebLingoMembers.Getter)) return;
            Pend(accessor, setter ? 1 : 0, result);
            _operands[0] = value;
            _pendingSelf = self;
        }

        /// <summary>
        /// An indexer's getter, with <paramref name="index"/>, or its setter, with the index and <paramref name="value"/>,
        /// that an index's read or assignment failed with (<see cref="WebLingoMembers.Getter"/>,
        /// <see cref="WebLingoMembers.Setter"/>): pending, called on the target, its result going to
        /// <paramref name="result"/> (section 3.9); nothing for another failure.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void PendIndexer(WebLingoRuntimeError? error, WebLingoValue accessor, WebLingoValue self, WebLingoValue index,
            WebLingoValue value, int result)
        {
            var setter = ReferenceEquals(error, WebLingoMembers.Setter);
            if (!setter && !ReferenceEquals(error, WebLingoMembers.Getter)) return;
            Pend(accessor, setter ? 2 : 1, result);
            _operands[0] = index;
            _operands[1] = value;
            _pendingSelf = self;
        }

        /// <summary>The name of the operator a class may declare for an operation, or null for one it may not.</summary>
        private static string? OperatorName(WebLingoOpCode opCode) => opCode switch
        {
            WebLingoOpCode.Add => "operator+",
            WebLingoOpCode.Subtract => "operator-",
            WebLingoOpCode.Multiply => "operator*",
            WebLingoOpCode.Divide => "operator/",
            WebLingoOpCode.Remainder => "operator%",
            WebLingoOpCode.IntegerDivide => "operator\\",
            WebLingoOpCode.Less => "operator<",
            WebLingoOpCode.LessOrEqual => "operator<=",
            WebLingoOpCode.Greater => "operator>",
            WebLingoOpCode.GreaterOrEqual => "operator>=",
            _ => null
        };

        /// <summary>Whether an operation on two values calls a class's operator (section 3.7), which it then keeps.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryOperator(WebLingoOpCode opCode, WebLingoValue left, WebLingoValue right)
        {
            if (OperatorName(opCode) is not { } name || !WebLingoMembers.TryFindOperator(name, left, right, heap, out _operator)) return false;
            _operands[0] = left;
            _operands[1] = right;
            _operatorCount = 2;
            return true;
        }

        /// <summary>
        /// The program's files' instances, once each has begun to run, and the closures of their functions, which run them
        /// (section 3.9); roots.
        /// </summary>
        private WebLingoModuleTable _modules = null!;

        /// <summary>What finds and compiles a module the program imports while it runs (section 3.9).</summary>
        public IWebLingoModuleLoader? Loader { get; init; }

        /// <summary>What finds and compiles a component the program renders (section 13).</summary>
        public IWebLingoModuleLoader? Components { get; init; }

        /// <summary>The values that modules imported while the program runs bring, their constants; a root.</summary>
        private readonly List<WebLingoValue> _linked = [];

        /// <summary>The built-in classes such a module declares again, and the program's of each, which making it gives.</summary>
        private Dictionary<WebLingoClassInfo, WebLingoValue>? _madeAlready;

        /// <summary>
        /// <c>Module.import(path, values)</c> (section 3.9): a task, which completes with the module's instance once it has
        /// run; null when a call is pending that gives one: the module's function, if the program has its file, or its
        /// compiled import's entry, joined to the program, if not. Its path is found from the importing file's folder.
        /// </summary>
        private WebLingoValue ImportedWhileRunning(WebLingoValue path, WebLingoValue values, bool anew, int result,
            WebLingoFunction function, out WebLingoRuntimeError? error)
        {
            error = null;
            if (path.Kind != WebLingoValueKind.String)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"A module's path is a string, not {WebLingoValueFormatter.Describe(path, heap)}.");
                return WebLingoValue.Null;
            }
            var text = heap.GetString(path);
            var importer = function.Source ?? (module.Units.Count > 0 ? module.Units[0].Name : module.Name);
            var loader = anew ? Components : Loader;
            string? why = anew ? "this host renders no components" : "this host imports no modules while a program runs";
            if (loader is null || !loader.TryResolve(importer, text, out var name, out why))
            {
                error = anew
                    ? new WebLingoRuntimeError(WebLingoDiagnosticCodes.ComponentNotFound,
                        $"The component '{text}' cannot be rendered: {why}.")
                    : new WebLingoRuntimeError(WebLingoDiagnosticCodes.ModuleNotFound, $"The module '{text}' cannot be imported: {why}.");
                return WebLingoValue.Null;
            }
            var unit = UnitNamed(name);
            if (unit < 0)
            {
                // A file the program has not: compiled, with the files it imports, and joined to the program, whose entry
                // imports it with the values given; a component's checked first, as a render's are (section 13).
                if (loader.Compile(name, out var failure) is not { } compiled)
                {
                    var notFound = anew ? WebLingoDiagnosticCodes.ComponentNotFound : WebLingoDiagnosticCodes.ModuleNotFound;
                    var what = anew ? "component" : "module";
                    error = new WebLingoRuntimeError(failure?.Code ?? notFound,
                        $"The {what} '{text}' does not compile: {failure?.Message ?? "it cannot be read"}");
                    return WebLingoValue.Null;
                }
                if (!TryLink(compiled, out var entry, out error)) return WebLingoValue.Null;
                Debugger?.ModuleLoaded(compiled);
                if (anew && UnitNamed(name) is var joined and >= 0
                    && !_modules.TryFind(joined, name, module.Units[joined].Parameters, values, out _, out error, anew: true))
                {
                    return WebLingoValue.Null;
                }
                _operands[0] = values;
                Pend(entry, 1, result);
                return WebLingoValue.Null;
            }
            var info = module.Units[unit];
            if (!_modules.TryFind(unit, info.Name, info.Parameters, values, out var instance, out error, anew))
            {
                return WebLingoValue.Null;
            }
            if (anew && _unitClosures[unit].Kind != WebLingoValueKind.Function)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.ComponentNotFound,
                    $"The component '{text}' cannot be rendered: it is the program's own file.");
                return WebLingoValue.Null;
            }
            var ran = instance.Kind == WebLingoValueKind.Module || _unitClosures[unit].Kind != WebLingoValueKind.Function;
            if (!anew && ran)
            {
                // A file that has run, or is running, for these values: a task already complete with its instance.
                if (!heap.TryAllocateTask(out var task, out error)) return WebLingoValue.Null;
                _scheduler.Complete(task, instance);
                return task;
            }
            if (info.Parameters.Count > 0 && values.Kind == WebLingoValueKind.Null && !heap.TryAllocateMap([], 0, 0, out values, out error))
            {
                return WebLingoValue.Null;
            }
            if (anew) _modules.RunAnew();
            _operands[0] = values;
            Pend(_unitClosures[unit], info.Parameters.Count > 0 ? 1 : 0, result);
            return WebLingoValue.Null;
        }

        /// <summary>
        /// The values the host gives the program's parameters, a map of strings in the entry point's first register: each a
        /// parameter's, and every parameter without a default given (WL2016 otherwise, which ends the execution as it starts).
        /// </summary>
        private bool TryGiveParameters(WebLingoValue[] first)
        {
            var entries = new WebLingoValue[2 * context.Parameters.Count];
            heap.AddRoots(entries); // While its strings are made, which may collect.
            try
            {
                return TryGiveParameters(first, entries);
            }
            finally
            {
                heap.RemoveLastRoots(entries);
            }
        }

        private bool TryGiveParameters(WebLingoValue[] first, WebLingoValue[] entries)
        {
            var count = 0;
            WebLingoRuntimeError? error = null;
            foreach (var (name, value) in context.Parameters)
            {
                var declared = false;
                foreach (var parameter in module.Parameters) declared |= parameter.Name == name;
                if (!declared)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.ModuleParameter, $"'{name}' is not a parameter of the program.");
                    break;
                }
                if (!heap.TryAllocateString(name, out entries[2 * count], out error)
                    || !heap.TryAllocateString(value, out entries[2 * count + 1], out error))
                {
                    break;
                }
                count++;
            }
            foreach (var parameter in module.Parameters)
            {
                if (error is not null || parameter.HasDefault || context.Parameters.ContainsKey(parameter.Name)) continue;
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.ModuleParameter, $"The program's parameter '{parameter.Name}' is not given.");
            }
            if (error is null && heap.TryAllocateMap(entries, 0, count, out first[0], out error)) return true;
            _ended = Failure(error!.Code, error.Message, new WebLingoSourceSpan(0, 0));
            return false;
        }

        /// <summary>The program's file of that name, by its index in its table of files, or -1.</summary>
        private int UnitNamed(string name)
        {
            for (var i = 0; i < module.Units.Count; i++)
            {
                if (module.Units[i].Name == name) return i;
            }
            return -1;
        }

        /// <summary>
        /// A module compiled as an import joined to the running program (section 3.9): its functions, constants, built-ins
        /// and classes added to the program's, its instructions' indices moved to where they now are, its files added but for
        /// those the program has, whose own it keeps, and its built-in classes the program's where the program has them.
        /// Gives the closure of its entry, which imports its file with the values it is given.
        /// </summary>
        private bool TryLink(WebLingoCompiledModule import, out WebLingoValue entry, out WebLingoRuntimeError? error)
        {
            entry = WebLingoValue.Null;
            var verification = new WebLingoBytecodeVerifier().Verify(import);
            if (!import.IsImport || !verification.IsValid)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BytecodeVerificationFailure,
                    $"The imported module failed verification: {verification.Error ?? "it is not an import's"}");
                return false;
            }
            foreach (var function in import.Functions)
            {
                if (function.RegisterCount <= context.Resources.MaxRegisters) continue;
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BudgetExceeded,
                    $"'{function.Name}' needs {function.RegisterCount} registers; this execution allows {context.Resources.MaxRegisters}.");
                return false;
            }
            var (functionBase, constantBase, classBase, builtinBase) = (module.Functions.Count, module.Constants.Count, module.Classes.Count, builtins.Length);

            var addedBuiltins = new IWebLingoBuiltinFunction[import.Imports.Count];
            for (var i = 0; i < addedBuiltins.Length; i++)
            {
                var wanted = import.Imports[i];
                if (WebLingoIntrinsicBuiltins.TryResolve(context.Services, wanted.Name, out var builtin) && builtin.Arity == wanted.Arity)
                {
                    addedBuiltins[i] = builtin;
                    continue;
                }
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.UndefinedIdentifier,
                    $"This host has no built-in '{wanted.Name}' taking {wanted.Arity} arguments.");
                return false;
            }
            var addedConstants = new WebLingoValue[import.Constants.Count];
            var addedNames = new WebLingoMemberName[import.Constants.Count];
            for (var i = 0; i < addedConstants.Length; i++)
            {
                var constant = import.Constants[i];
                if (constant.Kind == WebLingoConstantKind.String)
                {
                    addedNames[i] = new WebLingoMemberName(WebLingoMembers.Find(constant.Text!), Text(constant.Text!));
                    if (!heap.TryAllocateString(constant.Text!, out addedConstants[i], out error)) return false;
                    _linked.Add(addedConstants[i]);
                    continue;
                }
                if (constant.Kind == WebLingoConstantKind.Decimal)
                {
                    if (!heap.TryAllocateDecimal(WebLingoValue.ParseDecimal(constant.Text!), out addedConstants[i], out error)) return false;
                    _linked.Add(addedConstants[i]);
                    continue;
                }
                addedConstants[i] = constant.Kind == WebLingoConstantKind.Int64
                    ? WebLingoValue.FromInt64(constant.Bits)
                    : WebLingoValue.FromBinary64Bits(constant.Bits);
            }

            // Its files: the program's where it has them by name, else new ones.
            var units = new List<WebLingoUnitInfo>(module.Units);
            var unitMap = new int[import.Units.Count];
            for (var i = 0; i < unitMap.Length; i++)
            {
                var unit = import.Units[i];
                var existing = i == 0 ? -1 : UnitNamed(unit.Name);
                unitMap[i] = existing >= 0 ? existing : units.Count;
                if (existing < 0) units.Add(unit with { Function = unit.Function >= 0 ? unit.Function + functionBase : -1 });
            }

            WebLingoInstruction[] Relocated(IReadOnlyList<WebLingoInstruction> code)
            {
                var moved = new WebLingoInstruction[code.Count];
                for (var i = 0; i < moved.Length; i++)
                {
                    var ins = code[i];
                    moved[i] = ins.OpCode switch
                    {
                        WebLingoOpCode.LoadConstant or WebLingoOpCode.SetMember or WebLingoOpCode.SetField or WebLingoOpCode.CallMethod
                            => ins with { B = ins.B + constantBase },
                        WebLingoOpCode.GetMember or WebLingoOpCode.GetField or WebLingoOpCode.AddConstant or WebLingoOpCode.SubtractConstant or WebLingoOpCode.MultiplyConstant or WebLingoOpCode.RemainderConstant or WebLingoOpCode.EqualConstant or WebLingoOpCode.NotEqualConstant or WebLingoOpCode.LessConstant or WebLingoOpCode.LessOrEqualConstant or WebLingoOpCode.GreaterConstant or WebLingoOpCode.GreaterOrEqualConstant => ins with { C = ins.C + constantBase },
                        WebLingoOpCode.GetSuper => ins with { D = ins.D + constantBase },
                        WebLingoOpCode.CallBuiltin or WebLingoOpCode.LoadBuiltin => ins with { B = ins.B + builtinBase },
                        WebLingoOpCode.MakeClosure => ins with { B = ins.B + functionBase },
                        WebLingoOpCode.MakeClass or WebLingoOpCode.Extend => ins with { B = ins.B + classBase },
                        WebLingoOpCode.RegisterUnit => ins with { A = unitMap[ins.A] },
                        WebLingoOpCode.MakeModule or WebLingoOpCode.Import => ins with { B = unitMap[ins.B] },
                        _ => ins
                    };
                }
                return moved;
            }

            var functions = new List<WebLingoFunction>(module.Functions);
            var addedCodes = new List<WebLingoInstruction[]>();
            foreach (var function in import.Functions)
            {
                functions.Add(function);
                addedCodes.Add(Relocated(function.Code));
            }
            var entryIndex = functions.Count;
            functions.Add(import.EntryPoint with { Name = "import", Source = import.Units.Count > 1 ? import.Units[1].Name : null });
            addedCodes.Add(Relocated(import.EntryPoint.Code));

            WebLingoClassMethod Moved(WebLingoClassMethod method) => method with { Function = method.Function + functionBase };
            var classes = new List<WebLingoClassInfo>(module.Classes);
            foreach (var info in import.Classes)
            {
                var methods = new List<WebLingoClassMethod>();
                foreach (var method in info.Methods) methods.Add(Moved(method));
                var statics = new List<WebLingoClassMethod>();
                foreach (var method in info.StaticMethods) statics.Add(Moved(method));
                classes.Add(info with { Constructor = info.Constructor >= 0 ? info.Constructor + functionBase : -1, Methods = methods, StaticMethods = statics });
            }

            // Its built-in classes: the program's, where the program has made them, else its own, which become the program's.
            var madeAlready = _madeAlready ?? new Dictionary<WebLingoClassInfo, WebLingoValue>(ReferenceEqualityComparer.Instance);
            int Role(int programs, int imports, WebLingoValue made)
            {
                if (imports < 0) return programs;
                if (programs >= 0 && made.Kind == WebLingoValueKind.Class) madeAlready[classes[imports + classBase]] = made;
                return programs >= 0 ? programs : imports + classBase;
            }
            var valueClasses = new List<int>();
            for (var i = 0; i < Math.Max(module.ValueClasses.Count, import.ValueClasses.Count); i++)
            {
                var programs = i < module.ValueClasses.Count ? module.ValueClasses[i] : -1;
                var imports = i < import.ValueClasses.Count ? import.ValueClasses[i] : -1;
                valueClasses.Add(Role(programs, imports, i < _valueClasses.Length ? _valueClasses[i] : WebLingoValue.Null));
            }
            var objectClass = Role(module.ObjectClass, import.ObjectClass, _objectClass[0]);
            var errorClass = Role(module.ErrorClass, import.ErrorClass, _errorClass[0]);
            var errorClasses = new List<WebLingoErrorClass>(module.ErrorClasses);
            foreach (var kind in import.ErrorClasses)
            {
                var programs = -1;
                for (var i = 0; i < errorClasses.Count && programs < 0; i++)
                {
                    if (errorClasses[i].Code == kind.Code) programs = i;
                }
                if (programs >= 0)
                {
                    Role(errorClasses[programs].Class, kind.Class, _errorClasses[programs]);
                    continue;
                }
                if (errorClasses.Count == _errorClasses.Length)
                {
                    error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BudgetExceeded, "The program has too many kinds of runtime errors' classes.");
                    return false;
                }
                errorClasses.Add(kind with { Class = kind.Class + classBase });
            }

            var constants2 = new WebLingoValue[constants.Length + addedConstants.Length];
            Array.Copy(constants, constants2, constants.Length);
            Array.Copy(addedConstants, 0, constants2, constants.Length, addedConstants.Length);
            var names2 = new WebLingoMemberName[names.Length + addedNames.Length];
            Array.Copy(names, names2, names.Length);
            Array.Copy(addedNames, 0, names2, names.Length, addedNames.Length);
            var builtins2 = new IWebLingoBuiltinFunction[builtins.Length + addedBuiltins.Length];
            Array.Copy(builtins, builtins2, builtins.Length);
            Array.Copy(addedBuiltins, 0, builtins2, builtins.Length, addedBuiltins.Length);
            var codes2 = new WebLingoInstruction[codes.Length + addedCodes.Count][];
            Array.Copy(codes, codes2, codes.Length);
            addedCodes.CopyTo(codes2, codes.Length);
            var constantInfos = new List<WebLingoConstant>(module.Constants);
            constantInfos.AddRange(import.Constants);
            var imports2 = new List<WebLingoImport>(module.Imports);
            imports2.AddRange(import.Imports);

            module = module with
            {
                Constants = constantInfos,
                Imports = imports2,
                Functions = functions,
                Classes = classes,
                Units = units,
                ValueClasses = valueClasses,
                ObjectClass = objectClass,
                ErrorClass = errorClass,
                ErrorClasses = errorClasses
            };
            (constants, names, builtins, codes) = (constants2, names2, builtins2, codes2);
            _callables = Callables(functions, codes, _callables.Length, _callables);
            _texts = TextBuiltins(builtins);
            _builtin = BuiltinClasses(module);
            _madeAlready = madeAlready;
            while (_unitClosures.Count < units.Count) _unitClosures.Add(WebLingoValue.Null);
            _modules.Grow(units.Count);
            return heap.TryAllocateClosure(entryIndex, [], out entry, out error);
        }
        private readonly List<WebLingoValue> _unitClosures = [];

        /// <summary>A file's instance, from then on the one its imports get (section 3.9).</summary>
        private bool TryMakeModule(int unit, WebLingoValue[] values, int first, int count, out WebLingoValue made,
            out WebLingoRuntimeError? error)
        {
            var info = module.Units[unit];
            if (!heap.TryAllocateModule(info.Name, info.Exports, values, first, count, out made, out error)) return false;
            _modules.Made(unit, made);
            return true;
        }

        /// <summary>
        /// What <c>Import</c> gives at once: a file's instance, if it has begun to run for those values; else null, its
        /// function's call pending, given the values if it has parameters, its result to go to <paramref name="result"/>.
        /// </summary>
        private WebLingoValue Imported(int unit, WebLingoValue values, int result, out WebLingoRuntimeError? error)
        {
            var info = module.Units[unit];
            if (!_modules.TryFind(unit, info.Name, info.Parameters, values, out var instance, out error)) return WebLingoValue.Null;
            if (instance.Kind == WebLingoValueKind.Module) return instance;
            // A file with parameters given no values is given an empty map, which its function reads them from.
            if (info.Parameters.Count > 0 && values.Kind == WebLingoValueKind.Null && !heap.TryAllocateMap([], 0, 0, out values, out error))
            {
                return WebLingoValue.Null;
            }
            // A file with neither its instance nor a function that makes it, which only damaged bytecode imports.
            if (_unitClosures[unit].Kind != WebLingoValueKind.Function)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BytecodeVerificationFailure,
                    $"The bytecode is invalid: file {unit} ('{info.Name}') is imported, but nothing makes its instance.");
                return WebLingoValue.Null;
            }
            _operands[0] = values;
            Pend(_unitClosures[unit], info.Parameters.Count > 0 ? 1 : 0, result);
            return WebLingoValue.Null;
        }

        /// <summary>What an <c>await</c> waits for, on its way to the code that suspends the frame.</summary>
        private WebLingoValue _awaited;

        /// <summary>What a failure to start, or the scheduler, ends the execution with (section 3.8).</summary>
        private WebLingoExecutionResult? _ended;

        /// <summary>A task made for an async function's call, on its way to the function's frame.</summary>
        private WebLingoValue _newTask;

        /// <summary>Whether the frame the scheduler resumed throws, at its await, rather than being given a value.</summary>
        private bool _resumedThrowing;

        /// <summary>Whether the disposal begun last has an async method (section 3.8).</summary>
        private bool _asyncDisposal;

        /// <summary>Whether a using's caller is to await its async disposal's task (<see cref="_awaited"/>).</summary>
        private bool _usingAwaits;

        /// <summary>A top level that awaits: an async function itself, whose task is the program's (section 3.8).</summary>
        private bool TryStartMain(Frame frame)
        {
            if (!heap.TryAllocateTask(out var task, out var error))
            {
                _ended = Failure(error!.Code, error.Message, new WebLingoSourceSpan(0, 0));
                return false;
            }
            _main[0] = frame.Task = frame.Closure[3] = task;
            return true;
        }

        /// <summary>An async function's frame, about to run: its task, made for its call, which the call gives (section 3.8).</summary>
        private void Started(Frame called)
        {
            if (called.DisposeChain is not null) return;
            called.Task = called.Closure[3] = _newTask;
            called.FirstRun = true;
            _newTask = WebLingoValue.Null;
        }

        /// <summary>An async function has returned (<see cref="_returned"/>): its task is done, and is what its call gives.</summary>
        private void Completed(Frame frame)
        {
            var task = frame.Task;
            frame.Task = WebLingoValue.Null;
            frame.Closure[0] = WebLingoValue.Null;
            frame.Closure[3] = WebLingoValue.Null;
            _scheduler.Complete(task, _returned);
            _returned = task;
        }

        /// <summary>An exception leaves an async function: its frame let go of, its task faults, and is what its call gives.</summary>
        private void Faulted(Frame frame)
        {
            Array.Clear(frame.Registers, 0, frame.Function.RegisterCount);
            var task = frame.Task;
            frame.Task = WebLingoValue.Null;
            frame.Closure[0] = WebLingoValue.Null;
            frame.Closure[1] = WebLingoValue.Null;
            frame.Closure[2] = WebLingoValue.Null;
            frame.Closure[3] = WebLingoValue.Null;
            frame.DisposeChain = null;
            frame.Holds = false;
            frame.HeldError = null;
            _scheduler.Fault(task, _thrown, _thrownError, _origin);
            _thrown = WebLingoValue.Null;
            _thrownError = null;
            _returned = task;
        }

        /// <summary>
        /// await: the frame's roots let go of, the frame kept by its task, with the values it holds, until what it awaits
        /// (<see cref="_awaited"/>) completes; the task is what its call gives, if it is still on its caller's stack.
        /// </summary>
        private void Suspend(Frame frame)
        {
            var waiting = heap.GetTask(frame.Task);
            waiting.Frame = frame;
            var roots = frame.WaitingRoots;
            if (roots is null || roots[0] != frame.Registers) frame.WaitingRoots = roots = [frame.Registers, frame.Closure];
            waiting.FrameRoots = roots;
            _scheduler.Await(frame.Task, _awaited);
            _awaited = WebLingoValue.Null;
            _returned = frame.Task;
        }

        /// <summary>
        /// An async function's first run has ended: its call gives its task (<see cref="_returned"/>) to its caller, or,
        /// for a toString() a text waits for, to that text, which this puts in <see cref="_text"/>, or, for a call a
        /// built-in's work waits for, to that work, in <see cref="_calls"/>.
        /// </summary>
        private void Gave(Frame frame, Frame[] frames, int depth)
        {
            var caller = frames[depth - 1];
            if (frame.Function.IsNative) frame.CalledFrom = RaisedAt(frames, depth - 1, caller.Pc - 1);
            frame.FirstRun = false;
            var waits = frame.Waiter;
            frame.Waiter = null;
            if (waits is CallsWaiting calls)
            {
                _calls = calls;
                return;
            }
            _text = (TextCall?)waits;
            if (waits is null)
            {
                if (frame.ResultRegister >= 0)
                {
                    caller.Registers[frame.ResultRegister] = _returned;
                }
                else
                {
                    // A using's disposal: the using awaits it.
                    _awaited = _returned;
                    _usingAwaits = true;
                }
                _returned = WebLingoValue.Null;
            }
        }

        /// <summary>
        /// Nothing runs on the VM's stack (section 3.8): the next ready task's frame resumes, at depth 0, its await given
        /// a value, or about to throw (<see cref="_resumedThrowing"/>), while timers complete, in order, when nothing is
        /// ready. False, with the end in <see cref="_ended"/>, once the top level is done, cancelled, or waits for what
        /// nothing will complete.
        /// </summary>
        /// <summary>While it waits: its frames, and how many instructions it has run, from which it goes on (section 6.6).</summary>
        private Frame[] _frames = [];
        private long _executed;

        /// <summary>Whether <see cref="TryResume"/>'s false is a wait, not the execution's end.</summary>
        private bool _waits;

        /// <summary>
        /// Whether a worker's execution, its top level ended, goes on running its tasks (<see cref="WebLingoExecutionContext.IsWorker"/>),
        /// which <see cref="TryResume"/> then ends once none has work.
        /// </summary>
        private bool _drains;

        /// <summary>
        /// Whether the execution, a worker's, which has just ended with its top level, goes on, its tasks having work: it then
        /// resumes them, from the instructions it had run, ending once none has.
        /// </summary>
        public bool GoesOn(WebLingoExecutionResult ended)
        {
            if (!context.IsWorker || !_scheduler.HasWork) return false;
            _drains = true;
            _executed = ended.Instructions;
            return true;
        }

        private bool TryResume(Frame[] frames, WebLingoCancellationSignal? cancellation, long executed)
        {
            while (true)
            {
                if (!_drains && (_main[0].Kind != WebLingoValueKind.Task || heap.GetTask(_main[0]).State != WebLingoTaskState.Pending))
                {
                    _ended = Finish(executed);
                    return false;
                }
                if (cancellation is not null && cancellation.IsCancellationRequested)
                {
                    _ended = Failure(WebLingoDiagnosticCodes.Cancelled, "The program was cancelled.", new WebLingoSourceSpan(0, 0))
                        with { Instructions = executed };
                    return false;
                }
                if (_scheduler.TryTakeReady(out var resumption))
                {
                    var resumed = heap.GetTask(resumption.Task);
                    var frame = (Frame)resumed.Frame!;
                    resumed.Frame = null;
                    resumed.FrameRoots = null;
                    frames[0] = frame;
                    _resumedThrowing = resumption.Throws;
                    if (resumption.Throws) (_thrown, _thrownError, _origin) = (resumption.Value, resumption.Error, resumption.Origin);
                    // A timeout's error, which no instruction raised, is thrown from the await it reaches (section 3.8).
                    if (resumption.Throws && _origin == WebLingoExceptions.Nowhere)
                    {
                        _origin = frame.Function.IsNative && frame.CalledFrom != WebLingoExceptions.Nowhere ? frame.CalledFrom
                            : OriginOf(frame.Function, frame.Pc - 1);
                    }
                    else if (frame.AwaitDestination >= 0) frame.Registers[frame.AwaitDestination] = resumption.Value;
                    return true;
                }
                if (!_scheduler.TryAdvanceTime())
                {
                    if (_scheduler.Waiting)
                    {
                        _waits = true;
                        return false;
                    }
                    if (_drains)
                    {
                        // A worker's tasks, its top level ended, have nothing more to run (section 9, "M0.26 parts", part 2).
                        _ended = Finish(executed);
                        return false;
                    }
                    if (TryRaiseStuck()) continue;
                    _ended = Failure(WebLingoDiagnosticCodes.Deadlock, "The program waits for a task that nothing will complete.",
                        new WebLingoSourceSpan(0, 0)) with { Instructions = executed };
                    return false;
                }
            }
        }

        /// <summary>
        /// The execution's end: the top level's exception, if it faulted; else the first fault nothing awaited; else success
        /// (section 3.8).
        /// </summary>
        private WebLingoExecutionResult Finish(long executed)
        {
            if (_main[0].Kind == WebLingoValueKind.Task && heap.GetTask(_main[0]) is { State: WebLingoTaskState.Faulted } main)
            {
                (_thrown, _thrownError, _origin) = (main.Exception, main.Error, main.Origin);
                return Uncaught(executed);
            }
            if (_scheduler.TryFindUnobserved(out var faulted))
            {
                (_thrown, _thrownError, _origin) = (faulted.Exception, faulted.Error, faulted.Origin);
                return Uncaught(executed);
            }
            return WebLingoExecutionResult.Success with { Instructions = executed };
        }

        /// <summary>A text to build, on its way to the code that builds it; and what a toString() a text waits for gave.</summary>
        private TextCall? _text;
        private WebLingoValue _returned;

        /// <summary>
        /// A built-in's work, on its way to the code that runs it (<see cref="GoOn"/>), and, from it, to the call it waits
        /// for; null otherwise. <see cref="CallsBegun"/> is what <see cref="CallBuiltin(IWebLingoBuiltinFunction,
        /// WebLingoValue[], in WebLingoInstruction)"/> gives once it has begun one, for <c>Run</c> to run it.
        /// </summary>
        private CallsWaiting? _calls;

        private static readonly WebLingoRuntimeError CallsBegun =
            new(WebLingoDiagnosticCodes.InvalidOperation, "A built-in's work has begun.");

        /// <summary>A built-in's work begun, by the instruction that calls it, its values made roots.</summary>
        private WebLingoRuntimeError? Begin(IWebLingoCallingBuiltin builtin, WebLingoValue[] registers, in WebLingoInstruction instruction)
        {
            var work = builtin.Begin(new ReadOnlySpan<WebLingoValue>(registers, instruction.C, instruction.D), heap, out var error);
            if (work is null) return error ?? new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed.");
            heap.AddRoots(work.Values);
            _calls = new CallsWaiting(work, instruction.A);
            return CallsBegun;
        }

        /// <summary>
        /// The work in <see cref="_calls"/> goes on: true, <see cref="_calls"/> null, when it is done, its result in
        /// <paramref name="registers"/>, its roots let go of; true, <see cref="_calls"/> kept, when it waits for a call of
        /// a function, which <c>Run</c> makes; false, its roots let go of, when it fails, a built-in it calls among them.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool GoOn(WebLingoValue[] registers, out WebLingoRuntimeError? error)
        {
            var waiting = _calls!;
            var work = waiting.Work;
            while (true)
            {
                if (work.TryGoOn(heap, out var calls, out var result, out error))
                {
                    heap.RemoveLastRoots(work.Values);
                    registers[waiting.Destination] = result;
                    _calls = null;
                    return true;
                }
                if (calls)
                {
                    if (!IsBuiltinCallee(work.Callee, out var builtin)) return true;
                    // A built-in, which Run's call would call without a frame: called here, at once.
                    var given = WebLingoValue.Null;
                    error = builtin.Arity != work.ArgumentCount
                        ? WrongArgumentCount(builtin.Name, builtin.Arity, work.ArgumentCount)
                        : CallBuiltin(builtin, work.Values, work.FirstArgument, work.ArgumentCount, out given);
                    if (error is null)
                    {
                        work.Supply(given);
                        continue;
                    }
                }
                heap.RemoveLastRoots(work.Values);
                _calls = null;
                error ??= new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, "A built-in's work failed.");
                return false;
            }
        }

        /// <summary>Whether a call of the value calls a built-in, through a bound method or an instance holding one.</summary>
        private bool IsBuiltinCallee(WebLingoValue callee, [NotNullWhen(true)] out IWebLingoBuiltinFunction? builtin)
        {
            builtin = null;
            while (true)
            {
                if (callee.Kind == WebLingoValueKind.BoundMethod && !heap.IsDisposal(callee)) callee = heap.GetBoundMethod(callee).Closure;
                else if (callee.Kind == WebLingoValueKind.Instance && heap.IsCallable(callee)) callee = heap.Held(callee);
                else break;
            }
            if (callee.Kind != WebLingoValueKind.Builtin) return false;
            builtin = builtins[callee.Handle];
            return true;
        }

        private static bool[] TextBuiltins(IWebLingoBuiltinFunction[] builtins)
        {
            var texts = new bool[builtins.Length];
            for (var i = 0; i < builtins.Length; i++) texts[i] = builtins[i] is IWebLingoTextBuiltin;
            return texts;
        }

        /// <summary>Whether a value's text may hold an instance's, which its class's toString() may give.</summary>
        private static bool NeedsText(WebLingoValue value) =>
            value.Kind is WebLingoValueKind.Instance or WebLingoValueKind.Array or WebLingoValueKind.Map or WebLingoValueKind.Set;

        /// <summary>The kinds of values' classes, by <c>WebLingoValueClass</c>, once the module has made them (section 3.7); a root.</summary>
        private readonly WebLingoValue[] _valueClasses = new WebLingoValue[(int)WebLingoValueClass.Html + 1];

        /// <summary>The kinds of runtime errors' classes, as <c>module.ErrorClasses</c> lists them, once made; a root.</summary>
        private readonly WebLingoValue[] _errorClasses = new WebLingoValue[Math.Max(module.ErrorClasses.Count, MaxErrorClasses)];

        /// <summary>The most kinds of runtime errors' classes a program, and the modules it imports while it runs, may have.</summary>
        private const int MaxErrorClasses = 32;

        /// <summary>For each class of the module, whether it is a built-in one that the VM keeps when it makes it.</summary>
        private bool[] _builtin = BuiltinClasses(module);

        private static bool[] BuiltinClasses(WebLingoCompiledModule module)
        {
            var builtin = new bool[module.Classes.Count];
            if (module.ErrorClass >= 0) builtin[module.ErrorClass] = true;
            if (module.ObjectClass >= 0) builtin[module.ObjectClass] = true;
            foreach (var index in module.ValueClasses)
            {
                if (index >= 0) builtin[index] = true;
            }
            foreach (var (_, index) in module.ErrorClasses) builtin[index] = true;
            return builtin;
        }

        /// <summary>A built-in class the module has made: <c>Object</c>, <c>Error</c>, a kind of value's, or of runtime error's.</summary>
        private void Remember(int index, WebLingoValue made)
        {
            if (index == module.ErrorClass) _errorClass[0] = heap.ErrorClass = made;
            if (index == module.ObjectClass)
            {
                _objectClass[0] = made;
                heap.ObjectClass = made;
                heap.TryGetMethod(made, "toString", out var defaultToString);
                heap.DefaultToString = defaultToString;
            }
            for (var i = 0; i < module.ValueClasses.Count; i++)
            {
                if (module.ValueClasses[i] != index) continue;
                _valueClasses[i] = made;
                // Array, Map and Set make collections, and classes extend them; Boolean, Integer, Number, String and the
                // other kinds of values make plain values, and classes extending them hold one (section 3.7).
                if (i == (int)WebLingoValueClass.Array) heap.MarkCollectionClass(made, WebLingoValueKind.Array);
                else if (i == (int)WebLingoValueClass.Map) heap.MarkCollectionClass(made, WebLingoValueKind.Map);
                else if (i == (int)WebLingoValueClass.Set) heap.MarkCollectionClass(made, WebLingoValueKind.Set);
                else if (i == (int)WebLingoValueClass.Boolean) heap.MarkValueClass(made, WebLingoValueKind.Boolean);
                else if (i == (int)WebLingoValueClass.Integer) heap.MarkValueClass(made, WebLingoValueKind.Int64);
                else if (i == (int)WebLingoValueClass.Number) heap.MarkValueClass(made, WebLingoValueKind.Binary64);
                else if (i == (int)WebLingoValueClass.Decimal) heap.MarkValueClass(made, WebLingoValueKind.Decimal);
                else if (i == (int)WebLingoValueClass.Regex) heap.MarkValueClass(made, WebLingoValueKind.Regex);
                else if (i == (int)WebLingoValueClass.String) heap.MarkValueClass(made, WebLingoValueKind.String);
                else if (i == (int)WebLingoValueClass.Function) heap.MarkValueClass(made, WebLingoValueKind.Function);
                else if (i == (int)WebLingoValueClass.Class) heap.MarkClassClass(made);
                else if (i == (int)WebLingoValueClass.Iterator) heap.IteratorClass = made; // A plain class (section 3.6).
                // Bytes, a bytes builder and a date: a kind of value's, new making a value, and a class extending one
                // holding one (section 3.7, "No sealed classes").
                else if (i == (int)WebLingoValueClass.Bytes) heap.MarkValueClass(made, WebLingoValueKind.Bytes);
                else if (i == (int)WebLingoValueClass.BytesBuilder) heap.MarkValueClass(made, WebLingoValueKind.BytesBuilder);
                else if (i == (int)WebLingoValueClass.DateTime) heap.MarkValueClass(made, WebLingoValueKind.DateTime);
                // A module's: new making a module, a class extending it modules of its own (section 3.7, "No sealed classes").
                else if (i == (int)WebLingoValueClass.Module) heap.MarkModuleClass(made);
                // Task's: new making a task completed by hand (section 3.8), and a class extending it tasks of its own
                // (section 3.7, "No sealed classes"). Json's and Html's are plain classes, with static members.
                else if (i == (int)WebLingoValueClass.Task) heap.MarkTaskClass(made);
            }
            for (var i = 0; i < module.ErrorClasses.Count; i++)
            {
                if (module.ErrorClasses[i].Class == index) _errorClasses[i] = made;
            }
        }

        /// <summary>
        /// The <c>equal()</c> that <c>value is type</c> calls when the type is not a class: the type's own, an instance's or
        /// a classed collection's class's, or <c>Object</c>'s (section 3.7); false before the program has <c>Object</c>.
        /// </summary>
        private bool TryGetEqual(WebLingoValue type, out WebLingoValue method)
        {
            var classValue = heap.ClassOfValue(type);
            if (classValue.Kind == WebLingoValueKind.Class && heap.TryGetMethod(classValue, "equal", out method)
                && method.Kind == WebLingoValueKind.Function)
            {
                return true;
            }
            return WebLingoMembers.TryGetValueMethod(type, WebLingoMember.Equal, "equal", heap, out method);
        }

        /// <summary>The class a runtime error of this code becomes: its kind's, or Error.</summary>
        private WebLingoValue ErrorClassFor(string code)
        {
            for (var i = 0; i < module.ErrorClasses.Count; i++)
            {
                if (module.ErrorClasses[i].Code == code && _errorClasses[i].Kind == WebLingoValueKind.Class) return _errorClasses[i];
            }
            return _errorClass[0];
        }

        private long OriginOf(WebLingoFunction function, int pc)
        {
            for (var i = 0; i < module.Functions.Count; i++)
            {
                if (ReferenceEquals(module.Functions[i], function)) return WebLingoExceptions.Origin(i + 1, pc);
            }
            return WebLingoExceptions.Origin(0, pc);
        }

        /// <summary>
        /// Nothing able to run, but a task <c>new Task()</c> made, which something awaits: a <c>DeadlockError</c> thrown at
        /// that <c>await</c>, naming where the task was made and where it is awaited, as lines where the host knows them
        /// (section 3.8, "Tasks completed by hand"); false where no such task is awaited.
        /// </summary>
        private bool TryRaiseStuck()
        {
            if (!heap.TryFindStuckTask(out var task, out var waiter)) return false;
            var message = "The program waits for a task nothing can complete";
            if (heap.GetTask(waiter).Frame is Frame frame && context.Lines is { } lines)
            {
                var made = heap.GetTask(task).MadeAt;
                var awaited = frame.Function.IsNative && frame.CalledFrom != WebLingoExceptions.Nowhere ? frame.CalledFrom
                    : OriginOf(frame.Function, frame.Pc - 1);
                var awaitedIn = OwnerOfOrigin(awaited)?.Source;
                var at = $"awaited at line {lines(awaitedIn, SpanOfOrigin(awaited).Start)}";
                if (made != WebLingoExceptions.Nowhere)
                {
                    var madeIn = OwnerOfOrigin(made)?.Source;
                    var file = madeIn == awaitedIn ? "" : $" of {madeIn ?? "the program"}";
                    at = $"made at line {lines(madeIn, SpanOfOrigin(made).Start)}{file}, " + at;
                }
                message += ": " + at;
            }
            _scheduler.Raise(task, waiter, new WebLingoRuntimeError(WebLingoDiagnosticCodes.WaitsForever, message + "."));
            return true;
        }

        /// <summary>
        /// <c>new</c> on a kind of value's class, <c>new Integer(42)</c>, its value written where it goes; a task made by
        /// hand told where it was made, for a program stuck on it (section 3.8). Kept out of <c>Run</c>, whose locals it would
        /// otherwise change.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryMakeValue(WebLingoValue classValue, WebLingoValue[] values, int first, int count, int destination, WebLingoFunction function,
            int pc, out WebLingoRuntimeError? error)
        {
            if (!heap.TryMakeValue(classValue, values, first, count, out values[destination], out error)) return false;
            if (values[destination].Kind == WebLingoValueKind.Task) heap.GetTask(values[destination]).MadeAt = OriginOf(function, pc);
            return true;
        }

        /// <summary>Where an origin is in the source; nowhere if it names no function of the module.</summary>
        private WebLingoSourceSpan SpanOfOrigin(long origin)
        {
            var (function, pc) = WebLingoExceptions.FromOrigin(origin);
            var owner = function == 0 ? module.EntryPoint
                : function > 0 && function <= module.Functions.Count ? module.Functions[function - 1] : null;
            return owner is null || pc < 0 ? new WebLingoSourceSpan(0, 0) : SpanOf(owner, pc);
        }

        /// <summary>The function an origin is in, or null.</summary>
        private WebLingoFunction? OwnerOfOrigin(long origin)
        {
            var (function, _) = WebLingoExceptions.FromOrigin(origin);
            return function == 0 ? module.EntryPoint
                : function > 0 && function <= module.Functions.Count ? module.Functions[function - 1] : null;
        }

        /// <summary>The exception being thrown as a value, a runtime error made an Error; false if it cannot be one.</summary>
        private bool TryTakeException(out WebLingoValue exception)
        {
            if (_thrownError is null)
            {
                exception = _thrown;
                return true;
            }
            var error = _thrownError;
            if (error.Code == WebLingoDiagnosticCodes.TestAssertionFailed)
            {
                var source = OwnerOfOrigin(_origin)?.Source;
                var offset = SpanOfOrigin(_origin).Start;
                error = WebLingoExceptions.AtAssertion(error, source ?? module.Name, offset, context.Lines?.Invoke(source, offset));
            }
            if (!WebLingoExceptions.TryMakeError(heap, ErrorClassFor(error.Code), error, out exception)) return false;
            _thrown = exception;
            _thrownError = null;
            return true;
        }

        /// <summary>
        /// Where a runtime error raised at <paramref name="pc"/> in the frame at <paramref name="depth"/> is reported: there,
        /// or, in a native class's member, which only calls its built-in, at that member's call (section 3.9). One that
        /// cannot be caught is reported where it was raised, so that this failure path stays as it was: a change to it
        /// slowed the stack VM's calls (Documents/implementation.md, "A VM's Dispatch Method Is Sensitive to Its Locals").
        /// </summary>
        private long RaisedAt(Frame[] frames, int depth, int pc)
        {
            while (depth > 0 && frames[depth].Function.IsNative) pc = frames[--depth].Pc - 1;
            // A native function the scheduler resumed has no caller below it: where it was called.
            if (depth == 0 && frames[0].Function.IsNative && frames[0].CalledFrom != WebLingoExceptions.Nowhere) return frames[0].CalledFrom;
            return OriginOf(frames[depth].Function, pc);
        }

        /// <summary>
        /// A runtime error raised at <paramref name="pc"/> in the frame at <paramref name="depth"/>: where it is reported
        /// (<see cref="RaisedAt"/>), the calls it was raised in kept with it. Kept out of <c>Run</c>, as its callers are.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private long Raised(Frame[] frames, int depth, int pc)
        {
            var origin = RaisedAt(frames, depth, pc);
            KeepStack(frames, depth, pc, origin);
            return origin;
        }

        /// <summary>A value thrown at <paramref name="pc"/> in the frame at <paramref name="depth"/>: where, the calls it was thrown in kept with it.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private long Thrown(Frame[] frames, int depth, int pc)
        {
            var origin = OriginOf(frames[depth].Function, pc);
            KeepStack(frames, depth, pc, origin);
            return origin;
        }

        /// <summary>
        /// The calls an exception raised at <paramref name="origin"/> is raised in, each where its frame is, its callers' at
        /// their calls, the innermost first; a native class's member, which only calls its built-in, left out, its caller
        /// standing for it, as <see cref="RaisedAt"/> reports one, and one the scheduler resumed, which has no caller below
        /// it, at where it was called. An asynchronous function resumed has no caller below it either: its stack begins
        /// with its resumption.
        /// </summary>
        private void KeepStack(Frame[] frames, int depth, int pc, long origin)
        {
            var stack = new List<long>(depth + 1);
            for (var at = depth; at >= 0; at--)
            {
                var frame = frames[at];
                if (frame.Function.IsNative)
                {
                    if (at == 0 && frame.CalledFrom != WebLingoExceptions.Nowhere) stack.Add(frame.CalledFrom);
                    continue;
                }
                stack.Add(OriginOf(frame.Function, at == depth ? pc : frame.Pc - 1));
            }
            (_raisedStack, _raisedOrigin) = (stack.ToArray(), origin);
        }

        /// <summary>The calls the exception at <see cref="_origin"/> was raised in, where they were kept for it; null otherwise.</summary>
        private List<WebLingoStackFrame>? StackOfOrigin()
        {
            if (_raisedStack is not { } kept || _raisedOrigin != _origin) return null;
            var stack = new List<WebLingoStackFrame>(kept.Length);
            foreach (var origin in kept)
            {
                if (OwnerOfOrigin(origin) is not { } owner) continue;
                var (function, _) = WebLingoExceptions.FromOrigin(origin);
                var body = function == 0; // The program's top level, or a file's it imports.
                foreach (var unit in module.Units) body |= unit.Function == function - 1;
                stack.Add(new WebLingoStackFrame(body ? null : owner.Name, owner.Source, SpanOfOrigin(origin)));
            }
            return stack;
        }

        /// <summary>An exception that nothing caught: the program stops, reporting it where it was first thrown, with the calls it was thrown in.</summary>
        private WebLingoExecutionResult Uncaught(long executed)
        {
            string? exceptionClass = null;
            var (code, message) = _thrownError is { } error
                ? (error.Code, error.Message)
                : WebLingoExceptions.Uncaught(_thrown, _errorClass[0], heap, out exceptionClass);
            var failure = Failure(code, message, SpanOfOrigin(_origin)) with { Instructions = executed };
            return failure with
            {
                Error = failure.Error! with { Source = OwnerOfOrigin(_origin)?.Source, ExceptionClass = exceptionClass, Stack = StackOfOrigin() },
                Thrown = context.IsWorker ? CopyThrown() : null
            };
        }

        /// <summary>
        /// A worker's uncaught exception, copied for its parent (section 9, "M0.26 parts", part 2): the value thrown, or a
        /// runtime error as an instance of its class; null where a message cannot hold it.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private WebLingoMessage? CopyThrown()
        {
            var thrown = _thrown;
            if (_thrownError is { } error && !WebLingoExceptions.TryMakeError(heap, ErrorClassFor(error.Code), error, out thrown)) return null;
            return WebLingoMessage.TryCopy(heap, thrown, _errorClass[0], null, out var message, out _) ? message : null;
        }

        /// <summary>The first handler whose region holds the instruction, or -1.</summary>
        private static int FindHandler(IReadOnlyList<WebLingoHandler> handlers, int pc)
        {
            for (var i = 0; i < handlers.Count; i++)
            {
                if (pc >= handlers[i].Start && pc < handlers[i].End) return i;
            }
            return -1;
        }

        /// <summary>A disposal frame keeps the exception being thrown, to throw it again after its last method (section 3.7).</summary>
        private void Hold(Frame frame)
        {
            frame.Holds = true;
            frame.HeldError = _thrownError;
            frame.HeldOrigin = _origin;
            frame.Closure[2] = _thrown;
        }

        /// <summary>The exception a disposal frame kept becomes the one being thrown.</summary>
        private void TakeHeld(Frame frame)
        {
            _thrown = frame.Closure[2];
            _thrownError = frame.HeldError;
            _origin = frame.HeldOrigin;
            frame.Closure[2] = WebLingoValue.Null;
            frame.Holds = false;
            frame.HeldError = null;
            frame.Waiter = null;
        }

        /// <summary>A frame an exception leaves keeps nothing: its closure, its instance, a disposal, a kept exception.</summary>
        private static void Release(Frame frame)
        {
            frame.DebugActivation = 0;
            frame.Closure[0] = WebLingoValue.Null;
            frame.Closure[1] = WebLingoValue.Null;
            frame.Closure[2] = WebLingoValue.Null;
            frame.Closure[3] = WebLingoValue.Null;
            frame.Task = WebLingoValue.Null;
            frame.FirstRun = false;
            frame.Constructed = WebLingoValue.Null;
            frame.DisposeChain = null;
            frame.Holds = false;
            frame.HeldError = null;
            frame.Waiter = null;
        }

        /// <summary>
        /// A call of a function with a rest parameter that gives more arguments than its parameters (section 3.5): those
        /// after them an array, held in <see cref="_rest"/>, a root, until the function's first instruction takes it.
        /// </summary>
        private bool TryGatherRest(WebLingoFunction function, int withThis, WebLingoValue[] arguments, int firstArgument,
            int argumentCount, out WebLingoRuntimeError? error)
        {
            var others = function.ParameterCount - withThis;
            if (!heap.TryAllocateArray(arguments, firstArgument + others, argumentCount - others, out var rest, out error)) return false;
            _rest[0] = rest;
            return true;
        }

        /// <summary>
        /// A value's text at once, into <paramref name="destination"/>, for one that is neither a collection nor an
        /// instance, whose text the text builder would give as the formatter does, whatever <c>toString()</c> a class has
        /// (the specification, section 5.6, "Where the time goes"): a string itself; false for any other value, whose text
        /// the builder makes. An error is the string's allocation's.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryPlainText(WebLingoValue value, WebLingoHeap heap, WebLingoValue[] registers, int destination,
            out WebLingoRuntimeError? error)
        {
            error = null;
            if (value.Kind is WebLingoValueKind.Array or WebLingoValueKind.Map or WebLingoValueKind.Set or WebLingoValueKind.Instance)
                return false;
            if (value.Kind == WebLingoValueKind.String)
            {
                registers[destination] = value;
                return true;
            }
            if (heap.TryAllocateString(WebLingoValueFormatter.Format(value, heap), out var text, out error)) registers[destination] = text;
            return true;
        }

        /// <summary>
        /// <c>x.toString()</c> of an integer, a number, a decimal, a Boolean or a string, where no class has been given a
        /// <c>toString()</c> of its own (<see cref="WebLingoHeap.ToStringGiven"/>), so that <c>Object</c>'s would run,
        /// <c>toText(this)</c>: its text at once, without that call (section 3.7); false for any other call.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryValueToString(WebLingoValue target, WebLingoMemberName name, int count, WebLingoHeap heap,
            WebLingoValue[] registers, int destination, out WebLingoRuntimeError? error)
        {
            error = null;
            return name.Member == WebLingoMember.ToString && count == 0 && !heap.ToStringGiven
                && target.Kind is WebLingoValueKind.Int64 or WebLingoValueKind.Binary64 or WebLingoValueKind.Decimal
                    or WebLingoValueKind.Boolean or WebLingoValueKind.String
                && TryPlainText(target, heap, registers, destination, out error);
        }

        /// <summary>WL2006 for a call of a function, which may have a rest parameter (section 3.5).</summary>
        private static WebLingoRuntimeError WrongArgumentCountOf(string name, int arity, int optional, bool hasRest, int count)
        {
            if (!hasRest) return WrongArgumentCount(name, arity, count, optional);
            var least = arity - optional;
            return new(WebLingoDiagnosticCodes.WrongArgumentCount,
                $"'{name}' takes at least {least} {(least == 1 ? "argument" : "arguments")}, not {count}.");
        }

        /// <summary>
        /// A rest parameter's value (section 3.5): the arguments its call gathered, held in <see cref="_rest"/>, else a new
        /// empty array, the call having given none after the parameters.
        /// </summary>
        private bool TryTakeRest(out WebLingoValue rest, out WebLingoRuntimeError? error)
        {
            if (_rest[0].Kind == WebLingoValueKind.Array)
            {
                (rest, _rest[0], error) = (_rest[0], WebLingoValue.Null, null);
                return true;
            }
            return heap.TryAllocateArray(_rest, 0, 0, out rest, out error);
        }

        /// <summary>
        /// A spread call's arguments (section 3.5): the elements of its array in the registers its call takes them from, from
        /// the array's own on, or after the target's for a method, the frame's registers grown where they are too few; and
        /// the call it is, in <see cref="_spread"/>. Elements beyond the function's own registers stay in its frame's
        /// array, as values above a stack VM's operands do, until something else is put there.
        /// </summary>
        private bool TrySpread(Frame frame, in WebLingoInstruction ins, out WebLingoRuntimeError? error)
        {
            var method = ins.OpCode == WebLingoOpCode.CallMethodSpread;
            var registers = frame.Registers;
            var values = registers[method ? ins.D : ins.C];
            if (values.Kind != WebLingoValueKind.Array)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'...' spreads an array into a call, not {WebLingoValueFormatter.Describe(values, heap)}.");
                return false;
            }
            var elements = heap.GetArray(values);
            if (elements.Count > WebLingoFunction.MaxRegisters)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    $"'...' spreads at most {WebLingoFunction.MaxRegisters} arguments into a call, not {elements.Count}.");
                return false;
            }
            var first = method ? ins.C + 1 : ins.C;
            if (first + elements.Count > registers.Length) registers = Grown(frame, registers, first + elements.Count);
            for (var i = 0; i < elements.Count; i++) registers[first + i] = elements[i];
            _spread[0] = ins.OpCode switch
            {
                WebLingoOpCode.CallSpread => new WebLingoInstruction(WebLingoOpCode.Call, ins.A, ins.B, first, elements.Count),
                WebLingoOpCode.NewSpread => new WebLingoInstruction(WebLingoOpCode.New, ins.A, ins.B, first, elements.Count),
                _ => new WebLingoInstruction(WebLingoOpCode.CallMethod, ins.A, ins.B, ins.C, elements.Count)
            };
            error = null;
            return true;
        }

        /// <summary>The running frame's registers made <paramref name="length"/> long.</summary>
        private static WebLingoValue[] Grown(Frame frame, WebLingoValue[] registers, int length)
        {
            var grown = new WebLingoValue[length];
            Array.Copy(registers, grown, registers.Length);
            frame.Registers = grown;
            return grown;
        }

        private static WebLingoRuntimeError WrongArgumentCount(string name, int arity, int count, int optional = 0) =>
            new(WebLingoDiagnosticCodes.WrongArgumentCount, optional == 0
                ? $"'{name}' takes {arity} {(arity == 1 ? "argument" : "arguments")}, not {count}."
                : $"'{name}' takes {arity - optional} to {arity} arguments, not {count}.");

        private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        /// <summary>
        /// A closure of the module's function, its captures taken from the making function's registers or its own
        /// captures, as the function's capture table says.
        /// </summary>
        private bool TryMakeClosure(int function, WebLingoValue[] registers, WebLingoValue[] cells, out WebLingoValue closure, out WebLingoRuntimeError? error)
        {
            var captures = module.Functions[function].Captures;
            var captured = new WebLingoValue[captures.Count];
            for (var i = 0; i < captured.Length; i++)
            {
                captured[i] = captures[i].FromLocal ? registers[captures[i].Index] : cells[captures[i].Index];
                if (captured[i].Kind != WebLingoValueKind.Cell) throw new InvalidOperationException("A capture is not a cell.");
            }
            return heap.TryAllocateClosure(function, captured, out closure, out error);
        }

        /// <summary>
        /// A class value, made as <c>new Class(name, base)</c> makes one, and given its members as a program gives them while
        /// it runs, by the heap's same methods (section 3.7).
        /// </summary>
        private bool TryMakeClass(WebLingoClassInfo info, WebLingoValue baseClass, WebLingoValue[] registers, WebLingoValue[] cells, out WebLingoValue made,
            out WebLingoRuntimeError? error)
        {
            // A built-in class that a module imported while the program runs declares again is the program's (section 3.9).
            if (_madeAlready is not null && _madeAlready.TryGetValue(info, out made))
            {
                error = null;
                return true;
            }
            if (!heap.TryMakeClass(info.Name, baseClass, out made, out error)) return false;
            heap.GiveFields(made, LayoutOf(info), info);
            if (!TryGiveMembers(info, made, registers, cells, out error)) return false;
            heap.Made(made);
            return true;
        }

        /// <summary>
        /// The places of a class's instances' fields, as its definition gives them, each name the string the program's
        /// instructions name it by, which <c>GetField</c> and <c>SetField</c> compare by reference; made once for each
        /// definition (specification, section 9, "M0.21 parts", part 2).
        /// </summary>
        private string[] LayoutOf(WebLingoClassInfo info)
        {
            if (info.Fields.Count == 0) return [];
            if (_layouts.TryGetValue(info, out var layout)) return layout;
            layout = new string[info.Fields.Count];
            for (var i = 0; i < layout.Length; i++) layout[i] = Text(info.Fields[i]);
            _layouts.Add(info, layout);
            return layout;
        }
        private readonly Dictionary<WebLingoClassInfo, string[]> _layouts = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// The program's names, its string constants' texts, by their text, each the one string its instructions read: a
        /// module joined to it given the same strings for the same names.
        /// </summary>
        private string Text(string text)
        {
            if (_nameTexts is null)
            {
                _nameTexts = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var name in names)
                {
                    if (!string.IsNullOrEmpty(name.Text)) _nameTexts.TryAdd(name.Text, name.Text);
                }
            }
            if (_nameTexts.TryGetValue(text, out var same)) return same;
            _nameTexts.Add(text, text);
            return text;
        }
        private Dictionary<string, string>? _nameTexts;

        /// <summary>A <c>GetField</c> or <c>SetField</c> read or assigned by its name, as the instruction it is then.</summary>
        private readonly WebLingoInstruction[] _member = new WebLingoInstruction[1];

        /// <summary>
        /// A <c>GetField</c> or <c>SetField</c> as the <c>GetMember</c> or <c>SetMember</c> that reads or assigns its field by its
        /// name, which the dispatch method runs in its place; not inlined, its copy of the instruction no part of that method.
        /// </summary>
        /// <summary>
        /// What a member's instruction found last (specification, section 9, "M0.21 parts", part 3), in its function's
        /// <see cref="WebLingoFunction.MemberSites"/>: a field's place among those of a class's definition; or a method, the
        /// definition of the instance's class, how many bases up the method was declared, and its place among that class's
        /// declared methods. Immutable, replaced whole, keyed by definitions every execution shares.
        /// </summary>
        private sealed class FieldSite(object definition, int place)
        {
            public readonly object Definition = definition;
            public readonly int Place = place;
        }

        private sealed class MethodSite(object definition, int depth, int index)
        {
            public readonly object Definition = definition;
            public readonly int Depth = depth;
            public readonly int Index = index;
        }

        /// <summary>
        /// <c>GetMember</c>'s, or a <c>GetField</c>'s by its name, member of an instance (specification, section 9, "M0.21
        /// parts", part 3): the field at the place its cache found it last, else as the runtime reads it, a field at one of
        /// the instance's places remembered; the instruction read from the code, its operands where both have them.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryGetInstanceMember(WebLingoFunction function, WebLingoInstruction[] code, int pc, WebLingoValue[] registers,
            out WebLingoValue member, out WebLingoRuntimeError? error)
        {
            ref readonly var ins = ref code[pc];
            var instance = registers[ins.B];
            var name = names[ins.C];
            error = null;
            if (function.MemberSites[pc] is FieldSite site && heap.TryGetPlaceOf(instance, site.Definition, site.Place, name.Text, out member))
            {
                return true;
            }
            var optional = ins.OpCode == WebLingoOpCode.GetMember && ins.D != 0;
            if (!WebLingoMembers.TryGet(instance, name.Member, name.Text, heap, optional, out member, out error)) return false;
            RememberField(function, pc, instance, ins.C);
            return true;
        }

        /// <summary><c>SetMember</c>'s, or a <c>SetField</c>'s by its name, field of an instance assigned, as its cache or the runtime finds it.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TrySetInstanceMember(WebLingoFunction function, WebLingoInstruction[] code, int pc, WebLingoValue[] registers,
            out WebLingoRuntimeError? error, out WebLingoValue setter)
        {
            ref readonly var ins = ref code[pc];
            var instance = registers[ins.A];
            var name = names[ins.B].Text;
            (error, setter) = (null, WebLingoValue.Null);
            if (function.MemberSites[pc] is FieldSite site && heap.TrySetPlaceOf(instance, site.Definition, site.Place, name, registers[ins.C]))
            {
                return true;
            }
            if (!WebLingoMembers.TrySet(instance, name, registers[ins.C], heap, out error, out setter)) return false;
            RememberField(function, pc, instance, ins.B);
            return true;
        }

        /// <summary>A field an instance has assigned at one of its places, remembered for the instruction that reached it by name.</summary>
        private void RememberField(WebLingoFunction function, int pc, WebLingoValue instance, int name)
        {
            var place = heap.AssignedPlaceOf(instance, names[name].Text, out var definition);
            if (place < 0 || function.MemberSites[pc] is FieldSite same && ReferenceEquals(same.Definition, definition) && same.Place == place)
            {
                return;
            }
            function.MemberSites[pc] = new FieldSite(definition!, place);
        }

        /// <summary><c>CallMethod</c>'s method where its cache found it last; false where its name must find it.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryGetCachedMethod(WebLingoFunction function, int pc, WebLingoValue instance, out WebLingoValue method)
        {
            method = WebLingoValue.Null;
            return function.MemberSites[pc] is MethodSite site
                && heap.TryGetDeclaredMethod(instance, site.Definition, site.Depth, site.Index, out method);
        }

        /// <summary>
        /// A method an instance's class declared, remembered for the instruction that called it by name, but for a disposal,
        /// which runs its chain; the name the instruction's, <c>CallMethod</c>'s or a spread's.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RememberMethod(WebLingoFunction function, WebLingoInstruction[] code, int pc, WebLingoValue instance, WebLingoValue method)
        {
            var name = names[code[pc].B];
            if (name.Member == WebLingoMember.Dispose) return;
            if (!heap.TryFindDeclaredMethod(instance, name.Text, method, out var definition, out var depth, out var index)) return;
            if (function.MemberSites[pc] is MethodSite same && ReferenceEquals(same.Definition, definition) && same.Depth == depth
                && same.Index == index)
            {
                return;
            }
            function.MemberSites[pc] = new MethodSite(definition!, depth, index);
        }

        /// <summary><c>GetField</c>'s field at its place, given its destination; false where the name must read it.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryGetField(WebLingoValue[] registers, WebLingoInstruction[] code, int pc)
        {
            ref readonly var ins = ref code[pc];
            var target = registers[ins.B];
            if (target.Kind != WebLingoValueKind.Instance || !heap.TryGetPlace(target, ins.D, names[ins.C].Text, out var value)) return false;
            registers[ins.A] = value;
            return true;
        }

        /// <summary><c>SetField</c>'s field assigned at its place; false where the name must assign it.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TrySetField(WebLingoValue[] registers, WebLingoInstruction[] code, int pc)
        {
            ref readonly var ins = ref code[pc];
            var target = registers[ins.A];
            return target.Kind == WebLingoValueKind.Instance && heap.TrySetPlace(target, ins.D, names[ins.B].Text, registers[ins.C]);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ref readonly WebLingoInstruction ByName(WebLingoInstruction[] code, int pc)
        {
            ref readonly var ins = ref code[pc];
            _member[0] = ins with { OpCode = ins.OpCode == WebLingoOpCode.GetField ? WebLingoOpCode.GetMember : WebLingoOpCode.SetMember, D = 0 };
            return ref _member[0];
        }

        /// <summary><c>extend</c> (section 3.7): an extension's members given to the class <paramref name="target"/>, as a class's are.</summary>
        private bool TryExtendClass(WebLingoClassInfo info, WebLingoValue target, WebLingoValue[] registers, WebLingoValue[] cells, out WebLingoRuntimeError? error) =>
            heap.TryCheckClass(target, out error) && TryGiveMembers(info, target, registers, cells, out error);

        /// <summary>
        /// A class's members, its declaration's or an extension's, each a closure made as its captures say, given to the class
        /// by the heap's method that gives a program's own while it runs: its constructor, its methods, a property's getter
        /// and setter among them, its static methods, and its static fields, <c>null</c> until they are set (section 3.7).
        /// The class, and each closure until the class holds it, are kept as roots, since making one may collect.
        /// </summary>
        private bool TryGiveMembers(WebLingoClassInfo info, WebLingoValue target, WebLingoValue[] registers, WebLingoValue[] cells,
            out WebLingoRuntimeError? error)
        {
            var roots = new WebLingoValue[] { target, WebLingoValue.Null };
            heap.AddRoots(roots);
            try
            {
                error = null;
                if (info.Constructor != -1)
                {
                    if (!TryMakeClosure(info.Constructor, registers, cells, out roots[1], out error)) return false;
                    heap.GiveConstructor(target, roots[1], info.DefaultConstructor);
                }
                for (var i = 0; i < info.Methods.Count; i++)
                {
                    if (!TryMakeClosure(info.Methods[i].Function, registers, cells, out roots[1], out error)
                        || !heap.TrySetMethod(target, info.Methods[i].Name, roots[1], out error))
                    {
                        return false;
                    }
                }
                for (var i = 0; i < info.StaticMethods.Count; i++)
                {
                    if (!TryMakeClosure(info.StaticMethods[i].Function, registers, cells, out roots[1], out error)
                        || !heap.TrySetStaticMethod(target, info.StaticMethods[i].Name, roots[1], out error,
                            module.Functions[info.StaticMethods[i].Function].HasThis))
                    {
                        return false;
                    }
                }
                for (var i = 0; i < info.StaticFields.Count; i++)
                {
                    if (!heap.TryDeclareStatic(target, info.StaticFields[i], WebLingoValue.Null, out error)) return false;
                }
                return true;
            }
            finally
            {
                heap.RemoveLastRoots(roots);
            }
        }

        /// <summary>
        /// A native member's arguments, read where they are (section 3.9): after the instance it runs on, when that is the
        /// value just before them, as a method's call has it; else copied, the instance first, into the execution's own
        /// buffer, which no other call uses meanwhile, a built-in calling nothing of the VM's.
        /// </summary>
        private ReadOnlySpan<WebLingoValue> NativeArguments(WebLingoValue self, int withThis, WebLingoValue[] arguments, int first, int count)
        {
            if (withThis == 0) return new ReadOnlySpan<WebLingoValue>(arguments, first, count);
            if (first > 0 && arguments[first - 1] == self) return new ReadOnlySpan<WebLingoValue>(arguments, first - 1, count + 1);
            if (_nativeArguments.Length < count + 1) _nativeArguments = new WebLingoValue[count + 1];
            _nativeArguments[0] = self;
            Array.Copy(arguments, first, _nativeArguments, 1, count);
            return new ReadOnlySpan<WebLingoValue>(_nativeArguments, 0, count + 1);
        }

        private WebLingoValue[] _nativeArguments = new WebLingoValue[4];

        /// <summary>
        /// Calls a built-in with the <paramref name="count"/> arguments from <paramref name="first"/> on, read where they are,
        /// its result written once it has returned. Its failure, even an exception, is a runtime error.
        /// </summary>
        private WebLingoRuntimeError? CallBuiltin(IWebLingoBuiltinFunction builtin, WebLingoValue[] values, int first, int count, out WebLingoValue result)
        {
            result = WebLingoValue.Null;
            try
            {
                if (!builtin.TryInvoke(new ReadOnlySpan<WebLingoValue>(values, first, count), heap, out result, out var error))
                {
                    return error ?? new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed.");
                }
                return null;
            }
            catch (Exception exception)
            {
                return new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed: {exception.Message}");
            }
        }

        /// <summary>
        /// A native member that only calls its built-in (section 3.9): the built-in called in its place, given the instance
        /// first when the call has one, the member's frame never made, what the member gives going to
        /// <paramref name="result"/>; its failure, even an exception, a runtime error where the member was called. False,
        /// leaving the call to the member's frame, for a disposal's call or a text's, which go on when that frame returns.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryCallNative(WebLingoFunction function, WebLingoValue self, int withThis, WebLingoValue[] arguments, int first,
            int count, WebLingoValue[] registers, int result, WebLingoValue constructed, Frame[] frames, int depth,
            out WebLingoRuntimeError? error)
        {
            error = null;
            if (result < 0 || (depth + 1 < frames.Length && frames[depth + 1] is { Waiter: not null } or { DisposeChain: not null })) return false;
            var builtin = builtins[function.NativeBuiltin];
            var values = NativeArguments(self, withThis, arguments, first, count);
            try
            {
                if (!builtin.TryInvoke(values, heap, out var given, out error))
                {
                    error ??= new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed.");
                    return true;
                }
                registers[result] = function.NativeResult switch
                {
                    WebLingoNativeResult.LastArgument => values[^1],
                    WebLingoNativeResult.Nothing => constructed,
                    _ => given
                };
            }
            catch (Exception exception)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed: {exception.Message}");
            }
            return true;
        }

        /// <summary>
        /// Calls a built-in with its arguments read where they are, in the registers, its result written once it has
        /// returned. Its failure, even an exception, is a runtime error.
        /// </summary>
        private WebLingoRuntimeError? CallBuiltin(IWebLingoBuiltinFunction builtin, WebLingoValue[] registers, in WebLingoInstruction instruction)
        {
            try
            {
                if (!builtin.TryInvoke(new ReadOnlySpan<WebLingoValue>(registers, instruction.C, instruction.D), heap, out var result, out var error))
                {
                    if (error is null && builtin is IWebLingoCallingBuiltin calling) return Begin(calling, registers, instruction);
                    return error ?? new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed.");
                }
                registers[instruction.A] = result;
                return null;
            }
            catch (Exception exception)
            {
                return new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed: {exception.Message}");
            }
        }
    }

    private static WebLingoArithmeticOperator ArithmeticOf(WebLingoOpCode opCode) => opCode switch
    {
        WebLingoOpCode.Add => WebLingoArithmeticOperator.Add,
        WebLingoOpCode.Subtract => WebLingoArithmeticOperator.Subtract,
        WebLingoOpCode.Multiply => WebLingoArithmeticOperator.Multiply,
        WebLingoOpCode.Remainder => WebLingoArithmeticOperator.Remainder,
        WebLingoOpCode.IntegerDivide => WebLingoArithmeticOperator.IntegerDivide,
        _ => WebLingoArithmeticOperator.Divide
    };

    private static WebLingoComparisonOperator ComparisonOf(WebLingoOpCode opCode) => opCode switch
    {
        WebLingoOpCode.Equal => WebLingoComparisonOperator.Equal,
        WebLingoOpCode.NotEqual => WebLingoComparisonOperator.NotEqual,
        WebLingoOpCode.Less => WebLingoComparisonOperator.Less,
        WebLingoOpCode.LessOrEqual => WebLingoComparisonOperator.LessOrEqual,
        WebLingoOpCode.Greater => WebLingoComparisonOperator.Greater,
        _ => WebLingoComparisonOperator.GreaterOrEqual
    };

    private static WebLingoSourceSpan SpanOf(WebLingoFunction function, int pc) =>
        function.Spans is { } spans && pc < spans.Count ? spans[pc] : new WebLingoSourceSpan(0, 0);

    private static WebLingoExecutionResult Failure(string code, string message, WebLingoSourceSpan span) =>
        new(false, new WebLingoDiagnostic(code, WebLingoDiagnosticSeverity.Error, message, span));

    /// <summary>A failure at an instruction of a function, naming the file the function is in, if not the program's (section 3.9).</summary>
    private static WebLingoExecutionResult Failure(string code, string message, WebLingoFunction function, int pc) =>
        new(false, new WebLingoDiagnostic(code, WebLingoDiagnosticSeverity.Error, message, SpanOf(function, pc)) { Source = function.Source });
}
