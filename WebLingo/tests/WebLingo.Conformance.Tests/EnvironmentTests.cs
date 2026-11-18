using WebLingo.Cli;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>std:env</c> (specification, section 7.15) through <c>weblingo</c>, whose scripts read all of the machine's
/// environment and set its variables, each program run as a script, on the register VM, and as a <c>.wlbc</c> compiled for
/// the stack VM, which must give the same: the machine's name, a variable, the map of them, a variable set and removed,
/// and the errors. Each test's variables have names of their own, the process's environment being every test's.
/// </summary>
public sealed class EnvironmentTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-env-" + Guid.NewGuid().ToString("N"));
    private readonly string _name = "WL_ENV_TEST_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    private const string Import = "import { Environment } from \"std:env\"; import { writeLn } from \"std:io\"; ";

    public EnvironmentTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, null);
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void AScriptReadsTheMachinesNameAndItsVariables()
    {
        Environment.SetEnvironmentVariable(_name, "a value");
        var program = Import + $"writeLn(Environment.machineName); writeLn(Environment.variable(\"{_name}\")); "
                      + $"writeLn(Environment.variables[\"{_name}\"]); writeLn(Environment.variable(\"{_name}_NOT_SET\")); "
                      + "writeLn([Environment.variables is Map, Environment.variable(\"\"), Environment.variable(\"A=B\")]);";
        Assert.Equal(($"{Environment.MachineName}\na value\na value\nnull\n[true, null, null]\n", ""), Run(program));
    }

    [Fact]
    public void AScriptSetsAVariableForTheProcessAlone()
    {
        // Set, read back, and removed, given null; and set again, which the process then has.
        var program = Import + $"Environment.setVariable(\"{_name}\", \"set\"); writeLn(Environment.variable(\"{_name}\")); "
                      + $"Environment.setVariable(\"{_name}\", null); writeLn([Environment.variable(\"{_name}\"), Environment.variables.has(\"{_name}\")]); "
                      + $"writeLn(Environment.setVariable(\"{_name}\", \"kept\"));";
        Assert.Equal(("set\n[null, false]\nnull\n", ""), Run(program));
        Assert.Equal("kept", Environment.GetEnvironmentVariable(_name));
    }

    [Theory]
    [InlineData("Environment.variable(5);", "'variable' takes a variable's name as a string, not the integer 5.")]
    [InlineData("Environment.setVariable(null, \"x\");", "'setVariable' takes a variable's name as a string, not null.")]
    [InlineData("Environment.setVariable(\"WL_ENV_TEST\", 5);", "'setVariable' sets a variable to a string, or removes it, given null, not the integer 5.")]
    [InlineData("Environment.setVariable(\"A=B\", \"x\");",
        "'setVariable' cannot set 'A=B': a variable's name is not empty, and holds neither '=' nor U+0000.")]
    [InlineData("Environment.setVariable(\"\", \"x\");",
        "'setVariable' cannot set '': a variable's name is not empty, and holds neither '=' nor U+0000.")]
    [InlineData("Environment.setVariable(\"WL_ENV_TEST\", \"a\\0b\");", "'setVariable' cannot set 'WL_ENV_TEST' to a value holding U+0000.")]
    [InlineData("Environment.machineName = \"x\";", "'machineName' of the class Environment cannot be assigned: its property has no setter.")]
    public void WrongArgumentsAreTypeErrors(string call, string message)
    {
        Assert.Equal(("WL3001 " + message + "\n", ""), Run(Import + "try { " + call + " } catch (TypeError e) { writeLn(e.code + \" \" + e.message); }"));
    }

    /// <summary>
    /// The program run as a script, on the register VM, then compiled for the stack VM and run from its <c>.wlbc</c>: what
    /// both write, which must be the same, and their errors.
    /// </summary>
    private (string Output, string Error) Run(string program)
    {
        var path = Path.Combine(_folder, "program.wl");
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
