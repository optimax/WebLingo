# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] Duplicated Files

Two files exist twice in the repository (`Documents/implementation.md`, section 11):

- the architecture specification, `Documents/weblingo-architecture.md` and `WebLingo/docs/architecture/weblingo-architecture.md`, identical on 2026-11-12; the second is the canonical one (`WebLingo/docs/architecture/README.md`);
- `.gitignore`: the root one covers the whole repository, and `WebLingo/.gitignore` is a subset of it.

**Q1: Shall `Documents/weblingo-architecture.md` be removed, leaving the canonical copy only, or is it kept on purpose (as your working copy, say), and if so, which copy do you edit?**

**Q2: Shall `WebLingo/.gitignore` be removed, now that the root `.gitignore` covers it, or kept so that the solution folder has its own (for example, for when it becomes the public repository)?**

## Make the CI Workflow Run

`WebLingo/.github/workflows/build.yml` builds and tests on Windows and Linux, but GitHub Actions does not run it: it is in the solution folder, and Actions reads workflows only from `.github/workflows` at the repository root. Its commands also expect `WebLingo.slnx` at the root (`Documents/implementation.md`, section 11). Move it to the root and point its commands at the solution folder, so that M0.8's Windows and Linux CI (specification, section 9) has a start. It first runs on a push, which needs the user's go-ahead.

## Verify the M0.6.1 Scaffold

The specification's next activity (section 9): validate M0.6.1 with `dotnet restore`, `dotnet build` and `dotnet test` on Windows and Linux. Then update what reports its verification as pending: `WebLingo/README.md` ("Verification status"), `WebLingo/docs/architecture/M0.6.1-status.md`, and the specification's sections 9 and 14 (and its copy, while there is one).

Found 2026-11-12 (Claude, on Windows, .NET SDK 10.0.404): restore and the Release build succeed with no warnings, the five tests pass, and `weblingo --version` answers. Linux is not tried yet: it waits for "Make the CI Workflow Run".

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
