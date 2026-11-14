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
        Assert.Equal(WebLingoVerificationResult.Valid, Verify(Module(locals: 1, maxStack: 2,
            I(WebLingoStackOpCode.LoadConstant, 0), I(WebLingoStackOpCode.StoreLocal, 0),
            I(WebLingoStackOpCode.LoadLocal, 0), I(WebLingoStackOpCode.LoadConstant, 1), I(WebLingoStackOpCode.Add), I(WebLingoStackOpCode.Negate),
            I(WebLingoStackOpCode.CallBuiltin, 0, 1), I(WebLingoStackOpCode.Pop), I(WebLingoStackOpCode.Return))));
    }

    [Fact]
    public void PoppingFromAnEmptyStackIsRejected()
    {
        AssertRejected(Module(locals: 0, maxStack: 1, I(WebLingoStackOpCode.Pop), I(WebLingoStackOpCode.Return)), 0, "pops 1 values from a stack of 0");
    }

    [Fact]
    public void AHandlersStackDepthIsNoDeeperThanItsRegions()
    {
        WebLingoStackModule Protected(int depth) => Module(locals: 2, maxStack: 2,
                I(WebLingoStackOpCode.LoadNull), I(WebLingoStackOpCode.Throw), I(WebLingoStackOpCode.LoadLocal, 0), I(WebLingoStackOpCode.ReturnValue)) with
            {
                EntryPoint = Module(locals: 2, maxStack: 2, I(WebLingoStackOpCode.LoadNull), I(WebLingoStackOpCode.Throw),
                    I(WebLingoStackOpCode.LoadLocal, 0), I(WebLingoStackOpCode.ReturnValue)).EntryPoint with
                {
                    Handlers = [new WebLingoStackHandler(0, 2, 2, 0, 1, depth)]
                }
            };
        Assert.Equal(WebLingoVerificationResult.Valid, Verify(Protected(0)));
        AssertRejected(Protected(1), 0, "below its handler's");
    }

    [Fact]
    public void DisposePopsItsResource()
    {
        Assert.Equal(WebLingoVerificationResult.Valid,
            Verify(Module(locals: 0, maxStack: 1, I(WebLingoStackOpCode.LoadNull), I(WebLingoStackOpCode.Dispose), I(WebLingoStackOpCode.Return))));
        AssertRejected(Module(locals: 0, maxStack: 1, I(WebLingoStackOpCode.Dispose), I(WebLingoStackOpCode.Return)), 0,
            "pops 1 values from a stack of 0");
    }

    [Fact]
    public void AStackDeeperThanItsMaximumIsRejected()
    {
        AssertRejected(Module(locals: 0, maxStack: 1, I(WebLingoStackOpCode.LoadNull), I(WebLingoStackOpCode.LoadNull), I(WebLingoStackOpCode.Return)),
            1, "leaves 2 values on a stack of at most 1");
    }

    [Fact]
    public void PathsThatMeetWithDifferentDepthsAreRejected()
    {
        // 0: push true; 1: if false go to 3; 2: push null; 3: return. Instruction 3 is reached with 0 and 1 values.
        AssertRejected(Module(locals: 0, maxStack: 1,
            I(WebLingoStackOpCode.LoadBoolean, 1), I(WebLingoStackOpCode.JumpIfFalse, 3), I(WebLingoStackOpCode.LoadNull), I(WebLingoStackOpCode.Return)),
            3, "with stacks of 0 and 1 values");
    }

    [Fact]
    public void ALocalReadBeforeItIsWrittenIsRejected()
    {
        AssertRejected(Module(locals: 1, maxStack: 1, I(WebLingoStackOpCode.LoadLocal, 0), I(WebLingoStackOpCode.ReturnValue)), 0, "reads local 0");
    }

    [Fact]
    public void ALocalWrittenOnOnlyOnePathIsRejected()
    {
        // 0: push true; 1: if false go to 4; 2: push null; 3: local 0 = it; 4: push local 0; 5: return it.
        AssertRejected(Module(locals: 1, maxStack: 1,
            I(WebLingoStackOpCode.LoadBoolean, 1), I(WebLingoStackOpCode.JumpIfFalse, 4), I(WebLingoStackOpCode.LoadNull),
            I(WebLingoStackOpCode.StoreLocal, 0), I(WebLingoStackOpCode.LoadLocal, 0), I(WebLingoStackOpCode.ReturnValue)),
            4, "reads local 0");
    }

    [Fact]
    public void AParameterIsWrittenWhenTheFunctionStarts()
    {
        var function = new WebLingoStackFunction("f", 1, 1, [I(WebLingoStackOpCode.LoadLocal, 0), I(WebLingoStackOpCode.ReturnValue)]) { ParameterCount = 1 };
        var module = Module(locals: 0, maxStack: 0, I(WebLingoStackOpCode.Return)) with { Functions = [function] };
        Assert.Equal(WebLingoVerificationResult.Valid, Verify(module));
    }

    [Fact]
    public void CodeThatRunsOffItsEndIsRejected()
    {
        AssertRejected(Module(locals: 0, maxStack: 1, I(WebLingoStackOpCode.LoadNull)), 0, "run off its end");
    }

    [Theory]
    [InlineData(WebLingoStackOpCode.LoadConstant, 9, 0, "constant 9 does not exist")]
    [InlineData(WebLingoStackOpCode.LoadLocal, 1, 0, "local 1 does not exist")]
    [InlineData(WebLingoStackOpCode.Jump, 7, 0, "target instruction 7 does not exist")]
    [InlineData(WebLingoStackOpCode.LoadBoolean, 2, 0, "its value is 2")]
    [InlineData(WebLingoStackOpCode.LoadNull, 0, 1, "an operand it does not use is not 0")]
    [InlineData(WebLingoStackOpCode.CallBuiltin, 0, 2, "'writeLn' takes 1 arguments, not 2")]
    [InlineData(WebLingoStackOpCode.Call, -1, 0, "its argument count, -1")]
    [InlineData(WebLingoStackOpCode.MakeClosure, 0, 0, "function 0 does not exist")]
    [InlineData((WebLingoStackOpCode)200, 0, 0, "the operation 200 is unknown")]
    [InlineData(WebLingoStackOpCode.ImportDynamic, 4, 0, "its flags, 4, are not 0 to 3")]
    public void AnInvalidOperandIsRejected(WebLingoStackOpCode opCode, int a, int b, string error)
    {
        AssertRejected(Module(locals: 1, maxStack: 4, I(opCode, a, b), I(WebLingoStackOpCode.Return)), 0, error);
    }

    [Fact]
    public void MembersAndIndexesHaveTheirStackEffects()
    {
        // "abc".length; "abc"[0]; "abc"[0] = "abc", keeping it; "abc".length("abc").
        var module = new WebLingoStackModule("members", [WebLingoConstant.String("abc"), WebLingoConstant.String("length"), WebLingoConstant.Int64(0)],
            [], new WebLingoStackFunction("main", 0, 3, [
                I(WebLingoStackOpCode.LoadConstant, 0), I(WebLingoStackOpCode.GetMember, 1), I(WebLingoStackOpCode.Pop),
                I(WebLingoStackOpCode.LoadConstant, 0), I(WebLingoStackOpCode.LoadConstant, 2), I(WebLingoStackOpCode.GetIndex), I(WebLingoStackOpCode.Pop),
                I(WebLingoStackOpCode.LoadConstant, 0), I(WebLingoStackOpCode.LoadConstant, 2), I(WebLingoStackOpCode.LoadConstant, 0),
                I(WebLingoStackOpCode.StoreIndex, 1), I(WebLingoStackOpCode.Pop),
                I(WebLingoStackOpCode.LoadConstant, 0), I(WebLingoStackOpCode.LoadConstant, 0), I(WebLingoStackOpCode.CallMethod, 1, 1),
                I(WebLingoStackOpCode.Pop), I(WebLingoStackOpCode.Return)
            ]));
        Assert.Equal(WebLingoVerificationResult.Valid, Verify(module));

        AssertRejected(module with { EntryPoint = module.EntryPoint with { Code = Changed(module, 1, I(WebLingoStackOpCode.GetMember, 2)) } },
            1, "constant 2 is not a name");
        AssertRejected(module with { EntryPoint = module.EntryPoint with { Code = Changed(module, 10, I(WebLingoStackOpCode.StoreIndex, 2)) } },
            10, "not 0 or 1");
        AssertRejected(module with { EntryPoint = module.EntryPoint with { Code = Changed(module, 5, I(WebLingoStackOpCode.GetIndex, 2)) } },
            5, "not 0 or 1");
        // Keeping no value leaves nothing for the Pop after it.
        AssertRejected(module with { EntryPoint = module.EntryPoint with { Code = Changed(module, 10, I(WebLingoStackOpCode.StoreIndex)) } },
            11, "pops 1 values from a stack of 0");
        AssertRejected(module with { EntryPoint = module.EntryPoint with { Code = Changed(module, 14, I(WebLingoStackOpCode.CallMethod, 1, 2)) } },
            14, "pops 3 values from a stack of 2");
    }

    private static WebLingoStackInstruction[] Changed(WebLingoStackModule module, int at, WebLingoStackInstruction instruction)
    {
        var code = module.EntryPoint.Code.ToArray();
        code[at] = instruction;
        return code;
    }

    [Fact]
    public void ForEachWritesItsItemsOnlyOnTheWayIntoTheLoop()
    {
        // Locals: 0 the collection, 1 the position, 2 the item. 0-3: both set; 4: next, or go to 6; 5: go to 4;
        // 6: return local 2, which the way out has not written.
        var module = new WebLingoStackModule("loop", [WebLingoConstant.Int64(1)], [],
            new WebLingoStackFunction("main", 3, 1, [
                I(WebLingoStackOpCode.LoadConstant, 0), I(WebLingoStackOpCode.StoreLocal, 0), I(WebLingoStackOpCode.LoadConstant, 0),
                I(WebLingoStackOpCode.StoreLocal, 1), I(WebLingoStackOpCode.ForEachNext, 6, 0), I(WebLingoStackOpCode.Jump, 4),
                I(WebLingoStackOpCode.LoadLocal, 2), I(WebLingoStackOpCode.ReturnValue)
            ]));
        AssertRejected(module, 6, "reads local 2");
        AssertRejected(module with { EntryPoint = module.EntryPoint with { Code = Changed(module, 4, I(WebLingoStackOpCode.ForEachNextEntry, 6, 0)) } },
            4, "locals, 0 to 3");
        var fine = Changed(module, 6, I(WebLingoStackOpCode.LoadLocal, 1));
        Assert.Equal(WebLingoVerificationResult.Valid, Verify(module with { EntryPoint = module.EntryPoint with { Code = fine } }));
    }

    [Fact]
    public void TooManySlotsAreRejected()
    {
        AssertRejected(Module(locals: 65_000, maxStack: 1_000, I(WebLingoStackOpCode.Return)), -1, "65535 slots");
    }

    [Fact]
    public void RandomCodeNeverThrows()
    {
        var random = new Random(5_5);
        var opCodes = Enum.GetValues<WebLingoStackOpCode>();
        var valid = 0;
        for (var round = 0; round < 20_000; round++)
        {
            var code = new WebLingoStackInstruction[random.Next(1, 12)];
            for (var i = 0; i < code.Length; i++)
            {
                var opCode = random.Next(16) == 0 ? (WebLingoStackOpCode)random.Next(256) : opCodes[random.Next(opCodes.Length)];
                code[i] = new WebLingoStackInstruction(opCode, random.Next(4) == 0 ? random.Next(-2, 12) : random.Next(3),
                    random.Next(4) == 0 ? random.Next(-1, 3) : 0);
            }
            if (random.Next(2) == 0) code[^1] = I(WebLingoStackOpCode.Return);
            var result = Verify(Module(locals: random.Next(3), maxStack: random.Next(4), code));
            Assert.True(result.IsValid || result.Error is not null);
            if (result.IsValid) valid++;
        }
        Assert.InRange(valid, 1, 19_999);
    }

    private static WebLingoStackInstruction I(WebLingoStackOpCode opCode, int a = 0, int b = 0) => new(opCode, a, b);

    private static WebLingoStackModule Module(int locals, int maxStack, params WebLingoStackInstruction[] code) =>
        new("test", [WebLingoConstant.Int64(1), WebLingoConstant.Int64(2)], [new WebLingoImport("writeLn", 1)],
            new WebLingoStackFunction("main", locals, maxStack, code));

    private static WebLingoVerificationResult Verify(WebLingoStackModule module) => new WebLingoStackBytecodeVerifier().Verify(module);

    private static void AssertRejected(WebLingoStackModule module, int instruction, string error)
    {
        var result = Verify(module);
        Assert.False(result.IsValid);
        Assert.Equal(instruction, result.InstructionIndex);
        Assert.Contains(error, result.Error);
    }
}
