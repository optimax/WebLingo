# WebLingo — Implementation

This document covers the WebLingo repository as a whole: the product, its documents, the layout and projects, the development environment, how the specification's decisions are handled, the architectural rules for code, source text and positions, building and testing (a finished task's checks included), licensing, known traps, the choices not to change without asking, and where the next tasks come from. The general rules for agents are in `AGENTS.md`, and how to work through the tasks in `TO-DO-INSTRUCTIONS.md`. The design itself is in the specification (section 2), and the state of the work is in `HANDOFF.md`.

Paths in this document start at the repository root.

------

# 1. Product

WebLingo is a JavaScript-inspired, native-oriented, cross-platform **server-side scripting language**, meant to combine the easy deployment of PHP and classic ASP with modern language semantics, secure defaults and efficient execution (specification, section 1).

- Scripts and modules are `.wl` files; HTML pages with server-side WebLingo templates are `.wlp` files.
- Source is compiled to WBC, a verified portable bytecode, and executed by the WebLingo VM.
- The language is JavaScript-inspired but does **not** promise ECMAScript compatibility.
- The compiler, VM, runtime and hosting are written first in C# on .NET 10. The long-term goal is to compile that same C# code with the project's independent Native C# compiler, so that WebLingo runs without .NET and without a rewrite in C++. Native C# and WebLingo are separate languages and runtime systems.
- The engine and the platform services are independent of IIS, ASP.NET Core, FastCGI, Kestrel, Windows and Linux. IIS, through an ASP.NET Core adapter, is the first deployment target; FastCGI and a standalone HTTP host are architectural targets.

The current state (specification, sections 9 and 15): a compiler and two VMs, a register VM and a stack VM, for M0.6's subset and the language of the specification's sections 3.5 to 3.8 (statements, collections, classes and exceptions, and asynchrony), whose register VM the `weblingo` CLI runs; and pages (`.wlp`, section 13, M0.12.1), their includes (M0.12.2), components (M0.12.3) and layouts (M0.12.4), which `welp`, WebLingo Pages' command line, runs; hosting and the platform layer are not implemented yet. `samples/HelloWorld`, `samples/Arithmetic` and `samples/Modules` run through `weblingo`, and `samples/SimpleWebsite` through `welp`.

**Names** (specification, section 1): the project is **WebLingo** (formerly WebScript), its intended domain `weblingo.dev`, the solution `WebLingo.slnx`, the project and namespace roots `WebLingo`, the CLI executable `weblingo`, and WebLingo Pages', the technology of templates for web servers (specification, section 13), `welp`. Scripts and modules are `.wl` files, templates `.wlp` files; specification IDs (`WL-L011`, `WL-BC002`) and diagnostic codes (`WL1001`) have the prefix `WL`, and type names in source the prefix `WebLingo` (`WebLingoLexer`, `WebLingoToken`), each type in a file of its name. WebScript's `.ws`, `.wsp`, `WS` and `Ws` were renamed so on 14 November 2026, the owner's decision, with nothing kept for backward compatibility.

------

# 2. Documents

- `Documents/weblingo-architecture.md` is the architecture specification, and its only copy (user decision, 2026-11-12; specification, section 8.2): the language, the runtime values and the GC, the bytecode and the VM, hosting, the platform abstraction layer, templates, the solution's structure, the milestone roadmap, the open questions, and the register of decisions with their status. In this document, "the specification" means that document. It is the design document that `AGENTS.md` and `TO-DO-INSTRUCTIONS.md` refer to.
- `WebLingo/README.md` says how to build, test and run the solution.
- `Documents/M0.6.1-status.md` records what the current milestone delivered and what it does not implement.
- `Documents/NativeCompatibility.md` is the inventory of .NET facilities and how the portable core may use each (section 6).
- `HANDOFF.md` at the root, when present, records the current state of the work and its next steps.
- `TO-DO.md`, `TO-DO-INSTRUCTIONS.md` and `DONE.md` at the root hold the tasks, how to work them, and the tasks that are done.

