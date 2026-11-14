using WebLingo.Bytecode;
using WebLingo.Core;
using Xunit;
namespace WebLingo.Bytecode.Tests;

public sealed class VerifierTests
{
    private static readonly WebLingoVerificationResult Valid = WebLingoVerificationResult.Valid;

    [Fact]
    public void AnEmptyModuleIsValid()
    {
        Assert.Equal(Valid, Verify(WebLingoCompiledModule.Empty("empty")));
    }

    [Fact]
    public void AWellFormedModuleIsValid()
    {
        // var a = 1; var b = a + 2; writeLn(-b);
        Assert.Equal(Valid, Verify(Module(
            registers: 4, locals: 2,
            I(WebLingoOpCode.LoadConstant, 0, 0),
            I(WebLingoOpCode.LoadConstant, 2, 1),
            I(WebLingoOpCode.Add, 1, 0, 2),
            I(WebLingoOpCode.Negate, 2, 1),
            I(WebLingoOpCode.CallBuiltin, 3, 0, 2, 1),
            I(WebLingoOpCode.Return))));
    }

    [Fact]
    public void ALocalReadBeforeItIsWrittenIsRejected()
    {
        // A local has a value from its declaration on (specification, section 3.1), so like a temporary it is
        // written before it is read.
        AssertRejected(Module(registers: 2, locals: 1, I(WebLingoOpCode.Negate, 1, 0), I(WebLingoOpCode.Return)), 0, "register 0");
    }

    [Fact]
    public void AHandlerIsReachedFromEachInstructionOfItsRegion()
    {
        // r2 is written inside the region, so the handler may not read it if the region begins before that write.
        WebLingoCompiledModule Protected(int start) => Module(registers: 4, locals: 4,
                I(WebLingoOpCode.LoadNull, 0), I(WebLingoOpCode.LoadNull, 2), I(WebLingoOpCode.Throw, 0), I(WebLingoOpCode.ReturnValue, 2)) with
            {
                EntryPoint = Module(registers: 4, locals: 4, I(WebLingoOpCode.LoadNull, 0), I(WebLingoOpCode.LoadNull, 2),
                    I(WebLingoOpCode.Throw, 0), I(WebLingoOpCode.ReturnValue, 2)).EntryPoint with { Handlers = [new WebLingoHandler(start, 3, 3, 1, 3)] }
            };
        Assert.Equal(WebLingoVerificationResult.Valid, Verify(Protected(2)));
        AssertRejected(Protected(1), 3, "reads register 2");
        var outside = Protected(2) with
        {
            EntryPoint = Protected(2).EntryPoint with { Handlers = [new WebLingoHandler(2, 9, 3, 1, 3)] }
        };
        AssertRejected(outside, -1, "not within the code");
    }

    [Fact]
    public void AStaticMethodIsAFunctionWithoutThis()
    {
        var method = new WebLingoFunction("m", 1, 1, [I(WebLingoOpCode.Return)]) { HasThis = true, ParameterCount = 1 };
        var function = new WebLingoFunction("f", 0, 0, [I(WebLingoOpCode.Return)]);
        var module = Module(registers: 0, locals: 0, I(WebLingoOpCode.Return)) with
        {
            Functions = [method, function],
            Classes = [new WebLingoClassInfo("A", -1, []) { StaticMethods = [new WebLingoClassMethod("s", 1)], StaticFields = ["n"] }]
        };
        Assert.Equal(WebLingoVerificationResult.Valid, Verify(module));
        AssertRejected(module with
        {
            Classes = [new WebLingoClassInfo("A", -1, []) { StaticMethods = [new WebLingoClassMethod("s", 0)] }]
        }, -1, "not a function without 'this'");
        AssertRejected(module with
        {
            Classes = [new WebLingoClassInfo("A", -1, []) { StaticMethods = [new WebLingoClassMethod("n", 1)], StaticFields = ["n"] }]
        }, -1, "one it shares");
    }

