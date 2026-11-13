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
