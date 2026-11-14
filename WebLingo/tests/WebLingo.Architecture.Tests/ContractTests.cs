using WebLingo.Bytecode;
using WebLingo.Runtime;
using WebLingo.Templates;
using Xunit;
namespace WebLingo.Architecture.Tests;

public sealed class ContractTests
{
    [Fact]
    public void TemplateCompositionKindsArePresent()
    {
        Assert.Contains(WebLingoTemplateDependencyKind.Include, Enum.GetValues<WebLingoTemplateDependencyKind>());
        Assert.Contains(WebLingoTemplateDependencyKind.Layout, Enum.GetValues<WebLingoTemplateDependencyKind>());
        Assert.Contains(WebLingoTemplateDependencyKind.Component, Enum.GetValues<WebLingoTemplateDependencyKind>());
    }
    [Fact]
    public void EmptyBytecodeModuleCanBeRepresented()
    {
        var module = WebLingoCompiledModule.Empty("test");
        Assert.Equal("test", module.Name);
        Assert.True(new WebLingoBytecodeVerifier().Verify(module).IsValid);
    }
    [Fact]
    public void WorkerIdentityIsExplicit()
    {
        Assert.Equal(3, new WebLingoExecutionContext(new WebLingoWorkerId(3)).OwnerWorkerId.Value);
    }
}
