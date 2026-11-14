using System.Reflection;
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
/// The stack VM (specification, section 5.6, part 3) gives every program of the conformance tests what the register VM
/// gives: the same diagnostics, the same output, and the same runtime error, with its code, message and source span.
/// The programs are the conformance tests' own, so that a case added there is checked here too.
/// </summary>
public sealed class StackVmTests
{
    /// <summary>The conformance tests whose cases start with a program's source.</summary>
    private static readonly string[] ProgramTests =
    [
        nameof(ConformanceTests.ValidProgramsCompileAndRun),
        nameof(ConformanceTests.CompileErrorsAreReportedAndNothingRuns),
        nameof(ConformanceTests.ResultsAreTheSpecifiedOnes),
        nameof(ConformanceTests.BooleansAndNumbersDoNotMix),
        nameof(ConformanceTests.CallsAreCheckedAtRunTime),
        nameof(ConformanceTests.MembersAndIndexesAreCheckedAtRunTime),
        nameof(ConformanceTests.AsynchronyErrorsAreReported),
    ];

    public static TheoryData<string> ConformancePrograms
    {
        get
        {
            var programs = new TheoryData<string>();
            foreach (var name in ProgramTests)
            {
                var method = typeof(ConformanceTests).GetMethod(name)!;
                foreach (var attribute in method.GetCustomAttributes<InlineDataAttribute>())
                {
                    foreach (var row in attribute.GetData(method)) programs.Add((string)row[0]);
                }
            }
            programs.Add("var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);\n");
            programs.Add("var x = 9223372036854775807 + 1;");
            return programs;
        }
    }

    [Fact]
    public void EveryProgramTestIsCollected() => Assert.True(((IEnumerable<object[]>)ConformancePrograms).Count() > 100);

    [Theory]
    [MemberData(nameof(ConformancePrograms))]
    public void TheStackVmGivesWhatTheRegisterVmGives(string source)
    {
        Assert.Equal(RunOnRegisterVm(source, long.MaxValue), RunOnStackVm(source, long.MaxValue));
    }

    [Fact]
    public void RandomSourceGivesTheSameOnBothVms()
    {
        string[] fragments =
        [
            "var", "x", "y", "f", "writeLn", "write", "=", "+", "-", "*", "/", "(", ")", ";", ",", "{", "}", "0", "1", "2.5",
            "NaN", "\"s\"", "true", "null", "==", "<", "&&", "||", "!", "if", "else", "while", "for", "break", "continue",
            "function", "return", "class", "new", "this", "C", "?.", "9223372036854775807", "[", "]", ".", "length", "size", "\"ab\"",
        ];
        var random = new Random(5_6);
        var compared = 0;
        for (var round = 0; round < 20_000; round++)
        {
            var words = Enumerable.Range(0, random.Next(1, 12)).Select(_ => fragments[random.Next(fragments.Length)] + " ");
            var source = string.Concat(words);
            var register = RunOnRegisterVm(source, 100_000);
            var stack = RunOnStackVm(source, 100_000);
            // The two VMs count different instructions, so a program that runs out of its budget stops at different places.
            if (register.Error.StartsWith(WsDiagnosticCodes.BudgetExceeded, StringComparison.Ordinal)
                || stack.Error.StartsWith(WsDiagnosticCodes.BudgetExceeded, StringComparison.Ordinal))
            {
                continue;
            }
            Assert.Equal(register, stack);
            if (register.Diagnostics.Length == 0) compared++;
        }
        Assert.InRange(compared, 1, 20_000);
    }

    [Fact]
    public void MalformedStackBytecodeFailsVerificationNotACrash()
    {
        var module = new WsStackCompiler(Options)
            .Compile("var a = 10; var b = a * 2 - 3; function f(n) { return n + b; } var c = 0; c = -f(b) / a; writeLn(c);",
                "base.ws").Module!;
        var random = new Random(8_6);
        var opCodes = Enum.GetValues<WsStackOpCode>();
        var vm = new WsStackVirtualMachine();
        var rejected = 0;
        for (var round = 0; round < 5_000; round++)
        {
            var code = module.EntryPoint.Code.ToArray();
            for (var mutation = random.Next(1, 4); mutation > 0; mutation--)
            {
                var i = random.Next(code.Length);
                code[i] = random.Next(3) switch
                {
                    0 => code[i] with
                    {
                        OpCode = random.Next(8) == 0 ? (WsStackOpCode)random.Next(256) : opCodes[random.Next(opCodes.Length)]
                    },
                    1 => code[i] with { A = random.Next(-2, 8) },
                    _ => code[i] with { B = random.Next(-2, 3) },
                };
            }
            var mutated = module with { EntryPoint = module.EntryPoint with { Code = code } };
            var context = new WsExecutionContext(new WsWorkerId(0))
            {
                Services = new WsBuiltinRegistry(WsStandardBuiltins.Create(new WsCollectedOutput())),
                InstructionBudget = 10_000,
            };
            var result = vm.Execute(mutated, context);
            Assert.True(result.Succeeded || result.Error is not null);
            if (result.Error?.Code == WsDiagnosticCodes.BytecodeVerificationFailure) rejected++;
        }
        Assert.InRange(rejected, 1, 5_000);
    }

    private static readonly WsCompilerOptions Options =
        new([new WsBuiltinDeclaration("write", 1), new WsBuiltinDeclaration("writeLn", 1), new WsBuiltinDeclaration("delay", 1)]);

    /// <summary>What a program gives: its diagnostics, its output, and its runtime error, each as text.</summary>
    private sealed record Outcome(string[] Diagnostics, string Output, string Error)
    {
        public bool Equals(Outcome? other) =>
            other is not null && Diagnostics.SequenceEqual(other.Diagnostics) && Output == other.Output && Error == other.Error;

        public override int GetHashCode() => HashCode.Combine(Output, Error);

        public override string ToString() => $"[{string.Join(" | ", Diagnostics)}] output \"{Output}\" error \"{Error}\"";
    }

    private static Outcome RunOnRegisterVm(string source, long budget)
    {
        var compiled = new WsCompiler(Options).Compile(source, "program.ws");
        var output = new WsCollectedOutput();
        var result = compiled.Module is { } module ? new WsVirtualMachine().Execute(module, Context(output, budget)) : null;
        return OutcomeOf(compiled.Diagnostics, output, result);
    }

    private static Outcome RunOnStackVm(string source, long budget)
    {
        var compiled = new WsStackCompiler(Options).Compile(source, "program.ws");
        var output = new WsCollectedOutput();
        var result = compiled.Module is { } module ? new WsStackVirtualMachine().Execute(module, Context(output, budget)) : null;
        return OutcomeOf(compiled.Diagnostics, output, result);
    }

    private static WsExecutionContext Context(IWsTextOutput output, long budget) =>
        new(new WsWorkerId(0))
        {
            Services = new WsBuiltinRegistry(WsStandardBuiltins.Create(output)),
            InstructionBudget = budget,
        };

    private static Outcome OutcomeOf(IReadOnlyList<WsDiagnostic> diagnostics, WsCollectedOutput output, WsExecutionResult? result) =>
        new([.. diagnostics.Select(Describe)], output.Text, result?.Error is { } error ? Describe(error) : "");

    private static string Describe(WsDiagnostic diagnostic) =>
        $"{diagnostic.Code} at {diagnostic.Span.Start}+{diagnostic.Span.Length}: {diagnostic.Message}";
}
