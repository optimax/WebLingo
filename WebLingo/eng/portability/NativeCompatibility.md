# Native C# portability inventory — M0.6

| Facility | Classification | Notes |
|---|---|---|
| Basic C# classes, structs, primitives | Portable target | Verify Native C# compiler support |
| Exceptions, delegates, generics | Planned | Inventory changes with compiler maturity. The VM catches a built-in's exception and reports it as a runtime error; the core's own code reports errors as results, not exceptions |
| C# records, pattern matching, switch expressions, string interpolation | Planned | Used throughout the compiler, bytecode, runtime and VM (M0.6) |
| System.Collections.Generic (`List<T>`, `Dictionary<TKey, TValue>`, `Stack<T>`) | Planned | Compiler, verifier, VM (M0.6) |
| System.Linq | Not used | Kept out of the portable core's source (tests use it) |
| `BitConverter.DoubleToInt64Bits` / `Int64BitsToDouble` | Portable target | binary64 values and constants are kept as their IEEE 754 bits |
| Invariant number formatting (`long.ToString`, `double.ToString("R")` with `CultureInfo.InvariantCulture`) | Planned | The text `print` writes for a number (`WsValueFormatter`). The Native C# runtime must give the same text: the shortest decimal that reads back as the same binary64 |
| `System.Text.StringBuilder` | Planned | The disassembler only |
| `volatile` fields | Planned | `WsCancellationSignal`, set by the host, read by the VM |
| .NET Task / ValueTask | Adapted only | Not permitted as WebLingo core async contract |
| .NET `CancellationToken` | Not used | The VM's cancellation is WebLingo's own `WsCancellationSignal` (WS-P007) |
| System.Diagnostics.Stopwatch | Adapted | DotNetClock only |
| System.IO.File | Adapted | CLI/bootstrap only; PAL required for runtime |
| ASP.NET Core | Adapted | Hosting adapter only |
| ADO.NET | Adapted | Future database adapters only |
| Reflection.Emit | Prohibited | No dynamic CLR codegen in portable core |

Dependency direction is enforced by the project references. The CI workflow (`.github/workflows/build.yml` at the repository root) builds and tests on both OSes once it runs; as of 12 November 2026 it has not run yet. Automated API analyzers remain future work.
