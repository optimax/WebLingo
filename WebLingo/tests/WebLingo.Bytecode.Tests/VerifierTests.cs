using WebLingo.Bytecode;
using WebLingo.Core;
using Xunit;
namespace WebLingo.Bytecode.Tests;

public sealed class VerifierTests
{
    private static readonly WsVerificationResult Valid = WsVerificationResult.Valid;

    [Fact]
    public void AnEmptyModuleIsValid()
    {
        Assert.Equal(Valid, Verify(WsCompiledModule.Empty("empty")));
    }

    [Fact]
    public void AWellFormedModuleIsValid()
    {
        // var a = 1; var b = a + 2; writeLn(-b);
        Assert.Equal(Valid, Verify(Module(
            registers: 4, locals: 2,
            I(WsOpCode.LoadConstant, 0, 0),
            I(WsOpCode.LoadConstant, 2, 1),
            I(WsOpCode.Add, 1, 0, 2),
            I(WsOpCode.Negate, 2, 1),
            I(WsOpCode.CallBuiltin, 3, 0, 2, 1),
            I(WsOpCode.Return))));
    }

    [Fact]
    public void ALocalReadBeforeItIsWrittenIsRejected()
    {
        // A local has a value from its declaration on (specification, section 3.1), so like a temporary it is
        // written before it is read.
        AssertRejected(Module(registers: 2, locals: 1, I(WsOpCode.Negate, 1, 0), I(WsOpCode.Return)), 0, "register 0");
    }

    [Fact]
    public void AStaticMethodIsAFunctionWithoutThis()
    {
        var method = new WsFunction("m", 1, 1, [I(WsOpCode.Return)]) { HasThis = true, ParameterCount = 1 };
        var function = new WsFunction("f", 0, 0, [I(WsOpCode.Return)]);
        var module = Module(registers: 0, locals: 0, I(WsOpCode.Return)) with
        {
            Functions = [method, function],
            Classes = [new WsClassInfo("A", -1, []) { StaticMethods = [new WsClassMethod("s", 1)], StaticFields = ["n"] }]
        };
        Assert.Equal(WsVerificationResult.Valid, Verify(module));
        AssertRejected(module with
        {
            Classes = [new WsClassInfo("A", -1, []) { StaticMethods = [new WsClassMethod("s", 0)] }]
        }, -1, "not a function without 'this'");
        AssertRejected(module with
        {
            Classes = [new WsClassInfo("A", -1, []) { StaticMethods = [new WsClassMethod("n", 1)], StaticFields = ["n"] }]
        }, -1, "one it shares");
    }

    [Fact]
    public void ADisposedRegisterMustHaveBeenWritten()
    {
        Assert.Equal(WsVerificationResult.Valid,
            Verify(Module(registers: 1, locals: 1, I(WsOpCode.LoadNull, 0), I(WsOpCode.Dispose, 0), I(WsOpCode.Return))));
        AssertRejected(Module(registers: 1, locals: 1, I(WsOpCode.Dispose, 0), I(WsOpCode.Return)), 0, "register 0");
    }

    [Fact]
    public void AnUnknownOperationIsRejected()
    {
        AssertRejected(Module(registers: 0, locals: 0, I((WsOpCode)200), I(WsOpCode.Return)), 0, "unknown");
    }

    [Theory]
    [InlineData(WsOpCode.Move, 5, 0, 0)]
    [InlineData(WsOpCode.Move, 0, -1, 0)]
    [InlineData(WsOpCode.Add, 0, 1, 3)]
    [InlineData(WsOpCode.Negate, 3, 0, 0)]
    [InlineData(WsOpCode.LoadConstant, 3, 0, 0)]
    [InlineData(WsOpCode.Dispose, 3, 0, 0)]
    public void ARegisterOutOfRangeIsRejected(WsOpCode opCode, int a, int b, int c)
    {
        AssertRejected(Module(registers: 3, locals: 3, I(opCode, a, b, c), I(WsOpCode.Return)), 0, "register");
    }

