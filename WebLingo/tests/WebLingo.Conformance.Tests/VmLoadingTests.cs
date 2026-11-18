using System.Reflection;
using System.Runtime.Loader;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>weblingo</c> loads the VM it runs a program on, and no other (the owner's request, 16 November 2026): run in a load
/// context of its own, with its own copies of its assembly and of both VMs', each command is seen to load only the VM
/// assemblies it needs, and <c>compile</c>, <c>check</c> and <c>disasm</c> none.
/// </summary>
public sealed class VmLoadingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-vms-" + Guid.NewGuid().ToString("N"));

    public VmLoadingTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("register", "WebLingo.VM")]
    [InlineData("stack", "WebLingo.VM.Stack")]
    public void AWlbcLoadsOnlyItsVm(string vm, string assembly)
    {
        var main = Path.Combine(_folder, "main.wl");
        File.WriteAllText(main, "import { write, writeLn } from \"std:io\"; writeLn(6 * 7);\n");
        var compiled = Path.Combine(_folder, "main.wlbc");
        Assert.Empty(VmsLoadedBy(0, "", "compile", "--" + vm, main));
        Assert.Empty(VmsLoadedBy(0, "", "check", compiled));
        Assert.Empty(VmsLoadedBy(0, null, "disasm", compiled));
        Assert.Equal([assembly], VmsLoadedBy(0, "42\n", "run", compiled));
    }

    [Fact]
    public void AScriptLoadsOnlyTheRegisterVm()
    {
        var main = Path.Combine(_folder, "main.wl");
        File.WriteAllText(main, "import { write, writeLn } from \"std:io\"; writeLn(6 * 7);\n");
        Assert.Empty(VmsLoadedBy(0, "", "check", main));
        Assert.Equal(["WebLingo.VM"], VmsLoadedBy(0, "42\n", "run", main));
    }

    /// <summary>
    /// The VM assemblies <c>weblingo</c> loads running <paramref name="args"/>, which must exit with <paramref name="exit"/>
    /// and write <paramref name="output"/>, if it is given.
    /// </summary>
    private static string[] VmsLoadedBy(int exit, string? output, params string[] args)
    {
        var context = new Isolated();
        try
        {
            var cli = context.LoadFromAssemblyName(new AssemblyName("weblingo"));
            var run = cli.GetType("WebLingo.Cli.WebLingoCommandLine", throwOnError: true)!
                .GetMethod("Run", [typeof(string[]), typeof(TextWriter), typeof(TextWriter)])!;
            var (written, errors) = (new StringWriter(), new StringWriter());
            Assert.Equal((exit, ""), ((int)run.Invoke(null, [args, written, errors])!, errors.ToString()));
            if (output is not null) Assert.Equal(output, written.ToString());
            return [.. context.Assemblies.Select(a => a.GetName().Name!).Where(name => name.StartsWith("WebLingo.VM", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)];
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>A load context with its own <c>weblingo</c> and VMs, every other assembly the test's own.</summary>
    private sealed class Isolated() : AssemblyLoadContext("weblingo", isCollectible: true)
    {
        private static readonly string[] Own = ["weblingo", "WebLingo.VM", "WebLingo.VM.Stack"];

        protected override Assembly? Load(AssemblyName name) =>
            Array.IndexOf(Own, name.Name) >= 0 ? LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, name.Name + ".dll")) : null;
    }
}
