# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## Runtime and CLI Choices (M0.6.6)

Where the specification is silent, the M0.6.6 runtime, VM and CLI made choices of their own, recorded as Proposed in the specification's section 15.2 (rows "Runtime values" to "Diagnostic codes").

**Q1: Arithmetic: an int64 meeting a binary64 becomes a binary64 (`10 / 4 + 1` is 3.5), and binary64 results follow IEEE 754, so `1 / 0` is `Infinity` and `0 / 0` is `NaN`, with no error; only integer overflow is an error. Or should mixing need an explicit conversion, or division by zero be an error?**

A1: Mixing allowed within reason. Division by zero `Infinity` (1/0) or `NaN` (0/0). There need to be literals that mean `Infinity` and `NaN`.

**Q2: The text of a number (what `write` writes): an int64 in digits; a binary64 as the shortest decimal that reads back as the same number, always with a point or an exponent (`5.0`, `0.1`, `-0.0`, `1E+21`), and `NaN`, `Infinity`, `-Infinity`. Is that the format you want (JavaScript would write `5`, `1e+21`)?**

A2: Yes.

**Q3: It is .NET's invariant `double.ToString("R")` that gives that text, so the Native C# runtime must give exactly the same, an obligation now in the portability inventory (`Documents/NativeCompatibility.md`, "Planned"). Is it acceptable for the core to rely on it until then?**

A3: Yes.

**Q4: The CLI's exit codes: 0 success, 1 compilation errors, 2 a usage error or an unreadable file, 3 a runtime error (M0.6.1's 3 meant "not implemented"); diagnostics as `file(line,column): error CODE: message`; `write`'s lines end in LF on every platform; `--version` says `WebLingo M0.6 (pre-alpha)`. Agreed?**

A4: 2 seems to mean two different things. Separate them to make interpretation of the codes easier. Also note the change: `print` --> `write`.

**Q5: The new diagnostic codes are WS3002 (integer overflow), WS3003 (budget exceeded) and WS3004 (cancelled). May they keep these codes? And a reading before initialization at run time keeps WS2004, from the compile-time 2xxx range: is that right, or should it have a runtime code of its own?**

A5: Yes.

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
