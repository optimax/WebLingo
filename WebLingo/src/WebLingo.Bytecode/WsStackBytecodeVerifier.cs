namespace WebLingo.Bytecode;

/// <summary>
/// The stack bytecode's verifier (specification, section 5.5): a module must pass it before the stack VM runs it. It
/// checks what <see cref="WsBytecodeVerifier"/> checks of a register module (the tables, every instruction's operation
/// and operands, that unused operands are 0, that no path runs off the end of the code, and that no local is read
/// before it is written, on every path), and, following every path, that the operand stack has the same depth
/// wherever paths meet, never pops a value it does not have, and never holds more than the function's
/// <see cref="WsStackFunction.MaxStack"/>. It never throws on malformed input.
/// </summary>
public sealed class WsStackBytecodeVerifier
{
    /// <summary>How many 64-bit words of state the dataflow check may keep (128 MB), as the register verifier.</summary>
    public const long MaxStateWords = WsBytecodeVerifier.MaxStateWords;
    /// <summary>How many instruction steps the dataflow check may take, as the register verifier's.</summary>
    public const long MaxSteps = WsBytecodeVerifier.MaxSteps;

    public WsVerificationResult Verify(WsStackModule module)
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
        if (module.EntryPoint is { } main && (main.ParameterCount != 0 || main.Captures is not { Count: 0 }))
        {
            return Fail("The entry point takes no parameters and captures nothing.");
        }
        var result = VerifyFunction(module, module.EntryPoint);
        for (var i = 0; i < module.Functions.Count && result.IsValid; i++)
        {
            result = VerifyFunction(module, module.Functions[i]);
            if (!result.IsValid) result = result with { Error = $"Function {i}: {result.Error}" };
        }
        return result;
    }

    private static WsVerificationResult VerifyFunction(WsStackModule module, WsStackFunction? function)
    {
        if (function is null) return Fail("The module has no entry point.");
        if (function.Code is null || function.Code.Count == 0) return Fail("The function has no code.");
        if (function.LocalCount < 0 || function.MaxStack < 0
            || (long)function.LocalCount + function.MaxStack > WsFunction.MaxRegisters)
        {
            return Fail($"The function has {function.LocalCount} locals and a stack of {function.MaxStack}; " +
                        $"it may have 0 to {WsFunction.MaxRegisters} slots in all.");
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
        return CheckFlow(module, function);
    }

    private static string? CheckOperands(WsStackModule module, WsStackFunction function, WsStackInstruction instruction)
    {
        var (a, b) = (instruction.A, instruction.B);
        switch (instruction.OpCode)
        {
            case WsStackOpCode.LoadConstant:
                return Index(a, module.Constants.Count, "constant") ?? Unused(b);
            case WsStackOpCode.LoadBoolean:
                return (a is 0 or 1 ? null : $"its value is {a}, not 0 or 1.") ?? Unused(b);
            case WsStackOpCode.LoadLocal:
            case WsStackOpCode.StoreLocal:
            case WsStackOpCode.NewCell:
            case WsStackOpCode.BoxLocal:
            case WsStackOpCode.CopyCell:
            case WsStackOpCode.LoadCell:
            case WsStackOpCode.StoreCell:
                return Index(a, function.LocalCount, "local") ?? Unused(b);
            case WsStackOpCode.LoadCapture:
            case WsStackOpCode.StoreCapture:
                return Index(a, function.Captures.Count, "capture") ?? Unused(b);
            case WsStackOpCode.Jump:
            case WsStackOpCode.JumpIfFalse:
            case WsStackOpCode.JumpIfTrue:
                return Index(a, function.Code.Count, "target instruction") ?? Unused(b);
            case WsStackOpCode.LoadBuiltin:
                return Index(a, module.Imports.Count, "import") ?? Unused(b);
            case WsStackOpCode.MakeClosure:
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
            case WsStackOpCode.Call:
                return a is < 0 or > WsFunction.MaxRegisters
                    ? $"its argument count, {a}, is not 0 to {WsFunction.MaxRegisters}."
                    : Unused(b);
            case WsStackOpCode.CallBuiltin:
            {
                if (Index(a, module.Imports.Count, "import") is { } importError) return importError;
                var import = module.Imports[a];
                return b == import.Arity ? null : $"'{import.Name}' takes {import.Arity} arguments, not {b}.";
            }
            case WsStackOpCode.GetMember:
                return Name(module, a) ?? Unused(b);
            case WsStackOpCode.CallMethod:
                return Name(module, a) ?? (b is < 0 or > WsFunction.MaxRegisters
                    ? $"its argument count, {b}, is not 0 to {WsFunction.MaxRegisters}."
                    : null);
            case WsStackOpCode.NewArray:
                return (a is < 0 or > WsFunction.MaxRegisters ? $"its element count, {a}, is not 0 to {WsFunction.MaxRegisters}." : null)
                    ?? Unused(b);
            case WsStackOpCode.StoreIndex:
                return (a is 0 or 1 ? null : $"its A is {a}, not 0 or 1.") ?? Unused(b);
            case WsStackOpCode.GetIndex:
            case WsStackOpCode.LoadNull:
            case WsStackOpCode.Duplicate:
            case WsStackOpCode.Pop:
            case WsStackOpCode.Negate:
            case WsStackOpCode.Plus:
            case WsStackOpCode.Not:
            case WsStackOpCode.Add:
            case WsStackOpCode.Subtract:
            case WsStackOpCode.Multiply:
            case WsStackOpCode.Divide:
            case WsStackOpCode.Equal:
            case WsStackOpCode.NotEqual:
            case WsStackOpCode.Less:
            case WsStackOpCode.LessOrEqual:
            case WsStackOpCode.Greater:
            case WsStackOpCode.GreaterOrEqual:
            case WsStackOpCode.CheckBoolean:
            case WsStackOpCode.ReturnValue:
            case WsStackOpCode.Return:
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
    private static WsVerificationResult CheckFlow(WsStackModule module, WsStackFunction function)
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
                if (Writes(instruction) is int local) Set(written, local);

                if (instruction.OpCode is WsStackOpCode.Return or WsStackOpCode.ReturnValue) break;
                if (IsJump(instruction.OpCode))
                {
                    var jumpError = Flow(instruction.A, depth, written, atBlockStart, pending);
                    if (jumpError is not null) return Fail(jumpError, index);
                    if (instruction.OpCode == WsStackOpCode.Jump) break; // A conditional one goes on, too.
                }
                index++;
                if (isBlockStart[index])
                {
                    if (Flow(index, depth, written, atBlockStart, pending) is { } flowError) return Fail(flowError, index);
                    break;
                }
            }
        }
        return WsVerificationResult.Valid;
    }

    /// <summary>How many values the instruction pops, then pushes.</summary>
    private static (int Pops, int Pushes) StackEffect(WsStackInstruction instruction) => instruction.OpCode switch
    {
        WsStackOpCode.LoadConstant or WsStackOpCode.LoadNull or WsStackOpCode.LoadBoolean or WsStackOpCode.LoadLocal
            or WsStackOpCode.LoadCell or WsStackOpCode.LoadCapture or WsStackOpCode.MakeClosure
            or WsStackOpCode.LoadBuiltin => (0, 1),
        WsStackOpCode.StoreLocal or WsStackOpCode.Pop or WsStackOpCode.StoreCell or WsStackOpCode.StoreCapture
            or WsStackOpCode.JumpIfFalse or WsStackOpCode.JumpIfTrue or WsStackOpCode.ReturnValue => (1, 0),
        WsStackOpCode.Duplicate => (1, 2),
        WsStackOpCode.Negate or WsStackOpCode.Plus or WsStackOpCode.Not or WsStackOpCode.CheckBoolean => (1, 1),
        WsStackOpCode.Add or WsStackOpCode.Subtract or WsStackOpCode.Multiply or WsStackOpCode.Divide
            or WsStackOpCode.Equal or WsStackOpCode.NotEqual or WsStackOpCode.Less or WsStackOpCode.LessOrEqual
            or WsStackOpCode.Greater or WsStackOpCode.GreaterOrEqual => (2, 1),
        WsStackOpCode.Call => (instruction.A + 1, 1),
        WsStackOpCode.CallBuiltin => (instruction.B, 1),
        WsStackOpCode.GetMember => (1, 1),
        WsStackOpCode.NewArray => (instruction.A, 1),
        WsStackOpCode.CallMethod => (instruction.B + 1, 1),
        WsStackOpCode.GetIndex => (2, 1),
        WsStackOpCode.StoreIndex => (3, instruction.A),
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
    private static int FirstUnwrittenRead(WsStackModule module, WsStackInstruction instruction, ulong[] written)
    {
        switch (instruction.OpCode)
        {
            case WsStackOpCode.LoadLocal:
            case WsStackOpCode.BoxLocal:
            case WsStackOpCode.CopyCell:
            case WsStackOpCode.LoadCell:
            case WsStackOpCode.StoreCell:
                return IsSet(written, instruction.A) ? -1 : instruction.A;
            case WsStackOpCode.MakeClosure:
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
    private static int? Writes(WsStackInstruction instruction) => instruction.OpCode switch
    {
        WsStackOpCode.StoreLocal or WsStackOpCode.NewCell or WsStackOpCode.BoxLocal
            or WsStackOpCode.CopyCell => instruction.A,
        _ => null
    };

    private static bool IsJump(WsStackOpCode opCode) =>
        opCode is WsStackOpCode.Jump or WsStackOpCode.JumpIfFalse or WsStackOpCode.JumpIfTrue;

    private static bool EndsPath(WsStackOpCode opCode) =>
        opCode is WsStackOpCode.Return or WsStackOpCode.ReturnValue or WsStackOpCode.Jump;

    private static bool IsSet(ulong[] state, int local) => (state[local >> 6] & (1UL << (local & 63))) != 0;

    private static void Set(ulong[] state, int local) => state[local >> 6] |= 1UL << (local & 63);

    /// <summary>A member's name, which must be a string constant.</summary>
    private static string? Name(WsStackModule module, int constant) =>
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
