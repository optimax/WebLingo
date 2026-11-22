using System.Text;
using WebLingo.Bytecode;
using WebLingo.Core;
using Xunit;
namespace WebLingo.Bytecode.Tests;

/// <summary>
/// WLBC (specification, section 5.3, "As built"; section 15.2, "WLBC's layout"): its header and directory, its numbers and
/// strings, what reads back, and every way a file is refused, each with its reason.
/// </summary>
public sealed class BytecodeFileTests
{
    [Fact]
    public void TheCrc32IsIeee8023s() => Assert.Equal(0xCBF43926u, WebLingoCrc32.Compute("123456789"u8.ToArray(), 0, 9));

    [Fact]
    public void TheHeaderAndTheDirectoryAreLaidOutAsSpecified()
    {
        var file = WebLingoBytecodeFile.Write(WebLingoCompiledModule.Empty("main"));
        Assert.Equal("WLBC", Encoding.ASCII.GetString(file, 0, 4));
        Assert.Equal((0ul, 1ul, 1ul, 0ul), (Fixed(file, 4, 2), Fixed(file, 6, 2), Fixed(file, 8, 2), Fixed(file, 10, 2)));
        Assert.Equal((7ul, 32ul, (ulong)file.Length), (Fixed(file, 12, 4), Fixed(file, 16, 8), Fixed(file, 24, 8)));
        var offset = 32ul + 7 * 32;
        for (var i = 0; i < 7; i++)
        {
            var entry = 32 + 32 * i;
            Assert.Equal((ulong)(i + 1), Fixed(file, entry, 4));
            Assert.Equal((0ul, offset), (Fixed(file, entry + 4, 4), Fixed(file, entry + 8, 8)));
            var length = Fixed(file, entry + 16, 8);
            Assert.Equal(WebLingoCrc32.Compute(file, (int)offset, (int)length), (uint)Fixed(file, entry + 24, 4));
            Assert.Equal(0ul, Fixed(file, entry + 28, 4));
            offset += length;
        }
        Assert.Equal((ulong)file.Length, offset);
        Assert.Equal(2ul, Fixed(WebLingoBytecodeFile.Write(StackModule()), 8, 2));
    }

    [Fact]
    public void ConstantsAndStringsReadBackExactly()
    {
        string[] texts = ["", "a", "é", "€", "😀", "a😀b", "\uD800", "\uDC00", "\uD800x", "\uDC00\uD800", "􏿿", new string('x', 300)];
        long[] numbers = [0, 1, -1, 63, 64, -64, -65, 127, 128, int.MaxValue, int.MinValue, long.MaxValue, long.MinValue];
        double[] binaries = [0.0, -0.0, 1.5, double.NaN, double.PositiveInfinity, double.Epsilon, double.MaxValue];
        var constants = new List<WebLingoConstant>();
        constants.AddRange(texts.Select(WebLingoConstant.String));
        constants.AddRange(numbers.Select(WebLingoConstant.Int64));
        constants.AddRange(binaries.Select(WebLingoConstant.Binary64));
        var module = WebLingoCompiledModule.Empty("strings") with { Constants = constants };

        var read = ReadRegister(WebLingoBytecodeFile.Write(module));
        Assert.Equal(constants, read.Constants);
    }

    [Fact]
    public void AModuleReadBackIsWrittenAsTheSameBytes()
    {
        var module = RegisterModule();
        var file = WebLingoBytecodeFile.Write(module, Texts);
        Assert.Equal(file, WebLingoBytecodeFile.Write(module, Texts));
        Assert.True(WebLingoBytecodeFile.TryReadRegister(file, out var read, out var positions, out var error), error);
        Assert.Equal(file, WebLingoBytecodeFile.Write(read, Texts));
        Assert.Equal(WebLingoDisassembler.Format(module), WebLingoDisassembler.Format(read));
        Assert.Equal(module.EntryPoint.Spans, read.EntryPoint.Spans);
        Assert.Equal(module.Functions[0].LocalNames, read.Functions[0].LocalNames);
        Assert.Equal(module.Units.Select(u => (u.Name, u.Function, u.IsNative, string.Join(",", u.Exports))),
            read.Units.Select(u => (u.Name, u.Function, u.IsNative, string.Join(",", u.Exports))));
        Assert.Equal(["count"], read.Classes[0].StaticFields);

        var stack = StackModule();
        var stackFile = WebLingoBytecodeFile.Write(stack, Texts);
        Assert.True(WebLingoBytecodeFile.TryReadStack(stackFile, out var readStack, out _, out error), error);
        Assert.Equal(stackFile, WebLingoBytecodeFile.Write(readStack, Texts));
        Assert.Equal(stack.EntryPoint.Code, readStack.EntryPoint.Code);
        Assert.Equal(stack.EntryPoint.Handlers, readStack.EntryPoint.Handlers);
    }

