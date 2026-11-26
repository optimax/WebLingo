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
        // Not the register VM's: a host running one VM loads that VM's assembly alone.
        { "WebLingo.Templates", ["WebLingo.Core", "WebLingo.Language", "WebLingo.Compiler"] },
        { "WebLingo.StdLib", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Tasks", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Fs", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Http", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Net", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Db", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Crypto", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Env", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Log", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Math", ["WebLingo.Core", "WebLingo.Runtime"] },
        { "WebLingo.StdLib.Mail", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        { "WebLingo.StdLib.Csv", ["WebLingo.Core", "WebLingo.Runtime"] },
        { "WebLingo.StdLib.Test", ["WebLingo.Core", "WebLingo.Runtime"] },
        { "WebLingo.StdLib.Random", ["WebLingo.Core", "WebLingo.Runtime"] },
        { "WebLingo.StdLib.Process", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        // std:console registers the standard library's writing and reading, which hosts and tests give as their own too.
        { "WebLingo.StdLib.Console", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions", "WebLingo.StdLib"] },
        { "WebLingo.Platform.Abstractions", ["WebLingo.Core"] },
        { "WebLingo.Hosting.Abstractions", ["WebLingo.Core", "WebLingo.Runtime", "WebLingo.Platform.Abstractions"] },
        // The host's core composes the engine for every adapter (section 6.12), using the site's configured VM.
        {
            "WebLingo.Hosting",
            ["WebLingo.Core", "WebLingo.Language", "WebLingo.Bytecode", "WebLingo.Compiler", "WebLingo.Templates", "WebLingo.Runtime",
             "WebLingo.VM", "WebLingo.StdLib", "WebLingo.Platform.Abstractions", "WebLingo.Hosting.Abstractions"]
        },
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
        Assert.Equal(26, PortableCore.Count); // Section 3 of Documents/implementation.md lists the portable assemblies.
    }

    [Theory]
    [InlineData("WebLingo.StdLib.Path")]
    [InlineData("WebLingo.StdLib.Url")]
    [InlineData("WebLingo.StdLib.Compression")]
    public void HostUtilityModulesDependOnlyOnCoreAndRuntime(string assembly)
    {
        APortableAssemblyReferencesOnlyTheWebLingoAssembliesItMay(assembly, ["WebLingo.Core", "WebLingo.Runtime"]);
    }

    /// <summary>
    /// The hosts' own code, and the FastCGI host, use nothing of ASP.NET Core's (section 9, "M0.23 parts", part 1), so that a
    /// host without it, the standalone one, Native C#'s, has all it needs: neither references ASP.NET Core, its extensions,
    /// nor its adapter.
    /// </summary>
    [Theory]
    [InlineData("WebLingo.Hosting.DotNet")]
    [InlineData("WebLingo.Hosting.FastCgi")]
    public void TheHostsOwnCodeUsesNothingOfAspNetCores(string assembly)
    {
        foreach (var name in References(assembly))
        {
            Assert.False(name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)
                         || name == "WebLingo.Hosting.AspNetCore", $"{assembly} references {name}");
        }
    }

    private static IEnumerable<string> References(string assembly) =>
        Assembly.Load(assembly).GetReferencedAssemblies().Select(reference => reference.Name ?? "");
}
