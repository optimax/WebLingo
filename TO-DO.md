# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] A Young Generation for WebLingo's Collector

"Short-Lived Objects Freed Before .NET Promotes Them" (done the same day) collects soon while little is live; where much is live, the threshold twice what is, what a program makes and drops still stays in the heap's table long enough for .NET's collector to promote it: 100,000 instances kept, then two million made and dropped, about 550 ns each, 24 of .NET's oldest collections. A young generation, the objects made since the last collection collected on their own, often, would free them in time, but it needs a write barrier: every store of a reference into an older object (a field, an array's element, a map's entry, a cell) noting that object, so that a young collection finds what the old ones refer to. WebLingo's collector is Approved as non-moving mark-and-sweep (the specification, section 4.2), which a young generation keeps, non-moving, but changes. Added by Claude, 2026-11-23.

Q1: (a) a young generation, designed with you first, its write barrier measured on the benchmark; or (b) none for now, the collector as it is, "Short-Lived Objects" having taken most of the gain?

A1: (a)

2026-11-23 (Claude): designed, its design in the specification's section 4.2, "A young generation", Proposed, and in its register, landed on `main` as a document alone (nothing built). In short: an object young until it survives a young collection, its handle kept; the young collected on their own at every 32K units made, marked from the roots and the old objects remembered; a full collection as now; every store into an old object of a value that refers to one remembering it (the write barrier), and so every store into an object through the heap, `GetArray` read-only, a change to the API native modules use. Measured first: two million `new P(i, i)` made and dropped, 136 ns each with nothing else live, 266 with 100,000 instances kept, 119 ms of .NET's pauses a run; a mark array reused in place of one made for each collection, tried, changed nothing, so that the cost is what .NET promotes.

Q2: The design, in the specification's section 4.2, "A young generation": (a) built as written; or (b) changed as you write here?

A2: (a)

Q3: Where the barrier makes a workload of the benchmark slower while the young generation makes the case above faster: (a) landed only where no workload is slower beyond the machine's noise; (b) landed where none is more than 5 percent slower; or (c) the numbers put to you first, nothing landed until you decide?

A3: (a)

2026-11-23 (Claude): built as written (A2), on the branch `claude/young-generation`, committed there, not landed (A3 (a)): where much is live, the case above about 1.7 times as fast, two million `new P(i, i)` made and dropped with 100,000 instances kept about 315 ns each before, 185 after, with 400,000 about 390 and 240, .NET's collections of its generation 1 from 48 a run to 5, of its generation 2 from 7 to 2, its pauses from about 140 ms to 25. But the benchmark's workloads against `main`'s, 24 rounds alternating, each round's order the reverse of the last's, a rank test (Mann-Whitney's) of the rounds: `closures` 3.3 percent slower (p < 0.001), `objects` 2.9 (p = 0.003), `strings` 2.2 (p = 0.007) and `imports` 1.1 (p = 0.03); `loops` 1.1 faster (p = 0.01); the rest the same. Each is within the 5 to 10 percent a workload varies from one session to the next on this machine, but beyond the noise of the rounds compared, and so, as I read A3 (a), beyond the machine's noise. The first build was 4 to 10 percent slower on four workloads; what brought them down: no young handle listed where no young collection can come, so little being live; no young collection where the last freed less than half of what was made, the young living on, as `objects` keeps what it makes; a value's kind tested before the rest of the barrier is called; the runtime reading arrays as lists again, not through `GetArray`'s interface; the barrier kept out of the VM's dispatch method (the scheduler's `Await` calls it, not the VM's `Suspend`); and `TryAddToArray` and `TrySetField` inlined, as their callers had them before the barrier grew them. What is left is a test or two on every store and allocation, and how .NET lays out the code around them. Promotion after two young collections, tried: no faster with 100,000 kept, about 10 percent slower with 400,000; so one, the faster, as the design says. Its checks, on the branch: the Release build; the full suite, 3,450 tests, passing; and every conformance and template test passing with a young collection before nearly every allocation (`YoungBudget` 16, a trial never committed). Tried on Windows only; Linux not tried.

**Q4: The young generation, as it stands on its branch: (a) landed as it is, 1 to 3 percent on those four workloads for 1.7 times where much is live; (b) kept on its branch and tuned further, landed only once no workload is slower by the rank test; or (c) dropped, its branch deleted, the specification's design kept as it is?**

A4:

## STOP

## [QUESTION] Sessions in WASP

A prerequisite of "Extensive WASP Example" (its A4): sessions built into WASP, for signing in, where a page now reads the `Cookie` header and writes `Set-Cookie` itself (the WASP User Manual, section 19). Designed with the owner first, its questions here. Added by Claude, 2026-11-23.

