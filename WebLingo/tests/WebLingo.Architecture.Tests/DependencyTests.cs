using System.Reflection;
using Xunit;
namespace WebLingo.Architecture.Tests;

/// <summary>
/// The specification's dependency rules (section 8.3) and its acceptance criterion that the portable core has no
/// ASP.NET Core or ADO.NET dependency (section 8.8), checked on the built assemblies: an assembly references only
/// the WebLingo assemblies it may, and no framework it must not.
/// </summary>
public sealed class DependencyTests
{
    public static TheoryData<string, string[]> PortableCore => new()
    {
        { "WebLingo.Core", [] },
        { "WebLingo.Language", ["WebLingo.Core"] },
        { "WebLingo.Bytecode", ["WebLingo.Core"] },
        { "WebLingo.Compiler", ["WebLingo.Core", "WebLingo.Language", "WebLingo.Bytecode"] },
        { "WebLingo.Runtime", ["WebLingo.Core", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.VM", ["WebLingo.Core", "WebLingo.Bytecode", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        {
            "WebLingo.VM.Stack",
            ["WebLingo.Core", "WebLingo.Bytecode", "WebLingo.Runtime", "WebLingo.VM", "WebLingo.Platform.Abstractions"]
        },
        { "WebLingo.Templates", ["WebLingo.Core", "WebLingo.Language", "WebLingo.Compiler"] },
        { "WebLingo.StdLib", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Tasks", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.Platform.Abstractions", ["WebLingo.Core"] },
        { "WebLingo.Hosting.Abstractions", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
    };

    /// <summary>Frameworks the portable core must not use (section 7.2): adapters may.</summary>
    private static readonly string[] Forbidden =
    [
        "Microsoft.AspNetCore", "System.Data", "Microsoft.Data", "System.Reflection.Emit", "System.Net.Http",
    ];

    [Theory]
    [MemberData(nameof(PortableCore))]
    public void APortableAssemblyReferencesOnlyTheWebLingoAssembliesItMay(string assembly, string[] allowed)
    {
        var references = References(assembly).Where(name => name.StartsWith("WebLingo.", StringComparison.Ordinal));
        Assert.All(references, name => Assert.Contains(name, allowed));
    }

    [Theory]
    [MemberData(nameof(PortableCore))]
    public void APortableAssemblyUsesNoForbiddenFramework(string assembly, string[] allowed)
    {
        _ = allowed;
        foreach (var name in References(assembly))
        {
            Assert.DoesNotContain(Forbidden, forbidden => name.StartsWith(forbidden, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheRulesCoverEveryPortableAssembly()
    {
        Assert.Equal(12, PortableCore.Count); // Section 3 of Documents/implementation.md lists twelve.
    }

    private static IEnumerable<string> References(string assembly) =>
        Assembly.Load(assembly).GetReferencedAssemblies().Select(reference => reference.Name ?? "");
}
