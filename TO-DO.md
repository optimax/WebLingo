# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] Stack VM Prototype and Benchmark (WS-BC002)

The specification keeps the choice between a register VM and a stack VM provisional until both run equivalent workloads from the same lowered IR (sections 5.2, 5.6, and open question 1). Build a stack code generator from `WsIr`, a stack VM sharing `WebLingo.Runtime`'s semantics, and a benchmark comparing them (throughput, bytecode size, allocation). Waits for loops and calls in the language (see "What M0.7 Covers"): on M0.6's straight-line programs a benchmark would measure little. Added by Claude, 2026-11-12, from the roadmap.

2026-11-12: waits for loops and calls in the language, which wait for the answer to "What M0.7 Covers".

2026-11-12: loops (M0.7.4) and calls (M0.7.6) have landed, so it no longer waits; it is next.

Two choices here are the owner's: new projects and packages need approval (`Documents/implementation.md`, sections 3 and 6). Added by Claude, 2026-11-12.

Q1: Where does the stack VM go? (a) a new project, `WebLingo.VM.Stack`, beside `WebLingo.VM`, with its own code generator in `WebLingo.Compiler`; (b) inside `WebLingo.VM` and `WebLingo.Compiler`, next to the register ones.

A1: (a)

Q2: How is it measured? (a) a small console project, `WebLingo.Benchmarks`, timing both VMs with `Stopwatch` over fixed workloads (loops, calls, closures, strings), with no new package; (b) the same with BenchmarkDotNet, a new third-party package (MIT).

A2: (a)

Q3: Which register VM is compared: (a) the one as it is; (b) first given a dispatch tuned for speed (the specification's section 5.6), so that both are compared at their best?

A3: (b)

2026-11-13: the answers are recorded as approved in the specification, section 5.6 ("The comparison's parts"), and in its decision register. That section splits the work into four parts, each a task below: "Stack VM Comparison 1: The Benchmark", "Stack VM Comparison 2: The Register VM's Dispatch, Tuned", "Stack VM Comparison 3: The Stack VM" and "Stack VM Comparison 4: The Comparison". Waits for them.

## [WORKING] Stack VM Comparison 3: The Stack VM

Stack bytecode and its verifier in `WebLingo.Bytecode`, its code generator in `WebLingo.Compiler`, from the same lowered IR as the register one, and the stack VM in `WebLingo.VM.Stack`, sharing `WebLingo.Runtime`'s semantics (specification, section 5.6, part 3). Every conformance program gives the same output and the same errors on both VMs. Added by Claude, 2026-11-13.

## Stack VM Comparison 4: The Comparison

Both VMs measured on the same workloads, the results recorded in the specification (section 5.6, part 4), and the choice between them (WS-BC002) put to the owner as a question. Added by Claude, 2026-11-13.

# END-OF-TO-DO
