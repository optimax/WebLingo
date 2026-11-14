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
public sealed class WebLingoCodeGenerator
{
    private readonly ModuleState _module;
    private readonly int _locals;
    private readonly List<WebLingoInstruction> _code = [];
    private readonly List<WebLingoSourceSpan> _spans = [];
    private readonly Stack<LoopJumps> _loops = new();
    private int _nextRegister;
    private int _registerCount;

    private WebLingoCodeGenerator(ModuleState module, int locals)
    {
        _module = module;
        _locals = locals;
        _nextRegister = _locals;
        _registerCount = _locals;
    }

    private List<WebLingoConstant> _constants => _module.Constants;
    private Dictionary<long, int> _int64Constants => _module.Int64Constants;
    private Dictionary<long, int> _binary64Constants => _module.Binary64Constants;
    private Dictionary<string, int> _stringConstants => _module.StringConstants;

    /// <summary>
    /// Generates the module: the program's statements as its entry point, and each function it declares; null, with a
    /// diagnostic, if a function needs more registers than a function may have.
    /// </summary>
    public static WebLingoCompiledModule? Generate(WebLingoIrProgram program, string moduleName, List<WebLingoDiagnostic> diagnostics)
    {
        var module = new ModuleState();
        var main = GenerateFunction(module, "main", 0, program.LocalNames, [], program.Statements, diagnostics);
        var functions = new List<WebLingoFunction>();
        foreach (var function in program.Functions)
        {
            var captures = new List<WebLingoCapture>();
            foreach (var capture in function.Captures) captures.Add(new WebLingoCapture(capture.FromLocal, capture.Index));
            var name = function.Name.Length == 0 ? "function" : function.Name;
            var generated = GenerateFunction(module, name, function.ParameterCount, function.LocalNames, captures, function.Body,
                diagnostics);
            if (generated is not null) functions.Add(generated with
            {
                HasThis = function.HasThis,
                OptionalCount = function.OptionalCount,
                IsAsync = function.IsAsync,
                Source = function.Source
            });
        }
        if (main is null || functions.Count != program.Functions.Count) return null;

        var imports = new WebLingoImport[program.Builtins.Count];
        foreach (var builtin in program.Builtins) imports[builtin.Index] = new WebLingoImport(builtin.Name, builtin.Arity);
        return new WebLingoCompiledModule(moduleName, module.Constants, imports, main with { IsAsync = program.IsAsync })
        {
            Functions = functions,
            Classes = WebLingoClassTable.Of(program),
            Units = UnitsOf(program),
            ErrorClass = program.ErrorClass,
            ObjectClass = program.ObjectClass,
            ValueClasses = program.ValueClasses,
            ErrorClasses = ErrorClassesOf(program)
        };
    }

    /// <summary>The program's files, if it imports others (section 3.9).</summary>
    private static List<WebLingoUnitInfo> UnitsOf(WebLingoIrProgram program)
    {
        var units = new List<WebLingoUnitInfo>();
        foreach (var unit in program.Units) units.Add(new WebLingoUnitInfo(unit.Name, unit.Function, unit.Exports));
        return units;
    }

    private static WebLingoFunction? GenerateFunction(ModuleState module, string name, int parameterCount,
        IReadOnlyList<string> localNames, IReadOnlyList<WebLingoCapture> captures, IReadOnlyList<WebLingoIrStatement> body,
        List<WebLingoDiagnostic> diagnostics)
    {
        var generator = new WebLingoCodeGenerator(module, localNames.Count);
        foreach (var statement in body) generator.EmitStatement(statement);
        generator.Emit(new WebLingoInstruction(WebLingoOpCode.Return), new WebLingoSourceSpan(0, 0));
        if (generator._registerCount > WebLingoFunction.MaxRegisters)
        {
            diagnostics.Add(new WebLingoDiagnostic(WebLingoDiagnosticCodes.ProgramTooLarge, WebLingoDiagnosticSeverity.Error,
                $"'{name}' needs {generator._registerCount} registers; a function may have {WebLingoFunction.MaxRegisters}.",
                new WebLingoSourceSpan(0, 0)));
            return null;
        }
        return new WebLingoFunction(name, generator._registerCount, generator._locals, generator._code, generator._spans, localNames)
        {
            ParameterCount = parameterCount,
            Captures = captures,
            Handlers = generator._handlers
        };
    }

