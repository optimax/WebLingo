using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// Generates register bytecode from the lowered IR (specification, section 8.4, "Bytecode generator"). Locals take
/// registers 0 to n - 1; temporaries are allocated above them, stack-wise, and freed after each statement. An
/// operation writes its result directly into the register that wants it (a local being initialized or assigned)
/// where that is safe. A local is used where it is, without a copy, unless a later operand of the same operation
/// assigns it, which would change the value already evaluated (left-to-right evaluation, section 15.2).
/// </summary>
public sealed class WsCodeGenerator
{
    private readonly WsIrProgram _program;
    private readonly int _locals;
    private readonly List<WsInstruction> _code = [];
    private readonly List<WsSourceSpan> _spans = [];
    private readonly List<WsConstant> _constants = [];
    private readonly Dictionary<long, int> _int64Constants = [];
    private readonly Dictionary<long, int> _binary64Constants = []; // By IEEE bits, so that 0.0 and -0.0 stay apart.
    private int _nextRegister;
    private int _registerCount;

    private WsCodeGenerator(WsIrProgram program)
    {
        _program = program;
        _locals = program.LocalNames.Count;
        _nextRegister = _locals;
        _registerCount = _locals;
    }

    /// <summary>Generates the module; null, with a diagnostic, if it needs more registers than a function may have.</summary>
    public static WsCompiledModule? Generate(WsIrProgram program, string moduleName, List<WsDiagnostic> diagnostics)
    {
        var generator = new WsCodeGenerator(program);
        foreach (var statement in program.Statements) generator.EmitStatement(statement);
        generator.Emit(new WsInstruction(WsOpCode.Return), new WsSourceSpan(0, 0));

        if (generator._registerCount > WsFunction.MaxRegisters)
        {
            diagnostics.Add(new WsDiagnostic(WsDiagnosticCodes.ProgramTooLarge, WsDiagnosticSeverity.Error,
                $"The program needs {generator._registerCount} registers; a function may have {WsFunction.MaxRegisters}.",
                new WsSourceSpan(0, 0)));
            return null;
        }

        var imports = new WsImport[program.Builtins.Count];
        foreach (var builtin in program.Builtins) imports[builtin.Index] = new WsImport(builtin.Name, builtin.Arity);
        var main = new WsFunction("main", generator._registerCount, generator._locals, generator._code, generator._spans,
            program.LocalNames);
        return new WsCompiledModule(moduleName, generator._constants, imports, main);
    }

