namespace WebLingo.Bytecode;

/// <summary>
/// The stack bytecode's verifier (specification, section 5.5): a module must pass it before the stack VM runs it. It
/// checks what <see cref="WebLingoBytecodeVerifier"/> checks of a register module (the tables, every instruction's operation
/// and operands, that unused operands are 0, that no path runs off the end of the code, and that no local is read
/// before it is written, on every path), and, following every path, that the operand stack has the same depth
/// wherever paths meet, never pops a value it does not have, and never holds more than the function's
/// <see cref="WebLingoStackFunction.MaxStack"/>. It never throws on malformed input.
/// </summary>
public sealed class WebLingoStackBytecodeVerifier
{
    /// <summary>How many 64-bit words of state the dataflow check may keep (128 MB), as the register verifier.</summary>
    public const long MaxStateWords = WebLingoBytecodeVerifier.MaxStateWords;
    /// <summary>How many instruction steps the dataflow check may take, as the register verifier's.</summary>
    public const long MaxSteps = WebLingoBytecodeVerifier.MaxSteps;

    public WebLingoVerificationResult Verify(WebLingoStackModule module)
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
        if (module.EntryPoint is { } main && (main.ParameterCount != (module.IsImport || module.Parameters is { Count: > 0 } ? 1 : 0) || main.Captures is not { Count: 0 } || main.HasThis))
        {
            return Fail("The entry point takes no parameters and captures nothing.");
        }
        var methods = new List<WebLingoStackFunction?>();
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

    private static WebLingoVerificationResult VerifyFunction(WebLingoStackModule module, WebLingoStackFunction? function)
    {
        if (function is null) return Fail("The module has no entry point.");
        if (function.Code is null || function.Code.Count == 0) return Fail("The function has no code.");
        if (function.LocalCount < 0 || function.MaxStack < 0
            || (long)function.LocalCount + function.MaxStack > WebLingoFunction.MaxRegisters)
        {
            return Fail($"The function has {function.LocalCount} locals and a stack of {function.MaxStack}; " +
                        $"it may have 0 to {WebLingoFunction.MaxRegisters} slots in all.");
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
                : handler.Target < 0 || handler.Target >= function.Code.Count
                    ? $"its target instruction {handler.Target} does not exist."
                    : handler.Value < 0 || handler.Value >= function.LocalCount || handler.Origin < 0
                      || handler.Origin >= function.LocalCount || handler.Value == handler.Origin
                        ? $"its exception and origin, locals {handler.Value} and {handler.Origin}, are not two of its locals."
                        : handler.Depth < 0 || handler.Depth > function.MaxStack
                            ? $"its stack depth, {handler.Depth}, is not 0 to {function.MaxStack}."
                            : null;
            if (error is not null) return Fail($"Handler {i}: {error}");
        }

        var code = function.Code;
        for (var i = 0; i < code.Count; i++)
        {
            var error = CheckOperands(module, function, code[i])
                ?? (code[i].OpCode == WebLingoStackOpCode.Await && !function.IsAsync ? "an await in a function that is not async." : null);
            if (error is not null) return Fail($"Instruction {i} ({code[i].OpCode}): {error}", i);
            if (!EndsPath(code[i].OpCode) && i == code.Count - 1)
            {
                return Fail($"Instruction {i} ({code[i].OpCode}) is the last one, and the code would run off its end.", i);
            }
        }
        return CheckFlow(module, function);
    }

