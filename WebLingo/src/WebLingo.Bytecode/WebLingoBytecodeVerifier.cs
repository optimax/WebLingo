namespace WebLingo.Bytecode;

/// <summary>
/// The bytecode verifier (specification, sections 5.5 and 8.4). A module must pass it before it runs. It checks the
/// module's structure, every instruction's operation and operands (registers, constants, imports and their arity,
/// branch targets, and that unused operands are 0), that no path runs off the end of the code, and, following every
/// path, that no register, local or temporary, is read before it is written. It never throws on malformed input.
/// </summary>
public sealed class WebLingoBytecodeVerifier : IWebLingoBytecodeVerifier
{
    public WebLingoVerificationResult Verify(WebLingoCompiledModule module)
    {
        if (module is null) return Fail("There is no module.");
        if (module.Name is null) return Fail("The module has no name.");
        if (module.Constants is null) return Fail("The module has no constant table.");
        if (module.Imports is null) return Fail("The module has no import table.");

        for (var i = 0; i < module.Constants.Count; i++)
        {
            var constant = module.Constants[i];
            if (constant.Kind is not (WebLingoConstantKind.Int64 or WebLingoConstantKind.Binary64 or WebLingoConstantKind.String))
            {
                return Fail($"Constant {i} has the unknown kind {(int)constant.Kind}.");
            }
            if ((constant.Kind == WebLingoConstantKind.String) != (constant.Text is not null))
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
        if (module.EntryPoint is { } main && (main.ParameterCount != (module.IsImport ? 1 : 0) || main.Captures is not { Count: 0 } || main.HasThis))
        {
            return Fail("The entry point takes no parameters and captures nothing.");
        }
        var methods = new List<WebLingoFunction?>();
        foreach (var function in module.Functions) methods.Add(function);
        if (WebLingoClassTables.Check(module.Classes,
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
        if (WebLingoClassTables.CheckRoles(module.Classes.Count, module.ValueClasses, module.ErrorClasses) is { } rolesError)
        {
            return Fail(rolesError);
        }
        if (WebLingoClassTables.CheckUnits(module.Units,
                (index, parameters) => index >= 0 && index < methods.Count
                    && methods[index] is { IsAsync: true, HasThis: false } unit && unit.ParameterCount == parameters)
            is { } unitsError)
        {
            return Fail(unitsError);
        }
        var result = VerifyFunction(module, module.EntryPoint);
        for (var i = 0; i < module.Functions.Count && result.IsValid; i++)
        {
            result = VerifyFunction(module, module.Functions[i]);
            if (!result.IsValid) result = result with { Error = $"Function {i}: {result.Error}" };
        }
        return result;
    }

    private static WebLingoVerificationResult VerifyFunction(WebLingoCompiledModule module, WebLingoFunction? function)
    {
        if (function is null) return Fail("The module has no entry point.");
        if (function.Code is null || function.Code.Count == 0) return Fail("The function has no code.");
        if (function.RegisterCount < 0 || function.RegisterCount > WebLingoFunction.MaxRegisters)
        {
            return Fail($"The function has {function.RegisterCount} registers; it may have 0 to {WebLingoFunction.MaxRegisters}.");
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
            var error = CheckOperands(module, function, code[i])
                ?? (code[i].OpCode == WebLingoOpCode.Await && !function.IsAsync ? "an await in a function that is not async." : null);
            if (error is not null) return Fail($"Instruction {i} ({code[i].OpCode}): {error}", i);
            if (!EndsPath(code[i].OpCode) && i == code.Count - 1)
            {
                return Fail($"Instruction {i} ({code[i].OpCode}) is the last one, and the code would run off its end.", i);
            }
        }
        return CheckRegistersAreWrittenBeforeRead(module, function);
    }

    private static string? CheckOperands(WebLingoCompiledModule module, WebLingoFunction function, WebLingoInstruction instruction)
    {
        var (a, b, c, d) = (instruction.A, instruction.B, instruction.C, instruction.D);
        switch (instruction.OpCode)
        {
            case WebLingoOpCode.LoadConstant:
                return Register(function, a, "destination") ?? Index(b, module.Constants.Count, "constant") ?? Unused(c, d);
            case WebLingoOpCode.Move:
            case WebLingoOpCode.Negate:
            case WebLingoOpCode.Plus:
            case WebLingoOpCode.ToText:
            case WebLingoOpCode.Await:
                return Register(function, a, "destination") ?? Register(function, b, "operand") ?? Unused(c, d);
            case WebLingoOpCode.Add:
            case WebLingoOpCode.Subtract:
            case WebLingoOpCode.Multiply:
            case WebLingoOpCode.Divide:
                return Register(function, a, "destination") ?? Register(function, b, "left")
                    ?? Register(function, c, "right") ?? Unused(d);
            case WebLingoOpCode.CallBuiltin:
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
            case WebLingoOpCode.Jump:
                return Index(a, function.Code.Count, "target instruction") ?? Unused(b, c, d);
            case WebLingoOpCode.LoadNull:
                return Register(function, a, "destination") ?? Unused(b, c, d);
            case WebLingoOpCode.LoadBoolean:
                return Register(function, a, "destination") ?? (b is 0 or 1 ? null : $"its value is {b}, not 0 or 1.")
                    ?? Unused(c, d);
            case WebLingoOpCode.Not:
                return Register(function, a, "destination") ?? Register(function, b, "operand") ?? Unused(c, d);
            case WebLingoOpCode.Equal:
            case WebLingoOpCode.NotEqual:
            case WebLingoOpCode.Less:
            case WebLingoOpCode.LessOrEqual:
            case WebLingoOpCode.Greater:
            case WebLingoOpCode.GreaterOrEqual:
                return Register(function, a, "destination") ?? Register(function, b, "left")
                    ?? Register(function, c, "right") ?? Unused(d);
            case WebLingoOpCode.JumpIfFalse:
            case WebLingoOpCode.JumpIfTrue:
            case WebLingoOpCode.JumpIfGiven:
                return Register(function, a, "condition") ?? Index(b, function.Code.Count, "target instruction")
                    ?? Unused(c, d);
            case WebLingoOpCode.CheckBoolean:
            case WebLingoOpCode.Dispose:
            case WebLingoOpCode.Throw:
            case WebLingoOpCode.NewCell:
            case WebLingoOpCode.BoxRegister:
            case WebLingoOpCode.CopyCell:
            case WebLingoOpCode.ReturnValue:
                return Register(function, a, "operand") ?? Unused(b, c, d);
            case WebLingoOpCode.LoadCell:
                return Register(function, a, "destination") ?? Register(function, b, "cell") ?? Unused(c, d);
            case WebLingoOpCode.Rethrow:
                return Register(function, a, "exception") ?? Register(function, b, "origin") ?? Unused(c, d);
            case WebLingoOpCode.IsInstance:
                return Register(function, a, "destination") ?? Register(function, b, "value") ?? Register(function, c, "class")
                    ?? Unused(d);
            case WebLingoOpCode.Hold:
                return Register(function, a, "destination") ?? Register(function, b, "instance") ?? Register(function, c, "value")
                    ?? Unused(d);
            case WebLingoOpCode.StoreCell:
                return Register(function, a, "cell") ?? Register(function, b, "source") ?? Unused(c, d);
            case WebLingoOpCode.LoadCapture:
                return Register(function, a, "destination") ?? Index(b, function.Captures.Count, "capture") ?? Unused(c, d);
            case WebLingoOpCode.StoreCapture:
                return Index(a, function.Captures.Count, "capture") ?? Register(function, b, "source") ?? Unused(c, d);
            case WebLingoOpCode.LoadBuiltin:
                return Register(function, a, "destination") ?? Index(b, module.Imports.Count, "import") ?? Unused(c, d);
            case WebLingoOpCode.MakeClass:
            case WebLingoOpCode.Extend:
            {
                var operandError = instruction.OpCode == WebLingoOpCode.Extend
                    ? Register(function, a, "class") ?? Index(b, module.Classes.Count, "class") ?? Unused(c, d)
                    : Register(function, a, "destination") ?? Index(b, module.Classes.Count, "class")
                      ?? (d is 0 or 1 ? null : $"its D is {d}, not 0 or 1.") ?? (d == 1 ? Register(function, c, "base") : Unused(c));
                if (operandError is not null) return operandError;
                foreach (var member in WebLingoClassTables.Functions(module.Classes[b]))
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
            case WebLingoOpCode.New:
                if ((Register(function, a, "destination") ?? Register(function, b, "class")) is { } newError) return newError;
                if (d < 0) return $"its argument count, {d}, is negative.";
                if (d == 0) return Unused(c);
                if (c < 0 || c > function.RegisterCount - d)
                {
                    return $"its arguments, registers {c} to {(long)c + d - 1}, are not all in the function's {function.RegisterCount}.";
                }
                return null;
            case WebLingoOpCode.SetMember:
                return Register(function, a, "target") ?? Name(module, b) ?? Register(function, c, "value") ?? Unused(d);
            case WebLingoOpCode.GetSuper:
                return Register(function, a, "destination") ?? Register(function, b, "class") ?? Register(function, c, "this")
                    ?? Name(module, d);
            case WebLingoOpCode.MakeClosure:
            {
                if ((Register(function, a, "destination") ?? Index(b, module.Functions.Count, "function") ?? Unused(c, d))
                    is { } closureError)
                {
                    return closureError;
                }
                if (module.Functions[b] is not { Captures: { } captures }) return $"function {b} is missing.";
                foreach (var capture in captures)
                {
                    var error = capture.FromLocal
                        ? Register(function, capture.Index, "captured")
                        : Index(capture.Index, function.Captures.Count, "capture");
                    if (error is not null) return $"function {b}'s capture: {error}";
                }
                return null;
            }
            case WebLingoOpCode.Call:
                if ((Register(function, a, "destination") ?? Register(function, b, "callee")) is { } callError) return callError;
                if (d < 0) return $"its argument count, {d}, is negative.";
                if (d == 0) return Unused(c);
                if (c < 0 || c > function.RegisterCount - d)
                {
                    return $"its arguments, registers {c} to {(long)c + d - 1}, are not all in the function's {function.RegisterCount}.";
                }
                return null;
            case WebLingoOpCode.Return:
                return Unused(a, b, c, d);
            case WebLingoOpCode.GetMember:
                return Register(function, a, "destination") ?? Register(function, b, "target") ?? Name(module, c)
                    ?? (d is 0 or 1 ? null : $"its D is {d}, not 0 or 1.");
            case WebLingoOpCode.CallMethod:
                if ((Register(function, a, "destination") ?? Name(module, b)) is { } methodError) return methodError;
                if (d < 0) return $"its argument count, {d}, is negative.";
                if (c < 0 || c > function.RegisterCount - 1 - d)
                {
                    return $"its target and arguments, registers {c} to {(long)c + d}, " +
                           $"are not all in the function's {function.RegisterCount}.";
                }
                return null;
            case WebLingoOpCode.NewArray:
            case WebLingoOpCode.NewSet:
                if (Register(function, a, "destination") is { } arrayError) return arrayError;
                if (c < 0) return $"its element count, {c}, is negative.";
                if (c == 0) return Unused(b, d);
                if (b < 0 || b > function.RegisterCount - c)
                {
                    return $"its elements, registers {b} to {(long)b + c - 1}, are not all in the function's {function.RegisterCount}.";
                }
                return Unused(d);
            case WebLingoOpCode.ImportDynamic:
                return Register(function, a, "destination") ?? Register(function, b, "path")
                    ?? (d == 1 ? Register(function, c, "values")
                        : d == 0 && c == 0 ? null : $"its values' flag, {d}, is not 0 or 1, or C, {c}, is not 0 without them.");
            case WebLingoOpCode.Import:
                // D is 1 when C holds the values it gives the file's parameters, which a file that has some is always given.
                return Register(function, a, "destination") ?? Index(b, module.Units.Count, "file")
                    ?? (d == 1 ? Register(function, c, "values")
                        : d == 0 && c == 0 ? null : $"its values' flag, {d}, is not 0 or 1, or C, {c}, is not 0 without them.")
                    ?? (d == 0 && module.Units[b].Parameters.Count > 0 ? $"file {b} has parameters, and is given no values." : null);
            case WebLingoOpCode.RegisterUnit:
                return (a >= 1 && a < module.Units.Count ? null : $"its file, {a}, is not one the program imports.")
                    ?? Register(function, b, "closure") ?? Unused(c, d);
            case WebLingoOpCode.MakeModule:
                if ((Register(function, a, "destination") ?? Index(b, module.Units.Count, "file")) is { } moduleError) return moduleError;
                if (d != module.Units[b].Exports.Count) return $"its cell count, {d}, is not file {b}'s {module.Units[b].Exports.Count} exports.";
                if (d == 0) return Unused(c);
                return c < 0 || c > function.RegisterCount - d
                    ? $"its cells, registers {c} to {(long)c + d - 1}, are not all in the function's {function.RegisterCount}."
                    : null;
            case WebLingoOpCode.NewMap:
                if (Register(function, a, "destination") is { } mapError) return mapError;
                if (c < 0 || c > WebLingoFunction.MaxRegisters) return $"its entry count, {c}, is not 0 to {WebLingoFunction.MaxRegisters}.";
                if (c == 0) return Unused(b, d);
                if (b < 0 || b > function.RegisterCount - 2 * c)
                {
                    return $"its keys and values, registers {b} to {(long)b + 2 * c - 1}, are not all in the function's {function.RegisterCount}.";
                }
                return Unused(d);
            case WebLingoOpCode.ForEachNext:
                if (Index(a, function.Code.Count, "target instruction") is { } targetError) return targetError;
                if (c is not (1 or 2)) return $"its item count is {c}, not 1 or 2.";
                if (b < 0 || b > function.RegisterCount - 2 - c)
                {
                    return $"its registers, {b} to {(long)b + 1 + c}, are not all in the function's {function.RegisterCount}.";
                }
                return Unused(d);
            case WebLingoOpCode.GetIndex:
                return Register(function, a, "destination") ?? Register(function, b, "target")
                    ?? Register(function, c, "index") ?? (d is 0 or 1 ? null : $"its D is {d}, not 0 or 1.");
            case WebLingoOpCode.SetIndex:
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
    private static WebLingoVerificationResult CheckRegistersAreWrittenBeforeRead(WebLingoCompiledModule module, WebLingoFunction function)
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
                if (instruction.OpCode == WebLingoOpCode.ForEachNext)
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

                if (instruction.OpCode is WebLingoOpCode.Return or WebLingoOpCode.ReturnValue or WebLingoOpCode.Throw or WebLingoOpCode.Rethrow) break;
                if (instruction.OpCode == WebLingoOpCode.Jump)
                {
                    Flow(instruction.A, state, atBlockStart, pending);
                    break;
                }
                if (instruction.OpCode is WebLingoOpCode.JumpIfFalse or WebLingoOpCode.JumpIfTrue or WebLingoOpCode.JumpIfGiven)
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
        return WebLingoVerificationResult.Valid;
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
                WebLingoHandler handler => (handler.Start, handler.End),
                WebLingoStackHandler handler => (handler.Start, handler.End),
                _ => (0, 0)
            };
            steps += end - start;
            if (steps > MaxSteps) return at;
            for (var index = start; index < end; index++) at[index] = i;
        }
        return at;
    }

    private static int? JumpTarget(WebLingoInstruction instruction) => instruction.OpCode switch
    {
        WebLingoOpCode.Jump or WebLingoOpCode.ForEachNext => instruction.A,
        WebLingoOpCode.JumpIfFalse or WebLingoOpCode.JumpIfTrue or WebLingoOpCode.JumpIfGiven => instruction.B,
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
    private static int FirstUnwrittenRead(WebLingoCompiledModule module, WebLingoInstruction instruction, ulong[] state, int locals)
    {
        switch (instruction.OpCode)
        {
            case WebLingoOpCode.BoxRegister:
            case WebLingoOpCode.CopyCell:
            case WebLingoOpCode.ReturnValue:
                return Unwritten(instruction.A, state, locals);
            case WebLingoOpCode.LoadCell:
            case WebLingoOpCode.StoreCapture:
                return Unwritten(instruction.B, state, locals);
            case WebLingoOpCode.StoreCell:
            {
                var cell = Unwritten(instruction.A, state, locals);
                return cell >= 0 ? cell : Unwritten(instruction.B, state, locals);
            }
            case WebLingoOpCode.MakeClosure:
                foreach (var capture in module.Functions[instruction.B].Captures)
                {
                    if (!capture.FromLocal) continue;
                    var captured = Unwritten(capture.Index, state, locals);
                    if (captured >= 0) return captured;
                }
                return -1;
            case WebLingoOpCode.Call:
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
            case WebLingoOpCode.Move:
            case WebLingoOpCode.Negate:
            case WebLingoOpCode.Plus:
            case WebLingoOpCode.Not:
            case WebLingoOpCode.ToText:
            case WebLingoOpCode.Await:
                return Unwritten(instruction.B, state, locals);
            case WebLingoOpCode.JumpIfFalse:
            case WebLingoOpCode.JumpIfTrue:
            case WebLingoOpCode.JumpIfGiven:
            case WebLingoOpCode.CheckBoolean:
            case WebLingoOpCode.Dispose:
            case WebLingoOpCode.Throw:
                return Unwritten(instruction.A, state, locals);
            case WebLingoOpCode.Import:
                return instruction.D == 1 ? Unwritten(instruction.C, state, locals) : -1;
            case WebLingoOpCode.ImportDynamic:
            {
                var path = Unwritten(instruction.B, state, locals);
                return path >= 0 || instruction.D != 1 ? path : Unwritten(instruction.C, state, locals);
            }
            case WebLingoOpCode.Rethrow:
            {
                var exception = Unwritten(instruction.A, state, locals);
                return exception >= 0 ? exception : Unwritten(instruction.B, state, locals);
            }
            case WebLingoOpCode.IsInstance:
            case WebLingoOpCode.Hold:
            {
                var value = Unwritten(instruction.B, state, locals);
                return value >= 0 ? value : Unwritten(instruction.C, state, locals);
            }
            case WebLingoOpCode.Add:
            case WebLingoOpCode.Subtract:
            case WebLingoOpCode.Multiply:
            case WebLingoOpCode.Divide:
            case WebLingoOpCode.Equal:
            case WebLingoOpCode.NotEqual:
            case WebLingoOpCode.Less:
            case WebLingoOpCode.LessOrEqual:
            case WebLingoOpCode.Greater:
            case WebLingoOpCode.GreaterOrEqual:
                var left = Unwritten(instruction.B, state, locals);
                return left >= 0 ? left : Unwritten(instruction.C, state, locals);
            case WebLingoOpCode.CallBuiltin:
                for (var i = 0; i < instruction.D; i++)
                {
                    var argument = Unwritten(instruction.C + i, state, locals);
                    if (argument >= 0) return argument;
                }
                return -1;
            case WebLingoOpCode.GetMember:
                return Unwritten(instruction.B, state, locals);
            case WebLingoOpCode.SetMember:
            {
                var target = Unwritten(instruction.A, state, locals);
                return target >= 0 ? target : Unwritten(instruction.C, state, locals);
            }
            case WebLingoOpCode.New:
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
            case WebLingoOpCode.GetSuper:
            {
                var classValue = Unwritten(instruction.B, state, locals);
                return classValue >= 0 ? classValue : Unwritten(instruction.C, state, locals);
            }
            case WebLingoOpCode.MakeClass:
            case WebLingoOpCode.Extend:
                if (instruction.OpCode == WebLingoOpCode.MakeClass && instruction.D == 1
                    && Unwritten(instruction.C, state, locals) is var baseValue and >= 0)
                {
                    return baseValue;
                }
                if (instruction.OpCode == WebLingoOpCode.Extend && Unwritten(instruction.A, state, locals) is var extended and >= 0)
                {
                    return extended;
                }
                foreach (var member in WebLingoClassTables.Functions(module.Classes[instruction.B]))
                {
                    foreach (var capture in module.Functions[member].Captures)
                    {
                        if (!capture.FromLocal) continue;
                        var captured = Unwritten(capture.Index, state, locals);
                        if (captured >= 0) return captured;
                    }
                }
                return -1;
            case WebLingoOpCode.ForEachNext:
            {
                var collection = Unwritten(instruction.B, state, locals);
                return collection >= 0 ? collection : Unwritten(instruction.B + 1, state, locals);
            }
            case WebLingoOpCode.NewMap:
                for (var i = 0; i < 2 * instruction.C; i++)
                {
                    var entry = Unwritten(instruction.B + i, state, locals);
                    if (entry >= 0) return entry;
                }
                return -1;
            case WebLingoOpCode.RegisterUnit:
                return Unwritten(instruction.B, state, locals);
            case WebLingoOpCode.MakeModule:
                for (var i = 0; i < instruction.D; i++)
                {
                    var cell = Unwritten(instruction.C + i, state, locals);
                    if (cell >= 0) return cell;
                }
                return -1;
            case WebLingoOpCode.NewArray:
            case WebLingoOpCode.NewSet:
                for (var i = 0; i < instruction.C; i++)
                {
                    var element = Unwritten(instruction.B + i, state, locals);
                    if (element >= 0) return element;
                }
                return -1;
            case WebLingoOpCode.CallMethod:
                for (var i = 0; i <= instruction.D; i++)
                {
                    var operand = Unwritten(instruction.C + i, state, locals);
                    if (operand >= 0) return operand;
                }
                return -1;
            case WebLingoOpCode.GetIndex:
            {
                var target = Unwritten(instruction.B, state, locals);
                return target >= 0 ? target : Unwritten(instruction.C, state, locals);
            }
            case WebLingoOpCode.SetIndex:
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

    private static int? Writes(WebLingoInstruction instruction) => instruction.OpCode switch
    {
        WebLingoOpCode.LoadConstant or WebLingoOpCode.Move or WebLingoOpCode.Negate or WebLingoOpCode.Plus or WebLingoOpCode.ToText or WebLingoOpCode.IsInstance or WebLingoOpCode.Hold or WebLingoOpCode.Await or WebLingoOpCode.Import or WebLingoOpCode.ImportDynamic
            or WebLingoOpCode.MakeModule or WebLingoOpCode.Add or WebLingoOpCode.Subtract
            or WebLingoOpCode.Multiply or WebLingoOpCode.Divide or WebLingoOpCode.CallBuiltin or WebLingoOpCode.LoadNull or WebLingoOpCode.LoadBoolean
            or WebLingoOpCode.Not or WebLingoOpCode.Equal or WebLingoOpCode.NotEqual or WebLingoOpCode.Less or WebLingoOpCode.LessOrEqual
            or WebLingoOpCode.Greater or WebLingoOpCode.GreaterOrEqual or WebLingoOpCode.NewCell or WebLingoOpCode.BoxRegister or WebLingoOpCode.CopyCell
            or WebLingoOpCode.LoadCell or WebLingoOpCode.LoadCapture or WebLingoOpCode.MakeClosure or WebLingoOpCode.LoadBuiltin
            or WebLingoOpCode.Call or WebLingoOpCode.GetMember or WebLingoOpCode.CallMethod or WebLingoOpCode.GetIndex
            or WebLingoOpCode.NewArray or WebLingoOpCode.NewMap or WebLingoOpCode.NewSet or WebLingoOpCode.MakeClass or WebLingoOpCode.New
            or WebLingoOpCode.GetSuper => instruction.A,
        _ => null
    };

    private static bool EndsPath(WebLingoOpCode opCode) =>
        opCode is WebLingoOpCode.Return or WebLingoOpCode.ReturnValue or WebLingoOpCode.Jump or WebLingoOpCode.Throw or WebLingoOpCode.Rethrow;

    private static string? Register(WebLingoFunction function, int register, string role) =>
        register >= 0 && register < function.RegisterCount
            ? null
            : $"its {role} register {register} is not one of the function's {function.RegisterCount}.";

    /// <summary>A member's name, which must be a string constant.</summary>
    private static string? Name(WebLingoCompiledModule module, int constant) =>
        Index(constant, module.Constants.Count, "constant")
        ?? (module.Constants[constant].Kind == WebLingoConstantKind.String ? null : $"constant {constant} is not a name.");

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

    private static WebLingoVerificationResult Fail(string error, int instruction = -1) => new(false, error, instruction);
}
