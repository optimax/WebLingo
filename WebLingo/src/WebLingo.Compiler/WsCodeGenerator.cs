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
            if (generated is not null) functions.Add(generated with { HasThis = function.HasThis, OptionalCount = function.OptionalCount });
        }
        if (main is null || functions.Count != program.Functions.Count) return null;

        var imports = new WsImport[program.Builtins.Count];
        foreach (var builtin in program.Builtins) imports[builtin.Index] = new WsImport(builtin.Name, builtin.Arity);
        return new WsCompiledModule(moduleName, module.Constants, imports, main)
        {
            Functions = functions,
            Classes = WsClassTable.Of(program),
            ErrorClass = program.ErrorClass,
            ObjectClass = program.ObjectClass,
            ValueClasses = program.ValueClasses,
            ErrorClasses = ErrorClassesOf(program)
        };
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
            Captures = captures,
            Handlers = generator._handlers
        };
    }

    private static List<WsErrorClass> ErrorClassesOf(WsIrProgram program)
    {
        var classes = new List<WsErrorClass>();
        foreach (var (code, index) in program.ErrorClasses) classes.Add(new WsErrorClass(code, index));
        return classes;
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
            case WsIrForEach forEach:
                EmitForEach(forEach);
                break;
            case WsIrBreak breakStatement:
                LeaveGuards(_loops.Peek().Guards);
                _loops.Peek().Breaks.Add(EmitJump(WsOpCode.Jump, 0, breakStatement.Span));
                ReopenGuards(_loops.Peek().Guards);
                break;
            case WsIrContinue continueStatement:
                LeaveGuards(_loops.Peek().Guards);
                _loops.Peek().Continues.Add(EmitJump(WsOpCode.Jump, 0, continueStatement.Span));
                ReopenGuards(_loops.Peek().Guards);
                break;
            case WsIrUsing usingStatement:
            {
                // Its body inside a guard that disposes on each way out; its handler disposes, then throws again.
                foreach (var initialize in usingStatement.Initialize) EmitStatement(initialize);
                var guard = OpenGuard(usingStatement.Span, disposes: usingStatement.Resource);
                EmitStatement(usingStatement.Body);
                EndGuard(guard);
                EmitDispose(usingStatement.Resource, usingStatement.Span);
                if (guard.Ranges.Count == 0) break;
                var skip = EmitJump(WsOpCode.Jump, 0, usingStatement.Span);
                AddHandlers(guard, usingStatement.Value, usingStatement.Origin);
                EmitDispose(usingStatement.Resource, usingStatement.Span);
                EmitRethrow(usingStatement.Value, usingStatement.Origin, usingStatement.Span);
                PatchJump(skip);
                break;
            }
            case WsIrTry tryStatement:
                EmitTry(tryStatement);
                break;
            case WsIrThrow throwStatement:
                Emit(new WsInstruction(WsOpCode.Throw, EmitExpression(throwStatement.Value, null)), throwStatement.Span);
                break;
            case WsIrRethrow rethrow:
                EmitRethrow(rethrow.Value, rethrow.Origin, rethrow.Span);
                break;
            case WsIrNewCell cell:
                Emit(new WsInstruction(WsOpCode.NewCell, cell.Local), cell.Span);
                break;
            case WsIrDefaultParameter parameter:
            {
                // Skipped when the caller gave the parameter; otherwise its default, into its register or its cell.
                var skip = _code.Count;
                Emit(new WsInstruction(WsOpCode.JumpIfGiven, parameter.Local), parameter.Span);
                if (parameter.Captured)
                {
                    var mark = _nextRegister;
                    var value = EmitExpression(parameter.Value, null);
                    Emit(new WsInstruction(WsOpCode.StoreCell, parameter.Local, value), parameter.Span);
                    _nextRegister = mark;
                }
                else
                {
                    EmitExpression(parameter.Value, parameter.Local);
                }
                _code[skip] = _code[skip] with { B = _code.Count };
                break;
            }
            case WsIrBoxLocal box:
                Emit(new WsInstruction(WsOpCode.BoxRegister, box.Local), box.Span);
                break;
            case WsIrCopyCells copies:
                foreach (var local in copies.Locals) Emit(new WsInstruction(WsOpCode.CopyCell, local), copies.Span);
                break;
            case WsIrReturn returnStatement:
                if (returnStatement.Value is null)
                {
                    LeaveGuards(0);
                    Emit(new WsInstruction(WsOpCode.Return), returnStatement.Span);
                    ReopenGuards(0);
                    break;
                }
                if (_guards.Count == 0)
                {
                    Emit(new WsInstruction(WsOpCode.ReturnValue, EmitExpression(returnStatement.Value, null)), returnStatement.Span);
                    break;
                }
                {
                    // The value, in a register of its own, before the guards' cleanups run.
                    var value = EmitExpression(returnStatement.Value, Allocate());
                    LeaveGuards(0);
                    Emit(new WsInstruction(WsOpCode.ReturnValue, value), returnStatement.Span);
                    ReopenGuards(0);
                }
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
            case WsIrIs test:
            {
                var mark = _nextRegister;
                var value = EmitExpression(test.Value, null);
                if (value < _locals && Assigns(test.Class, value))
                {
                    var copy = Allocate();
                    Emit(new WsInstruction(WsOpCode.Move, copy, value), test.Value.Span);
                    value = copy;
                }
                var type = EmitExpression(test.Class, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.IsInstance, destination, value, type), test.Span);
                return destination;
            }
            case WsIrToText text:
            {
                var mark = _nextRegister;
                var operand = EmitExpression(text.Value, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.ToText, destination, operand), text.Span);
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
            case WsIrNewSet set:
            {
                // The values in consecutive registers, as an array's elements.
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i < set.Elements.Count; i++) Allocate();
                for (var i = 0; i < set.Elements.Count; i++) EmitExpression(set.Elements[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.NewSet, destination, set.Elements.Count == 0 ? 0 : first, set.Elements.Count),
                    set.Span);
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
                Emit(new WsInstruction(WsOpCode.GetIndex, destination, subject, key, index.IsOptional ? 1 : 0), index.Span);
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
                Emit(new WsInstruction(WsOpCode.GetMember, destination, subject, StringConstant(member.Name), member.IsOptional ? 1 : 0),
                    member.Span);
                return destination;
            }
            case WsIrStoreMember store:
            {
                var mark = _nextRegister;
                var subject = KeptIfAssigned(EmitExpression(store.Target, null), store.Target.Span, store.Value);
                var value = EmitExpression(store.Value, null);
                Emit(new WsInstruction(WsOpCode.SetMember, subject, StringConstant(store.Name), value), store.Span);
                if (target is { } destination)
                {
                    if (destination != value) Emit(new WsInstruction(WsOpCode.Move, destination, value), store.Span);
                    _nextRegister = mark;
                    return destination;
                }
                _nextRegister = value >= _locals ? Math.Max(mark, value + 1) : mark;
                return value;
            }
            case WsIrMakeClass made:
            {
                var mark = _nextRegister;
                var baseClass = made.Base is null ? 0 : EmitExpression(made.Base, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.MakeClass, destination, made.Class, baseClass, made.Base is null ? 0 : 1), made.Span);
                return destination;
            }
            case WsIrGetSuper super:
            {
                var mark = _nextRegister;
                var classValue = EmitExpression(super.Class, null);
                var self = EmitExpression(super.This, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.GetSuper, destination, classValue, self, StringConstant(super.Name)), super.Span);
                return destination;
            }
            case WsIrNew created:
            {
                var mark = _nextRegister;
                var subject = KeptIfAssigned(EmitExpression(created.Class, null), created.Class.Span, ToArray(created.Arguments));
                var first = _nextRegister;
                for (var i = 0; i < created.Arguments.Count; i++) Allocate();
                for (var i = 0; i < created.Arguments.Count; i++) EmitExpression(created.Arguments[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WsInstruction(WsOpCode.New, destination, subject, created.Arguments.Count == 0 ? 0 : first,
                    created.Arguments.Count), created.Span);
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
        WsIrToText text => Assigns(text.Value, local),
        WsIrIs test => Assigns(test.Value, local) || Assigns(test.Class, local),
        WsIrBinary binary => Assigns(binary.Left, local) || Assigns(binary.Right, local),
        WsIrLogical logical => Assigns(logical.Left, local) || Assigns(logical.Right, local),
        WsIrCallBuiltin call => AnyAssigns(call.Arguments, local),
        WsIrCall call => Assigns(call.Callee, local) || AnyAssigns(call.Arguments, local),
        WsIrStoreCell store => Assigns(store.Value, local),
        WsIrStoreCapture store => Assigns(store.Value, local),
        WsIrIndex index => Assigns(index.Target, local) || Assigns(index.Index, local),
        WsIrNewArray array => AnyAssigns(array.Elements, local),
        WsIrNewSet set => AnyAssigns(set.Elements, local),
        WsIrNewMap map => AnyEntryAssigns(map.Entries, local),
        WsIrStoreIndex store => Assigns(store.Target, local) || Assigns(store.Index, local) || Assigns(store.Value, local),
        WsIrMember member => Assigns(member.Target, local),
        WsIrCallMethod call => Assigns(call.Target, local) || AnyAssigns(call.Arguments, local),
        WsIrStoreMember store => Assigns(store.Target, local) || Assigns(store.Value, local),
        WsIrNew created => Assigns(created.Class, local) || AnyAssigns(created.Arguments, local),
        WsIrMakeClass { Base: { } baseClass } => Assigns(baseClass, local),
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

    /// <summary>A copy of the expressions, without LINQ, which the portable core does not use.</summary>
    private static WsIrExpression[] ToArray(IReadOnlyList<WsIrExpression> expressions)
    {
        var array = new WsIrExpression[expressions.Count];
        for (var i = 0; i < array.Length; i++) array[i] = expressions[i];
        return array;
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
        var jumps = new LoopJumps { Guards = _guards.Count };
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

    /// <summary>
    /// <c>foreach</c>: the collection into its local, the position 0 into the next; then <c>ForEachNext</c>, which
    /// leaves the loop when there is nothing more, or writes the next item and goes on to the body.
    /// </summary>
    private void EmitForEach(WsIrForEach loop)
    {
        var collection = loop.FirstLocal;
        EmitExpression(loop.Collection, collection);
        _nextRegister = _locals;
        Emit(new WsInstruction(WsOpCode.LoadConstant, collection + 1, Int64Constant(0)), loop.Span);
        var jumps = new LoopJumps { Guards = _guards.Count };
        _loops.Push(jumps);
        var top = _code.Count;
        Emit(new WsInstruction(WsOpCode.ForEachNext, 0, collection, loop.ItemCount), loop.Collection.Span);
        foreach (var item in loop.CapturedItems) Emit(new WsInstruction(WsOpCode.BoxRegister, item), loop.Span);
        EmitStatement(loop.Body);
        foreach (var at in jumps.Continues) PatchJump(at);
        Emit(new WsInstruction(WsOpCode.Jump, top), loop.Span);
        _code[top] = _code[top] with { A = _code.Count };
        foreach (var at in jumps.Breaks) PatchJump(at);
        _loops.Pop();
    }

    private sealed class LoopJumps
    {
        public List<int> Breaks { get; } = [];
        public List<int> Continues { get; } = [];
        /// <summary>How many guards were open where the loop began: a jump out of it leaves the rest.</summary>
        public int Guards { get; init; }
    }

    /// <summary>
    /// A protected region being generated, a <c>try</c>'s, its <c>catch</c>'s or a <c>using</c>'s (section 3.7): where it
    /// was last opened, or -1 while it is closed; the ranges of code it has covered; and what a jump out of it runs, a
    /// <c>finally</c>'s body or a <c>using</c>'s disposal, if anything.
    /// </summary>
    private sealed class Guard
    {
        public int OpenedAt;
        public List<(int Start, int End)> Ranges { get; } = [];
        public WsIrStatement? Finally { get; init; }
        public WsIrExpression? Disposes { get; init; }
        public WsSourceSpan Span { get; init; }
    }

    /// <summary>The open guards, innermost last.</summary>
    private readonly List<Guard> _guards = [];

    /// <summary>The function's handlers, innermost first, as each guard's handler is generated.</summary>
    private readonly List<WsHandler> _handlers = [];

    private Guard OpenGuard(WsSourceSpan span, WsIrStatement? cleanup = null, WsIrExpression? disposes = null)
    {
        var guard = new Guard { OpenedAt = _code.Count, Finally = cleanup, Disposes = disposes, Span = span  };
        _guards.Add(guard);
        return guard;
    }

    private void CloseGuard(Guard guard)
    {
        if (guard.OpenedAt >= 0 && guard.OpenedAt < _code.Count) guard.Ranges.Add((guard.OpenedAt, _code.Count));
        guard.OpenedAt = -1;
    }

    /// <summary>Ends the innermost guard, whose handler comes next.</summary>
    private void EndGuard(Guard guard)
    {
        CloseGuard(guard);
        _guards.RemoveAt(_guards.Count - 1);
    }

    /// <summary>The ranges of an ended guard become handlers that go to here, with the exception and its origin in these locals.</summary>
    private void AddHandlers(Guard guard, int value, int origin)
    {
        foreach (var (start, end) in guard.Ranges) _handlers.Add(new WsHandler(start, end, _code.Count, value, origin));
    }

    /// <summary>
    /// Leaves the guards from the innermost down to <paramref name="depth"/>, as a jump out of them does: each one's
    /// cleanup runs outside it, but inside those around it, so that a <c>finally</c> or a disposal that throws there is
    /// caught by them and not by its own handler. <see cref="ReopenGuards"/> opens them again after the jump.
    /// </summary>
    private void LeaveGuards(int depth)
    {
        for (var i = _guards.Count - 1; i >= depth; i--)
        {
            var guard = _guards[i];
            CloseGuard(guard);
            if (guard.Finally is null && guard.Disposes is null) continue;
            var inner = _guards.GetRange(i, _guards.Count - i);
            _guards.RemoveRange(i, inner.Count);
            if (guard.Disposes is { } resource) EmitDispose(resource, guard.Span);
            else EmitStatement(guard.Finally!);
            _guards.AddRange(inner);
        }
    }

    private void ReopenGuards(int depth)
    {
        for (var i = depth; i < _guards.Count; i++) _guards[i].OpenedAt = _code.Count;
    }

    /// <summary>
    /// <c>try</c>: its body inside its guards, the <c>catch</c>'s inner; the catch's handler after it, inside the
    /// finally's guard; then the finally's body on the way out, and its handler, which runs it and throws again.
    /// </summary>
    private void EmitTry(WsIrTry statement)
    {
        var finallyGuard = statement.Finally is { } cleanup ? OpenGuard(statement.Span, cleanup.Body) : null;
        var catchGuard = statement.Catch is not null ? OpenGuard(statement.Span) : null;
        EmitStatement(statement.Body);
        if (statement.Catch is { } handler)
        {
            EndGuard(catchGuard!);
            var skip = EmitJump(WsOpCode.Jump, 0, statement.Span);
            AddHandlers(catchGuard!, handler.Value, handler.Origin);
            foreach (var inner in handler.Body) EmitStatement(inner);
            PatchJump(skip);
        }
        if (statement.Finally is { } final)
        {
            EndGuard(finallyGuard!);
            EmitStatement(final.Body);
            var skip = EmitJump(WsOpCode.Jump, 0, statement.Span);
            AddHandlers(finallyGuard!, final.Value, final.Origin);
            EmitStatement(final.Body);
            EmitRethrow(final.Value, final.Origin, statement.Span);
            PatchJump(skip);
        }
    }

    private void EmitRethrow(int value, int origin, WsSourceSpan span) =>
        Emit(new WsInstruction(WsOpCode.Rethrow, value, origin), span);

    /// <summary>Disposes a resource, unless it is <c>null</c> (section 3.7).</summary>
    private void EmitDispose(WsIrExpression resource, WsSourceSpan span)
    {
        var mark = _nextRegister;
        var value = EmitExpression(resource, null);
        Emit(new WsInstruction(WsOpCode.Dispose, value), span);
        _nextRegister = mark;
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