2026-11-23 (Claude): what is there (the WASP User Manual, sections 9 and 19): a page reads the `Cookie` header and writes `Set-Cookie` with `response.headers.add`. Each request runs in a heap of its own, so that what a session holds must be kept between requests as data, and Apache's `mod_fcgid` starts several `wasp` processes for one site, which share no memory. My guesses, to change if you say so: a session's id 128 random bits from the platform's cryptography, in a cookie `WASPSESSIONID`, `HttpOnly`, `SameSite=Lax`, `Secure` under HTTPS, until the browser closes; a session ending 20 minutes after its last request, as classic ASP's, unless `wasp.config` gives another; what it holds what JSON holds, null, Booleans, numbers, strings, arrays and maps, anything else an error where it is set; a session made, and its cookie sent, only once a page sets a value in it; and its id made anew by `session.renew()`, for a page to call as a user signs in.

Q1: Where sessions are kept: (a) in files, in a folder `wasp.config` names outside the site, `"sessions": { "folder": "../sessions" }`, as PHP keeps them, so that every process serving the site sees them; (b) in the process's memory, lost when the site reloads or its process ends, one process only; (c) in a database `wasp.config` names, through `std:db`; or (d) in the cookie itself, signed, nothing kept on the server, 4 KB at most?

A1: (b) and (c), configurable per site.

Q2: How a page reaches its session: (a) `var session = await request.session();`, read from where sessions are kept only where a page asks for it, its values read and set as a map's, written back when the request ends; or (b) `request.session`, without `await`, read before the page runs, for every request that has a session's cookie?

A2: (b)

Q3: Two requests of one session at once: (a) one at a time, the second waiting until the first ends, as PHP locks a session's file; or (b) both at once, the one that ends last writing the session, as ASP.NET Core does?

A3: (a), one at a time, the second waiting until the first ends. Besides: a page that only reads the session takes no lock, and a script may release the lock early, once it has done with the session, so that long requests do not hold the others back; a lock not released when its request ends is released then, and a lock waited for too long is a runtime error of its own.

Q4: Cookies besides sessions: (a) `request.cookies`, a map of the request's, and `response.setCookie(name, value, options)`, so that a page writes neither header itself; or (b) sessions only, other cookies as now?

A4: (a), `request.cookies`, a map of the request's cookies, and `response.setCookie(name, value, options)`, with `response.deleteCookie(name)`; secure by default, `HttpOnly`, `SameSite=Lax`, `Path=/`, and `Secure` over HTTPS, each changed by its option; setting a cookie once the response has begun to be sent an error, as for any header; and the session's own cookie set through the same means.

2026-11-23 (Claude): a session read before the page runs (A2), and its lock taken then (A3), the host must know before it runs a page whether the page only reads it. My guesses besides, to change if you say so: memory the store where `wasp.config` names none, `"sessions": { "store": "memory" }`, one process only, as the WASP User Manual will say for `mod_fcgid`; a session ending 20 minutes after its last request, `"timeout": 20`; a lock waited for 30 seconds at most, `"lockTimeout": 30`, then a `SessionLockError`, extending `Error`, a code of its own; `request.session` a session with no values for a request without its cookie, made, and its cookie sent with the response's headers, once a page sets a value, so that a page whose output has begun to be sent sets none, as for any header; `request.session.release()`, writing it back and releasing its lock, a change after it an error; `request.session.renew()`, a new id for the same values; `request.session.end()`, the session removed, as a user signs out; in a database, its values written as JSON, a lock a column of its row, with when it ends, which a request waiting for it reads again every 100 ms, as ASP.NET's SQL Server sessions do; and `response.setCookie`'s options `httpOnly`, `secure`, `sameSite`, `path`, `domain`, `maxAge` and `expires`, `deleteCookie(name, options)` taking `path` and `domain`.

**Q5: How a page says it only reads its session (A3): (a) a statement of its own, in a page's block, as `layout` is, `session readonly;`, and a `.wlg` handler's `export const session = "readonly";`; or (b) `wasp.config` naming the paths that only read, `"sessions": { "readOnly": ["/reports/*"] }`?**

A5:

**Q6: A database's table of sessions (A1's (c)): (a) made by WASP where it is missing, the configuration naming the database and the table, `"sessions": { "store": "database", "database": "main", "table": "wasp_sessions" }`; or (b) made by the site's administrator, with SQL the WASP User Manual gives for each database, a session on a site whose table is missing an error?**

A6:

2026-11-23 (Claude): marked [QUESTION] again: A5 and A6 are still empty.

