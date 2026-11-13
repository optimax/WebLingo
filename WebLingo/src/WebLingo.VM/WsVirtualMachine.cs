using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.VM;

/// <summary>
/// The register VM (specification, sections 5.2 and 8.4, "VM"). It verifies a module before running it, links the
/// module's imports to the host's built-ins by name and arity, and runs it with a <c>switch</c> over the operations
/// (section 5.6), synchronously, on the calling thread, which stands for the context's owner worker. A runtime
/// error stops the execution and is returned with the source span of the instruction that raised it. Nothing a
/// module or a built-in does can crash the VM: a failure is a result.
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

        var function = module.EntryPoint;
        if (function.RegisterCount > context.Resources.MaxRegisters)
        {
            return Failure(WsDiagnosticCodes.BudgetExceeded,
                $"The program needs {function.RegisterCount} registers; this execution allows {context.Resources.MaxRegisters}.",
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

        // The execution's own heap (worker-local, section 4.3), whose roots are the constants and the registers.
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

        return Run(function, constants, builtins, context, heap);
    }

    private static WsExecutionResult Run(WsFunction function, WsValue[] constants, IWsBuiltinFunction[] builtins,
        WsExecutionContext context, WsHeap heap)
    {
        var registers = new WsValue[function.RegisterCount];
        heap.AddRoots(registers);
        var code = function.Code;
        var budget = context.InstructionBudget;
        var cancellation = context.Cancellation;
        var executed = 0L;
        var pc = 0;

        while (true)
        {
            if (executed >= budget)
            {
                return Failure(WsDiagnosticCodes.BudgetExceeded,
                    $"The program ran {executed} instructions, its whole budget, and was stopped.", SpanOf(function, pc));
            }
            if (cancellation is not null && executed % CancellationInterval == 0 && cancellation.IsCancellationRequested)
            {
                return Failure(WsDiagnosticCodes.Cancelled, "The program was cancelled.", SpanOf(function, pc));
            }
            executed++;

            var instruction = code[pc];
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
                    if (WsOperators.TryNegate(registers[instruction.B], out var negated, out error))
                    {
                        registers[instruction.A] = negated;
                    }
                    break;
                case WsOpCode.Plus:
                    if (WsOperators.TryPlus(registers[instruction.B], out var same, out error))
                    {
                        registers[instruction.A] = same;
                    }
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
                    error = Call(builtins[instruction.B], registers, instruction, heap);
                    break;
                case WsOpCode.Jump:
                    pc = instruction.A;
                    continue;
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
                        pc = instruction.B;
                        continue;
                    }
                    break;
                }
                case WsOpCode.CheckBoolean:
                    error = WsOperators.CheckBoolean(registers[instruction.A], "'&&' and '||' need Booleans:");
                    break;
                case WsOpCode.Return:
                    return WsExecutionResult.Success;
                default:
                    // The verifier admits no other operation; this only guards against a verifier that did.
                    error = new WsRuntimeError(WsDiagnosticCodes.BytecodeVerificationFailure,
                        $"The operation {(int)instruction.OpCode} is unknown.");
                    break;
            }
            if (error is not null) return Failure(error.Code, error.Message, SpanOf(function, pc));
            pc++;
        }
    }

    /// <summary>
    /// Calls a built-in with its arguments copied out of the registers, so that its result may overwrite them.
    /// Its failure, even an exception, is a runtime error.
    /// </summary>
    private static WsRuntimeError? Call(IWsBuiltinFunction builtin, WsValue[] registers, WsInstruction instruction,
        WsHeap heap)
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
        function.Spans is { } spans ? spans[pc] : new WsSourceSpan(0, 0);

    private static WsExecutionResult Failure(string code, string message, WsSourceSpan span) =>
        new(false, new WsDiagnostic(code, WsDiagnosticSeverity.Error, message, span));
}
