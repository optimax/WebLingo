# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

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

2026-11-24 (Claude): "Uploaded Files in WASP" is done (`8da8353`); it waits for "Sessions in WASP", whose Q5 and Q6 wait for your answers, and then for both installed (A3 to A5).

2026-11-24 (Claude): "Sessions in WASP" is done (`a69a546`), its prerequisites all done; it waits for them installed on this machine (A3 to A5), as `build.bat` installs the current checkout.

2026-11-25 (Claude): its prerequisites are installed: `C:\Program Files\WebLingo` holds a build of 25 November 2026 (`WASP 0.20.0 (2026.11.25.1203)`), with uploads, sessions and `std:mail`. Taken up: its design document first, which defines its parts, each a task.

2026-11-25 (Claude): designed in `Documents/weblingo-forum.md` (`c05087f`): what it does, how it is built, what it needs of WASP, and its ten parts (section 6), each a task below. My guesses there, marked **Guess**, to change if you say so: no page needing a script; query strings for its pages, `/topic?id=42`, WASP having no route parameters; `setup.wlg` making its database and first administrator, run with `weblingo`, rather than an install page; names in lower case for the four databases; English first; hooks for a client's code; a Markdown subset of CommonMark, rendered by its own module; and a search index of its own. It waits for its parts.

NOTE: Use MarkdownEditor.Web for the markdown text box to enter/edit text.

2026-11-25 (Claude): the note is the task "WebLingo Forum: MarkdownEditor.Web in the Post Form", below, whose questions wait for your answers.

## [IDLE] WebLingo Forum: MarkdownEditor.Web in the Post Form

The owner's note on "Extensive WASP Example": "Use MarkdownEditor.Web for the markdown text box to enter/edit text." The forum's form for a post, a reply and an edit, `site/components/post-form.wasp`, has a `<textarea>` for its Markdown, and a preview the server renders. MarkdownEditor.Web, the owner's WYSIWYG Markdown editor in plain JavaScript (no framework, no build step, no dependencies, ES modules, about 490 KB of source), gives a `<markdown-editor name="body">` element that a form submits as it does a `<textarea>`, and a toolbar a page may cut down. Added by Claude, 2026-11-25.

2026-11-25 (Claude): my guesses, to change if you say so: the editor in the forms for a post, a reply and an edit, and for a signature; the preview kept, the server's rendering being what readers see; its toolbar cut down to what the forum's renderer reads.

Q1: How the forum gets the editor's files: (a) a copy in the forum's site, `site/js/markdown-editor/`, committed, made by a script of the forum's from a MarkdownEditor.Web checkout beside this repository's, `C:/PROJECTS/MarkdownEditor.Web` here, and made again when the editor changes; (b) the same copy, made by the script but ignored by git, so that each deployment makes it; or (c) from wherever the editor is published, once it is (a CDN, npm)?

A1: (a) for now.

Q2: Its licence: MarkdownEditor.Web has no `LICENSE` file, and this repository is MIT's and may become public (`Documents/implementation.md`, section 10), so a copy in it needs a licence that allows that: (a) MarkdownEditor.Web under the MIT License too, its `LICENSE` added in its own repository, a task of that repository's, its copy here carrying it, and `THIRD_PARTY_NOTICES.md` naming it where the forum ships; or (b) another licence, which you name?

A2: (a)

Q3: Markdown the editor writes that the forum's renderer does not read, pipe tables, task lists, `==underline==`, `~subscript~`, `^superscript^` and front matter: (a) the editor's toolbar cut down to what the renderer reads, the rest shown as the text it is; (b) the renderer extended to read all of it, the toolbar whole; or (c) (a) now, (b) as a task of its own later?

A3: (b) as MarkdownEditor.Web change.

Q4: A reader whose browser runs no script: (a) the page sends a `<textarea>`, which a script of the forum's replaces with the editor where scripts run, so that the form works without them, as the design's guess has it ("No page needs JavaScript"); or (b) the page sends the editor's element alone, scripts needed to write a post?

A4: (a)