    [Fact]
    public void EachFilesPositionsAreItsLinesAndColumns()
    {
        Assert.True(WebLingoBytecodeFile.TryReadRegister(WebLingoBytecodeFile.Write(RegisterModule(), Texts), out _, out var positions,
            out var error), error);
        Assert.Equal([null, "lib.wlg"], positions.Select(p => p.Source));
        // The program's own spans start at 0 and 7 (line 2, column 1); lib.wlg's at 3 (line 1, column 3: "😀" is one scalar).
        Assert.Equal([new(0, 1, 1), new(7, 2, 1)], positions[0].Locations);
        Assert.Equal([new WebLingoSourceLocation(3, 1, 3)], positions[1].Locations);
        Assert.Equal(new WebLingoSourceLocation(9, 2, 3), positions[0].GetLocation(9));
        Assert.Equal(new WebLingoSourceLocation(-1, 1, 1), positions[0].GetLocation(-1));

        // Without the files' texts, there are no tables, but the spans are kept.
        Assert.True(WebLingoBytecodeFile.TryReadRegister(WebLingoBytecodeFile.Write(RegisterModule()), out var read, out positions,
            out error), error);
        Assert.Empty(positions);
        Assert.Equal(RegisterModule().EntryPoint.Spans, read.EntryPoint.Spans);
    }

    [Fact]
    public void AFileWithoutSourcePositionsReadsWithoutThem()
    {
        var file = WebLingoBytecodeFile.Write(RegisterModule(), Texts);
        var positionsAt = (int)Fixed(file, 32 + 6 * 32 + 8, 8);
        var shortened = file[..positionsAt];
        SetFixed(shortened, 12, 4, 6);
        SetFixed(shortened, 24, 8, (ulong)shortened.Length);
        Assert.True(WebLingoBytecodeFile.TryReadRegister(shortened, out var read, out var positions, out var error), error);
        Assert.Empty(positions);
        Assert.Null(read.EntryPoint.Spans);
        Assert.Null(read.Functions[0].LocalNames);
    }

    [Fact]
    public void TheDebuggersMetadataReadsBackFromItsOwnSection()
    {
        // weblingo compile --debug's section (the owner's request, 22 November 2026, "Debug Info"): written after the source
        // positions, an eighth section, only for a module that has the metadata; its files are the functions' own.
        var file = WebLingoBytecodeFile.Write(DebugRegisterModule(), Texts);
        Assert.Equal(8UL, Fixed(file, 12, 4));
        Assert.Equal(7UL, Fixed(WebLingoBytecodeFile.Write(RegisterModule(), Texts), 12, 4));
        Assert.True(WebLingoBytecodeFile.TryReadRegister(file, out var read, out var positions, out var error), error);
        Assert.Equal(file, WebLingoBytecodeFile.Write(read, Texts));

        var main = read.EntryPoint.DebugInfo!;
        Assert.Equal("main.wlg", main.Source);
        Assert.Equal(new[] { (0, new WebLingoSourceSpan(0, 3)), (2, new WebLingoSourceSpan(7, 10)) },
            main.Points.OrderBy(point => point.Key).Select(point => (point.Key, point.Value)));
        Assert.Equal(DebugRegisterModule().EntryPoint.DebugInfo!.Locals, main.Locals);
        Assert.Equal("writeLn", main.Locals[1].ExportName);
        Assert.Equal("lib.wlg", read.Functions[0].DebugInfo!.Source);
        Assert.Empty(read.Functions[0].DebugInfo!.Captures);
        Assert.Null(read.Functions[1].DebugInfo);

        // The disassembly shows the statements and the variables where the line and column tables put them, lib.wlg's
        // "😀" making its offset 3 column 3.
        var text = WebLingoDisassembler.Format(read, positions);
        Assert.Contains("; 1:1\n00  LoadConstant r0(a), 42\n", text);
        Assert.Contains("; 2:1\n02  Return\n", text);
        Assert.Contains("variable a: r0, visible from 1:5\n", text);
        Assert.Contains("variable w: r0(a).\"writeLn\", visible 2:1 to 2:11\n", text);
        Assert.Contains("variable e: r0, visible 1:3 to 1:4\n", text);
        Assert.DoesNotContain("variable", WebLingoDisassembler.Format(ReadRegister(WebLingoBytecodeFile.Write(RegisterModule(), Texts))));

        var stack = WebLingoBytecodeFile.Write(DebugStackModule(), Texts);
        Assert.True(WebLingoBytecodeFile.TryReadStack(stack, out var readStack, out _, out error), error);
        Assert.Equal("stack.wlg", readStack.EntryPoint.DebugInfo!.Source);
        Assert.Equal(DebugStackModule().EntryPoint.DebugInfo!.Locals, readStack.EntryPoint.DebugInfo.Locals);
        Assert.Equal(stack, WebLingoBytecodeFile.Write(readStack, Texts));
    }

