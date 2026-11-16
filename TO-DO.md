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

## [WORKING] M0.14.3: The Request and the Response in Scripts

What a page and a `.wl` route handler see of their request and response, and what a handler is (specification, section 9, "M0.14 parts", part 3). Added by Claude, 2026-11-16.

2026-11-16: M0.14.2 is done. A1 has a page's request and response given as values its parameters take, `param request;`; a script's parameters are given values as it starts, before its classes are made, and only strings (`WebLingoExecutionContext.Parameters`), and a map's entries are read by indexing, `m["key"]`, not as members (specification, section 15.2, "Members and indexing"). One decision, answerable with a letter:

Q1, what a page's `request` and `response` are, and a handler's: (a) instances of two built-in classes, `Request` and `Response`, read and changed through their members, as A1's examples have them: `request.method`, `request.query["id"]`, `request.headers["Accept"]`, `response.status = 404;`, `response.headers["Content-Type"] = "text/plain";`, a wrong status or header an error where it is set; this needs the compiler to give `param request;` its value when the statement runs, once the program's classes are made, a change to the compiler and the runtime, not to the VMs; (b) maps, which a host can give a page as it starts, with no change to the compiler: `request["method"]`, `request["query"]["id"]`, `response["status"] = 404;`, `response["headers"]["Content-Type"] = "text/plain";`, the response's status and headers read once the page has run, a wrong one then a 500 that names no line.

A1: (a)

2026-11-16 (Claude): built (specification, sections 6.12 and 15.2, "The request and the response in scripts"), A1 recorded as approved in sections 1.1 and 6.12. Guessed, and recorded as Proposed: a request's members `method`, `path`, `scheme`, `host`, `query`, a map, decoded, a name given twice having its last value, as PHP's `$_GET` has, and `headers`; a response's `status` and `headers`; the headers' class, `Headers`, read and set by indexing, a missing header WL3007, as a map's key is, `?[]` giving `null`, a value a string, with `has`, `add` and `remove`; the three classes no names a script can write; a handler's `HEAD` calling `get` when it has no `head`, another method a 405 whose `Allow` lists the handler's, and a `.wl` exporting no method's function a 404; a handler's body what it writes, as a page's; and a 404, 405 or 500 dropping the headers a page set. Not built: reading a request's body, which M0.14.7 streams. The compiler's part is tried on both VMs; the host runs the register VM. Tried on Windows only: the tests, and `wasp serve` on Kestrel serving a page and a handler; not under IIS, nor on Linux.

## M0.14.7: Streaming

A response's body written as the page runs, with backpressure, and a request's body read as it arrives, neither buffered whole (specification, sections 6.7 and WL-T008; section 9, "M0.14 parts", part 7). Added by Claude, 2026-11-16.

2026-11-16 (Claude): M0.14.4 is done. Today a page's output is kept until the page has run, then sent whole, so that a runtime error is still a 500; and a VM runs a page on its worker to its end, a write never waiting. Streaming changes both. Two decisions, each answerable with a letter:

**Q1, when a page's output is sent, and so what a runtime error after it gives: (a) as the page writes it, in pieces of about 16 KB, the status and headers sent with the first, so that a runtime error after it ends the response there, the connection closed so that the client sees it unfinished, the error in the host's log, and, in development, at the page's end too; (b) kept until it reaches about 64 KB, then sent as it is written, so that most pages, smaller, are still a 500 on an error, and a larger one ends as in (a); (c) kept whole, as now, unless the page sends what it has written with `response.flush()`, after which an error ends it as in (a).**

A1: (b)

**Q2, what a page does while a slow client has not yet taken what it wrote: (a) its worker waits for the client, as a file's write waits for the disk, for at most a time the configuration gives (30 seconds by default), the request then cancelled; (b) the page waits as at an `await`, its worker running other requests' tasks meanwhile, a change to both VMs, template output becoming an asynchronous operation (WL-T008).**

A2: (b) but with a timeout.

2026-11-16: M0.14.4 is done; waits for the answers.

# END-OF-TO-DO

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.