2026-11-25 (Claude): taken up, and landed but for the editor's copy (`c9d2fe3`): the forms' Markdown textareas, a post's, a reply's, an edit's, a message's and a signature's, replaced by MarkdownEditor.Web's editor where scripts run and its files are in `site/js/markdown-editor`, by `site/js/forum-editor.js` (A4 (a)); `setup/copy-editor.wlg`, which copies its `src` and its `LICENSE` there from the checkout beside this repository's (A1 (a)); and the renderer reading what the editor writes, tables, task lists, `==underline==`, `~subscript~`, `^superscript^` and front matter, shown as written (A3 (b), which I read as the renderer following what MarkdownEditor.Web writes, as it changes: say if you meant another). Tried in a browser with a scratch copy of the editor. It waits for MarkdownEditor.Web's `LICENSE`, the task "A License: the MIT License", which I added to its `TO-DO.md` (A2 (a)), to commit the copy.

## [IDLE] WebLingo Forum, Part 10: Deployment

`Documents/weblingo-forum.md`, section 6, part 10: the forum's `README.md`, its deployment under IIS by the repository's `Scripts` and behind nginx and Apache through FastCGI, the WASP User Manual pointing to it, and its pages measured under load, `WebLingo.Benchmarks --load --url`. Added by Claude, 2026-11-25, a part of "Extensive WASP Example".

2026-11-25 (Claude): waits for parts 8 and 9: its deployment covers the folder of uploads, which part 8 makes and the site writes, and a client's folder over the forum's, which part 9 makes; deployed before them, it would be deployed again.

2026-11-25 (Claude): taken up, parts 8 and 9 landed (`eefc3d5`, `607e7f7`).

2026-11-25 (Claude): landed but for IIS (`075910a`): the README's "Deploying it", under IIS, behind nginx and Apache through `wasp fastcgi`, nginx's `client_max_body_size` raised for a post's files, and a client's forum over the installed one; the WASP User Manual pointing to it; `WebServerTests.NginxServesTheForumFromWaspFastCgi`, a copy of the forum behind nginx, a picture of 1.5 MB attached through it; and `WebLingo.Benchmarks --load --site forum --url`, its pages measured, in the design's section 6, part 10. Checks: the Release build of the whole solution, no warnings; `ForumTests` and `WebServerTests`, 10 passing, and `LoadTests`, 11, none skipped; on Windows only.

It waits for you to deploy it under IIS, as administrator, as the README says, after copying `setup`, `site` and `data` (its README alone) to `C:\WWW\forum`: `weblingo run setup.wlg` from `C:\WWW\forum\setup`, then, from the repository's `Scripts`, `weblingo run add-wasp-site.wlg name=wasp.forum folder=C:\WWW\forum\site host=forum.wasp.localhost writable=C:\WWW\forum\data`. Then I try it at https://forum.wasp.localhost/ and measure it there, `--site forum --url https://forum.wasp.localhost/`.

## [IDLE] A WASP Site Served on Linux

No site has been served on Linux (the WASP User Manual, section 20, "What is not there yet"). Serve `Examples.wasp/example1` on Linux behind nginx through `wasp fastcgi`, and behind Apache httpd through `mod_proxy_fcgi` and `mod_fcgid`, as the manual's section 18 configures them, WebLingo installed by the Linux installer (recommendation 3), and fix what fails; the manual's section 20 then says so. This machine has no Linux: the Windows Subsystem for Linux is not installed. Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 4.

My guesses, to change if you say so: `WebServerTests`, which runs the web servers on Windows, run on Linux too, nginx and Apache httpd from the distribution's packages.

Q1: Where: (a) both: WSL 2 with Ubuntu, which you install here (`wsl --install`, as administrator, then a restart), and a job of CI's serving the site on `ubuntu-latest` on each push, so that it stays tried; (b) WSL 2 here only; or (c) CI's job only, tried on a push?

A1: (a)

