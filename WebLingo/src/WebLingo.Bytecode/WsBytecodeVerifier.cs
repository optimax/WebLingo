namespace WebLingo.Bytecode;

/// <summary>
/// The bytecode verifier (specification, sections 5.5 and 8.4). A module must pass it before it runs. It checks the
/// module's structure, every instruction's operation and operands (registers, constants, imports and their arity,
/// branch targets, and that unused operands are 0), that no path runs off the end of the code, and, following every
/// path, that no register, local or temporary, is read before it is written. It never throws on malformed input.
/// </summary>
public sealed class WsBytecodeVerifier : IWsBytecodeVerifier
{
    public WsVerificationResult Verify(WsCompiledModule module)
    {
        if (module is null) return Fail("There is no module.");
        if (module.Name is null) return Fail("The module has no name.");
        if (module.Constants is null) return Fail("The module has no constant table.");
        if (module.Imports is null) return Fail("The module has no import table.");

        for (var i = 0; i < module.Constants.Count; i++)
        {
            var constant = module.Constants[i];
            if (constant.Kind is not (WsConstantKind.Int64 or WsConstantKind.Binary64 or WsConstantKind.String))
            {
                return Fail($"Constant {i} has the unknown kind {(int)constant.Kind}.");
            }
            if ((constant.Kind == WsConstantKind.String) != (constant.Text is not null))
            {
                return Fail($"Constant {i} has text if and only if it is not a string.");
            }
        }
        for (var i = 0; i < module.Imports.Count; i++)
        {
            var import = module.Imports[i];
            if (import is null || string.IsNullOrEmpty(import.Name)) return Fail($"Import {i} has no name.");
            if (import.Arity < 0) return Fail($"Import {i} ('{import.Name}') has a negative arity.");
        }
        if (module.Functions is null) return Fail("The module has no function table.");
        if (module.EntryPoint is { } main && (main.ParameterCount != 0 || main.Captures is not { Count: 0 } || main.HasThis))
        {
            return Fail("The entry point takes no parameters and captures nothing.");
        }
        var methods = new List<WsFunction?>();
        foreach (var function in module.Functions) methods.Add(function);
        if (WsClassTables.Check(module.Classes,
                index => index >= 0 && index < methods.Count && methods[index] is { } function ? (function.HasThis ? 1 : 0) : -1)
            is { } classError)
        {
            return Fail(classError);
        }
        if (module.ErrorClass < -1 || module.ErrorClass >= module.Classes.Count)
        {
            return Fail($"The module's Error class, {module.ErrorClass}, is not -1 or one of its classes.");
        }
        if (module.ObjectClass < -1 || module.ObjectClass >= module.Classes.Count)
        {
            return Fail($"The module's Object class, {module.ObjectClass}, is not -1 or one of its classes.");
        }
        if (WsClassTables.CheckRoles(module.Classes.Count, module.ValueClasses, module.ErrorClasses) is { } rolesError)
        {
            return Fail(rolesError);
        }
        var result = VerifyFunction(module, module.EntryPoint);
        for (var i = 0; i < module.Functions.Count && result.IsValid; i++)
        {
            result = VerifyFunction(module, module.Functions[i]);
            if (!result.IsValid) result = result with { Error = $"Function {i}: {result.Error}" };
        }
        return result;
    }

