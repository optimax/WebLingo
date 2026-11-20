# Windows debugger build

The repository-root `build.bat` builds `WebLingo.Cli` in Release using the SDK selected by `WebLingo/global.json`.
It prints the full path to `weblingo.exe` for VS Code and pauses after success or failure.
The build and editor instructions are in `Documents/implementation.md` and `Editors/README.md`.

On Windows, the batch file ran from another working directory and the executable answered debugger initialization
and disconnect requests. With `dotnet` absent from `PATH`, the batch file reported failure and returned exit code 9009.
The Release solution build had zero errors and zero warnings. All 4,243 tests passed.

To use the build, set **WebLingo: Debugger Path** in VS Code's user settings to the path the batch file prints.

For future changes, `AGENTS.md` section 7, `Documents/implementation.md` section 8, and `TO-DO-INSTRUCTIONS.md`
require only tests whose results could be affected. Changes limited to this batch wrapper can be verified directly.
