# WebLingo and WASP — Production Readiness

**Assessed:** 24 November 2026, at commit `21602c3`, version 0.20.0, at the owner's request.
**Scope:** whether WebLingo and WASP can serve real sites in production, what stands in the way, and what it would take.
Each recommendation in section 4 is a task of `TO-DO.md`, named there by its title, which says which recommendation it
is. This is an assessment at one date: the state of the work is in `HANDOFF.md` and the specification, and this
document is not kept current with them.

## 1. Verdict

Not ready for production. At most, a low-traffic pilot on the owner's own Windows server under IIS, with pages the
owner writes and the owner at hand to fix what breaks. Not for public-facing sites with real users, for code written by
others, or for anyone but the owner (section 3.8).

## 2. What is ready

- **The engine.** Source compiled to bytecode that is verified before it runs; a register VM; a tracing collector of
  WebLingo's own; asynchrony on WebLingo's own scheduler; malformed source and bytecode reported, never crashing the VM.
- **Pages and sites.** Includes, layouts and components; output escaped for where it stands in the page's HTML
  (context-aware escaping, M0.15); `.wlg` route handlers; a site's generations reloaded atomically when its files
  change; a page's output streamed, a slow client waited for without blocking its worker; uploads, cookies, and
  sessions in memory or in a database table.
- **Secure defaults.** A site reaches only the folders, network hosts, COM objects and environment variables its
  `wasp.config` allows; secrets in a configuration read from the environment or a file; passwords hashed by PBKDF2
  with 600,000 iterations; cookies `HttpOnly` and `SameSite=Lax`, `Secure` over HTTPS; a session's id 128 random bits;
  a request's body 30,000,000 bytes at most by default; a regular expression's match one second at most; errors shown
  in development and hidden in production.
- **Standard library.** Files, HTTP, TCP, databases, cryptography, mail, JSON, dates and time zones, regular
  expressions, CSV, paths, URLs, compression, processes and tests.
- **Hosts.** IIS through ASP.NET Core's module, and FastCGI behind nginx and Apache httpd, each tried on Windows.
- **Tests.** 3,525 tests in nine projects, among them fuzz tests of source, bytecode and pages; CI building and
  testing on Windows and Linux on every push.
- **Tooling and documents.** Windows installers; a debugger for VS Code, for scripts, pages and sites; the WASP User
  Manual, the Language Reference, the Standard Library Reference and the specification.

## 3. What stands in the way

### 3.1 It is young, and still changing

The repository's history begins on 12 November 2026. The language, its standard modules and the configuration have
changed since with nothing kept for backward compatibility: files renamed, standard names moved behind imports,
`write` moved to `std:console`. The bytecode format is not frozen, and many decisions are Proposed. The owner keeps
every decision preliminary until the work is done (16 November 2026). A site written today may need changing next week.

### 3.2 A request's time is not limited

Each request's heap has a fixed budget, 64 million units, about 128 MB of strings (`WebLingoHeap.DefaultMaxUnits`),
which `wasp.config` cannot change. Its time has none: `wasp` sets no instruction budget (`InstructionBudget` is
unlimited by default), and the specification's open question 14, the scheduler's fairness, is unanswered. A page that
loops forever holds its worker, one of one per processor, and the other requests that worker serves, until its request
is aborted. A few such requests stop a site: a denial of service within reach of any bug, or of any input that makes a
page loop.

### 3.3 It has never been deployed in earnest

- No site has been served on Linux (the WASP User Manual, section 19), and there is no Linux installer: the task
  "Minimal WebLingo Installer: Linux" has its answers, and waited below `# END-OF-TO-DO` until this assessment.
- No test reaches a MySQL or a PostgreSQL server: `std:db`'s providers for them, and the sessions table's SQL for
  them, are untried. SQL Server's tests run only where a server answers, so not on CI.
- Nothing has measured WASP under load: requests at once, memory over hours, a reload while busy, a database's
  connections under contention.

### 3.4 Security has gaps

- No protection against cross-site request forgery, beyond the session cookie's `SameSite=Lax`.
- No security review of what WASP reads from a request: headers, cookies, query strings, forms and uploads, the
  static files it serves, its error pages.
- The specification says that sites sharing one process are isolated logically, not as separate processes are
  (open question 13, section 12.3): mutually untrusted sites must not share one, and the WASP User Manual does not yet
  say so.
