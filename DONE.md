# DONE WebLingo

The tasks from [`TO-DO.md`](TO-DO.md) that are done, each moved to the end of this file when it was marked [DONE] ([`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md)).

## [DONE] Duplicated Files

Two files exist twice in the repository (`Documents/implementation.md`, section 11):

- the architecture specification, `Documents/weblingo-architecture.md` and `WebLingo/docs/architecture/weblingo-architecture.md`, identical on 2026-11-12; the second is the canonical one (`WebLingo/docs/architecture/README.md`);
- `.gitignore`: the root one covers the whole repository, and `WebLingo/.gitignore` is a subset of it.

Q1: Shall `Documents/weblingo-architecture.md` be removed, leaving the canonical copy only, or is it kept on purpose (as your working copy, say), and if so, which copy do you edit?

A1: Only one should remain. Keep the folder structure shallow, so no `architecture `folder.

Q2: Shall `WebLingo/.gitignore` be removed, now that the root `.gitignore` covers it, or kept so that the solution folder has its own (for example, for when it becomes the public repository)?

A2: Yes, remove the redunadant one. Keep in mind that the entire repository can become public so don't put anything in there that should be kept private.

Done (Claude, 2026-11-12, `cb17fed`):

- **The specification** exists once, as `Documents/weblingo-architecture.md`, the copy at the shallower place. The copy in `WebLingo/docs/architecture/` is gone, with that folder's `README.md`, whose one rule (update the specification when a decision is approved) is in `Documents/implementation.md`, section 5. `M0.6.1-status.md` moved to `Documents/`, so `WebLingo/docs/` is gone too. `CLAUDE.md` and `WebLingo/README.md` point at the remaining copy.
- **`WebLingo/.gitignore`** is gone; the root `.gitignore` covers the solution (a build leaves `git status` clean).
- **Your decisions are recorded:** in the specification's decision register and section 8.2, and in `Documents/implementation.md` (shallow folders in section 3; the whole repository may become public in section 10, with what that rules out).
- The other nested document folder, `WebLingo/eng/portability/`, is a question of its own: "Shallow `eng/portability/` Folder".

## [DONE] Make the CI Workflow Run

`WebLingo/.github/workflows/build.yml` builds and tests on Windows and Linux, but GitHub Actions does not run it: it is in the solution folder, and Actions reads workflows only from `.github/workflows` at the repository root. Its commands also expect `WebLingo.slnx` at the root (`Documents/implementation.md`, section 11). Move it to the root and point its commands at the solution folder, so that M0.8's Windows and Linux CI (specification, section 9) has a start. It first runs on a push, which needs the user's go-ahead.

Done (Claude, 2026-11-12, `ea07107`):

- The workflow is `.github/workflows/build.yml` at the repository root. Its steps run in the solution folder (`working-directory: WebLingo`), and `fail-fast: false` lets both platforms report their results when one fails.
- Its three commands, run locally as written from the solution folder on Windows, pass: restore, Release build with no warnings, 5 tests.
- **The workflow itself has not run:** it runs on a push, which waits for your go-ahead (see "Verify the M0.6.1 Scaffold"). `Documents/implementation.md`, section 11, "CI Has Not Run Yet", says so until it has.

## [DONE] M0.6.2: Lexer and Tokenization Tests

The lexer of the specification, section 8.4 ("Lexer"): identifiers, the keywords `var` and `const`, literals, operators, punctuation, source spans, and diagnostics for malformed input, with tests. It replaces the M0.6.1 lexer seed in `WebLingo.Language`.

Done (Claude, 2026-11-12, `c20e0c2`):

- **`WsLexer`** reads identifiers, `var`, `const` and the reserved `let`, decimal integer literals (with their values, checked against int64), `+ - * / =`, and `; ( ) ,`, and skips whitespace and `//` and `/* */` comments. Malformed input gives a `Bad` token and a diagnostic, and lexing goes on, so one pass reports every lexical error: WS1003 invalid character, WS1004 invalid numeric literal, WS1005 unterminated comment.
- **`WsSourceText`** (`WebLingo.Core`) turns positions into one-based lines and columns, for the diagnostics of every later stage.
- **Literals:** M0.6's parser subset needs integer literals only. String, binary64 and decimal literals are not lexed, since the specification does not give their syntax yet: their characters are invalid. So `samples/HelloWorld/hello.ws`, which uses a string, will not compile in M0.6.
- **The choices** where the specification is silent are in its section 15.2, as Proposed, and asked about in "Lexical Choices (M0.6.2)".
- Tests: 53 new (lexer and source text), all passing on Windows; Linux not tried.

## [DONE] M0.6.3: Parser, AST and Diagnostics

The parser of the specification, section 8.4 ("Parser"): the initial subset of variable declarations, integer literals, identifiers, arithmetic, parentheses and mandatory semicolons, with source spans preserved and enough recovery to report several errors, and the diagnostics of section 8.4.

Done (Claude, 2026-11-12, `70081d4`):

- **`WsParser`** parses `var` and `const` declarations, with or without a value, and expression statements: integer literals, names, unary `-`, `* /` above `+ -`, parentheses, assignment and calls, each statement ended by a mandatory `;`. **`WsSyntax.cs`** is the tree; every node has its source span.
- **Diagnostics:** WS1001 unexpected token (with a hint when `let` is used), WS1002 missing semicolon (where the `;` belongs), and the new WS1006 `const` without a value, WS1007 invalid assignment target, and WS1008 nested too deeply. Recovery resumes at the next `;` or declaration, a lexical error is not reported again, and the diagnostics come in source order.
- **No crash on deep source:** recursion is limited to 256 levels and expressions to 1000 operations deep, so 100,000 nested parentheses or a 100,000-term sum is a diagnostic, not a stack overflow (the later stages walk the tree recursively).
- **`print(...)`** parses as an ordinary call; binding decides what `print` is (M0.6.4).
- **The choices** where the specification is silent are in its section 15.2, as Proposed, and asked about in "Syntax Choices (M0.6.3)".
- Tests: 47 new parser tests, all passing on Windows; Linux not tried.

## [DONE] M0.6.4: Binder and Lowered IR

The binder and the lowered IR of the specification, section 8.4 ("Binder/IR"): lexical symbols and the temporal dead zone resolved, a bound tree, and a lowered IR independent of the choice between a register VM and a stack VM (WS-BC002).

Done (Claude, 2026-11-12, `6228d5a`):

- **`WsBinder`** resolves names lexically into a bound tree. A block's declarations are known from its start, so a name used before its declaration refers to it (and the temporal dead zone makes that a runtime error). The host's built-ins come in through `WsCompilerOptions`, in a scope around the program's. Errors: WS2001 undefined, WS2002 duplicate (at the later name), WS2003 assignment to a constant, and WS3001 for an invalid call or a built-in used as a value; binding goes on after an error.
- **`WsLowerer`** gives the lowered IR, a tree that is neither register- nor stack-shaped. It follows each variable from its dead zone to declared-without-value to assigned, and keeps a runtime check only where a read or store is not known to be safe.
- **`WsCompilation`** runs parse, bind and lower, and stops at the first stage that reports an error.
- **`WebLingo.Compiler.Tests`**, a new test project: 32 tests, all passing on Windows; Linux not tried.
- **The choices** where the specification is silent are in its section 15.2, as Proposed, and asked about in "Binding Choices (M0.6.4)".

## [DONE] M0.6.5: Minimal Bytecode Generator and Verifier

The bytecode generator and the verifier of the specification, section 8.4: an executable in-memory module (serialization waits until the WBC encoding is decided), and a verifier that rejects invalid opcodes, operands, branches, constants and uninitialized temporaries.

Done (Claude, 2026-11-12, `30523da`):

- **The bytecode** (`WebLingo.Bytecode`, `WsBytecodeContracts.cs`) is an in-memory register instruction model, not WBC: 13 operations with operands A to D, a constant table, the built-ins a module imports by name and arity, and one function, whose instructions each have a source span, with its locals' names, for runtime messages. It replaces the M0.6.1 placeholder.
- **`WsCodeGenerator`** puts locals in registers 0 to n − 1 and temporaries above them, writes a result straight into the local that wants it, and copies a local only when a later operand assigns it, so evaluation stays left to right. **`WsCompiler`** runs the whole compiler and verifies its own output.
- **`WsBytecodeVerifier`** rejects unknown operations, operands out of range (registers, constants, imports, argument counts, branch targets), nonzero unused operands, code that can run off its end, and, by dataflow over every path, a temporary read before it is written. Its memory and steps are bounded, so a hostile module is rejected, never a crash or exhausted memory; a fuzz test of 20,000 random modules checks that it never throws.
- **`WsDisassembler`** shows a module as text (for tests now, and a later `weblingo disasm`).
- **`WebLingo.Bytecode.Tests`**, a new test project: 32 tests; and 15 code generator tests. All pass on Windows; Linux not tried.
- **The choices** where the specification is silent are in its section 15.2, as Proposed, and asked about in "Bytecode Choices (M0.6.5)".

## [DONE] M0.6.6: Minimal Register VM, Arithmetic, Locals and `print`

The VM of the specification, section 8.4 ("VM", "Runtime values"): execution of verified modules in an execution context (owner worker, cancellation, instruction and resource budgets, runtime services), null, Boolean, int64 and binary64 values, and `print` as a built-in the CLI registers, so that `weblingo run` runs section 8.4's example and prints `30`.

Done (Claude, 2026-11-12, `05fb485`):

- **`weblingo run samples/Arithmetic/arithmetic.ws` prints `30`.** `weblingo check` compiles and reports without running. Diagnostics read `file(line,column): error CODE: message`, and the exit code tells success (0), compilation errors (1), a usage error or unreadable file (2), and a runtime error (3).
- **Runtime values** (`WebLingo.Runtime`): `WsValue`, 16 bytes as section 4.1 targets, holds null, Boolean, int64 and binary64. **`WsOperators`** is the arithmetic every VM will share: checked int64 (overflow is WS3002), `/` giving binary64 (10 / 2 is 5.0), IEEE 754 for binary64. **`WsValueFormatter`** gives a value's text, never culture-dependent.
- **`WsVirtualMachine`** (`WebLingo.VM`) verifies a module, links its imports to the host's built-ins, and runs it in an execution context with its owner worker, a cancellation signal (WS3004), instruction and register budgets (WS3003), and the runtime services. A runtime error stops it, reported at the instruction's source span; nothing a module or a built-in does can crash it.
- **`print`** is `WsPrintFunction` in `WebLingo.StdLib`, registered by the CLI (`WsCommandLine`), which is the host.
- **Not runnable in M0.6:** `samples/HelloWorld` (a string) and `samples/SimpleWebsite` (a template).
- **Tests:** `WebLingo.Runtime.Tests` (45) and `WebLingo.VM.Tests` (25), new projects; all 254 tests pass on Windows; Linux not tried. The CLI was run by hand on the samples and on files with compilation and runtime errors, for each exit code.
- **The choices** where the specification is silent are in its section 15.2, as Proposed, and asked about in "Runtime and CLI Choices (M0.6.6)". The portability inventory now lists the facilities M0.6 uses.

## [DONE] M0.8: Machine-Checked Portability Inventory

The approved WS-P016 asks for the portability inventory and CI checks (specification, sections 7.14 and 1.1), and the inventory says automated API checks remain future work. Check the portable core's built assemblies against `WebLingo/eng/portability/NativeCompatibility.md` in a test, so that CI fails when portable code uses what the inventory rules out: `System.IO` file access, the console, `Reflection.Emit`, P/Invoke, OS threads, .NET `Task`/`ValueTask` and `CancellationToken`, ADO.NET, ASP.NET Core, `System.Net.Http`, and LINQ (which the inventory keeps out). Added by Claude, 2026-11-12, from the specification's roadmap (`Documents/implementation.md`, section 13).

Done (Claude, 2026-11-12, `37d2925`):

- **`PortabilityTests`** (`WebLingo.Architecture.Tests`) reads the type references of the ten portable assemblies, and fails on any type the inventory rules out for the core, or on a P/Invoke. Besides the list above, it rules out `System.IO.Path` and `System.Environment`, which tie code to the platform. A self-test checks that it does find such uses (in its own assembly, which reads files and uses LINQ).
- **The inventory** has a row for every rule the test enforces, and says what is checked, where a new rule goes, and that its "Planned" rows are obligations on the Native C# toolchain, not checks.
- **The specification:** M0.8 is "Started" in section 9: CI configured (not yet run), and the dependency and portability checks in place.
- All 305 tests pass on Windows; Linux not tried.

## [DONE] Verify the M0.6.1 Scaffold

The specification's next activity (section 9): validate M0.6.1 with `dotnet restore`, `dotnet build` and `dotnet test` on Windows and Linux. Then update what reports its verification as pending: `WebLingo/README.md` ("Verification status"), `Documents/M0.6.1-status.md`, and the specification's sections 9 and 14.

Windows done (Claude, 2026-11-12, `f0ee564`): a fresh clone of `main`, with .NET SDK 10.0.404, restores, builds in Release with no warnings, and passes its 5 tests, and `weblingo --version` answers. `WebLingo/README.md`, `Documents/M0.6.1-status.md` and the specification (header, section 9, section 14) say so, and say that Linux is not verified yet.

Linux waits for the CI workflow's first run (`.github/workflows/build.yml`, since "Make the CI Workflow Run"), which runs on a push. There is no Linux here to try it on.

Q: May I push `main` to GitHub (`optimax/WebLingo`), so that CI runs on Linux and Windows? Everything committed since `e45fa73` would go: the agent files, `Documents/implementation.md`, the one specification, the CI move, and the M0.6 work done by then. Pushing needs your go-ahead (`AGENTS.md`, section 16).

A: Yes.

Done (Claude, 2026-11-12): `main` was pushed (`e45fa73..5229806`), and CI's first run passed on both `ubuntu-latest` and `windows-latest`: restore, Release build and tests, in 1 minute 29 seconds. So M0.6.1, and everything up to `5229806`, builds and passes its tests on Linux. `WebLingo/README.md`, `Documents/M0.6.1-status.md`, the specification (header, sections 9, 14 and 15), the portability inventory and `Documents/implementation.md` (section 11, now "CI Runs Only on a Push") say so. The run's two warnings (actions on Node.js 20) are the task "Update the CI Actions Off Node.js 20". The logs, with the test counts, need a signed-in GitHub account and were not read; the steps' success is what was checked.

## [DONE] M0.6.7: End-to-End Conformance on Windows and Linux

The conformance tests of the specification, section 8.6, end to end, on Windows and Linux, and the M0.6 acceptance criteria of section 8.8.

Windows done (Claude, 2026-11-12, `93df634`):

- **`WebLingo.Conformance.Tests`**, a new project, runs every program of section 8.6 as a file through the `weblingo` command line, and checks its output, its diagnostic (code, line and column) and its exit code. It also checks that section 8.4's example and `samples/Arithmetic` print `30`, that LF, CRLF and CR give the same results and positions, and, by fuzzing, that 5,000 random sources give diagnostics (or run) and 5,000 mutated modules fail verification (or run), never a crash.
- **`DependencyTests`** (`WebLingo.Architecture.Tests`) checks the built portable assemblies against section 8.3's dependency rules, and that none of them uses ASP.NET Core, ADO.NET, `Reflection.Emit` or `System.Net.Http`.
- **Acceptance criteria:** the specification's section 15.1 now gives each of section 8.8's its status.
- 294 tests, all passing on Windows.

Linux done (Claude, 2026-11-12): CI's first run, at `5229806`, which includes all of M0.6.7, passed on `ubuntu-latest`. Every acceptance criterion of section 8.8 is now met, "Builds cleanly on Windows and Linux" included (specification, section 15.1).

## [DONE] Shallow `eng/portability/` Folder

Since "Duplicated Files" (`DONE.md`), documents live in `Documents/`, and the folder structure is kept shallow (`Documents/implementation.md`, section 3). One nested document folder is left: `WebLingo/eng/portability/`, holding only `NativeCompatibility.md`, the portability inventory. The specification names that path in its build conventions (section 8.5, approved with M0.6) and proposes `eng/scripts/` and `eng/ci/` beside it (section 8.2), so I left it where it is.

Q: Shall `NativeCompatibility.md` move to `Documents/`, with the specification's section 8.5 changed to match and `eng/` removed, or stay in `WebLingo/eng/portability/` as the specification has it?

A: Move to `Documents\`.

Done (Claude, 2026-11-12): the inventory is now `Documents/NativeCompatibility.md`, `WebLingo/eng/` is gone, and the specification (sections 8.2, 8.5 and 15.1), `Documents/implementation.md` and `PortabilityTests` say so. Build and 296 tests pass on Windows; Linux not tried.

## [DONE] Lexical Choices (M0.6.2)

Where the specification is silent, the M0.6.2 lexer made choices of its own, recorded as Proposed in the specification's section 15.2 (rows "Line terminators" to "Diagnostic codes"). Each is made in one place, so changing one is small.

Q1: Do these suit you, or which do you want otherwise? Lines end at CRLF, LF or CR; a column counts Unicode scalar values (a tab is one); whitespace is space, tab, CR and LF only; comments are `//` and `/* */` (not nested); identifiers are ASCII letters, digits and `_`; integer literals are decimal, leading zeros allowed (`007` is 7), at most 9223372036854775807; digits followed by a letter are one invalid literal, which keeps `10m` free for decimals.

A1: I made comments inline, in the document. If there are follow up questions, bring them up here, not in the document. Redact the document in-place with agreed upon choices without keeping history of those choices.

Q2: The new diagnostic codes are WS1003 (invalid character), WS1004 (invalid numeric literal) and WS1005 (unterminated comment). May they keep these codes?

A: Yes.

Done (Claude, 2026-11-12): no comments were found on the lexical rows of the specification's section 15.2, so they stand as written and are marked Approved, as are WS1003 to WS1005 (section 8.4, which now gives each code its status). Nothing in the code changed. Build and tests pass on Windows; Linux not tried.

## [DONE] Syntax Choices (M0.6.3)

Where the specification is silent, the M0.6.3 parser made choices of its own, recorded as Proposed in the specification's section 15.2 (rows "Statements" to "Diagnostic codes").

Q1: Do these suit you, or which do you want otherwise? No empty statement (a lone `;` is an error); unary `-` but no unary `+`; assignment is an expression (`a = b = 3`, and `print(x = 5)` prints 5) whose target must be a plain variable name; calls take no trailing comma; `const x;` is a syntax error.

A1: NO, I commented in the document where appropriate. Again, redact the document in-place with agreed upon choices without keeping history of those choices.

Q2: Nesting limits: parsing recurses at most 256 levels, and an expression is at most 1000 operations deep, beyond which the source is an error (WS1008) rather than a crash. Are these limits right, or do you want others?

A2: They are arbitrary, so make them easy to change constants in the implementation. 

Q3: The new diagnostic codes are WS1006 (`const` without a value), WS1007 (invalid assignment target) and WS1008 (nested too deeply). May they keep these codes?

A3: Yes.

Done (Claude, 2026-11-12): from the comment in the specification's "Operators" row, unary `+` is now accepted. It gives the number itself and is WS3001 on anything else, through a new `Plus` operation (parser, code generator, verifier, disassembler, VM, `WsOperators.TryPlus`), with tests. The code went into `06c82f1`, together with the lexical approval. The other syntax rows of section 15.2 had no comments, and stand as written. All of them, and WS1006 to WS1008, are marked Approved. The nesting limits were already the constants `WsParser.MaxRecursionDepth` and `MaxExpressionDepth`, and the specification now says so. Build and 300 tests pass on Windows; Linux not tried.

## [DONE] Binding Choices (M0.6.4)

Where the specification is silent, the M0.6.4 binder and lowering made choices of their own, recorded as Proposed in the specification's section 15.2 (rows "Compilation stages" to "Diagnostic codes").

Q1: The temporal dead zone at run time: reading a variable before its declaration has run, or a `var x;` before it is assigned, is a runtime error (WS2004), as the specification's `var x; print(x);` example says; so is assigning a variable before its declaration has run (as JavaScript does). Neither is reported at compile time, though in M0.6's straight-line programs the compiler could prove both. Is that what you want, or should provable cases be compile-time errors (or warnings)?

A1 (the user, in chat, 2026-11-12): No temporal dead zone. A name means its outer meaning until its declaration, which takes effect after its initializer; and every declaration needs an initializer, so `var x;` is a syntax error (WS1006).

Done (Claude, 2026-11-12, not yet committed): the specification's sections 1.1, 3.1, 8.4, 8.6 and 15.2 say so, and the code follows. `print(x); var x = 1;` and `var x = x + 1;` (with no outer `x`) are compile-time errors (WS2001), the VM's dead-zone checks are gone, and the verifier now requires every register, locals included, to be written before it is read. WS2004 is no longer reported. 296 tests pass on Windows.

Q2: The host's built-ins (`print`) are in a scope around the program's, so `var print = 1;` is allowed and hides the built-in. Should a declaration be allowed to shadow a built-in, or should that be an error?

A2: I changed `print` to `write` and introduced `writeLn`. Otherwise, built-ins should not be privileged in any way, so yes, allow shadowing.

Q3: Until functions are values, naming a built-in other than to call it is an error, and so is calling anything but a built-in, or with the wrong number of arguments: all WS3001, at compile time. Do they want codes of their own (say WS2005 "not a function", WS2006 "wrong number of arguments")?

A3: Yes. However, make sure that functions can be values (add to TO-DO?)

Q4: Evaluation is left to right, and an assignment evaluates its value before it stores it. Agreed?

A4: Yes.

Done (Claude, 2026-11-12): Q1 as recorded above. Q2: `print` is now `write`, with no line break, and `writeLn` writes a line break after the value (`WsWriteFunction`, `WsWriteLnFunction`). The output contract writes text as it comes (`IWsTextOutput.Write`). Samples, tests and documents use them. I guessed that `writeLn` takes one argument, like `write`. Built-ins stay unprivileged, and may be shadowed. Q3: calling what is not a function is WS2005, and calling with the wrong number of arguments is WS2006. Naming a built-in other than to call it stays WS3001 until functions are values, which is the new task "Functions as Values". Q4: approved. The "Checks" row is gone, per the comment on it. The specification's section 15.2 marks these rows Approved, and section 8.4 lists WS2005 and WS2006. `samples/Arithmetic` prints 30. `samples/HelloWorld` needs string literals, so it still does not compile. Build and 303 tests pass on Windows; Linux not tried.

## [DONE] Bytecode Choices (M0.6.5)

Where the specification is silent or only proposes, the M0.6.5 bytecode made choices of its own, recorded as Proposed in the specification's section 15.2 (rows "Instruction model" to "Diagnostic codes"). The bytecode is an in-memory model, not WBC, so none of them freezes a format.

Q1: The instruction set is 13 operations with up to four integer operands each, locals in the low registers and temporaries above them, and at most 65535 registers per function (section 5.3's proposed limit, enforced as WS4002). Does that suit you as M0.6's model, until the WBC encoding is decided?

A1: Yes.

Q2: Besides what section 5.5 asks, the verifier rejects nonzero unused operands, code that can run off its end, and a function whose check would need more than 16 million words of state or 100 million steps (rejected, rather than verified slowly). Are these bounds right?

A2: Sure, but make them adjustable via constants, as they too are arbitrary.

Q3: The new diagnostic code is WS4002 (program too large for the bytecode). May it keep this code?

A3: Yes.

Done (Claude, 2026-11-12): the specification's section 15.2 marks the instruction model, the registers, the operations and the verification as Approved, as is WS4002 in section 8.4. It also names the constants that hold the verifier's bounds and the register limit (`MaxStateWords`, `MaxSteps`, `MaxRegisters`), which already existed, so the code did not change. Nothing to build; Linux not tried.

## [DONE] Runtime and CLI Choices (M0.6.6)

Where the specification is silent, the M0.6.6 runtime, VM and CLI made choices of their own, recorded as Proposed in the specification's section 15.2 (rows "Runtime values" to "Diagnostic codes").

Q1: Arithmetic: an int64 meeting a binary64 becomes a binary64 (`10 / 4 + 1` is 3.5), and binary64 results follow IEEE 754, so `1 / 0` is `Infinity` and `0 / 0` is `NaN`, with no error; only integer overflow is an error. Or should mixing need an explicit conversion, or division by zero be an error?

A1: Mixing allowed within reason. Division by zero `Infinity` (1/0) or `NaN` (0/0). There need to be literals that mean `Infinity` and `NaN`.

Q2: The text of a number (what `write` writes): an int64 in digits; a binary64 as the shortest decimal that reads back as the same number, always with a point or an exponent (`5.0`, `0.1`, `-0.0`, `1E+21`), and `NaN`, `Infinity`, `-Infinity`. Is that the format you want (JavaScript would write `5`, `1e+21`)?

A2: Yes.

Q3: It is .NET's invariant `double.ToString("R")` that gives that text, so the Native C# runtime must give exactly the same, an obligation now in the portability inventory (`Documents/NativeCompatibility.md`, "Planned"). Is it acceptable for the core to rely on it until then?

A3: Yes.

Q4: The CLI's exit codes: 0 success, 1 compilation errors, 2 a usage error or an unreadable file, 3 a runtime error (M0.6.1's 3 meant "not implemented"); diagnostics as `file(line,column): error CODE: message`; `write`'s lines end in LF on every platform; `--version` says `WebLingo M0.6 (pre-alpha)`. Agreed?

A4: 2 seems to mean two different things. Separate them to make interpretation of the codes easier. Also note the change: `print` --> `write`.

Q5: The new diagnostic codes are WS3002 (integer overflow), WS3003 (budget exceeded) and WS3004 (cancelled). May they keep these codes? And a reading before initialization at run time keeps WS2004, from the compile-time 2xxx range: is that right, or should it have a runtime code of its own?

A5: Yes.

Done (Claude, 2026-11-12): the specification's section 15.2 marks the arithmetic, the binary64 results, the text of a value, the invalid operands (with your comment that `+` will also concatenate strings), the CLI and the diagnostic codes as Approved, as are WS3002 to WS3004 in section 8.4. The inventory records that relying on `double.ToString("R")` is approved until Native C#. Exit code 2 now means a usage error only; a file that cannot be read exits with 4 (`WsCommandLine.FileUnreadable`; the number is my guess), with tests and the README updated. The literals for `Infinity` and `NaN` need their spelling decided: that is the new question "Literals for `Infinity` and `NaN`". Build and 307 tests pass on Windows, and the CLI was run (missing file: 4, bad usage: 2, `samples/Arithmetic`: 30); Linux not tried.

## [DONE] Update the CI Actions Off Node.js 20

CI's first run (12 November 2026, commit `5229806`) passed, with a warning on both jobs: `actions/checkout@v4` and `actions/setup-dotnet@v4` target Node.js 20, which GitHub has deprecated and now forces onto Node.js 24. Move `.github/workflows/build.yml` to the actions' current major versions, which run on Node.js 24, checking the versions on their release pages first (`AGENTS.md`, section 4). It is tried on the next push, which needs the user's go-ahead. A notice says `ubuntu-latest` is moving to Ubuntu 26 (from 19 October 2026); nothing to do for that but watch the next runs. Added by Claude, 2026-11-12, from that run's annotations.

Done (Claude, 2026-11-12): `build.yml` now uses `actions/checkout@v7` (latest v7.0.1) and `actions/setup-dotnet@v6` (latest v6.1.0). Both release pages show that these majors run on Node.js 24 (`using: node24`). Their breaking changes don't affect this workflow: checkout's only change is for `pull_request_target` and `workflow_run`, and setup-dotnet's new global.json check doesn't apply, since the workflow passes `dotnet-version: '10.0.x'`. Not run yet: CI tries it on the next push, which needs your go-ahead.

## [DONE] Literals for `Infinity` and `NaN`

The owner's answer to "Runtime and CLI Choices (M0.6.6)", Q1: there need to be literals that mean `Infinity` and `NaN` (specification, section 15.2, "Binary64 results"). Today they arise only from division (`1 / 0`, `0 / 0`), and the language has no binary64 literals at all yet (`1.5` is not lexed). Added by Claude, 2026-11-12.

Q: How are they spelled? My proposal: two keywords, `Infinity` and `NaN`, spelled as `write` shows them, each a binary64 literal; `-Infinity` is then unary minus applied to it. Being keywords, they cannot be declared or shadowed, unlike the built-ins. The alternatives are predeclared constants, which a declaration could shadow as it can `write`, or a spelling of your own (`inf`, `nan`). Shall I add them as keywords, and should binary64 literals (`1.5`, `1e21`) come in the same task?

A: Yes, and yes.

Done (Claude, 2026-11-12): `Infinity` and `NaN` are keywords, and binary64 literals are lexed: `1.5`, `1e21`, `2.5E-3`, with digits needed on both sides of a point. I guessed two details, recorded in the specification (section 15.2, "Binary64 literals"): a literal too large for a binary64 is WS1004, and one too small becomes 0. They go through the lexer, parser, binder, IR and code generator (`WsBinary64LiteralSyntax`, `WsIrBinary64Constant`; constants are kept apart by their bits, so `0.0` and `-0.0` differ). The inventory gains a row for `double.Parse`. Tests cover lexing, errors and running. Build and 327 tests pass on Windows, and the CLI runs `writeLn(1.5 * 2); writeLn(Infinity - Infinity); writeLn(2.5E-3);` as `3.0`, `NaN`, `0.0025`. Linux not tried.

## [DONE] M0.7.1: Blocks and Nested Scopes

`{ statements }` as a statement, opening a scope, with shadowing (specification, sections 3.1 and 3.5). Added by Claude, 2026-11-12.

Done (Claude, 2026-11-12): `{ … }` is a statement through every stage (`WsBlockSyntax`, `WsBoundBlock`, `WsIrBlock`), and opens a scope in the binder. Error recovery stops at a brace, so an error inside a block does not swallow its `}`. A missing `}` is WS1001, reported at its `{` (my guess). Blocks count toward the parser's nesting limit. Each local still gets its own register, and inner blocks do not reuse them. Tests cover parsing, recovery, scope ends, shadowing and running. Build and 338 tests pass on Windows; Linux not tried.

## [DONE] M0.7.2: Booleans, `null`, Comparisons and Logic

The keywords `true`, `false` and `null`; `==`, `!=`, `<`, `<=`, `>`, `>=`; `!`, `&&` and `||`, short-circuiting, Booleans only (specification, section 3.5). Added by Claude, 2026-11-12.

Done (Claude, 2026-11-12): `true`, `false` and `null`, `== != < <= > >=`, `!`, and short-circuit `&&` and `||` at the approved precedence. Comparing an int64 with a binary64 is exact: `9007199254740993 == 9007199254740992.0` is false. New operations cover these, including the first conditional jumps (`JumpIfFalse` and `JumpIfTrue`, whose condition must be a Boolean), and the verifier follows both of their paths. The right operand of `&&` and `||` is checked to be a Boolean too (`CheckBoolean`). The result goes through a temporary, so `x = y && x` reads the old `x`. Build and 373 tests pass on Windows; Linux not tried.

## [DONE] M0.7.3: `if` and `else`

`if (condition) statement`, with an optional `else`, and a strict Boolean condition (specification, section 3.5). Added by Claude, 2026-11-12.

Done (Claude, 2026-11-12): `if (condition) statement`, with an optional `else`. The `else` binds to the nearest `if`, and the condition must be a Boolean at run time (WS3001). The code generator patches forward jumps (`EmitJump`, `PatchJump`), which the loops will use too. I guessed one thing, recorded in the specification as proposed: a branch that is not a block still has a scope of its own, so a declaration in it ends with the branch. Build and 388 tests pass on Windows; Linux not tried.

## [DONE] Syntax of `for` and `do … while`

The owner put `for` and `do … while` in M0.7 ("What M0.7 Covers", A4), but their syntax was not proposed. Added by Claude, 2026-11-12.

Q1: `for (init; condition; step) statement`, as in C: `init` is a declaration or an expression, or empty; `condition` is a Boolean expression, or empty, meaning `true`; `step` is an expression, or empty. A `var` declared in `init` is scoped to the loop and gets a new binding on each iteration, so that closures capture each one (section 3.1). Agreed?

A1: Yes.

Q2: `do statement while (condition);`, ending with a `;`, as in C. Agreed?

A2: Yes;

Q3: Should a `for … of` loop over collections wait until arrays exist? (a) yes, later; (b) design it now.

A3: (b), but I'd rather have `foreach` loops over collections. `for`-`of` is unintuitive for me.

BTW: Thank you for thoughtfully inserting `An:` placeholders for my answers.

Done (Claude, 2026-11-12): Q1 and Q2 are recorded as approved in the specification, section 3.5 ("`for`", "`do … while`"). Q3, a `foreach` over collections, is the new question "Syntax of `foreach`". Nothing to build.

## [DONE] M0.7.4: Loops, `break` and `continue`

`while`, `for` and `do … while`, with `break` and `continue` (specification, section 3.5). Added by Claude, 2026-11-12.

Done (Claude, 2026-11-12): `while`, `for` (any part may be empty, and a missing condition is true) and `do … while`, all lowered to one loop form (`WsBoundLoop`, `WsIrLoop`). `continue` goes to the step or the test, and `break` goes past the end. `break` or `continue` outside a loop is the new WS2007, which I picked and the specification lists as proposed. A `for`'s init is in a scope of the loop's own. Per-iteration bindings only become observable with closures, so they come with M0.7.6. Build and 408 tests pass on Windows, and the CLI ran a loop sample (a 100,000-iteration sum: 5000050000); Linux not tried.

## [DONE] M0.7.5: String Literals and Concatenation

String values, literals in either quote with their escapes (WS1009, WS1010), `+` of two strings, and their text when written (specification, sections 3.3 and 3.5). Strings are the first values the runtime keeps outside a `WsValue`'s payload, so this part also needs the runtime's first heap objects (section 4). Added by Claude, 2026-11-12.

Done (Claude, 2026-11-12): string literals in either quote, with the approved escapes (WS1009, WS1010). Strings are `+`-concatenated, compared by text with `==` and `!=`, and written as their text; nothing is converted implicitly. Strings are the first heap objects: `WsHeap` is the execution's own, values refer to it by handle, and WebLingo's own mark-and-sweep collects it from the VM's registers and constants, within a budget (WS3003). Its numbers are constants, recorded in the specification's section 15.2 ("Heap") as proposed. Built-ins now receive the heap (`IWsBuiltinFunction.TryInvoke`). `samples/HelloWorld` runs. Build and 449 tests pass on Windows; Linux not tried.

## [DONE] Syntax of `foreach`

The owner's answer to "Syntax of `for` and `do … while`", Q3: design a loop over collections now, as `foreach` rather than `for … of`. It is implemented when collections exist (specification, section 3.2: arrays, maps, sets). Added by Claude, 2026-11-12.

Q1: Which form? (a) `foreach (var item in items) statement`, as in C#; (b) `foreach (var item of items) statement`; (c) `foreach (var item : items) statement`, as in Java.

A1: (a)

Q2: May the loop variable be declared `const` as well as `var` (`foreach (const item in items)`)? Like `for`'s, it gets a new binding on each iteration.

A2: No. Why would it even make sense? `const` means it cannot be assigned to.

Q3: Over a map, `foreach` gives each entry as a key and a value: `foreach (var (key, value) in map)`. Agreed, with the destructuring syntax decided when maps are?

A3: Yes.

Done (Claude, 2026-11-12): recorded as approved in the specification, section 3.5 ("`foreach`"): the form `foreach (var item in items)`, with `var` only, and a key and a value for each entry of a map. It is implemented with the collections, which no task covers yet. Nothing to build.

## [DONE] M0.7.6: Functions, Function Values and Closures

Function declarations and expressions, `return`, function values, and closures capturing binding cells (specification, section 3.5). It takes over "Functions as Values", and needs calls in the bytecode and the VM (sections 5.4 and 5.5). Added by Claude, 2026-11-12.

Done (Claude, 2026-11-12): function declarations, which are in scope throughout their block, function expressions, `return`, calls of any function value, built-ins as values, and closures. Captured variables live in cells the closures share, and a `for`'s captured variable gets a binding per iteration. The VM keeps its own frame stack, at most 10,000 deep. Calls are checked at compile time where the callee is known, and at run time otherwise (WS2005, WS2006). The heap traces closures and cells. Choices of mine, recorded in the specification's section 15.2 ("Functions and calls") as proposed: function names are constants; `return` outside a function is WS2008; a function is written as `function`. Until "A Hoisted Function Called Before a Variable It Uses" is answered, such a read is WS2004 at run time. Build and 492 tests pass on Windows; Linux not tried.

## [DONE] Functions as Values

The owner's answer to "Binding Choices (M0.6.4)", Q3: functions must be values. Today a built-in can only be called; naming it otherwise is WS3001, and calling anything but a built-in is WS2005 (specification, section 15.2, "Functions as values"). The specification lists Function, a callable closure, among the value categories (section 3.2), but gives no syntax for declaring functions yet, so this waits for the language's functions: their syntax goes into "What M0.7 Covers". Then a function becomes a `WsValue`, a call takes any callee expression, and the verifier and VM check calls at run time where the compiler cannot. Added by Claude, 2026-11-12.

2026-11-12: waits for the language's function syntax, which waits for "What M0.7 Covers".

2026-11-12: the syntax is approved (specification, section 3.5); this task is done as part of "M0.7.6: Functions, Function Values and Closures".

Done (Claude, 2026-11-12) with "M0.7.6: Functions, Function Values and Closures": functions, and the host's built-ins, are values.

## [DONE] What M0.7 Covers

M0.7 is "Conformance suite expansion" (specification, section 9), with no detail yet. Expanding the conformance suite needs language beyond M0.6's subset, and most of it needs syntax the specification does not give yet. Added by Claude, 2026-11-12, from the roadmap (`Documents/implementation.md`, section 13).

Q: Which features should M0.7 bring, and in what order? My proposal, from what the specification already decides: (1) blocks and nested scopes, whose semantics are approved (block scope, shadowing, section 3.1); (2) `true`, `false` and `null` literals, comparisons, and `if`/`else` with strict Boolean conditions (section 3.2); (3) `while` loops, which also give the stack VM benchmark something to measure; (4) string literals, whose observable semantics are approved (section 3.3) but whose syntax and escapes are not. Each needs its syntax decided first; shall I propose it, feature by feature, as questions here?

A: Yes, propose here.

The proposals below follow that order, with functions added as (5), since "Functions as Values" waits for them. Each feature would be one task, landing on its own, with its conformance tests. Proposed by Claude, 2026-11-12.

Q1, blocks: `{ statements }` is a statement. A block opens a scope; a declaration inside it is in scope from where it stands to the block's closing `}`, and may shadow an outer name (section 3.1). An empty block `{}` is allowed, though a lone `;` stays an error. No `;` after the `}`. Agreed?

A1: Yes.

Q2, Booleans, `null` and comparisons: `true`, `false` and `null` become keywords. The comparisons are `== != < <= > >=`, and they give a Boolean. `==` and `!=` never convert, except that an int64 and a binary64 compare as numbers (`1 == 1.0` is true, and `NaN == NaN` is false, as IEEE 754 says). `null == null` is true, and comparing values of different kinds is false, not an error. `< <= > >=` take numbers only (WS3001 otherwise; strings later). The logical operators are `!`, `&&` and `||`, which take Booleans only (WS3001 otherwise, no truthiness) and short-circuit. Precedence, loosest first: assignment, `||`, `&&`, `== !=`, `< <= > >=`, `+ -`, `* /`, unary. Agreed, and should `==` be spelled that way rather than `===`?

A2: Yes. NO `===` nonsense!

Q3, `if`: `if (condition) statement`, with an optional `else statement`, where `else` belongs to the nearest `if`. The condition must be a Boolean at run time (WS3001 otherwise). Should the branches have to be blocks (`if (x) { … }`), which rules out a dangling `else`, or may they be any statement?

A3: Any statement.

Q4, `while`: `while (condition) statement`, with a strict Boolean condition, plus `break;` and `continue;` for the innermost loop (a compile-time error outside one). A loop is bounded only by the execution's instruction budget (WS3003). Is `for`, or `do … while`, wanted in M0.7 too, or later?

A4: OK. And yes, `for` and `do-while` are also in scope.

Q5, strings: literals in double or single quotes (`"a"`, `'a'`), alike in meaning, each on one line. Escapes: `\n`, `\r`, `\t`, `\0`, `\\`, `\'`, `\"`, and `\u{1F600}`, which names a Unicode scalar value (at most six hex digits, and no surrogates). Any other escape is an error, a new WS1009 "invalid escape", and an unterminated string is WS1010. `+` concatenates two strings, but a string and a number are not joined (WS3001; an explicit conversion comes later). `length` and indexing come with member access, later. Agreed, and should `+` with a string and a number convert the number, as JavaScript does?

A5: Agreed. No sneaky conversions.

Q6, functions: `function name(a, b) { … }` declares a function, in scope throughout its block (so that functions can call each other in any order), unlike `var` and `const`. `return value;` and `return;` (giving `null`) end it. Parameters are like `var`s. A function is a value (`var f = name;`), and `function (a) { … }` is a function expression. Closures capture variables, not copies. Is this the syntax you want, should functions be hoisted like that, and do they belong in M0.7, or in a milestone of their own?

A6: This is the syntax I want. NOTE: When you formulate a question, make it so that terse answer ("yes", "(a)") is possible. When you say "Is this what you want, or something else?" I can't answer with a simple "yes".

Done (Claude, 2026-11-12): the answers are recorded as approved in the specification, section 3.5. M0.7 is split into the parts the specification defines in section 9 ("M0.7 parts"), each a task below: "M0.7.1: Blocks and Nested Scopes" to "M0.7.6: Functions, Function Values and Closures". The syntax of `for` and `do … while` is the question "Syntax of `for` and `do … while`".

2026-11-12: waits for its parts, M0.7.1 to M0.7.6.

Done (Claude, 2026-11-12): all six parts, M0.7.1 to M0.7.6, have landed. `foreach` is approved but waits for collections, which no task covers yet.

## [DONE] A Hoisted Function Called Before a Variable It Uses

A function is in scope throughout its block (specification, section 3.5), so it can be called before a variable it uses is declared, which no other code can do (section 3.1):

```javascript
f();
var y = 1;
function f() { writeLn(y); }
```

Added by Claude, 2026-11-12, while implementing "M0.7.6: Functions, Function Values and Closures".

Q1: What does that call do? (a) It is a runtime error (WS2004, "read before initialization", the code you kept), and only a variable a hoisted function uses can raise it; (b) a function sees only the variables declared before the function's own declaration, so `y` is undefined in `f` (WS2001), and a call can then never come too early; (c) a function is in scope only from its declaration on, like `var`, and mutual recursion needs a function expression stored in a variable declared first.

A1: (a).

Until you answer, M0.7.6 does (a), which is the easiest to change: it touches only the variables that functions capture.

Done (Claude, 2026-11-13, `50f7dc2`): recorded as approved in the specification, section 3.5 ("Functions"), in its decision register, in its section 8.6's note, and in section 15.2 ("Functions and calls"). The code already does (a), and its tests cover it, so nothing changes in it.

## [DONE] Stack VM Comparison 1: The Benchmark

`WebLingo.Benchmarks`, a console project with no new package, timing a VM with `Stopwatch` over fixed workloads (loops, calls, closures, strings), and reporting time, instructions run, bytecode size and allocation (specification, section 5.6, part 1). It runs the register VM as it is, for the baseline that part 2 is measured against. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `97c3a06`): `WebLingo.Benchmarks`, in the solution's new `benchmarks/` folder, with four workloads (`loops`, `calls`, `closures`, `strings`), each a `.ws` file that `weblingo run` runs too, and the output every VM must give. It reports each workload's median and fastest time, the instructions a run executes, what it allocates, and its code's size. The register VM now reports the instructions an execution ran (`WsExecutionResult.Instructions`), which the instruction budget counts. The baseline is recorded in the specification, section 5.6. Guesses of mine: the `benchmarks/` folder; code size counted as a byte per operation and each operand it uses in LEB128, since WBC's encoding is not decided. Build and 507 tests pass on Windows; Linux not tried.

## [DONE] Stack VM Comparison 2: The Register VM's Dispatch, Tuned

The register VM runs the same bytecode faster: its dispatch loop tuned for speed (specification, section 5.6, part 2), with no change to the bytecode or to what a program does, and measured with the benchmark against part 1's baseline. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `12e52c4`): `WsVirtualMachine` runs every instruction in one loop, with the running frame's code, registers and position in locals. It looks at the budget and the cancellation signal only when they come due, and does int64 and binary64 arithmetic and comparisons itself, leaving the rest, and every error, to `WsOperators`. A call reuses the frame and the registers that the last call at its depth left. The bytecode, the instruction counts, the results and the messages are unchanged. The speed-ups, both measured fully optimized, are 6.2× on `loops`, 3.5× on `calls`, 3.7× on `closures` and 2.7× on `strings`; `calls` allocates 10 KB instead of 49 MB. They are recorded in the specification, section 5.6. New: `OperatorTests`, which checks the VM's own arithmetic against `WsOperators` on every operator and pairs of edge values. The benchmark now turns .NET's tiered compilation off, because with it the VM's loop was still being compiled while it was timed; the baseline was measured again that way. The inventory has the facilities the VM now uses. Build and 517 tests pass on Windows; Linux not tried.

## [DONE] Stack VM Comparison 3: The Stack VM

Stack bytecode and its verifier in `WebLingo.Bytecode`, its code generator in `WebLingo.Compiler`, from the same lowered IR as the register one, and the stack VM in `WebLingo.VM.Stack`, sharing `WebLingo.Runtime`'s semantics (specification, section 5.6, part 3). Every conformance program gives the same output and the same errors on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `817f242`): the stack bytecode, with its own operations and instruction (an operation and at most two operands), shares the register bytecode's constants, imports and captures. Its verifier checks the operand stack's depth on every path, besides what the register verifier checks. `WsStackCodeGenerator` and `WsStackCompiler` generate it from the same lowered IR, and `WsStackVirtualMachine` runs it, tuned as the register VM is, sharing `WebLingo.Runtime` and `WsExecutionResult`. The benchmark measures both (`--vm` picks one). Tests: every conformance program, and 5,000 random sources, give the same diagnostics, output and errors on both VMs (`StackVmTests`). Each VM's arithmetic is checked against `WsOperators`; the verifier is checked on bad modules and random code, and the VM on mutated bytecode. Guesses of mine, recorded in the specification's section 5.6: the stack bytecode lives in `WebLingo.Bytecode`, because the compiler may not depend on a VM; `WebLingo.VM.Stack` depends on `WebLingo.VM` for `WsExecutionResult`; a function's slots, locals and stack together, are bounded by the register limit and the register budget; and the instruction budget counts each VM's own instructions, so a program stopped by it stops at a different point on each. Build and 674 tests pass on Windows; Linux not tried.

## [DONE] Stack VM Comparison 4: The Comparison

Both VMs measured on the same workloads, the results recorded in the specification (section 5.6, part 4), and the choice between them (WS-BC002) put to the owner as a question. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `5ef0107`): both VMs measured on the four workloads, 11 timed runs each, fully optimized. The register VM is 1.10 to 1.65 times as fast and runs 1.29 to 1.75 times fewer instructions; the stack bytecode is 3 to 35 percent smaller in bytes; allocation is the same. Recorded in the specification, section 5.6 ("The comparison"), with each implementation's size in lines, and in sections 5.2, 9 and 10. The choice is the question "Register VM or Stack VM (WS-BC002)" below. Nothing in the code changed; Windows only.

## [DONE] Stack VM Prototype and Benchmark (WS-BC002)

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

Done (Claude, 2026-11-13): all four parts have landed (`97c3a06`, `12e52c4`, `817f242`, `5ef0107`): the benchmark, the register VM tuned, the stack VM, and the comparison, recorded in the specification, section 5.6. The choice between the VMs is the question "Register VM or Stack VM (WS-BC002)".

## [DONE] Register VM or Stack VM (WS-BC002)

The comparison is done (specification, section 5.6, "The comparison"). On the four workloads (loops, calls, closures, strings), the register VM is 1.10 to 1.65 times as fast as the stack VM, and runs 1.29 to 1.75 times fewer instructions. The stack bytecode is 3 to 35 percent smaller in bytes, and its code generator is simpler. Both allocate the same. Not measured yet, because the language does not have them: property access, async suspension, template output, I/O-bound workloads, startup and latency. Added by Claude, 2026-11-13.

Q1: Which VM does WebLingo keep? (a) the register VM: WS-BC002 is approved, and the stack VM is removed; (b) the register VM: WS-BC002 is approved, and the stack VM stays as it is, for comparisons later; (c) both, for now: WS-BC002 stays provisional until the workloads not measured yet can be, and every new feature is built on both VMs; (d) the stack VM.

A1: (c)

Done (Claude, 2026-11-13): recorded as approved in the specification: its decision register, section 5.2, section 5.6 ("Both VMs, for now") and open question 1. `Documents/implementation.md`, section 6, now has the rule that every feature reaching the bytecode is built on both VMs in the same change, and that property access, async suspension, template output and I/O each get a benchmark workload when the language can express them. No code changed.

## [DONE] What Comes After M0.8

The roadmap ends with M0.8 (specification, section 9). M0.7's language is implemented, but for `foreach`, which waits for collections; M0.8's CI and portability checks are in place. The specification does not say what comes next, and each candidate needs syntax or a format it does not give yet, which I would propose here, feature by feature, before building it. Added by Claude, 2026-11-13, from the roadmap (`Documents/implementation.md`, section 13).

Q1: Which comes next? (a) collections: arrays, maps and sets (section 3.2), with indexing, `length` and member access (section 3.3), and `foreach` (section 3.5); (b) `.wsp` templates: includes, layouts and parameterized components together (WS-T001 to WS-T008); (c) classes, `dispose()` and `using` (sections 3.2 and 3.4, WS-L011); (d) WBC, the bytecode's binary format (sections 5.1 and 5.3).

A1:  (a), then (c), then (b), then (d).

My suggestion is (a): templates and classes both need member access, which comes with it.

Done (Claude, 2026-11-13, `8a9cf5c`): recorded as approved in the specification, section 9, as milestones M0.9 (collections), M0.10 (classes, `dispose()` and `using`), M0.11 (`.wsp` templates) and M0.12 (WBC), in that order, and in its decision register. The milestones' numbers are mine. Each is a task below. M0.9's syntax comes first, as the questions in "M0.9 Syntax: Collections".

## [DONE] M0.9 Syntax: Collections

M0.9 brings collections (specification, section 9). Their syntax, and part of their meaning, is not in the specification yet. These are my proposals, one decision each, in the order I would build them; each can be answered with "yes" or a letter. Everything here is built on both VMs (section 5.6, "Both VMs, for now"). Added by Claude, 2026-11-13.

Q1, arrays: `[a, b, c]` makes a new array of those values, which may be of any kinds, mixed; `[]` makes an empty one; there is no trailing comma, as in calls. Agreed?

A1: Yes.

Q2, indexing: `x[i]` reads element `i` of an array, counting from 0, and `x[i] = v` replaces it. The index must be an int64: a binary64, even `1.0`, is WS3001, since nothing converts implicitly. An index outside 0 to the length − 1 is a runtime error with a new code, WS3005 "index out of range". Agreed?

A2: Yes.

Q3, member access: `x.name` reads a member of a value, and `x.name(…)` calls one, a method. M0.9 gives collections and strings the members below; a member a value does not have is a runtime error with a new code, WS3006 "no such member" (section 3.2: missing property access throws). Agreed?

A3: Yes.

Q4, length: `x.length` is an array's number of elements, a map's or a set's number of entries, and a string's number of Unicode scalar values (section 3.3). It is an int64, and cannot be assigned. Agreed?

A4: Yes.

Q5, strings by index: `s[i]` is the string's `i`-th Unicode scalar value, as a string of one (`"abc"[1]` is `"b"`), not its number. Strings stay immutable, so `s[i] = v` is WS3001. Agreed?

A5: Yes.

Q6, growing arrays: `a.push(v)` adds `v` at the end, giving `null`; `a.pop()` removes the last element and gives it, and on an empty array is WS3005. Other array methods come when they are needed. Agreed?

A6: Yes.

Q7, writing a map: (a) `{ key: value, … }`, where a key written as a name or as a string literal is a string (`{ active: true }` is `{ "active": true }`, as the specification's examples write it), and `{}` is an empty map; other keys are added by indexing; (b) the same, but a key is any expression, so `{ active: true }` uses the value of the variable `active`; (c) no literal: maps are made by `new Map()`, which waits for classes (M0.10). At the start of a statement, `{` still opens a block in each case.

A7: (a).

Q8, a map's entries: `m[k]` reads the value of key `k`, and a missing key is a runtime error with a new code, WS3007 "no such key"; `m[k] = v` adds or replaces it; `m.has(k)` tells whether it is there; `m.remove(k)` removes it, giving whether it was there. Agreed?

A8: Yes.

Q9, a map's keys as names: (a) no: a map's keys are read only by indexing (`m["active"]`), and `.name` is kept for members, and later for classes' fields; (b) yes: `m.active` also reads the key `"active"`, and `m.active = v` sets it, for every key that is a name and not one of a map's own members (`length`, `has`, `remove`), as JavaScript's objects do.

A9: (a).

Q10, which values are keys: strings, int64s, Booleans and `null`, compared as `==` compares them (so an int64 is never equal to a string key). A binary64 key is WS3001, so that `1` and `1.0`, and NaN, raise no question; so are arrays, maps, sets and functions, until a use for keying by identity comes. The same values may be a set's. Agreed?

A10: Yes.

Q11, order: `foreach` visits a map's entries, and a set's values, in the order they were first added; replacing a value keeps its entry's place. Agreed?

A11: No, the order of keys in a map could well be undefined, and so is set's. Has table implementation does not lend itself to ordering.

Q12, `foreach` over each kind: `foreach (var item in a)` visits an array's elements in order, a set's values, and a string's Unicode scalar values, each as a string of one; `foreach (var (key, value) in m)` visits a map's entries, and `(key, value)` is allowed only there for now (section 3.5). `foreach (var item in m)` over a map is WS3001: a map's loop takes `(key, value)`. Anything else to loop over is WS3001. Agreed?

A12: Yes.

Q13, an array changed while `foreach` visits it: (a) is a runtime error at the loop's next step, with a new code, WS3008 "collection changed during foreach"; (b) the loop sees the change, as a `for` loop with an index would. The same rule holds for maps and sets.

A13: (b).

Q14, making a set: (a) a built-in function, `set(a)`, makes a set of an array's values, until classes give `new Set(…)`; (b) sets wait for classes (M0.10), since they have no literal.

A14: Can we invent a literal for sets? How is it done in other languages?

Q15, a set's members: `s.add(v)` adds `v`, giving whether it was new; `s.has(v)` tells whether `v` is in it; `s.remove(v)` removes it, giving whether it was there. Agreed?

A15: Yes.

Q16, equality: arrays, maps and sets are equal only to themselves, as functions are (section 3.2: object equality uses identity), so `[1] == [1]` is false. Agreed?

A16: Yes.

Q17, writing a collection as text: (a) `write` writes an array as `[1, "a", null]`, a map as `{"active": true}`, and a set as `set([1, 2])`, its strings quoted with their escapes, its collections written the same way, and one it is already writing as `[…]` or `{…}`; (b) it writes only `array`, `map` or `set`, as it writes a function as `function`.

A17: (a) with whatever we decide for literal sets.

Done (Claude, 2026-11-13, `57ee846`): recorded as approved in the specification, a new section 3.6 ("Collections"), its decision register, and its section 8.4, with the codes WS3005 to WS3007 (WS3008 is not needed, after A13). A14's question is answered in "M0.9 Syntax: Optional Access and Set Literals", where how a set is made, and so written (A17), is decided.

## [DONE] M0.9.1: Member Access and Indexing

`x.name`, `x.name(…)`, `x[i]` and `x[i] = v`, with strings' `length` and indexing (specification, section 3.6), on both VMs, with their conformance tests. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `7af3c9a`): members (`x.name`, `x.name(…)`) and indexes (`x[i]`, `x[i] = v`) are parsed, bound, lowered, and generated for both VMs, whose new operations share their semantics in `WsMembers` (`WebLingo.Runtime`). Strings have `length` (in Unicode scalar values) and `s[i]` (a string of one); `s[i] = v` is WS3001. WS3005 and WS3006 are raised as section 3.6 says. Every new conformance case runs on both VMs, with the same results. Choices of mine, recorded in the specification's section 15.2 as proposed: `.` is now a token, so `1.` and `.5` are a number and a point (two lexer tests changed accordingly); a member is never assigned (WS1007) until classes have fields; calling a member that is not a method is WS2005; the new operations' shapes. Build and 727 tests pass on Windows; Linux not tried.

## [DONE] M0.9.2: Arrays

Array literals, indexing, `length`, `push` and `pop`, identity and text (specification, section 3.6), on both VMs, with their conformance tests. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `a8ea4ee`): arrays, `[a, b]` and `[]`, with `a[i]`, `a[i] = v`, `a.length`, `a.push(v)` and `a.pop()`, on both VMs. They are a new kind of heap object, traced by the collector, and their units grow and shrink with them within the heap's budget (WS3003). They are equal only to themselves. `write` writes them as `[1, "a", null]`, their strings quoted, one being written already as `[…]`, and without .NET's stack, so a deeply nested array cannot overflow it. Choices of mine, in the specification's section 15.2 as proposed: `a.push` read without a call is WS3001; the cycle mark is the character U+2026. Every new conformance case gives the same result on both VMs. Build and 778 tests pass on Windows; Linux not tried.

## [DONE] M0.9.3: Maps

Map literals, entries, `has`, `remove`, `length`, keys and text (specification, section 3.6), on both VMs, with their conformance tests. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `83ba9ca`): maps, `{ key: value }` and `{}`, with `m[k]`, `m[k] = v`, `m.has(k)`, `m.remove(k)` and `m.length`, on both VMs. Keys are strings, int64s, Booleans and `null` (WS3001 otherwise), and a missing key is WS3007. A map is a heap object the collector traces, within the budget, equal only to itself, written as `{"active": true}`. Choices of mine, in the specification's section 15.2 as proposed: a later key in a literal replaces an earlier one; a removal moves the last entry into its place, so the order is a hash table's, not defined; a malformed key in a literal skips the rest of it, for one diagnostic. `{` at the start of a statement still opens a block, as section 3.6 says, so `{a: 1};` there is a syntax error. Every new conformance case gives the same result on both VMs. Build and 827 tests pass on Windows; Linux not tried.

## [DONE] M0.9.4: `foreach`

`foreach` over arrays, strings and maps (specification, sections 3.5 and 3.6), on both VMs, with their conformance tests, and a benchmark workload of collections (section 5.6). Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `2a8113c`): `foreach (var x in a)` over arrays (in order), strings (by Unicode scalar value) and, with `(key, value)`, maps, on both VMs. The loop sees changes to its collection, as section 3.6 says. Closures capture each iteration's variables, and `break`, `continue` and `return` work in it. One new operation per VM, `ForEachNext`, whose way out the verifiers check writes only the loop's position. A `collections` workload joins the benchmark; the register VM ran it in 3.7 to 4.5 ms, the stack VM in 4.7 to 5.2 ms (specification, section 5.6). Choices of mine, in section 15.2 as proposed: `foreach` is a keyword and `in` only inside its parentheses; recovery reads `const` as `var` and a wrong word as `in`; the loop's state lives in hidden locals. Every new conformance case gives the same result on both VMs. Build and 878 tests pass on Windows; Linux not tried.

## [DONE] M0.9 Syntax: Optional Access and Set Literals

Two follow-ups to "M0.9 Syntax: Collections". In the chat (2026-11-13), the owner chose new syntax for a read that may find nothing: `m?[k]` gives `null` where `m[k]` raises a missing key (WS3007), and `x?.name` extends it to members later, so that a template can show an absent value as blank. Whether a template writes `null` as nothing is decided with M0.11 ("M0.11: Templates"). And A14 asks for a set literal. Added by Claude, 2026-11-13.

Q1, `?[` and a conditional operator: (a) `m?[k]`, as chosen, and as C# writes it; if WebLingo gets a conditional operator, `c ? a : b`, its parser tells `c ?[k]` and `c ? [k] : d` apart, as C#'s does; (b) `m?.[k]`, as JavaScript writes it, precisely so that `?[` never needs telling apart from a conditional operator.

A1: (a).

Q2, arrays and strings too: `a?[i]` and `s?[i]` give `null` for an index out of range, where `a[i]` and `s[i]` raise WS3005. Agreed?

A2: Yes.

Q3, a `null` before `?[`: (a) `x?[k]` with `x` null is `null` too, as in C# and JavaScript, so that `rows?[0]?["name"]` stops at the first `null`; (b) only a missing key or index gives `null`, and a `null` `x` is WS3001, as `x[k]` is.

A3: (a).

Q4, what `?[` still rejects: a key or index of the wrong kind (`m?[1.5]`, `a?["x"]`), and a value that cannot be indexed (`5?[0]`), stay WS3001: `?[` allows for an absent entry, not for a wrong program. Agreed?

A4: Yes.

How other languages write sets (A14): Python writes `{1, 2, 3}`, but its empty set is `set()`, since `{}` is a dictionary; Clojure writes `#{1 2 3}`; Swift writes an array literal where a set is expected (`let s: Set = [1, 2, 3]`), and C#'s collection expressions do the same; JavaScript, Kotlin and Ruby have no literal (`new Set([1, 2, 3])`, `setOf(1, 2, 3)`, `Set[1, 2, 3]`). WebLingo cannot take Python's way as it is: with A7, `{ a, b }` would look like a map whose keys are the names `"a"` and `"b"`, and `{}` is already the empty map. Nor Swift's, without declared types.

Q5, a set literal: (a) `#{1, 2, 3}`, and `#{}` for an empty set, as Clojure writes it, its elements any expressions; `#` is unused in WebLingo code; (b) no literal: a built-in function, `set([1, 2, 3])`, as A14's option (a) said. Either way, `write` writes a set as its literal (A17).

A5: (a).

Done (Claude, 2026-11-13, `78cf2fd`): recorded as approved in the specification, section 3.6 ("Optional access", "Sets", "Text") and its decision register. They are built as "M0.9.5: Sets" and "M0.9.6: Optional Access".

## [DONE] M0.9.5: Sets

Sets, their members, `foreach` over them, and their text (specification, section 3.6), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.9 Syntax: Optional Access and Set Literals", which decide how a set is made and written.

2026-11-13: the answers are in (specification, section 3.6); it no longer waits.

Done (Claude, 2026-11-13, `fe4392d`): sets, `#{1, "a"}` and `#{}`, with `s.add(v)`, `s.has(v)`, `s.remove(v)`, `s.length` and `foreach`, on both VMs. Their values are what a map's keys may be (WS3001 otherwise, "cannot be in a set"), an equal one given twice is there once, and `write` writes a set as its literal, also inside other collections. A set is a heap object the collector traces, within the budget, equal only to itself. Choices of mine, in section 15.2 as proposed: `#{` is one token, and `#` alone stays invalid; a set shares a map's hash table. Every new conformance case gives the same result on both VMs. Build and 908 tests pass on Windows; Linux not tried.

## [DONE] M0.9.6: Optional Access

`x?[k]` (specification, section 3.6), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.9 Syntax: Optional Access and Set Literals".

2026-11-13: the answers are in (specification, section 3.6); it no longer waits.

Done (Claude, 2026-11-13, `2035e47`): `x?[k]` on both VMs: `null` for a missing key, an index out of range (of an array or a string) and a `null` target; WS3001 still for a wrong key or index kind, or a value that cannot be indexed; `m?[k] = v` is WS1007. No new operation: `GetIndex` takes a flag. Choices of mine, in section 15.2 as proposed: `?[` is one token, and `?` alone stays invalid; the index is evaluated even when the target is `null`; each `?[` stands alone, so `m?["a"].length` is WS3006 when there is no `"a"`. Every new conformance case gives the same result on both VMs. Build and 932 tests pass on Windows; Linux not tried.

## [DONE] M0.9: Collections

Arrays, maps and sets, with indexing, `length`, member access and `foreach` (specification, section 9), on both VMs, with their conformance tests, and a benchmark workload for property access (section 5.6). Its parts are defined in the specification once its syntax is approved. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.9 Syntax: Collections".

2026-11-13: and for the answers to "M0.9 Syntax: Optional Access and Set Literals".

2026-11-13: the syntax is approved but for sets and optional access (specification, section 3.6). M0.9 is split into the parts the specification defines in section 9 ("M0.9 parts"), each a task below, M0.9.1 to M0.9.6. Waits for them.

Done (Claude, 2026-11-13): all six parts have landed, M0.9.1 to M0.9.6 (`7af3c9a`, `a8ea4ee`, `83ba9ca`, `2a8113c`, `fe4392d`, `2035e47`): members and indexing, arrays, maps, `foreach`, sets and optional access, on both VMs, recorded in the specification, sections 3.6 and 9.

## [DONE] M0.10 Syntax: Classes, `dispose()` and `using`

M0.10 brings classes, the disposal protocol and `using` (specification, sections 3.2 and 3.4). The specification approves `dispose()`, its `destructor()` sugar and `using` (WS-L011), and gives section 3.4's example, but not the syntax of classes, nor several points of their meaning. These are my proposals, one decision each, each answerable with "yes" or a letter; whatever is decided is built on both VMs. Added by Claude, 2026-11-13.

Q1, declaring a class: `class Name { constructor(a) { … } method(b) { … } }`, its methods written as a name, parameters and a body, without `function`, as section 3.4's example writes them. A class is in scope throughout its block, as a function is, and its name is a constant. Agreed?

A1: Yes, but allow optionally to include the keyword `function`.

Q2, making an instance: `new Name(a, b)` makes one and runs its `constructor` with those arguments; a class without a `constructor` takes none. Calling a class without `new`, or `new` on anything but a class, is WS2005. Agreed?

A2: Yes.

Q3, fields: (a) a field exists once a method assigns it, `this.file = …`, as section 3.4's example does, and reading one that was never assigned is WS3006; (b) fields are declared in the class body, `var file = null;`, each instance getting its own, and assigning or reading an undeclared one is WS3006.

A3: (a).

Q4, `this`: a keyword, the instance a method or the constructor runs on; a function expression inside a method sees the method's `this`, as a closure sees a variable. `this` anywhere else is a compile-time error, with a new code, WS2009. Agreed?

A4: Yes.

Q5, methods as values: `obj.method` without a call (a) gives a function bound to `obj`, so `var f = obj.method; f();` calls it on `obj`; (b) is WS3001, "a method can only be called", as an array's `push` is now.

A5: (a).

Q6, inheritance: `class B extends A { … }`; a method of B with the name of one of A's replaces it; `super(…)` in B's constructor runs A's, and `super.name(…)` calls A's method. A class extends at most one other (section 3.2). Agreed?

A6: Yes.

Q7, a constructor that does not call `super(…)`: (a) is a compile-time error when the base class has a `constructor` (a new code, WS2010); (b) runs the base's constructor first, with no arguments, as C# does.

A7: (b).

Q8, visibility: every field and method is public, and there are no static members, for now. Agreed?

A8: Yes to public, but `static` is useful.

Q9, `x?.name`: (a) gives `null` when `x` is `null`, and when it has no field `name`, as `x?[k]` does for a missing key; (b) gives `null` only when `x` is `null`.

A9: (a).

Q10, equality: an instance is equal only to itself (section 3.2: object equality uses identity), so two instances with equal fields are not equal. Agreed?

A10: Yes. Base Object should provide `.equal()` that can be overriden. 

Q11, an instance as text: (a) `write` writes it as its class's name and its fields, `Point {x: 1, y: 2}`, in no defined order, one already being written as `Point {…}`; (b) only its class's name, `Point`.

A11: (a).

Q12, `destructor()`: a class may declare `dispose()` or `destructor()`, which are the same method; both in one class is a compile-time error, with a new code, WS2011 (section 3.4 recommends it). Callers always call `dispose()`. Agreed?

A12: Yes.

Q13, disposing once: the runtime runs an object's disposal at most once; a second `dispose()` does nothing and gives `null` (section 3.4: disposal is idempotent). Agreed?

A13: Yes.

Q14, derived classes: (a) disposing an instance of B runs B's disposal, then A's, each once, without `super.dispose()`, as section 3.4 proposes; (b) B's disposal calls `super.dispose()` itself, as any other method would.

A14: (a).

Q15, `using`: `using (var x = expr) statement` declares `x`, in scope in the statement only, and calls `x.dispose()` when the statement ends, by its end, `break`, `continue` or `return`; a `null` value is not disposed, and a value without `dispose` is WS3006 at the end. One resource per `using`; nest them for more. Agreed?

A15: Yes.

Q16, a runtime error inside `using`: WebLingo has no exceptions yet, so a runtime error stops the program. (a) As it stops, the VM disposes what the open `using` statements hold, innermost first, and an error in a disposal is reported after the first; (b) nothing is disposed: `using` disposes only on the ways out of Q15, until exceptions exist.

A16: Design exceptions now.

Q17, use after disposal: for a class written in WebLingo, (a) nothing is checked: the class checks its own state if it needs to; (b) calling any method but `dispose()` on a disposed instance is a runtime error, a new WS3008 (section 3.4 proposes a `ResourceClosedError`).

A17: (a).

Done (Claude, 2026-11-13, `20eeb6c`): recorded as approved in the specification, a new section 3.7 ("Classes and disposal"), its decision register, and its section 8.4, with WS2009 and WS2011 (WS2010 and WS3008 are not needed, after A7 and A17). A1's optional `function` before a method's name is part of it. A8 (static members), A10 (`Object` and `equal()`) and A16 (exceptions) are follow-up questions, in "M0.10 Syntax: Static Members, `Object` and Exceptions". M0.10 is split into the parts the specification defines in section 9 ("M0.10 parts"), each a task below.

## [DONE] M0.10.1: Classes

Class declarations, `new`, fields, methods, `this`, methods as values, `x?.name`, and an instance's text (specification, section 3.7), on both VMs, with their conformance tests. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `54cc651`): classes on both VMs: declarations (a method with or without `function`, A1), hoisted as functions are; `new`, running the constructor; fields made by assignment; methods, with `this`, which closures inside them see; methods as bound values; `x?.name`; and an instance's text, `Point {x: 1, y: 2}`. WS2009 is reported for `this` outside a method. Both VMs' calls now share one path for functions, built-ins, bound methods, methods and constructors. The benchmark gets an `objects` workload, property access: 81 ms on the register VM, 86 to 93 ms on the stack VM (specification, section 5.6). Choices of mine, in section 15.2 as proposed: the keywords and `?.`; a field cannot take a method's name; a bound method is made on each read; a constructor's result is dropped; an instance's fields are written in the order first assigned. Two tests changed, since `x.name = v` is now a field's assignment, not WS1007. Every new conformance case gives the same result on both VMs. Build and 1,000 tests pass on Windows; Linux not tried.

## [DONE] M0.10.2: Inheritance

`extends`, `super(…)`, `super.name(…)`, and the base's constructor run first when a constructor does not call `super(…)` (specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `5c89f1d`): `class B extends A`, with `super(…)` in a constructor, `super.name(…)` and `super.name` in methods, methods overridden (a base method calling `this.name()` reaches the derived one), and a constructor without `super(…)` running its base's first, with no arguments, through any number of levels. One new operation per VM, `GetSuper`, and `MakeClass` takes the base. Choices of mine, in section 15.2 as proposed: a class's base is evaluated where its block begins, since the class is made there, so it cannot name the block's variables (WS2001); the block's classes are made bases first, and a cycle is WS2013, a new code; a class without a constructor gets one; misplaced `super` is WS2009. Every new conformance case gives the same result on both VMs. Build and 1,028 tests pass on Windows; Linux not tried.

## [DONE] M0.10.3: Disposal

`dispose()` and `destructor()`, disposal once and chained from a derived class to its base, and `using` on its ways out but exceptions (specification, section 3.7, WS-L011), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `f6c014a`): `dispose()`, or `destructor()` (both in one class is WS2011), run at most once per instance, a derived class's then its bases', by `x.dispose()`, by `x.dispose` called as a value, and by `using (var r = …) statement` at its end, `break`, `continue` and `return`, innermost first; `null` is not disposed. One new operation per VM, `Dispose`; each VM runs the chain of dispose methods in one frame, off its ordinary call and return paths, so `calls`, `closures` and `objects` take the same time as before, within their noise. An instance is 8 bytes larger in .NET's heap, for its disposed flag (`objects` allocates 44.0 MB, not 43.2). Choices of mine, in section 15.2 as proposed (row "Disposal"): disposal passes no arguments (`x.dispose(1)`, or a dispose method with parameters, is WS2006); a `using` of a value with no dispose method is WS3006 at its end, a field named `dispose` not counting; `x.destructor()` is WS3006; `super.dispose()` calls the base's method as any call does, so it runs once more than the disposal's own run of it, which you may prefer otherwise. Every new conformance case gives the same result on both VMs. Build and 1,066 tests pass on Windows; Linux not tried.

## [DONE] M0.10 Syntax: Static Members, `Object` and Exceptions

Follow-ups to "M0.10 Syntax: Classes, `dispose()` and `using`": A8 asks for static members, A10 for a base class `Object` whose `equal()` can be overridden, and A16 for exceptions, designed now. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, static members: `static name(…) { … }` declares a method of the class itself, called as `Name.name(…)`, and `static var count = 0;` a field of the class, `Name.count`, set when the class is declared. In a static method, `this` is WS2009. Agreed?

A1: Yes.

Q2, statics and inheritance: (a) `B.name` finds A's static member when B has none of its own, as C# does; (b) each class has only its own static members.

A2: (a).

Q3, `Object`: a class without `extends` extends `Object`, a built-in class with no fields and one method, `equal(other)`, which gives `this == other` and may be overridden; `new Object()` makes a plain instance. Agreed?

A3: Yes. And also `toString()`.

Q4, `==` and `equal`: (a) `==` stays identity for instances (A10), and `a.equal(b)` runs only where a program calls it; (b) `==` between two instances calls the left one's `equal`.

A4: (a).

Q5, `equal` on other values: (a) only instances have `equal`; (b) every value has it, meaning `==` for values that are not instances (`"a".equal("a")` is true).

A5: (a).

Q6, throwing: `throw value;` throws (a) any value; (b) only an instance of `Error`, or of a class extending it (WS3001 otherwise).

A6: (a).

Q7, catching: `try { … } catch (e) { … }`, `try { … } finally { … }`, and `try { … } catch (e) { … } finally { … }`, with one `catch`, which catches every exception, and `e` a `var` in scope in its block. Typed catches may come later. Agreed?

A7: OK. Add a TO-DO for typed catches.

Q8, `Error`: a built-in class, `new Error(message)`, with the fields `message` and `code` (a diagnostic code, or `null` for one a program throws), which programs may extend. Agreed?

A8: Yes.

Q9, runtime errors as exceptions: the runtime errors (WS2004 to WS2006 at run time, WS3001, WS3002, WS3005 to WS3007) become instances of `Error`, with their code and message, which a `catch` may catch; the execution's limits (WS3003) and a cancellation (WS3004) cannot be caught, and still stop the program. Agreed?

A9: Yes.

Q10, an exception nothing catches stops the program as a runtime error does now: exit code 3, reported where it was thrown, with its own code for a runtime error, and a new one, WS3008 "uncaught exception", for a value a program threw. Agreed?

A10: Yes.

Q11, `finally`: it runs on every way out of its `try` (its end, `break`, `continue`, `return`, an exception); a `break`, `continue` or `return` that would leave a `finally` block is a compile-time error, a new WS2012, so that a `finally` cannot drop an exception or a result. Agreed?

A11: Yes.

Q12, rethrowing: `throw;` alone, in a `catch`, throws the caught exception again, keeping where it was first thrown. Agreed?

A12: Yes.

Q13, `using` and exceptions: `using` disposes on every way out, an exception's included, as a `finally` would. An exception from `dispose()` while another is on its way out: (a) replaces it, as in C#; (b) is dropped, and the first one goes on.

A13: (a).

Q14, how the VMs do it: (a) table-driven protected regions, as section 5.4 prefers: each function lists its `try` ranges and their handlers, and nothing runs on the way into a `try`; (b) instructions that push and pop a handler as the code enters and leaves a `try`. Open question 4 (section 10) keeps this choice for you.

A14: (a).

Done (Claude, 2026-11-13, `f6dc8c8`): recorded as approved in the specification: section 3.7 (static members, `Object`, equality, and exceptions in eight points), its decision register (with section 5.4's table-driven regions, A14), section 8.4 (WS2012 and WS3008), section 10 (open question 4, but for a base's disposal when a derived one throws), and section 9's parts of M0.10. A3's `toString()` is a part of its own, M0.10.7, since its details are asked in "M0.10 Syntax: `toString()` and Disposal That Throws"; A7's typed catches are the task "Typed Catches".

## [DONE] M0.10.4: Static Members

Static methods and fields (specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: Static Members, `Object` and Exceptions".

2026-11-13: answered; next.

Done (Claude, 2026-11-13, `622798b`): `static name(…) { … }` and `static var name = value;` in a class, reached as `Name.name(…)` and `Name.name`, a class without a member of that name finding its base's (A2); a static field can be assigned, and `this` or `super` in a static member is WS2009. No new operation: the class table gives each class its static methods and fields, and `GetMember`, `SetMember` and `CallMethod` reach them on a class. Choices of mine, in section 15.2 as proposed (row "Static members"): a static field's value is evaluated where its block begins, as a base is, so it cannot name the block's variables (WS2001), and the block's static fields are set once its classes are made, each `null` until then; a static member and a method may share a name; an instance does not reach its class's statics (`p.count` is WS3006); `static const` is reported and read as `static var`. Every new conformance case gives the same result on both VMs. Build and 1,106 tests pass on Windows; Linux not tried.

## [DONE] M0.10.5: `Object` and `equal()`

The base class `Object` and its `equal()` (specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: Static Members, `Object` and Exceptions".

2026-11-13: answered; waits for M0.10.4.

Done (Claude, 2026-11-13, `3024a26`): `Object`, with `equal(other)` giving `this == other`, which a class may override and call as `super.equal(…)`; `new Object()`; every class without `extends` extends it. No new operation, and no change to either VM: `Object` is a class written in WebLingo, which the compiler adds to a program that uses it, so programs without classes compile as before, and `new` runs no extra call, since a class that extends `Object` without saying so does not run its empty constructor (`objects` takes the same time). Choices of mine, in section 15.2 as proposed (row "`Object`"): a program may declare its own `Object`, which classes do not extend; `super` is allowed in every class now, so `super.m()` with no `m` in `Object` is WS3006 when it runs, where it was WS2009 (one conformance case changed for this). `toString()` is M0.10.7. Every new conformance case gives the same result on both VMs. Build and 1,126 tests pass on Windows; Linux not tried.

## [DONE] M0.10.6: Exceptions

`throw`, `try`, `catch` and `finally`, runtime errors as exceptions, and `using` when an exception leaves it (specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: Static Members, `Object` and Exceptions".

2026-11-13: answered; waits for M0.10.5, whose `Object` `Error` extends.

2026-11-13: paused at the user's request. Work in progress is committed on the branch `claude/m0.10.6-exceptions`, not merged, and not built as a whole. Done there: the bytecode (handler tables, `Throw`, `Rethrow`, the module's `Error` class) and both verifiers; the front end and the binder (`try`, `catch`, `finally`, `throw`, `throw;`, `Error`, WS2010, WS2012); the IR, the lowerer and both code generators; and `WsExceptions` in the runtime. Left: the unwinding in both VMs, the tests, and the documents. Q5 of "M0.10 Syntax: `toString()` and Disposal That Throws" is `WsExceptions.DisposalGoesOn`, set to (a) until it is answered.

2026-11-13: resumed, and finished.

Done (Claude, 2026-11-13, `760ad15`): `throw value;`, `throw;`, `try` with `catch`, `finally`, or both, and `Error`, with the runtime errors a handler catches becoming `Error`s with their code, but the execution's limits and a cancellation; `finally` and `using` run on every way out, an exception's included, and an exception from `dispose()` replaces the one on its way out (A13); WS2010 and WS2012. Both VMs search table-driven handler regions (A14) only when an exception is thrown, so nothing runs on the way into a `try`, and the benchmark's times did not change beyond the noise of a busy machine. Choices of mine, in section 15.2 as proposed (row "Exceptions"): `throw;` outside a `catch` is a new code, WS2010; `Error` is written in WebLingo and added to a program that names it or has a `try` or a `using`; an uncaught `Error` with a code is reported as that code, so a runtime error caught and rethrown is reported as before; a disposal goes on after one of its methods throws, as I recommended for Q5 of "M0.10 Syntax: `toString()` and Disposal That Throws", which is still open (`WsExceptions.DisposalGoesOn` changes it). Every new conformance case gives the same result on both VMs. Build and 1,187 tests pass on Windows; Linux not tried.

## [DONE] M0.10 Syntax: `toString()` and Disposal That Throws

Follow-ups to "M0.10 Syntax: Static Members, `Object` and Exceptions": A3 adds `toString()` to `Object`, and exceptions raise what a disposal does when one of its methods throws. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, `toString()` when a class does not override it gives (a) the text `write` writes, `Point {x: 1, y: 2}`; (b) the class's name, `Point`, as C#'s `ToString()` does.

A1: (a).

Q2, `write`, `writeLn` and a template's `<%= %>` given an instance whose class overrides `toString()`: (a) write what it returns, wherever the instance is, inside an array or a map too, as C#'s `Console.Write` does; (b) write its class and fields as now, `toString()` running only where a program calls it.

A2: (a).

Q3, if Q2 is (a): a `toString()` that returns anything but a string is (a) WS3001 where it is written; (b) written as the value it returns.

A3: (a).

Q4, `toString()` on other values: (a) only instances have it; (b) every value has it, giving the text `write` writes (`n.toString()` is `"5"` when `n` is 5), so that `"n = " + n.toString()` joins a number to a string, which `+` alone does not (section 3.5).

A4: (b).

Q5, disposal that throws: when B's `dispose()` throws while an instance of B is disposed, (a) A's `dispose()` still runs, as section 3.4 proposes, and an exception it throws replaces B's, as A13 has it for `using`; (b) A's does not run, and B's exception goes on.

A5: (

Done (Claude, 2026-11-13, `73d16fa`): A1 to A4 recorded as approved in the specification, section 3.7 (a point of its own, `toString()`), its decision register, and section 9's M0.10.7. A5 was left unfinished, "(", so Q5 is asked again in "M0.10 Syntax: Disposal That Throws"; until it is answered, a disposal goes on after one of its methods throws, (a), as M0.10.6 made it, one constant to change.

## [DONE] M0.10.7: `toString()`

`Object`'s `toString()` (A3 of "M0.10 Syntax: Static Members, `Object` and Exceptions"; specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: `toString()` and Disposal That Throws".

2026-11-13: answered; next.

Done (Claude, 2026-11-13, `036125b`): every value's `toString()`, `Object`'s giving the text `write` writes, which a class may override; `write`, `writeLn` and a `toString()` of `Object`'s then write what the override gives, wherever the instance is, and anything but a string is WS3001 (A1 to A4). The VMs run an override as a call on their own stacks, the text being built with a stack of its own that waits for it (`WsTextBuilder`); one new operation, `ToText`, which only `Object` uses. A program that overrides no `toString()` writes as before, and the benchmark's times did not change beyond the noise. Choices of mine, in section 15.2 as proposed (row "`toString()`"): `null.toString()` is `"null"` and `Point.toString()` `"class Point"`; a `toString()` with parameters is WS2006 where the text is written; `Object`'s `toString()` writes an instance that holds itself as `Name {…}`. Every new conformance case gives the same result on both VMs. Build and 1,206 tests pass on Windows; Linux not tried.

## [DONE] M0.10: Classes, `dispose()` and `using`

Classes, with single inheritance and no exposed prototype chain (specification, section 3.2), the disposal protocol, `dispose()` and its `destructor()` sugar, and `using` blocks (section 3.4, WS-L011), on both VMs. Their syntax and part of their meaning are not in the specification yet, and will be proposed as questions. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.9.

2026-11-13: M0.9 is done; now waits for the answers to "M0.10 Syntax: Classes, `dispose()` and `using`".

2026-11-13: the syntax is approved, but for static members, `Object` and exceptions (specification, section 3.7). M0.10 is split into the parts the specification defines in section 9 ("M0.10 parts"), each a task below, M0.10.1 to M0.10.6. Waits for them.

2026-11-13: static members, `Object` and exceptions approved; `toString()` (A3) is a seventh part, M0.10.7, a task below. Waits for its parts.

Done (Claude, 2026-11-13): its seven parts landed, each with its own task in DONE.md: classes (`54cc651`), inheritance (`5c89f1d`), disposal (`f6c014a`), static members (`622798b`), `Object` and `equal()` (`3024a26`), exceptions (`760ad15`), and `toString()` (`036125b`), on both VMs. What a disposal does when one of its methods throws is asked again in "M0.10 Syntax: Disposal That Throws"; typed catches are "Typed Catches".

## [DONE] M0.10 Syntax: Disposal That Throws

Asked again: A5 of "M0.10 Syntax: `toString()` and Disposal That Throws" was left as "(". Until it is answered, the disposal goes on, (a), as M0.10.6 implemented it (`WsExceptions.DisposalGoesOn`). Added by Claude, 2026-11-13.

Q1, disposal that throws: when B's `dispose()` throws while an instance of B is disposed, (a) A's `dispose()` still runs, as section 3.4 proposes, and an exception it throws replaces B's, as A13 has it for `using`; (b) A's does not run, and B's exception goes on.

A1: (a).

Done (Claude, 2026-11-13, `27bdb58`): recorded as approved in the specification, section 3.7 ("Disposal that throws"), its decision register and section 10's open question 4; the code already does this (`WsExceptions.DisposalGoesOn`), whose comment now says so.

## [DONE] Typed Catches Syntax: Classes of Values

Follow-ups to "Typed Catches": A4 asks whether every value can be an instance of a class, such as `Integer`. It can: each kind of value gets a built-in class, which `is` and a typed catch test by the value's kind, with nothing stored in the value and nothing slower. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, every value's class: each kind of value has a built-in class extending `Object`: `Boolean`, `Integer`, `Number` (binary64), `String`, `Array`, `Map`, `Set`, `Function` and `Class`, so that `5 is Integer` and `catch (String e)`; programs cannot extend them, nor make one with `new`. Agreed?

A1: Yes, for value types. One should be able to extend arrays, maps, sets.

Q2, binary64's class: (a) `Number`; (b) `Float`; (c) `Double`.

A2: Yes.

Q3, `null`: (a) a value of no class, so `null is Object` is false, and only a catch without a class catches it; (b) it has a class too, `Null`.

A3: (a). WebLingo means to give every kind of value a class, except for `null`.

Q4, `equal` on every value: "M0.10 Syntax: Static Members, `Object` and Exceptions" had only instances have it (A5). With every value an instance of a class extending `Object`: (a) every value has `Object`'s `equal`, giving `==` (`"a".equal("a")` is true); (b) still only instances of the program's classes.

A4: (a).

Q5, a catch's name that is not a class when an exception reaches it (`var A = 5;` … `catch (A e)`), asked again: (a) WS3001, which replaces the exception; (b) the catch does not match, and the exception goes on.

A5: The catch should match, A.equals(5). Is there a problem with that?

Q6, the runtime errors' classes, each extending `Error`: `InitializationError` (WS2004), `CallError` (WS2005), `ArgumentError` (WS2006), `TypeError` (WS3001), `OverflowError` (WS3002), `IndexError` (WS3005), `MemberError` (WS3006) and `KeyError` (WS3007): (a) these; (b) others, which you give.

A6: (a).

Done (Claude, 2026-11-13, `31d35c3`): recorded as approved in the specification, section 3.7 ("Classes of values", "Equality", "Typed catches", "`is`" and the runtime errors' classes) and its decision register. A1's extending of `Array`, `Map` and `Set`, A5's question, and what is left of A1 are asked in "Classes of Values, Continued".

## [DONE] Typed Catches

A `catch` that catches only some exceptions (A7 of "M0.10 Syntax: Static Members, `Object` and Exceptions"; specification, section 3.7), on both VMs. Its syntax, and how a `catch` names what it catches, will be proposed as questions once exceptions are implemented. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.10.6.

2026-11-13: exceptions are implemented; the questions below, one decision each, answerable with "yes" or a letter.

Q1, syntax: (a) `catch (e: NotFound) { … }`, the class after a colon; (b) `catch (NotFound e) { … }`, the class before the name, as C# and Java write it.

A1: (b).

Q2, several catches: a `try` may have several, `catch (e: A) { … } catch (e: B) { … } catch (e) { … }`. An exception goes to the first whose class it is an instance of, a class extending it included; a catch without a class catches any exception, and only the last may be one; an exception that none catches goes on outward, after the `finally`. Agreed?

A2: Yes.

Q3, what names the class: (a) any expression that gives a class, evaluated when an exception reaches the catch (`catch (e: errors.NotFound)`); (b) only a class's name, as it is in scope there.

A3: A middle ground: allow dotted names only (a.b.C), with no calls or arbitrary expressions. That covers the `errors.NotFound` case and still has nothing that can fail or have side effects.

Q4, a catch's class that is not a class when an exception reaches it (`var A = 5;` … `catch (e: A)`): (a) WS3001, which replaces the exception; (b) the catch does not match, and the exception goes on.

A4: Can we make the internals such that any value is implicitly an instance of a class, in this case `Integer`?

Q5, runtime errors' classes: (a) every runtime error is an `Error`, told apart by its `code`, as now; (b) each kind has a class of its own, extending `Error` (`TypeError` for WS3001, `IndexError` for WS3005, `KeyError` for WS3007, and so on), so that a catch can name one kind.

A5: (b).

Q6, a thrown value that is not an instance (`throw "x";`): (a) only a catch without a class catches it; (b) a catch may name a kind of value too (`catch (e: String)`), which needs names for the kinds.

A6: (b), especially if we can make every value an instance of a class.

Q7, the same test outside a catch: `x is Point`, true when `x` is an instance of `Point` or of a class extending it. (a) Add it now, with typed catches; (b) not now.

A7: (a).

2026-11-13: recorded as approved in the specification, section 3.7 ("Typed catches", "`is`", and the runtime errors' classes): A1, A2, A3 (dotted names), A5, A6 and A7. A4 asks whether every value could be an instance of a class: it can, and "Typed Catches Syntax: Classes of Values" proposes how, with Q4 asked again and the runtime errors' class names. Waits for those answers.

2026-11-13: answered ("Typed Catches Syntax: Classes of Values"); next, but for extending `Array`, `Map` and `Set`, which waits for "Classes of Values, Continued", and the equality a catch whose name is not a class uses, `==` until then (Q1 there).


Done (Claude, 2026-11-13, `f0fd61d`): `catch (KeyError e)`, several catches tried in order, the last of which may catch anything, dotted names, an exception none catches going on after the `finally`; `x is Point`; every value's class (`5 is Integer`, `catch (String e)`), `null` having none; every value's `equal`; and every runtime error an instance of its kind's class (`IndexError`, `KeyError`, …), on both VMs. One new operation, `IsInstance`. Choices of mine, in section 15.2 as proposed (row "Typed catches and classes of values"): a catch's class is evaluated when an exception reaches it; a catch-everything before the last is WS1001; the built-in classes are added to a program that names them, every runtime error's to one with a `try` or a `using`; the kinds of values' classes are sealed, `Array`, `Map` and `Set` too until "Classes That Extend Array, Map and Set"; a catch whose name is not a class compares with `==` until Q1 of "Classes of Values, Continued" is answered. Two conformance cases changed for decisions of yours: `n.equal(5)` is no longer WS3006, and a caught index error is an `IndexError`. Every new conformance case gives the same result on both VMs. Build and 1,238 tests pass on Windows; Linux not tried.

## [DONE] Asynchrony: `async` and `await`

A15 of "M0.11: Templates": asynchrony is designed first, before templates. WebLingo's own operation model (WS-P007), worker-affine executions (WS-H012) and worker-local heaps (WS-P013) are approved; how a program writes asynchrony is not. One decision each, answerable with "yes" or a letter. Once they are answered, its implementation becomes tasks of its own, before M0.11. Added by Claude, 2026-11-13.

NOTE: C# is annoying requiring that Task be explicitly returned from async methods. WebLingo should allow returning normal values from an async methods which are then magically wrapped in a task by the runtime and delivered to await. OK?

Q1, syntax: `async function f() { … }`, `async` methods and `async function (…) { … }`, whose call gives a task at once; `await t` waits for task `t`, giving its result, or throwing its exception. Agreed?

A1: Yes.

Q2, where `await` may stand: (a) in an `async` function, and at the top level of a script or a template; (b) only in an `async` function.

A2: (a). The top level of a script/module is its executable block. A script/module becomes an instance of `Module`, which has an `exec()` function. Would that work? 

Q3, when an `async` function starts: (a) at once, when it is called, running until its first `await`, as in C# and JavaScript; (b) only when something awaits its task.

A3: (a)

Q4, concurrency: an execution's tasks run on its one worker, one at a time, switching only at `await`, so that no two run at once and nothing needs a lock (WS-H012, WS-P013). Agreed?

A4: How about a real thread pool?

Q5, what `await` takes: (a) only a task, anything else being WS3001; (b) any value, a value that is not a task giving itself.

A5: (b) and awaiting a plain value still yields.

Q6, a task is a value of its own kind, of the built-in class `Task` (section 3.2), with nothing but `await` for now; `Task.all`, `Task.any` and timeouts come when they are needed. Agreed?

A6: OK. Add a TO-DO.

Q7, a task's exception that nothing awaits: (a) it is lost; (b) when the execution ends, the first such exception stops it, as an uncaught one does, so that none goes unnoticed.

A7: (b) with a way to opt out for fire-and-forget.

Q8, where tasks come from, besides `async` functions: the host's operations (files, network, database: WS-P004, WS-P009, WS-P011) give tasks, and the standard library has `delay(milliseconds)`, for tests. Agreed?

A8: Yes.

Q9, the end of an execution: (a) it waits for every task still running; (b) it ends when the top level does, the tasks still running being cancelled.

A9: (b).

Q10, how the VMs do it: a waiting `async` function's frames are kept on the heap, held by its task, and resumed by the worker's scheduler when what it waits for completes; no thread blocks (WS-P007, WS-P008). Agreed?

A10: OK.

Q11, cancellation: an execution's cancellation (WS3004) ends its waiting tasks too, uncatchable, as now. Agreed?

A11: Yes.

Q12, `using` and asynchrony: (a) `dispose()` stays synchronous for now, as section 3.4 begins; (b) a `dispose()` may be `async`, and `using` awaits it.

A12: (b)

Q13, templates: a template's body may `await` anywhere, as an `async` function's may, and `renderComponent(…)` gives a task, which the page awaits, as section 13's example writes. Agreed?

A13: Yes.

Done (Claude, 2026-11-13, `deabd70`): recorded as approved in the specification, a new section 3.8 ("Asynchrony"), its decision register, and section 13. What the answers leave open (A2's `Module`, A4's thread pool, A7's opting out, A12 in a function that is not `async`) is asked in "Asynchrony, Continued"; A6's `Task.all`, `Task.any` and timeouts are the task "Task Combinators"; the implementation is the task "Asynchrony".

## [DONE] Classes of Values, Continued

Follow-ups to "Typed Catches Syntax: Classes of Values". One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

NOTE: It should be possible to extend a class via `extends` as expected, but also via assigning a method or a property of a class directly, which is the same as declaring those as `static`.

Q1, a catch whose name is not a class (A5, "Is there a problem with that?"): no problem, but for one point: `A.equal(e)` runs the program's own `equal` when `A` is an instance whose class overrides it, while the VM is searching for a handler, so that a catch could fail or have side effects, which A3 wanted to avoid. (a) `==`, which runs no code, and differs only for such instances; (b) `A.equal(e)`, as you write, an exception it throws replacing the one being caught.

A1: (b)

Q2, `Function` and `Class`: (a) like `Boolean`, `Integer`, `Number` and `String`: no class extends them, and `new` makes none; (b) classes may extend them.

A2: I am taking back whatever I said that makes you think "`Boolean`, `Integer`, `Number` and `String`: no class extends them". They should essentially be like any other class and it should be possible to extend them with new methods, for example.

Q3, extending `Array`, `Map` and `Set`: `class Stack extends Array { top() { return this[this.length - 1]; } }`; `new Stack()` is an empty array of class `Stack`: indexed, with `length`, `push` and `pop`, visited by `foreach`, `is Array` and `is Stack`, with its own methods. Agreed?

A3: Sure.

Q4, fields on such an instance: (a) allowed, as on any instance; (b) none: only methods.

A4: (a)

Q5, `new Array()`, `new Map()` and `new Set()` make empty ones, as `[]`, `{}` and `#{}` do. Agreed?

A5: Yes.

Q6, the text of a derived array: (a) `Stack [1, 2]`, its class's name before it, as an instance's is; (b) `[1, 2]`, as any array's.

A6: (a). The text of an instance must agree with its literal representation, if available.

Q7, the classes' names: `Integer`, `String` and the rest are in scope everywhere, as `Object` and `Error` are, and a program may declare a name of its own over one. Agreed?

A7: Sure.

Done (Claude, 2026-11-13, `c45b3aa`): recorded as approved in the specification, section 3.7 ("Classes of values", "Typed catches" and "Static members", the NOTE among them). What A2 leaves open, what extending `Boolean`, `Integer`, `Number`, `String`, `Function` and `Class` means, is asked in "Classes of Values, Further".

## [DONE] Asynchrony, Continued

Follow-ups to "Asynchrony: `async` and `await`". One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, concurrency (A4, "How about a real thread pool?"): the approved hosting design runs each execution on one worker (WS-H012), with a heap of its own that no other thread touches (WS-P013), while many executions, a server's requests, run at once on a pool of workers, and blocking I/O runs on a bounded pool of its own (section 6.10), so a server already uses its cores. Tasks of one execution running on several threads at once would share its heap, which needs locks around every object, or a collector that runs beside the program, and makes every program open to data races. (a) Keep one worker per execution, its tasks taking turns at `await`, and add parallel work later as workers with heaps of their own that exchange copied messages (WS-P013's explicit transfer), as JavaScript's workers do; (b) a thread pool for one execution's tasks, their heap shared under locks, changing WS-H012 and WS-P013.

A1: (a)

Q2, scripts as modules (A2, "Would that work?"): it would. A script's or a module's top level becomes the body of an `async` method, `exec()`, of an instance of a built-in class `Module`, which the host calls and awaits; the module's exports would be its instance's members. It belongs with `import` ("M0.11 Syntax: Templates, Continued", Q3). (a) Adopt it now, as what a script is; (b) decide it with `import`, when the module system is designed.

A2: (a)

Q3, forgetting a task (A7's opting out): (a) `t.forget()`, a method of the task, after which nothing reports its exception; (b) a statement, `forget t;`; (c) a function, `Task.forget(t)`.

A3: (a).

Q4, an `async` `dispose()` in a function that is not `async` (A12), where `using` cannot wait for it: (a) WS3001, at the end of the `using`; (b) the disposal starts, and is not waited for, as if forgotten.

A4: (a)

Q5, its milestone: asynchrony comes before templates, so (a) it is M0.11, templates becoming M0.12 and WBC M0.13; (b) it is a part of M0.10, M0.10.8; (c) another, which you give.

A5: (a)

Done (Claude, 2026-11-13, `c45b3aa`): recorded as approved in the specification, section 3.8, its decision register, and section 9, where asynchrony is M0.11, templates M0.12 and WBC M0.13.

## [DONE] M0.11 Syntax: Templates, Continued

Follow-ups to "M0.11: Templates": what its answers leave open. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, the technology's name, WebLingo with templates in a web server: (a) WebLingo Pages, as you suggest; (b) WebLingo Server Pages; (c) another, which you give.

A1: (a)

Q2, its command-line program, beside `weblingo`: (a) `wlpages`; (b) `weblingo-pages`; (c) another, which you give. Its project would be `WebLingo.Pages.Cli`, beside `WebLingo.Cli`.

A2: `welp`.

Q3, `import`: (a) the language's module system, for `.ws` files and templates alike (section 2.3 plans modules), designed as a task of its own, since a general-purpose language needs one; (b) for templates only: `<% import "/x.wsp"; %>`, a template run in a scope of its own.

A3: (a)

Q4, default values in a component's parameters: `<%@ params product, size = 10 %>`, a default being any expression, evaluated when the parameter is missing. Agreed?

A4: Yes. It should be possible to specify each parameter separately, like var declarations, so \

```
<%
  param product;
  param size = 10;
%>
```

Why couldn't we have `param` as a regular keyword in the language (like `var`), specifying a parameter to a module?

Q5, functions too: `function f(a, b = 1) { … }`, by the same rule, so that functions and components agree: (a) yes; (b) components only.

A5:  Yes, functions too.

Q6, a layout's parameters: a layout declares them as a component does, `<%@ params title %>`, and is given them by the page's `layout` statement, a missing or an extra one being an error. Agreed?

A6: Yes, but as in A4.

Q7, components found at run time (A13): each is compiled on first use and kept, recompiled when its file changes, in the application's generation (WS-T004, WS-T007); a missing one is a runtime error, WS5xxx, which a catch may catch. Agreed?

A7: Yes.

Done (Claude, 2026-11-13, `c45b3aa`): recorded as approved in the specification, section 13 ("Contracts") and section 3.5 (functions' default values). A4's question, `param` as a keyword of the language, is asked with the module system in "Modules: `import` and `param`"; functions' default values are the task "Default Parameter Values".

## [DONE] Classes That Extend Array, Map and Set

A1 of "Typed Catches Syntax: Classes of Values": classes may extend `Array`, `Map` and `Set` (specification, section 3.7, "Classes of values"), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "Classes of Values, Continued".

2026-11-13: answered; next, with A1's `A.equal(e)` for a catch whose name is not a class, and the NOTE's static members by assignment.

Done (Claude, 2026-11-13, `439330d`): `class Stack extends Array { … }`, and `Map` and `Set` likewise: `new Stack()` is an array of class `Stack`, with its methods and fields, written `Stack [1, 2]`; `new Array()`, `new Map()` and `new Set()` make empty ones; a catch, or `is`, whose name is not a class calls its `equal()` (A1); and assigning a class a member it has not declares a static one (the NOTE), on both VMs. No new operation: a classed collection's value is marked, so that plain collections cost nothing more (the first try, which looked at the heap, was 5 to 8% slower on `collections`; now within the noise). Choices of mine, in section 15.2 as proposed (row "Classes extending `Array`, `Map` and `Set`"): a member is a field, then a class's method, then the collection's own, so a class may override `push`, but `super.push(…)` is WS3006; the text shows no fields; `dispose()` and `using` do not apply to such a collection yet. `Boolean`, `Integer`, `Number`, `String`, `Function` and `Class` stay sealed until "Classes of Values, Further" is answered. Two conformance cases removed for your decisions (`A.x = 1;` and `class S extends Array {}` no longer fail). Every new conformance case gives the same result on both VMs. Build and 1,252 tests pass on Windows; Linux not tried.

## [DONE] Default Parameter Values

`function f(a, b = 1) { … }`, for functions, methods and constructors (A5 of "M0.11 Syntax: Templates, Continued"; specification, section 3.5), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `2305a0d`): default values for the parameters of functions, function expressions, methods and constructors, on both VMs: evaluated when the parameter is left out, a `null` given being a value; a call checked against the range of arguments it may give. One new operation, `JumpIfGiven`, and a function's `OptionalCount`; the call path gained no work for a call that gives every argument, and `calls` takes the same time. Choices of mine, in section 15.2 as proposed (row "Default values"): a default is evaluated before its own parameter is declared, so it may use the ones before it, and `b = b` names an outer `b`; a parameter without a default after one with one is WS1001. Every new conformance case gives the same result on both VMs. Build and 1,275 tests pass on Windows; Linux not tried.

## [DONE] Classes of Values, Further

A2 of "Classes of Values, Continued": `Boolean`, `Integer`, `Number`, `String`, `Function` and `Class` are to be like any other class, and extendable, with new methods for example. What that means needs deciding. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, new methods for a kind's values: (a) a block that adds methods to an existing class, `extend Integer { isEven() { return this % 2 == 0; } }`, after which `4.isEven()`, as C#'s extension methods and Swift's extensions do; (b) only subclasses, `class Even extends Integer { … }`; (c) both.

A1: (c).

Q2, a subclass of `Integer`, `Number`, `String` or `Boolean`, if there are any: `new Celsius(21.5)` holds a number, which arithmetic, comparisons and `==` use, `is Number` and `is Celsius`, with its own methods and fields, written `Celsius 21.5`. Agreed?

A2:  Yes. Maybe write those [class-name]:[value(s)], i.e. `Celsius:21.5`, or `Celsius(21.5)` i.e. the same way as constructing it, except without the `new`.

Q3, `new` on those classes: (a) `new Integer()` is `0`, `new String()` `""`, `new Boolean()` `false`, and `new Integer(5)` `5`; (b) WS3001, their values being made by literals.

A3: (a). So `var x = new Integer(42);` is the same as `var x = 42`.

Q4, `Function` and `Class`: (a) as (1) to (3) say for the others, a subclass of `Function` being a function with methods of its own; (b) new methods only, as (1) (a), with no subclasses.

A4: (a).

Done (Claude, 2026-11-13, `b0d3ff3`): recorded as approved in the specification, section 3.7 ("Classes of values"). A2's way of writing such an instance, and what `extend`, `new Function(…)` and `new Class(…)` need besides, are asked in "Classes of Values, Extensions"; the implementation is the task "Extending the Classes of Values".

## [DONE] M0.11.1: Tasks and `await`

`async` functions, methods and function expressions, `await`, tasks, their exceptions, `t.forget()`, the end of an execution, `delay()`, and a benchmark workload (specification, sections 3.8 and 9), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `1a7bee3`): on both VMs, with their conformance tests, and the benchmark's `async` workload (specification, section 5.6). The choices the specification leaves open are recorded as Proposed in its section 15.2 ("Asynchrony"), among them two codes, WS2014 (`await` outside an `async` function) and WS3009 (a task that nothing can complete); until M0.11.2, a disposal whose dispose method is `async` is WS3001 when it begins. Checks: the Release build, without warnings, the 1,363 tests, and the CLI on the samples and on programs that await, on Windows only; Linux not tried.

## [DONE] M0.11: Asynchrony

`async` functions and methods, `await`, tasks, and asynchronous disposal (specification, section 3.8), on both VMs, before M0.12's templates, which use them. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "Asynchrony, Continued", whose first question, concurrency, shapes it.

2026-11-13: answered; it is M0.11 (specification, section 9), after the tasks above it.

2026-11-13: M0.11 is split into the parts the specification defines in section 9 ("M0.11 parts"), each a task below. Waits for them.

Done (Claude, 2026-11-13, `9d3247e`): its last part, M0.11.2, landed.

## [DONE] M0.11.2: Asynchronous Disposal

An `async` `dispose()`, which `using` awaits, and WS3001 where it cannot (specification, section 3.8), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `9d3247e`): on both VMs, with their conformance tests. Its choices are recorded as Proposed in the specification, section 15.2 ("Asynchrony"); what `x.dispose()` gives when its disposal is `async` is asked in "Asynchronous Disposal, Continued", built meanwhile as that question's (a). Checks: the Release build, without warnings, the 1,398 tests, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Classes of Values, Extensions

Follow-ups to "Classes of Values, Further". One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, writing an instance of a class extending a kind of value (A2): (a) `Celsius:21.5`; (b) `Celsius(21.5)`, as it is made, without `new`; and the collections the same way, `Stack:[1, 2]` or `Stack([1, 2])`, in place of `Stack [1, 2]`, so that every classed value is written alike.

A1: (b).

Q2, `extend`'s reach: (a) the class itself, everywhere, from when the `extend` runs: every instance and subclass, made before or after, has the new methods; (b) only code where the extension is in scope, as C#'s extension methods are.

A2: (a).

Q3, when an `extend` runs: (a) it is hoisted with its block's classes, and runs when the block begins, after they are made; (b) where it stands, in order with the statements around it.

A3: (a)

Q4, an extension's method whose name the class has already: (a) an error, WS3001 when the `extend` runs; (b) it replaces the class's own, as a subclass's would.

A4: (b)

Q5, what an `extend` block may hold: (a) methods only; (b) methods and static members, `static` as in a class.

A5: (b) 

Q6, arithmetic on a boxed value: `new Celsius(21.5) + 1` is (a) a plain number, `22.5`; (b) a `Celsius`, its class kept.

A6: (b). Wouldn't it depend on the meaning of `+`, so that we would need operator overloading for full control?

Q7, the held value: a class extending `Number` gives it by its constructor's `super(value)`, as `new Number(value)` makes one; a class without a constructor passes its arguments on, so `new Celsius(21.5)` holds `21.5`. Agreed?

A7: Yes.

Q8, `Function`: `new Function(f)` is `f` itself, and `class Handler extends Function { constructor(f) { super(f); } }` makes a function that calls `f`, with `Handler`'s methods and fields; `new Function()` is a function that does nothing and gives `null`. Agreed?

A8: Yes.

Q9, `Class`: (a) `new Class()` and `extends Class` are WS3001, classes being made by `class` declarations, and `Class` takes new methods only by `extend`, which every class then has (`Point.describe()`); (b) as for the other kinds, with a design of its own to come.

A9: Well, internally `Class` is represented by some object, right? `new Class()` should make an instance of that object, that can then be patched by giving it a name, adding methods and fields, etc, at run time, after which it functions as if the class was declared normally in code. So it should in principle be possible to construct classes, and their methods, and fields, completely programmatically.

Done (Claude, 2026-11-13, `ec4677a`): recorded as approved in the specification, section 3.7 ("Classes of values"). What A6 and A9 leave open, and reading A1's text back, are asked in "Classes of Values, Extensions, Continued".

## [DONE] Asynchronous Disposal, Continued

What "M0.11.2: Asynchronous Disposal" left to the owner. Added by Claude, 2026-11-13.

Q1, `x.dispose()` on an instance whose disposal has an `async` method: (a) it gives a task, which completes, with `null`, once the last of its methods has run, so that `await x.dispose();` waits for it; (b) it gives `null`, as a disposal does now, and the disposal goes on by itself.

A1: (a)

2026-11-13: M0.11.2 built (a), which is easy to change: each VM gives the task in one place (`Gave`).

Done (Claude, 2026-11-13, `ec4677a`): recorded as approved in the specification, section 3.8 ("`using`"); M0.11.2 had built it so.

## [DONE] Modules: `import` and `param`

The language's module system, which A3 of "M0.11 Syntax: Templates, Continued" makes `import`, and A4's `param` as a keyword of the language, a module's parameter; A2 of "Asynchrony, Continued" makes a script, or a module, an instance of `Module`. Templates (M0.12) need both. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, importing: (a) `import db from "lib/db.ws";`, a statement at a module's top level, naming the module's instance; (b) `var db = Module.import("lib/db.ws");`, an expression, anywhere.

A1: (c) both. (a) is static, known at compile time, (b) is dynamic, only at runtime.

Q2, when a module runs: once per execution, the first time it is imported; every later import gets the same instance, already run. Agreed?

A2: Yes.

Q3, exports: (a) every top-level declaration of a module is a member of its instance; (b) only those marked `export` (`export function f() { … }`).

A3: (b).

Q4, paths: as A9 of "M0.11: Templates" has it for the language: from the importing file's folder, `./` that folder, `/` the drive's root; and `.ws` may be left out. Agreed?

A4: Yes.

Q5, cycles: a module that imports itself, directly or through others: (a) a compile-time error; (b) allowed, the second import getting the instance as it is so far, as JavaScript does.

A5: (b).

Q6, `param` in the language: `param name;` and `param size = 10;` declare a module's parameters, at its top level, before its other statements, each a `const` of it; an importer gives them, `import db from "lib/db.ws" { size: 10 };`, and a template's caller gives a component's or a layout's. Agreed?

A6: Yes.

Q7, a parameter without a default that is not given: (a) an error, where the module is imported, of a new code; (b) `null`.

A7: (a).

Q8, a script's parameters from the command line: `weblingo run script.ws size=10 name=Anna`, each value read as a WebLingo literal if it is one (`10`, `true`), as a string otherwise. Agreed?

A8: Yes. But, `weblingo run script.ws value="10"` reads as `String(value)`.

Q9, when imports are compiled: (a) with the importing program, so that a program is complete before it runs; (b) when they run, as `renderComponent` finds its component.

A9: Static imports can be resolved at compile time so they can be compiled right away. Dynamic imports compiled at the time of executing `Module.import(...)`.

Q10, the standard library: it becomes modules imported by name (`import fs from "std/fs";`), while `write`, `writeLn` and the like stay built-ins of every program. Agreed?

A10: Well, even built-ins should be treated like they belong to modules, so maybe `write` and `writeLn` are in `system`, but `system` is always implicitly imported by all modules.

Done (Claude, 2026-11-13, `fb78f7f`): recorded as approved in the specification, section 3.9, a new section. What the answers leave open is asked in "Modules, Continued"; the implementation is the task "Modules".

## [DONE] Extending the Classes of Values: Classed Collections' Text

A collection of a class extending `Array`, `Map` or `Set` written `Stack([1, 2])`, in place of `Stack [1, 2]` (specification, sections 3.7 and 9), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `2af1f42`): on both VMs, with its conformance tests. Checks: the Release build, without warnings, the 1,400 tests, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Extending the Classes of Values: `extend`

`extend` blocks: methods and static members given to a class, a program's or a kind of value's, hoisted with its block's classes, reaching every instance and subclass (specification, sections 3.7 and 9), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `6b05174`): on both VMs, with its conformance tests; its choices are recorded as Proposed in the specification, section 15.2 ("Extensions"). Checks: the Release build, without warnings, the 1,450 tests, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Classes of Values, Extensions, Continued

Follow-ups to "Classes of Values, Extensions". One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, the class of an arithmetic result (A6): (a) its left operand's class, if it has one, else its right's: `new Celsius(21.5) + 1` and `1 + new Celsius(21.5)` are both a `Celsius`, and `celsius + fahrenheit` a `Celsius`; (b) a class only when its classed operands all have the same one, a plain value otherwise.

A1: (a)

Q2, how arithmetic makes that result: (a) a new instance of the class, holding the result, its constructor not run, with no fields; (b) the same, with the fields of the operand whose class it is, copied.

A2: (b)

Q3, operators (A6's remark: defining `+` is what gives a class full control of it): (a) a class may declare what an operator does with its values, as C# does, by a static method, `static operator +(a, b) { … }`, which `+` calls when either operand's class declares it, the left's first, and likewise for `-`, `*`, `/` and the comparisons, Q1's default applying where none does; (b) later, as a design of its own, Q1's default holding until then.

A3: (a)

Q4, reading A1's text back: `Celsius(21.5)` and `Stack([1, 2])` agree with a literal representation only if calling a class makes an instance: (a) calling a class makes one, as `new` does, so that `Celsius(21.5)` in a program is `new Celsius(21.5)` (it is WS2005 now); (b) they stay a text only, and calling a class stays WS2005.

A4: (b) and we use the display reprsenetation `Class:Value(s)`

Q5, `new Class()` (A9) needs what "Adding Methods at Run Time" describes, a function with a receiver and a way to attach it to a class, and that task is deferred: (a) design them now, with `new Class()` and `extends Class`, as a task of their own after "Extending the Classes of Values"; (b) they wait with that task, and `new Class()` and `extends Class` stay WS3001 until then.

A5: (a)

Done (Claude, 2026-11-13, `e871811`): recorded as approved in the specification, section 3.7 ("Classes of values"), with parts 6 and 7 in section 9 ("Extending the classes of values") for A4's text and A3's operators, each a task below; A5's design is the task "Building Classes at Run Time".

## [DONE] Extending the Classes of Values: Classed Values' Text

A classed value written as its class's name, a colon and its value, `Stack:[1, 2]`, in place of `Stack([1, 2])` (A4 of "Classes of Values, Extensions, Continued"; specification, sections 3.7 and 9), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `81b722b`): for collections, on both VMs, with their conformance tests; part 3 writes the other classed values so. Checks: the Release build, without warnings, the 1,450 tests, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Extending the Classes of Values: `Boolean`, `Integer`, `Number` and `String`

Classes extending them, whose instances hold a value, given by `super(value)`, which arithmetic, comparisons and `==` use, written `Celsius(21.5)`; and `new` on those classes (specification, sections 3.7 and 9), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `fb8815d`): on both VMs, with their conformance tests; its choices are recorded as Proposed in the specification, section 15.2 ("Classes holding a value"), two of them asked in "Classes of Values, Constructors". Checks: the Release build, without warnings, the 1,476 tests, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Extending the Classes of Values: `Function`

`new Function(f)`, `new Function()`, and classes extending `Function`, whose instances are functions with methods of their own (specification, sections 3.7 and 9), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `ab81414`): on both VMs, with their conformance tests; its choices are recorded as Proposed in the specification, section 15.2 ("Classes holding a value"): `new Function()` is an instance of `Function` holding a function that does nothing, written `Function:function`. Checks: the Release build, without warnings, the 1,492 tests, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Extending the Classes of Values: Operators

`static operator +(a, b) { … }`, and `-`, `*`, `/` and the comparisons, which a class may declare (A3 of "Classes of Values, Extensions, Continued"; specification, sections 3.7 and 9), on both VMs. Added by Claude, 2026-11-13.

Done (Claude, 2026-11-13, `185690f`): on both VMs, with their conformance tests; its choices are recorded as Proposed in the specification, section 15.2 ("Operators"), among them that `==` and `!=`, and unary operators, are not declared. Checks: the Release build, without warnings, the 1,520 tests, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Static Members Callable on Instances

Let's make sure that class static members are accessible through instance references (unless shadowed by the instance member) as well as class references. 

Done (Claude, 2026-11-14, `9b9bf93`): on both VMs, with their conformance tests; recorded in the specification, section 3.7 ("Static members"), and section 15.2 ("Static members"). What assigning one through an instance does is asked in "Static Members Through Instances, Continued". Checks: the Release build, without warnings, the 1,528 tests, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Rename WebScript's Leftovers to WebLingo

The project was renamed from WebScript to WebLingo (specification, section 1), but kept WebScript's `ws` in its file extensions, its diagnostic codes, its specification IDs and its type names. The owner decided, 2026-11-13, in a chat with Claude, to rename them all now, while nothing is released: "Better now than later." Added by Claude, 2026-11-14.

- **Scripts:** `.ws` becomes `.wl`.
- **Templates:** `.wsp` becomes `.wlp` (the owner's answer: yes).
- **Diagnostic codes:** `WS1001` becomes `WL1001`, and so on: the numbers and their meanings stay, the prefix changes.
- **Specification IDs:** `WS-BC002` becomes `WL-BC002`, `WS-T001` `WL-T001`, and so on.
- **Type names in source:** the `Ws` prefix becomes `WebLingo` (`WsLexer` becomes `WebLingoLexer`, `WsValue` `WebLingoValue`, and so on; the owner's choice, 2026-11-14, over `Wl`), with the files named after them.
- **No backward compatibility:** the old extension is not accepted, and the old codes and names are not kept as aliases (the owner: "Don't worry about backwards compatibility. We are building and designing the system as we speak.").

Where it reaches: the CLI and anything else that looks at an extension; the samples, and the benchmark's workloads (`Workloads/*.ws`); every test that writes a source file or checks a code (`program.ws`, `"WS2001"`); `WsDiagnosticCodes` and every `Ws` type, in every project; the specification (sections 1, 1.1, 2.2, 6.9, 8.4, 13, 15.2, and every `WS-` ID and `WS` code), `Documents/implementation.md` (sections 1, 7, 12, and its paths), `Documents/NativeCompatibility.md`, `WebLingo/README.md`, `TO-DO.md` and `DONE.md`'s open text, and the GitHub workflow if it names any. `Documents/implementation.md`, section 12, lists the extensions and the `WS` prefixes among the names not to change without asking: this task is the owner's asking, and the section is updated to the new names. The rename is mechanical, so it lands as one change, checked by the Release build, the full test suite, and the CLI run on the renamed samples, on both VMs.

Done (Claude, 2026-11-14, `68f65d5`): as listed, on both VMs: 60 files renamed (the `Ws` types' and the samples' and workloads'), and every `WS` code and ID, `Ws` type name and `.ws` or `.wsp` in the solution, the documents and `TO-DO.md` (but this task's own text); recorded in the specification, sections 1 and 1.1, and `Documents/implementation.md`, sections 1, 5 and 12. `DONE.md` is left as written, a record of what was done under the old names. WSHI, the host interface's name (specification, section 6), is not in the list, and is asked in "WSHI". Checks: the Release build, without warnings, the 1,528 tests, and the CLI on the renamed samples, on Windows only; Linux not tried.

## [DONE] Modules, Continued

Follow-ups to "Modules: `import` and `param`" (specification, section 3.9). One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, `Module.import(path)`: a module's top level is `async` (`exec()`), so it gives a task, which completes with the instance once the module has run, `var db = await Module.import("lib/db.wl");`. Agreed?

A1: Yes.

Q2, when a static import's module runs: (a) before the importing module's top level, in the order of its imports, each awaited, so that the importer begins with its imports run, as JavaScript's do; (b) where its `import` stands, in order with the statements around it.

A2: (a)

Q3, a module's lifetime under a host: A2 runs a module once per execution, and section 2.3 once per application generation: (a) once per execution, a request's, its instance the request's own; (b) once per application generation, its instance shared by the requests, which then cannot change it, since a heap is one worker's (WL-P013).

A3: (a)

Q4, `export` marks a `function`, `class`, `var` or `const` declaration at a module's top level, and an importer reads an exported `var` as it is when read (`db.count`); assigning it: (a) WL3001, an export being the module's to change; (b) allowed, as a member is assigned.

A4: (a)

Q5, importing names: (a) only the instance, `import util from "lib/util.wl";`, then `util.f()`; (b) names too, `import { f, g } from "lib/util.wl";`, as JavaScript does.

A5: (b), both forms allowed, really

Q6, a quoted parameter (A8): a shell removes the quotes of `value="10"`, so `weblingo` gets `value=10`, an integer: (a) quotes that reach it make a string, `value='"10"'` in a shell; (b) `weblingo run script.wl --string value=10` makes a string, whatever the value looks like.

A6: "a shell removes the quotes " Does it? Have you checked?

Q7, the standard modules' names: (a) `system` and `std/fs`, as A10 and Q10 name them; (b) every one under `std/`, `std/system` among them; (c): standard (built-in) modules named by a scheme, std:fs and std:system, resolved by the compiler and runtime, never as paths.

A7: (c)

Done (Claude, 2026-11-14, `f7e55ef`): recorded as approved in the specification, section 3.9, and A3 in section 2.3 too. A6 is answered in "Modules, Quoted Parameters", which asks it again.

## [DONE] Modules: Static Imports and Exports

`import db from "lib/db.wl";`, `import { f, g } from "lib/util.wl";`, `export`, a module's instance, each module run once, its imports first, cycles, the host finding the files, and diagnostics and runtime errors naming their file (specification, sections 3.9 and 9), on both VMs. Added by Claude, 2026-11-14.

Done (Claude, 2026-11-14, `a17b034`): on both VMs, with conformance tests through the CLI, `ModuleTests` (`WebLingo.VM.Tests`), and a sample, `samples/Modules`; its choices are recorded as Proposed in the specification, section 15.2 ("Modules"), among them a new code, WL2015, for a module that cannot be found. Checks: the Release build, without warnings, the 1,540 tests, the benchmark against `main`'s, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] WSHI

A follow-up to "Rename WebScript's Leftovers to WebLingo". Added by Claude, 2026-11-14.

Q1, WSHI, the host-neutral interface's name (specification, sections 6.2, 6.6 and 6.8), WebScript's: (a) WLHI; (b) WSHI, as it is.

A1: (a) WLHI, but you need to define it first (fully spelled out) in the spec, and describe what it is: : the full name in words, and a short paragraph in §6.2 saying which contracts make up the host interface, with references to §6.4, §6.6, §6.7 and §6.8.

Done (Claude, 2026-11-14, `796e6c4`): WLHI, the WebLingo Host Interface, defined in the specification, section 6.2: what it is, and the contracts it is made of, the runtime's lifecycle (§6.4), a request's (§6.6), the HTTP request and response (§6.7), and the native ABI (§6.8); WSHI renamed WLHI everywhere but `DONE.md`.

## [DONE] Classes of Values, Constructors

What "Extending the Classes of Values: `Boolean`, `Integer`, `Number` and `String`" chose where the answers so far leave it open (specification, section 15.2, "Classes holding a value"). One decision each, answerable with a letter. Added by Claude, 2026-11-13.

Q1, A7's "a class without a constructor passes its arguments on": (a) for every class, so that `class B extends A {}` makes `new B(1)` run `A`'s constructor with `1` (it is WS2006 now, a class without a constructor taking none); (b) only for a class holding a value, as built.

A1: Only if A defines a constructor that takes a parameter. In general, supplying a parameter where none is expected is an error. This is a special case that saves calling base(1).

Q2, an arithmetic result not of its class's kind, `count / 2` where `Count` extends `Integer`, a binary64: (a) a plain value, `1.5`, as built; (b) WS3001.

A2: (a)

Done (Claude, 2026-11-14, `e992b43`): recorded as approved in the specification, section 3.7 ("Instances" and "Classes of values"): every class without a constructor passes its arguments on to the nearest base that declares one, and with none takes none; on both VMs, with their conformance tests. This task's heading was lost from `TO-DO.md` in the commit `68f65d5`, and is restored. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Modules: Standard Modules

The scheme `std:`, and `std:system`, which every module imports, with `write` and `writeLn` (specification, sections 3.9 and 9), on both VMs. Added by Claude, 2026-11-14.

Done (Claude, 2026-11-14, `318f33a`): `std:system`, the host's built-ins, on both VMs, with its tests in `ModuleTests`; its choices are recorded as Proposed in the specification, section 15.2 ("Modules"). It needed no dynamic imports. Other standard modules (`std:fs`) come with the services they give. Checks: the Release build, without warnings, the 1,549 tests, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Properties

With getters and setters.

The design needs the owner's decisions first. One decision each, answerable with "yes" or a letter. Questions added by Claude, 2026-11-14.

Q1, declaring one: (a) `get area() { return this.w * this.h; }` and `set area(value) { … }` in a class, as JavaScript does; (b) a block, `property area { get { … } set(value) { … } }`, as C# groups them.

A1: (b)

Q2, using one: `p.area` runs its getter, and `p.area = 5` its setter, with `5`; a property with only a getter cannot be assigned (WL3001), and one with only a setter cannot be read (WL3006). Agreed?

A2: Yes.

Q3, a property and a field of the same name: (a) the property wins, always, so a setter keeps its value in a field of another name (`this._area`), as in JavaScript; (b) a property's own field, `field` inside its getter and setter, as C# 14 has it, which nothing else can reach.

A3: (b)

Q4, where else: static properties (`static get count() { … }`), properties in `extend` blocks, a subclass's replacing its base's, and `super.area` reading the base's, all as methods do. Agreed?

A4: Yes.

Q5, text: `write` writes an instance's fields, as now, and not its properties, which are computed. Agreed?

A5: Yes. That's what toString() overriding is for. How would it write the `field` of a property though? Presumably as property name.

2026-11-14 (Claude): answered, and recorded as approved in the specification, section 3.7 ("Properties"), A5's `field` written under the property's name, as A5 presumes. The implementation, on both VMs, waits behind the tasks above it.

Done (Claude, 2026-11-14, `e657f29`): on both VMs, with conformance tests; the choices the answers leave open are recorded as Proposed in the specification, section 15.2 ("Properties"), among them that a static property is reached through its class only, while "Static Members Through Instances, Continued" is open, and that a variable named `field` declared around a property does not reach into its accessors. The specification's section 3.7 wrote WS3001 and WS3006 for this task, and section 3.9 WS3001, which the renaming had missed: now WL3001 and WL3006. Checks: the Release build, without warnings, the 1,605 tests, the benchmark against `main`'s (no workload slower beyond the machine's noise, `objects` within 0.2 percent on both VMs, once an assignment looked its class up once), and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Building Classes at Run Time

`new Class()`, which a program names and gives methods while it runs, after which it acts as a declared class does (A9 of "Classes of Values, Extensions", and A5 of "Classes of Values, Extensions, Continued": design it now, with what "Adding Methods at Run Time" describes, and `extends Class`). One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

**Q1, making one: (a) `new Class("Point")`, a class extending `Object`, or `new Class("Point", Shape)`, extending `Shape`, its name and base given when it is made; (b) `new Class()`, named later, `c.name = "Point"`, as A9 has it.**

A1: (a) AND (b), both. 

**Q2, a function with a receiver ("Adding Methods at Run Time", Q1): `function (this, prefix) { … }`, its first parameter named `this`, bound by a call's target (`p.describe("x = ")`), or given as the first argument when it is called directly (`describe(p, "x = ")`). Agreed?**

A2: Yes.

**Q3, giving a class a method: (a) a built-in method of every class, `Point.addMethod("describe", f)`, after which its instances, and its subclasses', have it, replacing one of that name, as an extension's does; (b) a statement, `extend Point with describe = f;`.**

A3: (a),

**Q4, its constructor: (a) a method like the others, `Point.addMethod("constructor", f)`, which `new Point(…)` runs; (b) one of its own, `Point.setConstructor(f)`.**

A4: (a)

**Q5, `extends Class`: (a) WL3001 still, `new Class(…)` being how a program makes a class; (b) a class extending `Class` makes classes, `new Meta("Point")`, its methods becoming theirs as static ones, as a metaclass does.**

A5: We need to be careful here. Let's fully lay out how this might work. Create an example of a class created programmatically at run time, with a method being added to it, a static method, a field, a static field, a property (get, set), a static property (get, set), and a const. Then create an instance of this class and call all its members to exercise them.

2026-11-14 (Claude): A5 laid out. A1 to A4 settle making a class, a function with a receiver, `addMethod`, and the constructor; the rest of the example below is a proposal, each new point a question after it. The example builds `Point` while the program runs, then uses every member it gave it:

```javascript
// The class: named and based when it is made (A1 (a)); new Class() would make an unnamed one, named later (A1 (b)).
var Point = new Class("Point");                 // extends Object; new Class("Point3", Point) would extend Point

// Its constructor and a method: functions with a receiver (A2), given by addMethod (A3, A4).
Point.addMethod("constructor", function (this, x, y) { this.x = x; this.y = y; });
Point.addMethod("describe", function (this, prefix) { return prefix + this.x.toString() + ", " + this.y.toString(); });

// A static method: a function without a receiver (Q6).
Point.addStaticMethod("origin", function () { return new Point(0, 0); });

// A field: nothing to declare; the constructor assigns this.x and this.y, as a declared class's does (section 3.7, "Fields").

// A static field: assigned, which declares it, as for a declared class (section 3.7, "Static members").
Point.count = 0;

// A property: its getter and its setter, functions with a receiver, null for none (Q7).
Point.addProperty("sum",
    function (this) { return this.x + this.y; },
    function (this, value) { this.x = value; this.y = 0; });

// A static property: its getter and its setter, functions without a receiver (Q7).
Point.addStaticProperty("unit",
    function () { return Point.unitValue; },
    function (value) { Point.unitValue = value; });
Point.unitValue = 1;

// A constant: a static member that cannot be assigned (Q8).
Point.addConstant("dimensions", 2);

// Using every member.
var p = new Point(3, 4);
writeLn(p.describe("p = "));             // p = 3, 4             the method
writeLn(Point.origin());                 // Point {x: 0, y: 0}   the static method
writeLn(p.x);                            // 3                    a field
Point.count = Point.count + 1;
writeLn(Point.count);                    // 1                    the static field
writeLn(p.sum);                          // 7                    the property's getter
p.sum = 10;
writeLn(p);                              // Point {x: 10, y: 0}  its setter
Point.unit = 5;
writeLn([Point.unit, Point.unitValue]);  // [5, 5]               the static property
writeLn(Point.dimensions);               // 2                    the constant
try { Point.dimensions = 3; } catch (e) { writeLn(e.code); }    // WL3001
writeLn([p is Point, p is Object, Point.name, Point]);          // [true, true, "Point", class Point]
```

From then on `Point` acts as a declared class does: `class Line extends Point { … }` may extend it, `extend Point { … }` extend it, and `catch (Point e)` catch its instances. A method, a property or a constant given after its instances were made reaches them too, as an `extend` block's does.

**Q6, a static method: (a) `Point.addStaticMethod("origin", f)`, a static method, which cannot be assigned (WL3001), as a declared class's cannot; (b) none: `Point.origin = f;` declares a static field holding `f`, which `Point.origin()` calls, as it does now.**

A6: (b). Clarification: a declared class's static method should be replaceable, too, so somehow it needs to be assignable. `Point.setStaticMethod("origin",f)` maybe?

**Q7, properties: `Point.addProperty("sum", getter, setter)`, each a function with a receiver, or `null` for none, and `Point.addStaticProperty("unit", getter, setter)`, each without one, acting as a declared class's properties do (section 3.7, "Properties"). A run-time property has no `field` of its own, `field` being a word of the `property` syntax only: it keeps its value in a field it names (`Point.unitValue` above). Agreed?**

A7: Yes, but like in A6, a setter for property is needed, too.

**Q8, constants: (a) `Point.addConstant("dimensions", 2)`, a static member that cannot be assigned (WL3001), and `static const dimensions = 2;` in a declared class, which is WL1001 now; (b) none: a static property with only a getter serves.**

A8: (a), with A6 - a setter.

**Q9, a class's name: `Point.name` gives a class's name, a string, or `null` for an unnamed one; assigning it names an unnamed class, once, and is WL3001 for a named one, so that a class may no longer declare a static member called `name` (WL2002). An unnamed class is written `class`, and its instances `class {x: 1}`. Agreed?**

A9: Should be perhaps `className`. It makes possible things like `Integer.className = "Double"`, but that's OK, I guess. With power comes responsibility.

**Q5 again, `extends Class`, laid out: (b) would let a class make classes and give them members of their own, as a metaclass does:**

```javascript
class Entity extends Class {
    constructor(name) {
        super(name);                                               // the new class, named
        this.addMethod("save", function (this) { return "saved " + this.toString(); });
    }
    describe() { return "entity " + this.name; }                   // a member of every class Entity makes
}
var User = new Entity("User");                                     // a class, of class Entity
writeLn([User.describe(), User is Entity, User is Class]);         // ["entity User", true, true]
var u = new User();                                                // an instance of User
writeLn([u.save(), u is User]);                                    // ["saved User {}", true]
```

**(a) WL3001 still, `new Class(…)` and the `add…` methods being how a program makes a class; (b) as above: an instance of a class extending `Class` is a class, its constructor's `super(name)` making it, and its class's methods are its own members, as its static members are, after them.**

A5 (again): (b). Then, an object instance must be able to point to its class: `p.instanceOf`, and class instance to its parent `Point.parent`  (->  `Object`).

Done (Claude, 2026-11-14, `c485b03`): the design recorded as approved in the specification, section 3.7 ("Classes of values"), A9's `className` renaming any class, as A9 allows; the parts of its implementation defined in section 9 (part 5 of "Extending the classes of values"), each a task below "Extending the Classes of Values: `Class`". What A6 to A9 and A5 leave open is asked in "Building Classes at Run Time, Continued". Checks: the Release build, without warnings, and the full test suite, on Windows only; Linux not tried.

## [DONE] Extending the Classes of Values: `Class`, Making Classes

`new Class("Point")`, `new Class("Point", Shape)` and `new Class()`; functions with a receiver, `function (this, a) { … }`; and `Point.addMethod(name, f)`, a constructor included (specification, section 3.7, and part 5.1 of "Extending the classes of values", section 9), on both VMs. Added by Claude, 2026-11-14.

Done (Claude, 2026-11-14, `d009021`): on both VMs, with conformance tests; the choices the design leaves open are recorded as Proposed in the specification, section 15.2 ("Classes made while the program runs"), among them that a field holding a function with a receiver runs it on its first argument, not on the field's instance, as a field's function is called as any function is, and that `addMethod` takes a name only, so that no program names a property's accessor or an operator with it. Checks: the Release build, without warnings, the 1,653 tests, the benchmark against `main`'s, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Building Classes at Run Time, Continued

The follow-ups of "Building Classes at Run Time" (specification, section 3.7, "Classes of values"). One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-14.

**Q1, replacing a declared class's static method (A6): (a) by assignment, `Point.origin = f;`, which then replaces a static method as it declares a new static member, assigning one being WL3001 no longer; (b) by a method of every class, `Point.setStaticMethod("origin", f)`, assignment staying WL3001.**

A1: (a)

**Q2, replacing a property or a constant (A7, A8: "a setter"): (a) its `add…` method again, `addProperty`, `addStaticProperty` or `addConstant` given a name the class has replacing that member, as `addMethod` replaces a method (A3); (b) `set…` methods of their own, `setProperty`, `setStaticProperty` and `setConstant`, an `add…` given a name the class has being WL3001.**

A2: So, instead of add____ and set____, have just set____ with the semantics that it adds the first time, and replaces subsequently.

**Q3, a program's own members named `className`, `instanceOf` or `parent` (a tree's `this.parent = node;` is common): (a) the program's own come first, a static member `className` or `parent` of a class, or a field or method `instanceOf` of an instance, hiding the built-in one there; (b) the built-in ones are reserved: declaring a static member `className` or `parent` is WL2002, and assigning a field `instanceOf` WL3001.**

A3: Ugly, but effective: use `__className`, `__instanceOf`, and `__parent`.

Done (Claude, 2026-11-14, `93f214e`): recorded as approved in the specification, section 3.7 ("Classes of values"), and in the parts of section 9 they change ("Extending the classes of values", parts 5.2 and 5.3). Whether `addMethod`, approved before A2, becomes `setMethod` too is asked in "Building Classes at Run Time: `addMethod`'s Name". Checks: the Release build, without warnings, and the full test suite, on Windows only; Linux not tried.

## [DONE] Extending the Classes of Values: `Class`, Names

`Point.className`, `p.instanceOf` and `Point.parent` (specification, section 3.7, and part 5.2 of "Extending the classes of values", section 9), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for Q3 of "Building Classes at Run Time, Continued", and for "Extending the Classes of Values: `Class`, Making Classes".

2026-11-14: both done; the names are `__className`, `__instanceOf` and `__parent` (A3 of "Building Classes at Run Time, Continued").

Done (Claude, 2026-11-14, `8651b8e`): on both VMs, with conformance tests; the choices A3 leaves open are recorded as Proposed in the specification, section 15.2 ("Classes made while the program runs"): the three names come before a program's own members of those names, `__className` and `__parent` are a class's only, `null.__instanceOf` is `null`, assigning `null` to `__className` unnames a class, and `__instanceOf` and `__parent` cannot be assigned. Checks: the Release build, without warnings, the 1,669 tests, a timing of member reads and assignments against `main`'s, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Extending the Classes of Values: `Class`, Properties and Constants

`addProperty`, `addStaticProperty`, `addConstant` and `static const`, and replacing a class's static methods, properties and constants while the program runs (specification, section 3.7, and part 5.3 of "Extending the classes of values", section 9), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for Q1 and Q2 of "Building Classes at Run Time, Continued", and for "Extending the Classes of Values: `Class`, Making Classes".

2026-11-14: both done; the methods are `setProperty`, `setStaticProperty` and `setConstant`, each adding its member the first time and replacing it after, and assignment replaces a static method (A1 and A2 of "Building Classes at Run Time, Continued").

Done (Claude, 2026-11-14, `c6ba136`): on both VMs, with conformance tests; its choices are recorded as Proposed in the specification, section 15.2 ("Classes made while the program runs", and "Static members", which it changes: `static const` declares a constant now, and assigning a static method replaces it), among them that `null` for both of a property's functions leaves it with neither, and that a constant cannot be assigned through a subclass either. A parser test that took `static const` for malformed takes `static let` now, and a conformance test that took assigning a static method for WL3001 is gone, the new ones testing that it replaces the method. Checks: the Release build, without warnings, the 1,697 tests, a timing of member reads and assignments against `main`'s, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Building Classes at Run Time: `addMethod`'s Name

A follow-up to A2 of "Building Classes at Run Time, Continued", which names `setProperty`, `setStaticProperty` and `setConstant`, each adding its member the first time and replacing it after, as `addMethod` (A3 of "Building Classes at Run Time", implemented in `d009021`) already does for methods. Added by Claude, 2026-11-14.

**Q1, `addMethod`: (a) renamed `setMethod`, so that every member a program gives a class while it runs has a `set…` method; (b) kept `addMethod`.**

A1: (a)

ASIDE1: `Point.__instanceOf` == `Class`, right? What about `Class.__instanceOf`? `null`? `Class`? `Object`?

ASIDE2: `var f = function () {};`. Then, `f.__instanceOf` is?

Done (Claude, 2026-11-14, `f081a02`): `addMethod` renamed `setMethod` in the runtime, the tests, the specification (section 3.7, recorded as approved, and section 15.2) and the README, with nothing kept for the old name; `DONE.md` keeps the name its tasks used. The asides, as built: `Point.__instanceOf` is `Class`, and so is `Class.__instanceOf`, a class being a value of the class `Class`, `Class` included (as Python's `type(type)` is `type`); `f.__instanceOf` is `Function`; and once metaclasses land, a class that `new Entity("User")` made gives `Entity`. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Extending the Classes of Values: `Class`, Metaclasses

`class Entity extends Class { … }`, whose instances are classes (specification, section 3.7, and part 5.4 of "Extending the classes of values", section 9), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "Extending the Classes of Values: `Class`, Names", whose `className` a metaclass's methods read.

Done (Claude, 2026-11-14, `123f46f`): on both VMs, with conformance tests; its choices are recorded as Proposed in the specification, section 15.2 ("Classes made while the program runs", and the rows that called `Class` sealed): `Class` is written in WebLingo, its constructor naming and basing the class a metaclass makes by the built-in classes' `hold`, so that the bytecode is unchanged; a class a metaclass made has its metaclass's methods after its own static members, and before the built-in `setMethod` and the like, which a metaclass may therefore replace. A conformance test that took `extends Class` for WL3001 is gone. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Extending the Classes of Values: `Class`

`new Class()` and `extends Class` (specification, sections 3.7 and 9), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the design of "Building Classes at Run Time".

2026-11-14: designed (specification, section 3.7, "Classes of values"); split into the parts the specification defines in section 9 (part 5 of "Extending the classes of values"), each a task below. Waits for them.

Done (Claude, 2026-11-14): its four parts have landed, on both VMs: making classes (`d009021`), names (`8651b8e`), properties and constants (`c6ba136`), and metaclasses (`123f46f`); `addMethod` became `setMethod` (`f081a02`). Checks: those of each part, on Windows only; Linux not tried.

## [DONE] Extending the Classes of Values

`extend` blocks, and classes extending `Boolean`, `Integer`, `Number`, `String`, `Function` and `Class`, with `new` on those classes ("Classes of Values, Further"; specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "Classes of Values, Extensions".

2026-11-13: answered; `extend`, classes extending `Boolean`, `Integer`, `Number`, `String` and `Function`, and their text (`Celsius(21.5)`, `Stack([1, 2])`) can be built; the class of an arithmetic result, and `Class`, wait for "Classes of Values, Extensions, Continued".

2026-11-13: split into the parts the specification defines in section 9 ("Extending the classes of values"), each a task below. Waits for them.

Done (Claude, 2026-11-14): every part section 9 defines has landed, on both VMs, the last of them `Class` (part 5, in four parts of its own, the last `123f46f`). Checks: those of each part, on Windows only; Linux not tried.

## [DONE] Task Combinators

`Task.all`, `Task.any` and timeouts (A6 of "Asynchrony: `async` and `await`"; specification, section 3.8). Added by Claude, 2026-11-13.

2026-11-13: waits for "M0.11: Asynchrony".

2026-11-14: M0.11 is done; what they do needs the owner's decisions first. One decision each, answerable with "yes" or a letter.

**Q1, `Task.all(values)`, an array of values, tasks or not, gives a task that completes with an array of their results, in their order, once all have; when one faults: (a) it faults at once with that exception, as JavaScript's `Promise.all` does, the others going on, observed; (b) once all are done, with the first exception in their order.**

A1: (a)

**Q2, `Task.any(values)`: (a) the first to complete, with its result, or faulting with its exception (JavaScript's `Promise.race`); (b) the first to complete with a result, faulting only if all fault, with an error holding their exceptions (JavaScript's `Promise.any`).**

A2: (a). Is `Task` and `Promise` essentially the same thing?

**Q3, a timeout: (a) `Task.timeout(task, milliseconds)`, a task that gives the task's result, or faults with a `TimeoutError` (a new class extending `Error`, with a code of its own) if it takes longer, the task itself going on; (b) the same as a method of tasks, `t.timeout(milliseconds)`.**

A3: (a)

**Q4, where they live: (a) static methods of `Task`, as section 3.8 names them; (b) a standard module, `std:tasks`, once modules exist.**

A4: (a) in module `std:tasks`, once it exists, so both (a) and (b) I guess.

Done (Claude, 2026-11-14, `98bafad`): recorded as approved in the specification, section 3.8 ("Tasks"), with the new code WL3010 (section 8.4); `Task.all`, `Task.any` and `Task.timeout` on both VMs, with conformance tests, their choices recorded as Proposed in section 15.2 ("Asynchrony"), among them that `Task.any([])` is WL3001, and that a timeout's error is thrown from the `await` it reaches. `std:tasks` is "Standard Module `std:tasks`", below. A2's question: yes, a task is WebLingo's promise, much as a JavaScript `Promise` or a C# `Task` is: a value standing for a result that comes later. Unlike a `Promise`, a task is used only through `await`, with no `then`; its exception, if nothing awaits it, is reported when the execution ends, unless `t.forget()`; and every task of an execution runs on its one worker, switching only at `await`. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Modules, Quoted Parameters

A6 of "Modules, Continued" asked whether a shell removes the quotes of `value="10"`. Checked on 2026-11-14, with a program printing the arguments it gets: bash gives it `value=10`, and only `'value="10"'` keeps the quotes; Windows PowerShell 5.1 gives `value=10` for both `value="10"` and `'value="10"'`; and cmd.exe gives `value=10`. So A8's quotes rarely reach `weblingo`, and never from Windows PowerShell 5.1. Added by Claude, 2026-11-14.

**Q1, a parameter given as a string when it looks like another literal: (a) quotes, as A8 has it, which a shell must be asked to keep (`'value="10"'` in bash), and which Windows PowerShell 5.1 cannot pass; (b) a flag before it, `weblingo run script.wl --string value=10`; (c) every parameter a string, which the script converts itself (`new Integer(value)`).**

A1: (c)

Done (Claude, 2026-11-14, `b260d5f`): recorded as approved in the specification, section 3.9 ("A script's parameters"), in place of reading a value as a literal. A script converting one needs a conversion `new Integer(…)` does not make yet (a string given to it is WL3001): asked in "Converting Strings to Values". Checks: the Release build, without warnings, and the full test suite, on Windows only; Linux not tried.

## [DONE] Modules: Parameters

`param`, the values an importer gives, and a missing one's error (specification, sections 3.9 and 9), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "Modules: Static Imports and Exports".

2026-11-14: that landed; what a module with parameters does when imported twice needs the owner's decision first, as does how an import's values are checked. One decision each, answerable with a letter.

**Q1, a module imported again with other values, `import a from "box" { size: 1 };` here and `{ size: 2 }` there: (a) it runs once, with the values of the import that ran it first, a later import's values unused, as "once per execution" has it; (b) it runs once for each set of values, each import with other values getting an instance of its own, as a template's component is rendered once per call; (c) an error, a module with parameters being imported once.**

A1: (b). It looks to me like a sneaky way of introducing generics (the same module instantiated with different parameters).

**Q2, an import's values: (a) a map literal only, `{ size: 10 }`, whose names the compiler checks against the module's parameters, a missing or an extra one an error at compile time (a new code, WL2016); (b) any expression giving a map, checked when the import runs.**

A2: (a) for static imports, (b) for dynamic.

Done (Claude, 2026-11-14, `02ae50c`): recorded as approved in the specification, section 3.9 ("Parameters"), with the new code WL2016 (section 8.4); on both VMs, with tests in `ModuleTests` (`WebLingo.VM.Tests`); its choices recorded as Proposed in section 15.2 ("Modules"), among them that a value left out counts as different from one given, even the default, and that the program's own parameters are their defaults until "Modules: A Script's Parameters" gives them values. The dynamic import's runtime check is built, for "Modules: Dynamic Imports" to use. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Converting Strings to Values

A follow-up to A1 of "Modules, Quoted Parameters": a script's parameters are strings, which a script converts itself; `new Integer("10")` is WL3001 now, `new` on a class of values taking only a value of its kind (specification, section 15.2, "Classes holding a value"). Added by Claude, 2026-11-14.

**Q1, converting a string: (a) `new Integer("10")`, `new Number("1.5")` and `new Boolean("true")` read a string as a literal of their kind, WL3001 for one that is not; (b) static methods of their own, `Integer.parse("10")`, `Number.parse("1.5")` and `Boolean.parse("true")`, `new` taking a value of its kind alone, as now.**

A1: It could be both. `Integer.parse("10")` is good. But also `Integer("10")` could effectively mean:

```
class Integer {
    var __value = 0;
    
    constructor (stringValue) {
        __value = Integer.parse(stringValue);
    }
}
```

Done (Claude, 2026-11-14, `dee6093`): both, recorded as approved in the specification, section 3.7 ("Classes of values"): `Integer.parse("10")`, `Number.parse("1.5")` and `Boolean.parse("true")`, and `new Integer("10")` and the like reading a string the same way, as A1's constructor does, on both VMs, with conformance tests; their choices recorded as Proposed in section 15.2 ("Classes holding a value"), among them that a number may have a sign, and nothing around it, not even a space. A1 writes `Integer("10")`, which is read as `new Integer("10")`: a class called without `new` stays WL2005 (section 3.7, "Instances"). A1's code block had taken the place of the line "## [IDLE] Modules", the heading of the task after it, which is restored. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Modules: Dynamic Imports

`await Module.import(path)`, a module compiled when it runs (specification, sections 3.9 and 9), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "Modules: Parameters".

2026-11-14: a module compiled while the program runs must join the running program: its functions, constants and classes added to the program's, its built-in classes the program's own (so that `x is Object` holds across them), and a file it imports that has run already not run again. That is a large change to both VMs and to the bytecode, which templates' `renderComponent(…)` needs too (section 13: a component is found and compiled when it runs). A decision of sequence, answerable with a letter:

**Q1, when to build the VMs' linking of a module compiled while the program runs: (a) now, as this part, before the standard modules and properties; (b) with M0.12's components, which need it too, dynamic imports landing with them.**

A1: (a)

Done (Claude, 2026-11-14, `e27005d`): on both VMs, with tests in `ModuleTests` (`WebLingo.VM.Tests`) and through the CLI; its choices recorded as Proposed in the specification, section 15.2 ("Modules"), among them that a module that does not compile is a runtime error of its first diagnostic's code, and that a member's name after a point may be a keyword, which `Module.import` needs (a parser test that took `a.if;` for malformed takes `a.5;` now). The compiler compiles the file as an import, a program of its own, and each VM joins it to the running program by appending its tables and moving its instructions' indices; M0.12's components can join the same way. Checks: the Release build, without warnings, the full test suite, the benchmark against `main`'s, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Modules: A Script's Parameters

`weblingo run script.wl name=value` (specification, sections 3.9 and 9). Added by Claude, 2026-11-14.

2026-11-14: every parameter a string (A1 of "Modules, Quoted Parameters"); a script converting one waits for "Converting Strings to Values", which this part does not need.

2026-11-14: waits for "Modules, Quoted Parameters", and for "Modules: Standard Modules".

Done (Claude, 2026-11-14, `3599f1d`): on both VMs, with tests through the CLI and in `ModuleTests`; its choices recorded as Proposed in the specification, section 15.2 ("Modules"), in place of the program's parameters being their defaults: a value for no parameter, or a parameter without a default left out, is WL2016 as the program starts; an argument that is not `name=value`, or a name given twice, is a usage error (exit 2). The CLI's usage line says so: `run <file.wl> [name=value …]`. A `ModuleTests` case that took a program's parameter without a default for a compile error takes it for a runtime error now. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Modules

The module system: `import`, static and dynamic, `export`, `param`, a script's parameters, and the standard library and the built-ins as modules (specification, section 3.9), on both VMs, before M0.12's templates, which build on it. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "Modules, Continued", and for "Extending the Classes of Values", above it.

2026-11-14: answered, but for how a parameter is given as a string ("Modules, Quoted Parameters"), which only a script's parameters wait for.

2026-11-14: split into the parts the specification defines in section 9 ("Modules"), each a task below. Waits for them.

Done (Claude, 2026-11-14): every part section 9 defines has landed, on both VMs, the last a script's parameters (`3599f1d`). Checks: those of each part, on Windows only; Linux not tried.

## [DONE] Static Members Through Instances, Continued

A follow-up to "Static Members Callable on Instances". Added by Claude, 2026-11-14.

**Q1, `p.count = 5`, where `count` is a static field of `p`'s class: (a) it assigns a field of the instance, which then shadows the static, as built; (b) it assigns the static field, as Java does.**

A1: (b) if the static field already exists.

Done (Claude, 2026-11-14, `3c20e93`): recorded as approved in the specification, section 3.7 ("Static members"), on both VMs, with conformance tests; its choices recorded in section 15.2 ("Static members"): an instance's own field of that name, made before, still comes first, as reading one does, and a static method is not replaced through an instance. Checks: the Release build, without warnings, the full test suite, a timing of field assignments against `main`'s, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Adding Methods at Run Time

`extend` (see "Classes of Values, Extensions") adds methods to a class as a declaration, written once in the source, its methods having `this` as a class's do. This task would add a way to do the same while the program runs, for a method, or a class to give it to, that is known only then: chosen by a condition, built by a factory, or named by data. Discussed with the owner, 2026-11-13; added by Claude the same day.

Again, ensure that the implementation is the same for declared classes as well as those instantiated at runtime.

What it needs:

- **A function with a receiver.** A method is a function whose first, hidden parameter is `this` (specification, section 15.2, "Classes"), and a plain function expression has none (`this` in one is WL2009, unless a method around it lends its own). One way: a function expression may declare `this` as its first parameter, `function (this, prefix) { return prefix + this.x; }`, the parameter bound by a call's target (`p.describe("x = ")`), or given as an argument when the function is called directly (`describe(p, "x = ")`).
- **A way to attach it.** A built-in method of classes, `Point.addMethod("describe", describe)`, after which every instance of `Point`, and of its subclasses, has `describe`, with `this` bound, replacing a method of that name as an extension's does (the owner chose replacing for `extend`, 2026-11-13).

Its cost: a class's members could change at any point of a run, so the same call could mean different things at different times, and the VMs could no longer gather a class's methods once, when it is made.

It is deferred: it can be added later, if it proves necessary, since `extend` already covers methods known when the program is written, and nothing in this design would change what programs without it do. When it is taken up, these are the owner's to decide:

**Q1: a function with a receiver, `function (this, a) { … }`, its first parameter named `this`, bound by a call's target, or given as the first argument of a direct call. Agreed?**

A1:  Yes.

**Q2: attaching it: (a) a built-in method of every class, `Point.addMethod("describe", f)`; (b) a statement, `extend Point with describe = f;`.**

A2: (a)

**Q3: may a method attached at run time be removed again (`Point.removeMethod("describe")`)? (a) no; (b) yes.**

A3: (b)

Done (Claude, 2026-11-14, `13a0a52`): A1 and A2 were built with "Extending the Classes of Values: `Class`, Making Classes" (`d009021`), `addMethod` since named `setMethod`; A3, `Point.removeMethod(name)`, on both VMs, with conformance tests, recorded as approved in the specification, section 3.7, its choices in section 15.2 ("Classes made while the program runs"): it gives whether the class had the method, and removes a declared method as one given while the program runs, both being a class's own methods alike. That declared classes and classes made while the program runs share one implementation is "Internals on Par with Script", below. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Native Code Extensions

Make it possible to install and bind native (C# on CLR, later Native C#) code as (external) modules extending the library available to scripts (like Python does for C).

The design needs the owner's decisions first, and the module system ("Modules"; specification, section 3.9), which such modules extend. One decision each, answerable with "yes" or a letter. Questions added by Claude, 2026-11-14.

**Q1, what a native module is, on .NET: (a) an assembly (a DLL) with a class implementing an interface of WebLingo's (`IWebLingoNativeModule`), whose functions and classes become the module's exports; (b) C# source, which the host compiles when it installs it.**

A1: (a) but let's discuss this in detail.

**Q2, importing one: (a) a scheme of its own, `import db from "native:sqlite";`, as `std:` names the standard modules; (b) the standard modules' scheme, `std:sqlite`, so that a standard module and an installed one look alike; (c) a path to the assembly.**

A2: (c), and (a) when the compiler is configured to understand the scheme as pointing to a particular folder, so `native:sqlite` resolves to `sqlite` module in the folder user-configured as `native`. Otherwise, explicit path to the asembly (relative or absolute) should work.

**Q3, where they are found: (a) installed into a folder the host is configured with (`weblingo.json`, or the CLI's own), and loaded when first imported; (b) beside the script that imports them.**

A3: As in A2, multiple folders can be configured, each as a separate scheme. Native  executable modules and scripts (text) should not in general be mixed.

**Q4, trust: native code runs with the host's own rights, past WebLingo's capabilities (section 7.13): (a) only a module an application's capability policy names may be loaded, never one a script asks for alone; (b) any installed module, by any script.**

A4: (b). Installed modules are like built-ins and trusted. What should be prevented, is script downloading a native module which is then executed.

**Q5, its contract: a native function takes and gives WebLingo's values (`WebLingoValue`) through the runtime's API, may make classes and instances, and may give a task for asynchronous work, on WebLingo's operation model (WS-P007), never on .NET's `Task` in the core; and under Native C#, the same module's source is compiled by that toolchain, the contract unchanged (WL-017). Agreed?**

A5: Yes. In fact, standard modules should follow the same model: they are pre-installed external native extensions that can - for example - be swapped by an alternative implementation. Everything should follow the same packaging model, without priviledged code. Runtime treats every native library/moodule the same.

**Q6, C libraries, as Python's `ctypes` reaches them: (a) not directly: a native module reaches them, through P/Invoke, adapter-only (section 7.14); (b) a foreign-function interface for scripts too, later.**

A6: (b), API TBD.

Done (Claude, 2026-11-14, `97ebbdd`): recorded as approved in the specification, section 3.9 ("Native modules"), A1's details and A2's paths against A4's concern being asked in "Native Code Extensions, Continued", as A1 asks to discuss them. Checks: the Release build, without warnings, and the full test suite, on Windows only; Linux not tried.

## [DONE] Constants Inside Classes

This should be allowed:

```
class MathStuff {
    const PI = 3.14;
}

var m = new MathStuff();
var v = m.PI;
```

Done (Claude, 2026-11-14, `a3722a9`): `const PI = 3.14;` in a class, or an `extend` block, declares a constant of the class itself, as C#'s `const` does and as `static const` does, so that `m.PI` reads it through the instance, as any static member is read, and `MathStuff.PI` through the class; recorded in the specification, section 3.7, as the owner's request, its being static recorded as Proposed in section 15.2 ("Classes made while the program runs"). On both VMs, with conformance tests. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Internals on Par with Script

Make sure that there is only one implementation of - for example - how to make a class, so the way to make a class at run time via code (`var Point = new Class("Point");` ) and the way the runtime makes classes from 

```
class Point {
}
```

is *literally* the same implementation in C# (not just identical code in two places - the very same code should be executed in both cases).

Done (Claude, 2026-11-14, `30cb3a5`): a class's declaration, and an `extend` block, now make and fill a class by the very methods of `WebLingoHeap` that a program's own calls run: `TryMakeClass` (`new Class(name, base)`, a metaclass's `new`, a declaration), `GiveConstructor` and `TrySetMethod` (`setMethod`, `setProperty`'s accessors, a declaration's constructor, methods and properties), `TrySetStaticMethod` (`setStaticProperty`'s accessors, a declaration's static methods), `TryDeclareStatic` (assigning a class a static member it does not have, a declaration's static fields), and `TrySetConstant` (`setConstant`, `static const`); each VM's `TryGiveMembers` only makes the closures. Recorded in the specification, section 3.7, as the owner's request, its details in section 15.2 ("Classes made while the program runs"), and in `Documents/implementation.md`, section 6. Two things changed that a program can see: a metaclass's base that is not a class, or is sealed, is reported in a declared class's words (WL3001 as before); and a static method an extension gives in place of a constant no longer leaves the constant's mark, which made the field assigned in its place WL3001 on its second assignment. A change to a class's methods now gathers anew only that class's, unless another extends it, and sets the flag that gives plain values their class's methods only for `Object` and the kinds of values' classes. On both VMs, with conformance tests; the VMs' `Run` is unchanged, so no timing against `main`'s. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Arrow Notation

```
function (this) { return this.x + this.y; }
```

should be equivalent to

```
(this) => { return this.x + this.y; }
```

Done (Claude, 2026-11-14, `07e94bb`): `(parameters) => { … }` is a function expression, parsed into the very syntax `function (parameters) { … }` is, so that the two are the same from the binder on, on both VMs: its parameters are a function's, defaults and a receiver, `(this) => { … }`, included, and inside a method it sees the method's `this`, as any function expression does. Recorded in the specification, section 3.5, as the owner's request, its details as Proposed in section 15.2 ("Functions and calls"). What JavaScript's arrows have besides, an expression for a body, a parameter without parentheses, and `async`, is asked in "Arrow Notation, Continued", below; until then `=>` takes a block. With lexer and conformance tests. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] M0.12.1: Pages

`.wlp` pages: their text, `<% … %>`, `<%= … %>`, `<%- … %>` and `<%# … %>`, statements across blocks, escaping, `null` and line breaks, compiled through the WebLingo compiler into a program that writes the page, in `WebLingo.Templates`, with `WebLingo.Templates.Tests`, and `welp run page.wlp` in `WebLingo.Pages.Cli` (specification, section 13, "Contracts", and section 9, M0.12.1), on both VMs. Added by Claude, 2026-11-14.

Its syntax needs one decision first, which section 13 leaves open; answerable with a letter.

Q1, `%>` inside a block's code, in a string or a comment (`<% var end = "%>"; %>`): (a) it ends the block wherever it stands, as in classic ASP, a string writing it as `"%" + ">"`; (b) the template compiler reads a block's code as WebLingo's tokens, so that `%>` in a string or a comment does not end it.

A1: (b)

Done (Claude, 2026-11-14, `224b1f9`): A1 recorded as approved in the specification, section 13 ("Contracts", "A block's end"), and its decision register. A page is read by `WebLingoTemplateParser` into the WebLingo parser's tokens, and compiled by `WebLingoTemplateCompiler`, for either VM; `welp run`, `welp check` and `welp --version`, in `WebLingo.Pages.Cli`, share `weblingo`'s command line; `WebLingo.Templates.Tests` runs each page on both VMs, and `welp` end to end. Guessed, and recorded as Proposed in section 15.2 ("Pages"): a `<%#` block, which holds no code, ends at its first `%>`; a line of several statement or comment blocks, and spaces, writes nothing too; a block without its `%>` is WL5001, the first of the templates' range, and `<%= %>` without an expression WL1001; a page takes parameters (`welp run page.wlp name=value`) and imports modules, as a script does. A consequence of A1 worth knowing: `<% // a note %>` comments out its own `%>`, so the page's text after it is read as code, and reported as such. Checks: the Release build, without warnings, the full test suite, and both command lines on the samples and on a page, on Windows only; Linux not tried.

## [DONE] M0.12.2: Includes

`<% include "/includes/header.wlp"; %>`, textual, resolved and compiled with the page, a cycle of them an error of the templates' range, WL5001 on (specification, section 13, "Contracts", and section 9, M0.12.2), on both VMs. Added by Claude, 2026-11-14.

Section 13 has a path starting with `/` be "from the application's root", which a host gives; `welp` needs a decision of its own, answerable with a letter.

Q1, the application's root under `welp`: (a) the folder of the page it is given; (b) the current folder; (c) an option, `welp run --root site page.wlp`, the page's folder when it is left out.

A1: (c), with site root under web server.

Done (Claude, 2026-11-14, `8b9b951`): A1 recorded as approved in the specification, section 13 ("Contracts", "Includes"), and its decision register. `include "path";` in a block's code is replaced by the template it names, as if its text stood there, on both VMs; `welp run --root site page.wlp` gives the application's root, the page's folder when it is left out. Each file's positions are its own among the program's, so that an error in an included template, at compile time or at run time, names that template, at its own line and column. Guessed, and recorded as Proposed in section 15.2 ("Includes"): `include` is not a keyword, only the name followed by a string literal, in a statement block, being an include; `.wlp` is added to a path without an extension, as `.wl` is to an import's; a template that cannot be found or read, or is outside the root, is WL5002, a cycle WL5003, and includes nested more than 64 deep, or reading more than 16M characters in all, WL5004; the imports of an included template's code are found from the page's folder, its text standing in the page. Checks: the Release build, without warnings, the full test suite, and both command lines, on Windows only; Linux not tried.

## [DONE] M0.12.3: Components

`<% param product; %>` in a component, `renderComponent(path, values)`, a task, the component found and compiled when it runs, as `Module.import(…)` is, and a missing one's error (specification, section 13, "Contracts", and section 9, M0.12.3), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "M0.12.1: Pages".

Done (Claude, 2026-11-14, `fbeeacf`): `renderComponent(path, values)` renders a component, on both VMs: found by the templates' resolver when it is first rendered, compiled as an import by the template compiler, kept, and run anew at each render, the task of running it its call's result; its `param` statements are its parameters, which each render gives. Guessed, and recorded as Proposed in the specification, section 15.2 ("Components"): a component's path is found as an include's is (`/` from the root, any other from the rendering file's folder, `.wlp` added); the task completes with the component's instance; a component that cannot be found or read is WL5005, and its parameters wrong WL5006, runtime errors a catch may catch, while one that does not compile is its first diagnostic's code and message, as a dynamic import's is. The compiler knows `renderComponent` as a built-in the template compiler declares so (`RendersComponent`), a dynamic import that runs its file anew (`ImportDynamic`'s flag 2), so that the language itself knows nothing of templates; a VM finds components with a loader of its own. The register VM's `Run` changed by an operand's reading only: timed against `main`'s, alternately, twice, with no difference beyond the noise. Checks: the Release build, without warnings, the full test suite, and both command lines, on Windows only; Linux not tried.

## [DONE] M0.12.4: Layouts

`<% layout "/layouts/main.wlp", { title: "Products" }; %>`, `renderBody()`, a layout's own layout, and a cycle of them (specification, section 13, "Contracts", and section 9, M0.12.4), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "M0.12.2: Includes", whose paths it shares.

Done (Claude, 2026-11-14, `a9d158c`): `<% layout "/layouts/main.wlp", { title: "Products" }; %>` runs the layout, given the values as its parameters, and the rest of the page, its body, where the layout calls `renderBody()`, streaming, on both VMs; a layout may have a layout of its own, a cycle of them being WL5003 at compile time. The page imports its layout as a module of its program, its scope its own, the body an `async` function the layout awaits; the template compiler reads a layout as one (a parse hook in the compiler's options, which reads the files a program imports). `samples/SimpleWebsite` now has a layout, an include and a component, and runs through `welp`. Guessed, and recorded as Proposed in the specification, section 15.2 ("Layouts"): `layout` is not a keyword; the statement stands after a page's parameters and imports, before anything else, at most once, never in an included template (WL5007 otherwise); its values are a map literal; a layout's parameters given wrong are WL5006 at compile time; `renderBody()` may be called any number of times; the page's body runs in a function of the page's scope, so `export` there is WL1001. Checks: the Release build, without warnings, the full test suite, and both command lines on the samples, on Windows only; Linux not tried.

## [DONE] M0.12.5: Template Output's Workload

A benchmark workload of template output (specification, sections 5.6 and 9, M0.12.5), the third of those "Both VMs, for now" waits for. Added by Claude, 2026-11-14.

2026-11-14: waits for "M0.12.1: Pages".

Done (Claude, 2026-11-14, `0a96ad0`): `templates`, a page (`benchmarks/WebLingo.Benchmarks/Workloads/templates.wlp`) that writes a table of 10,000 rows, each with text, an escaped string and integer, and a raw integer, compiled by the template compiler for either VM, its expected output built in C#, its line breaks read as LF so that it writes the same on every platform. Measured twice, alternately: the register VM 6.1 to 6.7 ms, the stack VM 6.4 to 6.5 ms, 280,010 and 400,015 instructions, from 40 instructions of code (135 bytes) and 58 (104 bytes); writing and escaping text takes most of the time, so the VMs are close here. Recorded in the specification, section 5.6 ("Template output"). I/O-bound work, the last workload "Both VMs, for now" waits for, needs the platform layer. Checks: the Release build, without warnings, the full test suite, the benchmark, and both command lines, on Windows only; Linux not tried.

## [DONE] M0.12: Templates

`.wlp` templates, with includes, layouts and parameterized components together (specification, section 13, WL-T001 to WL-T008), in `WebLingo.Templates`, with `WebLingo.Templates.Tests`, on both VMs, and a benchmark workload for template output (section 5.6). Their open contracts (the include and component syntax, context-aware escaping) will be proposed as questions. Added by Claude, 2026-11-13.

NOTE: WebLingo must be designed in a way that lends itself to using it outside of the templated web environment, as a general purpose, embeddable scripting language. Nothing in the language itself should depend on having templates. Propose a name for the technology that uses WebLingo and templates in a web server (classic-ASP-like). Maybe WebLingoPages?

To propose with it, from the chat (2026-11-13): `<%= value %>` writes `null` as nothing, so that an optional read (`m?[k]`, `x?.name`) that finds nothing shows as blank, while `write` and `writeLn` go on writing `null`.

2026-11-13: waits for M0.10.

2026-11-13: M0.10 is done. The questions below settle what sections 2.2 and 13 leave open, one decision each, answerable with "yes" or a letter. Once they are answered, M0.11 will be split into parts in the specification, as M0.10 was, with includes, layouts and components still released together (WL-T001).

Q1, how a `.wlp` runs: it compiles, through the same compiler and VM as a `.wl` (WL-T001), into a program that writes the page: its text as it stands, `<% … %>` as statements, `<%= … %>` and `<%- … %>` as writes of their expression, and `<%# … %>` as nothing. Agreed?

A1: It compiles through a derivative compiler that incorporates the regular WebLingo compiler and extends it to support templating of web pages.

Q2, statements across blocks: a statement may span several `<% %>` blocks, the text between them in its body: `<% foreach (var p in products) { %><li><%= p.name %></li><% } %>`. Agreed?

A2: Yes, the text being rendered the same as if it were being printed, the same way classic ASP did it. This is useful for e.g. loops, where the text inside the loop may itself contain script expressions, conditionals, etc.

Q3, where a template writes: (a) where `write` writes, standard output under the CLI and a response's body under a host later; (b) to an output of its own, which a host gives, `write` going elsewhere.

A3: (a).

Q4, the CLI: `weblingo run page.wlp` writes the page to standard output, and `weblingo check page.wlp` compiles it, its includes, layout and components included. Agreed?

A4: Yes. Weblingo being strictly the language text compiler. We need a different executable for the combined WebLingo+templates compiler.

Q5, escaping: `<%= value %>` writes the text `write` would write, an instance's `toString()` included, with `&`, `<`, `>`, `"` and `'` as `&amp;`, `&lt;`, `&gt;`, `&quot;` and `&#39;`, the same in text and in attribute values; escaping for URLs, scripts and styles comes later, as its own decision. Agreed?

A5: Yes.

Q6, `null`: `<%= null %>` and `<%- null %>` write nothing, so that an optional read that finds nothing is blank, while `write(null)` goes on writing `null`. Agreed?

A6: Yes.

Q7, line breaks: (a) the text is written exactly as it stands, every line break included; (b) a line holding only a `<% … %>` block, and spaces, writes nothing, not even its line break, so that statements leave no blank lines.

A7: (b).

Q8, includes: `<% include "/includes/header.wlp"; %>`, a statement whose path is a string literal, resolved and compiled with the page, as section 13 illustrates. Agreed?

A8: Yes. 

Q9, include paths: (a) a path starting with `/` is from the application's root, any other from the including file's folder; (b) every path is from the application's root, as section 13 proposes.

A9: (a), with the WebLingo compiler-proper interpreting `/` as drive's root, `./` current folder, `xyz/abc` starting in current folder's `xyz` subfolder, etc.

Q10, what an include sees: (a) the variables where it is included, as if its text stood there; (b) none of them: it has a scope of its own, and a component's parameters are the way to give it data.

A10: (a). Also introduce `import` for the other case, so `include` is textual inclusion.

Q11, layouts: `<% layout "/layouts/main.wlp"; %>`, at most one, before anything the page writes; the layout runs, and the page's own output comes where the layout calls `renderBody()`, as section 13 illustrates. Agreed?

A11: At moset one for a given page, but  alayout can itself have a layout, all the way down. The compiler must prevent dependency cycles (compile-time error).

Q12, a layout's data, such as the page's title: (a) values given with it, `<% layout "/layouts/main.wlp", { title: "Products" }; %>`, which the layout reads as its parameters, so that the page's body can run, and stream, at `renderBody()` (WL-T003); (b) the page's top-level declarations, which the layout sees (`pageTitle` in section 13's example), the page's body then running before the layout writes anything, buffered.

A12: (a).

Q13, components: `renderComponent("/components/card.wlp", { product: p })`, where the compiler reads the path, a string literal, compiles the component with the page, and checks its parameters: (a) agreed; (b) a built-in instead, its path any string, the component found and compiled when it runs.

A13: (b), it's more flexible.

Q14, a component's parameters: (a) declared at its top, `<%@ params product, size %>`, each then a variable of the component, a missing one an error, an extra one an error; (b) no declaration: the component reads `params.product`, a missing one being `null`.

A14: (a), it's more descriptive, like declaring variables. So, also allow for default values, i.e. optional parameters.

Q15, asynchrony: WebLingo has no `await` yet, so a component renders when it is called, and section 13's `await renderComponent(…)` is written without `await` until asynchrony is designed. Agreed?

A15: Design async await first.

Q16, template errors (a cycle of includes or layouts, a missing file, a path outside the application, a component's parameters): (a) a new range of codes, WL5001 on, for templates; (b) codes in the existing ranges, WL1xxx for what is wrong in a template's text, WL2xxx for the rest.

A16: (a).

2026-11-13: recorded as approved in the specification, section 13 ("Contracts"), its decision register and section 9. What the answers leave open (the technology's and its program's names, `import`, default values, layouts' parameters, components found at run time) is asked in "M0.11 Syntax: Templates, Continued"; asynchrony, which A15 asks to come first, in "Asynchrony: `async` and `await`". Waits for those.

2026-11-14: those are done, and the module system it needs too; split into the parts the specification defines in section 9 ("M0.12 parts"), each a task below. Waits for them.

Done (Claude, 2026-11-14): its five parts landed, on both VMs: pages (`224b1f9`), includes (`8b9b951`), components (`fbeeacf`), layouts (`a9d158c`), and template output's benchmark workload (`0a96ad0`); `samples/SimpleWebsite` has a layout, an include and a component, and runs through `welp`. The technology is WebLingo Pages, its program `welp` (specification, section 13, "Contracts"). What WL-T007 and WL-T008 ask of a host (dependency invalidation, application generations, cancellation, and streaming to a response) waits for hosting.

## [DONE] Native Code Extensions, Continued

The details A1 of "Native Code Extensions" asks to discuss, and one point between A2 and A4 (specification, section 3.9, "Native modules"). One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-14.

Q1, what a native module gives: an assembly with one public class implementing `IWebLingoNativeModule`, whose `Register(IWebLingoNativeRegistry registry)` gives its exports, each a function with a name, an arity, and a delegate that takes the call's arguments, as `WebLingoValue`s, with the execution's heap, and gives a value, or a task for asynchronous work, as the host's built-ins (`IWebLingoBuiltinFunction`) do now. Agreed?

A1: OK

Q2, a native module's classes: (a) none at first, functions only, classes coming later; (b) classes from the start, each declared by the module with its methods as native functions (`registry.Class("Connection", …)`).

A2: (b). That's how built-in classes should be implemented.

Q3, loading one on .NET (adapter-only, section 7.14): (a) each in an `AssemblyLoadContext` of its own, so that two modules may depend on different versions of one library, and a reload can unload it with its application's generation (WL-H004); (b) into the host's own context, simpler, and never unloaded.

A3: (b), for now

Q4, a scheme's folder (A2, A3): (a) the module's assembly in it, `native/sqlite.dll`, its dependencies beside it; (b) a folder of its own for each module, `native/sqlite/sqlite.dll`, its dependencies inside it.

A4: (a)

Q5, A2's explicit paths and A4's concern, a script that writes an assembly and imports it: (a) a native module loads only from a configured scheme's folder, an explicit path too, which scripts cannot write to (section 7.13's capabilities); (b) explicit paths anywhere, the host's filesystem capability alone keeping scripts from writing assemblies where they can be loaded from.

A5: (a)

Q6, the standard modules as native ones (A5): (a) now: `std:system`'s built-ins moved into a native module installed with the runtime, in the `std` scheme's folder; (b) with the first standard module that is not the host's built-ins (`std:fs`, `std:tasks`).

A6: (a)

Done (Claude, 2026-11-14, `209a21e`): recorded as approved in the specification, section 3.9 ("Native modules"), and its decision register ("Modules"). Building them is "Native Modules", below, which needs three decisions of its own first; "Standard Module `std:tasks`" waits for it. Checks: the Release build, without warnings, and the full test suite, on Windows only; Linux not tried.

## [DONE] Arrow Notation, Continued

What JavaScript's arrow functions have beyond "Arrow Notation", which made `(a, b) => { … }` the same as `function (a, b) { … }` (specification, section 3.5). One decision each, answerable with a letter. Added by Claude, 2026-11-14.

Q1, an expression for a body, `(a, b) => a + b`, giving its value, as `(a, b) => { return a + b; }` does: (a) yes; (b) no, a block always.

A1: (a)

Q2, one parameter without parentheses, `x => { … }` (and `x => x * 2`, with Q1's (a)): (a) yes; (b) no, parentheses always.

A2: (a)

Q3, `async (x) => { … }`, the same as `async function (x) { … }`: (a) yes; (b) no.

A3: (a)

Done (Claude, 2026-11-14, `230b2d4`): `(a, b) => a + b` gives its expression's value, `x => x * 2` takes one parameter without parentheses, and `async (x) => { … }` and `async x => …` are `async` functions, on both VMs, each parsed into the syntax `function` expressions are. Recorded as approved in the specification, section 3.5 ("Functions"), its details as Proposed in section 15.2 ("Functions and calls"): a `{` after `=>` always begins a block, so a map is given in parentheses, `() => ({a: 1})`; `this => …` is not a receiver. A conformance case that expected `(a) => a + 1` to be an error now expects an error only where the expression is missing. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] Native Modules, Part 1: Native Functions

`IWebLingoNativeModule` and `IWebLingoNativeRegistry`, a module's functions; a host's schemes, from `weblingo.json` and `--scheme`; an assembly loaded from a scheme's folder only, into the host's context; a native module imported by its scheme, or by a path within a scheme's folder, statically and while the program runs (specification, sections 3.9 and 9, "Native modules", part 1), on both VMs. Added by Claude, 2026-11-14.

Done (Claude, 2026-11-14, `c29614f`): a native module's assembly, in a configured scheme's folder, registers its functions with `IWebLingoNativeRegistry`, each then one of the host's built-ins; a script imports the module by its scheme, `import t from "native:test";`, or by the assembly's path within a scheme's folder, statically or with `Module.import`, on both VMs; `weblingo` and `welp` take their schemes from `weblingo.json`, in the program's folder, and `--scheme name=folder`, which comes before the file's. Tested with a native module of the tests' own (`tests/WebLingo.Native.TestModule`), on both VMs and through `weblingo`. Guessed, and recorded as Proposed in the specification, section 15.2 ("Native modules"): a scheme's name is two or more letters and digits, so that a drive letter is never one; an import path ending in `.dll` names an assembly; an unconfigured scheme's path is a file's; a module that cannot be found, loaded or registered is WL2015; `weblingo.json` holds only `"schemes"`, anything else stopping the command (exit 4); a malformed `--scheme` is a usage error (exit 2). `std:` stays the compiler's own until part 3. Checks: the Release build, without warnings, the full test suite, and both command lines on the samples, on Windows only; Linux not tried.

## [DONE] Native Modules, Part 3: Standard Modules as Native Modules

`std:system`'s built-ins in a native module installed with the runtime, in `std/` beside its program, the `std` scheme configurable as any other (specification, sections 3.9 and 9, "Native modules", part 3), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "Native Modules, Part 1: Native Functions".

2026-11-14: part 1 is done; this can begin.

Done (Claude, 2026-11-14, `1556607`): `std:system` is a native module, `WebLingoSystemModule` in `WebLingo.StdLib`, installed as `std/system.dll` beside `weblingo` and `welp`; each loads it before it compiles, from the `std` scheme's folder, which `weblingo.json` or `--scheme std=folder` may point elsewhere, and gives its functions, by their own names, as the built-ins every module sees and `std:system` exports, so that another `system.dll` replaces them, as the tests show with the tests' own module. Guessed, and recorded as Proposed in the specification, section 15.2 ("Native modules"): a missing `std:system` stops the command (exit 4); a native module may register a function that writes text (`TextFunction`), given an instance's `toString()`; a native function reaches the execution's output through the heap (`WebLingoHeap.Output`, from `WebLingoExecutionContext.Output`). The tests and the benchmark still give the standard built-ins themselves, as a host may. Checks: the Release build, without warnings, the full test suite, and both command lines on the samples, on Windows only; Linux not tried.

## [DONE] Standard Module `std:tasks`

`Task.all`, `Task.any` and `Task.timeout` as the exports of a standard module, `std:tasks` (A4 of "Task Combinators"; specification, sections 3.8 and 3.9), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for the design of how standard modules are packaged, which A5 of "Native Code Extensions" makes native extensions, installed as other native modules are.

2026-11-14: the packaging is decided (A6 of "Native Code Extensions, Continued"): a standard module is a native module, in the `std` scheme's folder. Waits for "Native Modules, Part 3: Standard Modules as Native Modules".

2026-11-14: part 3 is done (`1556607`), `std:system` a native module; this can begin.

Done (Claude, 2026-11-14, `c908bc9`): `import { all, any, timeout } from "std:tasks";` gives `Task.all`, `Task.any` and `Task.timeout`, the same tasks and the same errors, on both VMs: `std:tasks` is a native module, `WebLingoTasksModule`, in a project of its own, `WebLingo.StdLib.Tasks` (an assembly holds one module), installed as `std/tasks.dll` beside `std/system.dll`. A standard module other than `std:system` is now a native module of the `std` scheme; one that is not installed is still "there is no such standard module" (WL2015). Recorded in the specification, sections 3.8 and 15.2 ("Native modules"), and in `Documents/implementation.md`, whose portable core is now twelve projects, with the architecture tests' rules for the new one. Checks: the Release build, without warnings, the full test suite, and both command lines on the samples and on a script importing `std:tasks`, on Windows only; Linux not tried.

## [DONE] Multi-line Strings

WebLingo's string literals end at their line (WL1010, specification, section 3.5, "Strings"), and the language needs strings that span lines too: SQL, HTML fragments, messages and test data. The owner decided so, 2026-11-14, in a chat with Claude, while weighing how a template's `<% … %>` block ends ("`%>` inside a block's code"): if the template compiler reads a block's code as WebLingo's tokens, an unclosed multi-line string, like an unclosed `/* */` comment, runs on over the blocks and the text after it, so its diagnostic should name both where it opened and the `<%` of the block it is in, and its closing delimiter should be one that ordinary text rarely holds. Added by Claude, 2026-11-14.

Q1, the delimiter: (a) three double quotes, `"""…"""`, as C# 11 and Python write them, with more quotes (`""""…""""`) for a string that holds three; (b) backticks, `` `…` ``, as JavaScript writes them.

A1: (a), with alternative three single quotes `'''`.

Q2, escapes in a multi-line string: (a) none, a raw string, `\` being a character like any other (good for regular expressions and Windows paths); (b) the same escapes as an ordinary string's.

A2: (a). 

Q3, indentation: the whitespace before the closing delimiter's column is removed from every line, as C# does, so that a string indented with the code around it does not keep that indentation; and the line breaks just after the opening delimiter and just before the closing one are not part of the string. Agreed?

A3: Yes.

Q4, line breaks inside a multi-line string are always LF (`\n`), whether the source file's lines end in CRLF, LF or CR, so that a program gives the same string on every platform. Agreed?

A4: Yes,

Q5, interpolation (`"Hello, {name}"`), a separate feature: (a) not now; (b) not now, but the delimiter chosen in Q1 leaves room for it (C# writes `$"""…{name}…"""`, JavaScript `` `…${name}…` ``); (c) designed together with multi-line strings.

A5: (c), but also needed for single line strings.

2026-11-14 (Claude): A5 has interpolation designed with multi-line strings, single-line ones included, so both wait for its decisions, below, one each, answerable with a letter. Q1 to Q4 stand as answered.

Q6, which strings interpolate: (a) those marked with `$` before them, as C# marks them, `$"Hello, {name}"` and `$"""…{name}…"""`, so that a string without it keeps its braces as they are; (b) every string, `{` in an ordinary string then beginning an interpolation, which changes what existing strings holding braces mean.

A6: (a)

Q7, what an interpolation holds: (a) any expression, `{a + b}`, `{user.name}`, `{f(x)}`; (b) a name, or a chain of members, `{user.name}`, only.

A7: (a)

Q8, an interpolated value's text: (a) the text `write` writes, an instance's `toString()` included, `null` as `null`; (b) the same, but `null` as nothing, as `<%= %>` writes it.

A8: (b)

Q9, a brace written as itself in an interpolated single-line string: (a) doubled, `{{` and `}}`, as C# writes it; (b) escaped, `\{` and `\}`.

A9: (c) Both.

Q10, an interpolated multi-line string, raw by A2, holding braces of its own, as JSON, CSS or code does: (a) as C# has it, as many `$` before the string as the braces that open an interpolation, `$$"""{ "name": {{name}} }"""`; (b) braces doubled, as Q9's (a) has them.

A10: (a)

Q11, a format after a colon, `{price:0.00}`, as C# has it: (a) not now, a `:` at an interpolation's top level being an error, so that it stays free for them; (b) not at all, formatting being a function's, `{format(price, "0.00")}`; (c) yes, format after  the colon supported.

A11: (c)

Done (Claude, 2026-11-14, `d776599`): `"""…"""` and `'''…'''` strings, raw, on one line or over several, without the closing delimiter's indentation, their line breaks LF; and interpolation, `$"Hello, {name}!"` and `$$"""{ "n": {{n}} }"""`, any expression, `null` as nothing, a brace doubled or escaped in a single-line one, on both VMs, and in pages' blocks, where a multi-line string never closed also names its block's `<%`. Recorded as approved in the specification, section 3.5 ("Strings"), and its decision register, the details as Proposed in section 15.2 ("Multi-line and interpolated strings"), with WL1011 and WL1012 (section 8.4). The parser reads an interpolated string into the syntax of `+` and a hidden class's `of(value)`, written in WebLingo, which gives `""` for `null` and else `value.toString()`, so that nothing after the parser is new. Guessed: `"""a"""` on one line is a raw string too, as C# has it; an interpolation in a multi-line string stays on one line; interpolations nest at most 32 deep. A11's formats need their language decided: "Interpolation Formats", below; until then a format is WL1001. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples and a script of such strings, on Windows only; Linux not tried.

## [DONE] Interpolation Formats

A format after an interpolation's colon, `{price:0.00}`, as A11 of "Multi-line Strings" has it (specification, section 3.5, "Strings"); until it is built, a format is WL1001. Its language needs a decision, and alignment, which C# writes beside it, one; each answerable with a letter. Added by Claude, 2026-11-14.

Q1, the format's language: (a) .NET's numeric format strings, standard and custom (`N2`, `0.00`, `X4`, `E3`), with the invariant culture, for integers and numbers, a format given any other value being WL3001; (b) a small language of WebLingo's own, which the specification lists, so that another toolchain (Native C#) need not copy .NET's; (c) (a) now, and the subset WebLingo keeps written into the specification as it is used.

A1: (c)

Q2, alignment, as C# writes it, `{name,10}` and `{name,-10}`, the value padded with spaces to that width, on the left or the right: (a) yes; (b) no, not now.

A2: (a)

Done (Claude, 2026-11-14, `a468fd8`): `{price:N2}`, `{name,10}` and `{name,-10}`, and both, `{price,10:N2}`, on both VMs and in pages' blocks. Recorded as approved in the specification, section 3.5 ("Strings"), and its decision register, with the formats in use so far, the subset A1 asks for: the standard `D`, `E`, `F`, `N` and `X`, and custom formats of `0`, `#`, `.`, `,` and `;`; and with .NET 10's rounding, which another toolchain must copy, a standard format rounding an exact half to even (`{0.125:F2}` is `0.12`) and a custom one away from zero (`{0.125:0.00}` is `0.13`). The details are Proposed in section 15.2 ("Interpolation formats and alignments"), and the formatting is in the portability inventory. The parser writes such an interpolation as a call of the hidden class's built-in method, `<text>.<format>(value, width, format)`, which the runtime runs (`WebLingoInterpolationFormat`), so neither VM's `Run` changed and no benchmark was needed. Guessed: an alignment is any expression giving an integer, not only a literal, as C# would need, at most 999,999 either way; it pads in Unicode scalar values, as `length` counts them; `null` with a format writes nothing, as C# writes it; a value of a class extending `Integer` or `Number` is formatted as the value it holds; a format is its text as it stands, without escapes, an empty one, or a standard one with a precision above 999, being WL1012. Checks: the Release build, without warnings, the full test suite (2,084 tests), and the CLI on the samples and a script of formats, and `welp` on `samples/SimpleWebsite`, on Windows only; Linux not tried.

## [DONE] Native Modules, Part 2: Native Classes

A native module's classes, `registry.Class(…)`, their constructors, methods and static members native functions, made by the heap's own methods (specification, sections 3.7, 3.9 and 9, "Native modules", part 2), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "Native Modules, Part 1: Native Functions".

2026-11-14: part 1 is done. A native class needs one decision that section 3.9 leaves open, answerable with a letter.

Q1, a native class's instance's own state, such as a database's connection, a .NET object no script may see: (a) an instance may hold one host object, opaque to scripts, which only its class's native methods reach (`heap.TryGetHostObject(instance, …)`), and which its `dispose()`, or the collector, releases; (b) only WebLingo's values, in its fields, the module keeping its host objects in a table of its own, an instance's field holding an integer that names one.

A1: (a). TryGetHostObject should check the instance’s class, not just the .NET type, so that a native method moved by extend fails cleanly.
Disposal should be idempotent and clear the slot, so later calls report “disposed” instead of reaching a freed object.

2026-11-14 (Claude): the plan, not yet begun. `registry.Class(name)` gives a builder of a constructor, methods and static methods, each a native function, a method's first argument its instance, and a handle through which the module sets, reads and releases an instance's host object, checking the instance's class against its own (A1's first point), a released one reported as disposed (its second). The compiler gets a native module's classes with its functions; a native unit's classes are made where the program begins, before the module's instance, their members built-ins in the class table; the VMs give a built-in method its instance (in `Run`'s call path, so timed against `main`'s), `new` keeping the instance as its result, and the disposal path calling a native `dispose`; the heap keeps an instance's host object, which the collector releases.

Done (Claude, 2026-11-14, `6841bfb`): `registry.Class(name)` gives a `WebLingoNativeClass` (`WebLingo.Runtime`), on which a module declares its constructor, methods and static methods, each a native function, a constructor's and a method's first argument its instance; on both VMs. Recorded as approved in the specification, section 3.9 ("Native modules"), and its decision register, A1 with both its points: the host object's checks look at whether the class's constructor made the instance, not only at the object's .NET type, so that a native method given another instance fails cleanly; and a release is idempotent, and leaves the instance disposed ("This Counter has been disposed.", WL3001). The details are Proposed in section 15.2 ("Native classes"), and `IDisposable` is in the portability inventory. Done otherwise than the plan above: the compiler makes a native class a class written in WebLingo, made with the built-in classes, whose members call the module's built-ins, so that neither VM's calls, `new` or disposal changed, and a native class is extended, given methods by `extend`, disposed by `using` and written as any class is; a call of a native method is thus two calls, which part 4's measurement will weigh. An error in a native member is reported where the member was called, by one line of each VM's failure path, measured against `main`'s, alternately, four rounds of every workload on both VMs, all within the machine's noise. Two first versions were slower, and changed before landing: three fields on every instance made `objects` 20 percent slower (a native instance's state is now in a table of the heap's); and a change to the uncaught error's return made the stack VM's `objects` and `closures` 5 to 15 percent slower (the lesson is in `Documents/implementation.md`, section 11). Guessed: an error that cannot be caught (a limit of the execution, a cancellation), raised in a native member, is reported at the program's start; a native `dispose` runs synchronously; a native class extends `Object` only; a host object still held is disposed, if it is `IDisposable`, by the collector, or when the execution ends, its failure ignored; a disposed instance's error is WL3001 until section 3.4's proposed `ResourceClosedError` has a code. Checks: the Release build, without warnings, the full test suite (2,104 tests), and the CLI on the samples, on Windows only; Linux not tried.

## [DONE] WASP — Web Application Server Pages

Change the name of the templating compiler for web pages to WSP (`.wasp` extension) —Web Application Server Pages, by analogy to Active Server Pages. 

NOTE: It would be nice to use WASP (Web _____ Server Pages) but we don't really want to use "Active" in this context, so propose something better. 

QUESTION: Do we support COM objects in scripts yet so that "Active" would be justified? 

2026-11-14 (Claude): deferred, as asked. The answers:

The question: no. WebLingo has no COM, and nothing in the specification plans it. A script reaches native code only through native modules (section 3.9), assemblies loaded from a configured scheme's folder, and later through a foreign-function interface to C libraries, approved but not designed yet. COM is Windows's, and the portable core may depend on no platform (section 11), so COM objects could only come from a native module written for Windows, as an adapter (section 7.14), which none is. The "Active" of Active Server Pages came from Microsoft's ActiveX and Active Scripting, both COM, so it would not be justified.

The note: proposals for WASP's A are in Q1, below. Two collisions to weigh first (a web search, 2026-11-14): `.wsp` is SharePoint's solution package (a CAB file, deployed with `Add-SPSolution`), found on the Windows servers where IIS, WebLingo's first target, runs; and Wasp is a full-stack web framework (wasp.sh), whose projects were defined in `main.wasp` files, now `main.wasp.ts`, so WASP above all would need the name check the specification asks for before a public launch (section 12.3, item 6). The rename would change the names in use, WebLingo Pages, `.wlp` and `welp` (sections 1 and 13), with `WebLingo.Pages.Cli` and the samples' pages; and `.wsp` was WebScript Pages' extension until the renaming of 14 November 2026, so that name would come back.

Q1, the word for WASP's A, "Web _ Server Pages": (a) Application, Web Application Server Pages: what pages are part of, an application with its modules, routes and generations (section 6), "application server" being the usual term; (b) Async, Web Async Server Pages: what sets them apart from classic ASP, a page that may await anywhere, its components tasks, its output streamed (sections 3.8 and 13); (c) Assembled, Web Assembled Server Pages: pages put together from includes, layouts and components (WL-T001); (d) none: WSP, as above.

A1: Web Application Server Pages

Done (Claude, 2026-11-15, `54753f4`): WebLingo Pages renamed WASP, Web Application Server Pages: pages are `.wasp` files, their command line `wasp`, built by `WebLingo.Wasp.Cli` (was `WebLingo.Pages.Cli`, `welp`), `wasp --version` printing `WASP M0.12 (pre-alpha)`; the samples' pages, the benchmark's page, the tests, the README and both documents follow, nothing kept for the old names. Recorded as approved in the specification, section 1, its decision register ("Project naming") and section 13. Guessed: the command line's name, `wasp`, and its project's, `WebLingo.Wasp.Cli`, following the technology's, and `WebLingo.Templates` keeping its name, the template compiler's library that section 13 approved. Worth weighing before a public launch (section 12.3, item 6): the Wasp web framework's command line is `wasp` too, so a machine with both would have two commands of that name. Checks: the Release build, without warnings, the full test suite (2,104 tests; six expected columns moved by one, `.wasp` being a character longer than `.wlp`), `wasp` on `samples/SimpleWebsite`, and the benchmark's page, on Windows only; Linux not tried.

## [DONE] I/O-Bound Workload

A benchmark workload of I/O-bound work (specification, section 5.6), the last of those "Both VMs, for now" waits for, after which the owner chooses between the VMs (WL-BC002), and M0.13 can begin. Scripts reach I/O through the platform layer (section 7: WL-P004's filesystem, synchronous and asynchronous, on WebLingo's own operation model, WL-P007) and a standard module, such as `std:fs` (section 3.9), a native module installed with the runtime. Added by Claude, 2026-11-14.

2026-11-14: waits for "Native Modules", which standard modules are; its own contracts (the standard module's functions, and the capabilities, section 7.13, a host gives a script) will be asked then.

2026-11-14 (Claude): native modules' functions and classes, which `std:fs` needs, are done; the platform layer has only a clock so far (`WebLingo.Platform.Abstractions`), its asynchronous operations being WebLingo's own (WL-P007, approved). `std:fs`'s contracts need five decisions, each answerable with a letter. The workload itself I would guess: a program that writes 1,000 small files and reads them back, one at a time, awaiting each, then all at once, with `Task.all`.

Q1, `std:fs`'s first functions: (a) whole files as text: `readText(path)`, `writeText(path, text)`, `appendText(path, text)`, `exists(path)` and `remove(path)`, none for folders yet; (b) (a), and folders: `list(path)`, `makeFolder(path)` and `removeFolder(path)`; (c) (b), and files opened as handles, a native class `File` (`File.open(path, mode)`, `read`, `write` and `dispose()`), as section 3.4's example has one.

A1: (c)

Q2, synchronous and asynchronous, which WL-P004 asks for both: (a) each function in both forms, `readText(path)` and `readTextAsync(path)`, a task, as .NET names them; (b) asynchronous by default, `await fs.readText(path)`, a synchronous one named `readTextSync(path)`, as Node.js names them; (c) asynchronous only, for now.

A2: (b)

Q3, text's encoding: (a) UTF-8 always, a byte order mark skipped when read and none written, malformed bytes an error; (b) UTF-8 by default, another encoding named by an optional argument.

A3: (b)

Q4, the files a script may reach under `weblingo` (capabilities, section 7.13): (a) any the process may, capabilities coming with hosting; (b) those below the script's folder, unless the command line grants more (`--allow-fs folder`); (c) none, unless the command line grants them, `--allow-read folder` and `--allow-write folder`, as Deno does.

A4: (a)

Q5, an I/O failure (a missing file, a path not granted): (a) a runtime error of a code of its own, WL3011, of a class `IOError` extending `Error`, which a catch may catch, its message naming the path as the script gave it, never the host's own paths; (b) WL3001, as another invalid operation is.

A5: (a)

Done (Claude, 2026-11-15, `ddf8cdf`): `std:fs`, a native module installed as `std/fs.dll` (`WebLingo.StdLib.Fs`), as A1 to A5 have it: `readText`, `writeText`, `appendText`, `exists`, `remove`, `list`, `makeFolder` and `removeFolder`, and the native class `File`, `File.open(path, mode)`, `read`, `write` and `dispose()`; each asynchronous by default, its `Sync` form beside it; UTF-8 unless an optional argument names an encoding; any file the process may; and every failure an `IOError` (WL3011) naming the path as the script gave it. Recorded as approved in the specification, section 3.9 ("Files: `std:fs`"), its register, and its codes (WL3011); the details are Proposed in section 15.2 ("Files (`std:fs`)", and "Native modules' optional parameters and factories"). Built for it: the platform's file system, `IWebLingoFileSystem` (`DotNetFileSystem` on .NET), and the host's side of the scheduler, `IWebLingoSchedulerHost` (`DotNetSchedulerHost`), on whose threads an asynchronous function's work runs, its outcome posted back to the worker, which waits for it when nothing else can run, both VMs unchanged but for handing the heap the host's file system and scheduler; a native function's optional parameters, through a function written in WebLingo with those defaults; and native classes' factories, static methods given their class, `File.open` one. The benchmark's `io` workload writes 1,000 small files and reads them back, one at a time, then all at once with `Task.all`: 1,100.5 ms on the register VM, 1,136.0 ms on the stack VM, nearly all of it the file system's (the specification, section 5.6, "I/O-bound work", with every workload's numbers, for the choice between the VMs, a task below). Guessed: `File`'s `read()` gives its next line, `null` at its end; modes `"r"`, `"w"` and `"a"`; `list` gives names, in ordinal order; `makeFolder` makes the folders above it too; paths from the process's current folder; UTF-8 written without a byte order mark and read skipping one; text not valid in its encoding an `IOError`; an unknown encoding WL3001. Checks: the Release build, without warnings, the full test suite (2,131 tests), the CLI on the samples, and the benchmark, on Windows only; Linux not tried.

## [DONE] The Choice Between the VMs (WL-BC002)

The register VM or the stack VM (specification, sections 5.2 and 5.6; WL-BC002, Provisional), which the owner's decision of 13 November 2026 left open until property access, async suspension, template output and I/O-bound work could be measured too; all four can now, the last since `std:fs` ("I/O-Bound Workload", in `DONE.md`). M0.13, WBC, the bytecode's binary format, is the chosen VM's, and waits for it. Added by Claude, 2026-11-15.

Every workload, measured together in one run, 11 timed runs after 3 warm-ups, on Windows (the specification, section 5.6, "I/O-bound work"):

| Workload | Register | Stack | Stack ÷ register | Instructions run, register | Instructions run, stack | Code bytes, register | Code bytes, stack |
|---|---:|---:|---:|---:|---:|---:|---:|
| loops | 26.9 ms | 33.5 ms | 1.25 | 10,010,008 | 17,514,512 | 76 | 74 |
| calls | 10.4 ms | 11.4 ms | 1.10 | 2,063,676 | 2,670,639 | 74 | 48 |
| closures | 13.6 ms | 15.9 ms | 1.17 | 3,120,009 | 4,660,014 | 106 | 93 |
| strings | 10.9 ms | 12.9 ms | 1.18 | 1,920,008 | 3,100,012 | 83 | 76 |
| collections | 4.4 ms | 4.9 ms | 1.11 | 605,064 | 1,030,106 | 223 | 218 |
| objects | 98.5 ms | 102.4 ms | 1.04 | 3,200,026 | 5,000,042 | 187 | 156 |
| async | 4.0 ms | 5.0 ms | 1.25 | 701,913 | 1,403,221 | 108 | 108 |
| templates | 6.2 ms | 6.1 ms | 0.98 | 280,010 | 400,015 | 135 | 104 |
| io | 1,100.5 ms | 1,136.0 ms | 1.03 | 114,080 | 152,109 | 758 | 560 |

The register VM is faster on every workload where the VMs' own work counts, by 1.04 to 1.25 times, running 1.3 to 2.0 times fewer instructions; the stack VM's bytecode is up to 35 percent smaller, and its code generator about a fifth shorter (section 5.6, "The comparison"). Where other work dominates, writing a page's text or files, the two are as close as the machine's noise. Two decisions, each answerable with a letter.

Q1, the VM: (a) the register VM; (b) the stack VM; (c) both still, each feature built on both, until something more is measured.

A1: (c)

Q2, if one is chosen, the other: (a) removed, with its code generator, its verifier and its tests; (b) kept as it is, built on no further, its tests kept for what it has, until it is removed.

A2: does not apply while both VMs are kept (A1).

Q3, asked in the chat once A1 kept both VMs, M0.13, WBC, which waited for the choice: (a) it goes ahead now, a format for each VM's bytecode, the container shared (header, section directory, constants, functions, exceptions, debug information), only the code section's encoding differing; (b) it waits until one VM is chosen, hosting first, so that startup and latency can be measured.

A3: (a)

2026-11-15: answered by the owner in the chat, one question at a time; recorded by Claude.

2026-11-15 (Claude): A1 and A3 recorded as approved in the specification (sections 1.1, 5.2, 5.6, 9 and 10) and in `Documents/implementation.md`: both VMs kept, every feature built on both, until something more is measured, startup and latency among what is not; M0.13 goes ahead with a format for each VM's bytecode in one container ("M0.13: WBC", above, asks its format's questions). Documents only: no code changed, so the build and the tests are as on `main`.

Done (Claude, 2026-11-15, `8ba2b90`): the owner kept both VMs (A1, c), every feature built on both until something more is measured; A2 does not apply; and M0.13 goes ahead now, a format for each VM's bytecode in one container (A3, a). Recorded as approved in the specification, sections 1.1 (WL-BC002, until decided), 5.2, 5.6 ("Both VMs, for now"), 9 (M0.13) and 10 (open question 1), and in `Documents/implementation.md`, section 6. Documents only.

## [DONE] COM Module

Design and propose a native extension for Windows that would allow the scripts to use COM objects, like old ASP did. Do not code it yet.

2026-11-15 (Claude): designed, not coded, in the specification, section 3.10 ("COM objects on Windows"), all of it Proposed: a native module that only Windows hosts install; `com.create("ADODB.Connection")` and `com.get(moniker)`, as `Server.CreateObject` and `GetObject`, giving a native class's instances that hold the COM objects and release them when disposed; their members through `IDispatch`, `x[k]` their default member, through the indexers part 4 gives native classes; `VARIANT`s mapped to WebLingo's values, currency and decimals as numbers and dates as ISO 8601 strings until WebLingo has those kinds; a failed call a catchable `ComError` (WL3012, with `number`, `source` and `description`, as ASP's `Err`); each COM object its worker's, as COM's apartments ask; and `foreach` over a COM collection waiting for the question part 4 asks. Five decisions, each answerable with a letter.

Q1, its name: (a) `std:com`, a standard module that only a Windows host installs; (b) a scheme of its own for Windows's native modules, `windows:com`.

A1: (b)

Q2, its objects' members, known only while the program runs: (a) as a program writes any member, `conn.Open(text)`, `rs.EOF`, a native class getting a fallback for the members it does not declare (a getter, a setter and a call, each a native function); (b) through methods of `ComObject`'s own, `conn.call("Open", [text])`, `rs.get("EOF")`, `rs.set("Filter", text)`, native classes unchanged.

A2: (a)

Q3, the threads COM runs on: (a) each worker that runs a script a single-threaded apartment's, with a message pump, as ASP's were, every object's calls direct; (b) workers as now, COM's objects made in an apartment of COM's own, every call to one marshalled to it, slower; (c) a thread of its own for COM in each execution, every call sent to it.

A3: (a)

Q4, the objects a script may make: (a) any the process may; (b) only the ProgIDs and CLSIDs an application's configuration lists, `weblingo.json`'s `"com": { "allow": ["ADODB.Connection"] }`, none by default, since objects such as `WScript.Shell` reach the whole machine.

A4: Don't allow com by default. It requires a config permission. (Claude's reading, which the owner did not correct: (b), the permission listing the objects allowed, `"*"` allowing any.)

Q5, how its C# calls COM: (a) `IDispatch` through function pointers on its table of methods, by platform invoke, so that the same source compiles with Native C#; (b) .NET's own COM interop (`dynamic`, `[ComImport]`), simpler, but for .NET only.

A5: (a)

2026-11-15: answered by the owner in the chat, one question at a time; recorded by Claude.

2026-11-15 (Claude): the answers recorded as approved in the specification, section 3.10 and its register: `windows:com`, under a scheme of its own (A1); members written as any member, through a fallback native classes get for members they do not declare (A2); each worker a single-threaded apartment's thread with a message pump (A3); COM not allowed by default, a permission in the application's configuration (A4, read as a list of the ProgIDs and CLSIDs allowed, `"*"` for any, which the owner did not correct, and recorded as Proposed); `IDispatch` through function pointers, by platform invoke (A5). Guessed, and Proposed: the `windows` scheme's folder `windows/` beside the runtime; a COM member hidden by one every value has reached by `ComObject`'s `call`, `get` and `set`; a ProgID the configuration does not list WL3001. Designed only, as the task asks: nothing is built. Documents only.

Done (Claude, 2026-11-15, `3848d3e`): designed, not coded, as the task asks, in the specification, section 3.10 ("COM objects on Windows"), its name (`windows:com`), its members (as any member, through a native class's fallback), its threads (each worker a single-threaded apartment's), its permission (none by default, the application's configuration listing what a script may make) and its interop (`IDispatch` through function pointers) approved on 15 November 2026, the rest Proposed. Building it is not a task yet. Documents only.

## [DONE] Native Modules, Part 4: The Built-in Classes as Native Classes

`Array`, `Map`, `Set`, `Task`, `Module`, `Class` and `Function` as a standard module's native classes, their methods native functions, measured against the VMs' own on the benchmark, as A3 of "Native Modules" asks (specification, sections 3.9 and 9, "Native modules", part 4), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for parts 2 and 3.

2026-11-14: parts 2 and 3 are done. Native classes' members call their built-ins through members written in WebLingo, two calls for one ("Native Modules, Part 2: Native Classes", in `DONE.md`), so the built-in classes' methods, which the VMs reach directly now, will need a direct call in the VMs to be measured fairly.

2026-11-14 (Claude): part 2's native classes call their built-ins through members written in WebLingo, two calls for one, while the VMs call the built-in classes' methods directly now (`a.push(x)` is one step of `WebLingoMembers`, a runtime class). A3 of "Native Modules" asks that the move be measured; four decisions come first, each answerable with a letter.

Q1, the standard module that holds them: (a) `std:system`, which every module imports implicitly, so that their names stay in scope everywhere, as now; (b) a module of their own, `std:core`, imported implicitly as `std:system` is.

A1: (b)

Q2, how the VMs call a built-in class's native method, `a.push(x)`: (a) directly, the class's method being the native function, which the VM gives the instance first, a change to `Run`'s calls, measured; (b) through a method written in WebLingo that calls it, as part 2's native classes do, `Run` unchanged, each native method two calls; (c) both, measured on the benchmark, the owner choosing on the numbers.

A2: (c)

Q3, if the native methods measure slower than the VMs' own: (a) they land all the same, the numbers recorded, A3's (a) having chosen them; (b) the VMs keep their own, the native methods landing only when as fast, within the benchmark's noise; (c) nothing lands until the owner has seen the numbers.

A3: (c)

Q4, the members that are not methods, `length`, indexing (`a[i]`) and `foreach`: (a) they stay the VMs' own, only methods becoming native; (b) native too, native classes getting properties and indexers, each getter and setter a native function.

A4: (b), `length` should look like a property to the scripts. 

2026-11-15 (Claude): the plan. `std:core`, a native module installed as `std/core.dll`, as `std:tasks` is, which the compiler loads for every program, as it loads `std:system`; its classes are the classes of values (`WebLingoNativeClass.OfValues(kind)`), their methods run on values of their kind, not on instances a constructor marked. Native classes get properties (`DeclareProperty`) and an indexer (`DeclareIndexer`), each getter and setter a native function, `length` a property. Built on two branches, neither landing until the owner has seen the numbers (A3): `claude/native-builtin-classes`, the members called through methods written in WebLingo, as part 2's are (A2's (b)); and `claude/native-builtin-direct`, on it, the VMs calling a native member's built-in directly, given the instance first, without the method's frame (A2's (a)). Measured on the benchmark, every workload on both VMs, against `main`, the three run alternately. Guessed: the measurement covers `Array`, `Map`, `Set` and `Task`, whose members the benchmark's workloads use; `Module`, `Class` and `Function` follow the mode chosen, their members (`Module.import`, `setMethod`) rare enough that no workload would show them; and `foreach` stays the VMs' own for now, the language having no way yet for a class to be visited by it, which a question will ask.

2026-11-15 (Claude): built on two branches, neither landed, as A3 asks: `claude/native-builtin-classes`, A2's (b), `std:core`'s members called through methods written in WebLingo; and `claude/native-builtin-direct`, on it, A2's (a), the VMs calling a native member's built-in directly, given the instance first, without the method's frame. `std:core` (`std/core.dll`) has `Array`, `Map`, `Set` and `Task`, their methods, `length` (a property) and their indexers native functions; native classes got properties and an indexer. Measured against `main`, the three run alternately, three rounds of every workload on both VMs, each the middle of its three medians (recorded in the specification, section 5.6, "The built-in classes as native classes"):

| Workload | VM | `main` | (b) through methods | (a) direct |
|---|---|---:|---:|---:|
| collections | register | 4.5 ms | 12.6 ms (2.80 times) | 10.2 ms (2.27 times) |
| collections | stack | 5.1 ms | 13.7 ms (2.69 times) | 11.7 ms (2.29 times) |
| the seven others | both | | within the machine's noise | within the machine's noise |

Where the time goes, both ways: each call of a native member copies its arguments into an array of its own, as every built-in's call does (7 of the 8.1 MB `collections` allocates, against `main`'s 1.06 MB); finds the member by its name in its class, where the VMs' own find it by a number; and passes through two delegates to the runtime's own member. (b) runs the method's frame besides: 1.1 million instructions for `main`'s 605,000. Found on the way, and fixed on both branches: three changes that made the stack VM's `loops` workload, which calls no built-in member, 7 to 19 percent slower, .NET inlining the index wrappers into its dispatch method, the indexer's pending call written in it, and the direct call written in its call path; each moved out of it, `loops` is as fast as on `main` (the lesson is in `Documents/implementation.md`, section 11). What a script would see either way: a built-in method read without a call (`[1].push`) is a function bound to its value, as any class's method is, where it was WL3001; and `a.length = 2` says that the property has no setter. All 660 conformance programs give the same diagnostics, output and errors on both VMs with `std:core` as with the VMs' own members, but for that message. All 2,763 tests pass on both branches, on Windows only. Guessed: `Module`, `Class` and `Function` follow the choice, unbuilt; `foreach` stays the VMs' own (Q2).

Q1, what lands: (a) the direct calls, (a), as they are, the built-in classes' members about 2.3 times slower where a program uses them most; (b) the direct calls, after their costs are cut (arguments passed without a copy, a member found by its number, not its name), measured again before anything lands; (c) the VMs keep their own members, `std:core` dropped, and what lands is what native modules gain, their classes' properties, indexers and direct calls; (d) nothing.

A1: (b) and let's look at the numbers again. Explain them in plain language though.

Q2, how a class is visited by `foreach`, which part 4's native classes and COM's collections both need: (a) a native class declares an iterator, a native function given the instance and a position, giving the next item, or entry, and the next position, as the VMs visit collections now; (b) the language gets a protocol of its own, a class's `iterate()` giving an iterator whose `next()` gives each item, which classes written in WebLingo implement too.

A2: (b)

2026-11-15 (Claude): A1 (b) and A2 (b). The plan: the two branches' work moved onto today's `main` (`std:fs` landed since), on a branch of its own, then its costs cut: a native call's arguments read where they are, not copied into a new array (the native functions' contract taking a read-only view of them), a class's members that the runtime names by number (`push`, `length`, an indexer) found by that number, std:core's functions reaching the heap directly, and the stack VM calling directly too when the result is dropped; then measured against `main` and against the uncut build, alternately, and the numbers explained in plain language. Nothing lands before the owner has seen them. A2's protocol is a task of its own ("The `foreach` Protocol", below), whose details need answers first.

2026-11-15 (Claude): done as A1 asks, and measured again; nothing landed. All of part 4's work is now on one branch, `claude/native-builtins`, moved onto today's `main` (`std:fs` included), its costs cut: a built-in's arguments read where they are, never copied (the built-ins' contract takes a read-only view of them, `ReadOnlySpan`); a member the runtime names by number (`push`, `length`, an indexer) found by that number; `std:core`'s members reaching the heap directly; and the stack VM calling directly when a result is dropped too. All 2,790 tests pass, on Windows. `main`, the branch before the cuts and after them, run alternately, three rounds of all nine workloads on both VMs:

| Workload | VM | `main` | Before the cuts | After the cuts |
|---|---|---:|---:|---:|
| collections | register | 4.4 ms | 9.3 ms (2.11 times) | 9.0 ms (2.05 times) |
| collections | stack | 5.0 ms | 11.2 ms (2.24 times) | 10.2 ms (2.04 times) |
| templates | register | 5.9 ms | 5.6 ms | 4.6 ms (0.78 times) |
| templates | stack | 5.8 ms | 5.6 ms | 5.2 ms (0.90 times) |
| the seven others | both | | as `main`, within the noise | as `main`, within the noise |

In plain language:

- Programs that hardly use the built-in classes' members run as fast as on `main`, before the cuts and after: loops, calls, closures, strings, objects, async, and io.
- `collections` uses them about 120,000 times a run (`push`, `length`, `a[i]`, `m[k]`), and takes about twice as long as on `main`, after the cuts as before them.
- The cuts did what they were for: `collections` allocates 1.10 MB again, as on `main` (8.1 MB before), and the stack VM runs `main`'s count of instructions again. But the time hardly moved: the copying and the lookups by name were not where it went.
- Where it goes: on `main`, `a.push(x)` is one check inside the VM ("an array, and `push`? then add the value"), a few billionths of a second. As a native member it is a call: the VM finds `Array`'s `push`, sees that it is native, finds its function, hands it the arguments through the module's interface, which checks that the value is an array, and then adds it. Each step is small; together they cost about 38 billionths of a second a use on the register VM, and 43 on the stack VM, which over 120,000 uses is `collections`' extra 4.6 to 5.2 ms. Making that much cheaper would mean making a call as cheap as the check the VMs do now, which is what their own members already are.
- A gain either way: with arguments no longer copied, every built-in's call is cheaper, `write` and a page's text among them, so `templates` is 22 percent faster on the register VM and 10 percent on the stack VM, and allocates 12.0 MB, not 14.8.

Q3, what lands: (a) the built-in classes as `std:core`'s native classes, as the branch has them, about twice as slow where a program uses their members most, as fast elsewhere; (b) the VMs keep their own members for the built-in classes, `std:core` dropped, and what lands is what helps every native module and built-in: arguments read where they are, native classes' properties, indexers and direct calls, members found by number; (c) `std:core` lands as what defines the built-in classes, but the VMs keep their own fast way for its members until a program replaces one (`extend`, `setMethod`), each member then written twice, which the tests keep alike; (d) nothing.

A3: (b)

2026-11-15: answered by the owner in the chat, one question at a time; recorded by Claude.

2026-11-15 (Claude): done as A3 (b) has it, on `claude/native-classes-members`, from `main`: the built-in classes keep the VMs' own members, `std:core` dropped, and what serves every native module landed from `claude/native-builtins`: native classes' properties (`DeclareProperty`) and indexer (`DeclareIndexer`), the VMs calling a native member's built-in directly, without its frame, every built-in's arguments read where they are (`ReadOnlySpan<WebLingoValue>`, added to the portability inventory), and members the runtime names by number found by it. The test module's `Counter` got two properties and an indexer, tested on both VMs. Recorded in the specification, sections 3.9 (the owner's decision, in place of making the built-in classes native), 5.6 and 9, and in `Documents/implementation.md` (the pipeline, and a lesson in section 11). Measured against `main`, alternately, three rounds of every workload but `io`: all within the machine's noise, but `templates`, 18 to 23 percent faster; two slowdowns of `collections` found and fixed on the way, the runtime looking for an indexer on every array and map, and the stack VM's indexing moved into helpers. Guessed, and Proposed: `x?[k]` does not call a native class's indexer yet, WL3001 on an instance whose class has one ("Optional Indexing Through a Native Class's Indexer", below). Checks: the Release build, without warnings, and the full test suite (2,140 tests), on Windows only; Linux not tried. `claude/native-builtins` is kept until this lands, then deleted.

2026-11-15 (Claude): landed in `main` (`5ffbfe8`); main's checks passed: the Release build, without warnings, the full test suite (2,140 tests), the CLI on the samples and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux not tried. `claude/native-builtins`, `std:core` included, is deleted.

Done (Claude, 2026-11-15, `5ffbfe8`): as A3 (b) has it, the built-in classes keep the VMs' own members, `std:core` dropped, and what serves every native module landed: native classes' properties and indexers, the VMs calling a native member's built-in directly, every built-in's arguments read where they are (`ReadOnlySpan<WebLingoValue>`), and members the runtime names by number found by it; recorded in the specification, sections 3.9, 5.6 and 9, the owner's decision in place of making the built-in classes native. Measured against `main`: all workloads within the machine's noise, `templates` 18 to 23 percent faster. `x?[k]` through a native indexer is a task of its own. Checks: the Release build, without warnings, 2,140 tests, the CLI on the samples and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux not tried.

## [DONE] Native Modules

Native modules as the specification, section 3.9 ("Native modules"), has them, approved 14 November 2026: `IWebLingoNativeModule` and `IWebLingoNativeRegistry`, a module's functions and classes; an assembly loaded, into the host's context, from a configured scheme's folder only; a module imported by its scheme or by a path within such a folder, statically and while the program runs, its exports known to the compiler; and `std:system`'s built-ins moved into a native module in the `std` scheme's folder; on both VMs. Added by Claude, 2026-11-14.

Section 3.9 leaves three decisions open that the work needs, one each, answerable with a letter.

Q1, how a host is configured with its schemes, `native` and the others, each naming a folder: (a) a file, `weblingo.json`, in the program's folder, `{ "schemes": { "native": "C:/WebLingo/native" } }`; (b) the command line, `weblingo run --scheme native=C:/WebLingo/native script.wl`, a host under a web server having a configuration of its own; (c) both, the command line over the file.

A1: (c)

Q2, the `std` scheme's folder: (a) a folder beside the runtime's program, `std/`, which a host's configuration may point elsewhere; (b) configured as any other scheme is, with no default.

A2: (a)

Q3, which built-in classes become a standard module's native classes now (A2 of "Native Code Extensions, Continued"): (a) the ones the VMs implement (`Array`, `Map`, `Set`, `Task`, `Module`, `Class`, `Function`), their methods (`push`, `has`, `forget`, `setMethod`, …) native functions, those written in WebLingo (`Object`, `Error`, the runtime errors' classes) staying so; (b) all of them, `Object` and `Error` too; (c) none yet: native classes first, for native modules, and the built-in classes moved by a task of their own, measured, since the VMs reach their methods directly now.

A3: (a) but - like (c) says - measure the impact on performance. 

2026-11-14 (Claude): the answers recorded as approved in the specification, section 3.9 ("Native modules"), and its decision register; split into the parts section 9 defines ("Native modules"), each a task below. Waits for them.

2026-11-15 (Claude): every part is done, the last, part 4, in `5ffbfe8` (the specification, section 9, "Native modules").

Done (Claude, 2026-11-15, `5ffbfe8`): native modules as section 3.9 has them, in the four parts section 9 defines ("Native modules"): native functions, native classes, the standard modules as native modules, and native classes' properties, indexers and direct calls, the built-in classes keeping the VMs' own members (part 4, the owner's decision of 15 November 2026). Each part's checks are in its own entry above.

## [DONE] The `foreach` Protocol

How `foreach` visits a class's instances, as A2 of "Native Modules, Part 4" has it: the class's `iterate()` gives an iterator, whose `next()` gives each item, which classes written in WebLingo, native classes and COM's collections all implement (specification, section 3.6, approved 15 November 2026). Added by Claude, 2026-11-15. Three decisions it needs, each answerable with a letter.

Q1, how an iterator says it has no more items: (a) `next()` gives `null` at the end, so that `null` cannot be an item; (b) the iterator also has `hasNext()`, asked before each `next()`, as Java's has; (c) `next()` gives a map, `{ value: v, done: false }`, and `{ done: true }` at the end, as JavaScript's iterators do, a map made for each item.

A1: (b)

Q2, a key and a value, `foreach (var (key, value) in x)`, for a class's instances: (a) `next()` gives each entry as an array of two, `[key, value]`, which the loop takes apart; (b) not yet: only `foreach (var item in x)` visits a class's instances.

A2: (b)

Q3, arrays, maps, sets and strings: (a) `foreach` visits them as now, the VMs' own way, the protocol for every other class; (b) as now, but each also has `iterate()`, which a script may call itself; (c) through the protocol too, as any class, measured against the VMs' own way first.

A3: (b)

2026-11-15: answered by the owner in the chat, one question at a time; recorded by Claude.

2026-11-15 (Claude): done as A1 to A3 have it, on both VMs, recorded as approved in the specification, section 3.6 and its register; the details, guessed, are Proposed in section 15.2 ("The `foreach` protocol"): `iterate()` called once, before the loop, then `hasNext()` before each `next()`; a collection of a class extending `Array`, `Map` or `Set` visited the VMs' own way, its class's `iterate()` aside; a class or a module with a static `iterate()`, and a value an `extend` block gives one, visited by it; the built-in collections' iterators instances of a built-in class `Iterator`, with the fields `items` and `position`, an array's visiting it as it is, a set's, a map's (entries as `[key, value]`) and a string's what it held when `iterate()` was called; `next()` past the end WL3005, a value without `iterate` WL3001 as before, an iterator without `hasNext()` WL3006. Built as two new operations in each bytecode, `ForEachStart` and `ForEachVisits`, around the VMs' own loop, which is unchanged. Measured against `main`, alternately, three rounds of every workload but `io`: all within the machine's noise, `collections` running 1.7 percent more instructions (two for each of its loops). Checks: the Release build, without warnings, the full test suite (2,170 tests, 15 new programs on both VMs), on Windows only; Linux not tried.

Done (Claude, 2026-11-15, `1302851`): the protocol as A1 to A3 have it, `iterate()`, `hasNext()` and `next()`, on both VMs, one item at a time, arrays, maps, sets and strings visited the VMs' own way and having `iterate()` too; recorded as approved in the specification, section 3.6, the details Proposed in section 15.2 ("The `foreach` protocol"). Main's checks passed: the Release build, without warnings, 2,170 tests, the CLI on the samples and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux not tried.

## [DONE] Optional Indexing Through a Native Class's Indexer

`x?[k]` on an instance whose class has an indexer, a native class's (specification, section 3.9), calls the indexer's getter as `x[k]` does, and gives `null` where the getter raises a missing key (WL3007) or an index out of range (WL3005), as `?[` does for a collection (section 3.6); for now it is WL3001. It needs a pending call that turns those two errors into `null`, in both VMs' dispatch methods, so it is measured against `main`. Added by Claude, 2026-11-15, from "Native Modules, Part 4".

2026-11-15 (Claude): done, on both VMs: `x?[k]` on an instance whose class has a native indexer calls it, and gives `null` where its getter raises WL3007 or WL3005, other errors raised as they are, through a third built-in of the indexer, its optional getter `?[]`, which the runtime gives the VMs as the pending call it gives the getter for `x[k]`, so that neither VM changed (a first version, in the VMs' failure paths, slowed the register VM's `loops`, `strings` and `collections` by 5 to 11 percent, and was not kept). Recorded in the specification, section 3.9 (applying section 3.6's `?[`, no new decision), and in `Documents/implementation.md`. Guessed: an indexer given by a program's `setMethod("[]", …)`, which has no optional getter, is called by `?[` as `x[k]` calls it. Checks: the Release build, without warnings, the full test suite (2,172 tests), on Windows only; Linux not tried.

Done (Claude, 2026-11-15, `537f7d4`): `x?[k]` through a native class's indexer, its optional getter `?[]` giving `null` for a missing key or an index out of range, neither VM changed. Main's checks passed: the Release build, without warnings, 2,172 tests, the CLI on the samples and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux not tried.

## [DONE] M0.13.1: WLBC's Format

WLBC's writer and reader for both VMs' modules, each section's CRC-32 checked, a damaged file rejected as WL4001, and what is read verified; every conformance program written, read back and run on both VMs (specification, section 9, "M0.13 parts", part 1; section 5.3, "As built"). Added by Claude, 2026-11-16.

2026-11-16 (Claude): done, on both VMs: `WebLingoBytecodeFile` (`WebLingo.Bytecode`) writes a module of either VM as WLBC 0.1 and reads it back, the container as section 5.3 proposes it with the owner's answers applied (one VM's bytecode in a file, its header saying which, each reader refusing the other's; jump targets as instruction indices; each section's CRC-32 in its directory entry, in place of an integrity section), the details Proposed in section 15.2 ("WLBC's layout"). Guessed: version 0.1, since nothing is frozen; counts and lengths as unsigned LEB128, every other integer signed; strings as WTF-8, so that a lone surrogate reads back as it was; source positions kept as the compiler's, with a table of each file's lines and columns, so that a runtime error is located without the source; the source positions' section optional, every other one required, once and in order. Tests: 11 of the format's own (its layout, CRC-32, numbers and strings, and every refusal, with its reason, a fuzz test among them), and all 679 conformance programs written for both VMs, read back, written again as the same bytes, and run, giving the same output and error, its line and column found from the file; files changed at random are refused, or run without crashing either VM. No VM changed, so nothing was benchmarked. Checks: the Release build, without warnings, the full test suite (2,864 tests), on Windows only; Linux not tried.

Done (Claude, 2026-11-16, `6ad6ddd`): WLBC 0.1, written and read for both VMs' modules, each section's CRC-32 checked, every refusal a WL4001, what is read verified; every conformance program read back from its file and run on both VMs. Main's checks passed: the Release build, without warnings, 2,864 tests, on Windows only; Linux not tried. The CLI and `wasp` were not run: neither changed.

## [DONE] M0.13.2: WLBC on the Command Line

`weblingo compile [--vm stack]`, `weblingo run` of a `.wlbc` on the VM its header names, and `weblingo disasm`, with a disassembler of the stack bytecode; the native modules a `.wlbc` names loaded when it runs; its files named from the program's folder; and `weblingo run` of a `.wl` writing no `.wlbc` (specification, section 9, "M0.13 parts", part 2; A1 and A2 of "M0.13: WLBC"). Added by Claude, 2026-11-16.

2026-11-16 (Claude): done, on both VMs: `weblingo compile [--vm register|stack] [--scheme …] script.wl` writes `script.wlbc` beside it; `weblingo run` and `check` take a `.wlbc`, `run` running it on the VM its header names, the native modules it names loaded first; `weblingo disasm script.wlbc` shows it as text, through a new disassembler of the stack bytecode, `WebLingoStackDisassembler`; `wasp` has none of it. A `.wlbc` names its files from the program's folder, the program's own by its name alone, and the functions that run its files by those names, so that it holds none of the host's paths; `run` names them again from the `.wlbc`'s folder, so that its errors read as from source, line and column found from the file, the sources not needed. Recorded in the specification (sections 8.4 and 9; the details Proposed in section 15.2, "WLBC on the command line"), `Documents/implementation.md` and the README. Guessed: a file is a `.wlbc` by its extension; `compile` prints nothing; a `.wlbc` that cannot be used is WL4001 and exit code 1, as compilation errors are, and one that cannot be written exit code 4, as a file that cannot be read; `check` of a `.wlbc` reads and verifies it. Tests: 11 end to end (both VMs; a program and its import run from the `.wlbc` with the sources deleted; runtime errors located as from source; no host path in the file; `disasm`; a damaged file refused by `run`, `check` and `disasm`; `run` of a `.wl` writing nothing), and the disassemblers on every conformance program read back. No VM changed, so nothing was benchmarked. Checks: the Release build, without warnings, the full test suite (2,875 tests), the CLI on the samples, each from its source and from its `.wlbc` for both VMs, the same output, and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux not tried.

Done (Claude, 2026-11-16, `3a888b0`): `weblingo compile [--vm register|stack]`, `run` and `check` of a `.wlbc` on the VM it is for, and `disasm`, with the stack bytecode's disassembler; the file's names from the program's folder, none of the host's paths; `run` of a `.wl` writing nothing. Main's checks passed: the Release build, without warnings, 2,875 tests, the CLI on the samples, from source and from their `.wlbc` on both VMs, and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux not tried.

## [DONE] M0.13: WLBC

WLBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WL-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

2026-11-13: M0.13 now, after M0.12, templates (specification, section 9); waits for M0.12.

2026-11-14: M0.12 is done; still waits for the choice between the VMs (WL-BC002), which waits for an I/O-bound workload, the last of those "Both VMs, for now" waits for ("I/O-Bound Workload", below).

2026-11-15: the I/O-bound workload is done; waits for the owner's choice between the VMs ("The Choice Between the VMs (WL-BC002)", below).

2026-11-15 (Claude): the owner kept both VMs, and has M0.13 go ahead now, a format for each VM's bytecode in one container (A1 and A3 of "The Choice Between the VMs (WL-BC002)"). The specification proposes the container (section 5.3), none of it approved, and does not say what uses it. Guessed, for now: a `.wlbc` holds a whole program as the compiler joins it, its static imports included, the native modules and dynamic imports it names found when it runs, as from source; its fixed-size fields little-endian; the files its source positions name written as the compiler was given them, relative to the program's folder, never as the host's own paths; a file of a version its reader does not know rejected, as WL4001. Five decisions, each answerable with a letter.

Q1, what uses WLBC in M0.13: (a) the command line: `weblingo compile script.wl` writes `script.wlbc`, `weblingo run script.wlbc` runs one, and `weblingo disasm script.wlbc` shows one as text, commands section 8.4 lists for later; (b) nothing but its tests yet: the format, its writer and its reader, with verification, the command line and a host's cache coming with hosting.

A1: (a), but also `weblingo run script.wl` should work without saving any  `.wlbc` on disk.

Q2, which VM's bytecode a `.wlbc` holds: (a) one, its header saying which, the compiler writing the one it is asked for (`--vm stack`), a VM refusing the other's; (b) either or both, each in a code section of its own, each VM taking its own.

A2: (a)

Q3, the container as section 5.3 proposes it (a 32-byte header with the magic `WLBC`, a major and a minor version, flags, the sections' count, the directory's offset and the file's length; a directory of 32-byte entries; sections for constants, functions, code, classes and modules, exception tables, source positions and integrity; operands in LEB128, strings in UTF-8, binary64s as their 8 bytes): (a) approved, and frozen as version 1.0, every later change a new version; (b) built as proposed, but kept Proposed, free to change without a new version until the owner freezes it.

A3: (b) Nothing is frozen until we are done. All decisions are preliminary. Don't get hung up on formalities.

Q4, a jump's target in a file: (a) a byte offset, as section 5.3 proposes, turned into the instruction's index when read; (b) the instruction's index, as in memory, since both VMs decode a function before they run it, so that nothing is turned.

A4: (b)

Q5, protecting a file against damage, which the verifier does not catch where it leaves the code valid (a flipped constant or string): (a) a CRC-32 of each section, in its directory entry, checked when the file is read; (b) a SHA-256 of the file, which a cache may key by too, through the platform's cryptography (section 7.10); (c) none yet.

A5: (a)

2026-11-16 (Claude): the answers recorded in the specification (sections 1.1, 5.1, 5.3 and 9), A3's "nothing is frozen until we are done" among them. Split into two parts, which the specification defines (section 9, "M0.13 parts"): "M0.13.1: WLBC's Format" and "M0.13.2: WLBC on the Command Line", below. Waits for them.

2026-11-16 (Claude): both parts done ("M0.13.1: WLBC's Format", `6ad6ddd`, and "M0.13.2: WLBC on the Command Line", `3a888b0`); the specification's section 9 has M0.13 implemented.

Done (Claude, 2026-11-16, `6ad6ddd` and `3a888b0`): WLBC, WebLingo ByteCode, for both VMs, as the owner's answers have it: one VM's bytecode in a `.wlbc`, jump targets as instruction indices, each section's CRC-32 in its directory, the container kept Proposed; `weblingo compile`, `run`, `check` and `disasm` of a `.wlbc`, `weblingo run` of a `.wl` and WASP writing none. Main's checks passed: the Release build, without warnings, 2,875 tests, the CLI on the samples and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux not tried.

## [DONE] After M0.13: The Next Milestone

The roadmap ends with M0.13, which is done (specification, section 9), so the next milestone is the owner's to choose. Hosting and the platform layer are the parts of the design not yet built (sections 6 and 7): `WebLingo.Hosting.AspNetCore` and `WebLingo.Platform.DotNet` exist, but no web host runs a page yet. Added by Claude, 2026-11-16.

Q1, which comes next: (a) hosting: a web application served through the ASP.NET Core adapter, for IIS first (section 6), its `.wasp` pages and `.wl` route handlers compiled and cached in memory, its generations reloaded atomically (WL-T007), and `weblingo serve` to run it locally (section 8.4); (b) decimal128, the third numeric category (section 3; open question 3); (c) context-aware template escaping, for attributes, JavaScript, URLs and CSS (open question 9); (d) another, which you name.

A1: (a)

2026-11-16 (Claude): A1 recorded as approved in the specification, sections 1.1 and 9: M0.14, hosting, in seven parts, which section 9 defines ("M0.14 parts"); its questions, and a task for each part, are in `TO-DO.md` ("M0.14: Hosting").

Done (Claude, 2026-11-16, `8bd7002`): hosting is M0.14, the milestone after M0.13, its parts defined in the specification, section 9. Main's checks were not run: only documents changed.

## [DONE] M0.14.1: WLHI's HTTP Contracts

A request's method, path, query, scheme, host, headers and body, and a response's status, headers and body, Uncommitted, then Committed, its status and headers fixed, then Completed (specification, section 6.7); and a request and a response held in memory, for tests and for hosts that buffer (section 9, "M0.14 parts", part 1). Added by Claude, 2026-11-16.

2026-11-16 (Claude): done: WLHI's request and response in `WebLingo.Hosting.Abstractions` (`IWebLingoHttpRequest`, `IWebLingoHttpResponse`, `IWebLingoHeaderCollection`, `IWebLingoBodyReader`, `IWebLingoBodyWriter`), free of .NET's HTTP types, their errors results; `WebLingoHeaderList`, and `WebLingoBufferedRequest` and `WebLingoBufferedResponse`, held in memory. Recorded in the specification, section 6.7, "As built", the details Proposed in section 15.2 ("WLHI's HTTP contracts"). Guessed: a header's name is an HTTP token, compared without regard to ASCII case, and its value visible ASCII, spaces and tabs only, so that no CR or LF can inject a header; a response's status is 200 until set, from 100 to 999; writing its body commits it; bodies are read and written synchronously, their streaming being M0.14.7's. Tests: 15, in a new test project, `WebLingo.Hosting.Tests`. Checks: the Release build, without warnings, the full test suite (2,898 tests), on Windows only; Linux not tried. Neither the CLI nor `wasp` changed.

Done (Claude, 2026-11-16, `3479ad6`): WLHI's HTTP contracts, and a request and a response held in memory. Main's checks passed: the Release build, without warnings, 2,898 tests, on Windows only; Linux not tried.

## [DONE] The Configuration File, `weblingo.config`

A5 of "M0.14: Hosting": the configuration that `weblingo` and `wasp` read in a script's or a page's folder, its schemes today (specification, section 3.9) and an application's settings from M0.14.2 on, is `weblingo.config`, in place of `weblingo.json`, with nothing kept for `weblingo.json`. Guessed: its content stays JSON. Added by Claude, 2026-11-16.

2026-11-16 (Claude): done: `WebLingoSchemes.ConfigurationFile` is `weblingo.config`, which `weblingo` and `wasp` read as they read `weblingo.json` before, a `weblingo.json` no longer read; recorded in the specification (sections 1.1, 3.9, 3.10, 6.9, 9 and 15.2), `Documents/implementation.md` (the names not to change without asking among them) and the README. Tests: the native modules' test of the configuration, a `weblingo.json` now ignored. Checks: the Release build, without warnings, the full test suite (2,898 tests), the CLI on the samples and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux not tried.

Done (Claude, 2026-11-16, `a7cc7d2`): the configuration is `weblingo.config`, `weblingo.json` no longer read. Main's checks passed: the Release build, without warnings, 2,898 tests, the CLI on the samples and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux not tried.

## [DONE] M0.14.2: The Host's Core

The runtime, its applications and their generations, an application's pages and handlers compiled and cached in memory, a request's path found among them, run on a worker, its output written to the response, and a runtime error's response (specification, sections 6.3 to 6.6; section 9, "M0.14 parts", part 2). Added by Claude, 2026-11-16.

2026-11-16: the configuration renamed; taken up.

2026-11-16 (Claude): done: `WebLingo.Hosting`, a new portable project (A6), whose `WebLingoApplication` loads an application from its root through the platform's files, its routes made then (`WebLingoRouteTable`, A3), and serves a request on the worker its host gives, its page compiled when first requested, once on each worker, and kept, with its components, for the generation's life; its output kept until it has run, then written as UTF-8; a page that does not compile or fails a 500, its errors shown in development only (A5); a path without a route a 404; any other file left to the host to serve. The platform's file system gains `IsFolder`. Recorded in the specification (sections 6.12, 7.4 and 9), the details Proposed in section 15.2 ("The host's core"), and in `Documents/implementation.md`. Guessed: pages compiled when first requested, not when the application loads, since an include or a layout is a `.wasp` too, which does not compile alone; each worker's pages its own, so that nothing needs a lock; paths compared with their case on every platform; dot-files, `.wlbc` files, `weblingo.config` and `web.config` never routed; a page's includes, layouts, components and imports found within the root only; the environment given by the host, which reads the configuration (M0.14.4 and M0.14.5); `.wl` handlers 404 until M0.14.3. Tests: 17 more in `WebLingo.Hosting.Tests`, an application whose files are in memory. Checks: the Release build, without warnings, the full test suite (2,918 tests), on Windows only; Linux not tried. Neither the CLI nor `wasp` changed.

Done (Claude, 2026-11-16, `8cef2d5`): the host's core, `WebLingo.Hosting`: an application's routes, its pages compiled and cached in memory on each worker, run, and their responses, a failure a 500, a missing route a 404. Main's checks passed: the Release build, without warnings, 2,918 tests, on Windows only; Linux not tried.

## [DONE] M0.14.4: The ASP.NET Core Adapter

ASP.NET Core's requests translated to WLHI's, an application's other files served as they are, and IIS through the ASP.NET Core Module (specification, section 6.1, WL-H009; section 9, "M0.14 parts", part 4). Added by Claude, 2026-11-16.

2026-11-16: waits for M0.14.2.

2026-11-16 (Claude): done: `WebLingo.Hosting.AspNetCore`'s `UseWebLingo` ends an ASP.NET Core pipeline, under IIS through the ASP.NET Core Module or on Kestrel: a request translated to WLHI's, run on a worker of `WebLingoWorkerPool`, threads of the adapter's own, and its response translated back; a file the application leaves to its host sent with the content type ASP.NET Core gives its extension. Recorded in the specification (section 9; the details Proposed in section 15.2, "The ASP.NET Core adapter") and in `Documents/implementation.md`. Guessed: a request's path without the application's base (`PathBase`), so that an application under an IIS virtual directory has the routes it has at a site's root; its body read whole before it runs, and its response sent whole after, until M0.14.7; a client that goes away cancels its request; an extension ASP.NET Core does not know a 404, as its own static files are. Its IIS deployment, `wasp serve` named in a site's `web.config`, comes with M0.14.5. Tests: 5 in `WebLingo.Hosting.Tests`, on ASP.NET Core's `DefaultHttpContext`, with no new package. A delay test of the conformance tests failed once, under the whole suite's load, racing 1 against 3 milliseconds on the real clock; its margins are wider now (`0fe538e`). Checks: the Release build, without warnings, the full test suite (2,923 tests), on Windows only; Linux and IIS not tried.

Done (Claude, 2026-11-16, `f67e840`): the ASP.NET Core adapter, its worker pool, and an application's files sent as they are. Main's checks passed: the Release build, without warnings, 2,923 tests, on Windows only; Linux and IIS not tried.

## [DONE] M0.14.5: Serving an Application Locally

An application served on Kestrel from the command line (specification, section 8.4; section 9, "M0.14 parts", part 5). Added by Claude, 2026-11-16.

Create a simple, but non-trivial example application, and put its complete source into `Examples\example1`. It must contain everything that one needs to just copy over to an IIS server to run. It must showcase the use of WASP to build a website.

2026-11-16: M0.14.4 is done; taken up.

2026-11-16 (Claude): done: `wasp serve [--root <folder>] [--port <port>] [--scheme <name>=<folder> …]` (`WebLingoWaspServer`) serves an application through the ASP.NET Core adapter, locally on Kestrel, or under IIS, whose ASP.NET Core Module runs it out of process, as a site's `web.config` names it; and `Examples/example1`, a tea-house website of four pages with a layout, two includes, a component and a module, its `web.config`, and `publish.ps1`, which publishes `wasp` into the example's `wasp` folder, so that the folder holds everything an IIS server needs, its README saying how to run and deploy it. Recorded in the specification (sections 8.4 and 9; the details Proposed in section 15.2, "`wasp serve`"), `Documents/implementation.md` and the README. Guessed: port 8080 on `localhost` by default; the root the current folder, and under IIS the site's; IIS known by `ASPNETCORE_PORT` or `ASPNETCORE_IIS_PHYSICAL_PATH`; the environment development locally and production under IIS, unless `weblingo.config`'s `"environment"` names one; a worker for each processor, each with its own built-ins and native-module loader (`WebLingoWorkerServices`, a change to M0.14.2's options), so that loading a native module on one never changes what another reads; a page's import of a path starting with `/` from the application's root under `wasp run` too, as under `wasp serve`; the published `wasp` not committed (`.gitignore`), the example's `publish.ps1` making it. Tests: `WaspServeTests` serves an application, and the example, over HTTP. Checks: the Release build, without warnings, the full test suite (2,928 tests), the CLI on the samples, `wasp` on `samples/SimpleWebsite`, and the published `wasp.exe` serving the example on Kestrel, every page and file as it should be, on Windows only; Linux and IIS not tried.

Done (Claude, 2026-11-16, `55ef8c6`): `wasp serve`, locally and under IIS, and the example application `Examples/example1`. Main's checks passed: the Release build, without warnings, 2,928 tests, the CLI on the samples and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux and IIS not tried.

## [DONE] M0.14.6: Reloading

An application's files watched, a new generation compiled when one changes, and published atomically once healthy, the old one drained (specification, section 6.5, WL-T007; section 9, "M0.14 parts", part 6). Added by Claude, 2026-11-16.

2026-11-16: waits for M0.14.2.

2026-11-16 (Claude): done: `WebLingoApplication.TryReload` makes a new generation from the root's files and publishes it, at once and whole, if its routes can be made, the generation served staying otherwise; a request already running finishes on the generation it began on. `WebLingoReloader`, in the ASP.NET Core adapter, watches an application's root and reloads it, and `wasp serve` uses it, writing a line for each reload. Recorded in the specification (sections 6.5 and 9; the details Proposed in section 15.2, "Reloading") and in `Documents/implementation.md`. Guessed: a generation healthy when its routes can be made, its pages compiled when first requested, as the first generation's are, so that a page a change breaks is a 500 in the new generation; a burst of changes, the files unchanged for 250 milliseconds after it, making one generation, two reloads never at once; a change to `weblingo.config` needing `wasp serve` started again; an old generation going, with its compiled pages, once nothing refers to it. Tests: a reload in the core, over files in memory, and the reloader on disk. Checks: the Release build, without warnings, the full test suite (2,930 tests), and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux and IIS not tried.

Done (Claude, 2026-11-16, `b8a70f5`): reloading, a new generation published whole when the files change, a failed one keeping the generation served. Main's checks passed: the Release build, without warnings, 2,930 tests, and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux and IIS not tried.

## [DONE] M0.14.3: The Request and the Response in Scripts

What a page and a `.wl` route handler see of their request and response, and what a handler is (specification, section 9, "M0.14 parts", part 3). Added by Claude, 2026-11-16.

2026-11-16: M0.14.2 is done. A1 has a page's request and response given as values its parameters take, `param request;`; a script's parameters are given values as it starts, before its classes are made, and only strings (`WebLingoExecutionContext.Parameters`), and a map's entries are read by indexing, `m["key"]`, not as members (specification, section 15.2, "Members and indexing"). One decision, answerable with a letter:

Q1, what a page's `request` and `response` are, and a handler's: (a) instances of two built-in classes, `Request` and `Response`, read and changed through their members, as A1's examples have them: `request.method`, `request.query["id"]`, `request.headers["Accept"]`, `response.status = 404;`, `response.headers["Content-Type"] = "text/plain";`, a wrong status or header an error where it is set; this needs the compiler to give `param request;` its value when the statement runs, once the program's classes are made, a change to the compiler and the runtime, not to the VMs; (b) maps, which a host can give a page as it starts, with no change to the compiler: `request["method"]`, `request["query"]["id"]`, `response["status"] = 404;`, `response["headers"]["Content-Type"] = "text/plain";`, the response's status and headers read once the page has run, a wrong one then a 500 that names no line.

A1: (a)

2026-11-16 (Claude): built (specification, sections 6.12 and 15.2, "The request and the response in scripts"), A1 recorded as approved in sections 1.1 and 6.12. Guessed, and recorded as Proposed: a request's members `method`, `path`, `scheme`, `host`, `query`, a map, decoded, a name given twice having its last value, as PHP's `$_GET` has, and `headers`; a response's `status` and `headers`; the headers' class, `Headers`, read and set by indexing, a missing header WL3007, as a map's key is, `?[]` giving `null`, a value a string, with `has`, `add` and `remove`; the three classes not names a script can write; a handler's `HEAD` calling `get` when it has no `head`, another method a 405 whose `Allow` lists the handler's, and a `.wl` exporting no method's function a 404; a handler's body what it writes, as a page's; and a 404, 405 or 500 dropping the headers a page set. Not built: reading a request's body, which M0.14.7 streams. The compiler's part is tried on both VMs; the host runs the register VM. Tried on Windows only: the tests, and `wasp serve` on Kestrel serving a page and a handler; not under IIS, nor on Linux.

Done (Claude, 2026-11-16, `0387b75`): a page's `request` and `response` as instances of `Request` and `Response`, and `.wl` route handlers. Main's checks passed: the Release build, without warnings, 2,947 tests, `wasp` on `samples/SimpleWebsite`, and `wasp serve` on a page and a handler, on Windows only; Linux and IIS not tried.

## [DONE] M0.14.7.1: Output Sent as It Is Written

A page's output kept until it reaches 64 KB, then its response committed and its output sent as it is written, in pieces; a runtime error after that ending the response unfinished, its error in the host's log and, in development, at the page's end; and the ASP.NET Core adapter sending a response's body as it comes, rather than once the page has run (specification, section 9, "M0.14.7 parts", part 1). Until part 3, a write the client has not yet taken keeps the worker waiting. Added by Claude, 2026-11-16.

2026-11-16 (Claude): built (specification, sections 6.12, 9 and 15.2, "Output sent as it is written"). Guessed, and recorded as Proposed: 64 KiB of UTF-8 kept, then 16 KiB pieces; WLHI's response given `Abort()`, which ends it unfinished; a late failure's error, in development, written after the output in `<pre>`, escaped, as a 500 page shows it; the host's log a line per late failure, `WebLingoApplicationOptions.Log`, which `wasp serve` writes to its output; and the ASP.NET Core adapter letting four pieces wait for the client, a body sent in one piece keeping its `Content-Length`. Tried on Windows only: the tests, and `wasp serve` on Kestrel sending a 300 KB page in chunks and closing the connection of one that failed after it began; not under IIS, nor on Linux.

Done (Claude, 2026-11-16, `b0617d6`): a page's output kept until it reaches 64 KB, then sent as it is written, a late failure ending its response unfinished. Main's checks passed: the Release build, without warnings, 2,955 tests, and `wasp serve` on long, short and failing pages, on Windows only; Linux and IIS not tried.

## [DONE] M0.14.7.2: Executions That Wait Without a Thread

An execution waiting only for work off its worker returns to its host, kept, and goes on where it was once that work is posted back, on both VMs; a host's worker runs other requests' executions meanwhile (specification, sections 6.6 and 6.10; section 9, "M0.14.7 parts", part 2). Today a VM keeps its worker's thread while it waits, so that a request waiting for a file holds a worker. Added by Claude, 2026-11-16.

2026-11-16 (Claude): built (specification, sections 6.6, 9 and 15.2, "Executions that wait without a thread"), on both VMs. Guessed, and recorded as Proposed: an execution context's `Suspends`, which only the web host sets, so that `weblingo` and `wasp run` wait as before; a timer on the host's clock waited for so too, the host resuming at its time; a request's resumption queued for its own worker, taken before new work; at most 64 requests waiting on a worker before it takes no new work; and the pool's disposal cancelling the requests that wait. Tried on Windows only: the tests, and `wasp serve` running 12 pages that each wait 500 milliseconds on 8 workers, all ended by 513 milliseconds, a quick page answered meanwhile; not under IIS, nor on Linux.

Done (Claude, 2026-11-16, `b1eb5c3`): executions that wait without a thread, on both VMs, and a worker pool that runs other requests while one waits. Main's checks passed: the Release build, without warnings, 2,962 tests, and `wasp serve` on pages that wait, on Windows only; Linux and IIS not tried.

## [DONE] M0.14.7.3: Backpressure

A page whose output the client has not yet taken waits as at an `await`, its worker running other requests meanwhile, template output an asynchronous operation (WL-T008), for at most a time, its request then cancelled (specification, section 9, "M0.14.7 parts", part 3). Builds on part 2. Added by Claude, 2026-11-16.

One decision, answerable with a letter:

Q1, how long a page waits for a client that has not taken its output, before its request is cancelled, and where that is set: (a) 30 seconds, unless the application's `weblingo.config` gives another, `{ "sendTimeout": 30 }`, in seconds; (b) 30 seconds, for every application, until one needs another; (c) none of WebLingo's own, the web server's limits deciding, as Kestrel's minimum data rate and IIS's connection timeout do now.

A1: (a)

2026-11-16 (Claude): built (specification, sections 6.12, 9 and 15.2, "Backpressure"), A1 recorded as approved in sections 1.1 and 6.12. Built in the compiler and the host, the VMs unchanged but for part 2's waiting, rather than in both VMs as the question supposed: after a write where the page can `await`, the compiler adds a wait, taken only while the response is full, so that a write that does not wait costs one more built-in's call, not measured. Guessed, and recorded as Proposed: the writers that wait, the template's, `write` and `writeLn`; a write in a function that is not `async`, where it cannot wait, kept until the page next writes where it can; the ASP.NET Core adapter's response full while four pieces of 16 KiB wait; and a timeout's line in the host's log. Tried on Windows only: the tests, on both VMs, and `wasp serve` sending a 40 MB page to a client that reads, and, with `"sendTimeout": 1`, cancelling one that does not; not under IIS, nor on Linux.

Done (Claude, 2026-11-16, `67c31aa`): a page waiting for a slow client without its worker, for at most its send timeout, 30 seconds or `weblingo.config`'s `"sendTimeout"`. Main's checks passed: the Release build, without warnings, 2,966 tests, `wasp` on `samples/SimpleWebsite`, and `wasp serve` sending a 40 MB page and timing out a client that never reads, on Windows only; Linux and IIS not tried.

## [DONE] M0.14.7.4: A Request's Body as It Arrives

What a script reads a request's body with, and the ASP.NET Core adapter giving the body as it comes, rather than read whole before the page runs (specification, section 9, "M0.14.7 parts", part 4). Today a request's body is read whole first, and no script can read it. Added by Claude, 2026-11-16.

One decision, answerable with a letter:

Q1, how a script reads a request's body: (a) `await request.text()`, the whole body as text, UTF-8 unless its `Content-Type` names another charset, and `await request.form()`, an `application/x-www-form-urlencoded` form's fields as a map, as PHP's `$_POST`, each read as the body arrives but given whole; (b) as (a), and `await request.read()`, the body's next piece as text as it arrives, `null` at its end, for a body too large to hold whole; (c) only `await request.read()`, a script joining the pieces itself.

A1: (b) but stream bytes, rather than text.

2026-11-16 (Claude): A1 recorded as approved in the specification, sections 6.12 and 9. Bytes need a value the language does not have yet (specification, section 3.2, "Bytes"), so `request.read()` is a part of its own, "M0.14.7.5: A Body's Bytes", below (section 9, "M0.14.7 parts", part 5); this part builds `request.text()` and `request.form()`, and the body given as it comes.

2026-11-16 (Claude): built (specification, sections 9 and 15.2, "A request's body"). Guessed, and recorded as Proposed: the body read once, when a script first asks for it, and kept, so that `text()` and `form()` give the same again; the charsets `text()` reads, those .NET knows by name, an unknown one WL3001; `form()` reading only `application/x-www-form-urlencoded`, as UTF-8, another type WL3001, a name without `=` an empty string; no size limit of WebLingo's own. Tried on Windows only: the tests, and `wasp serve` given a form, and 5 MB of text, posted; not under IIS, nor on Linux.

Done (Claude, 2026-11-16, `19c5b46`): a request's body read as it arrives, with `request.text()` and `request.form()`; `request.read()`, as bytes, is "M0.14.7.5: A Body's Bytes". Main's checks passed: the Release build, without warnings, 2,973 tests, and `wasp serve` given a form and a 5 MB body, on Windows only; Linux and IIS not tried.

## [DONE] WASP User Manual

Write a comprehensive WASP User Manual with examples. This is not meant to be an exhaustive WebLingo Language Reference though.

2026-11-16 (Claude): written, `Documents/wasp-user-manual.md`, listed among the project's documents (`Documents/implementation.md`, section 2) and linked from both READMEs. Every example in it was run, with `wasp serve` and `wasp run`, and gives the output shown; it describes what is built, what is missing in its last section, and the language only as far as pages need it, the WebLingo Language Reference being the next task. Guessed: its place and its name, and that a change to what a site's author sees updates it in the same change (`Documents/implementation.md`, section 2). Its examples were run on Windows only.

Done (Claude, 2026-11-16, `13a6d90`): `Documents/wasp-user-manual.md`, the WASP User Manual, its examples run. Main's checks: documentation only, the solution unchanged since its last checks (the Release build, without warnings, and 2,973 tests, on Windows).

## [DONE] WebLingo Language Reference

Write a comprehensive WebLingo Language Reference with extensive and numerous examples.

2026-11-16 (Claude): written, `Documents/weblingo-language-reference.md`, listed among the project's documents (`Documents/implementation.md`, section 2) and linked from `WebLingo/README.md` and the WASP User Manual: running programs, the lexical structure, variables, values, operators, strings, statements, functions, collections, classes, exceptions, disposal, asynchrony, modules, the standard library, and the diagnostic codes, with over sixty examples, each run with `weblingo` and giving the output shown, and what the language does not have yet in its last section. Guessed: its place and its name, and that a change to the language updates it in the same change (`Documents/implementation.md`, section 2). Its examples were run on Windows only.

Done (Claude, 2026-11-16, `cd23266`): `Documents/weblingo-language-reference.md`, the WebLingo Language Reference, its examples run. Main's checks: documentation only, the solution unchanged since its last checks (the Release build, without warnings, and 2,973 tests, on Windows).

## [DONE] Keep .NET's Encodings, Timers and Locks Out of the Portable Core

`Documents/NativeCompatibility.md` keeps `System.Text.Encoding`, OS threads and `Monitor` out of the portable core, adapter-only, but `PortabilityTests` checks only some of the inventory's rules, and work of 16 November 2026 broke others unnoticed: `System.Text.Encoding` and `Uri` in `WebLingoHttpObjects` (`0387b75`, `19c5b46`), a `lock` there (`19c5b46`), a `Timer`, `Interlocked` and `Volatile` in `WebLingoResponseText` (`67c31aa`), and `Interlocked` in `WebLingoScheduler.TryWaitForHost` (`67c31aa`). The core gets a text codec of its own, the send timeout's timer moves to the adapter, the body's reading hands over through the scheduler host, and `PortabilityTests` gets the rules it lacked. Found by Claude while preparing "M0.14.7.5: A Body's Bytes", which builds on the codec; added 2026-11-16.

2026-11-16 (Claude): done. `WebLingoTextCodec` (`WebLingo.Runtime`) reads and writes UTF-8, UTF-16, US-ASCII and ISO-8859-1 without `System.Text.Encoding`, a broken sequence U+FFFD; `request.text()` reads those charsets, UTF-32 no longer, and a query's and a form's `%xx` are decoded by the core. WLHI's `IWebLingoBodyWriter.WhenReady` takes the timeout, the ASP.NET Core adapter keeping the timer; the body's reading posts its end to the worker, without a lock; and `TryWaitForHost` takes the host at its word that it calls once. `PortabilityTests` now rules out `System.Text.Encoding` and its encodings, `Timer`, `Monitor`, `Interlocked`, `Volatile` and `Uri`, and `Documents/NativeCompatibility.md` and `Documents/implementation.md` (section 11) say so. Benchmarking, which `Documents/implementation.md` asks for before a change to a VM's `Run` lands and M0.14.7.2's work did not do: alternately against the work before it, its waiting changed no workload's time beyond this machine's noise; but M0.14.7.3's check after a page's write made `templates` about a fifth slower, so a template's writer now gives whether the output is full itself, saving a call per write, after which `templates` is level on the register VM and up to a tenth slower on the stack VM, within the noise. Checks: the Release build, without warnings, 2,990 tests, and `wasp serve` timing out a slow client and reading a form and a 5 MB body, on Windows only.

Done (Claude, 2026-11-16, `ea7a37d`): the portable core free of .NET's encodings, timers, locks and atomics again, `PortabilityTests` checking for them, and template output's check made cheaper. Main's checks passed: the Release build, without warnings, 2,990 tests, and `wasp serve`, on Windows only; Linux and IIS not tried.

## [DONE] M0.14.7.5: A Body's Bytes

`await request.read()`, a request's body's next piece as bytes as it arrives, `null` at its end (the owner's answer to M0.14.7.4's question), and the value that holds bytes, which the language does not have yet (specification, section 3.2, "Bytes": binary data, distinct from strings, proposed), designed first (section 9, "M0.14.7 parts", part 5). Added by Claude, 2026-11-16.

One decision, answerable with a letter:

Q1, what holds a script's bytes: (a) a value of a kind of its own, `Bytes`, immutable, as a string is, built into both VMs, as strings and arrays are: `b.length`, `b[i]`, an integer from 0 to 255, `b.slice(start, end)`, `b.text()`, decoding UTF-8, or `b.text("latin1")`, `Bytes.from("text")`, encoding it, `==` comparing their contents, and `write(b)` writing `Bytes:[104, 105]`; (b) the same members, on instances of a native class, `Bytes`, which the host and native modules make, the VMs unchanged, `==` then comparing identity, as for any instance; (c) an array of integers from 0 to 255, no new kind of value, `request.read()` giving one, and a standard function decoding it as text.

A1:(a) with (1) Building bytes without O(n²) cost, a mutable builder and Bytes.concat([parts]); and
(2) Constant-time comparison for secrets.

2026-11-16 (Claude): A1 recorded as approved in the specification, sections 1.1 and 3.2; built (sections 9 and 15.2, "Bytes" and "A request's body"), on both VMs, and described in the WebLingo Language Reference and the WASP User Manual. Guessed, and recorded as Proposed: the builder, `BytesBuilder`, a class written in WebLingo, `add(value)` of bytes, an integer from 0 to 255, or a string, giving the builder, its `length`, and `toBytes()`; `Bytes.concat` taking integers from 0 to 255 too, and `Bytes.from` an array of them, or bytes; the constant-time comparison `Bytes.fixedTimeEqual(a, b)`, its time given away only by the lengths; `slice`'s end optional; bytes not keys, not visited by `foreach`, not joined by `+`, and `new Bytes()` WL3001, for now; `read()`'s pieces up to 16 KiB, a body read in pieces then not readable whole, and one read whole given by `read()` as one piece. Tried on Windows only: the tests, on both VMs, and `wasp serve` reading 20 MB posted to a handler in 1,221 pieces; not under IIS, nor on Linux.

Done (Claude, 2026-11-16, `d903bd5`): Bytes, a kind of value, with a builder and a comparison in constant time, and `request.read()`. Main's checks passed: the Release build, without warnings, 3,026 tests, and `wasp serve` reading 20 MB in pieces, on Windows only; Linux and IIS not tried.

## [DONE] M0.14.7: Streaming

A response's body written as the page runs, with backpressure, and a request's body read as it arrives, neither buffered whole (specification, sections 6.7 and WL-T008; section 9, "M0.14 parts", part 7). Added by Claude, 2026-11-16.

2026-11-16 (Claude): M0.14.4 is done. Today a page's output is kept until the page has run, then sent whole, so that a runtime error is still a 500; and a VM runs a page on its worker to its end, a write never waiting. Streaming changes both. Two decisions, each answerable with a letter:

Q1, when a page's output is sent, and so what a runtime error after it gives: (a) as the page writes it, in pieces of about 16 KB, the status and headers sent with the first, so that a runtime error after it ends the response there, the connection closed so that the client sees it unfinished, the error in the host's log, and, in development, at the page's end too; (b) kept until it reaches about 64 KB, then sent as it is written, so that most pages, smaller, are still a 500 on an error, and a larger one ends as in (a); (c) kept whole, as now, unless the page sends what it has written with `response.flush()`, after which an error ends it as in (a).

A1: (b)

Q2, what a page does while a slow client has not yet taken what it wrote: (a) its worker waits for the client, as a file's write waits for the disk, for at most a time the configuration gives (30 seconds by default), the request then cancelled; (b) the page waits as at an `await`, its worker running other requests' tasks meanwhile, a change to both VMs, template output becoming an asynchronous operation (WL-T008).

A2: (b) but with a timeout.

2026-11-16: M0.14.4 is done; waits for the answers.

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 6.12 ("Streaming"). Too large to land in one piece: in four parts, which the specification defines (section 9, "M0.14.7 parts"), each a task below. Waits for its parts.

Done (Claude, 2026-11-16): its five parts done, the last, "M0.14.7.5: A Body's Bytes", in `d903bd5`.

## [DONE] M0.14: Hosting

Web applications served through the ASP.NET Core adapter, for IIS first (specification, section 6, WL-H009), their `.wasp` pages and `.wl` route handlers compiled and cached in memory (section 5.1), their generations reloaded atomically (WL-T007), and served locally from the command line (section 8.4); the owner's choice of 16 November 2026 ("After M0.13: The Next Milestone"). In seven parts, which the specification defines (section 9, "M0.14 parts"), each a task below. Added by Claude, 2026-11-16.

The specification leaves these open; each is one decision, answerable with a letter. Guessed meanwhile, and recorded as Proposed when built: a host runs pages on the register VM, as `wasp` does; an application's files are watched through the platform (section 7.4), a burst of changes making one new generation; a request's execution has the instruction and resource budgets `weblingo.json` gives, none by default.

Q1, how a page or a handler sees its request and its response: (a) as `request` and `response`, which every page and `.wl` handler has, as classic ASP's `Request` and `Response` were, each an instance of a native class (`request.query["id"]`, `request.headers["Accept"]`, `response.status = 404;`, `response.headers["Content-Type"] = "text/plain";`); (b) imported from a standard module, `import { request, response } from "std:http";`; (c) given to a page as values its parameters take, `param request;`.

A1: (c)

Q2, what a `.wl` route handler is: (a) a script whose top level runs for each request, writing the response's body with `write`, as a page does; (b) a module exporting a function for each HTTP method it answers, `export async function get(request, response) { … }`, `post`, and so on, any other method a 405; (c) a module exporting one function, `handle(request, response)`, for every method.

A2: (b)

Q3, how a request's path finds its file, under the application's root: (a) by the file's path without its extension: `/products` is `products.wasp` or `products.wl` (both being an error), `/` and a folder's path its `index.wasp` or `index.wl`; any other file of the root served as it is (CSS, images), but never a `.wl` that is not a handler, a `.wlbc` or `weblingo.json`; (b) as (a), but with the extension in the path, `/products.wasp`, as classic ASP's pages had `.asp`; (c) only by the routes `weblingo.json` lists, `"routes": { "/products/{id}": "products.wasp" }`, each `{name}` given to the page as a parameter.

A3: (a)

Q4, which command serves an application locally, and what IIS's ASP.NET Core Module runs: (a) `wasp serve [--root site] [--port 8080]`, WASP's command line, which the site's `web.config` names for IIS too; (b) `weblingo serve`, as section 8.4 lists, `web.config` naming it for IIS.

A4: (a), in the chat, 2026-11-16: "weblingo is strictly the script interpreter/runner. wasp is the web handler". Recorded in the specification, sections 1.1, 8.4 and 9.

Q5, what a runtime error in a request gives: (a) a 500, its body a page showing the error (its file, line, column, code and message) when the application runs in development, and a page without them otherwise, `weblingo.json`'s `"environment"` saying which, development for the local command and production under IIS by default; (b) always a 500 page without the error, which only the host's log has.

A5: (a), but call it `weblingo.config` not json.

Q6, where the host's own code is, the runtime, its applications, their generations, routes and requests (sections 6.3 to 6.6), which every adapter uses: (a) a new portable project, `WebLingo.Hosting`, composing the compiler, the templates and the VMs, the ASP.NET Core adapter only translating HTTP to and from WLHI; (b) in `WebLingo.Hosting.Abstractions`, which then references the compiler and the VMs; (c) in the ASP.NET Core adapter, until a second adapter needs it.

A6: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 6.12, A5's `weblingo.config` as the configuration's name, for a script's folder as for an application's root, in place of `weblingo.json`, its content JSON as before (a task of its own, below, "The Configuration File, `weblingo.config`"). Waits for its parts.

Done (Claude, 2026-11-16): its seven parts done, the last, "M0.14.7: Streaming", with `d903bd5`; what comes after M0.14 is the owner's to choose (specification, section 9).

## [DONE] Create Notepad++ Syntax Coloring

For WebLingo (.wl) and WASP (.wasp).

2026-11-16 (Claude): `Editors/NotepadPlusPlus`, two user-defined languages of Notepad++ (UDL 2.1), applied by extension: `WebLingo.udl.xml`, for `.wl`, and `WASP.udl.xml`, for `.wasp`, whose HTML is a delimiter from a block's end, `%>`, to the next block's start, `<%`, or from a tag that begins a file, nesting HTML's tags and comments, so that the WebLingo code in a page's blocks is coloured as a `.wl`'s is, and a word of the page's text never as a keyword. `Editors/README.md` says how to install them, and their limits, which a UDL cannot avoid: a template comment, `<%# %>`, coloured as code; a page beginning with an unusual tag coloured as code until its first block; `i<n` in a block, without spaces, taken for a tag; and colours for a light background. Guessed: the folder, `Editors/`, at the repository's root, a folder for each editor; and the colours. The Language Reference, the WASP User Manual and `Documents/implementation.md`, section 3, point to it.

Done (Claude, 2026-11-16, `277f101`): Notepad++'s syntax colouring for `.wl` and `.wasp`, tried with Notepad++ 8.9.8 from a separate settings folder, on the example site's pages, on Windows. Main's checks: syntax files and documentation only, the solution unchanged since its last checks (the Release build, without warnings, and 3,026 tests, on Windows).

## [DONE] Create Syntax Highlighting for VS Code

Self-explanatory.

2026-11-16 (Claude): `Editors/VSCode`, an extension of VS Code, in source: two languages, WebLingo for `.wl` and WASP for `.wasp`, each with a TextMate grammar and a language configuration. WebLingo's grammar gives its keywords, declarations, constants, built-in classes and functions, calls, strings with their escapes and interpolations, numbers, comments and operators the scopes themes know; WASP's is VS Code's grammar for HTML that others embed (`text.html.derivative`), with an injection finding a page's blocks anywhere, in a tag, an attribute's value or a script, `<%# %>` a comment, and a `%>` in a block's string or comment not ending it. `Editors/README.md` says how to install it (**Developer: Install Extension from Location…**). Guessed: the extension's name and publisher, `weblingo`, its version, 0.1.0, and the scopes; it has no license metadata, as the repository has none.

Done (Claude, 2026-11-16, `d54aa3f`): VS Code's syntax highlighting for `.wl` and `.wasp`, tried with VS Code 1.146 on Windows: tokenized with VS Code's own TextMate engine, and shown in a separate profile, in a light and a dark theme; the comment toggle and `<%`'s closing `%>` configured, not tried by typing. Main's checks: editor files and documentation only, the solution unchanged since its last checks (the Release build, without warnings, and 3,026 tests, on Windows).

## [DONE] WASP.config

`weblingo.config` should really be `WASP.config`. We are configuring the WASP site, not the language.

2026-11-16 (Claude): `weblingo.config` is read in two places: by `wasp`, from a site's root (`"environment"`, `"sendTimeout"`, `"schemes"`), and from a page's folder for `wasp run` and `wasp check`; and by `weblingo run` and `weblingo check`, from a script's folder, for its `"schemes"`, the folders of native modules. One decision, answerable with a letter:

Q1, what configures a script's folder, for `weblingo`: (a) `weblingo.config`, still, `WASP.config` being what configures a site, and a page's folder, for `wasp` only; (b) `WASP.config` too, one name for both, `weblingo.config` no longer read anywhere.

A1: (a), in the chat, 2026-11-16.

Either way, as the renames before it: a site's `weblingo.config` no longer read, nothing kept for backward compatibility; the name exactly `WASP.config`, which matters on Linux; and neither name ever served from a site's root.

TODO: For Linux's sake, change it to `wasp.config`.

2026-11-16 (Claude): A1 recorded as approved in the specification, sections 1.1 and 6.12, and built, first as `WASP.config` (`94993ee`), then, as the TODO above asks, in lower case: a site's configuration, in its root, and a page's folder's under `wasp run` and `wasp check`, is `wasp.config` (`WebLingoRouteTable.ConfigurationFile`, `WebLingoCommandLineProgram.Configuration`), a script's folder's under `weblingo` `weblingo.config`, `wasp` reading no `weblingo.config`. The specification (sections 3.9, 3.10, 6.9, 9 and 15.2), the WASP User Manual, the READMEs and `Documents/implementation.md` say so. Guessed, and recorded as Proposed (section 15.2, "The host's core"): neither name served from a site's root, nor `web.config`, whatever their names' case, since Windows reads `WASP.config` as `wasp.config`.

Done (Claude, 2026-11-16, `94993ee` and `e838ba0`): a site's configuration named `wasp.config`. Main's checks passed: the Release build, without warnings, 3,030 tests, `wasp serve` with `weblingo.config`, then `wasp.config`, and `weblingo` and `wasp` on the samples, on Windows only; Linux and IIS not tried.

## [DONE] Dates and Times

A page cannot show the date or the time: `<%= DateTime.Now %>` is WL2001, `'DateTime' is not defined`, since WebLingo has no dates or times yet (the Language Reference, "What is not in the language yet"), and nothing in the standard library gives the time. Add them: the current date and time, the server's, and dates and times as values a script can hold, compare, format, parse and do arithmetic with. Asked by the owner in the chat, 2026-11-16; added by Claude the same day.

What bears on it: the platform layer's clocks (specification, section 7.6, WL-P006): a wall clock for timestamps, apart from the monotonic one the scheduler has (`IWebLingoExecutionClock`), time zones and calendars as services of their own, and a clock a test can set; the portable core uses only what `Documents/NativeCompatibility.md` allows, so the time and the zones come through the platform layer; COM's `VT_DATE` comes back as an ISO 8601 string until WebLingo has dates (section 3.10); and the WASP User Manual's "What is not there yet" should say that dates and times are not there, until they are.

One decision each, answerable with a letter:

Q1, what a date and time is in a script: (a) a kind of value of its own, immutable, built into both VMs, as strings and bytes are, its class `DateTime`; (b) a native class of a standard module, `std:time`, as `std:fs`'s `File` is; (c) a class written in WebLingo, holding a count of time since an epoch, as `BytesBuilder` is written in WebLingo.

A1: (a)

Q2, what it holds: (a) an instant only, as JavaScript's `Date` does, shown in UTC or in a zone only when it is formatted or read; (b) an instant with its offset from UTC, as .NET's `DateTimeOffset` does, so that the server's time keeps the server's offset; (c) kinds of their own, an instant, a date, a time of day, and a date and time without a zone, as Java's `java.time` has them.

A2: (b)

Q3, its precision: (a) milliseconds, as JavaScript's; (b) 100 nanoseconds, as .NET's ticks; (c) microseconds.

A3: (a)

Q4, time zones: (a) UTC and the server's own zone only, for now; (b) any zone by its IANA name, `"Europe/Warsaw"`, from the platform's database, through the platform layer.

A4: (a)

Q5, how it is formatted and parsed: (a) with .NET's date and time format strings, standard and custom, with the invariant culture, in an interpolation too, `{d:yyyy-MM-dd HH:mm}`, as numbers' formats are (section 3.5), the subset in use written into the specification; (b) ISO 8601 only, other text built from its members, `d.year`, `d.month` and so on.

A5: (a)

Q6, what `write` and `<%= %>` write: (a) ISO 8601, `2026-11-16T14:30:00Z`; (b) its class's name and that, `DateTime:2026-11-16T14:30:00Z`, as a classed value is written (section 3.7).

A6: (b)

Q7, how time is added and measured: (a) a duration of its own, `Duration`, `d + Duration.hours(2)`, and `b - a` a `Duration`; (b) methods and numbers, `d.addHours(2)`, and `b - a` a number of milliseconds.

A7: (b)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.2, and built, on both VMs: `DateTime`, a kind of value held in the value itself, its instant and its offset sharing its 64 bits; `DateTime.now()`, `utcNow()`, `local(…)`, `utc(…)`, `parse(text, format)` and `fromUnixMilliseconds(ms)`; a date's properties and its `add…`, `toUtc()`, `toLocal()` and `format(format)`; `==` and the orderings by instant, `b - a` in milliseconds; its text `DateTime:` and ISO 8601's. The time is the platform's wall clock, and the server's zone a new platform service, `IWebLingoTimeZone` (`DotNetTimeZone`), which `weblingo`, `wasp` and the web host give; `PortabilityTests` keeps .NET's clock and `TimeZoneInfo` out of the portable core. Guessed, and recorded as Proposed (section 15.2, "Dates and times"): the members' names; the years 1 to 9999 and a WL3001 beyond them; `==` by instant, as .NET's `DateTimeOffset`; `add…` keeping the offset, as .NET does; ISO 8601's forms `parse` reads, and a time without a zone the server's; dates not map keys; `new DateTime()` WL3001; and, without a host's clock, `now()` WL3001 and the zone UTC. Described in the Language Reference (section 4, "Dates and times") and the WASP User Manual (section 3).

Done (Claude, 2026-11-16, `c5d970c`): dates and times. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted change to `Examples/example1/includes/footer.wasp` (`<%=DateTime.Now%>`, a member `DateTime` does not have) fails; with the committed footer, served by `wasp` from a copy, every page of the site is a 200; `weblingo` and `wasp` tried with dates, on Windows only; Linux and IIS not tried.

## [DONE] Compiler Error WL4001 When a Page Gives Its Request to Its Layout

Under `wasp serve`, a page that gives its request to its layout,

```html
<% param request; %>
<% layout "/layouts/main.wasp", { title: "Plain", request: request }; %>
<p>no params here</p>
```

its layout declaring `<% param title; param request; %>`, does not compile: `plain.wasp(1,1): error WL4001: Internal compiler error: the generated bytecode failed verification: Instruction 9 (Move) reads register 4, not written on every path to it.` The compiler must never generate bytecode its own verifier rejects: find the cause, fix it, on both VMs where it applies, and test it. The same page declaring `param request; param response;` gives WL2004 instead, `'request' is used by a function called before its declaration has run`, since a layout's values are evaluated before the page's `param` statements have run; which of the two is right for the host's parameters waits for the next task's question, but neither may be an internal error. Found by Claude, 2026-11-16, answering the owner's question in the chat.

2026-11-16 (Claude): the cause: a page's layout statement is an import, and a program's imports ran before the parameters its host gives were made, so that the layout's values read the request before it was made, a WL2004 when the page's body captured it and, when nothing did, a register read before it was written, which the verifier rejected. A program's host parameters are now made before its imports, after the built-in classes, as its other parameters are (`WebLingoBoundVariableDeclaration.IsHostParameter`, `WebLingoLowerer`); tested with a page giving its request to its layout, alone and with its response.

Done (Claude, 2026-11-16, `5a06d7a`): a page gives its request to its layout. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; the committed site, served by `wasp` from a copy, gives every page; `wasp serve` tried on a site of each arrangement, on Windows only; Linux and IIS not tried.

## [DONE] The Request and the Response in Layouts and Components

The owner asked, 2026-11-16, how a page uses the request and the response. A page declares them, `param request;` and `param response;`, and an include in the page sees them, but a layout cannot have them, nor what it includes, such as `Examples/example1`'s footer: a layout's `param request;` is an ordinary parameter, which the page must give, and giving it fails (the task above). A component can be given them, `await renderComponent(path, { request: request })`, its `param request;` being an ordinary parameter too. Tried by Claude with `wasp serve`, 2026-11-16; added at the owner's request the same day.

One decision, answerable with a letter:

Q1, how a layout and a component get the request and the response: (a) as a page does: `param request;` and `param response;` in a layout or a component are the host's, the request's own, without the page giving them; (b) given by the page, as any other parameter is, `{ request: request }`, the page's `param request;` then running before its layout's values are evaluated; (c) both: the host's, unless the page gives another.

A1: (c)

2026-11-16 (Claude): A1 recorded as approved in the specification, sections 1.1 and 6.12, and built: a layout's and a component's `param request;` and `param response;` are module parameters whose default is the host's instance, neither the template parser's check of a layout's values nor the binder's of an import's asking for them; so a layout, and what it includes, read the request; described in the WASP User Manual, section 9. Tested with a layout having the host's request, its include reading it, and a component having the host's, and its caller's.

Done (Claude, 2026-11-16, `5a06d7a`): layouts and components have the request and the response. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; the committed site, served by `wasp` from a copy, gives every page; `wasp serve` tried on a site of each arrangement, on Windows only; Linux and IIS not tried.

## [DONE] Add Missing Features

Add from `weblingo-language-reference.md` the items listed in "What is not in the language yet" each as a separate to-do task here. Enhance the descriptions of these tasks.

2026-11-16 (Claude): each item of the Language Reference's "What is not in the language yet" is a task of its own in `TO-DO.md`, eighteen of them, from `%` to network access, each saying what the specification already decides, what it touches, and its open decisions as questions, so each is marked [QUESTION].

Done (Claude, 2026-11-16): the tasks added to `TO-DO.md`, which is left uncommitted while their questions are open. Main's checks: none needed, nothing of the solution changed.

## [DONE] Standard Library Reference

2026-11-16 (Claude): the task had its title only, read as a reference for the standard library, beside the Language Reference: `Documents/weblingo-standard-library-reference.md`, the built-in functions, every built-in class's members, the standard modules `std:system`, `std:tasks` and `std:fs`, native modules, and the request and the response of WASP's pages, a table and an example for each, every example run with `weblingo`. Linked from the Language Reference, the WASP User Manual, the solution's README and `Documents/implementation.md`, section 2, which says that a change to a built-in class or a standard module updates it in the same change. Guessed: what the title asks for, the document's name, and that the Language Reference keeps its own short section on the standard library, pointing to it.

Done (Claude, 2026-11-16, `d175e0d`): the WebLingo Standard Library Reference. Main's checks: documentation only, the solution unchanged since its last checks.

## [DONE] Remainder: `%`

`a % b`, the remainder of a division, of integers and of numbers, beside `*` and `/` in precedence. Built as every feature is: in the lexer and the parser where it is syntax, the binder and the lowered IR, both VMs or the runtime they share, with its conformance tests on both VMs, and the Language Reference. A number's remainder is IEEE 754's as C# gives it (`5.5 % 2` is `1.5`), and an int64's cannot overflow but for `long.MinValue % -1`, which C# gives as 0. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, the sign of an integer's remainder: (a) the dividend's, as C# and JavaScript give it, `-7 % 3` being `-1`; (b) the divisor's, as Python gives it, `-7 % 3` being `2`.

A1: (a)

Q2, an integer's remainder by 0: (a) WL3001, invalid operation; (b) an error of a code of its own, division by zero, which `/` does not need, since its result is a number (`7 / 0` is `Infinity`).

A2: (b)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1, 3.5 and 8.4, and built on both VMs: a `Percent` token, read after a template block's `%>`; a `Remainder` operation appended to both VMs' instructions; the dividend's sign; by -1 always 0; an integer's by 0 WL3013, `DivisionByZeroError`, a new class of runtime error; a number's IEEE 754's; `static operator %`. Guessed: the class's name, `DivisionByZeroError`, and the code, WL3013, WL3012 being COM's (section 3.10). Tested by the operator cross-check of both VMs against the runtime and by conformance cases; described in the Language Reference and the Standard Library Reference.

Done (Claude, 2026-11-16, `99a84f1`): `%`. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; `weblingo` and `wasp` tried, on Windows only; Linux not tried.

## [DONE] Compound Assignment: `+=` and Its Kin

`x += 1` for `x = x + 1`, its target evaluated once (`a[f()] += 1` calls `f` once), on a variable, a member, an element and a property, with the same errors as the operator and the assignment. Built as every feature is: in the lexer and the parser where it is syntax, the binder and the lowered IR, both VMs or the runtime they share, with its conformance tests on both VMs, and the Language Reference. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, which: (a) `+=`, `-=`, `*=`, `/=` and `%=`, and `??=` with `??`; (b) `+=` and `-=` only.

A1: (a)

Q2, what it is: (a) an expression, giving the value assigned, as in C# (`y = x += 1`); (b) a statement only.

A2: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.5, and built with the compound assignment, `++` and `--`, `??` and `??=`, and `?:` and `if` expressions together, in one change (`89ab22a`): the binder writes each with two new expressions, a conditional and a sequence, and temporaries that hold what is evaluated once, which both code generators emit with the jumps the VMs have, neither VM changed. Guessed, and recorded as Proposed (section 15.2, "Assignment's operators and choosing a value"): `?[` written together always an optional index; an interpolation's conditional in parentheses. Tested by conformance cases on both VMs; described in the Language Reference, section 5.

Done (Claude, 2026-11-16, `89ab22a`): compound assignment. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs gave the same output; the benchmark within the machine's noise; Windows only, Linux not tried.

## [DONE] Increment and Decrement: `++` and `--`

`i++` and `--i`, adding or subtracting 1, an int64's overflow WL3002 as `+`'s is. Built as every feature is: in the lexer and the parser where it is syntax, the binder and the lowered IR, both VMs or the runtime they share, with its conformance tests on both VMs, and the Language Reference. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, what they are: (a) prefix and postfix, as expressions, as in C# (`a[i++]`); (b) statements only, `i++;`, giving no value; (c) none: `i += 1` suffices.

A1: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.5, and built with the compound assignment, `++` and `--`, `??` and `??=`, and `?:` and `if` expressions together, in one change (`89ab22a`): the binder writes each with two new expressions, a conditional and a sequence, and temporaries that hold what is evaluated once, which both code generators emit with the jumps the VMs have, neither VM changed. Guessed, and recorded as Proposed (section 15.2, "Assignment's operators and choosing a value"): `?[` written together always an optional index; an interpolation's conditional in parentheses. Tested by conformance cases on both VMs; described in the Language Reference, section 5.

Done (Claude, 2026-11-16, `89ab22a`): `++` and `--`. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs gave the same output; the benchmark within the machine's noise; Windows only, Linux not tried.

## [DONE] The Conditional Operator: `c ? a : b`

A choice between two values by a Boolean, which the parser tells apart from `?[` and `?.` (specification, section 3.6), the condition a Boolean or WL3001, as `if`'s is, only the chosen value evaluated. Built as every feature is: in the lexer and the parser where it is syntax, the binder and the lowered IR, both VMs or the runtime they share, with its conformance tests on both VMs, and the Language Reference. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, its form: (a) C#'s, `c ? a : b`, right-associative, looser than `||` and tighter than assignment; (b) an `if` expression, `if (c) a else b`, as Kotlin's.

A1: (a) and (b), both.

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.5, and built with the compound assignment, `++` and `--`, `??` and `??=`, and `?:` and `if` expressions together, in one change (`89ab22a`): the binder writes each with two new expressions, a conditional and a sequence, and temporaries that hold what is evaluated once, which both code generators emit with the jumps the VMs have, neither VM changed. Guessed, and recorded as Proposed (section 15.2, "Assignment's operators and choosing a value"): `?[` written together always an optional index; an interpolation's conditional in parentheses. Tested by conformance cases on both VMs; described in the Language Reference, section 5.

Done (Claude, 2026-11-16, `89ab22a`): `c ? a : b` and `if (c) a else b`. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs gave the same output; the benchmark within the machine's noise; Windows only, Linux not tried.

## [DONE] Null Coalescing: `??`

`a ?? b`, `a` unless it is `null`, `b` evaluated only then, looser than `||`, as in C#; with `?.` and `?[`, `user?.name ?? "stranger"`. Built as every feature is: in the lexer and the parser where it is syntax, the binder and the lowered IR, both VMs or the runtime they share, with its conformance tests on both VMs, and the Language Reference. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, `??=`: (a) with `??`, `x ??= v` assigning `v` only when `x` is `null`; (b) not now.

A1: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.5, and built with the compound assignment, `++` and `--`, `??` and `??=`, and `?:` and `if` expressions together, in one change (`89ab22a`): the binder writes each with two new expressions, a conditional and a sequence, and temporaries that hold what is evaluated once, which both code generators emit with the jumps the VMs have, neither VM changed. Guessed, and recorded as Proposed (section 15.2, "Assignment's operators and choosing a value"): `?[` written together always an optional index; an interpolation's conditional in parentheses. Tested by conformance cases on both VMs; described in the Language Reference, section 5.

Done (Claude, 2026-11-16, `89ab22a`): `??` and `??=`. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs gave the same output; the benchmark within the machine's noise; Windows only, Linux not tried.

## [DONE] Comparing Strings with `<`

`<`, `<=`, `>` and `>=` between two strings, which take numbers only today (specification, section 3.5: "Strings may be added to them later"), never by culture (`Documents/implementation.md`, section 7); a string and a number still WL3001. Two strings' `==` is unchanged. Built as every feature is: in the lexer and the parser where it is syntax, the binder and the lowered IR, both VMs or the runtime they share, with its conformance tests on both VMs, and the Language Reference. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, their order: (a) by Unicode scalar values, as a string's length and indexing count them; (b) by UTF-16 code units, as .NET's ordinal comparison does, which orders some characters beyond U+FFFF before others below it.

A1: (a)

Q2, a method too: (a) `a.compare(b)`, giving -1, 0 or 1, for sorting; (b) not now.

A2: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.5, and built: two strings compared by their Unicode scalar values (`WebLingoOperators.CompareStrings`), and `s.compare(other)`; a comparison of other kinds now names every kind it takes, and both operands. Tested by conformance cases on both VMs; described in the Language Reference and the Standard Library Reference.

Done (Claude, 2026-11-16, `e3421f0`): strings compared with `<`, and `compare()`. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; Windows only, Linux not tried.

## [DONE] `switch`

A choice among cases by a value, with no implicit fallthrough (specification, section 3.2), its cases matched as `==` matches, a `break` leaving it. Built as every feature is: in the lexer and the parser where it is syntax, the binder and the lowered IR, both VMs or the runtime they share, with its conformance tests on both VMs, and the Language Reference. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, its form: (a) C#'s statement, `switch (x) { case 1: case 2: … break; default: … }`, a section that does not end in `break`, `return`, `throw` or `continue` a compile-time error; (b) cases with blocks and no `break`, `switch (x) { case 1, 2 { … } default { … } }`.

A1: (b), break is unnecessary.

Q2, an expression too: (a) C#'s `x switch { 1 => "one", 2 => "two", _ => "many" }`; (b) not now.

A2: (a)

Q3, how a case matches: (a) by `==` only; (b) by a class too, `case Point p:`, as a typed catch does.

A3: (b)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.5, and built on both VMs, with no instruction of their own: the statement as the `if`s it means, the expression as the conditionals it means. Guessed, and recorded as Proposed (section 15.2, "`switch`"): `switch`, `case` and `default` words of their own only where a `switch` stands; `break` and `continue` in a case acting on the loop around it, as in an `if`, since a case needs no `break`; a switch expression needing a `_` arm; a class's pattern standing alone. Tested by conformance cases; described in the Language Reference, section 7.

Done (Claude, 2026-11-16, `4d3355b`): `switch`. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs gave the same output; Windows only, Linux not tried.

## [DONE] Decimals: `1.5m`

Exact decimal arithmetic, a kind of value of its own, approved as decimal128 with the literal suffix `m`, mixing with binary64 only by an explicit conversion (specification, sections 3.2 and 1.1, M0.1), its encoding IEEE 754 BID, still Proposed (section 5.3, and open question 3). Its class `Decimal`, its text, its arithmetic and comparisons, its formats, and WLBC's constants. Built as every feature is: in the lexer and the parser where it is syntax, the binder and the lowered IR, both VMs or the runtime they share, with its conformance tests on both VMs, and the Language Reference. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, how it is built: (a) a decimal128 of WebLingo's own, 34 digits, in the core, as approved; (b) .NET's `decimal`, 28 or 29 digits, until Native C#, which would change the approved decimal128 for now.

A1: (b)

Q2, an integer with a decimal: (a) converted exactly, `1.5m + 1` being `2.5m`; (b) WL3001, as a number with a decimal is.

A2: (a)

Q3, its text: (a) its digits as written, `1.50m` written `1.50`, as decimal128 and C#'s `decimal` keep them; (b) the fewest digits, `1.5`.

A3: (b) `1.5` written as `1.5m`.

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.2, the change from decimal128 to .NET's `decimal` for now noted in the register's M0.1 row and open question 3, and built on both VMs: a literal, a constant of its own kind in modules and WLBC, a kind of value in the heap, its class `Decimal`, arithmetic, comparisons, text and formats. Guessed, and recorded as Proposed (section 15.2, "Decimals"): `new Decimal(x)` of a number converted as .NET converts it; `new Integer(d)` only of a whole decimal; `Decimal.parse` reading a sign, digits and a point; a decimal never equal to a binary64; decimals no key of a map. The inventory lists `System.Decimal` as Planned. Described in the Language Reference and the Standard Library Reference.

Done (Claude, 2026-11-16, `afa79ca`): decimals. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs and WLBC; Windows only, Linux not tried.

## [DONE] Time Zones Other Than UTC and the Server's

A date in any time zone, by its IANA name, `"Europe/Warsaw"`, beside UTC and the server's own, which is all dates have today (the owner's answer, 16 November 2026, specification, section 3.2, "Dates and times"), through the platform layer's time zones (section 7.6). From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, where the zones come from: (a) the platform's (Windows' and ICU's, through .NET), which may differ from one server to another; (b) a time zone database shipped with WebLingo, the same everywhere.

A1: (a)

Q2, how a script names one: (a) `d.toZone("Europe/Warsaw")` and `DateTime.inZone("Europe/Warsaw", year, month, day, …)`; (b) a class, `TimeZone.find("Europe/Warsaw")`, `d.toZone(zone)`.

A2: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.2, and built in the runtime both VMs share: `IWebLingoTimeZone.Find(name)`, `DotNetTimeZone` asking .NET; `d.toZone(name)` and `DateTime.inZone(name, year, month, day, …)`. Guessed, and recorded as Proposed (section 15.2, "Time zones"): a name looked for only when written as IANA writes them, so that Windows' own names are not, and none reaches the platform as a path; a skipped or repeated time taking the zone's standard offset, as the server's does; a date keeping its offset, not its zone, when moved; WL3001 for an unknown name and under a host without a zone. The inventory's row for .NET's clock and zones names `TimeZoneInfo.TryFindSystemTimeZoneById`. Described in the Language Reference and the Standard Library Reference, their examples run with `weblingo`.

Done (Claude, 2026-11-16, `0d15d6d`): time zones. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail (`DateTime.Now`, a member dates do not have: `DateTime.now()`), the committed site serving its pages; both VMs; Windows only, Linux not tried.

## [DONE] Hexadecimal and Binary Literals

`0x1F` and `0b1010`, int64s, in the lexer, which reads decimal literals only today. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, which: (a) `0x` and `0b`, their letters in either case; (b) octal too, `0o17`.

A2: (a)

Q2, one beyond int64, `0xFFFFFFFFFFFFFFFF`: (a) WL1004, as a decimal literal beyond int64 is; (b) its bits, `-1`, as C#'s `unchecked` gives them.

A2: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.5, and built in the lexer: `0x`/`0X` and `0b`/`0B`, int64s. Guessed, and recorded as Proposed (section 15.2, "Hexadecimal and binary literals"): a prefix without digits, or digits running into a letter, a digit or `_`, one invalid literal (WL1004); the message for one beyond int64 naming the largest in decimal. The editors' colouring (Notepad++ and VS Code) reads them, and the decimal's `m`, which the decimals' task had left out. Described in the Language Reference.

Done (Claude, 2026-11-16, `e4ead72`): hexadecimal and binary literals. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs and WLBC; Windows only, Linux not tried.

## [DONE] Digit Separators: `_`

`1_000_000`, `0.000_1` and `1e1_0`, the `_` ignored, in the lexer. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, where: (a) between digits only, one or more, as C# allows them, never first, last, or beside a `.` or an `e`; (b) between digits only, one at a time.

A1: (b)

2026-11-16 (Claude): the answer recorded as approved in the specification, sections 1.1 and 3.5, and built in the lexer, for decimal, hexadecimal and binary digits, fractions and exponents, and decimals. Guessed, and recorded as Proposed (section 15.2, "Digit separators"): a `_` that is not between two digits of the literal's base makes the literal run into it, one invalid literal (WL1004), as digits running into letters do; a token's text keeps them, the value and a decimal's constant do not. The editors' colouring reads them. Described in the Language Reference, whose list of what is not in the language yet no longer names literals.

Done (Claude, 2026-11-16, `ad83018`): digit separators. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs and WLBC; Windows only, Linux not tried.

## [DONE] Rest Parameters

A function taking any number of arguments, the last parameter an array of those left (specification, section 3.2: "Functions have defined arity/default/rest behavior"), its arity checked for the others (WL2006). Built as every feature is: in the lexer and the parser where it is syntax, the binder and the lowered IR, both VMs or the runtime they share, with its conformance tests on both VMs, and the Language Reference. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, its form: (a) JavaScript's, `function f(first, ...rest)`; (b) C#'s, `function f(first, params rest)`.

A: (a) 

2026-11-16 (Claude): the answer recorded as approved in the specification, sections 1.1 and 3.5, and built through the lexer, the parser, the binder, the IR, both code generators, WLBC, both verifiers and both VMs. Guessed, and recorded as Proposed (section 15.2, "Rest parameters"): a rest parameter after any with defaults, without a default of its own, the last; a new array each call; WL2006 "takes at least N arguments", at compile time for a function called by its name; in the bytecode, a function counting only the parameters before it, `HasRest` (WLBC's function flag 16), and a new instruction of both VMs, `TakeRest`, its first, which takes the array a call with more arguments than its parameters gathered on the VMs' existing slow path, so that the fast call path is as it was: a first build that looked for a rest parameter on every call made the stack VM's `objects` 2.5% slower, and the landed one measured within the machine's noise against `main` (`objects` and `calls`, both VMs, five rounds alternately, and the stack VM's `calls` seven more). Described in the Language Reference, its example run with `weblingo`; the working copy's other, uncommitted edit to that file, in the `if` example's output, is not in the commit.

Done (Claude, 2026-11-16, `b74d9d5`): rest parameters. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs and WLBC; Windows only, Linux not tried.

## [DONE] Spreading into a Call

`f(...args)`, an array's elements as a call's arguments. Built as every feature is: in the lexer and the parser where it is syntax, the binder and the lowered IR, both VMs or the runtime they share, with its conformance tests on both VMs, and the Language Reference. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, where: (a) into calls only; (b) into calls and array literals, `[...a, ...b]`; (c) into map and set literals too, `{ ...defaults, ...given }`.

A1: (c)

Q2, what may be spread: (a) arrays only; (b) anything `foreach` visits.

A2: (b)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.5, and built through the parser, the binder, the IR, both code generators and both VMs. Guessed, and recorded as Proposed (section 15.2, "Spreading"): spread elsewhere, and into `Module.import` and a component's render, WL1001; into a map, maps only, a later key replacing an earlier one; a value that cannot be spread WL3001; a spread call's arity checked when it runs, at most 65,535 arguments (WL3001). As built: literals through a hidden class written in WebLingo, whose functions are native so that an error is reported at the spread; calls through three new instructions of each VM, CallSpread, CallMethodSpread and NewSpread, which put the array's elements where the call takes its arguments, growing the frame where needed, as the stack VM already grows its slots for an operator's call, then run the call's own case by `goto case` on a rewritten instruction; values put beyond a function's own registers or slots stay in its frame's array until overwritten, keeping them alive meanwhile, as an operator's operands already are. The benchmark's workloads but `io`, both VMs, against `main`, alternately, were within the machine's noise. Described in the Language Reference, its example run with `weblingo`; implementation.md's trap about the dispatch method records what rest parameters and spreading measured.

Done (Claude, 2026-11-16, `ce836fe`): spreading. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs and WLBC; Windows only, Linux not tried. Seen meanwhile: `WebLingo.Language.Tests` reports a varying number of tests from run to run of the same build (233 to 241), random ones missing; not caused by this task, and not yet looked into.

## [DONE] String Methods

Searching, splitting, trimming and case, which strings lack beyond `length`, indexing and `iterate()`: a proposed first set, `indexOf`, `lastIndexOf`, `contains`, `startsWith`, `endsWith`, `slice(start, end)`, `split(separator)`, `trim`, `trimStart`, `trimEnd`, `toUpper`, `toLower`, `replace(old, new)`, every one replaced, `repeat(n)`, `padStart(width, pad)` and `padEnd`; and `separator.join(array)` or `array.join(separator)`; positions in Unicode scalar values, as indexing counts them; in the runtime, on both VMs. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, their names: (a) C#'s, in lower camel case, `contains`, `toUpper`, as the proposed set has them; (b) JavaScript's, `includes`, `toUpperCase`.

A2: Provide aliases, for example `contains` == `includes`, `toUpper` == `toUpperCase`.

Q2, case: (a) the invariant culture's, as .NET's `ToUpperInvariant` gives it, the same on every server; (b) Unicode's own simple case mapping, in the core, so that Native C# needs no culture data.

A2: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.5, and built in the runtime both VMs share. Guessed, and recorded as Proposed (section 15.2, "String methods"): ordinal searching; `indexOf`'s optional `from` and `slice` strict, as bytes' `slice` is, WL3005 outside the string, never counting from its end; `lastIndexOf` without a `from`; `split("")` into scalar values, empty parts kept; .NET's white space for `trim`; case per UTF-16 code unit (`"Straße"` stays `"STRAßE"`); `replace` of an empty string WL3001; a result too large WL3003 before it is built (a new `WebLingoHeap.TryMakeRoom`); `separator.join(array)` of strings, numbers, Booleans, decimals, dates and `null` (as nothing), anything else WL3001; JavaScript's `substring` left out, its meaning differing from C#'s; an array's `join(separator)` left to the array methods. The inventory lists the .NET string methods used, Planned. Described in the Language Reference, its example run with `weblingo`, and the Standard Library Reference.

Done (Claude, 2026-11-16, `0ea51e7`): string methods. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs and WLBC; Windows only, Linux not tried.

## [DONE] Array Methods

Arrays have `push`, `pop`, `length`, indexing and `iterate()` only: a proposed first set, `insert(i, v)`, `removeAt(i)`, `indexOf(v)`, `contains(v)`, `slice(start, end)`, `concat(other)`, `join(separator)`, `reverse()`, `sort(compare)`, `map(f)`, `filter(f)`, `reduce(f, initial)`, `find(f)`, `any(f)` and `all(f)`; in the runtime, on both VMs, a function argument called as the VMs call any function, `await`ed where it is `async`. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, their names: (a) as the proposed set has them, `contains`, `any`, `all`, `map`, `filter`; (b) JavaScript's, `includes`, `some`, `every`; (c) C#'s LINQ's, `contains`, `any`, `all`, `select`, `where`.

a1: (a) but provide aliases whenever possible so that both C# developers and JS devs feel at home.

Q2, `sort()` and `reverse()`: (a) change the array, as JavaScript's do; (b) give a new one, the array unchanged.

A2: (b) with a compiler warning for a discarded result.

Q3, `sort()` without a function: (a) integers and numbers by value, strings by `<` (the task above), anything else, or a mix, WL3001; (b) by their text, as JavaScript's.

A3: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.6, and built: the methods that take no function in the runtime, those that take one in WebLingo, an extension of `Array` the compiler adds where a program names one. Guessed, and recorded as Proposed (section 15.2, "Array methods"): `insert` and `removeAt` changing the array, a position outside it WL3005; `indexOf` and `contains` by `==`'s own comparison; `slice` strict; `concat` of an array; `join`'s separator a comma unless given; `reduce`'s initial value required; callbacks given the element alone; an `async` callback awaited, the method then giving a task; `sort(compare)` stable, its compare never awaited, without one by `<`; aliases `includes`, `select`, `where`, `some` and `every`, not `splice`, `Aggregate` or `First`; the warning WL2017 for `x.sort();` or `x.reverse();` whatever `x` is, the compilation now stopping for the binder's errors only. Described in the Language Reference, its example run with `weblingo`, and the Standard Library Reference.

Done (Claude, 2026-11-16, `f5e9047`): array methods. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs and WLBC; Windows only, Linux not tried.

## [DONE] Regular Expressions

Matching, finding and replacing by a pattern, with a limit on time against patterns that backtrack without end. The portable core cannot rely on .NET's regular expressions under Native C# without an engine of its own, which `Documents/NativeCompatibility.md` would list. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, the syntax: (a) .NET's, its engine used now, Native C# then needing one that reads the same; (b) ECMAScript's, a subset of .NET's (`RegexOptions.ECMAScript`), smaller to build for Native C#.

A1: (a)

Q2, how a script writes one: (a) a class only, `new Regex("a+b", "i")`, `r.test(s)`, `r.match(s)`, `s.replace(r, …)`; (b) a literal too, `/a+b/i`, which the lexer tells apart from `/`.

A2: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.6, and built as a kind of value of its own, its class `Regex`, in the runtime and both VMs. Guessed, and recorded as Proposed (section 15.2, "Regular expressions"): the flags `i`, `m`, `s`, `x` and `n`; the invariant culture, never `RegexOptions.Compiled`; a match as an array of its groups' text, the whole match first, an unmatched group `null`; aliases `isMatch`, `exec` and `matches`; .NET's substitutions in replacements and its splitting; a second for each operation, then a `TimeoutError` (WL3010, catchable); text `Regex:/pattern/flags`; identity for `==`; no map key. The inventory lists `System.Text.RegularExpressions` as Planned. The editors' colouring knows `Regex`, and `DateTime`, `Decimal` and `DivisionByZeroError`, which earlier tasks had left out. Described in the Language Reference, its example run with `weblingo`, and the Standard Library Reference, in a section of its own, the sections after it renumbered.

Done (Claude, 2026-11-16, `09f4e36`): regular expressions. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs and WLBC; Windows only, Linux not tried.

## [DONE] JSON

Reading and writing JSON, which a route handler builds as text today (the WASP User Manual, section 10): objects as maps, arrays as arrays, strings, `true`, `false` and `null`, and text that is not JSON an error of its own or WL3001. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, where: (a) a standard module, `std:json`, `json.parse(text)` and `json.stringify(value, indent)`; (b) a built-in class, `Json.parse(text)` and `Json.stringify(value)`.

A1: (b)

Q2, its numbers: (a) an integer when it has no fraction or exponent and fits int64, a number otherwise; (b) always a number, binary64, as JavaScript reads them.

A2: (a)

Q3, an instance written: (a) its fields, as `write` writes them, a class's `toJson()` replacing them; (b) WL3001 unless its class has a `toJson()`.

A3: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 3.6, and built: `Json.parse` in the runtime, `Json.stringify` through a new instruction of both VMs, `ToJson`, which calls a class's `toJson()` as `ToText` calls a `toString()`. Guessed, and recorded as Proposed (section 15.2, "JSON"): an optional indent, 0 to 10 spaces; integer map keys written as their digits, others WL3001; sets as arrays, decimals without their `m`, dates as ISO text, classes of values as what they hold; a number written as `write` writes it, `5.0` reading back as a number; a number too large for a binary64 read as its infinity; nesting at most 512 deep; every error WL3001, with a line and a column for text that is not JSON. The WASP User Manual's route handler now writes its JSON with `Json.stringify`, its output checked with `weblingo`, not served; the editors know `Json`. Described in the Language Reference, its examples run with `weblingo`, and the Standard Library Reference, in a section of its own, the sections after it renumbered.

Done (Claude, 2026-11-16, `de93ff5`): JSON. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs and WLBC; Windows only, Linux not tried.

## [DONE] Network Access

Reaching other servers, through the platform layer's networking (specification, section 7.9, WL-P009), its capabilities checked where the connection is made (section 7.13), asynchronous, on WebLingo's own operations. From the Language Reference's "What is not in the language yet" (section 17); added by Claude, 2026-11-16, at the owner's request ("Add Missing Features").

One decision each, answerable with a letter:

Q1, what first: (a) an HTTP client, `std:http`, `await http.get(url)` and `await http.post(url, body, headers)`, giving the status, the headers and the body; (b) sockets too.

A1: (b)

Q2, which hosts a script may reach: (a) any under `weblingo`, and under `wasp` only those `wasp.config` allows, `"network": { "allow": ["api.example.com"] }`, as COM's objects are allowed; (b) any, everywhere, for now.

A2: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 7.9, and built: the platform's network, `IWebLingoNetwork`, `DotNetNetwork` on .NET, which checks a host where the connection is made; `std:http`, `get`, `post` and `request`, each a task of a map of the status, the headers, the body and its bytes; and `std:net`, `Socket.connect(host, port)`, `read()`, `write(data)` and `dispose()`, both native modules installed with the runtime, each operation off the worker. Guessed, and recorded as Proposed (section 15.2, "Network access"): headers' names in lower case, a header sent more than once joined by `, `; a redirect given, not followed; 30 seconds to connect, 100 in all, a body at most 64 MB; a read at most 64 KB; a host not allowed, or an address not `http` or `https`, WL3001; a failure a new class, `NetworkError`, with a new code, WL3014 (Proposed); a host compared by its name or address as written, ignoring case, at any port; under `wasp`, none without the setting; the addresses a name resolves to not checked, so DNS rebinding is still open (section 7.9). Tested against a local server of the tests' own, and by hand against one on this machine, with `weblingo`, and with `wasp run` for the allow list. Described in the Language Reference, the Standard Library Reference, sections of their own, the sections after them renumbered, and the WASP User Manual's `"network"`; their examples run with `weblingo` against a server on this machine; the editors know `NetworkError` and `Socket`.

Done (Claude, 2026-11-16, `c8cc3fe`): network access. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs; Windows only, Linux not tried.

## [DONE] Database Access: SQLite

Create `example2` that illustrates the use of a database. Use SQLite with a database of tropical fish, and the example being of a tropical fish store site. Obtain free images of fish (with provenance/license) and scale them to the same size, e.g. 1024 x 768.

2026-11-16 (Claude): WebLingo has no database access yet: the specification proposes provider-neutral connections, parameters, transactions, cancellation, streamed rows and disposal, SQL Server and SQLite first, through ADO.NET (section 7.11, WL-P011, its API "not yet frozen"). This machine has neither SQLite's tools nor a SQL Server, nor their .NET packages, which a new dependency would bring (`AGENTS.md`, sections 9 and 13). One decision each, answerable with a letter:

Q1, how a script reaches a database: (a) one standard module for every database, `std:db`, `var db = await Database.open("fish")`, `await db.query(sql, values)` giving an array of maps, one for each row, `await db.execute(sql, values)` giving the rows changed, `await db.transaction(async () => { … })`, and `using`, as the specification's example has it; (b) a module for each database, `std:sqlite` and `std:sqlserver`, each with its own API.

How (a) would still reach what one database has and another has not (Claude, 2026-11-16, at the owner's request), as JDBC, ADO.NET, PHP's PDO and Go's `database/sql` do:

- The SQL text carries most of the differences: `std:db` passes it on unchanged, translating no dialect, so that `TOP` or `LIMIT`, `OUTPUT` or `RETURNING`, JSON operators and window functions are each database's own SQL.
- Which database, and how it is reached, is the host's configuration, not the script's (Q3): `Database.open("fish")` names a configured connection, so that credentials never appear in a script or its output, the host grants a script a database by its name (section 7.13, capabilities), and one script can run on SQLite in development and SQL Server in production, where its SQL allows. HOWEVER, there has to be a way of specifying that in script, too, like in classic ASP.
- A script may ask what it is talking to, `db.provider` (`"sqlserver"`, `"sqlite"`) and `db.serverVersion`, and choose its SQL by it.

- What is common but tunable is an options map, so that the API's shape stays fixed: `db.query(sql, values, { timeout: 30 })`, `db.transaction(async () => { … }, { isolation: "serializable" })`; an option a provider does not support is a runtime error, never silently ignored.
- What only one database has (SQL Server's bulk copy, SQLite's own functions, PostgreSQL's `LISTEN`) is a provider's extension module, taking the common `db`: `import { bulkCopy } from "std:db/sqlserver"; await bulkCopy(db, "orders", rows);`, an error on another provider's `db`. Code that imports only `std:db` stays portable, and an import of an extension module marks, where it can be seen, the code that is not; a host may grant an extension module apart from `std:db`; and the extension reaches its provider's .NET connection through the instance's host object (Q1 of "Native Modules, Part 2: Native Classes", if (a)), which no script sees.
- Left for questions of their own: placeholders (Q2); how each database type becomes a WebLingo value (decimals, dates and times, bytes, a provider's own types such as a geography, by its text); a cursor for results too large to hold whole, `for await (row of db.rows(sql, values))`, later, as a request body's streaming; and connection pooling, the host's, for each configured connection, which `using` and transactions must agree with.

(b) fits each module's API to its database exactly, but shares nothing: a library for any database adapts to each module itself, and each module repeats transactions, parameters, pooling, `using` and the mapping of types.

A1: (a). 

Q2, how a statement takes values: (a) named, `@name`, from a map, `db.query("SELECT * FROM Fish WHERE Price < @max", { max: 10 })`, as the specification's example has it; (b) by position, `?`, from an array.

A2: (a)

Q3, how a site names its databases: (a) in `wasp.config`, `"databases": { "fish": "sqlite:data/fish.db" }`, a script naming one by its name, and `weblingo.config` the same for scripts; (b) a connection string in the script.

A3: (a) and (b), both.

Q4, where a site's SQLite database file is: (a) a folder of the site's, `data/`, which `wasp` never serves, as it never serves `wasp.config`; (b) outside the site's folder, as the IIS logs are.

A4: (b), specified in the config.

Q5, the packages: (a) ADO.NET's providers, Microsoft.Data.Sqlite (MIT, with SQLitePCLRaw and the native SQLite it brings) and Microsoft.Data.SqlClient (MIT), in a new adapter project, `WebLingo.Data.DotNet`, outside the portable core, the dependencies listed in the task's notes; (b) another way, which you name.

A5: (a)

2026-11-16 (Claude): waiting for two things before the work can start. Your go-ahead in chat to reach nuget.org, to restore Microsoft.Data.Sqlite and the SQLitePCLRaw packages it brings, and Wikimedia Commons, to choose the fish photos and download them; the harness refuses both without it, and I will list each file, its source and its size before downloading anything. And one question about A4:

Q6, A4's "or taken from PATH": (a) a database file named without a folder, `"fish": "sqlite:fish.db"`, is looked for in the folders of the `PATH` environment variable, the first that has it, as a program is; (b) something else, which you name.

A6: N/A any more.

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 7.11, and built: the platform's databases, `IWebLingoDatabases`, `DotNetDatabases` on .NET in a new adapter, `WebLingo.Data.DotNet`, SQLite's provider for now; `std:db`, its class `Database`, `open`, `query`, `execute`, `begin`, `commit`, `rollback`, `transaction`, `provider`, `serverVersion` and `dispose`; `"databases"` in `weblingo.config` and `wasp.config`; and `Examples/example2`, Lingo Aquatics, a fish store whose pages read and change a SQLite database outside the site, made by `database/setup.wl`, with fourteen fish from rivers and reefs, ordered in a transaction. `transaction` calls a script's function and awaits it, as a native function cannot, so native classes may now have methods written in WebLingo (`DeclareWebLingoMethods`), read when the module loads. Guessed, and recorded as Proposed (section 15.2, "Database access" and "Native classes' methods written in WebLingo"): a connection string as a provider's name, a colon, and what the provider reads, SQLite's a path, `:memory:` or its own connection string; a relative path from the configuration's folder, or a script's current folder; a file made when it is not there; options `timeout` and `isolation`, any other WL3001; columns declared `DECIMAL`, `BOOL` and `DATE` read as decimals, Booleans and dates; one operation at a time on a connection; transactions not nesting; a script's connection string allowed under `wasp` too; and a database's failure a new class, `DatabaseError`, with a new code, WL3015 (Proposed). Packages, in `WebLingo.Data.DotNet` only: Microsoft.Data.Sqlite and Microsoft.Data.Sqlite.Core 10.0.15 (MIT), and SQLitePCLRaw.bundle_e_sqlite3, .core, .provider.e_sqlite3 and .lib.e_sqlite3 2.1.13 (Apache-2.0), the last holding SQLite (public domain); a published installation is 35 MB, SQLite's native library for every platform included. The photos are from Wikimedia Commons, 1920-pixel renderings, the clownfish's 1280, cropped to 4:3 and scaled to 1024 x 768, each credited in `images/CREDITS.md` and on `/credits`. The example was tried with `wasp serve` in a browser, its pages, an order and a refused one; the documents' examples run with `weblingo`.

Done (Claude, 2026-11-16, `ece0740`): database access, SQLite. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which the working copy's uncommitted edits to `Examples/example1` fail; both VMs; Windows only, Linux not tried.

## [DONE] Built-ins Made Before a Program's Imports

Found by Claude, 2026-11-17, serving example3: the built-ins the compiler writes in WebLingo for a program, the methods of arrays that take a function (`find`, `map`, `filter` and the others, section 3.6) and a native function's optional parameters (`fs.readText(path)`, section 3.9), are made where the program's top level begins, after its static imports have run, where the built-in classes are made before them. So a file a program imports cannot use them at its top level (`export const found = [1, 2].find(x => x == 2);` is WL3006, "An array has no member 'find'"), nor a page's body under a layout, which the layout's import runs (`all.find(…)` in example3's `index.wasp` is WL3006, and `await fs.readText(path)` WL2004, "'readText' of the module std:fs is read before its module has set it"). They are to be made first, with the built-in classes, on both VMs, the lowered IR being theirs alike.

2026-11-17 (Claude): built on its own branch, `claude/builtins-before-imports`, in a worktree of its own, `main`'s copy without your uncommitted edits: `WebLingoBoundProgram.MadeFirst` holds the built-ins the compiler writes in WebLingo for a program, a native function's optional parameters and the methods of arrays that take a function, which `WebLingoLowerer.LowerBlock` now makes after the built-in classes and before the program's prologue and imports, on both VMs, whose lowered IR is the same. Tests: an imported module's top level using `std:fs`'s `readTextSync`, `find` and `map`, on the register VM and from a stack VM's `.wlbc` (`ConformanceTests.AnImportedModulesTopLevelHasTheBuiltinsWrittenInWebLingo`), and a page's body under a layout using `find`, `map` and spreading, on both VMs (`PageTests.ALayoutWrapsItsPage`), both failing before the fix; a trap in `Documents/implementation.md`, "A Program's Imports Run Before Its Top Level". It changes no VM's dispatch method, so the benchmark was not run.

Done (Claude, 2026-11-17, `90bb4e9`): the built-ins written in WebLingo made before a program's imports. Main's checks: the Release build, without warnings, and the complete test suite, every test passing, on `main`'s own files; `weblingo` on the samples and `wasp` on `samples/SimpleWebsite`; both VMs; Windows only, Linux not tried.

## [DONE] DatabaseAccess: MS SQL Server

2026-11-17 (Claude): A3's (b) is built on `claude/sqlserver` (`c6491e2`, after the rebase): the example and the tests on `localhost`, by TCP/IP, which SqlClient's managed network layer reaches on SQL Server 2025, `TrustServerCertificate=true` needed for the certificate its installation made. Both `SqlServerTests` pass; example3's own test failed on "Built-ins Made Before a Program's Imports" (DONE.md), which it waited for, then the example in a browser, and landing.

2026-11-17 (Claude): built, with A2's (b), on the branch `claude/sqlserver` (`b90e028`, after the rebase), not yet landed: SQL Server's provider, `sqlserver`, through SqlClient's managed network layer, its native one left out, and `Examples/example3`, Lingo To-Do, whose README, `Examples/README.md`, says how to install LocalDB. The Release build has no warnings, and every test passes but example1's, which your uncommitted edits to it fail; the three tests that need LocalDB are skipped. It waits for SQL Server Express LocalDB, which you install (A1): `sqllocaldb` is not on this machine, so neither the example nor whether SqlClient's managed network layer works with LocalDB, which A2's (b) asks me to check first, has been tried. Once it is installed, I run those tests and the example in a browser, then land it.

Create example3 that illustrates the use of a SQLServer database. Create a to-do list database, where items can be added, deleted, edited. Populate it with a few dozen items already.

2026-11-16 (Claude): WebLingo has no database access yet: the specification proposes provider-neutral connections, parameters, transactions, cancellation, streamed rows and disposal, SQL Server and SQLite first, through ADO.NET (section 7.11, WL-P011, its API "not yet frozen"). This machine has neither SQLite's tools nor a SQL Server, nor their .NET packages, which a new dependency would bring (`AGENTS.md`, sections 9 and 13). One decision each, answerable with a letter:

The API, the values, the naming and the packages are the questions of the task above.

Q1, which SQL Server the example runs on, there being none here: (a) SQL Server Express LocalDB, which you install, the example's README saying how, and its database made by a script the example holds, `setup.sql`, with a few dozen items; (b) a SQL Server you already have, named in the example's `wasp.config`, the same script making its database.

A1: (a)

Q2, SqlClient's native part (Claude, 2026-11-16): Microsoft.Data.SqlClient 7.1.2 (MIT) brings 23 more packages, 14.6 MB, all MIT but one, Microsoft.Data.SqlClient.SNI.runtime, its native network layer on Windows, under Microsoft's own licence terms: an application may distribute it, but only within the application, requiring its users to agree to terms that protect it at least as much, and indemnifying Microsoft. (a) take it as it comes, WebLingo's published installation carrying it under those terms, named in its notices; (b) leave it out, SqlClient using its managed network layer on Windows too, as it does on Linux, so that everything WebLingo ships stays MIT, if it works with LocalDB, which I would check first; (c) something else, which you name.

A: (b)

Q3, which SQL Server the example and the tests run on, the one installed not being LocalDB (Claude, 2026-11-17): what is installed is SQL Server 2025 Developer's engine, its default instance, `MSSQLSERVER`, with Shared Memory its only protocol, which SqlClient's managed network layer does not speak, TCP/IP and Named Pipes being off, so that nothing WebLingo ships can reach it (tried: TCP refused, error 35); LocalDB is not installed. (a) you install LocalDB as well, as A1 has it (SQL Server 2025 Express's installer, Download Media, LocalDB, `SqlLocalDB.msi`), the example, its README and the tests staying as built; (b) you enable TCP/IP on the default instance (SQL Server Configuration Manager, SQL Server Network Configuration, Protocols for MSSQLSERVER, TCP/IP, Enabled, then restart the service), and the example and the tests run on it, `Server=localhost`, A1 becoming (b), the README saying how a reader sets up such a server.

A3: (b), show me how, or better do it yourself.

2026-11-17 (Claude): built, with A2's (b) and A3's (b), and landed: SQL Server's provider, `sqlserver`, in `DotNetDatabases` (`WebLingo.Data.DotNet`), SqlClient's own connection string, through its managed network layer on Windows too, its switch set before it first connects, Microsoft.Data.SqlClient.SNI.runtime referenced only to be excluded, which takes it out of every project's graph and of the published installation; and `Examples/example3`, Lingo To-Do, a to-do list on the machine's own SQL Server, `localhost`, by TCP/IP: `database/setup.sql`, which `database/setup.wl` runs through `std:db`, batch by batch, makes `WebLingoTodo`, four lists and 36 items due around today, and the site lists, adds, edits, marks done and deletes them, each change a form posted and the browser sent back (303). Checked against SQL Server 2025 Developer (17.00.1000), its TCP/IP enabled: the managed network layer reaches it by TCP, and `TrustServerCertificate=true` is needed for the certificate its installation made, without which the handshake fails; LocalDB, which A1 first chose, was not tried. The answers are recorded in the specification (section 1.1's "Database access", section 7.11, section 15.2's "Database access"). Tests: `DatabaseTests`, SQL Server's connection strings, a server that is not there a `DatabaseError`, and no SNI library beside the tests; `SqlServerTests` and `WaspServeTests.TheToDoListServesItsSqlServerDatabase` on `localhost`, all passing here, skipped where nothing answers on port 1433 (`SqlServerFactAttribute`). example3 tried in the browser: its lists, adding, marking done, editing, deleting, search, the done items and `/api/items`, with no error in `wasp`'s log; the database then made afresh by `setup.wl`. Docs: the specification, the Language Reference, the Standard Library Reference, the WASP User Manual, the portability inventory, `Documents/implementation.md` (a trap, "SqlClient's Native Network Layer Is Left Out"), both READMEs, and example3's section of `Examples/README.md`, with how to set up SQL Server and enable its TCP/IP. example2's `web.config` writes `wasp`'s log under example2's name.

Packages added, in `WebLingo.Data.DotNet` only, all MIT: Microsoft.Data.SqlClient 7.1.2, Microsoft.Data.SqlClient.Extensions.Abstractions 7.1.2, Microsoft.Data.SqlClient.Internal.Logging 7.1.2; Microsoft.IdentityModel.Abstractions, .JsonWebTokens, .Logging, .Protocols, .Protocols.OpenIdConnect and .Tokens 8.16.0, System.IdentityModel.Tokens.Jwt 8.16.0; Microsoft.Extensions.Caching.Abstractions, .Caching.Memory, .Options and .Primitives 9.0.21, Microsoft.Extensions.DependencyInjection.Abstractions and .Logging.Abstractions 10.0.0; Microsoft.Bcl.Cryptography, System.Configuration.ConfigurationManager, System.Diagnostics.EventLog, System.Security.Cryptography.Pkcs, System.Security.Cryptography.ProtectedData and System.Threading.RateLimiting 9.0.21; Microsoft.SqlServer.Server 1.0.0. Microsoft.Data.SqlClient.SNI.runtime 7.1.2 (Microsoft Software License Terms) is referenced and excluded, never shipped. A published installation grows from 35 MB to 41 MB.

Done (Claude, 2026-11-17, `c6491e2`): SQL Server in `std:db`, and example3. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail, the SQL Server tests included; `weblingo` on the samples and `setup.wl`, and `wasp` on `samples/SimpleWebsite` and example3; both VMs; Windows only, Linux not tried, where the SQL Server tests are skipped.

## [DONE] What Comes After M0.14

2026-11-17 (Claude): the five tasks are written into TO-DO.md, in A1's order, and the order is recorded in the specification, M0.15 to M0.19 (section 9's roadmap, and section 1.1's "Milestones after M0.14").

M0.14, hosting, is implemented, and the roadmap ends there: what comes after it is the owner's to choose (specification, section 9). The tasks since have been features you asked for. Added by Claude, 2026-11-17, from the roadmap (`Documents/implementation.md`, section 13), while the task above waits for its answers.

Q1, the next milestone, each a part of the specification not built yet: (a) file capabilities, a site's pages reaching only the folders its `wasp.config` allows, as `"network"` allows hosts, where today `std:fs` reaches any file the server's process may (sections 7.4 and 7.13, open question 13); (b) COM objects on Windows, `windows:com`, designed, its main points approved (section 3.10); (c) context-aware escaping in pages, for attributes, URLs and scripts, not only HTML's text (section 2.2, open question 9); (d) cryptography, `std:crypto`: secure random bytes, SHA-256 and SHA-512, HMAC and password hashing (section 7.10); (e) FastCGI, a host beside IIS's (section 6.1); (f) none for now, your own tasks coming first; (g) something else, which you name.

A1: (c), then (a), then (b), then (d), then (e). Write the tasks into TODO.

Done (Claude, 2026-11-17, `d24a9e7`): the owner's order after M0.14 recorded as approved in the specification, M0.15 context-aware escaping in pages, M0.16 file capabilities, M0.17 COM objects on Windows, M0.18 cryptography and M0.19 FastCGI, each a task in TO-DO.md. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail; Windows only, Linux not tried.

## [DONE] std:db: The Order of a Connection's Operations

Found by Claude, 2026-11-17, running the complete test suite: `DatabaseTests.StatementsGiveRowsAndCounts` failed once, and passed run again. Its case `Task.all([db.query("SELECT COUNT(*) AS n FROM Fish"), db.execute("DELETE FROM Fish WHERE Id = 2")])` starts two operations on one connection without awaiting the first; `DotNetDatabases` runs a connection's operations one at a time, each off the worker, behind a lock, but in whichever order they reach it, so that the `DELETE` sometimes runs first and the count is 1, not 2. The specification does not say which order they run in (section 7.11).

Q1, the order of operations a script starts on one connection without awaiting them: (a) the order it started them in, each waiting for the one before, as node-postgres queues a client's queries, the test kept as it is; (b) any order, as now, the test changed not to depend on it, a script awaiting each operation whose order matters.

A1: (b)

2026-11-17 (Claude): A1's (b) recorded as approved in the specification (section 1.1's "Database access", section 7.11, section 15.2) and in the Standard Library Reference: a connection's operations run one at a time, those a script starts without awaiting them in no defined order. `DotNetDatabases` is unchanged; the test's case of `Task.all` now counts a row the delete does not touch, and passed three runs in a row.

Done (Claude, 2026-11-17, `ea51bad`): the order of a connection's operations left undefined. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail; Windows only, Linux not tried.

## [DONE] Context-Aware Escaping in Pages

The owner's next milestone, M0.15 (A1 of "What Comes After M0.14", 2026-11-17; specification, section 9), the first of five. A page's `<%= %>` escapes its value for where it stands in the HTML, not only for HTML's text, and raw output, `<%- %>`, stays explicit (specification, section 2.2, open question 9; `AGENTS.md`'s secure defaults, `Documents/implementation.md`, section 6). Added by Claude, 2026-11-17.

Today `<%= %>` escapes `&`, `<`, `>`, `"` and `'` wherever it stands (`WebLingoTemplateWriteFunctions`), which is safe in HTML's text and in a quoted attribute's value, but not elsewhere: in an event's attribute, `onclick="show('<%= name %>')"`, the browser turns `&#39;` back into `'` before it runs the script, so that a name holding `');alert(1);//` runs as code; in a URL's attribute, `href="<%= link %>"`, a `javascript:` URL runs when it is clicked; in an unquoted attribute, a space ends the value; and in `<script>` and `<style>`, whose text the browser does not decode, a value is escaped wrongly and may still break out.

Q1, how a page's `<%= %>` knows where it writes: (a) the template compiler reads the HTML around each `<%= %>` and escapes for the context it stands in, text, a quoted or unquoted attribute, an event's attribute, a URL's, `<script>`, `<style>`, as Go's `html/template` does, with functions the author calls for what it cannot tell, `<%= Html.url(x) %>` and the like; (b) the compiler alone, as (a), without such functions; (c) the author names the context, `<%= Html.js(x) %>`, `<%= Html.url(x) %>`, HTML's escaping staying the default, as Razor's helpers do.

A1: (a)

2026-11-17 (Claude): built with A1's (a), and landed: the template parser reads each page's HTML as Go's `html/template` follows its contexts (`WebLingoHtmlContext`), changing none of the page's text, and each `<%= %>` calls the writer of the escaping where it stands (`WebLingoTemplateWriters.EscapedAs`), `<%=>` still for HTML's text and a quoted attribute's plain value, so that a page that writes only there is written as before; the escapes are the runtime's (`WebLingoHtmlEscaping`): a URL's scheme other than `http`, `https` and `mailto` refused, URLs percent-encoded, a script's value as JSON safe in HTML, JavaScript's and CSS's escapes in their strings, a style's value refused where it could change what the style means, `zWebLingoz` in place of a value refused; the code between blocks followed by its braces, as Go follows its branches; what cannot be followed is WL5008, a new code (Proposed); and `Html`, a built-in class, `escape`, `url`, `urlPart`, `js`, `jsString` and `css`, for what the compiler cannot tell. The answer is recorded in the specification (section 1.1's "Context-aware escaping (M0.15)", sections 2.2, 10's question 9, and 13's "Context-aware escaping"), the details as built in section 15.2, Proposed.

Guessed, where Go's `html/template` leaves a choice or does what WASP cannot, each recorded as Proposed in section 15.2: the page's text is never rewritten (Go removes comments and escapes a stray `<`), and an end tag, `</script`, ends a script wherever it stands, as browsers do; a value in an HTML comment is HTML-escaped (Go writes nothing); `<%= null %>` in a script writes `null` (elsewhere nothing, as before), nothing being no script's value; an attribute's name a value gives may hold `-` (Go: letters and digits); a URL's scheme is the text before a first `:` that no `/`, `?` or `#` precedes (Go: no `/`); percent-encoding in upper case (Go: lower); a `srcset`'s descriptor may hold `.`; the marker is `zWebLingoz` (Go: `ZgotmplZ`); a loop's body must end where it began (Go reads it twice and joins); a function written in a page is read where it is written, not where it is called; an exception that leaves a block midway is not followed; `<%- %>` and `write` are taken to leave the HTML where it was. The helpers' names, `Html.escape`, `url`, `urlPart`, `js`, `jsString` and `css`, are mine too.

Tests: `EscapingTests` (`WebLingo.Templates.Tests`), every context on both VMs, includes and components, eleven WL5008 cases and a layout's, the escapers themselves and the writers' names; conformance cases of `Html`, on both VMs and through WLBC. Docs: the specification, the WASP User Manual (section 4, rewritten), the Standard Library Reference (section 19, `Html`), the Language Reference's diagnostic codes, `Documents/implementation.md`, the README and the editors' colouring.

Done (Claude, 2026-11-17, `7aa54fa`): context-aware escaping in pages, M0.15. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail (its footer's `DateTime.Now`); `weblingo` on the samples, and `wasp` on `samples/SimpleWebsite` and the pages of example2 and example3, which compile unchanged; both VMs; the benchmark not run, neither VM's dispatch having changed; Windows only, Linux not tried.

## [DONE] File Capabilities: A Site's Folders

The owner's second milestone, M0.16 (A1 of "What Comes After M0.14", 2026-11-17; specification, section 9). Under `wasp`, a site's pages reach only the folders its `wasp.config` allows, as `"network"` allows hosts (section 7.9), where today `std:fs` reaches any file the server's process may (sections 3.9, "Files: `std:fs`", 7.4 and 7.13, open question 13); under `weblingo`, a script reaching any file, as now. Added by Claude, 2026-11-17.

2026-11-17 (Claude): its questions, while "Context-Aware Escaping in Pages", above, waits for its answer. Today a host `"network"` does not allow is WL3001 where the connection is made (`DotNetNetwork`), and a relative path in `std:fs` is from the process's current folder, which `wasp serve` and IIS set differently. One decision each:

Q1, how a site lists its folders in configuration: (a) two lists, `"files": { "read": ["../data"], "write": ["../uploads"] }`, a folder listed for writing readable too, so that a site that only reads its catalog cannot change it; (b) one list, `"files": { "allow": ["../data"] }`, each folder read and written, as `"network"`'s one list allows hosts.

A1: (a)

Q2, what a site reaches that lists none: (a) no file, as a site without `"network"` reaches no host; (b) the files below its own root, its pages' folder, besides those it lists.

A2: (a)

Q3, a relative path in a page's script, `fs.readText("../data/teas.json")`, under `wasp`: (a) from the site's root, as an `include`'s and the configuration's own paths are, the same under `wasp serve` and IIS; (b) from the process's current folder, as now and as under `weblingo`. A folder in the list, relative, is from the site's root either way, as a SQLite database's path in `"databases"` is from the configuration's folder.

A3: (a)

Q4, how a path is checked against the folders: (a) its real path, made absolute, `.` and `..` resolved and its links and junctions followed, compared as the file system compares names (ignoring case on Windows), Windows' device names (`CON`, `NUL`, `\\.\…`) always refused; (b) the path as written, made absolute and `..` resolved, its links not followed, so that a link inside an allowed folder reaches wherever it points.

A4: (a)

Q5, what a path a site may not reach is: (a) WL3001, a `TypeError`, as a host `"network"` does not allow is, its message naming the path as the script gave it; (b) an `IOError`, WL3011, as a file that cannot be opened is.

A5: (c) `PermissionError`

Q6, a SQLite database a page's script names by its own connection string, `Database.open("sqlite:../data/shop.db")`: (a) checked as `std:fs`'s paths are, for reading and writing, those the configuration names in `"databases"` trusted, the configuration being the site's own; (b) not checked, file capabilities being `std:fs`'s only.

A6: (a)

2026-11-17 (Claude): built with A1's (a), A2's (a), A3's (a), A4's (a), A5's (c), `PermissionError`, and A6's (a), and landed: the platform's `IWebLingoFileCapabilities` and, on .NET, `DotNetFileCapabilities`, which resolve a script's path from the site's root to its real path, its links and junctions followed, and refuse one outside the folders `wasp.config`'s `"files"` lists, or a Windows device's; a site's scripts reach files through a file system given them (`WebLingoApplicationOptions.ScriptFileSystem`), which opens the real path, the host reading its own pages as before; `wasp run` and `wasp serve` both; a SQLite database a script names by its own connection string checked the same way; `PermissionError`, WL3016, a new code and class. The answers are recorded in the specification (section 1.1's "File capabilities (M0.16)", sections 3.9, 7.4 and 7.13's "File capabilities"), the details as built in section 15.2, Proposed.

Guessed, each recorded as Proposed in section 15.2: `..` is resolved as written before the links are followed, the real path then being what is opened, so that `link/..` means the link's folder, not its target's parent; the listed folders are resolved once, when the site loads; names are compared ignoring case on macOS too; a look (`exists`) at a path the site may not reach is a `PermissionError`, not `false`; a SQLite database opened read-only (`Mode=ReadOnly`) needs a folder to read only, any other one to write, a `file:` URI is refused, and `:memory:` is as before; SQL Server's `AttachDbFilename`, which the server's own process reads, is not checked; a link made between the check and the opening is not guarded against, no script being able to make one. A host that `"network"` does not allow is still WL3001, as approved; a question of its own asks whether it, and COM's, should be a `PermissionError` too.

Tests: `WaspTests`, through `wasp run`, no folders, read and write folders, `..`, the root and `--root`, a junction leading out of a folder (a symbolic link elsewhere), on Windows another case and device names, a page's own databases, and the configuration's errors; `WaspServeTests`, through `wasp serve`. Docs: the specification, the WASP User Manual (section 14), the Standard Library Reference, the Language Reference, `Documents/implementation.md`, the portability inventory and the editors' colouring.

Done (Claude, 2026-11-17, `4b520e8`): file capabilities, M0.16. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail; `weblingo` on the samples and `wasp` on `samples/SimpleWebsite`; Windows only, Linux not tried, where links are symbolic links and names' case counts.

## [DONE] COM Objects on Windows: `windows:com`

The owner's third milestone, M0.17 (A1 of "What Comes After M0.14", 2026-11-17; specification, section 9): the native module designed in the specification's section 3.10, its name, members, threads, permission and interop approved on 15 November 2026, built: `com.create(progId)`, `com.get(moniker)`, `ComObject`'s members through `IDispatch`, `VARIANT`s mapped to values, `ComError` (WL3012), each worker a single-threaded apartment, and COM allowed only by `wasp.config`'s or `weblingo.config`'s `"com"`. Windows only; skipped, as the SQL Server tests are, elsewhere. Added by Claude, 2026-11-17.

2026-11-17 (Claude): its questions, asked before it is built, since it is large and its Proposed points shape what scripts write; the two tasks above wait for theirs. Section 3.10 was written before WebLingo had decimals, bytes and dates (section 3.2), which Q2 to Q4 bring in. One decision each:

Q1, the rest of section 3.10, which is Proposed: `com.create(progId)` by a ProgID or a CLSID and `com.get(moniker)`; values going out and coming back as its "Values" maps them; `ComError`'s fields, `number`, `source` and `description`; `foreach` through `_NewEnum`; the `windows` scheme's folder, `windows/`, beside the runtime's program, as `std/` is; a COM member named as one every value has (`toString`, `equal`, `dispose`) reached by `ComObject`'s `call(name, arguments)`, `get(name)` and `set(name, value)`; and `"com": { "allow": ["ADODB.Connection"] }`, `"*"` allowing any: (a) approved as written, but for Q2 to Q4; (b) not yet, you naming what changes.

A1: (a)

Q2, COM's currency and decimals, `VT_CY` and `VT_DECIMAL`, which section 3.10 reads as numbers "until WebLingo has decimal128 values": (a) as decimals, which WebLingo has had since 16 November 2026, a decimal going out as `VT_DECIMAL`, so that ADO's `MONEY` and `DECIMAL` fields keep their digits; (b) as numbers, as written.

A2: (a)

Q3, COM's arrays of bytes, `VT_ARRAY | VT_UI1`, as ADO gives a binary field: (a) as `Bytes`, and `Bytes` going out as one; (b) as an array of integers, as any other `SAFEARRAY` is.

A3: (a)

Q4, a date going out to COM, which section 3.10 does not list: (a) as `VT_DATE`, its time on the server's clocks, as `VT_DATE` comes back; (b) WL3001, as a value COM does not take is.

A4: (a)

2026-11-17 (Claude): built with A1's (a), A2's (a), A3's (a) and A4's (a), and landed: `windows:com`, `WebLingo.Windows.Com`, installed as `windows/com.dll`, the `windows` scheme's folder by default on Windows only: `com.create(progId)` and `com.get(moniker)`, only for what the configuration's `"com"` allows; `ComObject`'s members through `IDispatch`, called through function pointers, never .NET's COM interop; values to `VARIANT`s and back, decimals, bytes and dates as your answers have them; `x[k]` the default member; `foreach` through `_NewEnum`; `call`, `get` and `set`; `ComError`, WL3012. For it, native modules gained a class's fallback for the members it does not declare (`DeclareFallback`), which the runtime runs, each VM's method call reaching it in its failure's path, its one change to either VM; a class its module's own functions make (`DeclareMadeByModule`); a runtime error's fields; and the configuration's allow-lists through the heap. `weblingo`'s and `wasp`'s `Main` are `[STAThread]`, and the web host's workers single-threaded apartments. The answers are recorded in the specification (section 1.1's "COM objects (Windows)", section 3.10, now built), the details as built in section 15.2, "COM objects", Proposed.

Guessed, each recorded as Proposed in section 15.2: `com.get(moniker)` needs the moniker itself, or `"*"`, in `"com"`'s list, as `create` needs its ProgID; a CLSID is compared as written; `x[k]` goes on to the default member's own default member when the first takes no argument, as VBScript's `rs("Name")` does; an assignment of an object is tried by reference first, then by value; every call is made with the invariant locale; a failure without an `EXCEPINFO` takes the thread's error information, its `description` "" where COM gave none, the message then in my words; `VT_ERROR` comes back as its code, `DISP_E_PARAMNOTFOUND` as `null`; the enumerator's class is `ComEnumerator`. ADO gives a big integer back as a decimal, which the tests show. A host `"com"` does not list is WL3001, as approved; the question "Permissions: Hosts and COM Objects as `PermissionError`" asks whether it should be a `PermissionError`.

Benchmark, three rounds of every workload but `io` and `async`, alternately against `main`: within the machine's noise on both VMs (`objects` 100 ms against 119 on the register VM, 107 against 101 on the stack VM, `main`'s own rounds ranging 100 to 110).

Tests: `ComTests` (`WebLingo.Conformance.Tests`, Windows only, skipped elsewhere), through `weblingo`, each program on the register VM and from a stack `.wlbc`, on a single-threaded apartment's thread: `Scripting.Dictionary`, an ADO recordset in memory, the errors, the permission and the configuration's error. Docs: the specification, the Standard Library Reference (section 20, `windows:com`), the WASP User Manual's configuration, the Language Reference, `Documents/implementation.md`, the portability inventory and the editors' colouring.

Done (Claude, 2026-11-17, `90b7e5e`): COM objects on Windows, M0.17. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail; `weblingo` on a script and `wasp` on a page that use COM; Windows only, where COM is; not tried under IIS.

## [DONE] Cryptography: `std:crypto`

The owner's fourth milestone, M0.18 (A1 of "What Comes After M0.14", 2026-11-17; specification, section 9): a standard module of the specification's section 7.10, secure random bytes, SHA-256 and SHA-512, HMAC, a comparison of bytes in constant time (approved for `Bytes`, section 3.2, not built yet), and password hashing, through the platform layer (`IWebLingoCryptoService`, section 7.3), so that the portable core reaches no .NET cryptography. Added by Claude, 2026-11-17.

2026-11-17 (Claude): its questions, asked now, with the tasks above, so that they can be answered together. One decision each:

Q1, password hashing: (a) PBKDF2 with HMAC-SHA-256, built into .NET (`Rfc2898DeriveBytes.Pbkdf2`), 600,000 iterations and a 16-byte random salt, as OWASP advises, its result one string that names them, `pbkdf2-sha256$600000$salt$hash`, so that a later default still verifies an older hash; no new package, and simple for Native C# to implement again; (b) Argon2id, stronger against graphics cards, through a package (MIT), a new dependency; (c) bcrypt, through a package (MIT).

A1: (a)

Q2, the module's names: (a) functions, `crypto.randomBytes(n)`, `crypto.sha256(data)`, `crypto.sha512(data)`, `crypto.hmac("sha256", key, data)`, `crypto.equal(a, b)` in constant time, `crypto.hashPassword(password)` and `crypto.verifyPassword(password, hash)`, data a string, as UTF-8, or `Bytes`, a hash given as `Bytes`; (b) Node.js's, `crypto.createHash("sha256").update(data).digest()`, a hash built in pieces.

A2: (a)

Q3, waiting: (a) `hashPassword` and `verifyPassword` asynchronous, run off the worker, since each takes a large part of a second by design, the rest synchronous; (b) all synchronous, as they are quick, a password's hash blocking its worker.

A3: (a) designates the sync methods; `hashPasswordAsync` and `verifyPasswordAsync` for the async versions.

2026-11-17 (Claude): built with A1's (a), A2's (a) and A3's answer, `hashPassword` and `verifyPassword` synchronous and `hashPasswordAsync` and `verifyPasswordAsync` run off the worker, and landed: `std:crypto`, `WebLingo.StdLib.Crypto`, installed as `std/crypto.dll`, in the portable core, through the platform's `IWebLingoCrypto`, `DotNetCrypto` on .NET, the heap's `Crypto`, which the command lines and the web host give; `PortabilityTests` keeps `System.Security.Cryptography` out of the portable core. The answers are recorded in the specification (section 1.1's "Cryptography (M0.18)" and section 7.10), the details as built in section 15.2, "Cryptography", Proposed.

Guessed, each recorded as Proposed in section 15.2: the platform's contract is `IWebLingoCrypto`, section 7.3's conceptual `IWebLingoCryptoService`, as `IWebLingoNetwork` and `IWebLingoDatabases` are named; `randomBytes` gives at most 1,048,576 bytes; `hmac` takes `"sha256"` or `"sha512"`; `equal` of two values of different lengths is `false`, their lengths not secret; a password's hash's salt and hash are in RFC 4648's Base64, with its padding, the hash 32 bytes; `verifyPassword` reads 1 to 10,000,000 iterations, so that a hash from outside cannot hold a worker for ever, and a hash it does not read is WL3001, its text kept out of the message. A script that wants a hash as text formats its bytes itself, `$"{b[i]:x2}"`; a question of its own asks about hexadecimal and Base64 text for `Bytes`.

Tests: `CryptoTests` (`WebLingo.Conformance.Tests`), each program on the register VM and from a stack `.wlbc`: SHA-256's and SHA-512's, RFC 4231's HMAC and PBKDF2's published vectors, the last read by `verifyPassword`; random bytes; `equal`; hashes made and verified, synchronously and as tasks; nine errors. Docs: the specification, the Standard Library Reference (section 18, its later sections one further on), the Language Reference, `Documents/implementation.md` and the portability inventory.

Done (Claude, 2026-11-17, `2192b88`): cryptography, `std:crypto`, M0.18. Main's checks: the Release build, without warnings, and every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail; `weblingo` on the reference's example; both VMs; Windows only, Linux not tried.

## [DONE] FastCGI: A Host Beside IIS's

The owner's fifth milestone, M0.19 (A1 of "What Comes After M0.14", 2026-11-17; specification, section 9): a host that speaks FastCGI, as PHP's servers run it, beside the ASP.NET Core adapter, translating its requests to WLHI's (sections 6.1, 6.2 and 6.9), so that a web server other than IIS (nginx, Apache) runs WASP's sites. Added by Claude, 2026-11-17.

2026-11-17 (Claude): its questions, asked now, with the tasks above. Neither nginx nor Apache is on this machine; its tests would talk FastCGI to it themselves, a web server needing your go-ahead to download. One decision each:

Q1, how it runs: (a) `wasp fastcgi --root site --port 9000`, a process the server's service manager (systemd, or a Windows service) starts and keeps running, which the web server connects to by TCP, or a Unix socket on Linux, as PHP-FPM is; (b) started by the web server itself, on demand, its socket given on its standard input, as Apache's mod_fcgid starts PHP.

A1: both (a) and then (b)

Q2, its code: (a) FastCGI's protocol written in WebLingo's own C#, a small one (a record's header and its kinds), in a new adapter project, `WebLingo.Hosting.FastCgi`, on .NET's sockets, with no package; (b) through a package that implements FastCGI.

A2: (a)

Q3, the servers it is tried with, beside its tests: (a) nginx, the commonest in front of PHP-FPM, on Windows and in its documentation's example; (b) nginx and Apache httpd; (c) none, its tests alone, the documentation giving each server's configuration untried.

A3: (b)

2026-11-17 (Claude): built with A1's both, (a) then (b), and A2's (a), and landed: `WebLingo.Hosting.FastCgi`, an adapter beside the ASP.NET Core one, whose `WebLingoFastCgiServer` translates FastCGI's responder role to WLHI's and back, and `wasp fastcgi [--root <folder>] [--port <port> | --socket <path> | --stdin]`, listening on a port, 9000 by default, or on a Unix socket, as PHP-FPM does, or serving the socket or the named pipe a web server gives it as its standard input, which it finds without `--stdin`, as mod_fcgid starts PHP. Its requests run on the ASP.NET Core adapter's worker pool, and their responses go through that adapter's streaming response, which now takes any host's `IWebLingoResponseSink`, so that a page's output is kept, sent and waited for as under IIS. The answers are recorded in the specification (section 1.1's "FastCGI (M0.19)" and section 6.13), the details as built in section 15.2, "FastCGI", Proposed; the WASP User Manual's new section 18 gives nginx's and Apache's configurations, untried.

Guessed, each recorded as Proposed in section 15.2: only the responder role; one request at a time on a connection, a second one's `FCGI_CANT_MPX_CONN`, as PHP-FPM answers; a request's parameters at most 1 MiB; its path from `REQUEST_URI`, else `SCRIPT_NAME` and `PATH_INFO`; its environment production unless `wasp.config` names another, a web server's process being a deployment; its log on its standard error when a web server started it; on a named pipe, a request read whole before it runs, since a web server may make a pipe's handle for synchronous use, which cannot be read and written at once, and the pipe disconnected after each connection, once its response has been read, and waited on again, as php-cgi does.

A3's (b), trying it with nginx and Apache httpd, needs downloads, which a task of its own asks your go-ahead for: tried instead with its tests and with a FastCGI client of my own, against `wasp fastcgi` on a port and on a named pipe given as its standard input, synchronous and overlapped, as mod_fcgid makes one on Windows. Found while trying it: Git Bash turns a lone `/` argument into Git's own folder before a Windows program sees it, which made the client's first requests 404s (`AGENTS.md`, "Git Bash Rewrites Arguments That Look Like Paths"); and a pipe was closed, not disconnected, after a request whose web server did not keep its connection, so that no second one could connect, fixed. Not this task's: the language tests' host crashes on every run, `ParserTests.DeepNestingIsADiagnosticNotACrash` overflowing its stack on `main`'s parser, unchanged here, which an aborted run's `Passed!` line had hidden (`Documents/implementation.md`, "An Aborted Test Run Still Says "Passed!""); a task of its own asks how to fix it.

Tests: `FastCgiTests` (`WebLingo.Templates.Tests`), twelve, each the web server: a page from CGI's variables; a form in `FCGI_STDIN`; a status, a header, a 404 and a 500; a file; a large page in several records; a kept connection; a busy connection's second request refused; management records, an unknown type and an unknown role; a named pipe connected to anew three times; `wasp fastcgi` itself, on a port and on a Unix socket; its options. Docs: the specification, the WASP User Manual (sections 16 and 18, its last section now 19), `Documents/implementation.md`, the portability inventory, with `PortabilityTests` now keeping `System.Net.Sockets` and `System.IO.Pipes` out of the portable core, the README and `AGENTS.md`. No package added.

Done (Claude, 2026-11-17, `5a49363`): FastCGI, M0.19. Main's checks: the Release build, without warnings; every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail, and the language tests, whose host `ParserTests.DeepNestingIsADiagnosticNotACrash` crashes, as on `main` before this task ("Parser: Deep Nesting Overflows the Stack"); `wasp fastcgi` on `samples/SimpleWebsite`, on a port and on a named pipe as its standard input; Windows only, Linux not tried, nginx and Apache not tried.

## [DONE] Permissions: Hosts and COM Objects as `PermissionError`

Added by Claude, 2026-11-17, after "File Capabilities: A Site's Folders" (`DONE.md`): a path a site may not reach is a `PermissionError` (WL3016), the owner's answer; but a host `"network"` does not allow is WL3001, a `TypeError`, as approved on 16 November 2026 (specification, section 7.9), and a COM object `"com"` does not list is WL3001 in section 3.10's design, approved on 17 November 2026 (A1 of "COM Objects on Windows"), so that one kind of refusal has two classes. COM is built with WL3001, as approved, which one line changes.

Q1, what a host `"network"` does not allow, and a COM object `"com"` does not list, are: (a) a `PermissionError`, WL3016, as a path a site may not reach is, so that a script catches every refusal of its configuration as one class; (b) WL3001, as approved, a `PermissionError` being files' only.

A1: These are three different errors. Do not combine them under a single error code. One has to do with network configuration, the other with COM configuration, and the third with folder configuration. Separate them as differentr Error classes and give each a distinct code so that troubleshooting is easier.

2026-11-17 (Claude): built with A1's answer, and landed: a host `"network"` does not allow is a `NetworkPermissionError`, WL3017, from `std:http` and `std:net`, and a COM object `"com"` does not list a `ComPermissionError`, WL3018, from `windows:com`, each in place of WL3001; a folder's refusal stays a `PermissionError`, WL3016. The answer is recorded in the specification (section 1.1's "Refusals' errors", sections 3.10 and 7.9, the codes in section 8.4, and section 15.2's "Network access", "File capabilities" and "COM objects").

Guessed, recorded as Proposed: the classes' names, `NetworkPermissionError` and `ComPermissionError`, beside `PermissionError`, and their codes, the next two, WL3017 and WL3018; each extends `Error`, none another, so that no class catches two kinds of refusal. Whether `PermissionError` should be named `FilePermissionError` beside them is a question of its own.

Tests: `ComTests` catches a `ComPermissionError`, WL3018, for a ProgID and a moniker the configuration does not list, and for one it does not list among other failures; `WaspTests` catches a `NetworkPermissionError`, WL3017, from `std:http` and `std:net`. Docs: the specification, the Standard Library Reference, the Language Reference, the WASP User Manual and Notepad++'s languages.

Done (Claude, 2026-11-17, `a9d695f`): refused hosts and COM objects, errors of their own. Main's checks: the Release build, without warnings; every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail, and the language tests, whose host `ParserTests.DeepNestingIsADiagnosticNotACrash` crashes, as before ("Parser: Deep Nesting Overflows the Stack"); `wasp run` on pages that reach a refused host, in `WaspTests`; `windows:com` on Windows; Windows only, Linux not tried.

## [DONE] Bytes as Text: Hexadecimal and Base64

Added by Claude, 2026-11-17, after "Cryptography: `std:crypto`" (`DONE.md`): a hash, an HMAC and random bytes are `Bytes`, which a script stores, compares or sends as text, a token in a URL, a hash in a database, a key in a configuration; today it formats each byte itself, `$"{b[i]:x2}"`, and cannot read such text back. `Bytes` has `b.text()` and `Bytes.from(text)` for text's own encodings (specification, section 3.2, "Bytes"), but neither hexadecimal nor Base64.

Q1, `Bytes` as hexadecimal and Base64 text: (a) members of `Bytes`, `b.toHex()` and `b.toBase64()`, and its class's `Bytes.fromHex(text)` and `Bytes.fromBase64(text)`, Base64 RFC 4648's with its padding, and its URL-safe alphabet by an argument, `b.toBase64(true)`; (b) functions of `std:crypto`, `crypto.toHex(b)` and the like; (c) neither, a script formatting its bytes itself.

A1: (a)

2026-11-17 (Claude): built with A1's (a), and landed: `b.toHex()`, `b.toBase64()` and `b.toBase64(true)`, and `Bytes.fromHex(text)`, `Bytes.fromBase64(text)` and `Bytes.fromBase64(text, true)`, in the runtime, so on both VMs alike, by the runtime's own codec, `WebLingoBinaryText`, which `std:crypto` now writes and reads its passwords' hashes with, in place of its own copy. The answer is recorded in the specification (section 1.1's "Bytes" and section 3.2), the details as built in section 15.2, "Bytes", Proposed.

Guessed, each recorded as Proposed: hexadecimal written in lower case, read in either case; URL-safe Base64 written unpadded, since a URL would escape `=`, and read padded or not, `Bytes.fromBase64(text, true)` taking the alphabet as `toBase64` does; standard Base64 read with its padding only; `""` no bytes; anything else, white space included, WL3001, the text quoted in the message, as `Integer.parse` quotes it.

Tests: `BytesTests` (`WebLingo.VM.Tests`), on both VMs: RFC 4648's vectors written and read, the URL-safe alphabet padded and not, round trips, and eleven errors. Docs: the specification, the Standard Library Reference, whose crypto example now writes its hashes with `toHex()`, and the Language Reference, each example run with `weblingo`.

Done (Claude, 2026-11-17, `7912e4d`): bytes as hexadecimal and Base64. Main's checks: the Release build, without warnings; every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail, and the language tests, whose host `ParserTests.DeepNestingIsADiagnosticNotACrash` crashes, as before ("Parser: Deep Nesting Overflows the Stack"); `weblingo` on the references' examples; both VMs; Windows only, Linux not tried.

## [DONE] Parser: Deep Nesting Overflows the Stack

Added by Claude, 2026-11-17, while running the checks of "FastCGI: A Host Beside IIS's" (`DONE.md`): `ParserTests.DeepNestingIsADiagnosticNotACrash` crashes the language tests' host with a stack overflow on every run now, on `main`'s parser, which that task did not change; an aborted run still prints `Passed!`, which hid it, perhaps for some time. The parser stops at 256 levels of nesting (WL1008, `MaxRecursionDepth`), each level some seventeen of its methods deep, which fits a thread's 1 MB stack once .NET has optimized those methods, but not before: unoptimized, it overflowed at 206 levels. With .NET's tiered compilation off, which optimizes every method from its first call, all 263 language tests pass. So a page or a script nested some 200 levels deep, compiled before .NET has optimized the parser, as when `wasp` or `weblingo` starts, can crash its process instead of giving WL1008; the binder and the lowerer recurse as deep after the parser.

Q1, how the compiler stays within its stack: (a) it asks how much stack is left where its stages recurse, .NET's `RuntimeHelpers.TryEnsureSufficientExecutionStack()`, as Roslyn does, and gives WL1008 when it runs low, the limit of 256 levels kept; a facility of .NET's in the portable core, added to the portability inventory, which Native C# would need one of its own for; (b) a lower limit, 64 levels, which fits the stack unoptimized, nothing else changing; (c) the command lines and the hosts compile on a thread of their own with a larger stack, 16 MB, the limit kept.

A: (c)

2026-11-17 (Claude): built with A's (c), and landed: the command lines and the web hosts compile on threads of their own with a 16 MB stack, `WebLingoParser.CompilerStackSize`, the limits of 256 levels and 1,000 operations kept. `weblingo`'s and `wasp`'s `Main` run each command on such a thread (`WebLingoCommandThread`, in `WebLingo.Cli`), a single-threaded apartment's on Windows, as `Main`'s was, for COM; the web hosts' workers, where a page is compiled when first requested, ASP.NET Core's and FastCGI's alike, are such threads (`WebLingoWorkerPool`). The answer is recorded in the specification (section 1.1's "The compiler's stack" and section 15.2's "Nesting limits"), and in `Documents/implementation.md`'s traps, "The Compiler Needs a Stack of Its Own".

Guessed: the whole command runs on the thread, not only its compilation, since a running program compiles what it imports dynamically, and a component is compiled when first rendered; the benchmark, a tool, compiles on its own first thread, its workloads being shallow.

Tests: `ParserTests` parses its deepest sources on such a thread, all 263 language tests passing on every run again; `CompilerStackTests` (`WebLingo.Conformance.Tests`) runs `weblingo`'s command thread on source 250 levels and 990 operations deep, and on source 100,000 levels deep, WL1008; `FastCgiTests` serves a page nested 250 deep from a worker. `weblingo run` and `wasp run` themselves, the executables, run such source, and `weblingo check` reports the deeper one's WL1008.

Done (Claude, 2026-11-17, `7d936ea`): the compiler's stack. Main's checks: the Release build, without warnings; every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail, the language tests' host no longer crashing; `weblingo` and `wasp` on deep source; Windows only, Linux not tried.

## [DONE] FastCGI: Trying nginx and Apache httpd

Added by Claude, 2026-11-17, after "FastCGI: A Host Beside IIS's" (`DONE.md`): its A3 asked for the host to be tried with nginx and Apache httpd, beside its tests; neither is on this machine, so the WASP User Manual's configurations for them (section 18) are untried. Trying them needs three downloads, as their pages give them today, each unpacked into a temporary folder of its own outside the repository, run from the command line on 127.0.0.1 only, nothing installed or registered as a service, and removed afterwards. Apache Lounge's build needs the Visual C++ Redistributable 14.52 or later, which this machine has.

- nginx for Windows, its stable version, 1.30.6: `nginx-1.30.6.zip`, from `https://nginx.org/download/nginx-1.30.6.zip`, about 2 MB (its page states no size).
- Apache httpd 2.4.71 for 64-bit Windows, Apache Lounge's build: `httpd-2.4.71-261112-Win64-VS18.zip`, from `https://www.apachelounge.com/download/VS18/binaries/httpd-2.4.71-261112-Win64-VS18.zip`, 14,173 KB, as its page states.
- mod_fcgid 2.3.10 for it, which starts `wasp` on demand: `mod_fcgid-2.3.10-win64-VS18.zip`, from `https://www.apachelounge.com/download/VS18/modules/mod_fcgid-2.3.10-win64-VS18.zip`, 44 KB.

Q1, the downloads: (a) all three, to try nginx and both of Apache's ways, `mod_proxy_fcgi` to a running `wasp` and `mod_fcgid` starting it; (b) nginx's only; (c) none, the configurations staying untried.

A1: (a). Keep the downloads, so that the process can be repeated.

2026-11-17 (Claude): done with A1's (a), and landed: the three files downloaded as listed, each from the address given, and kept in the repository's `WebServers` folder, which git ignores, one folder each (`WebServers/nginx`, `WebServers/httpd`, `WebServers/mod_fcgid`), their sizes and SHA-256 recorded in `Documents/implementation.md`, "Trying FastCGI with nginx and Apache", so that they can be downloaded again and checked. nginx's zip is 2,773,293 bytes, not the "about 2 MB" guessed here. The trial is `WebServerTests` (`WebLingo.Templates.Tests`), which runs with the rest, unpacking each server into a temporary folder of its own, on 127.0.0.1 only, stopping it after, and is skipped where a download is not there, and on other platforms: nginx and Apache httpd through `mod_proxy_fcgi`, each to `wasp fastcgi` on a port, and Apache httpd through `mod_fcgid`, starting `wasp` itself, each configured as the WASP User Manual's section 18 configures it, through every route: a page, a folder's index, a form, a status and a header, a page larger than 64 KB, a file, and the module, the configuration and a path without a route, 404s.

Found, and fixed, each with a test of its own in `FastCgiTests`: nginx sends a request's `Content-Type` and `Content-Length` as headers too, `HTTP_CONTENT_TYPE` and `HTTP_CONTENT_LENGTH`, which were read beside CGI's own, so that every form posted through nginx was a 500; and a connection closed whole with a request's last `FCGI_STDIN` unread, which nginx may send after the response, was reset, losing the response's end, a file's body most often. A socket is now closed for writing only, as PHP-FPM closes one, what the web server still sends read and dropped until it closes the connection too, for at most 30 seconds. Also learned: a console's Apache httpd stops on Ctrl+C alone, Ctrl+Break restarting it, so the tests stop it by force, with the processes it started; nginx stops by `nginx -s stop`.

Not tried: the manual's systemd service, and Linux; a `wasp.exe` path with spaces in `mod_fcgid`'s `FcgidWrapper`, the tests' having none, though written quoted, as the manual writes it.

Done (Claude, 2026-11-17, `edd3655`): FastCGI tried behind nginx and Apache httpd. Main's checks: the Release build, without warnings; every test, `WebServerTests` among them, but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail; Windows only, Linux not tried.

## [DONE] Permissions: The Folders' Class's Name

Added by Claude, 2026-11-17, after "Permissions: Hosts and COM Objects as `PermissionError`" (`DONE.md`): a host `"network"` does not allow is now a `NetworkPermissionError`, WL3017, and a COM object `"com"` does not list a `ComPermissionError`, WL3018, each a class of its own; a folder `"files"` does not list stays a `PermissionError`, WL3016, as you named it, whose name now reads as if it were all three's.

Q1, a folder's refusal's class: (a) renamed `FilePermissionError`, beside the other two, nothing kept for the old name; (b) `PermissionError`, as it is.

A1: (a)

2026-11-17 (Claude): done with A1's (a), and landed: a folder's refusal, WL3016, is a `FilePermissionError`, beside `NetworkPermissionError` and `ComPermissionError`, nothing kept for `PermissionError`, which a script now finds undefined (WL2001); the C# constant followed, `FilePermissionDenied`. The rename is recorded in the specification (section 1.1's "Refusals' errors", and section 7.13); the references, the WASP User Manual, `Documents/implementation.md`, Notepad++'s languages and the tests use the new name.

Done (Claude, 2026-11-17, `033694f`): `PermissionError` renamed `FilePermissionError`. Main's checks: the Release build, without warnings; every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which your uncommitted edits to `Examples/example1` fail; `wasp run` and `wasp serve` catching the renamed class, in `WaspTests` and `WaspServeTests`; Windows only, Linux not tried.

## [DONE] A `toString()` an `extend` Block Gives a Kind of Value

2026-11-17 (Claude): found while making an interpolation check what `toString()` gives (`8a21bd2`). A `toString()` that an `extend` block gives one of the kinds of values built into the VMs, `extend Integer { toString() { return "int"; } }`, is called by one way of writing text and not by the others. `write`, `writeLn` and a page's `<%= %>` never call it, at any depth: `writeLn(5)` writes `5`, and `writeLn([5])` `[5]`, as the specification's section 15.2, "Extensions" (Proposed), has it, such a method called "only" by `x.toString()`; `ConformanceTests` holds that `writeLn("a")` writes `a` under `extend String { toString() … }`. An interpolation calls `x.toString()` for the value it writes, so `$"{5}"` gives `int`, though `$"{[5]}"` gives `[5]`; section 3.5 says an interpolation writes a value as `write` writes it. A class's own instances, and those of a class extending a kind of value, `class S extends Array {}`, have an extension's `toString()` written everywhere: only the kinds of values built into the VMs differ.

Q1: (a) an interpolation writes as `write` does: such a `toString()` is not called there either, only by `x.toString()`, so that `$"{5}"` gives `5`, as section 15.2 has it; (b) every way of writing text calls it, as it calls one a class's instances have, inside arrays and maps too, so that `writeLn(5)` writes `int` and `writeLn([5])` `[int]`, the VMs looking for one only in a program that gives a kind of value a `toString()`; (c) as now, an interpolation calling it for the value it writes, and nothing else.

A1 (the user, in chat, 2026-11-17): (a)

2026-11-17 (Claude): done with A1's (a), and landed: an interpolation builds a value's text as `write` builds it, so that a `toString()` an `extend` block gives a kind of value built into the VMs is called only by `x.toString()`, `$"{5}"` being `5` under `extend Integer { toString() { … } }`; a class's own instances' `toString()`, an extension's included, still called by both. Recorded in the specification, sections 3.5 and 15.2, "Extensions", and the language reference.

Done (Claude, 2026-11-17, `35b6e3a`): an interpolation writes a value as `write` writes it. Main's checks: the Release build, without warnings; every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which `Examples/example1`'s uncommitted footer's `env.machineName()` fails (WL2001); and the CLI, `weblingo` on both VMs and `wasp`. Tried on Windows only.

## [DONE] Write std/Environment

Write a standard Environment class that would provide information about the machine, such as machine name and its environment variables. This needs to be restricted and require explicit permission in the config.

2026-11-17 (Claude): nothing reads the machine or its environment yet: `std:system` has only `write`, `writeLn` and `delay`, and `env.machineName()` in `Examples/example1`'s footer is WL2001. The specification lists the environment among an application's capabilities (section 6.11), with no design. Under `wasp`, a site's environment variables are its process's, those its server gives it, IIS's `web.config` among them (`<environmentVariables>`), which may hold secrets, a connection string or a key; the portable core would read them through the platform layer, as it reads the clock.

Q1, where it is: (a) a standard module of its own, `std:environment`, its class `Environment`, `import { Environment } from "std:environment";`, its members static, as `Database.open` is; (b) the same, its module named `std:env`; (c) a built-in class, `Environment`, in every program without an import, as `Json` is.

A1: (b)

Q2, what it gives: (a) what this task names, the machine's name, `Environment.machineName`, and its environment variables, `Environment.variable("PATH")`, `null` for one that is not set, and `Environment.variables`, a map of those the configuration allows; (b) those, and more of the machine: its operating system's name and version, its number of processors, the process's user, and its current folder; (c) other: say which.

A2: (c): (a) with variable() under the same allowlist and the machine name granted per site.

Q3, reading or writing: (a) reading only; (b) also setting a variable, for the process alone, `Environment.setVariable(name, value)`, which every request of the site would then see.

A3: (a)

Q4, the configuration's permission: (a) `"environment": { "machineName": true, "variables": ["PATH", "APP_*"] }`, the machine's name, and each variable by its name, a name ending in `*` allowing those it begins, `"*"` any, nothing without it; (b) one list, as `"network"`'s and `"com"`'s are, `"environment": { "allow": ["machineName", "PATH", "APP_*"] }`; (c) all or nothing, `"environment": true`.

A4: (a)

Q5, under `weblingo`: (a) a script needs `weblingo.config`'s permission too, as for COM's objects; (b) a script reaches the whole environment without one, as it reaches any file and any host, the permission `wasp`'s alone.

A5: (b) and setting variables is allowed too.

Q6, a refusal: (a) an error of its own, as the other refusals are, `EnvironmentPermissionError`, WL3019, extending `Error`; (b) `null`, as for a variable that is not set, so that a script cannot tell them apart.

A6: (b)

2026-11-18 (Claude): the answers read so: `std:env`, its class `Environment`, `import { Environment } from "std:env";`, its members static: `Environment.machineName`, `Environment.variable(name)`, `null` for one that is not set or not allowed, and `Environment.variables`, a map of those allowed. Under `wasp`, a site reaches the machine's name only with `"machineName": true`, and a variable only by the configuration's list, a name ending in `*` allowing those it begins, `"*"` any; under `weblingo`, a script reaches all of it, and sets a variable too, for the process alone, `Environment.setVariable(name, value)`. Two points need answering before it is built:

Q7, the permission's name: `wasp.config`'s `"environment"` already names the application's environment, `"environment": "production"`, whether errors are shown in their pages (specification, section 6.12, "Errors"; the WASP User Manual's table of settings). (a) the permission named after the module, `"env": { "machineName": true, "variables": ["PATH", "APP_*"] }`; (b) the permission `"environment"`, and the application's environment renamed, `"mode": "production"`, nothing kept for the old name; (c) other: say which.

A7: None of these. The permission named after the module, `"env"`. But also the application's environment renamed, `"mode": "production"`, nothing kept for the old name.

Q8, `setVariable` under `wasp`, whose scripts only read (A3): (a) it sets nothing and gives `null`, as a refusal to read gives `null` (A6); (b) a runtime error, WL3001, a `TypeError`; (c) an error of its own, `EnvironmentPermissionError`, WL3019.

A8: (c).

2026-11-18 (Claude): done with the answers, and landed: `std:env`, its class `Environment`, `machineName`, `variable(name)`, `variables` and `setVariable(name, value)`, static; under `wasp` only what `wasp.config`'s `"env"` allows, anything else `null`, `setVariable` an `EnvironmentPermissionError`, WL3019; under `weblingo` all of it, variables set too. The application's environment is `"mode"` now, `"environment"` no longer a setting. Read through the platform layer, `IWebLingoEnvironment`, `DotNetEnvironment` on .NET; native classes may declare static properties for it. Recorded in the specification (the decision register, sections 6.12, 7.15, 8.4 and 15.2), the Standard Library Reference (section 19), the language reference, the WASP User Manual and implementation.md. Found in passing, not changed: a class's static property, a declared class's or a native one's, is not read through an instance, `new P().x`, WL3006, though section 3.7 has a static member reached through an instance. `Examples/example1`'s footer, the user's uncommitted edit, still calls `env.machineName()`, which would be `<% import { Environment } from "std:env"; %>` and `Environment.machineName`, with `"env": { "machineName": true }` in its `wasp.config`.

Done (Claude, 2026-11-18, `901acfa`): `std:env`. Main's checks: the Release build, without warnings; every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which `Examples/example1`'s uncommitted footer's `env.machineName()` fails (WL2001); and the CLI, `weblingo` on both VMs and `wasp`. Tried on Windows only.

## [DONE] Class References

Explain: Can class references be assigned or passed around? Something like

```
class MyClass { 
  doIt() { ... } 
}

var cf = MyClass;

var cObj = new cf();
cObj.doIt();
```

If not, make sure they do.

2026-11-18 (Claude): yes, they can. A class is a value, of the class `Class` (specification, section 3.7): it may be assigned, `var cf = MyClass;`, compared, `cf == MyClass` being `true`, passed to a function and returned from one, and kept in an array or a map; and `new` makes an instance through any of them, `new cf()`, `new c()` in `function make(c) { return new c(); }`, `new (list[0])()`, the instance being `is MyClass` and calling `doIt()` as one `new MyClass()` makes does (tried with `weblingo`, 18 November 2026). Two things in the example are not WebLingo as it stands, both of `new`'s syntax rather than of class references: `new cf;` without parentheses is WL1001, "Expected '('", `new` always taking its arguments in parentheses, as C#'s does (section 3.7, `new Name(a, b)`); and `new` finds its class by a name and its members, `new lib.Point()`, or by any expression in parentheses, so that `new list[0]()` is WL1001 too, written `new (list[0])()`.

Q1, `new cf;` without parentheses: (a) allowed, the same as `new cf()`, as JavaScript allows it; (b) parentheses required, as now and in C#.

A1: (b)

Q2, `new list[0]()`: (a) allowed, `new` finding its class through indexes too, as JavaScript's does; (b) as now, a name and its members, anything else in parentheses.

A2: (b)

2026-11-18 (Claude): answered; with A1's and A2's (b), `new` stays as it is, so nothing changes: class references are assigned and passed around as the note above shows.

Done (Claude, 2026-11-18): explained; nothing to land, `new`'s syntax kept as section 3.7 has it.

## [DONE] Standard Modules, Imported: The Import Taught

The specification's section 9, "M0.20 parts", part 1, of "JSON import": a name a standard module exports, used without importing it, is WL2001, `'Json' is not defined; did you mean Json, from std:json?`, the same for a name it differs from only by case, `JSON`, and for every standard export, so that each name the later parts take out of every program's scope teaches its import from then on.

2026-11-18 (Claude): done, and landed: a name that is not defined, WL2001, ends its message with what it may have been meant as, a name in scope it differs from only by case, `did you mean count?`, else a standard module's export of that name or of one it differs from only by case, `did you mean Database, from std:db?`, the host's resolver loading the standard modules only for a program with such a name. The owner's decision is recorded in the specification (the decision register, sections 3.6 and 3.9), and its parts in section 9, "M0.20 parts".

Done (Claude, 2026-11-18, `8f616a9`): the import taught. Main's checks: the Release build, without warnings; every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which `Examples/example1`'s uncommitted footer's `env.machineName()` fails (WL2001); and the CLI, `weblingo` and `wasp`. Tried on Windows only.

## [DONE] Standard Modules, Imported: `std:json` and `std:html`

The specification's section 9, "M0.20 parts", part 2, of "JSON import": `Json` only by `import { Json } from "std:json";`, and `Html` only by `import { Html } from "std:html";` (A1, A2), every program's no longer; a page's own escaping keeping its use of them without an import; the examples, tests and documents that use them importing them.

2026-11-18 (Claude): done, and landed: `Json` only by `import { Json } from "std:json";` and `Html` only by `import { Html } from "std:html";`, `std:json` and `std:html` standard modules the compiler makes itself, as `std:system` is (`WebLingoStandardModules`); a page's own escaping uses `Html` without an import; the examples' handlers, the tests and the documents import them. Found in passing, not changed: while it was being built, a module the compiler made and the verifier accepted, its import of a module without a function or built-ins, crashed the register VM with a .NET `NullReferenceException` (`WebLingoVirtualMachine.Run`, its failure path given no error), where malformed bytecode should fail verification; a `.wlbc` made so by hand may do the same.

Done (Claude, 2026-11-18, `416853d`): `std:json` and `std:html`. Main's checks: the Release build, without warnings; every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which `Examples/example1`'s uncommitted footer's `env.machineName()` fails (WL2001); and the CLI, `weblingo` and `wasp`. Tried on Windows only.

## [DONE] Standard Modules, Imported: `std:io`

The specification's section 9, "M0.20 parts", part 3, of "JSON import": `write` and `writeLn` only by `import { write, writeLn } from "std:io";` (A1), `std:system`'s implicit import gone; a template's text and its `<%= %>` writing without an import, a block that calls `write` importing it; every script, sample, test, example, benchmark workload and document gaining its import.

2026-11-18 (Claude): done, and landed: `write` and `writeLn` only by `import { write, writeLn } from "std:io";`, `std:io` a standard module the compiler makes; an imported `writeLn` is still one direct call of the built-in, so nothing a script writes is slower, and an import of a module the compiler makes is not awaited, so a script that imports only `std:io` is not made `async` by it; a page's text and `<%= %>` write without an import, a block that calls `write` imports it. The samples, the benchmark's workloads, the examples' scripts, every test that writes and every example in the documents import what they write, the documents' examples run again with the output shown; the compiler's own tests call a test's `print` and `printLn` instead. `std:system` is still imported implicitly, for `delay` alone, until "The Rest" is answered.

Done (Claude, 2026-11-18, `c012cf6`): `std:io`. Main's checks: the Release build, without warnings; every test but `WaspServeTests.TheExampleApplicationServesItsPages`, which `Examples/example1`'s uncommitted footer's `env.machineName()` fails (WL2001); and the CLI, `weblingo` on the samples and `wasp`. Tried on Windows only.

## [DONE] Enhance std:fs

`std:fs` can only read and write text. Extend it with the ability to read and write binary files so that installation scripts could be written in weblingo without the use of COM.

2026-11-18 (Claude): what a script does with files now is text (`readText`, `writeText`, `appendText`, and a `File`'s lines), and `list(path)` gives a folder's names without telling a folder from a file (the standard library reference, section 14), so that a script cannot copy a folder whole without COM's `Scripting.FileSystemObject`, as `Scripts/install-weblingo.wl` does.

Q1, the functions for bytes: (a) `readBytes(path)`, giving `Bytes`, `writeBytes(path, bytes)` and `appendBytes(path, bytes)`, as `readText`, `writeText` and `appendText` are for text, a whole file at a time, each with its `…Sync` form; (b) those, and a `File` opened for bytes, `File.open(path, "rb")` or `"wb"`, its `read(count)` giving up to that many bytes, `null` at the end, and `write(bytes)`, for a file too large to hold whole; (c) other: say which.

A1: (b)

Q2, what else a script needs to copy a folder without COM: (a) `isFolder(path)`, and `copy(source, target)`, a file copied as it is, its bytes never reaching the script, each with its `…Sync` form; (b) `isFolder(path)` only, a script copying a file by `readBytes` and `writeBytes`; (c) neither, for now.

A2: (a)

2026-11-18 (Claude): done, and landed: `readBytes`, `writeBytes` and `appendBytes`, whole files as `Bytes`; a `File` opened for its bytes, `"rb"` or `"wb"`, `read(count)` up to that many, `null` at its end, and `write(bytes)` (A1); `isFolder(path)`, and `copy(source, target)`, a file copied as it is, replacing one at `target`, its errors naming both paths (A2); each with its `Sync` form, a site's capabilities reading a copy's source and writing its target. Guessed, and recorded as Proposed in the specification's section 15.2: one `read(count)` gives at most 16 MiB, a file opened for its bytes takes no encoding, and there is no `"ab"` yet. `Scripts/install-weblingo.wl` now copies WebLingo through `std:fs` (`windows.wl`'s `copyFolder`), tried on the published WebLingo, its 106 files copied identical, twice; it has not been run as administrator since.

Done (Claude, 2026-11-18, `6cc40d5`). Main's checks: the Release build, without warnings; the full suite, 3,922 tests, none failed or aborted; and the CLI, `weblingo` on the samples. Tried on Windows only; Linux not tried.

## [DONE] Enhance String.trimEnd)

`trimEnd` takes no argument in WebLingo. Make it work so this is possible:

        var prefix = folder.toLower().trimEnd("\\") + "\\";

2026-11-18 (Claude): C#'s `TrimEnd` takes the characters to trim, any of them, as many as end the string, and JavaScript's takes nothing; WebLingo's string methods have C#'s names (the specification, section 3.5, "String methods").

Q1, what the argument trims: (a) any of the characters it holds, as C#'s `TrimEnd(chars)` does, each a Unicode scalar value: `"a//".trimEnd("/")` is `"a"`, and `"x;,;".trimEnd(";,")` is `"x"`; (b) the argument as a whole, a suffix removed as often as it ends the string: `"x;,;,".trimEnd(";,")` is `"x"`, but `"x;,;".trimEnd(";,")` is `"x;"`.

A1: (a): the argument is a set of characters, each a Unicode scalar value, and `trim`, `trimStart` and `trimEnd` remove any of them from their end or ends, as C#'s `Trim(chars)` and Python's `strip(chars)` do: `"a//".trimEnd("/")` is `"a"`, and `"x;,;".trimEnd(";,")` is `"x"`. An empty argument trims nothing. To remove a fixed string, there are two methods of their own, `removePrefix(text)` and `removeSuffix(text)`, as Python 3.9 and Kotlin have them: each removes `text` once, only where the string begins or ends with it, and otherwise gives the string unchanged, `"report.txt".removeSuffix(".txt")` being `"report"`. Separate names keep Python's trap out of the language: with `strip(chars)`, people pass a suffix and get `"report.txt".rstrip(".txt")`, which is `"repor"`. The standard library reference warns of it, with that example, where it describes `trim`.

Q2, which methods take it: (a) `trim`, `trimStart` and `trimEnd`, as C#'s three do; (b) `trimEnd` only.

A2: (a)

2026-11-18 (Claude): done, and landed: `trim`, `trimStart` and `trimEnd` take a set of characters, each a Unicode scalar value, a surrogate pair one character, any of them removed from the end or ends, an empty argument trimming nothing, so that `folder.toLower().trimEnd("\\") + "\\"` works (A1, A2); and `removePrefix(text)` and `removeSuffix(text)`, a fixed text removed once, only where the string begins or ends with it; the Standard Library Reference warns of `"report.txt".trimEnd(".txt")` being `"repor"` where it describes `trim`. `"tea".trim(1)` is WL3001 now, a string being wanted, where it was WL2006.

Done (Claude, 2026-11-18, `995ee9c`). Main's checks: the Release build, without warnings; the full suite, 3,940 tests, none failed or aborted; and the CLI, `weblingo` on the samples. Tried on Windows only; Linux not tried.

## [DONE] Standard Modules, Imported: The Rest

The specification's section 9, "M0.20 parts", part 4, of "JSON import": what A1 leaves open.

Q1, the kinds of values no literal makes, `Bytes` and `BytesBuilder`, `DateTime` and `Regex`, whose values modules and methods give too (`crypto.sha256(x)` gives `Bytes`): (a) each in a module of its own, imported, `std:bytes` (`Bytes`, `BytesBuilder`), `std:datetime` (`DateTime`) and `std:regex` (`Regex`), a value of the kind keeping its members without an import, `b.toHex()`, only the class's own name needing one, `Bytes.from(text)`, `DateTime.now()`, `x is Regex`; (b) every program's, as `Integer` and `String` are, being kinds of values the VMs build in; (c) other: say which.

A1: (a): Bytes and BytesBuilder from std:bytes, DateTime from std:datetime, Regex from std:regex. A value of these kinds keeps its members without an import, and only naming the class needs one. The rule, written into the specification: a kind of value is every program’s exactly when the language’s own syntax makes it, as literals make Integer and String. 

Q2, `delay(milliseconds)`, `std:system`'s third function: (a) `std:tasks`'s, `import { delay } from "std:tasks";`, beside `all`, `any` and `timeout`, `std:system` then exporting nothing, and gone; (b) `std:io`'s, beside `write` and `writeLn`, `std:system` gone likewise; (c) `std:system` stays, `delay` its one export, imported.

A2: (c) for now. Let's move write and writeLn back to std:system.  `std:system` implicitly imported.

2026-11-18 (Claude): landed (`284ba05`), main's checks passed, as A1 and A2 read when it was begun: `Bytes` and `BytesBuilder` `std:bytes`'s, `DateTime` `std:datetime`'s, `Regex` `std:regex`'s, and `write`, `writeLn` and `delay` `std:system`'s, `std:io` gone, every one imported, `import { writeLn, delay } from "std:system";`, as (c) has it. A2's last sentence was added while it was being built.

Q3, A2's "`std:system` implicitly imported": (a) `write`, `writeLn` and `delay` in every program without an import, as before part 3 (M0.20.3), an import of them allowed and meaning the same, and the imports parts 3 and 4 gave every script, sample, test, example and document taken out again; (b) the same, those imports left where they are; (c) as landed, each imported, `std:system` being where they are imported from.

A3: (b)

2026-11-18 (Claude): A3 (b) done, and landed (`064a240`): `std:system` imported implicitly by every program again, `write`, `writeLn` and `delay` in its scope without an import, an import of them allowed and meaning the same, the imports parts 3 and 4 gave left where they are; a misspelt `Writeln` suggests the `writeLn` in scope.

Done (Claude, 2026-11-18, `284ba05` and `064a240`). Main's checks: the Release build, without warnings; the full suite, 3,939 tests, none failed or aborted; and the CLI, `weblingo` on the samples. Tried on Windows only; Linux not tried.

## [DONE] JSON import

You said [module is available] "in every program without an import, as `Json` is". JSON should not be treated special. It should require an import of `std:JSON`.

2026-11-18 (Claude): `Json` is a built-in class in every program now, the owner's decision of 16 November 2026 (specification, section 3.6, "JSON", "rather than a standard module"), which this changes: a program that names `Json` without importing it would be WL2001, nothing kept for the old way. It is used by `Examples/example2`'s and `example3`'s handlers, about twenty tests, and the documents, all of which would import it.

Q1, how it is imported: (a) its class, as `std:db`'s and `std:net`'s are, `import { Json } from "std:json";`, then `Json.parse(text)` and `Json.stringify(value)`, as now; (b) its functions, as `std:fs`'s and `std:crypto`'s are, `import json from "std:json";`, then `json.parse(text)` and `json.stringify(value)`.

A1: (a), and further: every standard module is imported, with nothing from a standard module available without an import.

- `Json` too: `import { Json } from "std:json";`.
- `write` and `writeLn` too: they move to a module of their own, `std:io`, as in `import { write, writeLn } from "std:io";.` A script with no import of them can’t write.
- Global without import: only what the language itself makes. That means the classes its literals and statements produce or need (`Object`, `Array`, `Map`, `Set`, `Error` and the runtime errors’ classes, `Task`, `Function`, `Class`, `Module`) and its constants (`Infinity`, `NaN`).
- The reason: a script’s imports list everything it depends on, and everything a host must grant it. No standard name is reserved in a script that doesn’t import it, and an unused standard module costs nothing.
- Teaching the import: where a script uses a standard module’s name without importing it, the compiler reports the name as undefined. It also suggests the import, e.g. “Json is not defined; did you mean Json, from std:json?”, or for JSON, “did you mean Json?”. It does the same for `write` and `writeLn` (`“from std:io”`), and for every other standard export.
- Templates: a template’s text and its `<%= %>` keep writing to the page without an import. That’s the template compiler’s own output, not a script’s call of write. A block of code that calls write itself imports it, as any script does.
- No backward compatibility: existing scripts, samples and tests gain their imports.

Q2, `Html`, the other library class every program has without an import, whose `Html.url(x)`, `Html.js(x)` and the rest a page calls for what its escaping cannot tell (section 13): (a) it stays so; (b) it moves to a module too, `std:html`, imported as Q1 says.

A2: (b), let's stay consistent.

2026-11-18 (Claude): too large to land in one piece, so split into parts, which the specification's section 9, "M0.20 parts", defines, each a task below: the import taught, `std:json` and `std:html`, `std:io`, and the rest. Read so, as the rule says, the classes literals make, `Boolean`, `Integer`, `Number`, `String` and `Decimal`, and the one `foreach` needs, `Iterator`, are every program's too; the runtime errors' classes are, the standard modules' failures' among them (`IOError`, `NetworkError`, `DatabaseError`, `ComError` and the permissions' errors), and `Module` and `Task`; and `std:system`'s third function, `delay`, and the kinds of values no literal makes, `Bytes`, `BytesBuilder`, `DateTime` and `Regex`, wait for the questions under the last part. Idle until its parts are done.

2026-11-18 (Claude): parts 1 to 3 done (`8f616a9`, `416853d`, `c012cf6`); idle, waiting for the answers to "The Rest"'s questions, its part 4.

2026-11-18 (Claude): every part landed, the specification's section 9, "M0.20 parts": the import taught (`8f616a9`), `std:json` and `std:html` (`416853d`), `std:io` (`c012cf6`), and the rest (`284ba05`, `064a240`), `std:io` gone and `std:system` imported implicitly again, as "The Rest"'s A2 and A3 have it.

Done (Claude, 2026-11-18): all its parts done, each with main's checks passed. Tried on Windows only.

## [DONE] Native BytesBuilder

`BytesBuilder` is written in WebLingo now, in the prelude (`WebLingoPrelude.cs`): an array of parts, which `toBytes()` joins with `Bytes.concat` (standard library reference, section 10). Make it native, as `Bytes` is: a growing buffer of .NET bytes, `add(value)` (bytes, an integer from 0 to 255, or a string as UTF-8) appending to it, `length` its count, and `toBytes()` giving immutable `Bytes`, which no later `add` changes. Its members and their errors stay as the reference has them, on both VMs, its growth counted against the heap's budget. Measure it against the prelude's on the benchmark, with a workload that builds bytes in many small pieces, and keep the native one only if it is faster, as A3 of "Native Modules" asks of the built-in classes. 

2026-11-18: waits for "Standard Modules, Imported: The Rest", whose Q1 decides the module `BytesBuilder` is imported from.

2026-11-18 (Claude): done, and landed: `BytesBuilder` a kind of value of its own, as bytes are, a buffer that doubles as it must, its growth within the heap's budget and given back when collected; `add`, `length` and `toBytes()` as the reference has them, on both VMs. Measured on a new workload, `bytes`, 300,000 small pieces, alternately against the class written in WebLingo: about 18 times as fast (11.2–12.7 ms against 216.9–222.4 on the register VM, 12.4–14.4 against 228.0–238.5 on the stack VM), a quarter of the instructions, a twelfth of the allocation; so it was kept, the other workloads within the machine's noise (the specification's section 5.6, "Bytes built from pieces"). Guessed, and recorded as Proposed in section 15.2: its text is its bytes so far, `BytesBuilder:[104, 105]`; `add`'s error names `add`, not `concat`; and its class is sealed, as `Bytes` is, where a class could extend the one written in WebLingo. Found while measuring, and fixed first (`e07f95c`): the collector gave back only an object's overhead for bytes, decimals and regular expressions, not what their content was charged, so that an execution making many of them ran out of its budget with almost nothing alive.

Done (Claude, 2026-11-18, `e07f95c` and `9e10eed`). Main's checks: the Release build, without warnings; the full suite, 3,950 tests, none failed or aborted; and the CLI, `weblingo` on the samples. Tried on Windows only; Linux not tried.

## [DONE] What `--version` Says

`weblingo --version` prints `WebLingo M0.6 (pre-alpha)`, the text approved on 12 November 2026 (specification, section 15.2, "CLI"), and `wasp --version` prints `WASP M0.12 (pre-alpha)`, which no document records (`WebLingoWaspCommandLine.Version`). The roadmap has since reached M0.20 (section 9), so both name milestones long past. Found 18 November 2026, when `weblingo` went on a machine's PATH; added by Claude at the owner's request.

Q1, what both print: (a) the last milestone landed, the same for both, since they are installed together, `WebLingo M0.19 (pre-alpha)` and `WASP M0.19 (pre-alpha)`, from one constant both command lines share (`wasp` references `WebLingo.Cli`), brought up to date by the task that finishes each milestone; (b) a version number, `WebLingo 0.19.0-pre-alpha` and `WASP 0.19.0-pre-alpha`, set once for the whole solution in `Directory.Build.props`, which then versions every assembly too, raised as (a) says; (c) they stay as they are until a first release.

A1: (b). Also print this as the first line when weblingo is invoked without parameters.

2026-11-18 (Claude): done, and landed: one version for the solution, set in `Directory.Build.props`, every assembly's, `0.20.0-pre-alpha`, M0.20 having landed whole; `weblingo --version` prints `WebLingo 0.20.0-pre-alpha` and `wasp --version` `WASP 0.20.0-pre-alpha`, and each prints it first when run without arguments (asked of `weblingo`; `wasp` shares its code). `Documents/implementation.md` now says the task that lands a milestone raises the version.

Done (Claude, 2026-11-18, `149391b`). Main's checks: the Release build, without warnings; the full suite, 3,951 tests, none failed or aborted; and the CLI, `weblingo` on the samples, and both `--version`s. Tried on Windows only; Linux not tried.

## [DONE] Extend Example2

Extend it with a shopping cart and examples of web form actions.

2026-11-18 (Claude): done, and landed: Lingo Aquatics has a shopping cart, `/cart`, kept in the database's new table `CartItems` by a random id that a cookie holds (`HttpOnly`, `SameSite=Lax`). Fish are added from a fish's page. The cart's one form updates every quantity at once, and its Remove and Empty buttons post the same form elsewhere through their `formaction`. Each change is a route handler exporting `post`, which changes the cart and redirects with a 303, so that a reload posts nothing again; a form no page of the site sends is refused, a 400. Checkout orders every fish in the cart in one transaction, or none. Adding a fish the cart holds already is an upsert, `INSERT … ON CONFLICT … DO UPDATE`. A line's total is worked out in WebLingo, the decimal price times how many: SQLite's product of the two has no declared type and comes back as a binary number, which does not mix with a decimal. `Examples/README.md` describes all of it, and `TheFishStoreServesItsDatabase` follows a cart from a new browser's first add to its checkout. Guessed: the cart is kept in the database, the cookie holding only its id; each change goes back to the cart, not to the page it came from; checkout is a page, as the single order's is.

Done (Claude, 2026-11-18, `268f9d3`). Main's checks: the Release build, without warnings; the full suite, 3,951 tests, none failed or aborted; and `wasp serve` on the example in a browser, fish added, one removed by its button, the cart reloaded without posting again. Tried on Windows only; Linux not tried.

## [DONE] Append Mode

`read(count)` returns at most 16 MiB at a time, and there’s no `"ab"` (append bytes) mode yet. Support append mode.

2026-11-18 (Claude): done, and landed: `File.open(path, "ab")` opens a file for its bytes, which it writes after the file's own, making a file that is not there, as `"a"` does for text, and takes no encoding, as `"rb"` and `"wb"` do; recorded as the owner's request in the specification (section 3.9, and its register) and in both references. `read(count)` keeps its 16 MiB: a larger count gives at most that many, and the next `read` the rest.

Done (Claude, 2026-11-18, `578d5d2`). Main's checks: the Release build, without warnings; the full suite, 3,954 tests, none failed or aborted; and `weblingo` on a script that appends bytes to a file there and to one that is not. Tried on Windows only; Linux not tried.

## [DONE] Standard Classes Extended, Part 1: Their Values' Methods

Specification, section 9, "Standard classes extended, parts", part 1: an instance of a class extending `String`, `Integer`, `Number`, `Boolean`, `Decimal`, `Regex` or `Function` has its value's methods, as it has its properties, `new Name("anna").toUpper()` giving `"ANNA"`.

2026-11-18 (Claude): done, and landed: an instance of a class extending a kind of value's class has its value's methods, `new Name("anna").toUpper()` giving `"ANNA"`, a string, and `new Strict("a+").test("aa")` `true`, an `extend` block's method using them through `this`; a member neither the class nor the value has stays the instance's WL3006, `An instance of Name has no member 'nope'.` A call `TryFindCallee` does not find on such an instance goes to its value's member in `WebLingoMembers.TryCall`, as a native class's fallback does, so neither VM's `Run` changed. The Language Reference shows it.

Done (Claude, 2026-11-18, `dbf6295`). Main's checks: the Release build, without warnings; the full suite, 3,966 tests, none failed or aborted; and the CLI, `weblingo` on the samples and on the Language Reference's new example. Tried on Windows only; Linux not tried.

## [DONE] Standard Classes Extended, Part 2: Bytes, BytesBuilder, DateTime, Json and Html

Specification, section 9, "Standard classes extended, parts", part 2: these five no longer sealed; a class extending one of the first three holding a value of its kind, `new Mine(…)` taking `Bytes.from(…)`'s arguments, nothing, or `DateTime.utc(…)`'s, and `new Bytes(…)` and `new DateTime(…)` making plain values; a class extending `Json` or `Html` reaching their static members.

2026-11-18 (Claude): done, and landed: `Bytes`, `BytesBuilder`, `DateTime`, `Json` and `Html` are no longer sealed. A class extending one of the first three holds a value of its kind, `new Day(2026, 11, 21)` a date as `DateTime.utc(…)` makes it, `new Blob("tea")` bytes as `Bytes.from(…)` makes them, `new Lines()` a builder, with the value's members, through part 1, and its class's own; `new Bytes(…)` and `new DateTime(…)` make plain values, as `new Integer(42)` does. A class extending `Json` or `Html` is a plain class reaching their static members; and the runtime's class methods, `Json.parse`, `Bytes.from`, `DateTime.utc`, `Integer.parse` and the like, are found for a class extending theirs too, `Day.utc(2026, 1, 1)` (`WebLingoMembers.Reaches`). Read so: `new` takes `Bytes.from(…)`'s arguments for a class extending `Bytes`. Found and fixed on the way: the binder lost a built-in class's `hold` when a spread in its source bound the `Spread` class, by resetting, not restoring, that it was binding the prelude. `new Json()` and `new Html()` make plain instances now, where they were WL3001.

Done (Claude, 2026-11-18, `fded9a3`). Main's checks: the Release build, without warnings; the full suite, 3,971 tests, none failed or aborted; and the CLI, `weblingo` on the samples and on the Standard Library Reference's new example. Tried on Windows only; Linux not tried.

## [DONE] Argument Errors That Name the Argument

`"tea".trim(1)` is WL3001 with a message saying a string was expected, which reads as if `"tea"` were the problem: the code is right (`trim` now takes an optional set of characters, so one argument is allowed, and an integer is a wrong kind of value, not a wrong number of arguments, WL2006), but the message does not say what should have been a string. A wrong kind of argument's message names the method and the argument, and says what it must be and what it was given: "`trim`'s argument must be a string of characters to trim, not an integer". Go through the built-in classes' methods that take arguments (strings', arrays', maps', `Bytes`', `DateTime`'s, `Regex`'s, and the standard modules' functions) for the same vague wording, and give each such message that form, with a test of the message, not only of its code.

2026-11-18 (Claude): every message names a member in single quotes now (`'trim' takes 0 to 1 arguments, not 2.`), and gives the value it was given, not only its kind (`not the integer 1`, `not a string`, as `WebLingoValueFormatter.Describe` gives it).

Q1, the message's form: (a) `'trim''s argument must be a string of characters to trim, not the integer 1.`, the name quoted as every message quotes it, its value as messages give it now; (b) `The argument of 'trim' must be a string of characters to trim, not the integer 1.`; (c) as you wrote it, unquoted, its kind only: `trim's argument must be a string of characters to trim, not an integer.`

A1: (b)

Q2, a method or a function of several arguments: (a) the argument named as the references name it, `'padStart''s width must be an integer, not a string.`; (b) by its place and its name, `'padStart''s first argument, the width, must be an integer, not a string.`

A2:  (b)

2026-11-18 (Claude): done, and landed: a wrong argument's message names the member, as the program called it, and, where the member has several parameters, the argument by its place and its name, then says what it must be and what it was given: `The argument of 'trim' must be a string of characters to trim, not the integer 1.`, `The first argument of 'padStart', the width, must be an integer, not a string.` Read so: A1's (b) and A2's (b) together, A2's place and name in A1's form. Every such message is made by one type, `WebLingoArgument`, for strings', arrays', `Bytes`', builders', dates' and regular expressions' methods, the built-in classes' class methods, `delay`, and the functions of `std:fs`, `std:db`, `std:http`, `std:net`, `std:crypto`, `std:env`, `std:tasks` and `windows:com`; an element of an argument is named through it, `The second argument of 'query', the values, must give @x null, a Boolean, …`; an array's own elements, which no argument holds, keep the member's words. Each has a test of its message: a conformance theory of 44 calls, and rows in the network, database, COM, file, crypto, environment, bytes and dates tests. Left as they are, not being arguments of those: a property's assignment, and the host's `Request`, `Response` and `Headers`. The specification records the rule (section 3.7, "Argument errors").

Done (Claude, 2026-11-18, `59a5cab`). Main's checks: the Release build, without warnings; the full suite, 4,017 tests, none failed or aborted; and the CLI, `weblingo` on the samples and on a script that makes each message. Tried on Windows only; Linux not tried.

## [DONE] Standard Classes Extended, Part 4: The Standard Modules' Classes

Specification, section 9, "Standard classes extended, parts", part 4. A standard module's class can be extended now, `class LogFile extends File {}`, but its instances are made by its factories, `File.open(path, mode)`, a task, and `File.openSync(path, mode)`, each of which makes a `File` even when called as `LogFile.open(…)`; `new LogFile()` makes an instance holding no file, its methods then failing.

Q1, how a class extending a standard module's class gets its instances: (a) its base's factories called on it make its own, `await LogFile.open(path, "a")` a `LogFile`, with the file's members and the class's own, `new LogFile(…)` staying as `new File()` is; (b) as (a), and `new LogFile(path, mode)` too, taking the synchronous factory's arguments where the class has one, `File.openSync`'s, and blocking as it does, and WL3001 where its only factory is asynchronous, `Database.open`, `Socket.connect`; (c) only `new`, as (b) has it, the factories making their own class's instances, as now.

A1: (d), other: a `LogFile` **is** a `File`, made in two steps, as .NET makes its connections (`new SqlConnection(cs)`, then `await conn.OpenAsync()`): first created, then opened. Each standard module's class whose instances its factories make gets instance methods that do what the factories did, `f.open(path, mode)`, a task, and `f.openSync(path, mode)` for `File`, `db.open()` for `Database` (its name given to `new Database("fish")`), `s.connect(host, port)` for `Socket`, and so for the others; `new File()` making a file not yet open, its other methods reporting so until it is. Its factories become shorthand for both steps, `File.open(path, mode)` making `new this()` and awaiting its `open(path, mode)`, so that called on a class extending it, `LogFile.open(path, "a")`, they make that class's instance, where its constructor takes no arguments. A class extending one keeps an ordinary constructor, synchronous, with arguments of its own, which never blocks on I/O:

    class LogFile extends File {
        constructor(prefix) { super(); this.prefix = prefix; }
        log(line) { this.write($"{this.prefix} {line}\n"); }
    }
    var log = new LogFile("[app]");
    await log.open("app.log", "a");

So there are no WL3001 cases for a class whose only factory is asynchronous, and the subclass's own constructor is provided for, which (a) to (c) did not provide. Opening an instance that is already open, or after its `dispose()`, is an error, its message saying which.

2026-11-18 (Claude): done, and landed, as A1 (d) has it: `new File()`, `new Socket()` and `new Database(name)` make instances not yet open, which `f.open(path, mode)` (a task) or `f.openSync(path, mode)`, `s.connect(host, port)` and `db.open()` open, each giving the instance; each is opened once, and not after its disposal, its other methods saying it is not open until it is, `This File is not open: open it first, await f.open(path, mode).` The factories are shorthand for both steps, written in WebLingo, `new this()` then the instance's method, or `new this(name)` then `open()` for `Database`, so that `LogFile.open(path, "a")` makes a `LogFile`, its constructor run with no arguments. A factory takes the class it is called on as its receiver: a static method's lookup binds it to that class, and the verifiers allow a static method a receiver. Two of the module's errors changed: `Database.open(5)`'s message names `'Database'`, whose constructor checks the name, and `Socket.connect`'s argument errors come through its task. The three are the standard modules' classes made by factories; `windows:com`'s objects are made by its module, and keep their making. Guessed: `open`, `openSync` and `connect` give the instance, so that `var f = await new File().open(path, mode);` reads as one step.

Done (Claude, 2026-11-18, `76a1e84`). Main's checks: the Release build, without warnings; the full suite, 4,030 tests, none failed or aborted; and the CLI, `weblingo` on the samples, on the Standard Library Reference's new example and on a database made, then opened. Tried on Windows only; Linux not tried.

## [DONE] Tasks Completed by Hand

A task that a program completes itself, rather than one an `async` function's call or the host's operation gives, as C#'s `TaskCompletionSource` and the `resolve` and `reject` of JavaScript's `new Promise(executor)` make one: for queues, events, and test doubles, where the code that gives the result is not the code that started the wait. Independent of Q1 of "Standard Classes Extended, Part 3: Task and Module": a class extending `Task` holds such a task as it holds any other. [It IS a Task]

```javascript
var t = new Task();
// later, elsewhere:
t.complete(value);          // or t.fail(error)
var result = await t;
```

Q1, its form, given A1 of "Standard Classes Extended, Part 3: Task and Module" (a `Job` is a task, completed by hand): `new Task()` makes a pending task, and `t.complete(value)` and `t.fail(error)` complete it, a class extending `Task` the same through its `super()`, so that no factory (`Task.source()`, `Task.pending()`) is needed; whoever holds such a task may complete it, C#'s split between a source and its task being given up for it; and a task that `new` did not make, an `async` function's call's, `Task.all`'s, `Task.any`'s, `Task.timeout`'s or the host's, cannot be completed by hand, `complete` or `fail` on one being an error, its message saying so. Agreed?

A1: Yes.

Q2, completing it a second time, or failing it once completed: (a) a runtime error, WL3001; (b) nothing happens, `complete` and `fail` giving whether they completed it, as C#'s `TrySetResult` does.

A2: (b)

Q3, a task nothing ever completes, when the execution ends: (a) cancelled with the execution's other running tasks (§3.8, "The end of an execution"), with nothing reported; (b) reported, as an exception nothing awaits is, unless it was forgotten.

A3: (a): a task made by `new Task()` that is still pending when the execution ends is cancelled with its other running tasks (§3.8, "The end of an execution"), and nothing is reported, a pending task not being a failure: a signal that this run did not need, a consumer still waiting at shutdown, a test double the test did not use. And a program stuck waiting for such a task gets an error, not a hang: where nothing is ready to run, no timer is due, no work is out on the host, and the execution waits for a task only a `complete` or a `fail` could finish, a runtime error is raised at the `await` that waits for it, an instance of a class of its own extending `Error`, with a code of its own, its message naming where the task was made and where it is awaited, "The program waits for a task nothing can complete: made at line 12, awaited at line 30.", as Go stops a program whose goroutines are all asleep. It travels outward as any exception does, failing the tasks of the `async` functions it leaves, so that a program may catch it, to log it and go on or to shut down in order, and one that does not stops as for any uncaught exception. A program that is stuck again gets it again, at the `await` it is stuck at then. The class's name and its code, in the WL3xxx range, are to be assigned.

2026-11-18 (Claude): done, and landed: `new Task()` makes a pending task, which `t.complete(value)` and `t.fail(error)` complete, whoever holds it, each giving whether it completed it (A2); `complete` or `fail` on a task `new` did not make is WL3001, its message saying so; a task still pending when the execution ends is cancelled with the others, nothing reported (A3). A program stuck awaiting such a task gets a `DeadlockError`, WL3020, at that `await`, `The program waits for a task nothing can complete: made at line 13, awaited at line 15.`, which it may catch, and again where it is stuck again; its class's name and its code are Proposed, as A3 left them to be assigned. The lines are the host's, which knows the files' texts: the execution context's `Lines`, which the command line gives for a `.wl` and a `.wlbc`, and the web host for a page. Guessed: where several functions wait on such tasks, the error goes to the first waiting on the first such task the heap holds; a task awaited only through `Task.all` or `Task.any` is not found so, and its program still ends with WL3009, a deadlock of tasks no `new` made. `new` on a kind of value's class runs through a helper now, kept out of `Run`; measured against `main`, five rounds alternately, `loops`, `calls`, `closures` and `collections` stayed within the machine's noise.

Done (Claude, 2026-11-18, `3cfb707`). Main's checks: the Release build, without warnings; the full suite, 4,042 tests, none failed or aborted; and the CLI, `weblingo` on the samples, on a program that completes tasks by hand, and on one stuck, run as a `.wl` and as a `.wlbc`. Tried on Windows only; Linux not tried.

## [DONE] Standard Classes Extended, Part 3: Task and Module

Specification, section 9, "Standard classes extended, parts", part 3: `Task` and `Module` no longer sealed. Each is a kind of value whose values no `new` makes: a task comes from calling an `async` function, or from `Task.all`, `Task.any` and `Task.timeout`, and a module from an import.

Q1, what `new Job(…)` takes, for `class Job extends Task {}`: (a) a task, which it holds, `new Job(fetch(url))`, `await job` awaiting that task and `job.forget()` forgetting it; (b) an `async` function, which it calls at once, holding the task the call gives, `new Job(async () => { … })`, as JavaScript's `new Promise(executor)` takes a function; (c) nothing: a class of its own, its instances holding no task, `await job` giving the instance, as it gives any value that is not a task.

A1: (c), but as a task, not merely an instance: a `Job` **is** a task, not a holder of one. `new Job(…)` makes a pending task, its constructor's `super()` taking nothing, and the job is completed by hand, `job.complete(value)` or `job.fail(error)`, as "Tasks Completed by Hand" has it (its Q1 (b), a task that is its own source, which this answer takes). `await job` waits for it, `job.forget()` forgets it, `Task.all([job, …])` and `job is Task` work as for any task, and its class's own members come first, as a subclass's do. A class that only wants to hold a task someone else started holds it in a field, by composition, without extending `Task`.

Q2, what `new Plugin(…)` takes, for `class Plugin extends Module {}`: (a) a module, which it holds, `new Plugin(await Module.import("a.wl"))`, the module's exports read through the instance, `plugin.run()`; (b) nothing: a class of its own, its instances holding no module.

A2: (b), but as a module, not merely an instance: a `Plugin` **is** a module, not a holder of one. `new Plugin(name)` makes a module defined in code rather than in a file, its constructor's `super(name)` taking what `Module`'s own making takes, a name, and `new Module(name)` making a plain, empty one. Its members are its exports, its own and its class's, and its body is its `exec()`, which it may override; `p is Module` is true, and it goes wherever a module goes. A module made so has no path, so `import` cannot find it; registering one under a name an import can find, and a file whose module is of a class it names (`report.wl` importing as a `Plugin`), are questions of their own, for later. A class that only wants to hold a module someone imported holds it in a field, by composition, without extending `Module`.

2026-11-18 (Claude): `Module`'s half done and landed (`656a5f0`): `new Module(name)`, and a class extending `Module`, its instances modules defined in code, with fields, their class's methods and an overridable `exec()`. `Task`'s half waits for "Tasks Completed by Hand", taken up first, since a `Job` is a task completed by hand.

2026-11-18 (Claude): `Task`'s half done, and landed, after "Tasks Completed by Hand": a class extending `Task` makes tasks completed by hand, `new Job(…)` a pending task, `job.complete(value)` and `job.fail(error)`, awaited, forgotten and combined as any task, its class's own members first, `job is Task`, written `Job:task`. Such a task does not record where `new` made it, so that a program stuck on one is told only where it awaits it, `new` on every class not being slowed to record it.

Done (Claude, 2026-11-18, `656a5f0` and `78a3e9d`). Main's checks: the Release build, without warnings; the full suite, 4,042 tests, none failed or aborted; and the CLI, `weblingo` on the samples and on the Standard Library Reference's examples of a module defined in code and of a `Job`. Tried on Windows only; Linux not tried.

## [DONE] Standard Classes Extended, Part 5: Held Values Taken as Their Kind's

Specification, section 9, "Standard classes extended, parts", part 5: wherever a built-in takes a value of a kind, a string, an integer, bytes, a date or a regular expression, it takes an instance holding one, as that value, `"ab".startsWith(new Name("a"))`, `fs.readText(new Path("a.txt"))`, `[1, 2][new Index(1)]`. Comes after "Argument Errors That Name the Argument", whose argument checks it shares.

2026-11-18 (Claude): done, and landed: wherever a built-in takes a value of a kind, it takes an instance holding one as that value, `"ab".startsWith(new Name("a"))`, `fs.readText(new Path("a.txt"))`, `[1, 2][new Index(1)]`, `crypto.sha256(new Blob("x"))`: a string's, bytes', a date's, a regular expression's, a map's and a set's methods, an array's that take a position or a separator, the built-in classes' methods, indices and keys, native modules' functions and native classes' members, given a copy of their arguments with each held value unwrapped where any is held, and the elements `Bytes.concat`, `Bytes.from` and `join` read. What keeps what it is given keeps the instance, `push`, `insert`'s value, a task's `complete`, `setConstant`; and what writes it, `write`, writes the instance, `Name:"printed"`. Measured against `main`, four rounds alternately, `calls`, `strings`, `collections` and `templates` the same or faster.

Done (Claude, 2026-11-18, `c9fcbe9`). Main's checks: the Release build, without warnings; the full suite, 4,047 tests, none failed or aborted; and the CLI, `weblingo` on the samples and on a script that gives held values to strings', maps', bytes', dates', regular expressions' members, `std:fs` and `std:crypto`. Tried on Windows only; Linux not tried.

## [DONE] BytesBuilder Extendability

A builder prints as `BytesBuilder:[104, 105]`, and nothing can extend `BytesBuilder` now, the same as `Bytes`. Previously a class could extend it — make it extensible again.

All standard classes must behave like real classes from the script's perspective.

2026-11-18 (Claude): what a class extending a standard class gets now: one extending `Array`, `Map` or `Set` has all their members; one extending `String`, `Integer`, `Number`, `Boolean`, `Decimal`, `Regex` or `Function` holds a value of that kind and has its properties, but none of its methods, `class Name extends String {}` giving `new Name("anna").toUpper()` WL3006, and `class Strict extends Regex {}` giving `s.test(x)` WL3006, which this task fixes whatever the answer; and `Bytes`, `BytesBuilder`, `DateTime`, `Task`, `Module`, `Iterator`, `Json` and `Html`, and the standard modules' classes (`File`, `Database`, `Socket`, …), cannot be extended at all.

Q1, which classes a class may extend, its instances having all their base's members: (a) every standard class, the standard modules' too, an instance of a class extending one holding its base's value where the base has a kind of value, `new Mine(…)` taking what its base's own making takes (`DateTime.utc(…)`'s arguments for a class extending `DateTime`, which has no `new`); (b) every class of a kind of value, `Bytes`, `BytesBuilder`, `DateTime` and `Regex` among them, but not `Task`, `Module`, `Iterator`, `Json`, `Html` or the standard modules' classes, which stay sealed; (c) `BytesBuilder` again, with the members of every class that may be extended now, the rest staying sealed.

A1: No sealed classes. (a)

2026-11-18 (Claude): too large to land in one piece, so split into parts, which the specification's section 9, "Standard classes extended, parts", defines, each a task below, the decision recorded in its section 3.7, "No sealed classes". Found while looking: `Iterator` and the standard modules' classes can be extended already, but a factory called on a class extending one makes the base's instance, `LogFile.openSync(path, "w")` a `File`; and a held value is not taken where a built-in takes its kind, `"ab".startsWith(new Name("a"))` WL3001. Idle until its parts are done.

Done (Claude, 2026-11-18): every part landed, the specification's section 9, "Standard classes extended, parts": their values' methods (`dbf6295`); `Bytes`, `BytesBuilder`, `DateTime`, `Json` and `Html` (`fded9a3`); `Module` (`656a5f0`) and `Task` (`78a3e9d`), after "Tasks Completed by Hand" (`3cfb707`); the standard modules' classes, made, then opened (`76a1e84`); and held values taken as their kind's (`c9fcbe9`). No standard class is sealed now; `BytesBuilder` may be extended again, a class extending it holding a builder. Main's checks passed after each part. Tried on Windows only; Linux not tried.

## [DONE] WebLingo Examples

Create an extensive set of WebLingo script examples showcasing the capabilities of the language, but also its limitations and deliberate choices for reasons of security, etc.

Put the examples into `Examples.wl`. Rename existing Examples, into `Examples.wasp`.

2026-11-18 (Claude): done, and landed: `Examples.wl`, read as a folder, as "into" has it, holds 25 scripts that show the language at work, values, strictness, variables, strings, regular expressions, collections, functions, control flow, classes, classes of values, classes built while a program runs, errors, disposal, asynchrony, modules, a script's parameters, files, bytes, JSON and dates, cryptography, a database, the environment, the network, COM and the execution's limits, with its choices made on purpose and what it does not have yet in its `README.md`; each ends with what it writes, `/* Output: … */`, and its errors where it fails on purpose, `/* Errors: … */`; and `does-not-compile`, 13 scripts the compiler refuses on purpose, each ending with its errors. `ExamplesTests` runs every one, from its source on the register VM and from its WLBC on the stack VM, against its block, the network's only checked, and COM's on Windows only. `Examples` is `Examples.wasp`, every reference to it changed, the tests', the documents', `Scripts`' and the sites' `web.config`s'. Writing them found a binder bug, fixed: binding a built-in written in WebLingo, `new Error(…)` or an array's `map`, in a property's accessor lost the accessor, so that `field` after it was WL2001. And two questions, asked in tasks of their own: "Text Written on Windows: UTF-8" and "Mathematics".

Done (Claude, 2026-11-18, `37d4e0c`). Main's checks: the Release build, without warnings; the full suite, 4,089 tests, none failed or aborted; and the CLI, `weblingo` on the samples and on the examples, `17-parameters.wl` with its parameters, `10-classes.wl`, and `check` of `does-not-compile/missing-semicolon.wl`. Tried on Windows only; Linux not tried.

## [DONE] Minimal WebLingo Installer: Windows

Create a zip containing the absolute minimum to install WebLingo on a pristine machine, so that a human could easily do it. Provide `readme.md` in it with the steps. Make the process as simple as possible. The end result is supposed to be weblingo.exe functioning as a script execution engine on the PATH capable of running further installation scripts.

2026-11-18 (Claude): WebLingo is published now for a machine that has .NET 10's runtime, which a pristine machine has not. Planned, unless you say otherwise: the readme has the human unzip it anywhere and run, as administrator, `weblingo.exe run install.wl` in that folder, a WebLingo script that copies it into `C:\Program Files\WebLingo` and puts that on the PATH; x64 only, for now.

Q1, .NET on a pristine machine, for this installer, and so for WASP's and Linux's: (a) the zip carries .NET's runtime with WebLingo (self-contained, about 70 MB unzipped), nothing else to install; (b) the readme has the human install .NET 10's runtime first, from Microsoft, and the zip holds WebLingo's own files, a few MB.

A1: Make two versions of the zip: one that assumes .NET is already installed, and one that bundles it inside.

2026-11-18 (Claude): done, and landed, as A1 has it, two zips, for Windows on x64, which `Installers/Windows/package.ps1` makes into `WebLingo/publish/installers`: `WebLingo-0.20.0-pre-alpha-win-x64-self-contained.zip`, 37 MB, .NET's runtime inside, and `…-framework-dependent.zip`, 3 MB, which needs .NET 10's runtime, each `weblingo` and the standard modules, without symbols or SqlClient's resources in other languages, in a folder `WebLingo`, beside `install.wl`, the scripts it shares, `windows.wl` and `weblingo.config`, and `readme.md`, a human's steps: unblock the zip, unzip it, and, in a console run as administrator, in its folder, `WebLingo\weblingo.exe run install.wl`, which copies `WebLingo` into `C:\Program Files\WebLingo` and puts that on the machine's `PATH`, once, then `weblingo --version` in a new console. `install.wl` is in `Scripts`, sharing `windows.wl`, which gains the `PATH`'s function, `add-weblingo-to-path.wl` using it too. Guessed: the packaging script is PowerShell, as `publish.ps1` is, a build step on a machine with the SDK rather than a script that sets a machine up, and WebLingo has no zip of its own to write one with; `wasp` is left to WASP's installer. The zips' `weblingo` ran `check` and `run` of their `install.wl`, which stopped, not being run as administrator, as it should; `install.wl` was not run as administrator, which would have installed over the WebLingo this machine's IIS sites run, nor on a machine without WebLingo or .NET. Not signed: Windows's SmartScreen warns, and Smart App Control, where it is on, blocks it, as the readme says.

Done (Claude, 2026-11-18, `217c916`). Main's checks: the Release build, without warnings; the full suite, 4,089 tests, none failed or aborted; and the CLI, `weblingo` on the samples, and each zip's `weblingo` on its `install.wl`, as above. Tried on Windows only; Linux not tried.

## [DONE] Minimal WASP Installer: Windows

Create a zip containing the minimum needed to install WASP on a computer on which WebLingo is already installed. Include a `readme.md` for a human to follow. Provide a script `iis-install.wl` that does everything needed to make WASP applications run on an IIS.

2026-11-18 (Claude): waits for "Minimal WebLingo Installer: Windows"'s Q1, which decides whether WASP's zip carries .NET's runtime too. Planned: `iis-install.wl` turns IIS on (its features, through DISM), downloads and installs Microsoft's ASP.NET Core Hosting Bundle for .NET 10 where IIS has no ASP.NET Core Module, which IIS needs to run `wasp` whatever the answer, and copies `wasp` in beside `weblingo`.

2026-11-18 (Claude): the Windows installer's Q1 is answered, two zips, one with .NET's runtime and one without; queued behind "Minimal WebLingo Installer: Windows", whose zips this one's builds on.

2026-11-18 (Claude): done, and landed: two zips, as WebLingo's are, which `Installers/Windows/package.ps1` makes with WebLingo's, `WASP-0.20.0-pre-alpha-win-x64-self-contained.zip`, 12 MB, and `…-framework-dependent.zip`, 0.2 MB, for a machine with WebLingo installed from the zip of the same version and kind, each the folder `WASP`, what `wasp` adds, with `wasp-needs.json`, the SHA-256 of each of WebLingo's files it needs, `iis-install.wl`, `add-wasp-site.wl`, the scripts they share and `readme.md`, a human's steps and a site's `web.config` and first page. As administrator, `weblingo run iis-install.wl` refuses a WebLingo of another build or kind, which `wasp` would not run with; turns IIS on, unless it is, through DISM; installs the ASP.NET Core Module, unless IIS has it, from the Hosting Bundle Microsoft's release metadata for .NET 10 names, 10.0.15 today, downloaded with `curl.exe` and checked against the SHA-512 it gives, the module alone for a self-contained `wasp`, repaired where it was installed before IIS, then restarts IIS; and copies `WASP` into `C:\Program Files\WebLingo`, the pools of the sites that run its `wasp` stopped meanwhile, through `iis.wl`'s `copyIntoInstallation`, which `install-weblingo.wl` uses now too, and which restarts the pools it stopped where the copy fails, as `install-weblingo.wl` did not. Guessed: two zips, mirroring WebLingo's, since a self-contained WebLingo and a framework-dependent `wasp` cannot share a folder, .NET's host then finding the runtime the folder holds and not the one installed; the Hosting Bundle's latest 10.0 release, rather than one named in the script; and `add-wasp-site.wl` in the zip, so that a site can be added. Tried: each WASP zip over its WebLingo zip's folder, in a scratch folder, `wasp` serving example1's pages and `weblingo` the examples, and the needs' check passing there and refusing a WASP of the other kind; the metadata read and `certutil`'s hash parsed, by a script of those pieces; `iis-install.wl` and `install.wl`, from their zips, stopping, not run as administrator, as they should. Not run as administrator: neither `iis-install.wl` nor the changed `install-weblingo.wl`, nor on a machine without IIS, which only a run as administrator, on a machine of its own, would try.

Done (Claude, 2026-11-18, `718d2f7`). Main's checks: the Release build, without warnings; the full suite, 4,089 tests, none failed or aborted; and the CLI, `weblingo check` of every script in `Scripts`, and the zips as above. Tried on Windows only; Linux not tried.

## [DONE] Text Written on Windows: UTF-8

2026-11-18 (Claude, found writing "WebLingo Examples"): `weblingo` and `wasp` write their output and their errors in the console's code page on Windows, .NET's default, so that a letter outside it is lost or replaced: run through a pipe, `weblingo run 05-strings.wl` wrote `"ZOLW"` for `"ŻÓŁW"`, and an `€` as `?`, though the strings are right, as `ExamplesTests`, which reads the output in the process, shows; `wasp run page.wasp > page.html` would do the same. Linux writes UTF-8. A served page is not affected: its response is UTF-8.

Q1, the text `weblingo` and `wasp` write on Windows: (a) UTF-8, without a byte-order mark, to a file or a pipe, and to a console through Windows's own Unicode console output, whatever its code page, as Python and Node.js write; (b) UTF-8 wherever it goes, the console's code page set to UTF-8 while they run, and set back after; (c) as now, the console's code page.

A1: (a)

2026-11-18 (Claude): done, and landed, as A1 has it: `weblingo` and `wasp` write UTF-8 without a byte-order mark to a file or a pipe, and to a console through .NET's Unicode console output, `WriteConsoleW`, whatever its code page, which is left as it is (`WebLingoConsoleText`, which both programs open first, `Console.Out` and `Console.Error` set to it, so that ASP.NET Core's logging under `wasp serve` writes so too). Tried: through a pipe and into a file, `weblingo run 05-strings.wl` writing `"ŻÓŁW"` as UTF-8, an error's message too, and `wasp run` a page's `·`; in a console of code page 437, the letters right and the code page still 437 after. `ConsoleTextTests` runs `weblingo` as a program of its own and checks its bytes, and fails without the change. The `wasp` IIS runs is the one installed before, until WebLingo is published and installed again.

Done (Claude, 2026-11-18, `da045c6`). Main's checks: the Release build, without warnings; the full suite, 4,090 tests, none failed or aborted; and the CLI, `weblingo` on the samples and the examples, through a pipe and in a console, and `wasp run` on a page. Tried on Windows only; Linux not tried, where .NET wrote UTF-8 already.

## [DONE] Mathematics

2026-11-18 (Claude, found writing "WebLingo Examples"): WebLingo has no mathematical functions, no `sqrt`, `floor`, `round`, `abs`, `min`, `max` or power, and no integer division, `/` always dividing exactly; a number becomes an integer only through a decimal, `new Integer(new Decimal(x))`, where it is whole, or through its text, `Integer.parse($"{x:F0}")`. The Language Reference's "What is not in the language yet" says so now.

Q1, where they are: (a) a standard module, `std:math`, imported, `import math from "std:math";`, then `math.sqrt(2.0)`, `math.floor(x)`, `math.max(a, b)` and `math.PI`, as the standard modules' rule has every standard name imported; (b) methods of the values, every program's, `x.sqrt()`, `x.floor()`, `a.max(b)`, and constants of `Number`, `Number.PI`; (c) both, the values' methods and the module.

A1: (a)

Q2, a number made an integer: (a) `floor`, `ceiling`, `round` and `truncate` each giving an integer, `2.7.floor()` being `2`, an error where the number is beyond int64, NaN or infinite; (b) each giving a number, `2.0`, as C#'s `Math.Floor` does, and `new Integer(x)` converting a whole number, `new Integer(2.0)` being `2`.

A2: (a)

Q3, integer division: (a) none, `(a - a % b) / b` then made an integer as Q2 has it; (b) a method, `a.div(b)`, rounding towards zero, as `%` pairs with it, `-7.div(2)` being `-3`; (c) an operator of its own, Python's `//`, rounding down.

A3: Could we do `\`?

2026-11-18 (Claude): yes, `\` can be integer division, as Visual Basic's is: outside a string it means nothing yet, a character that begins nothing (WL1003), so no program changes. A few points of it, and of `std:math`, are yours to decide, below; the module waits for them, so that it lands whole.

Q4, `std:math`'s members, its first set: `math.abs(x)`, `math.sign(x)`, `math.min(a, b, …)` and `math.max(a, b, …)`, each keeping its argument's kind, an integer's an integer, a decimal's a decimal; `math.floor(x)`, `math.ceiling(x)`, `math.round(x)` and `math.truncate(x)`, each giving an integer, as A2 has it, an integer itself; `math.sqrt(x)`, `math.pow(x, y)`, `math.exp(x)`, `math.log(x)`, `math.log10(x)`, `math.log2(x)`, `math.sin(x)`, `math.cos(x)`, `math.tan(x)`, `math.asin(x)`, `math.acos(x)`, `math.atan(x)` and `math.atan2(y, x)`, each taking an integer or a number and giving a number, as IEEE 754 has them, `math.sqrt(-1.0)` being `NaN`; and the constants `math.PI` and `math.E`. Yes?

A4: Yes.

Q5, `math.round`'s halfway: (a) away from zero, `2.5` to `3` and `-2.5` to `-3`, as most people round by hand, C#'s `MidpointRounding.AwayFromZero`; (b) to the even integer, `2.5` to `2` and `3.5` to `4`, as C#'s `Math.Round` and Python's `round` do unless told otherwise; (c) up, `2.5` to `3` and `-2.5` to `-2`, as JavaScript's `Math.round`.

A5: (a)

Q6, `a \ b`: (a) rounding towards zero, as C#'s and Java's integer division and Visual Basic's `\` do, pairing with `%`, whose sign is the dividend's, so that `a == (a \ b) * b + a % b`, `-7 \ 2` being `-3`; (b) rounding down, as Python's `//`, `-7 \ 2` being `-4`. Either way: integers only, a number or a decimal WL3001, made an integer first by `math.floor` and the others; by 0 a `DivisionByZeroError`, WL3013, as `%`'s; the smallest integer by -1 an `OverflowError`; as tight as `*`, `/` and `%`; with `\=`, as `%=`; and a class's `static operator \`, as its `%`.

A6: (a)

2026-11-18 (Claude): done, and landed in two parts, as answered. `std:math` (`bb8f667`): a standard module, imported, `import math from "std:math";`, with Q4's members, `math.round` halfway away from zero (A5), `floor`, `ceiling`, `round` and `truncate` giving integers (A2); for it, a native module's function may have a rest parameter, `math.max(a, …)`, and a native module may export constants, `math.PI`, which no importer may assign. Integer division (`c23d952`): `a \ b`, rounding towards zero, pairing with `%` (A6), with `\=` and a class's `static operator \`, an operation of its own on each VM. Guessed, and recorded in the specification as Proposed: a number or a decimal beyond 64 bits, infinity among them, an `OverflowError`, WL3002, and NaN WL3001; `min` and `max` taking one value or more, compared as `<` compares, a number and a decimal not compared, NaN where one is NaN; a decimal WL3001 to the functions giving numbers; and the error of `\` on a string or a number naming what it was given, with how to make a number an integer. Measured against `main` on the benchmark, alternately, for the VMs' dispatch: every workload the same, the stack VM's `loops` and `closures` measured eight rounds, within the machine's noise. `MathTests` checks `std:math`'s results and every error's message on both VMs, `OperatorTests` runs `\` on every pair of edge values through both VMs, and the conformance tests its results and errors. The editors' colouring has `\`, and `%`, which it lacked.

Done (Claude, 2026-11-18, `bb8f667` and `c23d952`). Main's checks, after each: the Release build, without warnings; the full suite, 4,125 tests, then 4,148, none failed or aborted; and the CLI, `weblingo` on the samples and on `Examples.wl/02-values.wl`, which uses both. Tried on Windows only; Linux not tried.

## [DONE] The Machine's Architecture and System: `std:env`

2026-11-19 (Claude, at the owner's question whether WebLingo can tell the machine's bitness): it cannot, directly. `std:env` has only `Environment.machineName`, `variable(name)`, `variables` and `setVariable`. On Windows a script works it out from Windows's variables, `Environment.variable("PROCESSOR_ARCHITECTURE")`, `"AMD64"`, `"x86"` or `"ARM64"`, the running program's, with `PROCESSOR_ARCHITEW6432` for a 32-bit program on 64-bit Windows, or `%ProgramFiles(x86)%`, as the installers' scripts now do, or from WMI through `windows:com`; on Linux from nothing; and a site under `wasp` reads those variables only as its `wasp.config` allows. Proposed: members of `std:env` that say it on every platform, as .NET's `RuntimeInformation` reports it, through the platform layer (`IWebLingoEnvironment`).

Q1, what `std:env` gives: (a) `Environment.architecture`, the machine's, and `Environment.processArchitecture`, the running program's, which differ for a 32-bit program on 64-bit Windows, each `"x64"`, `"x86"`, `"arm64"` or `"arm"`, as .NET's runtime identifiers name them, and `Environment.os`, `"windows"`, `"linux"` or `"macos"`; (b) (a) without `os`; (c) Booleans, `Environment.is64Bit` and `Environment.is64BitProcess`, as .NET's `Environment` names them, which cannot tell ARM from x64.

A1: (a)

Q2, a site under `wasp`: (a) reads them without a permission, since they tell no more than the server's software does; (b) only where its `wasp.config` allows them, `"env": { "platform": true }`, as it allows the machine's name.

A2: (b)

2026-11-19 (Claude): done, and landed, as answered: `Environment.architecture`, `Environment.processArchitecture` and `Environment.os` (A1), a site reading them only where its `wasp.config` says `"env": { "platform": true }`, `null` otherwise (A2). Through the platform's `IWebLingoEnvironment`, as .NET's `RuntimeInformation` reports them. Guessed, and recorded in the specification as Proposed: another architecture's name in small letters, as .NET names it, `"freebsd"` for FreeBSD and `"other"` for another system. A 32-bit `weblingo`, published for the trial outside the repository, said `"x64"` for the machine and `"x86"` for itself. `wasp.config`'s message for a wrong `"env"` names `"platform"` now, and says each setting is optional. `EnvironmentTests` and `WaspTests` check them, and `22-environment.wl` shows them.

Done (Claude, 2026-11-19, `cfae134`). Main's checks: the Release build, without warnings; the full suite, 4,151 tests, none failed or aborted; and the CLI, `weblingo` on `22-environment.wl`, and `wasp run` on a page with and without the permission. Tried on Windows only; Linux not tried.

## [DONE] Console Input: `readLine`

A running script cannot read what its user types: `std:io` has `write` and `writeLn` only, and nothing reads the console's standard input, so that a script's input is its parameters (`weblingo run script.wl name=Anna`), files, environment variables, the network, or, under WASP, a request's form. Add reading to `std:io`, beside writing, for scripts run by `weblingo`, interactive and piped alike (`type data.txt | weblingo run filter.wl`):

```javascript
import { write, readLine } from "std:io";
write("Your name: ");
var name = await readLine();      // null at the end of input
```

Under WASP there is no user at a console, and under Apache's `mod_fcgid` the standard input is the FastCGI socket itself. Asked by the owner in a chat with Claude, 2026-11-19; added by Claude the same day.

Q1, its form: (a) asynchronous, `await readLine()`, a task, as `std:fs`'s functions are, and `readLineSync()`, which blocks its worker, as `fs.readTextSync` does; (b) synchronous only, `readLine()`, as a console program's usually is.

A1: (a)

Q2, under `wasp`: (a) `readLine` an error where it is called, its message saying that a site has no console; (b) not exported to a site's scripts, so that importing it is WL2001, as for a name `std:io` does not have.

A2: (a)

Q3, the end of the input (the user's Ctrl+Z or Ctrl+D, or a piped file's end): (a) `null`, so that `while ((line = await readLine()) != null) { … }` reads it all; (b) an `IOError`.

A3: (a)

Q4, besides a line: (a) nothing more for now; (b) also `prompt(text)`, which writes `text` and reads a line; (c) also `prompt(text)` and a password's reading, `readPassword()`, which does not echo what is typed.

A4: (c)

2026-11-19 (Claude): one point is open. The task puts reading in `std:io`, "beside writing", but `std:io` is gone: on 18 November 2026 `write` and `writeLn` moved back to `std:system`, which every program imports implicitly, nothing kept of `std:io` (specification, section 3.9, "The standard library", "The rest"). So `import { write, readLine } from "std:io";` would not compile.

Q5, where `readLine`, `readLineSync`, `prompt` and `readPassword` go: (a) `std:system`, beside `write` and `writeLn`, every program having them without an import, as it has `write`; (b) `std:io`, a module of its own again, holding reading only, imported to read, `import { readLine } from "std:io";`, so that a script's imports say it reads its console; (c) `std:io` holding reading, and `write` and `writeLn` too, as well as `std:system`.

A5: (a)

2026-11-19 (Claude): done, and landed, as answered: `readLine()`, a task, its reading off the worker, and `readLineSync()`, blocking it (A1); `null` at the input's end (A3); `prompt(text)` and `readPassword()`, which does not echo where the input is a console's (A4); all four `std:system`'s, every program having them without an import (A5); and, under `wasp`, each an error where it is called, saying that a site has no console (A2). Through the platform's `IWebLingoConsoleInput`, which `weblingo` gives an execution and `wasp` does not; a piped input read as UTF-8, a console's through .NET's Unicode input, as output is written. Guessed, and recorded in the specification as Proposed: the error an `IOError`, WL3011; `prompt` and `readPassword` tasks, without `Sync` forms; a password read key by key, Backspace removing the last, Ctrl+Z or Ctrl+D with nothing typed ending the input. `ConsoleInputTests` run each on both VMs, given piped text, and `WaspTests` a page's read; both references, the specification and the WASP User Manual describe them. Not tried: typing at a console, `readPassword`'s hidden input among it, which no test here can do.

Done (Claude, 2026-11-19, `19fc846`). Main's checks: the Release build, without warnings; the full suite, 4,157 tests, none failed or aborted; and the CLI, `weblingo` given piped lines, UTF-8 text and an empty input, the samples, and `wasp run` on a page that reads. Tried on Windows only; Linux not tried.

## [DONE] Language Reference: Surprises

An explicit goal of WebLingo (WL) is that itis a "language with no surprises." State that goal at the beginning of th eLanguage Reference.

Analyze WL syntax and try to discover anything that would or could be potentially surprising , or unintuitive to a programmer coming from another language. Create a new section of the Language Reference "Comparison with Other Languages". Document any gotchas with examples in that section, identifying the other language.

Cover at the very least the following languages: JavaScript, Python, PHP, Perl, C#, Java.

Done (Codex, 2026-11-19, `c98446d`). The Release build on main had zero warnings and errors. The Windows suite passed 4,151 tests, with six integration tests skipped. Seventeen comparison examples passed on both VMs. Linux was not tried.

## [DONE] Change the WebLingo Source Extension to `.wlg`

Replace `.wl` with `.wlg` throughout WebLingo. Rename scripts, modules, WASP route handlers, and paths such as `Examples.wl`. Update CLI and WASP file discovery, import resolution, editor grammars and associations, the Windows file-association script, installers, build and CI scripts, examples, tests, and documentation. Wolfram Language already uses `.wl` for package source.

Done (Codex, 2026-11-19, `55ebd2a`). On Windows, the Release build on main had zero errors or warnings. The suite passed 4,151 tests and six integration tests were skipped. The CLI samples ran, the WASP sample rendered, and the Windows installer scripts passed `weblingo check`. Registry registration, packaging, and Linux were not exercised.

## [DONE] BytesBuilder Extendability

A builder prints as `BytesBuilder:[104, 105]`, and nothing can extend `BytesBuilder` now, the same as `Bytes`. Previously a class could extend it — make it extensible again.

All standard classes must behave like real classes from the script's perspective.

Verified already implemented (Codex, 2026-11-19, `fded9a3`, `76a1e84`, `c9fcbe9`). `BytesTests` covers both VMs, including extending `Bytes` and `BytesBuilder`, construction, and inherited behavior. The full Windows suite on main passed with six integration tests skipped. Linux was not tried.

## [DONE] Append Mode

`read(count)` returns at most 16 MiB at a time, and there’s no `"ab"` (append bytes) mode yet. Support append mode.

Verified already implemented (Codex, 2026-11-19, `578d5d2`). `AFileIsOpenedToAppendBytes` passed on both VMs and covers appending to an existing file, creating a new one, and refusing reads on an append-only handle. The 16 MiB per-read limit is independent of append mode. Linux was not tried.

## [DONE] Indexes and Integers

Python's `items[-1]` reads the last item. WebLingo rejects that index with `WL3005`. Make it work in WebLingo. Also, take into account similar indexing scheme with C#.

Done (Codex, 2026-11-19, `28c4b01`). Negative array subscripts work for reads, assignments, compound assignments, and optional indexing. `-1` selects the last element. The Language Reference compares this spelling with `^1` in C#. On Windows, the Release build on main had zero errors or warnings. The CLI samples and the indexing example ran. The suite passed 4,165 tests with six integration tests skipped. Linux was not tried.

## [DONE] Argument Errors That Name the Argument

`"tea".trim(1)` is WL3001 with a message saying a string was expected, which reads as if `"tea"` were the problem: the code is right (`trim` now takes an optional set of characters, so one argument is allowed, and an integer is a wrong kind of value, not a wrong number of arguments, WL2006), but the message does not say what should have been a string. A wrong kind of argument's message names the method and the argument, and says what it must be and what it was given: "`trim`'s argument must be a string of characters to trim, not an integer". Go through the built-in classes' methods that take arguments (strings', arrays', maps', `Bytes`', `DateTime`'s, `Regex`'s, and the standard modules' functions) for the same vague wording, and give each such message that form, with a test of the message, not only of its code.

EXPLAIN: How are other methods that expect a certain type of argument handle it? How can a programmer annotate a method to expect a certain type of value so that the compiler could issue a similar error message for user-defined functions?

2026-11-19 (Codex). The shared argument formatter was added in `59a5cab`. Commit `0ecc35e` adds named diagnostics for invalid map and set arguments and documents runtime validation with `is` and `TypeError` for user-defined functions. Those changes passed the Windows checks on main. Array callbacks remain a separate case: `[1].map(1)` raises WL2005 at the attempted call, while an empty array never invokes the callback.

Should higher-order array methods reject non-callable arguments up front with a named WL3001 diagnostic, including when the array is empty?

A: Yes.

Done (Codex, 2026-11-19, `f657ed6`). Higher-order array methods reject non-callable callbacks with a named WL3001 before visiting any elements. The Windows Release build on main passed with zero warnings or errors. The full suite passed 4,182 tests with six integration tests skipped. CLI samples, register and stack bytecode, and a WASP page with a valid callback were exercised. Linux was not tried.

## [DONE] Stream Database Rows

Implement `db.rows(sql, values)` and the disposable async cursor approved in the parent task. Exercise row conversion, exhaustion, early disposal, and both VMs.

Done (Codex, 2026-11-19, `a2c3fe0`). On Windows, the Release build on main passed with zero warnings or errors. 4,184 tests passed and six integration tests were skipped. SQLite cursor tests covered both VMs, early disposal, exhaustion, and a later row failing after the first row was read. CLI and WASP samples passed. Live SQL Server and Linux were not tried.

## [DONE] Identify Standard Library Gaps

Identify everything that's not in the standard library but should be either because it would be very useful, or because other languages predominantly offer it. Implement it as new std: modules.

2026-11-19 (Codex). Reviewed the Standard Library Reference and the shipped modules. Proposed additions for scripts and WASP applications:

- Path manipulation for joining and splitting paths without touching the filesystem, path-delimiter agnostic, i.e. it works both on Windows and Linux.
- URL parsing, resolution, and query encoding for HTTP clients and server-side applications.
- CSV parsing and writing for exchanging tabular data.
- Gzip and ZIP support for compressed data and archives, with explicit output limits.
- Seeded random numbers for reproducible tests and simulations. Secure random bytes are already in `std:crypto`.
- Subprocess execution for CLI scripts, with a separate decision about what WASP sites may launch.

Q1. Should path manipulation, URL handling, and CSV be the first standard-library additions?

A1: Yes.

Q2. Path helpers should accept either separator. Should they emit the host separator or always emit `/`?

A2: Emit host, but accept either.

Q3. Should the first CSV API handle arrays of strings only or also support maps keyed by the header row?

A3: Both.

2026-11-19 (Codex). Your answers set the priority, separator policy, and CSV row types. The names, signatures, and data formats below are proposed public APIs.

Q4. Approve `std:path` with `join(...parts)`, `dirname(path)`, `basename(path)`, and `extension(path)`, recognizing both separators, drive prefixes, and UNC roots on both platforms?

A4: Yes.

Q5. Approve `std:url` with `parse(url)` returning named components, `resolve(base, relative)`, and query parsing and writing that use ordered name/value pairs so duplicate keys round-trip?

A5: Yes.

Q6. Approve `std:csv` with `parse(text, headers = false)` and `stringify(rows, headers = null)`, where parsing with headers returns row maps and writing row maps takes an explicit ordered header array?

A6: Yes.

Done for the three approved modules (Codex, 2026-11-19, `fabe2aa`). The Windows Release build on main passed with zero warnings or errors. 4,195 tests passed and six integration tests were skipped. The CLI and WASP reference examples passed. Linux was not tried. The remaining candidates are in “Choose the Next Standard-Library Additions.”

## [DONE] Add the MySQL Database Provider

The owner chose MySQL as the first new provider. The local NuGet cache has no MySQL provider. Proposed adapter dependency: MySqlConnector, with the existing ADO.NET connection contract and a `mysql:` provider prefix.

Q1. May I restore MySqlConnector from the configured NuGet feeds for the MySQL adapter?

A1: Yes.

2026-11-19 (Codex). Added MySqlConnector 3.0.0 via the existing ADO.NET adapter in `4b45d3b`. The CLI accepts `mysql:` connection strings and WASP requires a configured name. Windows Release builds passed with zero warnings or errors on the task branch and main. The full suite passed 4,196 tests with six skipped on both. The CLI and WASP database samples ran on main, and the CLI publish included MySqlConnector. Live MySQL and Linux were not tested locally.

## [DONE] Error Reporting

```
function two(a, b) { return a + b; }
two(1);
```

This is an example of an error being reported:

    ... : error WL2006: 'two' takes 2 arguments, not 1.

 The error should be loud and bold: `ERROR`, and it should name the exception that occurred, not just give the error code:

        ERROR WL2006 (ArgumentError): 'two' takes 2 arguments, not 1.

2026-11-19 (Codex). In `b283c2d`, `weblingo` and `wasp` report uppercase ERROR on standard error, in bold on supporting terminals, with the diagnostic code and the WebLingo exception class where available. Redirected text uses uppercase without terminal escapes. Windows Release builds passed with zero warnings or errors on the task branch and main. The complete suite passed 4,198 tests with six skipped on both. CLI and WASP samples and Windows pseudoterminal output were checked on main. Linux was not tested locally.

## [DONE] Regex Timeout

The Language Reference says: A pattern that takes more than a second to match, as some do on some text, stops with a `TimeoutError`.  Make it configurable via a command line flag, something like `--timeout 300`, the time being given in milliseconds.

2026-11-19 (Codex). In `8771532`, `weblingo run --timeout <ms>` and `wasp run --timeout <ms>` set the regex match limit before the file argument. The setting reaches scripts and both bytecode VMs. The default is one second. Windows Release built cleanly and the full suite passed 4,204 tests with six skipped on the task branch and main. The CLI and WASP samples verified the default and overrides. Linux was not tested locally.

## [DONE] Unnecessary Imports in Language Ref

The Language Reference is full of examples where this line is present:

    import { writeLn } from "std:system";

This is unnecessary since `std:system` is supposed to be implicitly imported.

2026-11-19 (Codex). Removed 69 redundant `std:system` imports in `30733a8` and verified five updated diagnostic locations. Compared the original and edited examples on the task branch and main, running 68 and checking the HTTP example without a server. A WASP sample verified implicit `write`. Windows Release built cleanly and the full suite passed 4,204 tests with six skipped on both. Linux was not tested locally.

## [DONE] Explain Argument Types in User-Defined Functions

EXPLAIN: How are other methods that expect a certain type of argument handle it? How can a programmer annotate a method to expect a certain type of value so that the compiler could issue a similar error message for user-defined functions?

2026-11-19 (Codex). The Language Reference section “Checking argument types” was added in `0ecc35e` and clarified in `0c4fd9d`. It explains compiler arity checks, built-in runtime type checks, and using `is` with `TypeError` for user-defined functions. The example now handles `null` and names the received class. Exact sample output matched on source and stack bytecode on both the branch and main. Windows Release built cleanly and the full suite passed 4,204 tests with six skipped on both. Linux was not tested locally.

## [DONE] Add the PostgreSQL Database Provider

The owner chose PostgreSQL after MySQL. The local NuGet cache has no PostgreSQL provider. Proposed adapter dependency: Npgsql, with the existing ADO.NET connection contract and a `postgresql:` provider prefix.

Q1. May I restore Npgsql from the configured NuGet feeds for the PostgreSQL adapter?

A1: Yes.

2026-11-19 (Codex). In `6a89a2b`, added Npgsql 10.0.3 (PostgreSQL License) to WebLingo.Data.DotNet. No existing transitive package versions changed. The CLI accepts `postgresql:` strings. WASP opens configured names. Date parameters bind as UTC instants. On the task branch and main, Windows Release built cleanly and the full suite passed 4,206 tests with six skipped. CLI and WASP samples checked connection validation and SQLite streaming, and the CLI publish included Npgsql. Live PostgreSQL and Linux were not tested locally.

## [DONE] Implement What's Not in the Language

`Documents\weblingo-language-reference.md` has section "17. What is not in the language yet". Implement everything it describes, and remove that section.

2026-11-19 (Codex). The Language Reference lists streaming database rows and providers beyond SQLite and SQL Server. The architecture specification (§10, item 11) still leaves the streaming API open. A disposable async cursor would let callers read one row at a time and release the reader on early exit. PostgreSQL is a candidate for the first additional provider, followed by MySQL or MariaDB.

Q1. Should `await db.rows(sql, values)` return a disposable cursor whose `await cursor.next()` gives a row map or `null` at end of input?

A1: Yes.

Q2. Which additional database provider should ship first?

A2: MySQL, PostgreSQL

2026-11-19 (Codex). The owner approved the cursor and chose MySQL before PostgreSQL. See the architecture specification, §7.11, “Streaming rows and additional providers”. Streaming landed in `a2c3fe0`. MySQL landed in `4b45d3b`. The parent task waits for the PostgreSQL adapter.

2026-11-19 (Codex). Streaming rows shipped in `a2c3fe0`, MySQL in `4b45d3b`, and PostgreSQL in `6a89a2b`. The last provider change removed the obsolete Language Reference section. The Windows Release build and full suite passed on the task branch and main. Live MySQL, live PostgreSQL and Linux were not tested locally.

## [DONE] Add Gzip and ZIP Compression

Add in-memory gzip and ZIP compression and decompression with explicit decompressed output limits.

2026-11-19 (Codex). In `37622d1`, added in-memory `std:compression` with gzip and ZIP. ZIPs use file-name-to-bytes maps. The required unzip limit applies to the total file bytes. The heap budget also applies. Windows Release built cleanly and the full suite passed 4,209 tests with six skipped on the branch and on main. Both VMs covered limits, round trips, and damaged inputs. CLI and WASP samples and an externally generated ZIP ran on main, and the CLI publish included the module. Linux was not tested locally.

## [DONE] Add Seeded Random Numbers

Add a seeded generator that produces reproducible sequences for tests and simulations.

2026-11-19 (Codex). Added `std:random` in `0f251ee`. Each `Random(seed)` uses SplitMix64, with `next()` in [0, 1) and unbiased `nextInt(min, max)` with an exclusive upper bound. Known sequences, range boundaries and errors passed on both VMs. Windows Release built cleanly and the full suite passed 4,214 tests with six skipped on the branch and main. The CLI and WASP examples and CLI publish passed on main. Linux was not tested locally.

## [DONE] Add CLI Subprocess Execution

Run a local program from a CLI script with an argument array, capturing its exit status and output. Keep WASP subprocess access for a separate decision.

2026-11-19 (Codex). In `480d4dd`, added `std:process` for the CLI, passing arguments directly and collecting the exit code and both UTF-8 streams. The default deadline is 30 seconds and the combined output limit is 1 MiB, both configurable per call. Windows Release built cleanly and the full suite passed 4,221 tests with six skipped on the branch and main. CLI samples on both VMs, WASP denial and the CLI publish passed. Linux was not tested locally.

## [DONE] Choose the Next Standard-Library Additions

Path, URL, and CSV have shipped on local main. The remaining candidates are compression, seeded random numbers, and subprocess execution.

Q1. Which of these should be the next standard-library addition?

A1: As you list them. Don't ask trivial questions. Act autonomously.

2026-11-19 (Codex). The owner approved the order. See the architecture specification, §7.16, “Standard-library utilities”. This task waits for its three tasks below.

2026-11-19 (Codex). Completed the three additions defined in the architecture specification, §7.16, “Standard-library utilities”. Gzip and ZIP landed in `37622d1`, seeded random in `0f251ee`, and CLI subprocess execution in `480d4dd`. Each passed its Windows Release checks and complete test suite on main. Linux was not tested locally.

## [DONE] VS Code Debugging

Buiid an extension for VS Code to debug WebLingo.

2026-11-19 (Codex). The VS Code extension currently provides syntax highlighting. The register VM's compiled functions already carry source spans and local names. A debugger needs a VM pause and inspection interface and a host for the Debug Adapter Protocol.

Q1. Should the first VS Code debugger launch local `.wlg` scripts on the register VM with breakpoints, stepping, a call stack and local variables, with WASP, the stack VM and attaching to running processes left for follow-up tasks?

A: Use the register VM (the default) and yes to all of your suggestions.

2026-11-19 (Codex). Implemented in `70b49ad`, following the approved scope in the architecture specification §5.7. The extension launches local .wlg scripts on the register VM. Debug metadata stays in memory and variables show bounded previews. VS Code 1.129.1 on Windows hit a breakpoint and inspected a local. The protocol tests covered stepping, stack frames, shadowing and termination. The Windows Release builds passed with zero warnings or errors and the full suite passed 4,223 tests with six skipped on the branch and main. CLI and WASP samples passed. Linux was not tested locally.

## [DONE] Use the heap's byte accounting for gzip and ZIP decompression

`gunzip` rejected a 2,048-byte result with `WL3003` when 979 heap units were free. A direct `Bytes` allocation of the same size succeeded. In `WebLingoCompressionModule.TryRead`, use the heap's allocation rule of one unit per eight bytes plus object overhead, and allow collection before reporting exhaustion. Keep the caller's `maxBytes` limit in bytes, cumulative across ZIP entries.

Verify that both `gunzip` and `unzip` accept output that fits under both limits, including when collection frees the necessary space. Check that exceeding `maxBytes` still reports `WL3001` and exhausting the heap still reports `WL3003`.

2026-11-19 (Codex): Fixed in `dbbee82`. gzip and ZIP use the heap byte-allocation rule and can collect before reporting WL3003. The cumulative caller byte limit still reports WL3001. The Windows Release build and full suite passed on the task branch and main (4,231 passed, six skipped, zero build warnings). The CLI and WASP compression examples and CLI reading of an independently generated ZIP passed. Linux was not run locally.

## [DONE] Stop surviving subprocesses when capture times out

A launcher can exit while a descendant still holds an output pipe open. When `std:process.run` reaches its deadline, `DotNetProcessRunner.StopIfRunning` sees that the launcher has exited and skips termination. Track the launched process tree through capture so timeout and output-limit cleanup can terminate descendants even after the launcher exits.

Add a regression test with a short-lived launcher and a descendant that inherits an output pipe. Verify that the caller receives `TimeoutError` on timeout or `IOError` on output overflow, and that the descendant terminates in both cases.

2026-11-19 (Codex): Fixed in `1877066`. Windows starts the launcher inside a job object. Linux uses a process group and retains its group ID until capture finishes. Regressions verify TimeoutError and IOError and confirm that the descendant exits after the launcher has exited. The Windows Release build and full suite passed on the task branch and main (4,233 passed, six skipped, zero build warnings). The CLI examples ran as source and stack bytecode, and WASP returned its subprocess error. Linux was not run locally.

## [DONE] Migrate WebLingo's old Windows `.wl` file association

On accounts that registered WebLingo for `.wl`, running `Scripts/add-wlg-file-type.wlg` registers `.wlg` but leaves Explorer offering WebLingo for the old extension. Remove WebLingo's legacy entries, including its `OpenWithProgids` and `SupportedTypes` entries, while preserving other applications' registrations and the user's explicit default-app choice.

Verify an upgrade with both WebLingo and another application's entries already present for `.wl`. Check that `.wlg` opens with WebLingo when selected, that the other application's entries survive, and that running the migration twice produces the same result.

2026-11-19 (Codex): Fixed in `e7028b2`. The migration removes WebLingo's legacy ProgID and supported-type entries, including the unnamed default when it still points to WebLingo. Generic extension metadata and the explicit Windows default choice are preserved. Windows checks on the task branch and main included `weblingo check`, a Release build with zero warnings, and the full suite (4,233 passed, six skipped). In a temporary registry subtree, two runs produced identical contents with either WebLingo or another app's existing ProgID. Windows resolved the new open command by extension and by application, and it ran a .wlg script. The live Explorer defaults were untouched. The migration is Windows-only.

## [DONE] Preserve a literal U+FEFF in the first CSV field

When the first field starts with U+FEFF, `WebLingoCsvModule.WriteRow` can emit the character unquoted. The parser then consumes it as a byte-order mark. A four-character field containing U+FEFF followed by `Tea` came back as the three-character string `Tea`. Quote the first field when its leading U+FEFF would otherwise appear at the start of the CSV text.

Add round-trip tests for a leading U+FEFF in the first data field and in the first header. Also check that parsing an input file with a genuine byte-order mark still removes the mark.

2026-11-19 (Codex): Fixed in `08e97c7`. The writer quotes an initial field or header beginning with a literal U+FEFF. Regressions on both VMs confirmed data and header round-trips and consumption of a leading BOM in the input. The Windows Release build and full suite passed on the task branch and main (4,237 passed, six skipped, zero build warnings). The round-trips worked in the CLI and WASP, and the CLI parsed a real UTF-8 CSV file decoded with its BOM intact. Linux was not run locally.

## [DONE] Bytecode File Extensions

Depending on whether bytecode is intended for register of stack VM, use the extension `.wlbr` for register, and `.wlbs` for stack. Regardless of the extension, check which VM a particular bytecode model is intended for and refuse to run on the wrong one.

2026-11-20 (Codex). Implemented in `b2d260e`. Register and stack outputs are written beside each other as `.wlbr` and `.wlbs`. The CLI verifies the header and bytecode and reports WL4001 for an extension/VM mismatch. Existing `.wlbc` inputs remain readable, and WASP does not serve any of the three extensions. Windows Release built without warnings on the branch and main, and the full suite passed (4,242 passed, six skipped). The CLI samples ran as source and on both bytecode targets, and the WASP sample rendered. Linux was not tested locally.

## [DONE] DAG of Dependencies

Create an SVG illustration of the DAG of dependencies and insert into "Dependency Direction" section of the `implementation.md`.

2026-11-20 (Codex). Added the diagram in `9c9383c` and embedded it in Dependency Direction. It covers all 33 product projects, groups the native modules the CLI builds and ships, and omits redundant arrows. Compared every illustrated dependency path with the project references and checked the render in Edge. On the branch and main, Windows Release built with zero warnings and the suite passed (4,242 passed, six skipped). Linux was not tested locally.

## [DONE] Native Compatibility Document

`Documents/NativeCompatibility.md` was renamed `native-compatibility.md`, yet documents still refer to the old name. Fix that.

2026-11-20 (Codex). Fixed eight current document and source-comment references in `321df38`. The exact tracked path is `Documents/native-compatibility.md`. Historical task records retain the name they describe. Windows Release built without warnings on the branch and main, and the full suite passed (4,242 passed, six skipped). Linux was not tested locally.

## [DONE] Example Person's Name

Whenever the name `Ann` is mentioned, in the example code, in the documentation, etc., change it to `Anna`.

2026-11-20 (Codex). Renamed the example person throughout examples, documentation, code comments and test fixtures in `3670810`. Updated the expected padding, length, case and split results. The Windows Release build had zero warnings on the branch and main, and the full suite passed (4,242 passed, six skipped). Three standalone examples, six reference examples and the WASP greeting passed. Linux was not tested locally.

## [DONE] CLI's VM

The `implementation.md` seems to imply that the WebLingo CLI is only capable of running the register VM, while WASP is running both, as a choice. The intent was to make it possible for either exe to run either VM, based on configuration. If the choice of VM is not implemented that way, implement it.

Approve "vm": "register" or "stack" in weblingo.config and wasp.config, using the register VM for source when no setting is given? The proposal applies to weblingo run/check/compile and wasp run/check/serve/fastcgi. The existing explicit compile flag would override the setting. With no setting, compiled files would still select the VM from their verified header. With a setting, a compiled file for the other VM would be refused. The VS Code debugger would keep its approved register-only behavior.

A: Approved.

2026-11-20 (Codex). Implemented in `e68932c`. Both executables compile and run source on the configured VM, with register as the default. An explicit compile flag overrides the file setting, and run and check validate compiled bytecode against it. WASP uses the site root to select the VM when given --root and selects it at host startup for serving. The debugger still uses register. Windows Release built with zero warnings on the branch and main, and the full suite passed (4,252 passed, six skipped). Both executables ran their samples on both VMs. Hosted checks covered FastCGI, HTTP handlers, async component rendering and runtime diagnostics. Linux was not tested locally.

## [DONE] Testing Framework

Write a standard testing framework for WebLingo code, so that test classes can easily be written in WebLingo and run via a built-in test harness.

Approve weblingo test <file.wlg> [more files], with each module exporting a tests map from case names to zero-argument functions? A test class can register a case with a function such as () => new ArithmeticTests().adds(), giving that case a fresh instance. The harness would await both synchronous and asynchronous cases, report each failure and a summary, and exit nonzero on failure. Start std:test with assert(condition, message = null), equal(actual, expected, message = null) and async throws(action, expectedClass = Error). If you prefer automatic discovery of exported classes and test methods, please specify the discovery convention.

A: Design the discovery convention and present here.

Proposed discovery for `weblingo test <file.wlg> [more files]`

- Discover top-level classes declared with `export class` whose names end in `Tests`. Within each class, discover
  directly declared instance methods named `testName`, with an uppercase letter after `test`. Both matches are
  case-sensitive. Static methods, inherited methods, and imported classes do not become tests.
- A test method must declare zero parameters and may be synchronous or asynchronous. The harness initializes each
  file's module once and constructs a fresh class instance with no arguments before each case. Run files in command
  order and classes and methods in declaration order.
- Report cases using `file::Class.method`. Await each case, record uncaught errors as failures, continue with the
  remaining cases, and exit nonzero when a test or discovery fails. A file with no matching cases or a test method
  with parameters gets a discovery diagnostic. No registration map or annotation is needed.

```javascript
export class ArithmeticTests {
    testAddition() {
        if (2 + 2 != 4) throw new Error("Expected four");
    }

    async testAfterDelay() {
        await delay(1);
        if (3 * 4 != 12) throw new Error("Expected twelve");
    }
}
```

Approve this discovery convention for the built-in harness, along with the proposed `std:test` assertions?

Explain: What about multiple assertions in a single test case? How do we make assertions easier? Instead of `throw...` I'd rather say `assertEqual(2 + 2, 4, "Expected four.");`.

Proposed assertion behavior

Import `assertEqual` from `std:test` and call it as `assertEqual(actual, expected, message = null)`.
The message is optional. On failure the harness reports the assertion's file and line, the expected and actual values,
and any message supplied.

A test method can make several assertions. They run in order. The first failure ends that case, and the harness
continues with the next case. Put independent checks in separate methods when you want a result for each one even
if another fails.

```javascript
import { assertEqual } from "std:test";

export class ArithmeticTests {
    testAddition() {
        assertEqual(2 + 2, 4, "Expected four.");
        assertEqual(4 + 0, 4);
    }
}
```

Approve the discovery convention with `assertEqual` and this failure behavior?

There is more to it. Investigate what JavaScript and Python frameworks define for their most popular test frameworks and propose a more comprehensive design for std:test.

### Proposed `std:test` design

Use explicit imports from `std:test`. Ordinary assertions stop the current case at the first failure. Use
`checkAll` when several independent checks in the same case should report their failures together.

#### Framework references

- Jest separates ordinary equality from strict structural comparison, offers matchers for collections and exceptions,
  and supports parameterized tests. Vitest provides soft assertions that keep running and still fail the case.
  [Jest matchers](https://jestjs.io/docs/expect) · [Jest parameterization](https://jestjs.io/docs/api#testeachtablename-fn-timeout) · [Vitest soft assertions](https://vitest.dev/api/expect#soft)
- Node provides an explicit assertion module alongside a separate runner with lifecycle hooks, filtering, and
  reporting. [Node assertions](https://nodejs.org/api/assert.html) · [Node test runner](https://nodejs.org/api/test.html)
- Python's `unittest` provides named assertions, class-based setup and cleanup, and subtests. pytest provides
  detailed comparison failures, fixtures with teardown, and one result per parameter row.
  [Python unittest](https://docs.python.org/3/library/unittest.html) · [pytest assertions](https://docs.pytest.org/en/stable/how-to/assert.html) · [pytest fixtures](https://docs.pytest.org/en/stable/how-to/fixtures.html) · [pytest parameterization](https://docs.pytest.org/en/stable/how-to/parametrize.html)

#### Assertions

| API | Proposed behavior |
| --- | --- |
| `assert(condition, message = null)` | Checks a Boolean condition. |
| `assertEqual(actual, expected, message = null)` and `assertNotEqual(actual, expected, message = null)` | Use WebLingo's `==` semantics, including identity for collections. |
| `assertDeepEqual(actual, expected, message = null)` | Compares array elements in order, maps by key and value regardless of insertion order, sets by membership, and bytes by content. Handles cycles and reports the first differing path. |
| `assertContains(actual, item, message = null)`, `assertHasKey(map, key, message = null)`, and `assertMatches(text, regex, message = null)` | Check membership, key existence, or a regular-expression match. |
| `assertClose(actual, expected, tolerance, message = null)` | Checks numeric results using an explicit absolute or relative tolerance. |
| `assertThrows(action, expectedClass = Error)` and `await assertRejects(action, expectedClass = Error)` | Check for a synchronous throw or a rejected task, respectively. Return the matching error so the caller can inspect its code or message. |
| `await checkAll(action)` | Gives the callback a `check` object with the same assertion methods and reports their failures together. An unexpected exception stops the block and is reported alongside the failures collected so far. |

```javascript
import { assertEqual, checkAll } from "std:test";

export class ArithmeticTests {
    testAddition() {
        assertEqual(2 + 2, 4, "Expected four.");
    }

    async testIndependentResults() {
        await checkAll(check => {
            check.assertEqual(2 + 2, 4);
            check.assertEqual(3 * 4, 12);
        });
    }
}
```

#### Runner and release scope

Keep the class discovery convention above. The runner selects exported classes ending in `Tests` and their directly
declared, zero-argument instance methods named `testSomething`. Each case gets a fresh instance and may use optional
sync or async `beforeEach` and `afterEach` hooks. Run cleanup even when a test fails, and report a cleanup failure
alongside the original failure. An assertion failure includes the case name, source location, expected and actual
values when applicable, and any supplied message. The runner continues with the remaining cases.

Include discovery, the assertions above, `checkAll`, per-case hooks, and the runner's `--list` and `--filter` in
the first release. Add named parameter rows next, with their own result and fresh instance for each row. Follow with
temporary-directory fixtures and explicit spies. Design snapshots, module mocking, fake timers, and coverage
separately because they affect file updates, imports, scheduling, or compiler instrumentation.

Approve this `std:test` design and the proposed first-release scope?

A: Approved.

2026-11-20 (Codex). Implemented in `d93f9d7`. The runner discovers exported test classes, supports `--list` and a case-sensitive substring `--filter`, and runs each case on a fresh instance with setup and cleanup hooks. `std:test` supplies the approved assertions and `checkAll`. The tolerance argument to `assertClose` is a map with `absolute`, `relative`, or both. The Windows Release build had zero warnings. The full suite on `main` passed 4,265 tests, with six integration tests skipped because their SQL Server, nginx, or Apache prerequisites were unavailable. CLI samples and passing and failing test files ran on both VMs. A temporary publish included the native testing support and ran a test. Linux was not tested locally.

## [DONE] Double-click Windows setup for WebLingo and its VS Code debugger

Ship `Install WebLingo.bat` in each WebLingo ZIP. Have the bundled runtime run setup in WebLingo. Request elevation
for the machine installation and PATH, then offer `.wlg` file registration and the bundled VS Code debugger for the
original Windows user. Keep the extension package in the installed `VSCode` folder for later use. Verify the ZIPs
and the installer's success, cancellation and optional integration paths on Windows.

2026-11-20 (Codex). Implemented in `ca533ec`. The Windows Release build passed with zero errors and warnings.
All six WebLingo ZIPs carried the launcher and the same offline VSIX. The six WASP manifests matched their paired
runtime archives. On x64 Windows, isolated launcher checks covered the prompts, cancellation, missing VS Code,
optional steps, reruns and failures. VS Code installed the packaged debugger and the installed runtime answered
protocol requests. The x86 self-contained launcher also ran on x64 Windows. UAC, machine PATH and the real user's
file association were simulated. Native ARM64 and 32-bit Windows were not exercised.

## [DONE] Uninstall WebLingo on Windows

Provide a double-click uninstaller for the Windows ZIP and the installed runtime. Undo the runtime installation,
machine PATH entry, file association and VS Code integration. Preserve other applications, accounts and user files.

2026-11-20 (Codex). Implemented in `73f19eb`. Windows Release built without warnings or errors. The six WebLingo
ZIP inventories and their paired WASP hashes passed verification. Targeted Windows checks covered registration
restoration, side-by-side runtimes, WASP, changed files, locks and retry. The x64 round trip covered install,
update, cancellation, self-removal from the installed folder, an extracted ZIP rerun, older-package removal and
VS Code extension ownership. It used the actual VS Code CLI and disposable registry keys, with UAC simulated.
Native ARM64 and 32-bit Windows were not exercised.

## [DONE] Console Module, Part 1: The Writing and the Reading

Part 1 of "Console Module" (the specification, section 9, "Console module, parts"): `std:console`, a native module installed as `std/console.dll`, its `write`, `writeLn`, `writeError` and `writeErrorLn` (A9), and the console's reading, `readLine`, `readLineSync`, `prompt` and `readPassword`, moved from `std:system` (A1), which keeps `delay`; under `wasp`, its import WL2015 (A3); a page's blocks writing with `write` and `writeLn` of their own, and a `.wlg` handler with `response.write` and `response.writeLn` (A2); every script, sample, test, example, workload and document gaining its import.

2026-11-20 (Claude): done, and landed, as answered: `std:console`, `WebLingo.StdLib.Console`, a native module installed as `std/console.dll`, exporting `write`, `writeLn`, `writeError` and `writeErrorLn`, the last two to the standard error (A9), and `readLine`, `readLineSync`, `prompt` and `readPassword`, moved from `std:system`, which keeps `delay` (A1); `write` without its import WL2001, suggesting `std:console`; under `wasp`, its import WL2015, its message saying that a site has no console and how a page and a handler write (A3); a page's blocks writing with `write` and `writeLn` of their own, which only a template's code sees, a page's, its includes', a layout's and a component's, never a module it imports, and a handler with `response.write` and `response.writeLn` (A2). Every script, sample, test, example, workload, Script and document imports `std:console` where it writes or reads the console, which "Import std:console" asks, done here since nothing would run otherwise. Guessed, and recorded in the specification as Proposed (section 15.2, "The console"): `response.write`'s text made as an interpolation makes it, `null` as `null`, an instance's `toString()` run, then given to the response's own native `__write(text)`; a statement that calls a member named `write` or `writeLn` waiting for a slow client where the host declares the output's wait, so that a handler's writes wait as a page's did, a script's own `write` methods under `wasp` waiting so too; a page's `write(null)` writing `null`, as `write` does, where `<%= null %>` writes nothing; `import { writeLn } from "std:system";` compiling and WL3006 where it is used, as for any name a module does not export; and in-process test hosts giving `write` and `writeLn` as their own built-ins, `std:system` exporting them to them, so that the VM tests are as they were.

Done (Claude, 2026-11-20, `8216d21`). Main's checks: the Release build, without warnings; the full suite, 4,277 tests, none failed or aborted; and the CLI: `weblingo` on the samples, every Script checked, the references' and the getting-started guide's examples run, their output as shown but where one needs a file, a server or a parameter of its own; `wasp run` on the website sample, and `wasp check` on the example sites' pages, but `example2`'s, whose layout takes the request, which `wasp check` cannot give, as before. Tried on Windows only; Linux not tried.

## [DONE] Console Module, Part 3: Console Programs on Windows

Part 3 of "Console Module" (the specification, section 9, "Console module, parts"): a script a console program only when it imports `std:console`: `weblingo.exe`'s manifest's `detached`, a console made where such a script has none, and `weblingow.exe`, a Windows application, which a double-click runs (A7); a failure without a console shown in a message box (A8).

2026-11-20 (Claude): split in two, the specification's section 9, "Console module, parts", parts 3 and 4, the double-click's registration, which the uninstaller and its tests mirror, being part 4, "Console Module, Part 4: A Double-Click Runs weblingow". Done, and landed, as answered: `weblingo.exe`'s manifest saying `detached` (A7); `weblingow.exe`, `WebLingo.Windowed.Cli`, a Windows application, published beside `weblingo.exe` by `publish.ps1`, `wasp`'s project referencing it, and by `Installers/Windows/package.ps1`; either, started with no console and no standard output or error a file or a pipe takes, makes a console window of its own for a script that imports `std:console` (A7), and shows a failure's errors in a message box where it never had one (A8). Guessed, and recorded in the specification as Proposed (section 15.2, "The console"): a console made when `std:console` is first loaded to run a script, statically or dynamically, from source or bytecode, for `run` and `test`, never `check` or `compile`, so that a script that imports it and fails to compile shows its errors in its console; a process whose standard output or error a file or a pipe takes, as a debugger's or a test's, having a console for this; the message box titled with the program's name, showing the errors, or else the output, on a failure only; a console window of its own waiting for a key at the end, as before.

Done (Claude, 2026-11-20, `c110b98`). Main's checks: the Release build, without warnings; the tests the change could affect, the conformance, template, hosting, architecture and VM tests, 3,787 tests, none failed or aborted, the compiler's, language's, bytecode's and runtime's unchanged since part 1's run; `publish.ps1` into a scratch folder, `weblingow.exe` and `std/console.dll` in it; and the programs themselves: `weblingo.exe` and `weblingow.exe` started without a console on a script that does not import `std:console`, which ran with no window, on one that does, which had a console window of its own, and on one that does not compile and one that throws, whose message boxes showed their errors, and both with their output piped, which went there. Not tried: Windows before 24H2, where `weblingo.exe` opens a console as before; `package.ps1`'s zips, not built. Tried on Windows only; Linux not tried.

## [DONE] Console Module, Part 4: A Double-Click Runs weblingow

Part 4 of "Console Module" (the specification, section 9, "Console module, parts"): `.wlg` files registered to `weblingow.exe` (A7), by `Scripts/add-wlg-file-type.wlg` and the Windows setup, the uninstaller removing it, an earlier registration of `weblingo.exe` moved over.

2026-11-20 (Claude): done, and landed, as answered: `Scripts/add-wlg-file-type.wlg` registers `.wlg` files to run `weblingow.exe` (A7). Guessed, and recorded in the specification as Proposed (section 15.2, "The console"): the registration's keys as before, `WebLingo.Script` and `Applications\weblingo.exe`, so that a choice of `weblingo.exe` already made in Windows's "open with" holds, running `weblingow.exe` now, their command and icon `weblingow.exe`'s; the uninstaller removing a registration whose command runs either program, taking a surviving installation's own program, and refusing to remove an installation while either runs from it. While its tests were changed, a case reached the uninstaller's removal of WebLingo's VS Code extension, which `Find-Code` found in this account's own VS Code and uninstalled; it was reinstalled, version 0.2.0, from a VSIX built from `Editors/VSCode` as `package.ps1` builds it, the tests now stub `Find-Code`, and `implementation.md` records the trap.

Done (Claude, 2026-11-20, `472d62a`). Main's checks: no .NET code changed, so neither the build nor its tests; the uninstaller's tests, `Installers/Windows/tests/uninstall.ps1`, which passed, an earlier registration's removal among them; and `weblingo check` of the changed scripts. Not tried: the registration on this machine, and setup, which the owner runs once `build.bat` has installed `weblingow.exe`. Tried on Windows only; Linux not tried.

## [DONE] Write Aliases

Add `print` and `printLn` as aliases for `write` and `writeLn` respectively in `std:console`.

2026-11-20 (Claude): waits for "Console Module"'s answers, `std:console` being that task's.

2026-11-20 (Claude): done, and landed: `std:console` exports `print` and `printLn`, `write` and `writeLn` by other names, registered with their functions, text built-ins as they are, so that an instance's `toString()` runs as for `write`; recorded in the specification's register as the owner's request, and in both references, and coloured in scripts by the editors' grammars, with `writeError` and `writeErrorLn`. `Console.print` and `Console.printLn` come with the `Console` class, "Console Module, Part 2: The Console's Control", which waits for the owner's answers.

Done (Claude, 2026-11-20, `d692b77`). Main's checks: the Release build, without warnings; the conformance tests, 3,115 tests, none failed or aborted, `PrintAndPrintLnAreWriteAndWriteLn` among them, no other project's results affected; the editors' grammars parsed. Tried on Windows only; Linux not tried.

## [DONE] Import std:console

Make sure that all examples and documentation properly import `std:console` if they use `write` or `writeLn`.

2026-11-20 (Claude): waits for "Console Module"'s answers, `std:console` being that task's.

2026-11-20 (Claude): done, and landed. "Console Module, Part 1" (`8216d21`) gave every script, sample, example, workload, Script and document its import, since nothing would have run otherwise; this task checked them after "Write Aliases": each `.wlg` in the repository and each code block of its documents that writes or reads the console, for its import of `std:console`, and each page and handler, for no import of `std:system`'s former names. The specification's M0.6 example was the one left, and now imports `writeLn`; the examples' readme says where `01-hello.wlg`'s `write` and `writeLn` come from. The tests that run their own hosts in process, the VM tests among them, are neither examples nor documents: their hosts give `write` and `writeLn` as their own built-ins, which `std:system` exports to them, as `implementation.md` says.

Done (Claude, 2026-11-20, `e8fd278`). Main's checks: documents and a readme only, which no build or test reads; the scan found nothing more. Tried on Windows only; Linux not tried.

## [DONE] Console Module, Part 2: The Console's Control

Part 2 of "Console Module" (the specification, section 9, "Console module, parts"): `Console`, its members static, all of .NET's `Console`, the writing and the reading among them (A4), `readKey()` and `readKeySync()` giving a map (A6), and `ConsoleColor`, the colours, which the colour members take as their names too (A5).

2026-11-20 (Claude): part 1 landed (`8216d21`), `std:console` a native module, `std/console.dll`. Planned as the notes of "Console Module" say: `Console`'s members static, .NET's names in lower camel case, `Console.title`, `foregroundColor`, `backgroundColor`, `cursorLeft`, `cursorTop`, `cursorSize`, `cursorVisible`, `windowWidth`, `windowHeight`, `windowLeft`, `windowTop`, `bufferWidth`, `bufferHeight`, `largestWindowWidth`, `largestWindowHeight`, `keyAvailable`, `capsLock`, `numberLock`, `treatControlCAsInput`, `isInputRedirected`, `isOutputRedirected` and `isErrorRedirected`; `beep()`, `clear()`, `resetColor()`, `setCursorPosition(left, top)`, `getCursorPosition()`, a map `{ left, top }`, `setWindowSize`, `setWindowPosition`, `setBufferSize` and `moveBufferArea`, its optional arguments as .NET's overloads have them; `readKey()` and `readKeySync()`, an argument saying whether the key is shown, as .NET's `intercept`; the writing and the reading too, `Console.writeLn(x)` (A4); `ConsoleColor.darkBlue` being the string `"darkBlue"`, so that a colour read compares with either (A5, read so); through the platform's console, a new interface beside `IWebLingoConsoleInput`, which `weblingo` gives a script and `wasp` never does. Five things of .NET's `Console` have no plain counterpart:

Q1, Ctrl+C and Ctrl+Break, .NET's `Console.CancelKeyPress`: (a) `Console.onCancelKeyPress(handler)`, the handler called on the script's worker with `"c"` or `"break"`, a handler giving `true` keeping the program running, as .NET's `e.Cancel = true`, `null` for no handler; (b) a task, `await Console.cancelKeyPress()`, completing with `"c"` or `"break"` at the next press, the program kept running while one is pending; (c) only `Console.treatControlCAsInput`, Ctrl+C then read as a key.

A1: (a)

Q2, .NET's `Console.In`, `Out` and `Error`, `SetIn`, `SetOut` and `SetError`, and `OpenStandardInput` and the others, the console's readers, writers and streams: (a) none, `std:console`'s writing and reading, and `isInputRedirected` and the others, being all of it; (b) `Console.setOut(file)`, `setError(file)` and `setIn(file)`, each a `std:fs` `File`, or `null` for the console again.

A2: (a)

Q3, `Console.read()`, .NET's `Console.Read()`, the input's next character: (a) a task of a string of one character, `null` at the input's end, with `Console.readSync()`; (b) none, `readLine` and `readKey` being enough.

A3: (a)

Q4, `Console.inputEncoding` and `outputEncoding`: (a) read only, `"utf-8"`, a script's text being UTF-8 always ("Text Written on Windows: UTF-8"); (b) set too, as .NET's, `Console.outputEncoding = "latin1";`.

A4: (a)

Q5, a member .NET has on Windows only, `Console.title`'s read, `beep(frequency, duration)`, `setWindowSize` and the like, used on Linux or macOS: (a) an `IOError`, WL3011, saying so; (b) an error of its own, `PlatformError`, a new code, as .NET's `PlatformNotSupportedException`.

A5: (b)

2026-11-20 (Claude): done, and landed, as answered: `Console`'s members .NET's `Console`'s in lower camel case, static, the writing and the reading among them; `onCancelKeyPress(handler)`, the handler on the script's worker, `true` keeping the program running (A1); none of .NET's readers, writers and streams (A2); `read()` and `readSync()` (A3); the encodings read only, `"utf-8"` (A4); and a member .NET has on Windows alone, used elsewhere, a `PlatformError`, WL3022 (A5). Guessed, and recorded in the specification as Proposed (section 15.2, "The console"): the code WL3022; `ConsoleColor`'s constants being the names; a key's `key` `null` for one .NET does not name, its `char` `null` for none; `readKey(intercept)`, `true` for a key not shown; `read`, `readSync`, `readKey` and `readKeySync` `Console`'s alone, not exported by name; `beep(frequency, duration)` both or neither; a press while the handler is busy with an earlier one ending the program, as .NET's default does, and a handler that gives anything but `true` ending it with Windows's `STATUS_CONTROL_C_EXIT`, or 130 elsewhere; without a console, or what the console cannot do, an `IOError`; a value it does not take WL3001. For `Console`'s members written in WebLingo, a static method of a native class's may take its class as a receiver, `static write(this, value)`, as the compiler's factories do, which the parser allows a host's native class alone (`WebLingoParser.StaticReceivers`).

Done (Claude, 2026-11-20, `4562fd0`). Main's checks: the Release build, without warnings; the full suite, 4,287 tests, none aborted, all passed but `ConsoleTextTests.CaptureFailureStopsADescendantAfterTheLauncherExits`'s two cases, whose launched process outran their 6-second timeout under the full suite's load, as once before in part 1's runs, and which passed on their own; `ConsoleControlTests` among them, each member on both VMs with a console of the test's own, Ctrl+C's presses among them; the reference's example run; and `weblingo` in a real console window, its title, colours, cursor, window, buffer, locks, redirections, `moveBufferArea` both ways, an out-of-range cursor, Ctrl+C's handler set and taken away, and `clear()`. Not tried: a real press of Ctrl+C, which no test here can make; Linux and macOS, where the Windows-only members are a `PlatformError`. Tried on Windows only; Linux not tried.


## [DONE] Console Module

I want to revisit the decision made previously regarding `write` and `writeLn`. I want to build a `std:console` module that would export those, but also allow more control over the console, like setting background and foreground colors, controlling the position of the cursor, etc. At the minimum, I want all the capabilities that .NET Console has.

We must no longer assume that every WebLingo program is necessarily a console app and therefore that it supports `write` or `writeLn`. A script becomes a console program only when it imports `std:console`. Otherwise it should run as a "normal" WinAPI application. This is in preparation to making it possible to write GUI scripts that bring up visible windows.

2026-11-20 (Claude): read with "Write Aliases" and "Import std:console", which build on it. As the task asks, `write` and `writeLn` leave `std:system`, which every program imports implicitly, for `std:console`, which a program imports, and a program that does not import it is no console program; `std:system` keeps `delay`. That is the owner's revisiting of two approved decisions ("Standard Modules, Imported: The Rest", A3, and "Console Input", A5), recorded in the specification once the questions below are answered. Today a site's pages and its `.wlg` handlers write their responses with `write` too (`write(Json.stringify(…))` in `example2/site/api/fish.wlg`), so a site needs a way of its own. Windows decides whether a program gets a console window from its executable: `weblingo.exe` is a console program, and so opens a window when double-clicked. Since Windows 11 24H2 (build 26100; this machine is 26200), its manifest can say `consoleAllocationPolicy` `detached`, so that it gets a console only when started from one, and `AllocConsoleWithOptions` can make one later, once the compiler has seen `import … from "std:console"` (Microsoft's "Console allocation policy"). Everything else in .NET's `Console` is planned as it is, its names in lower camel case: `title`, `beep()`, `clear()`, `resetColor()`, the cursor's position, size and visibility, the window's and the buffer's size and position, `moveBufferArea`, `keyAvailable`, `capsLock`, `numberLock`, `treatControlCAsInput`, the redirections, and Ctrl+C; what .NET has on Windows only an error elsewhere, as .NET's `PlatformNotSupportedException`.

Q1, the console's reading, `readLine`, `readLineSync`, `prompt` and `readPassword`: (a) moved to `std:console` with `write`, `std:system` keeping none of them; (b) left in `std:system`.

A1: (a)

Q2, how a site's pages and `.wlg` handlers write their response, a page's text and its `<%= %>` writing it as now: (a) `response.write(value)` and `response.writeLn(value)`, members of `Response`, as classic ASP's `Response.Write`; (b) `std:console`'s `write` and `writeLn`, writing the response under `wasp` and the console under `weblingo`; (c) (a), and in a page's blocks, `write` and `writeLn` of its own, without an import, writing the response, as its text does.

A2: (c)

Q3, a site's script that imports `std:console`, under `wasp`, where there is no console: (a) it compiles, and each of `std:console`'s members is an `IOError` where it is used, as `readLine` is now; (b) WL2015 where it is imported, as for a module that is not installed.

A3: (b)

Q4, the console's control: (a) a class, `Console`, its members static, as `std:env`'s `Environment`, `Console.foregroundColor = "red";`, `Console.setCursorPosition(0, 5);`, `Console.clear();`, beside `write`, `writeLn`, `print`, `printLn` and the reading, exported by name, `import { Console, writeLn } from "std:console";`; (b) (a), `Console` having the writing and the reading among its members too, `Console.writeLn(x)`, as .NET's has; (c) functions only, `import console from "std:console"; console.setForegroundColor("red");`.

A4: (b)

Q5, a colour: (a) a string, .NET's `ConsoleColor`'s names in lower camel case, `"darkBlue"`, a wrong one WL3001; (b) a constant of a class of its own, `ConsoleColor.darkBlue`, as .NET's enumeration; (c) both.

A5: (c)

Q6, a key read, `await Console.readKey()`, a task, and `Console.readKeySync()`, as the reading's forms: (a) a map, `{ key: "enter", char: "\r", shift: false, alt: false, control: false }`, the key's name .NET's `ConsoleKey`'s in lower camel case; (b) an instance of a class of its own, `KeyInfo`, with those as its properties.

A6: (a)

Q7, a script that does not import `std:console`, started without a console, by a double-click (from a terminal, it runs in the terminal, writing nothing to it): (a) no window, `weblingo.exe`'s manifest saying `detached`, a console window made only for a script that imports `std:console`, on Windows 11 24H2 and later, earlier Windows opening one as now; (b) a second program, `weblingow.exe`, a Windows application, as Python's `pythonw.exe`, which a double-click runs, on every Windows; (c) both.

A7: (c)

Q8, such a script, without a console, failing, as a compile error or an uncaught exception: (a) a message box showing what `weblingo` writes for it; (b) a console window, made for it, showing it, waiting for a key; (c) nothing shown, only the exit code.

A8: (a)

Q9, writing to the standard error, .NET's `Console.Error`: (a) `writeError(value)` and `writeErrorLn(value)`, beside `write`; (b) members of `Console`, `Console.error.write(value)`, as .NET's `Console.Error.Write`; (c) none for now.

A9: (a)

2026-11-20 (Claude): too large to land in one piece, so built in three parts, which the specification defines (section 9, "Console module, parts"), each a task below: "Console Module, Part 1: The Writing and the Reading", "Console Module, Part 2: The Console's Control" and "Console Module, Part 3: Console Programs on Windows", part 3 split in two, "Console Module, Part 4: A Double-Click Runs weblingow" its second. This task is [IDLE] until the last of them is done.

2026-11-20 (Claude): parts 1, 3 and 4 landed (`8216d21`, `c110b98`, `472d62a`), and so did "Write Aliases" (`d692b77`) and "Import std:console" (`e8fd278`); part 2 waits for the owner's answers to its questions.

2026-11-20 (Claude): done, its four parts landed: "Console Module, Part 1: The Writing and the Reading" (`8216d21`), "Console Module, Part 2: The Console's Control" (`4562fd0`), "Console Module, Part 3: Console Programs on Windows" (`c110b98`) and "Console Module, Part 4: A Double-Click Runs weblingow" (`472d62a`), each in `DONE.md` with its checks.

Done (Claude, 2026-11-20, `4562fd0`). Main's checks: its parts'. Tried on Windows only; Linux not tried.

## [DONE] Static Methods Given Their Class

Let a script's own class have static methods that are given the class they are called on, as Python's `@classmethod` is, so that a script writes factories of its own: `static make(this) { return new this(); }`, called on a class extending it, `Tea.make()`, makes that class's instance. The owner asked for it on 20 November 2026, after "Console Module, Part 2" (`4562fd0`) let a native class's methods written in WebLingo do so, the parser's `WebLingoParser.StaticReceivers`, as the compiler's factories, `File.open(path, mode)`, already did.

2026-11-20 (Claude): planned as a language change, recorded in the specification (sections 3.7 and the register): the runtime has it already, a static method whose function takes a receiver being given its class where it is read (`WebLingoHeap.TryGetStatic`'s `takesClass`), on both VMs; so the parser, for a class's body and an `extend` block alike, and the tests on both VMs, the references and the editors' grammars, if the syntax needs them. A function given a class while the program runs, `Point.make = function (this) { … };`, is already given its call's target, the class, as any function with a receiver is.

Q1, how a static method reaches the class it is called on: (a) a receiver, `static make(this, …)`, as a function has one (section 3.7), `this` in a static method without one still WL2009; (b) `this` in every static method, the class it is called on, without a receiver, as JavaScript's static methods have it.

A1:  (a)

2026-11-20 (Claude): the owner changed the answer in chat, to (b): "I want this to be an implicit parameter on static methods, so it can be referenced and resolves to the class reference." So `this` in a static method is the class it is called on, `static make() { return new this(); }`, and `static make(this)` stays WL1001; `Console`'s methods and the compiler's factories, which took a receiver, use the implicit `this` too, and `WebLingoParser.StaticReceivers` is gone.

2026-11-20 (Claude): done, and landed, as the owner decided in chat: `this` in a static method is the class it is called on, without a receiver, `static make() { return new this(); }`, `Green.make()` making a `Green`, in a class's body and an `extend` block alike, a static property's accessor too; `static make(this)` stays WL1001; recorded in the specification (section 3.7, "Static members", and the register, "Static methods' `this`", Approved), the Language Reference and `Examples.wlg/10-classes.wlg`. `Console`'s methods written in WebLingo and the compiler's factories use the implicit `this`, and `WebLingoParser.StaticReceivers` is gone. Guessed, and recorded in the specification as Proposed (section 15.2, "Static members"): only a static method whose body names `this` takes its class, so that one that does not costs nothing more; reached through an instance, or a collection of a class extending `Array`, `Map` or `Set`, its `this` is the instance's class; `this` in an operator, and in a static field's value, stays WL2009, its message now `'this' is not allowed in an operator or a static field's value.`

Done (Claude, 2026-11-20, `0eacc21`). Main's checks: the Release build, without warnings; the full suite, 4,303 tests, none aborted, all passed, the conformance cases on both VMs and read back from their bytecode files among them; `weblingo` on both VMs, from source and from `.wlbr` and `.wlbs` files, on factories inherited, bound, reached through an instance, in an `extend` block, a static property's getter and a class extending `Array`, on `Console`'s methods and a class extending `fs.File` opened by its factory, and on the Language Reference's example; and the benchmark, `main` and the branch run alternately, every workload the same within the machine's noise. Tried on Windows only; Linux not tried.

## [DONE] Drop Pre-Alpha

Drop the pre-alpha designation. Don't replace it with anything — just display the version and build number. Construct build number the same way as in `MarkdownTools\MaxPad`.

2026-11-21 (Claude): done, and landed: the solution's version is `0.20.0`, with nothing after it, and each build has a number, its local time, `yyyy.MM.dd.HHmm`, made in `WebLingo/Directory.Build.props` as MaxPad's project makes its `BuildStamp`, every assembly's file version; `weblingo --version` prints `WebLingo 0.20.0 (2026.11.21.2323)` and `wasp --version` `WASP 0.20.0 (2026.11.21.2323)`, each first when run without arguments, as before. The documents no longer call WebLingo or WASP pre-alpha (the READMEs, the references, the WASP User Manual, the examples' README and WASP's package readme); the specification's register, "Versions", records the owner's request, and `Documents/implementation.md` and `HANDOFF.md` say how the number is made. Guessed, and recorded as Proposed: the version, then the build number in parentheses, as MaxPad's About box writes them; the number in the file version only, the product version staying `0.20.0`, which the uninstaller compares between builds; the time read from the sortable format, which is the invariant culture's, so that a machine's calendar does not change it. A build more than a minute after the last now compiles every project again, about 18 s against 6 s for a build with nothing to do; `-p:BuildStamp=…` fixes a build's number.

Done (Claude, 2026-11-21, `21b673e`). Main's checks: the Release build, without warnings; the full suite, 4,303 tests, none failed; and the CLI, `weblingo --version`, `weblingo` alone and `wasp --version`, each file's version as Windows reads it, and the version `Installers/Windows/package.ps1` reads. No package was built. Tried on Windows only; Linux not tried.

## [DONE] Enhance Error s About Command Line

Enhance the error reporting regarding the command line parameters. In addition to displaying the usage, actually tell what was wrong with the command line as given.

2026-11-21 (Claude): done, and landed: a usage error now says, on the standard error, what was wrong, before the usage, which is written as before; the exit code stays 2. `weblingo` names a command it does not have, listing its own, `'rnu' is not a command: run, check, compile, disasm, test or --version.`, and, for what looks like a file, which files it runs when named first; a command without its file, `run requires the file to run.`, or with only options, `… after its options.`; something after `check`'s, `compile`'s or `disasm`'s file, or after `--version`; an option of another command, `'--timeout' is not an option of check, but of run.`; and `--scheme` or `--vm` without its value. `wasp` does the same, its own `serve` and `fastcgi` among its commands (`WebLingoCommandLineProgram.MoreCommands`), and says what is wrong with `--root`; `weblingo test` says what is wrong with its options and its files. `serve`'s and `fastcgi`'s errors, and those already given for `--timeout`, a VM, a scheme and a parameter, are as they were. Recorded in the specification (section 15.2, "CLI", the messages Proposed), the Language Reference and the WASP User Manual. Guessed: what each message says, and that a command line read as before is still read so, a run of a file named like an option, `weblingo run --register`, still reporting the file it cannot read (exit 4).

Done (Claude, 2026-11-21, `1b9d36b`). Main's checks: the Release build, without warnings; the full suite, 4,328 tests, none failed, 25 of them new, each checking a message; and the CLI, `weblingo` and `wasp` given 28 wrong command lines and one right one. `weblingow`, which shows a failure in a message box where it has no console, not tried. Tried on Windows only; Linux not tried.

## [DONE] Debug Info

When compiling with --debug option, put debug symbol information into bytecode file such that `disasm`  can provide names for variables, functions, classes, etc.

2026-11-22 (Claude): done, and landed. `weblingo compile --debug` compiles a script as for a debugger, for either VM, and writes the debugger's metadata into the `.wlbr` or `.wlbs`: each function's statements' first instructions, its variables, each with where in the source it is visible, a named import as the export it reads, and its captures' names. `disasm` shows them: a capture as `capture0(k)`, a line `; 6:21` before each statement's first instruction, and each function's variables after its code, `variable doubled: r1, visible 6:32 to 6:59`. With or without `--debug`, `disasm` now names the functions, classes and files instructions refer to, `function94(twice)`, `class5(Point)`, `file1(std:console)`. Found while designing it: a file already held every local's name, and every function's, class's and file's, which `disasm` showed only in part, and a runtime error (WL2004) uses the locals' names, so they stay in every file. Recorded in the specification's register ("Debug information in bytecode", Approved, the layout and `disasm`'s text Proposed), section 5.3 and section 15.2 ("WLBC's layout", "WLBC on the command line"), the Language Reference, `Documents/implementation.md` and `HANDOFF.md`. Guessed, and Proposed: the metadata an eighth, optional section, after the source positions, which a file compiled without `--debug` does not have, its bytes as before; its positions added to the file's line and column tables; a function's file not written, the reader giving it the function's own or the module's, so that no host path is in the file; the stack VM's compiler giving its functions the same metadata as the register VM's, which nothing runs under a debugger yet; and `disasm`'s text.

Done (Claude, 2026-11-22, `e36669a`). Main's checks: the Release build, without warnings; the full suite, 4,334 tests, none failed, every conformance program written compiled for a debugger and read back on both VMs; and the CLI, `compile --debug`, `run` and `disasm` of a sample and of a copy of `Examples.wlg/01-hello.wlg` on both VMs, and a file compiled without `--debug` compared with one from before the change, the same bytes. Tried on Windows only; Linux not tried.

## [DONE] A Native Module's Named Imports Bound Directly

`import { writeLn } from "std:console";` makes the whole module where the program begins, a `LoadBuiltin` for each of its exports, ten for `std:console`, and its `MakeModule`, though the program uses one, and each call reads the export from the module by its name first, `GetMember r11, r1."writeLn"`, then calls it (`Examples.wlg/01-hello.disasm.txt`). Reading an export at each use is what a module written in WebLingo needs, whose exported `var` may change (specification, section 3.9, "Exports"); but a native module's exports are the host's built-ins, which nothing assigns, so that for them it protects nothing. Discussed by the owner and Claude, 2026-11-22; added by Claude the same day. In two steps, each on both VMs:

1. A named import of a native module's function bound to the built-in itself, as a call of any built-in is, `LoadBuiltin` and `Call` without a `GetMember`, or the built-in loaded once where the program begins and called from there; a class or a constant a native module exports likewise, where it is as fixed.
2. A native module's instance made only where something uses it as a value: a whole import, `import console from "std:console";`, a dynamic one, `Module.import("std:console")`, which the VM then makes when first asked, as complete as a static import's would be, and the same instance for every importer, as a module is run once per execution; otherwise neither its `MakeModule` nor its `LoadBuiltin`s.

What stays as it is: a module written in WebLingo, whose named imports read their exports at each use; what a program writes, its errors and their positions, on both VMs; and the debugger's view of a named import (`Documents/implementation.md`, section 11, "Inspecting named imports in the debugger"), which a program compiled with `--debug` keeps, either by the module's form as now or by the debugger's learning the direct binding. Each step measured on the benchmark against `main`, alternately, as changes to the call path are (`Documents/implementation.md`, section 11, "A VM's Dispatch Method Is Sensitive to Its Locals"), with a workload that calls an imported native function in a loop.

2026-11-22 (Claude): done, and landed, in its two steps, each on both VMs. Step 1 (`3a97860`): the program's native modules are bound before its statements, and a name an import takes of one is its export itself, a function the host's built-in, `writeLn("x")` a `CallBuiltin` without a `GetMember`, a class, a constant, or a function written in WebLingo (one with optional or rest parameters) the local the program begins by making. Step 2 (`f9269e5`): a native module's instance is made by a function of its own, registered as its file, the first time something imports it as a value, a whole import in any of the program's files or a dynamic one, every later importer given the same instance, its classes and constants the program's own, captured; an import whose every name is bound to its export makes nothing, `Examples.wlg/01-hello.wlg`'s `main` 33 instructions where it was 47, with no `MakeModule`, `LoadBuiltin` or `Await` of `std:console`. Recorded in the specification (section 15.2, "Native modules"; section 5.6, "Imported native functions"), `Documents/implementation.md` (section 11, "Inspecting named imports in the debugger") and `HANDOFF.md`. Guessed, and Proposed: a call with other than the function's arguments calls the built-in's value, so that its error stays a runtime one where it was, rather than becoming a compile-time WL2006 as a call of a built-in every program has is; a program compiled with `--debug` still imports, and so makes, the module, the debugger's view of a named import reading it as before; and WLBC's check of a file's function relaxed for a native module's, a file written before, without one, still read. Measured against `main`, alternately, four rounds of 11 timed runs, with a new workload, `imports`, `std:math`'s `abs` and `sign` imported by name and called 400,000 times: step 1 about halved it on both VMs, 21.6–25.6 ms to 11.8–13.6 on the register VM and 26.8–27.8 to 16.2–17.3 on the stack VM, the other workloads within the machine's noise; step 2, measured so against step 1, 33 instructions fewer and 2.5 KB less allocated in `imports`, every workload within the noise.

Done (Claude, 2026-11-22, `3a97860` and `f9269e5`). Main's checks: the Release build, without warnings; the full suite, 4,339 tests, none failed; and the CLI, 21 programs on the register VM, the stack VM and a `--debug` build, with `main`'s build and with each step's, the same output, errors, positions and exit codes: named and whole imports of `std:console` and `std:math` together, a function's, a class's and a constant's identity through each, dynamic imports before and after a named one, a module imported whole in one file and by name in another, spreads, closures, a wrong argument count, an assignment to a name an import takes, and a missing export. Tried on Windows only; Linux not tried.

## [DONE] Built-ins Written in WebLingo, in `.wlg` Files

The built-ins written in WebLingo are C# string constants now: `Object` and `Error` (`WebLingoBinder.ObjectSource`, `ErrorSource`), the runtime errors' classes and the kinds of values' (`WebLingoPrelude.Source`), `Json` and `Iterator`, the interpolation's hidden class `<text>` (`WebLingoBinder.UseInterpolationText`), the spread and the array methods that take a function (`SpreadSource`, `ArrayMethodsSource`), `std:test` (`WebLingoTestSource.Text`), the WebLingo source in `WebLingoNativeModules.cs`, and the methods native classes declare in WebLingo (`Console`'s in `WebLingoConsoleClass.cs`, `Database.transaction` in `WebLingoDbModule.cs`, `Response`'s writing in `WebLingoHttpObjects.cs`). Move each into a `.wlg` file in the repository, beside the code that uses it, embedded in its assembly as a resource at build time and read from there by the compiler or the module, so that it is read, edited and checked as WebLingo (an editor's colouring, no escaped quotes, `weblingo check` on it where it can be), rather than as C# strings. 

What stays as it is: they stay built in, the installed runtime holding no such file a site or a user can change, since they run in every site's requests and the specification keeps one semantics on every host (`Documents/implementation.md`, section 6), a program changing a built-in's behaviour for itself by `extend`, a subclass or `setMethod`, as now; the compiler-only names and intrinsics they use (`hold`, `writtenText`, `this.__write`, `<text>`, `<format>`) stay reachable from them alone, never from a script; each stays bound as native, an error in one reported where the program called it (`Documents/implementation.md`, section 11, "A Built-in Written in WebLingo Has No File"), its resource no file of the program's; and what they compile to, every test's output, and both VMs' results stay the same.

2026-11-22 (Claude): every one of these sources is in an assembly of the portable core (`WebLingo.Compiler`, `WebLingo.StdLib.Console`, `WebLingo.StdLib.Db`, `WebLingo.Hosting`), which `Documents/native-compatibility.md` keeps from .NET's text decoding (`StreamReader` and `System.Text.Encoding`, Adapted), and where it lists no way of reading an assembly's resources, a part of .NET's reflection that Native C# may not have (WL-017). A resource read while the program runs would need both, or a decoder of the core's own, in each of those assemblies.

Q1, how a `.wlg` file reaches its assembly: (a) at build time, as a C# constant the build generates from the file, by a task of the solution's own in `Directory.Build.targets`, with no package, which the compiler and the modules read as they read the constants now: the `.wlg` file the only source, nothing read while a program runs, and the portable core needing nothing new; (b) as a manifest resource, as the task says, read while a program runs by `Assembly.GetManifestResourceStream` and decoded by a UTF-8 decoder of the core's own, both added to `Documents/native-compatibility.md` as Planned, for Native C# to give.

A1: (a) for now.

2026-11-22 (Claude): done, and landed, as A1 (a) has it: each built-in written in WebLingo is a `.wlg` file beside the code that uses it, `WebLingo/src/WebLingo.Compiler/Builtins/` for `Object`, `Error`, the kinds of values' classes, `Json`, `Html`, `Iterator`, the interpolation's `<text>` (`Text.wlg`), the spread, the array methods and `std:test` (`Test.wlg`), and `Console.wlg`, `Database.wlg` and `Response.wlg` in their projects for the methods those native classes declare in WebLingo. `WebLingo/Directory.Build.targets` makes each a constant of a class the build generates in `obj`, `WebLingoSources`, in its project's root namespace, by a task of MSBuild's own C# task factory, with no package, writing the class only when its text changes; nothing is read while a program runs, and the portable core needs nothing new. Recorded in the specification's register ("Built-ins written in WebLingo", Approved, "for now"), `Documents/implementation.md` (sections 3, 4 and 11) and `HANDOFF.md`. Left as it was: the wrapper `class Native { … }` in `WebLingoNativeModules.cs`, the compiler's way of parsing a native module's methods, not a built-in of its own; and a class with no source, `class Array {}`, which `WebLingoPrelude` makes from its name. Guessed: a runtime error's class made from one file, `RuntimeError.wlg`, its name put in, rather than a file for each kind; the files without comments of their own, the C# beside them keeping theirs, so that the strings stayed as they were; a constant's text with LF line breaks and its file's last line break left out. Checked against the strings before the move, read from the built assemblies: 21 the same, and three, `ArrayMethods`, `Database` and `Response`, without the final line break they had; `Html`, `Test` and `Console`, written as C# raw strings before, had CRLF in a Windows checkout and have LF now, as on Linux. `weblingo check` passes on five, `Error`, `Iterator`, `RuntimeError`, `Spread` and `Test`, which `BuiltinSourcesTests` checks; the others use the compiler's own intrinsics (`hold`, `toText`, `writtenText`, `toJson`, `checkCallback`), name a class a script would import (`Html`'s `Json`), or are a native class's methods rather than a file.

Done (Claude, 2026-11-22, `b999499`). Main's checks: the Release build, without warnings, and a Debug build, both from no generated files; a second build with nothing new leaving the generated class untouched; the full suite, 4,344 tests, none failed; and the CLI, a script using the array methods, the spread, an interpolation, a runtime error's class, a class extending `Integer` and `Console.writeLn`, on both VMs, and a WASP page writing a value into a script through `Html.js`. Tried on Windows only; Linux not tried.

## [DONE] A Native Module's Unused Exports Left Out

"The compiler still links in std:console function like beep() and getCursorPos, even though they are unused in the program." (the owner, in chat, 2026-11-22). A native module's exports are all bound before the program's statements (`WebLingoBinder.BindNativeUnit`, "A Native Module's Named Imports Bound Directly"), so that a program importing `writeLn` from `std:console` compiles `Console`'s and `ConsoleColor`'s classes, every member of theirs a function, and makes them where it begins. Bind an export only where the program can use it: where a name an import takes is it, or where the module's instance can be made, a whole import, an import kept, or a dynamic one; a class the module makes instances of itself (`MadeByModule`) whenever the module is imported; what a program writes, its errors, and both VMs' results the same. Added by Claude the same day.

2026-11-22 (Claude): done, and landed. A native module's export is now bound only where the program can use it: where a name an import takes is it, a function then the built-in alone, a class, a constant or a function written in WebLingo its local; or where the module's instance may be made, a whole import of it in any of the program's files, an import of it kept (a name it does not export, or a debug build), or a dynamic import anywhere in the program, `Module.import` or a component's render, where its every export is, as before; and a class the module makes instances of itself (`MadeByModule`: `DatabaseCursor`, `ComObject`, `ComEnumerator`) wherever it is imported, its functions able to give one. So `Examples.wlg/01-hello.wlg` compiles to one function, `main`, 18 instructions, 726 bytes where it was 12,610, with nothing of `Console`'s or `ConsoleColor`'s, nor `Object`, `Task` or the interpolation's class, which only `Console`'s methods needed. Found while testing it: a program importing a native module only dynamically compiles that module's import program, whose import of its file the binder makes itself, which needed its instance marked too; and each VM, given an import of a file with neither an instance nor a function that makes it, which no compiler writes, failed as it called nothing, and now reports WL4001. Recorded in the specification (section 15.2, "Native modules", Proposed) and `HANDOFF.md`. Guessed: a dynamic import anywhere in a program binding every native module's exports, the module it will ask for known only when it runs.

Done (Claude, 2026-11-22, `3fc7d89`). Main's checks: the Release build, without warnings; the full suite, 4,345 tests, none failed; and the CLI, the same 21 programs on the register VM, the stack VM and a `--debug` build, with `origin/main`'s build (`2ec573b`) and with this one, the same output, errors, positions and exit codes. `Examples.wlg/01-hello.wlbr` and `01-hello.disasm.txt`, which the owner had made again, left as they were. Tried on Windows only; Linux not tried.

## [DONE] A Dynamic Import of a File the Program Has

"Why is 16-modules example still linking the entire std:console module, even though writeLn is being imported by name?" (the owner, in chat, 2026-11-22). "A Native Module's Unused Exports Left Out" bound every native module's every export in a program that imports any module while it runs, the path known only then; `Examples.wlg/16-modules.wlg` imports `lib/teas.wlg` so, a file it has. Added by Claude the same day.

2026-11-22 (Claude): done, and landed. A `Module.import` whose path is written as a string naming a file the program has, found as its static imports are, runs that file as compiled, and so needs nothing of a native module the program does not name; one naming a native module by its scheme, `Module.import("std:console")`, needs only that module's every export; any other path, known only when the program runs, or an assembly's, still binds every native module's, and a component's render, a page, none. So `Examples.wlg/16-modules.wlg`, which imports `lib/teas.wlg` again with other values, holds nothing of `Console`'s or `ConsoleColor`'s, 35 functions, its own and the built-ins it uses: `Object`, `Module`, the interpolation's class, and `Error` with every runtime error's class, which a program that catches has. Found while checking it: a module compiled while the program runs joins the program's native modules by name, keeping the program's, so that leaving the program's without exports where the joined module makes its instance failed, WL4001, `MakeModule` finding its exports missing; a file the program has not is why such a path keeps the rule as it was. Recorded in the specification (section 15.2, "Native modules", Proposed) and `HANDOFF.md`.

Done (Claude, 2026-11-22, `878d573`). Main's checks: the Release build, without warnings; the full suite, 4,346 tests, none failed; and the CLI, 25 programs on the register VM, the stack VM and a `--debug` build, with `origin/main`'s build (`73450f4`) and with this one, the same output, errors and exit codes: the 21 before, a file loaded while the program runs whose `writeLn` and `Console` the program compares with its own, that file's whole import of `std:console` against the program's named one, `16-modules.wlg`, and a path made while it runs. `Examples.wlg`'s compiled files and disassemblies, the owner's, left as they were. Tried on Windows only; Linux not tried.

## [DONE] The Name of a File's Top Level in `disasm`

"The disasembly of 16a-modules still shows function function0 lib/teas.wlg (1 parameters, 2 captures, 9 locals, 14 registers) instead of giving the name of the function." (the owner, in chat, 2026-11-22). `function0` is `lib/teas.wlg`'s top level, the module's body, which has no name in the source: the compiler names it by its file, as it names the program's own top level `main`, and that name is also what the VS Code debugger's call stack shows for it. Added by Claude the same day.

Q1: How should `disasm` show a file's top level: (a) by its file, said so, `function function0, the top level of lib/teas.wlg (…)`, and the program's `function main, the top level of 16a-modules.wlg (…)`, the debugger's call stack unchanged; or (b) named `exec`, the method of `Module` its body is (specification, section 3.8), with its file after it, `function function0 exec (lib/teas.wlg) (…)`, in the debugger's call stack too?

A1: "function0 is not very descriptiive. Meybe something like `function module_body of lib/teas.wlg (…)`"; "Don't rename main. It clearly shows the entry point." (the owner, in chat, 2026-11-22).

2026-11-22 (Claude): done, and landed. Both disassemblers head a file's body, its top level, `function module_body of lib/teas.wlg (…)`, and an instruction refers to it as `module_body(lib/teas.wlg)`, `MakeClosure r10, module_body(lib/teas.wlg)`, a native module's body, which makes its instance, so too, `module_body(std:console)`; the program's own stays `main`, the entry point, and every other function `function3 serve`, as before. Only `disasm`'s text changed: the function's name in the file, which the VS Code debugger's call stack and the VMs' messages show, is still its file's. Recorded in the specification (section 15.2, "WLBC on the command line", Proposed).

Done (Claude, 2026-11-22, `581d78b`). Main's checks: the Release build, without warnings; the full suite, 4,349 tests, none failed, a new test of the heading and the reference on both VMs among them; and the CLI, `compile`, `disasm` and `run` of copies of `Examples.wlg/16a-modules.wlg` and `16-modules.wlg` on both VMs, and of a whole import of `std:console`. `Examples.wlg`'s compiled files and disassemblies, the owner's, left as they were. Tried on Windows only; Linux not tried.

## [DONE] No Empty Constructors

"Why does this function even make it into the compiled program: `function function7 Text (1 parameters, 1 locals, 1 registers) 00 Return`. It does nothing." (the owner, in chat, 2026-11-22). The binder gave every class that declares no constructor one of its own, empty, or only calling `super()`, which `new` passes over for a base's that a class declares, and which, where none does, only refuses arguments, as `new` does for a class without one. Added by Claude the same day.

2026-11-22 (Claude): done, and landed. A class that declares no constructor has none in the program: `new` passes its arguments on to the nearest base that declares one, as before, or, with none, makes the instance without a call, its arguments WL2006, `'P' takes 0 arguments, not 1.`, as for a class made while the program runs, the VMs unchanged. `Object` alone keeps one that does nothing, which `super(…)` runs where no base declares one, and which a module compiled while the program runs may need, its classes joined to the program's. So `16a-modules.wlg` has no `Text` constructor, and a program of classes without constructors runs one call fewer for each `new`. Found while checking it: `super(1)` in a class whose bases declare no constructor now says `'Object' takes 0 arguments, not 1.`, where it named the base, `'A'`, whose own empty constructor it ran; `new A(1)` still names `A`. Recorded in the specification (section 15.2, "Classes", Proposed).

Done (Claude, 2026-11-22, `3360089`). Main's checks: the Release build, without warnings; the full suite, 4,349 tests, none failed, and the new test, on both VMs, 4,351 in all; and the CLI, 34 programs on the register VM, the stack VM and a `--debug` build, with `main`'s build (`581d78b`) and with this one, the same output, errors and exit codes, but for that one message: the 25 before, and 9 of classes without constructors, `new` with and without arguments, `super` with and without them, arguments passed on through several bases, classes extending `Number`, `Array`, `Error` and `Class`, a constructor given and removed while the program runs, a class extending one made while it runs, modules imported while it runs, and 100,000 `new`s. `Examples.wlg`'s compiled files and disassemblies, the owner's, left as they were. Tried on Windows only; Linux not tried.

## [DONE] `main` of Its File

"To be consistent, also say `function main of **16a-modules.wlg** (10 locals, 15 registers)`" (the owner, in chat, 2026-11-22), after "The Name of a File's Top Level in `disasm`" headed a file's body `function module_body of lib/teas.wlg (…)`. Added by Claude the same day.

2026-11-22 (Claude): done, and landed. Both disassemblers head the entry point `function main of 16a-modules.wlg (…)`, its name kept, as a file's body is headed `function module_body of lib/teas.wlg (…)`: the program's file, or, in a module that lists no files, its own name, `main` alone where it has neither. The Language Reference's example of `disasm`, `hello.wlg`'s, written before `std:console`, shows what `disasm` writes now. Recorded in the specification (section 15.2, "WLBC on the command line", Proposed).

Done (Claude, 2026-11-22, `625f322`). Main's checks: the Release build, without warnings; the full suite, 4,351 tests, none failed, the tests of `disasm`'s heading updated to it; and the CLI, `compile` and `disasm` of `hello.wlg`, of a script without imports on the stack VM, and of a copy of `Examples.wlg/16a-modules.wlg`. `Examples.wlg`'s compiled files and disassemblies, the owner's, left as they were. Tried on Windows only; Linux not tried.

## [DONE] Comparative Benchmarks

Create a series of representative benchmarks comparing the performance of WebLingo programs versus JavaScript, Python, and Lua.

2026-11-23 (Claude): of the runtimes, only Python 3.14 is installed here; Node.js and Lua are not. My guesses, to change if you say so: the same programs in each language, each giving the same result, `loops`, `calls` (a recursive `fib`), `closures`, `strings`, `arrays`, `maps`, `objects` (instances, methods and fields) and `sort` (an array sorted by a function), the benchmark's own workloads made comparable; JavaScript on Node.js, with its JIT and without it (`--jitless`, V8's interpreter alone, the nearest to WebLingo's VMs), Python on CPython, and Lua on PUC-Rio's Lua 5.4, an interpreter, as WebLingo's are; WebLingo on both VMs, from source and from compiled bytecode; each program timed as a whole run, as a user runs it, each language's start-up, an empty program's run, measured beside it; run by `WebLingo.Benchmarks`, given a mode of its own; and the results recorded in the specification, section 5.6, as the VMs' comparison is.

Q1: May I download the runtimes the comparison needs: Node.js's current LTS for Windows x64, its zip from nodejs.org (about 30 MB), and Lua 5.4's Windows x64 executables from LuaBinaries (SourceForge, about 1 MB), each into a folder of its own in a git-ignored `Runtimes/`, as `WebServers/` keeps nginx's and Apache's, each recorded in `Documents/implementation.md` with where it came from, its size and its SHA-256: (a) yes; (b) no, you install them, and the comparison is given their paths?

A1: (a) Yes. Install Node and Lua globally, so they can be used outside of this repo as well.

2026-11-23 (Claude): Node.js 26.13.1, its current LTS, downloaded from nodejs.org as its installer, `node-v26.13.1-x64.msi`, 36,564,992 bytes, its SHA-256 `0398ced8446e119478c724d1aa37c5afd6aa1a76fceab12787eb9714c5ed8b45` as nodejs.org's `SHASUMS256.txt` gives it, which installs it for the whole machine, on its PATH; this session may not start an installer, so the owner runs it. Lua: LuaBinaries' zip has no installer, its folder then needing to be put on the PATH, a system setting I may not change; asked in chat whether to download instead a community build's installer, `Lua-5.4.6-win64.msi`, the one winget's `DEVCOM.Lua` installs. The programs and the comparison, meanwhile.

2026-11-23 (Claude): the owner installed both, Node.js from its installer above, and Lua 5.4.6 for their account, each on the PATH. Done, and landed. `WebLingo.Benchmarks --compare` (`WebLingoComparison`) runs nine programs, each written alike in WebLingo, JavaScript, Python and Lua (`benchmarks/WebLingo.Benchmarks/Comparative/`), as the guesses above have them, with `empty`, each runtime's start-up, which its second table takes away, each run a process of its runtime's, timed whole, its one line checked. Found: as a user runs it, WebLingo is the slowest of them, as fast as CPython only at `loops`, two to seven times slower than CPython and Lua at the rest, `objects` slowest by far, 1.3 s against CPython's 0.2 s; its start-up, 136 to 172 ms, against CPython's 37 and Lua's 16, is .NET's JIT compiling `weblingo` as it starts, and the in-process benchmark, fully optimized before it times, runs the same work two to three times faster; neither `weblingo`'s build nor its publish is ReadyToRun. Recorded in the specification (section 5.6, "Against other languages"), `Documents/implementation.md` (section 3, the benchmark, and section 8, "Comparing with other languages", with the runtimes measured) and the solution's `README.md`.

Done (Claude, 2026-11-23, `0881a59`). Main's checks: the Release build, without warnings; the full suite, 4,371 tests, none failed, `ComparisonTests` among them, 20 tests, each program in every language, WebLingo's on both VMs, and the command on WebLingo alone; and the comparison itself, every program on all eight runtimes, each giving its line. Tried on Windows only; Linux not tried.

## [DONE] One Instance per `new`, Its Fields in a Small Array

the specification, section 5.6, "Where the time goes", cause 1: every `new` made two `InstanceObject`s, and each holds its fields in a `Dictionary` and a `List`. One `InstanceObject` an instance, its fields in a small array, searched in order, a dictionary only for many. Tried on `claude/experiment-instances`, not landed: `new P(i, i)` 842 to 300 ns, `objects` 1,549 to 476 ms in one process, the conformance tests passing. Measured against `main`, alternately, on the benchmark and on the comparison (`--compare`), as `Documents/implementation.md` asks of a change to a VM's `Run`, the comparison's numbers in section 5.6 updated. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

2026-11-23 (Claude): done, and landed. An instance of an ordinary class is made once, where `TryAllocateInstance` made a second `InstanceObject`, which only a class extending `Array`, `Map`, `Set`, `Module` or `Task` uses; and its fields are an array, in the order they were first assigned, searched in order, by reference first, a dictionary of their positions only past eight (`WebLingoHeap.InstanceObject`, `FieldsSearched`). Measured against `main`, alternately, three rounds of the benchmark each: `objects` 137–155 ms to 44–77 ms on both VMs, `io`, which makes a `File` for each file, 2.6–3.6 s to 1.0–1.2 s, the rest within the machine's noise; and the comparison through `weblingo`, its start-up taken away, `objects` 1,301 ms to 647 ms on the register VM, 1,351 to 783 on the stack VM. At the owner's word the same day ("Don't run the benchmarks every time. Run them after you finish all 8 optimizations."), the comparison's numbers in section 5.6 are updated once, after the optimizations, not by each.

Done (Claude, 2026-11-23, `d5df7a7`). Main's checks: the Release build, without warnings; the full suite, none failed, a conformance case of an instance of twelve fields, read, assigned and given another, on both VMs and read back from its file, among them; and the benchmark and the comparison, above. Tried on Windows only; Linux not tried. Corrected the same day: the conformance case did not compile, its expected output's line break a real one inside its C# string, so that the suite that passed was the earlier build's; fixed in `de0cc94`, where the case passes and every test with it.

## [DONE] Choose Between the Two VMs

the specification, section 5.6, "Where the time goes": every task above is built on both VMs until the owner chooses between them (WL-BC002, Provisional; section 5.6, "Both VMs, for now"), and the stack VM is the slower on every program measured, running about twice the instructions. The numbers put to the owner, for the choice. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

Choose the Register VM, Stop work on Stack VM. Tag the present commit as `stack-vm-end` and remove stack VM from the project on the next commit.

2026-11-23 (Claude): done, and landed. The commit the answer named, `d5df7a7`, did not compile: its new conformance case wrote its expected output's line break as a real one, inside a C# string, which the runs before it never compiled, using their earlier build. Fixed on `main` first (`de0cc94`, every test passing), and the tag `stack-vm-end` put there, the last commit with the stack VM, tagged locally, not pushed. Then the stack VM removed: its project, `WebLingo.VM.Stack`, its code generator and compiler, its bytecode, verifier and disassembler, `IWebLingoStackModuleLoader`, the hosts' and the command lines' paths to it, `--register`, `--stack` and `--vm`, the configurations' `"vm"`, now a setting they do not know, the benchmark's stack engine and the comparison's stack runtimes, and the stack VM's tests; the tests that compared the VMs now compare a program's compiled file with its source, the conformance programs `ConformancePrograms.All`. At the owner's word the same day ("Change the bytecode file extension to just `.wlb`"), a bytecode file is a `.wlb` (`WebLingoBytecodeFile.Extension`); my guesses, Proposed: an earlier build's `.wlbr`, and the first `.wlbc`, still read as a `.wlb`, by the header; a `.wlbs`, or a header naming the stack VM, refused with WL4001, `The file holds the stack VM's bytecode, which WebLingo no longer runs: it has one VM, the register VM.`; `--register`, `--stack` and `--vm` usage errors as any option a command does not take. Recorded in the specification (the register's WL-BC002, Approved, "Bytecode filenames" and "VM configuration"; section 5.2, "Decided", which says the stack VM's mentions elsewhere describe it as it was until the tag; sections 5.3, 5.6 and 15.2, "WLBC on the command line"), `Documents/implementation.md`, the Language Reference, the WASP User Manual, the solution's `README.md`, the dependency diagram, `.gitignore` and `HANDOFF.md`, which lists the tests still taking a VM parameter of one value, a cleanup left for later.

Done (Claude, 2026-11-23, `47f4dc8`). Main's checks: the Release build, without warnings; the full suite, 3,375 tests, none failed, the conformance tests 2,250 where they were 3,177, the stack VM's own gone; and the CLI by hand: `compile`, `run` and `disasm` of `samples/Arithmetic/arithmetic.wlg` and its `.wlb`, `compile --stack` a usage error, a `.wlbs` refused, a `.wlbr` run, a `weblingo.config` with `"vm"` refused, the usage, and `wasp run` of `Examples.wasp/example1/about.wasp`. Tried on Windows only; Linux not tried.

## [DONE] Small Maps Without a Dictionary

the specification, section 5.6, "Where the time goes", cause 1: a map holds its entries in a `Dictionary` and a `List` (`MapObject`), `{ a: i }` about 680 ns. A map of a few entries in a small array, searched in order, a dictionary only for many. Measured against `main`, alternately, on the benchmark and on the comparison (`--compare`), as `Documents/implementation.md` asks of a change to a VM's `Run`, the comparison's numbers in section 5.6 updated. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

2026-11-23 (Claude): done, and landed. A map's or a set's entries are an array, in no defined order, a removal filling its place from the end, searched in order, by a key's kind, bits and text, a hash table of their positions only past eight (`WebLingoHeap.MapObject`, `EntriesSearched`), where every map and set made a `Dictionary` and a `List`, as instances did until "One Instance per `new`, Its Fields in a Small Array". Not measured: the owner's word the same day, "Don't run the benchmarks every time. Run them after you finish all 8 optimizations."

Done (Claude, 2026-11-23, `995c9c3`). Main's checks: the Release build, without warnings; the full suite, 3,377 tests, none failed, a conformance case of a map and a set of twelve entries, added, replaced, removed, visited and looked up across the change, and a small map's removal, run and read back from its file, among them. Tried on Windows only; Linux not tried.

## [DONE] A Value's Text Without a Method Call

the specification, section 5.6, "Where the time goes", cause 2: `i.toString()` runs `Object`'s, written in WebLingo, and the text builder, 165 ns and 521 bytes for an integer. An integer's, a number's, a decimal's, a Boolean's and a string's text made at once, by the VMs or the runtime, where no class has been given a `toString()` of its own (`WebLingoHeap.ToStringGiven`). Measured against `main`, alternately, on the benchmark and on the comparison (`--compare`), as `Documents/implementation.md` asks of a change to a VM's `Run`, the comparison's numbers in section 5.6 updated. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

2026-11-23 (Claude): done, and landed. `ToText` of a value that is neither a collection nor an instance takes its text from the formatter at once, as the text builder gives it, a string being its own text, nothing allocated (`TryPlainText`); and `x.toString()` of an integer, a number, a decimal, a Boolean or a string, without arguments, where no class has been given a `toString()` of its own (`WebLingoHeap.ToStringGiven`), so that `Object`'s, `toText(this)`, would run, the same, without that call (`TryValueToString`). Each a helper the VM's `Run` calls, marked not to be inlined, adding no local of `Run`'s, as `Documents/implementation.md` asks. Not measured: the owner's word the same day, the benchmarks after the eight optimizations.

Done (Claude, 2026-11-23, `d0a625e`). Main's checks: the Release build, without warnings; the full suite, 3,379 tests, none failed, a conformance case of an integer's, a number's, negative zero's, NaN's, a decimal's, a Boolean's and a string's `toString()` and an interpolation of them, run and read back from its file, among them; and the CLI, that program with the build before and this one, the same output. Tried on Windows only; Linux not tried.

## [DONE] Fewer Instructions for Loops and Arithmetic

the specification, section 5.6, "Where the time goes", cause 5: on both VMs, instructions that take a constant or a small integer as an operand (`Add`, `Less`, `Equal`, `Remainder` and the others), a test and its branch in one instruction, an increment that copies nothing where its value is not used, and a loop tested at its bottom, so that `loops`' inner loop is about half its 16 instructions; the verifiers, the disassemblers and WLBC's reader and writer with them. Measured against `main`, alternately, on the benchmark and on the comparison (`--compare`), as `Documents/implementation.md` asks of a change to a VM's `Run`, the comparison's numbers in section 5.6 updated. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

2026-11-23 (Claude): done, and landed, on the one VM, the stack VM gone the same day ("Choose Between the Two VMs"). Ten operations whose right operand is a constant, read from the module's constants: `AddConstant`, `SubtractConstant`, `MultiplyConstant` and `RemainderConstant`, and the six comparisons', each with the register form's results and errors, sharing its helper (`Arithmetic`, `Compare`, given the right operand). Not as a fused compare-and-jump instruction, as this task proposed, since a class's own operator runs as a call whose result a register takes: a comparison whose next instruction is a `JumpIfFalse` or a `JumpIfTrue` on its result takes the branch in its own dispatch, its result in its register too (`compared:` in `Run`, no local added), the operator's result branched on by the jump after its call. `x++` whose value is not used, an expression statement's or a `for` loop's step, bound as `++x` (`WebLingoBinder.Discarded`); `while` and `for` entered by a jump to their test, at their bottom. `loops`' inner loop 16 instructions to 12, 6 to 9 of them dispatched on each turn. The verifier, the disassembler, the VM's renumbering of a joined module's constants (`TryLink`) and the benchmark's code sizes know the new operations; the fuzz tests take every operation of the enum. Recorded in the specification (section 5.6, "Where the time goes", Proposed) and `Documents/implementation.md`. Not measured: the owner's word, the benchmarks after the eight optimizations.

Done (Claude, 2026-11-23, `1cdb7ac`). Main's checks: the Release build, without warnings; the full suite, none failed, `CodeGeneratorTests`' three new and changed tests, a temporary's reuse, every constant form, and a loop tested at its bottom with an increment copying nothing, and a conformance case of a class's `<` with a constant in a `while` and an `if`, `continue` and `break` in a `for`, `for (;;)`, `do … while`, and a string's, a decimal's and an integer's constants, run and read back from its file, among them, `i++` past the largest integer still WL3002; and the CLI, `loops.wlg` compiled, disassembled and run, that case's program with the build before and this one, the same output, `samples/Arithmetic`, and `wasp run` of `Examples.wasp/example1/brewing.wasp`. Tried on Windows only; Linux not tried.

## [DONE] Short-Lived Objects Freed Before .NET Promotes Them

the specification, section 5.6, "Where the time goes", cause 1: what a program no longer reaches stays in the heap's table until WebLingo's collector runs, at a million units, so that .NET's collector promotes it first, 40% of `new P(i, i)`'s time in .NET's collections. A young generation of WebLingo's collector, or another way to free short-lived objects sooner; WebLingo's own non-moving mark-and-sweep collector being Approved (section 4.2), a design for the owner first, its questions here. Measured against `main`, alternately, on the benchmark and on the comparison (`--compare`), as `Documents/implementation.md` asks of a change to a VM's `Run`, the comparison's numbers in section 5.6 updated. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

2026-11-23 (Claude): done, and landed, without changing the collector's design, which stays non-moving mark-and-sweep, as Approved (section 4.2): the heap's first collection, and any while little is live, at 32K units in place of 1M (`WebLingoHeap.FirstCollectionAt`), its later ones at twice what is live, as before. Probed on the register VM, two million turns of each, the median of three runs: an instance of two fields made and dropped 610 to about 290 ns, .NET's pauses 286 to 13 ms a run, its oldest generation's collections 49 to 3; a map literal about 470 to 95 ns; 16K units the same, 64K and 256K units less. Where much is live, 100,000 instances kept, the gain is small, 704 to about 550 ns, the threshold being twice what is live, so that .NET still promotes what is dropped: a young generation would be the remedy there, a design of its own, its question in the task "A Young Generation for WebLingo's Collector". Not measured on the benchmarks: the owner's word, after the eight optimizations.

Done (Claude, 2026-11-23, `67e8fe0`). Main's checks: the Release build, without warnings; the full suite, none failed, a runtime test of a heap collecting within its first units among them. Tried on Windows only; Linux not tried.

## [DONE] Members Found Without Their Names: Shapes and Caches

the specification, section 5.6, "Where the time goes", cause 3: a field's read is a dictionary's lookup by its name, a method's call two, a field's assignment about six. Instances of a class sharing a shape, their fields' positions, and each member's instruction remembering the shape and position, or the method, it found last, on both VMs. A design first, after "One Instance per `new`, Its Fields in a Small Array". Measured against `main`, alternately, on the benchmark and on the comparison (`--compare`), as `Documents/implementation.md` asks of a change to a VM's `Run`, the comparison's numbers in section 5.6 updated. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

2026-11-23 (Claude): done, and landed, by a design of my own, Proposed, in place of shapes and per-instruction caches: those would add state to the VM's dispatch method, which `Documents/implementation.md` warns against, and instances' fields, an array since "One Instance per `new`", searched by reference first, leave little to gain on a field, a read probed at about 13.5 ns. A class remembers the four names its methods were last looked up by, compared by reference, the program's names being the same strings each time, and what each lookup found, before its methods' table, emptied when its methods are gathered anew (`WebLingoHeap.FindMethod`, `ClassObject.LookedUp`); a method's call, and a field's assignment, which asks whether the name is a method's, find it so. Probed on the register VM, two million turns: a method's call 61 to 55 ns, two methods' 131 to 111, a field's assignment 27 to 26. What is left of a method's call is mostly the call's own, "A Cheaper Call". Not measured on the benchmarks: the owner's word, after the eight optimizations.

Done (Claude, 2026-11-23, `8685a51`). Main's checks: the Release build, without warnings; the full suite, none failed, a conformance case of a method given anew by `setMethod` while the same place calls it, and a base's given anew under a subclass, run and read back from its file, among them, that program giving with the build before the same. Tried on Windows only; Linux not tried.

## [DONE] A Native `sort`

the specification, section 5.6, "Where the time goes", cause 6: `sort` is a merge sort written in WebLingo, 120 million instructions for 200,000 integers. Sorted by native code, calling the comparison function, if one is given, through the VM, which a native function cannot do yet; and the other array methods that take a function, where the same serves them. Measured against `main`, alternately, on the benchmark and on the comparison (`--compare`), as `Documents/implementation.md` asks of a change to a VM's `Run`, the comparison's numbers in section 5.6 updated. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

2026-11-23 (Claude): done, and landed, as Proposed in the specification, section 15.2, "Array methods". `sort` calls a built-in of the runtime's, `#sortArray`, whose work, `WebLingoSort`, is the merge sort that was written in WebLingo, comparing the same elements in the same order, so that it gives what that gave and fails where that failed: every edge tried against `main`'s build gave the same, its comparison count included. For it, a built-in may call the program's functions without awaiting them (`IWebLingoCallingBuiltin`, `WebLingoCalls`): the VM makes each call the work asks for as any call, in a frame of its own on its own stack, and gives the work what it returned, so that a call that throws unwinds as any; no native module can register one yet. The other array methods that take a function stay written in WebLingo: made native, they would have to await a function's task, which a work cannot, and would differ for a class extending `Array`; what cost them most was their cells, their `async` continuations capturing the loop's variables, given as arguments now. Probed in one process: 200,000 integers sorted by a comparison function 577 to 217 ms, 81 to 6.7 million instructions, by `<` 455 to 24 ms, strings by `<` 499 to 96 ms; `map` 223 to 147 ns an element, `filter` 270 to 134, `any` 184 to 116, `reduce` 124 to 116; `weblingo run` of the comparison's `sort` about 950 to 590 ms, start-up included. Not measured on the benchmark or the comparison, against `main`, alternately, as `Documents/implementation.md` asks of a change to `Run`: the owner's word, after the eight optimizations.

Done (Claude, 2026-11-23, `e5ccee0`). Main's checks: the Release build, without warnings; the full suite, none failed, four new conformance cases among them, a class's operator `<`, a comparison that changes the array or sorts, a built-in as the comparison, and the errors of a comparison that gives what `<` cannot compare or throws, and of an operator that gives no Boolean, each giving what `main`'s merge sort gave, run and read back from its file. Tried on Windows only; Linux not tried.

## [DONE] A Cheaper Call

the specification, section 5.6, "Where the time goes", cause 4: a call and its return cost about 40 ns, Lua's 10 to 15. What a call does looked at, and what can be done once rather than on each call, done so, on both VMs. Measured against `main`, alternately, on the benchmark and on the comparison (`--compare`), as `Documents/implementation.md` asks of a change to a VM's `Run`, the comparison's numbers in section 5.6 updated. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

2026-11-23 (Claude): done, and landed, as Proposed in the specification, section 5.6, "Where the time goes", cause 4. Of what a call does, what could be done once is its frame's roots: each call added its registers and closure to the heap's list of roots and each return removed them, four checked operations; the VM now gives the heap its frames once (`WebLingoHeap.Frames`, an `IWebLingoRootSource`), the collector reading them when it collects, from the first frame to the depth that runs, which `Run` keeps in a field. Probed in one process: a function's call 35 to 26 ns, with two arguments 38 to 30, a closure's 46 to 36, a method's 56 to 43, `fib(27)` 35.2 to 29.0 ms. Testing for a function first, before a bound method and a built-in, measured within the machine's noise and was left out; dropping the clearing of a frame's registers on its return would have saved about 1 ns, and let a reused frame's stale values keep objects alive, so it stays. "On both VMs" in the task's text: WebLingo has one VM since "Choose Between the Two VMs". Not measured on the benchmark or the comparison, against `main`, alternately, as `Documents/implementation.md` asks of a change to `Run`: the owner's word, after the eight optimizations. Found on the way, and added as a task of its own, "Values Lost When the Heap Collects as One Is Added".

Done (Claude, 2026-11-23, `071ad6b`). Main's checks: the Release build, without warnings; the full suite, none failed, a runtime test of a root source and two conformance cases of values that only frames 300 deep hold, and async frames, kept while the heap collects, among them; and the VM, runtime, hosting, conformance and template tests on a heap made to collect every 16 units, failing the same tests with the change as on `main`. Tried on Windows only; Linux not tried.

## [DONE] Values Lost When the Heap Collects as One Is Added

Found by Claude, 2026-11-23, while trying "A Cheaper Call" on a heap made to collect every 16 units, on `main` as on its branch: the heap's methods that add a value to an object, `TryAddField` (and so `TrySetField`'s new field), `TryAddToArray`, `TryInsertIntoArray`, and a set's and a map's new entry, reserve its unit before they store it, and the reservation may collect, so that a value the caller has just made, which only .NET holds, no root, is freed, and its handle given to the next object made. A runtime error that a program catches gets its code as its `message` (`WebLingoExceptions.TryMakeError`): `try { var x = 1 + "a"; } catch (e) { writeLn(e.message); }` writes `WL3001`. With the heap's own thresholds a program meets it only where a collection falls between the two, rarely, and then gets a wrong value, not an error. Under that stress, 219 conformance cases fail alike on `main` and the branch, many of them for this. To do: each value stored before its unit is reserved, the store undone where the reservation fails, so that the collector reaches the value through its object; a test that collects at each reservation; and the stress's other failures looked at. Added by Claude, 2026-11-23.

2026-11-23 (Claude): done, and landed. Rather than each store undone where its reservation fails, the heap keeps what is about to be stored while it reserves (`TryReserve`'s kept values, a map's key among them) and what an object being made holds while its allocation collects (`Collect`'s `making`), one change serving every adding method and every allocation. The stress's other failures were five more places of the same kind, each fixed: a script's parameters made into an array no root held, so that one could arrive as `null` (`TryGiveParameters`); the instance `new` made, which waited in `Run`'s locals while its constructor's call made a rest parameter's array or an async constructor's task (`_made`, a root of the execution's); a pending call's arguments, a module's values among them (`_operands`, a root now); `Task.all`'s task while its results' array was made; and `std:http`'s header names while their values were made. With them, under that stress, every conformance case passes but one that counts collections, and the runtime tests fail only where a test keeps its own values unrooted; `Documents/implementation.md` has the trap, "A Value Just Made Is No Root". Found on the way, and added as a task of its own, "The Benchmark's Native Built-ins Shared Between Threads", a test that failed once in a full run, for a reason of the benchmark tool's.

Done (Claude, 2026-11-23, `9c2cc3b`). Main's checks: the Release build, without warnings; the full suite, none failed but `BenchmarkTests.AWorkloadGivesItsExpectedOutput` (`io`) once, the tool's own race, which passed in three runs of its project after; a runtime test that makes the heap collect at each step of adding a value, for fields, arrays, sets, maps, a new array's elements and a runtime error made an `Error`, which fails without the fix; and the conformance, VM, runtime and template tests on a heap made to collect every 16 units. Tried on Windows only; Linux not tried.

## [DONE] Calls Slower Since Fewer Instructions

Found by Claude, 2026-11-23, measuring the eight remedies of the specification's section 5.6, "Where the time goes", commit by commit (its "After the remedies"): "Fewer Instructions for Loops and Arithmetic" (`1cdb7ac`) made the benchmark's `calls`, `fib(25)`, 12.5 to 12.8 ms in place of its parent's 11.0 to 11.7, and `closures` 15.0 to 15.3 in place of 14.2 to 14.4, three rounds run alternately, though both run fewer instructions, as it made `loops` 24 to 18 ms. It changed the VM's dispatch method, `Run`: ten cases of operations with a constant operand, and a comparison's branch taken in its own dispatch (`compared:`), which `Documents/implementation.md` warns may cost calls ("A VM's Dispatch Method Is Sensitive to Its Locals"); it landed unmeasured on the benchmark, at the owner's word, until the eight were done. "A Cheaper Call" took the loss back, `calls` 10.5 ms now, as before them; without it, calls would gain as much again. To do: which of its lines cost calls, restored one at a time, and what keeps `loops`' gain without that loss, measured against `main`, alternately, on the benchmark and on the comparison. Added by Claude, 2026-11-23.

2026-11-23 (Claude): done, and landed, as Proposed in the specification, section 5.6, "After the remedies". What cost calls was not a line of `compared:`, which without its fused branch made `calls` hardly faster and `loops` slower, but the size of `Run`: `Arithmetic` and `Compare`, inlined whole into each operation, 21 copies where there had been 11. Each now keeps its two fast paths inline and gives the rest to a helper .NET does not inline (`ArithmeticOtherwise`, `CompareOtherwise`). On the benchmark, three rounds against `main` run alternately: `calls` 11.3 to 10.1 ms, `closures` 13.9 to 12.2, `imports` 10.7 to 9.3, `loops` 19.5 to 17.7; on the comparison, two rounds, from source: `loops` about 510 to 290 ms, `sort` 410 to 350, `calls` 270 to 233. On the way, `io` proved to measure .NET's thread pool more than the VM, bimodal by build, which the specification now says where it gave `io`'s ratio for the eight remedies.

Done (Claude, 2026-11-23, `d3b1b47`). Main's checks: the Release build, without warnings; the full suite, none failed, `OperatorTests` among them, every operator on edge values through the VM against the runtime's; the benchmark and the comparison against `main`, alternately. Tried on Windows only; Linux not tried.

## [DONE] The Benchmark's Native Built-ins Shared Between Threads

Found by Claude, 2026-11-23: `BenchmarkTests.AWorkloadGivesItsExpectedOutput` (`io`, `WebLingo.VM.Tests`) failed once in a full run with a `NullReferenceException` in `Dictionary.TryInsert`, under `WebLingoBuiltinRegistry.Register`, where `WebLingoNativeModuleLoader` loaded a native module for `WebLingoBenchmark.Measure`, then passed three runs of its project in a row: `WebLingoBenchmark`'s `NativeBuiltins` is one static registry, which tests running at once on xUnit's threads write together, a `Dictionary` written by two threads. To do: a registry for each measurement, or one written under a lock, the tool's only; the benchmark's numbers unchanged. Added by Claude, 2026-11-23.

2026-11-23 (Claude): done, and landed: a loader and a registry for each measurement (`WebLingoBenchmark.StandardModules`), in place of one static pair, so that measurements made at once share neither; no lock, the tool's code staying as plain as the product's. The modules' assemblies load once, as before, and what is timed is unchanged: `calls` 9.6 ms and `io` 1.0 s, as the last runs gave.

Done (Claude, 2026-11-23, `cd5ee8c`). Main's checks: the Release build, without warnings; the VM tests, whose benchmark and comparison tests use the tool, three runs, none failed; and the benchmark's command run on two workloads. Tried on Windows only; Linux not tried.

## [DONE] Mail: `std:mail`

A prerequisite of "Extensive WASP Example" (its A5): a standard module sending mail by SMTP, for confirming a sign-up and resetting a password. Designed with the owner first, its questions here. Added by Claude, 2026-11-23.

2026-11-23 (Claude): what is there: `std:net`'s TCP sockets and `std:http`, through the platform's network (`IWebLingoNetwork`, `DotNetNetwork`), with no TLS of the platform's own, `std:http`'s HTTPS being .NET's HTTP client's. My guesses, to change if you say so: a native module, `WebLingo.StdLib.Mail`, `std/mail.dll`, through the platform layer, asynchronous, `await mail.send(…)`, with no synchronous form; text and HTML bodies, their headers in UTF-8 as RFC 6532 and MIME's encoded words have them, attachments later; a failure, a server that refuses or cannot be reached, a `MailError`, a code of its own, extending `Error`; and its tests against a small SMTP server of their own, on 127.0.0.1, never a real one.

Q1: How it speaks SMTP: (a) a client of WebLingo's own, in an adapter, on .NET's sockets, TLS by .NET's `SslStream`, with no package, as the FastCGI host was written; (b) .NET's `SmtpClient`, with no package, which Microsoft no longer recommends for new code; or (c) MailKit, MIT-licensed, a package with MimeKit and BouncyCastle, in an adapter of its own?

A1: (a), a client of WebLingo's own, in an adapter, on .NET's sockets, TLS by `SslStream`, with no package. For sending only, to a relay: STARTTLS on 587 and implicit TLS on 465, AUTH PLAIN and LOGIN, and XOAUTH2 with a token the script gives; and a MIME writer of its own, text and HTML alternatives, attachments, and non-ASCII headers, with no MIME parser. DKIM, SMTPUTF8, and reading mail (IMAP, POP3) left for later, when asked for, MailKit then reconsidered.

Q2: Where its server is set: (a) in `wasp.config` and `weblingo.config`, `"mail": { "host": "smtp.example.com", "port": 587, "user": "…", "password": "…" }`, one server a site sends through, which needs no permission; (b) named servers, as `"databases"` names databases, a script choosing one by name; or (c) each `send` naming its server, under `wasp` only one `"network"` allows?

A2: (b), named servers, as `"databases"` names databases, `"mail": { "default": { "host": "smtp.example.com", "port": 587, "user": "…", "password": { "env": "SMTP_PASSWORD" } }, "newsletter": { … } }`, a `send` that names none using `"default"`, or the one server listed; and (c) besides, a `send` naming a server of its own, its host, port and credentials, under `wasp` only one that `"network"` allows, as a database's connection string in a script is allowed beside the configuration's. A secret in the configuration, the mail's and the databases' alike, may be given where it is kept rather than as itself: `{ "env": "NAME" }`, an environment variable, or `{ "file": "path" }`, a file outside the site, read by the host, never by a script, so that no password need be stored in the configuration, or checked in with it; the operating system's secret store (Windows Credential Manager, libsecret) left for later. A script never sees a server's password, and no error, nor the development error page, shows it.

Q3: What a message is: (a) a map, `await mail.send({ from: "…", to: ["…"], subject: "…", text: "…", html: "…" })`, `to` a string or an array; or (b) a class, `var message = new MailMessage();`, its members set, then `await mail.send(message)`?

A3: (a), a map, `await mail.send({ from: "…", to: "…", subject: "…", text: "…", html: "…" })`, with `cc`, `bcc`, `replyTo`, `headers` and `attachments` besides, `to`, `cc` and `bcc` each a string or an array, an attachment a map, `{ path }`, a file read by the host, or `{ name, content, type }`, its content `Bytes` or a string; every key checked, one `send` does not know an error naming it, with the name it may have meant, `'subjet' is not a field of a message; did you mean subject?`, and every value's kind checked, as an argument's is; a class, `MailMessage`, left for later, if asked for, `send` then taking either.

Q4: Its encryption: (a) TLS required, by STARTTLS, or from the start where the configuration says so (`"tls": "implicit"`, port 465), a server that offers none refused unless the configuration says `"tls": "none"`; or (b) TLS where the server offers it, plain text otherwise?

A4: (a), TLS required, by STARTTLS on 587, or from the start where the configuration says `"tls": "implicit"`, as on 465, a server that offers none refused, its error naming the setting, unless the configuration says `"tls": "none"`; the server's certificate checked, its chain and its name, a server whose certificate cannot be checked accepted only where the configuration says so, by its certificate's thumbprint; at least TLS 1.2; and, even with `"tls": "none"`, no password sent without TLS but to a server on the same machine.

2026-11-23 (Claude): done, and landed, as the answers have it, recorded in the specification's section 7.17, "Mail", and its register (Approved), what is built beyond them Proposed there. `std:mail`, `WebLingo.StdLib.Mail`, `std/mail.dll`, in the portable core, through the platform's `IWebLingoMail`, `DotNetMail` on .NET, with an SMTP client and a MIME writer of WebLingo's own (`WebLingoSmtpClient`, `WebLingoMimeWriter`, `WebLingo.Platform.DotNet`), on .NET's sockets and `SslStream`, with no package: `await mail.send(message, server)`, a task of the message's `Message-ID`; STARTTLS, a server offering none refused unless its settings say `"tls": "none"`, or TLS from the start, `"implicit"`; TLS 1.2 or 1.3, the certificate checked, one that cannot be accepted by its SHA-256 or SHA-1 `"thumbprint"`, which the error names; XOAUTH2 with a script's token, else PLAIN, else LOGIN; no password without TLS but to a loopback address; text and HTML as alternatives, attachments, headers that are not ASCII as encoded words; every key and value of a message, an attachment and a server checked, a field it does not have naming the one it may have meant. `"mail"` in `weblingo.config` and `wasp.config`, named servers, `"default"` or the one there is; a server of the script's own, under `wasp` one `"network"` allows. Secrets, a mail server's password and a database's connection string alike, `{ "env": "NAME" }` or `{ "file": "path" }`, read each time used (`WebLingoSecret`, `IWebLingoSecrets`, `DotNetSecrets`), a site's file inside the site refused, none shown in an error. `MailError`, WL3023, with `reply`, the server's reply code.

My guesses, Proposed, to change if you say so: `send`'s second argument, a configured server's name, `{ token }` or `{ server, token }`, or `{ host, port, user, password, token, tls, thumbprint }`; `send` giving the `Message-ID`, 128 random bits and the sender's domain; a field given `null` as if not given; nothing sent where a recipient is refused; `EHLO` naming this end's address, `[192.0.2.1]`; 60 seconds for a reply, ten minutes after the message's end; an attachment's type by its name's extension; a name the configuration does not give, and a user without a password or a token, WL3001; `MailError`'s name, its code and its `reply`.

Checks: the Release build of the whole solution, no warnings; the full suite, 3,447 tests, passing, `WaspServeTests.TheConfigurationNamesTheMode` given the settings' list with `"mail"` in it;`MailTests` (43, against `SmtpTestServer`, an SMTP server of the tests' own on 127.0.0.1, with a self-signed certificate made for each test, never a real server), `WaspTests.APageSendsMailThroughItsConfiguredServerOrAHostNetworkAllows`, the architecture tests (`std:mail` in the portable core, `System.Net.Security` added to the inventory and to `PortabilityTests`); `weblingo` on the samples and the Standard Library Reference's example, and `wasp` on SimpleWebsite and the WASP User Manual's page, against a plain SMTP server of my own on 127.0.0.1. Tried on Windows only; Linux not tried (CI runs on a push). No package added.

## [DONE] Uploaded Files in WASP

A prerequisite of "Extensive WASP Example" (its A3): `request.form()` reading a form posted as `multipart/form-data`, with the files uploaded in it, which the WASP User Manual's section 19 lists as missing, for the forum's avatars and attachments. Designed with the owner first, its questions here. Added by Claude, 2026-11-23.

2026-11-23 (Claude): what is there (the WASP User Manual, section 9): `await request.form()` reads a form posted as `application/x-www-form-urlencoded`, a name given twice its last value, anything else an error; `await request.read()` gives a body's pieces as bytes; and WASP sets no limit of its own on a body, IIS's and Kestrel's, 30,000,000 bytes, being what there is. My guesses, to change if you say so: a form posted as `multipart/form-data` read by the same `request.form()`, its fields strings as now, in UTF-8; each file an instance of a class of the host's, `UploadedFile`, with `fileName`, as the browser gave it and never used as a path, `type`, its `Content-Type`, `size`, in bytes, and `await file.bytes()` and `await file.text()`; a malformed body an error where `form()` is awaited; and the manual's section 19 updated.

Q1: Where a form's files are: (a) in `form()`'s map, beside its fields, `form["avatar"]` an `UploadedFile`, as JavaScript's `FormData` has them; or (b) apart, `await request.files()`, a map of their own, as PHP's `$_FILES` and ASP.NET's `Request.Form.Files` have them?

A1: (b)

Q2: A file input that takes several files, `<input type="file" name="photos" multiple>`: (a) its name's value an array of `UploadedFile`s whenever the form gives it more than once, a single one otherwise; or (b) an array always, for every file's name, even of one, so that a page reads `form["photos"][0]` alike?

A2: (b)

Q3: Where an upload is kept until the page reads it: (a) in memory, within the execution's memory budget; or (b) in a temporary file of the server's past a few KB, as PHP and ASP.NET keep one, removed when the request ends?

A3: (b), let's decide on the threshold. The total maximum size of the upload should be configurable for the site.

Q4: The largest body a site takes: (a) WASP's own limit, 30,000,000 bytes unless `wasp.config` gives another, `"maxBodySize": 50000000`, a larger body refused with 413 before it is read; or (b) none of WASP's own, the web server's being all there is?

A4: (a)

Q5: Saving an upload: (a) `await file.save(path)`, its path checked against `wasp.config`'s `"files"` as `std:fs` checks one; or (b) nothing of its own, a page writing `await file.bytes()` with `std:fs`?

A5: (a)

2026-11-23 (Claude): A3 leaves the threshold to decide together, and a total maximum to configure. My guesses besides, to change if you say so: `request.files()` a map of each file input's name to an array of `UploadedFile`s (A1, A2), each with `name`, its input's, `fileName`, `type`, `size`, `bytes()`, `text()` and `save(path)` (A5), `save` replacing a file that is there, as PHP's `move_uploaded_file` does; `form()` and `files()` reading one body, parsed once, whichever is awaited first, and `read()` after either, or either after `read()`, an error; the temporary files in the system's temporary folder, unless `wasp.config` names another, `"uploads": { "folder": "../tmp" }`, and removed when the request ends; a body whose `Content-Length` passes `maxBodySize` refused with 413 before the page runs, and one sent in chunks an error where it passes it, where `form()`, `files()` or `read()` is awaited.

Q6: The size past which an upload goes to a temporary file (A3): (a) 64 KB, as ASP.NET Core keeps a form's file in memory up to (`FormOptions.MemoryBufferThreshold`); (b) 1 MB; or (c) none, every upload in a temporary file, as PHP keeps them? Each changed by `wasp.config`, `"uploads": { "inMemory": 65536 }`.

A6: (a)

Q7: The total maximum size of an upload (A3): (a) `maxBodySize` itself (A4), a body's size bounding its files; or (b) a limit of its own on the files' bytes, `"uploads": { "maxSize": 20000000 }`, beside `maxBodySize`?

A7: (b) with a default of (a)

2026-11-23 (Claude): done, and landed (`8da8353`), as the answers have it, recorded in the specification's section 6.12, "Uploaded files", and its register, what is built beyond them Proposed there. A form posted as `multipart/form-data` read as it arrives, once, by the hosting core's own reader (`WebLingoMultipartReader`): `await request.form()` its fields, `await request.files()` its files, each input's name an array of `UploadedFile`s, even of one (A1, A2), with `name`, `fileName`, `type`, `size`, and `bytes()`, `text()` and `save(path)`, `save` within `wasp.config`'s `"files"` (A5); a file kept in memory up to 64 KB, `"uploads": { "inMemory": 65536 }`, the rest in a temporary file, removed as its request ends (A3, A6); `"maxBodySize"`, 30,000,000 bytes unless it says otherwise, a larger body by its `Content-Length` a 413 before the page runs (A4); and `"uploads": { "maxSize": … }`, the files' most in all, the body's largest by default (A7).

My guesses, Proposed, to change if you say so: a file input left empty no file; `save` replacing a file there; `text()` and `read()` after `form()` or `files()` an error, and either after `read()`; a body sent without a `Content-Length`, or longer than it said, an error where it is read once it passes the limit; `files()` of an urlencoded form an empty map; the temporary files in the system's temporary folder unless `"uploads": { "folder": … }` names one, each deleted by the operating system as it is closed; the ASP.NET Core adapter raising Kestrel's own 30,000,000 bytes to the site's; under IIS, its own request filtering, which a site raises in its `web.config` (the WASP User Manual, section 17).

Checks: the Release build of the whole solution, no warnings; the full suite, 3,459 tests, passing; `UploadTests` (11, `WebLingo.Hosting.Tests`), and `WaspServeTests.AFormsFilesAreUploaded`, through Kestrel, a 30.5 MB upload past Kestrel's own limit within the site's among them, and a 413; `wasp` on the samples. Tried on Windows only; Linux not tried (CI runs on a push). No package added.

## [DONE] Debugging WASP Pages, Part 1: A Page Under the Debugger

The specification, section 5.7, "Debugging WASP pages, parts", part 1: `wasp --debug-adapter`; one page launched as `wasp run` runs it; the template compiler compiling for a debugger, each point located in its own file; breakpoints in `.wasp` and `.wlg` files; VS Code's debugger `wasp`. Added by Claude, 2026-11-24, a part of "Debugging WASP Pages".

2026-11-24 (Claude): done, landed on `main` as `8a6df12`. `wasp --debug-adapter` runs `weblingo`'s debug session (`WebLingoDebugSession`), given the command line's program: a launch's `program` is a `.wasp` page, `root` its site's root, the page's folder by default, `cwd`, `args` and `stopOnEntry` as a script's; the page compiled and run as `wasp run` runs it, for a debugger, each point located in its own file through the compilation's `Locate`, so that a breakpoint in the page, an include, a layout, a component or a module it imports stops there, at its line, a component's verified once it is first rendered; a line of a page's text is a point too. VS Code's extension, 0.3.0, has a debugger `wasp`, its executable `wasp` or the setting `weblingo.waspDebuggerPath`. As built, in the specification's section 5.7, "A page under the debugger, as built", Proposed where it says more; the WASP User Manual's sections 15 and 19 and `Editors/README.md`, "Debug a WASP page", describe it. My guesses, to change if you say so: the setting's name, `weblingo.waspDebuggerPath`; and a page debugged with F5 and no `launch.json` given as its root the nearest folder above it, within its workspace folder, holding a `wasp.config` or a `web.config`, else the page's folder. A page's code under a layout shows in the call stack as `function`, the anonymous function it is. Checks: the Release build, no warnings; the full suite, 3,460 tests, passing, `WaspDebugTests` stopping in a page, its layout, an include, a component and a module; `weblingo` and `wasp` run on the samples. Tried on Windows only; Linux not tried. The extension was tried against a stub of VS Code's API, not in VS Code itself, which needs it installed again.

## [DONE] Debugging WASP Pages, Part 2: A Site Under the Debugger

The specification, section 5.7, "Debugging WASP pages, parts", part 2: the site served as `wasp serve` serves it, each request a thread, `GET /cart #12`, a stop suspending only its request, as at an `await`; the debugger's pause pausing every request; a stopped request kept from WASP's timeouts. Added by Claude, 2026-11-24, a part of "Debugging WASP Pages".

2026-11-24 (Claude): done, landed on `main` as `db16e90`. A launch without `program` serves the site at `root`, as `wasp serve` does, on Kestrel, on `port` of this machine, 8080 by default, 0 for one the system chooses, the address said first on the Debug Console; each request for a page or a handler a thread, `GET /cart #12`; a request at a breakpoint stops alone, the VM returning waiting with its frames kept, as at an `await`, its worker serving the others, and going on where it stopped once the debugger resumes it through the request's scheduler host; stepping and continuing act on the thread named; the debugger's pause stops every request at its next statement, those after it too, until a `continue`; the send timeout leaves out the time a request is stopped. As built, in the specification's section 5.7, "A site under the debugger, as built", Proposed where it says more; the WASP User Manual's sections 15 and 19, and `Editors/README.md`, "Debug a WASP site". VS Code's extension, 0.4.0, has the configuration "Debug WASP Site" and `port`. My guesses, to change if you say so: a site's launch told from a page's by having no `program`; `port`'s default `wasp serve`'s, 8080; the pause, which the protocol always gives a thread, pausing every request whatever thread it names; and `continue` letting every request a pause stopped go on, those a breakpoint stopped waiting still. Checks: the Release build, no warnings; the full suite, 3,465 tests, passing, among them `DebuggerStopTests` (the VM), `DebuggedRequestTests` (the hosting core: a request stopped while its worker serves another on the same worker; the send timeout) and `WaspDebugTests.ASiteStopsOnlyTheRequestAtABreakpoint` (end to end, over HTTP); the benchmark against `main`, 12 rounds alternating, no workload slower by the rank test, after the resumption was moved out of the dispatch loop, where it made `closures` 9 percent slower. Tried on Windows only; Linux not tried. The extension was not tried in VS Code itself.

## [DONE] A Young Generation for WebLingo's Collector

"Short-Lived Objects Freed Before .NET Promotes Them" (done the same day) collects soon while little is live; where much is live, the threshold twice what is, what a program makes and drops still stays in the heap's table long enough for .NET's collector to promote it: 100,000 instances kept, then two million made and dropped, about 550 ns each, 24 of .NET's oldest collections. A young generation, the objects made since the last collection collected on their own, often, would free them in time, but it needs a write barrier: every store of a reference into an older object (a field, an array's element, a map's entry, a cell) noting that object, so that a young collection finds what the old ones refer to. WebLingo's collector is Approved as non-moving mark-and-sweep (the specification, section 4.2), which a young generation keeps, non-moving, but changes. Added by Claude, 2026-11-23.

Q1: (a) a young generation, designed with you first, its write barrier measured on the benchmark; or (b) none for now, the collector as it is, "Short-Lived Objects" having taken most of the gain?

A1: (a)

2026-11-23 (Claude): designed, its design in the specification's section 4.2, "A young generation", Proposed, and in its register, landed on `main` as a document alone (nothing built). In short: an object young until it survives a young collection, its handle kept; the young collected on their own at every 32K units made, marked from the roots and the old objects remembered; a full collection as now; every store into an old object of a value that refers to one remembering it (the write barrier), and so every store into an object through the heap, `GetArray` read-only, a change to the API native modules use. Measured first: two million `new P(i, i)` made and dropped, 136 ns each with nothing else live, 266 with 100,000 instances kept, 119 ms of .NET's pauses a run; a mark array reused in place of one made for each collection, tried, changed nothing, so that the cost is what .NET promotes.

Q2: The design, in the specification's section 4.2, "A young generation": (a) built as written; or (b) changed as you write here?

A2: (a)

Q3: Where the barrier makes a workload of the benchmark slower while the young generation makes the case above faster: (a) landed only where no workload is slower beyond the machine's noise; (b) landed where none is more than 5 percent slower; or (c) the numbers put to you first, nothing landed until you decide?

A3: (a)

2026-11-23 (Claude): built as written (A2), on the branch `claude/young-generation`, committed there, not landed (A3 (a)): where much is live, the case above about 1.7 times as fast, two million `new P(i, i)` made and dropped with 100,000 instances kept about 315 ns each before, 185 after, with 400,000 about 390 and 240, .NET's collections of its generation 1 from 48 a run to 5, of its generation 2 from 7 to 2, its pauses from about 140 ms to 25. But the benchmark's workloads against `main`'s, 24 rounds alternating, each round's order the reverse of the last's, a rank test (Mann-Whitney's) of the rounds: `closures` 3.3 percent slower (p < 0.001), `objects` 2.9 (p = 0.003), `strings` 2.2 (p = 0.007) and `imports` 1.1 (p = 0.03); `loops` 1.1 faster (p = 0.01); the rest the same. Each is within the 5 to 10 percent a workload varies from one session to the next on this machine, but beyond the noise of the rounds compared, and so, as I read A3 (a), beyond the machine's noise. The first build was 4 to 10 percent slower on four workloads; what brought them down: no young handle listed where no young collection can come, so little being live; no young collection where the last freed less than half of what was made, the young living on, as `objects` keeps what it makes; a value's kind tested before the rest of the barrier is called; the runtime reading arrays as lists again, not through `GetArray`'s interface; the barrier kept out of the VM's dispatch method (the scheduler's `Await` calls it, not the VM's `Suspend`); and `TryAddToArray` and `TrySetField` inlined, as their callers had them before the barrier grew them. What is left is a test or two on every store and allocation, and how .NET lays out the code around them. Promotion after two young collections, tried: no faster with 100,000 kept, about 10 percent slower with 400,000; so one, the faster, as the design says. Its checks, on the branch: the Release build; the full suite, 3,450 tests, passing; and every conformance and template test passing with a young collection before nearly every allocation (`YoungBudget` 16, a trial never committed). Tried on Windows only; Linux not tried.

**Q4: The young generation, as it stands on its branch: (a) landed as it is, 1 to 3 percent on those four workloads for 1.7 times where much is live; (b) kept on its branch and tuned further, landed only once no workload is slower by the rank test; or (c) dropped, its branch deleted, the specification's design kept as it is?**

A4: Since it does not  seem to improve the performance, (c).

2026-11-24 (Claude): done, as A4 (c) says: dropped, its local branch `claude/young-generation` deleted (`bac741a` its last commit), the specification's design kept, Proposed, with what was measured (section 4.2, "Built, measured and dropped"; its register's row), landed on `main` as `4634328`, documents only, no test affected. The branch is still on GitHub, `origin/claude/young-generation`: deleting it there is a push, which waits for you to say so.

## [DONE] Sessions in WASP, Part 1: Cookies

The specification, section 6.12, "Sessions and cookies, parts", part 1: `request.cookies`, `response.setCookie(name, value, options)` and `response.deleteCookie(name, options)`, secure by default, their options, a cookie set once the response is being sent an error. Added by Claude, 2026-11-24, a part of "Sessions in WASP".

2026-11-24 (Claude): done, landed on `main` as `e3309e6`. `request.cookies`, a map of the request's cookies, decoded, a name sent twice its first value; `response.setCookie(name, value, options)` and `response.deleteCookie(name, options)`, secure by default, `HttpOnly`, `SameSite=Lax`, `Path=/`, and `Secure` over HTTPS, each changed by its option, `httpOnly`, `secure`, `sameSite`, `path`, `domain`, `maxAge` and `expires`. As built, in the specification's section 6.12, "Cookies, as built", Proposed where it says more than A4; the WASP User Manual's section 9, "Cookies", and the Standard Library Reference's section 22. My guesses, to change if you say so: a value encoded as `%xx` where a cookie cannot hold it, and `%` itself, and decoded so; a name sent twice read as its first value; `sameSite: "none"` an error for a cookie that is not secure, as browsers refuse one; a cookie of more than 4,096 bytes an error; and a cookie set again, of the same name, path and domain, replacing the one set before in the same response. Checks: the Release build, no warnings; the full suite, 3,481 tests, passing, `CookieTests` and `AspNetCoreTests.CookiesAreReadAndEachIsSetByAHeaderOfItsOwn` among them; the manual's example run under `wasp serve`. Tried on Windows only; Linux not tried.

## [DONE] Debugging WASP Pages, Part 3: Conditional Breakpoints

The specification, section 5.7, "Debugging WASP pages, parts", part 3: the protocol's `condition`, an expression of WebLingo's evaluated in the stopped frame's scope, for scripts, pages and sites. Added by Claude, 2026-11-24, a part of "Debugging WASP Pages".

2026-11-24 (Claude): what is there: at a stop, the VM reads a frame's variables without running the program's code (`Inspect`), and a site's request stops without holding its worker (part 2). A condition is evaluated at every visit of its line, before the debugger is told of a stop. My guesses, to change if you say so: `hitCondition` and `logMessage`, the protocol's other kinds, left out; and a condition parsed when it is set, a syntax error making the breakpoint unverified, its message the compiler's.

**Q1: What a condition may hold: (a) any WebLingo expression, its calls running the program's code, a method or a property's getter written in WebLingo among them, the VM making the call from where it stopped, its effect on the benchmark measured before it lands; or (b) an expression without calls: names, literals, members, indexing and operators, read without running the program's code, so that a condition changes nothing, but a property whose getter is written in WebLingo is an error there?**

A1: (b), an expression without calls of the program's code: names, literals, members, indexing and operators, read without running the program's code, so that a condition changes nothing; a native member, a property's or a method's that the runtime or a native module gives, `request.path`, `items.length`, `name.startsWith("A")`, allowed, since no WebLingo code runs for it; a property or a method written in WebLingo an error there, its message saying that a condition does not run the program's code; and calls of the program's code, in conditions, watches and the debugger's console alike, a task of its own, later, if asked for. a native module's methods can have side effects too, such as fs.remove or db.execute. If built-in methods are allowed, it should be the built-in classes' methods that only read (strings, arrays, maps), not every native function.

**Q2: A condition that fails, by an error or by giving anything but a Boolean: (a) the breakpoint stops, its message saying why; or (b) it does not stop, why written once to the Debug Console?**

A2: (a), the breakpoint stops, its message saying why, the condition's error or the kind of value it gave; and a condition checked where it is set, against the scope of its line, a syntax error, a name not in scope there, or what A1 does not allow, leaving the breakpoint unverified, its message saying why, as the Debug Adapter Protocol shows one, before the program runs.

2026-11-24 (Claude): done, landed on `main` as `548711c`, as A1 (b) and A2 (a) say, recorded in the specification's section 5.7, "Conditional breakpoints", as approved. A condition is read where it is set into the VM's own form: names, literals, `.` and `?.`, `[]` and `?[]`, operators, `&&`, `||`, `??`, `? :`, and the methods of strings, arrays, maps and sets that only read; anything else, a syntax error, or a name not in scope on its line once the file is compiled, leaves the breakpoint unverified, saying why. At each visit of its line the VM evaluates it in the frame's scope without running the program's code, a native property, `request.path`, called, a property, an indexer or an operator written in WebLingo refused; one that fails stops, saying why, in the `stopped` event's text and the Debug Console. As built, in the same section, "Conditional breakpoints, as built", Proposed where it says more; the WASP User Manual's section 15 and `Editors/README.md`, "A breakpoint's condition". My guesses, to change if you say so: the read-only methods are `indexOf`, `lastIndexOf`, `contains`, `includes`, `startsWith`, `endsWith`, `slice`, `split`, `trim` and its kin, the case changes, `replace`, `repeat`, the paddings, `join`, `compare`, `removePrefix` and `removeSuffix` for strings, `indexOf`, `contains`, `includes`, `slice`, `concat`, `join`, `reverse` and `sort` for arrays, and `has` for maps and sets, none given a function; a collection of a class of the program's, `is`, `this` and collections' literals refused; and `hitCondition` and `logMessage` still unsupported. Checks: the Release build, no warnings; the full suite, 3,483 tests, passing, `DebugAdapterTests.ABreakpointsConditionStopsOnlyWhereItHolds` and `WaspDebugTests.AConditionStopsOnlyTheRequestsItNames` among them; the VM's dispatch method unchanged, so no benchmark run. Tried on Windows only; Linux not tried.

## [DONE] Sessions in WASP, Part 2: Sessions in Memory

The specification, section 6.12, "Sessions and cookies, parts", part 2: `request.session`, read and locked before the page runs; its cookie; its lock, waited for as at an `await`, `SessionLockError`; `session readonly;` and a handler's `export const session = "readonly";`; `release()`, `renew()` and `end()`; a session's ending; `wasp.config`'s `"sessions"`, its store `"memory"`. Added by Claude, 2026-11-24, a part of "Sessions in WASP".

2026-11-24 (Claude): a request runs in a heap of its own, so that a session's values are kept between requests as data, as JSON holds them, and made values of the request's heap when it reads them. My guesses, to change if you say so: `request.session` read and set as a map is, `request.session["user"]`, `has(name)` and `remove(name)`, a missing name WL3007, `?[]` giving `null`; and a value JSON cannot hold an error where it is set.

Q1: An array or a map read from the session: (a) the session's own, a change to it, `request.session["cart"].push(item)`, kept when the request ends, as PHP keeps `$_SESSION`, a value JSON cannot hold, put inside it, an error found then, at the request's end, and so in the host's log where the response has been sent; or (b) a copy, a change to it kept only once it is set again, `request.session["cart"] = cart`, as classic ASP's `Session` gives an array, every value checked where it is set?

A1: (b)

2026-11-24 (Claude): done (`aa8e535`), as the specification's section 6.12, "Sessions in memory, as built", describes it, A1 (b) recorded there as approved; the WASP User Manual's section 9, "Sessions", describes it for a site's author. My guesses, Proposed there, to change if you say so:

- `request.session` has `has(name)`, `remove(name)`, `release()`, `renew()` and `end()`, and no list of its names or `foreach` yet.
- A value keeps its kind: `5.0` comes back a number. A decimal, a date, a set, an instance of a class, an array or a map of a class, NaN, a map whose key is not a string, and a value that holds itself are errors where they are set.
- A lock waited for too long, 30 seconds, does not stop the page: it runs, and reading `request.session` there is a `SessionLockError`, WL3024, so that a page may catch it, rather than a 500 before the page runs.
- A request that fails still writes its session's changes, as PHP does.
- `end()` once the response is being sent removes the session, but cannot delete its cookie, and says nothing: the cookie then names no session.
- Sessions in memory are kept as the site reloads for a change to its files, the store being the application's, and lost as its process stops; Q1 (b) of "Sessions in WASP" described them as lost when the site reloads.
- `session readonly;` may stand anywhere at the page's top level, not only first; one misplaced is WL5009, a new code, as is a handler's `session` export that is not `export const session = "readonly";`.
- `wasp.config`'s `"sessions": { "store": "memory", "timeout": 20, "lockTimeout": 30 }`, minutes and seconds; `"store": "database"` refused until part 3.

Checks: the Release build of the whole solution, no warnings; the full suite, 3,515 tests passing, on Windows; the session pages, the manual's examples among them, served by `wasp serve` and tried with curl, two requests of one session one at a time. Linux not tried.

## [DONE] Sessions in WASP, Part 3: Sessions in a Database

The specification, section 6.12, "Sessions and cookies, parts", part 3: the store `"database"`, through the platform's databases; the SQL for each database in the WASP User Manual; `wasp sessions create --database name`, with `--sql`; the table's schema versioned; `"create": true`. Added by Claude, 2026-11-24, a part of "Sessions in WASP".

2026-11-24 (Claude): taken up, part 2 being done (`aa8e535`).

2026-11-24 (Claude): done (`a69a546`), as the specification's section 6.12, "Sessions in a database, as built", describes it; the WASP User Manual's section 9, "Sessions in a database", gives each database's SQL. My guesses, Proposed there, to change if you say so:

- The table: `id`, `data`, the session's values as one JSON object, `used` and `locked_until`, milliseconds since 1970 by the server's clock, not the database's, so that each database's statements are the same; an index of `used`; and a row `schema`, `{"schema": 1}`, which says the table's schema, no second table.
- A lock lasts 60 seconds unless its process renews it, every 20, so that a lock a stopped process held ends within a minute; a request waiting for one reads it again every 100 ms.
- A missing table is reported where a page first uses a session, a `DatabaseError`, WL3015, not as the site starts.
- Opening a session and writing it back run off the worker; making, renewing and ending one, which a page does without `await`, block the worker for one statement.
- `wasp sessions create` takes `--root`, `--database`, `--table` and `--sql`, its database and table those `"sessions"` names by default; a table there already is said to be, exit 0, and left as it is; there is no `wasp sessions upgrade` until a schema 2 needs it.
- A table's name: letters, digits and `_`, 58 at most, so that its index's name fits MySQL's 64.

Checks: the Release build of the whole solution, no warnings; the full suite, 3,525 tests passing, on Windows, SQL Server's tests among them, on the machine's own; SQLite sessions served by two `wasp serve` processes at once, one waiting for the other's lock, tried with curl. MySQL's and PostgreSQL's SQL not tried, no server of theirs being here; Linux not tried.

## [DONE] Sessions in WASP

A prerequisite of "Extensive WASP Example" (its A4): sessions built into WASP, for signing in, where a page now reads the `Cookie` header and writes `Set-Cookie` itself (the WASP User Manual, section 19). Designed with the owner first, its questions here. Added by Claude, 2026-11-23.

2026-11-23 (Claude): what is there (the WASP User Manual, sections 9 and 19): a page reads the `Cookie` header and writes `Set-Cookie` with `response.headers.add`. Each request runs in a heap of its own, so that what a session holds must be kept between requests as data, and Apache's `mod_fcgid` starts several `wasp` processes for one site, which share no memory. My guesses, to change if you say so: a session's id 128 random bits from the platform's cryptography, in a cookie `WASPSESSIONID`, `HttpOnly`, `SameSite=Lax`, `Secure` under HTTPS, until the browser closes; a session ending 20 minutes after its last request, as classic ASP's, unless `wasp.config` gives another; what it holds what JSON holds, null, Booleans, numbers, strings, arrays and maps, anything else an error where it is set; a session made, and its cookie sent, only once a page sets a value in it; and its id made anew by `session.renew()`, for a page to call as a user signs in.

Q1: Where sessions are kept: (a) in files, in a folder `wasp.config` names outside the site, `"sessions": { "folder": "../sessions" }`, as PHP keeps them, so that every process serving the site sees them; (b) in the process's memory, lost when the site reloads or its process ends, one process only; (c) in a database `wasp.config` names, through `std:db`; or (d) in the cookie itself, signed, nothing kept on the server, 4 KB at most?

A1: (b) and (c), configurable per site.

Q2: How a page reaches its session: (a) `var session = await request.session();`, read from where sessions are kept only where a page asks for it, its values read and set as a map's, written back when the request ends; or (b) `request.session`, without `await`, read before the page runs, for every request that has a session's cookie?

A2: (b)

Q3: Two requests of one session at once: (a) one at a time, the second waiting until the first ends, as PHP locks a session's file; or (b) both at once, the one that ends last writing the session, as ASP.NET Core does?

A3: (a), one at a time, the second waiting until the first ends. Besides: a page that only reads the session takes no lock, and a script may release the lock early, once it has done with the session, so that long requests do not hold the others back; a lock not released when its request ends is released then, and a lock waited for too long is a runtime error of its own.

Q4: Cookies besides sessions: (a) `request.cookies`, a map of the request's, and `response.setCookie(name, value, options)`, so that a page writes neither header itself; or (b) sessions only, other cookies as now?

A4: (a), `request.cookies`, a map of the request's cookies, and `response.setCookie(name, value, options)`, with `response.deleteCookie(name)`; secure by default, `HttpOnly`, `SameSite=Lax`, `Path=/`, and `Secure` over HTTPS, each changed by its option; setting a cookie once the response has begun to be sent an error, as for any header; and the session's own cookie set through the same means.

2026-11-23 (Claude): a session read before the page runs (A2), and its lock taken then (A3), the host must know before it runs a page whether the page only reads it. My guesses besides, to change if you say so: memory the store where `wasp.config` names none, `"sessions": { "store": "memory" }`, one process only, as the WASP User Manual will say for `mod_fcgid`; a session ending 20 minutes after its last request, `"timeout": 20`; a lock waited for 30 seconds at most, `"lockTimeout": 30`, then a `SessionLockError`, extending `Error`, a code of its own; `request.session` a session with no values for a request without its cookie, made, and its cookie sent with the response's headers, once a page sets a value, so that a page whose output has begun to be sent sets none, as for any header; `request.session.release()`, writing it back and releasing its lock, a change after it an error; `request.session.renew()`, a new id for the same values; `request.session.end()`, the session removed, as a user signs out; in a database, its values written as JSON, a lock a column of its row, with when it ends, which a request waiting for it reads again every 100 ms, as ASP.NET's SQL Server sessions do; and `response.setCookie`'s options `httpOnly`, `secure`, `sameSite`, `path`, `domain`, `maxAge` and `expires`, `deleteCookie(name, options)` taking `path` and `domain`.

**Q5: How a page says it only reads its session (A3): (a) a statement of its own, in a page's block, as `layout` is, `session readonly;`, and a `.wlg` handler's `export const session = "readonly";`; or (b) `wasp.config` naming the paths that only read, `"sessions": { "readOnly": ["/reports/*"] }`?**

A5: (a), a statement of its own, in a page's block, as `layout` is, `session readonly;`, and a `.wlg` handler's `export const session = "readonly";`; declared only by the page or the handler that answers the request, its includes, its layout and its components reading the session as it says; a write to a session declared read-only a runtime error, its message naming the declaration and where it is; a page that never writes its session, as far as the compiler sees, given a warning suggesting the declaration, later, if asked for.

**Q6: A database's table of sessions (A1's (c)): (a) made by WASP where it is missing, the configuration naming the database and the table, `"sessions": { "store": "database", "database": "main", "table": "wasp_sessions" }`; or (b) made by the site's administrator, with SQL the WASP User Manual gives for each database, a session on a site whose table is missing an error?**

A6: (b), made by the site's administrator, with SQL the WASP User Manual gives for each database, a session on a site whose table is missing an error; besides, a command that makes it, `wasp sessions create --database main`, run with an account allowed to make tables, not the site's, `--sql` printing its SQL instead of running it, and the error naming that command; the table's schema versioned, a later WASP reporting an older table with the command that upgrades it, never upgrading it by itself; and the configuration's `"create": true` making the table where it is missing, for development or a SQLite file of the site's own, false unless it is set.

2026-11-23 (Claude): marked [QUESTION] again: A5 and A6 are still empty.

2026-11-24 (Claude): answered; too large to land in one piece: split into three parts, defined in the specification's section 6.12, "Sessions and cookies, parts", each a task below, the answers recorded in its "Sessions and cookies" as approved, my guesses of 23 November 2026 there as Proposed. This task waits for them.

2026-11-24 (Claude): parts 1 and 2 are done (`e3309e6`, `aa8e535`); it waits for part 3.

2026-11-24 (Claude): done, its three parts landed: cookies (`e3309e6`), sessions in memory (`aa8e535`) and sessions in a database (`a69a546`), each in `DONE.md`, the specification's section 6.12 describing them.

## [DONE] Flaky Test: A Lock a Process That Ended Held

`DatabaseSessionTests.ALockAProcessThatEndedHeldEndsWithinAMinute` (`WebLingo.Hosting.Tests`) failed on GitHub's `windows-latest` runner (the CI run for `e104bf8`, 24 November 2026): the test's folder could not be removed, `sessions.db` being in use. Its first opening waits for a lock, and the store reads the table again every 100 ms for its lock timeout, 30 s, long after the test has done with it: the test ends with that reading still going, which opens the database as the test removes its folder, and which may take the lock the test's second opening expects to get. Make the test let that opening time out before it goes on, so that nothing of it runs after the test. Added by Claude, 2026-11-24, at the owner's request ("Fix the flaky sessions test as the first TO-DO item").

2026-11-24 (Claude): done (`21602c3`). The waiting opening's store has a lock timeout of 1 s on the test's clock: the test checks that it still waits after 250 ms, moves the clock past its timeout, and waits for it to time out, before the lock's life passes, so that none of its reading runs after the test, nor takes the lock the second opening expects.

Checks: the Release build, no warnings; `DatabaseSessionTests` five times, and the full suite, 3,525 tests, passing, all projects at once. Windows only; Linux not tried until CI runs on the push.

## [DONE] Flaky Tests Found Under Load

On 24 November 2026, three of four CI runs in a row failed on Windows, each on a test that depended on timing (`Documents/production-readiness.md`, section 3.7); the two tests are fixed (`e104bf8`, `21602c3`), and others may depend on timing as they did. Run the full suite many times on this machine while it is loaded, and fix each test that fails, its cause found and removed, not only its timeout raised. Done when twenty runs in a row under load pass. Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 2.

My guesses, to change if you say so: the load, two runs of the suite at once beside a program that keeps every processor busy; the runs in the background, their failures' logs kept in the scratchpad.

2026-11-24 (Claude): done (`5bb5629`). The load, as guessed: two runs of the full suite at once, each in a copy of the repository of its own, beside eight processes spinning at normal priority, on this machine's eight logical processors. Before the fixes, five of the first six runs failed and one hung; with them, twenty-two runs in a row passed, eleven rounds of two at once, each exiting 0 (two of them listing eight of the nine projects' summaries, their logs not kept). Six causes, each removed:

- `DatabaseSessionTests`: a request was left once 200 ms passed without the database's answer, so that the page holding the lock was left before its delay, which then fell at a time the test's manual clock never reached, and the test resumed it for ever, spinning a processor (a run hung 20 minutes). It now goes on to its timer, each answer waited for 10 s at most, and a timer the clock does not reach fails the test.
- `DebugAdapterSession`: the whole session had 30 s from its start, and its pipes were read and written through the thread pool: each answer and event now has 30 s from when it is asked for, its requests are written synchronously, and a thread of its own reads the adapter.
- `SmtpTestServer`: it accepted through the pool, a client waiting a minute for its greeting; it serves on a thread of its own.
- `WebLingoSmtpClient`, `std:mail`'s, the product: it waited on a pool thread for an asynchronous connect and TLS handshake, which needed another pool thread to complete, the test server giving up after 10 s; it now connects with a non-blocking socket and `Socket.Select`, each address in turn within its 60 s, and makes the handshake synchronously. `MailTests` gains a server nothing answers at.
- `ConsoleTextTests`' capture test: its launcher and its descendant were two PowerShells, which took more than its 20 s to start under the load; the descendant is now a cmd, which starts in milliseconds, the launcher writing its id for the test, a `ping` holding the pipes.
- SQL Server took 0.35 s to make a database on an idle machine and 35 s on the loaded one, more than a statement's 30 s: the tests' `CREATE DATABASE` and `DROP DATABASE`, and `Examples.wasp/example3`'s `setup.wlg`, give it five minutes, the server's own time, not what the tests try.

`Documents/implementation.md`, section 11, gains the trap "The Thread Pool Runs Out Under Load"; the specification's section 7.17 says how the client connects. Found and left as it is, for "Load and Soak Tests for WASP" to measure: the product's blocking work off the worker runs on the thread pool (`RunBlocking`), and `DotNetProcessRunner` waits there for its pipes' asynchronous reads, as the SMTP client did; no test failed on it.

Not stopped: the test host of the first run that hung, `testhost.exe` of the repository's copy `main-wt` in the scratchpad (process 18940, with its `dotnet test`, 31984, and `vstest.console`, 9428), which spins on one processor until stopped; stopping it was refused here, so it was part of the load for every later run. Stop it with `Stop-Process -Id 18940,9428,31984`.

Checks: the Release build, no warnings; the full suite, all projects at once, twenty-two runs in a row passing under the load above, of the tree that landed but for two of its documents; and `MailTests`, `DebugAdapterTests`, `WaspDebugTests`, `DatabaseSessionTests`, `ConsoleTextTests`, `SqlServerTests` and `WaspServeTests`' SQL Server tests passing alone. Windows only; Linux not tried until CI runs on a push.

## [DONE] Minimal WebLingo Installer: Linux

Create the easiest installer for Linux, so that WebLingo could be executed like any scripting language (e.g. Python) on a Linux machine.

2026-11-18 (Claude): a script runs as `weblingo run script.wl` now; a first line `#!…` is a syntax error, and `weblingo script.wl` a usage error. Its .NET waits for "Minimal WebLingo Installer: Windows"'s Q1.

Q1, a script that runs by itself, as a Python one does: (a) a first line `#!/usr/bin/env weblingo`, which the lexer skips, a `.wl` file made executable then run by its name, and `weblingo script.wl` the same as `weblingo run script.wl`; (b) the first line only, `#!/usr/bin/env -S weblingo run`, Linux's `env -S` passing `run` on; (c) neither, `weblingo run script.wl` only.

A1: (a)

2026-11-19 (Claude): `weblingo script.wl` runs the script now, as `weblingo run script.wl` (`397c55f`), at the owner's request; the first line `#!/usr/bin/env weblingo` waits for this task.

Q2, the installer: (a) a `.tar.gz` with an `install.sh`, which puts WebLingo in `/opt/weblingo` and links `/usr/local/bin/weblingo`, run as `sudo ./install.sh`; (b) a `.deb` package, `sudo apt install ./weblingo.deb`; (c) both.

A2: (c)

2026-11-24 (Claude): moved above `# END-OF-TO-DO`, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), as `Documents/production-readiness.md`'s recommendation 3. Scripts are `.wlg` files since 19 November 2026, and `weblingo script.wlg` runs one.

2026-11-24 (Claude): done (`b7d2207`). `Installers/Linux/package.sh`, run on Linux, makes `weblingo-<version>-linux-<x64 or arm64>.tar.gz`, whose `install.sh`, `sudo ./install.sh`, installs WebLingo in `/opt/weblingo` and links `/usr/local/bin/weblingo` and `/usr/local/bin/wasp` to it, and `weblingo_<version>_<amd64 or arm64>.deb`, `sudo apt install ./weblingo_….deb`, both (A2 (c)); a script's first line `#!/usr/bin/env weblingo` skipped by the lexer, each line keeping its number (A1 (a)). Recorded in the specification's section 6.9 and its register, the Language Reference's sections 1 and 2, the WASP User Manual's sections 1 and 18, `Documents/implementation.md`'s sections 3 and 8, the solution's README and `HANDOFF.md`; VS Code's grammar colours the line as a comment. CI gains a job, `linux-installer`, which makes the x64 packages and installs, runs and removes them, each way (`Installers/Linux/tests/test.sh`).

My guesses, to change if you say so: self-contained only, with .NET's runtime, no framework-dependent kind, for x64 and arm64; `wasp` in the same packages, its publish holding `weblingo`'s, without `weblingow`'s program and `windows:com`, which only Windows runs; `install.sh` replacing an installation in its folder whole, refusing a folder that holds anything else and a `weblingo` or `wasp` in `/usr/local/bin` that is not its own link, `--prefix` and `--bin` for an account's own folders, without `sudo`; the .deb's dependencies the libraries Microsoft lists for .NET on Debian and Ubuntu, ICU and OpenSSL by their releases' names, and tzdata; its maintainer "The WebLingo project", with no address, until the project has one ("A License and a Security Policy"); a script without the `.wlg` extension still a usage error, as `weblingo script` without it was (the register's "A script run by its name").

Checks: the Release build, no warnings; the full suite, 3,534 tests, passing, all projects at once, on Windows; the shell scripts' syntax, `bash -n` and `sh -n`; a self-contained publish of `wasp`'s project for win-x64 ran `weblingo --version`, `wasp --version` and the test's script, its first line `#!`. Not tried on Linux at all: this machine has no Linux, nor .NET's runtime packs for Linux, which publishing for it restores from NuGet; CI's new job tries the x64 packages on the next push, and the arm64 ones are tried nowhere.

## [DONE] Request Limits in WASP

A request's time has no limit under `wasp`: no host sets an instruction budget (`InstructionBudget` is unlimited), and a page that loops forever holds its worker, one of one per processor, and the other requests that worker serves, until its request is aborted; a request's heap has a fixed budget, 64 million units, about 128 MB of strings, which `wasp.config` cannot change (`Documents/production-readiness.md`, section 3.2). Stop a request that runs too long, and let `wasp.config` set both limits, each with a safe default. Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 1.

My guesses, to change if you say so: the limits in `wasp.config`'s `"limits"`, `{ "requestTimeout": 60, "requestMemory": 128 }`, seconds and megabytes, the memory's default the budget it has now; a request stopped so an error nothing catches, as WL3003 and WL3004 are, a 503 in production and its error shown in development. The other requests its worker serves still wait until it is stopped: giving each request its share of a worker is the specification's open question 14, not this task.

Q1: What a request's time limit counts: (a) the time since it began, its waits for a database, a client or a lock included, as IIS's request timeout counts it, 60 seconds by default; (b) the instructions it runs, as the runtime's budget counts them, so that a request that waits costs nothing, its default measured; or (c) both, each with a default?

A1: (c)

2026-11-24 (Claude): done (`2cd40b0`), A1 (c): a request under `wasp` is stopped once it passes its time, from when its host began it, its waits included, 60 s by default, its instructions, 1,000,000,000 by default, or its heap's memory, 128 MB of strings by default, each `wasp.config`'s `"limits": { "requestTimeout": 60, "requestInstructions": 1000000000, "requestMemory": 128 }`; a 503, WL3003, its error shown in development only and logged. Recorded in the specification's section 6.12, "A request's limits", and its register, the WASP User Manual's sections 12 and 14, the Language Reference's codes, `Documents/implementation.md` and `HANDOFF.md`.

The instruction default measured: the VM runs 73 to 245 million instructions a second on this machine (the specification's section 5.6, "After the remedies"), so that 1,000,000,000 is 4 to 14 s of computing, where a page of the benchmark's `templates` runs 270,000.

My guesses, as before, kept: the names in `"limits"`, seconds and megabytes; WL3003, an error nothing catches, its time's message naming the setting; a 503. Besides: no time limit under the debugger, whose stops a request's time must not count ("Debugging WASP Pages", A2); none under `wasp run`, one page from the command line; a request waiting for a session's lock stopped once that wait ends. The other requests on a computing page's worker still wait until it is stopped: the specification's open question 14.

Checks: the Release build, no warnings; the full suite, 3,543 tests, passing, all projects at once, `RequestLimitTests`, `WaspServeTests.ARequestsLimitsAreTheConfigurations` and `FastCgiTests.APageThatPassesItsTimeIsStopped` new; `wasp serve` on a site of its own whose page loops, its limit 2 s: a 503 after 2.07 s, its error WL3003. Windows only; Linux not tried until CI runs on a push. IIS not tried: its ASP.NET Core Module's own `requestTimeout`, two minutes by default, which the manual names, also applies there.

## [DONE] Load and Soak Tests for WASP, Part 1: Throughput, Latency and a Reload

The specification, section 5.6, "Under load, parts", part 1: `WebLingo.Benchmarks --load` and its load site; each page at 1 to 256 clients under `wasp serve` and through `wasp fastcgi` behind nginx; a page replaced while busy. Added by Claude, 2026-11-24, a part of "Load and Soak Tests for WASP".

2026-11-24 (Claude): done (`adf45f6`). `WebLingo.Benchmarks --load` and its site, `benchmarks/WebLingo.Benchmarks/LoadSite`: seven pages, each asked for by 1, 4, 16, 64 and 256 clients at once, for 10 s after 3 s, every answer checked, under `wasp serve` and through `wasp fastcgi` behind nginx; and `--reload`, a page replaced every quarter of a second while 16 clients ask for it. Its numbers are in the specification's section 5.6, "Under load". It found three things, each a task above: a page replaced while served answered empty 5% of the time ("A Page Replaced While WASP Reads It"); SQL Server's page stalling from 64 clients ("SQL Server Pages Under Many Clients"); and FastCGI behind nginx at about 1,000 requests a second ("FastCGI Behind nginx Under Load").

My guesses, to change if you say so: the clients the benchmark's own, on the same machine as the server, each with one connection and its cookies; a request counted where it ends after the warm-up; the SQL Server page reading `master` on `localhost`, a page the machine cannot serve left out; nginx unpacked from `WebServers`, as the tests do, Windows only.

Checks: the Release build, no warnings; `WebLingo.VM.Tests`, `LoadTests` new, and `WebLingo.Architecture.Tests` passing; the load command run on both hosts and the reload, as measured. Windows only; the test host the flaky tests' first run left spinning took one of the eight processors throughout.

## [DONE] A Page Replaced While WASP Reads It

Found by "Load and Soak Tests for WASP, Part 1" (24 November 2026): `hello.wasp` replaced every quarter of a second, written beside it and moved over it as a deployment replaces a file, while 16 clients asked for it under `wasp serve`: 1,485 of 37,992 answers were a 200 with an empty body, neither version of the page, where every one should be one version's whole (the specification's section 6.12, WL-H004, atomic generations); and the move was refused while `wasp` read the page, Windows refusing to replace a file that is open without sharing its deletion. Find why a page is served empty, likely a file not read at that moment compiled as an empty page (`WebLingoApplication.Compile`'s `templates.Read(file) ?? ""`), and fix it, a page that cannot be read failing as it should or read again; read the site's own files sharing their writing and deletion, so that a deployment's replacement never waits for a request; and a test of both. Added by Claude, 2026-11-24.

2026-11-25 (Claude): done (`36a8769`). The cause: a read of a page Windows was replacing failed with .NET's default sharing, and the page, its route found, was compiled from "" and kept by its worker, served as an empty 200 for the rest of its generation. Now `DotNetFileSystem` reads sharing a file's writing and deletion, `std:fs`'s reads too, an experiment's reads failing 788 times in 6,703 with the default sharing and never with this; and a page whose file cannot be read when compiled is a 500, WL5002, not kept, read again on its next request. Recorded in the specification's section 6.5, the Standard Library Reference's `std:fs`, and the WASP User Manual's section 18. Windows still refuses a move over a file a request is reading, for that moment, which a deployment's tools try again (`robocopy` does): the manual says so.

Checks: the Release build, no warnings; the full suite, 3,553 tests, passing, all projects at once, `ApplicationTests.APageThatCannotBeReadJustNowIsA500_ReadAgainOnItsNextRequest` failing without the fix, an empty 200, and passing with it, `FileSystemTests.AFileBeingReadMayBeWrittenAndRemovedMeanwhile` and `WaspServeTests.APageReplacedWhileItIsServedIsServedWhole` new; the load benchmark's reload, 30 s, 16 clients: no answer wrong in 111,425, where 19,732 of 371,351 were before, the soak running beside it. The served test does not catch the old fault, its timing not the benchmark's: kept as a check of whole answers across reloads. Windows only; Linux not tried until CI runs on a push.

## [DONE] SQL Server Pages Under Many Clients

Found by "Load and Soak Tests for WASP, Part 1" (the specification's section 5.6, "Under load"): from 64 clients at once, the load site's `sqlserver` page, ten rows of SQL Server's `master`, gives less than a third of what it gives 16, and at 256 some of its requests wait 13 s under `wasp serve` and 15 to 17 s through FastCGI, where 15 were a 500. Each request opens a connection from SqlClient's pool, 100 at most, by blocking a thread of .NET's pool, as all of a script's work off its worker does (the scheduler host's `RunBlocking`). Find why, and fix it, so that a site on SQL Server serves 256 clients at once without its requests waiting for seconds or failing; measured again with `WebLingo.Benchmarks --load --page sqlserver`. Added by Claude, 2026-11-24.

2026-11-25 (Claude): done (`e416e33`). The cause: every request's database work ran on .NET's thread pool, blocking its threads (the scheduler hosts' `RunBlocking`), and the pool, growing by a thread or two a second, left ASP.NET Core's requests and SqlClient's own completions waiting; raising the pool's minimum for `wasp` alone took the slowest 1% at 256 clients from 7.1 s to 0.4 s. Now that work runs on threads of WebLingo's own, `DotNetBlockingPool`, made as work comes and none waits, up to 256, as the specification's section 6.10 recommends bounded blocking-operation pools: `weblingo`'s executions, `wasp`'s requests, and the database session store. Measured beside the soak, which shared the machine: at 64 clients 989 requests a second where 567 were, at 256 924, 99% under 404 ms, where 453 were, 99% under 7,147 ms. Recorded in the specification's sections 6.10 and 5.6 and `Documents/implementation.md`'s trap "The Thread Pool Runs Out Under Load".

My guesses, to change if you say so: 256 threads at most, a constant; an idle thread kept, never ended; SqlClient's own pool, 100 connections, left as its connection string says.

Checks: the Release build, no warnings; the full suite, 3,554 tests, passing, all projects at once, `BlockingPoolTests` new; the load benchmark's `sqlserver`, `sqlite` and `hello` at 16, 64 and 256 clients, nothing wrong. Not measured on an idle machine, the soak running throughout. Windows only; Linux not tried until CI runs on a push.

## [DONE] FastCGI Behind nginx Under Load

Found by "Load and Soak Tests for WASP, Part 1": `wasp fastcgi` behind nginx 1.30.6 for Windows, as the WASP User Manual's section 18 configures it, serves about 1,000 requests a second whatever the page and the clients, a fifth to a thirtieth of `wasp serve`'s, and at 256 clients 1% of its requests take 3 s, and 13 s on the pages that send most. Find where the time goes, `wasp`'s FastCGI host, nginx for Windows, or a connection from nginx to `wasp` for each request, which `fastcgi_keep_conn` and an upstream's `keepalive` would keep; fix what is `wasp`'s, and say in the manual what configures nginx best; measured again with `WebLingo.Benchmarks --load --host fastcgi`. Added by Claude, 2026-11-24.

2026-11-25 (Claude): done (`7271c37`). Where the time went: a connection from nginx to `wasp` for each request, the manual's configuration; nginx keeping them, `fastcgi_keep_conn on` and an upstream's `keepalive`, served `hello` 3,729 times a second at 16 clients where it served it 556, nginx's HTTP proxy to `wasp serve` 3,069, and nginx for Windows' own limit at 64. Kept so, a request's next could come in before the last had let the connection go, refused as a second at once, `FCGI_CANT_MPX_CONN`, nginx's 502, 3 of some 17,000: fixed, the request letting it go before its end is written, none of 59,000. The manual's nginx keeps its connections, and so do `WebServerTests`', the load benchmark's and CI's `serve.sh`. Recorded in the specification's section 5.6 and the WASP User Manual's section 18.

My guesses, to change if you say so: `keepalive 16` in the manual's configuration; Apache's `mod_proxy_fcgi`, whose `enablereuse=on` would keep its connections, left as it is, untried.

Checks: the Release build, no warnings; the full suite, 3,555 tests, passing, all projects at once, `WebServerTests`' nginx with kept connections among them, `FastCgiTests.AKeptConnectionsNextRequestMayBeginAsSoonAsTheLastHasEnded` new, which did not catch the old fault without nginx's timing; the comparison through nginx, beside the soak. Windows only; Linux not tried until CI runs on a push.

## [DONE] Security Review of WASP's Request Path

Nothing has reviewed what WASP reads from a request and what it serves (`Documents/production-readiness.md`, section 3.4). Review, against the OWASP Top 10 and the parts of OWASP's ASVS that apply: a request's path to a file (traversal, `wasp.config`, a handler's source, dot-files), headers, cookies, query strings and forms, `multipart/form-data` and its uploads (names, sizes, temporary files), sessions (fixation, ids, locks, the table's SQL), escaping in pages, the permissions of files, hosts, COM objects and the environment, error pages in production, and the response's headers. Each finding with a test; each fix made, or a task of its own where it is large. Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 8.

My guesses, to change if you say so: the findings in a document of their own, `Documents/security-review.md`, dated, as this assessment is.

2026-11-25 (Claude): done (`fbb58d8`), its findings in `Documents/security-review.md`, dated, as guessed. Two findings, both fixed in `WebLingoRouteTable`, each with a test that fails without its fix: a page or a module whose extension had another case, `lib/DB.WLG`, which Windows imports as `lib/db.wlg`, was served as a file as it is, its source with it; and `web.config`, `wasp.config` and `weblingo.config` were refused only in the root, a folder's `web.config`, which IIS reads, served. What holds, each with its tests: no path reaches a file outside the routes, however its `..` is written, under both hosts; headers without line breaks; cookies `HttpOnly` and `SameSite=Lax`; an unknown session id never taken; uploads' names never paths; errors hidden in production; FastCGI on 127.0.0.1 only. Its recommendations: security headers and sensitive files served as they are, the task "Security Headers and Sensitive Files in WASP", with questions; and the tasks already in `TO-DO.md`, cross-site request forgery, logging, isolation, and a license and a security policy.

Checks: the Release build, no warnings; the full suite, 3,571 tests, passing, all projects at once, `ApplicationTests.APathWithoutARouteIs404`'s new cases and `FastCgiTests.APathThatIsNoRouteIs404` among them. Not reviewed, as the document says: the runtime beyond what a request reaches, the web servers' own configuration, Kestrel itself, and floods of connections. Windows only; Linux not tried until CI runs on a push.

## [DONE] Cross-Site Request Forgery Protection in WASP

A site has no protection against cross-site request forgery beyond the session cookie's `SameSite=Lax` (`Documents/production-readiness.md`, section 3.4). Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 9. 

Q1: How: (a) both: `Origin`, or `Sec-Fetch-Site`, checked on every POST, PUT, PATCH and DELETE, another site's refused, and a token, `request.csrfToken` for a form's hidden field, checked on those methods, both on by default, a page or a handler opting out by a declaration; (b) the `Origin` check only, on by default; or (c) tokens only, checked where a page calls the check?

A1: (b), the `Origin` check, by `Sec-Fetch-Site` where the request has it and by `Origin` where it does not, on every POST, PUT, PATCH and DELETE, a request from another site refused, one with neither header allowed, as no browser that sends a cookie omits both, on by default, as Go's `CrossOriginProtection` has it; a page or a handler opting out by a declaration, for an API other sites call; and a token besides, `request.csrfToken` and its check, `request.checkCsrf()`, for a page that wants both, called where the page calls it, never required.

2026-11-25 (Claude): done, `de3568b`. Every POST, PUT, PATCH and DELETE another site sent is a 403 before its page runs: by `Sec-Fetch-Site` (anything but `same-origin` or `none`, `same-site` included, as Go's), else by `Origin` against the request's host, without the scheme, ignoring case, `null` refused; a request with neither taken. A page opts out by `<% crossorigin allowed; %>`, a handler by `export const crossorigin = "allowed";`, misplaced WL5010 (a new code, Proposed). The token: `request.csrfToken`, its `WASPCSRF` cookie's or a new one, the cookie `HttpOnly`, `SameSite=Lax`, `Secure` over HTTPS; and `await request.checkCsrf(token)`, a task, which takes the `X-CSRF-Token` header, else the form's `csrfToken` field, where it is given none. My guesses, Proposed in the specification's section 6.12, "Cross-site request forgery": the declarations' names, the cookie's, the header's and the field's names, and `checkCsrf` taking an optional token and being a task. Checks: Release build without warnings; the full suite on Windows; the manual's example run under `wasp serve` with curl. Linux not tried.

## [DONE] Logging for WASP Sites, Part 1: The Log

The specification's section 6.12, "Logging, parts", part 1: `std:log`; the host's log, or a file a day, `wasp.config`'s `"log"`; each line; a request's failures logged at `error`, and one refused as another site's at `warn`; under `weblingo` and `wasp run`, the standard error. Added by Claude, 2026-11-25.

2026-11-25 (Claude): done, `b40a156`. `std:log`, `log.debug/info/warn/error(text)`, the text a string. Under `wasp`, the site's log: the host's own (`wasp serve`'s terminal, `wasp fastcgi`'s standard error, IIS's `stdout` log), or a file a day, `wasp.config`'s `"log": { "file": "C:/inetpub/logs/WebLingo/mysite.log", "level": "info" }`, written `mysite-2026-11-25.log`, outside the site (a file inside it refused), `info` by default. Each line `2026-11-25T08:24:43.520Z INFO GET /greet #3 Greeted Anna`, a request numbered when it first writes; text kept to one line. WASP logs a 500's and a 503's errors in every mode, at `error`, a cross-site refusal at `warn`, reloads at `info`. Under `weblingo` and `wasp run`, the standard error, every level. My guesses, Proposed in the specification's section 6.12, "Logging", as built: `"log"` an object of `"file"` and `"level"`, the line's format, levels in capitals, the text a string only, the request numbered when it first writes, `weblingo` writing every level and ignoring `weblingo.config`'s `"log"`. Changed: `wasp fastcgi`'s own messages and reloads now go to its standard error when it listens on a port too. Checks: Release build without warnings; the full suite on Windows, two load-sensitive tests (Apache's cleanup, the capture timeout) failing once under the soak's load and passing when run again; the manual's example run under `wasp serve`. Linux not tried.

## [DONE] Isolating Sites, Documented

The specification says that sites sharing a process are isolated logically, not as separate processes are (open question 13; section 12.3, item 7), and the WASP User Manual does not say so (`Documents/production-readiness.md`, section 3.4). Make its deployment sections say what a site's permissions protect and what they do not: what its native modules and allowed COM objects reach, as its process's account; one process for each site, an application pool of its own under IIS, a process of its own under `mod_fcgid` or a service; and untrusted sites each in a process of its own, under an account of its own. Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 11.

2026-11-25 (Claude): done, `ba05fa0`. The WASP User Manual's new section 19, "Sites sharing a server": what a site's `wasp.config` limits (its pages' reach through the standard modules, checked by WASP in its process) and what it does not (its native modules, its allowed COM objects, a database reached with the account's credentials, and the configuration itself, each reaching what the process's account may); then the operating system's wall, an application pool of its own for each site under IIS, a user of its own for each `wasp fastcgi`, a temporary folder of its own, WebLingo's folder read only, and untrusted sites each in a process and an account of their own, with no schemes but `std` and no `"com"`. "What is not there yet" is now section 20. The specification's open question 13 and section 12.3, item 7, point to it. Documentation only, no test reads it; the IIS and FastCGI steps it gives were not tried here.

## [DONE] WebServerTests' Cleanup Under Load

`WebServerTests.ApacheServesASiteFromWaspFastCgiThroughModProxyFcgi` failed once in its `Dispose`, under the soak's load: removing its folder retried an `IOException` only, and `httpd.exe`, held a moment after its process was killed, was refused with an `UnauthorizedAccessException`. Retry both. Added by Claude, 2026-11-25, found while running the suite for "Logging for WASP Sites, Part 1: The Log".

2026-11-25 (Claude): done, `c629232`. `WebServerTests.Dispose` retries an `UnauthorizedAccessException` as it retries an `IOException`, a killed server's own program held a moment after it has gone. Checks: the Templates tests' build without warnings, and `WebServerTests` run on Windows, all three passing; the failure itself, under the soak's load, was not made again. Linux not tried (the tests are Windows's only).

## [DONE] Load and Soak Tests for WASP, Part 2: The Soak

The specification, section 5.6, "Under load, parts", part 2: the load site's pages in turn for 8 hours under `wasp serve`, its memory, handles and threads sampled. Added by Claude, 2026-11-24, a part of "Load and Soak Tests for WASP".

2026-11-25 (Claude): runs in the background from 00:20 to about 08:20, `WebLingo.Benchmarks --load --soak 8 --clients 16`, the load site's pages in turn under `wasp serve`, from a copy of the repository at `adf45f6`, a row every 10 minutes; recorded once it ends.

2026-11-25 (Claude): done (`b873543`). The soak ran its 8 hours, 00:20 to 08:21, and the specification's section 5.6, "Under load", records it, after part 1: 43,625,022 requests, every answer checked, none wrong or failed; the working set between 108 and 116 MB, the handles between 909 and 967 and the threads between 30 and 36 from the first sample to the last, so that nothing grew with time, and part 1's growth came with its clients; its private bytes 50 to 54 MB from 7 hours and 20 minutes on, 45 to 49 before, too little in one run to call a leak. Nothing failed or leaked, so no task came of it. The machine was busy for the run's first three hours, a test host an earlier run had left spinning on one processor among it, until stopped 3 hours and 1 minute in, when throughput rose from about 1,100 requests a second to about 1,700; its last 20 minutes were faster again, 1,889 and 3,114, why not looked into. Documentation only, nothing built or tested; under `wasp serve` on Windows only, IIS and Linux being parts 3 and 4.

## [DONE] Logging for WASP Sites, Part 2: A Runtime Error's Stack

The specification's section 6.12, "Logging, parts", part 2: the VM keeps, where an error is raised, the calls it is raised in; a request's error logged with them. Added by Claude, 2026-11-25.

2026-11-25 (Claude): built and tested on branch `claude/error-stack` (`32ab262`); waits for its benchmark against `main`, two lines of the VM's `Run` having changed, once the soak ("Load and Soak Tests for WASP, Part 2") has left the machine idle, about 08:20.

2026-11-25 (Claude): done (`e55feef`, `17def9b`). The VM keeps, where a runtime error is raised or a value thrown, the calls it is raised in (`KeepStack`, through `Raised` and `Thrown`, helpers of `Run`'s .NET does not inline), an uncaught error's `WebLingoDiagnostic.Stack`, the innermost first; the hosting core logs each after a request's error, at `error`, `  at priceOf in lib/prices.wlg(2,12)`, `  at a function in …`, `  at the top level of cart.wasp(2,15)`, for a 500, a 503 and a page that fails while it is sent; the development error page and `weblingo`'s report unchanged. Two lines of `Run` changed: measured against `main` (`b873543`), every workload, 16 rounds alternately on an idle machine after the soak, no workload slower by a rank test, none with p below 0.05, recorded in the specification's section 6.12, part 2 as built. Checks: the Release build of the whole solution, no warnings; before the rebase onto `b873543`, every test project; after it, `WebLingo.Hosting.Tests` (211), `WebLingo.VM.Tests` (244) and `WaspServeTests` (15), all passing, the commits between, a test's cleanup and the specification, touching nothing it does. Windows only; Linux not tried, nothing pushed.

## [DONE] Logging for WASP Sites

A page has no way to log: `std:console` is refused under `wasp`, and only `wasp`'s own output reaches IIS's log (`Documents/production-readiness.md`, section 3.6). Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 10.

My guesses, to change if you say so: an error nothing caught, in production, logged with its request; each line its time, its level, its request's method, path and number, and its text.

**Q1: How a site logs: (a) a standard module, `std:log`, `log.info(text)`, `log.warn(text)` and `log.error(text)`, writing to a file `wasp.config`'s `"log"` names, outside the site, a file a day; (b) the same module, writing to the host's own log, IIS's `stdout` log or the journal on Linux; or (c) both, the host's log unless `"log"` names a file?**

A1: (c), both, the host's log unless `"log"` names a file: under `wasp serve` the terminal, under FastCGI the standard error, which a service manager's journal keeps, and under IIS its `stdout` log, which the WASP User Manual says is not for production, naming `"log"` there; a file a day where `"log"` names one, outside the site, written by the host, so that a site needs no permission to write it and cannot change it; levels `debug`, `info`, `warn` and `error`, the configuration's `"level"` the lowest written; each line its time in UTC, its level, its request, as the debugger names it, `GET /cart #12`, and its text; a request's runtime error logged with its stack, whatever the mode; and under `weblingo`, the standard error.

Split in two parts (the specification's section 6.12, "Logging, parts"), each a task below, since the stack the answer asks for is the VM's to keep, which it does not yet.

2026-11-25 (Claude): waits for its parts.

2026-11-25 (Claude): done, both parts landed: part 1, the log, `std:log` and a site's log, the host's or a file a day `wasp.config`'s `"log"` names (`b40a156`), and part 2, a runtime error's stack in it (`e55feef`, `17def9b`); the specification's section 6.12, "Logging" and "Logging, parts", and the WASP User Manual's "The log". Windows only; Linux not tried, nothing pushed.

## [DONE] MySQL and PostgreSQL Tried

No test reaches a MySQL or a PostgreSQL server: `std:db`'s providers for them, and the SQL of the WASP User Manual's "Sessions in a database" for them, are untried (`Documents/production-readiness.md`, section 3.3). Run `std:db`'s tests that SQL Server has (`SqlServerTests`), and sessions in a database (`wasp sessions create`, a session kept and locked), on a server of each, each skipped where no server answers, as SQL Server's are; fix what fails; and remove the manual's note that their SQL is untried. Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 5.

Q1: Their servers: (a) downloaded by me, each database's archive for Windows of its current release, some hundreds of MB each, from dev.mysql.com and from PostgreSQL's binaries at enterprisedb.com, into a git-ignored folder, `Databases`, run by the tests on ports of their own, listening on 127.0.0.1 only, nothing installed, as nginx's and Apache's are; (b) installed by you, as services; or (c) on CI's `ubuntu-latest`, as its service containers, tried only on a push?

A1: (a)

2026-11-24 (Claude): waits for your go-ahead in chat to download, into `Databases`, which git will ignore: MySQL Community Server 8.4.13.1 LTS, `mysql-8.4.13.1-winx64.zip`, "Windows (x86, 64-bit), ZIP Archive", 268.3 MB, from dev.mysql.com's page for 8.4 (MD5 `d05e97a33b1bb0f49d88f09447bc3f9c`), and PostgreSQL 19.0's binaries for Windows x86-64, from EnterpriseDB's page of PostgreSQL binaries, its size not given there, some hundreds of MB, no checksum given; the harness downloads nothing without it. Each then run by the tests, unpacked, on a port of its own, 127.0.0.1 only.

2026-11-25 (Claude): done (`2dfaf15`). Downloaded, on your go-ahead in chat, into `Databases`, which git now ignores: `mysql-8.4.13.1-winx64.zip`, 281,355,352 bytes, its MD5 the one dev.mysql.com gives, and `postgresql-19.0-2-windows-x64-binaries.zip`, 68,546,952 bytes, each with its SHA-256 in `Documents/implementation.md`, "Trying MySQL and PostgreSQL". `MySqlTests` and `PostgreSqlTests` run `std:db`'s tests that SQL Server has, and `WaspServeTests.SessionsAreKeptInMySql` and `SessionsAreKeptInPostgreSql` sessions in each, `wasp sessions create` making their table, a session kept and locked, SQL Server's test now through the same helper, its lock checked too; on servers the tests run themselves (`DatabaseServer`): unpacked into a folder of the test process's, a cluster made, started once on 127.0.0.1, in seconds, in a job object that ends it with the process. My guesses: a server for each test process rather than one shared, and its folder in the system's temporary folder, a later run removing what an earlier one left, once no process holds its lock file, the test host being ended before its own cleanup finishes. What they found, fixed in `DotNetDatabases`: a date went to MySQL as its time in UTC, MySqlConnector's way, and came back as one on the server's clocks, six hours off here, now written as it is read; and Npgsql's messages broke their lines as the platform does, CRLF on Windows, so that a script read another text there, a provider's message's line breaks now LF everywhere. The manual's note that their SQL was untried is gone; the specification's section 7.11 and the Standard Library Reference say how their values go and come. Checks: the Release build, no warnings; the full suite, 3,642 tests, none skipped. Windows only: CI skips them, having no downloads; Linux not tried, nothing pushed.

## [DONE] A License and a Security Policy

No license has been chosen, so that nobody but the owner may use WebLingo, and nobody can report a vulnerability (`Documents/production-readiness.md`, sections 3.4 and 3.8). The specification's section 12 has MIT as the preferred candidate, and the public release when WebLingo runs without .NET (12.1). Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 13.

Q1: When: (a) at the public release, as the specification's section 12.1 has it, this task deferred until then; or (b) now: MIT, a `LICENSE` file, and `SECURITY.md` saying how to report a vulnerability?

A1: (b) 

2026-11-25 (Claude): prepared on branch `claude/license`, not landed: `LICENSE`, the MIT License's text; `SECURITY.md`, how to report a vulnerability, what is supported (the latest `main`), and what a site's security rests on (the WASP User Manual's section 19); `LICENSE` copied into every package the Windows and Linux installers make; the README's "License"; `Documents/implementation.md`'s section 10 and the specification's section 12 and its register saying MIT is chosen. My guesses, to change if you say so: `LICENSE` covers the whole repository, its documentation included; no license headers in source files, and no license metadata in the assemblies, none being published as a package; and `SECURITY.md` promising no time to answer in. Three things it names are yours to decide:

Q2: The copyright holder `LICENSE` names, `Copyright (c) 2026 …`: (a) "The WebLingo Authors", as Go's names "The Go Authors"; (b) the repository's GitHub organization, optimax; or (c) a name you write in A2?

A2: (a)

Q3: How a vulnerability is reported, which `SECURITY.md` says: (a) through GitHub's private vulnerability reporting, the repository's Security tab, which you turn on in its settings (Settings, Advanced Security, Private vulnerability reporting); or (b) by mail, to an address you write in A3, which `SECURITY.md` makes public?

A3: (a)

Q4: A list of the third-party packages WebLingo ships, each with its version and its license, whose notices MIT's and Apache's terms ask every copy to carry (`Documents/implementation.md`, section 10, asks before one is made): (a) now, `THIRD_PARTY_NOTICES.md` at the root, shipped in the packages, in this task; or (b) a task of its own, later?

A4: (a)

2026-11-25 (Claude): done (`811e34d`, `6e42a55`). `LICENSE`, the MIT License, Copyright (c) 2026 The WebLingo Authors (A2 (a)); `SECURITY.md`, a vulnerability reported through GitHub's private vulnerability reporting (A3 (a)), which you turn on in the repository's settings (Settings, Advanced Security, Private vulnerability reporting) once it is on GitHub's side; and `THIRD_PARTY_NOTICES.md` (A4 (a)): the 31 packages `weblingo`, `weblingow` and `wasp` ship, their dependencies included, each with its version, license, copyright and program, the .NET and ASP.NET Core runtimes the self-contained packages carry, the MIT, Apache 2.0 and PostgreSQL licenses' texts, and Microsoft's notices for .NET's libraries and runtimes, verbatim, 4,277 lines. Both ship in every installer's package; `ThirdPartyNoticesTests` fails while the notices' packages differ from the programs'. My guesses: `LICENSE` covers the whole repository, its documentation included; no license headers or assembly metadata; `SECURITY.md` promising no time to answer in; Apache's text taken from ASP.NET Core's notices and Npgsql's from its repository's `LICENSE`, read on GitHub, neither package carrying its own. Checks: the Release build, no warnings; `WebLingo.Architecture.Tests`, 87, the new test failing on a version changed by hand; `package.ps1`, all 12 zips carrying `LICENSE` and the notices, the uninstaller's inventory listing both. Windows only: `package.sh` and Linux not tried, nothing pushed.

## [DONE] Security Headers and Sensitive Files in WASP

From "Security Review of WASP's Request Path" (`Documents/security-review.md`, section 3): WASP sets no security header of its own, and serves any file in a site's folder by its path, a SQLite database, a `.sql` script, a `.bak`, a `.log` or an `.env` among them. Added by Claude, 2026-11-25.

Q1: Security headers: (a) `X-Content-Type-Options: nosniff` on every response, a page's and a file's, a page able to remove it, and nothing else, `Referrer-Policy` and `frame-ancestors` each a site's own; (b) those three by default, `nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` and `Content-Security-Policy: frame-ancestors 'self'`, a page able to change each; or (c) none, the manual saying how a site sets them?

A1: (b), those three by default, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` and `Content-Security-Policy: frame-ancestors 'self'`, with `X-Frame-Options: SAMEORIGIN` beside the last, for browsers without it; on every response, a page's and a file's; each changed or removed by `wasp.config`, for a whole site, `"headers": { "frameAncestors": ["https://partner.example"] }`, and by a page, for itself; WASP's `frame-ancestors` left out where a page or the site sends a `Content-Security-Policy` of its own, the WASP User Manual saying so; `Strict-Transport-Security` and a full policy left to each site.

Q2: Sensitive files: (a) refused by default by their extensions, `.db`, `.sqlite`, `.sql`, `.bak`, `.log`, `.env`, `.config` and `.pfx` among them, a 404, `wasp.config`'s `"serve"` able to allow one; (b) refused only where `wasp.config` lists them; or (c) served as now, the manual's advice to keep them outside the site enough?

A2: (a)

2026-11-25 (Claude): done (`ef9803a`). Every response a site gives, a page's, a handler's, a file's and an error's, has `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Content-Security-Policy: frame-ancestors 'self'` and `X-Frame-Options: SAMEORIGIN` (A1 (b)): set on a page's response before it runs, so that it changes or removes each through `response.headers`, its own `Content-Security-Policy` replacing WASP's; again on an error page's; and on a file's by its host, ASP.NET Core's and FastCGI's. `wasp.config`'s `"headers"` changes them for a whole site, `{ "nosniff": true, "referrerPolicy": "strict-origin-when-cross-origin", "frameAncestors": ["self", "https://partner.example"] }`, `null` removing the last two. And a file whose extension is a database's, SQL's, a backup's or an editor's copy's, a log's, an environment's or a configuration's, or a key's has no route, a 404, unless `"serve": { "allow": [".pfx"] }` allows it (A2 (a)). My guesses, Proposed in the specification's section 6.12, "Security headers and sensitive files": the settings' names, `"nosniff"`, `"referrerPolicy"` and `"frameAncestors"`, and `"serve"`'s `"allow"`; `"frameAncestors"` the whole list of `frame-ancestors`' sources, as CSP has it, so that your example, `["https://partner.example"]`, lets the partner frame the site's pages and not the site itself, `["self", "https://partner.example"]` both; `"self"` and `"none"` written without CSP's quotes; `X-Frame-Options` sent for `["self"]` (`SAMEORIGIN`) and `["none"]` (`DENY`) only, since it cannot name another site; a policy the site's web server adds, IIS's `customHeaders` or nginx's `add_header`, which WASP cannot see, the manual having such a site say `"frameAncestors": null` or write `frame-ancestors` in its own; the extensions refused, `.db`, `.db3`, `.sqlite`, `.sqlite3`, `.mdb`, `.accdb`, `.mdf`, `.ldf`, `.sql`, `.bak`, `.backup`, `.old`, `.orig`, `.swp`, `.tmp`, `.log`, `.env`, `.config`, `.ini`, `.pfx`, `.p12`, `.pem` and `.key`, whatever their case, a folder such as `.git` already never served; and an extension `"serve"` allows sent as any other file, so that one the host has no content type for, `.log`, stays a 404. Checks: the Release build, no warnings; the full suite, 3,654 tests, none skipped; `wasp serve` on `Examples.wasp/example1`, its page, its stylesheet, a 404 and `web.config`'s 404 each with the four headers, read in the browser. Windows only; Linux not tried, nothing pushed.

## [DONE] Roadmap and Open Questions Brought Up to Date

The specification's section 9 and 10 say what was true before 23 November 2026: M0.3's row has the choice between the VMs open, which the owner made (WL-BC002, 23 November 2026), M0.8's says CI was started, its "Immediate next activity" names M0.14 to M0.19 and nothing after, and open questions 1, 11 and 15 ask what has been settled or built since (the register VM; the database's connections, transactions and streamed rows; CI's configuration). Bring section 9's table, its "Immediate next activity" and section 10 up to date with what is built and decided, deciding nothing anew. Added by Claude, 2026-11-25, at the owner's request ("Yes, add tasks", new tasks from the specification's roadmap).

2026-11-25 (Claude): done (`b5b0e23`). The specification's section 9: M0.3's row, the register VM chosen (WL-BC002, 23 November 2026) and WLBC's format built; M0.8's, CI implemented, its Linux installers' job among it; its "Immediate next activity", M0.20 and the work since, every milestone of the table implemented, the next the owner's choice, which "The Next Milestone" asks. Section 10: open question 1 decided, 11 settled and built, 15 built. Nothing decided anew. Documentation only: nothing built or tested, nothing pushed.

## [DONE] Debugging WASP Pages, Part 4: Another Client

The specification, section 5.7, "Debugging WASP pages, parts", part 4: the launch settings documented, and pages' debugging tried through a client without an extension of its own (A3). Added by Claude, 2026-11-24, a part of "Debugging WASP Pages".

2026-11-24 (Claude): neither client is on this machine: Visual Studio waits for its install ("Visual Studio Debug Adapter", A1), and nvim-dap needs Neovim and nvim-dap downloaded.

**Q1: The client to try it with: (a) nvim-dap, I download Neovim's Windows release, nvim-win64.zip, about 12 MB, from github.com/neovim/neovim's releases, and nvim-dap, a Lua plugin of about 1 MB, from github.com/mfussenegger/nvim-dap, both into a git-ignored folder, run there only, nothing installed; or (b) Visual Studio's `DebugAdapterHost.Launch`, once you have installed Visual Studio Community 2026 here?**

A1: (b)

2026-11-24 (Claude): A1 (b): waits for Visual Studio Community 2026 installed here, as "Visual Studio Debug Adapter" does.

2026-11-25 (Claude): done (`2e27309`). A page debugged in Visual Studio Community 2026 (18.12), through its own Debug Adapter Host's `DebugAdapterHost.Launch`, with no extension (A1 (b)): a launch file whose `$adapter` is `wasp` and `$adapterArgs` `--debug-adapter`, Visual Studio started with it from the command line (`devenv /Command`), the page opened in its editor and its breakpoints set in the margin while the page ran, each verified; the page stopped at them, its call stack and **Locals** shown, an array expanded, stepped over and continued, its output in the **Output** window; every request Visual Studio made answered, read in a log of the protocol, nothing in the adapter changed. Two differences, documented: Visual Studio answers a stop on entry with `continue` at once, so that `stopOnEntry` does not stop there, a breakpoint on the first statement doing it; and its **Autos** stays empty, with no language service for WebLingo. The launch settings documented as the protocol's own, for any client, in `Editors/README.md`, "Any debugger: the launch settings", with Visual Studio's launch file and command, which the WASP User Manual's section 15 points to; the specification's section 5.7, part 4. Tried on Windows, by hand, on a page; a breakpoint in an include, a layout or a component in Visual Studio not tried, the adapter verifying them as for VS Code. Documentation only: nothing built, nothing pushed.

## [DONE] Debugging WASP Pages

Make it possible to debug WASP pages in VS Code and VS.

2026-11-23 (Claude): what is there: VS Code debugs a `.wlg` script launched on this machine, through `weblingo --debug-adapter`, with line breakpoints, stepping, the active call stack and local variables (approved 19 November 2026; the specification, section 5.7, "Local source debugging": "Debugging WASP, the stack VM, and attaching to running processes require separate work"); Visual Studio has none yet, its adapter the task "Visual Studio Debug Adapter". A page compiles to one program with its includes', layouts' and components' code, each file's positions its own (`WebLingoTemplateFiles`), so that a breakpoint can stand in any of them. My guesses, to change if you say so: `wasp --debug-adapter`, sharing `weblingo`'s adapter (`WebLingoDebugSession`); a debugger of type `wasp` in the VS Code extension, for `.wasp` files and a site's `.wlg` handlers and modules; breakpoints in a page's blocks, `<% %>` and `<%= %>`, and in its includes, layouts, components, modules and handlers, a page's text between blocks stepped over as one statement; and IIS not attached to, for now.

Q1: What a WASP debugging session runs: (a) one page, as `wasp run page.wasp` runs it, its parameters from `launch.json`, with no request; (b) the site, as `wasp serve` serves it, a breakpoint stopping the request a browser sent; or (c) both, as two launch configurations?

A1: (c)

Q2: Under `wasp serve`, a request that reaches a breakpoint: (a) it alone stops, each request a thread of the debugger's, the others going on; or (b) every request stops, the site paused?

A2: (a), it alone stops, each request a thread of the debugger's, named by its method, its path and its number, `GET /cart #12`, the others going on, its worker running them meanwhile, the stopped request waiting as at an `await`; the debugger's pause, given no thread, pausing every request, for a snapshot; a breakpoint's condition, as the Debug Adapter Protocol gives it, so that a breakpoint in shared code stops only the requests it names; and a request stopped by the debugger kept from WASP's own timeouts, `"sendTimeout"`'s among them, while it is stopped.

Q3: Visual Studio: (a) VS Code here, Visual Studio with "Visual Studio Debug Adapter", which then debugs pages as it debugs scripts; or (b) both here, that task's extension first?

A3: (a), VS Code here, Visual Studio with "Visual Studio Debug Adapter", which then debugs pages as it debugs scripts. The adapter's launch settings for pages kept to the Debug Adapter Protocol's own, documented, with nothing only VS Code's extension gives, so that any client launches them; and pages' debugging tried once, besides VS Code, through a client of another kind, without an extension of its own, Visual Studio's `DebugAdapterHost.Launch` or nvim-dap, so that nothing VS Code alone does is relied on.


2026-11-24 (Claude): too large to land in one piece: split into four parts, defined in the specification's section 5.7, "Debugging WASP pages, parts", each a task below, the answers recorded there as approved. This task waits for them.

2026-11-24 (Claude): parts 1 to 3 are done (`8a6df12`, `db16e90`, `548711c`); it waits for part 4, which waits for Visual Studio installed here.

2026-11-25 (Claude): done, its four parts landed: a page under the debugger (`8a6df12`), a site under the debugger (`db16e90`), conditional breakpoints (`548711c`) and another client, Visual Studio's Debug Adapter Host (`2e27309`); the specification's section 5.7, "Debugging WASP pages" and its parts. Visual Studio's own extension for scripts and pages is the task "Visual Studio Debug Adapter" (A3 (a)). Windows only; Linux not tried, nothing pushed.

## [DONE] Build Number

MarkdownTools project changed the way they generate the build number: no leading zeroes in any of its parts. Make a corresponding change here.

2026-11-25 (Claude): done (`b8b2877`). As MarkdownTools's `cac58430` makes it: the build number's parts, the year, the month, the day, and the hour and minute, each a number without a leading zero, `2027.1.5.7` for 00:07 on 5 January 2027, read in `WebLingo/Directory.Build.props` from the time taken once, so that they agree; every assembly's file version, and what `weblingo --version` and `wasp --version` print, `WebLingo 0.20.0 (2026.11.25.1405)`. The version's test checks the new form; the specification's register ("Versions"), `Documents/implementation.md` and `HANDOFF.md` say it. Left as it was: WebLingo's build number is the build's local time, where MarkdownTools's has been UTC since 23 November 2026; say so if WebLingo's should be UTC too. Checks: the Release build of the whole solution, no warnings; the build number evaluated for 00:07 on 5 January 2027, 23:59, 10:00 on 9 October and with `-p:BuildStamp`; `TheVersionIsTheSolutionsAndComesFirstWithoutArguments`; both commands' `--version`. Windows only; Linux not tried, nothing pushed.

## [DONE] WebLingo Forum, Part 1: Foundations

`Documents/weblingo-forum.md`, section 6, part 1: the folders, `lib/sql.wlg` and `lib/schema.wlg`, `setup.wlg` making the database on each of the four databases with its first administrator, category and board, the settings and the texts, the layout, its includes and the CSS, the front page, a board and a topic read from the database, and the tests' frame, `weblingo test` and `ForumTests`. Added by Claude, 2026-11-25, a part of "Extensive WASP Example".

2026-11-25 (Claude): done (`cdfa20a`). `Examples.wasp/WebLingoForum`: `site/`, `setup/` and `data/` outside the site. `lib/sql.wlg` gives what differs between the four databases: each kind of column's type, a page of rows (`LIMIT … OFFSET`, SQL Server's `OFFSET … FETCH`) and a new row's id (`RETURNING`, `OUTPUT INSERTED`, MySQL's `LAST_INSERT_ID()`). `lib/schema.wlg` describes the tables once, version 1: settings, members, groups (`member_groups`, since MySQL reserves `GROUPS`), categories, boards, topics and posts, with their keys, foreign keys and indexes, written as each database's DDL. `setup/setup.wlg`, run with `weblingo`, makes them with the groups, the first administrator (asked for, the password unseen, or given as parameters) and a first category, board and pinned topic, in a transaction except on MySQL; run again, it does nothing. The settings have defaults, their colours checked; the texts are a module for each language, `texts/en.wlg`, with plural forms; members' names, addresses and passwords are checked. The front page, a board and a topic are paged (25 topics and 20 posts a page), pinned topics first, dates in the forum's time zone, and a missing one is a 404; the layout sets its colours from the settings in its `<style>`, rather than a `/theme.css` handler (the design document says so now). Tests: the modules' own, 17 cases run by `weblingo test`; `ForumTests` runs them and serves the forum, set up and given 30 more topics, on SQLite, SQL Server, MySQL and PostgreSQL, all passing here. Checks: the Release build of the whole solution, no warnings; `ForumTests`; the pages looked at in the browser pane, at desktop and phone widths. Windows only; Linux not tried, nothing pushed.

## [DONE] WebLingo Forum, Part 2: Members

`Documents/weblingo-forum.md`, section 6, part 2: signing up with an address confirmed by mail, signing in and out with sessions, a forgotten password reset by mail, limits on failed sign-ins, the profile without its avatar, and the list of members. Added by Claude, 2026-11-25, a part of "Extensive WASP Example".

2026-11-25 (Claude): done (`ae49457`). Signing up (a name, an address and a password, a name or an address taken refused), the member made unconfirmed and mailed a link through `std:mail`, or signed in where the settings' `"confirmEmail"` is false; links mailed to confirm an address (48 hours) or reset a password (an hour), 256 random bits kept as their SHA-256, used once, their address the settings' `"address"` where given; signing in by name or address, back to where the member was, never to another site; five failures in 15 minutes making the next wait, counted for each account only, since WASP gives a page no client's address; an unconfirmed member mailed a new link; signing out, posted with the form's CSRF token; a forgotten password reset, the answer the same for any address; the profile's time zone, language and signature, and its password; the members' list and a member's page. The schema's version 2 (tokens, failed sign-ins), which `setup.wlg` upgrades a version 1 database to. My guesses, to change if you say so: signing up says when a name or an address is taken, as forums do; a confirmation link is a GET that confirms; settings `"signUp"`, `"confirmEmail"`, `"mailFrom"` and `"address"`, edited in part 5. Also fixed: `ForumTests` read its pages' line breaks as they came, and the checkout's CRLF templates broke it on this machine after part 1 landed; it reads them as LF now. Checks: the Release build of the whole solution, no warnings; `ForumTests` (5) on SQLite, SQL Server, MySQL and PostgreSQL, again on `main`'s checkout; the forum's own tests, 22 cases; the sign-in form looked at in the browser pane. Windows only; Linux not tried, nothing pushed.

## [DONE] WebLingo Forum, Part 3: Writing

`Documents/weblingo-forum.md`, section 6, part 3: starting topics, replying, quoting, editing and deleting one's own posts, the Markdown renderer and previews, page numbers, unread posts, and watching a topic, with its mail. Added by Claude, 2026-11-25, a part of "Extensive WASP Example".

2026-11-25 (Claude): stopped at your request, half done, and waits for you to say to continue: the Markdown renderer, `site/lib/markdown.wlg`, with its tests, and the schema's version 3, committed, unfinished, on the branch `claude/forum-writing`, pushed at your request; the posting pages not written. `HANDOFF.md`, "WebLingo Forum", says what is left.

2026-11-25 (Claude): taken up again, at your request ("Continue processing TODO").

2026-11-25 (Claude): done (`7da1281`, one commit in place of `9093b78`, the unfinished one on the branch pushed to GitHub, which may be deleted there). Topics started, replies, quotes, and one's own posts edited and deleted, each with a preview first, in Markdown, rendered by the forum's own renderer (`site/lib/markdown.wlg`); what is new to each member, marked on a board, `&unread=1` its first new post; topics watched, a watcher mailed of a reply once until they read the topic again; the schema's version 3, `setup.wlg` upgrading an earlier one. My guesses, marked in `Documents/weblingo-forum.md`, section 2, to change if you say so: until part 4's permissions, any member signed in writes on any board, and only its author changes a post, while its topic is not locked; a topic's first post is deleted only where it is its only post, the topic with it; a post has at most 20,000 characters, a title 200; a deleted post is kept, marked deleted; a post from before a member joined is never new to them; a member watches the topics they start; images in posts off by default, `"images"` in the settings. Checks: the Release build of the whole solution, no warnings; the forum's own tests (`weblingo test`, 38 passing, `writing-tests.wlg` new); `ForumTests`, its writing walked by two members, on SQLite, SQL Server, MySQL and PostgreSQL, again on `main`; the pages looked at in a browser, served by `wasp serve`. Windows only; Linux not tried.

## [DONE] WebLingo Forum, Part 4: Permissions and Moderation

`Documents/weblingo-forum.md`, section 6, part 4: groups and each board's permissions for each group; moderators' locking, pinning, moving, deleting and restoring; editing others' posts; bans; reports and the moderation log. Added by Claude, 2026-11-25, a part of "Extensive WASP Example".

2026-11-25 (Claude): done (`3a0d18b`). Each board's permissions for each group, applied to every page; moderators lock, pin, move, delete and restore topics, edit, delete and restore others' posts, work through members' reports, and ban members, which keeps them from signing in; every action in the moderation log; the schema's version 4, `setup.wlg` giving each board its groups' permissions by default, on making and on upgrading. My guesses, marked in `Documents/weblingo-forum.md`, section 2, to change if you say so: a reader not signed in has the guests' group's permissions alone, and an administrator every permission everywhere; a new board's defaults, guests seeing and reading it, members writing too, moderators moderating; a board one may not see is not there for them, one not read shows no topics; a moderator replies to a locked topic and changes its posts, moves a topic only to a board they moderate too, deletes a topic's first post only with the topic, and restores a post from the log; whoever moderates any board reads every report of the boards they moderate and the whole log, and bans any member but an administrator, for a day, a week, 30 days or for good; a banned member cannot sign in, told until when and why. Checks: the Release build of the whole solution, no warnings; the forum's own tests (`weblingo test`, 43 passing, `moderation-tests.wlg` new); `ForumTests`, its moderation walked on a second board, on SQLite, SQL Server, MySQL and PostgreSQL, again on `main`; `setup.wlg`'s upgrade from version 3, and the pages, tried by hand. Windows only; Linux not tried.

## [DONE] WebLingo Forum, Part 5: Administration

`Documents/weblingo-forum.md`, section 6, part 5: the settings pages, categories and boards, groups and their permissions, and members. Added by Claude, 2026-11-25, a part of "Extensive WASP Example".

2026-11-25 (Claude): done (`2c5c4d6`). The administration's pages, `/admin`, for administrators alone: the forum's settings, its name, description, language, time zone, pages, sign-up, mail, address and colours; categories and boards made, changed and removed, and each group's permissions on a board; groups made, renamed and removed; members' groups, and their addresses confirmed. No change to the schema. My guesses, marked in `Documents/weblingo-forum.md`, section 2, to change if you say so: a category is removed only without boards, a board only where it has never had a topic; the forum's own four groups stay, and a group an administrator makes may do nothing until a board says so; an administrator never takes themselves out of the administrators' group. The logo the design names waits for part 8, which sends files back. Checks: the Release build of the whole solution, no warnings; the forum's own tests (`weblingo test`, 47 passing, `administration-tests.wlg` new); `ForumTests`, its administration walked, on SQLite, SQL Server, MySQL and PostgreSQL, again on `main`; the pages looked at in a browser. Windows only; Linux not tried.

## [DONE] WebLingo Forum, Part 6: Private Messages

`Documents/weblingo-forum.md`, section 6, part 6: conversations of two or more members, with unread counts. Added by Claude, 2026-11-25, a part of "Extensive WASP Example".

2026-11-25 (Claude): done (`b778ec1`). Private messages: a member begins a conversation with up to ten others, named by their names, with a subject and a first message, previewed if asked for; its members read and reply to it, each message new to them until read, the header counting them, `Messages (2)`; a member leaves one, the others keeping it; a member reads only the conversations they are in. The schema's version 5. My guesses, marked in `Documents/weblingo-forum.md`, section 2, to change if you say so: at most ten others; no moderator reads a conversation; no mail tells of a message. Checks: the Release build of the whole solution, no warnings; the forum's own tests (`weblingo test`, 49 passing, `messages-tests.wlg` new); `ForumTests`, its messages walked by three members, on SQLite, SQL Server, MySQL and PostgreSQL, again on `main`; the pages looked at in a browser. Windows only; Linux not tried.

## [DONE] WebLingo Forum, Part 7: Search

`Documents/weblingo-forum.md`, section 6, part 7, and section 3.9: the forum's own index of words, searched within a board or by an author. Added by Claude, 2026-11-25, a part of "Extensive WASP Example".

2026-11-25 (Claude): done (`23fa1fd`). The forum's own index of words, the schema's version 6, kept as posts are written and edited, a topic's title with its first post; `/search`, the posts with every word given, on the boards the reader may read, on one of them and by an author where asked; `setup.wlg` indexing the posts there are, on making and on upgrading. My guesses, marked in `Documents/weblingo-forum.md`, section 3.9, to change if you say so: a word letters and digits of any script, 2 to 40 of them; whole words only, up to 8 a search; the latest posts first; a result showing the beginning of its post as it was written, its Markdown included. Checks: the Release build of the whole solution, no warnings; the forum's own tests (`weblingo test`, 52 passing, `search-tests.wlg` new); `ForumTests`, its search walked, on SQLite, SQL Server, MySQL and PostgreSQL, again on `main`; the page looked at in a browser. Windows only; Linux not tried.

## [DONE] Indexing a String in Constant Time

A string's `length` and `s[i]` count Unicode scalar values (the specification, section 3.3), and the runtime counts them from the string's start each time (`WebLingoMembers.ScalarCount`, and the offset found by `WebLingoMembers`' indexing): each takes time in proportion to the string's length, so that a loop over a string by its index takes time in proportion to its length's square, 20,000 lookups in a string of 20,000 characters 683 ms, against 2 ms visiting it with `foreach`. Found while writing the forum's Markdown renderer, which reads an array of a line's characters instead (`charsOf`, `Examples.wasp/WebLingoForum/site/lib/markdown.wlg`). Make `length`, `s[i]` and `slice` take constant time where they can, and no longer than now elsewhere, measured by a workload of the benchmark's. Added by Claude, 2026-11-25.

My guesses, to change if you say so: the heap keeps, with each string, its count of scalar values and whether it has a character beyond the Basic Multilingual Plane, a surrogate pair, found once, when it is first counted, so that a string without one, most strings, is indexed directly; and a string with one indexed through a table of its scalar values' positions, made when it is first indexed, and kept while the string lives.

2026-11-25 (Claude): done (`25d9038`), as my guesses had it: each string's count of Unicode scalar values kept with it in the heap once counted, and, for a string with a surrogate pair, where each of its scalar values starts, made the first time a position in it is asked for, counted in the heap's units and freed with the string (`WebLingoHeap.ScalarCount`, `ScalarStart` and `ScalarsBefore`). `length`, `s[i]`, `slice`, `indexOf`, `lastIndexOf`, `padStart` and `padEnd` take constant time after the first. Measured with a new workload of the benchmark's, `indexing`, against `main`, six rounds alternating: 1,981 ms to 7.7 ms; `closures`, `objects` and `strings`, sixteen rounds, within the machine's noise, none slower by a rank test (the specification, section 5.6, "A string read by its index"). Checks: the Release build of the whole solution, no warnings; the full suite, 3,661 tests passing, none skipped, `RuntimeTests.AStringsScalarValuesAreCountedOnceAndFoundWithoutCountingAgain` new; the VM's tests again on `main`. Windows only; Linux not tried.

## [DONE] Sending Files Back in WASP

WebLingo Forum's part 8 needs WASP to send a file back (the owner's answer, A1 (a) of "WebLingo Forum, Part 8: Avatars and Attachments"): `response.write(bytes)` writing bytes as they are, and `await response.sendFile(path)`, a file within the folders the site may read, its type by its extension, or the type given as its second, optional, parameter. Designed with you before it is built. Added by Claude, 2026-11-25.

2026-11-25 (Claude): my guesses, to change if you say so: both in a page and in a handler; `sendFile(path, type = null)` reading the file through the site's file capabilities (`"files"`'s `"read"`), so that a path the site may not read is a `FilePermissionError`, and sending it as it reads it, never whole in memory; nothing written after it, an error where something is; its type, where the extension names none the host knows and none is given, `application/octet-stream`; `response.write(bytes)` keeping the type the response has, a page's `text/html` unless it sets another.

Q1: The file's other headers: (a) `Content-Length`, `Last-Modified` and an `ETag`, a request whose `If-None-Match` or `If-Modified-Since` matches answered 304 with no body, as WASP serves a site's own files; or (b) `Content-Length` alone, the rest the handler's?

A1: (a)

Q2: A part of a file, as video players and resumed downloads ask for it: (a) a `Range` request answered 206, with the bytes it asks for; or (b) not now, the whole file always?

A2: (a)

Q3: A file to be saved rather than shown: (a) a third, optional, parameter, `sendFile(path, type, { download: "report.pdf" })`, which sends `Content-Disposition: attachment` with the name, encoded for names that are not ASCII; or (b) the handler sets `response.headers["Content-Disposition"]` itself?

A3: (a)

2026-11-25 (Claude): taken up, A1 to A3 (a).

2026-11-25 (Claude): landed (`c6fc201`). Both in a page and in a handler: `response.write(bytes)` and `writeLn(bytes)` write `Bytes` as they are, in order with the text around them; `await response.sendFile(path, type = null, options = null)` finds the file through the site's file capabilities, `"files"`'s `"read"`, off the worker, a path the site may not read a `FilePermissionError`, a file that is not there, or a folder, an `IOError`; its type the one given, else the response's `Content-Type`, else its extension's, else `application/octet-stream`; `{ download: "name" }`, or `true` for the file's own name. Both hosts, ASP.NET Core's and FastCGI's, send it, read in pieces, never whole in memory, with the page's status and headers, by one plan, `WebLingoFileSending`: `ETag`, `Last-Modified`, 304 for `If-None-Match` or `If-Modified-Since` (A1 (a)), one range a 206, beyond the file a 416, several ranges the whole file (A2 (a)), and `Content-Disposition: attachment` with the name, `filename*=UTF-8''…` for one that is not ASCII (A3 (a)); a site's own files get the same tags, 304s and ranges. Changed guess, to change if you say so: what a page writes before `sendFile`, and after it, is not sent, rather than writing after it being an error; sending a file once the output is being sent, past its first 64 KB, is the error. Recorded in the specification's section 6.12, "Files sent", and its register, the WASP User Manual's section 9, "Sending a file", its example run with `wasp serve` and curl, the Standard Library Reference's `Response`, and the forum's design, sections 4 and 6. Checks: the Release build of the whole solution, no warnings; `WebLingo.Hosting.Tests`, `WebLingo.Architecture.Tests` and `WebLingo.Templates.Tests`, the forum's on all four databases, and nginx's and Apache's, among them, 233, 87 and 289 passing, none skipped. Tried on Windows only: Linux not tried.

## [DONE] WebLingo Forum, Part 8: Avatars and Attachments

`Documents/weblingo-forum.md`, section 6, part 8: members' avatars and posts' attachments, uploaded, kept in `data/uploads`, outside the site, and sent back. Added by Claude, 2026-11-25, a part of "Extensive WASP Example".

2026-11-25 (Claude): WASP cannot send them back yet: `response.write` writes a value's text, so that bytes go out as `Bytes:[137, 80, …]`, and a file outside the site is never served (section 4 of the design).

Q1: Sending a file back: (a) built into WASP first, a task of its own, designed with you: `response.write(bytes)` writing bytes as they are, and `await response.sendFile(path)`, a file within the folders the site may read, its type by its extension or provided as the second optional parameter. (b) the files served by the web server itself, from a folder it maps (IIS's virtual directory, nginx's `location`), outside WASP, so that the forum's permissions do not guard them; or (c) the forum without avatars and attachments, for now?

A1: (a)

2026-11-25 (Claude): A1 (a) taken up: "Sending Files Back in WASP", above, designs it with you, its questions waiting for your answers; this part waits for it.

2026-11-25 (Claude): taken up, WASP sending files back since `c6fc201` ("Sending Files Back in WASP", in `DONE.md`).

2026-11-25 (Claude): landed (`eefc3d5`). `site/lib/uploads.wlg`: a file known by its first bytes, never by its name, kept in `data/uploads` under a random name, its row in `uploads` (the schema's version 7), sent back by `response.sendFile` with its tag, a picture shown, any other file saved under the name it came with. A member's avatar on the profile page, shown on their posts and their page (`/avatar?id=7`); a post's attachments on the new topic's, the reply's and the edit's forms, an edit removing ticked ones (`/attachment?id=12`, to whoever may read the board, a deleted post's and topic's to its moderators); and the forum's logo, on the settings page, in the header (`/logo`). `setup.wlg` makes the folder, `uploads=` naming another; the site's `wasp.config` lets the site write it alone. My guesses, each marked **Guess** in the design's section 2, to change if you say so: an avatar a PNG, JPEG, GIF or WebP picture of at most 256 KB; a post at most 5 files, each at most 2 MB, of pictures, PDF documents, ZIP archives and UTF-8 text, the administrator changing each limit, 0 turning them off; a preview keeping no file, so that they are chosen again to be posted; the logo a picture of at most an avatar's size; and a private message without files. Checks: the Release build of the whole solution, no warnings; the forum's own tests, 59 passing; `ForumTests` on SQLite, SQL Server, MySQL and PostgreSQL, all 5 passing, none skipped. Tried on Windows only: Linux not tried. A topic's page, with an avatar, a picture and a document attached, looked at in a browser on a scratch forum.

## [DONE] Overlays in WASP

WebLingo Forum's part 9 needs a client's files in place of the forum's (the owner's answer, A1 (a) of "WebLingo Forum, Part 9: Customization"): folders searched before the site's own for its pages, includes, layouts, components, modules and files, a path taken from the first that has it, so that a client's folder holds only what it changes; and the forum's look kept apart from its logic, so that its look is easy to change without touching what it does. Designed with you before it is built. Added by Claude, 2026-11-25.

2026-11-25 (Claude): my guesses, to change if you say so: an overlay's files compiled and cached as the site's own, and watched, a change reloading the site; a path that leaves its folder, `../`, refused in an overlay as in the site; the overlays readable by the site's scripts as its own folder is; the forum's look its layouts, includes, components, stylesheets and texts, and its logic its modules in `site/lib`, which the look's files call and never the other way, so that a client changes the look by those files alone.

Q1: Which folder is the site's root: (a) as the answer has it, the forum's own `site`, its `wasp.config` naming the client's folders, `"overlays": ["../clients/acme"]`, so that each client has a copy of the forum's site, or its own `wasp.config` beside it; or (b) the other way round, the client's folder the site's root, with its own `web.config` and `wasp.config`, which names the forum's site as what it lies over, `"base": "C:/WebLingoForum/site"`, so that one installed forum serves many clients, each a folder of its own, its files taken first, the forum's after?

A1: (b)

Q2: A client's `wasp.config` settings beside the forum's (its database, its mail): (a) the site root's `wasp.config` alone, as now; or (b) an overlay's own `wasp.config` merged over the site's, setting by setting?

A2: (b)

2026-11-25 (Claude): taken up, A1 (b) and A2 (b).

2026-11-25 (Claude): landed (`325674f`). A site's `wasp.config` names the site it lies over, `"base": "C:/WebLingoForum/site"`, from its own folder (A1 (b)): a path under the site's root read from the site's folder where it has it, else from the base's, pages, includes, layouts, components, modules and files alike, its routes both folders', a site's file sent from where it is; the base's `wasp.config` read first for the settings the site's own does not give, each path in them from the base's folder (A2 (b)); both folders watched, a change to either reloading the site. My readings and guesses, to change if you say so: "setting by setting" read as the top level's settings, a client's `"databases"` replacing all the base's, not database by database; one base, a base's own `"base"` an error, and neither folder within the other; `"base"` a site's setting alone, `weblingo.config`'s an error; a script's `std:fs` paths from the client's folder, the base's files reached only where `"files"` lists them; and `wasp run` and `wasp check` reading a page without its base. The ASP.NET Core adapter's `Serve` and `UseWebLingo`, and `WebLingoFastCgiServer`'s constructor, take no root now, the application giving each file's path. Recorded in the specification's section 6.12, "A site over a base", and its register, the WASP User Manual's section 14, "A site over a base", and the forum's design, section 4. Checks: the Release build of the whole solution, no warnings; `WebLingo.Hosting.Tests`, `WebLingo.Architecture.Tests`, `WebLingo.Templates.Tests` and `WebLingo.Conformance.Tests`, 237, 87, 290 and 2,326 passing, none skipped, the web servers' and the forum's on all four databases among them. Tried on Windows only: Linux not tried.

## [DONE] WebLingo Forum, Part 9: Customization

`Documents/weblingo-forum.md`, section 6, part 9, and section 3.5: a client's files in place of the forum's, hooks and an example plugin, and an example client with its own look, texts and plugin. Added by Claude, 2026-11-25, a part of "Extensive WASP Example".

2026-11-25 (Claude): a client's look goes beyond the settings' name, logo and colours when its own files replace the forum's: a layout, an include, a component, a stylesheet, a text or a module. An include's and a layout's paths are fixed when the page compiles (the WASP User Manual, sections 5 and 6), so that the forum cannot choose them by client as it runs.

Q1: A client's files in place of the forum's: (a) built into WASP first, a task of its own, designed with you: `wasp.config`'s `"overlays": ["../clients/acme"]`, folders searched before the site's own for its pages, includes, layouts, components, modules and files, a path taken from the first that has it, so that a client's folder holds only what it changes; (b) the forum's own, no change to WASP: a script, `weblingo run deploy.wlg client=acme`, copying the forum's site, then the client's folder over it, into the folder the server serves; or (c) themes by name, no change to WASP: each piece of the look a component, rendered from the theme's folder where it has one, else the forum's, the layout and its includes the forum's alone?

A1: (a). Make sure the presentation is well separated from the logic so that the look and feel of the site is easy to customize, without impeding the function.

2026-11-25 (Claude): A1 (a) taken up: "Overlays in WASP", above, designs it with you, its questions waiting for your answers; this part waits for it.

2026-11-25 (Claude): taken up, a client's folder over the forum's site since `325674f` ("Overlays in WASP", in `DONE.md`).

2026-11-25 (Claude): landed (`607e7f7`), over WASP's bases (`325674f`). A client's folder is a site over the forum's; `site/lib/hooks.wlg` calls the plugins `plugins/index.wlg` lists, each exporting a map, `hooks`, at the events `"post.created"` and `"member.signedUp"` and the filters `"html"` and `"nav.links"`. The look kept apart from the logic, as your note asked: the look's files, layouts, includes, components, stylesheets and texts, reach no database and render no Markdown, a text's HTML and the header's links coming from the request's forum, `site.html` and `site.links`, through the plugins' filters; and three files, empty in the forum's site, for a client to replace without copying the forum's: `css/custom.css`, after the forum's colours, `texts/custom.wlg`, its texts in place of the forum's of the same names, and `plugins/index.wlg`. An example client, `clients/acme`: its own stylesheet, texts, plugin, `tea`, and page, `/rules`, its own SQLite database made by the forum's `setup.wlg`. My guesses, each marked **Guess** in the design's sections 3.5 and 3.8, to change if you say so: plugins listed by a file a client replaces rather than by the settings; those four hooks, and a plugin's error its page's; a client's stylesheet with the last word over the colours the settings give; any other file of the look replaced whole. Checks: the Release build of the whole solution, no warnings; the forum's own tests, 63 passing; `ForumTests`, the forum on SQLite, SQL Server, MySQL and PostgreSQL and Acme's on SQLite, 6 passing, none skipped. Tried on Windows only: Linux not tried; Acme's pages not looked at in a browser, only through the tests.

## [DONE] The Next Milestone

Every milestone of the specification's roadmap is implemented, M0.20 the last (section 9), and what comes after is the owner's choice, as M0.15 to M0.19 were (section 9, "Immediate next activity"). Each candidate below is something the specification leaves open, each designed with you before it is built, in parts, as the milestones before it were. Added by Claude, 2026-11-25, at the owner's request ("Yes, add tasks").

Q1: The next milestone, M0.21: (a) fair workers: a request that computes for long made to yield its worker to the other requests on it, at safe points, after a quantum of instructions, and the blocking work off the workers bounded (open question 14); (b) faster members: an instance's fields in slots its class gives them, rather than a dictionary, and where each instruction last found a member remembered, an inline cache (section 5.6, "Where the time goes", causes 1 and 3); (c) start-up: `weblingo` and `wasp` published ReadyToRun, the deferred task "`weblingo` and `wasp` Published ReadyToRun" (cause 7); (d) generations drained: a reload's old generation given a deadline, the requests and tasks still running on it then ended, and what it leaks found (open question 8); (e) parallel work: workers with heaps of their own within one program, exchanging copied messages (section 3.8, "Concurrency"; open question 7); (f) a standalone HTTP host, `WebLingo.Hosting.Standalone`, serving a site without ASP.NET Core, which Native C# will need (sections 1 and 6.1); or (g) another, which you write in A1?

A1: (b), (d), (f), (a), (c), (e). Write up TO-DO tasks.

2026-11-25 (Claude): done (`71b27a0`): the specification's roadmap, section 9, has M0.21 to M0.26 in your order, faster members, old generations drained, a standalone HTTP host, fair workers, start-up compiled ahead of time and parallel work, and `TO-DO.md` a task for each, "`weblingo` and `wasp` Published AOT-Compiled" moved out of [DEFER] as M0.25, with what to ask when it is taken up: Native AOT loads no assembly while a program runs, so that the native modules would need another way in, or ReadyToRun instead. Each is designed with you before it is built. No build or test: documents only.

## [DONE] WebLingo Forum: Pages Under Load

What "WebLingo Forum, Part 10: Deployment" measured (`Documents/weblingo-forum.md`, section 6, part 10): the forum's pages that only read, its front page, a board and a search, level off at 4 clients, at about 140 to 185 requests a second on a machine of 8 logical processors, as though something served them one at a time; and a topic's page, which counts each view, a write to SQLite's one file, at about 40, its 99th percentile 9.5 s at 64 clients. Find what serializes the pages that read, in the forum's code or in WASP's, and what a topic's view should cost on SQLite (its journal mode, counting views otherwise), measured before and after. Added by Claude, 2026-11-25.

2026-11-25 (Claude): done (`459eb41`). Probed piece by piece: each request runs the forum's modules anew, about 2.7 ms, as a module runs once for each execution; its texts, imported while the request ran, about 2 ms more than imported with their importer; with 4 clients and more, `wasp`'s requests share .NET's workstation collector, `wasp` using about 2.3 of 8 processors, the server collector doubling or tripling the pages that read; and a topic's view waits for SQLite's lock, which every reader takes too. Changed, the forum's own: `setup.wlg` sets SQLite's WAL, and `lib/texts.wlg` imports English's texts and a client's statically. Measured again, with the workstation collector: the pages that read 1.4 to 1.6 times as many a second, a topic's 4.6 times at 64 clients, its median 728 to 17 ms; in the forum's design, section 6, part 10. What is left, each a task of its own: "The Server Collector for `wasp`" and "A Topic's View Without a Write Each", with questions, and "Dynamic Imports Joined Once". Checks: `ForumTests` and `WebServerTests`, 10 passing, none skipped; the forum's own tests, 63; on Windows only.

## [DONE] Dynamic Imports Joined Once

What "WebLingo Forum: Pages Under Load" found: a module a page imports while it runs, `Module.import`, costs about 2 ms a request more than one imported with the page, a module of 370 texts: compiled once, it is joined to the request's program anew each request, its tables appended and its instructions' indices moved (`Documents/implementation.md`, section 3, "The Pipeline"). Make a module imported while a request runs cost what a static import costs after its first, without changing what a program sees, measured on the forum's pages and the benchmark's `imports`, against `main`, alternately. Added by Claude, 2026-11-25.

2026-11-25 (Claude): first measured, on a scratch build: the join's verification of the module's bytecode, each request, is not the cost, skipping it changing the import within the machine's noise (3.25 against 3.06 ms, 3.80 against 3.75). What is left, to measure next with timings of the join's own steps: its string constants made in the request's heap, every instruction relocated, and the program's tables copied, each request (`WebLingoVirtualMachine.TryLink`).

2026-11-25 (Claude): done (`b79f11e`). The join was not the cost: a module a page imports while it runs was compiled anew each request, `CompiledPage.Modules` a plain loader, where WASP keeps the compilations of a page's other files, as the specification's section 5.1 says it does; a cache now, as a page's components have, the module joined to each request's program as before. The forum's texts imported so: 3.25 ms a page before, 1.10 after, imported with the page 0.96. Checks: the Release build of the whole solution, no warnings; `WebLingo.Hosting.Tests`, 238, a test that such a module is read once on each worker among them, and `WebLingo.Templates.Tests`, 292, passing, none skipped. On Windows only.

## [DONE] Visual Studio Debug Adapter

A small Visual Studio extension (VSIX). It registers a debug engine through a .pkgdef that points at the adapter, so F5 and launch.vs.json start a .wlg script directly. This is how the Python and other DAP-based debuggers integrate with Visual Studio. It means a second extension to build and ship next to the VS Code WebLingo.vsix, because VS Code and Visual Studio VSIX files aren't interchangeable.

2026-11-23 (Claude): what is there: VS Code's extension, `Editors/VSCode`, which `Installers/Windows/package.ps1` packs into `WebLingo.vsix` itself, with no tool of Microsoft's; and no Visual Studio on this machine, only SQL Server Management Studio 22 (`vswhere`), so that nothing here can try an extension of Visual Studio's. Visual Studio runs a debugger that speaks DAP through its Debug Adapter Host, registered by a `.pkgdef` an extension installs, and starts it from `launch.vs.json` in a folder opened as a folder. My guesses, to change if you say so: `Editors/VisualStudio`, a `.pkgdef` and its `extension.vsixmanifest`, with no code of its own; the adapter the installed `weblingo.exe --debug-adapter`; Visual Studio 2022 and 2026; `.wlg` files coloured by the TextMate grammar VS Code's extension has, which Visual Studio reads too; setup offering it as it offers VS Code's; and what it debugs what VS Code's does, a script launched on this machine.

Q1: Trying it: (a) you install Visual Studio Community 2026 here, so that I build it and try F5 on a script; or (b) I build it from Visual Studio's documented contract, untried, and you try it?

A1: (a). Wait for an install.

Q2: Building it: (a) packed by a script of ours, as VS Code's `WebLingo.vsix` is, a zip of its manifest and `.pkgdef`, with no package; or (b) by the Visual Studio SDK's build tools, `Microsoft.VSSDK.BuildTools`, a NuGet package under Microsoft's own terms, downloaded for its build only?

A2: (a)

2026-11-23 (Claude): waits for Visual Studio Community 2026 installed on this machine (A1), packed then by a script of ours (A2).

2026-11-25 (Claude): Visual Studio Community 2026 (18.12) is installed, and debugs a page through its own Debug Adapter Host with no extension ("Debugging WASP Pages, Part 4", done, `2e27309`; `Editors/README.md`, "Visual Studio, without an extension"). What an extension of manifest and `.pkgdef` alone can do, read in Visual Studio's own (its CMake support registers its adapter so) and in Microsoft's VSDebugAdapterHost wiki: register an engine, `AD7Metrics\Engine\{…}`, whose `CLSID` is the Debug Adapter Host's and whose `Adapter` and `AdapterArgs` name `weblingo --debug-adapter`, which `DebugAdapterHost.Launch` then starts by its name, a launch file needing no `$adapter`. What it cannot: F5. Open Folder starts a `launch.vs.json`'s configuration, or F5 on a file, through a launch provider, a MEF component of an extension's own code (`IVsDebugLaunchTargetProvider`, `ExportVsDebugLaunchTarget`, in Visual Studio's `Microsoft.VisualStudio.Workspace.Extensions.VS.dll`), which a `.pkgdef` cannot declare; the wiki says F5 for an adapter was still to come, and Visual Studio's own adapters, CMake's and JavaScript's, start theirs with code. A provider of ours can be built here with no package: Visual Studio's own C# compiler (`MSBuild\Current\Bin\Roslyn\csc.exe`), .NET Framework 4.8's reference assemblies, which are installed, and Visual Studio's own Workspace assemblies, referenced, never shipped.

Q3: The extension: (a) with code: a launch provider of ours in C#, compiled for .NET Framework 4.8 by a script of ours, with the compiler and the assemblies Visual Studio installs, no package (A2's script kept), the VSIX its assembly, its `.pkgdef` and its manifest, so that F5 on a `.wlg`, or on a `.wasp`, in a folder Visual Studio opens debugs it, and `launch.vs.json`'s configurations name it; (b) the `.pkgdef` alone, as A2 (a) has it: the engine registered, named WebLingo, which `DebugAdapterHost.Launch` starts with a launch file and no `$adapter`, F5 not working; or (c) no extension: Visual Studio debugs through `DebugAdapterHost.Launch`, as `Editors/README.md` says, this task closed?

A3: (a)

2026-11-25 (Claude): taken up, A3 (a).

2026-11-25 (Claude): built, untried, on the branch `claude/vs-extension` (`f275a03`), not landed: `Editors/VisualStudio`, a launch provider in C#, `WebLingoLaunch.cs`, for F5 on a `.wlg` or a `.wasp` file in a folder Visual Studio opens and for `launch.vs.json`'s configurations of the type `weblingo`; `WebLingo.pkgdef`, registering `weblingo --debug-adapter` and `wasp --debug-adapter` with the Debug Adapter Host; `extension.vsixmanifest`; and `build.ps1`, which compiles it for .NET Framework 4.8 with Visual Studio's own compiler and assemblies, no package, and packs `bin/WebLingo.VisualStudio.vsix`; `Editors/README.md`, "Visual Studio, with WebLingo's extension". It waits for you to close Visual Studio, which is running here, so that its installer can install the extension; then I try F5 on a script and on a page, and a `launch.vs.json`, with clicks alone.

2026-11-25 (Claude): taken up again, Visual Studio closed.

2026-11-25 (Claude): done (`ef9dbde`), tried with Visual Studio Community 2026 (18.12). Two things failed and are fixed: Visual Studio's installer refused the VSIX as "not a valid VSIX package" until it had `manifest.json` and `catalog.json`, which `build.ps1` now writes from the manifest, as Visual Studio's SDK does; and F5 on a file gave its adapter the file's path relative to the folder, a launch's paths now from the folder Visual Studio opened. Installed by `VSIXInstaller.exe /quiet`, then driven by clicks alone: `launch.vs.json`'s configuration of a script, stopped on entry, at a breakpoint, its locals changing as it went on; F5 on a page in front, at a breakpoint in its loop, with its locals and its output; and a page's configuration with a relative `program`. Not tried: a site's configuration, without `program`, and **Set as Startup Item**, which needs a right-click. Found: Visual Studio lists a configuration only with the file its `project` names, which the README now says. Left for a task of its own, "WebLingo's Setup Offers Visual Studio's Extension": the guess that setup offers it, as it offers VS Code's. Checks: the extension built by `build.ps1`, without warnings; no test of the solution's reaches it. On Windows only, as Visual Studio is.

## [DONE] M0.21, Part 1: The Call's Own Cost

The specification's section 9, "M0.21 parts", part 1 (A1 (c) of "M0.21: Faster Members"): a call and its return, about 26 ns for a function and 43 for a method, where Lua's take 10 to 15; what each does measured, and what can be done once, or not at all, done so, each change measured against `main` on the benchmark, alternately, `calls` and `objects` among the workloads. Added by Claude, 2026-11-25.

2026-11-25 (Claude): started.

2026-11-26 (Claude): done (`1be18ca`). Found with the JIT's own listing of the VM's dispatch method, `DOTNET_JitDisasm=Run`: past its switch .NET had spent its budget of inlining, so that each property of the function a call read was a call of its own, ten on a function's call. Now a call reads them from the VM's own record of each function, `Callable`, as fields; a frame keeps the function, code and cells it holds already; a return clears its registers in a loop rather than by `Array.Clear`. Against `main` (`7bfbc3e`), twelve rounds run alternately, every workload, by a rank test: `calls` 9.90 to 7.70 ms, 22 percent faster, `closures` 10 percent, `objects` 4, none slower; a function's call and return 16.0 ns where they took 21.1, a method's 32.5 where they took 38.3 (the specification, section 5.6, "The call's own cost"). Tried and not kept, each measured: the dispatch method's `catch` reading the execution's fields in place of its loop's locals, `calls` 3 percent faster still but `imports` 8 percent slower; the debugger's test folded into the budget's, and `WebLingoValue.Kind` a field, each within the machine's noise. What is left of a call is mostly what every instruction costs, about 3.5 ns of dispatch each, and a method's lookup by name, which parts 2 and 3 address. Checks: the Release build of the whole solution, no warnings; the full suite, 3,687 tests, passing, none skipped; `weblingo` on the samples and `wasp` on `samples/SimpleWebsite`. Tried on Windows only; Linux not tried (CI runs on a push).

## [DONE] M0.21, Part 2: Fixed Places for a Class's Fields

The specification's section 9, "M0.21 parts", part 2 (A2 (b) of "M0.21: Faster Members"): the fields a class's constructor assigns, and those its other methods assign to `this`, given fixed places when the class is made, a base's first, in the same places in each class extending it; `this.x` in a class's own methods compiled to its place; any other access by the class's table of names; any other field kept as now; measured on its own against `main`. Added by Claude, 2026-11-25.

2026-11-26 (Claude): done (`e914420`). The lowerer gives each class the fields its constructor and its other methods assign to `this`, after its base's where its base is a class the program declares, never its own methods', properties' or static members' names; a class whose base is another value, one `new Class` made or a parameter, has its base's places, its own fields kept as before, as A2 allows. `this.x` in a class's own constructor and methods, a compound assignment's, an increment's and a closure's inside them among them, is `GetField` or `SetField`; each falls back to `GetMember` and `SetMember` wherever the place does not hold the field, a first assignment among them, so that a static field, a property's setter, a refusal and an error are as before (`FieldPlaceTests` runs 19 programs through places and by name and compares their output and errors). A method or a property given to a class already made, named as a placed field, stops `SetField`'s direct writes for the rest of the execution. WLBC is 0.2, its classes listing their fields' places. Against `main` (`1be18ca`), twelve rounds run alternately, every workload, by a rank test: `objects` 34.35 to 28.60 ms, 17 percent faster, none slower beyond the machine's noise; `this.v` read 3.5 ns where it took 11.1; a placed field read by its name from outside its class 1.2 ns slower, and a method's call 1 ns, both part 3's paths (the specification, section 5.6, "Fields in places"). Found and fixed on the way: placed inline, the two operations spent .NET's budget of inlining into the dispatch method, `calls` 14 percent slower, so each calls helpers .NET does not inline (`Documents/implementation.md`). Guessed, Proposed: a class extending one the program declares later in the same block, or a class of another file imported by name, has no compile-time places of its own, its base not known when it is lowered. Not done: `Examples.wlg/01-hello.wlbr`, your WLBC 0.1 file, is refused by this build, a question in "Your Compiled 01-hello for WLBC 0.2". Checks: the Release build of the whole solution, no warnings; the full suite, 3,709 tests, passing, none skipped; `weblingo` on the samples. Tried on Windows only; Linux not tried (CI runs on a push).

## [DONE] M0.21, Part 3: A Member's Cache

The specification's section 9, "M0.21 parts", part 3 (A3 (a) of "M0.21: Faster Members"): a table of the module's beside its code, an entry for each instruction that reads, assigns or calls a member, remembering the class and the place, or the method, it found last; made when the module is loaded, never written to its bytecode; safe across workers, each entry replaced whole; keyed by the class's compiled definition; checked against the class's version, which its changes while the program runs increase; measured after part 2, against it. Added by Claude, 2026-11-25.

2026-11-26 (Claude): done (`518a75f`). `GetMember`, `SetMember` and `CallMethod` on an instance remember what they found in their function's table beside its code, made with the function, never written to a file: a field's place among a class definition's, or a method, by the definition of the instance's class, how many bases up it was declared and its place among that class's declared methods, each entry immutable, replaced whole by one reference's write. Keyed by the definitions, which every execution of the module shares, so that a site's every request finds what the first remembered, in classes it makes anew. A method's entry serves only where each class up to the one declaring it is unchanged since it was made, which your A3's "class's version" is here: given or losing a method or a property, or given a base anew, after its definition's members; and where the instance has no field outside its places, which could hold a function of the method's name. Guessed, Proposed: a module imported while the program runs keeps its functions' tables, but its classes' definitions are copies its join makes, so that what they remember serves the execution that joined it, never another. `MemberCacheTests`: methods replaced, removed and a base's replaced after a call remembered them, two classes and a plain instance at one call, a field missing at its place, a class made twice from one definition, and one module run in four executions, one changing its class first. Against `main` with part 2 (`e914420`), twelve rounds run alternately, every workload, by a rank test: `objects` 4 percent faster, `loops` 2, none slower; a method's call 25.6 ns where it took 34.6, a field read by its name 7.2 where it took 12.3 (the specification, section 5.6, "A member's cache"). Found and fixed on the way: the loop kept the dispatch method's `resuming`, read once, in a register of its own, which made `calls` 4.5 percent slower once the cache's cases were in, until it was a field (`Documents/implementation.md`). Checks: the Release build of the whole solution, no warnings; the full suite, 3,717 tests, passing, none skipped; `weblingo` on the samples and `wasp` on `samples/SimpleWebsite`. Tried on Windows only; Linux not tried (CI runs on a push).

## [DONE] M0.21: Faster Members

An instance's fields in slots its class gives them, rather than looked up by their names, and where each instruction last found a member remembered, an inline cache, so that reading a field, assigning one and calling a method cost little more than reading a variable (the specification, section 5.6, "Where the time goes", causes 1 and 3, and "After the remedies", what the remedies left). Designed with you before it is built, in parts, each measured against `main` on the benchmark. Added by Claude, 2026-11-25, at the owner's answer, A1 of "The Next Milestone": (b), (d), (f), (a), (c), (e), in that order (the specification, section 9).

2026-11-25 (Claude): where it starts. "Members Found Without Their Names: Shapes and Caches" (`8685a51`) built neither: a class remembers its last four lookups instead, to keep state out of the VM's dispatch method, which has cost calls before; with the other remedies, a field's read costs about 13.5 ns, its assignment 26 and a method's call about 43, most of it the call's own (the specification, section 5.6, "After the remedies").

Q1: What it does first: (a) shapes, and a cache for each instruction that reaches a member, as the milestone says, each part landed only where the benchmark gains and no workload is slower by a rank test; (b) a probe on a branch of what they would save, its numbers put to you before anything lands; or (c) the call's own cost instead, `calls` being where Lua is three to four times as fast?

A1: (c)

Q2: An instance's shape: (a) hidden classes, as V8's: instances whose fields came in the same order share a shape, each field added a step to the next; or (b) a class's fields, those its constructor assigns, given fixed places when the class is made, any other field kept as now?

A2: (b), a class's fields, those its constructor assigns, and those its other methods assign to `this`, given fixed places when the class is made, a base's first, in the same places in each class extending it; `this.x` in a class's own methods compiled to its place; any other access by the class's table of names, an instruction remembering the last class it saw and the place it found, so that the next instance of that class reads it directly; any other field kept as now; on both VMs, measured on the benchmark, `objects` among the workloads, against `main`; hidden classes later, if fields made otherwise are measured to matter.

Q3: Where a member's cache lives: (a) a table of the module's beside its code, an entry for each instruction that reads, assigns or calls a member, remembering the shape and place, or the method, it found last; or (b) the class's own, as now, widened to fields?

A3: (a), a table of the module's beside its code, an entry for each instruction that reads, assigns or calls a member, remembering the class and the place, or the method, it found last; made when the module is loaded, never written to its bytecode; safe across workers, each entry replaced whole, never seen half-written; keyed by the class's compiled definition, which every execution shares, not the class an execution makes; and checked against the class's version, which `extend`, `setMethod`, `removeMethod` and its other changes while the program runs increase. Measured in two steps, on both VMs, against `main`: the fixed places alone, then the cache.

2026-11-25 (Claude): your answers recorded in the specification's register and its section 9, "M0.21 parts" (`7bfbc3e`), which defines its three parts, each a task below, landed in that order. WebLingo has one VM since 23 November 2026, so that "on both VMs" is the register VM. Waits for its parts.

2026-11-26 (Claude): done, its three parts landed (`1be18ca`, `e914420`, `518a75f`), the version 0.21.0 (`518a75f`). Each measured against the commit before it, twelve rounds run alternately, every workload, by a rank test, none slower: the call's own cost, `calls` 22 percent faster, `closures` 10, `objects` 4; fixed places for a class's fields, `objects` 17, `this.x` read 3.5 ns where it took 11.1; a member's cache, `objects` 4, a method's call 26 percent faster and a field read by its name 41. The specification's roadmap, section 9, "M0.21 parts", and section 5.6, "The call's own cost", "Fields in places" and "A member's cache". Hidden classes, as A2 leaves for later, not begun: no field made otherwise has been measured to matter yet. Tried on Windows only; Linux not tried (CI runs on a push).