Before changing source, read the specification's decision register (section 1.1) and open questions (section 10), and the sections that bear on the task. Each stage of the pipeline (lexer, parser, binder, lowered IR, bytecode generator, verifier, VM) is built on by every later one, so when a task works on one stage, read the specification's sections for the later stages too, and the tasks for them in `TO-DO.md`, before settling a data structure or a contract.

------

# 3. Repository Layout and Projects

The repository root holds the files for agents and for planning (`AGENTS.md`, `CLAUDE.md`, `TO-DO.md`, `TO-DO-INSTRUCTIONS.md`, `DONE.md`, `HANDOFF.md`), the `.gitignore` for the whole repository, `.github/workflows/` for CI, and `Documents/`, the project's documents. The solution is in the `WebLingo/` folder:

```text
WebLingo/                      the repository
├── AGENTS.md, CLAUDE.md, TO-DO.md, ...
├── .github/workflows/
├── Documents/
└── WebLingo/                  the solution
    ├── WebLingo.slnx
    ├── Directory.Build.props
    ├── Directory.Packages.props
    ├── global.json
    ├── benchmarks/WebLingo.Benchmarks/
    ├── samples/
    ├── src/WebLingo.*/
    └── tests/WebLingo.*.Tests/
```

Commands in this document are run from the solution folder, `WebLingo/`, unless they say otherwise.

**Shallow folders** (user decision, 2026-11-12; specification, section 8.2): keep the folder structure shallow. Documents go into `Documents/` itself, not into subfolders of it or into a `docs/` tree in the solution.

A ZIP of changes (`AGENTS.md`, section 10) includes the repository's root folder, with the solution one level below it:

```text
WebLingo.202611121500.zip
└── WebLingo/
    └── WebLingo/
        └── src/
            └── WebLingo.Language/
                ├── WebLingoLexer.cs
                └── WebLingoTokens.cs
```

## Projects

