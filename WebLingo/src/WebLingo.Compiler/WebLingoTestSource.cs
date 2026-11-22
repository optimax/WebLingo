namespace WebLingo.Compiler;

/// <summary>Standard assertions that call WebLingo functions and await their tasks on the program's VM.</summary>
internal static class WebLingoTestSource
{
    public const string Name = "std:test";

    /// <summary>Its source, <c>Builtins/Test.wlg</c>, made a constant at build time (<see cref="WebLingoSources"/>).</summary>
    public const string Text = WebLingoSources.Test;
}