    private static string? CheckOperands(WebLingoStackModule module, WebLingoStackFunction function, WebLingoStackInstruction instruction)
    {
        var (a, b) = (instruction.A, instruction.B);
        switch (instruction.OpCode)
        {
            case WebLingoStackOpCode.LoadConstant:
                return Index(a, module.Constants.Count, "constant") ?? Unused(b);
            case WebLingoStackOpCode.LoadBoolean:
                return (a is 0 or 1 ? null : $"its value is {a}, not 0 or 1.") ?? Unused(b);
            case WebLingoStackOpCode.LoadLocal:
            case WebLingoStackOpCode.StoreLocal:
            case WebLingoStackOpCode.NewCell:
            case WebLingoStackOpCode.BoxLocal:
            case WebLingoStackOpCode.CopyCell:
            case WebLingoStackOpCode.LoadCell:
            case WebLingoStackOpCode.StoreCell:
                return Index(a, function.LocalCount, "local") ?? Unused(b);
            case WebLingoStackOpCode.LoadCapture:
            case WebLingoStackOpCode.StoreCapture:
                return Index(a, function.Captures.Count, "capture") ?? Unused(b);
            case WebLingoStackOpCode.Jump:
            case WebLingoStackOpCode.JumpIfFalse:
            case WebLingoStackOpCode.JumpIfTrue:
                return Index(a, function.Code.Count, "target instruction") ?? Unused(b);
            case WebLingoStackOpCode.JumpIfGiven:
                return Index(a, function.Code.Count, "target instruction") ?? Index(b, function.LocalCount, "local");
            case WebLingoStackOpCode.LoadBuiltin:
                return Index(a, module.Imports.Count, "import") ?? Unused(b);
            case WebLingoStackOpCode.ImportDynamic:
                // A: 1 for values, plus 2 for a component's render (section 13).
                return (a is >= 0 and <= 3 ? null : $"its flags, {a}, are not 0 to 3.") ?? Unused(b);
            case WebLingoStackOpCode.Import:
                // B is 1 when the values it gives the file's parameters are on the stack, which a file that has some is always given.
                return Index(a, module.Units.Count, "file")
                    ?? (b is 0 or 1 ? null : $"its values' flag, {b}, is not 0 or 1.")
                    ?? (b == 0 && module.Units[a].Parameters.Count > 0 ? $"file {a} has parameters, and is given no values." : null);
            case WebLingoStackOpCode.RegisterUnit:
                return (a >= 1 && a < module.Units.Count ? null : $"its file, {a}, is not one the program imports.") ?? Unused(b);
            case WebLingoStackOpCode.MakeModule:
                return Index(a, module.Units.Count, "file")
                    ?? (b == module.Units[a].Exports.Count ? null : $"its cell count, {b}, is not file {a}'s {module.Units[a].Exports.Count} exports.");
            case WebLingoStackOpCode.MakeClass:
            case WebLingoStackOpCode.Extend:
            {
                var operandError = Index(a, module.Classes.Count, "class")
                    ?? (instruction.OpCode == WebLingoStackOpCode.Extend ? Unused(b) : b is 0 or 1 ? null : $"its B is {b}, not 0 or 1.");
                if (operandError is not null) return operandError;
                foreach (var member in WebLingoClassTables.Functions(module.Classes[a]))
                {
                    foreach (var capture in module.Functions[member].Captures)
                    {
                        var error = capture.FromLocal
                            ? Index(capture.Index, function.LocalCount, "captured local")
                            : Index(capture.Index, function.Captures.Count, "capture");
                        if (error is not null) return $"function {member}'s capture: {error}";
                    }
                }
                return null;
            }
            case WebLingoStackOpCode.New:
                return a is < 0 or > WebLingoFunction.MaxRegisters
                    ? $"its argument count, {a}, is not 0 to {WebLingoFunction.MaxRegisters}."
                    : Unused(b);
            case WebLingoStackOpCode.SetMember:
                return Name(module, a) ?? (b is 0 or 1 ? null : $"its B is {b}, not 0 or 1.");
            case WebLingoStackOpCode.GetSuper:
                return Name(module, a) ?? Unused(b);
            case WebLingoStackOpCode.MakeClosure:
            {
                if ((Index(a, module.Functions.Count, "function") ?? Unused(b)) is { } closureError) return closureError;
                if (module.Functions[a] is not { Captures: { } captures }) return $"function {a} is missing.";
                foreach (var capture in captures)
                {
                    var error = capture.FromLocal
                        ? Index(capture.Index, function.LocalCount, "captured local")
                        : Index(capture.Index, function.Captures.Count, "capture");
                    if (error is not null) return $"function {a}'s capture: {error}";
                }
                return null;
            }
            case WebLingoStackOpCode.Call:
                return a is < 0 or > WebLingoFunction.MaxRegisters
                    ? $"its argument count, {a}, is not 0 to {WebLingoFunction.MaxRegisters}."
                    : Unused(b);
            case WebLingoStackOpCode.CallBuiltin:
            {
                if (Index(a, module.Imports.Count, "import") is { } importError) return importError;
                var import = module.Imports[a];
                return b == import.Arity ? null : $"'{import.Name}' takes {import.Arity} arguments, not {b}.";
            }
            case WebLingoStackOpCode.GetMember:
                return Name(module, a) ?? (b is 0 or 1 ? null : $"its B is {b}, not 0 or 1.");
            case WebLingoStackOpCode.CallMethod:
                return Name(module, a) ?? (b is < 0 or > WebLingoFunction.MaxRegisters
                    ? $"its argument count, {b}, is not 0 to {WebLingoFunction.MaxRegisters}."
                    : null);
            case WebLingoStackOpCode.ForEachNext:
            case WebLingoStackOpCode.ForEachNextEntry:
            {
                if (Index(a, function.Code.Count, "target instruction") is { } targetError) return targetError;
                var last = (long)b + 1 + ItemCount(instruction.OpCode);
                return b >= 0 && last < function.LocalCount
                    ? null
                    : $"its locals, {b} to {last}, are not all in the function's {function.LocalCount}.";
            }
            case WebLingoStackOpCode.NewMap:
                return (a is < 0 or > WebLingoFunction.MaxRegisters / 2
                    ? $"its entry count, {a}, is not 0 to {WebLingoFunction.MaxRegisters / 2}."
                    : null) ?? Unused(b);
            case WebLingoStackOpCode.NewArray:
            case WebLingoStackOpCode.NewSet:
                return (a is < 0 or > WebLingoFunction.MaxRegisters ? $"its element count, {a}, is not 0 to {WebLingoFunction.MaxRegisters}." : null)
                    ?? Unused(b);
            case WebLingoStackOpCode.StoreIndex:
                return (a is 0 or 1 ? null : $"its A is {a}, not 0 or 1.") ?? Unused(b);
            case WebLingoStackOpCode.Dispose:
            case WebLingoStackOpCode.Throw:
            case WebLingoStackOpCode.Rethrow:
            case WebLingoStackOpCode.ToText:
            case WebLingoStackOpCode.IsInstance:
            case WebLingoStackOpCode.Await:
            case WebLingoStackOpCode.Hold:
                return Unused(a, b);
            case WebLingoStackOpCode.GetIndex:
                return (a is 0 or 1 ? null : $"its A is {a}, not 0 or 1.") ?? Unused(b);
            case WebLingoStackOpCode.LoadNull:
            case WebLingoStackOpCode.Duplicate:
            case WebLingoStackOpCode.Pop:
            case WebLingoStackOpCode.Negate:
            case WebLingoStackOpCode.Plus:
            case WebLingoStackOpCode.Not:
            case WebLingoStackOpCode.Add:
            case WebLingoStackOpCode.Subtract:
            case WebLingoStackOpCode.Multiply:
            case WebLingoStackOpCode.Divide:
            case WebLingoStackOpCode.Equal:
            case WebLingoStackOpCode.NotEqual:
            case WebLingoStackOpCode.Less:
            case WebLingoStackOpCode.LessOrEqual:
            case WebLingoStackOpCode.Greater:
            case WebLingoStackOpCode.GreaterOrEqual:
            case WebLingoStackOpCode.CheckBoolean:
            case WebLingoStackOpCode.ReturnValue:
            case WebLingoStackOpCode.Return:
                return Unused(a, b);
            default:
                return $"the operation {(int)instruction.OpCode} is unknown.";
        }
    }

