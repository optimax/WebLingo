# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] M0.13: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WL-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

2026-11-13: M0.13 now, after M0.12, templates (specification, section 9); waits for M0.12.

2026-11-14: M0.12 is done; still waits for the choice between the VMs (WL-BC002), which waits for an I/O-bound workload, the last of those "Both VMs, for now" waits for ("I/O-Bound Workload", below).

2026-11-15: the I/O-bound workload is done; waits for the owner's choice between the VMs ("The Choice Between the VMs (WL-BC002)", below).

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

## [QUESTION] Native Modules, Part 4: The Built-in Classes as Native Classes

`Array`, `Map`, `Set`, `Task`, `Module`, `Class` and `Function` as a standard module's native classes, their methods native functions, measured against the VMs' own on the benchmark, as A3 of "Native Modules" asks (specification, sections 3.9 and 9, "Native modules", part 4), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for parts 2 and 3.

2026-11-14: parts 2 and 3 are done. Native classes' members call their built-ins through members written in WebLingo, two calls for one ("Native Modules, Part 2: Native Classes", in `DONE.md`), so the built-in classes' methods, which the VMs reach directly now, will need a direct call in the VMs to be measured fairly.

2026-11-14 (Claude): part 2's native classes call their built-ins through members written in WebLingo, two calls for one, while the VMs call the built-in classes' methods directly now (`a.push(x)` is one step of `WebLingoMembers`, a runtime class). A3 of "Native Modules" asks that the move be measured; four decisions come first, each answerable with a letter.

Q1, the standard module that holds them: (a) `std:system`, which every module imports implicitly, so that their names stay in scope everywhere, as now; (b) a module of their own, `std:core`, imported implicitly as `std:system` is.

A1: (b)

Q2, how the VMs call a built-in class's native method, `a.push(x)`: (a) directly, the class's method being the native function, which the VM gives the instance first, a change to `Run`'s calls, measured; (b) through a method written in WebLingo that calls it, as part 2's native classes do, `Run` unchanged, each native method two calls; (c) both, measured on the benchmark, the owner choosing on the numbers.

A2: (c)

Q3, if the native methods measure slower than the VMs' own: (a) they land all the same, the numbers recorded, A3's (a) having chosen them; (b) the VMs keep their own, the native methods landing only when as fast, within the benchmark's noise; (c) nothing lands until the owner has seen the numbers.

A3: (c)

Q4, the members that are not methods, `length`, indexing (`a[i]`) and `foreach`: (a) they stay the VMs' own, only methods becoming native; (b) native too, native classes getting properties and indexers, each getter and setter a native function.

A4: (b), `length` should look like a property to the scripts. 

