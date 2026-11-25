# WebLingo debugger

Named imports in the Variables view resolve through the same module exports that the script reads.
The in-memory debug metadata is in `WebLingo/src/WebLingo.Bytecode/WebLingoFunctionDebugInfo.cs`, produced by
`WebLingo/src/WebLingo.Compiler/WebLingoLowerer.cs` and read by `WebLingo/src/WebLingo.VM/WebLingoVirtualMachine.cs`.
`WebLingo/tests/WebLingo.Conformance.Tests/DebugAdapterTests.cs` covers imported classes, functions, mutable exports,
null exports, and a standard module's built-in function. `Documents/implementation.md` records how inspection works.

The Windows Release solution built with zero warnings and zero errors. All three debugger protocol tests passed.
The installed debugger stopped on line 10 of `Examples.wlg/16-modules.wlg` and reported `Tea` as `class Tea`
(type `Class`) and `serve` as `function` (type `Function`). The example continued to completion.

## Debugging WASP pages

The specification's section 5.7, "Debugging WASP pages, parts", splits the task "Debugging WASP Pages" in four; part
1, a page under the debugger, is built. `wasp --debug-adapter` (`WebLingo/src/WebLingo.Wasp.Cli/Program.cs`) runs
`WebLingoDebugSession` (`WebLingo/src/WebLingo.Cli`), given the command line's program; a launch's `program` is a page,
`root` the site's root. `WebLingoCommandLine.RunDebugFile` compiles it for a debugger and gives the session the
compilation's `Locate` and `TextOf`, so that each point is found in its own file, an include's, a layout's, a
component's or a module's. VS Code's extension (`Editors/VSCode`, 0.3.0) has a debugger `wasp`, its executable named by
`weblingo.waspDebuggerPath`. `WaspDebugTests` (`WebLingo.Templates.Tests`) stops in a page, its layout, an include, a
component and a module, through `DebugAdapterSession`, the conformance tests' client, linked into that project.
A page's code under a layout is an anonymous function in the call stack, `function`. The extension was tried with a
stub of VS Code's API only, not in VS Code itself: install it again, from the folder or with `build.bat`, to try it.

Part 2, a site under the debugger, is built too: a launch without `program` serves the site at `root`
(`WebLingoWaspServer.ServeForDebugger`, Kestrel, `port`, 8080 by default), each request a thread of the session's
(`IWebLingoDebuggerThreads` in `WebLingo.VM`, `WebLingoApplicationOptions.Debugger`, `WebLingoDebugSession.DebugThread`),
named `GET /cart #12`. A request's stop makes `IWebLingoRegisterDebugger.Stopped` give false, and the VM returns waiting
with its frames kept (`_stopDepth`), resumed in `Run`'s prologue through `GoesOn` once the session posts to the request's
scheduler host; a script's and a page's stops still block their own thread. The send timeout leaves out time stopped
(`WebLingoResponseText`, `IWebLingoDebuggerThread.Stops`). Tests: `DebuggerStopTests` (`WebLingo.VM.Tests`),
`DebuggedRequestTests` (`WebLingo.Hosting.Tests`), `WaspDebugTests.ASiteStopsOnlyTheRequestAtABreakpoint`. The VS Code
extension, 0.4.0, has "Debug WASP Site"; it was not tried in VS Code itself. The benchmark against `main`, 12 rounds alternating: no workload slower by the rank test; the resumption first written inside the dispatch loop made `closures` 9 percent slower, so it is in `Run`'s prologue.

Part 3, conditional breakpoints, is built: the session reads a condition where it is set
(`WebLingo/src/WebLingo.Cli/WebLingoDebugCondition.cs`) into `WebLingoDebugExpression` (`WebLingo.VM`), checks its
names against its line's scope once the file is compiled, and the VM evaluates it at each visit (`TryTest`, outside
`Run`), never running the program's code: the built-in classes' read-only methods of strings, arrays, maps and sets,
and native properties, allowed. Tested by `DebugAdapterTests.ABreakpointsConditionStopsOnlyWhereItHolds` and
`WaspDebugTests.AConditionStopsOnlyTheRequestsItNames`. Part 4 waits for Visual Studio installed here.

## Windows local installation