    /// <summary>
    /// Follows every path from the first instruction, keeping, at the start of each block (where a path can arrive by
    /// a jump), the operand stack's depth, which every path must agree on, and the set of locals certainly written
    /// (the intersection over the paths), until nothing changes. Its memory and its steps are bounded, and a module
    /// beyond either bound is rejected rather than verified slowly.
    /// </summary>
    private static WebLingoVerificationResult CheckFlow(WebLingoStackModule module, WebLingoStackFunction function)
    {
        var code = function.Code;
        var words = (function.LocalCount + 63) / 64;

        var isBlockStart = new bool[code.Count];
        isBlockStart[0] = true;
        var blockCount = 1L;
        foreach (var instruction in code)
        {
            if (IsJump(instruction.OpCode) && !isBlockStart[instruction.A])
            {
                isBlockStart[instruction.A] = true;
                blockCount++;
            }
        }
        foreach (var handler in function.Handlers)
        {
            if (isBlockStart[handler.Target]) continue;
            isBlockStart[handler.Target] = true;
            blockCount++;
        }
        var handlerAt = WebLingoBytecodeVerifier.HandlerAt(code.Count, function.Handlers, out var coverSteps);
        if (coverSteps > MaxSteps) return Fail("The function's handlers are too many to verify in the steps allowed.");
        if (blockCount * Math.Max(words, 1) > MaxStateWords)
        {
            return Fail($"The function is too large to verify: {blockCount} blocks of {function.LocalCount} locals.");
        }

        var entry = new ulong[words];
        for (var parameter = 0; parameter < function.ParameterCount; parameter++) Set(entry, parameter); // Arguments.
        var atBlockStart = new Dictionary<int, (int Depth, ulong[] Written)> { [0] = (0, entry) };
        var pending = new Stack<int>();
        pending.Push(0);
        var steps = 0L;
        while (pending.Count > 0)
        {
            var index = pending.Pop();
            var (depth, start) = atBlockStart[index];
            var written = (ulong[])start.Clone();
            while (true)
            {
                if (++steps > MaxSteps) return Fail("The function is too complex to verify in the steps allowed.");
                var instruction = code[index];
                if (handlerAt[index] >= 0)
                {
                    // An exception raised here goes to the handler, the stack cut to its depth, with what is written so far.
                    var handler = function.Handlers[handlerAt[index]];
                    if (depth < handler.Depth)
                    {
                        return Fail($"Instruction {index} ({instruction.OpCode}) has a stack of {depth}, below its handler's, " +
                                    $"{handler.Depth}.", index);
                    }
                    var caught = (ulong[])written.Clone();
                    Set(caught, handler.Value);
                    Set(caught, handler.Origin);
                    if (Flow(handler.Target, handler.Depth, caught, atBlockStart, pending) is { } handlerError)
                    {
                        return Fail(handlerError, index);
                    }
                }
                var unwritten = FirstUnwrittenRead(module, instruction, written);
                if (unwritten >= 0)
                {
                    return Fail($"Instruction {index} ({instruction.OpCode}) reads local {unwritten}, " +
                                "not written on every path to it.", index);
                }
                var (pops, pushes) = StackEffect(instruction);
                if (depth < pops)
                {
                    return Fail($"Instruction {index} ({instruction.OpCode}) pops {pops} values from a stack of {depth}.",
                        index);
                }
                depth += pushes - pops;
                if (depth > function.MaxStack)
                {
                    return Fail($"Instruction {index} ({instruction.OpCode}) leaves {depth} values on a stack of at most " +
                                $"{function.MaxStack}.", index);
                }
                if (instruction.OpCode is WebLingoStackOpCode.ForEachNext or WebLingoStackOpCode.ForEachNextEntry)
                {
                    // The position is written on both paths; the items only on the one that goes on into the loop.
                    Set(written, instruction.B + 1);
                    var doneError = Flow(instruction.A, depth, written, atBlockStart, pending);
                    if (doneError is not null) return Fail(doneError, index);
                    for (var item = 0; item < ItemCount(instruction.OpCode); item++) Set(written, instruction.B + 2 + item);
                    index++;
                    if (isBlockStart[index])
                    {
                        if (Flow(index, depth, written, atBlockStart, pending) is { } nextError) return Fail(nextError, index);
                        break;
                    }
                    continue;
                }
                if (Writes(instruction) is int local) Set(written, local);

                if (instruction.OpCode is WebLingoStackOpCode.Return or WebLingoStackOpCode.ReturnValue or WebLingoStackOpCode.Throw
                    or WebLingoStackOpCode.Rethrow)
                {
                    break;
                }
                if (IsJump(instruction.OpCode))
                {
                    var jumpError = Flow(instruction.A, depth, written, atBlockStart, pending);
                    if (jumpError is not null) return Fail(jumpError, index);
                    if (instruction.OpCode == WebLingoStackOpCode.Jump) break; // A conditional one goes on, too.
                }
                index++;
                if (isBlockStart[index])
                {
                    if (Flow(index, depth, written, atBlockStart, pending) is { } flowError) return Fail(flowError, index);
                    break;
                }
            }
        }
        return WebLingoVerificationResult.Valid;
    }

