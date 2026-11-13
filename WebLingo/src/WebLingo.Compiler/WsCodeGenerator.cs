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
    private readonly ModuleState _module;
    private readonly int _locals;
    private readonly List<WsInstruction> _code = [];
    private readonly List<WsSourceSpan> _spans = [];
    private readonly Stack<LoopJumps> _loops = new();
    private int _nextRegister;
    private int _registerCount;

    private WsCodeGenerator(ModuleState module, int locals)
    {
        _module = module;
        _locals = locals;
        _nextRegister = _locals;
        _registerCount = _locals;
    }

    private List<WsConstant> _constants => _module.Constants;
    private Dictionary<long, int> _int64Constants => _module.Int64Constants;
    private Dictionary<long, int> _binary64Constants => _module.Binary64Constants;
    private Dictionary<string, int> _stringConstants => _module.StringConstants;

    /// <summary>
    /// Generates the module: the program's statements as its entry point, and each function it declares; null, with a
    /// diagnostic, if a function needs more registers than a function may have.
    /// </summary>
    public static WsCompiledModule? Generate(WsIrProgram program, string moduleName, List<WsDiagnostic> diagnostics)
    {
        var module = new ModuleState();
        var main = GenerateFunction(module, "main", 0, program.LocalNames, [], program.Statements, diagnostics);
        var functions = new List<WsFunction>();
        foreach (var function in program.Functions)
        {
            var captures = new List<WsCapture>();
            foreach (var capture in function.Captures) captures.Add(new WsCapture(capture.FromLocal, capture.Index));
            var name = function.Name.Length == 0 ? "function" : function.Name;
            var generated = GenerateFunction(module, name, function.ParameterCount, function.LocalNames, captures, function.Body,
                diagnostics);
            if (generated is not null) functions.Add(generated);
        }
        if (main is null || functions.Count != program.Functions.Count) return null;

        var imports = new WsImport[program.Builtins.Count];
        foreach (var builtin in program.Builtins) imports[builtin.Index] = new WsImport(builtin.Name, builtin.Arity);
        return new WsCompiledModule(moduleName, module.Constants, imports, main) { Functions = functions };
    }

    private static WsFunction? GenerateFunction(ModuleState module, string name, int parameterCount,
        IReadOnlyList<string> localNames, IReadOnlyList<WsCapture> captures, IReadOnlyList<WsIrStatement> body,
        List<WsDiagnostic> diagnostics)
    {
        var generator = new WsCodeGenerator(module, localNames.Count);
        foreach (var statement in body) generator.EmitStatement(statement);
        generator.Emit(new WsInstruction(WsOpCode.Return), new WsSourceSpan(0, 0));
        if (generator._registerCount > WsFunction.MaxRegisters)
        {
            diagnostics.Add(new WsDiagnostic(WsDiagnosticCodes.ProgramTooLarge, WsDiagnosticSeverity.Error,
                $"'{name}' needs {generator._registerCount} registers; a function may have {WsFunction.MaxRegisters}.",
                new WsSourceSpan(0, 0)));
            return null;
        }
        return new WsFunction(name, generator._registerCount, generator._locals, generator._code, generator._spans, localNames)
        {
            ParameterCount = parameterCount,
            Captures = captures
        };
    }

    /// <summary>What the functions of one module share: its constants.</summary>
    private sealed class ModuleState
    {
        public List<WsConstant> Constants { get; } = [];
        public Dictionary<long, int> Int64Constants { get; } = [];
        public Dictionary<long, int> Binary64Constants { get; } = []; // By IEEE bits, so that 0.0 and -0.0 stay apart.
        public Dictionary<string, int> StringConstants { get; } = new(StringComparer.Ordinal);
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
            case WsIrLoop loop:
                EmitLoop(loop);
                break;
            case WsIrBreak breakStatement:
                _loops.Peek().Breaks.Add(EmitJump(WsOpCode.Jump, 0, breakStatement.Span));
                break;
            case WsIrContinue continueStatement:
                _loops.Peek().Continues.Add(EmitJump(WsOpCode.Jump, 0, continueStatement.Span));
                break;
            case WsIrNewCell cell:
                Emit(new WsInstruction(WsOpCode.NewCell, cell.Local), cell.Span);
                break;
            case WsIrBoxLocal box:
                Emit(new WsInstruction(WsOpCode.BoxRegister, box.Local), box.Span);
                break;
            case WsIrCopyCells copies:
                foreach (var local in copies.Locals) Emit(new WsInstruction(WsOpCode.CopyCell, local), copies.Span);
                break;
            case WsIrReturn returnStatement:
                if (returnStatement.Value is null)
                {
                    Emit(new WsInstruction(WsOpCode.Return), returnStatement.Span);
                    break;
                }
                Emit(new WsInstruction(WsOpCode.ReturnValue, EmitExpression(returnStatement.Value, null)), returnStatement.Span);
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
            case WsIrStringConstant constant:
            {
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.LoadConstant, destination, StringConstant(constant.Value)), constant.Span);
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
            case WsIrLoadCell load:
            {
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.LoadCell, destination, load.Local), load.Span);
                return destination;
            }
            case WsIrLoadCapture load:
            {
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.LoadCapture, destination, load.Capture), load.Span);
                return destination;
            }
            case WsIrStoreCell store:
            {
                var value = EmitExpression(store.Value, target);
                Emit(new WsInstruction(WsOpCode.StoreCell, store.Local, value), store.Span);
                return value;
            }
            case WsIrStoreCapture store:
            {
                var value = EmitExpression(store.Value, target);
                Emit(new WsInstruction(WsOpCode.StoreCapture, store.Capture, value), store.Span);
                return value;
            }
            case WsIrMakeClosure closure:
            {
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.MakeClosure, destination, closure.Function), closure.Span);
                return destination;
            }
            case WsIrLoadBuiltin builtin:
            {
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.LoadBuiltin, destination, builtin.Builtin), builtin.Span);
                return destination;
            }
            case WsIrCall call:
            {
                var mark = _nextRegister;
                var callee = EmitExpression(call.Callee, null);
                if (callee < _locals && AnyAssigns(call.Arguments, callee))
                {
                    var copy = Allocate();
                    Emit(new WsInstruction(WsOpCode.Move, copy, callee), call.Callee.Span);
                    callee = copy;
                }
                var first = _nextRegister;
                for (var i = 0; i < call.Arguments.Count; i++) Allocate();
                for (var i = 0; i < call.Arguments.Count; i++) EmitExpression(call.Arguments[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.Call, destination, callee, call.Arguments.Count == 0 ? 0 : first,
                    call.Arguments.Count), call.Span);
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
            case WsIrNewArray array:
            {
                // The elements in consecutive registers, as a call's arguments.
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i < array.Elements.Count; i++) Allocate();
                for (var i = 0; i < array.Elements.Count; i++) EmitExpression(array.Elements[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.NewArray, destination, array.Elements.Count == 0 ? 0 : first, array.Elements.Count),
                    array.Span);
                return destination;
            }
            case WsIrNewMap map:
            {
                // Each key, then its value, in consecutive registers.
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i < 2 * map.Entries.Count; i++) Allocate();
                for (var i = 0; i < map.Entries.Count; i++)
                {
                    Emit(new WsInstruction(WsOpCode.LoadConstant, first + 2 * i, StringConstant(map.Entries[i].Key)), map.Span);
                    EmitExpression(map.Entries[i].Value, first + 2 * i + 1);
                }
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.NewMap, destination, map.Entries.Count == 0 ? 0 : first, map.Entries.Count),
                    map.Span);
                return destination;
            }
            case WsIrIndex index:
            {
                var mark = _nextRegister;
                var subject = KeptIfAssigned(EmitExpression(index.Target, null), index.Target.Span, index.Index);
                var key = EmitExpression(index.Index, null);
                _nextRegister = mark; // The operation reads its operands before it writes its destination.
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.GetIndex, destination, subject, key), index.Span);
                return destination;
            }
            case WsIrStoreIndex store:
            {
                var mark = _nextRegister;
                var subject = KeptIfAssigned(EmitExpression(store.Target, null), store.Target.Span, store.Index, store.Value);
                var key = KeptIfAssigned(EmitExpression(store.Index, null), store.Index.Span, store.Value);
                var value = EmitExpression(store.Value, null);
                Emit(new WsInstruction(WsOpCode.SetIndex, subject, key, value), store.Span);
                if (target is { } destination)
                {
                    if (destination != value) Emit(new WsInstruction(WsOpCode.Move, destination, value), store.Span);
                    _nextRegister = mark;
                    return destination;
                }
                // The value is the result: a temporary of its own stays allocated, a local is read where it is.
                _nextRegister = value >= _locals ? Math.Max(mark, value + 1) : mark;
                return value;
            }
            case WsIrMember member:
            {
                var mark = _nextRegister;
                var subject = EmitExpression(member.Target, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.GetMember, destination, subject, StringConstant(member.Name)), member.Span);
                return destination;
            }
            case WsIrCallMethod call:
            {
                // The target, then the arguments, in consecutive registers.
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i <= call.Arguments.Count; i++) Allocate();
                EmitExpression(call.Target, first);
                for (var i = 0; i < call.Arguments.Count; i++) EmitExpression(call.Arguments[i], first + 1 + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.CallMethod, destination, StringConstant(call.Name), first, call.Arguments.Count),
                    call.Span);
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
        WsIrCall call => Assigns(call.Callee, local) || AnyAssigns(call.Arguments, local),
        WsIrStoreCell store => Assigns(store.Value, local),
        WsIrStoreCapture store => Assigns(store.Value, local),
        WsIrIndex index => Assigns(index.Target, local) || Assigns(index.Index, local),
        WsIrNewArray array => AnyAssigns(array.Elements, local),
        WsIrNewMap map => AnyEntryAssigns(map.Entries, local),
        WsIrStoreIndex store => Assigns(store.Target, local) || Assigns(store.Index, local) || Assigns(store.Value, local),
        WsIrMember member => Assigns(member.Target, local),
        WsIrCallMethod call => Assigns(call.Target, local) || AnyAssigns(call.Arguments, local),
        _ => false
    };

    /// <summary>
    /// A register an operand was evaluated into, copied first if it is a local that a later operand of the same
    /// operation assigns: the operation must see the value read, not the one assigned (section 15.2).
    /// </summary>
    private int KeptIfAssigned(int register, WsSourceSpan span, params WsIrExpression[] later)
    {
        if (register >= _locals || !AnyAssigns(later, register)) return register;
        var copy = Allocate();
        Emit(new WsInstruction(WsOpCode.Move, copy, register), span);
        return copy;
    }

    private static bool AnyEntryAssigns(IReadOnlyList<WsIrMapEntry> entries, int local)
    {
        foreach (var entry in entries)
        {
            if (Assigns(entry.Value, local)) return true;
        }
        return false;
    }

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

    /// <summary>
    /// <c>while</c> and <c>for</c>: test, body, step, back to the test. <c>do … while</c>: body, then the test, which
    /// jumps back if true. A <c>continue</c> goes to the step (or the test), a <c>break</c> past the end.
    /// </summary>
    private void EmitLoop(WsIrLoop loop)
    {
        var jumps = new LoopJumps();
        _loops.Push(jumps);
        var top = _code.Count;
        int? exit = null;
        if (loop.TestFirst && loop.Condition is { } test)
        {
            var condition = EmitExpression(test, null);
            exit = EmitJump(WsOpCode.JumpIfFalse, condition, test.Span);
            _nextRegister = _locals;
        }
        EmitStatement(loop.Body);
        foreach (var at in jumps.Continues) PatchJump(at);
        if (loop.PerIteration is { } perIteration) EmitStatement(perIteration);
        if (loop.Step is { } step)
        {
            EmitExpression(step, null);
            _nextRegister = _locals;
        }
        if (!loop.TestFirst && loop.Condition is { } last)
        {
            var condition = EmitExpression(last, null);
            Emit(new WsInstruction(WsOpCode.JumpIfTrue, condition, top), last.Span);
            _nextRegister = _locals;
        }
        else
        {
            Emit(new WsInstruction(WsOpCode.Jump, top), loop.Span);
        }
        if (exit is { } exitJump) PatchJump(exitJump);
        foreach (var at in jumps.Breaks) PatchJump(at);
        _loops.Pop();
    }

    private sealed class LoopJumps
    {
        public List<int> Breaks { get; } = [];
        public List<int> Continues { get; } = [];
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

    private int StringConstant(string value)
    {
        if (_stringConstants.TryGetValue(value, out var index)) return index;
        index = _constants.Count;
        _constants.Add(WsConstant.String(value));
        _stringConstants.Add(value, index);
        return index;
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
