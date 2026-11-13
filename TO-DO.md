# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] Shallow `eng/portability/` Folder

Since "Duplicated Files" (`DONE.md`), documents live in `Documents/`, and the folder structure is kept shallow (`Documents/implementation.md`, section 3). One nested document folder is left: `WebLingo/eng/portability/`, holding only `NativeCompatibility.md`, the portability inventory. The specification names that path in its build conventions (section 8.5, approved with M0.6) and proposes `eng/scripts/` and `eng/ci/` beside it (section 8.2), so I left it where it is.

**Q: Shall `NativeCompatibility.md` move to `Documents/`, with the specification's section 8.5 changed to match and `eng/` removed, or stay in `WebLingo/eng/portability/` as the specification has it?**

## [QUESTION] Verify the M0.6.1 Scaffold

The specification's next activity (section 9): validate M0.6.1 with `dotnet restore`, `dotnet build` and `dotnet test` on Windows and Linux. Then update what reports its verification as pending: `WebLingo/README.md` ("Verification status"), `Documents/M0.6.1-status.md`, and the specification's sections 9 and 14.

Windows done (Claude, 2026-11-12, `f0ee564`): a fresh clone of `main`, with .NET SDK 10.0.404, restores, builds in Release with no warnings, and passes its 5 tests, and `weblingo --version` answers. `WebLingo/README.md`, `Documents/M0.6.1-status.md` and the specification (header, section 9, section 14) say so, and say that Linux is not verified yet.

Linux waits for the CI workflow's first run (`.github/workflows/build.yml`, since "Make the CI Workflow Run"), which runs on a push. There is no Linux here to try it on.

**Q: May I push `main` to GitHub (`optimax/WebLingo`), so that CI runs on Linux and Windows? Everything committed since `e45fa73` would go: the agent files, `Documents/implementation.md`, the one specification, the CI move, and the M0.6 work done by then. Pushing needs your go-ahead (`AGENTS.md`, section 16).**

## [QUESTION] Lexical Choices (M0.6.2)

Where the specification is silent, the M0.6.2 lexer made choices of its own, recorded as Proposed in the specification's section 15.2 (rows "Line terminators" to "Diagnostic codes"). Each is made in one place, so changing one is small.

**Q1: Do these suit you, or which do you want otherwise? Lines end at CRLF, LF or CR; a column counts Unicode scalar values (a tab is one); whitespace is space, tab, CR and LF only; comments are `//` and `/* */` (not nested); identifiers are ASCII letters, digits and `_`; integer literals are decimal, leading zeros allowed (`007` is 7), at most 9223372036854775807; digits followed by a letter are one invalid literal, which keeps `10m` free for decimals.**

**Q2: The new diagnostic codes are WS1003 (invalid character), WS1004 (invalid numeric literal) and WS1005 (unterminated comment). May they keep these codes?**

## [QUESTION] Syntax Choices (M0.6.3)

Where the specification is silent, the M0.6.3 parser made choices of its own, recorded as Proposed in the specification's section 15.2 (rows "Statements" to "Diagnostic codes").

**Q1: Do these suit you, or which do you want otherwise? No empty statement (a lone `;` is an error); unary `-` but no unary `+`; assignment is an expression (`a = b = 3`, and `print(x = 5)` prints 5) whose target must be a plain variable name; calls take no trailing comma; `const x;` is a syntax error.**

**Q2: Nesting limits: parsing recurses at most 256 levels, and an expression is at most 1000 operations deep, beyond which the source is an error (WS1008) rather than a crash. Are these limits right, or do you want others?**

**Q3: The new diagnostic codes are WS1006 (`const` without a value), WS1007 (invalid assignment target) and WS1008 (nested too deeply). May they keep these codes?**

## [QUESTION] Binding Choices (M0.6.4)

Where the specification is silent, the M0.6.4 binder and lowering made choices of their own, recorded as Proposed in the specification's section 15.2 (rows "Compilation stages" to "Diagnostic codes").

**Q1: The temporal dead zone at run time: reading a variable before its declaration has run, or a `var x;` before it is assigned, is a runtime error (WS2004), as the specification's `var x; print(x);` example says; so is assigning a variable before its declaration has run (as JavaScript does). Neither is reported at compile time, though in M0.6's straight-line programs the compiler could prove both. Is that what you want, or should provable cases be compile-time errors (or warnings)?**

**Q2: The host's built-ins (`print`) are in a scope around the program's, so `var print = 1;` is allowed and hides the built-in. Should a declaration be allowed to shadow a built-in, or should that be an error?**

**Q3: Until functions are values, naming a built-in other than to call it is an error, and so is calling anything but a built-in, or with the wrong number of arguments: all WS3001, at compile time. Do they want codes of their own (say WS2005 "not a function", WS2006 "wrong number of arguments")?**

**Q4: Evaluation is left to right, and an assignment evaluates its value before it stores it. Agreed?**

## [QUESTION] Bytecode Choices (M0.6.5)

Where the specification is silent or only proposes, the M0.6.5 bytecode made choices of its own, recorded as Proposed in the specification's section 15.2 (rows "Instruction model" to "Diagnostic codes"). The bytecode is an in-memory model, not WBC, so none of them freezes a format.

