using System.Runtime.CompilerServices;
using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.VM.Stack;

/// <summary>
/// The stack VM (specification, section 5.6, "The comparison's parts", part 3), which runs the stack bytecode of
/// <see cref="WebLingoStackModule"/> so that it can be compared with the register VM, <see cref="WebLingoVirtualMachine"/>, before
/// the choice between them is made (WL-BC002, provisional). It shares everything but its instructions with the
/// register VM: the runtime's values, operators, heap and built-ins (<c>WebLingo.Runtime</c>), the execution context
/// and its budgets, the result (<see cref="WebLingoExecutionResult"/>), and the messages of its errors, so that a program
/// gives the same output and the same errors on both. Its instruction budget counts its own instructions, which are
/// more than the register VM's for the same program.
/// <para>
/// It is tuned as the register VM is (section 5.6): one loop runs every instruction, with the running frame's code,
/// slots, stack top and position in locals; the budget and the cancellation signal are looked at only when they come
/// due; int64 and binary64 arithmetic and comparisons are done in the loop, the rest and every error by
/// <see cref="WebLingoOperators"/>; and a call reuses the frame, and the slots, that the last call at its depth left. A
/// frame's slots hold its locals, then its operand stack, and are a root of the execution's heap.
/// </para>
/// </summary>
public sealed class WebLingoStackVirtualMachine
{
    /// <summary>How many instructions run between two looks at the cancellation signal, as in the register VM.</summary>
    public const int CancellationInterval = WebLingoVirtualMachine.CancellationInterval;

    private readonly WebLingoStackBytecodeVerifier _verifier = new();

