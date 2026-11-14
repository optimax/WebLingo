using WebLingo.Bytecode;
using WebLingo.Core;
using WebLingo.Language;
namespace WebLingo.Compiler;

/// <summary>
/// Generates stack bytecode from the lowered IR (specification, section 5.6, "The comparison's parts", part 3), the
/// same IR as <see cref="WsCodeGenerator"/>'s register bytecode, so that the two VMs run equivalent code (WS-BC002).
/// An expression pushes its value, its operands evaluated left to right (section 15.2); a statement leaves the operand
/// stack as it found it, empty, so that every jump between statements keeps the stack's depth. An assignment whose value
/// is not used stores it without keeping a copy. The generator counts the deepest the stack gets, the function's
/// <see cref="WsStackFunction.MaxStack"/>.
/// </summary>
public sealed class WsStackCodeGenerator
{
    private readonly ModuleState _module;
    private readonly List<WsStackInstruction> _code = [];
    private readonly List<WsSourceSpan> _spans = [];
    private readonly Stack<LoopJumps> _loops = new();
    private int _depth;
    private int _maxDepth;

    private WsStackCodeGenerator(ModuleState module) => _module = module;

    /// <summary>
    /// Generates the module: the program's statements as its entry point, and each function it declares; null, with a
    /// diagnostic, if a function needs more slots (locals and stack) than a function may have.
    /// </summary>
    public static WsStackModule? Generate(WsIrProgram program, string moduleName, List<WsDiagnostic> diagnostics)
    {
        var module = new ModuleState();
        var main = GenerateFunction(module, "main", 0, program.LocalNames, [], program.Statements, diagnostics);
        var functions = new List<WsStackFunction>();
        foreach (var function in program.Functions)
        {
            var captures = new List<WsCapture>();
            foreach (var capture in function.Captures) captures.Add(new WsCapture(capture.FromLocal, capture.Index));
            var name = function.Name.Length == 0 ? "function" : function.Name;
            var generated = GenerateFunction(module, name, function.ParameterCount, function.LocalNames, captures, function.Body,
                diagnostics);
            if (generated is not null) functions.Add(generated with
            {
                HasThis = function.HasThis,
                OptionalCount = function.OptionalCount,
                IsAsync = function.IsAsync
            });
        }
        if (main is null || functions.Count != program.Functions.Count) return null;

        var imports = new WsImport[program.Builtins.Count];
        foreach (var builtin in program.Builtins) imports[builtin.Index] = new WsImport(builtin.Name, builtin.Arity);
        return new WsStackModule(moduleName, module.Constants, imports, main with { IsAsync = program.IsAsync })
        {
            Functions = functions,
            Classes = WsClassTable.Of(program),
            ErrorClass = program.ErrorClass,
            ObjectClass = program.ObjectClass,
            ValueClasses = program.ValueClasses,
            ErrorClasses = ErrorClassesOf(program)
        };
    }

