# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## Shallow `eng/portability/` Folder

Since "Duplicated Files" (`DONE.md`), documents live in `Documents/`, and the folder structure is kept shallow (`Documents/implementation.md`, section 3). One nested document folder is left: `WebLingo/eng/portability/`, holding only `NativeCompatibility.md`, the portability inventory. The specification names that path in its build conventions (section 8.5, approved with M0.6) and proposes `eng/scripts/` and `eng/ci/` beside it (section 8.2), so I left it where it is.

**Q: Shall `NativeCompatibility.md` move to `Documents/`, with the specification's section 8.5 changed to match and `eng/` removed, or stay in `WebLingo/eng/portability/` as the specification has it?**

A: Move to `Documents\`.

## Lexical Choices (M0.6.2)

Where the specification is silent, the M0.6.2 lexer made choices of its own, recorded as Proposed in the specification's section 15.2 (rows "Line terminators" to "Diagnostic codes"). Each is made in one place, so changing one is small.

**Q1: Do these suit you, or which do you want otherwise? Lines end at CRLF, LF or CR; a column counts Unicode scalar values (a tab is one); whitespace is space, tab, CR and LF only; comments are `//` and `/* */` (not nested); identifiers are ASCII letters, digits and `_`; integer literals are decimal, leading zeros allowed (`007` is 7), at most 9223372036854775807; digits followed by a letter are one invalid literal, which keeps `10m` free for decimals.**

A1: I made comments inline, in the document. If there are follow up questions, bring them up here, not in the document. Redact the document in-place with agreed upon choices without keeping history of those choices.

**Q2: The new diagnostic codes are WS1003 (invalid character), WS1004 (invalid numeric literal) and WS1005 (unterminated comment). May they keep these codes?**

A: Yes.

## Syntax Choices (M0.6.3)

Where the specification is silent, the M0.6.3 parser made choices of its own, recorded as Proposed in the specification's section 15.2 (rows "Statements" to "Diagnostic codes").

**Q1: Do these suit you, or which do you want otherwise? No empty statement (a lone `;` is an error); unary `-` but no unary `+`; assignment is an expression (`a = b = 3`, and `print(x = 5)` prints 5) whose target must be a plain variable name; calls take no trailing comma; `const x;` is a syntax error.**

A1: NO, I commented in the document where appropriate. Again, redact the document in-place with agreed upon choices without keeping history of those choices.

**Q2: Nesting limits: parsing recurses at most 256 levels, and an expression is at most 1000 operations deep, beyond which the source is an error (WS1008) rather than a crash. Are these limits right, or do you want others?**

A2: They are arbitrary, so make them easy to change constants in the implementation. 

**Q3: The new diagnostic codes are WS1006 (`const` without a value), WS1007 (invalid assignment target) and WS1008 (nested too deeply). May they keep these codes?**

A3: Yes.

## Binding Choices (M0.6.4)

Where the specification is silent, the M0.6.4 binder and lowering made choices of their own, recorded as Proposed in the specification's section 15.2 (rows "Compilation stages" to "Diagnostic codes").

Q1: The temporal dead zone at run time: reading a variable before its declaration has run, or a `var x;` before it is assigned, is a runtime error (WS2004), as the specification's `var x; print(x);` example says; so is assigning a variable before its declaration has run (as JavaScript does). Neither is reported at compile time, though in M0.6's straight-line programs the compiler could prove both. Is that what you want, or should provable cases be compile-time errors (or warnings)?

A1 (the user, in chat, 2026-11-12): No temporal dead zone. A name means its outer meaning until its declaration, which takes effect after its initializer; and every declaration needs an initializer, so `var x;` is a syntax error (WS1006).

Done (Claude, 2026-11-12, not yet committed): the specification's sections 1.1, 3.1, 8.4, 8.6 and 15.2 say so, and the code follows. `print(x); var x = 1;` and `var x = x + 1;` (with no outer `x`) are compile-time errors (WS2001), the VM's dead-zone checks are gone, and the verifier now requires every register, locals included, to be written before it is read. WS2004 is no longer reported. 296 tests pass on Windows.

**Q2: The host's built-ins (`print`) are in a scope around the program's, so `var print = 1;` is allowed and hides the built-in. Should a declaration be allowed to shadow a built-in, or should that be an error?**

A2: I changed `print` to `write` and introduced `writeLn`. Otherwise, built-ins should not be privileged in any way, so yes, allow shadowing.

**Q3: Until functions are values, naming a built-in other than to call it is an error, and so is calling anything but a built-in, or with the wrong number of arguments: all WS3001, at compile time. Do they want codes of their own (say WS2005 "not a function", WS2006 "wrong number of arguments")?**

A3: Yes. However, make sure that functions can be values (add to TO-DO?)

**Q4: Evaluation is left to right, and an assignment evaluates its value before it stores it. Agreed?**

A4: Yes.

## Bytecode Choices (M0.6.5)

Where the specification is silent or only proposes, the M0.6.5 bytecode made choices of its own, recorded as Proposed in the specification's section 15.2 (rows "Instruction model" to "Diagnostic codes"). The bytecode is an in-memory model, not WBC, so none of them freezes a format.