    [Fact]
    public void ADynamicImportsFlagsAreItsValuesAndAComponentsRender()
    {
        // D: 1 for values in C, plus 2 for a component's render (section 13).
        for (var d = 0; d <= 3; d++)
        {
            var c = (d & 1) == 1 ? 1 : 0;
            Assert.Equal(WebLingoVerificationResult.Valid, Verify(Module(registers: 3, locals: 2,
                I(WebLingoOpCode.LoadNull, 0), I(WebLingoOpCode.LoadNull, 1), I(WebLingoOpCode.ImportDynamic, 2, 0, c, d), I(WebLingoOpCode.Return))));
        }
        AssertRejected(Module(registers: 3, locals: 2, I(WebLingoOpCode.LoadNull, 0), I(WebLingoOpCode.ImportDynamic, 2, 0, 0, 4),
            I(WebLingoOpCode.Return)), 1, "its flags, 4, are not 0 to 3");
        AssertRejected(Module(registers: 3, locals: 2, I(WebLingoOpCode.LoadNull, 0), I(WebLingoOpCode.ImportDynamic, 2, 0, 1, 2),
            I(WebLingoOpCode.Return)), 1, "C, 1, is not 0 without values");
        AssertRejected(Module(registers: 3, locals: 2, I(WebLingoOpCode.LoadNull, 0), I(WebLingoOpCode.ImportDynamic, 2, 0, 1, 3),
            I(WebLingoOpCode.Return)), 1, "register 1");
    }

    [Fact]
    public void ADisposedRegisterMustHaveBeenWritten()
    {
        Assert.Equal(WebLingoVerificationResult.Valid,
            Verify(Module(registers: 1, locals: 1, I(WebLingoOpCode.LoadNull, 0), I(WebLingoOpCode.Dispose, 0), I(WebLingoOpCode.Return))));
        AssertRejected(Module(registers: 1, locals: 1, I(WebLingoOpCode.Dispose, 0), I(WebLingoOpCode.Return)), 0, "register 0");
    }

    [Fact]
    public void AnUnknownOperationIsRejected()
    {
        AssertRejected(Module(registers: 0, locals: 0, I((WebLingoOpCode)200), I(WebLingoOpCode.Return)), 0, "unknown");
    }

    [Theory]
    [InlineData(WebLingoOpCode.Move, 5, 0, 0)]
    [InlineData(WebLingoOpCode.Move, 0, -1, 0)]
    [InlineData(WebLingoOpCode.Add, 0, 1, 3)]
    [InlineData(WebLingoOpCode.Negate, 3, 0, 0)]
    [InlineData(WebLingoOpCode.LoadConstant, 3, 0, 0)]
    [InlineData(WebLingoOpCode.Dispose, 3, 0, 0)]
    [InlineData(WebLingoOpCode.ToText, 0, 3, 0)]
    [InlineData(WebLingoOpCode.IsInstance, 0, 1, 3)]
    [InlineData(WebLingoOpCode.JumpIfGiven, 3, 0, 0)]
    public void ARegisterOutOfRangeIsRejected(WebLingoOpCode opCode, int a, int b, int c)
    {
        AssertRejected(Module(registers: 3, locals: 3, I(opCode, a, b, c), I(WebLingoOpCode.Return)), 0, "register");
    }

    [Fact]
    public void AMissingConstantIsRejected()
    {
        AssertRejected(Module(registers: 1, locals: 1, I(WebLingoOpCode.LoadConstant, 0, 2), I(WebLingoOpCode.Return)), 0, "constant 2");
    }

    [Fact]
    public void AnUnknownConstantKindIsRejected()
    {
        var module = Module(registers: 1, locals: 1, I(WebLingoOpCode.Return)) with
        {
            Constants = [new WebLingoConstant((WebLingoConstantKind)9, 0)]
        };
        AssertRejected(module, -1, "kind");
    }

    [Fact]
    public void AMissingImportIsRejected()
    {
        AssertRejected(Module(registers: 2, locals: 1, I(WebLingoOpCode.CallBuiltin, 0, 1, 1, 1), I(WebLingoOpCode.Return)), 0, "import 1");
    }

    [Fact]
    public void ACallWithTheWrongArgumentCountIsRejected()
    {
        AssertRejected(Module(registers: 3, locals: 3, I(WebLingoOpCode.CallBuiltin, 0, 0, 1, 2), I(WebLingoOpCode.Return)), 0, "takes 1");
    }

