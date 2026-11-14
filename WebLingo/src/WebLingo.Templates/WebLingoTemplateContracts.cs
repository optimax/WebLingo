namespace WebLingo.Templates;

/// <summary>
/// All three composition features are mandatory for the first template-capable release.
/// Contracts only in M0.6.1; no template compiler implementation yet.
/// </summary>
public enum WebLingoTemplateDependencyKind { Include, Layout, Component }
public sealed record WebLingoTemplateDependency(WebLingoTemplateDependencyKind Kind, string ApplicationRelativePath);
public interface IWebLingoTemplateDependencyResolver
{
    IReadOnlyList<WebLingoTemplateDependency> ResolveDependencies(string templatePath);
}
