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
        public readonly WsValue[] Closure = new WsValue[1];
        public WsValue[] Cells = [];
        /// <summary>The next instruction, while the frame waits for a call it made to return.</summary>
        public int Pc;
        /// <summary>The caller's register that the call's result goes to.</summary>
        public int ResultRegister;
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
                            var captures = module.Functions[ins.B].Captures;
                            var captured = new WsValue[captures.Count];
                            for (var i = 0; i < captured.Length; i++)
                            {
                                captured[i] = captures[i].FromLocal ? registers[captures[i].Index] : cells[captures[i].Index];
                                if (captured[i].Kind != WsValueKind.Cell)
                                {
                                    throw new InvalidOperationException("A capture is not a cell.");
                                }
                            }
                            if (!heap.TryAllocateClosure(ins.B, captured, out var closure, out error)) break;
                            registers[ins.A] = closure;
                            pc++;
                            continue;
                        }
                        case WsOpCode.LoadBuiltin:
                            registers[ins.A] = WsValue.FromHandle(WsValueKind.Builtin, ins.B);
                            pc++;
                            continue;
                        case WsOpCode.CallBuiltin:
                            error = CallBuiltin(builtins[ins.B], registers, ins);
                            if (error is not null) break;
                            pc++;
                            continue;
                        case WsOpCode.Call:
                        {
                            // Register B must hold a function taking D arguments: a built-in is called at once, a closure
                            // in a new frame, its arguments in its first registers. The compiler checks what it can, and
                            // this the rest (WS2005, WS2006).
                            var callee = registers[ins.B];
                            if (callee.Kind == WsValueKind.Builtin)
                            {
                                var builtin = builtins[callee.Handle];
                                error = builtin.Arity != ins.D
                                    ? WrongArgumentCount(builtin.Name, builtin.Arity, ins.D)
                                    : CallBuiltin(builtin, registers, ins);
                                if (error is not null) break;
                                pc++;
                                continue;
                            }
                            if (callee.Kind != WsValueKind.Function)
                            {
                                var what = Capitalized(WsValueFormatter.Describe(callee));
                                error = new WsRuntimeError(WsDiagnosticCodes.NotAFunction,
                                    $"{what} is not a function, and cannot be called.");
                                break;
                            }
                            var (index, closureCells) = heap.GetClosure(callee);
                            var function = module.Functions[index];
                            if (function.ParameterCount != ins.D)
                            {
                                error = WrongArgumentCount(function.Name, function.ParameterCount, ins.D);
                                break;
                            }
                            if (depth + 1 >= maxCallDepth)
                            {
                                error = new WsRuntimeError(WsDiagnosticCodes.BudgetExceeded,
                                    $"The calls nest more than {maxCallDepth} deep, which this execution allows.");
                                break;
                            }

                            frame.Pc = pc + 1;
                            if (++depth == frames.Length) Array.Resize(ref frames, frames.Length * 2);
                            var called = frames[depth] ??= new Frame();
                            if (called.Registers.Length < function.RegisterCount)
                            {
                                called.Registers = new WsValue[function.RegisterCount];
                            }
                            var calledRegisters = called.Registers;
                            for (var i = 0; i < ins.D; i++) calledRegisters[i] = registers[ins.C + i];
                            called.Function = function;
                            called.Code = codes[index];
                            called.Cells = closureCells;
                            called.Closure[0] = callee;
                            called.ResultRegister = ins.A;
                            heap.AddRoots(calledRegisters);
                            heap.AddRoots(called.Closure);

                            frame = called;
                            code = called.Code;
                            registers = calledRegisters;
                            cells = closureCells;
                            pc = 0;
                            continue;
                        }
                        case WsOpCode.GetMember:
                        {
                            var name = names[ins.C];
                            if (!WsMembers.TryGet(registers[ins.B], name.Member, name.Text, heap, out var member, out error)) break;
                            registers[ins.A] = member;
                            pc++;
                            continue;
                        }
                        case WsOpCode.CallMethod:
                        {
                            var name = names[ins.B];
                            if (!WsMembers.TryCall(registers[ins.C], name.Member, name.Text, registers, ins.C + 1, ins.D, heap,
                                    out var result, out error))
                            {
                                break;
                            }
                            registers[ins.A] = result;
                            pc++;
                            continue;
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
                        case WsOpCode.Return:
                        case WsOpCode.ReturnValue:
                        {
                            var result = ins.OpCode == WsOpCode.ReturnValue ? registers[ins.A] : WsValue.Null;
                            if (depth == 0) return WsExecutionResult.Success with { Instructions = executed };
                            heap.RemoveLastRoots(frame.Closure);
                            heap.RemoveLastRoots(registers);
                            Array.Clear(registers, 0, frame.Function.RegisterCount); // So that nothing stays reachable.
                            frame.Closure[0] = WsValue.Null;
                            var resultRegister = frame.ResultRegister;

                            frame = frames[--depth];
                            code = frame.Code;
                            registers = frame.Registers;
                            cells = frame.Cells;
                            pc = frame.Pc;
                            registers[resultRegister] = result;
                            continue;
                        }
                        default:
                            // The verifier admits no other operation; this only guards against a verifier that did.
                            error = new WsRuntimeError(WsDiagnosticCodes.BytecodeVerificationFailure,
                                $"The operation {(int)ins.OpCode} is unknown.");
                            break;
                    }
                    return Failure(error!.Code, error.Message, SpanOf(frame.Function, pc)) with { Instructions = executed };
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

        private static WsRuntimeError WrongArgumentCount(string name, int arity, int count) =>
            new(WsDiagnosticCodes.WrongArgumentCount,
                $"'{name}' takes {arity} {(arity == 1 ? "argument" : "arguments")}, not {count}.");

        private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

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