Users installing a release double-click `Install WebLingo.bat` in the extracted ZIP. Its source is
`Installers/Windows/Install WebLingo.bat`, the setup logic is in `Scripts/setup.wlg`, and
`Installers/Windows/package.ps1` builds the VSIX from the shipped editor sources. Setup elevates `install.wlg`.
The launcher applies the two optional integrations in the original account. The installed `VSCode/WebLingo.vsix`
can also be used after installing VS Code later. See `Installers/Windows/readme.md`, `Editors/README.md`,
`Scripts/README.md` and the root `README.md`. The approved design is in `Documents/weblingo-architecture.md`
under “Local source debugging” and packaging is described in `Documents/implementation.md`.

On x64 Windows the isolated launcher checks covered cancellation, invalid input, both defaults, each integration
individually, skipping both, missing VS Code, rerunning setup and failures in the machine and editor steps.
The temporary install ran the hello example and answered debugger initialization and disconnect. VS Code installed
the bundled extension in an isolated profile. The checks substituted the UAC boundary, machine PATH and account
registration. A first install from a standard account using a different administrator's credentials remains a manual
check on a disposable Windows machine.

Both the ZIP and the installed runtime include `Installers/Windows/Uninstall WebLingo.bat`. Its PowerShell helper,
`Installers/Windows/uninstall.ps1`, runs outside the runtime, elevates machine cleanup and restores the current
account's integrations. `Scripts/install.wlg` keeps the original PATH and merges package inventories on updates.
`Scripts/setup.wlg` records optional integrations before applying them. The root README and both Windows package
readmes describe removal, including the WASP dependency.

`Installers/Windows/tests/uninstall.ps1` checks registry restoration, side-by-side installations, file ownership,
WASP sharing, locks and retry. The x64 Windows round trip also exercised setup and update, cancellation, both uninstall
launchers, the previous release ZIP and the real VS Code CLI in an isolated profile. Machine elevation was simulated
and registry writes were confined to a disposable test key. Direct UAC consent using another account remains a manual check.

The repository-root `build.bat` publishes WebLingo and WASP together and updates the local installation.
The publish runs with the caller's permissions. The installer requests administrator access when needed.
The build and editor instructions are in `Documents/implementation.md` and `Editors/README.md`.

`WebLingo/publish.ps1` and `Installers/Windows/package.ps1` select their runtime files through
`WebLingo/Runtime.pubxml`. Both omit debugging symbols and satellite resource assemblies.
The existing Windows installation was updated and its 13 SqlClient translation DLLs and 18 symbol files removed.
All 84 runtime files in `C:\Program Files\WebLingo` matched the local publish by SHA-256. The installed CLI ran
`Examples.wlg/01-hello.wlg` and answered debugger initialization and disconnect requests. WASP rendered the sample website.
All 12 Windows ZIPs passed file-list checks, and the shared-file hashes in each WASP manifest matched its WebLingo ZIP.
A publish into a populated folder removed the obsolete generated files and preserved unrelated files in that folder.

An open WebLingo script window can keep the old executable in use after its script has finished. Close it and
double-click `build.bat` again. The VS Code extension uses the installed CLI when its debugger setting is `weblingo`
and the installation folder is on PATH.

## Request limits

A request under `wasp` is stopped once it passes its time, 60 s, its instructions, 1,000,000,000, or its heap's memory,
128 MB of strings, each `wasp.config`'s `"limits"` (`WebLingoSchemes.TryReadLimits`, `WebLingoWaspServer.Load`), a 503,
WL3003. The hosting core reports a cancellation whose signal says it passed a limit
(`WebLingoCancellationSignal.Exceeded`) as WL3003; the time is kept by `WebLingoRequestTimer`, which ASP.NET Core's host
and the FastCGI host start for each request, and which wakes a waiting request as a gone client does. No time limit
under the debugger. Specification, section 6.12, "A request's limits"; WASP User Manual, section 12.

## Linux installation

`Installers/Linux/package.sh`, run on Linux, makes a self-contained archive with `install.sh` and a Debian package for
x64 and arm64, each holding `weblingo`, `wasp` and the standard modules in `/opt/weblingo`; its `readme.md` is a user's
steps, and `Documents/implementation.md`, section 8, how it is built. A script's first line `#!/usr/bin/env weblingo` is
skipped by the lexer, so that an executable `.wlg` runs by its name. Nothing of it was made or run on this machine,
which has no Linux and no Linux runtime packs: CI's job `linux-installer` makes the x64 packages and installs, runs and
removes them with `Installers/Linux/tests/test.sh` on every push, so it is first tried by the push after it lands. The
arm64 packages are made by `package.sh` but tried nowhere.

