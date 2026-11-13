# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] Functions as Values

The owner's answer to "Binding Choices (M0.6.4)", Q3: functions must be values. Today a built-in can only be called; naming it otherwise is WS3001, and calling anything but a built-in is WS2005 (specification, section 15.2, "Functions as values"). The specification lists Function, a callable closure, among the value categories (section 3.2), but gives no syntax for declaring functions yet, so this waits for the language's functions: their syntax goes into "What M0.7 Covers". Then a function becomes a `WsValue`, a call takes any callee expression, and the verifier and VM check calls at run time where the compiler cannot. Added by Claude, 2026-11-12.

2026-11-12: waits for the language's function syntax, which waits for "What M0.7 Covers".

## [QUESTION] What M0.7 Covers

M0.7 is "Conformance suite expansion" (specification, section 9), with no detail yet. Expanding the conformance suite needs language beyond M0.6's subset, and most of it needs syntax the specification does not give yet. Added by Claude, 2026-11-12, from the roadmap (`Documents/implementation.md`, section 13).

Q: Which features should M0.7 bring, and in what order? My proposal, from what the specification already decides: (1) blocks and nested scopes, whose semantics are approved (block scope, shadowing, section 3.1); (2) `true`, `false` and `null` literals, comparisons, and `if`/`else` with strict Boolean conditions (section 3.2); (3) `while` loops, which also give the stack VM benchmark something to measure; (4) string literals, whose observable semantics are approved (section 3.3) but whose syntax and escapes are not. Each needs its syntax decided first; shall I propose it, feature by feature, as questions here?

A: Yes, propose here.

The proposals below follow that order, with functions added as (5), since "Functions as Values" waits for them. Each feature would be one task, landing on its own, with its conformance tests. Proposed by Claude, 2026-11-12.

**Q1, blocks: `{ statements }` is a statement. A block opens a scope; a declaration inside it is in scope from where it stands to the block's closing `}`, and may shadow an outer name (section 3.1). An empty block `{}` is allowed, though a lone `;` stays an error. No `;` after the `}`. Agreed?**

A1:

**Q2, Booleans, `null` and comparisons: `true`, `false` and `null` become keywords. The comparisons are `== != < <= > >=`, and they give a Boolean. `==` and `!=` never convert, except that an int64 and a binary64 compare as numbers (`1 == 1.0` is true, and `NaN == NaN` is false, as IEEE 754 says). `null == null` is true, and comparing values of different kinds is false, not an error. `< <= > >=` take numbers only (WS3001 otherwise; strings later). The logical operators are `!`, `&&` and `||`, which take Booleans only (WS3001 otherwise, no truthiness) and short-circuit. Precedence, loosest first: assignment, `||`, `&&`, `== !=`, `< <= > >=`, `+ -`, `* /`, unary. Agreed, and should `==` be spelled that way rather than `===`?**

A2:

**Q3, `if`: `if (condition) statement`, with an optional `else statement`, where `else` belongs to the nearest `if`. The condition must be a Boolean at run time (WS3001 otherwise). Should the branches have to be blocks (`if (x) { … }`), which rules out a dangling `else`, or may they be any statement?**

A3:

**Q4, `while`: `while (condition) statement`, with a strict Boolean condition, plus `break;` and `continue;` for the innermost loop (a compile-time error outside one). A loop is bounded only by the execution's instruction budget (WS3003). Is `for`, or `do … while`, wanted in M0.7 too, or later?**

A4:

**Q5, strings: literals in double or single quotes (`"a"`, `'a'`), alike in meaning, each on one line. Escapes: `\n`, `\r`, `\t`, `\0`, `\\`, `\'`, `\"`, and `\u{1F600}`, which names a Unicode scalar value (at most six hex digits, and no surrogates). Any other escape is an error, a new WS1009 "invalid escape", and an unterminated string is WS1010. `+` concatenates two strings, but a string and a number are not joined (WS3001; an explicit conversion comes later). `length` and indexing come with member access, later. Agreed, and should `+` with a string and a number convert the number, as JavaScript does?**

A5:

**Q6, functions: `function name(a, b) { … }` declares a function, in scope throughout its block (so that functions can call each other in any order), unlike `var` and `const`. `return value;` and `return;` (giving `null`) end it. Parameters are like `var`s. A function is a value (`var f = name;`), and `function (a) { … }` is a function expression. Closures capture variables, not copies. Is this the syntax you want, should functions be hoisted like that, and do they belong in M0.7, or in a milestone of their own?**

A6:

## [IDLE] Stack VM Prototype and Benchmark (WS-BC002)

The specification keeps the choice between a register VM and a stack VM provisional until both run equivalent workloads from the same lowered IR (sections 5.2, 5.6, and open question 1). Build a stack code generator from `WsIr`, a stack VM sharing `WebLingo.Runtime`'s semantics, and a benchmark comparing them (throughput, bytecode size, allocation). Waits for loops and calls in the language (see "What M0.7 Covers"): on M0.6's straight-line programs a benchmark would measure little. Added by Claude, 2026-11-12, from the roadmap.

2026-11-12: waits for loops and calls in the language, which wait for the answer to "What M0.7 Covers".

# END-OF-TO-DO