**Q1: The instruction set is 13 operations with up to four integer operands each, locals in the low registers and temporaries above them, and at most 65535 registers per function (section 5.3's proposed limit, enforced as WS4002). Does that suit you as M0.6's model, until the WBC encoding is decided?**

**Q2: Besides what section 5.5 asks, the verifier rejects nonzero unused operands, code that can run off its end, and a function whose check would need more than 16 million words of state or 100 million steps (rejected, rather than verified slowly). Are these bounds right?**

**Q3: The new diagnostic code is WS4002 (program too large for the bytecode). May it keep this code?**

## [QUESTION] Runtime and CLI Choices (M0.6.6)

Where the specification is silent, the M0.6.6 runtime, VM and CLI made choices of their own, recorded as Proposed in the specification's section 15.2 (rows "Runtime values" to "Diagnostic codes").

**Q1: Arithmetic: an int64 meeting a binary64 becomes a binary64 (`10 / 4 + 1` is 3.5), and binary64 results follow IEEE 754, so `1 / 0` is `Infinity` and `0 / 0` is `NaN`, with no error; only integer overflow is an error. Or should mixing need an explicit conversion, or division by zero be an error?**

**Q2: The text of a number (what `print` writes): an int64 in digits; a binary64 as the shortest decimal that reads back as the same number, always with a point or an exponent (`5.0`, `0.1`, `-0.0`, `1E+21`), and `NaN`, `Infinity`, `-Infinity`. Is that the format you want (JavaScript would write `5`, `1e+21`)?**

**Q3: It is .NET's invariant `double.ToString("R")` that gives that text, so the Native C# runtime must give exactly the same, an obligation now in the portability inventory (`WebLingo/eng/portability/NativeCompatibility.md`, "Planned"). Is it acceptable for the core to rely on it until then?**

**Q4: The CLI's exit codes: 0 success, 1 compilation errors, 2 a usage error or an unreadable file, 3 a runtime error (M0.6.1's 3 meant "not implemented"); diagnostics as `file(line,column): error CODE: message`; `print`'s lines end in LF on every platform; `--version` says `WebLingo M0.6 (pre-alpha)`. Agreed?**

**Q5: The new diagnostic codes are WS3002 (integer overflow), WS3003 (budget exceeded) and WS3004 (cancelled). May they keep these codes? And a reading before initialization at run time keeps WS2004, from the compile-time 2xxx range: is that right, or should it have a runtime code of its own?**

## [IDLE] M0.6.7: End-to-End Conformance on Windows and Linux

The conformance tests of the specification, section 8.6, end to end, on Windows and Linux, and the M0.6 acceptance criteria of section 8.8.

2026-11-12: waits for the CI workflow's first run on Linux, which waits for a push ("Verify the M0.6.1 Scaffold", its question). Windows is done.

Windows done (Claude, 2026-11-12, `93df634`):

- **`WebLingo.Conformance.Tests`**, a new project, runs every program of section 8.6 as a file through the `weblingo` command line, and checks its output, its diagnostic (code, line and column) and its exit code. It also checks that section 8.4's example and `samples/Arithmetic` print `30`, that LF, CRLF and CR give the same results and positions, and, by fuzzing, that 5,000 random sources give diagnostics (or run) and 5,000 mutated modules fail verification (or run), never a crash.
- **`DependencyTests`** (`WebLingo.Architecture.Tests`) checks the built portable assemblies against section 8.3's dependency rules, and that none of them uses ASP.NET Core, ADO.NET, `Reflection.Emit` or `System.Net.Http`.
- **Acceptance criteria:** the specification's section 15.1 now gives each of section 8.8's its status; all are met on Windows, and "Builds cleanly on Windows and Linux" waits for Linux.
- 294 tests, all passing on Windows.

## [QUESTION] What M0.7 Covers

M0.7 is "Conformance suite expansion" (specification, section 9), with no detail yet. Expanding the conformance suite needs language beyond M0.6's subset, and most of it needs syntax the specification does not give yet. Added by Claude, 2026-11-12, from the roadmap (`Documents/implementation.md`, section 13).

**Q: Which features should M0.7 bring, and in what order? My proposal, from what the specification already decides: (1) blocks and nested scopes, whose semantics are approved (block scope, shadowing, the temporal dead zone, section 3.1); (2) `true`, `false` and `null` literals, comparisons, and `if`/`else` with strict Boolean conditions (section 3.2); (3) `while` loops, which also give the stack VM benchmark something to measure; (4) string literals, whose observable semantics are approved (section 3.3) but whose syntax and escapes are not. Each needs its syntax decided first; shall I propose it, feature by feature, as questions here?**

## [IDLE] Stack VM Prototype and Benchmark (WS-BC002)

The specification keeps the choice between a register VM and a stack VM provisional until both run equivalent workloads from the same lowered IR (sections 5.2, 5.6, and open question 1). Build a stack code generator from `WsIr`, a stack VM sharing `WebLingo.Runtime`'s semantics, and a benchmark comparing them (throughput, bytecode size, allocation). Waits for loops and calls in the language (see "What M0.7 Covers"): on M0.6's straight-line programs a benchmark would measure little. Added by Claude, 2026-11-12, from the roadmap.

2026-11-12: waits for loops and calls in the language, which wait for the answer to "What M0.7 Covers".

# END-OF-TO-DO