2026-11-24 (Claude): CI's half landed (`88c88ec`): `Installers/Linux/tests/serve.sh`, which CI's job `linux-installer` runs, serves `Examples.wasp/example1` from WebLingo installed by the Linux archive, through `wasp fastcgi` behind nginx and Apache httpd's `mod_proxy_fcgi`, and through `mod_fcgid`, Ubuntu's packages, my guess changed: the installed product served as a user serves it, rather than `WebServerTests` run on Linux. It waits for a push, which tries it, and for WSL 2 with Ubuntu installed here, `wsl --install` as administrator, then a restart (A1 (a)), to run the same there; the WASP User Manual's section 19 then says so.

2026-11-25 (Claude): CI's half failed on every push from `2e27309` to `b79f11e` (runs #90 to #94), the build and its tests passing on `ubuntu-latest` and `windows-latest` each time: `serve.sh`'s `ready` took nginx's answer, its 502 while `wasp fastcgi` behind it had not yet started listening, for the site being served, so that its next check, the front page, found nginx's error page. Fixed (`eef7703`): `ready` waits, up to 60 s, for a successful answer, which only `wasp` behind the web server gives. Not tried: it waits for a push, and WSL 2 with Ubuntu, for its other half, is still not installed here.

2026-11-25 (Claude): CI's half passes: pushed at your word, run #95 (`c275684`), `linux-installer`, the archive installed and `example1` served behind nginx and Apache httpd through `mod_proxy_fcgi` and `mod_fcgid` on `ubuntu-latest`, and the build and its tests on `ubuntu-latest` and `windows-latest`, all three passing. It waits for WSL 2 with Ubuntu here, its other half.

## [IDLE] Load and Soak Tests for WASP

Nothing has measured WASP under load (`Documents/production-readiness.md`, section 3.3). Measure a site's throughput and latency at 1 to 256 requests at once, on pages that compute, wait on SQLite and SQL Server, write large output, use sessions and receive uploads; its memory and handles over 8 hours; and a reload while it is busy, every request answered whole across it; served by `wasp serve`, by IIS and through FastCGI. The numbers in the specification's section 5.6; whatever fails or leaks a task of its own. Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 6.

My guesses, to change if you say so: a load generator of the solution's own, `WebLingo.Benchmarks --load`, with no package, rather than wrk or k6, which would need downloading; 8 hours for the soak, run in the background on this machine; under IIS, the site added by the repository's `Scripts`, which you run as administrator; on Linux once recommendation 4 is done.

2026-11-24 (Claude): too large to land in one piece: split into four parts, defined in the specification's section 5.6, "Under load, parts", each a task below. This task waits for them.

## [IDLE] Load and Soak Tests for WASP, Part 3: Under IIS

The specification, section 5.6, "Under load, parts", part 3: part 1's measurements of the load site under IIS. Added by Claude, 2026-11-24, a part of "Load and Soak Tests for WASP".

2026-11-24 (Claude): waits for you to add the load site, `WebLingo/benchmarks/WebLingo.Benchmarks/LoadSite`, to IIS with the repository's `Scripts`, and to install WebLingo from this checkout with `build.bat`, both as administrator; it runs once part 1 has landed.

## [IDLE] Load and Soak Tests for WASP, Part 4: On Linux

The specification, section 5.6, "Under load, parts", part 4: part 1's measurements on Linux. Added by Claude, 2026-11-24, a part of "Load and Soak Tests for WASP".

2026-11-24 (Claude): waits for "A WASP Site Served on Linux" to have a Linux here, WSL 2.

## [IDLE] WASP Pilot: the Forum in Real Use

Nothing has served real users (`Documents/production-readiness.md`, section 5). Serve the forum example, once "Extensive WASP Example" has built it, to real users for some weeks, after "Request Limits in WASP"; every failure, slow page and missing feature a task of its own. Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 7.

Q1: Where: (a) a server of yours, under IIS, reached by its users, deployed by you with the repository's `Scripts`; or (b) this machine, under IIS, used by you alone?

A1: (b)

2026-11-25 (Claude): waits for the forum, "Extensive WASP Example", built; then served under IIS on this machine, for you alone (A1 (b)), its site added by you with the repository's `Scripts`, as administrator.

