namespace WebLingo.Core;

/// <summary>
/// A module's parameter (specification, section 3.9), <c>param name;</c> or with a default, <c>param size = 10;</c>: its
/// name, and whether an import may leave it out.
/// </summary>
public sealed record WebLingoModuleParameter(string Name, bool HasDefault);
