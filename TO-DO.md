# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## Stack VM Prototype and Benchmark (WS-BC002)

The specification keeps the choice between a register VM and a stack VM provisional until both run equivalent workloads from the same lowered IR (sections 5.2, 5.6, and open question 1). Build a stack code generator from `WsIr`, a stack VM sharing `WebLingo.Runtime`'s semantics, and a benchmark comparing them (throughput, bytecode size, allocation). Waits for loops and calls in the language (see "What M0.7 Covers"): on M0.6's straight-line programs a benchmark would measure little. Added by Claude, 2026-11-12, from the roadmap.

2026-11-12: waits for loops and calls in the language, which wait for the answer to "What M0.7 Covers".

2026-11-12: loops (M0.7.4) and calls (M0.7.6) have landed, so it no longer waits; it is next.

Two choices here are the owner's: new projects and packages need approval (`Documents/implementation.md`, sections 3 and 6). Added by Claude, 2026-11-12.

**Q1: Where does the stack VM go? (a) a new project, `WebLingo.VM.Stack`, beside `WebLingo.VM`, with its own code generator in `WebLingo.Compiler`; (b) inside `WebLingo.VM` and `WebLingo.Compiler`, next to the register ones.**

A1: (a)

**Q2: How is it measured? (a) a small console project, `WebLingo.Benchmarks`, timing both VMs with `Stopwatch` over fixed workloads (loops, calls, closures, strings), with no new package; (b) the same with BenchmarkDotNet, a new third-party package (MIT).**

A2: (a)

**Q3: Which register VM is compared: (a) the one as it is; (b) first given a dispatch tuned for speed (the specification's section 5.6), so that both are compared at their best?**

A3: (b)

# END-OF-TO-DO
