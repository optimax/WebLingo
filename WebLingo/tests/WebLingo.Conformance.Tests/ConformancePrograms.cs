using System.Reflection;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// The conformance tests' programs, the source each of their cases starts with, so that a test of every program, such
/// as <see cref="BytecodeFileTests"/>'s, takes a case added there too.
/// </summary>
public sealed class ConformancePrograms
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

    public static TheoryData<string> All
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
            programs.Add("import { write, writeLn } from \"std:console\"; var a = 10;\nvar b = 20;\nvar result = a + b;\nwriteLn(result);\n");
            programs.Add("var x = 9223372036854775807 + 1;");
            return programs;
        }
    }

    [Fact]
    public void EveryProgramTestIsCollected() => Assert.True(((IEnumerable<object[]>)All).Count() > 100);
}