## [IDLE] M0.22: Old Generations Drained

A reload's old generation of an application given a deadline: the requests and the tasks still running on it then ended, its resources released, and what it leaks found and fixed (open question 8; section 6.12, "Reloading"). Designed with you before it is built, in parts. Added by Claude, 2026-11-25, at the owner's answer, A1 of "The Next Milestone": (b), (d), (f), (a), (c), (e), in that order (the specification, section 9).

2026-11-25 (Claude): as built, an old generation lives until its last request ends, and a request ends by its time limit, 60 s by default (section 6.12, "A request's limits"), its tasks cancelled with it; what an old generation keeps after that, its compiled pages and modules, its workers' state, is not checked.

Q1: How long an old generation may live after a reload: (a) a deadline of its own, `wasp.config`'s `"limits"`' `"drain"`, 60 s by default, its requests still running then cancelled as a request past its time is; or (b) as now, until its requests end by their own limits, no deadline of its own?

A1: (a), a deadline of its own, `wasp.config`'s `"limits"`' `"drain"`, 60 s by default, or the request's own time limit where that is longer, its requests still running then cancelled as a request past its time is, `finally` and disposal run; a request stopped by the debugger not cancelled so while it is stopped; and the log saying, for each request cancelled so, that a reload ended it.

Q2: How a leak is found: (a) a test that reloads a site many times while clients ask for its pages, and checks, by weak references, that every old generation is let go; (b) the soak, `--soak`, with a reload every few seconds, the server's memory sampled; or (c) both?

A2: (c), both: the test, with the others, every run, reloading a site many times while clients ask for its pages, mid-request reloads, drained requests and failed compiles among them, and checking, by weak references, that every old generation is let go, and its executions' heaps with it; and the soak, `--soak`, run before a release and when hosting changes, a reload every few seconds, the server's memory, its .NET heap, its handles and its threads sampled, a trend that keeps rising after warming up a failure, its report giving the growth per reload.

2026-11-26 (Claude): your answers recorded in the specification's register and its section 9, "M0.22 parts" (`53a4ded`), which defines its three parts, each a task below, landed in that order. Waits for its parts.

## M0.22, Part 1: The Drain

The specification's section 9, "M0.22 parts", part 1 (A1 (a) of "M0.22: Old Generations Drained"): `wasp.config`'s `"limits"`' `"drain"`, 60 seconds by default; a request still running on an older generation once `drain` has passed since the reload, or its own time limit where that ends later, cancelled as a request past its time is, its `finally` blocks and disposals run; one the debugger has stopped not cancelled so while it is stopped; the log saying, for each, that a reload ended it. Added by Claude, 2026-11-26.

2026-11-26 (Claude): found before building it. A request's time limit, `"requestTimeout"`, cannot be turned off (a number of seconds more than 0), and it counts from the request's start, before the reload: so a request still running on an old generation always reaches its own limit before the later of the two, `drain` after the reload and its own limit, and its own limit ends it. Under A1's rule the drain would end only a request that has no time limit, which today is a request under the debugger (`wasp`'s debugger serves without one), and, while it is stopped, not that either.

**Q1: The drain's deadline: (a) as A1 says, `drain` after the reload or the request's own limit, whichever ends later, so that it bounds only what has no time limit, a debugger's requests now; (b) `drain` after the reload alone, ending a request sooner than its own limit where that comes first, so that a reload's old generation is gone within `drain`; or (c) (b), and `"requestTimeout"` able to be turned off, `null`, the drain then the bound of those requests too?**

A1: (c): `"drain"` after the reload alone, a request still running then cancelled, sooner than its own limit where that comes first, so that a reload's old generation is gone within `"drain"`; its default 60 s, or `"requestTimeout"` where that is longer, so that by default a reload ends no request its own limit would not; `"requestTimeout"` able to be turned off, `null`, for a site, and, by a declaration of its own, for a page or a handler that is meant to stay open, the drain then the bound of those requests too; a request stopped by the debugger not cancelled while it is stopped.

