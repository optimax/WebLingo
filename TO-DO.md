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

## M0.6.2: Lexer and Tokenization Tests

The lexer of the specification, section 8.4 ("Lexer"): identifiers, the keywords `var` and `const`, literals, operators, punctuation, source spans, and diagnostics for malformed input, with tests. It replaces the M0.6.1 lexer seed in `WebLingo.Language`.

## M0.6.3: Parser, AST and Diagnostics

The parser of the specification, section 8.4 ("Parser"): the initial subset of variable declarations, integer literals, identifiers, arithmetic, parentheses and mandatory semicolons, with source spans preserved and enough recovery to report several errors, and the diagnostics of section 8.4.

## M0.6.4: Binder and Lowered IR

The binder and the lowered IR of the specification, section 8.4 ("Binder/IR"): lexical symbols and the temporal dead zone resolved, a bound tree, and a lowered IR independent of the choice between a register VM and a stack VM (WS-BC002).

## M0.6.5: Minimal Bytecode Generator and Verifier

The bytecode generator and the verifier of the specification, section 8.4: an executable in-memory module (serialization waits until the WBC encoding is decided), and a verifier that rejects invalid opcodes, operands, branches, constants and uninitialized temporaries.

## M0.6.6: Minimal Register VM, Arithmetic, Locals and `print`

The VM of the specification, section 8.4 ("VM", "Runtime values"): execution of verified modules in an execution context (owner worker, cancellation, instruction and resource budgets, runtime services), null, Boolean, int64 and binary64 values, and `print` as a built-in the CLI registers, so that `weblingo run` runs section 8.4's example and prints `30`.

## M0.6.7: End-to-End Conformance on Windows and Linux

The conformance tests of the specification, section 8.6, end to end, on Windows and Linux, and the M0.6 acceptance criteria of section 8.8.

# END-OF-TO-DO
