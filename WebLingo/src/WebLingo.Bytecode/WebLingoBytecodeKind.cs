namespace WebLingo.Bytecode;

/// <summary>Which VM's bytecode a WLBC file holds, as its header says (specification, section 5.3, the owner's answers).</summary>
public enum WebLingoBytecodeKind : ushort
{
    /// <summary>The register VM's (<see cref="WebLingoCompiledModule"/>).</summary>
    Register = 1,
    /// <summary>
    /// The stack VM's, which WebLingo had until the owner chose the register VM (23 November 2026, WL-BC002; the tag
    /// <c>stack-vm-end</c>): a file of it is refused.
    /// </summary>
    Stack = 2
}