## M0.22, Part 2: Old Generations Let Go, Tested

The specification's section 9, "M0.22 parts", part 2 (A2 (c) of "M0.22: Old Generations Drained"): a test, with the others, every run, reloading a site many times while clients ask for its pages, mid-request reloads, drained requests and failed compiles among them, checking by weak references that every old generation is let go, its executions' heaps with it; what it finds fixed. Added by Claude, 2026-11-26.

## M0.22, Part 3: The Soak with Reloads

The specification's section 9, "M0.22 parts", part 3 (A2 (c) of "M0.22: Old Generations Drained"): `--load --soak` with a reload every few seconds, the server's memory, .NET heap, handles and threads sampled, a trend that keeps rising after warming up a failure, its report giving the growth per reload; run, its numbers in the specification. Added by Claude, 2026-11-26.

## M0.23: A Standalone HTTP Host

`WebLingo.Hosting.Standalone`: a site served without ASP.NET Core, HTTP/1.1 on .NET's sockets, written in WebLingo's own C#, as the FastCGI host is, which Native C# will need (sections 1, 6.1 and 6.13). Designed with you before it is built, in parts. Added by Claude, 2026-11-25, at the owner's answer, A1 of "The Next Milestone": (b), (d), (f), (a), (c), (e), in that order (the specification, section 9).

**Q1: What it speaks: (a) HTTP/1.1 alone, with keep-alive and chunked bodies, TLS and HTTP/2 left to a reverse proxy in front of it; (b) HTTP/1.1 and TLS, on .NET's `SslStream`, its certificate named by `wasp.config`; or (c) HTTP/1.1, TLS and HTTP/2?**

A1: (a), HTTP/1.1 alone, with keep-alive and chunked bodies, TLS and HTTP/2 left to a reverse proxy in front of it; hardened as a server facing clients is, a request with both `Content-Length` and `Transfer-Encoding`, or either malformed, refused, limits on a request's line, its headers, its body and the time it takes to arrive, each `wasp.config`'s; a proxy's `Forwarded`, or `X-Forwarded-For` and `X-Forwarded-Proto`, trusted only from the proxies `wasp.config` lists, so that a request's scheme and its client are known behind one; TLS, on `SslStream`, a task of its own, later, if asked for, with certificates renewed without a restart; and HTTP/2 not planned.

**Q2: How it is run: (a) `wasp serve --host standalone`, ASP.NET Core's staying `wasp serve`'s own until the standalone one has passed the load and soak tests; (b) a command of its own, `wasp http`; or (c) in place of ASP.NET Core's under `wasp serve` once it has passed them, IIS keeping ASP.NET Core's?**

A2: (a), then (c): `wasp serve --host standalone` while the standalone server is proved, ASP.NET Core's staying `wasp serve`'s own until the standalone one has passed the load and soak tests, both run by the same tests; then the standalone one `wasp serve`'s own, `--host aspnetcore` choosing ASP.NET Core's, and `--host standalone` kept, naming the default; IIS keeping ASP.NET Core's, IIS's running the standalone server out of process a question for later.

## M0.26: Parallel Work

