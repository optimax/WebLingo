using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// Generates stack bytecode from the lowered IR (specification, section 5.6, "The comparison's parts", part 3), the
/// same IR as <see cref="WebLingoCodeGenerator"/>'s register bytecode, so that the two VMs run equivalent code (WL-BC002).
/// An expression pushes its value, its operands evaluated left to right (section 15.2); a statement leaves the operand
/// stack as it found it, empty, so that every jump between statements keeps the stack's depth. An assignment whose value
/// is not used stores it without keeping a copy. The generator counts the deepest the stack gets, the function's
/// <see cref="WebLingoStackFunction.MaxStack"/>.
/// </summary>
public sealed class WebLingoStackCodeGenerator
{
    private readonly ModuleState _module;
    private readonly List<WebLingoStackInstruction> _code = [];
    private readonly List<WebLingoSourceSpan> _spans = [];
    private readonly Stack<LoopJumps> _loops = new();
    private int _depth;
    private int _maxDepth;

    private WebLingoStackCodeGenerator(ModuleState module) => _module = module;

    /// <summary>
    /// Generates the module: the program's statements as its entry point, and each function it declares; null, with a
    /// diagnostic, if a function needs more slots (locals and stack) than a function may have.
    /// </summary>
    public static WebLingoStackModule? Generate(WebLingoIrProgram program, string moduleName, List<WebLingoDiagnostic> diagnostics)
    {
        var module = new ModuleState();
        var main = GenerateFunction(module, "main", program.IsImport || program.Parameters.Count > 0 ? 1 : 0, program.LocalNames, [], program.Statements, diagnostics);
        var functions = new List<WebLingoStackFunction>();
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
        return new WebLingoStackModule(moduleName, module.Constants, imports, main with { IsAsync = program.IsAsync })
        {
            Functions = functions,
            Classes = WebLingoClassTable.Of(program),
            Units = UnitsOf(program),
            ErrorClass = program.ErrorClass,
            ObjectClass = program.ObjectClass,
            ValueClasses = program.ValueClasses,
            ErrorClasses = ErrorClassesOf(program),
            IsImport = program.IsImport,
            Parameters = program.Parameters
        };
    }

    /// <summary>The program's files, if it imports others (section 3.9).</summary>
    private static List<WebLingoUnitInfo> UnitsOf(WebLingoIrProgram program)
    {
        var units = new List<WebLingoUnitInfo>();
        foreach (var unit in program.Units) units.Add(new WebLingoUnitInfo(unit.Name, unit.Function, unit.Exports) { Parameters = unit.Parameters });
        return units;
    }