    private static WsVerificationResult VerifyFunction(WsCompiledModule module, WsFunction? function)
    {
        if (function is null) return Fail("The module has no entry point.");
        if (function.Code is null || function.Code.Count == 0) return Fail("The function has no code.");
        if (function.RegisterCount < 0 || function.RegisterCount > WsFunction.MaxRegisters)
        {
            return Fail($"The function has {function.RegisterCount} registers; it may have 0 to {WsFunction.MaxRegisters}.");
        }
        if (function.LocalCount < 0 || function.LocalCount > function.RegisterCount)
        {
            return Fail($"The function has {function.LocalCount} locals in {function.RegisterCount} registers.");
        }
        if (function.Spans is not null && function.Spans.Count != function.Code.Count)
        {
            return Fail($"The function has {function.Spans.Count} source spans for {function.Code.Count} instructions.");
        }
        if (function.LocalNames is not null && function.LocalNames.Count != function.LocalCount)
        {
            return Fail($"The function has {function.LocalNames.Count} local names for {function.LocalCount} locals.");
        }
        if (function.ParameterCount < 0 || function.ParameterCount > function.LocalCount)
        {
            return Fail($"The function has {function.ParameterCount} parameters in {function.LocalCount} locals.");
        }
        if (function.Captures is null) return Fail("The function has no capture table.");
        if (function.Handlers is null) return Fail("The function has no handler table.");
        if (function.OptionalCount < 0 || function.OptionalCount > function.ParameterCount - (function.HasThis ? 1 : 0))
        {
            return Fail($"The function has {function.OptionalCount} optional parameters of {function.ParameterCount}.");
        }
        for (var i = 0; i < function.Handlers.Count; i++)
        {
            var handler = function.Handlers[i];
            var error = handler.Start < 0 || handler.Start >= handler.End || handler.End > function.Code.Count
                ? $"its region, {handler.Start} to {handler.End}, is not within the code."
                : Index(handler.Target, function.Code.Count, "target instruction") ?? Register(function, handler.Value, "exception")
                  ?? Register(function, handler.Origin, "origin")
                  ?? (handler.Value == handler.Origin ? "its exception and its origin share a register." : null);
            if (error is not null) return Fail($"Handler {i}: {error}");
        }

        var code = function.Code;
        for (var i = 0; i < code.Count; i++)
        {
            var error = CheckOperands(module, function, code[i]);
            if (error is not null) return Fail($"Instruction {i} ({code[i].OpCode}): {error}", i);
            if (!EndsPath(code[i].OpCode) && i == code.Count - 1)
            {
                return Fail($"Instruction {i} ({code[i].OpCode}) is the last one, and the code would run off its end.", i);
            }
        }
        return CheckRegistersAreWrittenBeforeRead(module, function);
    }