**Q1: The instruction set is 13 operations with up to four integer operands each, locals in the low registers and temporaries above them, and at most 65535 registers per function (section 5.3's proposed limit, enforced as WS4002). Does that suit you as M0.6's model, until the WBC encoding is decided?**

A1: Yes.

**Q2: Besides what section 5.5 asks, the verifier rejects nonzero unused operands, code that can run off its end, and a function whose check would need more than 16 million words of state or 100 million steps (rejected, rather than verified slowly). Are these bounds right?**

A2: Sure, but make them adjustable via constants, as they too are arbitrary.

**Q3: The new diagnostic code is WS4002 (program too large for the bytecode). May it keep this code?**

A3: Yes.

## Runtime and CLI Choices (M0.6.6)

Where the specification is silent, the M0.6.6 runtime, VM and CLI made choices of their own, recorded as Proposed in the specification's section 15.2 (rows "Runtime values" to "Diagnostic codes").

**Q1: Arithmetic: an int64 meeting a binary64 becomes a binary64 (`10 / 4 + 1` is 3.5), and binary64 results follow IEEE 754, so `1 / 0` is `Infinity` and `0 / 0` is `NaN`, with no error; only integer overflow is an error. Or should mixing need an explicit conversion, or division by zero be an error?**

A1: Mixing allowed within reason. Division by zero `Infinity` (1/0) or `NaN` (0/0). There need to be literals that mean `Infinity` and `NaN`.

**Q2: The text of a number (what `write` writes): an int64 in digits; a binary64 as the shortest decimal that reads back as the same number, always with a point or an exponent (`5.0`, `0.1`, `-0.0`, `1E+21`), and `NaN`, `Infinity`, `-Infinity`. Is that the format you want (JavaScript would write `5`, `1e+21`)?**

A2: Yes.

**Q3: It is .NET's invariant `double.ToString("R")` that gives that text, so the Native C# runtime must give exactly the same, an obligation now in the portability inventory (`WebLingo/eng/portability/NativeCompatibility.md`, "Planned"). Is it acceptable for the core to rely on it until then?**

A3: Yes.

**Q4: The CLI's exit codes: 0 success, 1 compilation errors, 2 a usage error or an unreadable file, 3 a runtime error (M0.6.1's 3 meant "not implemented"); diagnostics as `file(line,column): error CODE: message`; `write`'s lines end in LF on every platform; `--version` says `WebLingo M0.6 (pre-alpha)`. Agreed?**

A4: 2 seems to mean two different things. Separate them to make interpretation of the codes easier. Also note the change: `print` --> `write`.

**Q5: The new diagnostic codes are WS3002 (integer overflow), WS3003 (budget exceeded) and WS3004 (cancelled). May they keep these codes? And a reading before initialization at run time keeps WS2004, from the compile-time 2xxx range: is that right, or should it have a runtime code of its own?**

A5: Yes.

## Update the CI Actions Off Node.js 20

CI's first run (12 November 2026, commit `5229806`) passed, with a warning on both jobs: `actions/checkout@v4` and `actions/setup-dotnet@v4` target Node.js 20, which GitHub has deprecated and now forces onto Node.js 24. Move `.github/workflows/build.yml` to the actions' current major versions, which run on Node.js 24, checking the versions on their release pages first (`AGENTS.md`, section 4). It is tried on the next push, which needs the user's go-ahead. A notice says `ubuntu-latest` is moving to Ubuntu 26 (from 19 October 2026); nothing to do for that but watch the next runs. Added by Claude, 2026-11-12, from that run's annotations.

## [QUESTION] What M0.7 Covers

M0.7 is "Conformance suite expansion" (specification, section 9), with no detail yet. Expanding the conformance suite needs language beyond M0.6's subset, and most of it needs syntax the specification does not give yet. Added by Claude, 2026-11-12, from the roadmap (`Documents/implementation.md`, section 13).

**Q: Which features should M0.7 bring, and in what order? My proposal, from what the specification already decides: (1) blocks and nested scopes, whose semantics are approved (block scope, shadowing, the temporal dead zone, section 3.1); (2) `true`, `false` and `null` literals, comparisons, and `if`/`else` with strict Boolean conditions (section 3.2); (3) `while` loops, which also give the stack VM benchmark something to measure; (4) string literals, whose observable semantics are approved (section 3.3) but whose syntax and escapes are not. Each needs its syntax decided first; shall I propose it, feature by feature, as questions here?**

A: Yes, propose here.

## [IDLE] Stack VM Prototype and Benchmark (WS-BC002)

The specification keeps the choice between a register VM and a stack VM provisional until both run equivalent workloads from the same lowered IR (sections 5.2, 5.6, and open question 1). Build a stack code generator from `WsIr`, a stack VM sharing `WebLingo.Runtime`'s semantics, and a benchmark comparing them (throughput, bytecode size, allocation). Waits for loops and calls in the language (see "What M0.7 Covers"): on M0.6's straight-line programs a benchmark would measure little. Added by Claude, 2026-11-12, from the roadmap.

2026-11-12: waits for loops and calls in the language, which wait for the answer to "What M0.7 Covers".

# END-OF-TO-DO