    private void EmitStatement(WsIrStatement statement)
    {
        switch (statement)
        {
            case WsIrInitializeLocal initialize:
                EmitExpression(initialize.Value, initialize.Local);
                break;
            case WsIrEvaluate evaluate:
                EmitExpression(evaluate.Expression, null);
                break;
            case WsIrBlock block:
                foreach (var inner in block.Statements) EmitStatement(inner);
                break;
            case WsIrIf conditional:
            {
                var condition = EmitExpression(conditional.Condition, null);
                var skipThen = EmitJump(WsOpCode.JumpIfFalse, condition, conditional.Condition.Span);
                _nextRegister = _locals;
                EmitStatement(conditional.Then);
                if (conditional.Else is null)
                {
                    PatchJump(skipThen);
                    break;
                }
                var skipElse = EmitJump(WsOpCode.Jump, 0, conditional.Span);
                PatchJump(skipThen);
                EmitStatement(conditional.Else);
                PatchJump(skipElse);
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
        }
        _nextRegister = _locals; // Temporaries live within a statement.
    }

    /// <summary>
    /// Emits an expression and returns the register that holds its value: <paramref name="target"/> if given, else
    /// a temporary, or a local's own register when the expression reads it.
    /// </summary>
    private int EmitExpression(WsIrExpression expression, int? target)
    {
        switch (expression)
        {
            case WsIrInt64Constant constant:
            {
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.LoadConstant, destination, Int64Constant(constant.Value)), constant.Span);
                return destination;
            }
            case WsIrBinary64Constant constant:
            {
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.LoadConstant, destination, Binary64Constant(constant.Value)), constant.Span);
                return destination;
            }
            case WsIrBooleanConstant constant:
            {
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.LoadBoolean, destination, constant.Value ? 1 : 0), constant.Span);
                return destination;
            }
            case WsIrNullConstant constant:
            {
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.LoadNull, destination), constant.Span);
                return destination;
            }
            case WsIrLogical logical:
            {
                // The result goes to a temporary of its own, then to the target: the right operand may read the
                // target, which must still hold its old value then.
                var mark = _nextRegister;
                var result = Allocate();
                EmitExpression(logical.Left, result);
                var jump = _code.Count;
                Emit(new WsInstruction(logical.IsAnd ? WsOpCode.JumpIfFalse : WsOpCode.JumpIfTrue, result), logical.Span);
                EmitExpression(logical.Right, result);
                Emit(new WsInstruction(WsOpCode.CheckBoolean, result), logical.Right.Span);
                _code[jump] = _code[jump] with { B = _code.Count };
                if (target is not { } destination)
                {
                    _nextRegister = mark + 1;
                    return result;
                }
                Emit(new WsInstruction(WsOpCode.Move, destination, result), logical.Span);
                _nextRegister = mark;
                return destination;
            }
            case WsIrLoadLocal load:
            {
                if (target is not { } destination || destination == load.Local) return load.Local;
                Emit(new WsInstruction(WsOpCode.Move, destination, load.Local), load.Span);
                return destination;
            }
            case WsIrStoreLocal store:
            {
                var value = EmitExpression(store.Value, store.Local);
                if (value != store.Local) Emit(new WsInstruction(WsOpCode.Move, store.Local, value), store.Span);
                if (target is not { } destination || destination == store.Local) return store.Local;
                Emit(new WsInstruction(WsOpCode.Move, destination, store.Local), store.Span);
                return destination;
            }
            case WsIrUnary unary:
            {
                var mark = _nextRegister;
                var operand = EmitExpression(unary.Operand, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                var opCode = unary.Operator switch
                {
                    WsUnaryOperator.Negate => WsOpCode.Negate,
                    WsUnaryOperator.Plus => WsOpCode.Plus,
                    _ => WsOpCode.Not
                };
                Emit(new WsInstruction(opCode, destination, operand), unary.Span);
                return destination;
            }
            case WsIrBinary binary:
            {
                var mark = _nextRegister;
                var left = EmitExpression(binary.Left, null);
                if (left < _locals && Assigns(binary.Right, left))
                {
                    // The right operand assigns the local the left one read: keep the value read.
                    var copy = Allocate();
                    Emit(new WsInstruction(WsOpCode.Move, copy, left), binary.Left.Span);
                    left = copy;
                }
                var right = EmitExpression(binary.Right, null);
                _nextRegister = mark; // The operation reads its operands before it writes its destination.
                var destination = target ?? Allocate();
                Emit(new WsInstruction(OpCodeOf(binary.Operator), destination, left, right), binary.Span);
                return destination;
            }
            case WsIrCallBuiltin call:
            {
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i < call.Arguments.Count; i++) Allocate();
                for (var i = 0; i < call.Arguments.Count; i++) EmitExpression(call.Arguments[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.CallBuiltin, destination, call.Builtin, call.Arguments.Count == 0 ? 0 : first,
                    call.Arguments.Count), call.Span);
                return destination;
            }
            default:
                throw new InvalidOperationException($"Unknown expression {expression.GetType().Name}.");
        }
    }

    /// <summary>Whether evaluating the expression may assign the local.</summary>
    private static bool Assigns(WsIrExpression expression, int local) => expression switch
    {
        WsIrStoreLocal store => store.Local == local || Assigns(store.Value, local),
        WsIrUnary unary => Assigns(unary.Operand, local),
        WsIrBinary binary => Assigns(binary.Left, local) || Assigns(binary.Right, local),
        WsIrLogical logical => Assigns(logical.Left, local) || Assigns(logical.Right, local),
        WsIrCallBuiltin call => AnyAssigns(call.Arguments, local),
        _ => false
    };

    private static bool AnyAssigns(IReadOnlyList<WsIrExpression> expressions, int local)
    {
        foreach (var expression in expressions)
        {
            if (Assigns(expression, local)) return true;
        }
        return false;
    }

    private static WsOpCode OpCodeOf(WsBinaryOperator op) => op switch
    {
        WsBinaryOperator.Add => WsOpCode.Add,
        WsBinaryOperator.Subtract => WsOpCode.Subtract,
        WsBinaryOperator.Multiply => WsOpCode.Multiply,
        WsBinaryOperator.Divide => WsOpCode.Divide,
        WsBinaryOperator.Equal => WsOpCode.Equal,
        WsBinaryOperator.NotEqual => WsOpCode.NotEqual,
        WsBinaryOperator.Less => WsOpCode.Less,
        WsBinaryOperator.LessOrEqual => WsOpCode.LessOrEqual,
        WsBinaryOperator.Greater => WsOpCode.Greater,
        WsBinaryOperator.GreaterOrEqual => WsOpCode.GreaterOrEqual,
        _ => throw new InvalidOperationException($"Unknown operator {op}.")
    };

    private int Allocate()
    {
        var register = _nextRegister++;
        if (_nextRegister > _registerCount) _registerCount = _nextRegister;
        return register;
    }

    private int Int64Constant(long value)
    {
        if (_int64Constants.TryGetValue(value, out var index)) return index;
        index = _constants.Count;
        _constants.Add(WsConstant.Int64(value));
        _int64Constants.Add(value, index);
        return index;
    }

    /// <summary>Emits a jump whose target <see cref="PatchJump"/> fills in later, and returns where it is.</summary>
    private int EmitJump(WsOpCode opCode, int condition, WsSourceSpan span)
    {
        var at = _code.Count;
        Emit(opCode == WsOpCode.Jump ? new WsInstruction(opCode) : new WsInstruction(opCode, condition), span);
        return at;
    }

    /// <summary>Points the jump at <paramref name="at"/> to the next instruction to be emitted.</summary>
    private void PatchJump(int at)
    {
        var jump = _code[at];
        _code[at] = jump.OpCode == WsOpCode.Jump ? jump with { A = _code.Count } : jump with { B = _code.Count };
    }

    private int Binary64Constant(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        if (_binary64Constants.TryGetValue(bits, out var index)) return index;
        index = _constants.Count;
        _constants.Add(WsConstant.Binary64(value));
        _binary64Constants.Add(bits, index);
        return index;
    }

    private void Emit(WsInstruction instruction, WsSourceSpan span)
    {
        _code.Add(instruction);
        _spans.Add(span);
    }
}
