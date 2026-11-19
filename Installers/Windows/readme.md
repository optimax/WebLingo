# WebLingo for Windows

WebLingo is a scripting language. This zip holds `weblingo`, which runs WebLingo's scripts, `.wl` files, and its
standard modules, for Windows on x64, and `install.wl`, a script that installs them for the whole machine, in
`C:\Program Files\WebLingo`, and puts that folder on the machine's `PATH`.

It comes in two zips:

- **`…-self-contained.zip`** holds .NET's runtime too: nothing else needs installing.
- **`…-framework-dependent.zip`** is smaller, and needs .NET 10's runtime: install it first, ".NET Runtime 10.0" for
  x64, from <https://dotnet.microsoft.com/download/dotnet/10.0>.

## Installing

1. If the zip was downloaded, unblock it first, so that Windows does not mark every file in it as from the internet:
   right-click it, **Properties**, tick **Unblock**, **OK**.
2. Unzip it anywhere: right-click it, **Extract All…**, **Extract**.
3. Open a console as administrator: press the Windows key, type `cmd`, then choose **Run as administrator**.
4. In that console, go to the unzipped folder, the one that holds this `readme.md`, and run the installer with the
   `weblingo` the zip holds:

   ```bat
   cd /d "%USERPROFILE%\Downloads\WebLingo-0.20.0-pre-alpha-win-x64-self-contained"
   WebLingo\weblingo.exe run install.wl
   ```

   with the folder's own name, which is the zip's. It writes where it installed WebLingo, and that it added the folder
   to the `PATH`.
5. Open a new console, not as administrator, and check that it runs:

   ```bat
   weblingo --version
   ```

The unzipped folder may then be deleted. `weblingo run script.wl` runs a script, and `weblingo check script.wl`
reports its errors without running it. WebLingo is not signed yet: Windows may warn that `weblingo.exe` comes from an
unknown publisher (**More info**, then **Run anyway**), and Smart App Control, where it is on, blocks it.

To serve WASP's sites, WebLingo's web pages, on IIS, install WASP's zip of the same version and the same kind after
this one, as its `readme.md` says.

## Updating

Run a newer zip's `install.wl` the same way: it copies its files over the old ones, then WASP's, where it is installed. A program running from
`C:\Program Files\WebLingo`, an IIS site's `wasp` among them, keeps its files in use: stop it first, or the installer
says which programs to stop.

## What `install.wl` does

It stops unless it runs as administrator; copies the folder `WebLingo`, beside it, into `C:\Program Files\WebLingo`,
over any files of the same names; and adds that folder to the end of the machine's `PATH`, once, leaving its other
entries as they were. `target=<folder>` installs elsewhere: `WebLingo\weblingo.exe run install.wl target=D:\WebLingo`.
`windows.wl`, beside it, is what it shares with WebLingo's other setup scripts, and `weblingo.config` lists the Windows
objects they may use, through `windows:com`.

## Removing it

Delete `C:\Program Files\WebLingo`, and remove that folder from the machine's `PATH`: **Settings**, **System**,
**About**, **Advanced system settings**, **Environment Variables…**, **Path** under **System variables**, **Edit…**.