    private static WebLingoStackFunction? GenerateFunction(ModuleState module, string name, int parameterCount,
        IReadOnlyList<string> localNames, IReadOnlyList<WebLingoCapture> captures, IReadOnlyList<WebLingoIrStatement> body,
        List<WebLingoDiagnostic> diagnostics)
    {
        var generator = new WebLingoStackCodeGenerator(module);
        foreach (var statement in body) generator.EmitStatement(statement);
        generator.Emit(WebLingoStackOpCode.Return, new WebLingoSourceSpan(0, 0));
        var slots = (long)localNames.Count + generator._maxDepth;
        if (slots > WebLingoFunction.MaxRegisters)
        {
            diagnostics.Add(new WebLingoDiagnostic(WebLingoDiagnosticCodes.ProgramTooLarge, WebLingoDiagnosticSeverity.Error,
                $"'{name}' needs {slots} slots for its locals and its stack; a function may have {WebLingoFunction.MaxRegisters}.",
                new WebLingoSourceSpan(0, 0)));
            return null;
        }
        return new WebLingoStackFunction(name, localNames.Count, generator._maxDepth, generator._code, generator._spans, localNames)
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
                EmitValue(initialize.Value);
                Emit(WebLingoStackOpCode.StoreLocal, initialize.Span, initialize.Local);
                break;
            case WebLingoIrEvaluate evaluate:
                EmitDiscarded(evaluate.Expression);
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
                _loops.Peek().Breaks.Add(EmitJump(WebLingoStackOpCode.Jump, breakStatement.Span));
                ReopenGuards(_loops.Peek().Guards);
                break;
            case WebLingoIrContinue continueStatement:
                LeaveGuards(_loops.Peek().Guards);
                _loops.Peek().Continues.Add(EmitJump(WebLingoStackOpCode.Jump, continueStatement.Span));
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
                var skip = EmitJump(WebLingoStackOpCode.Jump, usingStatement.Span);
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
                EmitValue(throwStatement.Value);
                Emit(WebLingoStackOpCode.Throw, throwStatement.Span);
                break;
            case WebLingoIrRegisterUnit registered:
                EmitValue(registered.Closure);
                Emit(WebLingoStackOpCode.RegisterUnit, registered.Span, registered.Unit);
                break;
            case WebLingoIrExtend extension:
                EmitValue(extension.Target);
                Emit(WebLingoStackOpCode.Extend, extension.Span, extension.Class);
                break;
            case WebLingoIrRethrow rethrow:
                EmitRethrow(rethrow.Value, rethrow.Origin, rethrow.Span);
                break;
            case WebLingoIrNewCell cell:
                Emit(WebLingoStackOpCode.NewCell, cell.Span, cell.Local);
                break;
            case WebLingoIrDefaultParameter parameter:
            {
                // Skipped when the caller gave the parameter; otherwise its default, into its local or its cell.
                var skip = _code.Count;
                Emit(WebLingoStackOpCode.JumpIfGiven, parameter.Span, 0, parameter.Local);
                EmitValue(parameter.Value);
                Emit(parameter.Captured ? WebLingoStackOpCode.StoreCell : WebLingoStackOpCode.StoreLocal, parameter.Span, parameter.Local);
                PatchJump(skip);
                break;
            }
            case WebLingoIrBoxLocal box:
                Emit(WebLingoStackOpCode.BoxLocal, box.Span, box.Local);
                break;
            case WebLingoIrCopyCells copies:
                foreach (var local in copies.Locals) Emit(WebLingoStackOpCode.CopyCell, copies.Span, local);
                break;
            case WebLingoIrReturn returnStatement:
                if (returnStatement.Value is null)
                {
                    LeaveGuards(0);
                    Emit(WebLingoStackOpCode.Return, returnStatement.Span);
                    ReopenGuards(0);
                    break;
                }
                EmitValue(returnStatement.Value); // On the stack, while the guards' cleanups run.
                LeaveGuards(0);
                Emit(WebLingoStackOpCode.ReturnValue, returnStatement.Span);
                ReopenGuards(0);
                break;
            case WebLingoIrIf conditional:
            {
                EmitValue(conditional.Condition);
                var skipThen = EmitJump(WebLingoStackOpCode.JumpIfFalse, conditional.Condition.Span);
                EmitStatement(conditional.Then);
                if (conditional.Else is null)
                {
                    PatchJump(skipThen);
                    break;
                }
                var skipElse = EmitJump(WebLingoStackOpCode.Jump, conditional.Span);
                PatchJump(skipThen);
                EmitStatement(conditional.Else);
                PatchJump(skipElse);
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
        }
    }

    /// <summary>Emits an expression whose value is not used: an assignment stores it, anything else pops it.</summary>
    private void EmitDiscarded(WebLingoIrExpression expression)
    {
        switch (expression)
        {
            case WebLingoIrStoreLocal store:
                EmitValue(store.Value);
                Emit(WebLingoStackOpCode.StoreLocal, store.Span, store.Local);
                return;
            case WebLingoIrStoreCell store:
                EmitValue(store.Value);
                Emit(WebLingoStackOpCode.StoreCell, store.Span, store.Local);
                return;
            case WebLingoIrStoreCapture store:
                EmitValue(store.Value);
                Emit(WebLingoStackOpCode.StoreCapture, store.Span, store.Capture);
                return;
            case WebLingoIrStoreIndex store:
                EmitValue(store.Target);
                EmitValue(store.Index);
                EmitValue(store.Value);
                Emit(WebLingoStackOpCode.StoreIndex, store.Span);
                return;
            case WebLingoIrStoreMember field:
                EmitValue(field.Target);
                EmitValue(field.Value);
                Emit(WebLingoStackOpCode.SetMember, field.Span, StringConstant(field.Name));
                return;
            default:
                EmitValue(expression);
                Emit(WebLingoStackOpCode.Pop, expression.Span);
                return;
        }
    }

