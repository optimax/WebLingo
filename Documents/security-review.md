# WASP's Request Path — Security Review

**Reviewed:** 25 November 2026, at commit `7271c37`, version 0.20.0, for the task "Security Review of WASP's Request Path",
`Documents/production-readiness.md`'s recommendation 8.
**Scope:** what WASP reads from a request and what it serves, under `wasp serve`, IIS and `wasp fastcgi`: a request's path to
a file, its headers, cookies, query and form, its uploads, its session, the escaping of its pages, the permissions of its
site, its error pages, its limits and its response's headers.
**Against:** the OWASP Top 10 (2021) and the chapters of OWASP's ASVS 4.0.3 that apply to a server-side page framework:
V3 sessions, V5 validation and encoding, V7 errors, V8 data protection, V12 files and resources, V13 HTTP, V14
configuration.
**How:** by reading the hosting core, the adapters and the route table, and by a test for each finding and for each
property relied on, the tests named below. This is a review at one date, not kept current: the state of the work is in
`HANDOFF.md` and the specification.

## 1. Findings

Two findings, both fixed, each with a test that fails without its fix:

| # | Finding | OWASP | Fix |
|---|---|---|---|
| 1 | A page or a module whose extension has another case, `lib/DB.WLG` or `Page.WASP`, was served as a file as it is, its source with it, secrets among them: on Windows, `DB.WLG` is the module `import db from "lib/db.wlg";` imports | A01, A05; ASVS V12.5.1 | Never served nor a route; a page or a handler routed by `.wasp` and `.wlg` as written (`WebLingoRouteTable`) |
| 2 | `web.config`, `wasp.config` and `weblingo.config` were kept from being served only in the site's root: a folder's `web.config`, which IIS reads, and which may hold connection strings, was served | A05; ASVS V14.3.2 | Never served in any folder, whatever their names' case |

Tests: `ApplicationTests.APathWithoutARouteIs404` (`WebLingo.Hosting.Tests`), four of its cases failing without the fix.

## 2. What was checked, and holds

| Area | What holds | OWASP, ASVS | Tests |
|---|---|---|---|
| A request's path | Routes are made from the files a generation lists, and a path is looked up as it is: no `..`, however written, `%2e%2e`, `%2f`, `%5c` or a backslash, reaches a file outside them, nor a Windows name the listing has not, a trailing dot, an alternate data stream (`::$DATA`) or a short name; a file is served by the name the listing gave, never the request's. Names that start with `.`, folders too, and WLBC files are never served, nor a page or a handler by its own name | A01; V12.5 | `ApplicationTests.APathWithoutARouteIs404`, `FastCgiTests.APathThatIsNoRouteIs404` |
| Headers | A header's name is a token and its value visible ASCII, spaces and tabs, so that no line break reaches a response, `wasp`'s own FastCGI writer's included | A03; V5.3, V13.1 | `HttpContractTests.AHeaderMustBeAToken_ItsValueVisibleAscii` |
| Cookies | Each `Set-Cookie` a header of its own, `HttpOnly` and `SameSite=Lax` by default, `Secure` over HTTPS, a name or a value that cannot be a cookie's an error where it is set | A05; V3.4 | `CookieTests` |
| Query strings and forms | Decoded as UTF-8, a malformed one an error where it is read; a body 30,000,000 bytes at most by default, a larger one a 413 before its page runs; a part's headers 16 KB at most; what a form makes bounded by the request's memory | A04; V5.1, V13.1.3 | `RequestBodyTests`, `UploadTests` |
| Uploads | A file's name, as its sender gave it, never a path: `file.save(path)` takes the page's path, checked against `"files"`; a file past 64 KB kept in a temporary file of the server's, removed when its request ends, and deleted by the system if the process ends first | A01, A04; V12.1, V12.3 | `UploadTests` |
| Sessions | An id of 128 bits from the platform's cryptography; an id the site does not know never taken for a new session's, so that a client cannot fix one; `renew()` for a sign-in; its cookie `HttpOnly`, `SameSite=Lax`, `Secure` over HTTPS; a session's requests one at a time; the table's statements parameterized, its name only letters, digits and `_` | A07; V3.2, V3.3, V3.7 | `SessionTests.AValueSetIsKeptForTheSessionsNextRequest`, `SessionTests`, `DatabaseSessionTests` |
| Escaping | A page's `<%= %>` escaped for where it stands in its HTML, text, an attribute, a URL, a script or a style, raw output only by `<%- %>` | A03; V5.3 | `EscapingTests` (`WebLingo.Templates.Tests`) |
| A site's permissions | Its scripts reach only the folders, the hosts, the COM objects and the environment its `wasp.config` allows, each refusal an error of its own; a site cannot write a native module and import it | A01, A05; V12.5, V14.1 | `WaspServeTests.ASitesScriptsReachOnlyTheFoldersItsConfigurationAllows`, `NetworkTests`, `ComTests`, `EnvironmentTests` |
| Error pages | In production a 500 or a 503 shows no error, which only the host's log has; in development its errors, escaped | A05; V7.4 | `ApplicationTests`, `RequestLimitTests.InProductionALimitsErrorIsNotShown`, `FastCgiTests.AStatusAHeaderAndErrorsAreCgis` |
| Limits | A request's time, its instructions and its memory; a regular expression's match one second | A04 | `RequestLimitTests` |
| FastCGI | `wasp fastcgi` listens on 127.0.0.1 only, or on a Unix socket, so that only the machine's web server reaches it; it trusts the parameters the web server gives | A05 | `FastCgiTests` |

## 3. Recommendations, not done

Each a decision of the owner's, a task of `TO-DO.md` where it needs one:

- **Security headers.** WASP sets none of its own: no `X-Content-Type-Options: nosniff`, which keeps a browser from taking
  an uploaded file served as text for a script; no `Referrer-Policy`; no `frame-ancestors` against clickjacking. A site sets
  each with `response.headers`, a static file none ("Security Headers and Sensitive Files in WASP").
- **Sensitive files served as they are.** Any file in a site's folder is served by its path, a SQLite database, a `.sql`
  script, a `.bak`, a `.log` or an `.env` among them: the WASP User Manual keeps databases and secrets outside the site, and
  nothing refuses them there (the same task).
- **Cross-site request forgery.** `SameSite=Lax` alone: the task "Cross-Site Request Forgery Protection in WASP".
- **Logging.** A page cannot log, nor is a request's failure logged with the request: "Logging for WASP Sites".
- **Sites sharing a process** are isolated logically only: "Isolating Sites, Documented".
- **Components.** No inventory of the packages WASP ships, nor a way to report a vulnerability: "A License and a Security
  Policy".
- **`.well-known`.** A folder whose name starts with `.` is never served, so that a certificate's HTTP challenge in
  `.well-known/acme-challenge` is not: a site's web server answers it, or IIS, before WASP.
- **`wasp serve`'s mode.** Development unless `wasp.config` says otherwise, its errors shown in their pages: `wasp serve`
  is for a developer's machine, and a site served to others names `"mode": "production"`, or runs under IIS or FastCGI,
  production by default.

## 4. Not reviewed

The language's runtime and its standard modules beyond what a request reaches; IIS's, nginx's and Apache's own
configuration; Kestrel itself, its request parsing and its limits, `wasp serve`'s; denial of service beyond the limits
above, a slow client's sending and a flood of connections, which the web server in front of WASP limits; the debugger,
which serves only the developer's own machine.
