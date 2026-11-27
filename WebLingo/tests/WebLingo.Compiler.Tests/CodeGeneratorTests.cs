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
        var module = Generate("var a = 10;\nvar b = 20;\nvar result = a + b;\nprintLn(result);");
        Assert.Equal(
            """
            function main of test.wlg (3 locals, 4 registers)
            00  LoadConstant r0(a), 10
            01  LoadConstant r1(b), 20
            02  Add r2(result), r0(a), r1(b)
            03  Move r3, r2(result)
            04  CallBuiltin r3, printLn(r3)
            05  Return

            """.ReplaceLineEndings("\n"),
            WebLingoDisassembler.Format(module));
        Assert.Equal(new[] { new WebLingoImport("printLn", 1) }, module.Imports);
    }

    /// <summary>
    /// Each handler says what it is (section 9, "A cancelled request's cleanup, parts", part 1): a <c>catch</c>'s, a
    /// <c>finally</c>'s or a <c>using</c>'s, a cleanup's code ending after its <c>Rethrow</c>, and a <c>catch</c> inside a
    /// <c>finally</c> left a catch's.
    /// </summary>
    [Fact]
    public void EachHandlerSaysWhetherItIsACatchsOrACleanups()
    {
        var module = Generate("""
            class R { dispose() { } }
            try { printLn(1); } catch (e) { printLn(2); }
            try { printLn(3); } finally { try { printLn(4); } catch (e) { printLn(5); } }
            using (var r = new R()) { printLn(6); }
            """);
        var code = module.EntryPoint.Code;
        var handlers = module.EntryPoint.Handlers;
        Assert.Equal(
            [WebLingoHandlerKind.Catch, WebLingoHandlerKind.Catch, WebLingoHandlerKind.Finally, WebLingoHandlerKind.Catch,
             WebLingoHandlerKind.Using],
            handlers.Select(handler => handler.Kind).ToArray());
        foreach (var handler in handlers.Where(handler => handler.IsCleanup))
        {
            Assert.True(handler.CleanupEnd > handler.Target);
            Assert.Equal(new WebLingoInstruction(WebLingoOpCode.Rethrow, handler.Value, handler.Origin), code[handler.CleanupEnd - 1]);
        }
        Assert.All(handlers.Where(handler => !handler.IsCleanup), handler => Assert.Equal(0, handler.CleanupEnd));
        // The finally's body is emitted twice, on the way out and in its handler, and so its inner catch: the second within
        // the finally's cleanup, as a cancellation will tell it, the first before it.
        var (onTheWayOut, cleanup, inner) = (handlers[1], handlers[2], handlers[3]);
        Assert.InRange(inner.Start, cleanup.Target, cleanup.CleanupEnd - 1);
        Assert.True(onTheWayOut.Start < cleanup.Target);
        Assert.Contains(", finally to ", WebLingoDisassembler.Format(module), StringComparison.Ordinal);
        Assert.Contains(", using to ", WebLingoDisassembler.Format(module), StringComparison.Ordinal);
    }

    /// <summary>
    /// Each cleanup written inline on its way out with no exception is listed (section 9, "A cancelled request's cleanup,
    /// parts", and "A Cancellation in a Cleanup on Its Normal Way Out"): a <c>finally</c>'s body after its <c>try</c>'s, a
    /// <c>using</c>'s disposal after its body, and each before a <c>break</c> out of them.
    /// </summary>
    [Fact]
    public void EachCleanupOnItsWayOutIsListed()
    {
        var module = Generate("""
            class R { dispose() { } }
            try { printLn(1); } finally { printLn(2); }
            using (var r = new R()) { printLn(3); }
            while (true) { try { break; } finally { printLn(4); } }
            """);
        var code = module.EntryPoint.Code;
        var waysOut = module.EntryPoint.WaysOut;
        // The try's, the using's, the break's, and the loop's try's after its body, which the break leaves unreached.
        Assert.Equal(4, waysOut.Count);
        Assert.All(waysOut, wayOut => Assert.True(wayOut.Start < wayOut.End));
        Assert.Contains(waysOut, wayOut => code.Skip(wayOut.Start).Take(wayOut.End - wayOut.Start).Any(i => i.OpCode == WebLingoOpCode.Dispose));
        Assert.Contains("on its way out", WebLingoDisassembler.Format(module), StringComparison.Ordinal);
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
        Assert.Equal(new[] { WebLingoConstant.Int64(7), WebLingoConstant.Int64(8) }, module.Constants);
    }

    [Fact]
    public void TemporariesAreReusedAcrossStatements()
    {
        // a, and a temporary each statement reuses, its constants read from the module's, and the call's argument.
        var module = Generate("var a = 1; printLn(a * 2 + 3); printLn(a * 4 + 5);");
        Assert.Equal(3, module.EntryPoint.RegisterCount);
    }

    [Fact]
    public void AConstantRightOperandIsReadFromTheConstants()
    {
        // Each operation that has a constant form takes its constant from the module's constants, without a LoadConstant
        // (the specification, section 5.6, "Where the time goes"); a constant on the left, and another operation, as before.
        Assert.Equal(["LoadConstant r0(x), 5", "AddConstant r1, r0(x), 1", "CallBuiltin r1, printLn(r1)", "Return"],
            Instructions("var x = 5; printLn(x + 1);"));
        var all = Instructions("var x = 5; var s = \"a\"; var d = 1.5m; var ok = [x - 1, x * 2, x % 3, x == 4, x != 4, x < 4, x <= 4, x > 4, "
            + "x >= 4, s < \"b\", d + 2.5m, x + 0.5, 1 + x, x / 2];");
        foreach (var op in new[] { "SubtractConstant", "MultiplyConstant", "RemainderConstant", "EqualConstant", "NotEqualConstant",
                     "LessConstant", "LessOrEqualConstant", "GreaterConstant", "GreaterOrEqualConstant" })
            Assert.Contains(all, line => line.StartsWith(op + " ", StringComparison.Ordinal));
        Assert.Contains(all, line => line.StartsWith("LessConstant r", StringComparison.Ordinal) && line.EndsWith(", r1(s), \"b\"", StringComparison.Ordinal));
        Assert.Contains(all, line => line.StartsWith("AddConstant r", StringComparison.Ordinal) && line.EndsWith(", r2(d), 2.5m", StringComparison.Ordinal));
        Assert.Contains(all, line => line.StartsWith("AddConstant r", StringComparison.Ordinal) && line.EndsWith(", r0(x), 0.5d", StringComparison.Ordinal));
        Assert.Contains(all, line => line.StartsWith("Divide ", StringComparison.Ordinal));
        Assert.Contains(all, line => line.StartsWith("Add ", StringComparison.Ordinal));
    }

    [Fact]
    public void ALoopIsTestedAtItsBottomAndAnIncrementNotUsedCopiesNothing()
    {
        // A jump to the test, then the body and the step, then the test, which jumps back to the body; i++ as a step, and as
        // a statement, copying nothing, as ++i (section 5.6).
        var code = Instructions("for (var i = 0; i < 3; i++) { printLn(i); } var j = 0; j++;");
        Assert.Equal("Jump 5", code[1]);
        Assert.Equal("AddConstant r0(i), r0(i), 1", code[4]);
        Assert.StartsWith("LessConstant r", code[5]);
        Assert.EndsWith(", r0(i), 3", code[5]);
        Assert.StartsWith("JumpIfTrue r", code[6]);
        Assert.EndsWith(", 02", code[6]);
        Assert.DoesNotContain(code, line => line.Contains("temporary", StringComparison.Ordinal));
        Assert.Equal("AddConstant r1(j), r1(j), 1", code[^2]);
    }

    [Fact]
    public void EveryInstructionHasASourceSpan()
    {
        var module = Generate("var a = 1;\nprintLn(-a);");
        Assert.Equal(module.EntryPoint.Code.Count, module.EntryPoint.Spans!.Count);
        var negate = module.EntryPoint.Code.ToList().FindIndex(i => i.OpCode == WebLingoOpCode.Negate);
        Assert.Equal(new WebLingoSourceSpan(19, 2), module.EntryPoint.Spans[negate]);
    }

    [Theory]
    [InlineData("var a = 10; var b = 20; var result = a + b; printLn(result);")]
    [InlineData("var x = 0; x = (x = 1) + (x = 2) * -x; printLn(x);")]
    [InlineData("var a = 1; var b = a; a = b = a + b; printLn(a - b / 2);")]
    [InlineData("print(1); var print = 2;")]
    [InlineData("")]
    public void WhatTheCompilerGeneratesVerifies(string source)
    {
        var result = new WebLingoCompiler(BinderTests.WithWrite).Compile(source, "test.wlg");
        Assert.Empty(result.Diagnostics);
        Assert.True(new WebLingoBytecodeVerifier().Verify(result.Module!).IsValid);
    }

    [Fact]
    public void ASourceWithErrorsGivesNoModule()
    {
        var result = new WebLingoCompiler(BinderTests.WithWrite).Compile("var x = ;", "test.wlg");
        Assert.Null(result.Module);
        Assert.Equal(WebLingoDiagnosticCodes.UnexpectedToken, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void TooManyVariablesForAFunctionIsADiagnostic()
    {
        var source = string.Concat(Enumerable.Range(0, WebLingoFunction.MaxRegisters + 1).Select(i => $"var v{i} = 1;"));
        var result = new WebLingoCompiler(BinderTests.WithWrite).Compile(source, "test.wlg");
        Assert.Null(result.Module);
        Assert.Equal(WebLingoDiagnosticCodes.ProgramTooLarge, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void AnAsyncFunctionAwaitsAndATopLevelThatAwaitsIsAsync()
    {
        var module = Generate("async function f(x) { return await x; } printLn(f(1));");
        Assert.False(module.EntryPoint.IsAsync);
        var f = Assert.Single(module.Functions);
        Assert.True(f.IsAsync);
        Assert.Equal(new[] { WebLingoOpCode.Await, WebLingoOpCode.ReturnValue, WebLingoOpCode.Return }, f.Code.Select(i => i.OpCode).ToArray());
        Assert.Equal((1, 0), (f.Code[0].A, f.Code[0].B));
        Assert.True(Generate("await 1;").EntryPoint.IsAsync);
        Assert.Contains("Await r0, r0", Instructions("await 1;"));
    }

    private static WebLingoCompiledModule Generate(string source)
    {
        var result = new WebLingoCompiler(BinderTests.WithWrite).Compile(source, "test.wlg");
        Assert.Empty(result.Diagnostics);
        return result.Module!;
    }

    private static string[] Instructions(string source)
    {
        var module = Generate(source);
        return module.EntryPoint.Code.Select(i => WebLingoDisassembler.Format(module, i)).ToArray();
    }
}
