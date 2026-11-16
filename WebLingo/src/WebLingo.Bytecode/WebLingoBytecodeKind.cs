namespace WebLingo.Bytecode;

/// <summary>Which VM's bytecode a WLBC file holds, as its header says (specification, section 5.3, the owner's answers).</summary>
public enum WebLingoBytecodeKind : ushort
{
    /// <summary>The register VM's (<see cref="WebLingoCompiledModule"/>).</summary>
    Register = 1,
    /// <summary>The stack VM's (<see cref="WebLingoStackModule"/>).</summary>
    Stack = 2
}
