using System.Reflection;
using System.Runtime.Loader;
using Xunit;
namespace WebLingo.Conformance.Tests;

/// <summary>
/// <c>weblingo</c> loads its VM only to run a program (the owner's request, 16 November 2026): run in a load context of
/// its own, with its own copies of its assembly and of the VM's, <c>run</c> is seen to load the VM, and <c>compile</c>,
/// <c>check</c> and <c>disasm</c> none.
/// </summary>
public sealed class VmLoadingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "weblingo-vms-" + Guid.NewGuid().ToString("N"));

    public VmLoadingTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ABytecodeFileLoadsTheVmOnlyToRun()
    {
        var main = Path.Combine(_folder, "main.wlg");
        File.WriteAllText(main, "import { write, writeLn } from \"std:console\"; writeLn(6 * 7);\n");
        var compiled = Path.ChangeExtension(main, ".wlb");
        Assert.Empty(VmsLoadedBy(0, "", "compile", main));
        Assert.Empty(VmsLoadedBy(0, "", "check", compiled));
        Assert.Empty(VmsLoadedBy(0, null, "disasm", compiled));
        Assert.Equal(["WebLingo.VM"], VmsLoadedBy(0, "42\n", "run", compiled));
    }

    [Fact]
    public void SourceLoadsTheVmOnlyToRun()
    {
        var main = Path.Combine(_folder, "main.wlg");
        File.WriteAllText(Path.Combine(_folder, "lib.wlg"), "export const answer = 42;");
        File.WriteAllText(main, "import { writeLn } from \"std:console\"; var unit = await Module.import(\"./lib.wlg\"); writeLn(unit.answer);\n");
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
        private static readonly string[] Own = ["weblingo", "WebLingo.VM"];

        protected override Assembly? Load(AssemblyName name) =>
            Array.IndexOf(Own, name.Name) >= 0 ? LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, name.Name + ".dll")) : null;
    }
}