    [Fact]
    public void CallArgumentsBeyondTheRegistersAreRejected()
    {
        AssertRejected(Module(registers: 3, locals: 3, I(WebLingoOpCode.CallBuiltin, 0, 0, 3, 1), I(WebLingoOpCode.Return)), 0, "arguments");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void AJumpOutsideTheCodeIsRejected(int target)
    {
        AssertRejected(Module(registers: 0, locals: 0, I(WebLingoOpCode.Jump, target), I(WebLingoOpCode.Return)), 0, "target");
    }

    [Fact]
    public void CodeThatRunsOffItsEndIsRejected()
    {
        AssertRejected(Module(registers: 1, locals: 1, I(WebLingoOpCode.LoadConstant, 0, 0)), 0, "off its end");
    }

    [Fact]
    public void EmptyCodeIsRejected()
    {
        AssertRejected(Module(registers: 0, locals: 0), -1, "no code");
    }

    [Fact]
    public void AnUnusedOperandThatIsNotZeroIsRejected()
    {
        AssertRejected(Module(registers: 1, locals: 1, I(WebLingoOpCode.Return, 1)), 0, "not 0");
    }

    [Fact]
    public void ATemporaryReadBeforeItIsWrittenIsRejected()
    {
        AssertRejected(Module(registers: 3, locals: 1, I(WebLingoOpCode.LoadConstant, 0, 0), I(WebLingoOpCode.Add, 2, 0, 1),
            I(WebLingoOpCode.Return)), 1, "register 1");
    }

    [Fact]
    public void ATemporaryWrittenOnOnlyOnePathIsRejected()
    {
        // 0: jump 3; 1: r1 = c0; 2: jump 3; 3: r2 = -r1; 4: return. The path 0, 3 reaches 3 without writing r1.
        AssertRejected(Module(registers: 3, locals: 1,
            I(WebLingoOpCode.Jump, 3), I(WebLingoOpCode.LoadConstant, 1, 0), I(WebLingoOpCode.Jump, 3), I(WebLingoOpCode.Negate, 2, 1),
            I(WebLingoOpCode.Return)), 3, "every path");
    }

    [Fact]
    public void ATemporaryWrittenOnEveryPathIsAccepted()
    {
        // 0: r1 = c0; 1: jump 3; 2: return (unreachable); 3: r2 = -r1; 4: return.
        Assert.Equal(Valid, Verify(Module(registers: 3, locals: 1,
            I(WebLingoOpCode.LoadConstant, 1, 0), I(WebLingoOpCode.Jump, 3), I(WebLingoOpCode.Return), I(WebLingoOpCode.Negate, 2, 1),
            I(WebLingoOpCode.Return))));
    }

    [Fact]
    public void ATemporaryWrittenOnlyWhenAConditionHoldsIsRejected()
    {
        // 0: r0 = true; 1: if !r0 goto 3; 2: r1 = c0; 3: r2 = -r1; 4: return.
        AssertRejected(Module(registers: 3, locals: 0,
            I(WebLingoOpCode.LoadBoolean, 0, 1), I(WebLingoOpCode.JumpIfFalse, 0, 3), I(WebLingoOpCode.LoadConstant, 1, 0),
            I(WebLingoOpCode.Negate, 2, 1), I(WebLingoOpCode.Return)), 3, "every path");
    }

    [Fact]
    public void AConditionMustBeWrittenBeforeItIsTested()
    {
        AssertRejected(Module(registers: 1, locals: 0, I(WebLingoOpCode.JumpIfTrue, 0, 1), I(WebLingoOpCode.Return)), 0, "register 0");
    }

    [Theory]
    [InlineData(WebLingoOpCode.JumpIfFalse, 0, 2)]
    [InlineData(WebLingoOpCode.JumpIfTrue, 0, -1)]
    [InlineData(WebLingoOpCode.LoadBoolean, 0, 2)]
    public void BadConditionalOperandsAreRejected(WebLingoOpCode opCode, int a, int b)
    {
        AssertRejected(Module(registers: 1, locals: 0, I(opCode, a, b), I(WebLingoOpCode.Return)), 0, "");
    }

    [Fact]
    public void AFunctionsParametersAreWrittenWhenItBegins()
    {
        var function = new WebLingoFunction("f", 2, 2, [I(WebLingoOpCode.Add, 0, 0, 1), I(WebLingoOpCode.ReturnValue, 0)]) { ParameterCount = 2 };
        var module = Module(registers: 1, locals: 0, I(WebLingoOpCode.MakeClosure, 0, 0), I(WebLingoOpCode.Return)) with { Functions = [function] };
        Assert.Equal(Valid, Verify(module));
        var reading = function with { Code = [I(WebLingoOpCode.ReturnValue, 1)], ParameterCount = 1 };
        Assert.False(Verify(module with { Functions = [reading] }).IsValid);
    }

    [Theory]
    [InlineData(WebLingoOpCode.MakeClosure, 0, 1, 0, 0, "function")]
    [InlineData(WebLingoOpCode.LoadCapture, 0, 0, 0, 0, "capture")]
    [InlineData(WebLingoOpCode.StoreCapture, 0, 0, 0, 0, "capture")]
    [InlineData(WebLingoOpCode.LoadBuiltin, 0, 1, 0, 0, "import")]
    [InlineData(WebLingoOpCode.Call, 0, 0, 1, 2, "arguments")]
    [InlineData(WebLingoOpCode.Call, 0, 0, 0, -1, "negative")]
    public void BadFunctionOperandsAreRejected(WebLingoOpCode opCode, int a, int b, int c, int d, string reason)
    {
        var callee = new WebLingoFunction("f", 0, 0, [I(WebLingoOpCode.Return)]);
        var module = Module(registers: 2, locals: 2, I(WebLingoOpCode.LoadNull, 0), I(opCode, a, b, c, d), I(WebLingoOpCode.Return))
            with { Functions = [callee] };
        AssertRejected(module, 1, reason);
    }

    [Fact]
    public void AClosuresCapturedRegistersMustBeWritten()
    {
        var callee = new WebLingoFunction("f", 0, 0, [I(WebLingoOpCode.Return)]) { Captures = [new WebLingoCapture(true, 1)] };
        var module = Module(registers: 2, locals: 0, I(WebLingoOpCode.MakeClosure, 0, 0), I(WebLingoOpCode.Return)) with { Functions = [callee] };
        AssertRejected(module, 0, "register 1");
        var beyond = callee with { Captures = [new WebLingoCapture(true, 5)] };
        AssertRejected(module with { Functions = [beyond] }, 0, "captured");
    }

    [Fact]
    public void TheEntryPointTakesNoParameters()
    {
        var module = WebLingoCompiledModule.Empty("m");
        AssertRejected(module with { EntryPoint = module.EntryPoint with { ParameterCount = 1 } }, -1, "entry point");
    }

    [Fact]
    public void ALoopTerminatesVerification()
    {
        // 0: r1 = c0; 1: r1 = -r1; 2: jump 1. An endless loop, but a valid one.
        Assert.Equal(Valid, Verify(Module(registers: 2, locals: 1,
            I(WebLingoOpCode.LoadConstant, 1, 0), I(WebLingoOpCode.Negate, 1, 1), I(WebLingoOpCode.Jump, 1))));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(WebLingoFunction.MaxRegisters + 1, 0)]
    [InlineData(2, 3)]
    [InlineData(2, -1)]
    public void ImpossibleRegisterCountsAreRejected(int registers, int locals)
    {
        AssertRejected(Module(registers, locals, I(WebLingoOpCode.Return)), -1, "");
    }

    [Fact]
    public void DebugInformationMustMatchTheCode()
    {
        var function = new WebLingoFunction("main", 1, 1, [I(WebLingoOpCode.Return)], [new WebLingoSourceSpan(0, 1), new WebLingoSourceSpan(1, 1)]);
        AssertRejected(new WebLingoCompiledModule("m", [], [], function), -1, "spans");
        var named = new WebLingoFunction("main", 1, 1, [I(WebLingoOpCode.Return)], LocalNames: ["a", "b"]);
        AssertRejected(new WebLingoCompiledModule("m", [], [], named), -1, "local names");
    }

    [Fact]
    public void MissingPartsAreRejectedNotThrown()
    {
        var verifier = new WebLingoBytecodeVerifier();
        Assert.False(verifier.Verify(null!).IsValid);
        Assert.False(verifier.Verify(WebLingoCompiledModule.Empty("m") with { EntryPoint = null! }).IsValid);
        Assert.False(verifier.Verify(WebLingoCompiledModule.Empty("m") with { Constants = null! }).IsValid);
        Assert.False(verifier.Verify(WebLingoCompiledModule.Empty("m") with { Imports = [null!] }).IsValid);
        Assert.False(verifier.Verify(WebLingoCompiledModule.Empty("m") with { Imports = [new WebLingoImport("writeLn", -1)] }).IsValid);
    }

    [Fact]
    public void RandomModulesAreVerifiedWithoutThrowing()
    {
        var random = new Random(20261112);
        var opCodes = Enum.GetValues<WebLingoOpCode>();
        var verifier = new WebLingoBytecodeVerifier();
        var valid = 0;
        for (var round = 0; round < 20_000; round++)
        {
            var registers = random.Next(-1, 6);
            var locals = random.Next(-1, 4);
            var length = random.Next(0, 8);
            var code = new WebLingoInstruction[length];
            for (var i = 0; i < length; i++)
            {
                var opCode = random.Next(10) == 0 ? (WebLingoOpCode)random.Next(256) : opCodes[random.Next(opCodes.Length)];
                code[i] = new WebLingoInstruction(opCode, Operand(random), Operand(random), Operand(random), Operand(random));
            }
            if (length > 0 && random.Next(2) == 0) code[^1] = new WebLingoInstruction(WebLingoOpCode.Return); // Often near-valid.
            var module = new WebLingoCompiledModule("random", [WebLingoConstant.Int64(1)], [new WebLingoImport("writeLn", 1)],
                new WebLingoFunction("main", registers, locals, code));
            if (verifier.Verify(module).IsValid) valid++;
        }
        Assert.InRange(valid, 1, 19_999); // Some pass and most fail, and none of them threw.
    }

    [Fact]
    public void AHugeFunctionIsRejectedWithinItsBounds()
    {
        // Every instruction a jump target, over 65535 temporaries: too much state to keep, so rejected, not attempted.
        const int length = 300_000;
        var code = new WebLingoInstruction[length];
        for (var i = 0; i < length - 1; i++) code[i] = I(WebLingoOpCode.Jump, i + 1);
        code[^1] = I(WebLingoOpCode.Return);
        AssertRejected(Module(WebLingoFunction.MaxRegisters, 0, code), -1, "too large");
    }

    [Fact]
    public void MembersAndIndexesAreChecked()
    {
        // 0: r0 = "length"; 1: r1 = r0.length; 2: r1 = r0[r1]; 3: r0[r1] = r1; 4: r1 = r0.length(); 5: return.
        var module = new WebLingoCompiledModule("members", [WebLingoConstant.String("length"), WebLingoConstant.Int64(1)], [],
            new WebLingoFunction("main", 2, 0, [
                I(WebLingoOpCode.LoadConstant, 0, 0), I(WebLingoOpCode.GetMember, 1, 0, 0), I(WebLingoOpCode.GetIndex, 1, 0, 1),
                I(WebLingoOpCode.SetIndex, 0, 1, 1), I(WebLingoOpCode.CallMethod, 1, 0, 0, 0), I(WebLingoOpCode.Return)
            ]));
        Assert.Equal(Valid, Verify(module));
        var code = module.EntryPoint.Code.ToArray();

        AssertRejected(With(module, code, 1, I(WebLingoOpCode.GetMember, 1, 0, 1)), 1, "constant 1 is not a name");
        AssertRejected(With(module, code, 4, I(WebLingoOpCode.CallMethod, 1, 0, 1, 1)), 4, "registers 1 to 2");
        AssertRejected(With(module, code, 2, I(WebLingoOpCode.GetIndex, 1, 0, 2)), 2, "index register 2");
        Assert.Equal(Valid, Verify(With(module, code, 2, I(WebLingoOpCode.GetIndex, 1, 0, 1, 1)))); // ?[
        AssertRejected(With(module, code, 2, I(WebLingoOpCode.GetIndex, 1, 0, 1, 2)), 2, "not 0 or 1");
        AssertRejected(With(module, code, 3, I(WebLingoOpCode.SetIndex, 0, 1, 1, 1)), 3, "not 0");
        // r1 read before it is written: the member read that wrote it is gone.
        AssertRejected(With(module, code, 1, I(WebLingoOpCode.LoadConstant, 0, 0)), 2, "reads register 1");
    }

    [Fact]
    public void ForEachWritesItsItemsOnlyOnTheWayIntoTheLoop()
    {
        // 0: r0 = 1; 1: r1 = 1; 2: next of r0 into r2, or go to 4; 3: go to 2; 4: return r2, which the way out has not written.
        var module = new WebLingoCompiledModule("loop", [WebLingoConstant.Int64(1)], [],
            new WebLingoFunction("main", 3, 0, [
                I(WebLingoOpCode.LoadConstant, 0, 0), I(WebLingoOpCode.LoadConstant, 1, 0), I(WebLingoOpCode.ForEachNext, 4, 0, 1),
                I(WebLingoOpCode.Jump, 2), I(WebLingoOpCode.ReturnValue, 2)
            ]));
        AssertRejected(module, 4, "reads register 2");
        var code = module.EntryPoint.Code.ToArray();
        code[4] = I(WebLingoOpCode.ReturnValue, 1); // The position is written on both ways.
        Assert.Equal(Valid, Verify(module with { EntryPoint = module.EntryPoint with { Code = code } }));
        AssertRejected(With(module, code, 2, I(WebLingoOpCode.ForEachNext, 4, 0, 3)), 2, "not 1 or 2");
        AssertRejected(With(module, code, 2, I(WebLingoOpCode.ForEachNext, 4, 0, 2)), 2, "registers, 0 to 3");
        AssertRejected(With(module, code, 2, I(WebLingoOpCode.ForEachNext, 9, 0, 1)), 2, "target instruction 9");
    }

    private static WebLingoCompiledModule With(WebLingoCompiledModule module, WebLingoInstruction[] code, int at, WebLingoInstruction instruction)
    {
        var changed = (WebLingoInstruction[])code.Clone();
        changed[at] = instruction;
        return module with { EntryPoint = module.EntryPoint with { Code = changed } };
    }

    private static int Operand(Random random) => random.Next(3) switch
    {
        0 => 0,
        1 => random.Next(0, 6),
        _ => random.Next(8) == 0 ? random.Next(-3, 100) : random.Next(0, 3)
    };

    [Fact]
    public void AnAwaitIsOnlyInAnAsyncFunction()
    {
        var module = Module(registers: 2, locals: 0, I(WebLingoOpCode.LoadNull, 0), I(WebLingoOpCode.Await, 1, 0), I(WebLingoOpCode.Return));
        AssertRejected(module, 1, "not async");
        Assert.Equal(Valid, Verify(module with { EntryPoint = module.EntryPoint with { IsAsync = true } }));
        AssertRejected(Module(registers: 2, locals: 0, I(WebLingoOpCode.Await, 1, 0), I(WebLingoOpCode.Return)) is var unread
            ? unread with { EntryPoint = unread.EntryPoint with { IsAsync = true } } : unread, 0, "register 0");
    }

    private static WebLingoInstruction I(WebLingoOpCode opCode, int a = 0, int b = 0, int c = 0, int d = 0) => new(opCode, a, b, c, d);

    private static WebLingoCompiledModule Module(int registers, int locals, params WebLingoInstruction[] code) =>
        new("test", [WebLingoConstant.Int64(1), WebLingoConstant.Int64(2)], [new WebLingoImport("writeLn", 1)],
            new WebLingoFunction("main", registers, locals, code));

    private static WebLingoVerificationResult Verify(WebLingoCompiledModule module) => new WebLingoBytecodeVerifier().Verify(module);

    private static void AssertRejected(WebLingoCompiledModule module, int instruction, string reason)
    {
        var result = Verify(module);
        Assert.False(result.IsValid);
        Assert.Contains(reason, result.Error);
        Assert.Equal(instruction, result.InstructionIndex);
    }
}
