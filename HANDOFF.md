# WebLingo debugger

Named imports in the Variables view resolve through the same module exports that the script reads.
The in-memory debug metadata is in `WebLingo/src/WebLingo.Bytecode/WebLingoFunctionDebugInfo.cs`, produced by
`WebLingo/src/WebLingo.Compiler/WebLingoLowerer.cs` and read by `WebLingo/src/WebLingo.VM/WebLingoVirtualMachine.cs`.
`WebLingo/tests/WebLingo.Conformance.Tests/DebugAdapterTests.cs` covers imported classes, functions, mutable exports,
null exports, and a standard module's built-in function. `Documents/implementation.md` records how inspection works.

The Windows Release solution built with zero warnings and zero errors. All three debugger protocol tests passed.
The installed debugger stopped on line 10 of `Examples.wlg/16-modules.wlg` and reported `Tea` as `class Tea`
(type `Class`) and `serve` as `function` (type `Function`). The example continued to completion.

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