    private static List<WebLingoErrorClass> ErrorClassesOf(WebLingoIrProgram program)
    {
        var classes = new List<WebLingoErrorClass>();
        foreach (var (code, index) in program.ErrorClasses) classes.Add(new WebLingoErrorClass(code, index));
        return classes;
    }

    /// <summary>What the functions of one module share: its constants.</summary>
    private sealed class ModuleState
    {
        public List<WebLingoConstant> Constants { get; } = [];
        public Dictionary<long, int> Int64Constants { get; } = [];
        public Dictionary<long, int> Binary64Constants { get; } = []; // By IEEE bits, so that 0.0 and -0.0 stay apart.
        public Dictionary<string, int> StringConstants { get; } = new(StringComparer.Ordinal);
    }

    private void EmitStatement(WebLingoIrStatement statement)
    {
        switch (statement)
        {
            case WebLingoIrInitializeLocal initialize:
                EmitExpression(initialize.Value, initialize.Local);
                break;
            case WebLingoIrEvaluate evaluate:
                EmitExpression(evaluate.Expression, null);
                break;
            case WebLingoIrBlock block:
                foreach (var inner in block.Statements) EmitStatement(inner);
                break;
            case WebLingoIrLoop loop:
                EmitLoop(loop);
                break;
            case WebLingoIrForEach forEach:
                EmitForEach(forEach);
                break;
            case WebLingoIrBreak breakStatement:
                LeaveGuards(_loops.Peek().Guards);
                _loops.Peek().Breaks.Add(EmitJump(WebLingoOpCode.Jump, 0, breakStatement.Span));
                ReopenGuards(_loops.Peek().Guards);
                break;
            case WebLingoIrContinue continueStatement:
                LeaveGuards(_loops.Peek().Guards);
                _loops.Peek().Continues.Add(EmitJump(WebLingoOpCode.Jump, 0, continueStatement.Span));
                ReopenGuards(_loops.Peek().Guards);
                break;
            case WebLingoIrUsing usingStatement:
            {
                // Its body inside a guard that disposes on each way out; its handler disposes, then throws again.
                foreach (var initialize in usingStatement.Initialize) EmitStatement(initialize);
                var guard = OpenGuard(usingStatement.Span, disposes: usingStatement.Resource);
                EmitStatement(usingStatement.Body);
                EndGuard(guard);
                EmitDispose(usingStatement.Resource, usingStatement.Span);
                if (guard.Ranges.Count == 0) break;
                var skip = EmitJump(WebLingoOpCode.Jump, 0, usingStatement.Span);
                AddHandlers(guard, usingStatement.Value, usingStatement.Origin);
                EmitDispose(usingStatement.Resource, usingStatement.Span);
                EmitRethrow(usingStatement.Value, usingStatement.Origin, usingStatement.Span);
                PatchJump(skip);
                break;
            }
            case WebLingoIrTry tryStatement:
                EmitTry(tryStatement);
                break;
            case WebLingoIrThrow throwStatement:
                Emit(new WebLingoInstruction(WebLingoOpCode.Throw, EmitExpression(throwStatement.Value, null)), throwStatement.Span);
                break;
            case WebLingoIrRegisterUnit registered:
            {
                var mark = _nextRegister;
                var closure = EmitExpression(registered.Closure, null);
                _nextRegister = mark;
                Emit(new WebLingoInstruction(WebLingoOpCode.RegisterUnit, registered.Unit, closure), registered.Span);
                break;
            }
            case WebLingoIrExtend extension:
                Emit(new WebLingoInstruction(WebLingoOpCode.Extend, EmitExpression(extension.Target, null), extension.Class), extension.Span);
                break;
            case WebLingoIrRethrow rethrow:
                EmitRethrow(rethrow.Value, rethrow.Origin, rethrow.Span);
                break;
            case WebLingoIrNewCell cell:
                Emit(new WebLingoInstruction(WebLingoOpCode.NewCell, cell.Local), cell.Span);
                break;
            case WebLingoIrDefaultParameter parameter:
            {
                // Skipped when the caller gave the parameter; otherwise its default, into its register or its cell.
                var skip = _code.Count;
                Emit(new WebLingoInstruction(WebLingoOpCode.JumpIfGiven, parameter.Local), parameter.Span);
                if (parameter.Captured)
                {
                    var mark = _nextRegister;
                    var value = EmitExpression(parameter.Value, null);
                    Emit(new WebLingoInstruction(WebLingoOpCode.StoreCell, parameter.Local, value), parameter.Span);
                    _nextRegister = mark;
                }
                else
                {
                    EmitExpression(parameter.Value, parameter.Local);
                }
                _code[skip] = _code[skip] with { B = _code.Count };
                break;
            }
            case WebLingoIrBoxLocal box:
                Emit(new WebLingoInstruction(WebLingoOpCode.BoxRegister, box.Local), box.Span);
                break;
            case WebLingoIrCopyCells copies:
                foreach (var local in copies.Locals) Emit(new WebLingoInstruction(WebLingoOpCode.CopyCell, local), copies.Span);
                break;
            case WebLingoIrReturn returnStatement:
                if (returnStatement.Value is null)
                {
                    LeaveGuards(0);
                    Emit(new WebLingoInstruction(WebLingoOpCode.Return), returnStatement.Span);
                    ReopenGuards(0);
                    break;
                }
                if (_guards.Count == 0)
                {
                    Emit(new WebLingoInstruction(WebLingoOpCode.ReturnValue, EmitExpression(returnStatement.Value, null)), returnStatement.Span);
                    break;
                }
                {
                    // The value, in a register of its own, before the guards' cleanups run.
                    var value = EmitExpression(returnStatement.Value, Allocate());
                    LeaveGuards(0);
                    Emit(new WebLingoInstruction(WebLingoOpCode.ReturnValue, value), returnStatement.Span);
                    ReopenGuards(0);
                }
                break;
            case WebLingoIrIf conditional:
            {
                var condition = EmitExpression(conditional.Condition, null);
                var skipThen = EmitJump(WebLingoOpCode.JumpIfFalse, condition, conditional.Condition.Span);
                _nextRegister = _locals;
                EmitStatement(conditional.Then);
                if (conditional.Else is null)
                {
                    PatchJump(skipThen);
                    break;
                }
                var skipElse = EmitJump(WebLingoOpCode.Jump, 0, conditional.Span);
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
    private int EmitExpression(WebLingoIrExpression expression, int? target)
    {
        switch (expression)
        {
            case WebLingoIrInt64Constant constant:
            {
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.LoadConstant, destination, Int64Constant(constant.Value)), constant.Span);
                return destination;
            }
            case WebLingoIrBinary64Constant constant:
            {
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.LoadConstant, destination, Binary64Constant(constant.Value)), constant.Span);
                return destination;
            }
            case WebLingoIrBooleanConstant constant:
            {
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.LoadBoolean, destination, constant.Value ? 1 : 0), constant.Span);
                return destination;
            }
            case WebLingoIrStringConstant constant:
            {
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.LoadConstant, destination, StringConstant(constant.Value)), constant.Span);
                return destination;
            }
            case WebLingoIrNullConstant constant:
            {
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.LoadNull, destination), constant.Span);
                return destination;
            }
            case WebLingoIrLogical logical:
            {
                // The result goes to a temporary of its own, then to the target: the right operand may read the
                // target, which must still hold its old value then.
                var mark = _nextRegister;
                var result = Allocate();
                EmitExpression(logical.Left, result);
                var jump = _code.Count;
                Emit(new WebLingoInstruction(logical.IsAnd ? WebLingoOpCode.JumpIfFalse : WebLingoOpCode.JumpIfTrue, result), logical.Span);
                EmitExpression(logical.Right, result);
                Emit(new WebLingoInstruction(WebLingoOpCode.CheckBoolean, result), logical.Right.Span);
                _code[jump] = _code[jump] with { B = _code.Count };
                if (target is not { } destination)
                {
                    _nextRegister = mark + 1;
                    return result;
                }
                Emit(new WebLingoInstruction(WebLingoOpCode.Move, destination, result), logical.Span);
                _nextRegister = mark;
                return destination;
            }
            case WebLingoIrLoadCell load:
            {
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.LoadCell, destination, load.Local), load.Span);
                return destination;
            }
            case WebLingoIrLoadCapture load:
            {
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.LoadCapture, destination, load.Capture), load.Span);
                return destination;
            }
            case WebLingoIrStoreCell store:
            {
                var value = EmitExpression(store.Value, target);
                Emit(new WebLingoInstruction(WebLingoOpCode.StoreCell, store.Local, value), store.Span);
                return value;
            }
            case WebLingoIrStoreCapture store:
            {
                var value = EmitExpression(store.Value, target);
                Emit(new WebLingoInstruction(WebLingoOpCode.StoreCapture, store.Capture, value), store.Span);
                return value;
            }
            case WebLingoIrMakeClosure closure:
            {
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.MakeClosure, destination, closure.Function), closure.Span);
                return destination;
            }
            case WebLingoIrLoadBuiltin builtin:
            {
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.LoadBuiltin, destination, builtin.Builtin), builtin.Span);
                return destination;
            }
            case WebLingoIrCall call:
            {
                var mark = _nextRegister;
                var callee = EmitExpression(call.Callee, null);
                if (callee < _locals && AnyAssigns(call.Arguments, callee))
                {
                    var copy = Allocate();
                    Emit(new WebLingoInstruction(WebLingoOpCode.Move, copy, callee), call.Callee.Span);
                    callee = copy;
                }
                var first = _nextRegister;
                for (var i = 0; i < call.Arguments.Count; i++) Allocate();
                for (var i = 0; i < call.Arguments.Count; i++) EmitExpression(call.Arguments[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.Call, destination, callee, call.Arguments.Count == 0 ? 0 : first,
                    call.Arguments.Count), call.Span);
                return destination;
            }
            case WebLingoIrLoadLocal load:
            {
                if (target is not { } destination || destination == load.Local) return load.Local;
                Emit(new WebLingoInstruction(WebLingoOpCode.Move, destination, load.Local), load.Span);
                return destination;
            }
            case WebLingoIrStoreLocal store:
            {
                var value = EmitExpression(store.Value, store.Local);
                if (value != store.Local) Emit(new WebLingoInstruction(WebLingoOpCode.Move, store.Local, value), store.Span);
                if (target is not { } destination || destination == store.Local) return store.Local;
                Emit(new WebLingoInstruction(WebLingoOpCode.Move, destination, store.Local), store.Span);
                return destination;
            }
            case WebLingoIrAwait awaited:
            {
                var mark = _nextRegister;
                var operand = EmitExpression(awaited.Value, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.Await, destination, operand), awaited.Span);
                return destination;
            }
            case WebLingoIrIs test:
            {
                var mark = _nextRegister;
                var value = EmitExpression(test.Value, null);
                if (value < _locals && Assigns(test.Class, value))
                {
                    var copy = Allocate();
                    Emit(new WebLingoInstruction(WebLingoOpCode.Move, copy, value), test.Value.Span);
                    value = copy;
                }
                var type = EmitExpression(test.Class, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.IsInstance, destination, value, type), test.Span);
                return destination;
            }
            case WebLingoIrToText text:
            {
                var mark = _nextRegister;
                var operand = EmitExpression(text.Value, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.ToText, destination, operand), text.Span);
                return destination;
            }
            case WebLingoIrHold hold:
            {
                var mark = _nextRegister;
                var instance = EmitExpression(hold.Instance, null);
                var value = EmitExpression(hold.Value, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.Hold, destination, instance, value), hold.Span);
                return destination;
            }
            case WebLingoIrUnary unary:
            {
                var mark = _nextRegister;
                var operand = EmitExpression(unary.Operand, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                var opCode = unary.Operator switch
                {
                    WebLingoUnaryOperator.Negate => WebLingoOpCode.Negate,
                    WebLingoUnaryOperator.Plus => WebLingoOpCode.Plus,
                    _ => WebLingoOpCode.Not
                };
                Emit(new WebLingoInstruction(opCode, destination, operand), unary.Span);
                return destination;
            }
            case WebLingoIrBinary binary:
            {
                var mark = _nextRegister;
                var left = EmitExpression(binary.Left, null);
                if (left < _locals && Assigns(binary.Right, left))
                {
                    // The right operand assigns the local the left one read: keep the value read.
                    var copy = Allocate();
                    Emit(new WebLingoInstruction(WebLingoOpCode.Move, copy, left), binary.Left.Span);
                    left = copy;
                }
                var right = EmitExpression(binary.Right, null);
                _nextRegister = mark; // The operation reads its operands before it writes its destination.
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(OpCodeOf(binary.Operator), destination, left, right), binary.Span);
                return destination;
            }
            case WebLingoIrImport import:
            {
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.Import, destination, import.Unit), import.Span);
                return destination;
            }
            case WebLingoIrMakeModule made:
            {
                // Its exports, in consecutive registers.
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i < made.Values.Count; i++) Allocate();
                for (var i = 0; i < made.Values.Count; i++) EmitExpression(made.Values[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.MakeModule, destination, made.Unit, made.Values.Count == 0 ? 0 : first,
                    made.Values.Count), made.Span);
                return destination;
            }
            case WebLingoIrNewArray array:
            {
                // The elements in consecutive registers, as a call's arguments.
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i < array.Elements.Count; i++) Allocate();
                for (var i = 0; i < array.Elements.Count; i++) EmitExpression(array.Elements[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.NewArray, destination, array.Elements.Count == 0 ? 0 : first, array.Elements.Count),
                    array.Span);
                return destination;
            }
            case WebLingoIrNewSet set:
            {
                // The values in consecutive registers, as an array's elements.
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i < set.Elements.Count; i++) Allocate();
                for (var i = 0; i < set.Elements.Count; i++) EmitExpression(set.Elements[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.NewSet, destination, set.Elements.Count == 0 ? 0 : first, set.Elements.Count),
                    set.Span);
                return destination;
            }
            case WebLingoIrNewMap map:
            {
                // Each key, then its value, in consecutive registers.
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i < 2 * map.Entries.Count; i++) Allocate();
                for (var i = 0; i < map.Entries.Count; i++)
                {
                    Emit(new WebLingoInstruction(WebLingoOpCode.LoadConstant, first + 2 * i, StringConstant(map.Entries[i].Key)), map.Span);
                    EmitExpression(map.Entries[i].Value, first + 2 * i + 1);
                }
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.NewMap, destination, map.Entries.Count == 0 ? 0 : first, map.Entries.Count),
                    map.Span);
                return destination;
            }
            case WebLingoIrIndex index:
            {
                var mark = _nextRegister;
                var subject = KeptIfAssigned(EmitExpression(index.Target, null), index.Target.Span, index.Index);
                var key = EmitExpression(index.Index, null);
                _nextRegister = mark; // The operation reads its operands before it writes its destination.
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.GetIndex, destination, subject, key, index.IsOptional ? 1 : 0), index.Span);
                return destination;
            }
            case WebLingoIrStoreIndex store:
            {
                var mark = _nextRegister;
                var subject = KeptIfAssigned(EmitExpression(store.Target, null), store.Target.Span, store.Index, store.Value);
                var key = KeptIfAssigned(EmitExpression(store.Index, null), store.Index.Span, store.Value);
                var value = EmitExpression(store.Value, null);
                Emit(new WebLingoInstruction(WebLingoOpCode.SetIndex, subject, key, value), store.Span);
                if (target is { } destination)
                {
                    if (destination != value) Emit(new WebLingoInstruction(WebLingoOpCode.Move, destination, value), store.Span);
                    _nextRegister = mark;
                    return destination;
                }
                // The value is the result: a temporary of its own stays allocated, a local is read where it is.
                _nextRegister = value >= _locals ? Math.Max(mark, value + 1) : mark;
                return value;
            }
            case WebLingoIrMember member:
            {
                var mark = _nextRegister;
                var subject = EmitExpression(member.Target, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.GetMember, destination, subject, StringConstant(member.Name), member.IsOptional ? 1 : 0),
                    member.Span);
                return destination;
            }
            case WebLingoIrStoreMember store:
            {
                var mark = _nextRegister;
                var subject = KeptIfAssigned(EmitExpression(store.Target, null), store.Target.Span, store.Value);
                var value = EmitExpression(store.Value, null);
                Emit(new WebLingoInstruction(WebLingoOpCode.SetMember, subject, StringConstant(store.Name), value), store.Span);
                if (target is { } destination)
                {
                    if (destination != value) Emit(new WebLingoInstruction(WebLingoOpCode.Move, destination, value), store.Span);
                    _nextRegister = mark;
                    return destination;
                }
                _nextRegister = value >= _locals ? Math.Max(mark, value + 1) : mark;
                return value;
            }
            case WebLingoIrMakeClass made:
            {
                var mark = _nextRegister;
                var baseClass = made.Base is null ? 0 : EmitExpression(made.Base, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.MakeClass, destination, made.Class, baseClass, made.Base is null ? 0 : 1), made.Span);
                return destination;
            }
            case WebLingoIrGetSuper super:
            {
                var mark = _nextRegister;
                var classValue = EmitExpression(super.Class, null);
                var self = EmitExpression(super.This, null);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.GetSuper, destination, classValue, self, StringConstant(super.Name)), super.Span);
                return destination;
            }
            case WebLingoIrNew created:
            {
                var mark = _nextRegister;
                var subject = KeptIfAssigned(EmitExpression(created.Class, null), created.Class.Span, ToArray(created.Arguments));
                var first = _nextRegister;
                for (var i = 0; i < created.Arguments.Count; i++) Allocate();
                for (var i = 0; i < created.Arguments.Count; i++) EmitExpression(created.Arguments[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.New, destination, subject, created.Arguments.Count == 0 ? 0 : first,
                    created.Arguments.Count), created.Span);
                return destination;
            }
            case WebLingoIrCallMethod call:
            {
                // The target, then the arguments, in consecutive registers.
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i <= call.Arguments.Count; i++) Allocate();
                EmitExpression(call.Target, first);
                for (var i = 0; i < call.Arguments.Count; i++) EmitExpression(call.Arguments[i], first + 1 + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.CallMethod, destination, StringConstant(call.Name), first, call.Arguments.Count),
                    call.Span);
                return destination;
            }
            case WebLingoIrCallBuiltin call:
            {
                var mark = _nextRegister;
                var first = _nextRegister;
                for (var i = 0; i < call.Arguments.Count; i++) Allocate();
                for (var i = 0; i < call.Arguments.Count; i++) EmitExpression(call.Arguments[i], first + i);
                _nextRegister = mark;
                var destination = target ?? Allocate();
                Emit(new WebLingoInstruction(WebLingoOpCode.CallBuiltin, destination, call.Builtin, call.Arguments.Count == 0 ? 0 : first,
                    call.Arguments.Count), call.Span);
                return destination;
            }
            default:
                throw new InvalidOperationException($"Unknown expression {expression.GetType().Name}.");
        }
    }

    /// <summary>Whether evaluating the expression may assign the local.</summary>
    private static bool Assigns(WebLingoIrExpression expression, int local) => expression switch
    {
        WebLingoIrStoreLocal store => store.Local == local || Assigns(store.Value, local),
        WebLingoIrUnary unary => Assigns(unary.Operand, local),
        WebLingoIrToText text => Assigns(text.Value, local),
        WebLingoIrHold hold => Assigns(hold.Instance, local) || Assigns(hold.Value, local),
        WebLingoIrAwait awaited => Assigns(awaited.Value, local),
        WebLingoIrIs test => Assigns(test.Value, local) || Assigns(test.Class, local),
        WebLingoIrBinary binary => Assigns(binary.Left, local) || Assigns(binary.Right, local),
        WebLingoIrLogical logical => Assigns(logical.Left, local) || Assigns(logical.Right, local),
        WebLingoIrCallBuiltin call => AnyAssigns(call.Arguments, local),
        WebLingoIrCall call => Assigns(call.Callee, local) || AnyAssigns(call.Arguments, local),
        WebLingoIrStoreCell store => Assigns(store.Value, local),
        WebLingoIrStoreCapture store => Assigns(store.Value, local),
        WebLingoIrIndex index => Assigns(index.Target, local) || Assigns(index.Index, local),
        WebLingoIrNewArray array => AnyAssigns(array.Elements, local),
        WebLingoIrNewSet set => AnyAssigns(set.Elements, local),
        WebLingoIrNewMap map => AnyEntryAssigns(map.Entries, local),
        WebLingoIrStoreIndex store => Assigns(store.Target, local) || Assigns(store.Index, local) || Assigns(store.Value, local),
        WebLingoIrMember member => Assigns(member.Target, local),
        WebLingoIrCallMethod call => Assigns(call.Target, local) || AnyAssigns(call.Arguments, local),
        WebLingoIrStoreMember store => Assigns(store.Target, local) || Assigns(store.Value, local),
        WebLingoIrNew created => Assigns(created.Class, local) || AnyAssigns(created.Arguments, local),
        WebLingoIrMakeClass { Base: { } baseClass } => Assigns(baseClass, local),
        _ => false
    };

    /// <summary>
    /// A register an operand was evaluated into, copied first if it is a local that a later operand of the same
    /// operation assigns: the operation must see the value read, not the one assigned (section 15.2).
    /// </summary>
    private int KeptIfAssigned(int register, WebLingoSourceSpan span, params WebLingoIrExpression[] later)
    {
        if (register >= _locals || !AnyAssigns(later, register)) return register;
        var copy = Allocate();
        Emit(new WebLingoInstruction(WebLingoOpCode.Move, copy, register), span);
        return copy;
    }

    /// <summary>A copy of the expressions, without LINQ, which the portable core does not use.</summary>
    private static WebLingoIrExpression[] ToArray(IReadOnlyList<WebLingoIrExpression> expressions)
    {
        var array = new WebLingoIrExpression[expressions.Count];
        for (var i = 0; i < array.Length; i++) array[i] = expressions[i];
        return array;
    }

    private static bool AnyEntryAssigns(IReadOnlyList<WebLingoIrMapEntry> entries, int local)
    {
        foreach (var entry in entries)
        {
            if (Assigns(entry.Value, local)) return true;
        }
        return false;
    }

    private static bool AnyAssigns(IReadOnlyList<WebLingoIrExpression> expressions, int local)
    {
        foreach (var expression in expressions)
        {
            if (Assigns(expression, local)) return true;
        }
        return false;
    }

    private static WebLingoOpCode OpCodeOf(WebLingoBinaryOperator op) => op switch
    {
        WebLingoBinaryOperator.Add => WebLingoOpCode.Add,
        WebLingoBinaryOperator.Subtract => WebLingoOpCode.Subtract,
        WebLingoBinaryOperator.Multiply => WebLingoOpCode.Multiply,
        WebLingoBinaryOperator.Divide => WebLingoOpCode.Divide,
        WebLingoBinaryOperator.Equal => WebLingoOpCode.Equal,
        WebLingoBinaryOperator.NotEqual => WebLingoOpCode.NotEqual,
        WebLingoBinaryOperator.Less => WebLingoOpCode.Less,
        WebLingoBinaryOperator.LessOrEqual => WebLingoOpCode.LessOrEqual,
        WebLingoBinaryOperator.Greater => WebLingoOpCode.Greater,
        WebLingoBinaryOperator.GreaterOrEqual => WebLingoOpCode.GreaterOrEqual,
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
        _constants.Add(WebLingoConstant.Int64(value));
        _int64Constants.Add(value, index);
        return index;
    }

    /// <summary>
    /// <c>while</c> and <c>for</c>: test, body, step, back to the test. <c>do … while</c>: body, then the test, which
    /// jumps back if true. A <c>continue</c> goes to the step (or the test), a <c>break</c> past the end.
    /// </summary>
    private void EmitLoop(WebLingoIrLoop loop)
    {
        var jumps = new LoopJumps { Guards = _guards.Count };
        _loops.Push(jumps);
        var top = _code.Count;
        int? exit = null;
        if (loop.TestFirst && loop.Condition is { } test)
        {
            var condition = EmitExpression(test, null);
            exit = EmitJump(WebLingoOpCode.JumpIfFalse, condition, test.Span);
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
            Emit(new WebLingoInstruction(WebLingoOpCode.JumpIfTrue, condition, top), last.Span);
            _nextRegister = _locals;
        }
        else
        {
            Emit(new WebLingoInstruction(WebLingoOpCode.Jump, top), loop.Span);
        }
        if (exit is { } exitJump) PatchJump(exitJump);
        foreach (var at in jumps.Breaks) PatchJump(at);
        _loops.Pop();
    }

    /// <summary>
    /// <c>foreach</c>: the collection into its local, the position 0 into the next; then <c>ForEachNext</c>, which
    /// leaves the loop when there is nothing more, or writes the next item and goes on to the body.
    /// </summary>
    private void EmitForEach(WebLingoIrForEach loop)
    {
        var collection = loop.FirstLocal;
        EmitExpression(loop.Collection, collection);
        _nextRegister = _locals;
        Emit(new WebLingoInstruction(WebLingoOpCode.LoadConstant, collection + 1, Int64Constant(0)), loop.Span);
        var jumps = new LoopJumps { Guards = _guards.Count };
        _loops.Push(jumps);
        var top = _code.Count;
        Emit(new WebLingoInstruction(WebLingoOpCode.ForEachNext, 0, collection, loop.ItemCount), loop.Collection.Span);
        foreach (var item in loop.CapturedItems) Emit(new WebLingoInstruction(WebLingoOpCode.BoxRegister, item), loop.Span);
        EmitStatement(loop.Body);
        foreach (var at in jumps.Continues) PatchJump(at);
        Emit(new WebLingoInstruction(WebLingoOpCode.Jump, top), loop.Span);
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
        public WebLingoIrStatement? Finally { get; init; }
        public WebLingoIrExpression? Disposes { get; init; }
        public WebLingoSourceSpan Span { get; init; }
    }

    /// <summary>The open guards, innermost last.</summary>
    private readonly List<Guard> _guards = [];

    /// <summary>The function's handlers, innermost first, as each guard's handler is generated.</summary>
    private readonly List<WebLingoHandler> _handlers = [];

    private Guard OpenGuard(WebLingoSourceSpan span, WebLingoIrStatement? cleanup = null, WebLingoIrExpression? disposes = null)
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
        foreach (var (start, end) in guard.Ranges) _handlers.Add(new WebLingoHandler(start, end, _code.Count, value, origin));
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
    private void EmitTry(WebLingoIrTry statement)
    {
        var finallyGuard = statement.Finally is { } cleanup ? OpenGuard(statement.Span, cleanup.Body) : null;
        var catchGuard = statement.Catch is not null ? OpenGuard(statement.Span) : null;
        EmitStatement(statement.Body);
        if (statement.Catch is { } handler)
        {
            EndGuard(catchGuard!);
            var skip = EmitJump(WebLingoOpCode.Jump, 0, statement.Span);
            AddHandlers(catchGuard!, handler.Value, handler.Origin);
            foreach (var inner in handler.Body) EmitStatement(inner);
            PatchJump(skip);
        }
        if (statement.Finally is { } final)
        {
            EndGuard(finallyGuard!);
            EmitStatement(final.Body);
            var skip = EmitJump(WebLingoOpCode.Jump, 0, statement.Span);
            AddHandlers(finallyGuard!, final.Value, final.Origin);
            EmitStatement(final.Body);
            EmitRethrow(final.Value, final.Origin, statement.Span);
            PatchJump(skip);
        }
    }

    private void EmitRethrow(int value, int origin, WebLingoSourceSpan span) =>
        Emit(new WebLingoInstruction(WebLingoOpCode.Rethrow, value, origin), span);

    /// <summary>Disposes a resource, unless it is <c>null</c> (section 3.7).</summary>
    private void EmitDispose(WebLingoIrExpression resource, WebLingoSourceSpan span)
    {
        var mark = _nextRegister;
        var value = EmitExpression(resource, null);
        Emit(new WebLingoInstruction(WebLingoOpCode.Dispose, value), span);
        _nextRegister = mark;
    }

    /// <summary>Emits a jump whose target <see cref="PatchJump"/> fills in later, and returns where it is.</summary>
    private int EmitJump(WebLingoOpCode opCode, int condition, WebLingoSourceSpan span)
    {
        var at = _code.Count;
        Emit(opCode == WebLingoOpCode.Jump ? new WebLingoInstruction(opCode) : new WebLingoInstruction(opCode, condition), span);
        return at;
    }

    /// <summary>Points the jump at <paramref name="at"/> to the next instruction to be emitted.</summary>
    private void PatchJump(int at)
    {
        var jump = _code[at];
        _code[at] = jump.OpCode == WebLingoOpCode.Jump ? jump with { A = _code.Count } : jump with { B = _code.Count };
    }

    private int StringConstant(string value)
    {
        if (_stringConstants.TryGetValue(value, out var index)) return index;
        index = _constants.Count;
        _constants.Add(WebLingoConstant.String(value));
        _stringConstants.Add(value, index);
        return index;
    }

    private int Binary64Constant(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        if (_binary64Constants.TryGetValue(bits, out var index)) return index;
        index = _constants.Count;
        _constants.Add(WebLingoConstant.Binary64(value));
        _binary64Constants.Add(bits, index);
        return index;
    }

    private void Emit(WebLingoInstruction instruction, WebLingoSourceSpan span)
    {
        _code.Add(instruction);
        _spans.Add(span);
    }
}