    private static string? CheckOperands(WsCompiledModule module, WsFunction function, WsInstruction instruction)
    {
        var (a, b, c, d) = (instruction.A, instruction.B, instruction.C, instruction.D);
        switch (instruction.OpCode)
        {
            case WsOpCode.LoadConstant:
                return Register(function, a, "destination") ?? Index(b, module.Constants.Count, "constant") ?? Unused(c, d);
            case WsOpCode.Move:
            case WsOpCode.Negate:
            case WsOpCode.Plus:
            case WsOpCode.ToText:
                return Register(function, a, "destination") ?? Register(function, b, "operand") ?? Unused(c, d);
            case WsOpCode.Add:
            case WsOpCode.Subtract:
            case WsOpCode.Multiply:
            case WsOpCode.Divide:
                return Register(function, a, "destination") ?? Register(function, b, "left")
                    ?? Register(function, c, "right") ?? Unused(d);
            case WsOpCode.CallBuiltin:
                if (Register(function, a, "destination") is { } destination) return destination;
                if (Index(b, module.Imports.Count, "import") is { } importError) return importError;
                var import = module.Imports[b];
                if (d != import.Arity) return $"'{import.Name}' takes {import.Arity} arguments, not {d}.";
                if (d == 0) return Unused(c);
                if (c < 0 || c > function.RegisterCount - d)
                {
                    return $"its arguments, registers {c} to {(long)c + d - 1}, are not all in the function's {function.RegisterCount}.";
                }
                return null;
            case WsOpCode.Jump:
                return Index(a, function.Code.Count, "target instruction") ?? Unused(b, c, d);
            case WsOpCode.LoadNull:
                return Register(function, a, "destination") ?? Unused(b, c, d);
            case WsOpCode.LoadBoolean:
                return Register(function, a, "destination") ?? (b is 0 or 1 ? null : $"its value is {b}, not 0 or 1.")
                    ?? Unused(c, d);
            case WsOpCode.Not:
                return Register(function, a, "destination") ?? Register(function, b, "operand") ?? Unused(c, d);
            case WsOpCode.Equal:
            case WsOpCode.NotEqual:
            case WsOpCode.Less:
            case WsOpCode.LessOrEqual:
            case WsOpCode.Greater:
            case WsOpCode.GreaterOrEqual:
                return Register(function, a, "destination") ?? Register(function, b, "left")
                    ?? Register(function, c, "right") ?? Unused(d);
            case WsOpCode.JumpIfFalse:
            case WsOpCode.JumpIfTrue:
            case WsOpCode.JumpIfGiven:
                return Register(function, a, "condition") ?? Index(b, function.Code.Count, "target instruction")
                    ?? Unused(c, d);
            case WsOpCode.CheckBoolean:
            case WsOpCode.Dispose:
            case WsOpCode.Throw:
            case WsOpCode.NewCell:
            case WsOpCode.BoxRegister:
            case WsOpCode.CopyCell:
            case WsOpCode.ReturnValue:
                return Register(function, a, "operand") ?? Unused(b, c, d);
            case WsOpCode.LoadCell:
                return Register(function, a, "destination") ?? Register(function, b, "cell") ?? Unused(c, d);
            case WsOpCode.Rethrow:
                return Register(function, a, "exception") ?? Register(function, b, "origin") ?? Unused(c, d);
            case WsOpCode.IsInstance:
                return Register(function, a, "destination") ?? Register(function, b, "value") ?? Register(function, c, "class")
                    ?? Unused(d);
            case WsOpCode.StoreCell:
                return Register(function, a, "cell") ?? Register(function, b, "source") ?? Unused(c, d);
            case WsOpCode.LoadCapture:
                return Register(function, a, "destination") ?? Index(b, function.Captures.Count, "capture") ?? Unused(c, d);
            case WsOpCode.StoreCapture:
                return Index(a, function.Captures.Count, "capture") ?? Register(function, b, "source") ?? Unused(c, d);
            case WsOpCode.LoadBuiltin:
                return Register(function, a, "destination") ?? Index(b, module.Imports.Count, "import") ?? Unused(c, d);
            case WsOpCode.MakeClass:
            {
                if ((Register(function, a, "destination") ?? Index(b, module.Classes.Count, "class")
                     ?? (d is 0 or 1 ? null : $"its D is {d}, not 0 or 1.") ?? (d == 1 ? Register(function, c, "base") : Unused(c)))
                    is { } classError)
                {
                    return classError;
                }
                foreach (var member in WsClassTables.Functions(module.Classes[b]))
                {
                    foreach (var capture in module.Functions[member].Captures)
                    {
                        var error = capture.FromLocal
                            ? Register(function, capture.Index, "captured")
                            : Index(capture.Index, function.Captures.Count, "capture");
                        if (error is not null) return $"function {member}'s capture: {error}";
                    }
                }
                return null;
            }
            case WsOpCode.New:
                if ((Register(function, a, "destination") ?? Register(function, b, "class")) is { } newError) return newError;
                if (d < 0) return $"its argument count, {d}, is negative.";
                if (d == 0) return Unused(c);
                if (c < 0 || c > function.RegisterCount - d)
                {
                    return $"its arguments, registers {c} to {(long)c + d - 1}, are not all in the function's {function.RegisterCount}.";
                }
                return null;
            case WsOpCode.SetMember:
                return Register(function, a, "target") ?? Name(module, b) ?? Register(function, c, "value") ?? Unused(d);
            case WsOpCode.GetSuper:
                return Register(function, a, "destination") ?? Register(function, b, "class") ?? Register(function, c, "this")
                    ?? Name(module, d);
            case WsOpCode.MakeClosure:
            {
                if ((Register(function, a, "destination") ?? Index(b, module.Functions.Count, "function") ?? Unused(c, d))
                    is { } closureError)
                {
                    return closureError;
                }
                if (module.Functions[b] is not { Captures: { } captures }) return $"function {b} is missing.";
                if (module.Functions[b].HasThis) return $"function {b} is a method, which only its class makes.";
                foreach (var capture in captures)
                {
                    var error = capture.FromLocal
                        ? Register(function, capture.Index, "captured")
                        : Index(capture.Index, function.Captures.Count, "capture");
                    if (error is not null) return $"function {b}'s capture: {error}";
                }
                return null;
            }
            case WsOpCode.Call:
                if ((Register(function, a, "destination") ?? Register(function, b, "callee")) is { } callError) return callError;
                if (d < 0) return $"its argument count, {d}, is negative.";
                if (d == 0) return Unused(c);
                if (c < 0 || c > function.RegisterCount - d)
                {
                    return $"its arguments, registers {c} to {(long)c + d - 1}, are not all in the function's {function.RegisterCount}.";
                }
                return null;
            case WsOpCode.Return:
                return Unused(a, b, c, d);
            case WsOpCode.GetMember:
                return Register(function, a, "destination") ?? Register(function, b, "target") ?? Name(module, c)
                    ?? (d is 0 or 1 ? null : $"its D is {d}, not 0 or 1.");
            case WsOpCode.CallMethod:
                if ((Register(function, a, "destination") ?? Name(module, b)) is { } methodError) return methodError;
                if (d < 0) return $"its argument count, {d}, is negative.";
                if (c < 0 || c > function.RegisterCount - 1 - d)
                {
                    return $"its target and arguments, registers {c} to {(long)c + d}, " +
                           $"are not all in the function's {function.RegisterCount}.";
                }
                return null;
            case WsOpCode.NewArray:
            case WsOpCode.NewSet:
                if (Register(function, a, "destination") is { } arrayError) return arrayError;
                if (c < 0) return $"its element count, {c}, is negative.";
                if (c == 0) return Unused(b, d);
                if (b < 0 || b > function.RegisterCount - c)
                {
                    return $"its elements, registers {b} to {(long)b + c - 1}, are not all in the function's {function.RegisterCount}.";
                }
                return Unused(d);
            case WsOpCode.NewMap:
                if (Register(function, a, "destination") is { } mapError) return mapError;
                if (c < 0 || c > WsFunction.MaxRegisters) return $"its entry count, {c}, is not 0 to {WsFunction.MaxRegisters}.";
                if (c == 0) return Unused(b, d);
                if (b < 0 || b > function.RegisterCount - 2 * c)
                {
                    return $"its keys and values, registers {b} to {(long)b + 2 * c - 1}, are not all in the function's {function.RegisterCount}.";
                }
                return Unused(d);
            case WsOpCode.ForEachNext:
                if (Index(a, function.Code.Count, "target instruction") is { } targetError) return targetError;
                if (c is not (1 or 2)) return $"its item count is {c}, not 1 or 2.";
                if (b < 0 || b > function.RegisterCount - 2 - c)
                {
                    return $"its registers, {b} to {(long)b + 1 + c}, are not all in the function's {function.RegisterCount}.";
                }
                return Unused(d);
            case WsOpCode.GetIndex:
                return Register(function, a, "destination") ?? Register(function, b, "target")
                    ?? Register(function, c, "index") ?? (d is 0 or 1 ? null : $"its D is {d}, not 0 or 1.");
            case WsOpCode.SetIndex:
                return Register(function, a, "target") ?? Register(function, b, "index") ?? Register(function, c, "value")
                    ?? Unused(d);
            default:
                return $"the operation {(int)instruction.OpCode} is unknown.";
        }
    }