    private static WsStackFunction? GenerateFunction(ModuleState module, string name, int parameterCount,
        IReadOnlyList<string> localNames, IReadOnlyList<WsCapture> captures, IReadOnlyList<WsIrStatement> body,
        List<WsDiagnostic> diagnostics)
    {
        var generator = new WsStackCodeGenerator(module);
        foreach (var statement in body) generator.EmitStatement(statement);
        generator.Emit(WsStackOpCode.Return, new WsSourceSpan(0, 0));
        var slots = (long)localNames.Count + generator._maxDepth;
        if (slots > WsFunction.MaxRegisters)
        {
            diagnostics.Add(new WsDiagnostic(WsDiagnosticCodes.ProgramTooLarge, WsDiagnosticSeverity.Error,
                $"'{name}' needs {slots} slots for its locals and its stack; a function may have {WsFunction.MaxRegisters}.",
                new WsSourceSpan(0, 0)));
            return null;
        }
        return new WsStackFunction(name, localNames.Count, generator._maxDepth, generator._code, generator._spans, localNames)
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
                EmitValue(initialize.Value);
                Emit(WsStackOpCode.StoreLocal, initialize.Span, initialize.Local);
                break;
            case WsIrEvaluate evaluate:
                EmitDiscarded(evaluate.Expression);
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
                _loops.Peek().Breaks.Add(EmitJump(WsStackOpCode.Jump, breakStatement.Span));
                ReopenGuards(_loops.Peek().Guards);
                break;
            case WsIrContinue continueStatement:
                LeaveGuards(_loops.Peek().Guards);
                _loops.Peek().Continues.Add(EmitJump(WsStackOpCode.Jump, continueStatement.Span));
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
                var skip = EmitJump(WsStackOpCode.Jump, usingStatement.Span);
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
                EmitValue(throwStatement.Value);
                Emit(WsStackOpCode.Throw, throwStatement.Span);
                break;
            case WsIrExtend extension:
                EmitValue(extension.Target);
                Emit(WsStackOpCode.Extend, extension.Span, extension.Class);
                break;
            case WsIrRethrow rethrow:
                EmitRethrow(rethrow.Value, rethrow.Origin, rethrow.Span);
                break;
            case WsIrNewCell cell:
                Emit(WsStackOpCode.NewCell, cell.Span, cell.Local);
                break;
            case WsIrDefaultParameter parameter:
            {
                // Skipped when the caller gave the parameter; otherwise its default, into its local or its cell.
                var skip = _code.Count;
                Emit(WsStackOpCode.JumpIfGiven, parameter.Span, 0, parameter.Local);
                EmitValue(parameter.Value);
                Emit(parameter.Captured ? WsStackOpCode.StoreCell : WsStackOpCode.StoreLocal, parameter.Span, parameter.Local);
                PatchJump(skip);
                break;
            }
            case WsIrBoxLocal box:
                Emit(WsStackOpCode.BoxLocal, box.Span, box.Local);
                break;
            case WsIrCopyCells copies:
                foreach (var local in copies.Locals) Emit(WsStackOpCode.CopyCell, copies.Span, local);
                break;
            case WsIrReturn returnStatement:
                if (returnStatement.Value is null)
                {
                    LeaveGuards(0);
                    Emit(WsStackOpCode.Return, returnStatement.Span);
                    ReopenGuards(0);
                    break;
                }
                EmitValue(returnStatement.Value); // On the stack, while the guards' cleanups run.
                LeaveGuards(0);
                Emit(WsStackOpCode.ReturnValue, returnStatement.Span);
                ReopenGuards(0);
                break;
            case WsIrIf conditional:
            {
                EmitValue(conditional.Condition);
                var skipThen = EmitJump(WsStackOpCode.JumpIfFalse, conditional.Condition.Span);
                EmitStatement(conditional.Then);
                if (conditional.Else is null)
                {
                    PatchJump(skipThen);
                    break;
                }
                var skipElse = EmitJump(WsStackOpCode.Jump, conditional.Span);
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
    private void EmitDiscarded(WsIrExpression expression)
    {
        switch (expression)
        {
            case WsIrStoreLocal store:
                EmitValue(store.Value);
                Emit(WsStackOpCode.StoreLocal, store.Span, store.Local);
                return;
            case WsIrStoreCell store:
                EmitValue(store.Value);
                Emit(WsStackOpCode.StoreCell, store.Span, store.Local);
                return;
            case WsIrStoreCapture store:
                EmitValue(store.Value);
                Emit(WsStackOpCode.StoreCapture, store.Span, store.Capture);
                return;
            case WsIrStoreIndex store:
                EmitValue(store.Target);
                EmitValue(store.Index);
                EmitValue(store.Value);
                Emit(WsStackOpCode.StoreIndex, store.Span);
                return;
            case WsIrStoreMember field:
                EmitValue(field.Target);
                EmitValue(field.Value);
                Emit(WsStackOpCode.SetMember, field.Span, StringConstant(field.Name));
                return;
            default:
                EmitValue(expression);
                Emit(WsStackOpCode.Pop, expression.Span);
                return;
        }
    }

    /// <summary>Emits an expression that pushes its value.</summary>
    private void EmitValue(WsIrExpression expression)
    {
        switch (expression)
        {
            case WsIrInt64Constant constant:
                Emit(WsStackOpCode.LoadConstant, constant.Span, Int64Constant(constant.Value));
                return;
            case WsIrBinary64Constant constant:
                Emit(WsStackOpCode.LoadConstant, constant.Span, Binary64Constant(constant.Value));
                return;
            case WsIrStringConstant constant:
                Emit(WsStackOpCode.LoadConstant, constant.Span, StringConstant(constant.Value));
                return;
            case WsIrBooleanConstant constant:
                Emit(WsStackOpCode.LoadBoolean, constant.Span, constant.Value ? 1 : 0);
                return;
            case WsIrNullConstant constant:
                Emit(WsStackOpCode.LoadNull, constant.Span);
                return;
            case WsIrLogical logical:
            {
                // The left operand decides when it is false for &&, true for ||, and is then the result; otherwise it
                // is dropped, and the right one, which must be a Boolean too, is the result.
                EmitValue(logical.Left);
                Emit(WsStackOpCode.Duplicate, logical.Span);
                var decided = EmitJump(logical.IsAnd ? WsStackOpCode.JumpIfFalse : WsStackOpCode.JumpIfTrue, logical.Span);
                Emit(WsStackOpCode.Pop, logical.Span);
                EmitValue(logical.Right);
                Emit(WsStackOpCode.CheckBoolean, logical.Right.Span);
                PatchJump(decided);
                return;
            }
            case WsIrLoadLocal load:
                Emit(WsStackOpCode.LoadLocal, load.Span, load.Local);
                return;
            case WsIrStoreLocal store:
                EmitValue(store.Value);
                Emit(WsStackOpCode.Duplicate, store.Span);
                Emit(WsStackOpCode.StoreLocal, store.Span, store.Local);
                return;
            case WsIrLoadCell load:
                Emit(WsStackOpCode.LoadCell, load.Span, load.Local);
                return;
            case WsIrStoreCell store:
                EmitValue(store.Value);
                Emit(WsStackOpCode.Duplicate, store.Span);
                Emit(WsStackOpCode.StoreCell, store.Span, store.Local);
                return;
            case WsIrLoadCapture load:
                Emit(WsStackOpCode.LoadCapture, load.Span, load.Capture);
                return;
            case WsIrStoreCapture store:
                EmitValue(store.Value);
                Emit(WsStackOpCode.Duplicate, store.Span);
                Emit(WsStackOpCode.StoreCapture, store.Span, store.Capture);
                return;
            case WsIrMakeClosure closure:
                Emit(WsStackOpCode.MakeClosure, closure.Span, closure.Function);
                return;
            case WsIrLoadBuiltin builtin:
                Emit(WsStackOpCode.LoadBuiltin, builtin.Span, builtin.Builtin);
                return;
            case WsIrAwait awaited:
                EmitValue(awaited.Value);
                Emit(WsStackOpCode.Await, awaited.Span);
                return;
            case WsIrIs test:
                EmitValue(test.Value);
                EmitValue(test.Class);
                Emit(WsStackOpCode.IsInstance, test.Span);
                return;
            case WsIrToText text:
                EmitValue(text.Value);
                Emit(WsStackOpCode.ToText, text.Span);
                return;
            case WsIrUnary unary:
                EmitValue(unary.Operand);
                Emit(unary.Operator switch
                {
                    WsUnaryOperator.Negate => WsStackOpCode.Negate,
                    WsUnaryOperator.Plus => WsStackOpCode.Plus,
                    _ => WsStackOpCode.Not
                }, unary.Span);
                return;
            case WsIrBinary binary:
                EmitValue(binary.Left);
                EmitValue(binary.Right);
                Emit(OpCodeOf(binary.Operator), binary.Span);
                return;
            case WsIrCall call:
                EmitValue(call.Callee);
                foreach (var argument in call.Arguments) EmitValue(argument);
                Emit(WsStackOpCode.Call, call.Span, call.Arguments.Count);
                return;
            case WsIrNewArray array:
                foreach (var element in array.Elements) EmitValue(element);
                Emit(WsStackOpCode.NewArray, array.Span, array.Elements.Count);
                return;
            case WsIrNewSet set:
                foreach (var element in set.Elements) EmitValue(element);
                Emit(WsStackOpCode.NewSet, set.Span, set.Elements.Count);
                return;
            case WsIrNewMap map:
                foreach (var entry in map.Entries)
                {
                    Emit(WsStackOpCode.LoadConstant, map.Span, StringConstant(entry.Key));
                    EmitValue(entry.Value);
                }
                Emit(WsStackOpCode.NewMap, map.Span, map.Entries.Count);
                return;
            case WsIrIndex index:
                EmitValue(index.Target);
                EmitValue(index.Index);
                Emit(WsStackOpCode.GetIndex, index.Span, index.IsOptional ? 1 : 0);
                return;
            case WsIrStoreIndex store:
                EmitValue(store.Target);
                EmitValue(store.Index);
                EmitValue(store.Value);
                Emit(WsStackOpCode.StoreIndex, store.Span, 1);
                return;
            case WsIrMember member:
                EmitValue(member.Target);
                Emit(WsStackOpCode.GetMember, member.Span, StringConstant(member.Name), member.IsOptional ? 1 : 0);
                return;
            case WsIrStoreMember field:
                EmitValue(field.Target);
                EmitValue(field.Value);
                Emit(WsStackOpCode.SetMember, field.Span, StringConstant(field.Name), 1);
                return;
            case WsIrMakeClass made:
                if (made.Base is not null) EmitValue(made.Base);
                Emit(WsStackOpCode.MakeClass, made.Span, made.Class, made.Base is null ? 0 : 1);
                return;
            case WsIrGetSuper super:
                EmitValue(super.Class);
                EmitValue(super.This);
                Emit(WsStackOpCode.GetSuper, super.Span, StringConstant(super.Name));
                return;
            case WsIrNew created:
                EmitValue(created.Class);
                foreach (var argument in created.Arguments) EmitValue(argument);
                Emit(WsStackOpCode.New, created.Span, created.Arguments.Count);
                return;
            case WsIrCallMethod call:
                EmitValue(call.Target);
                foreach (var argument in call.Arguments) EmitValue(argument);
                Emit(WsStackOpCode.CallMethod, call.Span, StringConstant(call.Name), call.Arguments.Count);
                return;
            case WsIrCallBuiltin call:
                foreach (var argument in call.Arguments) EmitValue(argument);
                Emit(WsStackOpCode.CallBuiltin, call.Span, call.Builtin, call.Arguments.Count);
                return;
            default:
                throw new InvalidOperationException($"Unknown expression {expression.GetType().Name}.");
        }
    }

    private static WsStackOpCode OpCodeOf(WsBinaryOperator op) => op switch
    {
        WsBinaryOperator.Add => WsStackOpCode.Add,
        WsBinaryOperator.Subtract => WsStackOpCode.Subtract,
        WsBinaryOperator.Multiply => WsStackOpCode.Multiply,
        WsBinaryOperator.Divide => WsStackOpCode.Divide,
        WsBinaryOperator.Equal => WsStackOpCode.Equal,
        WsBinaryOperator.NotEqual => WsStackOpCode.NotEqual,
        WsBinaryOperator.Less => WsStackOpCode.Less,
        WsBinaryOperator.LessOrEqual => WsStackOpCode.LessOrEqual,
        WsBinaryOperator.Greater => WsStackOpCode.Greater,
        WsBinaryOperator.GreaterOrEqual => WsStackOpCode.GreaterOrEqual,
        _ => throw new InvalidOperationException($"Unknown operator {op}.")
    };

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
            EmitValue(test);
            exit = EmitJump(WsStackOpCode.JumpIfFalse, test.Span);
        }
        EmitStatement(loop.Body);
        foreach (var at in jumps.Continues) PatchJump(at);
        if (loop.PerIteration is { } perIteration) EmitStatement(perIteration);
        if (loop.Step is { } step) EmitDiscarded(step);
        if (!loop.TestFirst && loop.Condition is { } last)
        {
            EmitValue(last);
            Emit(WsStackOpCode.JumpIfTrue, last.Span, top);
        }
        else
        {
            Emit(WsStackOpCode.Jump, loop.Span, top);
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
    private void EmitForEach(WsIrForEach loop)
    {
        var collection = loop.FirstLocal;
        EmitValue(loop.Collection);
        Emit(WsStackOpCode.StoreLocal, loop.Span, collection);
        Emit(WsStackOpCode.LoadConstant, loop.Span, Int64Constant(0));
        Emit(WsStackOpCode.StoreLocal, loop.Span, collection + 1);
        var jumps = new LoopJumps { Guards = _guards.Count };
        _loops.Push(jumps);
        var top = _code.Count;
        Emit(loop.ItemCount == 1 ? WsStackOpCode.ForEachNext : WsStackOpCode.ForEachNextEntry, loop.Collection.Span, 0,
            collection);
        foreach (var item in loop.CapturedItems) Emit(WsStackOpCode.BoxLocal, loop.Span, item);
        EmitStatement(loop.Body);
        foreach (var at in jumps.Continues) PatchJump(at);
        Emit(WsStackOpCode.Jump, loop.Span, top);
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
        public WsIrStatement? Finally { get; init; }
        public WsIrExpression? Disposes { get; init; }
        public WsSourceSpan Span { get; init; }
        public int Depth { get; init; }
    }

    /// <summary>The open guards, innermost last.</summary>
    private readonly List<Guard> _guards = [];

    /// <summary>The function's handlers, innermost first, as each guard's handler is generated.</summary>
    private readonly List<WsStackHandler> _handlers = [];

    private Guard OpenGuard(WsSourceSpan span, WsIrStatement? cleanup = null, WsIrExpression? disposes = null)
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
        foreach (var (start, end) in guard.Ranges) _handlers.Add(new WsStackHandler(start, end, _code.Count, value, origin, guard.Depth));
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
            var skip = EmitJump(WsStackOpCode.Jump, statement.Span);
            AddHandlers(catchGuard!, handler.Value, handler.Origin);
            foreach (var inner in handler.Body) EmitStatement(inner);
            PatchJump(skip);
        }
        if (statement.Finally is { } final)
        {
            EndGuard(finallyGuard!);
            EmitStatement(final.Body);
            var skip = EmitJump(WsStackOpCode.Jump, statement.Span);
            AddHandlers(finallyGuard!, final.Value, final.Origin);
            EmitStatement(final.Body);
            EmitRethrow(final.Value, final.Origin, statement.Span);
            PatchJump(skip);
        }
    }

