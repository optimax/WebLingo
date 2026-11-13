using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;
namespace WebLingo.Architecture.Tests;

/// <summary>
/// The portability inventory (<c>Documents/NativeCompatibility.md</c>), checked on the portable core's built
/// assemblies (WS-P016): no type the inventory rules out for the core (adapter-only, prohibited, or not used) is
/// referenced, and no method is a P/Invoke. A rule added to the inventory for the portable core gets its entry here.
/// </summary>
public sealed class PortabilityTests
{
    /// <summary>Whole namespaces the portable core must not use, with the inventory's reason.</summary>
    private static readonly (string Namespace, string Reason)[] ForbiddenNamespaces =
    [
        ("System.Reflection.Emit", "prohibited: no dynamic CLR code generation"),
        ("System.Threading.Tasks", ".NET Task and ValueTask are adapter-only, not WebLingo's asynchronous contract"),
        ("System.Data", "ADO.NET is adapter-only"),
        ("Microsoft.Data", "ADO.NET is adapter-only"),
        ("Microsoft.AspNetCore", "ASP.NET Core is adapter-only"),
        ("System.Net.Http", "HTTP is a platform service, adapter-only"),
        ("System.Linq", "LINQ is kept out of the portable core"),
    ];

    /// <summary>Single types the portable core must not use, with the inventory's reason.</summary>
    private static readonly (string Type, string Reason)[] ForbiddenTypes =
    [
        ("System.IO.File", "file access is CLI/bootstrap only; the runtime goes through the PAL"),
        ("System.IO.FileInfo", "file access is CLI/bootstrap only"),
        ("System.IO.FileStream", "file access is CLI/bootstrap only"),
        ("System.IO.Directory", "file access is CLI/bootstrap only"),
        ("System.IO.DirectoryInfo", "file access is CLI/bootstrap only"),
        ("System.IO.Path", "paths are the PAL's, not the platform's"),
        ("System.Console", "the console belongs to a host"),
        ("System.Environment", "the core must not depend on the platform (its line ending among others)"),
        ("System.Threading.Thread", "OS threads are adapter-only"),
        ("System.Threading.ThreadPool", "OS threads are adapter-only"),
        ("System.Threading.CancellationToken", "cancellation is WebLingo's own WsCancellationSignal"),
        ("System.Threading.CancellationTokenSource", "cancellation is WebLingo's own WsCancellationSignal"),
    ];

    public static TheoryData<string> PortableCore => new()
    {
        "WebLingo.Core", "WebLingo.Language", "WebLingo.Bytecode", "WebLingo.Compiler", "WebLingo.Runtime",
        "WebLingo.VM", "WebLingo.VM.Stack", "WebLingo.Templates", "WebLingo.StdLib", "WebLingo.Platform.Abstractions",
        "WebLingo.Hosting.Abstractions",
    };

    [Theory]
    [MemberData(nameof(PortableCore))]
    public void APortableAssemblyUsesNothingTheInventoryRulesOut(string assembly)
    {
        Assert.Empty(Violations(Assembly.Load(assembly)));
    }

    [Fact]
    public void TheCheckFindsWhatItLooksFor()
    {
        // This test assembly reads files and uses LINQ, so the check must object to it.
        var violations = Violations(typeof(PortabilityTests).Assembly);
        Assert.Contains(violations, v => v.StartsWith("System.IO.File:", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith("System.Linq.", StringComparison.Ordinal));
    }

    private static List<string> Violations(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        var violations = new List<string>();

        foreach (var handle in metadata.TypeReferences)
        {
            var name = FullName(metadata, metadata.GetTypeReference(handle));
            foreach (var (prefix, reason) in ForbiddenNamespaces)
            {
                if (name.StartsWith(prefix + ".", StringComparison.Ordinal)) violations.Add($"{name}: {reason}");
            }
            foreach (var (type, reason) in ForbiddenTypes)
            {
                if (name == type || name.StartsWith(type + "+", StringComparison.Ordinal)) violations.Add($"{name}: {reason}");
            }
        }
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            {
                violations.Add($"{metadata.GetString(method.Name)}: P/Invoke is adapter-only");
            }
        }
        return violations;
    }

    private static string FullName(MetadataReader metadata, TypeReference type)
    {
        var name = metadata.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var outer = metadata.GetTypeReference((TypeReferenceHandle)type.ResolutionScope);
            return FullName(metadata, outer) + "+" + name;
        }
        var space = metadata.GetString(type.Namespace);
        return space.Length == 0 ? name : space + "." + name;
    }
}
