# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] Standard Module `std:tasks`

`Task.all`, `Task.any` and `Task.timeout` as the exports of a standard module, `std:tasks` (A4 of "Task Combinators"; specification, sections 3.8 and 3.9), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for the design of how standard modules are packaged, which A5 of "Native Code Extensions" makes native extensions, installed as other native modules are.

2026-11-14: the packaging is decided (A6 of "Native Code Extensions, Continued"): a standard module is a native module, in the `std` scheme's folder. Waits for "Native Modules".

## [IDLE] M0.13: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WL-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

2026-11-13: M0.13 now, after M0.12, templates (specification, section 9); waits for M0.12.

2026-11-14: M0.12 is done; still waits for the choice between the VMs (WL-BC002), which waits for an I/O-bound workload, the last of those "Both VMs, for now" waits for, which needs the platform layer.

## [WORKING] Native Code Extensions, Continued

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

Done (Claude, 2026-11-14): recorded as approved in the specification, section 3.9 ("Native modules"), and its decision register ("Modules"). Building them is "Native Modules", below, which needs three decisions of its own first; "Standard Module `std:tasks`" waits for it. Checks: the Release build, without warnings, and the full test suite, on Windows only; Linux not tried.

## Arrow Notation, Continued

What JavaScript's arrow functions have beyond "Arrow Notation", which made `(a, b) => { … }` the same as `function (a, b) { … }` (specification, section 3.5). One decision each, answerable with a letter. Added by Claude, 2026-11-14.

**Q1, an expression for a body, `(a, b) => a + b`, giving its value, as `(a, b) => { return a + b; }` does: (a) yes; (b) no, a block always.**

A1: (a)

**Q2, one parameter without parentheses, `x => { … }` (and `x => x * 2`, with Q1's (a)): (a) yes; (b) no, parentheses always.**

A2: (a)

**Q3, `async (x) => { … }`, the same as `async function (x) { … }`: (a) yes; (b) no.**

A3: (a)

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

## Multi-line Strings

WebLingo's string literals end at their line (WL1010, specification, section 3.5, "Strings"), and the language needs strings that span lines too: SQL, HTML fragments, messages and test data. The owner decided so, 2026-11-14, in a chat with Claude, while weighing how a template's `<% … %>` block ends ("`%>` inside a block's code"): if the template compiler reads a block's code as WebLingo's tokens, an unclosed multi-line string, like an unclosed `/* */` comment, runs on over the blocks and the text after it, so its diagnostic should name both where it opened and the `<%` of the block it is in, and its closing delimiter should be one that ordinary text rarely holds. Added by Claude, 2026-11-14.

**Q1, the delimiter: (a) three double quotes, `"""…"""`, as C# 11 and Python write them, with more quotes (`""""…""""`) for a string that holds three; (b) backticks, `` `…` ``, as JavaScript writes them.**

A1: (a), with alternative three single quotes `'''`.

**Q2, escapes in a multi-line string: (a) none, a raw string, `\` being a character like any other (good for regular expressions and Windows paths); (b) the same escapes as an ordinary string's.**

A2: (a). 

**Q3, indentation: the whitespace before the closing delimiter's column is removed from every line, as C# does, so that a string indented with the code around it does not keep that indentation; and the line breaks just after the opening delimiter and just before the closing one are not part of the string. Agreed?**

A3: Yes.

**Q4, line breaks inside a multi-line string are always LF (`\n`), whether the source file's lines end in CRLF, LF or CR, so that a program gives the same string on every platform. Agreed?**

A4: Yes,

**Q5, interpolation (`"Hello, {name}"`), a separate feature: (a) not now; (b) not now, but the delimiter chosen in Q1 leaves room for it (C# writes `$"""…{name}…"""`, JavaScript `` `…${name}…` ``); (c) designed together with multi-line strings.**

A5: (c), but also needed for single line strings.

## [QUESTION] Native Modules

Native modules as the specification, section 3.9 ("Native modules"), has them, approved 14 November 2026: `IWebLingoNativeModule` and `IWebLingoNativeRegistry`, a module's functions and classes; an assembly loaded, into the host's context, from a configured scheme's folder only; a module imported by its scheme or by a path within such a folder, statically and while the program runs, its exports known to the compiler; and `std:system`'s built-ins moved into a native module in the `std` scheme's folder; on both VMs. Added by Claude, 2026-11-14.

Section 3.9 leaves three decisions open that the work needs, one each, answerable with a letter.

**Q1, how a host is configured with its schemes, `native` and the others, each naming a folder: (a) a file, `weblingo.json`, in the program's folder, `{ "schemes": { "native": "C:/WebLingo/native" } }`; (b) the command line, `weblingo run --scheme native=C:/WebLingo/native script.wl`, a host under a web server having a configuration of its own; (c) both, the command line over the file.**

A1:

**Q2, the `std` scheme's folder: (a) a folder beside the runtime's program, `std/`, which a host's configuration may point elsewhere; (b) configured as any other scheme is, with no default.**

A2:

**Q3, which built-in classes become a standard module's native classes now (A2 of "Native Code Extensions, Continued"): (a) the ones the VMs implement (`Array`, `Map`, `Set`, `Task`, `Module`, `Class`, `Function`), their methods (`push`, `has`, `forget`, `setMethod`, …) native functions, those written in WebLingo (`Object`, `Error`, the runtime errors' classes) staying so; (b) all of them, `Object` and `Error` too; (c) none yet: native classes first, for native modules, and the built-in classes moved by a task of their own, measured, since the VMs reach their methods directly now.**

A3:

# END-OF-TO-DO
