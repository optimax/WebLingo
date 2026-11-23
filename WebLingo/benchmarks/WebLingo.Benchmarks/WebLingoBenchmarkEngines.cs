using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.VM;
using WebLingo.Templates;
namespace WebLingo.Benchmarks;

/// <summary>A VM the benchmark measures, with the compiler that generates its bytecode.</summary>
public interface IWebLingoBenchmarkEngine
{
    string Name { get; }

    /// <summary>Compiles the source for this VM; an error if it does not compile.</summary>
    IWebLingoBenchmarkProgram Compile(string source, string name, WebLingoCompilerOptions options);
}

/// <summary>A workload compiled for one VM: its code's size, and a way to run it.</summary>
public interface IWebLingoBenchmarkProgram
{
    /// <summary>How many instructions its functions hold, the entry point's included.</summary>
    int CodeInstructions { get; }

    /// <summary>
    /// How many bytes its instructions would take in a variable-length encoding: a byte for the operation, and each
    /// operand it uses as an unsigned LEB128 number. WLBC's encoding is not decided (section 5.3); this only compares
    /// the two instruction sets' sizes. Constants and other tables, the same for every VM, are not counted.
    /// </summary>
    long CodeBytes { get; }

    WebLingoExecutionResult Run(WebLingoExecutionContext context);
}

/// <summary>The register VM, <see cref="WebLingoVirtualMachine"/>, with <see cref="WebLingoCompiler"/>.</summary>
public sealed class WebLingoRegisterEngine : IWebLingoBenchmarkEngine
{
    public string Name => "register";

    public IWebLingoBenchmarkProgram Compile(string source, string name, WebLingoCompilerOptions options)
    {
        // A page (section 13) through the template compiler; a script through the WebLingo compiler.
        var compiled = name.EndsWith(".wasp", StringComparison.Ordinal)
            ? new WebLingoTemplateCompiler(options).Compile(new WebLingoSourceText(source, name))
            : new WebLingoCompiler(options).Compile(source, name);
        if (compiled.Module is not { } module)
        {
            throw new InvalidOperationException($"{name} does not compile: {string.Join("; ", compiled.Diagnostics)}");
        }
        return new CompiledProgram(module);
    }

    private sealed class CompiledProgram(WebLingoCompiledModule module) : IWebLingoBenchmarkProgram
    {
        private readonly WebLingoVirtualMachine _vm = new();

        public int CodeInstructions { get; } = Functions(module).Sum(function => function.Code.Count);

        public long CodeBytes { get; } = Functions(module).Sum(function => function.Code.Sum(Bytes));

        public WebLingoExecutionResult Run(WebLingoExecutionContext context) => _vm.Execute(module, context);

        private static IEnumerable<WebLingoFunction> Functions(WebLingoCompiledModule module) => module.Functions.Prepend(module.EntryPoint);

        private static long Bytes(WebLingoInstruction instruction)
        {
            int[] operands = [instruction.A, instruction.B, instruction.C, instruction.D];
            // GetIndex uses its D only for ?[.
            var count = instruction switch
            {
                { OpCode: WebLingoOpCode.GetIndex or WebLingoOpCode.GetMember or WebLingoOpCode.MakeClass, D: not 0 } => 4,
                _ => OperandCount(instruction.OpCode)
            };
            return 1 + operands.Take(count).Sum(WebLingoEncoding.Leb128Size);
        }

        /// <summary>How many of A, B, C and D the operation uses (<see cref="WebLingoOpCode"/>); the ones it uses come first.</summary>
        private static int OperandCount(WebLingoOpCode opCode) => opCode switch
        {
            WebLingoOpCode.Return => 0,
            WebLingoOpCode.LoadNull or WebLingoOpCode.CheckBoolean or WebLingoOpCode.NewCell or WebLingoOpCode.BoxRegister or WebLingoOpCode.CopyCell
                or WebLingoOpCode.ReturnValue or WebLingoOpCode.Jump => 1,
            WebLingoOpCode.Add or WebLingoOpCode.Subtract or WebLingoOpCode.Multiply or WebLingoOpCode.Divide or WebLingoOpCode.Equal
                or WebLingoOpCode.NotEqual or WebLingoOpCode.Less or WebLingoOpCode.LessOrEqual or WebLingoOpCode.Greater
                or WebLingoOpCode.GreaterOrEqual or WebLingoOpCode.AddConstant or WebLingoOpCode.SubtractConstant or WebLingoOpCode.MultiplyConstant or WebLingoOpCode.RemainderConstant or WebLingoOpCode.EqualConstant or WebLingoOpCode.NotEqualConstant or WebLingoOpCode.LessConstant or WebLingoOpCode.LessOrEqualConstant or WebLingoOpCode.GreaterConstant or WebLingoOpCode.GreaterOrEqualConstant => 3,
            WebLingoOpCode.CallBuiltin or WebLingoOpCode.Call or WebLingoOpCode.CallMethod or WebLingoOpCode.New or WebLingoOpCode.GetSuper => 4,
            WebLingoOpCode.SetMember => 3,
            WebLingoOpCode.GetMember or WebLingoOpCode.GetIndex or WebLingoOpCode.SetIndex or WebLingoOpCode.NewArray or WebLingoOpCode.NewMap or WebLingoOpCode.NewSet
                or WebLingoOpCode.ForEachNext => 3,
            _ => 2
        };
    }
}

/// <summary>Sizes of operands in the variable-length encoding the benchmark assumes.</summary>
public static class WebLingoEncoding
{
    /// <summary>The bytes an unsigned LEB128 number takes: seven bits in each.</summary>
    public static int Leb128Size(int value)
    {
        var size = 1;
        for (var rest = (uint)value >> 7; rest != 0; rest >>= 7) size++;
        return size;
    }
}
