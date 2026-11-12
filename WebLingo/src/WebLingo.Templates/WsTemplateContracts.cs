namespace WebLingo.Templates;

/// <summary>
/// All three composition features are mandatory for the first template-capable release.
/// Contracts only in M0.6.1; no template compiler implementation yet.
/// </summary>
public enum WsTemplateDependencyKind { Include, Layout, Component }
public sealed record WsTemplateDependency(WsTemplateDependencyKind Kind, string ApplicationRelativePath);
public interface IWsTemplateDependencyResolver
{
    IReadOnlyList<WsTemplateDependency> ResolveDependencies(string templatePath);
}