    private void EmitRethrow(int value, int origin, WsSourceSpan span)
    {
        Emit(WsStackOpCode.LoadLocal, span, value);
        Emit(WsStackOpCode.LoadLocal, span, origin);
        Emit(WsStackOpCode.Rethrow, span);
    }

    /// <summary>Disposes a resource, unless it is <c>null</c> (section 3.7).</summary>
    private void EmitDispose(WsIrExpression resource, WsSourceSpan span)
    {
        EmitValue(resource);
        Emit(WsStackOpCode.Dispose, span);
    }

    /// <summary>Emits a jump whose target <see cref="PatchJump"/> fills in later, and returns where it is.</summary>
    private int EmitJump(WsStackOpCode opCode, WsSourceSpan span)
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
        _module.Constants.Add(WsConstant.Int64(value));
        _module.Int64Constants.Add(value, index);
        return index;
    }

    private int Binary64Constant(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        if (_module.Binary64Constants.TryGetValue(bits, out var index)) return index;
        index = _module.Constants.Count;
        _module.Constants.Add(WsConstant.Binary64(value));
        _module.Binary64Constants.Add(bits, index);
        return index;
    }

    private int StringConstant(string value)
    {
        if (_module.StringConstants.TryGetValue(value, out var index)) return index;
        index = _module.Constants.Count;
        _module.Constants.Add(WsConstant.String(value));
        _module.StringConstants.Add(value, index);
        return index;
    }

    /// <summary>Emits an instruction, and follows the depth of the operand stack it leaves.</summary>
    private void Emit(WsStackOpCode opCode, WsSourceSpan span, int a = 0, int b = 0)
    {
        var (pops, pushes) = opCode switch
        {
            WsStackOpCode.LoadConstant or WsStackOpCode.LoadNull or WsStackOpCode.LoadBoolean or WsStackOpCode.LoadLocal
                or WsStackOpCode.LoadCell or WsStackOpCode.LoadCapture or WsStackOpCode.MakeClosure
                or WsStackOpCode.LoadBuiltin => (0, 1),
            WsStackOpCode.StoreLocal or WsStackOpCode.Pop or WsStackOpCode.StoreCell or WsStackOpCode.StoreCapture
                or WsStackOpCode.JumpIfFalse or WsStackOpCode.JumpIfTrue or WsStackOpCode.ReturnValue => (1, 0),
            WsStackOpCode.Duplicate => (1, 2),
            WsStackOpCode.Add or WsStackOpCode.Subtract or WsStackOpCode.Multiply or WsStackOpCode.Divide
                or WsStackOpCode.Equal or WsStackOpCode.NotEqual or WsStackOpCode.Less or WsStackOpCode.LessOrEqual
                or WsStackOpCode.Greater or WsStackOpCode.GreaterOrEqual => (2, 1),
            WsStackOpCode.Call => (a + 1, 1),
            WsStackOpCode.CallBuiltin => (b, 1),
            WsStackOpCode.GetMember => (1, 1),
            WsStackOpCode.MakeClass => (b, 1),
            WsStackOpCode.GetSuper => (2, 1),
            WsStackOpCode.Dispose => (1, 0),
            WsStackOpCode.Extend => (1, 0),
            WsStackOpCode.Throw => (1, 0),
            WsStackOpCode.ToText => (1, 1),
            WsStackOpCode.IsInstance => (2, 1),
            WsStackOpCode.JumpIfGiven => (0, 0),
            WsStackOpCode.Await => (1, 1),
            WsStackOpCode.Rethrow => (2, 0),
            WsStackOpCode.New => (a + 1, 1),
            WsStackOpCode.SetMember => (2, b),
            WsStackOpCode.NewArray => (a, 1),
            WsStackOpCode.NewMap => (2 * a, 1),
            WsStackOpCode.NewSet => (a, 1),
            WsStackOpCode.CallMethod => (b + 1, 1),
            WsStackOpCode.GetIndex => (2, 1),
            WsStackOpCode.StoreIndex => (3, a),
            WsStackOpCode.Negate or WsStackOpCode.Plus or WsStackOpCode.Not or WsStackOpCode.CheckBoolean => (1, 1),
            _ => (0, 0)
        };
        _depth += pushes - pops;
        if (_depth > _maxDepth) _maxDepth = _depth;
        _code.Add(new WsStackInstruction(opCode, a, b));
        _spans.Add(span);
    }
}