- No way to report a vulnerability, and no license (section 3.8).

### 3.5 Performance

Measured on 23 November 2026, after that day's optimizations (the specification, section 5.6, "After the remedies"),
each program run whole, as a user runs it: WebLingo starts in 126 to 159 ms; it is faster than CPython at `loops`, 1.1
to 4 times slower than CPython at the rest, 3 to 8 times slower than Lua, and far slower than Node.js, whose JIT makes
most of them a few milliseconds. For pages that mostly wait on a database, at low traffic, this matters little; a
site doing heavy computation per request would feel it. Start-up is a deferred task, "`weblingo` and `wasp`
Published ReadyToRun".

### 3.6 Operating a site

A page has no way to log: `std:console` is refused under `wasp`, and only `wasp`'s own output reaches IIS's log, where
a site's `web.config` enables it. There are no metrics. A site can be debugged only as the debugger serves it, not
under IIS or FastCGI.

### 3.7 CI has not been reliably green

On 24 November 2026, three of four CI runs in a row failed on Windows, each on a test that depended on timing, not on
what its commit changed: `ConsoleTextTests.CaptureFailureStopsADescendantAfterTheLauncherExits` twice, and
`DatabaseSessionTests.ALockAProcessThatEndedHeldEndsWithinAMinute` once. Both are fixed (`e104bf8`, `21602c3`); that CI
now stays green is not yet shown, and other tests may depend on timing as these did.

### 3.8 No license

No license has been chosen (`Documents/implementation.md`, section 10; the specification, section 12): the owner may
use WebLingo, but nobody else may. The specification ties the public release to a WebLingo that runs without .NET
(section 12.1).

## 4. Recommendations

In order, each a task of `TO-DO.md` of the title given.

1. **Limit a request's time, and let a site set its limits** — "Request Limits in WASP": a request stopped once it has
   run too long, and its memory budget set in `wasp.config`, each with a safe default.
2. **Find the tests that depend on timing** — "Flaky Tests Found Under Load": the full suite run many times on a loaded
   machine, each test that fails fixed, until CI stays green.
3. **Install WebLingo on Linux** — "Minimal WebLingo Installer: Linux", the existing task, moved above
   `# END-OF-TO-DO`.
4. **Serve a site on Linux** — "A WASP Site Served on Linux": an example site behind nginx and Apache through
   `wasp fastcgi`, on Linux.
5. **Try MySQL and PostgreSQL** — "MySQL and PostgreSQL Tried": `std:db`'s tests, and sessions in a database, run on
   a server of each.
6. **Measure WASP under load** — "Load and Soak Tests for WASP": requests at once, memory over hours, reloads while
   busy, its numbers recorded.
7. **Run a pilot** — "WASP Pilot: the Forum in Real Use": the forum example, once built ("Extensive WASP Example"),
   served to real users, every failure a task.
8. **Review WASP's security** — "Security Review of WASP's Request Path": what WASP reads from a request and what it
   serves, its findings recorded and each fix a task.
9. **Protect against cross-site request forgery** — "Cross-Site Request Forgery Protection in WASP".
10. **Let a site log** — "Logging for WASP Sites".
11. **Say how to isolate sites** — "Isolating Sites, Documented": the WASP User Manual saying what a site's
    permissions protect and what they do not, and that untrusted sites each need a process of their own.
12. **Settle what a 1.0 freezes** — "A 1.0 Scope and a Compatibility Policy": deferred until the owner says the work
    is done, the owner's word of 16 November 2026 being that nothing is frozen until then.
13. **Choose a license and a way to report vulnerabilities** — "A License and a Security Policy".

## 5. Where it can be used now

A pilot for the owner: internal tools or the forum example, under IIS on the owner's own Windows server, at low
traffic, its pages written by the owner, its database SQLite or SQL Server, restarted by hand if a page loops. Its
users should be few and trusted until recommendations 1, 8 and 9 are done.

## 6. How this was assessed

From the source tree at `21602c3`, the specification, the WASP User Manual, the references, `HANDOFF.md`, `TO-DO.md`,
the benchmark's recorded numbers, and CI's runs on GitHub. Not done for it: a load test, a security review, a
deployment on Linux, or a database other than SQLite and SQL Server; those are recommendations above.
