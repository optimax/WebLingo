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
            if (module.Constants[i].Kind is not (WsConstantKind.Int64 or WsConstantKind.Binary64))
            {
                return Fail($"Constant {i} has the unknown kind {(int)module.Constants[i].Kind}.");
            }
        }
        for (var i = 0; i < module.Imports.Count; i++)
        {
            var import = module.Imports[i];
            if (import is null || string.IsNullOrEmpty(import.Name)) return Fail($"Import {i} has no name.");
            if (import.Arity < 0) return Fail($"Import {i} ('{import.Name}') has a negative arity.");
        }
        return VerifyFunction(module, module.EntryPoint);
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
        return CheckRegistersAreWrittenBeforeRead(function);
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
            case WsOpCode.Return:
                return Unused(a, b, c, d);
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
    /// intersection over the paths that reach it) at the start of each block, where a path can arrive by a jump,
    /// until nothing changes. A set only shrinks, so this ends; its memory and its steps are bounded, and a module
    /// beyond either bound is rejected rather than verified slowly.
    /// </summary>
    private static WsVerificationResult CheckRegistersAreWrittenBeforeRead(WsFunction function)
    {
        var code = function.Code;
        const int locals = 0; // Every register, locals included, must be written before it is read.
        var words = (function.RegisterCount - locals + 63) / 64;

        // Blocks start at the first instruction and at every jump target.
        var isBlockStart = new bool[code.Count];
        isBlockStart[0] = true;
        var blockCount = 1L;
        foreach (var instruction in code)
        {
            if (instruction.OpCode == WsOpCode.Jump && !isBlockStart[instruction.A])
            {
                isBlockStart[instruction.A] = true;
                blockCount++;
            }
        }
        if (blockCount * Math.Max(words, 1) > MaxStateWords)
        {
            return Fail($"The function is too large to verify: {blockCount} blocks of {function.RegisterCount} registers.");
        }

        var atBlockStart = new Dictionary<int, ulong[]> { [0] = new ulong[words] };
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
                var unwritten = FirstUnwrittenRead(instruction, state, locals);
                if (unwritten >= 0)
                {
                    return Fail(
                        $"Instruction {index} ({instruction.OpCode}) reads register {unwritten}, not written on every path to it.",
                        index);
                }
                if (Writes(instruction) is int target && target >= locals) Set(state, target - locals);

                if (instruction.OpCode == WsOpCode.Return) break;
                if (instruction.OpCode == WsOpCode.Jump)
                {
                    Flow(instruction.A, state, atBlockStart, pending);
                    break;
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
    private static int FirstUnwrittenRead(WsInstruction instruction, ulong[] state, int locals)
    {
        switch (instruction.OpCode)
        {
            case WsOpCode.Move:
            case WsOpCode.Negate:
            case WsOpCode.Plus:
                return Unwritten(instruction.B, state, locals);
            case WsOpCode.Add:
            case WsOpCode.Subtract:
            case WsOpCode.Multiply:
            case WsOpCode.Divide:
                var left = Unwritten(instruction.B, state, locals);
                return left >= 0 ? left : Unwritten(instruction.C, state, locals);
            case WsOpCode.CallBuiltin:
                for (var i = 0; i < instruction.D; i++)
                {
                    var argument = Unwritten(instruction.C + i, state, locals);
                    if (argument >= 0) return argument;
                }
                return -1;
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
        WsOpCode.LoadConstant or WsOpCode.Move or WsOpCode.Negate or WsOpCode.Plus or WsOpCode.Add or WsOpCode.Subtract
            or WsOpCode.Multiply or WsOpCode.Divide or WsOpCode.CallBuiltin => instruction.A,
        _ => null
    };

    private static bool EndsPath(WsOpCode opCode) => opCode is WsOpCode.Return or WsOpCode.Jump;

    private static string? Register(WsFunction function, int register, string role) =>
        register >= 0 && register < function.RegisterCount
            ? null
            : $"its {role} register {register} is not one of the function's {function.RegisterCount}.";

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
