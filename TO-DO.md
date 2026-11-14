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

## [QUESTION] Native Modules, Part 4: The Built-in Classes as Native Classes

`Array`, `Map`, `Set`, `Task`, `Module`, `Class` and `Function` as a standard module's native classes, their methods native functions, measured against the VMs' own on the benchmark, as A3 of "Native Modules" asks (specification, sections 3.9 and 9, "Native modules", part 4), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for parts 2 and 3.

2026-11-14: parts 2 and 3 are done. Native classes' members call their built-ins through members written in WebLingo, two calls for one ("Native Modules, Part 2: Native Classes", in `DONE.md`), so the built-in classes' methods, which the VMs reach directly now, will need a direct call in the VMs to be measured fairly.

2026-11-14 (Claude): part 2's native classes call their built-ins through members written in WebLingo, two calls for one, while the VMs call the built-in classes' methods directly now (`a.push(x)` is one step of `WebLingoMembers`, a runtime class). A3 of "Native Modules" asks that the move be measured; four decisions come first, each answerable with a letter.

**Q1, the standard module that holds them: (a) `std:system`, which every module imports implicitly, so that their names stay in scope everywhere, as now; (b) a module of their own, `std:core`, imported implicitly as `std:system` is.**

A1:

**Q2, how the VMs call a built-in class's native method, `a.push(x)`: (a) directly, the class's method being the native function, which the VM gives the instance first, a change to `Run`'s calls, measured; (b) through a method written in WebLingo that calls it, as part 2's native classes do, `Run` unchanged, each native method two calls; (c) both, measured on the benchmark, the owner choosing on the numbers.**

A2:

**Q3, if the native methods measure slower than the VMs' own: (a) they land all the same, the numbers recorded, A3's (a) having chosen them; (b) the VMs keep their own, the native methods landing only when as fast, within the benchmark's noise; (c) nothing lands until the owner has seen the numbers.**

A3:

**Q4, the members that are not methods, `length`, indexing (`a[i]`) and `foreach`: (a) they stay the VMs' own, only methods becoming native; (b) native too, native classes getting properties and indexers, each getter and setter a native function.**

A4:

## [QUESTION] I/O-Bound Workload

A benchmark workload of I/O-bound work (specification, section 5.6), the last of those "Both VMs, for now" waits for, after which the owner chooses between the VMs (WL-BC002), and M0.13 can begin. Scripts reach I/O through the platform layer (section 7: WL-P004's filesystem, synchronous and asynchronous, on WebLingo's own operation model, WL-P007) and a standard module, such as `std:fs` (section 3.9), a native module installed with the runtime. Added by Claude, 2026-11-14.

2026-11-14: waits for "Native Modules", which standard modules are; its own contracts (the standard module's functions, and the capabilities, section 7.13, a host gives a script) will be asked then.

2026-11-14 (Claude): native modules' functions and classes, which `std:fs` needs, are done; the platform layer has only a clock so far (`WebLingo.Platform.Abstractions`), its asynchronous operations being WebLingo's own (WL-P007, approved). `std:fs`'s contracts need five decisions, each answerable with a letter. The workload itself I would guess: a program that writes 1,000 small files and reads them back, one at a time, awaiting each, then all at once, with `Task.all`.

**Q1, `std:fs`'s first functions: (a) whole files as text: `readText(path)`, `writeText(path, text)`, `appendText(path, text)`, `exists(path)` and `remove(path)`, none for folders yet; (b) (a), and folders: `list(path)`, `makeFolder(path)` and `removeFolder(path)`; (c) (b), and files opened as handles, a native class `File` (`File.open(path, mode)`, `read`, `write` and `dispose()`), as section 3.4's example has one.**

A1:

**Q2, synchronous and asynchronous, which WL-P004 asks for both: (a) each function in both forms, `readText(path)` and `readTextAsync(path)`, a task, as .NET names them; (b) asynchronous by default, `await fs.readText(path)`, a synchronous one named `readTextSync(path)`, as Node.js names them; (c) asynchronous only, for now.**

A2:

**Q3, text's encoding: (a) UTF-8 always, a byte order mark skipped when read and none written, malformed bytes an error; (b) UTF-8 by default, another encoding named by an optional argument.**

A3:

**Q4, the files a script may reach under `weblingo` (capabilities, section 7.13): (a) any the process may, capabilities coming with hosting; (b) those below the script's folder, unless the command line grants more (`--allow-fs folder`); (c) none, unless the command line grants them, `--allow-read folder` and `--allow-write folder`, as Deno does.**

A4:

**Q5, an I/O failure (a missing file, a path not granted): (a) a runtime error of a code of its own, WL3011, of a class `IOError` extending `Error`, which a catch may catch, its message naming the path as the script gave it, never the host's own paths; (b) WL3001, as another invalid operation is.**

A5:

# END-OF-TO-DO
