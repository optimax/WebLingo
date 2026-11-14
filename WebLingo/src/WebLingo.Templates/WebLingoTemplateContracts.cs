namespace WebLingo.Templates;

/// <summary>
/// All three composition features are mandatory for the first template-capable release.
/// Contracts only, from M0.6.1, for the dependencies that includes, layouts and components make (section 13, WL-T007);
/// not used yet.
/// </summary>
public enum WebLingoTemplateDependencyKind { Include, Layout, Component }
public sealed record WebLingoTemplateDependency(WebLingoTemplateDependencyKind Kind, string ApplicationRelativePath);
public interface IWebLingoTemplateDependencyResolver
{
    IReadOnlyList<WebLingoTemplateDependency> ResolveDependencies(string templatePath);
}