    /// <summary>Emits an expression that pushes its value.</summary>
    private void EmitValue(WebLingoIrExpression expression)
    {
        switch (expression)
        {
            case WebLingoIrInt64Constant constant:
                Emit(WebLingoStackOpCode.LoadConstant, constant.Span, Int64Constant(constant.Value));
                return;
            case WebLingoIrBinary64Constant constant:
                Emit(WebLingoStackOpCode.LoadConstant, constant.Span, Binary64Constant(constant.Value));
                return;
            case WebLingoIrStringConstant constant:
                Emit(WebLingoStackOpCode.LoadConstant, constant.Span, StringConstant(constant.Value));
                return;
            case WebLingoIrBooleanConstant constant:
                Emit(WebLingoStackOpCode.LoadBoolean, constant.Span, constant.Value ? 1 : 0);
                return;
            case WebLingoIrNullConstant constant:
                Emit(WebLingoStackOpCode.LoadNull, constant.Span);
                return;
            case WebLingoIrLogical logical:
            {
                // The left operand decides when it is false for &&, true for ||, and is then the result; otherwise it
                // is dropped, and the right one, which must be a Boolean too, is the result.
                EmitValue(logical.Left);
                Emit(WebLingoStackOpCode.Duplicate, logical.Span);
                var decided = EmitJump(logical.IsAnd ? WebLingoStackOpCode.JumpIfFalse : WebLingoStackOpCode.JumpIfTrue, logical.Span);
                Emit(WebLingoStackOpCode.Pop, logical.Span);
                EmitValue(logical.Right);
                Emit(WebLingoStackOpCode.CheckBoolean, logical.Right.Span);
                PatchJump(decided);
                return;
            }
            case WebLingoIrLoadLocal load:
                Emit(WebLingoStackOpCode.LoadLocal, load.Span, load.Local);
                return;
            case WebLingoIrStoreLocal store:
                EmitValue(store.Value);
                Emit(WebLingoStackOpCode.Duplicate, store.Span);
                Emit(WebLingoStackOpCode.StoreLocal, store.Span, store.Local);
                return;
            case WebLingoIrLoadCell load:
                Emit(WebLingoStackOpCode.LoadCell, load.Span, load.Local);
                return;
            case WebLingoIrStoreCell store:
                EmitValue(store.Value);
                Emit(WebLingoStackOpCode.Duplicate, store.Span);
                Emit(WebLingoStackOpCode.StoreCell, store.Span, store.Local);
                return;
            case WebLingoIrLoadCapture load:
                Emit(WebLingoStackOpCode.LoadCapture, load.Span, load.Capture);
                return;
            case WebLingoIrStoreCapture store:
                EmitValue(store.Value);
                Emit(WebLingoStackOpCode.Duplicate, store.Span);
                Emit(WebLingoStackOpCode.StoreCapture, store.Span, store.Capture);
                return;
            case WebLingoIrMakeClosure closure:
                Emit(WebLingoStackOpCode.MakeClosure, closure.Span, closure.Function);
                return;
            case WebLingoIrLoadBuiltin builtin:
                Emit(WebLingoStackOpCode.LoadBuiltin, builtin.Span, builtin.Builtin);
                return;
            case WebLingoIrAwait awaited:
                EmitValue(awaited.Value);
                Emit(WebLingoStackOpCode.Await, awaited.Span);
                return;
            case WebLingoIrImportDynamic dynamic:
                EmitValue(dynamic.Path);
                if (dynamic.Values is { } given) EmitValue(given);
                // A: 1 for values, plus 2 for a component's render (section 13).
                var flags = (dynamic.Values is null ? 0 : 1) + (dynamic.Anew ? 2 : 0);
                Emit(WebLingoStackOpCode.ImportDynamic, dynamic.Span, flags);
                return;
            case WebLingoIrImport import:
                if (import.Values is { } values) EmitValue(values);
                Emit(WebLingoStackOpCode.Import, import.Span, import.Unit, import.Values is null ? 0 : 1);
                return;
            case WebLingoIrMakeModule made:
                // Its exports.
                foreach (var value in made.Values) EmitValue(value);
                Emit(WebLingoStackOpCode.MakeModule, made.Span, made.Unit, made.Values.Count);
                return;
            case WebLingoIrHold hold:
                EmitValue(hold.Instance);
                EmitValue(hold.Value);
                Emit(WebLingoStackOpCode.Hold, hold.Span);
                return;
            case WebLingoIrIs test:
                EmitValue(test.Value);
                EmitValue(test.Class);
                Emit(WebLingoStackOpCode.IsInstance, test.Span);
                return;
            case WebLingoIrToText text:
                EmitValue(text.Value);
                Emit(WebLingoStackOpCode.ToText, text.Span);
                return;
            case WebLingoIrUnary unary:
                EmitValue(unary.Operand);
                Emit(unary.Operator switch
                {
                    WebLingoUnaryOperator.Negate => WebLingoStackOpCode.Negate,
                    WebLingoUnaryOperator.Plus => WebLingoStackOpCode.Plus,
                    _ => WebLingoStackOpCode.Not
                }, unary.Span);
                return;
            case WebLingoIrBinary binary:
                EmitValue(binary.Left);
                EmitValue(binary.Right);
                Emit(OpCodeOf(binary.Operator), binary.Span);
                return;
            case WebLingoIrCall call:
                EmitValue(call.Callee);
                foreach (var argument in call.Arguments) EmitValue(argument);
                Emit(WebLingoStackOpCode.Call, call.Span, call.Arguments.Count);
                return;
            case WebLingoIrNewArray array:
                foreach (var element in array.Elements) EmitValue(element);
                Emit(WebLingoStackOpCode.NewArray, array.Span, array.Elements.Count);
                return;
            case WebLingoIrNewSet set:
                foreach (var element in set.Elements) EmitValue(element);
                Emit(WebLingoStackOpCode.NewSet, set.Span, set.Elements.Count);
                return;
            case WebLingoIrNewMap map:
                foreach (var entry in map.Entries)
                {
                    Emit(WebLingoStackOpCode.LoadConstant, map.Span, StringConstant(entry.Key));
                    EmitValue(entry.Value);
                }
                Emit(WebLingoStackOpCode.NewMap, map.Span, map.Entries.Count);
                return;
            case WebLingoIrIndex index:
                EmitValue(index.Target);
                EmitValue(index.Index);
                Emit(WebLingoStackOpCode.GetIndex, index.Span, index.IsOptional ? 1 : 0);
                return;
            case WebLingoIrStoreIndex store:
                EmitValue(store.Target);
                EmitValue(store.Index);
                EmitValue(store.Value);
                Emit(WebLingoStackOpCode.StoreIndex, store.Span, 1);
                return;
            case WebLingoIrMember member:
                EmitValue(member.Target);
                Emit(WebLingoStackOpCode.GetMember, member.Span, StringConstant(member.Name), member.IsOptional ? 1 : 0);
                return;
            case WebLingoIrStoreMember field:
                EmitValue(field.Target);
                EmitValue(field.Value);
                Emit(WebLingoStackOpCode.SetMember, field.Span, StringConstant(field.Name), 1);
                return;
            case WebLingoIrMakeClass made:
                if (made.Base is not null) EmitValue(made.Base);
                Emit(WebLingoStackOpCode.MakeClass, made.Span, made.Class, made.Base is null ? 0 : 1);
                return;
            case WebLingoIrGetSuper super:
                EmitValue(super.Class);
                EmitValue(super.This);
                Emit(WebLingoStackOpCode.GetSuper, super.Span, StringConstant(super.Name));
                return;
            case WebLingoIrNew created:
                EmitValue(created.Class);
                foreach (var argument in created.Arguments) EmitValue(argument);
                Emit(WebLingoStackOpCode.New, created.Span, created.Arguments.Count);
                return;
            case WebLingoIrCallMethod call:
                EmitValue(call.Target);
                foreach (var argument in call.Arguments) EmitValue(argument);
                Emit(WebLingoStackOpCode.CallMethod, call.Span, StringConstant(call.Name), call.Arguments.Count);
                return;
            case WebLingoIrCallBuiltin call:
                foreach (var argument in call.Arguments) EmitValue(argument);
                Emit(WebLingoStackOpCode.CallBuiltin, call.Span, call.Builtin, call.Arguments.Count);
                return;
            default:
                throw new InvalidOperationException($"Unknown expression {expression.GetType().Name}.");
        }
    }

