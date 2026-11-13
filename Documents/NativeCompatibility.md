# Native C# portability inventory — M0.6

| Facility | Classification | Notes |
|---|---|---|
| Basic C# classes, structs, primitives | Portable target | Verify Native C# compiler support |
| Exceptions, delegates, generics | Planned | Inventory changes with compiler maturity. The VM catches a built-in's exception and reports it as a runtime error; the core's own code reports errors as results, not exceptions |
| C# records, pattern matching, switch expressions, string interpolation | Planned | Used throughout the compiler, bytecode, runtime and VM (M0.6) |
| System.Collections.Generic (`List<T>`, `Dictionary<TKey, TValue>`, `Stack<T>`) | Planned | Compiler, verifier, VM (M0.6) |
| System.Linq | Not used | Kept out of the portable core's source (tests use it) |
| `BitConverter.DoubleToInt64Bits` / `Int64BitsToDouble` | Portable target | binary64 values and constants are kept as their IEEE 754 bits |
| Invariant number formatting (`long.ToString`, `double.ToString("R")` with `CultureInfo.InvariantCulture`) | Planned | The text `write` writes for a number (`WsValueFormatter`). The Native C# runtime must give the same text, exactly: the shortest decimal that reads back as the same binary64, in .NET's exponent style (`1E+21`, `1.5E-07`). Relying on .NET for it until then is approved (12 November 2026) |
| Invariant number parsing (`double.Parse` with `NumberStyles.Float` and `CultureInfo.InvariantCulture`) | Planned | The lexer's binary64 literals (`WsLexer`). The Native C# runtime must give the same value: the binary64 nearest to the decimal, as IEEE 754 rounds it |
| `System.Text.StringBuilder` | Planned | The disassembler, the lexer's string literals, and `WsCollectedOutput` (`WebLingo.StdLib`), which collects a script's output for tests and hosts |
| `volatile` fields | Planned | `WsCancellationSignal`, set by the host, read by the VM |
| `ref readonly` locals and `in` parameters | Planned | The VMs read their instructions where they are, without copying them |
| `Math.BigMul(long, long, out long)` | Planned | The VMs' int64 multiplication, whose 128-bit product shows an overflow |
| `Array.Resize`, `Array.Clear` | Planned | The VMs' stacks of frames, and the registers or slots a returning call clears |
| `[MethodImpl(MethodImplOptions.AggressiveInlining)]` | Planned | A hint to .NET's JIT on the VMs' arithmetic and comparisons; a compiler that ignores it still compiles the code correctly |
| .NET Task / ValueTask | Adapted only | Not permitted as WebLingo core async contract |
| .NET `CancellationToken` | Not used | The VM's cancellation is WebLingo's own `WsCancellationSignal` (WS-P007) |
| System.Diagnostics.Stopwatch | Adapted | DotNetClock only |
| System.IO.File, Directory, their `Info` types, FileStream | Adapted | CLI/bootstrap only; PAL required for runtime |
| System.IO.Path | Adapted | Paths and their separators are the PAL's, not the platform's |
| System.Console | Adapted | A host's (the CLI's), never the core's |
| System.Environment | Adapted | The core must not depend on the platform, its line ending among others |
| OS threads (`Thread`, `ThreadPool`) | Adapted | Adapter-only (§7.14) |
| P/Invoke | Adapted | Adapter-only (§7.14) |
| ASP.NET Core | Adapted | Hosting adapter only |
| ADO.NET | Adapted | Future database adapters only |
| System.Net.Http | Adapted | HTTP is a platform service (§7.2) |
| Reflection.Emit | Prohibited | No dynamic CLR codegen in portable core |

**Checks.** Dependency direction is enforced by the project references and by `DependencyTests`; the rows above classified Adapted, Prohibited or Not used are checked on the portable core's built assemblies by `PortabilityTests` (both in `WebLingo.Architecture.Tests`), which fail when the core references such a type or declares a P/Invoke. A new rule for the portable core goes into this table and into `PortabilityTests`. The CI workflow (`.github/workflows/build.yml` at the repository root) runs these tests on both OSes on every push; its first run passed (CI run 1, 12 November 2026, commit `5229806`). The Planned rows are not checked: they record what the Native C# compiler and runtime must provide.
