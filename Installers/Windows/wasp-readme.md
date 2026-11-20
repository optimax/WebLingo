# WASP for Windows and IIS

WASP, Web Application Server Pages, serves sites written in WebLingo: pages, `.wasp` files, and handlers, `.wlg` files,
as PHP serves PHP's. This zip holds `wasp` and `iis-install.wlg`, a script that makes this machine's IIS run WASP's
sites, and installs `wasp` beside `weblingo`, in WebLingo's folder, `C:\Program Files\WebLingo`, where every site
finds it, or, for the zips for x86, on 64-bit Windows, `C:\Program Files (x86)\WebLingo`.

It needs WebLingo installed first, from WebLingo's zip of the same version, processor and kind: `…-win-x64-…` with
WebLingo's `…-win-x64-…`, `…-win-arm64-…` with its `…-win-arm64-…`, `…-win-x86-…` with its `…-win-x86-…`,
`…-self-contained.zip` with WebLingo's self-contained zip, and `…-framework-dependent.zip` with its framework-dependent
one. Its files go with that WebLingo's, and `iis-install.wlg` stops, saying so, where WebLingo is another. The zips for
arm64 are for Windows on ARM, which the x64 ones run on too, more slowly. The zips for x86 are for 32-bit Windows,
or for a site on 64-bit Windows that uses a COM component made for 32 bits only, which a 64-bit `wasp` cannot load:
its `web.config` then runs `C:\Program Files (x86)\WebLingo\wasp.exe`.

## Installing

1. Install WebLingo, from its zip, as its `readme.md` says.
2. If this zip was downloaded, unblock it: right-click it, **Properties**, tick **Unblock**, **OK**. Then unzip it:
   right-click it, **Extract All…**, **Extract**.
3. Open a console as administrator: press the Windows key, type `cmd`, then choose **Run as administrator**.
4. In that console, go to the unzipped folder, the one that holds this `readme.md`, and run:

   ```bat
   cd /d "%USERPROFILE%\Downloads\WASP-0.20.0-pre-alpha-win-x64-self-contained"
   weblingo run iis-install.wlg
   ```

   with the folder's own name, which is the zip's. It takes a few minutes where it turns IIS on, and writes each step.

## What `iis-install.wlg` does

1. **IIS.** Unless IIS is on, it turns it on, with what a site needs and IIS's manager, through DISM. Windows may then
   ask to be restarted.
2. **The ASP.NET Core Module**, through which IIS runs `wasp`. Unless IIS has it, the script reads Microsoft's release
   metadata for .NET 10, downloads the latest ASP.NET Core Hosting Bundle it names, from Microsoft, checks that its
   SHA-512 is the one the metadata gives, and runs it quietly: for the self-contained kind, with the module alone,
   since that `wasp` carries its own runtime; for the framework-dependent kind, with .NET's and ASP.NET Core's runtimes,
   which `wasp` then needs, their 32-bit ones too for the zip for x86. It restarts IIS, which loads the module then.
3. **`wasp`.** It checks, by the SHA-256 of each, `wasp-needs.json`, that the WebLingo installed is the one this `wasp`
   was made with, then copies the folder `WASP` into WebLingo's folder. The application pools of the sites that run
   `wasp` from there are stopped meanwhile, and started again after.

It may be run again, from a newer zip, once WebLingo's newer zip is installed: it keeps what is there, and copies
`wasp` anew. `target=<folder>` names another installation of WebLingo.

## Adding a site

A site is a folder of its own files, its pages beside its `web.config`, which tells IIS to run `wasp` for it:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="C:\Program Files\WebLingo\wasp.exe" arguments="serve" hostingModel="outofprocess" />
    </system.webServer>
  </location>
</configuration>
```

beside an `index.wasp`, its home page:

```html
<% param request; %>
<!DOCTYPE html>
<html><body><h1>Hello from WASP</h1><p>You asked for <%= request.path %>.</p></body></html>
```

Then, from this folder, as administrator, `add-wasp-site.wlg` adds it to IIS: an application pool and a website of
that name, served by HTTPS on port 443 under its host's name, with a certificate this machine trusts, and the name in
the machine's `hosts` file:

```bat
weblingo run add-wasp-site.wlg name=mysite folder=C:\WWW\mysite host=mysite.localhost
```

and it is at <https://mysite.localhost/>. `writable=<folders>` gives the site's pool write access to folders outside
it, such as a SQLite database's. The WASP User Manual says how to write a site.

## Removing it

Remove the sites in IIS's manager, then delete `wasp.exe`, `wasp.dll`, `wasp.deps.json`, `wasp.runtimeconfig.json`
and the other files the folder `WASP` holds from `C:\Program Files\WebLingo`. To remove WebLingo too, remove WASP
first, then run **Uninstall WebLingo.bat** as described in WebLingo's `readme.md`.
