using System.Runtime.CompilerServices;
using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.VM.Stack;

/// <summary>
/// The stack VM (specification, section 5.6, "The comparison's parts", part 3), which runs the stack bytecode of
/// <see cref="WsStackModule"/> so that it can be compared with the register VM, <see cref="WsVirtualMachine"/>, before
/// the choice between them is made (WS-BC002, provisional). It shares everything but its instructions with the
/// register VM: the runtime's values, operators, heap and built-ins (<c>WebLingo.Runtime</c>), the execution context
/// and its budgets, the result (<see cref="WsExecutionResult"/>), and the messages of its errors, so that a program
/// gives the same output and the same errors on both. Its instruction budget counts its own instructions, which are
/// more than the register VM's for the same program.
/// <para>
/// It is tuned as the register VM is (section 5.6): one loop runs every instruction, with the running frame's code,
/// slots, stack top and position in locals; the budget and the cancellation signal are looked at only when they come
/// due; int64 and binary64 arithmetic and comparisons are done in the loop, the rest and every error by
/// <see cref="WsOperators"/>; and a call reuses the frame, and the slots, that the last call at its depth left. A
/// frame's slots hold its locals, then its operand stack, and are a root of the execution's heap.
/// </para>
/// </summary>
public sealed class WsStackVirtualMachine
{
    /// <summary>How many instructions run between two looks at the cancellation signal, as in the register VM.</summary>
    public const int CancellationInterval = WsVirtualMachine.CancellationInterval;

    private readonly WsStackBytecodeVerifier _verifier = new();

    public WsExecutionResult Execute(WsStackModule module, WsExecutionContext context)
    {
        var verification = _verifier.Verify(module);
        if (!verification.IsValid)
        {
            return Failure(WsDiagnosticCodes.BytecodeVerificationFailure,
                $"The module failed verification: {verification.Error}", new WsSourceSpan(0, 0));
        }

        var largest = module.EntryPoint;
        foreach (var function in module.Functions)
        {
            if (function.SlotCount > largest.SlotCount) largest = function;
        }
        if (largest.SlotCount > context.Resources.MaxRegisters)
        {
            return Failure(WsDiagnosticCodes.BudgetExceeded,
                $"'{largest.Name}' needs {largest.SlotCount} slots for its locals and its stack; " +
                $"this execution allows {context.Resources.MaxRegisters}.", new WsSourceSpan(0, 0));
        }

        var builtins = new IWsBuiltinFunction[module.Imports.Count];
        for (var i = 0; i < builtins.Length; i++)
        {
            var import = module.Imports[i];
            if (!context.Services.TryGetBuiltin(import.Name, out var builtin) || builtin.Arity != import.Arity)
            {
                return Failure(WsDiagnosticCodes.UndefinedIdentifier,
                    $"This host has no built-in '{import.Name}' taking {import.Arity} arguments.", new WsSourceSpan(0, 0));
            }
            builtins[i] = builtin;
        }

        // The execution's own heap (worker-local, section 4.3), whose roots are the constants and the frames.
        var heap = new WsHeap(context.Resources.MaxHeapUnits);
        var constants = new WsValue[module.Constants.Count];
        var names = new WsMemberName[module.Constants.Count]; // Each string constant as a member's name.
        heap.AddRoots(constants);
        for (var i = 0; i < constants.Length; i++)
        {
            var constant = module.Constants[i];
            if (constant.Kind == WsConstantKind.String)
            {
                names[i] = new WsMemberName(WsMembers.Find(constant.Text!), constant.Text!);
                if (!heap.TryAllocateString(constant.Text!, out constants[i], out var error))
                {
                    return Failure(error!.Code, error.Message, new WsSourceSpan(0, 0));
                }
                continue;
            }
            constants[i] = constant.Kind == WsConstantKind.Int64
                ? WsValue.FromInt64(constant.Bits)
                : WsValue.FromBinary64Bits(constant.Bits);
        }

        var codes = new WsStackInstruction[module.Functions.Count][];
        for (var i = 0; i < codes.Length; i++) codes[i] = ToArray(module.Functions[i].Code);

        return new Execution(module, ToArray(module.EntryPoint.Code), codes, constants, names, builtins, context, heap).Run();
    }

    /// <summary>A copy of the code, without LINQ, which the portable core does not use.</summary>
    private static WsStackInstruction[] ToArray(IReadOnlyList<WsStackInstruction> code)
    {
        var array = new WsStackInstruction[code.Count];
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
        public WsStackFunction Function = null!;
        public WsStackInstruction[] Code = [];
        public WsValue[] Slots = [];
        /// <summary>The closure it runs as, and, for a disposal, the instance disposed: the frame's roots.</summary>
        /// <summary>
        /// The frame's roots: the closure it runs as; for a disposal, the instance disposed; and an exception a disposal
        /// keeps while its next methods run (section 3.7).
        /// </summary>
        public readonly WsValue[] Closure = new WsValue[3];
        public WsValue[] Cells = [];
        /// <summary>The next instruction, while the frame waits for a call it made to return.</summary>
        public int Pc;
        /// <summary>The top of its stack, without the call's callee and arguments, while it waits for the call.</summary>
        public int Sp;
        /// <summary>For a constructor run by <c>new</c>: the instance, which is the call's result (section 3.7).</summary>
        public WsValue Constructed;
        /// <summary>
        /// For a disposal: the instance's dispose methods, which run on it in turn in this frame, and the next of them;
        /// the instance is the frame's second root (section 3.7).
        /// </summary>
        public WsValue[]? DisposeChain;
        public int DisposeNext;
        /// <summary>Whether a disposal keeps an exception one of its methods threw, its runtime error if it is one, and where it was thrown.</summary>
        public bool Holds;
        public WsRuntimeError? HeldError;
        public long HeldOrigin;
        /// <summary>For a toString() that a text being built waits for: that text (section 3.7).</summary>
        public TextCall? Text;
        /// <summary>Whether the call's result is dropped, as a using's disposal drops it, rather than pushed.</summary>
        public bool DiscardResult;
    }