2026-11-15 (Claude): the plan. `std:core`, a native module installed as `std/core.dll`, as `std:tasks` is, which the compiler loads for every program, as it loads `std:system`; its classes are the classes of values (`WebLingoNativeClass.OfValues(kind)`), their methods run on values of their kind, not on instances a constructor marked. Native classes get properties (`DeclareProperty`) and an indexer (`DeclareIndexer`), each getter and setter a native function, `length` a property. Built on two branches, neither landing until the owner has seen the numbers (A3): `claude/native-builtin-classes`, the members called through methods written in WebLingo, as part 2's are (A2's (b)); and `claude/native-builtin-direct`, on it, the VMs calling a native member's built-in directly, given the instance first, without the method's frame (A2's (a)). Measured on the benchmark, every workload on both VMs, against `main`, the three run alternately. Guessed: the measurement covers `Array`, `Map`, `Set` and `Task`, whose members the benchmark's workloads use; `Module`, `Class` and `Function` follow the mode chosen, their members (`Module.import`, `setMethod`) rare enough that no workload would show them; and `foreach` stays the VMs' own for now, the language having no way yet for a class to be visited by it, which a question will ask.

2026-11-15 (Claude): built on two branches, neither landed, as A3 asks: `claude/native-builtin-classes`, A2's (b), `std:core`'s members called through methods written in WebLingo; and `claude/native-builtin-direct`, on it, A2's (a), the VMs calling a native member's built-in directly, given the instance first, without the method's frame. `std:core` (`std/core.dll`) has `Array`, `Map`, `Set` and `Task`, their methods, `length` (a property) and their indexers native functions; native classes got properties and an indexer. Measured against `main`, the three run alternately, three rounds of every workload on both VMs, each the middle of its three medians (recorded in the specification, section 5.6, "The built-in classes as native classes"):

| Workload | VM | `main` | (b) through methods | (a) direct |
|---|---|---:|---:|---:|
| collections | register | 4.5 ms | 12.6 ms (2.80 times) | 10.2 ms (2.27 times) |
| collections | stack | 5.1 ms | 13.7 ms (2.69 times) | 11.7 ms (2.29 times) |
| the seven others | both | | within the machine's noise | within the machine's noise |

Where the time goes, both ways: each call of a native member copies its arguments into an array of its own, as every built-in's call does (7 of the 8.1 MB `collections` allocates, against `main`'s 1.06 MB); finds the member by its name in its class, where the VMs' own find it by a number; and passes through two delegates to the runtime's own member. (b) runs the method's frame besides: 1.1 million instructions for `main`'s 605,000. Found on the way, and fixed on both branches: three changes that made the stack VM's `loops` workload, which calls no built-in member, 7 to 19 percent slower, .NET inlining the index wrappers into its dispatch method, the indexer's pending call written in it, and the direct call written in its call path; each moved out of it, `loops` is as fast as on `main` (the lesson is in `Documents/implementation.md`, section 11). What a script would see either way: a built-in method read without a call (`[1].push`) is a function bound to its value, as any class's method is, where it was WL3001; and `a.length = 2` says that the property has no setter. All 660 conformance programs give the same diagnostics, output and errors on both VMs with `std:core` as with the VMs' own members, but for that message. All 2,763 tests pass on both branches, on Windows only. Guessed: `Module`, `Class` and `Function` follow the choice, unbuilt; `foreach` stays the VMs' own (Q2).

**Q1, what lands: (a) the direct calls, (a), as they are, the built-in classes' members about 2.3 times slower where a program uses them most; (b) the direct calls, after their costs are cut (arguments passed without a copy, a member found by its number, not its name), measured again before anything lands; (c) the VMs keep their own members, `std:core` dropped, and what lands is what native modules gain, their classes' properties, indexers and direct calls; (d) nothing.**

A1:

**Q2, how a class is visited by `foreach`, which part 4's native classes and COM's collections both need: (a) a native class declares an iterator, a native function given the instance and a position, giving the next item, or entry, and the next position, as the VMs visit collections now; (b) the language gets a protocol of its own, a class's `iterate()` giving an iterator whose `next()` gives each item, which classes written in WebLingo implement too.**

A2:


## [QUESTION] COM Module

Design and propose a native extension for Windows that would allow the scripts to use COM objects, like old ASP did. Do not code it yet.