    [Fact]
    public void AMissingConstantIsRejected()
    {
        AssertRejected(Module(registers: 1, locals: 1, I(WsOpCode.LoadConstant, 0, 2), I(WsOpCode.Return)), 0, "constant 2");
    }

    [Fact]
    public void AnUnknownConstantKindIsRejected()
    {
        var module = Module(registers: 1, locals: 1, I(WsOpCode.Return)) with
        {
            Constants = [new WsConstant((WsConstantKind)9, 0)]
        };
        AssertRejected(module, -1, "kind");
    }

    [Fact]
    public void AMissingImportIsRejected()
    {
        AssertRejected(Module(registers: 2, locals: 1, I(WsOpCode.CallBuiltin, 0, 1, 1, 1), I(WsOpCode.Return)), 0, "import 1");
    }

    [Fact]
    public void ACallWithTheWrongArgumentCountIsRejected()
    {
        AssertRejected(Module(registers: 3, locals: 3, I(WsOpCode.CallBuiltin, 0, 0, 1, 2), I(WsOpCode.Return)), 0, "takes 1");
    }

    [Fact]
    public void CallArgumentsBeyondTheRegistersAreRejected()
    {
        AssertRejected(Module(registers: 3, locals: 3, I(WsOpCode.CallBuiltin, 0, 0, 3, 1), I(WsOpCode.Return)), 0, "arguments");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void AJumpOutsideTheCodeIsRejected(int target)
    {
        AssertRejected(Module(registers: 0, locals: 0, I(WsOpCode.Jump, target), I(WsOpCode.Return)), 0, "target");
    }

    [Fact]
    public void CodeThatRunsOffItsEndIsRejected()
    {
        AssertRejected(Module(registers: 1, locals: 1, I(WsOpCode.LoadConstant, 0, 0)), 0, "off its end");
    }

    [Fact]
    public void EmptyCodeIsRejected()
    {
        AssertRejected(Module(registers: 0, locals: 0), -1, "no code");
    }

    [Fact]
    public void AnUnusedOperandThatIsNotZeroIsRejected()
    {
        AssertRejected(Module(registers: 1, locals: 1, I(WsOpCode.Return, 1)), 0, "not 0");
    }

    [Fact]
    public void ATemporaryReadBeforeItIsWrittenIsRejected()
    {
        AssertRejected(Module(registers: 3, locals: 1, I(WsOpCode.LoadConstant, 0, 0), I(WsOpCode.Add, 2, 0, 1),
            I(WsOpCode.Return)), 1, "register 1");
    }

    [Fact]
    public void ATemporaryWrittenOnOnlyOnePathIsRejected()
    {
        // 0: jump 3; 1: r1 = c0; 2: jump 3; 3: r2 = -r1; 4: return. The path 0, 3 reaches 3 without writing r1.
        AssertRejected(Module(registers: 3, locals: 1,
            I(WsOpCode.Jump, 3), I(WsOpCode.LoadConstant, 1, 0), I(WsOpCode.Jump, 3), I(WsOpCode.Negate, 2, 1),
            I(WsOpCode.Return)), 3, "every path");
    }

    [Fact]
    public void ATemporaryWrittenOnEveryPathIsAccepted()
    {
        // 0: r1 = c0; 1: jump 3; 2: return (unreachable); 3: r2 = -r1; 4: return.
        Assert.Equal(Valid, Verify(Module(registers: 3, locals: 1,
            I(WsOpCode.LoadConstant, 1, 0), I(WsOpCode.Jump, 3), I(WsOpCode.Return), I(WsOpCode.Negate, 2, 1),
            I(WsOpCode.Return))));
    }

    [Fact]
    public void ATemporaryWrittenOnlyWhenAConditionHoldsIsRejected()
    {
        // 0: r0 = true; 1: if !r0 goto 3; 2: r1 = c0; 3: r2 = -r1; 4: return.
        AssertRejected(Module(registers: 3, locals: 0,
            I(WsOpCode.LoadBoolean, 0, 1), I(WsOpCode.JumpIfFalse, 0, 3), I(WsOpCode.LoadConstant, 1, 0),
            I(WsOpCode.Negate, 2, 1), I(WsOpCode.Return)), 3, "every path");
    }

    [Fact]
    public void AConditionMustBeWrittenBeforeItIsTested()
    {
        AssertRejected(Module(registers: 1, locals: 0, I(WsOpCode.JumpIfTrue, 0, 1), I(WsOpCode.Return)), 0, "register 0");
    }

    [Theory]
    [InlineData(WsOpCode.JumpIfFalse, 0, 2)]
    [InlineData(WsOpCode.JumpIfTrue, 0, -1)]
    [InlineData(WsOpCode.LoadBoolean, 0, 2)]
    public void BadConditionalOperandsAreRejected(WsOpCode opCode, int a, int b)
    {
        AssertRejected(Module(registers: 1, locals: 0, I(opCode, a, b), I(WsOpCode.Return)), 0, "");
    }

    [Fact]
    public void AFunctionsParametersAreWrittenWhenItBegins()
    {
        var function = new WsFunction("f", 2, 2, [I(WsOpCode.Add, 0, 0, 1), I(WsOpCode.ReturnValue, 0)]) { ParameterCount = 2 };
        var module = Module(registers: 1, locals: 0, I(WsOpCode.MakeClosure, 0, 0), I(WsOpCode.Return)) with { Functions = [function] };
        Assert.Equal(Valid, Verify(module));
        var reading = function with { Code = [I(WsOpCode.ReturnValue, 1)], ParameterCount = 1 };
        Assert.False(Verify(module with { Functions = [reading] }).IsValid);
    }

    [Theory]
    [InlineData(WsOpCode.MakeClosure, 0, 1, 0, 0, "function")]
    [InlineData(WsOpCode.LoadCapture, 0, 0, 0, 0, "capture")]
    [InlineData(WsOpCode.StoreCapture, 0, 0, 0, 0, "capture")]
    [InlineData(WsOpCode.LoadBuiltin, 0, 1, 0, 0, "import")]
    [InlineData(WsOpCode.Call, 0, 0, 1, 2, "arguments")]
    [InlineData(WsOpCode.Call, 0, 0, 0, -1, "negative")]
    public void BadFunctionOperandsAreRejected(WsOpCode opCode, int a, int b, int c, int d, string reason)
    {
        var callee = new WsFunction("f", 0, 0, [I(WsOpCode.Return)]);
        var module = Module(registers: 2, locals: 2, I(WsOpCode.LoadNull, 0), I(opCode, a, b, c, d), I(WsOpCode.Return))
            with { Functions = [callee] };
        AssertRejected(module, 1, reason);
    }

    [Fact]
    public void AClosuresCapturedRegistersMustBeWritten()
    {
        var callee = new WsFunction("f", 0, 0, [I(WsOpCode.Return)]) { Captures = [new WsCapture(true, 1)] };
        var module = Module(registers: 2, locals: 0, I(WsOpCode.MakeClosure, 0, 0), I(WsOpCode.Return)) with { Functions = [callee] };
        AssertRejected(module, 0, "register 1");
        var beyond = callee with { Captures = [new WsCapture(true, 5)] };
        AssertRejected(module with { Functions = [beyond] }, 0, "captured");
    }

    [Fact]
    public void TheEntryPointTakesNoParameters()
    {
        var module = WsCompiledModule.Empty("m");
        AssertRejected(module with { EntryPoint = module.EntryPoint with { ParameterCount = 1 } }, -1, "entry point");
    }

    [Fact]
    public void ALoopTerminatesVerification()
    {
        // 0: r1 = c0; 1: r1 = -r1; 2: jump 1. An endless loop, but a valid one.
        Assert.Equal(Valid, Verify(Module(registers: 2, locals: 1,
            I(WsOpCode.LoadConstant, 1, 0), I(WsOpCode.Negate, 1, 1), I(WsOpCode.Jump, 1))));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(WsFunction.MaxRegisters + 1, 0)]
    [InlineData(2, 3)]
    [InlineData(2, -1)]
    public void ImpossibleRegisterCountsAreRejected(int registers, int locals)
    {
        AssertRejected(Module(registers, locals, I(WsOpCode.Return)), -1, "");
    }

    [Fact]
    public void DebugInformationMustMatchTheCode()
    {
        var function = new WsFunction("main", 1, 1, [I(WsOpCode.Return)], [new WsSourceSpan(0, 1), new WsSourceSpan(1, 1)]);
        AssertRejected(new WsCompiledModule("m", [], [], function), -1, "spans");
        var named = new WsFunction("main", 1, 1, [I(WsOpCode.Return)], LocalNames: ["a", "b"]);
        AssertRejected(new WsCompiledModule("m", [], [], named), -1, "local names");
    }

    [Fact]
    public void MissingPartsAreRejectedNotThrown()
    {
        var verifier = new WsBytecodeVerifier();
        Assert.False(verifier.Verify(null!).IsValid);
        Assert.False(verifier.Verify(WsCompiledModule.Empty("m") with { EntryPoint = null! }).IsValid);
        Assert.False(verifier.Verify(WsCompiledModule.Empty("m") with { Constants = null! }).IsValid);
        Assert.False(verifier.Verify(WsCompiledModule.Empty("m") with { Imports = [null!] }).IsValid);
        Assert.False(verifier.Verify(WsCompiledModule.Empty("m") with { Imports = [new WsImport("writeLn", -1)] }).IsValid);
    }

    [Fact]
    public void RandomModulesAreVerifiedWithoutThrowing()
    {
        var random = new Random(20261112);
        var opCodes = Enum.GetValues<WsOpCode>();
        var verifier = new WsBytecodeVerifier();
        var valid = 0;
        for (var round = 0; round < 20_000; round++)
        {
            var registers = random.Next(-1, 6);
            var locals = random.Next(-1, 4);
            var length = random.Next(0, 8);
            var code = new WsInstruction[length];
            for (var i = 0; i < length; i++)
            {
                var opCode = random.Next(10) == 0 ? (WsOpCode)random.Next(256) : opCodes[random.Next(opCodes.Length)];
                code[i] = new WsInstruction(opCode, Operand(random), Operand(random), Operand(random), Operand(random));
            }
            if (length > 0 && random.Next(2) == 0) code[^1] = new WsInstruction(WsOpCode.Return); // Often near-valid.
            var module = new WsCompiledModule("random", [WsConstant.Int64(1)], [new WsImport("writeLn", 1)],
                new WsFunction("main", registers, locals, code));
            if (verifier.Verify(module).IsValid) valid++;
        }
        Assert.InRange(valid, 1, 19_999); // Some pass and most fail, and none of them threw.
    }

    [Fact]
    public void AHugeFunctionIsRejectedWithinItsBounds()
    {
        // Every instruction a jump target, over 65535 temporaries: too much state to keep, so rejected, not attempted.
        const int length = 300_000;
        var code = new WsInstruction[length];
        for (var i = 0; i < length - 1; i++) code[i] = I(WsOpCode.Jump, i + 1);
        code[^1] = I(WsOpCode.Return);
        AssertRejected(Module(WsFunction.MaxRegisters, 0, code), -1, "too large");
    }

    [Fact]
    public void MembersAndIndexesAreChecked()
    {
        // 0: r0 = "length"; 1: r1 = r0.length; 2: r1 = r0[r1]; 3: r0[r1] = r1; 4: r1 = r0.length(); 5: return.
        var module = new WsCompiledModule("members", [WsConstant.String("length"), WsConstant.Int64(1)], [],
            new WsFunction("main", 2, 0, [
                I(WsOpCode.LoadConstant, 0, 0), I(WsOpCode.GetMember, 1, 0, 0), I(WsOpCode.GetIndex, 1, 0, 1),
                I(WsOpCode.SetIndex, 0, 1, 1), I(WsOpCode.CallMethod, 1, 0, 0, 0), I(WsOpCode.Return)
            ]));
        Assert.Equal(Valid, Verify(module));
        var code = module.EntryPoint.Code.ToArray();

        AssertRejected(With(module, code, 1, I(WsOpCode.GetMember, 1, 0, 1)), 1, "constant 1 is not a name");
        AssertRejected(With(module, code, 4, I(WsOpCode.CallMethod, 1, 0, 1, 1)), 4, "registers 1 to 2");
        AssertRejected(With(module, code, 2, I(WsOpCode.GetIndex, 1, 0, 2)), 2, "index register 2");
        Assert.Equal(Valid, Verify(With(module, code, 2, I(WsOpCode.GetIndex, 1, 0, 1, 1)))); // ?[
        AssertRejected(With(module, code, 2, I(WsOpCode.GetIndex, 1, 0, 1, 2)), 2, "not 0 or 1");
        AssertRejected(With(module, code, 3, I(WsOpCode.SetIndex, 0, 1, 1, 1)), 3, "not 0");
        // r1 read before it is written: the member read that wrote it is gone.
        AssertRejected(With(module, code, 1, I(WsOpCode.LoadConstant, 0, 0)), 2, "reads register 1");
    }

    [Fact]
    public void ForEachWritesItsItemsOnlyOnTheWayIntoTheLoop()
    {
        // 0: r0 = 1; 1: r1 = 1; 2: next of r0 into r2, or go to 4; 3: go to 2; 4: return r2, which the way out has not written.
        var module = new WsCompiledModule("loop", [WsConstant.Int64(1)], [],
            new WsFunction("main", 3, 0, [
                I(WsOpCode.LoadConstant, 0, 0), I(WsOpCode.LoadConstant, 1, 0), I(WsOpCode.ForEachNext, 4, 0, 1),
                I(WsOpCode.Jump, 2), I(WsOpCode.ReturnValue, 2)
            ]));
        AssertRejected(module, 4, "reads register 2");
        var code = module.EntryPoint.Code.ToArray();
        code[4] = I(WsOpCode.ReturnValue, 1); // The position is written on both ways.
        Assert.Equal(Valid, Verify(module with { EntryPoint = module.EntryPoint with { Code = code } }));
        AssertRejected(With(module, code, 2, I(WsOpCode.ForEachNext, 4, 0, 3)), 2, "not 1 or 2");
        AssertRejected(With(module, code, 2, I(WsOpCode.ForEachNext, 4, 0, 2)), 2, "registers, 0 to 3");
        AssertRejected(With(module, code, 2, I(WsOpCode.ForEachNext, 9, 0, 1)), 2, "target instruction 9");
    }

    private static WsCompiledModule With(WsCompiledModule module, WsInstruction[] code, int at, WsInstruction instruction)
    {
        var changed = (WsInstruction[])code.Clone();
        changed[at] = instruction;
        return module with { EntryPoint = module.EntryPoint with { Code = changed } };
    }

    private static int Operand(Random random) => random.Next(3) switch
    {
        0 => 0,
        1 => random.Next(0, 6),
        _ => random.Next(8) == 0 ? random.Next(-3, 100) : random.Next(0, 3)
    };

    private static WsInstruction I(WsOpCode opCode, int a = 0, int b = 0, int c = 0, int d = 0) => new(opCode, a, b, c, d);

    private static WsCompiledModule Module(int registers, int locals, params WsInstruction[] code) =>
        new("test", [WsConstant.Int64(1), WsConstant.Int64(2)], [new WsImport("writeLn", 1)],
            new WsFunction("main", registers, locals, code));

    private static WsVerificationResult Verify(WsCompiledModule module) => new WsBytecodeVerifier().Verify(module);

    private static void AssertRejected(WsCompiledModule module, int instruction, string reason)
    {
        var result = Verify(module);
        Assert.False(result.IsValid);
        Assert.Contains(reason, result.Error);
        Assert.Equal(instruction, result.InstructionIndex);
    }
}
