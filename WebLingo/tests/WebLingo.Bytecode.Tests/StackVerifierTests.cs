using WebLingo.Bytecode;
using Xunit;
namespace WebLingo.Bytecode.Tests;

/// <summary>The stack bytecode's verifier (specification, section 5.6, part 3): what it accepts, and what it rejects, and why.</summary>
public sealed class StackVerifierTests
{
    [Fact]
    public void AWellFormedModuleIsValid()
    {
        // var a = 1; writeLn(-(a + 2));
        Assert.Equal(WsVerificationResult.Valid, Verify(Module(locals: 1, maxStack: 2,
            I(WsStackOpCode.LoadConstant, 0), I(WsStackOpCode.StoreLocal, 0),
            I(WsStackOpCode.LoadLocal, 0), I(WsStackOpCode.LoadConstant, 1), I(WsStackOpCode.Add), I(WsStackOpCode.Negate),
            I(WsStackOpCode.CallBuiltin, 0, 1), I(WsStackOpCode.Pop), I(WsStackOpCode.Return))));
    }

    [Fact]
    public void PoppingFromAnEmptyStackIsRejected()
    {
        AssertRejected(Module(locals: 0, maxStack: 1, I(WsStackOpCode.Pop), I(WsStackOpCode.Return)), 0, "pops 1 values from a stack of 0");
    }

    [Fact]
    public void AStackDeeperThanItsMaximumIsRejected()
    {
        AssertRejected(Module(locals: 0, maxStack: 1, I(WsStackOpCode.LoadNull), I(WsStackOpCode.LoadNull), I(WsStackOpCode.Return)),
            1, "leaves 2 values on a stack of at most 1");
    }

    [Fact]
    public void PathsThatMeetWithDifferentDepthsAreRejected()
    {
        // 0: push true; 1: if false go to 3; 2: push null; 3: return. Instruction 3 is reached with 0 and 1 values.
        AssertRejected(Module(locals: 0, maxStack: 1,
            I(WsStackOpCode.LoadBoolean, 1), I(WsStackOpCode.JumpIfFalse, 3), I(WsStackOpCode.LoadNull), I(WsStackOpCode.Return)),
            3, "with stacks of 0 and 1 values");
    }

    [Fact]
    public void ALocalReadBeforeItIsWrittenIsRejected()
    {
        AssertRejected(Module(locals: 1, maxStack: 1, I(WsStackOpCode.LoadLocal, 0), I(WsStackOpCode.ReturnValue)), 0, "reads local 0");
    }

    [Fact]
    public void ALocalWrittenOnOnlyOnePathIsRejected()
    {
        // 0: push true; 1: if false go to 4; 2: push null; 3: local 0 = it; 4: push local 0; 5: return it.
        AssertRejected(Module(locals: 1, maxStack: 1,
            I(WsStackOpCode.LoadBoolean, 1), I(WsStackOpCode.JumpIfFalse, 4), I(WsStackOpCode.LoadNull),
            I(WsStackOpCode.StoreLocal, 0), I(WsStackOpCode.LoadLocal, 0), I(WsStackOpCode.ReturnValue)),
            4, "reads local 0");
    }

    [Fact]
    public void AParameterIsWrittenWhenTheFunctionStarts()
    {
        var function = new WsStackFunction("f", 1, 1, [I(WsStackOpCode.LoadLocal, 0), I(WsStackOpCode.ReturnValue)]) { ParameterCount = 1 };
        var module = Module(locals: 0, maxStack: 0, I(WsStackOpCode.Return)) with { Functions = [function] };
        Assert.Equal(WsVerificationResult.Valid, Verify(module));
    }

    [Fact]
    public void CodeThatRunsOffItsEndIsRejected()
    {
        AssertRejected(Module(locals: 0, maxStack: 1, I(WsStackOpCode.LoadNull)), 0, "run off its end");
    }

    [Theory]
    [InlineData(WsStackOpCode.LoadConstant, 9, 0, "constant 9 does not exist")]
    [InlineData(WsStackOpCode.LoadLocal, 1, 0, "local 1 does not exist")]
    [InlineData(WsStackOpCode.Jump, 7, 0, "target instruction 7 does not exist")]
    [InlineData(WsStackOpCode.LoadBoolean, 2, 0, "its value is 2")]
    [InlineData(WsStackOpCode.LoadNull, 0, 1, "an operand it does not use is not 0")]
    [InlineData(WsStackOpCode.CallBuiltin, 0, 2, "'writeLn' takes 1 arguments, not 2")]
    [InlineData(WsStackOpCode.Call, -1, 0, "its argument count, -1")]
    [InlineData(WsStackOpCode.MakeClosure, 0, 0, "function 0 does not exist")]
    [InlineData((WsStackOpCode)200, 0, 0, "the operation 200 is unknown")]
    public void AnInvalidOperandIsRejected(WsStackOpCode opCode, int a, int b, string error)
    {
        AssertRejected(Module(locals: 1, maxStack: 4, I(opCode, a, b), I(WsStackOpCode.Return)), 0, error);
    }

    [Fact]
    public void TooManySlotsAreRejected()
    {
        AssertRejected(Module(locals: 65_000, maxStack: 1_000, I(WsStackOpCode.Return)), -1, "65535 slots");
    }

    [Fact]
    public void RandomCodeNeverThrows()
    {
        var random = new Random(5_5);
        var opCodes = Enum.GetValues<WsStackOpCode>();
        var valid = 0;
        for (var round = 0; round < 20_000; round++)
        {
            var code = new WsStackInstruction[random.Next(1, 12)];
            for (var i = 0; i < code.Length; i++)
            {
                var opCode = random.Next(16) == 0 ? (WsStackOpCode)random.Next(256) : opCodes[random.Next(opCodes.Length)];
                code[i] = new WsStackInstruction(opCode, random.Next(4) == 0 ? random.Next(-2, 12) : random.Next(3),
                    random.Next(4) == 0 ? random.Next(-1, 3) : 0);
            }
            if (random.Next(2) == 0) code[^1] = I(WsStackOpCode.Return);
            var result = Verify(Module(locals: random.Next(3), maxStack: random.Next(4), code));
            Assert.True(result.IsValid || result.Error is not null);
            if (result.IsValid) valid++;
        }
        Assert.InRange(valid, 1, 19_999);
    }

    private static WsStackInstruction I(WsStackOpCode opCode, int a = 0, int b = 0) => new(opCode, a, b);

    private static WsStackModule Module(int locals, int maxStack, params WsStackInstruction[] code) =>
        new("test", [WsConstant.Int64(1), WsConstant.Int64(2)], [new WsImport("writeLn", 1)],
            new WsStackFunction("main", locals, maxStack, code));

    private static WsVerificationResult Verify(WsStackModule module) => new WsStackBytecodeVerifier().Verify(module);

    private static void AssertRejected(WsStackModule module, int instruction, string error)
    {
        var result = Verify(module);
        Assert.False(result.IsValid);
        Assert.Equal(instruction, result.InstructionIndex);
        Assert.Contains(error, result.Error);
    }
}
