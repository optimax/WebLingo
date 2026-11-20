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
`Installers/Windows/package.ps1` builds the VSIX from the shipped editor sources. Only `install.wlg` crosses UAC.
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
