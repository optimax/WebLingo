using WebLingo.Compiler;
using WebLingo.Core;
using WebLingo.Runtime;
using WebLingo.StdLib;
using Xunit;
namespace WebLingo.VM.Tests;

/// <summary>
/// A program's parameter that its host gives as an instance of a native class (specification, section 6.12, a page's
/// <c>param request;</c>), on both VMs: made by the host's built-in, given the class, when the statement runs, once the
/// program's classes are made; the program's other parameters given as before.
/// </summary>
public sealed class HostParameterTests
{
    [Fact]
    public void TheHostsParameterIsItsInstance()
    {
        const string program = "param thing; param n = 2; import { write, writeLn } from \"std:system\"; function f() { return thing.name; } class Late { } "
                               + "writeLn([thing.name, n, f(), thing == f2()]); function f2() { return thing; }";
        foreach (var vm in new[] { false })
        {
            var (diagnostics, output, error) = Run(program, vm);
            Assert.Empty(diagnostics);
            Assert.True(error is null, error?.Message);
            Assert.Equal("[\"from the host\", 2, \"from the host\", true]\n", output);
        }
    }

    [Fact]
    public void AProgramWithOnlyTheHostsParameterTakesNoOthers()
    {
        foreach (var vm in new[] { false })
        {
            var (diagnostics, output, error) = Run("param thing; import { write, writeLn } from \"std:system\"; writeLn(thing.name);", vm);
            Assert.Empty(diagnostics);
            Assert.True(error is null, error?.Message);
            Assert.Equal("from the host\n", output);
        }
    }

    private static (IReadOnlyList<WebLingoDiagnostic> Diagnostics, string Output, WebLingoDiagnostic? Error) Run(string program, bool stack)
    {
        var thing = new WebLingoNativeClass("Thing");
        thing.DeclareProperty("name", (a, heap, out r, out e) =>
        {
            r = WebLingoValue.Null;
            return thing.TryGetHostObject<string>(a[0], heap, out var text, out e) && heap.TryAllocateString(text, out r, out e);
        });
        var members = thing.Builtins("test.Thing");
        var info = new WebLingoNativeClassInfo("Thing", Export("constructor", members[0]), [], [])
        {
            Properties = [new WebLingoNativePropertyInfo("name", Export("name", members[1]), null)]
        };
        var options = new WebLingoCompilerOptions([new WebLingoBuiltinDeclaration("write", 1), new WebLingoBuiltinDeclaration("writeLn", 1)])
        {
            HostParameters = [new WebLingoHostParameter("thing", "test.thing", [info])]
        };
        var output = new WebLingoCollectedOutput();
        var services = new WebLingoBuiltinRegistry(WebLingoStandardBuiltins.Create(output));
        foreach (var member in members) services.Register(member);
        services.Register(new Maker(thing));
        var context = new WebLingoExecutionContext(new WebLingoWorkerId(0)) { Services = services };
        var registerCompiled = new WebLingoCompiler(options).Compile(program, "main.wlg");
        var registerResult = registerCompiled.Module is { } registerModule ? new WebLingoVirtualMachine().Execute(registerModule, context) : null;
        return (registerCompiled.Diagnostics, output.Text, registerResult?.Error);
    }

    private static WebLingoNativeExport Export(string name, IWebLingoBuiltinFunction builtin) => new(name, builtin.Name, builtin.Arity);

    /// <summary>The host's built-in that makes the parameter's instance, given its class, holding the host's text.</summary>
    private sealed class Maker(WebLingoNativeClass thing) : IWebLingoBuiltinFunction
    {
        public string Name => "test.thing";
        public int Arity => 1;

        public bool TryInvoke(ReadOnlySpan<WebLingoValue> arguments, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
            thing.TryMake(arguments[0], heap, out result, out error) && thing.TrySetHostObject(result, "from the host", heap, out error);
    }
}
