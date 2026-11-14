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
    public const int CancellationInterval = 1024;

    private readonly IWebLingoBytecodeVerifier _verifier;

    /// <summary>What finds and compiles a module a program imports while it runs (section 3.9); none, and it can import none.</summary>
    public IWebLingoModuleLoader? Loader { get; init; }

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
            if (!context.Services.TryGetBuiltin(import.Name, out var builtin) || builtin.Arity != import.Arity)
            {
                return Failure(WebLingoDiagnosticCodes.UndefinedIdentifier,
                    $"This host has no built-in '{import.Name}' taking {import.Arity} arguments.", new WebLingoSourceSpan(0, 0));
            }
            builtins[i] = builtin;
        }

        // The execution's own heap (worker-local, section 4.3), whose roots are the constants and the frames.
        var heap = new WebLingoHeap(context.Resources.MaxHeapUnits);
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
            constants[i] = constant.Kind == WebLingoConstantKind.Int64
                ? WebLingoValue.FromInt64(constant.Bits)
                : WebLingoValue.FromBinary64Bits(constant.Bits);
        }

        // Each function's code as an array, which the loop indexes directly.
        var codes = new WebLingoInstruction[module.Functions.Count][];
        for (var i = 0; i < codes.Length; i++) codes[i] = ToArray(module.Functions[i].Code);

        return new Execution(module, ToArray(module.EntryPoint.Code), codes, constants, names, builtins, context, heap) { Loader = Loader }.Run();
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
        /// <summary>For a toString() that a text being built waits for: that text (section 3.7).</summary>
        public TextCall? Text;
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
    /// A text being built (section 3.7), and what it is for: a <c>ToText</c>'s result, or a built-in that writes it,
    /// whose result goes where <see cref="Destination"/> says; and whether its VM keeps its values as roots, as it
    /// does while a toString() it waits for runs.
    /// </summary>
    private sealed class TextCall(WebLingoTextBuilder builder, IWebLingoBuiltinFunction? builtin, int destination)
    {
        public WebLingoTextBuilder Builder { get; } = builder;
        public IWebLingoBuiltinFunction? Builtin { get; } = builtin;
        /// <summary>The register its result goes to.</summary>
        public int Destination { get; } = destination;
        public bool Rooted { get; set; }
    }

    private sealed class Execution(WebLingoCompiledModule module, WebLingoInstruction[] entryCode, WebLingoInstruction[][] codes,
        WebLingoValue[] constants, WebLingoMemberName[] names, IWebLingoBuiltinFunction[] builtins, WebLingoExecutionContext context, WebLingoHeap heap)
    {
        public WebLingoExecutionResult Run()
        {
            var frames = new Frame[16];
            var depth = 0;
            var frame = frames[0] = new Frame
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
            _modules = new WebLingoModuleTable(module.Units.Count, heap);
            for (var i = 0; i < module.Units.Count; i++) _unitClosures.Add(WebLingoValue.Null);
            heap.AddRoots(_unitClosures);
            heap.AddRoots(_linked);
            heap.AddRoots(frame.Registers);
            heap.AddRoots(frame.Closure);
            _scheduler = new WebLingoScheduler(heap, context.Clock);
            heap.Scheduler = _scheduler;
            heap.ValueClasses = _valueClasses;
            if (module.EntryPoint.IsAsync && !TryStartMain(frame)) return _ended!;

            var code = frame.Code;
            var registers = frame.Registers;
            var cells = frame.Cells;
            var pc = 0;
            var budget = context.InstructionBudget;
            var cancellation = context.Cancellation;
            var maxCallDepth = context.Resources.MaxCallDepth;
            var executed = 0L;
            var dueAt = 0L; // The count of instructions at which the budget and the cancellation signal are looked at.
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
                        dueAt = Math.Min(budget, (executed / CancellationInterval + 1) * CancellationInterval);
                    }
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
                            if (!Arithmetic(WebLingoOpCode.Add, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Subtract:
                            if (!Arithmetic(WebLingoOpCode.Subtract, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Multiply:
                            if (!Arithmetic(WebLingoOpCode.Multiply, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Divide:
                            if (!Arithmetic(WebLingoOpCode.Divide, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Equal:
                            if (!Compare(WebLingoOpCode.Equal, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.NotEqual:
                            if (!Compare(WebLingoOpCode.NotEqual, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Less:
                            if (!Compare(WebLingoOpCode.Less, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.LessOrEqual:
                            if (!Compare(WebLingoOpCode.LessOrEqual, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.Greater:
                            if (!Compare(WebLingoOpCode.Greater, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WebLingoOpCode.GreaterOrEqual:
                            if (!Compare(WebLingoOpCode.GreaterOrEqual, registers, ins, out error)) break;
                            pc++;
                            continue;
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
                            if (_texts is not null && _texts[ins.B] && ins.D == 1 && NeedsText(registers[ins.C]))
                            {
                                // A built-in that writes text, of a value that may hold an instance whose toString() must run.
                                _text = new TextCall(new WebLingoTextBuilder(heap, registers[ins.C], false), builtins[ins.B], ins.A);
                                goto text;
                            }
                            error = CallBuiltin(builtins[ins.B], registers, ins);
                            if (error is not null) break;
                            pc++;
                            continue;
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
                            var name = names[ins.C];
                            if (!WebLingoMembers.TryGet(registers[ins.B], name.Member, name.Text, heap, ins.D != 0, out var member,
                                    out error))
                            {
                                PendAccessor(error, member, registers[ins.B], default, ins.A);
                                break;
                            }
                            registers[ins.A] = member;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.SetMember:
                            if (!WebLingoMembers.TrySet(registers[ins.A], names[ins.B].Text, registers[ins.C], heap, out error, out callee))
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
                                && WebLingoMembers.TryFindCollectionCallee(target, name.Text, heap, out callee, out var collectionMethod))
                            {
                                // A collection of a class extending Array, Map or Set: its field's function, or its class's
                                // method, before its own members (section 3.7).
                                self = collectionMethod ? target : WebLingoValue.Missing;
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
                                // A field's function is called as any function is; a method runs on the instance.
                                if (!WebLingoMembers.TryFindCallee(target, name.Text, heap, out callee, out var isMethod, out error)) break;
                                self = isMethod ? target : WebLingoValue.Missing;
                                arguments = registers;
                                firstArgument = ins.C + 1;
                                argumentCount = ins.D;
                                resultRegister = ins.A;
                                constructed = WebLingoValue.Null;
                                if (isMethod && name.Member == WebLingoMember.Dispose) goto dispose;
                                goto call;
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
                            // Module.import(path, values): a task, at once, or the call that gives one (section 3.9).
                            registers[ins.A] = ImportedWhileRunning(registers[ins.B], ins.D == 1 ? registers[ins.C] : WebLingoValue.Null, ins.A,
                                frame.Function, out error);
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
                                if (!heap.TryMakeValue(classValue, registers, ins.C, ins.D, out registers[ins.A], out error)) break;
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
                            self = instance;
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
                        case WebLingoOpCode.GetIndex:
                        {
                            if (!WebLingoMembers.TryGetIndex(registers[ins.B], registers[ins.C], heap, ins.D != 0, out var element,
                                    out error))
                            {
                                break;
                            }
                            registers[ins.A] = element;
                            pc++;
                            continue;
                        }
                        case WebLingoOpCode.SetIndex:
                            if (!WebLingoMembers.TrySetIndex(registers[ins.A], registers[ins.B], registers[ins.C], heap, out error)) break;
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
                            if (depth + 1 == frames.Length) Array.Resize(ref frames, frames.Length * 2);
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
                            _text = new TextCall(new WebLingoTextBuilder(heap, registers[ins.B], true), null, ins.A);
                            goto text;
                        case WebLingoOpCode.Await:
                            _awaited = registers[ins.B];
                            frame.AwaitDestination = ins.A;
                            frame.Pc = pc + 1;
                            goto awaiting;
                        case WebLingoOpCode.Throw:
                            _thrown = registers[ins.A];
                            _thrownError = null;
                            _origin = OriginOf(frame.Function, pc);
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
                            heap.RemoveLastRoots(frame.Closure);
                            heap.RemoveLastRoots(registers);
                            Array.Clear(registers, 0, frame.Function.RegisterCount); // So that nothing stays reachable.
                            if (frame.DisposeChain is not null) goto disposed;
                            if (frame.Task.Kind == WebLingoValueKind.Task)
                            {
                                _returned = result;
                                goto asyncReturned;
                            }
                            if (frame.Text is not null)
                            {
                                _returned = result;
                                goto texted;
                            }
                            frame.Closure[0] = WebLingoValue.Null;
                            if (frame.Constructed.Kind != WebLingoValueKind.Null) result = frame.Constructed; // What new made: an instance, or a collection.
                            frame.Constructed = WebLingoValue.Null;
                            var returnTo = frame.ResultRegister;

                            frame = frames[--depth];
                            code = frame.Code;
                            registers = frame.Registers;
                            cells = frame.Cells;
                            pc = frame.Pc;
                            registers[returnTo] = result;
                            continue;
                        }
                        default:
                            // The verifier admits no other operation; this only guards against a verifier that did.
                            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BytecodeVerificationFailure,
                                $"The operation {(int)ins.OpCode} is unknown.");
                            break;
                    }
                    goto failed;

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
                        if (_texts is not null && _texts[callee.Handle] && argumentCount == 1 && NeedsText(arguments[firstArgument]))
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
                        var function = module.Functions[index];
                        var withThis = function.HasThis && self.Kind != WebLingoValueKind.Missing ? 1 : 0; // A receiver, if the call has one.
                        if (function.ParameterCount != argumentCount + withThis
                            && (argumentCount + withThis > function.ParameterCount
                                || argumentCount + withThis < function.ParameterCount - function.OptionalCount))
                        {
                            error = WrongArgumentCount(function.Name, function.ParameterCount - withThis, argumentCount,
                                function.OptionalCount);
                            goto failed;
                        }
                        if (depth + 1 >= maxCallDepth)
                        {
                            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BudgetExceeded,
                                $"The calls nest more than {maxCallDepth} deep, which this execution allows.");
                            goto failed;
                        }

                        if (function.IsAsync && !TryMakeTask(frames, depth, out error)) goto failed;
                        frame.Pc = pc + 1;
                        if (++depth == frames.Length) Array.Resize(ref frames, frames.Length * 2);
                        var called = frames[depth] ??= new Frame();
                        if (called.Registers.Length < function.RegisterCount)
                        {
                            called.Registers = new WebLingoValue[function.RegisterCount];
                        }
                        var calledRegisters = called.Registers;
                        if (withThis == 1) calledRegisters[0] = self;
                        for (var i = 0; i < argumentCount; i++) calledRegisters[withThis + i] = arguments[firstArgument + i];
                        // The parameters left out are missing, until the function's code gives them their defaults.
                        for (var i = withThis + argumentCount; i < function.ParameterCount; i++) calledRegisters[i] = WebLingoValue.Missing;
                        called.Function = function;
                        called.Code = codes[index];
                        called.Cells = closureCells;
                        called.Closure[0] = callee;
                        called.ResultRegister = resultRegister;
                        called.Constructed = constructed;
                        if (function.IsAsync) Started(called);
                        heap.AddRoots(calledRegisters);
                        heap.AddRoots(called.Closure);

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
                        if (depth + 1 == frames.Length) Array.Resize(ref frames, frames.Length * 2);
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
                        frame = frames[--depth];
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
                        var nextFunction = module.Functions[nextIndex];
                        if (registers.Length < nextFunction.RegisterCount)
                        {
                            frame.Registers = registers = new WebLingoValue[nextFunction.RegisterCount];
                        }
                        registers[0] = frame.Closure[1];
                        frame.Function = nextFunction;
                        frame.Code = code = codes[nextIndex];
                        frame.Cells = cells = nextCells;
                        frame.Closure[0] = next;
                        heap.AddRoots(registers);
                        heap.AddRoots(frame.Closure);
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
                            if (!building.Rooted)
                            {
                                heap.AddRoots(building.Builder.Roots);
                                building.Rooted = true;
                            }
                            if (depth + 1 == frames.Length) Array.Resize(ref frames, frames.Length * 2);
                            (frames[depth + 1] ??= new Frame()).Text = building;
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
                        _text = frame.Text!;
                        frame.Text = null;
                        frame.Closure[0] = WebLingoValue.Null;
                        frame = frames[--depth];
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
                        if (_returned.Kind != WebLingoValueKind.String)
                        {
                            if (building.Rooted) heap.RemoveLastRoots(building.Builder.Roots);
                            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                                $"'toString()' gave {WebLingoValueFormatter.Describe(_returned, heap)}, not a string, to be written.");
                            _returned = WebLingoValue.Null;
                            goto failed;
                        }
                        building.Builder.Supply(heap.GetString(_returned));
                        _returned = WebLingoValue.Null;
                        _text = building;
                        goto text;
                    }

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
                    _text = Gave(frame, frames[depth - 1]);
                    frame = frames[--depth];
                    code = frame.Code;
                    registers = frame.Registers;
                    cells = frame.Cells;
                    if (_text is not null) goto textGiven;
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
                    if (!TryResume(frames, cancellation, executed)) return _ended!;
                    depth = 0;
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
                    // A call that failed before its frame was pushed leaves nothing in that frame: no disposal, no text.
                    if (depth + 1 < frames.Length && frames[depth + 1] is { } unpushed)
                    {
                        unpushed.DisposeChain = null;
                        unpushed.Task = WebLingoValue.Null;
                        unpushed.Closure[3] = WebLingoValue.Null;
                        unpushed.FirstRun = false;
                        if (unpushed.Text is { Rooted: true } stale) heap.RemoveLastRoots(stale.Builder.Roots);
                        unpushed.Text = null;
                    }
                    // A runtime error: one that cannot be caught stops the program, and any other is thrown (section 3.7).
                    if (!WebLingoExceptions.IsCatchable(error!.Code))
                    {
                        return Failure(error.Code, error.Message, frame.Function, pc) with { Instructions = executed };
                    }
                    _thrown = WebLingoValue.Null;
                    _thrownError = error;
                    _origin = OriginOf(frame.Function, pc);

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
                            heap.RemoveLastRoots(frame.Closure);
                            heap.RemoveLastRoots(registers);
                            Array.Clear(registers, 0, frame.Function.RegisterCount);
                            goto nextDisposal;
                        }
                        if (frame.Task.Kind == WebLingoValueKind.Task) goto faulted;
                        if (depth == 0) return Uncaught(executed);
                        heap.RemoveLastRoots(frame.Closure);
                        heap.RemoveLastRoots(registers);
                        Array.Clear(registers, 0, frame.Function.RegisterCount);
                        if (frame.Text is { Rooted: true } waiting) heap.RemoveLastRoots(waiting.Builder.Roots);
                        Release(frame);
                        frame = frames[--depth];
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
        private bool Arithmetic(WebLingoOpCode opCode, WebLingoValue[] registers, in WebLingoInstruction ins, out WebLingoRuntimeError? error)
        {
            var (left, right) = (registers[ins.B], registers[ins.C]);
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
                    default:
                        return Done(WebLingoValue.FromBinary64((double)a / b), registers, ins);
                }
            }
            else if (left.Kind == WebLingoValueKind.Binary64 && right.Kind == WebLingoValueKind.Binary64)
            {
                var (x, y) = (left.AsBinary64, right.AsBinary64);
                return Done(WebLingoValue.FromBinary64(opCode switch
                {
                    WebLingoOpCode.Add => x + y,
                    WebLingoOpCode.Subtract => x - y,
                    WebLingoOpCode.Multiply => x * y,
                    _ => x / y
                }), registers, ins);
            }
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
        /// A comparison: two int64s, or two binary64s (IEEE 754's, so that NaN is unordered and unequal to itself),
        /// here; anything else by <see cref="WebLingoOperators"/>, which gives the same results.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Compare(WebLingoOpCode opCode, WebLingoValue[] registers, in WebLingoInstruction ins, out WebLingoRuntimeError? error)
        {
            var (left, right) = (registers[ins.B], registers[ins.C]);
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

        /// <summary>The class Error, once the module has made it, which a caught runtime error becomes (section 3.7); a root.</summary>
        private readonly WebLingoValue[] _errorClass = new WebLingoValue[1];

        /// <summary>The class Object, once the module has made it, whose toString() every value has (section 3.7); a root.</summary>
        private readonly WebLingoValue[] _objectClass = new WebLingoValue[1];

        /// <summary>
        /// For each built-in, whether it writes text (<see cref="IWebLingoTextBuiltin"/>), and so is given a value's text with
        /// the toString() its instances override run; null if no class of the module overrides toString(), as then the
        /// formatter alone gives every text (section 3.7).
        /// </summary>
        private bool[]? _texts = TextBuiltins(module, builtins);

        /// <summary>The execution's tasks (section 3.8), made when it starts; the program's own task, if its top level awaits, a root.</summary>
        private WebLingoScheduler _scheduler = null!;
        private readonly WebLingoValue[] _main = new WebLingoValue[1];

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

        /// <summary>The name of the operator a class may declare for an operation, or null for one it may not.</summary>
        private static string? OperatorName(WebLingoOpCode opCode) => opCode switch
        {
            WebLingoOpCode.Add => "operator+",
            WebLingoOpCode.Subtract => "operator-",
            WebLingoOpCode.Multiply => "operator*",
            WebLingoOpCode.Divide => "operator/",
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

        /// <summary>The values that modules imported while the program runs bring, their constants; a root.</summary>
        private readonly List<WebLingoValue> _linked = [];

        /// <summary>The built-in classes such a module declares again, and the program's of each, which making it gives.</summary>
        private Dictionary<WebLingoClassInfo, WebLingoValue>? _madeAlready;

        /// <summary>
        /// <c>Module.import(path, values)</c> (section 3.9): a task, which completes with the module's instance once it has
        /// run; null when a call is pending that gives one: the module's function, if the program has its file, or its
        /// compiled import's entry, joined to the program, if not. Its path is found from the importing file's folder.
        /// </summary>
        private WebLingoValue ImportedWhileRunning(WebLingoValue path, WebLingoValue values, int result, WebLingoFunction function,
            out WebLingoRuntimeError? error)
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
            string? why = "this host imports no modules while a program runs";
            if (Loader is null || !Loader.TryResolve(importer, text, out var name, out why))
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.ModuleNotFound, $"The module '{text}' cannot be imported: {why}.");
                return WebLingoValue.Null;
            }
            var unit = UnitNamed(name);
            if (unit < 0)
            {
                // A file the program has not: compiled, with the files it imports, and joined to the program, whose entry
                // imports it with the values given.
                if (Loader.Compile(name, out var failure) is not { } compiled)
                {
                    error = new WebLingoRuntimeError(failure?.Code ?? WebLingoDiagnosticCodes.ModuleNotFound,
                        $"The module '{text}' does not compile: {failure?.Message ?? "it cannot be read"}");
                    return WebLingoValue.Null;
                }
                if (!TryLink(compiled, out var entry, out error)) return WebLingoValue.Null;
                _operands[0] = values;
                Pend(entry, 1, result);
                return WebLingoValue.Null;
            }
            var info = module.Units[unit];
            if (!_modules.TryFind(unit, info.Name, info.Parameters, values, out var instance, out error)) return WebLingoValue.Null;
            if (instance.Kind == WebLingoValueKind.Module || _unitClosures[unit].Kind != WebLingoValueKind.Function)
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
            _operands[0] = values;
            Pend(_unitClosures[unit], info.Parameters.Count > 0 ? 1 : 0, result);
            return WebLingoValue.Null;
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
                if (context.Services.TryGetBuiltin(wanted.Name, out var builtin) && builtin.Arity == wanted.Arity)
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
                    addedNames[i] = new WebLingoMemberName(WebLingoMembers.Find(constant.Text!), constant.Text!);
                    if (!heap.TryAllocateString(constant.Text!, out addedConstants[i], out error)) return false;
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
                        WebLingoOpCode.LoadConstant or WebLingoOpCode.SetMember or WebLingoOpCode.CallMethod => ins with { B = ins.B + constantBase },
                        WebLingoOpCode.GetMember => ins with { C = ins.C + constantBase },
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
            _texts = TextBuiltins(module, builtins);
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
            heap.RemoveLastRoots(frame.Closure);
            heap.RemoveLastRoots(frame.Registers);
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
            heap.RemoveLastRoots(frame.Closure);
            heap.RemoveLastRoots(frame.Registers);
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
        /// for a toString() a text waits for, to that text, which this gives.
        /// </summary>
        private TextCall? Gave(Frame frame, Frame caller)
        {
            frame.FirstRun = false;
            var text = frame.Text;
            frame.Text = null;
            if (text is null)
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
            return text;
        }

        /// <summary>
        /// Nothing runs on the VM's stack (section 3.8): the next ready task's frame resumes, at depth 0, its await given
        /// a value, or about to throw (<see cref="_resumedThrowing"/>), while timers complete, in order, when nothing is
        /// ready. False, with the end in <see cref="_ended"/>, once the top level is done, cancelled, or waits for what
        /// nothing will complete.
        /// </summary>
        private bool TryResume(Frame[] frames, WebLingoCancellationSignal? cancellation, long executed)
        {
            while (true)
            {
                if (_main[0].Kind != WebLingoValueKind.Task || heap.GetTask(_main[0]).State != WebLingoTaskState.Pending)
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
                    heap.AddRoots(frame.Registers);
                    heap.AddRoots(frame.Closure);
                    _resumedThrowing = resumption.Throws;
                    if (resumption.Throws) (_thrown, _thrownError, _origin) = (resumption.Value, resumption.Error, resumption.Origin);
                    // A timeout's error, which no instruction raised, is thrown from the await it reaches (section 3.8).
                    if (resumption.Throws && _origin == WebLingoExceptions.Nowhere) _origin = OriginOf(frame.Function, frame.Pc - 1);
                    else if (frame.AwaitDestination >= 0) frame.Registers[frame.AwaitDestination] = resumption.Value;
                    return true;
                }
                if (!_scheduler.TryAdvanceTime())
                {
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

        private static bool[]? TextBuiltins(WebLingoCompiledModule module, IWebLingoBuiltinFunction[] builtins)
        {
            var overrides = false;
            for (var i = 0; i < module.Classes.Count && !overrides; i++)
            {
                if (i == module.ObjectClass) continue;
                foreach (var method in module.Classes[i].Methods) overrides |= method.Name == "toString";
            }
            if (!overrides) return null;
            var texts = new bool[builtins.Length];
            for (var i = 0; i < builtins.Length; i++) texts[i] = builtins[i] is IWebLingoTextBuiltin;
            return texts;
        }

        /// <summary>Whether a value's text may hold an instance's, which its class's toString() may give.</summary>
        private static bool NeedsText(WebLingoValue value) =>
            value.Kind is WebLingoValueKind.Instance or WebLingoValueKind.Array or WebLingoValueKind.Map or WebLingoValueKind.Set;

        /// <summary>The kinds of values' classes, by <c>WebLingoValueClass</c>, once the module has made them (section 3.7); a root.</summary>
        private readonly WebLingoValue[] _valueClasses = new WebLingoValue[(int)WebLingoValueClass.Module + 1];

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
            if (index == module.ErrorClass) _errorClass[0] = made;
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
                // Array, Map and Set make collections, and classes extend them; Boolean, Integer, Number and String make
                // plain values, and classes extending them hold one; the others, for now, neither (section 3.7).
                if (i == (int)WebLingoValueClass.Array) heap.MarkCollectionClass(made, WebLingoValueKind.Array);
                else if (i == (int)WebLingoValueClass.Map) heap.MarkCollectionClass(made, WebLingoValueKind.Map);
                else if (i == (int)WebLingoValueClass.Set) heap.MarkCollectionClass(made, WebLingoValueKind.Set);
                else if (i == (int)WebLingoValueClass.Boolean) heap.MarkValueClass(made, WebLingoValueKind.Boolean);
                else if (i == (int)WebLingoValueClass.Integer) heap.MarkValueClass(made, WebLingoValueKind.Int64);
                else if (i == (int)WebLingoValueClass.Number) heap.MarkValueClass(made, WebLingoValueKind.Binary64);
                else if (i == (int)WebLingoValueClass.String) heap.MarkValueClass(made, WebLingoValueKind.String);
                else if (i == (int)WebLingoValueClass.Function) heap.MarkValueClass(made, WebLingoValueKind.Function);
                else if (i == (int)WebLingoValueClass.Class) heap.MarkClassClass(made);
                else heap.SealClass(made);
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
            if (!WebLingoExceptions.TryMakeError(heap, ErrorClassFor(_thrownError.Code), _thrownError, out exception)) return false;
            _thrown = exception;
            _thrownError = null;
            return true;
        }

        /// <summary>An exception that nothing caught: the program stops, reporting it where it was first thrown.</summary>
        private WebLingoExecutionResult Uncaught(long executed)
        {
            var (code, message) = _thrownError is { } error
                ? (error.Code, error.Message)
                : WebLingoExceptions.Uncaught(_thrown, _errorClass[0], heap);
            var failure = Failure(code, message, SpanOfOrigin(_origin)) with { Instructions = executed };
            return OwnerOfOrigin(_origin)?.Source is { } source ? failure with { Error = failure.Error! with { Source = source } } : failure;
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
            frame.Text = null;
        }

        /// <summary>A frame an exception leaves keeps nothing: its closure, its instance, a disposal, a kept exception.</summary>
        private static void Release(Frame frame)
        {
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
            frame.Text = null;
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
        /// A class value: its constructor's and its methods' closures made now, kept as roots until the class holds
        /// them, since making each may collect (section 3.7).
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
            made = WebLingoValue.Null;
            var closures = new WebLingoValue[info.Methods.Count + info.StaticMethods.Count + 1];
            heap.AddRoots(closures);
            try
            {
                if (info.Constructor != -1 && !TryMakeClosure(info.Constructor, registers, cells, out closures[0], out error)) return false;
                var methods = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.Methods.Count; i++)
                {
                    if (!TryMakeClosure(info.Methods[i].Function, registers, cells, out closures[i + 1], out error)) return false;
                    methods.Add(info.Methods[i].Name, closures[i + 1]);
                }
                var statics = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.StaticMethods.Count; i++)
                {
                    var slot = info.Methods.Count + 1 + i;
                    if (!TryMakeClosure(info.StaticMethods[i].Function, registers, cells, out closures[slot], out error)) return false;
                    statics.Add(info.StaticMethods[i].Name, closures[slot]);
                }
                return heap.TryAllocateClass(info.Name, closures[0], methods, baseClass, out made, out error, statics,
                    info.StaticFields, info.DefaultConstructor);
            }
            finally
            {
                heap.RemoveLastRoots(closures);
            }
        }

        /// <summary>
        /// <c>extend</c> (section 3.7): an extension's methods' and static methods' closures, made as their captures say,
        /// given to the class <paramref name="target"/>, with its static fields' names.
        /// </summary>
        private bool TryExtendClass(WebLingoClassInfo info, WebLingoValue target, WebLingoValue[] registers, WebLingoValue[] cells, out WebLingoRuntimeError? error)
        {
            var closures = new WebLingoValue[info.Methods.Count + info.StaticMethods.Count];
            heap.AddRoots(closures);
            try
            {
                var methods = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.Methods.Count; i++)
                {
                    if (!TryMakeClosure(info.Methods[i].Function, registers, cells, out closures[i], out error)) return false;
                    methods[info.Methods[i].Name] = closures[i];
                }
                var statics = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.StaticMethods.Count; i++)
                {
                    var slot = info.Methods.Count + i;
                    if (!TryMakeClosure(info.StaticMethods[i].Function, registers, cells, out closures[slot], out error)) return false;
                    statics[info.StaticMethods[i].Name] = closures[slot];
                }
                return heap.TryExtendClass(target, methods, statics, info.StaticFields, out error);
            }
            finally
            {
                heap.RemoveLastRoots(closures);
            }
        }

        /// <summary>
        /// Calls a built-in with the <paramref name="count"/> arguments from <paramref name="first"/> on, copied out, so
        /// that its result may overwrite them. Its failure, even an exception, is a runtime error.
        /// </summary>
        private WebLingoRuntimeError? CallBuiltin(IWebLingoBuiltinFunction builtin, WebLingoValue[] values, int first, int count, out WebLingoValue result)
        {
            result = WebLingoValue.Null;
            var arguments = new WebLingoValue[count];
            for (var i = 0; i < arguments.Length; i++) arguments[i] = values[first + i];
            try
            {
                if (!builtin.TryInvoke(arguments, heap, out result, out var error))
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
        /// Calls a built-in with its arguments copied out of the registers, so that its result may overwrite them.
        /// Its failure, even an exception, is a runtime error.
        /// </summary>
        private WebLingoRuntimeError? CallBuiltin(IWebLingoBuiltinFunction builtin, WebLingoValue[] registers, in WebLingoInstruction instruction)
        {
            var arguments = new WebLingoValue[instruction.D];
            for (var i = 0; i < arguments.Length; i++) arguments[i] = registers[instruction.C + i];
            try
            {
                if (!builtin.TryInvoke(arguments, heap, out var result, out var error))
                {
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
