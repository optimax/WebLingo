namespace WebLingo.Templates;

/// <summary>
/// What a template is read as (specification, section 13): a page, which a host runs, or a component a page renders; or a
/// layout, which a page's <c>layout</c> statement names, and whose <c>renderBody()</c> awaits the page's body, which the
/// page gives it as its first parameter.
/// </summary>
public enum WebLingoTemplateKind
{
    Page,
    Layout
}
