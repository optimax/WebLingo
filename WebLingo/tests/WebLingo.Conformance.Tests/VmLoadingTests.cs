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
    [InlineData("register", "WebLingo.VM", ".wlbr")]
    [InlineData("stack", "WebLingo.VM.Stack", ".wlbs")]
    public void ABytecodeFileLoadsOnlyItsVm(string vm, string assembly, string extension)
    {
        var main = Path.Combine(_folder, "main.wlg");
        File.WriteAllText(main, "import { write, writeLn } from \"std:system\"; writeLn(6 * 7);\n");
        var compiled = Path.ChangeExtension(main, extension);
        Assert.Empty(VmsLoadedBy(0, "", "compile", "--" + vm, main));
        Assert.Empty(VmsLoadedBy(0, "", "check", compiled));
        Assert.Empty(VmsLoadedBy(0, null, "disasm", compiled));
        Assert.Equal([assembly], VmsLoadedBy(0, "42\n", "run", compiled));
    }

    [Theory]
    [InlineData(null, "WebLingo.VM")]
    [InlineData("register", "WebLingo.VM")]
    [InlineData("stack", "WebLingo.VM.Stack")]
    public void SourceLoadsOnlyItsConfiguredVm(string? vm, string assembly)
    {
        var main = Path.Combine(_folder, "main.wlg");
        File.WriteAllText(Path.Combine(_folder, "lib.wlg"), "export const answer = 42;");
        File.WriteAllText(main, "var unit = await Module.import(\"./lib.wlg\"); writeLn(unit.answer);\n");
        if (vm is not null) File.WriteAllText(Path.Combine(_folder, "weblingo.config"), "{\"vm\":\"" + vm + "\"}");
        Assert.Empty(VmsLoadedBy(0, "", "check", main));
        Assert.Equal([assembly], VmsLoadedBy(0, "42\n", "run", main));
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