    public WebLingoExecutionResult Execute(WebLingoStackModule module, WebLingoExecutionContext context)
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
            if (function.SlotCount > largest.SlotCount) largest = function;
        }
        if (largest.SlotCount > context.Resources.MaxRegisters)
        {
            return Failure(WebLingoDiagnosticCodes.BudgetExceeded,
                $"'{largest.Name}' needs {largest.SlotCount} slots for its locals and its stack; " +
                $"this execution allows {context.Resources.MaxRegisters}.", new WebLingoSourceSpan(0, 0));
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

        var codes = new WebLingoStackInstruction[module.Functions.Count][];
        for (var i = 0; i < codes.Length; i++) codes[i] = ToArray(module.Functions[i].Code);

        return new Execution(module, ToArray(module.EntryPoint.Code), codes, constants, names, builtins, context, heap).Run();
    }

    /// <summary>A copy of the code, without LINQ, which the portable core does not use.</summary>
    private static WebLingoStackInstruction[] ToArray(IReadOnlyList<WebLingoStackInstruction> code)
    {
        var array = new WebLingoStackInstruction[code.Count];
        for (var i = 0; i < array.Length; i++) array[i] = code[i];
        return array;
    }

    /// <summary>
    /// A function running, at one depth of the VM's stack: its code, its slots (its locals, then its operand stack),
    /// the closure it runs as (a root, which keeps its captured cells alive), and where it goes on when a call it
    /// made returns. A frame, and its slots, are reused by the next call at the same depth; its slots may be more
    /// than the function's, and are null when unused.
    /// </summary>
    private sealed class Frame
    {
        public WebLingoStackFunction Function = null!;
        public WebLingoStackInstruction[] Code = [];
        public WebLingoValue[] Slots = [];
        /// <summary>The closure it runs as, and, for a disposal, the instance disposed: the frame's roots.</summary>
        /// <summary>
        /// The frame's roots: the closure it runs as; for a disposal, the instance disposed; an exception a disposal keeps
        /// while its next methods run (section 3.7); and an <c>async</c> function's task (section 3.8).
        /// </summary>
        public readonly WebLingoValue[] Closure = new WebLingoValue[4];
        public WebLingoValue[] Cells = [];
        /// <summary>The next instruction, while the frame waits for a call it made to return.</summary>
        public int Pc;
        /// <summary>The top of its stack, without the call's callee and arguments, while it waits for the call.</summary>
        public int Sp;
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
        /// For an <c>async</c> function's frame: its task, its fourth root too; and whether it still runs on its caller's
        /// stack, before its first <c>await</c> (section 3.8).
        /// </summary>
        public WebLingoValue Task;
        public bool FirstRun;
        /// <summary>Whether what the frame's await gives is dropped, as a using's await of its disposal's is.</summary>
        public bool AwaitDiscards;
        /// <summary>The roots a waiting frame's task holds: its slots and its closure, made once (section 3.8).</summary>
        public WebLingoValue[][]? WaitingRoots;
        /// <summary>Whether the call's result is dropped, as a using's disposal drops it, rather than pushed.</summary>
        public bool DiscardResult;
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
        /// <summary>The depth its frame's stack is cut to, where its result is pushed.</summary>
        public int Destination { get; } = destination;
        public bool Rooted { get; set; }
    }

    private sealed class Execution(WebLingoStackModule module, WebLingoStackInstruction[] entryCode, WebLingoStackInstruction[][] codes,
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
                Slots = new WebLingoValue[module.EntryPoint.SlotCount]
            };
            heap.AddRoots(_errorClass);
            heap.AddRoots(_objectClass);
            heap.AddRoots(_valueClasses);
            heap.AddRoots(_errorClasses);
            heap.AddRoots(_main);
            _modules = new WebLingoModuleTable(module.Units.Count, heap);
            _unitClosures = new WebLingoValue[module.Units.Count];
            heap.AddRoots(_unitClosures);
            heap.AddRoots(frame.Slots);
            heap.AddRoots(frame.Closure);
            _scheduler = new WebLingoScheduler(heap, context.Clock);
            heap.Scheduler = _scheduler;
            heap.ValueClasses = _valueClasses;
            if (module.EntryPoint.IsAsync && !TryStartMain(frame)) return _ended!;

            var code = frame.Code;
            var slots = frame.Slots;
            var cells = frame.Cells;
            var sp = module.EntryPoint.LocalCount; // The stack starts above the locals.
            var pc = 0;
            var budget = context.InstructionBudget;
            var cancellation = context.Cancellation;
            var maxCallDepth = context.Resources.MaxCallDepth;
            var executed = 0L;
            var dueAt = 0L; // The count of instructions at which the budget and the cancellation signal are looked at.
            WebLingoRuntimeError? error = null;
            // A call being made, which the operations that call set up, and the code after their switch carries out: the
            // value called, the instance a method runs on, where its arguments start in the slots, and, for new, the
            // instance made. The slot below the arguments is where the result goes.
            var callee = WebLingoValue.Null;
            var self = WebLingoValue.Null;
            var firstArgument = 0;
            var argumentCount = 0;
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
                        case WebLingoStackOpCode.LoadConstant:
                            slots[sp++] = constants[ins.A];
                            pc++;
                            continue;
                        case WebLingoStackOpCode.LoadNull:
                            slots[sp++] = WebLingoValue.Null;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.LoadBoolean:
                            slots[sp++] = WebLingoValue.FromBoolean(ins.A != 0);
                            pc++;
                            continue;
                        case WebLingoStackOpCode.LoadLocal:
                            slots[sp++] = slots[ins.A];
                            pc++;
                            continue;
                        case WebLingoStackOpCode.StoreLocal:
                            slots[ins.A] = slots[--sp];
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Duplicate:
                            slots[sp] = slots[sp - 1];
                            sp++;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Pop:
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Negate:
                        {
                            if (!WebLingoOperators.TryNegate(slots[sp - 1], heap, out var negated, out error)) break;
                            slots[sp - 1] = negated;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.Plus:
                        {
                            if (!WebLingoOperators.TryPlus(slots[sp - 1], heap, out var same, out error)) break;
                            slots[sp - 1] = same;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.Not:
                        {
                            if (!WebLingoOperators.TryNot(heap.Held(slots[sp - 1]), out var not, out error)) break;
                            slots[sp - 1] = not;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.Add:
                            if (!Arithmetic(WebLingoStackOpCode.Add, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Subtract:
                            if (!Arithmetic(WebLingoStackOpCode.Subtract, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Multiply:
                            if (!Arithmetic(WebLingoStackOpCode.Multiply, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Divide:
                            if (!Arithmetic(WebLingoStackOpCode.Divide, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Equal:
                            if (!Compare(WebLingoStackOpCode.Equal, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.NotEqual:
                            if (!Compare(WebLingoStackOpCode.NotEqual, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Less:
                            if (!Compare(WebLingoStackOpCode.Less, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.LessOrEqual:
                            if (!Compare(WebLingoStackOpCode.LessOrEqual, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Greater:
                            if (!Compare(WebLingoStackOpCode.Greater, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.GreaterOrEqual:
                            if (!Compare(WebLingoStackOpCode.GreaterOrEqual, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Jump:
                            pc = ins.A;
                            continue;
                        case WebLingoStackOpCode.JumpIfFalse:
                        {
                            var condition = slots[--sp];
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
                            pc = condition.AsBoolean ? pc + 1 : ins.A;
                            continue;
                        }
                        case WebLingoStackOpCode.JumpIfGiven:
                        {
                            // Whether the caller gave a parameter, which, captured, is in a cell (section 3.5).
                            var parameter = slots[ins.B];
                            if (parameter.Kind == WebLingoValueKind.Cell) heap.TryReadCell(parameter, out parameter);
                            pc = parameter.Kind == WebLingoValueKind.Missing ? pc + 1 : ins.A;
                            continue;
                        }
                        case WebLingoStackOpCode.JumpIfTrue:
                        {
                            var condition = slots[--sp];
                            if (condition.Kind != WebLingoValueKind.Boolean)
                            {
                                condition = heap.Held(condition);
                                if (condition.Kind != WebLingoValueKind.Boolean)
                                {
                                    error = WebLingoOperators.CheckBoolean(condition, "A condition");
                                    break;
                                }
                            }
                            pc = condition.AsBoolean ? ins.A : pc + 1;
                            continue;
                        }
                        case WebLingoStackOpCode.CheckBoolean:
                            if (slots[sp - 1].Kind != WebLingoValueKind.Boolean && heap.Held(slots[sp - 1]).Kind != WebLingoValueKind.Boolean)
                            {
                                error = WebLingoOperators.CheckBoolean(heap.Held(slots[sp - 1]), "'&&' and '||' need Booleans:");
                                break;
                            }
                            pc++;
                            continue;
                        case WebLingoStackOpCode.NewCell:
                            if (!heap.TryAllocateCell(WebLingoValue.Null, false, out slots[ins.A], out error)) break;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.BoxLocal:
                        {
                            if (!heap.TryAllocateCell(slots[ins.A], true, out var boxed, out error)) break;
                            slots[ins.A] = boxed;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.CopyCell:
                        {
                            var hasValue = heap.TryReadCell(slots[ins.A], out var held);
                            if (!heap.TryAllocateCell(held, hasValue, out var copy, out error)) break;
                            slots[ins.A] = copy;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.LoadCell:
                            error = ReadCell(slots[ins.A], frame.Function, ins.A, out slots[sp]);
                            if (error is not null) break;
                            sp++;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.StoreCell:
                            heap.WriteCell(slots[ins.A], slots[--sp]);
                            pc++;
                            continue;
                        case WebLingoStackOpCode.LoadCapture:
                            error = ReadCell(cells[ins.A], null, ins.A, out slots[sp]);
                            if (error is not null) break;
                            sp++;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.StoreCapture:
                            heap.WriteCell(cells[ins.A], slots[--sp]);
                            pc++;
                            continue;
                        case WebLingoStackOpCode.MakeClosure:
                        {
                            if (!TryMakeClosure(ins.A, slots, cells, out slots[sp], out error)) break;
                            sp++;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.LoadBuiltin:
                            slots[sp++] = WebLingoValue.FromHandle(WebLingoValueKind.Builtin, ins.A);
                            pc++;
                            continue;
                        case WebLingoStackOpCode.CallBuiltin:
                        {
                            if (_texts is not null && _texts[ins.A] && ins.B == 1 && NeedsText(slots[sp - 1]))
                            {
                                // A built-in that writes text, of a value that may hold an instance whose toString() must run.
                                _text = new TextCall(new WebLingoTextBuilder(heap, slots[sp - 1], false), builtins[ins.A], sp - 1);
                                goto text;
                            }
                            error = CallBuiltin(builtins[ins.A], slots, sp - ins.B, ins.B, out var result);
                            if (error is not null) break;
                            sp -= ins.B;
                            slots[sp++] = result;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.Call:
                            // Below the arguments, the callee must be a function taking A of them; the compiler checks
                            // what it can, and the call, after this switch, the rest (WL2005, WL2006).
                            argumentCount = ins.A;
                            firstArgument = sp - argumentCount;
                            callee = slots[firstArgument - 1];
                            self = WebLingoValue.Missing; // No receiver: a function with one takes it as its first argument.
                            constructed = WebLingoValue.Null;
                            goto call;
                        case WebLingoStackOpCode.GetMember:
                        {
                            var name = names[ins.A];
                            if (!WebLingoMembers.TryGet(slots[sp - 1], name.Member, name.Text, heap, ins.B != 0, out var member, out error))
                            {
                                sp = PendAccessor(error, member, slots[sp - 1], slots, sp, 1, false);
                                break;
                            }
                            slots[sp - 1] = member;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.SetMember:
                        {
                            var value = slots[sp - 1];
                            if (!WebLingoMembers.TrySet(slots[sp - 2], names[ins.A].Text, value, heap, out error, out callee))
                            {
                                // A setter gives the value assigned, which an assignment whose value is not kept drops.
                                sp = PendAccessor(error, callee, slots[sp - 2], slots, sp, 2, ins.B == 0);
                                break;
                            }
                            sp -= 2;
                            if (ins.B == 1) slots[sp++] = value;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.CallMethod:
                        {
                            var name = names[ins.A];
                            var first = sp - ins.B;
                            var target = slots[first - 1];
                            if (target.HasClass && target.Kind is WebLingoValueKind.Array or WebLingoValueKind.Map or WebLingoValueKind.Set
                                && WebLingoMembers.TryFindCollectionCallee(target, name.Text, heap, out callee, out var collectionMethod))
                            {
                                // A collection of a class extending Array, Map or Set: its field's function, or its class's
                                // method, before its own members (section 3.7).
                                self = collectionMethod ? target : WebLingoValue.Missing;
                                firstArgument = first;
                                argumentCount = ins.B;
                                constructed = WebLingoValue.Null;
                                goto call;
                            }
                            if (target.Kind is WebLingoValueKind.Class or WebLingoValueKind.Module)
                            {
                                // A static member, called as any function is (section 3.7).
                                if (WebLingoMembers.TryGetStaticCallee(target, name.Member, name.Text, heap, out callee, out error))
                                {
                                    self = WebLingoValue.Missing;
                                    firstArgument = first;
                                    argumentCount = ins.B;
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
                                firstArgument = first;
                                argumentCount = ins.B;
                                constructed = WebLingoValue.Null;
                                if (isMethod && name.Member == WebLingoMember.Dispose) goto dispose;
                                goto call;
                            }
                            if (WebLingoMembers.TryGetValueMethod(target, name.Member, name.Text, heap, out var objectMethod))
                            {
                                // Every value has Object's toString() and equal() (section 3.7).
                                callee = objectMethod;
                                self = target;
                                firstArgument = first;
                                argumentCount = ins.B;
                                constructed = WebLingoValue.Null;
                                goto call;
                            }
                            if (!WebLingoMembers.TryCall(target, name.Member, name.Text, slots, first, ins.B, heap, out var result, out error))
                            {
                                break;
                            }
                            sp = first;
                            slots[sp - 1] = result;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.MakeClass:
                        {
                            var baseClass = ins.B == 1 ? slots[sp - 1] : WebLingoValue.Null;
                            if (!TryMakeClass(module.Classes[ins.A], baseClass, slots, cells, out var made, out error)) break;
                            sp -= ins.B;
                            slots[sp++] = made;
                            if (_builtin[ins.A]) Remember(ins.A, made);
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.RegisterUnit:
                            _unitClosures[ins.A] = slots[--sp];
                            pc++;
                            continue;
                        case WebLingoStackOpCode.MakeModule:
                            if (!TryMakeModule(ins.A, slots, sp - ins.B, ins.B, out slots[sp - ins.B], out error)) break;
                            sp -= ins.B - 1;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Import:
                            // A file's instance, for the values given its parameters; else its function runs, as a call, whose
                            // task the code awaits, or the values are wrong (section 3.9).
                            sp = Imported(ins.A, ins.B, slots, sp, out error);
                            if (error is null && _operator.Kind != WebLingoValueKind.Function)
                            {
                                pc++;
                                continue;
                            }
                            break;
                        case WebLingoStackOpCode.Hold:
                            // hold(this, value): the built-in classes' constructors' (section 3.7).
                            if (!heap.TrySetHeld(slots[sp - 2], slots[sp - 1], out error)) break;
                            sp -= 2;
                            slots[sp++] = WebLingoValue.Null;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.Extend:
                            // extend: the class given its extension's methods and static members, then popped (section 3.7).
                            if (!TryExtendClass(module.Classes[ins.A], slots[sp - 1], slots, cells, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WebLingoStackOpCode.GetSuper:
                        {
                            if (!WebLingoMembers.TryGetSuper(slots[sp - 2], slots[sp - 1], names[ins.A].Text, heap, out var bound, out error))
                            {
                                sp = PendAccessor(error, bound, slots[sp - 1], slots, sp, 2, false);
                                break;
                            }
                            sp--;
                            slots[sp - 1] = bound;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.New:
                        {
                            // A new instance; its constructor, if its class has one, runs on it as a call, whose result
                            // is the instance (section 3.7).
                            var first = sp - ins.A;
                            var classValue = slots[first - 1];
                            if (classValue.Kind != WebLingoValueKind.Class)
                            {
                                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NotAFunction,
                                    $"{Capitalized(WebLingoValueFormatter.Describe(classValue, heap))} is not a class, and cannot be made with 'new'.");
                                break;
                            }
                            if (heap.MakesPlainValue(classValue, ins.A))
                            {
                                // new Integer(42) is 42, and new Integer() 0 (section 3.7).
                                if (!heap.TryMakeValue(classValue, slots, first, ins.A, out slots[first - 1], out error)) break;
                                sp = first;
                                pc++;
                                continue;
                            }
                            if (!heap.TryAllocateInstance(classValue, out var instance, out error)) break;
                            callee = heap.ConstructorFor(classValue);
                            if (callee.Kind == WebLingoValueKind.Null)
                            {
                                if (ins.A != 0)
                                {
                                    error = WrongArgumentCount(heap.ClassName(classValue), 0, ins.A);
                                    break;
                                }
                                sp = first;
                                slots[sp - 1] = instance;
                                pc++;
                                continue;
                            }
                            self = instance;
                            constructed = instance;
                            firstArgument = first;
                            argumentCount = ins.A;
                            goto call;
                        }
                        case WebLingoStackOpCode.NewArray:
                        {
                            var first = sp - ins.A;
                            if (!heap.TryAllocateArray(slots, first, ins.A, out var array, out error)) break;
                            sp = first;
                            slots[sp++] = array;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.NewSet:
                        {
                            var first = sp - ins.A;
                            if (!heap.TryAllocateSet(slots, first, ins.A, out var set, out error)) break;
                            sp = first;
                            slots[sp++] = set;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.NewMap:
                        {
                            var first = sp - 2 * ins.A;
                            if (!heap.TryAllocateMap(slots, first, ins.A, out var map, out error)) break;
                            sp = first;
                            slots[sp++] = map;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.ForEachNext:
                        case WebLingoStackOpCode.ForEachNextEntry:
                        {
                            var items = ins.OpCode == WebLingoStackOpCode.ForEachNextEntry ? 2 : 1;
                            if (!WebLingoMembers.TryNext(slots[ins.B], slots[ins.B + 1], items, slots, ins.B + 2, heap,
                                    out var next, out var done, out error))
                            {
                                break;
                            }
                            slots[ins.B + 1] = next;
                            pc = done ? ins.A : pc + 1;
                            continue;
                        }
                        case WebLingoStackOpCode.GetIndex:
                        {
                            if (!WebLingoMembers.TryGetIndex(slots[sp - 2], slots[sp - 1], heap, ins.A != 0, out var element, out error))
                            {
                                break;
                            }
                            sp--;
                            slots[sp - 1] = element;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.StoreIndex:
                        {
                            var value = slots[sp - 1];
                            if (!WebLingoMembers.TrySetIndex(slots[sp - 3], slots[sp - 2], value, heap, out error)) break;
                            sp -= 3;
                            if (ins.A == 1) slots[sp++] = value;
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.Dispose:
                        {
                            // A using's end: its resource, popped, disposed, unless it is null, as x.dispose() disposes
                            // it, the result dropped (section 3.7).
                            var resource = slots[sp - 1];
                            if (resource.Kind == WebLingoValueKind.Null)
                            {
                                sp--;
                                pc++;
                                continue;
                            }
                            error = BeginDisposal(resource, 0, frame.Function, out var chain);
                            if (error is not null) break;
                            if (chain is null)
                            {
                                sp--;
                                pc++;
                                continue;
                            }
                            if (_asyncDisposal && !heap.TryAllocateTask(out _newTask, out error)) break;
                            if (depth + 1 == frames.Length) Array.Resize(ref frames, frames.Length * 2);
                            Disposing(frames, depth + 1, chain, resource).DiscardResult = true;
                            callee = chain[0];
                            self = resource;
                            firstArgument = sp;
                            argumentCount = 0;
                            constructed = WebLingoValue.Null;
                            goto call;
                        }
                        case WebLingoStackOpCode.IsInstance:
                        {
                            var type = slots[sp - 1];
                            if (type.Kind != WebLingoValueKind.Class && TryGetEqual(type, out var equalMethod))
                            {
                                // A name that is not a class: type.equal(value) gives the answer (section 3.7). The value
                                // moves up to be the argument, and the result goes where the value was.
                                slots[sp - 1] = slots[sp - 2];
                                callee = equalMethod;
                                self = type;
                                firstArgument = sp - 1;
                                argumentCount = 1;
                                constructed = WebLingoValue.Null;
                                goto call;
                            }
                            sp--;
                            slots[sp - 1] = WebLingoValue.FromBoolean(WebLingoMembers.IsInstance(slots[sp - 1], type, heap, _valueClasses));
                            pc++;
                            continue;
                        }
                        case WebLingoStackOpCode.ToText:
                            _text = new TextCall(new WebLingoTextBuilder(heap, slots[sp - 1], true), null, sp - 1);
                            goto text;
                        case WebLingoStackOpCode.Await:
                            _awaited = slots[--sp];
                            frame.Sp = sp;
                            frame.Pc = pc + 1;
                            goto awaiting;
                        case WebLingoStackOpCode.Throw:
                            _thrown = slots[--sp];
                            _thrownError = null;
                            _origin = OriginOf(frame.Function, pc);
                            goto throwing;
                        case WebLingoStackOpCode.Rethrow:
                        {
                            // 'throw;', or a finally's end: the exception again, from where it was first thrown.
                            var origin = slots[--sp];
                            _thrown = slots[--sp];
                            _thrownError = null;
                            _origin = origin.Kind == WebLingoValueKind.Int64 ? origin.AsInt64 : OriginOf(frame.Function, pc);
                            goto throwing;
                        }
                        case WebLingoStackOpCode.Return:
                        case WebLingoStackOpCode.ReturnValue:
                        {
                            var result = ins.OpCode == WebLingoStackOpCode.ReturnValue ? slots[--sp] : WebLingoValue.Null;
                            if (depth == 0 && frame.Task.Kind != WebLingoValueKind.Task) return Finish(executed);
                            heap.RemoveLastRoots(frame.Closure);
                            heap.RemoveLastRoots(slots);
                            Array.Clear(slots, 0, frame.Function.SlotCount); // So that nothing stays reachable.
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

                            frame = frames[--depth];
                            code = frame.Code;
                            slots = frame.Slots;
                            cells = frame.Cells;
                            sp = frame.Sp;
                            pc = frame.Pc;
                            slots[sp++] = result;
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
                    // in its first locals, after the instance it runs on if it is a method (section 3.7). The callee,
                    // or the target, or the class, below the arguments, is where the result goes.
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
                        if (_texts is not null && _texts[callee.Handle] && argumentCount == 1 && NeedsText(slots[firstArgument]))
                        {
                            _text = new TextCall(new WebLingoTextBuilder(heap, slots[firstArgument], false), builtin, firstArgument - 1);
                            goto text;
                        }
                        error = CallBuiltin(builtin, slots, firstArgument, argumentCount, out var builtinResult);
                        if (error is not null) goto failed;
                        sp = firstArgument - 1;
                        slots[sp++] = builtinResult;
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
                        frame.Sp = firstArgument - 1;
                        if (++depth == frames.Length) Array.Resize(ref frames, frames.Length * 2);
                        var called = frames[depth] ??= new Frame();
                        if (called.Slots.Length < function.SlotCount) called.Slots = new WebLingoValue[function.SlotCount];
                        var calledSlots = called.Slots;
                        if (withThis == 1) calledSlots[0] = self;
                        for (var i = 0; i < argumentCount; i++) calledSlots[withThis + i] = slots[firstArgument + i];
                        // The parameters left out are missing, until the function's code gives them their defaults.
                        for (var i = withThis + argumentCount; i < function.ParameterCount; i++) calledSlots[i] = WebLingoValue.Missing;
                        called.Function = function;
                        called.Code = codes[index];
                        called.Cells = closureCells;
                        called.Closure[0] = callee;
                        called.Constructed = constructed;
                        if (function.IsAsync) Started(called);
                        heap.AddRoots(calledSlots);
                        heap.AddRoots(called.Closure);

                        frame = called;
                        code = called.Code;
                        slots = calledSlots;
                        cells = closureCells;
                        sp = function.LocalCount;
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
                            sp = firstArgument - 1;
                            slots[sp++] = WebLingoValue.Null;
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
                        var discard = frame.DiscardResult;
                        frame.DiscardResult = false;
                        var held = frame.Holds;
                        if (held) TakeHeld(frame);
                        frame = frames[--depth];
                        code = frame.Code;
                        slots = frame.Slots;
                        cells = frame.Cells;
                        sp = frame.Sp;
                        if (held)
                        {
                            pc = frame.Pc - 1;
                            goto throwing;
                        }
                        pc = frame.Pc;
                        if (!discard) slots[sp++] = WebLingoValue.Null;
                        continue;
                    }

                nextDisposal:
                    // The instance's next dispose method, in the same frame, whose roots were let go and slots cleared;
                    // its result is dropped.
                    {
                        var next = frame.DisposeChain![frame.DisposeNext++];
                        var (nextIndex, nextCells) = heap.GetClosure(next);
                        var nextFunction = module.Functions[nextIndex];
                        if (slots.Length < nextFunction.SlotCount) frame.Slots = slots = new WebLingoValue[nextFunction.SlotCount];
                        slots[0] = frame.Closure[1];
                        frame.Function = nextFunction;
                        frame.Code = code = codes[nextIndex];
                        frame.Cells = cells = nextCells;
                        frame.Closure[0] = next;
                        heap.AddRoots(slots);
                        heap.AddRoots(frame.Closure);
                        sp = nextFunction.LocalCount;
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
                            firstArgument = building.Destination + 1;
                            argumentCount = 0;
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
                        sp = building.Destination;
                        slots[sp++] = written;
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
                        slots = frame.Slots;
                        cells = frame.Cells;
                        sp = frame.Sp;
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
                    slots = frame.Slots;
                    cells = frame.Cells;
                    sp = frame.Sp;
                    if (_text is not null) goto textGiven;
                    if (_usingAwaits)
                    {
                        // A using awaits its async disposal, as await would, whatever it gives dropped (section 3.8).
                        _usingAwaits = false;
                        frame.AwaitDiscards = true;
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
                    slots = frame.Slots;
                    cells = frame.Cells;
                    sp = frame.Sp;
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
                        // A class's operator, which the operation found: called with its two operands, the slot below them
                        // made the callee's, as a call's is, its frame given one more slot if it has none (section 3.7).
                        callee = _operator;
                        _operator = WebLingoValue.Null;
                        if (sp == slots.Length) slots = Grown(frame, slots);
                        sp = BelowOperands(slots, sp, _operatorCount, callee);
                        // Or another call it left here: a property's getter or setter, on its target.
                        self = _pendingSelf;
                        _pendingSelf = WebLingoValue.Null;
                        if (_discards) frames = Discarding(frames, depth + 1);
                        firstArgument = sp - _operatorCount;
                        argumentCount = _operatorCount;
                        constructed = WebLingoValue.Null;
                        goto call;
                    }
                    // A call that failed before its frame was pushed leaves nothing in that frame: no disposal, no text.
                    if (depth + 1 < frames.Length && frames[depth + 1] is { } unpushed)
                    {
                        unpushed.DisposeChain = null;
                        unpushed.DiscardResult = false;
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
                            sp = frame.Function.LocalCount + handler.Depth;
                            slots[handler.Value] = exception;
                            slots[handler.Origin] = WebLingoValue.FromInt64(_origin);
                            pc = handler.Target;
                            continue;
                        }
                        if (WebLingoExceptions.DisposalGoesOn && frame.DisposeChain is { } chain && frame.DisposeNext < chain.Length)
                        {
                            // Its next dispose method runs; the exception waits for the last, a later one replacing it.
                            Hold(frame);
                            heap.RemoveLastRoots(frame.Closure);
                            heap.RemoveLastRoots(slots);
                            Array.Clear(slots, 0, frame.Function.SlotCount);
                            goto nextDisposal;
                        }
                        if (frame.Task.Kind == WebLingoValueKind.Task) goto faulted;
                        if (depth == 0) return Uncaught(executed);
                        heap.RemoveLastRoots(frame.Closure);
                        heap.RemoveLastRoots(slots);
                        Array.Clear(slots, 0, frame.Function.SlotCount);
                        if (frame.Text is { Rooted: true } waiting) heap.RemoveLastRoots(waiting.Builder.Roots);
                        Release(frame);
                        frame = frames[--depth];
                        code = frame.Code;
                        slots = frame.Slots;
                        cells = frame.Cells;
                        sp = frame.Sp;
                        pc = frame.Pc - 1; // The call it made.
                        goto throwing;
                    }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                                  or IndexOutOfRangeException)
            {
                // Only bytecode the verifier cannot judge (a local of the wrong kind) gets here.
                return Failure(WebLingoDiagnosticCodes.BytecodeVerificationFailure, $"The bytecode is invalid: {exception.Message}", frame.Function, pc) with { Instructions = executed };
            }
        }

        /// <summary>A cell's value; WL2004 if it has none yet, which only a hoisted function can meet (section 3.5).</summary>
        private WebLingoRuntimeError? ReadCell(WebLingoValue cell, WebLingoStackFunction? owner, int local, out WebLingoValue value)
        {
            if (heap.TryReadCell(cell, out value)) return null;
            var name = owner?.LocalNames is { } names && local < names.Count ? $"'{names[local]}'" : "A variable";
            return new WebLingoRuntimeError(WebLingoDiagnosticCodes.ReadBeforeInitialization,
                $"{name} is used by a function called before its declaration has run.");
        }

        /// <summary>
        /// Arithmetic on the two values on top of the stack, whose result replaces the left one: two int64s, without
        /// overflow, or two binary64s, here; anything else, and every error, by <see cref="WebLingoOperators"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Arithmetic(WebLingoStackOpCode opCode, WebLingoValue[] slots, int sp, out WebLingoRuntimeError? error)
        {
            var (left, right) = (slots[sp - 2], slots[sp - 1]);
            error = null;
            if (left.Kind == WebLingoValueKind.Int64 && right.Kind == WebLingoValueKind.Int64)
            {
                var (a, b) = (left.AsInt64, right.AsInt64);
                switch (opCode)
                {
                    case WebLingoStackOpCode.Add:
                    {
                        var sum = unchecked(a + b);
                        if (((a ^ sum) & (b ^ sum)) >= 0) return Done(WebLingoValue.FromInt64(sum), slots, sp);
                        break;
                    }
                    case WebLingoStackOpCode.Subtract:
                    {
                        var difference = unchecked(a - b);
                        if (((a ^ b) & (a ^ difference)) >= 0) return Done(WebLingoValue.FromInt64(difference), slots, sp);
                        break;
                    }
                    case WebLingoStackOpCode.Multiply:
                    {
                        var high = Math.BigMul(a, b, out var low);
                        if (high == low >> 63) return Done(WebLingoValue.FromInt64(low), slots, sp);
                        break;
                    }
                    default:
                        return Done(WebLingoValue.FromBinary64((double)a / b), slots, sp);
                }
            }
            else if (left.Kind == WebLingoValueKind.Binary64 && right.Kind == WebLingoValueKind.Binary64)
            {
                var (x, y) = (left.AsBinary64, right.AsBinary64);
                return Done(WebLingoValue.FromBinary64(opCode switch
                {
                    WebLingoStackOpCode.Add => x + y,
                    WebLingoStackOpCode.Subtract => x - y,
                    WebLingoStackOpCode.Multiply => x * y,
                    _ => x / y
                }), slots, sp);
            }
            if ((left.Kind == WebLingoValueKind.Instance || right.Kind == WebLingoValueKind.Instance || left.HasClass || right.HasClass)
                && TryOperator(opCode, left, right))
            {
                return false;
            }
            if (!WebLingoOperators.TryBinary(ArithmeticOf(opCode), left, right, heap, out var value, out error)) return false;
            return Done(value, slots, sp);
        }

        /// <summary>
        /// A comparison of the two values on top of the stack, whose result replaces the left one: two int64s, or two
        /// binary64s (IEEE 754's, so that NaN is unordered and unequal to itself), here; anything else by
        /// <see cref="WebLingoOperators"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Compare(WebLingoStackOpCode opCode, WebLingoValue[] slots, int sp, out WebLingoRuntimeError? error)
        {
            var (left, right) = (slots[sp - 2], slots[sp - 1]);
            error = null;
            if (left.Kind == WebLingoValueKind.Int64 && right.Kind == WebLingoValueKind.Int64)
            {
                var (a, b) = (left.AsInt64, right.AsInt64);
                return Done(WebLingoValue.FromBoolean(opCode switch
                {
                    WebLingoStackOpCode.Equal => a == b,
                    WebLingoStackOpCode.NotEqual => a != b,
                    WebLingoStackOpCode.Less => a < b,
                    WebLingoStackOpCode.LessOrEqual => a <= b,
                    WebLingoStackOpCode.Greater => a > b,
                    _ => a >= b
                }), slots, sp);
            }
            if (left.Kind == WebLingoValueKind.Binary64 && right.Kind == WebLingoValueKind.Binary64)
            {
                var (x, y) = (left.AsBinary64, right.AsBinary64);
                return Done(WebLingoValue.FromBoolean(opCode switch
                {
                    WebLingoStackOpCode.Equal => x == y,
                    WebLingoStackOpCode.NotEqual => x != y,
                    WebLingoStackOpCode.Less => x < y,
                    WebLingoStackOpCode.LessOrEqual => x <= y,
                    WebLingoStackOpCode.Greater => x > y,
                    _ => x >= y
                }), slots, sp);
            }
            if ((left.Kind == WebLingoValueKind.Instance || right.Kind == WebLingoValueKind.Instance || left.HasClass || right.HasClass)
                && TryOperator(opCode, left, right))
            {
                return false;
            }
            if (!WebLingoOperators.TryCompare(ComparisonOf(opCode), left, right, heap, out var value, out error)) return false;
            return Done(value, slots, sp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Done(WebLingoValue value, WebLingoValue[] slots, int sp)
        {
            slots[sp - 2] = value;
            return true;
        }

        /// <summary>
        /// Begins disposing an instance, for <c>x.dispose()</c> or a using's end (section 3.7): its dispose methods, to run
        /// on it in turn, or <c>null</c> if its disposal has begun already. Disposal gives them no arguments, so a call
        /// with some, or a dispose method that takes some, is WL2006.
        /// </summary>
        private WebLingoRuntimeError? BeginDisposal(WebLingoValue instance, int argumentCount, WebLingoStackFunction? usingIn, out WebLingoValue[]? chain)
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
        private readonly bool[]? _texts = TextBuiltins(module, builtins);

        /// <summary>The execution's tasks (section 3.8), made when it starts; the program's own task, if its top level awaits, a root.</summary>
        private WebLingoScheduler _scheduler = null!;
        private readonly WebLingoValue[] _main = new WebLingoValue[1];

        /// <summary>
        /// A class's operator that an arithmetic or a comparison found (section 3.7), with its operands: called as a
        /// function, from where the operation's failure leads.
        /// </summary>
        private WebLingoValue _operator;
        private readonly WebLingoValue[] _operands = new WebLingoValue[2];
        private int _operatorCount = 2;

        /// <summary>A call that an operation leaves to where its failure leads: a file's function, which an import runs.</summary>
        private void Pend(WebLingoValue callee, int count)
        {
            _operator = callee;
            _operatorCount = count;
        }

        /// <summary>What a pending call runs on, a property's target; and whether its result is dropped (section 3.7).</summary>
        private WebLingoValue _pendingSelf;
        private bool _discards;

        /// <summary>
        /// A property's getter, or its setter, that a member's read or assignment failed with
        /// (<see cref="WebLingoMembers.Getter"/>, <see cref="WebLingoMembers.Setter"/>): pending, called on
        /// <paramref name="self"/>, the operation's <paramref name="consumed"/> operands on top of the stack giving way
        /// to the setter's argument, the value on top; the stack's new top, or its old one for another failure
        /// (section 3.7).
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private int PendAccessor(WebLingoRuntimeError? error, WebLingoValue accessor, WebLingoValue self, WebLingoValue[] slots, int sp,
            int consumed, bool discards)
        {
            var setter = ReferenceEquals(error, WebLingoMembers.Setter);
            if (!setter && !ReferenceEquals(error, WebLingoMembers.Getter)) return sp;
            Pend(accessor, setter ? 1 : 0);
            _pendingSelf = self;
            _discards = discards;
            if (setter) slots[sp - consumed] = slots[sp - 1];
            return sp - consumed + _operatorCount;
        }

        /// <summary>
        /// Makes the frame at <paramref name="depth"/>, which the call about to be made runs in, drop its result: a
        /// disposal with no methods left, which pushes nothing when it returns; the frames, grown if it is past them.
        /// </summary>
        private Frame[] Discarding(Frame[] frames, int depth)
        {
            _discards = false;
            if (depth == frames.Length) Array.Resize(ref frames, frames.Length * 2);
            var frame = frames[depth] ??= new Frame();
            frame.DisposeChain = [];
            frame.DisposeNext = 0;
            frame.DiscardResult = true;
            return frames;
        }

        /// <summary>The callee put below the call's operands on the stack, as a call's callee is, the stack one slot deeper.</summary>
        private static int BelowOperands(WebLingoValue[] slots, int sp, int count, WebLingoValue callee)
        {
            for (var i = 0; i < count; i++) slots[sp - i] = slots[sp - i - 1];
            slots[sp - count] = callee;
            return sp + 1;
        }

        /// <summary>The name of the operator a class may declare for an operation, or null for one it may not.</summary>
        private static string? OperatorName(WebLingoStackOpCode opCode) => opCode switch
        {
            WebLingoStackOpCode.Add => "operator+",
            WebLingoStackOpCode.Subtract => "operator-",
            WebLingoStackOpCode.Multiply => "operator*",
            WebLingoStackOpCode.Divide => "operator/",
            WebLingoStackOpCode.Less => "operator<",
            WebLingoStackOpCode.LessOrEqual => "operator<=",
            WebLingoStackOpCode.Greater => "operator>",
            WebLingoStackOpCode.GreaterOrEqual => "operator>=",
            _ => null
        };

        /// <summary>Whether an operation on two values calls a class's operator (section 3.7), which it then keeps.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryOperator(WebLingoStackOpCode opCode, WebLingoValue left, WebLingoValue right)
        {
            if (OperatorName(opCode) is not { } name || !WebLingoMembers.TryFindOperator(name, left, right, heap, out _operator)) return false;
            _operands[0] = left;
            _operands[1] = right;
            _operatorCount = 2;
            return true;
        }

        /// <summary>The running frame's slots, one more, its roots registered anew, for an operator's call.</summary>
        private WebLingoValue[] Grown(Frame frame, WebLingoValue[] slots)
        {
            heap.RemoveLastRoots(frame.Closure);
            heap.RemoveLastRoots(slots);
            var grown = new WebLingoValue[slots.Length + 1];
            Array.Copy(slots, grown, slots.Length);
            frame.Slots = grown;
            heap.AddRoots(grown);
            heap.AddRoots(frame.Closure);
            return grown;
        }

        /// <summary>
        /// The program's files' instances, once each has begun to run, and the closures of their functions, which run them
        /// (section 3.9); roots.
        /// </summary>
        private WebLingoModuleTable _modules = null!;
        private WebLingoValue[] _unitClosures = [];

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
        /// <c>Import</c>: a file's instance pushed, in place of the values given its parameters (with
        /// <paramref name="hasValues"/> 1, on top of the stack), if it has begun to run for them; else its function's call
        /// pending, the values left where they are, its one argument, if it has parameters; the stack's new top.
        /// </summary>
        private int Imported(int unit, int hasValues, WebLingoValue[] slots, int sp, out WebLingoRuntimeError? error)
        {
            var info = module.Units[unit];
            var values = hasValues == 1 ? slots[sp - 1] : WebLingoValue.Null;
            if (!_modules.TryFind(unit, info.Name, info.Parameters, values, out var instance, out error)) return sp;
            if (instance.Kind == WebLingoValueKind.Module)
            {
                if (hasValues == 1) slots[sp - 1] = instance;
                else slots[sp++] = instance;
                return sp;
            }
            if (hasValues == 1 && info.Parameters.Count == 0) sp--; // An empty map, for a file without parameters.
            Pend(_unitClosures[unit], info.Parameters.Count > 0 ? 1 : 0);
            return sp;
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
            heap.RemoveLastRoots(frame.Slots);
            Array.Clear(frame.Slots, 0, frame.Function.SlotCount);
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
            heap.RemoveLastRoots(frame.Slots);
            var waiting = heap.GetTask(frame.Task);
            waiting.Frame = frame;
            var roots = frame.WaitingRoots;
            if (roots is null || roots[0] != frame.Slots) frame.WaitingRoots = roots = [frame.Slots, frame.Closure];
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
                if (!frame.DiscardResult)
                {
                    caller.Slots[caller.Sp++] = _returned;
                }
                else
                {
                    // A using's disposal: the using awaits it.
                    frame.DiscardResult = false;
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
                    heap.AddRoots(frame.Slots);
                    heap.AddRoots(frame.Closure);
                    _resumedThrowing = resumption.Throws;
                    var discards = frame.AwaitDiscards;
                    frame.AwaitDiscards = false;
                    if (resumption.Throws) (_thrown, _thrownError, _origin) = (resumption.Value, resumption.Error, resumption.Origin);
                    // A timeout's error, which no instruction raised, is thrown from the await it reaches (section 3.8).
                    if (resumption.Throws && _origin == WebLingoExceptions.Nowhere) _origin = OriginOf(frame.Function, frame.Pc - 1);
                    else if (!discards) frame.Slots[frame.Sp++] = resumption.Value;
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

        private static bool[]? TextBuiltins(WebLingoStackModule module, IWebLingoBuiltinFunction[] builtins)
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
        private readonly WebLingoValue[] _errorClasses = new WebLingoValue[module.ErrorClasses.Count];

        /// <summary>For each class of the module, whether it is a built-in one that the VM keeps when it makes it.</summary>
        private readonly bool[] _builtin = BuiltinClasses(module);

        private static bool[] BuiltinClasses(WebLingoStackModule module)
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

        private long OriginOf(WebLingoStackFunction function, int pc)
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
        private WebLingoStackFunction? OwnerOfOrigin(long origin)
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
        private static int FindHandler(IReadOnlyList<WebLingoStackHandler> handlers, int pc)
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
            frame.DiscardResult = false;
        }

        private static WebLingoRuntimeError WrongArgumentCount(string name, int arity, int count, int optional = 0) =>
            new(WebLingoDiagnosticCodes.WrongArgumentCount, optional == 0
                ? $"'{name}' takes {arity} {(arity == 1 ? "argument" : "arguments")}, not {count}."
                : $"'{name}' takes {arity - optional} to {arity} arguments, not {count}.");

        private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        /// <summary>
        /// Calls a built-in with the <paramref name="count"/> arguments from slot <paramref name="first"/> on, copied out
        /// of the slots. Its failure, even an exception, is a runtime error.
        /// </summary>
        /// <summary>
        /// A closure of the module's function, its captures taken from the making function's locals or its own
        /// captures, as the function's capture table says.
        /// </summary>
        private bool TryMakeClosure(int function, WebLingoValue[] slots, WebLingoValue[] cells, out WebLingoValue closure, out WebLingoRuntimeError? error)
        {
            var captures = module.Functions[function].Captures;
            var captured = new WebLingoValue[captures.Count];
            for (var i = 0; i < captured.Length; i++)
            {
                captured[i] = captures[i].FromLocal ? slots[captures[i].Index] : cells[captures[i].Index];
                if (captured[i].Kind != WebLingoValueKind.Cell) throw new InvalidOperationException("A capture is not a cell.");
            }
            return heap.TryAllocateClosure(function, captured, out closure, out error);
        }

        /// <summary>
        /// A class value: its constructor's and its methods' closures made now, kept as roots until the class holds
        /// them, since making each may collect (section 3.7).
        /// </summary>
        /// <summary>
        /// <c>extend</c> (section 3.7): an extension's methods' and static methods' closures, made as their captures say,
        /// given to the class <paramref name="target"/>, with its static fields' names.
        /// </summary>
        private bool TryExtendClass(WebLingoClassInfo info, WebLingoValue target, WebLingoValue[] slots, WebLingoValue[] cells, out WebLingoRuntimeError? error)
        {
            var closures = new WebLingoValue[info.Methods.Count + info.StaticMethods.Count];
            heap.AddRoots(closures);
            try
            {
                var methods = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.Methods.Count; i++)
                {
                    if (!TryMakeClosure(info.Methods[i].Function, slots, cells, out closures[i], out error)) return false;
                    methods[info.Methods[i].Name] = closures[i];
                }
                var statics = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.StaticMethods.Count; i++)
                {
                    var slot = info.Methods.Count + i;
                    if (!TryMakeClosure(info.StaticMethods[i].Function, slots, cells, out closures[slot], out error)) return false;
                    statics[info.StaticMethods[i].Name] = closures[slot];
                }
                return heap.TryExtendClass(target, methods, statics, info.StaticFields, out error);
            }
            finally
            {
                heap.RemoveLastRoots(closures);
            }
        }

        private bool TryMakeClass(WebLingoClassInfo info, WebLingoValue baseClass, WebLingoValue[] slots, WebLingoValue[] cells, out WebLingoValue made,
            out WebLingoRuntimeError? error)
        {
            made = WebLingoValue.Null;
            var closures = new WebLingoValue[info.Methods.Count + info.StaticMethods.Count + 1];
            heap.AddRoots(closures);
            try
            {
                if (info.Constructor != -1 && !TryMakeClosure(info.Constructor, slots, cells, out closures[0], out error)) return false;
                var methods = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.Methods.Count; i++)
                {
                    if (!TryMakeClosure(info.Methods[i].Function, slots, cells, out closures[i + 1], out error)) return false;
                    methods.Add(info.Methods[i].Name, closures[i + 1]);
                }
                var statics = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.StaticMethods.Count; i++)
                {
                    var slot = info.Methods.Count + 1 + i;
                    if (!TryMakeClosure(info.StaticMethods[i].Function, slots, cells, out closures[slot], out error)) return false;
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

        private WebLingoRuntimeError? CallBuiltin(IWebLingoBuiltinFunction builtin, WebLingoValue[] slots, int first, int count,
            out WebLingoValue result)
        {
            result = WebLingoValue.Null;
            var arguments = new WebLingoValue[count];
            for (var i = 0; i < arguments.Length; i++) arguments[i] = slots[first + i];
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
    }

    private static WebLingoArithmeticOperator ArithmeticOf(WebLingoStackOpCode opCode) => opCode switch
    {
        WebLingoStackOpCode.Add => WebLingoArithmeticOperator.Add,
        WebLingoStackOpCode.Subtract => WebLingoArithmeticOperator.Subtract,
        WebLingoStackOpCode.Multiply => WebLingoArithmeticOperator.Multiply,
        _ => WebLingoArithmeticOperator.Divide
    };

    private static WebLingoComparisonOperator ComparisonOf(WebLingoStackOpCode opCode) => opCode switch
    {
        WebLingoStackOpCode.Equal => WebLingoComparisonOperator.Equal,
        WebLingoStackOpCode.NotEqual => WebLingoComparisonOperator.NotEqual,
        WebLingoStackOpCode.Less => WebLingoComparisonOperator.Less,
        WebLingoStackOpCode.LessOrEqual => WebLingoComparisonOperator.LessOrEqual,
        WebLingoStackOpCode.Greater => WebLingoComparisonOperator.Greater,
        _ => WebLingoComparisonOperator.GreaterOrEqual
    };

    private static WebLingoSourceSpan SpanOf(WebLingoStackFunction function, int pc) =>
        function.Spans is { } spans && pc < spans.Count ? spans[pc] : new WebLingoSourceSpan(0, 0);

    private static WebLingoExecutionResult Failure(string code, string message, WebLingoSourceSpan span) =>
        new(false, new WebLingoDiagnostic(code, WebLingoDiagnosticSeverity.Error, message, span));

    /// <summary>A failure at an instruction of a function, naming the file the function is in, if not the program's (section 3.9).</summary>
    private static WebLingoExecutionResult Failure(string code, string message, WebLingoStackFunction function, int pc) =>
        new(false, new WebLingoDiagnostic(code, WebLingoDiagnosticSeverity.Error, message, SpanOf(function, pc)) { Source = function.Source });
}
