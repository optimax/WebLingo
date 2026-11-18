using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using WebLingo.VM.Stack;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// WLBC on every conformance program (specification, section 5.3, "As built"; section 9, "M0.13 parts", part 1): each
/// program, compiled for each VM, written as a file and read back, is written again as the same bytes, and gives what the
/// compiled module gives, its output and its runtime error, with the error's line and column found from the file alone;
/// and a file changed at random either is refused or runs without crashing either VM.
/// </summary>
public sealed class BytecodeFileTests
{
    [Theory]
    [MemberData(nameof(StackVmTests.ConformancePrograms), MemberType = typeof(StackVmTests))]
    public void AProgramReadBackFromItsFileRunsAsCompiled(string source)
    {
        var text = new WebLingoSourceText(source, "program.wl");
        WebLingoSourceText? TextOf(string? name) => name is null ? text : null;

        if (new WebLingoCompiler(Options).Compile(source, "program.wl").Module is { } module)
        {
            var file = WebLingoBytecodeFile.Write(module, TextOf);
            Assert.True(WebLingoBytecodeFile.TryReadRegister(file, out var read, out var positions, out var error), error);
            Assert.Equal(file, WebLingoBytecodeFile.Write(read, TextOf));
            Assert.Equal(WebLingoDisassembler.Format(module), WebLingoDisassembler.Format(read));
            Assert.Equal(Run(module, text, null), Run(read, text, positions));
        }
        if (new WebLingoStackCompiler(Options).Compile(source, "program.wl").Module is { } stack)
        {
            var file = WebLingoBytecodeFile.Write(stack, TextOf);
            Assert.True(WebLingoBytecodeFile.TryReadStack(file, out var read, out var positions, out var error), error);
            Assert.Equal(file, WebLingoBytecodeFile.Write(read, TextOf));
            var disassembly = WebLingoStackDisassembler.Format(stack);
            Assert.Equal(disassembly, WebLingoStackDisassembler.Format(read));
            // Every operation is shown by its name.
            foreach (var function in read.Functions.Prepend(read.EntryPoint))
            {
                foreach (var instruction in function.Code)
                {
                    Assert.StartsWith(instruction.OpCode.ToString(), WebLingoStackDisassembler.Format(read, function, instruction));
                }
            }
            Assert.Equal(Run(stack, text, null), Run(read, text, positions));
        }
    }

    [Fact]
    public void AChangedFileIsRefusedOrRunsWithoutCrashing()
    {
        const string source = """
            import { write, writeLn } from "std:io";
            class Shape { constructor(n) { this.n = n; } area() { return this.n * 2; } }
            class Square extends Shape { area() { return super.area() + 1; } }
            function make(k) { var total = 0; return function(x) { total = total + x * k; return total; }; }
            var add = make(3);
            var items = [new Shape(1), new Square(2)];
            foreach (var s in items) { writeLn(add(s.area())); }
            try { throw "oops"; } catch (e) { writeLn(e); } finally { writeLn("done"); }
            var m = { "a": 1, "b": 2.5 };
            foreach (var (key, value) in m) { write(key); writeLn(value); }
            """;
        var files = new[]
        {
            WebLingoBytecodeFile.Write(new WebLingoCompiler(Options).Compile(source, "p.wl").Module!),
            WebLingoBytecodeFile.Write(new WebLingoStackCompiler(Options).Compile(source, "p.wl").Module!)
        };
        var random = new Random(13_1);
        var ran = 0;
        for (var round = 0; round < 4_000; round++)
        {
            var file = (byte[])files[round % 2].Clone();
            for (var change = random.Next(1, 3); change > 0; change--)
            {
                var at = random.Next(32 + 7 * 32, file.Length);
                file[at] = random.Next(2) == 0 ? (byte)random.Next(256) : (byte)(file[at] ^ (1 << random.Next(8)));
            }
            for (var section = 1; section <= 7; section++) Recrc(file, section);
            var output = new WebLingoCollectedOutput();
            WebLingoExecutionResult result;
            if (round % 2 == 0)
            {
                if (!WebLingoBytecodeFile.TryReadRegister(file, out var module, out _, out _)) continue;
                result = new WebLingoVirtualMachine().Execute(module, Context(output, 20_000));
            }
            else
            {
                if (!WebLingoBytecodeFile.TryReadStack(file, out var module, out _, out _)) continue;
                result = new WebLingoStackVirtualMachine().Execute(module, Context(output, 20_000));
            }
            Assert.True(result.Succeeded || result.Error is not null);
            ran++;
        }
        Assert.InRange(ran, 1, 4_000);
    }

    private static readonly WebLingoCompilerOptions Options =
        new([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1), new WebLingoBuiltinDeclaration("delay", 1)]);

    /// <summary>What a module gives: its output, and its runtime error with its line and column, from the file's positions if it has them.</summary>
    private static string Run(WebLingoCompiledModule module, WebLingoSourceText text, IReadOnlyList<WebLingoSourcePositions>? positions)
    {
        var output = new WebLingoCollectedOutput();
        return Describe(output, new WebLingoVirtualMachine().Execute(module, Context(output, long.MaxValue)), text, positions);
    }

    private static string Run(WebLingoStackModule module, WebLingoSourceText text, IReadOnlyList<WebLingoSourcePositions>? positions)
    {
        var output = new WebLingoCollectedOutput();
        return Describe(output, new WebLingoStackVirtualMachine().Execute(module, Context(output, long.MaxValue)), text, positions);
    }

    private static string Describe(WebLingoCollectedOutput output, WebLingoExecutionResult result, WebLingoSourceText text,
        IReadOnlyList<WebLingoSourcePositions>? positions)
    {
        if (result.Error is not { } error) return $"output \"{output.Text}\"";
        var location = error.Source is null
            ? positions is null ? text.GetLocation(error.Span.Start) : positions[0].GetLocation(error.Span.Start)
            : default;
        return $"output \"{output.Text}\" error {error.Code} at {error.Span.Start}+{error.Span.Length} ({location.Line},{location.Column}) "
               + $"in {error.Source}: {error.Message}";
    }

    private static WebLingoExecutionContext Context(IWebLingoTextOutput output, long budget) =>
        new(new WebLingoWorkerId(0))
        {
            Services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output)),
            InstructionBudget = budget,
        };

    private static ulong Fixed(byte[] file, int at, int size)
    {
        ulong value = 0;
        for (var i = 0; i < size; i++) value |= (ulong)file[at + i] << (8 * i);
        return value;
    }

    private static void Recrc(byte[] file, int section)
    {
        var entry = 32 + 32 * (section - 1);
        var (offset, length) = (Fixed(file, entry + 8, 8), Fixed(file, entry + 16, 8));
        var crc = WebLingoCrc32.Compute(file, (int)offset, (int)length);
        for (var i = 0; i < 4; i++) file[entry + 24 + i] = (byte)(crc >> (8 * i));
    }
}