    private static WebLingoStackOpCode OpCodeOf(WebLingoBinaryOperator op) => op switch
    {
        WebLingoBinaryOperator.Add => WebLingoStackOpCode.Add,
        WebLingoBinaryOperator.Subtract => WebLingoStackOpCode.Subtract,
        WebLingoBinaryOperator.Multiply => WebLingoStackOpCode.Multiply,
        WebLingoBinaryOperator.Divide => WebLingoStackOpCode.Divide,
        WebLingoBinaryOperator.Equal => WebLingoStackOpCode.Equal,
        WebLingoBinaryOperator.NotEqual => WebLingoStackOpCode.NotEqual,
        WebLingoBinaryOperator.Less => WebLingoStackOpCode.Less,
        WebLingoBinaryOperator.LessOrEqual => WebLingoStackOpCode.LessOrEqual,
        WebLingoBinaryOperator.Greater => WebLingoStackOpCode.Greater,
        WebLingoBinaryOperator.GreaterOrEqual => WebLingoStackOpCode.GreaterOrEqual,
        _ => throw new InvalidOperationException($"Unknown operator {op}.")
    };

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
            EmitValue(test);
            exit = EmitJump(WebLingoStackOpCode.JumpIfFalse, test.Span);
        }
        EmitStatement(loop.Body);
        foreach (var at in jumps.Continues) PatchJump(at);
        if (loop.PerIteration is { } perIteration) EmitStatement(perIteration);
        if (loop.Step is { } step) EmitDiscarded(step);
        if (!loop.TestFirst && loop.Condition is { } last)
        {
            EmitValue(last);
            Emit(WebLingoStackOpCode.JumpIfTrue, last.Span, top);
        }
        else
        {
            Emit(WebLingoStackOpCode.Jump, loop.Span, top);
        }
        if (exit is { } exitJump) PatchJump(exitJump);
        foreach (var at in jumps.Breaks) PatchJump(at);
        _loops.Pop();
    }

    /// <summary>
    /// <c>foreach</c>: the collection into its local, the position 0 into the next; then <c>ForEachNext</c> (or
    /// <c>ForEachNextEntry</c>, for a key and a value), which leaves the loop when there is nothing more, or writes the
    /// next item into the locals after them and goes on to the body.
    /// </summary>
    private void EmitForEach(WebLingoIrForEach loop)
    {
        var collection = loop.FirstLocal;
        EmitValue(loop.Collection);
        Emit(WebLingoStackOpCode.StoreLocal, loop.Span, collection);
        Emit(WebLingoStackOpCode.LoadConstant, loop.Span, Int64Constant(0));
        Emit(WebLingoStackOpCode.StoreLocal, loop.Span, collection + 1);
        var jumps = new LoopJumps { Guards = _guards.Count };
        _loops.Push(jumps);
        var top = _code.Count;
        Emit(loop.ItemCount == 1 ? WebLingoStackOpCode.ForEachNext : WebLingoStackOpCode.ForEachNextEntry, loop.Collection.Span, 0,
            collection);
        foreach (var item in loop.CapturedItems) Emit(WebLingoStackOpCode.BoxLocal, loop.Span, item);
        EmitStatement(loop.Body);
        foreach (var at in jumps.Continues) PatchJump(at);
        Emit(WebLingoStackOpCode.Jump, loop.Span, top);
        PatchJump(top);
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
        public int Depth { get; init; }
    }

    /// <summary>The open guards, innermost last.</summary>
    private readonly List<Guard> _guards = [];

    /// <summary>The function's handlers, innermost first, as each guard's handler is generated.</summary>
    private readonly List<WebLingoStackHandler> _handlers = [];

    private Guard OpenGuard(WebLingoSourceSpan span, WebLingoIrStatement? cleanup = null, WebLingoIrExpression? disposes = null)
    {
        var guard = new Guard { OpenedAt = _code.Count, Finally = cleanup, Disposes = disposes, Span = span , Depth = _depth };
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
        foreach (var (start, end) in guard.Ranges) _handlers.Add(new WebLingoStackHandler(start, end, _code.Count, value, origin, guard.Depth));
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
            var skip = EmitJump(WebLingoStackOpCode.Jump, statement.Span);
            AddHandlers(catchGuard!, handler.Value, handler.Origin);
            foreach (var inner in handler.Body) EmitStatement(inner);
            PatchJump(skip);
        }
        if (statement.Finally is { } final)
        {
            EndGuard(finallyGuard!);
            EmitStatement(final.Body);
            var skip = EmitJump(WebLingoStackOpCode.Jump, statement.Span);
            AddHandlers(finallyGuard!, final.Value, final.Origin);
            EmitStatement(final.Body);
            EmitRethrow(final.Value, final.Origin, statement.Span);
            PatchJump(skip);
        }
    }

    private void EmitRethrow(int value, int origin, WebLingoSourceSpan span)
    {
        Emit(WebLingoStackOpCode.LoadLocal, span, value);
        Emit(WebLingoStackOpCode.LoadLocal, span, origin);
        Emit(WebLingoStackOpCode.Rethrow, span);
    }

    /// <summary>Disposes a resource, unless it is <c>null</c> (section 3.7).</summary>
    private void EmitDispose(WebLingoIrExpression resource, WebLingoSourceSpan span)
    {
        EmitValue(resource);
        Emit(WebLingoStackOpCode.Dispose, span);
    }

    /// <summary>Emits a jump whose target <see cref="PatchJump"/> fills in later, and returns where it is.</summary>
    private int EmitJump(WebLingoStackOpCode opCode, WebLingoSourceSpan span)
    {
        var at = _code.Count;
        Emit(opCode, span);
        return at;
    }

    /// <summary>Points the jump at <paramref name="at"/> to the next instruction to be emitted.</summary>
    private void PatchJump(int at) => _code[at] = _code[at] with { A = _code.Count };

    private int Int64Constant(long value)
    {
        if (_module.Int64Constants.TryGetValue(value, out var index)) return index;
        index = _module.Constants.Count;
        _module.Constants.Add(WebLingoConstant.Int64(value));
        _module.Int64Constants.Add(value, index);
        return index;
    }

    private int Binary64Constant(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        if (_module.Binary64Constants.TryGetValue(bits, out var index)) return index;
        index = _module.Constants.Count;
        _module.Constants.Add(WebLingoConstant.Binary64(value));
        _module.Binary64Constants.Add(bits, index);
        return index;
    }

    private int StringConstant(string value)
    {
        if (_module.StringConstants.TryGetValue(value, out var index)) return index;
        index = _module.Constants.Count;
        _module.Constants.Add(WebLingoConstant.String(value));
        _module.StringConstants.Add(value, index);
        return index;
    }

    /// <summary>Emits an instruction, and follows the depth of the operand stack it leaves.</summary>
    private void Emit(WebLingoStackOpCode opCode, WebLingoSourceSpan span, int a = 0, int b = 0)
    {
        var (pops, pushes) = opCode switch
        {
            WebLingoStackOpCode.LoadConstant or WebLingoStackOpCode.LoadNull or WebLingoStackOpCode.LoadBoolean or WebLingoStackOpCode.LoadLocal
                or WebLingoStackOpCode.LoadCell or WebLingoStackOpCode.LoadCapture or WebLingoStackOpCode.MakeClosure
                or WebLingoStackOpCode.LoadBuiltin => (0, 1),
            WebLingoStackOpCode.StoreLocal or WebLingoStackOpCode.Pop or WebLingoStackOpCode.StoreCell or WebLingoStackOpCode.StoreCapture
                or WebLingoStackOpCode.JumpIfFalse or WebLingoStackOpCode.JumpIfTrue or WebLingoStackOpCode.ReturnValue => (1, 0),
            WebLingoStackOpCode.Duplicate => (1, 2),
            WebLingoStackOpCode.Add or WebLingoStackOpCode.Subtract or WebLingoStackOpCode.Multiply or WebLingoStackOpCode.Divide
                or WebLingoStackOpCode.Equal or WebLingoStackOpCode.NotEqual or WebLingoStackOpCode.Less or WebLingoStackOpCode.LessOrEqual
                or WebLingoStackOpCode.Greater or WebLingoStackOpCode.GreaterOrEqual => (2, 1),
            WebLingoStackOpCode.Call => (a + 1, 1),
            WebLingoStackOpCode.CallBuiltin => (b, 1),
            WebLingoStackOpCode.GetMember => (1, 1),
            WebLingoStackOpCode.MakeClass => (b, 1),
            WebLingoStackOpCode.GetSuper => (2, 1),
            WebLingoStackOpCode.Dispose => (1, 0),
            WebLingoStackOpCode.Extend => (1, 0),
            WebLingoStackOpCode.Hold => (2, 1),
            WebLingoStackOpCode.Import => (b, 1),
            WebLingoStackOpCode.ImportDynamic => ((a & 1) + 1, 1),
            WebLingoStackOpCode.RegisterUnit => (1, 0),
            WebLingoStackOpCode.MakeModule => (b, 1),
            WebLingoStackOpCode.Throw => (1, 0),
            WebLingoStackOpCode.ToText => (1, 1),
            WebLingoStackOpCode.IsInstance => (2, 1),
            WebLingoStackOpCode.JumpIfGiven => (0, 0),
            WebLingoStackOpCode.Await => (1, 1),
            WebLingoStackOpCode.Rethrow => (2, 0),
            WebLingoStackOpCode.New => (a + 1, 1),
            WebLingoStackOpCode.SetMember => (2, b),
            WebLingoStackOpCode.NewArray => (a, 1),
            WebLingoStackOpCode.NewMap => (2 * a, 1),
            WebLingoStackOpCode.NewSet => (a, 1),
            WebLingoStackOpCode.CallMethod => (b + 1, 1),
            WebLingoStackOpCode.GetIndex => (2, 1),
            WebLingoStackOpCode.StoreIndex => (3, a),
            WebLingoStackOpCode.Negate or WebLingoStackOpCode.Plus or WebLingoStackOpCode.Not or WebLingoStackOpCode.CheckBoolean => (1, 1),
            _ => (0, 0)
        };
        _depth += pushes - pops;
        if (_depth > _maxDepth) _maxDepth = _depth;
        _code.Add(new WebLingoStackInstruction(opCode, a, b));
        _spans.Add(span);
    }
}
