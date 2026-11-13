# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] Literals for `Infinity` and `NaN`

The owner's answer to "Runtime and CLI Choices (M0.6.6)", Q1: there need to be literals that mean `Infinity` and `NaN` (specification, section 15.2, "Binary64 results"). Today they arise only from division (`1 / 0`, `0 / 0`), and the language has no binary64 literals at all yet (`1.5` is not lexed). Added by Claude, 2026-11-12.

**Q: How are they spelled? My proposal: two keywords, `Infinity` and `NaN`, spelled as `write` shows them, each a binary64 literal; `-Infinity` is then unary minus applied to it. Being keywords, they cannot be declared or shadowed, unlike the built-ins. The alternatives are predeclared constants, which a declaration could shadow as it can `write`, or a spelling of your own (`inf`, `nan`). Shall I add them as keywords, and should binary64 literals (`1.5`, `1e21`) come in the same task?**

## Update the CI Actions Off Node.js 20

CI's first run (12 November 2026, commit `5229806`) passed, with a warning on both jobs: `actions/checkout@v4` and `actions/setup-dotnet@v4` target Node.js 20, which GitHub has deprecated and now forces onto Node.js 24. Move `.github/workflows/build.yml` to the actions' current major versions, which run on Node.js 24, checking the versions on their release pages first (`AGENTS.md`, section 4). It is tried on the next push, which needs the user's go-ahead. A notice says `ubuntu-latest` is moving to Ubuntu 26 (from 19 October 2026); nothing to do for that but watch the next runs. Added by Claude, 2026-11-12, from that run's annotations.

## Functions as Values

The owner's answer to "Binding Choices (M0.6.4)", Q3: functions must be values. Today a built-in can only be called; naming it otherwise is WS3001, and calling anything but a built-in is WS2005 (specification, section 15.2, "Functions as values"). The specification lists Function, a callable closure, among the value categories (section 3.2), but gives no syntax for declaring functions yet, so this waits for the language's functions: their syntax goes into "What M0.7 Covers". Then a function becomes a `WsValue`, a call takes any callee expression, and the verifier and VM check calls at run time where the compiler cannot. Added by Claude, 2026-11-12.

## [QUESTION] What M0.7 Covers

M0.7 is "Conformance suite expansion" (specification, section 9), with no detail yet. Expanding the conformance suite needs language beyond M0.6's subset, and most of it needs syntax the specification does not give yet. Added by Claude, 2026-11-12, from the roadmap (`Documents/implementation.md`, section 13).

**Q: Which features should M0.7 bring, and in what order? My proposal, from what the specification already decides: (1) blocks and nested scopes, whose semantics are approved (block scope, shadowing, the temporal dead zone, section 3.1); (2) `true`, `false` and `null` literals, comparisons, and `if`/`else` with strict Boolean conditions (section 3.2); (3) `while` loops, which also give the stack VM benchmark something to measure; (4) string literals, whose observable semantics are approved (section 3.3) but whose syntax and escapes are not. Each needs its syntax decided first; shall I propose it, feature by feature, as questions here?**

A: Yes, propose here.

## [IDLE] Stack VM Prototype and Benchmark (WS-BC002)

The specification keeps the choice between a register VM and a stack VM provisional until both run equivalent workloads from the same lowered IR (sections 5.2, 5.6, and open question 1). Build a stack code generator from `WsIr`, a stack VM sharing `WebLingo.Runtime`'s semantics, and a benchmark comparing them (throughput, bytecode size, allocation). Waits for loops and calls in the language (see "What M0.7 Covers"): on M0.6's straight-line programs a benchmark would measure little. Added by Claude, 2026-11-12, from the roadmap.

2026-11-12: waits for loops and calls in the language, which wait for the answer to "What M0.7 Covers".

# END-OF-TO-DO