    /// <summary>How many 64-bit words of register state the dataflow check may keep (128 MB).</summary>
    public const long MaxStateWords = 16_000_000;
    /// <summary>How many instruction steps the dataflow check may take.</summary>
    public const long MaxSteps = 100_000_000;

    /// <summary>
    /// Follows every path from the first instruction, keeping the set of registers certainly written (the
    /// intersection over the paths that reach it) at the start of each block, where a path can arrive by a jump, or
    /// by an exception: every instruction in a protected region leads to its handler, with the registers written
    /// before it, and the handler's exception and origin (section 3.7). This goes on until nothing changes. A set only
    /// shrinks, so this ends; its memory and its steps are bounded, and a module beyond either bound is rejected
    /// rather than verified slowly.
    /// </summary>
    private static WsVerificationResult CheckRegistersAreWrittenBeforeRead(WsCompiledModule module, WsFunction function)
    {
        var code = function.Code;
        const int locals = 0; // Every register, locals included, must be written before it is read.
        var words = (function.RegisterCount - locals + 63) / 64;

        // Blocks start at the first instruction, at every jump target, and at every handler.
        var isBlockStart = new bool[code.Count];
        isBlockStart[0] = true;
        var blockCount = 1L;
        foreach (var instruction in code)
        {
            if (JumpTarget(instruction) is int target && !isBlockStart[target])
            {
                isBlockStart[target] = true;
                blockCount++;
            }
        }
        foreach (var handler in function.Handlers)
        {
            if (isBlockStart[handler.Target]) continue;
            isBlockStart[handler.Target] = true;
            blockCount++;
        }
        var handlerAt = HandlerAt(code.Count, function.Handlers, out var coverSteps);
        if (coverSteps > MaxSteps) return Fail("The function's handlers are too many to verify in the steps allowed.");
        if (blockCount * Math.Max(words, 1) > MaxStateWords)
        {
            return Fail($"The function is too large to verify: {blockCount} blocks of {function.RegisterCount} registers.");
        }

        var entry = new ulong[words];
        for (var parameter = 0; parameter < function.ParameterCount; parameter++) Set(entry, parameter); // Arguments.
        var atBlockStart = new Dictionary<int, ulong[]> { [0] = entry };
        var pending = new Stack<int>();
        pending.Push(0);
        var steps = 0L;
        while (pending.Count > 0)
        {
            var index = pending.Pop();
            var state = (ulong[])atBlockStart[index].Clone();
            while (true)
            {
                if (++steps > MaxSteps) return Fail("The function is too complex to verify in the steps allowed.");
                var instruction = code[index];
                if (handlerAt[index] >= 0)
                {
                    // An exception raised here goes to the handler, with what is written so far.
                    var handler = function.Handlers[handlerAt[index]];
                    var caught = (ulong[])state.Clone();
                    Set(caught, handler.Value - locals);
                    Set(caught, handler.Origin - locals);
                    Flow(handler.Target, caught, atBlockStart, pending);
                }
                var unwritten = FirstUnwrittenRead(module, instruction, state, locals);
                if (unwritten >= 0)
                {
                    return Fail(
                        $"Instruction {index} ({instruction.OpCode}) reads register {unwritten}, not written on every path to it.",
                        index);
                }
                if (instruction.OpCode == WsOpCode.ForEachNext)
                {
                    // The position is written on both paths; the items only on the one that goes on into the loop.
                    Set(state, instruction.B + 1 - locals);
                    Flow(instruction.A, state, atBlockStart, pending);
                    for (var item = 0; item < instruction.C; item++) Set(state, instruction.B + 2 + item - locals);
                }
                else if (Writes(instruction) is int target && target >= locals)
                {
                    Set(state, target - locals);
                }

                if (instruction.OpCode is WsOpCode.Return or WsOpCode.ReturnValue or WsOpCode.Throw or WsOpCode.Rethrow) break;
                if (instruction.OpCode == WsOpCode.Jump)
                {
                    Flow(instruction.A, state, atBlockStart, pending);
                    break;
                }
                if (instruction.OpCode is WsOpCode.JumpIfFalse or WsOpCode.JumpIfTrue or WsOpCode.JumpIfGiven)
                {
                    Flow(instruction.B, state, atBlockStart, pending); // And on to the next instruction.
                }
                index++;
                if (isBlockStart[index])
                {
                    Flow(index, state, atBlockStart, pending);
                    break;
                }
            }
        }
        return WsVerificationResult.Valid;
    }

