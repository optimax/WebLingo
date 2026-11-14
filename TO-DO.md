# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] M0.13: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WL-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

2026-11-13: M0.13 now, after M0.12, templates (specification, section 9); waits for M0.12.

2026-11-14: M0.12 is done; still waits for the choice between the VMs (WL-BC002), which waits for an I/O-bound workload, the last of those "Both VMs, for now" waits for ("I/O-Bound Workload", below).

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

## [WORKING] Multi-line Strings

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

Done (Claude, 2026-11-14): `"""…"""` and `'''…'''` strings, raw, on one line or over several, without the closing delimiter's indentation, their line breaks LF; and interpolation, `$"Hello, {name}!"` and `$$"""{ "n": {{n}} }"""`, any expression, `null` as nothing, a brace doubled or escaped in a single-line one, on both VMs, and in pages' blocks, where a multi-line string never closed also names its block's `<%`. Recorded as approved in the specification, section 3.5 ("Strings"), and its decision register, the details as Proposed in section 15.2 ("Multi-line and interpolated strings"), with WL1011 and WL1012 (section 8.4). The parser reads an interpolated string into the syntax of `+` and a hidden class's `of(value)`, written in WebLingo, which gives `""` for `null` and else `value.toString()`, so that nothing after the parser is new. Guessed: `"""a"""` on one line is a raw string too, as C# has it; an interpolation in a multi-line string stays on one line; interpolations nest at most 32 deep. A11's formats need their language decided: "Interpolation Formats", below; until then a format is WL1001. Checks: the Release build, without warnings, the full test suite, and the CLI on the samples and a script of such strings, on Windows only; Linux not tried.

## [QUESTION] Interpolation Formats

A format after an interpolation's colon, `{price:0.00}`, as A11 of "Multi-line Strings" has it (specification, section 3.5, "Strings"); until it is built, a format is WL1001. Its language needs a decision, and alignment, which C# writes beside it, one; each answerable with a letter. Added by Claude, 2026-11-14.

**Q1, the format's language: (a) .NET's numeric format strings, standard and custom (`N2`, `0.00`, `X4`, `E3`), with the invariant culture, for integers and numbers, a format given any other value being WL3001; (b) a small language of WebLingo's own, which the specification lists, so that another toolchain (Native C#) need not copy .NET's; (c) (a) now, and the subset WebLingo keeps written into the specification as it is used.**

A1:

**Q2, alignment, as C# writes it, `{name,10}` and `{name,-10}`, the value padded with spaces to that width, on the left or the right: (a) yes; (b) no, not now.**

A2:


## [IDLE] Native Modules

Native modules as the specification, section 3.9 ("Native modules"), has them, approved 14 November 2026: `IWebLingoNativeModule` and `IWebLingoNativeRegistry`, a module's functions and classes; an assembly loaded, into the host's context, from a configured scheme's folder only; a module imported by its scheme or by a path within such a folder, statically and while the program runs, its exports known to the compiler; and `std:system`'s built-ins moved into a native module in the `std` scheme's folder; on both VMs. Added by Claude, 2026-11-14.

Section 3.9 leaves three decisions open that the work needs, one each, answerable with a letter.

Q1, how a host is configured with its schemes, `native` and the others, each naming a folder: (a) a file, `weblingo.json`, in the program's folder, `{ "schemes": { "native": "C:/WebLingo/native" } }`; (b) the command line, `weblingo run --scheme native=C:/WebLingo/native script.wl`, a host under a web server having a configuration of its own; (c) both, the command line over the file.

A1: (c)

Q2, the `std` scheme's folder: (a) a folder beside the runtime's program, `std/`, which a host's configuration may point elsewhere; (b) configured as any other scheme is, with no default.

A2: (a)

Q3, which built-in classes become a standard module's native classes now (A2 of "Native Code Extensions, Continued"): (a) the ones the VMs implement (`Array`, `Map`, `Set`, `Task`, `Module`, `Class`, `Function`), their methods (`push`, `has`, `forget`, `setMethod`, …) native functions, those written in WebLingo (`Object`, `Error`, the runtime errors' classes) staying so; (b) all of them, `Object` and `Error` too; (c) none yet: native classes first, for native modules, and the built-in classes moved by a task of their own, measured, since the VMs reach their methods directly now.

A3: (a) but - like (c) says - measure the impact on performance. 

2026-11-14 (Claude): the answers recorded as approved in the specification, section 3.9 ("Native modules"), and its decision register; split into the parts section 9 defines ("Native modules"), each a task below. Waits for them.

## [QUESTION] Native Modules, Part 2: Native Classes

A native module's classes, `registry.Class(…)`, their constructors, methods and static members native functions, made by the heap's own methods (specification, sections 3.7, 3.9 and 9, "Native modules", part 2), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "Native Modules, Part 1: Native Functions".

2026-11-14: part 1 is done. A native class needs one decision that section 3.9 leaves open, answerable with a letter.

**Q1, a native class's instance's own state, such as a database's connection, a .NET object no script may see: (a) an instance may hold one host object, opaque to scripts, which only its class's native methods reach (`heap.TryGetHostObject(instance, …)`), and which its `dispose()`, or the collector, releases; (b) only WebLingo's values, in its fields, the module keeping its host objects in a table of its own, an instance's field holding an integer that names one.**

A1:

## [IDLE] Native Modules, Part 4: The Built-in Classes as Native Classes

`Array`, `Map`, `Set`, `Task`, `Module`, `Class` and `Function` as a standard module's native classes, their methods native functions, measured against the VMs' own on the benchmark, as A3 of "Native Modules" asks (specification, sections 3.9 and 9, "Native modules", part 4), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for parts 2 and 3.

## [IDLE] I/O-Bound Workload

A benchmark workload of I/O-bound work (specification, section 5.6), the last of those "Both VMs, for now" waits for, after which the owner chooses between the VMs (WL-BC002), and M0.13 can begin. Scripts reach I/O through the platform layer (section 7: WL-P004's filesystem, synchronous and asynchronous, on WebLingo's own operation model, WL-P007) and a standard module, such as `std:fs` (section 3.9), a native module installed with the runtime. Added by Claude, 2026-11-14.

2026-11-14: waits for "Native Modules", which standard modules are; its own contracts (the standard module's functions, and the capabilities, section 7.13, a host gives a script) will be asked then.

# END-OF-TO-DO
