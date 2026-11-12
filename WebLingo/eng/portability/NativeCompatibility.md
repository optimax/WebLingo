# Native C# portability inventory — M0.6.1

| Facility | Classification | Notes |
|---|---|---|
| Basic C# classes, structs, primitives | Portable target | Verify Native C# compiler support |
| Exceptions, delegates, generics | Planned | Inventory changes with compiler maturity |
| .NET Task / ValueTask | Adapted only | Not permitted as WebLingo core async contract |
| System.Diagnostics.Stopwatch | Adapted | DotNetClock only |
| System.IO.File | Adapted | CLI/bootstrap only; PAL required for runtime |
| ASP.NET Core | Adapted | Hosting adapter only |
| ADO.NET | Adapted | Future database adapters only |
| Reflection.Emit | Prohibited | No dynamic CLR codegen in portable core |

CI currently checks dependency direction through project references and builds/tests on both OSes; automated API analyzers remain future work.