    [Fact]
    public void ADebuggingSectionThatDoesNotFitItsFunctionsIsRefused()
    {
        var module = DebugRegisterModule();
        var info = module.EntryPoint.DebugInfo!;
        WebLingoCompiledModule With(WebLingoFunctionDebugInfo changed) => module with
        {
            EntryPoint = module.EntryPoint with { DebugInfo = changed }
        };
        AssertRefused(WebLingoBytecodeFile.Write(With(info with { Points = new Dictionary<int, WebLingoSourceSpan> { [3] = new(0, 1) } })),
            "The file is damaged: its debugging section has a statement's instruction outside its function, or out of order.");
        AssertRefused(WebLingoBytecodeFile.Write(With(info with { Locals = [new WebLingoDebugLocal("x", 1, 0, 1)] })),
            "The file is damaged: its debugging section has a variable in a local its function does not have.");
        AssertRefused(WebLingoBytecodeFile.Write(With(info with { Locals = [new WebLingoDebugLocal("x", 0, 5, 4)] })),
            "The file is damaged: its debugging section has a variable visible in a span that ends before it starts.");
        AssertRefused(WebLingoBytecodeFile.Write(With(info with { Captures = ["k"] })),
            "The file is damaged: its debugging section names captures its function does not have.");
        var file = WebLingoBytecodeFile.Write(module);
        file[^1] ^= 0x01;
        AssertRefused(file, "The file is damaged: its debugging section does not match its CRC-32.");
    }

    [Fact]
    public void AFileThatIsNotWlbcOrOfAnotherVersionIsRefused()
    {
        AssertRefused("hello"u8.ToArray(), "The file is not WLBC: it does not start with 'WLBC'.");
        var file = WebLingoBytecodeFile.Write(WebLingoCompiledModule.Empty("main"));
        var other = (byte[])file.Clone();
        SetFixed(other, 4, 2, 1);
        SetFixed(other, 6, 2, 0);
        AssertRefused(other, "The file is WLBC 1.0, which this reader does not know: it reads WLBC 0.1.");
        Assert.False(WebLingoBytecodeFile.TryReadKind(other, out _, out var error));
        Assert.Equal("The file is WLBC 1.0, which this reader does not know: it reads WLBC 0.1.", error);
        other = (byte[])file.Clone();
        SetFixed(other, 8, 2, 3);
        AssertRefused(other, "The file is damaged: its header names VM 3, which is neither 1, the register VM, nor 2, the stack VM.");
        other = (byte[])file.Clone();
        SetFixed(other, 10, 2, 1);
        AssertRefused(other, "The file is damaged: its header has an unknown flag.");
    }

    [Fact]
    public void EachVmRefusesTheOthersBytecode()
    {
        var stack = WebLingoBytecodeFile.Write(StackModule());
        Assert.True(WebLingoBytecodeFile.TryReadKind(stack, out var kind, out _));
        Assert.Equal(WebLingoBytecodeKind.Stack, kind);
        Assert.False(WebLingoBytecodeFile.TryReadRegister(stack, out _, out _, out var error));
        Assert.Equal("The file holds the stack VM's bytecode, which the register VM does not run.", error);
        Assert.False(WebLingoBytecodeFile.TryReadStack(WebLingoBytecodeFile.Write(RegisterModule()), out _, out _, out error));
        Assert.Equal("The file holds the register VM's bytecode, which the stack VM does not run.", error);
    }

