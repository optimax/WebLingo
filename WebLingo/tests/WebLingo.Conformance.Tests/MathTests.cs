using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:math</c> (the owner's answers, 18 November 2026, "Mathematics"), through <c>weblingo</c>, each program run as a
/// script, on the register VM, and as a <c>.wlbc</c> compiled for the stack VM, which must give the same: its functions,
/// each keeping its argument's kind or giving an integer or a number, as it says; its constants, which a native module
/// exports, and its rest parameters, <c>min(a, …)</c>; and its errors, each message tested.
/// </summary>
public sealed class MathTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-math-" + Guid.NewGuid().ToString("N"));

    private const string Math = "import math from \"std:math\"; import { writeLn } from \"std:system\"; ";

    public MathTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("writeLn([math.PI, math.E, math.PI is Number]);", "[3.141592653589793, 2.718281828459045, true]")]
    [InlineData("writeLn([math.abs(-5), math.abs(-2.5), math.abs(-1.50m), math.abs(7)]);", "[5, 2.5, 1.5m, 7]")]
    [InlineData("writeLn([math.sign(-3), math.sign(0), math.sign(2.5), math.sign(-0.5m), math.sign(NaN)]);", "[-1, 0, 1.0, -1m, NaN]")]
    // Each giving an integer, a number's halfway rounded away from zero (Q5's (a)), an integer itself.
    [InlineData("writeLn([math.floor(2.7), math.floor(-2.7), math.ceiling(2.1), math.ceiling(-2.1), math.truncate(-2.7), math.floor(7)]);",
        "[2, -3, 3, -2, -2, 7]")]
    [InlineData("writeLn([math.round(2.5), math.round(-2.5), math.round(2.4999), math.round(0.5), math.round(2.5m), math.round(-0.5m), math.floor(-0.5m)]);",
        "[3, -3, 2, 1, 3, -1, -1]")]
    [InlineData("writeLn([math.floor(9007199254740992.0), math.round(-9223372036854775808.0), math.floor(2.7) is Integer]);",
        "[9007199254740992, -9223372036854775808, true]")]
    // min and max: the value as it was given, its kind kept, compared as < compares them; NaN where a number is NaN.
    [InlineData("writeLn([math.min(3, 1, 2), math.max(3, 1.5, 2), math.max(1.5m, 2), math.min(5), math.max(1, NaN, 2), math.min(...[4, 2, 8])]);",
        "[1, 3, 2, 5, NaN, 2]")]
    [InlineData("writeLn([math.max(1, 1.0), math.min(1.0, 1), math.max(-0.0, 0.0)]);", "[1, 1.0, -0.0]")]
    [InlineData("writeLn([math.sqrt(2), math.sqrt(-1.0), math.pow(2, 10), math.pow(2, 0.5) == math.sqrt(2), math.exp(0)]);",
        "[1.4142135623730951, NaN, 1024.0, true, 1.0]")]
    [InlineData("writeLn([math.log(math.E), math.log10(1000), math.log2(8), math.log(0), math.log(-1)]);", "[1.0, 3.0, 3.0, -Infinity, NaN]")]
    [InlineData("writeLn([math.sin(0), math.cos(0), math.tan(0), math.asin(1) * 2 == math.PI, math.acos(1), math.atan(0), math.atan2(1, 1) * 4]);",
        "[0.0, 1.0, 0.0, true, 0.0, 0.0, 3.141592653589793]")]
    // A held value as its kind's, given as that value (section 3.7, "No sealed classes").
    [InlineData("class Celsius extends Number {} class Count extends Integer {} writeLn([math.floor(new Celsius(21.5)), math.max(new Count(2), 1), math.sqrt(new Count(4))]);",
        "[21, 2, 2.0]")]
    public void ResultsAreTheSpecifiedOnes(string program, string printed)
    {
        Assert.Equal((printed + "\n", ""), Run(Math + program));
    }

    [Theory]
    [InlineData("math.floor(1e300);", "WL3002 floor(1E+300) does not fit in a 64-bit integer.")]
    [InlineData("math.ceiling(Infinity);", "WL3002 ceiling(Infinity) does not fit in a 64-bit integer.")]
    [InlineData("math.round(-Infinity);", "WL3002 round(-Infinity) does not fit in a 64-bit integer.")]
    [InlineData("math.truncate(9223372036854775808.0);", "WL3002 truncate(9.223372036854776E+18) does not fit in a 64-bit integer.")]
    [InlineData("math.floor(79228162514264337593543950335m);", "WL3002 floor(79228162514264337593543950335m) does not fit in a 64-bit integer.")]
    [InlineData("math.abs(-9223372036854775807 - 1);", "WL3002 abs(-9223372036854775808) does not fit in a 64-bit integer.")]
    [InlineData("math.round(NaN);", "WL3001 The argument of 'round' must be an integer, a decimal or a number that is not NaN, not NaN.")]
    [InlineData("math.abs(\"5\");", "WL3001 The argument of 'abs' must be an integer, a number or a decimal, not a string.")]
    [InlineData("math.sign(null);", "WL3001 The argument of 'sign' must be an integer, a number or a decimal, not null.")]
    [InlineData("math.floor(true);", "WL3001 The argument of 'floor' must be an integer, a number or a decimal, not the Boolean true.")]
    [InlineData("math.sqrt(\"4\");", "WL3001 The argument of 'sqrt' must be an integer or a number, not a string.")]
    [InlineData("math.log(2m);", "WL3001 The argument of 'log' must be an integer or a number, not the decimal 2m.")]
    [InlineData("math.pow(2, 1.5m);", "WL3001 The second argument of 'pow', the exponent, must be an integer or a number, not the decimal 1.5m.")]
    [InlineData("math.atan2(\"y\", 1);", "WL3001 The first argument of 'atan2', y, must be an integer or a number, not a string.")]
    [InlineData("math.max(1, \"2\");", "WL3001 The second argument of 'max', a value, must be an integer, a number or a decimal, not a string.")]
    [InlineData("math.min([1, 2]);", "WL3001 The argument of 'min' must be an integer, a number or a decimal, not an array.")]
    [InlineData("math.max(1.5, 2m);", "WL3001 '<' compares two numbers, two decimals, two strings or two dates, not the number 1.5 and the decimal 2m.")]
    public void ErrorsAreTheSpecifiedOnes(string call, string message)
    {
        Assert.Equal((message + "\n", ""), Run(Math + "try { " + call + " } catch (e) { writeLn(e.code + \" \" + e.message); }"));
    }

    [Theory]
    [InlineData("math.min();", "error WL2006: 'min' takes at least 1 argument, not 0.")]
    [InlineData("math.sqrt(1, 2);", "error WL2006: 'std:math.sqrt' takes 1 argument, not 2.")]
    public void ACallGivesTheArgumentsItTakes(string call, string message)
    {
        var (output, error) = Run(Math + call);
        Assert.Equal("", output);
        Assert.Contains(message, error, StringComparison.Ordinal);
    }

    [Fact]
    public void AConstantIsTheModulesToChange()
    {
        Assert.Equal(("'PI' of the module std:math cannot be assigned: an export is its module's to change.\n", ""),
            Run(Math + "try { math.PI = 3; } catch (TypeError e) { writeLn(e.message); }"));
    }

    /// <summary>
    /// The program run as a script, on the register VM, then compiled for the stack VM and run from its <c>.wlbc</c>: what
    /// both write, which must be the same, and their errors.
    /// </summary>
    private (string Output, string Error) Run(string program)
    {
        var path = Path.Combine(_folder, "program.wlg");
        File.WriteAllText(path, program);
        var register = Weblingo("run", path);
        Assert.Equal(WebLingoCommandLine.Success, Weblingo("compile", "--stack", path).Exit);
        var stack = Weblingo("run", Path.ChangeExtension(path, ".wlbc"));
        Assert.Equal((register.Output, register.Error), (stack.Output, stack.Error));
        return (register.Output, register.Error);
    }

    private static (int Exit, string Output, string Error) Weblingo(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WebLingoCommandLine.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
