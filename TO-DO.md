# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] M0.14: Hosting

Web applications served through the ASP.NET Core adapter, for IIS first (specification, section 6, WL-H009), their `.wasp` pages and `.wl` route handlers compiled and cached in memory (section 5.1), their generations reloaded atomically (WL-T007), and served locally from the command line (section 8.4); the owner's choice of 16 November 2026 ("After M0.13: The Next Milestone"). In seven parts, which the specification defines (section 9, "M0.14 parts"), each a task below. Added by Claude, 2026-11-16.

The specification leaves these open; each is one decision, answerable with a letter. Guessed meanwhile, and recorded as Proposed when built: a host runs pages on the register VM, as `wasp` does; an application's files are watched through the platform (section 7.4), a burst of changes making one new generation; a request's execution has the instruction and resource budgets `weblingo.json` gives, none by default.

Q1, how a page or a handler sees its request and its response: (a) as `request` and `response`, which every page and `.wl` handler has, as classic ASP's `Request` and `Response` were, each an instance of a native class (`request.query["id"]`, `request.headers["Accept"]`, `response.status = 404;`, `response.headers["Content-Type"] = "text/plain";`); (b) imported from a standard module, `import { request, response } from "std:http";`; (c) given to a page as values its parameters take, `param request;`.

A1: (c)

Q2, what a `.wl` route handler is: (a) a script whose top level runs for each request, writing the response's body with `write`, as a page does; (b) a module exporting a function for each HTTP method it answers, `export async function get(request, response) { … }`, `post`, and so on, any other method a 405; (c) a module exporting one function, `handle(request, response)`, for every method.

A2: (b)

Q3, how a request's path finds its file, under the application's root: (a) by the file's path without its extension: `/products` is `products.wasp` or `products.wl` (both being an error), `/` and a folder's path its `index.wasp` or `index.wl`; any other file of the root served as it is (CSS, images), but never a `.wl` that is not a handler, a `.wlbc` or `weblingo.json`; (b) as (a), but with the extension in the path, `/products.wasp`, as classic ASP's pages had `.asp`; (c) only by the routes `weblingo.json` lists, `"routes": { "/products/{id}": "products.wasp" }`, each `{name}` given to the page as a parameter.

A3: (a)

Q4, which command serves an application locally, and what IIS's ASP.NET Core Module runs: (a) `wasp serve [--root site] [--port 8080]`, WASP's command line, which the site's `web.config` names for IIS too; (b) `weblingo serve`, as section 8.4 lists, `web.config` naming it for IIS.

A4: (a), in the chat, 2026-11-16: "weblingo is strictly the script interpreter/runner. wasp is the web handler". Recorded in the specification, sections 1.1, 8.4 and 9.

Q5, what a runtime error in a request gives: (a) a 500, its body a page showing the error (its file, line, column, code and message) when the application runs in development, and a page without them otherwise, `weblingo.json`'s `"environment"` saying which, development for the local command and production under IIS by default; (b) always a 500 page without the error, which only the host's log has.

A5: (a), but call it `weblingo.config` not json.

Q6, where the host's own code is, the runtime, its applications, their generations, routes and requests (sections 6.3 to 6.6), which every adapter uses: (a) a new portable project, `WebLingo.Hosting`, composing the compiler, the templates and the VMs, the ASP.NET Core adapter only translating HTTP to and from WLHI; (b) in `WebLingo.Hosting.Abstractions`, which then references the compiler and the VMs; (c) in the ASP.NET Core adapter, until a second adapter needs it.

A6: (a)

2026-11-16 (Claude): the answers recorded as approved in the specification, sections 1.1 and 6.12, A5's `weblingo.config` as the configuration's name, for a script's folder as for an application's root, in place of `weblingo.json`, its content JSON as before (a task of its own, below, "The Configuration File, `weblingo.config`"). Waits for its parts.

## [WORKING] The Configuration File, `weblingo.config`

A5 of "M0.14: Hosting": the configuration that `weblingo` and `wasp` read in a script's or a page's folder, its schemes today (specification, section 3.9) and an application's settings from M0.14.2 on, is `weblingo.config`, in place of `weblingo.json`, with nothing kept for `weblingo.json`. Guessed: its content stays JSON. Added by Claude, 2026-11-16.

2026-11-16 (Claude): done: `WebLingoSchemes.ConfigurationFile` is `weblingo.config`, which `weblingo` and `wasp` read as they read `weblingo.json` before, a `weblingo.json` no longer read; recorded in the specification (sections 1.1, 3.9, 3.10, 6.9, 9 and 15.2), `Documents/implementation.md` (the names not to change without asking among them) and the README. Tests: the native modules' test of the configuration, a `weblingo.json` now ignored. Checks: the Release build, without warnings, the full test suite (2,898 tests), the CLI on the samples and `wasp` on `samples/SimpleWebsite`, on Windows only; Linux not tried.

## [IDLE] M0.14.2: The Host's Core

The runtime, its applications and their generations, an application's pages and handlers compiled and cached in memory, a request's path found among them, run on a worker, its output written to the response, and a runtime error's response (specification, sections 6.3 to 6.6; section 9, "M0.14 parts", part 2). Added by Claude, 2026-11-16.

2026-11-16: waits for "The Configuration File, `weblingo.config`".

## [IDLE] M0.14.3: The Request and the Response in Scripts

What a page and a `.wl` route handler see of their request and response, and what a handler is (specification, section 9, "M0.14 parts", part 3). Added by Claude, 2026-11-16.

2026-11-16: waits for M0.14.2.

## [IDLE] M0.14.4: The ASP.NET Core Adapter

ASP.NET Core's requests translated to WLHI's, an application's other files served as they are, and IIS through the ASP.NET Core Module (specification, section 6.1, WL-H009; section 9, "M0.14 parts", part 4). Added by Claude, 2026-11-16.

2026-11-16: waits for M0.14.2.

## [IDLE] M0.14.5: Serving an Application Locally

An application served on Kestrel from the command line (specification, section 8.4; section 9, "M0.14 parts", part 5). Added by Claude, 2026-11-16.

2026-11-16: waits for M0.14.4.

## [IDLE] M0.14.6: Reloading

An application's files watched, a new generation compiled when one changes, and published atomically once healthy, the old one drained (specification, section 6.5, WL-T007; section 9, "M0.14 parts", part 6). Added by Claude, 2026-11-16.

2026-11-16: waits for M0.14.2.

## [IDLE] M0.14.7: Streaming

A response's body written as the page runs, with backpressure, and a request's body read as it arrives, neither buffered whole (specification, sections 6.7 and WL-T008; section 9, "M0.14 parts", part 7). Added by Claude, 2026-11-16.

2026-11-16: waits for M0.14.4.

# END-OF-TO-DO

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.
