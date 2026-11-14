using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using Xunit;
namespace WebLingo.Compiler.Tests;

/// <summary>The stack code generator (specification, section 5.6, part 3), from the same lowered IR as the register one.</summary>
public sealed class StackCodeGeneratorTests
{
    [Fact]
    public void GeneratesTheSpecificationsExample()
    {
        var module = Generate("var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);");
        Assert.Equal(
            [
                I(WsStackOpCode.LoadConstant, 0), I(WsStackOpCode.StoreLocal, 0),
                I(WsStackOpCode.LoadConstant, 1), I(WsStackOpCode.StoreLocal, 1),
                I(WsStackOpCode.LoadLocal, 0), I(WsStackOpCode.LoadLocal, 1), I(WsStackOpCode.Add), I(WsStackOpCode.StoreLocal, 2),
                I(WsStackOpCode.LoadLocal, 2), I(WsStackOpCode.CallBuiltin, 0, 1), I(WsStackOpCode.Pop),
                I(WsStackOpCode.Return),
            ],
            module.EntryPoint.Code);
        Assert.Equal((3, 2), (module.EntryPoint.LocalCount, module.EntryPoint.MaxStack));
        Assert.Equal([WsConstant.Int64(10), WsConstant.Int64(20)], module.Constants);
    }

    [Fact]
    public void AnAssignmentWhoseValueIsUnusedKeepsNoCopy()
    {
        var module = Generate("var x = 1; x = x + 1;");
        Assert.Equal(
            [
                I(WsStackOpCode.LoadConstant, 0), I(WsStackOpCode.StoreLocal, 0),
                I(WsStackOpCode.LoadLocal, 0), I(WsStackOpCode.LoadConstant, 0), I(WsStackOpCode.Add), I(WsStackOpCode.StoreLocal, 0),
                I(WsStackOpCode.Return),
            ],
            module.EntryPoint.Code);
    }

    [Fact]
    public void AnAssignmentWhoseValueIsUsedKeepsACopy()
    {
        var module = Generate("var x = 1; var y = x = 2;");
        Assert.Equal(
            [
                I(WsStackOpCode.LoadConstant, 0), I(WsStackOpCode.StoreLocal, 0),
                I(WsStackOpCode.LoadConstant, 1), I(WsStackOpCode.Duplicate), I(WsStackOpCode.StoreLocal, 0),
                I(WsStackOpCode.StoreLocal, 1),
                I(WsStackOpCode.Return),
            ],
            module.EntryPoint.Code);
    }

    [Fact]
    public void AndKeepsItsLeftOperandWhenItDecides()
    {
        // false && x: the left operand, copied, decides; the copy is the result. Otherwise the right one is.
        var module = Generate("var x = true; var y = false && x;");
        Assert.Equal(
            [
                I(WsStackOpCode.LoadBoolean, 1), I(WsStackOpCode.StoreLocal, 0),
                I(WsStackOpCode.LoadBoolean, 0), I(WsStackOpCode.Duplicate), I(WsStackOpCode.JumpIfFalse, 8),
                I(WsStackOpCode.Pop), I(WsStackOpCode.LoadLocal, 0), I(WsStackOpCode.CheckBoolean),
                I(WsStackOpCode.StoreLocal, 1),
                I(WsStackOpCode.Return),
            ],
            module.EntryPoint.Code);
    }

    [Fact]
    public void AFunctionsArgumentsGoOnTheStackAboveItsCallee()
    {
        var module = Generate("function add(a, b) { return a + b; } writeLn(add(1, 2));");
        var add = Assert.Single(module.Functions);
        Assert.Equal((2, 2, 2), (add.ParameterCount, add.LocalCount, add.MaxStack));
        Assert.Contains(I(WsStackOpCode.Call, 2), module.EntryPoint.Code);
        Assert.Equal(3, module.EntryPoint.MaxStack); // The callee and its two arguments.
    }

    [Fact]
    public void EveryLoopKeepsTheStackBalanced()
    {
        var compiled = new WsStackCompiler(Options).Compile(
            "var s = 0; for (var i = 0; i < 10; i = i + 1) { if (i == 5) continue; if (s > 100) break; s = s + i; } " +
            "var j = 0; do { j = j + 1; } while (j < 3 && s != 0); while (false) {} writeLn(s);", "loops.ws");
        Assert.Empty(compiled.Diagnostics); // The compiler verifies what it generates.
        Assert.NotNull(compiled.Module);
    }

    [Fact]
    public void ItsDiagnosticsAreTheRegisterCompilers()
    {
        const string source = "var x = 1; var x = 2; writeLn(y);";
        var register = new WsCompiler(Options).Compile(source, "bad.ws");
        var stack = new WsStackCompiler(Options).Compile(source, "bad.ws");
        Assert.Null(stack.Module);
        Assert.Equal(register.Diagnostics, stack.Diagnostics);
    }

    private static readonly WsCompilerOptions Options = new([new WsBuiltinDeclaration("writeLn", 1)]);

    private static WsStackInstruction I(WsStackOpCode opCode, int a = 0, int b = 0) => new(opCode, a, b);

    [Fact]
    public void AnAsyncFunctionAwaitsAndATopLevelThatAwaitsIsAsync()
    {
        var module = Generate("async function f(x) { return await x; } writeLn(f(1));");
        Assert.False(module.EntryPoint.IsAsync);
        var f = Assert.Single(module.Functions);
        Assert.True(f.IsAsync);
        Assert.Equal(new[] { WsStackOpCode.LoadLocal, WsStackOpCode.Await, WsStackOpCode.ReturnValue, WsStackOpCode.Return },
            f.Code.Select(i => i.OpCode).ToArray());
        Assert.True(Generate("await 1;").EntryPoint.IsAsync);
    }

    private static WsStackModule Generate(string source)
    {
        var compiled = new WsStackCompiler(Options).Compile(source, "test.ws");
        Assert.Empty(compiled.Diagnostics);
        return compiled.Module!;
    }
}
