# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## Built-ins Written in WebLingo, in `.wlg` Files

The built-ins written in WebLingo are C# string constants now: `Object` and `Error` (`WebLingoBinder.ObjectSource`, `ErrorSource`), the runtime errors' classes and the kinds of values' (`WebLingoPrelude.Source`), `Json` and `Iterator`, the interpolation's hidden class `<text>` (`WebLingoBinder.UseInterpolationText`), the spread and the array methods that take a function (`SpreadSource`, `ArrayMethodsSource`), `std:test` (`WebLingoTestSource.Text`), the WebLingo source in `WebLingoNativeModules.cs`, and the methods native classes declare in WebLingo (`Console`'s in `WebLingoConsoleClass.cs`, `Database.transaction` in `WebLingoDbModule.cs`, `Response`'s writing in `WebLingoHttpObjects.cs`). Move each into a `.wlg` file in the repository, beside the code that uses it, embedded in its assembly as a resource at build time and read from there by the compiler or the module, so that it is read, edited and checked as WebLingo (an editor's colouring, no escaped quotes, `weblingo check` on it where it can be), rather than as C# strings. Discussed by the owner and Claude, 2026-11-22; added by Claude the same day.

What stays as it is: they stay built in, the installed runtime holding no such file a site or a user can change, since they run in every site's requests and the specification keeps one semantics on every host (`Documents/implementation.md`, section 6), a program changing a built-in's behaviour for itself by `extend`, a subclass or `setMethod`, as now; the compiler-only names and intrinsics they use (`hold`, `writtenText`, `this.__write`, `<text>`, `<format>`) stay reachable from them alone, never from a script; each stays bound as native, an error in one reported where the program called it (`Documents/implementation.md`, section 11, "A Built-in Written in WebLingo Has No File"), its resource no file of the program's; and what they compile to, every test's output, and both VMs' results stay the same.

## A Native Module's Named Imports Bound Directly

`import { writeLn } from "std:console";` makes the whole module where the program begins, a `LoadBuiltin` for each of its exports, ten for `std:console`, and its `MakeModule`, though the program uses one, and each call reads the export from the module by its name first, `GetMember r11, r1."writeLn"`, then calls it (`Examples.wlg/01-hello.disasm.txt`). Reading an export at each use is what a module written in WebLingo needs, whose exported `var` may change (specification, section 3.9, "Exports"); but a native module's exports are the host's built-ins, which nothing assigns, so that for them it protects nothing. Discussed by the owner and Claude, 2026-11-22; added by Claude the same day. In two steps, each on both VMs:

1. A named import of a native module's function bound to the built-in itself, as a call of any built-in is, `LoadBuiltin` and `Call` without a `GetMember`, or the built-in loaded once where the program begins and called from there; a class or a constant a native module exports likewise, where it is as fixed.
2. A native module's instance made only where something uses it as a value: a whole import, `import console from "std:console";`, a dynamic one, `Module.import("std:console")`, which the VM then makes when first asked, as complete as a static import's would be, and the same instance for every importer, as a module is run once per execution; otherwise neither its `MakeModule` nor its `LoadBuiltin`s.

What stays as it is: a module written in WebLingo, whose named imports read their exports at each use; what a program writes, its errors and their positions, on both VMs; and the debugger's view of a named import (`Documents/implementation.md`, section 11, "Inspecting named imports in the debugger"), which a program compiled with `--debug` keeps, either by the module's form as now or by the debugger's learning the direct binding. Each step measured on the benchmark against `main`, alternately, as changes to the call path are (`Documents/implementation.md`, section 11, "A VM's Dispatch Method Is Sensitive to Its Locals"), with a workload that calls an imported native function in a loop.

# END-OF-TO-DO

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing  a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

## Minimal WebLingo Installer: Linux

Create the easiest installer for Linux, so that WebLingo could be executed like any scripting language (e.g. Python) on a Linux machine.

2026-11-18 (Claude): a script runs as `weblingo run script.wl` now; a first line `#!…` is a syntax error, and `weblingo script.wl` a usage error. Its .NET waits for "Minimal WebLingo Installer: Windows"'s Q1.

Q1, a script that runs by itself, as a Python one does: (a) a first line `#!/usr/bin/env weblingo`, which the lexer skips, a `.wl` file made executable then run by its name, and `weblingo script.wl` the same as `weblingo run script.wl`; (b) the first line only, `#!/usr/bin/env -S weblingo run`, Linux's `env -S` passing `run` on; (c) neither, `weblingo run script.wl` only.

A1: (a)

2026-11-19 (Claude): `weblingo script.wl` runs the script now, as `weblingo run script.wl` (`397c55f`), at the owner's request; the first line `#!/usr/bin/env weblingo` waits for this task.

Q2, the installer: (a) a `.tar.gz` with an `install.sh`, which puts WebLingo in `/opt/weblingo` and links `/usr/local/bin/weblingo`, run as `sudo ./install.sh`; (b) a `.deb` package, `sudo apt install ./weblingo.deb`; (c) both.

A2: (c)

## Rewrite Links

Rewrite the classic ASP application Links into WASP. Run locally, against the local SQL Server.

