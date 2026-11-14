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

Q8, a script's parameters from the command line: `weblingo run script.ws size=10 name=Ann`, each value read as a WebLingo literal if it is one (`10`, `true`), as a string otherwise. Agreed?

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