    /// <summary>
    /// A text being built (section 3.7), and what it is for: a <c>ToText</c>'s result, or a built-in that writes it,
    /// whose result goes where <see cref="Destination"/> says; and whether its VM keeps its values as roots, as it
    /// does while a toString() it waits for runs.
    /// </summary>
    private sealed class TextCall(WsTextBuilder builder, IWsBuiltinFunction? builtin, int destination)
    {
        public WsTextBuilder Builder { get; } = builder;
        public IWsBuiltinFunction? Builtin { get; } = builtin;
        /// <summary>The depth its frame's stack is cut to, where its result is pushed.</summary>
        public int Destination { get; } = destination;
        public bool Rooted { get; set; }
    }

    private sealed class Execution(WsStackModule module, WsStackInstruction[] entryCode, WsStackInstruction[][] codes,
        WsValue[] constants, WsMemberName[] names, IWsBuiltinFunction[] builtins, WsExecutionContext context, WsHeap heap)
    {
        public WsExecutionResult Run()
        {
            var frames = new Frame[16];
            var depth = 0;
            var frame = frames[0] = new Frame
            {
                Function = module.EntryPoint,
                Code = entryCode,
                Slots = new WsValue[module.EntryPoint.SlotCount]
            };
            heap.AddRoots(frame.Slots);
            heap.AddRoots(_errorClass);
            heap.AddRoots(_objectClass);
            heap.AddRoots(_valueClasses);
            heap.AddRoots(_errorClasses);

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
            WsRuntimeError? error = null;
            // A call being made, which the operations that call set up, and the code after their switch carries out: the
            // value called, the instance a method runs on, where its arguments start in the slots, and, for new, the
            // instance made. The slot below the arguments is where the result goes.
            var callee = WsValue.Null;
            var self = WsValue.Null;
            var firstArgument = 0;
            var argumentCount = 0;
            var constructed = WsValue.Null;
            try
            {
                while (true)
                {
                    if (executed == dueAt)
                    {
                        if (executed >= budget)
                        {
                            return Failure(WsDiagnosticCodes.BudgetExceeded,
                                    $"The program ran {executed} instructions, its whole budget, and was stopped.",
                                    SpanOf(frame.Function, pc))
                                with { Instructions = executed };
                        }
                        if (cancellation is not null && executed % CancellationInterval == 0
                            && cancellation.IsCancellationRequested)
                        {
                            var span = SpanOf(frame.Function, pc);
                            return Failure(WsDiagnosticCodes.Cancelled, "The program was cancelled.", span)
                                with { Instructions = executed };
                        }
                        dueAt = Math.Min(budget, (executed / CancellationInterval + 1) * CancellationInterval);
                    }
                    executed++;

                    ref readonly var ins = ref code[pc];
                    switch (ins.OpCode)
                    {
                        case WsStackOpCode.LoadConstant:
                            slots[sp++] = constants[ins.A];
                            pc++;
                            continue;
                        case WsStackOpCode.LoadNull:
                            slots[sp++] = WsValue.Null;
                            pc++;
                            continue;
                        case WsStackOpCode.LoadBoolean:
                            slots[sp++] = WsValue.FromBoolean(ins.A != 0);
                            pc++;
                            continue;
                        case WsStackOpCode.LoadLocal:
                            slots[sp++] = slots[ins.A];
                            pc++;
                            continue;
                        case WsStackOpCode.StoreLocal:
                            slots[ins.A] = slots[--sp];
                            pc++;
                            continue;
                        case WsStackOpCode.Duplicate:
                            slots[sp] = slots[sp - 1];
                            sp++;
                            pc++;
                            continue;
                        case WsStackOpCode.Pop:
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.Negate:
                        {
                            if (!WsOperators.TryNegate(slots[sp - 1], out var negated, out error)) break;
                            slots[sp - 1] = negated;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.Plus:
                        {
                            if (!WsOperators.TryPlus(slots[sp - 1], out var same, out error)) break;
                            slots[sp - 1] = same;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.Not:
                        {
                            if (!WsOperators.TryNot(slots[sp - 1], out var not, out error)) break;
                            slots[sp - 1] = not;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.Add:
                            if (!Arithmetic(WsStackOpCode.Add, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.Subtract:
                            if (!Arithmetic(WsStackOpCode.Subtract, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.Multiply:
                            if (!Arithmetic(WsStackOpCode.Multiply, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.Divide:
                            if (!Arithmetic(WsStackOpCode.Divide, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.Equal:
                            if (!Compare(WsStackOpCode.Equal, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.NotEqual:
                            if (!Compare(WsStackOpCode.NotEqual, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.Less:
                            if (!Compare(WsStackOpCode.Less, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.LessOrEqual:
                            if (!Compare(WsStackOpCode.LessOrEqual, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.Greater:
                            if (!Compare(WsStackOpCode.Greater, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.GreaterOrEqual:
                            if (!Compare(WsStackOpCode.GreaterOrEqual, slots, sp, out error)) break;
                            sp--;
                            pc++;
                            continue;
                        case WsStackOpCode.Jump:
                            pc = ins.A;
                            continue;
                        case WsStackOpCode.JumpIfFalse:
                        {
                            var condition = slots[--sp];
                            if (condition.Kind != WsValueKind.Boolean)
                            {
                                error = WsOperators.CheckBoolean(condition, "A condition");
                                break;
                            }
                            pc = condition.AsBoolean ? pc + 1 : ins.A;
                            continue;
                        }
                        case WsStackOpCode.JumpIfTrue:
                        {
                            var condition = slots[--sp];
                            if (condition.Kind != WsValueKind.Boolean)
                            {
                                error = WsOperators.CheckBoolean(condition, "A condition");
                                break;
                            }
                            pc = condition.AsBoolean ? ins.A : pc + 1;
                            continue;
                        }
                        case WsStackOpCode.CheckBoolean:
                            if (slots[sp - 1].Kind != WsValueKind.Boolean)
                            {
                                error = WsOperators.CheckBoolean(slots[sp - 1], "'&&' and '||' need Booleans:");
                                break;
                            }
                            pc++;
                            continue;
                        case WsStackOpCode.NewCell:
                            if (!heap.TryAllocateCell(WsValue.Null, false, out slots[ins.A], out error)) break;
                            pc++;
                            continue;
                        case WsStackOpCode.BoxLocal:
                        {
                            if (!heap.TryAllocateCell(slots[ins.A], true, out var boxed, out error)) break;
                            slots[ins.A] = boxed;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.CopyCell:
                        {
                            var hasValue = heap.TryReadCell(slots[ins.A], out var held);
                            if (!heap.TryAllocateCell(held, hasValue, out var copy, out error)) break;
                            slots[ins.A] = copy;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.LoadCell:
                            error = ReadCell(slots[ins.A], frame.Function, ins.A, out slots[sp]);
                            if (error is not null) break;
                            sp++;
                            pc++;
                            continue;
                        case WsStackOpCode.StoreCell:
                            heap.WriteCell(slots[ins.A], slots[--sp]);
                            pc++;
                            continue;
                        case WsStackOpCode.LoadCapture:
                            error = ReadCell(cells[ins.A], null, ins.A, out slots[sp]);
                            if (error is not null) break;
                            sp++;
                            pc++;
                            continue;
                        case WsStackOpCode.StoreCapture:
                            heap.WriteCell(cells[ins.A], slots[--sp]);
                            pc++;
                            continue;
                        case WsStackOpCode.MakeClosure:
                        {
                            if (!TryMakeClosure(ins.A, slots, cells, out slots[sp], out error)) break;
                            sp++;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.LoadBuiltin:
                            slots[sp++] = WsValue.FromHandle(WsValueKind.Builtin, ins.A);
                            pc++;
                            continue;
                        case WsStackOpCode.CallBuiltin:
                        {
                            if (_texts is not null && _texts[ins.A] && ins.B == 1 && NeedsText(slots[sp - 1]))
                            {
                                // A built-in that writes text, of a value that may hold an instance whose toString() must run.
                                _text = new TextCall(new WsTextBuilder(heap, slots[sp - 1], false), builtins[ins.A], sp - 1);
                                goto text;
                            }
                            error = CallBuiltin(builtins[ins.A], slots, sp - ins.B, ins.B, out var result);
                            if (error is not null) break;
                            sp -= ins.B;
                            slots[sp++] = result;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.Call:
                            // Below the arguments, the callee must be a function taking A of them; the compiler checks
                            // what it can, and the call, after this switch, the rest (WS2005, WS2006).
                            argumentCount = ins.A;
                            firstArgument = sp - argumentCount;
                            callee = slots[firstArgument - 1];
                            self = WsValue.Null;
                            constructed = WsValue.Null;
                            goto call;
                        case WsStackOpCode.GetMember:
                        {
                            var name = names[ins.A];
                            if (!WsMembers.TryGet(slots[sp - 1], name.Member, name.Text, heap, ins.B != 0, out var member, out error))
                            {
                                break;
                            }
                            slots[sp - 1] = member;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.SetMember:
                        {
                            var value = slots[sp - 1];
                            if (!WsMembers.TrySet(slots[sp - 2], names[ins.A].Text, value, heap, out error)) break;
                            sp -= 2;
                            if (ins.B == 1) slots[sp++] = value;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.CallMethod:
                        {
                            var name = names[ins.A];
                            var first = sp - ins.B;
                            var target = slots[first - 1];
                            if (target.Kind == WsValueKind.Class)
                            {
                                // A static member, called as any function is (section 3.7).
                                if (!WsMembers.TryGet(target, name.Member, name.Text, heap, false, out callee, out error)) break;
                                self = WsValue.Null;
                                firstArgument = first;
                                argumentCount = ins.B;
                                constructed = WsValue.Null;
                                goto call;
                            }
                            if (target.Kind == WsValueKind.Instance)
                            {
                                // A field's function is called as any function is; a method runs on the instance.
                                if (!WsMembers.TryFindCallee(target, name.Text, heap, out callee, out var isMethod, out error)) break;
                                self = isMethod ? target : WsValue.Null;
                                firstArgument = first;
                                argumentCount = ins.B;
                                constructed = WsValue.Null;
                                if (isMethod && name.Member == WsMember.Dispose) goto dispose;
                                goto call;
                            }
                            if (WsMembers.TryGetObjectMethod(name.Member, name.Text, heap, out var objectMethod))
                            {
                                // Every value has Object's toString() and equal() (section 3.7).
                                callee = objectMethod;
                                self = target;
                                firstArgument = first;
                                argumentCount = ins.B;
                                constructed = WsValue.Null;
                                goto call;
                            }
                            if (!WsMembers.TryCall(target, name.Member, name.Text, slots, first, ins.B, heap, out var result, out error))
                            {
                                break;
                            }
                            sp = first;
                            slots[sp - 1] = result;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.MakeClass:
                        {
                            var baseClass = ins.B == 1 ? slots[sp - 1] : WsValue.Null;
                            if (!TryMakeClass(module.Classes[ins.A], baseClass, slots, cells, out var made, out error)) break;
                            sp -= ins.B;
                            slots[sp++] = made;
                            if (_builtin[ins.A]) Remember(ins.A, made);
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.GetSuper:
                        {
                            if (!WsMembers.TryGetSuper(slots[sp - 2], slots[sp - 1], names[ins.A].Text, heap, out var bound, out error))
                            {
                                break;
                            }
                            sp--;
                            slots[sp - 1] = bound;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.New:
                        {
                            // A new instance; its constructor, if its class has one, runs on it as a call, whose result
                            // is the instance (section 3.7).
                            var first = sp - ins.A;
                            var classValue = slots[first - 1];
                            if (classValue.Kind != WsValueKind.Class)
                            {
                                error = new WsRuntimeError(WsDiagnosticCodes.NotAFunction,
                                    $"{Capitalized(WsValueFormatter.Describe(classValue, heap))} is not a class, and cannot be made with 'new'.");
                                break;
                            }
                            if (!heap.TryAllocateInstance(classValue, out var instance, out error)) break;
                            callee = heap.ClassConstructor(classValue);
                            if (callee.Kind == WsValueKind.Null)
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
                        case WsStackOpCode.NewArray:
                        {
                            var first = sp - ins.A;
                            if (!heap.TryAllocateArray(slots, first, ins.A, out var array, out error)) break;
                            sp = first;
                            slots[sp++] = array;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.NewSet:
                        {
                            var first = sp - ins.A;
                            if (!heap.TryAllocateSet(slots, first, ins.A, out var set, out error)) break;
                            sp = first;
                            slots[sp++] = set;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.NewMap:
                        {
                            var first = sp - 2 * ins.A;
                            if (!heap.TryAllocateMap(slots, first, ins.A, out var map, out error)) break;
                            sp = first;
                            slots[sp++] = map;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.ForEachNext:
                        case WsStackOpCode.ForEachNextEntry:
                        {
                            var items = ins.OpCode == WsStackOpCode.ForEachNextEntry ? 2 : 1;
                            if (!WsMembers.TryNext(slots[ins.B], slots[ins.B + 1], items, slots, ins.B + 2, heap,
                                    out var next, out var done, out error))
                            {
                                break;
                            }
                            slots[ins.B + 1] = next;
                            pc = done ? ins.A : pc + 1;
                            continue;
                        }
                        case WsStackOpCode.GetIndex:
                        {
                            if (!WsMembers.TryGetIndex(slots[sp - 2], slots[sp - 1], heap, ins.A != 0, out var element, out error))
                            {
                                break;
                            }
                            sp--;
                            slots[sp - 1] = element;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.StoreIndex:
                        {
                            var value = slots[sp - 1];
                            if (!WsMembers.TrySetIndex(slots[sp - 3], slots[sp - 2], value, heap, out error)) break;
                            sp -= 3;
                            if (ins.A == 1) slots[sp++] = value;
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.Dispose:
                        {
                            // A using's end: its resource, popped, disposed, unless it is null, as x.dispose() disposes
                            // it, the result dropped (section 3.7).
                            var resource = slots[sp - 1];
                            if (resource.Kind == WsValueKind.Null)
                            {
                                sp--;
                                pc++;
                                continue;
                            }
                            error = BeginDisposal(resource, 0, out var chain);
                            if (error is not null) break;
                            if (chain is null)
                            {
                                sp--;
                                pc++;
                                continue;
                            }
                            if (depth + 1 == frames.Length) Array.Resize(ref frames, frames.Length * 2);
                            Disposing(frames, depth + 1, chain, resource).DiscardResult = true;
                            callee = chain[0];
                            self = resource;
                            firstArgument = sp;
                            argumentCount = 0;
                            constructed = WsValue.Null;
                            goto call;
                        }
                        case WsStackOpCode.IsInstance:
                        {
                            var type = slots[--sp];
                            slots[sp - 1] = WsValue.FromBoolean(WsMembers.IsInstance(slots[sp - 1], type, heap, _valueClasses));
                            pc++;
                            continue;
                        }
                        case WsStackOpCode.ToText:
                            _text = new TextCall(new WsTextBuilder(heap, slots[sp - 1], true), null, sp - 1);
                            goto text;
                        case WsStackOpCode.Throw:
                            _thrown = slots[--sp];
                            _thrownError = null;
                            _origin = OriginOf(frame.Function, pc);
                            goto throwing;
                        case WsStackOpCode.Rethrow:
                        {
                            // 'throw;', or a finally's end: the exception again, from where it was first thrown.
                            var origin = slots[--sp];
                            _thrown = slots[--sp];
                            _thrownError = null;
                            _origin = origin.Kind == WsValueKind.Int64 ? origin.AsInt64 : OriginOf(frame.Function, pc);
                            goto throwing;
                        }
                        case WsStackOpCode.Return:
                        case WsStackOpCode.ReturnValue:
                        {
                            var result = ins.OpCode == WsStackOpCode.ReturnValue ? slots[--sp] : WsValue.Null;
                            if (depth == 0) return WsExecutionResult.Success with { Instructions = executed };
                            heap.RemoveLastRoots(frame.Closure);
                            heap.RemoveLastRoots(slots);
                            Array.Clear(slots, 0, frame.Function.SlotCount); // So that nothing stays reachable.
                            if (frame.DisposeChain is not null) goto disposed;
                            if (frame.Text is not null)
                            {
                                _returned = result;
                                goto texted;
                            }
                            frame.Closure[0] = WsValue.Null;
                            if (frame.Constructed.Kind == WsValueKind.Instance) result = frame.Constructed;
                            frame.Constructed = WsValue.Null;

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
                            error = new WsRuntimeError(WsDiagnosticCodes.BytecodeVerificationFailure,
                                $"The operation {(int)ins.OpCode} is unknown.");
                            break;
                    }
                    goto failed;

                call:
                    // The call the operations above set up: a built-in at once, a closure in a new frame, its arguments
                    // in its first locals, after the instance it runs on if it is a method (section 3.7). The callee,
                    // or the target, or the class, below the arguments, is where the result goes.
                    if (callee.Kind == WsValueKind.BoundMethod)
                    {
                        if (heap.IsDisposal(callee))
                        {
                            self = heap.GetBoundMethod(callee).Instance;
                            goto dispose;
                        }
                        (self, callee) = heap.GetBoundMethod(callee);
                    }
                    if (callee.Kind == WsValueKind.Builtin)
                    {
                        var builtin = builtins[callee.Handle];
                        if (builtin.Arity != argumentCount)
                        {
                            error = WrongArgumentCount(builtin.Name, builtin.Arity, argumentCount);
                            goto failed;
                        }
                        if (_texts is not null && _texts[callee.Handle] && argumentCount == 1 && NeedsText(slots[firstArgument]))
                        {
                            _text = new TextCall(new WsTextBuilder(heap, slots[firstArgument], false), builtin, firstArgument - 1);
                            goto text;
                        }
                        error = CallBuiltin(builtin, slots, firstArgument, argumentCount, out var builtinResult);
                        if (error is not null) goto failed;
                        sp = firstArgument - 1;
                        slots[sp++] = builtinResult;
                        pc++;
                        continue;
                    }
                    if (callee.Kind != WsValueKind.Function)
                    {
                        var what = Capitalized(WsValueFormatter.Describe(callee, heap));
                        error = new WsRuntimeError(WsDiagnosticCodes.NotAFunction, callee.Kind == WsValueKind.Class
                            ? $"{what} is not a function: make an instance with 'new'."
                            : $"{what} is not a function, and cannot be called.");
                        goto failed;
                    }
                    {
                        var (index, closureCells) = heap.GetClosure(callee);
                        var function = module.Functions[index];
                        var withThis = function.HasThis ? 1 : 0;
                        if (function.ParameterCount != argumentCount + withThis)
                        {
                            error = WrongArgumentCount(function.Name, function.ParameterCount - withThis, argumentCount);
                            goto failed;
                        }
                        if (depth + 1 >= maxCallDepth)
                        {
                            error = new WsRuntimeError(WsDiagnosticCodes.BudgetExceeded,
                                $"The calls nest more than {maxCallDepth} deep, which this execution allows.");
                            goto failed;
                        }

                        frame.Pc = pc + 1;
                        frame.Sp = firstArgument - 1;
                        if (++depth == frames.Length) Array.Resize(ref frames, frames.Length * 2);
                        var called = frames[depth] ??= new Frame();
                        if (called.Slots.Length < function.SlotCount) called.Slots = new WsValue[function.SlotCount];
                        var calledSlots = called.Slots;
                        if (withThis == 1) calledSlots[0] = self;
                        for (var i = 0; i < argumentCount; i++) calledSlots[withThis + i] = slots[firstArgument + i];
                        called.Function = function;
                        called.Code = codes[index];
                        called.Cells = closureCells;
                        called.Closure[0] = callee;
                        called.Constructed = constructed;
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
                        error = BeginDisposal(self, argumentCount, out var chain);
                        if (error is not null) goto failed;
                        if (chain is null)
                        {
                            sp = firstArgument - 1;
                            slots[sp++] = WsValue.Null;
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
                        frame.Closure[0] = WsValue.Null;
                        frame.Closure[1] = WsValue.Null;
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
                        if (!discard) slots[sp++] = WsValue.Null;
                        continue;
                    }

                nextDisposal:
                    // The instance's next dispose method, in the same frame, whose roots were let go and slots cleared;
                    // its result is dropped.
                    {
                        var next = frame.DisposeChain![frame.DisposeNext++];
                        var (nextIndex, nextCells) = heap.GetClosure(next);
                        var nextFunction = module.Functions[nextIndex];
                        if (slots.Length < nextFunction.SlotCount) frame.Slots = slots = new WsValue[nextFunction.SlotCount];
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
                            constructed = WsValue.Null;
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
                    // text, which goes on, at the instruction it is for; anything but a string is WS3001 (section 3.7).
                    {
                        var building = frame.Text!;
                        frame.Text = null;
                        frame.Closure[0] = WsValue.Null;
                        frame = frames[--depth];
                        code = frame.Code;
                        slots = frame.Slots;
                        cells = frame.Cells;
                        sp = frame.Sp;
                        pc = frame.Pc - 1;
                        if (_returned.Kind != WsValueKind.String)
                        {
                            if (building.Rooted) heap.RemoveLastRoots(building.Builder.Roots);
                            error = new WsRuntimeError(WsDiagnosticCodes.InvalidOperation,
                                $"'toString()' gave {WsValueFormatter.Describe(_returned, heap)}, not a string, to be written.");
                            _returned = WsValue.Null;
                            goto failed;
                        }
                        building.Builder.Supply(heap.GetString(_returned));
                        _returned = WsValue.Null;
                        _text = building;
                        goto text;
                    }

                failed:
                    // A call that failed before its frame was pushed leaves nothing in that frame: no disposal, no text.
                    if (depth + 1 < frames.Length && frames[depth + 1] is { } unpushed)
                    {
                        unpushed.DisposeChain = null;
                        if (unpushed.Text is { Rooted: true } stale) heap.RemoveLastRoots(stale.Builder.Roots);
                        unpushed.Text = null;
                    }
                    // A runtime error: one that cannot be caught stops the program, and any other is thrown (section 3.7).
                    if (!WsExceptions.IsCatchable(error!.Code))
                    {
                        return Failure(error.Code, error.Message, SpanOf(frame.Function, pc)) with { Instructions = executed };
                    }
                    _thrown = WsValue.Null;
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
                            slots[handler.Origin] = WsValue.FromInt64(_origin);
                            pc = handler.Target;
                            continue;
                        }
                        if (WsExceptions.DisposalGoesOn && frame.DisposeChain is { } chain && frame.DisposeNext < chain.Length)
                        {
                            // Its next dispose method runs; the exception waits for the last, a later one replacing it.
                            Hold(frame);
                            heap.RemoveLastRoots(frame.Closure);
                            heap.RemoveLastRoots(slots);
                            Array.Clear(slots, 0, frame.Function.SlotCount);
                            goto nextDisposal;
                        }
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
                return Failure(WsDiagnosticCodes.BytecodeVerificationFailure, $"The bytecode is invalid: {exception.Message}",
                    SpanOf(frame.Function, pc)) with { Instructions = executed };
            }
        }

        /// <summary>A cell's value; WS2004 if it has none yet, which only a hoisted function can meet (section 3.5).</summary>
        private WsRuntimeError? ReadCell(WsValue cell, WsStackFunction? owner, int local, out WsValue value)
        {
            if (heap.TryReadCell(cell, out value)) return null;
            var name = owner?.LocalNames is { } names && local < names.Count ? $"'{names[local]}'" : "A variable";
            return new WsRuntimeError(WsDiagnosticCodes.ReadBeforeInitialization,
                $"{name} is used by a function called before its declaration has run.");
        }

        /// <summary>
        /// Arithmetic on the two values on top of the stack, whose result replaces the left one: two int64s, without
        /// overflow, or two binary64s, here; anything else, and every error, by <see cref="WsOperators"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Arithmetic(WsStackOpCode opCode, WsValue[] slots, int sp, out WsRuntimeError? error)
        {
            var (left, right) = (slots[sp - 2], slots[sp - 1]);
            error = null;
            if (left.Kind == WsValueKind.Int64 && right.Kind == WsValueKind.Int64)
            {
                var (a, b) = (left.AsInt64, right.AsInt64);
                switch (opCode)
                {
                    case WsStackOpCode.Add:
                    {
                        var sum = unchecked(a + b);
                        if (((a ^ sum) & (b ^ sum)) >= 0) return Done(WsValue.FromInt64(sum), slots, sp);
                        break;
                    }
                    case WsStackOpCode.Subtract:
                    {
                        var difference = unchecked(a - b);
                        if (((a ^ b) & (a ^ difference)) >= 0) return Done(WsValue.FromInt64(difference), slots, sp);
                        break;
                    }
                    case WsStackOpCode.Multiply:
                    {
                        var high = Math.BigMul(a, b, out var low);
                        if (high == low >> 63) return Done(WsValue.FromInt64(low), slots, sp);
                        break;
                    }
                    default:
                        return Done(WsValue.FromBinary64((double)a / b), slots, sp);
                }
            }
            else if (left.Kind == WsValueKind.Binary64 && right.Kind == WsValueKind.Binary64)
            {
                var (x, y) = (left.AsBinary64, right.AsBinary64);
                return Done(WsValue.FromBinary64(opCode switch
                {
                    WsStackOpCode.Add => x + y,
                    WsStackOpCode.Subtract => x - y,
                    WsStackOpCode.Multiply => x * y,
                    _ => x / y
                }), slots, sp);
            }
            if (!WsOperators.TryBinary(ArithmeticOf(opCode), left, right, heap, out var value, out error)) return false;
            return Done(value, slots, sp);
        }

        /// <summary>
        /// A comparison of the two values on top of the stack, whose result replaces the left one: two int64s, or two
        /// binary64s (IEEE 754's, so that NaN is unordered and unequal to itself), here; anything else by
        /// <see cref="WsOperators"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Compare(WsStackOpCode opCode, WsValue[] slots, int sp, out WsRuntimeError? error)
        {
            var (left, right) = (slots[sp - 2], slots[sp - 1]);
            error = null;
            if (left.Kind == WsValueKind.Int64 && right.Kind == WsValueKind.Int64)
            {
                var (a, b) = (left.AsInt64, right.AsInt64);
                return Done(WsValue.FromBoolean(opCode switch
                {
                    WsStackOpCode.Equal => a == b,
                    WsStackOpCode.NotEqual => a != b,
                    WsStackOpCode.Less => a < b,
                    WsStackOpCode.LessOrEqual => a <= b,
                    WsStackOpCode.Greater => a > b,
                    _ => a >= b
                }), slots, sp);
            }
            if (left.Kind == WsValueKind.Binary64 && right.Kind == WsValueKind.Binary64)
            {
                var (x, y) = (left.AsBinary64, right.AsBinary64);
                return Done(WsValue.FromBoolean(opCode switch
                {
                    WsStackOpCode.Equal => x == y,
                    WsStackOpCode.NotEqual => x != y,
                    WsStackOpCode.Less => x < y,
                    WsStackOpCode.LessOrEqual => x <= y,
                    WsStackOpCode.Greater => x > y,
                    _ => x >= y
                }), slots, sp);
            }
            if (!WsOperators.TryCompare(ComparisonOf(opCode), left, right, heap, out var value, out error)) return false;
            return Done(value, slots, sp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Done(WsValue value, WsValue[] slots, int sp)
        {
            slots[sp - 2] = value;
            return true;
        }

        /// <summary>
        /// Begins disposing an instance, for <c>x.dispose()</c> or a using's end (section 3.7): its dispose methods, to run
        /// on it in turn, or <c>null</c> if its disposal has begun already. Disposal gives them no arguments, so a call
        /// with some, or a dispose method that takes some, is WS2006.
        /// </summary>
        private WsRuntimeError? BeginDisposal(WsValue instance, int argumentCount, out WsValue[]? chain)
        {
            chain = null;
            if (argumentCount != 0)
            {
                return new WsRuntimeError(WsDiagnosticCodes.WrongArgumentCount, $"'dispose' takes no arguments, not {argumentCount}.");
            }
            if (!WsMembers.TryBeginDisposal(instance, heap, out chain, out var error)) return error;
            if (chain is null) return null;
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
        private static Frame Disposing(Frame[] frames, int depth, WsValue[] chain, WsValue instance)
        {
            var frame = frames[depth] ??= new Frame();
            frame.DisposeChain = chain;
            frame.DisposeNext = 1;
            frame.Closure[1] = instance;
            return frame;
        }

        // The exception being thrown, while the frames are searched for its handler: a value, or a runtime error not
        // made a value yet, and where it was first thrown (WsExceptions.Origin).
        private WsValue _thrown;
        private WsRuntimeError? _thrownError;
        private long _origin;

        /// <summary>The class Error, once the module has made it, which a caught runtime error becomes (section 3.7); a root.</summary>
        private readonly WsValue[] _errorClass = new WsValue[1];

        /// <summary>The class Object, once the module has made it, whose toString() every value has (section 3.7); a root.</summary>
        private readonly WsValue[] _objectClass = new WsValue[1];

        /// <summary>
        /// For each built-in, whether it writes text (<see cref="IWsTextBuiltin"/>), and so is given a value's text with
        /// the toString() its instances override run; null if no class of the module overrides toString(), as then the
        /// formatter alone gives every text (section 3.7).
        /// </summary>
        private readonly bool[]? _texts = TextBuiltins(module, builtins);

        /// <summary>A text to build, on its way to the code that builds it; and what a toString() a text waits for gave.</summary>
        private TextCall? _text;
        private WsValue _returned;

        private static bool[]? TextBuiltins(WsStackModule module, IWsBuiltinFunction[] builtins)
        {
            var overrides = false;
            for (var i = 0; i < module.Classes.Count && !overrides; i++)
            {
                if (i == module.ObjectClass) continue;
                foreach (var method in module.Classes[i].Methods) overrides |= method.Name == "toString";
            }
            if (!overrides) return null;
            var texts = new bool[builtins.Length];
            for (var i = 0; i < builtins.Length; i++) texts[i] = builtins[i] is IWsTextBuiltin;
            return texts;
        }

        /// <summary>Whether a value's text may hold an instance's, which its class's toString() may give.</summary>
        private static bool NeedsText(WsValue value) =>
            value.Kind is WsValueKind.Instance or WsValueKind.Array or WsValueKind.Map or WsValueKind.Set;

        /// <summary>The kinds of values' classes, by <c>WsValueClass</c>, once the module has made them (section 3.7); a root.</summary>
        private readonly WsValue[] _valueClasses = new WsValue[(int)WsValueClass.Class + 1];

        /// <summary>The kinds of runtime errors' classes, as <c>module.ErrorClasses</c> lists them, once made; a root.</summary>
        private readonly WsValue[] _errorClasses = new WsValue[module.ErrorClasses.Count];

        /// <summary>For each class of the module, whether it is a built-in one that the VM keeps when it makes it.</summary>
        private readonly bool[] _builtin = BuiltinClasses(module);

        private static bool[] BuiltinClasses(WsStackModule module)
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
        private void Remember(int index, WsValue made)
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
                heap.SealClass(made); // For now, no class extends one, nor does new make one (section 3.7).
            }
            for (var i = 0; i < module.ErrorClasses.Count; i++)
            {
                if (module.ErrorClasses[i].Class == index) _errorClasses[i] = made;
            }
        }

        /// <summary>The class a runtime error of this code becomes: its kind's, or Error.</summary>
        private WsValue ErrorClassFor(string code)
        {
            for (var i = 0; i < module.ErrorClasses.Count; i++)
            {
                if (module.ErrorClasses[i].Code == code && _errorClasses[i].Kind == WsValueKind.Class) return _errorClasses[i];
            }
            return _errorClass[0];
        }

        private long OriginOf(WsStackFunction function, int pc)
        {
            for (var i = 0; i < module.Functions.Count; i++)
            {
                if (ReferenceEquals(module.Functions[i], function)) return WsExceptions.Origin(i + 1, pc);
            }
            return WsExceptions.Origin(0, pc);
        }

        /// <summary>Where an origin is in the source; nowhere if it names no function of the module.</summary>
        private WsSourceSpan SpanOfOrigin(long origin)
        {
            var (function, pc) = WsExceptions.FromOrigin(origin);
            var owner = function == 0 ? module.EntryPoint
                : function > 0 && function <= module.Functions.Count ? module.Functions[function - 1] : null;
            return owner is null || pc < 0 ? new WsSourceSpan(0, 0) : SpanOf(owner, pc);
        }

        /// <summary>The exception being thrown as a value, a runtime error made an Error; false if it cannot be one.</summary>
        private bool TryTakeException(out WsValue exception)
        {
            if (_thrownError is null)
            {
                exception = _thrown;
                return true;
            }
            if (!WsExceptions.TryMakeError(heap, ErrorClassFor(_thrownError.Code), _thrownError, out exception)) return false;
            _thrown = exception;
            _thrownError = null;
            return true;
        }

        /// <summary>An exception that nothing caught: the program stops, reporting it where it was first thrown.</summary>
        private WsExecutionResult Uncaught(long executed)
        {
            var (code, message) = _thrownError is { } error
                ? (error.Code, error.Message)
                : WsExceptions.Uncaught(_thrown, _errorClass[0], heap);
            return Failure(code, message, SpanOfOrigin(_origin)) with { Instructions = executed };
        }

        /// <summary>The first handler whose region holds the instruction, or -1.</summary>
        private static int FindHandler(IReadOnlyList<WsStackHandler> handlers, int pc)
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
            frame.Closure[2] = WsValue.Null;
            frame.Holds = false;
            frame.HeldError = null;
            frame.Text = null;
        }

        /// <summary>A frame an exception leaves keeps nothing: its closure, its instance, a disposal, a kept exception.</summary>
        private static void Release(Frame frame)
        {
            frame.Closure[0] = WsValue.Null;
            frame.Closure[1] = WsValue.Null;
            frame.Closure[2] = WsValue.Null;
            frame.Constructed = WsValue.Null;
            frame.DisposeChain = null;
            frame.Holds = false;
            frame.HeldError = null;
            frame.Text = null;
            frame.DiscardResult = false;
        }

        private static WsRuntimeError WrongArgumentCount(string name, int arity, int count) =>
            new(WsDiagnosticCodes.WrongArgumentCount,
                $"'{name}' takes {arity} {(arity == 1 ? "argument" : "arguments")}, not {count}.");

        private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        /// <summary>
        /// Calls a built-in with the <paramref name="count"/> arguments from slot <paramref name="first"/> on, copied out
        /// of the slots. Its failure, even an exception, is a runtime error.
        /// </summary>
        /// <summary>
        /// A closure of the module's function, its captures taken from the making function's locals or its own
        /// captures, as the function's capture table says.
        /// </summary>
        private bool TryMakeClosure(int function, WsValue[] slots, WsValue[] cells, out WsValue closure, out WsRuntimeError? error)
        {
            var captures = module.Functions[function].Captures;
            var captured = new WsValue[captures.Count];
            for (var i = 0; i < captured.Length; i++)
            {
                captured[i] = captures[i].FromLocal ? slots[captures[i].Index] : cells[captures[i].Index];
                if (captured[i].Kind != WsValueKind.Cell) throw new InvalidOperationException("A capture is not a cell.");
            }
            return heap.TryAllocateClosure(function, captured, out closure, out error);
        }

        /// <summary>
        /// A class value: its constructor's and its methods' closures made now, kept as roots until the class holds
        /// them, since making each may collect (section 3.7).
        /// </summary>
        private bool TryMakeClass(WsClassInfo info, WsValue baseClass, WsValue[] slots, WsValue[] cells, out WsValue made,
            out WsRuntimeError? error)
        {
            made = WsValue.Null;
            var closures = new WsValue[info.Methods.Count + info.StaticMethods.Count + 1];
            heap.AddRoots(closures);
            try
            {
                if (info.Constructor != -1 && !TryMakeClosure(info.Constructor, slots, cells, out closures[0], out error)) return false;
                var methods = new Dictionary<string, WsValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.Methods.Count; i++)
                {
                    if (!TryMakeClosure(info.Methods[i].Function, slots, cells, out closures[i + 1], out error)) return false;
                    methods.Add(info.Methods[i].Name, closures[i + 1]);
                }
                var statics = new Dictionary<string, WsValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.StaticMethods.Count; i++)
                {
                    var slot = info.Methods.Count + 1 + i;
                    if (!TryMakeClosure(info.StaticMethods[i].Function, slots, cells, out closures[slot], out error)) return false;
                    statics.Add(info.StaticMethods[i].Name, closures[slot]);
                }
                return heap.TryAllocateClass(info.Name, closures[0], methods, baseClass, out made, out error, statics,
                    info.StaticFields);
            }
            finally
            {
                heap.RemoveLastRoots(closures);
            }
        }

        private WsRuntimeError? CallBuiltin(IWsBuiltinFunction builtin, WsValue[] slots, int first, int count,
            out WsValue result)
        {
            result = WsValue.Null;
            var arguments = new WsValue[count];
            for (var i = 0; i < arguments.Length; i++) arguments[i] = slots[first + i];
            try
            {
                if (!builtin.TryInvoke(arguments, heap, out result, out var error))
                {
                    return error ?? new WsRuntimeError(WsDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed.");
                }
                return null;
            }
            catch (Exception exception)
            {
                return new WsRuntimeError(WsDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed: {exception.Message}");
            }
        }
    }

    private static WsArithmeticOperator ArithmeticOf(WsStackOpCode opCode) => opCode switch
    {
        WsStackOpCode.Add => WsArithmeticOperator.Add,
        WsStackOpCode.Subtract => WsArithmeticOperator.Subtract,
        WsStackOpCode.Multiply => WsArithmeticOperator.Multiply,
        _ => WsArithmeticOperator.Divide
    };

    private static WsComparisonOperator ComparisonOf(WsStackOpCode opCode) => opCode switch
    {
        WsStackOpCode.Equal => WsComparisonOperator.Equal,
        WsStackOpCode.NotEqual => WsComparisonOperator.NotEqual,
        WsStackOpCode.Less => WsComparisonOperator.Less,
        WsStackOpCode.LessOrEqual => WsComparisonOperator.LessOrEqual,
        WsStackOpCode.Greater => WsComparisonOperator.Greater,
        _ => WsComparisonOperator.GreaterOrEqual
    };

    private static WsSourceSpan SpanOf(WsStackFunction function, int pc) =>
        function.Spans is { } spans && pc < spans.Count ? spans[pc] : new WsSourceSpan(0, 0);

    private static WsExecutionResult Failure(string code, string message, WsSourceSpan span) =>
        new(false, new WsDiagnostic(code, WsDiagnosticSeverity.Error, message, span));
}
