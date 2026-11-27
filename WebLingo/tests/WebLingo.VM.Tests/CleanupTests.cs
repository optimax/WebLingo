using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
using WebLingo.StdLib;
using WebLingo.VM;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// A cancelled execution's cleanup, its running frames' (section 9, "A cancelled request's cleanup, parts", part 2): each
/// <c>finally</c> and each disposal around where it was, innermost first, frame by frame, no <c>catch</c> entered; an
/// exception a cleanup throws caught within it, or dropped and logged at <c>warn</c>, the cleanup going on; a cleanup
/// already running finished first; and the grace, past which it is stopped. Each program cancels itself, by the test's
/// <c>cancel()</c>, then loops, so that the VM's next look at its cancellation finds it.
/// </summary>
public sealed class CleanupTests
{
    [Fact]
    public void AFinallyRunsAndNoCatchIsEntered()
    {
        var (output, error, _) = Run("""
            try { cancel(); while (true) { } } catch (e) { writeLn("caught"); } finally { writeLn("finally"); }
            writeLn("after");
            """);
        Assert.Equal("finally\n", output);
        Assert.Equal(WebLingoDiagnosticCodes.Cancelled, error!.Code);
        Assert.Equal("The program was cancelled.", error!.Message);
    }

    [Fact]
    public void CleanupsRunInnermostFirstFrameByFrame()
    {
        var (output, _, _) = Run("""
            function f() { try { try { cancel(); while (true) { } } finally { writeLn("inner"); } } finally { writeLn("f"); } }
            try { f(); } finally { writeLn("main"); }
            """);
        Assert.Equal("inner\nf\nmain\n", output);
    }

    [Fact]
    public void AUsingDisposesItsResource()
    {
        var (output, _, _) = Run("""
            class R { dispose() { writeLn("disposed"); } }
            using (var r = new R()) { cancel(); while (true) { } }
            writeLn("after");
            """);
        Assert.Equal("disposed\n", output);
    }

    [Fact]
    public void ACleanupsExceptionIsCaughtWithinItOrDroppedAndLogged()
    {
        var (output, error, log) = Run("""
            try {
                try { cancel(); while (true) { } }
                finally {
                    try { throw new Error("caught inside"); } catch (e) { writeLn(e.message); }
                    throw new Error("dropped");
                }
            } finally { writeLn("outer"); }
            """);
        Assert.Equal("caught inside\nouter\n", output);
        Assert.Equal(WebLingoDiagnosticCodes.Cancelled, error!.Code);
        var line = Assert.Single(log);
        Assert.Equal(WebLingoLogLevel.Warn, line.Level);
        Assert.Contains("dropped", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACleanupRunningWhenTheCancellationComesFinishesFirst()
    {
        var (output, error, _) = Run("""
            try {
                try { throw new Error("x"); }
                finally { cancel(); var i = 0; while (i < 5000) { i = i + 1; } writeLn("finished"); }
            } finally { writeLn("outer"); }
            """);
        Assert.Equal("finished\nouter\n", output);
        Assert.Equal(WebLingoDiagnosticCodes.Cancelled, error!.Code);
    }

    [Fact]
    public void ADisposalRunningWhenTheCancellationComesRunsItsChainToItsEnd()
    {
        var (output, error, _) = Run("""
            class A { dispose() { writeLn("A"); } }
            class B extends A { dispose() { cancel(); var i = 0; while (i < 5000) { i = i + 1; } writeLn("B"); } }
            using (var b = new B()) { }
            writeLn("after");
            """);
        Assert.Equal("B\nA\n", output);
        Assert.Equal(WebLingoDiagnosticCodes.Cancelled, error!.Code);
    }

    [Fact]
    public void AnAsyncFunctionsFirstRunIsUnwoundToo()
    {
        var (output, _, _) = Run("""
            async function g() { try { cancel(); while (true) { } } finally { writeLn("g"); } }
            try { g(); writeLn("not here"); } finally { writeLn("main"); }
            """);
        Assert.Equal("g\nmain\n", output);
    }

    [Fact]
    public void ACleanupPastItsGraceIsStopped()
    {
        var (output, error, _) = Run("""
            try { cancel(); while (true) { } } finally { writeLn("begun"); while (true) { } }
            """, cleanupInstructions: 100_000);
        Assert.Equal("begun\n", output);
        Assert.Equal(WebLingoDiagnosticCodes.Cancelled, error!.Code);
        Assert.Equal("The program was cancelled, and its cleanup, past its grace, was stopped.", error!.Message);
    }

    [Fact]
    public void WithoutACancellationNothingChanges()
    {
        var (output, error, _) = Run("""
            try { try { throw new Error("x"); } finally { writeLn("finally"); } } catch (e) { writeLn(e.message); }
            """, cancels: false);
        Assert.Equal("finally\nx\n", output);
        Assert.Null(error);
    }

    private static (string Output, WebLingoDiagnostic? Error, List<(WebLingoLogLevel Level, string Text)> Log) Run(string program,
        long cleanupInstructions = 10_000_000, bool cancels = true)
    {
        var signal = new WebLingoCancellationSignal();
        var options = new WebLingoCompilerOptions([new("write", 1), new("writeLn", 1), new("cancel", 0)]);
        var output = new WebLingoCollectedOutput();
        var log = new Log();
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0))
        {
            Services = new WebLingoBuiltinRegistry([.. WebLingoStandardBuiltins.Create(output), new Cancel(signal)]),
            Cancellation = signal,
            CleanupInstructions = cleanupInstructions,
            Log = log
        };
        var compiled = new WebLingoCompiler(options).Compile(program, "main.wlg");
        Assert.Empty(compiled.Diagnostics);
        var result = new WebLingoVirtualMachine().Execute(compiled.Module!, context);
        if (!cancels) Assert.True(result.Error is null, result.Error?.Message);
        return (output.Text, result.Error, log.Lines);
    }

    /// <summary>The test's <c>cancel()</c>: the execution's cancellation, as a host's time limit, its client's going or a reload's drain gives it.</summary>
    private sealed class Cancel(WebLingoCancellationSignal signal) : IWebLingoBuiltinFunction
    {
        public string Name => "cancel";
        public int Arity => 0;

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
        {
            signal.Cancel();
            (result, error) = (WebLingoValue.Null, null);
            return true;
        }
    }

    private sealed class Log : IWebLingoLogWriter
    {
        public List<(WebLingoLogLevel Level, string Text)> Lines { get; } = [];
        public WebLingoLogLevel Level => WebLingoLogLevel.Debug;
        public void Write(WebLingoLogLevel level, string text) => Lines.Add((level, text));
    }
}