Workers with heaps of their own within one program, exchanging copied messages, so that a script uses more than one processor (section 3.8, "Concurrency"; open question 7; WL-P013's explicit transfer). Designed with you before it is built, in parts. Added by Claude, 2026-11-25, at the owner's answer, A1 of "The Next Milestone": (b), (d), (f), (a), (c), (e), in that order (the specification, section 9).

**Q1: How a script works in parallel: (a) `std:workers`, `new Worker("work.wlg")`, a module run on a worker of its own, `worker.post(message)` and `await worker.receive()`, as JavaScript's workers; (b) `Task.run(function)`, a function run on another worker, what it captures copied; or (c) another way, which you write?**

A1: (c): (a), `std:workers`, `new Worker("work.wlg")`, a module run on a worker of its own, `worker.post(message)` and `await worker.receive()`, as JavaScript's workers; and besides, built on it, `await workers.run("work.wlg", "name", [arguments])`, an exported function run on a worker of a pool, its arguments copied there and its result copied back, its exception the task's, no closure copied, so that nothing looks shared that is not; a parallel `map` later; under `wasp`, the workers a site may run limited by `wasp.config`, a request's ending with it. Not (b): a closure's captures, copied, would look shared and not be.

**Q2: What crosses between workers: (a) values copied, whole: `null`, Booleans, numbers, strings, bytes, dates, and arrays, maps and sets of them, anything else an error; or (b) the same, and bytes handed over without a copy, the sender's then empty?**

A2: (a), values copied, whole: `null`, Booleans, numbers, decimals, strings, bytes, dates, and arrays, maps and sets of them, their cycles and shared references kept as they are, anything else an error naming it; an error, of `Error`'s class or one extending it, crossing as an `Error`, its class's name, its message, its code and where it was thrown kept. Not (b): bytes are immutable, and one that emptied would no longer be; immutable values, strings, bytes and dates, shared between workers without a copy, later, as an optimization nothing can see, where a message's size is measured to matter.

**Q3: Where: (a) under `weblingo` and in a site's pages, a page's workers counted against its request's limits; or (b) under `weblingo` alone, for now?**

A3: (b), under `weblingo` alone, for now, its import under `wasp` WL2015, as `std:console`'s is; the number of workers a script may run, and each one's time and memory, limited by `weblingo.config`, so that a site's own limits can later be given the same way; a site's pages' workers a task of its own, later, its limits, their lifetimes, reloads and the debugger decided there.

## The Server Collector for `wasp`

What "WebLingo Forum: Pages Under Load" found (`Documents/weblingo-forum.md`, section 6, part 10, "Under load, found"): `wasp` runs with .NET's workstation collector, a console program's own, and its requests, each making and dropping a module's worth of objects, share it, `wasp` using about 2.3 of 8 processors at 16 clients; with the server collector, `DOTNET_gcServer=1`, the same forum's front page took 385 requests a second at 4 clients in place of 183, and a board 410 at 16 in place of 135. The server collector keeps a heap for each processor, and so more memory for each process, which a server of many sites, a `wasp` each, would feel; .NET 10's server collector adapts its heaps to an application's size (DATAS), which takes much of that back, to be measured. Added by Claude, 2026-11-25.

Q1: Which collector `wasp` runs with: (a) the server collector, by default, measured under load and in memory on the load site and the forum, a site's `web.config` or environment able to choose the workstation one; (b) the workstation one, as now, the WASP User Manual saying how a busy site chooses the server one, `DOTNET_gcServer=1` in its `web.config`'s environment; or (c) a setting of `wasp.config`'s?

A1: (c)

2026-11-26 (Claude): taken up, and found: .NET chooses a process's collector when it starts, from its `runtimeconfig.json` or `DOTNET_gcServer`, before `wasp` reads `wasp.config`, and cannot change it after; the installation's `wasp.runtimeconfig.json` is every site's, since WebLingo is installed once per server.

**Q2: How `wasp.config`'s setting chooses the collector: (a) `wasp`, finding that its `wasp.config` asks for the collector it was not started with, starts itself again as a process of its own with that one, the first waiting for it, passing on its signals and ending with its exit code, under IIS, nginx and `wasp serve` alike; (b) the scripts that add a site to IIS (`Scripts/`) read it and write `DOTNET_gcServer` into the site's `web.config`'s environment, `wasp serve` and `wasp fastcgi` started again by (a)'s means; or (c) (a), and the setting's default the server collector, measured first?**

A2: none of them; the collector chosen where the process starts, as .NET chooses it: `wasp`'s default fixed in its own `wasp.runtimeconfig.json`, the server collector or the workstation one as the load and soak tests measure them, with .NET's DATAS on, under IIS, nginx and `wasp serve`, and `weblingo`'s the workstation one; another chosen by the process's environment, `DOTNET_gcServer`, which the WASP User Manual says how to set under each host, `web.config`'s `<environmentVariables>` for an application pool's, a service's `Environment=`, and a terminal's; `wasp.config`'s setting dropped, or kept only for `wasp` to warn, when it starts, that it asks for a collector the process does not run, naming the variable; no second process.

## A Topic's View Without a Write Each

What "WebLingo Forum: Pages Under Load" found: a topic's page counts each view, one write of SQLite's, which takes its one writer's lock; with WAL, the page takes 180 requests a second at 64 clients, its median 17 ms, but its slowest wait seconds, 7.1 s at the 99th percentile, for the lock (`Documents/weblingo-forum.md`, section 6, part 10). SQL Server, MySQL and PostgreSQL lock a row, not the file. Added by Claude, 2026-11-25.

Q1: How a topic's views are counted: (a) as now, a write for each view, SQLite advised against for a busy forum; (b) once for each session and topic, a view the same reader makes again not counted, fewer writes; (c) kept in the process's memory and written for many views at once, a view or two lost where the process stops; or (d) not counted, the board showing its replies alone?

A1: (b), once for each session and topic, a view the same reader makes again not counted, the session keeping the last few hundred topics it has counted, a known crawler's view not counted; a board able to turn views off, its setting the forum's own; and kept in the process's memory, written for many views at once, (c), later, once WASP gives a site memory shared between its requests, a task of its own. 


2026-11-26 (Claude): built on the branch `claude/forum-views`, not landed: a view counted only where the topic's board counts views, the request is no known crawler's (a User-Agent with `bot`, `crawler`, `spider`, `slurp`, `mediapartners` or `facebookexternalhit`), and its session has not counted the topic yet, the session remembering the last 300 it has; each board's choice a checkbox of its administration, kept in the forum's setting `boardsWithoutViews`, its topics' rows then showing no views; `tests/views-tests.wlg`, 5 tests. Found: the topic page says `session readonly;`, so that a browser's requests do not wait for one another, and a page that only reads its session cannot change it, WL3001; and, under IIS, where the forum's sessions are its database's, a request that may change its session takes and lets go of the session's lock by two writes, so that dropping `session readonly;` would make each view two writes, more than the one it makes now.

**Q2: Where a session's counted topics are kept: (a) in the session, `session readonly;` dropped from the topic page, its views then a session's requests one at a time, and two writes each under IIS's database sessions; (b) in a cookie of the forum's own, the last topics counted, the page's session still read only; or (c) in the session, changed only where the topic is new to it, WASP first letting a page that reads its session take its lock only when it changes it, a task of WASP's own before this one?**

A2: (b), in a cookie of the forum's own, the last topics counted, a few hundred, the oldest dropped first, kept under 4 KB, its path the topic pages', a session cookie, with WASP's defaults, `HttpOnly` and `SameSite=Lax`, unsigned, a changed or cleared one only counting a view again or not at all; the topic page's session still read only.

## M0.24: Fair Workers

A request that computes for long made to yield its worker to the other requests on it, at safe points, after a quantum of instructions, so that one slow page does not hold up the others; and the blocking work off the workers bounded (open question 14; section 6.10). Designed with you before it is built, in parts, measured under load (`WebLingo.Benchmarks --load`). Added by Claude, 2026-11-25, at the owner's answer, A1 of "The Next Milestone": (b), (d), (f), (a), (c), (e), in that order (the specification, section 9).

**Q1: When a long request lets the others on its worker run: (a) after a quantum of instructions, 100,000 by default, at a call or a jump back, its worker running the other requests' ready work before it goes on; (b) after a slice of time, a few milliseconds by the host's clock; or (c) only at `await`, as now, the WASP User Manual telling a long computation to `await delay(0)`?**

A1: (a), after a quantum of instructions, 100,000 by default, `wasp.config`'s `"limits"`' `"quantum"`, at a call or a jump back, its worker running the other requests' ready work before it goes on, never another task of its own execution, so that a request's tasks still switch only at `await`; checked where the instruction budget is, at no cost while it is not due; its default measured by the load test, a slow page beside fast ones; a native function's work, which no quantum interrupts, left as it is.

**Q2: The work off the workers, files, databases and the like: (a) its threads bounded, four for each processor by default, work beyond them queued; or (b) as now, a thread made where none is free?**

A2: (a)

## M0.25: `weblingo` and `wasp` Published AOT-Compiled

the specification, section 5.6, "Where the time goes", cause 7: start-up, 136 to 172 ms, is .NET's JIT compiling the compiler and the VMs on every run. Their publish (`WebLingo/Runtime.pubxml`, `publish.ps1`, `Installers/Windows/package.ps1`) compiled ahead of time, the start-up and the programs' times measured with it and without it, and the installers' sizes. Added by Claude, 2026-11-23, at the owner's request ("Add the tasks"), after "WebLingo is the worst in practically every category. Investigate why and suggest remediation".

2026-11-25 (Claude): taken out of [DEFER] into the order you gave the next milestones, A1 of "The Next Milestone", fifth, as M0.25 (the specification, section 9). To ask when it is taken up: .NET's Native AOT loads no assembly while a program runs, so that the native modules, `std/*.dll` and `windows/com.dll`, loaded now by reflection, would need another way in, or ReadyToRun instead, which keeps them.

**Q1: How `weblingo` and `wasp` start faster: (a) ReadyToRun, the JIT's work done when they are published, the native modules loaded as now; (b) Native AOT, no JIT at all, the native modules then built into the programs, since Native AOT loads no assembly while a program runs; or (c) ReadyToRun now, and Native AOT with Native C#?**

A1: (c), ReadyToRun now, `weblingo`, `weblingow`, `wasp` and the standard modules published so, the native modules loaded as now, its start-up and its steady speed each measured against the publish without it, on the comparison and the benchmark; Native AOT not now, since it loads no assembly while a program runs, and native modules are; the question of programs without a JIT left to Native C#, with how it loads native modules.

## [DEFER] A 1.0 Scope and a Compatibility Policy

Production needs a WebLingo whose sites keep working from one version to the next (`Documents/production-readiness.md`, section 3.1). Write what a 1.0 freezes, the language, the standard modules, `wasp.config`, WLBC and the native modules' contract, and how later versions keep it: deprecations, versions, and what may break only at a 2.0. Added by Claude, 2026-11-24, at the owner's request ("Turn all of its recommendations into actionable TO-DO items"), `Documents/production-readiness.md`'s recommendation 12.

Deferred: nothing is frozen until the owner says the work is done (the owner's word, 16 November 2026). Taken up when they do.

## WebLingo's Setup Offers Visual Studio's Extension

What "Visual Studio Debug Adapter" left: its guess that WebLingo's setup offers Visual Studio's extension, `Editors/VisualStudio`'s `WebLingo.VisualStudio.vsix`, as it offers VS Code's: `Installers/Windows/package.ps1` building it where Visual Studio 2022 or 2026 is installed, and `setup.wlg` installing it with `VSIXInstaller.exe /quiet` into each Visual Studio it finds, the uninstaller removing it, as it removes VS Code's. Added by Claude, 2026-11-25.

## Your Compiled 01-hello for WLBC 0.2

`Examples.wlg/01-hello.wlbr` and `Examples.wlg/01-hello.disasm.txt`, committed at your request (`368f5e1`), are WLBC 0.1's: since M0.21's part 2 (`e914420`) WLBC is 0.2, a class listing its fields' places, and this build refuses the file, `The file is WLBC 0.1, which this reader does not know: it reads WLBC 0.2.`; nothing runs it. Added by Claude, 2026-11-26.

**Q1: (a) both written again by this build, the file compiled to `01-hello.wlbr` as before and the listing by `weblingo disasm`; (b) left as they are, your snapshot of WLBC 0.1; or (c) removed?**

A1: (c)

# END-OF-TO-DO

## Rewrite the Links App

Rewrite the classic ASP application Links into WASP. Run locally, against the local SQL Server.

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing  a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.
