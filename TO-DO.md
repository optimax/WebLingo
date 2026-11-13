# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] A Hoisted Function Called Before a Variable It Uses

A function is in scope throughout its block (specification, section 3.5), so it can be called before a variable it uses is declared, which no other code can do (section 3.1):

```javascript
f();
var y = 1;
function f() { writeLn(y); }
```

Added by Claude, 2026-11-12, while implementing "M0.7.6: Functions, Function Values and Closures".

**Q1: What does that call do? (a) It is a runtime error (WS2004, "read before initialization", the code you kept), and only a variable a hoisted function uses can raise it; (b) a function sees only the variables declared before the function's own declaration, so `y` is undefined in `f` (WS2001), and a call can then never come too early; (c) a function is in scope only from its declaration on, like `var`, and mutual recursion needs a function expression stored in a variable declared first.**

A1:

Until you answer, M0.7.6 does (a), which is the easiest to change: it touches only the variables that functions capture.

## [QUESTION] Stack VM Prototype and Benchmark (WS-BC002)

The specification keeps the choice between a register VM and a stack VM provisional until both run equivalent workloads from the same lowered IR (sections 5.2, 5.6, and open question 1). Build a stack code generator from `WsIr`, a stack VM sharing `WebLingo.Runtime`'s semantics, and a benchmark comparing them (throughput, bytecode size, allocation). Waits for loops and calls in the language (see "What M0.7 Covers"): on M0.6's straight-line programs a benchmark would measure little. Added by Claude, 2026-11-12, from the roadmap.

2026-11-12: waits for loops and calls in the language, which wait for the answer to "What M0.7 Covers".

2026-11-12: loops (M0.7.4) and calls (M0.7.6) have landed, so it no longer waits; it is next.

Two choices here are the owner's: new projects and packages need approval (`Documents/implementation.md`, sections 3 and 6). Added by Claude, 2026-11-12.

**Q1: Where does the stack VM go? (a) a new project, `WebLingo.VM.Stack`, beside `WebLingo.VM`, with its own code generator in `WebLingo.Compiler`; (b) inside `WebLingo.VM` and `WebLingo.Compiler`, next to the register ones.**

A1:

**Q2: How is it measured? (a) a small console project, `WebLingo.Benchmarks`, timing both VMs with `Stopwatch` over fixed workloads (loops, calls, closures, strings), with no new package; (b) the same with BenchmarkDotNet, a new third-party package (MIT).**

A2:

**Q3: Which register VM is compared: (a) the one as it is; (b) first given a dispatch tuned for speed (the specification's section 5.6), so that both are compared at their best?**

A3:


# END-OF-TO-DO