    [Fact]
    public void ADamagedFileIsRefused()
    {
        var file = WebLingoBytecodeFile.Write(RegisterModule(), Texts);
        AssertRefused(file[..^1], $"The file is damaged: its header says it is {file.Length} bytes long, and it is {file.Length - 1}.");

        // A changed byte in a section is caught by its CRC-32.
        var changed = (byte[])file.Clone();
        var constantsAt = (int)Fixed(file, 32 + 32 + 8, 8);
        changed[constantsAt + 1] ^= 0x10;
        AssertRefused(changed, "The file is damaged: its constants section does not match its CRC-32.");

        // So is the directory pointing outside the file.
        changed = (byte[])file.Clone();
        SetFixed(changed, 32 + 2 * 32 + 16, 8, (ulong)file.Length);
        AssertRefused(changed, "The file is damaged: its functions section is outside the file.");
        changed = (byte[])file.Clone();
        SetFixed(changed, 32 + 32, 4, 3);
        AssertRefused(changed, "The file is damaged: its directory's entry 2 is not section 2's.");

        // A count larger than its section, with the CRC-32 made to match, is caught as such: the constants' count is its first byte.
        changed = (byte[])file.Clone();
        changed[constantsAt] = 0x7F;
        Recrc(changed, 2);
        AssertRefused(changed, "The file is damaged: its constants section has a count larger than what is left of it.");
    }

    [Fact]
    public void AFileWhoseBytecodeFailsVerificationIsRefused()
    {
        var module = WebLingoCompiledModule.Empty("bad") with
        {
            EntryPoint = new WebLingoFunction("main", 0, 0, [new WebLingoInstruction(WebLingoOpCode.Jump, 5)])
        };
        AssertRefused(WebLingoBytecodeFile.Write(module), "The file's bytecode fails verification: ");
    }

    [Fact]
    public void ChangedBytesNeverMakeTheReaderThrow()
    {
        // The register module with the debugger's metadata, so that its debugging section is changed too.
        var files = new[] { WebLingoBytecodeFile.Write(DebugRegisterModule(), Texts), WebLingoBytecodeFile.Write(StackModule(), Texts) };
        var random = new Random(5_3);
        var (refusedByCrc, read, refused) = (0, 0, 0);
        for (var round = 0; round < 20_000; round++)
        {
            var file = (byte[])files[round % 2].Clone();
            for (var change = random.Next(1, 4); change > 0; change--)
            {
                var at = random.Next(file.Length);
                file[at] = random.Next(3) == 0 ? (byte)random.Next(256) : (byte)(file[at] ^ (1 << random.Next(8)));
            }
            // Half the time the sections' CRC-32s are made to match, so that what is in them is read.
            var recrc = round % 4 < 2;
            if (recrc)
            {
                for (var section = 1; section <= 8; section++) Recrc(file, section);
            }
            var ok = round % 2 == 0
                ? WebLingoBytecodeFile.TryReadRegister(file, out _, out _, out var error)
                : WebLingoBytecodeFile.TryReadStack(file, out _, out _, out error);
            if (ok)
            {
                read++;
                continue;
            }
            Assert.StartsWith("The file", error);
            if (error!.Contains("CRC-32", StringComparison.Ordinal)) refusedByCrc++;
            else refused++;
        }
        Assert.InRange(refusedByCrc, 1_000, 20_000);
        Assert.InRange(refused, 1_000, 20_000);
        Assert.InRange(read, 1, 20_000);
    }

    // The program's text, its second line starting at 7, and lib.wlg's, where "😀" puts y, at 3, in column 3.
    private static readonly WebLingoSourceText OwnText = new("var a;\nwriteLn(a);\n", "main.wlg");
    private static readonly WebLingoSourceText LibText = new("x😀y = 1;", "lib.wlg");

    private static WebLingoSourceText? Texts(string? source) => source switch
    {
        null => OwnText,
        "lib.wlg" => LibText,
        _ => null
    };

    /// <summary>A program of two files, with a class, a closure, a handler, and debugging information.</summary>
    private static WebLingoCompiledModule RegisterModule()
    {
        var main = new WebLingoFunction("main", 3, 1,
            [
                new WebLingoInstruction(WebLingoOpCode.LoadConstant, 0, 0),
                new WebLingoInstruction(WebLingoOpCode.MakeClosure, 1, 0),
                new WebLingoInstruction(WebLingoOpCode.Return)
            ],
            [new WebLingoSourceSpan(0, 3), new WebLingoSourceSpan(7, 10), new WebLingoSourceSpan(7, 1)], ["a"])
        {
            IsAsync = true
        };
        var closure = new WebLingoFunction("f", 2, 1,
            [
                new WebLingoInstruction(WebLingoOpCode.LoadConstant, 1, 1),
                new WebLingoInstruction(WebLingoOpCode.Throw, 1),
                new WebLingoInstruction(WebLingoOpCode.ReturnValue, 0)
            ],
            [new WebLingoSourceSpan(3, 1), new WebLingoSourceSpan(3, 1), new WebLingoSourceSpan(3, 1)], ["e"])
        {
            Source = "lib.wlg",
            Handlers = [new WebLingoHandler(0, 2, 2, 0, 1)]
        };
        var lib = new WebLingoFunction("lib.wlg", 1, 0, [new WebLingoInstruction(WebLingoOpCode.Return)])
        {
            IsAsync = true,
            Source = "lib.wlg"
        };
        return new WebLingoCompiledModule("main.wlg", [WebLingoConstant.Int64(42), WebLingoConstant.String("oops")],
            [new WebLingoImport("writeLn", 1)], main)
        {
            Functions = [closure, lib],
            Classes = [new WebLingoClassInfo("C", -1, []) { StaticFields = ["count"] }],
            Units = [new WebLingoUnitInfo("main.wlg", -1, []), new WebLingoUnitInfo("lib.wlg", 1, ["x"])],
        };
    }

