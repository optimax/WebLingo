using WebLingo.Bytecode;
using WebLingo.Compiler;
using WebLingo.Core;
using Xunit;
namespace WebLingo.Compiler.Tests;

public sealed class CodeGeneratorTests
{
    [Fact]
    public void GeneratesTheSpecificationsExample()
    {
        var module = Generate("var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);");
        Assert.Equal(
            """
            function main (3 locals, 4 registers)
            00  LoadConstant r0(a), 10
            01  LoadConstant r1(b), 20
            02  Add r2(result), r0(a), r1(b)
            03  Move r3, r2(result)
            04  CallBuiltin r3, writeLn(r3)
            05  Return

            """.ReplaceLineEndings("\n"),
            WsDisassembler.Format(module));
        Assert.Equal(new[] { new WsImport("writeLn", 1) }, module.Imports);
    }

    [Fact]
    public void AnAssignmentWritesItsLocalDirectly()
    {
        Assert.Equal(
            new[] { "LoadConstant r0(x), 1", "LoadConstant r0(x), 5", "Multiply r0(x), r0(x), r0(x)", "Return" },
            Instructions("var x = 1; x = 5; x = x * x;"));
    }

    [Fact]
    public void ALocalReadIsKeptWhenALaterOperandAssignsIt()
    {
        // x + (x = 5) is the old x plus 5: the old x is copied before the assignment.
        Assert.Equal(
            new[] {
                "LoadConstant r0(x), 1", "Move r2, r0(x)", "LoadConstant r0(x), 5", "Add r1(y), r2, r0(x)", "Return"
            },
            Instructions("var x = 1; var y = x + (x = 5);"));
    }

    [Fact]
    public void ConstantsAreShared()
    {
        var module = Generate("var a = 7; var b = 7 + 8; var c = 8;");
        Assert.Equal(new[] { WsConstant.Int64(7), WsConstant.Int64(8) }, module.Constants);
    }

    [Fact]
    public void TemporariesAreReusedAcrossStatements()
    {
        var module = Generate("var a = 1; writeLn(a * 2 + 3); writeLn(a * 4 + 5);");
        Assert.Equal(4, module.EntryPoint.RegisterCount);
    }

    [Fact]
    public void EveryInstructionHasASourceSpan()
    {
        var module = Generate("var a = 1;\nwriteLn(-a);");
        Assert.Equal(module.EntryPoint.Code.Count, module.EntryPoint.Spans!.Count);
        var negate = module.EntryPoint.Code.ToList().FindIndex(i => i.OpCode == WsOpCode.Negate);
        Assert.Equal(new WsSourceSpan(19, 2), module.EntryPoint.Spans[negate]);
    }

    [Theory]
    [InlineData("var a = 10; var b = 20; var result = a + b; writeLn(result);")]
    [InlineData("var x = 0; x = (x = 1) + (x = 2) * -x; writeLn(x);")]
    [InlineData("var a = 1; var b = a; a = b = a + b; writeLn(a - b / 2);")]
    [InlineData("write(1); var write = 2;")]
    [InlineData("")]
    public void WhatTheCompilerGeneratesVerifies(string source)
    {
        var result = new WsCompiler(BinderTests.WithWrite).Compile(source, "test.ws");
        Assert.Empty(result.Diagnostics);
        Assert.True(new WsBytecodeVerifier().Verify(result.Module!).IsValid);
    }

    [Fact]
    public void ASourceWithErrorsGivesNoModule()
    {
        var result = new WsCompiler(BinderTests.WithWrite).Compile("var x = ;", "test.ws");
        Assert.Null(result.Module);
        Assert.Equal(WsDiagnosticCodes.UnexpectedToken, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void TooManyVariablesForAFunctionIsADiagnostic()
    {
        var source = string.Concat(Enumerable.Range(0, WsFunction.MaxRegisters + 1).Select(i => $"var v{i} = 1;"));
        var result = new WsCompiler(BinderTests.WithWrite).Compile(source, "test.ws");
        Assert.Null(result.Module);
        Assert.Equal(WsDiagnosticCodes.ProgramTooLarge, Assert.Single(result.Diagnostics).Code);
    }

    private static WsCompiledModule Generate(string source)
    {
        var result = new WsCompiler(BinderTests.WithWrite).Compile(source, "test.ws");
        Assert.Empty(result.Diagnostics);
        return result.Module!;
    }

    private static string[] Instructions(string source)
    {
        var module = Generate(source);
        return module.EntryPoint.Code.Select(i => WsDisassembler.Format(module, i)).ToArray();
    }
}
