# WebLingo for Windows

WebLingo is a scripting language. This zip holds `weblingo`, which runs WebLingo's scripts, `.wlg` files, and its
standard modules. **Install WebLingo.bat** installs the runtime for the whole machine and offers file associations
and the debugger, in VS Code and in Visual Studio 2022 or 2026, for your Windows account.

It comes in six zips, named for the processors they run on and for whether they carry .NET:

- **`…-win-x64-…`** is for 64-bit Windows on Intel's and AMD's processors, which most Windows machines are.
- **`…-win-arm64-…`** is for Windows on ARM, such as a laptop with a Snapdragon processor. The x64 zips run there too,
  through Windows's emulation, but more slowly.
- **`…-win-x86-…`** is for 32-bit Windows, or, on 64-bit Windows, for a site or a script that uses a COM component
  made for 32 bits only, such as an old Access database's driver, which a 64-bit program cannot load. On 64-bit
  Windows it installs in `C:\Program Files (x86)\WebLingo`, beside the x64 one where both are installed.
- **`…-self-contained.zip`** holds .NET's runtime too: nothing else needs installing.
- **`…-framework-dependent.zip`** is smaller, and needs .NET 10's runtime: install it first, ".NET Runtime 10.0" for
  the same processor, x64, Arm64 or x86, from <https://dotnet.microsoft.com/download/dotnet/10.0>.

## Installing

1. If the zip was downloaded, unblock it first, so that Windows does not mark every file in it as from the internet:
   right-click it, **Properties**, tick **Unblock**, **OK**.
2. Unzip it anywhere: right-click it, **Extract All…**, **Extract**.
3. In the extracted folder, double-click **Install WebLingo.bat** from your usual Windows account.
4. Press **Enter** to enable running a `.wlg` file by double-clicking it. If setup finds VS Code, press **Enter** to
   enable debugging there too, and likewise for Visual Studio 2022 or 2026, where it finds one without WebLingo's
   extension. Answer **N** to skip a choice or **Q** to cancel before installation. Visual Studio's extension is
   installed once Visual Studio is closed: setup asks you to close it if it is open.
5. Approve the Windows administrator prompt. The machine installer copies WebLingo and adds it to PATH. Once it
   finishes, setup applies the choices to the account that started it.

Once setup reports **Setup complete**, you can delete the extracted folder. To debug, reopen VS Code, open a trusted
folder containing your script, open the `.wlg` file, and press **F5**. Choose **WebLingo** if asked for a debugger.
If you install VS Code later, open its Extensions view, choose **… → Install from VSIX…**, and select
`VSCode\WebLingo.vsix` in the installed WebLingo folder. In Visual Studio, open a folder holding the script, **File**,
**Open**, **Folder**, and press **F5** on a `.wlg` or `.wasp` file; to add its extension later, with Visual Studio
closed, open `VisualStudio\WebLingo.VisualStudio.vsix` in the installed WebLingo folder. A zip made on a machine
without Visual Studio has no such file, and its setup does not offer it.

From a new console, `weblingo --version` shows the installed version, `weblingo script.wlg` runs a script,
and `weblingo check script.wlg` checks it. Windows may warn that the unsigned `weblingo.exe` comes from an
unknown publisher (**More info**, then **Run anyway**), and Smart App Control, where it is on, blocks it.

To serve WASP's sites, WebLingo's web pages, on IIS, install WASP's zip of the same version, processor and kind after
this one, as its `readme.md` says.

## Running a script by a double-click

Setup offers this registration. To add it manually later, `add-wlg-file-type.wlg` makes Explorer run a `.wlg` file when it is
double-clicked, for your Windows account, with `weblingow.exe`, WebLingo as a Windows application: a script that
imports `std:console` gets a console that waits for a key once the script has ended, so that what it wrote stays to be
read; one that does not opens no window; and a script that fails shows why in a message box. **Edit** on its
right-click menu opens it in Notepad. It needs no administrator. In a console in
this folder:

```bat
weblingo add-wlg-file-type.wlg
```

At the first double-click, Windows asks once how to open the file, since only you may choose that, and its list may
leave WebLingo out: choose **Choose an app on your PC** at its end (on Windows 10, **More apps**, then **Look for
another app on this PC**), then `weblingo.exe` in WebLingo's folder, `C:\Program Files\WebLingo`, then **Always**:
the registration runs `weblingow.exe` for it.
`remove=yes` takes it away again. A script double-clicked runs as any program does, one downloaded from the internet
included: run only those you trust.

## Updating

Extract the newer ZIP and double-click **Install WebLingo.bat** again. Select the VS Code option to update your
extension too. Close programs running from the installed WebLingo folder first, including running debugger sessions.
If WASP is installed, stop the sites using it and follow the matching WASP ZIP's instructions after updating WebLingo.

## What `install.wlg` does

It stops unless it runs as administrator; copies the folder `WebLingo`, beside it, into WebLingo's folder, over any
files of the same names; and adds that folder to the end of the machine's `PATH`, once, leaving its other entries as
they were, so that where both WebLingo for x64 and for x86 are installed, `weblingo` by its name is the one installed
first. `target=<folder>` installs elsewhere: `WebLingo\weblingo.exe run install.wlg target=D:\WebLingo`. `windows.wlg`,
beside it, is what it shares with WebLingo's other setup scripts, and `weblingo.config` lists the Windows objects they
may use, through `windows:com`.

## Removing it

Close any running WebLingo scripts and debugging sessions. In your usual account, double-click **Uninstall WebLingo.bat**
in the installed WebLingo folder or in the extracted ZIP. Type **Y** to confirm and approve the administrator prompt.
If a file is in use, close the program named by the error and retry. You can always rerun the launcher from the ZIP.

Uninstall removes the packaged runtime and offline debugger, and the machine PATH entry that setup added. It restores
the file-association values setup changed if they still match its registration, and removes the VS Code extension
if setup added it for this account, and Visual Studio's from each Visual Studio setup added it to, once Visual Studio
is closed. Windows controls your explicit default-app choice. An installation of VS Code's
extension that predates setup stays installed. WASP keeps its shared runtime files and PATH. A second WebLingo
architecture keeps its editor integration. Files you added or edited in the installation folder are preserved.

For an older installation without uninstall records, use the new ZIP for the same runtime version, processor and kind.
The uninstaller removes matching runtime files, its PATH entry and the current account's WebLingo registrations.
It recognizes older builds of WebLingo's own DLLs by assembly identity and product version.
Earlier settings cannot be reconstructed for those installations.