## The console module

`std:console` holds the console's writing and reading, `write`, `writeLn`, `writeError`, `writeErrorLn`, `readLine`,
`readLineSync`, `prompt` and `readPassword`; `std:system` keeps `delay`. It is `WebLingo/src/WebLingo.StdLib.Console`,
installed as `std/console.dll`, which `wasp`'s loaders refuse (WL2015). A page's blocks have their own `write` and
`writeLn`, which only a template's code sees, and a handler writes with `response.write`. On Windows, `weblingo.exe`'s
manifest says `detached` and `weblingow.exe` (`WebLingo.Windowed.Cli`) is a Windows application: either, started with
no console, makes one only for a script that imports `std:console`, and shows a failure in a message box
(`WebLingoConsoleSession`). `Console` and `ConsoleColor` control the console through the platform's `IWebLingoConsole` (`DotNetConsole`), which
`weblingo` gives a script; Ctrl+C's handler runs on the script's worker. These are the specification's section 9,
"Console module, parts", all four landed. Part 4 registers `.wlg` files to run
`weblingow.exe`, under the keys `Scripts/add-wlg-file-type.wlg` wrote before, which the uninstaller removes for either
program; this machine's own registration and installation change only once its owner runs `build.bat` and setup.

In-process test hosts give `write` and `writeLn` as their own built-ins (`WebLingoStandardBuiltins`), which `std:system`
exports to them, so the VM tests still import them from there. The conformance tests that compile programs in process
use `ConformanceHost`, which loads the real `std:console` from the `std` folder beside the tests.

## Adding ASP.NET Core sites to IIS

`Scripts/add-aspnetcore-site.wlg` adds an ASP.NET Core site that is not WASP's to IIS, as `add-wasp-site.wlg` adds a
WASP site: both now call `iis.wlg`'s `addSite`, which holds what `add-wasp-site.wlg` did, unchanged but for the new
script's option to load the pool's profile. All of the scripts compile (`weblingo check`) with the WebLingo the
repository publishes; the WebLingo installed on this machine, from 20 November 2026, predates `std:console`, which
`iis.wlg` imports, so they run with `WebLingo\publish\WebLingo\weblingo.exe` until `build.bat` installs it again.
Neither has been run as administrator since the move.

## Versions and build numbers

The solution's version is `0.20.0`, with no designation, and each build has a number, its local time, `yyyy.MM.dd.HHmm`,
as MaxPad makes it: `WebLingo/Directory.Build.props` makes it every assembly's file version, the product version staying
the version, which the uninstaller compares. `weblingo --version` prints `WebLingo 0.20.0 (2026.11.21.2330)`, and
`wasp --version` the same after `WASP` (`WebLingoCommandLine.VersionAndBuild`). A build more than a minute after the last
compiles every project again; `-p:BuildStamp=…` gives a build a fixed number. The installers' ZIPs are named from the
version, `WebLingo-0.20.0-win-x64-…`; none has been packaged since the change.

## Debug information in bytecode

`weblingo compile --debug` compiles with `WebLingoCompilerOptions.Debugging`, and `WebLingoBytecodeFile` writes the
metadata as an eighth section, debugging, after the source positions, which a file without it does not have, its bytes
unchanged. `disasm` (`WebLingoDisassembler`, `WebLingoDisassemblyNames`) names the functions,
classes and files instructions refer to, and, from a debug file, captures, statements' lines and columns, and variables.
The VS Code debugger still compiles from source: nothing runs a debug file under a debugger yet.

## Built-ins written in WebLingo

The built-in classes, the array methods, the spread, `std:test` and the methods `Console`, `Database` and `Response`
declare in WebLingo are `.wlg` files: `WebLingo/src/WebLingo.Compiler/Builtins/`, and `Console.wlg`, `Database.wlg` and
`Response.wlg` in their projects. `WebLingo/Directory.Build.targets` makes each a constant of `WebLingoSources`, written
to `obj` when its text changes; the C# that used a string uses the constant. A runtime error's class is
`RuntimeError.wlg` with its name put in. Moved from the strings without a change but three final line breaks, which
`ArrayMethods`, `Database` and `Response` had, and the line breaks of `Html`, `Test` and `Console`, CRLF in a Windows
checkout before, LF now. `BuiltinSourcesTests` checks the five that `weblingo check` can.

## A native module's named imports

