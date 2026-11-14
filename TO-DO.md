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

## Native Modules, Part 2: Native Classes

A native module's classes, `registry.Class(…)`, their constructors, methods and static members native functions, made by the heap's own methods (specification, sections 3.7, 3.9 and 9, "Native modules", part 2), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "Native Modules, Part 1: Native Functions".

2026-11-14: part 1 is done. A native class needs one decision that section 3.9 leaves open, answerable with a letter.

**Q1, a native class's instance's own state, such as a database's connection, a .NET object no script may see: (a) an instance may hold one host object, opaque to scripts, which only its class's native methods reach (`heap.TryGetHostObject(instance, …)`), and which its `dispose()`, or the collector, releases; (b) only WebLingo's values, in its fields, the module keeping its host objects in a table of its own, an instance's field holding an integer that names one.**

A1: (a). TryGetHostObject should check the instance’s class, not just the .NET type, so that a native method moved by extend fails cleanly.
Disposal should be idempotent and clear the slot, so later calls report “disposed” instead of reaching a freed object.

2026-11-14 (Claude): the plan, not yet begun. `registry.Class(name)` gives a builder of a constructor, methods and static methods, each a native function, a method's first argument its instance, and a handle through which the module sets, reads and releases an instance's host object, checking the instance's class against its own (A1's first point), a released one reported as disposed (its second). The compiler gets a native module's classes with its functions; a native unit's classes are made where the program begins, before the module's instance, their members built-ins in the class table; the VMs give a built-in method its instance (in `Run`'s call path, so timed against `main`'s), `new` keeping the instance as its result, and the disposal path calling a native `dispose`; the heap keeps an instance's host object, which the collector releases.

## [IDLE] Native Modules, Part 4: The Built-in Classes as Native Classes

`Array`, `Map`, `Set`, `Task`, `Module`, `Class` and `Function` as a standard module's native classes, their methods native functions, measured against the VMs' own on the benchmark, as A3 of "Native Modules" asks (specification, sections 3.9 and 9, "Native modules", part 4), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for parts 2 and 3.

## [IDLE] I/O-Bound Workload

A benchmark workload of I/O-bound work (specification, section 5.6), the last of those "Both VMs, for now" waits for, after which the owner chooses between the VMs (WL-BC002), and M0.13 can begin. Scripts reach I/O through the platform layer (section 7: WL-P004's filesystem, synchronous and asynchronous, on WebLingo's own operation model, WL-P007) and a standard module, such as `std:fs` (section 3.9), a native module installed with the runtime. Added by Claude, 2026-11-14.

2026-11-14: waits for "Native Modules", which standard modules are; its own contracts (the standard module's functions, and the capabilities, section 7.13, a host gives a script) will be asked then.

# END-OF-TO-DO