    /// <summary>How many values the instruction pops, then pushes.</summary>
    private static (int Pops, int Pushes) StackEffect(WebLingoStackInstruction instruction) => instruction.OpCode switch
    {
        WebLingoStackOpCode.LoadConstant or WebLingoStackOpCode.LoadNull or WebLingoStackOpCode.LoadBoolean or WebLingoStackOpCode.LoadLocal
            or WebLingoStackOpCode.LoadCell or WebLingoStackOpCode.LoadCapture or WebLingoStackOpCode.MakeClosure
            or WebLingoStackOpCode.LoadBuiltin => (0, 1),
        WebLingoStackOpCode.StoreLocal or WebLingoStackOpCode.Pop or WebLingoStackOpCode.StoreCell or WebLingoStackOpCode.StoreCapture
            or WebLingoStackOpCode.JumpIfFalse or WebLingoStackOpCode.JumpIfTrue or WebLingoStackOpCode.ReturnValue => (1, 0),
        WebLingoStackOpCode.Duplicate => (1, 2),
        WebLingoStackOpCode.Negate or WebLingoStackOpCode.Plus or WebLingoStackOpCode.Not or WebLingoStackOpCode.CheckBoolean => (1, 1),
        WebLingoStackOpCode.Add or WebLingoStackOpCode.Subtract or WebLingoStackOpCode.Multiply or WebLingoStackOpCode.Divide
            or WebLingoStackOpCode.Equal or WebLingoStackOpCode.NotEqual or WebLingoStackOpCode.Less or WebLingoStackOpCode.LessOrEqual
            or WebLingoStackOpCode.Greater or WebLingoStackOpCode.GreaterOrEqual => (2, 1),
        WebLingoStackOpCode.Call => (instruction.A + 1, 1),
        WebLingoStackOpCode.CallBuiltin => (instruction.B, 1),
        WebLingoStackOpCode.GetMember => (1, 1),
        WebLingoStackOpCode.MakeClass => (instruction.B, 1),
        WebLingoStackOpCode.GetSuper => (2, 1),
        WebLingoStackOpCode.Dispose => (1, 0),
        WebLingoStackOpCode.Extend => (1, 0),
        WebLingoStackOpCode.Hold => (2, 1),
        WebLingoStackOpCode.Import => (instruction.B, 1),
        WebLingoStackOpCode.ImportDynamic => ((instruction.A & 1) + 1, 1),
        WebLingoStackOpCode.RegisterUnit => (1, 0),
        WebLingoStackOpCode.MakeModule => (instruction.B, 1),
        WebLingoStackOpCode.Throw => (1, 0),
        WebLingoStackOpCode.Rethrow => (2, 0),
        WebLingoStackOpCode.ToText => (1, 1),
        WebLingoStackOpCode.IsInstance => (2, 1),
        WebLingoStackOpCode.JumpIfGiven => (0, 0),
        WebLingoStackOpCode.Await => (1, 1),
        WebLingoStackOpCode.New => (instruction.A + 1, 1),
        WebLingoStackOpCode.SetMember => (2, instruction.B),
        WebLingoStackOpCode.NewArray or WebLingoStackOpCode.NewSet => (instruction.A, 1),
        WebLingoStackOpCode.NewMap => (2 * instruction.A, 1),
        WebLingoStackOpCode.CallMethod => (instruction.B + 1, 1),
        WebLingoStackOpCode.GetIndex => (2, 1),
        WebLingoStackOpCode.StoreIndex => (3, instruction.A),
        _ => (0, 0) // Jump, NewCell, BoxLocal, CopyCell and Return.
    };

