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