## Debugging WASP Pages

Make it possible to debug WASP pages in VS Code and VS.

2026-11-23 (Claude): what is there: VS Code debugs a `.wlg` script launched on this machine, through `weblingo --debug-adapter`, with line breakpoints, stepping, the active call stack and local variables (approved 19 November 2026; the specification, section 5.7, "Local source debugging": "Debugging WASP, the stack VM, and attaching to running processes require separate work"); Visual Studio has none yet, its adapter the task "Visual Studio Debug Adapter". A page compiles to one program with its includes', layouts' and components' code, each file's positions its own (`WebLingoTemplateFiles`), so that a breakpoint can stand in any of them. My guesses, to change if you say so: `wasp --debug-adapter`, sharing `weblingo`'s adapter (`WebLingoDebugSession`); a debugger of type `wasp` in the VS Code extension, for `.wasp` files and a site's `.wlg` handlers and modules; breakpoints in a page's blocks, `<% %>` and `<%= %>`, and in its includes, layouts, components, modules and handlers, a page's text between blocks stepped over as one statement; and IIS not attached to, for now.

**Q1: What a WASP debugging session runs: (a) one page, as `wasp run page.wasp` runs it, its parameters from `launch.json`, with no request; (b) the site, as `wasp serve` serves it, a breakpoint stopping the request a browser sent; or (c) both, as two launch configurations?**

A1: (c)

**Q2: Under `wasp serve`, a request that reaches a breakpoint: (a) it alone stops, each request a thread of the debugger's, the others going on; or (b) every request stops, the site paused?**

A2: (a), it alone stops, each request a thread of the debugger's, named by its method, its path and its number, `GET /cart #12`, the others going on, its worker running them meanwhile, the stopped request waiting as at an `await`; the debugger's pause, given no thread, pausing every request, for a snapshot; a breakpoint's condition, as the Debug Adapter Protocol gives it, so that a breakpoint in shared code stops only the requests it names; and a request stopped by the debugger kept from WASP's own timeouts, `"sendTimeout"`'s among them, while it is stopped.

**Q3: Visual Studio: (a) VS Code here, Visual Studio with "Visual Studio Debug Adapter", which then debugs pages as it debugs scripts; or (b) both here, that task's extension first?**

A3: (a), VS Code here, Visual Studio with "Visual Studio Debug Adapter", which then debugs pages as it debugs scripts. The adapter's launch settings for pages kept to the Debug Adapter Protocol's own, documented, with nothing only VS Code's extension gives, so that any client launches them; and pages' debugging tried once, besides VS Code, through a client of another kind, without an extension of its own, Visual Studio's `DebugAdapterHost.Launch` or nvim-dap, so that nothing VS Code alone does is relied on.

2026-11-23 (Claude): marked [QUESTION] again: A1 to A3 are still empty.

## [IDLE] Extensive WASP Example

Find a non-trivial application, such as a public bulletin board/dicsussion forum software, written in PHP and convert it to WebLingo.

2026-11-23 (Claude): most PHP forums are under the GPL (FluxBB, PunBB, phpBB, Vanilla), and a conversion is a work derived from its original, under its licence, where this repository may become MIT's (`Documents/implementation.md`, section 10). My guesses, to change if you say so: the whole application converted, its administration included, page for page, as `Examples.wasp/example4`, on SQLite, its database outside the site's folder; its original's release downloaded into a git-ignored folder, and its notices kept with the conversion.

Q1: Which application: (a) FluxBB 1.5, about 23,000 lines of PHP, a forum of plain pages, under the GPL v2, so that its conversion is the GPL's too, and kept outside this repository until its licence is chosen; (b) Phorum 5.2, a forum of pages with templates of its own, under the Phorum License 2.0, permissive, based on the Apache License 1.1, its conversion in the repository with its notice; or (c) none, an original forum written in WASP, with what such forums have, boards, topics, posts, users and sign-in, moderation and search, the repository's own? (a) or (b) allows me to download its release.

A1: (c) Make a powerful forum software in WASP, customizable, so that it can be branded and customized for different clients.

2026-11-23 (Claude): too large to land in one piece, so it will be split into parts, defined in a design document of its own first, each a task. WASP lacks what such a forum needs (the WASP User Manual, section 19): files uploaded by a form (`multipart/form-data`), sessions, a page reading the `Cookie` header and writing `Set-Cookie` itself; and the standard library sends no mail. My guesses, to change if you say so: customized by a theme of each client's, its layouts, includes, components and CSS overriding the forum's own, file by file, its texts in a file of each language's, and its settings (name, logo, colours, which features are on) in its administration's pages; extended by modules that the forum calls at named points (a post made, a page drawn), so that a client's own code stays apart from the forum's; boards in categories, topics, posts, users, groups and their permissions, moderation, private messages, search and its administration; posts written in Markdown, rendered by its own module, escaped, no HTML of the writer's; and on the page, no script of its own needed to read it.