    private static string? Flow(int target, int depth, ulong[] written,
        Dictionary<int, (int Depth, ulong[] Written)> atBlockStart, Stack<int> pending)
    {
        if (!atBlockStart.TryGetValue(target, out var existing))
        {
            atBlockStart[target] = (depth, (ulong[])written.Clone());
            pending.Push(target);
            return null;
        }
        if (existing.Depth != depth)
        {
            return $"Paths reach instruction {target} with stacks of {existing.Depth} and {depth} values.";
        }
        var changed = false;
        for (var i = 0; i < existing.Written.Length; i++)
        {
            var narrowed = existing.Written[i] & written[i];
            if (narrowed != existing.Written[i])
            {
                existing.Written[i] = narrowed;
                changed = true;
            }
        }
        if (changed) pending.Push(target);
        return null;
    }

    /// <summary>The first local the instruction reads that is not certainly written, or -1.</summary>
    private static int FirstUnwrittenRead(WebLingoStackModule module, WebLingoStackInstruction instruction, ulong[] written)
    {
        switch (instruction.OpCode)
        {
            case WebLingoStackOpCode.ForEachNext:
            case WebLingoStackOpCode.ForEachNextEntry:
                if (!IsSet(written, instruction.B)) return instruction.B;
                return IsSet(written, instruction.B + 1) ? -1 : instruction.B + 1;
            case WebLingoStackOpCode.LoadLocal:
            case WebLingoStackOpCode.BoxLocal:
            case WebLingoStackOpCode.CopyCell:
            case WebLingoStackOpCode.LoadCell:
            case WebLingoStackOpCode.StoreCell:
                return IsSet(written, instruction.A) ? -1 : instruction.A;
            case WebLingoStackOpCode.JumpIfGiven:
                return IsSet(written, instruction.B) ? -1 : instruction.B;
            case WebLingoStackOpCode.MakeClass:
            case WebLingoStackOpCode.Extend:
                foreach (var member in WebLingoClassTables.Functions(module.Classes[instruction.A]))
                {
                    foreach (var capture in module.Functions[member].Captures)
                    {
                        if (capture.FromLocal && !IsSet(written, capture.Index)) return capture.Index;
                    }
                }
                return -1;
            case WebLingoStackOpCode.MakeClosure:
                foreach (var capture in module.Functions[instruction.A].Captures)
                {
                    if (capture.FromLocal && !IsSet(written, capture.Index)) return capture.Index;
                }
                return -1;
            default:
                return -1;
        }
    }

