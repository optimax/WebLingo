using WebLingo.Bytecode;
using WebLingo.Core;
namespace WebLingo.Compiler;

public sealed record WsCompilationResult(WsCompiledModule? Module, IReadOnlyList<WsDiagnostic> Diagnostics);
public interface IWsCompiler { WsCompilationResult Compile(string source, string sourceName); }
