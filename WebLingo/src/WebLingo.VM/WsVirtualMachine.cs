using System.Runtime.CompilerServices;
using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.VM;

/// <summary>
/// The register VM (specification, sections 5.2 and 8.4, "VM"). It verifies a module before running it, links the
/// module's imports to the host's built-ins by name and arity, and runs it with a <c>switch</c> over the operations
/// (section 5.6), synchronously, on the calling thread, which stands for the context's owner worker. A call pushes a
/// frame onto a stack of the VM's own, not .NET's, so that deep recursion is bounded by the budget (WS3003), not by
/// the thread's stack. Each frame's registers, and its closure, are roots of the execution's heap. A runtime error
/// stops the execution and is returned with the source span of the instruction that raised it. Nothing a module or
/// a built-in does can crash the VM: a failure is a result.
/// <para>
/// Its dispatch is tuned for speed (section 5.6): one loop runs every instruction, with the running frame's code,
/// registers and position in locals; the budget and the cancellation signal are looked at only when a count of
/// instructions comes due; integer and binary64 arithmetic and comparisons are done in the loop, and anything else
/// (strings, mixed operands, every error) by <see cref="WsOperators"/>, so that the results and the messages are the
/// runtime's; and a call reuses the frame, and the registers, that the last call at its depth left.
/// </para>
/// </summary>
public sealed class WsVirtualMachine : IWsVirtualMachine
{
    /// <summary>How many instructions run between two looks at the cancellation signal.</summary>
    public const int CancellationInterval = 1024;

    private readonly IWsBytecodeVerifier _verifier;

    public WsVirtualMachine() : this(new WsBytecodeVerifier()) { }

    public WsVirtualMachine(IWsBytecodeVerifier verifier) => _verifier = verifier;

