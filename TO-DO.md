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

## [WORKING] Stack VM Comparison 4: The Comparison

Both VMs measured on the same workloads, the results recorded in the specification (section 5.6, part 4), and the choice between them (WS-BC002) put to the owner as a question. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13): both VMs measured on the four workloads, 11 timed runs each, fully optimized. The register VM is 1.10 to 1.65 times as fast and runs 1.29 to 1.75 times fewer instructions; the stack bytecode is 3 to 35 percent smaller in bytes; allocation is the same. Recorded in the specification, section 5.6 ("The comparison"), with each implementation's size in lines, and in sections 5.2, 9 and 10. The choice is the question "Register VM or Stack VM (WS-BC002)" below. Nothing in the code changed; Windows only.

## [QUESTION] Register VM or Stack VM (WS-BC002)

The comparison is done (specification, section 5.6, "The comparison"). On the four workloads (loops, calls, closures, strings), the register VM is 1.10 to 1.65 times as fast as the stack VM, and runs 1.29 to 1.75 times fewer instructions. The stack bytecode is 3 to 35 percent smaller in bytes, and its code generator is simpler. Both allocate the same. Not measured yet, because the language does not have them: property access, async suspension, template output, I/O-bound workloads, startup and latency. Added by Claude, 2026-11-13.

**Q1: Which VM does WebLingo keep? (a) the register VM: WS-BC002 is approved, and the stack VM is removed; (b) the register VM: WS-BC002 is approved, and the stack VM stays as it is, for comparisons later; (c) both, for now: WS-BC002 stays provisional until the workloads not measured yet can be, and every new feature is built on both VMs; (d) the stack VM.**

A1:

# END-OF-TO-DO
