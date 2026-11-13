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
        heap.AddRoots(constants);
        for (var i = 0; i < constants.Length; i++)
        {
            var constant = module.Constants[i];
            if (constant.Kind == WsConstantKind.String)
            {
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

        return new Execution(module, constants, builtins, context, heap).Run();
    }

    /// <summary>A function running: its registers, the closure it runs as (its roots), and where it is.</summary>
    private sealed class Frame(WsFunction function, WsValue[] registers, WsValue[] closure, WsValue[] cells)
    {
        public WsFunction Function { get; } = function;
        public WsValue[] Registers { get; } = registers;
        /// <summary>The closure, as the one root that keeps its captured cells alive; empty for the entry point.</summary>
        public WsValue[] Closure { get; } = closure;
        public WsValue[] Cells { get; } = cells;
        public int Pc { get; set; }
        /// <summary>The caller's register that the call's result goes to.</summary>
        public int ResultRegister { get; set; }
    }

    private sealed class Execution(WsCompiledModule module, WsValue[] constants, IWsBuiltinFunction[] builtins,
        WsExecutionContext context, WsHeap heap)
    {
        private readonly Stack<Frame> _callers = new();

        public WsExecutionResult Run()
        {
            var frame = new Frame(module.EntryPoint, new WsValue[module.EntryPoint.RegisterCount], [], []);
            heap.AddRoots(frame.Registers);
            var budget = context.InstructionBudget;
            var cancellation = context.Cancellation;
            var executed = 0L;
            while (true)
            {
                var pc = frame.Pc;
                if (executed >= budget)
                {
                    return Failure(WsDiagnosticCodes.BudgetExceeded,
                        $"The program ran {executed} instructions, its whole budget, and was stopped.", SpanOf(frame.Function, pc));
                }
                if (cancellation is not null && executed % CancellationInterval == 0 && cancellation.IsCancellationRequested)
                {
                    return Failure(WsDiagnosticCodes.Cancelled, "The program was cancelled.", SpanOf(frame.Function, pc));
                }
                executed++;

                WsRuntimeError? error;
                Frame? next;
                try
                {
                    error = Step(frame, out next);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                                      or IndexOutOfRangeException)
                {
                    // Only bytecode the verifier cannot judge (a register of the wrong kind) gets here.
                    error = new WsRuntimeError(WsDiagnosticCodes.BytecodeVerificationFailure,
                        $"The bytecode is invalid: {exception.Message}");
                    next = frame;
                }
                if (error is not null) return Failure(error.Code, error.Message, SpanOf(frame.Function, pc));
                if (next is null) return WsExecutionResult.Success;
                frame = next;
            }
        }

        /// <summary>Runs the frame's next instruction; <paramref name="next"/> is the frame to go on with, or null at the end.</summary>
        private WsRuntimeError? Step(Frame frame, out Frame? next)
        {
            next = frame;
            var registers = frame.Registers;
            var instruction = frame.Function.Code[frame.Pc];
            WsRuntimeError? error = null;
            switch (instruction.OpCode)
            {
                case WsOpCode.LoadConstant:
                    registers[instruction.A] = constants[instruction.B];
                    break;
                case WsOpCode.Move:
                    registers[instruction.A] = registers[instruction.B];
                    break;
                case WsOpCode.Negate:
                    if (WsOperators.TryNegate(registers[instruction.B], out var negated, out error)) registers[instruction.A] = negated;
                    break;
                case WsOpCode.Plus:
                    if (WsOperators.TryPlus(registers[instruction.B], out var same, out error)) registers[instruction.A] = same;
                    break;
                case WsOpCode.Add:
                case WsOpCode.Subtract:
                case WsOpCode.Multiply:
                case WsOpCode.Divide:
                    if (WsOperators.TryBinary(ArithmeticOf(instruction.OpCode), registers[instruction.B],
                            registers[instruction.C], heap, out var value, out error))
                    {
                        registers[instruction.A] = value;
                    }
                    break;
                case WsOpCode.CallBuiltin:
                    error = CallBuiltin(builtins[instruction.B], registers, instruction);
                    break;
                case WsOpCode.LoadNull:
                    registers[instruction.A] = WsValue.Null;
                    break;
                case WsOpCode.LoadBoolean:
                    registers[instruction.A] = WsValue.FromBoolean(instruction.B != 0);
                    break;
                case WsOpCode.Not:
                    if (WsOperators.TryNot(registers[instruction.B], out var not, out error)) registers[instruction.A] = not;
                    break;
                case WsOpCode.Equal:
                case WsOpCode.NotEqual:
                case WsOpCode.Less:
                case WsOpCode.LessOrEqual:
                case WsOpCode.Greater:
                case WsOpCode.GreaterOrEqual:
                    if (WsOperators.TryCompare(ComparisonOf(instruction.OpCode), registers[instruction.B],
                            registers[instruction.C], heap, out var compared, out error))
                    {
                        registers[instruction.A] = compared;
                    }
                    break;
                case WsOpCode.JumpIfFalse:
                case WsOpCode.JumpIfTrue:
                {
                    var condition = registers[instruction.A];
                    error = WsOperators.CheckBoolean(condition, "A condition");
                    if (error is null && condition.AsBoolean == (instruction.OpCode == WsOpCode.JumpIfTrue))
                    {
                        frame.Pc = instruction.B;
                        return null;
                    }
                    break;
                }
                case WsOpCode.CheckBoolean:
                    error = WsOperators.CheckBoolean(registers[instruction.A], "'&&' and '||' need Booleans:");
                    break;
                case WsOpCode.NewCell:
                    heap.TryAllocateCell(WsValue.Null, false, out registers[instruction.A], out error);
                    break;
                case WsOpCode.BoxRegister:
                    if (heap.TryAllocateCell(registers[instruction.A], true, out var boxed, out error)) registers[instruction.A] = boxed;
                    break;
                case WsOpCode.CopyCell:
                {
                    var hasValue = heap.TryReadCell(registers[instruction.A], out var held);
                    if (heap.TryAllocateCell(held, hasValue, out var copy, out error)) registers[instruction.A] = copy;
                    break;
                }
                case WsOpCode.LoadCell:
                    error = ReadCell(registers[instruction.B], frame.Function, instruction.B, out registers[instruction.A]);
                    break;
                case WsOpCode.StoreCell:
                    heap.WriteCell(registers[instruction.A], registers[instruction.B]);
                    break;
                case WsOpCode.LoadCapture:
                    error = ReadCell(frame.Cells[instruction.B], null, instruction.B, out registers[instruction.A]);
                    break;
                case WsOpCode.StoreCapture:
                    heap.WriteCell(frame.Cells[instruction.A], registers[instruction.B]);
                    break;
                case WsOpCode.MakeClosure:
                {
                    var captures = module.Functions[instruction.B].Captures;
                    var cells = new WsValue[captures.Count];
                    for (var i = 0; i < cells.Length; i++)
                    {
                        cells[i] = captures[i].FromLocal ? registers[captures[i].Index] : frame.Cells[captures[i].Index];
                        if (cells[i].Kind != WsValueKind.Cell) throw new InvalidOperationException("A capture is not a cell.");
                    }
                    if (heap.TryAllocateClosure(instruction.B, cells, out var closure, out error)) registers[instruction.A] = closure;
                    break;
                }
                case WsOpCode.LoadBuiltin:
                    registers[instruction.A] = WsValue.FromHandle(WsValueKind.Builtin, instruction.B);
                    break;
                case WsOpCode.Call:
                    return Call(frame, instruction, out next);
                case WsOpCode.Jump:
                    frame.Pc = instruction.A;
                    return null;
                case WsOpCode.Return:
                case WsOpCode.ReturnValue:
                {
                    var result = instruction.OpCode == WsOpCode.ReturnValue ? registers[instruction.A] : WsValue.Null;
                    if (_callers.Count == 0)
                    {
                        next = null;
                        return null;
                    }
                    heap.RemoveLastRoots(frame.Closure);
                    heap.RemoveLastRoots(frame.Registers);
                    var caller = _callers.Pop();
                    caller.Registers[frame.ResultRegister] = result;
                    next = caller;
                    return null;
                }
                default:
                    // The verifier admits no other operation; this only guards against a verifier that did.
                    error = new WsRuntimeError(WsDiagnosticCodes.BytecodeVerificationFailure,
                        $"The operation {(int)instruction.OpCode} is unknown.");
                    break;
            }
            if (error is null) frame.Pc++;
            return error;
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
        /// Calls the value in register B: a built-in at once, or a closure in a new frame, its arguments in its first
        /// registers. Anything else is not a function (WS2005), and a function given the wrong number of arguments is
        /// WS2006: the compiler checks what it can, and the VM the rest.
        /// </summary>
        private WsRuntimeError? Call(Frame frame, WsInstruction instruction, out Frame? next)
        {
            next = frame;
            var callee = frame.Registers[instruction.B];
            if (callee.Kind == WsValueKind.Builtin)
            {
                var builtin = builtins[callee.Handle];
                if (builtin.Arity != instruction.D) return WrongArgumentCount(builtin.Name, builtin.Arity, instruction.D);
                var error = CallBuiltin(builtin, frame.Registers, instruction);
                if (error is null) frame.Pc++;
                return error;
            }
            if (callee.Kind != WsValueKind.Function)
            {
                return new WsRuntimeError(WsDiagnosticCodes.NotAFunction,
                    $"{Capitalized(WsValueFormatter.Describe(callee))} is not a function, and cannot be called.");
            }
            var (index, cells) = heap.GetClosure(callee);
            var function = module.Functions[index];
            if (function.ParameterCount != instruction.D) return WrongArgumentCount(function.Name, function.ParameterCount, instruction.D);
            if (_callers.Count + 1 >= context.Resources.MaxCallDepth)
            {
                return new WsRuntimeError(WsDiagnosticCodes.BudgetExceeded,
                    $"The calls nest more than {context.Resources.MaxCallDepth} deep, which this execution allows.");
            }
            var registers = new WsValue[function.RegisterCount];
            for (var i = 0; i < instruction.D; i++) registers[i] = frame.Registers[instruction.C + i];
            var called = new Frame(function, registers, [callee], cells) { ResultRegister = instruction.A };
            heap.AddRoots(registers);
            heap.AddRoots(called.Closure);
            frame.Pc++;
            _callers.Push(frame);
            next = called;
            return null;
        }

        private static WsRuntimeError WrongArgumentCount(string name, int arity, int count) =>
            new(WsDiagnosticCodes.WrongArgumentCount,
                $"'{name}' takes {arity} {(arity == 1 ? "argument" : "arguments")}, not {count}.");

        private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        /// <summary>
        /// Calls a built-in with its arguments copied out of the registers, so that its result may overwrite them.
        /// Its failure, even an exception, is a runtime error.
        /// </summary>
        private WsRuntimeError? CallBuiltin(IWsBuiltinFunction builtin, WsValue[] registers, WsInstruction instruction)
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