    /// <summary>
    /// For each instruction, the first handler whose region holds it, which an exception raised there goes to, or -1;
    /// <paramref name="steps"/> is the work done, which a module of many handlers could make large.
    /// </summary>
    internal static int[] HandlerAt<THandler>(int count, IReadOnlyList<THandler> handlers, out long steps)
        where THandler : struct
    {
        var at = new int[count];
        for (var index = 0; index < count; index++) at[index] = -1;
        steps = 0;
        for (var i = handlers.Count - 1; i >= 0; i--)
        {
            var (start, end) = handlers[i] switch
            {
                WsHandler handler => (handler.Start, handler.End),
                WsStackHandler handler => (handler.Start, handler.End),
                _ => (0, 0)
            };
            steps += end - start;
            if (steps > MaxSteps) return at;
            for (var index = start; index < end; index++) at[index] = i;
        }
        return at;
    }

    private static int? JumpTarget(WsInstruction instruction) => instruction.OpCode switch
    {
        WsOpCode.Jump or WsOpCode.ForEachNext => instruction.A,
        WsOpCode.JumpIfFalse or WsOpCode.JumpIfTrue or WsOpCode.JumpIfGiven => instruction.B,
        _ => null
    };

    private static void Flow(int target, ulong[] state, Dictionary<int, ulong[]> atBlockStart, Stack<int> pending)
    {
        if (!atBlockStart.TryGetValue(target, out var existing))
        {
            atBlockStart[target] = (ulong[])state.Clone();
            pending.Push(target);
            return;
        }
        var changed = false;
        for (var i = 0; i < existing.Length; i++)
        {
            var narrowed = existing[i] & state[i];
            if (narrowed != existing[i])
            {
                existing[i] = narrowed;
                changed = true;
            }
        }
        if (changed) pending.Push(target);
    }