`WebLingoBinder` binds the program's native modules before its statements (`BindNativeUnit`), and a name an import
takes of one is its export itself: a function the built-in, a call a `CallBuiltin` where it gives the function's
arguments, else a call of its value, so that its error stays a runtime one; a class, a constant or a function written in
WebLingo the local the program makes it in (`ImportedNative`, `ImportedLocal`). Each native module's instance is made by
a function of its own, registered as its file, which a whole import or a dynamic one runs once; an import whose every
name is bound so is dropped, but in a debug build, whose view of a named import reads the module. An export is bound
only when a name an import takes is it, or when the module's instance may be made (`TryNativeExport`,
`FinishNativeUnit`): a whole import, an import kept, or a `Module.import` whose path is not a string naming a file the
program has (`NoteImportWhileRunning`), a scheme's naming only its module; a `MadeByModule` class always. A module
compiled while the program runs joins the program's native modules by name, keeping the program's: never leave the
program's without exports where one could be joined, or the VM's `MakeModule` finds its exports missing. `WebLingoClassTables`
lets a native module's file have that function, older files having none. The benchmark's `imports` workload calls
`std:math`'s `abs` and `sign` imported by name. Nothing of it is installed on this machine until `build.bat` runs.

## One VM

The owner chose the register VM (23 November 2026, WL-BC002; specification, section 5.2). The stack VM was removed:
`WebLingo.VM.Stack`, `WebLingoStackCodeGenerator`, `WebLingoStackCompiler`, the stack bytecode, its verifier and its
disassembler, `IWebLingoStackModuleLoader`, the hosts' and the command lines' stack paths, `--register`, `--stack`,
`--vm` and the configurations' `"vm"`, the benchmark's stack engine, and the tests that ran it. The tag `stack-vm-end`
(`de0cc94`) is the last commit with it. A bytecode file is `.wlb` (`WebLingoBytecodeFile.Extension`); an earlier
build's `.wlbr` and `.wlbc` are read as one, by the header, and a `.wlbs`, or a header naming VM 2, is refused with
WL4001 (`WebLingoBytecodeKind.Stack` kept only to recognize it). Some tests still take a VM parameter or loop over a
flag that now has the one value, `"register"` or `false` (`FileSystemTests`, `WaitingTests`, `BytesTests`,
`DateTimeTests`, `HostParameterTests`, `OperatorTests`, and `BytecodeCommandLineTests`' `Extension(vm)`): a cleanup
left for later. The conformance programs every-program tests take are `ConformancePrograms.All`.

## Speed and the collector

The specification's section 5.6 has where WebLingo's time went ("Where the time goes"), the eight remedies, each a
task in `DONE.md`, and their measurements against `da0bd06` ("After the remedies"): in one process `objects` 2.8
times as fast, `loops` 1.4; as a user runs them, since arithmetic's slow paths left `Run` (`d3b1b47`), `objects`
about 2.2 times, `loops` and `sort` about twice, `maps` 1.5, `calls` and `closures` as they were. Start-up is
unchanged, .NET's JIT at work: its remedy is the
deferred task "`weblingo` and `wasp` Published ReadyToRun". The benchmark's `io` measures .NET's thread pool more than
the VM, and falls at about 1.1 s or 2.7 s by build.

A young generation for the collector (the specification, section 4.2, "A young generation") was built on a branch,
measured, and dropped by the owner (24 November 2026): about 1.7 times as fast where much is live, but four of the
benchmark's workloads 1 to 3 percent slower. Its design stays in the specification, Proposed; nothing of it is on `main`.

Since `071ad6b` the heap reads the VM's frames when it collects (`WebLingoHeap.Frames`, `IWebLingoRootSource`), and
since `9c2cc3b` it keeps what is being stored or made while it collects; `Documents/implementation.md`, "A Value Just
Made Is No Root", says how to find such places with a heap that collects every 16 units, a local trial never
committed. Under it, every conformance case passes but one that counts collections.

## Mail

`std:mail` sends mail by SMTP to a relay (the specification, section 7.17, the owner's answers of 23 November 2026):
`WebLingo.StdLib.Mail`, `std/mail.dll`, in the portable core, through the platform's `IWebLingoMail`; on .NET,
`DotNetMail`, with an SMTP client and a MIME writer of WebLingo's own (`WebLingoSmtpClient`, `WebLingoMimeWriter`, in
`WebLingo.Platform.DotNet`), STARTTLS or TLS from the start, PLAIN, LOGIN and XOAUTH2. Its servers are
`"mail"` in `weblingo.config` and `wasp.config`, read by `WebLingoSchemes`; a password, and a database's connection
string, may be kept in an environment variable or a file (`WebLingoSecret`, read by `IWebLingoSecrets`,
`DotNetSecrets`, each time it is used). Its error is `MailError`, WL3023, Proposed. `MailTests` (`WebLingo.Conformance.Tests`)
send through `SmtpTestServer`, a server of the tests' own on 127.0.0.1 with a self-signed certificate made for each
test; nothing reaches a real server. Not built, as the owner left them for later: SMTPUTF8, DKIM, IMAP and POP3, and
a class `MailMessage`.

## Uploaded files

A form posted as `multipart/form-data` is read as it arrives by the hosting core's own reader (`WebLingoMultipartReader`),
`request.form()` its fields and `request.files()` its files, each an `UploadedFile` (`WebLingoHttpObjects`), a file past
64 KB kept in a temporary file through the platform's `IWebLingoTemporaryFiles` (`DotNetTemporaryFiles`), removed as the
request ends (`WebLingoApplication.Ended`). A site's limits are `wasp.config`'s `"maxBodySize"` and `"uploads"`
(`WebLingoSchemes`, `WebLingoUploads`), a body larger by its `Content-Length` a 413 before the page runs; the ASP.NET
Core adapter raises Kestrel's own limit to the site's, but IIS's request filtering keeps its own, which a site raises in
its `web.config` (the WASP User Manual, section 17). `UploadTests` (`WebLingo.Hosting.Tests`) and
`WaspServeTests.AFormsFilesAreUploaded`, through Kestrel, 30.5 MB among them.

## Sessions and cookies

The specification's section 6.12, "Sessions and cookies", has the owner's answers, and "Sessions and cookies, parts"
splits the task in three. Part 1, cookies, is built: `request.cookies`, `response.setCookie` and `response.deleteCookie`
(`WebLingo/src/WebLingo.Hosting/WebLingoCookies.cs`, the glue in `WebLingoHttpObjects`, the two methods written in
WebLingo in `Response.wlg`), tested by `CookieTests` and `AspNetCoreTests.CookiesAreReadAndEachIsSetByAHeaderOfItsOwn`.
Part 2, sessions in memory, is built: a request's session (`WebLingo/src/WebLingo.Hosting/WebLingoSessions.cs`,
`WebLingoSession`, its values JSON text, `WebLingoSessionValues`, and the store's contract, `IWebLingoSessionStore`) is
opened, its lock taken, in `WebLingoApplication.Run` before the page runs, a request that waits for it a `Waiting`
outcome, and closed in `Ended`; `request.session` and the class `Session` are in `WebLingoHttpObjects`; the store in
memory is `WebLingoMemorySessions` (`WebLingo.Hosting.AspNetCore`), which `WebLingoWaspServer.Load` gives a site, from
`wasp.config`'s `"sessions"` (`WebLingoSchemes`, `WebLingoSessionSettings`); `session readonly;` is the template
parser's (`AddSession`), a handler's `export const session = "readonly";` read with its exports (`ExportsOf`). Tested by
`SessionTests` (`WebLingo.Hosting.Tests`), `PageTests`' session cases and `WaspServeTests.ASessionIsKeptBetweenRequestsAndOneAtATime`.
Part 3, sessions in a database, is built: `WebLingoDatabaseSessions` (`WebLingo.Hosting.AspNetCore`), through the
platform's `IWebLingoDatabases`, its table's statements in `CreateStatements`, which `wasp sessions create`
(`WebLingo/src/WebLingo.Wasp.Cli/WebLingoWaspSessions.cs`) runs or prints; the store's contract gained a failure for
opening and a close done off the worker (`WebLingoApplication.Ended` waits for it before the response ends). Tested by
`DatabaseSessionTests` (`WebLingo.Hosting.Tests`, which references `WebLingo.Data.DotNet` for it, on SQLite),
`WaspServeTests.SessionsAreKeptInTheSitesDatabase` and `SessionsAreKeptInSqlServer`; MySQL's and PostgreSQL's SQL is not
tried, no server of theirs being here.

Next: "Extensive WASP Example", its prerequisites done, waits for them installed (its A3 to A5); debugging's part 4
waits for Visual Studio installed here, as "Visual Studio Debug Adapter" does.

The last full suite, on Windows, in Release: 3,525 tests passing. Linux not tried since the last push.
