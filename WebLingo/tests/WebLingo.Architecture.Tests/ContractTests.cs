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
        Assert.Contains(WsTemplateDependencyKind.Include, Enum.GetValues<WsTemplateDependencyKind>());
        Assert.Contains(WsTemplateDependencyKind.Layout, Enum.GetValues<WsTemplateDependencyKind>());
        Assert.Contains(WsTemplateDependencyKind.Component, Enum.GetValues<WsTemplateDependencyKind>());
    }
    [Fact]
    public void EmptyBytecodeModuleCanBeRepresented()
    {
        var module = WsCompiledModule.Empty("test");
        Assert.Equal("test", module.Name);
        Assert.True(new WsBytecodeVerifier().Verify(module).IsValid);
    }
    [Fact]
    public void WorkerIdentityIsExplicit()
    {
        Assert.Equal(3, new WsExecutionContext(new WsWorkerId(3)).OwnerWorkerId.Value);
    }
}