    /// <summary>The first register the instruction reads that is not certainly written, or -1.</summary>
    private static int FirstUnwrittenRead(WsCompiledModule module, WsInstruction instruction, ulong[] state, int locals)
    {
        switch (instruction.OpCode)
        {
            case WsOpCode.BoxRegister:
            case WsOpCode.CopyCell:
            case WsOpCode.ReturnValue:
                return Unwritten(instruction.A, state, locals);
            case WsOpCode.LoadCell:
            case WsOpCode.StoreCapture:
                return Unwritten(instruction.B, state, locals);
            case WsOpCode.StoreCell:
            {
                var cell = Unwritten(instruction.A, state, locals);
                return cell >= 0 ? cell : Unwritten(instruction.B, state, locals);
            }
            case WsOpCode.MakeClosure:
                foreach (var capture in module.Functions[instruction.B].Captures)
                {
                    if (!capture.FromLocal) continue;
                    var captured = Unwritten(capture.Index, state, locals);
                    if (captured >= 0) return captured;
                }
                return -1;
            case WsOpCode.Call:
            {
                var callee = Unwritten(instruction.B, state, locals);
                if (callee >= 0) return callee;
                for (var i = 0; i < instruction.D; i++)
                {
                    var argument = Unwritten(instruction.C + i, state, locals);
                    if (argument >= 0) return argument;
                }
                return -1;
            }
            case WsOpCode.Move:
            case WsOpCode.Negate:
            case WsOpCode.Plus:
            case WsOpCode.Not:
            case WsOpCode.ToText:
                return Unwritten(instruction.B, state, locals);
            case WsOpCode.JumpIfFalse:
            case WsOpCode.JumpIfTrue:
            case WsOpCode.JumpIfGiven:
            case WsOpCode.CheckBoolean:
            case WsOpCode.Dispose:
            case WsOpCode.Throw:
                return Unwritten(instruction.A, state, locals);
            case WsOpCode.Rethrow:
            {
                var exception = Unwritten(instruction.A, state, locals);
                return exception >= 0 ? exception : Unwritten(instruction.B, state, locals);
            }
            case WsOpCode.IsInstance:
            {
                var value = Unwritten(instruction.B, state, locals);
                return value >= 0 ? value : Unwritten(instruction.C, state, locals);
            }
            case WsOpCode.Add:
            case WsOpCode.Subtract:
            case WsOpCode.Multiply:
            case WsOpCode.Divide:
            case WsOpCode.Equal:
            case WsOpCode.NotEqual:
            case WsOpCode.Less:
            case WsOpCode.LessOrEqual:
            case WsOpCode.Greater:
            case WsOpCode.GreaterOrEqual:
                var left = Unwritten(instruction.B, state, locals);
                return left >= 0 ? left : Unwritten(instruction.C, state, locals);
            case WsOpCode.CallBuiltin:
                for (var i = 0; i < instruction.D; i++)
                {
                    var argument = Unwritten(instruction.C + i, state, locals);
                    if (argument >= 0) return argument;
                }
                return -1;
            case WsOpCode.GetMember:
                return Unwritten(instruction.B, state, locals);
            case WsOpCode.SetMember:
            {
                var target = Unwritten(instruction.A, state, locals);
                return target >= 0 ? target : Unwritten(instruction.C, state, locals);
            }
            case WsOpCode.New:
            {
                var target = Unwritten(instruction.B, state, locals);
                if (target >= 0) return target;
                for (var i = 0; i < instruction.D; i++)
                {
                    var argument = Unwritten(instruction.C + i, state, locals);
                    if (argument >= 0) return argument;
                }
                return -1;
            }
            case WsOpCode.GetSuper:
            {
                var classValue = Unwritten(instruction.B, state, locals);
                return classValue >= 0 ? classValue : Unwritten(instruction.C, state, locals);
            }
            case WsOpCode.MakeClass:
                if (instruction.D == 1 && Unwritten(instruction.C, state, locals) is var baseValue and >= 0) return baseValue;
                foreach (var member in WsClassTables.Functions(module.Classes[instruction.B]))
                {
                    foreach (var capture in module.Functions[member].Captures)
                    {
                        if (!capture.FromLocal) continue;
                        var captured = Unwritten(capture.Index, state, locals);
                        if (captured >= 0) return captured;
                    }
                }
                return -1;
            case WsOpCode.ForEachNext:
            {
                var collection = Unwritten(instruction.B, state, locals);
                return collection >= 0 ? collection : Unwritten(instruction.B + 1, state, locals);
            }
            case WsOpCode.NewMap:
                for (var i = 0; i < 2 * instruction.C; i++)
                {
                    var entry = Unwritten(instruction.B + i, state, locals);
                    if (entry >= 0) return entry;
                }
                return -1;
            case WsOpCode.NewArray:
            case WsOpCode.NewSet:
                for (var i = 0; i < instruction.C; i++)
                {
                    var element = Unwritten(instruction.B + i, state, locals);
                    if (element >= 0) return element;
                }
                return -1;
            case WsOpCode.CallMethod:
                for (var i = 0; i <= instruction.D; i++)
                {
                    var operand = Unwritten(instruction.C + i, state, locals);
                    if (operand >= 0) return operand;
                }
                return -1;
            case WsOpCode.GetIndex:
            {
                var target = Unwritten(instruction.B, state, locals);
                return target >= 0 ? target : Unwritten(instruction.C, state, locals);
            }
            case WsOpCode.SetIndex:
            {
                var target = Unwritten(instruction.A, state, locals);
                if (target >= 0) return target;
                var index = Unwritten(instruction.B, state, locals);
                return index >= 0 ? index : Unwritten(instruction.C, state, locals);
            }
            default:
                return -1;
        }
    }