    public WsExecutionResult Execute(WsCompiledModule module, WsExecutionContext context)
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
            if (function.RegisterCount > largest.RegisterCount) largest = function;
        }
        if (largest.RegisterCount > context.Resources.MaxRegisters)
        {
            return Failure(WsDiagnosticCodes.BudgetExceeded,
                $"'{largest.Name}' needs {largest.RegisterCount} registers; this execution allows {context.Resources.MaxRegisters}.",
                new WsSourceSpan(0, 0));
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

        // Each function's code as an array, which the loop indexes directly.
        var codes = new WsInstruction[module.Functions.Count][];
        for (var i = 0; i < codes.Length; i++) codes[i] = ToArray(module.Functions[i].Code);

        return new Execution(module, ToArray(module.EntryPoint.Code), codes, constants, names, builtins, context, heap).Run();
    }

    /// <summary>A copy of the code, without LINQ, which the portable core does not use.</summary>
    private static WsInstruction[] ToArray(IReadOnlyList<WsInstruction> code)
    {
        var array = new WsInstruction[code.Count];
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
        public WsFunction Function = null!;
        public WsInstruction[] Code = [];
        public WsValue[] Registers = [];
        /// <summary>The closure it runs as, and, for a disposal, the instance disposed: the frame's roots.</summary>
        /// <summary>
        /// The frame's roots: the closure it runs as; for a disposal, the instance disposed; and an exception a disposal
        /// keeps while its next methods run (section 3.7).
        /// </summary>
        public readonly WsValue[] Closure = new WsValue[3];
        public WsValue[] Cells = [];
        /// <summary>The next instruction, while the frame waits for a call it made to return.</summary>
        public int Pc;
        /// <summary>The caller's register that the call's result goes to.</summary>
        public int ResultRegister;
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
        /// <summary>The register its result goes to.</summary>
        public int Destination { get; } = destination;
        public bool Rooted { get; set; }
    }

    private sealed class Execution(WsCompiledModule module, WsInstruction[] entryCode, WsInstruction[][] codes,
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
                Registers = new WsValue[module.EntryPoint.RegisterCount]
            };
            heap.AddRoots(frame.Registers);
            heap.AddRoots(_errorClass);
            heap.AddRoots(_objectClass);
            heap.AddRoots(_valueClasses);
            heap.AddRoots(_errorClasses);

            var code = frame.Code;
            var registers = frame.Registers;
            var cells = frame.Cells;
            var pc = 0;
            var budget = context.InstructionBudget;
            var cancellation = context.Cancellation;
            var maxCallDepth = context.Resources.MaxCallDepth;
            var executed = 0L;
            var dueAt = 0L; // The count of instructions at which the budget and the cancellation signal are looked at.
            WsRuntimeError? error = null;
            // A call being made, which the operations that call set up, and the code after their switch carries out: the
            // value called, the instance a method runs on, where the arguments are, where the result goes, and, for
            // new, the instance made. A result register of -1, a using's disposal's, drops the result.
            var callee = WsValue.Null;
            var self = WsValue.Null;
            var arguments = registers;
            var firstArgument = 0;
            var argumentCount = 0;
            var resultRegister = 0;
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
                        case WsOpCode.LoadConstant:
                            registers[ins.A] = constants[ins.B];
                            pc++;
                            continue;
                        case WsOpCode.Move:
                            registers[ins.A] = registers[ins.B];
                            pc++;
                            continue;
                        case WsOpCode.Negate:
                        {
                            if (!WsOperators.TryNegate(registers[ins.B], out var negated, out error)) break;
                            registers[ins.A] = negated;
                            pc++;
                            continue;
                        }
                        case WsOpCode.Plus:
                        {
                            if (!WsOperators.TryPlus(registers[ins.B], out var same, out error)) break;
                            registers[ins.A] = same;
                            pc++;
                            continue;
                        }
                        case WsOpCode.Add:
                            if (!Arithmetic(WsOpCode.Add, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.Subtract:
                            if (!Arithmetic(WsOpCode.Subtract, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.Multiply:
                            if (!Arithmetic(WsOpCode.Multiply, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.Divide:
                            if (!Arithmetic(WsOpCode.Divide, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.Equal:
                            if (!Compare(WsOpCode.Equal, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.NotEqual:
                            if (!Compare(WsOpCode.NotEqual, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.Less:
                            if (!Compare(WsOpCode.Less, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.LessOrEqual:
                            if (!Compare(WsOpCode.LessOrEqual, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.Greater:
                            if (!Compare(WsOpCode.Greater, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.GreaterOrEqual:
                            if (!Compare(WsOpCode.GreaterOrEqual, registers, ins, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.LoadNull:
                            registers[ins.A] = WsValue.Null;
                            pc++;
                            continue;
                        case WsOpCode.LoadBoolean:
                            registers[ins.A] = WsValue.FromBoolean(ins.B != 0);
                            pc++;
                            continue;
                        case WsOpCode.Not:
                        {
                            if (!WsOperators.TryNot(registers[ins.B], out var not, out error)) break;
                            registers[ins.A] = not;
                            pc++;
                            continue;
                        }
                        case WsOpCode.JumpIfFalse:
                        {
                            var condition = registers[ins.A];
                            if (condition.Kind != WsValueKind.Boolean)
                            {
                                error = WsOperators.CheckBoolean(condition, "A condition");
                                break;
                            }
                            pc = condition.AsBoolean ? pc + 1 : ins.B;
                            continue;
                        }
                        case WsOpCode.JumpIfTrue:
                        {
                            var condition = registers[ins.A];
                            if (condition.Kind != WsValueKind.Boolean)
                            {
                                error = WsOperators.CheckBoolean(condition, "A condition");
                                break;
                            }
                            pc = condition.AsBoolean ? ins.B : pc + 1;
                            continue;
                        }
                        case WsOpCode.CheckBoolean:
                            if (registers[ins.A].Kind != WsValueKind.Boolean)
                            {
                                error = WsOperators.CheckBoolean(registers[ins.A], "'&&' and '||' need Booleans:");
                                break;
                            }
                            pc++;
                            continue;
                        case WsOpCode.Jump:
                            pc = ins.A;
                            continue;
                        case WsOpCode.NewCell:
                            if (!heap.TryAllocateCell(WsValue.Null, false, out registers[ins.A], out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.BoxRegister:
                        {
                            if (!heap.TryAllocateCell(registers[ins.A], true, out var boxed, out error)) break;
                            registers[ins.A] = boxed;
                            pc++;
                            continue;
                        }
                        case WsOpCode.CopyCell:
                        {
                            var hasValue = heap.TryReadCell(registers[ins.A], out var held);
                            if (!heap.TryAllocateCell(held, hasValue, out var copy, out error)) break;
                            registers[ins.A] = copy;
                            pc++;
                            continue;
                        }
                        case WsOpCode.LoadCell:
                            error = ReadCell(registers[ins.B], frame.Function, ins.B, out registers[ins.A]);
                            if (error is not null) break;
                            pc++;
                            continue;
                        case WsOpCode.StoreCell:
                            heap.WriteCell(registers[ins.A], registers[ins.B]);
                            pc++;
                            continue;
                        case WsOpCode.LoadCapture:
                            error = ReadCell(cells[ins.B], null, ins.B, out registers[ins.A]);
                            if (error is not null) break;
                            pc++;
                            continue;
                        case WsOpCode.StoreCapture:
                            heap.WriteCell(cells[ins.A], registers[ins.B]);
                            pc++;
                            continue;
                        case WsOpCode.MakeClosure:
                        {
                            if (!TryMakeClosure(ins.B, registers, cells, out var closure, out error)) break;
                            registers[ins.A] = closure;
                            pc++;
                            continue;
                        }
                        case WsOpCode.LoadBuiltin:
                            registers[ins.A] = WsValue.FromHandle(WsValueKind.Builtin, ins.B);
                            pc++;
                            continue;
                        case WsOpCode.CallBuiltin:
                            if (_texts is not null && _texts[ins.B] && ins.D == 1 && NeedsText(registers[ins.C]))
                            {
                                // A built-in that writes text, of a value that may hold an instance whose toString() must run.
                                _text = new TextCall(new WsTextBuilder(heap, registers[ins.C], false), builtins[ins.B], ins.A);
                                goto text;
                            }
                            error = CallBuiltin(builtins[ins.B], registers, ins);
                            if (error is not null) break;
                            pc++;
                            continue;
                        case WsOpCode.Call:
                            // Register B must hold a function taking D arguments; the compiler checks what it can, and
                            // the call, after this switch, the rest (WS2005, WS2006).
                            callee = registers[ins.B];
                            self = WsValue.Null;
                            arguments = registers;
                            firstArgument = ins.C;
                            argumentCount = ins.D;
                            resultRegister = ins.A;
                            constructed = WsValue.Null;
                            goto call;
                        case WsOpCode.GetMember:
                        {
                            var name = names[ins.C];
                            if (!WsMembers.TryGet(registers[ins.B], name.Member, name.Text, heap, ins.D != 0, out var member,
                                    out error))
                            {
                                break;
                            }
                            registers[ins.A] = member;
                            pc++;
                            continue;
                        }
                        case WsOpCode.SetMember:
                            if (!WsMembers.TrySet(registers[ins.A], names[ins.B].Text, registers[ins.C], heap, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.CallMethod:
                        {
                            var name = names[ins.B];
                            var target = registers[ins.C];
                            if (target.HasClass && target.Kind is WsValueKind.Array or WsValueKind.Map or WsValueKind.Set
                                && WsMembers.TryFindCollectionCallee(target, name.Text, heap, out callee, out var collectionMethod))
                            {
                                // A collection of a class extending Array, Map or Set: its field's function, or its class's
                                // method, before its own members (section 3.7).
                                self = collectionMethod ? target : WsValue.Null;
                                arguments = registers;
                                firstArgument = ins.C + 1;
                                argumentCount = ins.D;
                                resultRegister = ins.A;
                                constructed = WsValue.Null;
                                goto call;
                            }
                            if (target.Kind == WsValueKind.Class)
                            {
                                // A static member, called as any function is (section 3.7).
                                if (!WsMembers.TryGet(target, name.Member, name.Text, heap, false, out callee, out error)) break;
                                self = WsValue.Null;
                                arguments = registers;
                                firstArgument = ins.C + 1;
                                argumentCount = ins.D;
                                resultRegister = ins.A;
                                constructed = WsValue.Null;
                                goto call;
                            }
                            if (target.Kind == WsValueKind.Instance)
                            {
                                // A field's function is called as any function is; a method runs on the instance.
                                if (!WsMembers.TryFindCallee(target, name.Text, heap, out callee, out var isMethod, out error)) break;
                                self = isMethod ? target : WsValue.Null;
                                arguments = registers;
                                firstArgument = ins.C + 1;
                                argumentCount = ins.D;
                                resultRegister = ins.A;
                                constructed = WsValue.Null;
                                if (isMethod && name.Member == WsMember.Dispose) goto dispose;
                                goto call;
                            }
                            if (WsMembers.TryGetObjectMethod(name.Member, name.Text, heap, out var objectMethod))
                            {
                                // Every value has Object's toString() and equal() (section 3.7).
                                callee = objectMethod;
                                self = target;
                                arguments = registers;
                                firstArgument = ins.C + 1;
                                argumentCount = ins.D;
                                resultRegister = ins.A;
                                constructed = WsValue.Null;
                                goto call;
                            }
                            if (!WsMembers.TryCall(target, name.Member, name.Text, registers, ins.C + 1, ins.D, heap,
                                    out var result, out error))
                            {
                                break;
                            }
                            registers[ins.A] = result;
                            pc++;
                            continue;
                        }
                        case WsOpCode.MakeClass:
                        {
                            var baseClass = ins.D == 1 ? registers[ins.C] : WsValue.Null;
                            if (!TryMakeClass(module.Classes[ins.B], baseClass, registers, cells, out var made, out error)) break;
                            registers[ins.A] = made;
                            if (_builtin[ins.B]) Remember(ins.B, made);
                            pc++;
                            continue;
                        }
                        case WsOpCode.GetSuper:
                        {
                            if (!WsMembers.TryGetSuper(registers[ins.B], registers[ins.C], names[ins.D].Text, heap, out var bound,
                                    out error))
                            {
                                break;
                            }
                            registers[ins.A] = bound;
                            pc++;
                            continue;
                        }
                        case WsOpCode.New:
                        {
                            // A new instance; its constructor, if its class has one, runs on it as a call, whose result
                            // is the instance (section 3.7).
                            var classValue = registers[ins.B];
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
                        case WsOpCode.NewArray:
                        {
                            if (!heap.TryAllocateArray(registers, ins.B, ins.C, out var array, out error)) break;
                            registers[ins.A] = array;
                            pc++;
                            continue;
                        }
                        case WsOpCode.NewSet:
                        {
                            if (!heap.TryAllocateSet(registers, ins.B, ins.C, out var set, out error)) break;
                            registers[ins.A] = set;
                            pc++;
                            continue;
                        }
                        case WsOpCode.NewMap:
                        {
                            if (!heap.TryAllocateMap(registers, ins.B, ins.C, out var map, out error)) break;
                            registers[ins.A] = map;
                            pc++;
                            continue;
                        }
                        case WsOpCode.ForEachNext:
                        {
                            if (!WsMembers.TryNext(registers[ins.B], registers[ins.B + 1], ins.C, registers, ins.B + 2, heap,
                                    out var next, out var done, out error))
                            {
                                break;
                            }
                            registers[ins.B + 1] = next;
                            pc = done ? ins.A : pc + 1;
                            continue;
                        }
                        case WsOpCode.GetIndex:
                        {
                            if (!WsMembers.TryGetIndex(registers[ins.B], registers[ins.C], heap, ins.D != 0, out var element,
                                    out error))
                            {
                                break;
                            }
                            registers[ins.A] = element;
                            pc++;
                            continue;
                        }
                        case WsOpCode.SetIndex:
                            if (!WsMembers.TrySetIndex(registers[ins.A], registers[ins.B], registers[ins.C], heap, out error)) break;
                            pc++;
                            continue;
                        case WsOpCode.Dispose:
                        {
                            // A using's end: its resource disposed, unless it is null, as x.dispose() disposes it, the
                            // result dropped (section 3.7).
                            var resource = registers[ins.A];
                            if (resource.Kind == WsValueKind.Null)
                            {
                                pc++;
                                continue;
                            }
                            error = BeginDisposal(resource, 0, out var chain);
                            if (error is not null) break;
                            if (chain is null)
                            {
                                pc++;
                                continue;
                            }
                            if (depth + 1 == frames.Length) Array.Resize(ref frames, frames.Length * 2);
                            Disposing(frames, depth + 1, chain, resource);
                            callee = chain[0];
                            self = resource;
                            arguments = registers;
                            firstArgument = 0;
                            argumentCount = 0;
                            resultRegister = -1;
                            constructed = WsValue.Null;
                            goto call;
                        }
                        case WsOpCode.IsInstance:
                        {
                            var type = registers[ins.C];
                            if (type.Kind != WsValueKind.Class && TryGetEqual(type, out var equalMethod))
                            {
                                // A name that is not a class: type.equal(value) gives the answer (section 3.7).
                                callee = equalMethod;
                                self = type;
                                arguments = registers;
                                firstArgument = ins.B;
                                argumentCount = 1;
                                resultRegister = ins.A;
                                constructed = WsValue.Null;
                                goto call;
                            }
                            registers[ins.A] = WsValue.FromBoolean(WsMembers.IsInstance(registers[ins.B], type, heap, _valueClasses));
                            pc++;
                            continue;
                        }
                        case WsOpCode.ToText:
                            _text = new TextCall(new WsTextBuilder(heap, registers[ins.B], true), null, ins.A);
                            goto text;
                        case WsOpCode.Throw:
                            _thrown = registers[ins.A];
                            _thrownError = null;
                            _origin = OriginOf(frame.Function, pc);
                            goto throwing;
                        case WsOpCode.Rethrow:
                            // 'throw;', or a finally's end: the exception again, from where it was first thrown.
                            _thrown = registers[ins.A];
                            _thrownError = null;
                            _origin = registers[ins.B].Kind == WsValueKind.Int64 ? registers[ins.B].AsInt64 : OriginOf(frame.Function, pc);
                            goto throwing;
                        case WsOpCode.Return:
                        case WsOpCode.ReturnValue:
                        {
                            var result = ins.OpCode == WsOpCode.ReturnValue ? registers[ins.A] : WsValue.Null;
                            if (depth == 0) return WsExecutionResult.Success with { Instructions = executed };
                            heap.RemoveLastRoots(frame.Closure);
                            heap.RemoveLastRoots(registers);
                            Array.Clear(registers, 0, frame.Function.RegisterCount); // So that nothing stays reachable.
                            if (frame.DisposeChain is not null) goto disposed;
                            if (frame.Text is not null)
                            {
                                _returned = result;
                                goto texted;
                            }
                            frame.Closure[0] = WsValue.Null;
                            if (frame.Constructed.Kind != WsValueKind.Null) result = frame.Constructed; // What new made: an instance, or a collection.
                            frame.Constructed = WsValue.Null;
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
                            error = new WsRuntimeError(WsDiagnosticCodes.BytecodeVerificationFailure,
                                $"The operation {(int)ins.OpCode} is unknown.");
                            break;
                    }
                    goto failed;

                call:
                    // The call the operations above set up: a built-in at once, a closure in a new frame, its arguments
                    // in its first registers, after the instance it runs on if it is a method (section 3.7).
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
                        if (_texts is not null && _texts[callee.Handle] && argumentCount == 1 && NeedsText(arguments[firstArgument]))
                        {
                            _text = new TextCall(new WsTextBuilder(heap, arguments[firstArgument], false), builtin, resultRegister);
                            goto text;
                        }
                        error = CallBuiltin(builtin, arguments, firstArgument, argumentCount, out var builtinResult);
                        if (error is not null) goto failed;
                        registers[resultRegister] = builtinResult;
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
                        if (++depth == frames.Length) Array.Resize(ref frames, frames.Length * 2);
                        var called = frames[depth] ??= new Frame();
                        if (called.Registers.Length < function.RegisterCount)
                        {
                            called.Registers = new WsValue[function.RegisterCount];
                        }
                        var calledRegisters = called.Registers;
                        if (withThis == 1) calledRegisters[0] = self;
                        for (var i = 0; i < argumentCount; i++) calledRegisters[withThis + i] = arguments[firstArgument + i];
                        called.Function = function;
                        called.Code = codes[index];
                        called.Cells = closureCells;
                        called.Closure[0] = callee;
                        called.ResultRegister = resultRegister;
                        called.Constructed = constructed;
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
                        error = BeginDisposal(self, argumentCount, out var chain);
                        if (error is not null) goto failed;
                        if (chain is null)
                        {
                            registers[resultRegister] = WsValue.Null;
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
                        if (disposedTo >= 0) registers[disposedTo] = WsValue.Null;
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
                            frame.Registers = registers = new WsValue[nextFunction.RegisterCount];
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
                        registers[building.Destination] = written;
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
                        registers = frame.Registers;
                        cells = frame.Cells;
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
                            registers[handler.Value] = exception;
                            registers[handler.Origin] = WsValue.FromInt64(_origin);
                            pc = handler.Target;
                            continue;
                        }
                        if (WsExceptions.DisposalGoesOn && frame.DisposeChain is { } chain && frame.DisposeNext < chain.Length)
                        {
                            // Its next dispose method runs; the exception waits for the last, a later one replacing it.
                            Hold(frame);
                            heap.RemoveLastRoots(frame.Closure);
                            heap.RemoveLastRoots(registers);
                            Array.Clear(registers, 0, frame.Function.RegisterCount);
                            goto nextDisposal;
                        }
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
                return Failure(WsDiagnosticCodes.BytecodeVerificationFailure, $"The bytecode is invalid: {exception.Message}",
                    SpanOf(frame.Function, pc)) with { Instructions = executed };
            }
        }

        /// <summary>A cell's value; WS2004 if it has none yet, which only a hoisted function can meet (section 3.5).</summary>
        private WsRuntimeError? ReadCell(WsValue cell, WsFunction? owner, int register, out WsValue value)
        {
            if (heap.TryReadCell(cell, out value)) return null;
            var name = owner?.LocalNames is { } names && register < names.Count ? $"'{names[register]}'" : "A variable";
            return new WsRuntimeError(WsDiagnosticCodes.ReadBeforeInitialization,
                $"{name} is used by a function called before its declaration has run.");
        }

        /// <summary>
        /// Arithmetic: two int64s, without overflow, or two binary64s, here; anything else, and every error, by
        /// <see cref="WsOperators"/>, which gives the same results.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Arithmetic(WsOpCode opCode, WsValue[] registers, in WsInstruction ins, out WsRuntimeError? error)
        {
            var (left, right) = (registers[ins.B], registers[ins.C]);
            error = null;
            if (left.Kind == WsValueKind.Int64 && right.Kind == WsValueKind.Int64)
            {
                var (a, b) = (left.AsInt64, right.AsInt64);
                switch (opCode)
                {
                    case WsOpCode.Add:
                    {
                        var sum = unchecked(a + b);
                        if (((a ^ sum) & (b ^ sum)) >= 0) return Done(WsValue.FromInt64(sum), registers, ins);
                        break;
                    }
                    case WsOpCode.Subtract:
                    {
                        var difference = unchecked(a - b);
                        if (((a ^ b) & (a ^ difference)) >= 0) return Done(WsValue.FromInt64(difference), registers, ins);
                        break;
                    }
                    case WsOpCode.Multiply:
                    {
                        var high = Math.BigMul(a, b, out var low);
                        if (high == low >> 63) return Done(WsValue.FromInt64(low), registers, ins);
                        break;
                    }
                    default:
                        return Done(WsValue.FromBinary64((double)a / b), registers, ins);
                }
            }
            else if (left.Kind == WsValueKind.Binary64 && right.Kind == WsValueKind.Binary64)
            {
                var (x, y) = (left.AsBinary64, right.AsBinary64);
                return Done(WsValue.FromBinary64(opCode switch
                {
                    WsOpCode.Add => x + y,
                    WsOpCode.Subtract => x - y,
                    WsOpCode.Multiply => x * y,
                    _ => x / y
                }), registers, ins);
            }
            if (!WsOperators.TryBinary(ArithmeticOf(opCode), left, right, heap, out var value, out error)) return false;
            registers[ins.A] = value;
            return true;
        }

        /// <summary>
        /// A comparison: two int64s, or two binary64s (IEEE 754's, so that NaN is unordered and unequal to itself),
        /// here; anything else by <see cref="WsOperators"/>, which gives the same results.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Compare(WsOpCode opCode, WsValue[] registers, in WsInstruction ins, out WsRuntimeError? error)
        {
            var (left, right) = (registers[ins.B], registers[ins.C]);
            error = null;
            if (left.Kind == WsValueKind.Int64 && right.Kind == WsValueKind.Int64)
            {
                var (a, b) = (left.AsInt64, right.AsInt64);
                return Done(WsValue.FromBoolean(opCode switch
                {
                    WsOpCode.Equal => a == b,
                    WsOpCode.NotEqual => a != b,
                    WsOpCode.Less => a < b,
                    WsOpCode.LessOrEqual => a <= b,
                    WsOpCode.Greater => a > b,
                    _ => a >= b
                }), registers, ins);
            }
            if (left.Kind == WsValueKind.Binary64 && right.Kind == WsValueKind.Binary64)
            {
                var (x, y) = (left.AsBinary64, right.AsBinary64);
                return Done(WsValue.FromBoolean(opCode switch
                {
                    WsOpCode.Equal => x == y,
                    WsOpCode.NotEqual => x != y,
                    WsOpCode.Less => x < y,
                    WsOpCode.LessOrEqual => x <= y,
                    WsOpCode.Greater => x > y,
                    _ => x >= y
                }), registers, ins);
            }
            if (!WsOperators.TryCompare(ComparisonOf(opCode), left, right, heap, out var value, out error)) return false;
            registers[ins.A] = value;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Done(WsValue value, WsValue[] registers, in WsInstruction ins)
        {
            registers[ins.A] = value;
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

        private static bool[]? TextBuiltins(WsCompiledModule module, IWsBuiltinFunction[] builtins)
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

        private static bool[] BuiltinClasses(WsCompiledModule module)
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
                // Array, Map and Set make collections, and classes extend them; the others, for now, neither (section 3.7).
                if (i == (int)WsValueClass.Array) heap.MarkCollectionClass(made, WsValueKind.Array);
                else if (i == (int)WsValueClass.Map) heap.MarkCollectionClass(made, WsValueKind.Map);
                else if (i == (int)WsValueClass.Set) heap.MarkCollectionClass(made, WsValueKind.Set);
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
        private bool TryGetEqual(WsValue type, out WsValue method)
        {
            var classValue = heap.ClassOfValue(type);
            if (classValue.Kind == WsValueKind.Class && heap.TryGetMethod(classValue, "equal", out method)
                && method.Kind == WsValueKind.Function)
            {
                return true;
            }
            return WsMembers.TryGetObjectMethod(WsMember.Equal, "equal", heap, out method);
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

        private long OriginOf(WsFunction function, int pc)
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
        private static int FindHandler(IReadOnlyList<WsHandler> handlers, int pc)
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
        }

        private static WsRuntimeError WrongArgumentCount(string name, int arity, int count) =>
            new(WsDiagnosticCodes.WrongArgumentCount,
                $"'{name}' takes {arity} {(arity == 1 ? "argument" : "arguments")}, not {count}.");

        private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        /// <summary>
        /// A closure of the module's function, its captures taken from the making function's registers or its own
        /// captures, as the function's capture table says.
        /// </summary>
        private bool TryMakeClosure(int function, WsValue[] registers, WsValue[] cells, out WsValue closure, out WsRuntimeError? error)
        {
            var captures = module.Functions[function].Captures;
            var captured = new WsValue[captures.Count];
            for (var i = 0; i < captured.Length; i++)
            {
                captured[i] = captures[i].FromLocal ? registers[captures[i].Index] : cells[captures[i].Index];
                if (captured[i].Kind != WsValueKind.Cell) throw new InvalidOperationException("A capture is not a cell.");
            }
            return heap.TryAllocateClosure(function, captured, out closure, out error);
        }

        /// <summary>
        /// A class value: its constructor's and its methods' closures made now, kept as roots until the class holds
        /// them, since making each may collect (section 3.7).
        /// </summary>
        private bool TryMakeClass(WsClassInfo info, WsValue baseClass, WsValue[] registers, WsValue[] cells, out WsValue made,
            out WsRuntimeError? error)
        {
            made = WsValue.Null;
            var closures = new WsValue[info.Methods.Count + info.StaticMethods.Count + 1];
            heap.AddRoots(closures);
            try
            {
                if (info.Constructor != -1 && !TryMakeClosure(info.Constructor, registers, cells, out closures[0], out error)) return false;
                var methods = new Dictionary<string, WsValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.Methods.Count; i++)
                {
                    if (!TryMakeClosure(info.Methods[i].Function, registers, cells, out closures[i + 1], out error)) return false;
                    methods.Add(info.Methods[i].Name, closures[i + 1]);
                }
                var statics = new Dictionary<string, WsValue>(StringComparer.Ordinal);
                for (var i = 0; i < info.StaticMethods.Count; i++)
                {
                    var slot = info.Methods.Count + 1 + i;
                    if (!TryMakeClosure(info.StaticMethods[i].Function, registers, cells, out closures[slot], out error)) return false;
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

        /// <summary>
        /// Calls a built-in with the <paramref name="count"/> arguments from <paramref name="first"/> on, copied out, so
        /// that its result may overwrite them. Its failure, even an exception, is a runtime error.
        /// </summary>
        private WsRuntimeError? CallBuiltin(IWsBuiltinFunction builtin, WsValue[] values, int first, int count, out WsValue result)
        {
            result = WsValue.Null;
            var arguments = new WsValue[count];
            for (var i = 0; i < arguments.Length; i++) arguments[i] = values[first + i];
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

        /// <summary>
        /// Calls a built-in with its arguments copied out of the registers, so that its result may overwrite them.
        /// Its failure, even an exception, is a runtime error.
        /// </summary>
        private WsRuntimeError? CallBuiltin(IWsBuiltinFunction builtin, WsValue[] registers, in WsInstruction instruction)
        {
            var arguments = new WsValue[instruction.D];
            for (var i = 0; i < arguments.Length; i++) arguments[i] = registers[instruction.C + i];
            try
            {
                if (!builtin.TryInvoke(arguments, heap, out var result, out var error))
                {
                    return error ?? new WsRuntimeError(WsDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed.");
                }
                registers[instruction.A] = result;
                return null;
            }
            catch (Exception exception)
            {
                return new WsRuntimeError(WsDiagnosticCodes.InvalidOperation, $"'{builtin.Name}' failed: {exception.Message}");
            }
        }
    }

    private static WsArithmeticOperator ArithmeticOf(WsOpCode opCode) => opCode switch
    {
        WsOpCode.Add => WsArithmeticOperator.Add,
        WsOpCode.Subtract => WsArithmeticOperator.Subtract,
        WsOpCode.Multiply => WsArithmeticOperator.Multiply,
        _ => WsArithmeticOperator.Divide
    };

    private static WsComparisonOperator ComparisonOf(WsOpCode opCode) => opCode switch
    {
        WsOpCode.Equal => WsComparisonOperator.Equal,
        WsOpCode.NotEqual => WsComparisonOperator.NotEqual,
        WsOpCode.Less => WsComparisonOperator.Less,
        WsOpCode.LessOrEqual => WsComparisonOperator.LessOrEqual,
        WsOpCode.Greater => WsComparisonOperator.Greater,
        _ => WsComparisonOperator.GreaterOrEqual
    };

    private static WsSourceSpan SpanOf(WsFunction function, int pc) =>
        function.Spans is { } spans && pc < spans.Count ? spans[pc] : new WsSourceSpan(0, 0);

    private static WsExecutionResult Failure(string code, string message, WsSourceSpan span) =>
        new(false, new WsDiagnostic(code, WsDiagnosticSeverity.Error, message, span));
}