- **The portable core:** `WebLingo.Core`, `WebLingo.Language`, `WebLingo.Bytecode`, `WebLingo.Compiler`, `WebLingo.Runtime`, `WebLingo.VM`, `WebLingo.VM.Stack`, `WebLingo.Templates`, `WebLingo.StdLib`, `WebLingo.Platform.Abstractions`, and `WebLingo.Hosting.Abstractions`: eleven.
- **The adapters to .NET:** `WebLingo.Platform.DotNet` (the .NET implementation of the platform abstraction layer, the PAL) and `WebLingo.Hosting.AspNetCore` (the host for IIS).
- **The command lines:** `WebLingo.Cli`, which builds `weblingo`, and `WebLingo.Pages.Cli`, which builds `welp`, WebLingo Pages' command line, sharing `weblingo`'s code (`WebLingoCommandLine`, given a `WebLingoCommandLineProgram`), and so referencing `WebLingo.Cli`.
- **The benchmark:** `WebLingo.Benchmarks`, in `benchmarks/`, a console program that times the VMs on fixed workloads with `Stopwatch` (the specification's section 5.6, WL-BC002). It is a tool, not part of the product, and uses no package. Its workloads are `.wl` files in its `Workloads/` folder, and a `.wlp` page, `templates.wlp`, which the template compiler compiles, embedded in it, each with the output every VM must give (`WebLingoWorkloads`), their line breaks read as LF; a VM is added to it as an `IWebLingoBenchmarkEngine`.
- **The tests:** `WebLingo.Language.Tests` (lexer, parser, source text), `WebLingo.Compiler.Tests` (binder, lowering, and both code generators), `WebLingo.Bytecode.Tests` (both verifiers, with fuzz tests), `WebLingo.Runtime.Tests` (values, arithmetic, formatting, `write` and `writeLn`), `WebLingo.VM.Tests` (execution on both VMs, runtime errors, budgets, the VMs' arithmetic against the runtime's, and the benchmark's workloads on every VM it measures), `WebLingo.Templates.Tests` (pages: the template parser and compiler, each page on both VMs, and `welp` end to end), `WebLingo.Conformance.Tests` (the specification's section 8.6, end to end through the CLI; every conformance program on the stack VM, against the register VM's result; and fuzz tests of malformed source and bytecode), and `WebLingo.Architecture.Tests` (contracts, and the dependency rules checked on the built assemblies).

## The Pipeline

The compiler's stages are separate classes (specification, section 8.8): `WebLingoLexer` and `WebLingoParser` in `WebLingo.Language` give a `WebLingoSyntaxTree`; in `WebLingo.Compiler`, `WebLingoBinder` resolves names into a bound tree (`WebLingoBoundTree.cs`, `WebLingoSymbols.cs`), and `WebLingoLowerer` reduces it to the lowered IR (`WebLingoIr.cs`), a tree neither register- nor stack-shaped (WL-BC002). `WebLingoCompilation` runs them in order and stops at the first stage that reports an error; a program that imports others (specification, section 3.9) is parsed file by file first, each file found by the host's `IWebLingoSourceResolver` (`WebLingoCompilerOptions.Resolver`), and bound as one program, each imported file's top level an `async` function of its own. The host's built-ins reach the compiler as `WebLingoCompilerOptions`. `WebLingoCodeGenerator` turns the IR into a module of the in-memory bytecode (`WebLingo.Bytecode`, `WebLingoBytecodeContracts.cs`), and `WebLingoCompiler` runs the whole compiler, checking its own output with `WebLingoBytecodeVerifier`. `WebLingoDisassembler` shows a module as text. For the comparison of the two kinds of VM (WL-BC002, the specification's section 5.6), the same IR also goes to `WebLingoStackCodeGenerator`, which generates the stack bytecode (`WebLingoStackBytecodeContracts.cs`, in `WebLingo.Bytecode`, since the compiler may not depend on a VM), and `WebLingoStackCompiler` runs the whole compiler for it, checking its output with `WebLingoStackBytecodeVerifier`.

A page (`.wlp`, specification, section 13) is read by `WebLingoTemplateParser` (`WebLingo.Templates`) into the parser's tokens: each run of its text, and each `<%= … %>` and `<%- … %>`, a call of a built-in that writes it (`WebLingoTemplateWriters`, `WebLingo.Core`, names them so that no program can), and each `<% … %>` its code, which `WebLingoLexer` reads up to its `%>`. `include "path";` in a block is replaced by the template it names, found by the host's resolver (`IWebLingoSourceResolver`), each file at positions of its own among the program's (`WebLingoTemplateFiles`, whose `Locate` gives a diagnostic's file, at compile time and at run time). `WebLingoTemplateCompiler` compiles the tree with `WebLingoCompiler` or `WebLingoStackCompiler`, declaring those built-ins, and a host that runs pages registers them (`WebLingoTemplateBuiltins`, `WebLingo.StdLib`), as `welp` does, which finds templates from the application's root it is given (`--root`, the page's folder by default) with `WebLingoTemplateFileResolver`. `renderComponent(path, values)`, which the template compiler declares as a component's render (`WebLingoBuiltinDeclaration.RendersComponent`), is a dynamic import that runs its file anew each time (`ImportDynamic`'s flag 2): the VM finds and compiles a component with the loader `WebLingoTemplateCompiler.Components` gives it (`Components`), and `WebLingoTemplateSources` says which file a diagnostic of the page, or of a component or a layout, is in. A page's `layout "path", values;` makes the rest of the page its body: the page imports the layout (its path marked `layout:`), giving it the values and the body, an `async` function, which the layout's `renderBody()` awaits; the template compiler finds layouts with the templates' resolver, and reads them as layouts (`WebLingoCompilerOptions.Parse`). Nothing in the language's own stages knows of templates: they take tokens, or a parsed tree, from another reader.

A native module (specification, section 3.9) is an assembly a host loads from a scheme's folder: the compiler asks the host's `IWebLingoNativeResolver` (`WebLingoCompilerOptions.Native`) whether an import's path names one, and for its exports, each one of the host's built-ins, which the resolver registers as it loads the module; a native module is then a file without a function, as `std:system` is. The command lines' resolver is `WebLingoNativeModuleLoader` (`WebLingo.Cli`), which loads assemblies with reflection, adapter-only code, never the portable core's; their schemes come from `weblingo.json` and `--scheme` (`WebLingoSchemes`). `tests/WebLingo.Native.TestModule` is a native module the tests load, copied beside them, not referenced. `std:system` is a native module too, `WebLingoSystemModule` in `WebLingo.StdLib`, which `WebLingo.Cli`'s project installs as `std/system.dll` beside its output, and so beside `welp`'s and the tests'; a command line loads it before it compiles, its functions the host's built-ins.

