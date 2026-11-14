# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [DEFER] WSP — Web Server Pages

DEFER and answer the NOTE below: Change the name of the templating compiler for web pages to WSP (`.wsp` extension) —Web Server Pages, by analogy to Active Server Pages. 

NOTE: It would be nice to use WASP (Web _____ Server Pages) but we don't really want to use "Active" in this context, so propose something better. 

QUESTION: Do we support COM objects in scripts yet so that "Active" would be justified? 

2026-11-14 (Claude): deferred, as asked. The answers:

The question: no. WebLingo has no COM, and nothing in the specification plans it. A script reaches native code only through native modules (section 3.9), assemblies loaded from a configured scheme's folder, and later through a foreign-function interface to C libraries, approved but not designed yet. COM is Windows's, and the portable core may depend on no platform (section 11), so COM objects could only come from a native module written for Windows, as an adapter (section 7.14), which none is. The "Active" of Active Server Pages came from Microsoft's ActiveX and Active Scripting, both COM, so it would not be justified.

The note: proposals for WASP's A are in Q1, below. Two collisions to weigh first (a web search, 2026-11-14): `.wsp` is SharePoint's solution package (a CAB file, deployed with `Add-SPSolution`), found on the Windows servers where IIS, WebLingo's first target, runs; and Wasp is a full-stack web framework (wasp.sh), whose projects were defined in `main.wasp` files, now `main.wasp.ts`, so WASP above all would need the name check the specification asks for before a public launch (section 12.3, item 6). The rename would change the names in use, WebLingo Pages, `.wlp` and `welp` (sections 1 and 13), with `WebLingo.Pages.Cli` and the samples' pages; and `.wsp` was WebScript Pages' extension until the renaming of 14 November 2026, so that name would come back.

**Q1, the word for WASP's A, "Web _ Server Pages": (a) Application, Web Application Server Pages: what pages are part of, an application with its modules, routes and generations (section 6), "application server" being the usual term; (b) Async, Web Async Server Pages: what sets them apart from classic ASP, a page that may await anywhere, its components tasks, its output streamed (sections 3.8 and 13); (c) Assembled, Web Assembled Server Pages: pages put together from includes, layouts and components (WL-T001); (d) none: WSP, as above.**

A1:

## [IDLE] M0.13: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WL-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

2026-11-13: M0.13 now, after M0.12, templates (specification, section 9); waits for M0.12.

2026-11-14: M0.12 is done; still waits for the choice between the VMs (WL-BC002), which waits for an I/O-bound workload, the last of those "Both VMs, for now" waits for ("I/O-Bound Workload", below).

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

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

## [WORKING] Native Modules, Part 2: Native Classes

A native module's classes, `registry.Class(…)`, their constructors, methods and static members native functions, made by the heap's own methods (specification, sections 3.7, 3.9 and 9, "Native modules", part 2), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "Native Modules, Part 1: Native Functions".

2026-11-14: part 1 is done. A native class needs one decision that section 3.9 leaves open, answerable with a letter.

Q1, a native class's instance's own state, such as a database's connection, a .NET object no script may see: (a) an instance may hold one host object, opaque to scripts, which only its class's native methods reach (`heap.TryGetHostObject(instance, …)`), and which its `dispose()`, or the collector, releases; (b) only WebLingo's values, in its fields, the module keeping its host objects in a table of its own, an instance's field holding an integer that names one.

A1: (a). TryGetHostObject should check the instance’s class, not just the .NET type, so that a native method moved by extend fails cleanly.
Disposal should be idempotent and clear the slot, so later calls report “disposed” instead of reaching a freed object.

2026-11-14 (Claude): the plan, not yet begun. `registry.Class(name)` gives a builder of a constructor, methods and static methods, each a native function, a method's first argument its instance, and a handle through which the module sets, reads and releases an instance's host object, checking the instance's class against its own (A1's first point), a released one reported as disposed (its second). The compiler gets a native module's classes with its functions; a native unit's classes are made where the program begins, before the module's instance, their members built-ins in the class table; the VMs give a built-in method its instance (in `Run`'s call path, so timed against `main`'s), `new` keeping the instance as its result, and the disposal path calling a native `dispose`; the heap keeps an instance's host object, which the collector releases.

Done (Claude, 2026-11-14): `registry.Class(name)` gives a `WebLingoNativeClass` (`WebLingo.Runtime`), on which a module declares its constructor, methods and static methods, each a native function, a constructor's and a method's first argument its instance; on both VMs. Recorded as approved in the specification, section 3.9 ("Native modules"), and its decision register, A1 with both its points: the host object's checks look at whether the class's constructor made the instance, not only at the object's .NET type, so that a native method given another instance fails cleanly; and a release is idempotent, and leaves the instance disposed ("This Counter has been disposed.", WL3001). The details are Proposed in section 15.2 ("Native classes"), and `IDisposable` is in the portability inventory. Done otherwise than the plan above: the compiler makes a native class a class written in WebLingo, made with the built-in classes, whose members call the module's built-ins, so that neither VM's calls, `new` or disposal changed, and a native class is extended, given methods by `extend`, disposed by `using` and written as any class is; a call of a native method is thus two calls, which part 4's measurement will weigh. An error in a native member is reported where the member was called, by one line of each VM's failure path, measured against `main`'s, alternately, four rounds of every workload on both VMs, all within the machine's noise. Two first versions were slower, and changed before landing: three fields on every instance made `objects` 20 percent slower (a native instance's state is now in a table of the heap's); and a change to the uncaught error's return made the stack VM's `objects` and `closures` 5 to 15 percent slower (the lesson is in `Documents/implementation.md`, section 11). Guessed: an error that cannot be caught (a limit of the execution, a cancellation), raised in a native member, is reported at the program's start; a native `dispose` runs synchronously; a native class extends `Object` only; a host object still held is disposed, if it is `IDisposable`, by the collector, or when the execution ends, its failure ignored; a disposed instance's error is WL3001 until section 3.4's proposed `ResourceClosedError` has a code. Checks: the Release build, without warnings, the full test suite (2,104 tests), and the CLI on the samples, on Windows only; Linux not tried.

## [IDLE] Native Modules, Part 4: The Built-in Classes as Native Classes

`Array`, `Map`, `Set`, `Task`, `Module`, `Class` and `Function` as a standard module's native classes, their methods native functions, measured against the VMs' own on the benchmark, as A3 of "Native Modules" asks (specification, sections 3.9 and 9, "Native modules", part 4), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for parts 2 and 3.

## [IDLE] I/O-Bound Workload

A benchmark workload of I/O-bound work (specification, section 5.6), the last of those "Both VMs, for now" waits for, after which the owner chooses between the VMs (WL-BC002), and M0.13 can begin. Scripts reach I/O through the platform layer (section 7: WL-P004's filesystem, synchronous and asynchronous, on WebLingo's own operation model, WL-P007) and a standard module, such as `std:fs` (section 3.9), a native module installed with the runtime. Added by Claude, 2026-11-14.

2026-11-14: waits for "Native Modules", which standard modules are; its own contracts (the standard module's functions, and the capabilities, section 7.13, a host gives a script) will be asked then.

# END-OF-TO-DO