Q2: Where, and as what: (a) a product of its own, at the repository's root, `Forum/`, beside `WebLingo/`, named WebLingo Forum until you name it; or (b) an example, `Examples.wasp/example4`?

A2: In `examples.wasp\WebLingoForum`.

Q3: Files uploaded by a form, for avatars and attachments: (a) built into WASP first, `request.form()` reading `multipart/form-data`, a task of its own, designed with you; or (b) the forum without uploads at first?

A3: (a). Pause this task until the prerequisite is done and installed.

Q4: Signing in: (a) sessions built into WASP first, a task of its own, designed with you; or (b) the forum's own, a session's id in a cookie, signed with `std:crypto`'s HMAC, its sessions in the forum's database?

A4: (a). Pause this task until the prerequisite is done and installed.

Q5: Mail, for confirming a sign-up and resetting a password: (a) a standard module first, `std:mail`, sending by SMTP, a task of its own, designed with you; or (b) the forum without mail at first, an administrator confirming accounts and resetting passwords?

A5: (a) Pause this task until the prerequisite is done and installed.

Q6: Its databases: (a) SQLite and SQL Server, its SQL what both share; or (b) every database `std:db` reaches, SQLite, SQL Server, MySQL and PostgreSQL?

A6: (b)

2026-11-23 (Claude): waits for its prerequisites, each a task of its own below, done and installed (A3 to A5): "Uploaded Files in WASP", "Sessions in WASP" and "Mail: `std:mail`". Its folder, `Examples.wasp/WebLingoForum` (A2).

2026-11-23 (Claude): "Mail: `std:mail`" is done (`2f44df3`); it waits for "Uploaded Files in WASP" and "Sessions in WASP", each with questions of its own.

## [IDLE] Visual Studio Debug Adapter

A small Visual Studio extension (VSIX). It registers a debug engine through a .pkgdef that points at the adapter, so F5 and launch.vs.json start a .wlg script directly. This is how the Python and other DAP-based debuggers integrate with Visual Studio. It means a second extension to build and ship next to the VS Code WebLingo.vsix, because VS Code and Visual Studio VSIX files aren't interchangeable.

2026-11-23 (Claude): what is there: VS Code's extension, `Editors/VSCode`, which `Installers/Windows/package.ps1` packs into `WebLingo.vsix` itself, with no tool of Microsoft's; and no Visual Studio on this machine, only SQL Server Management Studio 22 (`vswhere`), so that nothing here can try an extension of Visual Studio's. Visual Studio runs a debugger that speaks DAP through its Debug Adapter Host, registered by a `.pkgdef` an extension installs, and starts it from `launch.vs.json` in a folder opened as a folder. My guesses, to change if you say so: `Editors/VisualStudio`, a `.pkgdef` and its `extension.vsixmanifest`, with no code of its own; the adapter the installed `weblingo.exe --debug-adapter`; Visual Studio 2022 and 2026; `.wlg` files coloured by the TextMate grammar VS Code's extension has, which Visual Studio reads too; setup offering it as it offers VS Code's; and what it debugs what VS Code's does, a script launched on this machine.

Q1: Trying it: (a) you install Visual Studio Community 2026 here, so that I build it and try F5 on a script; or (b) I build it from Visual Studio's documented contract, untried, and you try it?

A1: (a). Wait for an install.

Q2: Building it: (a) packed by a script of ours, as VS Code's `WebLingo.vsix` is, a zip of its manifest and `.pkgdef`, with no package; or (b) by the Visual Studio SDK's build tools, `Microsoft.VSSDK.BuildTools`, a NuGet package under Microsoft's own terms, downloaded for its build only?

A2: (a)

2026-11-23 (Claude): waits for Visual Studio Community 2026 installed on this machine (A1), packed then by a script of ours (A2).

# END-OF-TO-DO

## [DEFER] `weblingo` and `wasp` Published ReadyToRun

the specification, section 5.6, "Where the time goes", cause 7: start-up, 136 to 172 ms, is .NET's JIT compiling the compiler and the VMs on every run. Their publish (`WebLingo/Runtime.pubxml`, `publish.ps1`, `Installers/Windows/package.ps1`) compiled ahead of time, ReadyToRun, the start-up and the programs' times measured with it and without it, and the installers' sizes. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

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