The verifier must reject a malformed module, never throw or run out of memory on one: its dataflow check keeps state only at block starts, as bit sets, within fixed bounds (`MaxStateWords`, `MaxSteps`). Keep it so when operations are added, and add each one to the fuzz test's range.

At run time, `WebLingoVirtualMachine` (`WebLingo.VM`) verifies a module, links its imports to the host's built-ins, and runs it, a call pushing a frame onto the VM's own stack (never .NET's), in a `WebLingoExecutionContext` (`WebLingo.Runtime`: owner worker, cancellation signal, instruction and resource budgets, runtime services). Given a loader (`IWebLingoModuleLoader`, which the compiler's `WebLingoModuleLoader` implements), it compiles a module the program imports while it runs (`Module.import`, specification, section 3.9) and joins it to the program, appending its tables and moving its instructions' indices to match: a running program's tables only grow, each kept as a field of the execution, never as a local of its dispatch method, which would then miss what is added. The semantics of values live in `WebLingo.Runtime`, not in the VM, so that another VM shares them: `WebLingoValue`, `WebLingoOperators` (arithmetic, comparisons and concatenation), `WebLingoValueFormatter` (a value as text), `WebLingoMembers` (members and indexing, `x.name`, `x.name(…)`, `x[i]`), `WebLingoExceptions` (which runtime errors a handler catches, the `Error` one becomes, and how one nothing catches is reported), `WebLingoTextBuilder` (a value's text, which stops where an instance's `toString()` must run, for the VM to run it as a call), `WebLingoTasks.cs` (tasks, heap objects, and `WebLingoScheduler`, the execution's queue of tasks ready to resume and its timers, which a VM runs its waiting frames by; and `IWebLingoExecutionClock`, the host's clock, given as `WebLingoExecutionContext.Clock`, without which the execution runs in virtual time), and `WebLingoHeap`, the execution's worker-local heap, where strings live and values refer to them by handle; its roots are the VM's registers and constants, so anything else that holds a value of the heap must be registered as a root too (`AddRoots`), or a collection frees what it refers to. `WebLingoStackVirtualMachine` (`WebLingo.VM.Stack`) runs the stack bytecode the same way, sharing all of that, and `WebLingoExecutionResult` with the register VM (so it depends on `WebLingo.VM`); only its instructions differ, and its budget counts them. `weblingo` runs the register VM; the stack VM is run by the benchmark and the tests. `write` and `writeLn` are `WebLingoWriteFunction` and `WebLingoWriteLnFunction` in `WebLingo.StdLib`, and `delay` is `WebLingoDelayFunction` (`WebLingoStandardBuiltins` gives all three); the CLI, `WebLingoCommandLine`, is the host that gives them, loading them as `std:system`, a native module (see below), gives an execution the platform's monotonic clock (`DotNetClock`), finds the files a program imports (its `FileResolver`), and runs `run` and `check`.

## Dependency Direction

Project references follow the specification, section 8.3:

- Core has no WebLingo project dependencies.
- Language depends on Core, not on Compiler or Runtime.
- Bytecode depends on Core, not on Compiler or VM.
- Compiler consumes Language and Bytecode.
- Runtime does not depend on Compiler.
- VM consumes Bytecode and Runtime.
- Hosting and the CLI compose the engine; framework-specific types do not leak into the core.
- All references form a directed acyclic graph.

Add projects (FastCGI, database providers, native adapters, `WebLingo.Hosting.Standalone`, the further test projects the specification lists) when their milestone begins, not before (specification, sections 8.2 and 13, WL-T008).

------

# 4. Development Environment