    private static int Unwritten(int register, ulong[] state, int locals)
    {
        var temporary = register - locals;
        if (temporary < 0) return -1;
        return (state[temporary >> 6] & (1UL << (temporary & 63))) != 0 ? -1 : register;
    }

    private static void Set(ulong[] state, int temporary) => state[temporary >> 6] |= 1UL << (temporary & 63);

    private static int? Writes(WsInstruction instruction) => instruction.OpCode switch
    {
        WsOpCode.LoadConstant or WsOpCode.Move or WsOpCode.Negate or WsOpCode.Plus or WsOpCode.ToText or WsOpCode.IsInstance or WsOpCode.Add or WsOpCode.Subtract
            or WsOpCode.Multiply or WsOpCode.Divide or WsOpCode.CallBuiltin or WsOpCode.LoadNull or WsOpCode.LoadBoolean
            or WsOpCode.Not or WsOpCode.Equal or WsOpCode.NotEqual or WsOpCode.Less or WsOpCode.LessOrEqual
            or WsOpCode.Greater or WsOpCode.GreaterOrEqual or WsOpCode.NewCell or WsOpCode.BoxRegister or WsOpCode.CopyCell
            or WsOpCode.LoadCell or WsOpCode.LoadCapture or WsOpCode.MakeClosure or WsOpCode.LoadBuiltin
            or WsOpCode.Call or WsOpCode.GetMember or WsOpCode.CallMethod or WsOpCode.GetIndex
            or WsOpCode.NewArray or WsOpCode.NewMap or WsOpCode.NewSet or WsOpCode.MakeClass or WsOpCode.New
            or WsOpCode.GetSuper => instruction.A,
        _ => null
    };

    private static bool EndsPath(WsOpCode opCode) =>
        opCode is WsOpCode.Return or WsOpCode.ReturnValue or WsOpCode.Jump or WsOpCode.Throw or WsOpCode.Rethrow;

    private static string? Register(WsFunction function, int register, string role) =>
        register >= 0 && register < function.RegisterCount
            ? null
            : $"its {role} register {register} is not one of the function's {function.RegisterCount}.";

    /// <summary>A member's name, which must be a string constant.</summary>
    private static string? Name(WsCompiledModule module, int constant) =>
        Index(constant, module.Constants.Count, "constant")
        ?? (module.Constants[constant].Kind == WsConstantKind.String ? null : $"constant {constant} is not a name.");

    private static string? Index(int value, int count, string what) =>
        value >= 0 && value < count ? null : $"{what} {value} does not exist (there are {count}).";

    private static string? Unused(params int[] operands)
    {
        foreach (var operand in operands)
        {
            if (operand != 0) return "an operand it does not use is not 0.";
        }
        return null;
    }

    private static WsVerificationResult Fail(string error, int instruction = -1) => new(false, error, instruction);
}
