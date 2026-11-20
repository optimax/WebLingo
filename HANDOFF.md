# Windows local installation

The repository-root `build.bat` publishes WebLingo and WASP together and updates the local installation.
The publish runs with the caller's permissions. The installer requests administrator access when needed.
The build and editor instructions are in `Documents/implementation.md` and `Editors/README.md`.

On Windows, the batch file ran from another folder and installed the Release publish in `C:\Program Files\WebLingo`.
All 115 published files matched their installed copies by SHA-256. The installed CLI ran `Examples.wlg/01-hello.wlg`
and answered debugger initialization and disconnect requests. Both `weblingo` and `wasp` reported 0.20.0-pre-alpha.

An open WebLingo script window can keep the old executable in use after its script has finished. Close it and
double-click `build.bat` again. The VS Code extension uses the installed CLI when its debugger setting is `weblingo`
and the installation folder is on PATH.