2026-11-15 (Claude): designed, not coded, in the specification, section 3.10 ("COM objects on Windows"), all of it Proposed: a native module that only Windows hosts install; `com.create("ADODB.Connection")` and `com.get(moniker)`, as `Server.CreateObject` and `GetObject`, giving a native class's instances that hold the COM objects and release them when disposed; their members through `IDispatch`, `x[k]` their default member, through the indexers part 4 gives native classes; `VARIANT`s mapped to WebLingo's values, currency and decimals as numbers and dates as ISO 8601 strings until WebLingo has those kinds; a failed call a catchable `ComError` (WL3012, with `number`, `source` and `description`, as ASP's `Err`); each COM object its worker's, as COM's apartments ask; and `foreach` over a COM collection waiting for the question part 4 asks. Five decisions, each answerable with a letter.

**Q1, its name: (a) `std:com`, a standard module that only a Windows host installs; (b) a scheme of its own for Windows's native modules, `windows:com`.**

A1:

**Q2, its objects' members, known only while the program runs: (a) as a program writes any member, `conn.Open(text)`, `rs.EOF`, a native class getting a fallback for the members it does not declare (a getter, a setter and a call, each a native function); (b) through methods of `ComObject`'s own, `conn.call("Open", [text])`, `rs.get("EOF")`, `rs.set("Filter", text)`, native classes unchanged.**

A2:

**Q3, the threads COM runs on: (a) each worker that runs a script a single-threaded apartment's, with a message pump, as ASP's were, every object's calls direct; (b) workers as now, COM's objects made in an apartment of COM's own, every call to one marshalled to it, slower; (c) a thread of its own for COM in each execution, every call sent to it.**

A3:

**Q4, the objects a script may make: (a) any the process may; (b) only the ProgIDs and CLSIDs an application's configuration lists, `weblingo.json`'s `"com": { "allow": ["ADODB.Connection"] }`, none by default, since objects such as `WScript.Shell` reach the whole machine.**

A4:

**Q5, how its C# calls COM: (a) `IDispatch` through function pointers on its table of methods, by platform invoke, so that the same source compiles with Native C#; (b) .NET's own COM interop (`dynamic`, `[ComImport]`), simpler, but for .NET only.**

A5:

## [QUESTION] The Choice Between the VMs (WL-BC002)

The register VM or the stack VM (specification, sections 5.2 and 5.6; WL-BC002, Provisional), which the owner's decision of 13 November 2026 left open until property access, async suspension, template output and I/O-bound work could be measured too; all four can now, the last since `std:fs` ("I/O-Bound Workload", in `DONE.md`). M0.13, WBC, the bytecode's binary format, is the chosen VM's, and waits for it. Added by Claude, 2026-11-15.

Every workload, measured together in one run, 11 timed runs after 3 warm-ups, on Windows (the specification, section 5.6, "I/O-bound work"):

| Workload | Register | Stack | Stack ÷ register | Instructions run, register | Instructions run, stack | Code bytes, register | Code bytes, stack |
|---|---:|---:|---:|---:|---:|---:|---:|
| loops | 26.9 ms | 33.5 ms | 1.25 | 10,010,008 | 17,514,512 | 76 | 74 |
| calls | 10.4 ms | 11.4 ms | 1.10 | 2,063,676 | 2,670,639 | 74 | 48 |
| closures | 13.6 ms | 15.9 ms | 1.17 | 3,120,009 | 4,660,014 | 106 | 93 |
| strings | 10.9 ms | 12.9 ms | 1.18 | 1,920,008 | 3,100,012 | 83 | 76 |
| collections | 4.4 ms | 4.9 ms | 1.11 | 605,064 | 1,030,106 | 223 | 218 |
| objects | 98.5 ms | 102.4 ms | 1.04 | 3,200,026 | 5,000,042 | 187 | 156 |
| async | 4.0 ms | 5.0 ms | 1.25 | 701,913 | 1,403,221 | 108 | 108 |
| templates | 6.2 ms | 6.1 ms | 0.98 | 280,010 | 400,015 | 135 | 104 |
| io | 1,100.5 ms | 1,136.0 ms | 1.03 | 114,080 | 152,109 | 758 | 560 |

The register VM is faster on every workload where the VMs' own work counts, by 1.04 to 1.25 times, running 1.3 to 2.0 times fewer instructions; the stack VM's bytecode is up to 35 percent smaller, and its code generator about a fifth shorter (section 5.6, "The comparison"). Where other work dominates, writing a page's text or files, the two are as close as the machine's noise. Two decisions, each answerable with a letter.

**Q1, the VM: (a) the register VM; (b) the stack VM; (c) both still, each feature built on both, until something more is measured.**

A1:

**Q2, if one is chosen, the other: (a) removed, with its code generator, its verifier and its tests; (b) kept as it is, built on no further, its tests kept for what it has, until it is removed.**

A2:

# END-OF-TO-DO

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.
