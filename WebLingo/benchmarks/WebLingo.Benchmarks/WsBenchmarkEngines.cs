using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Runtime;
using WebLingo.VM;
namespace WebLingo.Benchmarks;

/// <summary>A VM the benchmark measures, with the compiler that generates its bytecode.</summary>
public interface IWsBenchmarkEngine
{
    string Name { get; }

    /// <summary>Compiles the source for this VM; an error if it does not compile.</summary>
    IWsBenchmarkProgram Compile(string source, string name, WsCompilerOptions options);
}

/// <summary>A workload compiled for one VM: its code's size, and a way to run it.</summary>
public interface IWsBenchmarkProgram
{
    /// <summary>How many instructions its functions hold, the entry point's included.</summary>
    int CodeInstructions { get; }

    /// <summary>
    /// How many bytes its instructions would take in a variable-length encoding: a byte for the operation, and each
    /// operand it uses as an unsigned LEB128 number. WBC's encoding is not decided (section 5.3); this only compares
    /// the two instruction sets' sizes. Constants and other tables, the same for every VM, are not counted.
    /// </summary>
    long CodeBytes { get; }

    WsExecutionResult Run(WsExecutionContext context);
}

/// <summary>The register VM, <see cref="WsVirtualMachine"/>, with <see cref="WsCompiler"/>.</summary>
public sealed class WsRegisterEngine : IWsBenchmarkEngine
{
    public string Name => "register";

    public IWsBenchmarkProgram Compile(string source, string name, WsCompilerOptions options)
    {
        var compiled = new WsCompiler(options).Compile(source, name);
        if (compiled.Module is not { } module)
        {
            throw new InvalidOperationException($"{name} does not compile: {string.Join("; ", compiled.Diagnostics)}");
        }
        return new CompiledProgram(module);
    }

    private sealed class CompiledProgram(WsCompiledModule module) : IWsBenchmarkProgram
    {
        private readonly WsVirtualMachine _vm = new();

        public int CodeInstructions { get; } = Functions(module).Sum(function => function.Code.Count);

        public long CodeBytes { get; } = Functions(module).Sum(function => function.Code.Sum(Bytes));

        public WsExecutionResult Run(WsExecutionContext context) => _vm.Execute(module, context);

        private static IEnumerable<WsFunction> Functions(WsCompiledModule module) => module.Functions.Prepend(module.EntryPoint);

        private static long Bytes(WsInstruction instruction)
        {
            int[] operands = [instruction.A, instruction.B, instruction.C, instruction.D];
            return 1 + operands.Take(OperandCount(instruction.OpCode)).Sum(WsEncoding.Leb128Size);
        }

        /// <summary>How many of A, B, C and D the operation uses (<see cref="WsOpCode"/>); the ones it uses come first.</summary>
        private static int OperandCount(WsOpCode opCode) => opCode switch
        {
            WsOpCode.Return => 0,
            WsOpCode.LoadNull or WsOpCode.CheckBoolean or WsOpCode.NewCell or WsOpCode.BoxRegister or WsOpCode.CopyCell
                or WsOpCode.ReturnValue or WsOpCode.Jump => 1,
            WsOpCode.Add or WsOpCode.Subtract or WsOpCode.Multiply or WsOpCode.Divide or WsOpCode.Equal
                or WsOpCode.NotEqual or WsOpCode.Less or WsOpCode.LessOrEqual or WsOpCode.Greater
                or WsOpCode.GreaterOrEqual => 3,
            WsOpCode.CallBuiltin or WsOpCode.Call => 4,
            _ => 2
        };
    }
}

/// <summary>Sizes of operands in the variable-length encoding the benchmark assumes.</summary>
public static class WsEncoding
{
    /// <summary>The bytes an unsigned LEB128 number takes: seven bits in each.</summary>
    public static int Leb128Size(int value)
    {
        var size = 1;
        for (var rest = (uint)value >> 7; rest != 0; rest >>= 7) size++;
        return size;
    }
}