    /// <summary>
    /// <see cref="RegisterModule"/> with the debugger's metadata: main's two statements, its variable and a named import, and
    /// the closure's own, in lib.wlg; their files given only to be left out of the file.
    /// </summary>
    private static WebLingoCompiledModule DebugRegisterModule()
    {
        var module = RegisterModule();
        return module with
        {
            EntryPoint = module.EntryPoint with
            {
                DebugInfo = new("C:/host/main.wlg",
                    new Dictionary<int, WebLingoSourceSpan> { [2] = new(7, 10), [0] = new(0, 3) },
                    [new WebLingoDebugLocal("a", 0, 4, int.MaxValue), new WebLingoDebugLocal("w", 0, 7, 17) { ExportName = "writeLn" }],
                    [])
            },
            Functions =
            [
                module.Functions[0] with
                {
                    DebugInfo = new("C:/host/lib.wlg", new Dictionary<int, WebLingoSourceSpan> { [0] = new(3, 1) },
                        [new WebLingoDebugLocal("e", 0, 3, 4)], [])
                },
                module.Functions[1]
            ]
        };
    }

    private static WebLingoStackModule DebugStackModule()
    {
        var module = StackModule();
        return module with
        {
            EntryPoint = module.EntryPoint with
            {
                DebugInfo = new("stack.wlg", new Dictionary<int, WebLingoSourceSpan> { [0] = new(0, 1) },
                    [new WebLingoDebugLocal("s", 1, 0, 5)], [])
            }
        };
    }

    private static WebLingoStackModule StackModule() =>
        new("stack.wlg", [WebLingoConstant.String("s")], [new WebLingoImport("writeLn", 1)],
            new WebLingoStackFunction("main", 2, 1,
                [
                    new WebLingoStackInstruction(WebLingoStackOpCode.LoadConstant, 0),
                    new WebLingoStackInstruction(WebLingoStackOpCode.Throw),
                    new WebLingoStackInstruction(WebLingoStackOpCode.Return)
                ])
            {
                Handlers = [new WebLingoStackHandler(0, 2, 2, 0, 1, 0)]
            });

    private static WebLingoCompiledModule ReadRegister(byte[] file)
    {
        Assert.True(WebLingoBytecodeFile.TryReadRegister(file, out var module, out _, out var error), error);
        return module;
    }

    private static void AssertRefused(byte[] file, string error)
    {
        Assert.False(WebLingoBytecodeFile.TryReadRegister(file, out var module, out _, out var actual));
        Assert.Null(module);
        Assert.StartsWith(error, actual);
    }

    private static ulong Fixed(byte[] file, int at, int size)
    {
        ulong value = 0;
        for (var i = 0; i < size; i++) value |= (ulong)file[at + i] << (8 * i);
        return value;
    }

    private static void SetFixed(byte[] file, int at, int size, ulong value)
    {
        for (var i = 0; i < size; i++) file[at + i] = (byte)(value >> (8 * i));
    }

    /// <summary>Makes a section's CRC-32 in the directory match it again, if its entry is still in the file and its bounds are.</summary>
    private static void Recrc(byte[] file, int section)
    {
        var entry = 32 + 32 * (section - 1);
        if (entry + 32 > file.Length) return;
        var (offset, length) = (Fixed(file, entry + 8, 8), Fixed(file, entry + 16, 8));
        if (offset > (ulong)file.Length || length > (ulong)file.Length - offset) return;
        SetFixed(file, entry + 24, 4, WebLingoCrc32.Compute(file, (int)offset, (int)length));
    }
}