- Windows and Linux: both are development and target platforms
- .NET 10 SDK; `global.json` asks for 10.0.100 and rolls forward to the latest feature band
- C#, with `LangVersion` latest
- Target framework: `net10.0`, not `net10.0-windows`: nothing may depend on Windows
- `Directory.Build.props` gives every project nullable reference types, implicit usings, deterministic builds, and warnings treated as errors
- Central package management: versions are in `Directory.Packages.props`, never in a project's `PackageReference`
- Tests: xUnit v2 (2.9.3) through `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`, run with `dotnet test` (section 8)
- Solution format: `.slnx`
- No IDE is required: the `dotnet` command line builds, tests and runs everything. Visual Studio 2026 or another IDE may be used, but nothing may depend on one.
- CI: GitHub Actions, `.github/workflows/build.yml` at the repository root, which restores, builds in Release and tests the solution from its folder on `ubuntu-latest` and `windows-latest`, on every push and pull request, and reports both platforms even when one fails (but see section 11)
- Repository: GitHub, `optimax/WebLingo`, branch `main`

The version-sensitive technologies here are .NET 10, C# at `LangVersion` latest, xUnit v2, ASP.NET Core 10, and the GitHub Actions in the workflow.

Do not replace the `.slnx` solution with a legacy `.sln` unless explicitly requested.

------

# 5. The Specification and Its Decisions

The specification marks each decision **Approved** (explicitly accepted by the project owner), **Proposed** (a recommendation that still needs confirmation or validation), or **Provisional** (deliberately undecided). Its decision register (section 1.1) and its open questions (section 10) list them.

- Implement what is Approved. Do not change an Approved decision, or work around it, without the owner's explicit approval.
- Do not treat a Proposed or Provisional item as Approved: not in code, in tests, in comments, or in documents. Where a task needs one of them settled, ask. Until it is answered, build so that the choice stays easy to change, and say which choice you made.
- Where the specification is silent and the work needs a decision, do not invent language semantics. Check the specification's stated intent and its open questions, then ask. A choice made only to unblock work is recorded in the specification as Proposed.
- Do not borrow JavaScript's behavior merely because JavaScript has it. WebLingo's semantics are its own, and where the specification has decided otherwise (mandatory semicolons, `var` and `const` without `let`, int64 overflow as an error), it wins.
- When the owner approves a decision, update the specification in the same change: the section concerned and the decision register, with the date.
- When a milestone's state changes, update the documents that report it: the specification's roadmap and status (sections 9 and 14), the solution's `README.md`, and the milestone's status document.

The specification defines the milestones (M0.1 to M0.8, section 9), the increments of M0.6 (M0.6.1 to M0.6.7, section 8.7), and the specification IDs (`WL-…`), so these may be named in tasks, handoffs and commit messages, with that section (`AGENTS.md`, section 14, "Referring to Work").

------

# 6. Architectural Rules for Code

The specification's guiding invariants (section 11) bind every change. In code they mean:

- **One semantics.** WebLingo behaves the same on every host (IIS, FastCGI, standalone, the CLI) and every toolchain (.NET now, Native C# later). A difference between hosts or toolchains is never visible to a script.
- **The portable core stays portable.** Code in the portable core uses only what `Documents/NativeCompatibility.md` allows: no ASP.NET Core, no ADO.NET, no `Reflection.Emit` or other generation of CLR code at run time, no .NET `Task` or `ValueTask` as WebLingo's asynchronous contract, and no direct `System.IO.File` in the runtime, which goes through the PAL. When portable code needs a .NET facility the inventory does not list, add it there, with its classification, in the same change, and ask if it is not obviously portable. `PortabilityTests` (`WebLingo.Architecture.Tests`) checks the built assemblies against the inventory's adapter-only, prohibited and unused rows, so a rule added there for the core is added to that test too.
- **Dependencies point one way** (section 3), and framework types (ASP.NET Core's, ADO.NET's) never leak into the portable core.
- **Minimal third-party dependencies.** Ask before adding any package to the portable core.
- **WebLingo is its own engine.** Do not execute WebLingo by delegating to an existing JavaScript engine (V8, Node.js, Jint, ClearScript), by translating it to C#, or by generating CLR code at run time. The released product must not need .NET, Node.js, V8 or PHP (specification, section 12.1).
- **The IR stays VM-neutral.** The choice between a register VM and a stack VM is Provisional (WL-BC002). The register VM comes first, but the bound tree and the lowered IR must not depend on it, so that both can be benchmarked on equivalent workloads.
- **Every feature on both VMs** (user decision, 2026-11-13; specification, section 5.6, "Both VMs, for now"). Until WL-BC002 is decided, a feature that reaches the bytecode is built on both VMs in the same change: `WebLingoCodeGenerator`, `WebLingoBytecodeVerifier` and `WebLingoVirtualMachine`, and `WebLingoStackCodeGenerator`, `WebLingoStackBytecodeVerifier` and `WebLingoStackVirtualMachine`, each with its tests. Its conformance cases then run on both (`StackVmTests`), and both must give the same output and errors. Property access, async suspension, template output and I/O-bound work each get a benchmark workload (`WebLingoWorkloads`) when the language can express them.
- **One implementation of classes** (the owner's request, 14 November 2026; specification, section 3.7). A class's declaration, an `extend` block, and a program building a class while it runs (`new Class`, `setMethod`, `setProperty`, assigning a static member) go through the same methods of `WebLingoHeap`: `TryMakeClass`, `GiveConstructor`, `TrySetMethod`, `TrySetStaticMethod`, `TryDeclareStatic` and `TrySetConstant`; each VM's `TryGiveMembers` only makes the closures it gives them. A new kind of class member gets one such method, which both use; a change to a class's methods gathers anew only that class's, unless another extends it.
- **Bytecode is verified before it runs.** Malformed bytecode fails verification; it never crashes the VM.
- **Bad input gives diagnostics, not crashes.** Malformed source produces diagnostics with stable codes and source spans, and the parser recovers far enough to report more than one error. A diagnostic code, once assigned, keeps its meaning; use the codes the specification suggests (section 8.4), and ask before starting a new range.
- **Secure by default.** Template output is escaped unless it is explicitly raw; filesystem, network, database and native access goes through capabilities; resource use is bounded.
- **Templates ship whole.** Includes, layouts and parameterized components are all required in the first `.wlp`-capable release (WL-T001 to WL-T008); none of them is deferred to a later one.
- **Ownership is explicit.** Mutable heaps are worker-local, data crosses workers only by explicit transfer, `dispose()` releases resources deterministically, and the tracing GC reclaims script-object memory. Disposal and GC are separate.
- **Hot reload is atomic.** A reload swaps whole, fully initialized application generations; no request sees a partly initialized one.
- **Vertical slices first.** Get the pipeline working end to end on a small subset (source, lexer, parser, binder, lowered IR, bytecode generator, verifier, VM) before widening the language or the hosting.

------

# 7. Source Text and Positions

WebLingo source is read on Windows and Linux, from files that may end their lines with CRLF or LF. The compiler and the runtime must not depend on the platform's line ending (`Environment.NewLine`), path separator, current culture, or file-name case rules.

Be careful when translating between:

- .NET string indices (UTF-16 code units), which the scaffold's `WebLingoSourceSpan` and lexer use today,
- UTF-8 byte offsets, which serialized offsets and source maps use (specification, sections 8.4 and 14),
- Unicode scalar values, which string indexing and `length` count (M0.1),
- the one-based line and column numbers that diagnostics report.

`WebLingoSourceText` (`WebLingo.Core`) is the one place that turns a position into a line and a column. Which characters end a line (CRLF, LF, CR) and how a column is counted (in Unicode scalar values) are proposed in the specification, section 15.2, and not yet decided: change them only there and in `WebLingoSourceText`, with the owner's decision.

## Implementation Choices

Where the specification is silent, the choices M0.6 made are listed, as Proposed, in its section 15.2 (section 5 above). Each is made in one place in the code, named in that table where it is not obvious, so that the owner's decision changes one place. The diagnostic codes are constants in `WebLingoDiagnosticCodes` (`WebLingo.Core`); a new one is added there and to the specification's section 8.4.

------

# 8. Building and Testing

Tests use xUnit v2 (2.9.3) with `Microsoft.NET.Test.Sdk`, run by `dotnet test` through VSTest. Do not use xUnit v3 or Microsoft.Testing.Platform APIs and command-line conventions here; changing the test framework needs approval (section 12).

These commands are known to work (2026-11-12, Windows, .NET SDK 10.0.404), from the solution folder; the third runs one test class:

```bash
dotnet build WebLingo.slnx -c Release
dotnet test WebLingo.slnx -c Release --no-build
dotnet test tests/WebLingo.Language.Tests -c Release --no-build --filter "FullyQualifiedName~LexerTests"
dotnet run --project src/WebLingo.Cli -c Release --no-build -- --version
dotnet run --project src/WebLingo.Cli -c Release --no-build -- run samples/Arithmetic/arithmetic.wl
dotnet run --project src/WebLingo.Pages.Cli -c Release --no-build -- run page.wlp
dotnet run --project benchmarks/WebLingo.Benchmarks -c Release --no-build
```

The benchmark (section 3) writes a Markdown table: each workload's median and fastest time on each VM, the instructions a run executes, what it allocates, and its code's size. Its times are only comparable when they come from one run of it, in Release, on an otherwise idle machine; `--runs`, `--warm-ups` and `--workload` change what it runs. It turns .NET's tiered compilation off (`TieredCompilation` in its project), so that every method is fully optimized from its first call and no VM is timed while .NET still compiles it.

- The solution is small: build all of it rather than one project, and run the complete test suite after a change, which today takes seconds. When the suite grows, run targeted test classes during active development and the complete suite before a task is done.
- Each behavior in the specification's conformance table (section 8.6) has a test in `WebLingo.Conformance.Tests`, which runs programs through the CLI as files; a new conformance case goes there too. Template tests go into `WebLingo.Templates.Tests` (specification, section 13), which runs each page on both VMs and `welp` end to end.
- A new portable project gets its row of allowed references in `DependencyTests` (`WebLingo.Architecture.Tests`), from the specification's section 8.3.
- A test of malformed input checks the diagnostic's code and span, not only that compilation failed.
- Tests must pass on Windows and on Linux: no hard-coded `\` or `\r\n`, drive letters, absolute paths, or culture-dependent formatting and parsing.
- A test of language semantics is changed only after the new behavior was approved (section 5).

## A Finished Task's Checks

These are the checks that `TO-DO-INSTRUCTIONS.md` runs after each task and calls main's checks when they are run again on `main`, after the task has landed:

- the Release build of the whole solution, with no warnings (warnings are errors here);
- the complete test suite;
- when the task changes what the `weblingo` CLI or `welp` does, the CLI run on the samples and the programs the task affects, and `welp` on the pages it affects.

Only Windows is tried locally. Linux is tried by CI, on a push, which needs the user's go-ahead (and see section 11), so a task's notes say that Linux was not tried.

------

# 9. Regression Areas

A small change in one stage of the pipeline can change the behavior of every later stage. Beyond what `AGENTS.md`, section 8, asks, consider the effects on:

- source spans and the line and column numbers of diagnostics,
- diagnostic codes, messages, and the order diagnostics are reported in,
- token kinds and keywords (`let` is reserved and never a declaration keyword),
- the approved semantics: block-scoped `var` and `const`, each in scope from its declaration on and always initialized, mandatory semicolons, the numeric categories, int64 overflow as a runtime error, string length in Unicode scalar values,
- bytecode verification, and the VM's behavior on bad input,
- the CLI's commands, output, and exit codes,
- the project dependency graph and the portability inventory.

------

# 10. Licensing, Dependencies and Publication

**The whole repository may become public** (user decision, 2026-11-12; specification, section 8.2), not only the solution folder. Put nothing into it that should stay private: no personal data or contact details, credentials, keys or tokens, paths or names of private machines and drives, or private notes. This holds for every file, `TO-DO.md`, `DONE.md` and `HANDOFF.md` included, and for commit messages.

WebLingo is meant to become free and open source, but no license has been chosen: MIT is the preferred candidate, not a decision (specification, section 12). Until the owner selects a license, do not add a `LICENSE` file, license headers, `PackageLicenseExpression` or other license metadata, and do not describe any license as WebLingo's.

The open-source release needs an inventory of every dependency, with its version and license (specification, section 12.3). No inventory or notices file exists yet: when a package is added, upgraded or removed, its own dependencies included, name it, its version, and its license in the task's notes and in the commit message, and ask before creating such a file.

------

# 11. Known Traps

### CI Runs Only on a Push

CI is the only place Linux is tried, and it runs on a push or a pull request, which needs the user's go-ahead: work not yet pushed has been tried on Windows only. Until 2026-11-12 the workflow was in the solution folder, where GitHub Actions does not look (it reads `.github/workflows` at the repository root only), so it never ran; its first run, from the root, passed on both platforms (CI run 1, 12 November 2026, commit `5229806`). Keep the workflow at the root and its `working-directory` pointing at the solution folder.

The run logs need a signed-in GitHub account; the run's page and its result do not.

### The VMs Repeat Part of the Runtime's Arithmetic, and Its Messages

For speed, each VM does the arithmetic and comparisons of two int64s, or of two binary64s, in its dispatch loop, and leaves everything else, and every error, to `WebLingoOperators` (`WebLingo.Runtime`). A change to the semantics of those operators changes all three; `OperatorTests` (`WebLingo.VM.Tests`) runs every operator on pairs of edge values through both VMs and checks that each gives what `WebLingoOperators` gives. The messages of the errors the VMs raise themselves (calls, cells, budgets) are written in each VM; `StackVmTests` (`WebLingo.Conformance.Tests`) checks that both VMs give every conformance program the same output and the same errors, so a change to one VM's message is a change to the other's.

### A VM's Dispatch Method Is Sensitive to Its Locals

Each VM's `Run` is one large method, which keeps the running frame's state in locals. Adding locals to it, even in a label that rarely runs, has twice slowed calls by 15 to 20 percent (disposal, M0.10.3; asynchrony, M0.11.1), probably because .NET's JIT then keeps fewer of them in registers. Keep a rare path in a helper method, passing what it needs through the execution's fields, and leave only the switch of frames inline; and before landing a change to `Run`, compare it with `main`'s on the benchmark, running the two alternately, since one run on this machine varies by as much.

### Warnings Are Errors

`TreatWarningsAsErrors` is on for every project, so a nullable or analyzer warning fails the build, locally and in CI.

### Spans Are UTF-16 Indices

The scaffold's `WebLingoSourceSpan` and lexer positions are .NET string indices, not the UTF-8 byte offsets serialized forms need (specification, section 14). Convert explicitly; do not assume they are the same.

------

# 12. Choices Not to Change Without Asking

In addition to the list in `AGENTS.md`, section 13, do not change these without explicit approval:

- any Approved decision in the specification's decision register, or the status of a Proposed or Provisional one
- the names: WebLingo, `weblingo`, WebLingo Pages, `welp`, the `.wl` and `.wlp` extensions, the `WebLingo` namespace roots, the `WL` prefixes of specification IDs and diagnostic codes, and the `WebLingo` prefix of type names
- the implementation strategy: C# on .NET 10 first, Native C# later, no rewrite in another language
- the target framework, `net10.0`, and `global.json`
- the choice between a register VM and a stack VM, and the WBC binary format: do not freeze either
- the project structure and dependency direction, beyond adding the projects a milestone calls for
- central package management, and the shared settings in `Directory.Build.props`
- the test framework, xUnit v2
- the CI's platforms, Windows and Linux
- any dependency in the portable core, an existing JavaScript engine, or generation of CLR code at run time
- diagnostic codes already assigned
- licensing, the repository's visibility, and public releases

------

# 13. Where the Next Tasks Come From

When `TO-DO-INSTRUCTIONS.md` has the agent generate tasks of its own ("Proceed Autonomously"), it takes them from the specification, in this order:

1. the remaining increments of M0.6 (section 8.7), each with its contracts in section 8.4;
2. M0.6's acceptance criteria (section 8.8);
3. the later milestones of the roadmap (section 9).

The open questions (section 10), and anything the specification marks Proposed or Provisional that a task depends on, become questions for the user when the work reaches them (section 5).