    /// <summary>The local the instruction writes, if any.</summary>
    private static int? Writes(WebLingoStackInstruction instruction) => instruction.OpCode switch
    {
        WebLingoStackOpCode.StoreLocal or WebLingoStackOpCode.NewCell or WebLingoStackOpCode.BoxLocal
            or WebLingoStackOpCode.CopyCell => instruction.A,
        _ => null
    };

    private static bool IsJump(WebLingoStackOpCode opCode) =>
        opCode is WebLingoStackOpCode.Jump or WebLingoStackOpCode.JumpIfFalse or WebLingoStackOpCode.JumpIfTrue or WebLingoStackOpCode.ForEachNext
            or WebLingoStackOpCode.ForEachNextEntry or WebLingoStackOpCode.JumpIfGiven;

    private static int ItemCount(WebLingoStackOpCode opCode) => opCode == WebLingoStackOpCode.ForEachNextEntry ? 2 : 1;

    private static bool EndsPath(WebLingoStackOpCode opCode) =>
        opCode is WebLingoStackOpCode.Return or WebLingoStackOpCode.ReturnValue or WebLingoStackOpCode.Jump or WebLingoStackOpCode.Throw
            or WebLingoStackOpCode.Rethrow;

    private static bool IsSet(ulong[] state, int local) => (state[local >> 6] & (1UL << (local & 63))) != 0;

    private static void Set(ulong[] state, int local) => state[local >> 6] |= 1UL << (local & 63);

    /// <summary>A member's name, which must be a string constant.</summary>
    private static string? Name(WebLingoStackModule module, int constant) =>
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
