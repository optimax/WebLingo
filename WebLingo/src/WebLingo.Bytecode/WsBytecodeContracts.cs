namespace WebLingo.Bytecode;

/// <summary>In-memory module placeholder. Serialized WBC and instruction set are not frozen.</summary>
public sealed record WsCompiledModule(string Name, ReadOnlyMemory<byte> Code);
public sealed record WsVerificationResult(bool IsValid, string? Error);
public interface IWsBytecodeVerifier { WsVerificationResult Verify(WsCompiledModule module); }
