# Scripts

Scripts that set up WebLingo, and WASP's sites, on a Windows machine with IIS, written in WebLingo and run by `weblingo`.
They reach Windows through its COM objects, `windows:com` (the Standard Library Reference, section 23): IIS's
configuration through IIS's own, `Microsoft.ApplicationHost.WritableAdminManager`; IIS's `appcmd`, `netsh`, `certutil`
and `icacls` through `WScript.Shell`; a relative path made full through `Scripting.FileSystemObject`; the processes
running through WMI; and certificates through CertEnroll's `X509Enrollment` objects. `weblingo.config`, beside them,
lists the objects they may make, as `windows:com` asks of a script. Files they copy, WebLingo's own as it is installed,
they copy through `std:fs`, without COM.

| Script | What it does |
|---|---|
| `setup.wlg` | Runs in the user's account from the ZIP's **Install WebLingo.bat**. Elevates `install.wlg` for the runtime and PATH and offers the file association and VS Code extension to the original user. |
| `install.wlg` | Installs WebLingo from its installer's zip, which holds it beside the folder it installs, `WebLingo`: copies that folder into WebLingo's folder for the zip's architecture, `C:\Program Files\WebLingo`, or `C:\Program Files (x86)\WebLingo` for x86 on 64-bit Windows, and puts it on the machine's `PATH` (`../Installers/Windows/readme.md`) |
| `iis-install.wlg` | Makes this machine's IIS run WASP's sites, from WASP's installer's zip: turns IIS on, unless it is; installs the ASP.NET Core Module, unless IIS has it, from Microsoft's Hosting Bundle, checked against the SHA-512 Microsoft's release metadata gives; and copies `wasp`, the zip's folder `WASP`, into WebLingo's folder for the zip's architecture, once the WebLingo there is found to be the one it was made with (`../Installers/Windows/wasp-readme.md`) |
| `add-wlg-file-type.wlg` | Makes Explorer run a `.wlg` file when it is double-clicked, for this Windows account, `weblingo script.wlg`, whose window waits for a key at the end, with **Edit** in its right-click menu opening Notepad, and WebLingo named in Windows's "open with" choice; no administrator needed; `remove=yes` undoes it |
| `install-weblingo.wlg` | Installs WebLingo in `C:\Program Files\WebLingo`, or updates it, from the folder `WebLingo/publish.ps1` publishes it to, `WebLingo\publish\WebLingo`; the application pools of the sites whose `wasp` runs from there are stopped while their files are replaced, and started again |
| `add-weblingo-to-path.wlg` | Puts `C:\Program Files\WebLingo` on the machine's `PATH`, once, so that consoles started after run `weblingo` and `wasp` by their names |
| `add-wasp-site.wlg` | Adds a WASP site to IIS: its application pool, with no managed code, and its website, served by HTTPS on port 443 under a name of its own, with a certificate this machine trusts |
| `add-sql-server-login.wlg` | Gives a site's application pool its database on SQL Server: a login for the pool's account, and a user of the database that reads and changes its rows |
| `windows.wlg`, `iis.wlg` | What the scripts share: running a program, the processes running, a folder copied, the machine's `PATH`, a certificate made and trusted; and IIS's configuration, its modules, its pools, and the installation copied into with the sites that run it stopped |

Double-click the ZIP's launcher in your usual account to run `setup.wlg`. To run a machine script directly, use an
administrator console in this folder. `add-sql-server-login.wlg` needs a SQL Server administrator, and
`add-wlg-file-type.wlg` runs as the account whose file association it registers.

When registering `.wlg`, `add-wlg-file-type.wlg` also removes WebLingo's old per-account registration for `.wl`.
Other apps' registrations and the app explicitly chosen in Windows for `.wl` are preserved.

## Installing WebLingo

On a machine without WebLingo, its installer's zips install it, `weblingo` and the standard modules, each with
`install.wlg` and the scripts it shares, which `../Installers/Windows/package.ps1` makes and its `readme.md` explains;
one holds .NET's runtime too, and the other needs it installed. WASP's zips, made with them, then make IIS run
WASP's sites, with `iis-install.wlg`, as `../Installers/Windows/wasp-readme.md` explains. On a server that builds
WebLingo from this repository, `wasp` with it:

With IIS turned on, and the ASP.NET Core Hosting Bundle for .NET 10 installed after it (`Examples.wasp/README.md`, example1,
"Deploying it to IIS"), publish WebLingo from the solution's folder, `WebLingo`:

```powershell
powershell -ExecutionPolicy Bypass -File publish.ps1
```

then, from this folder, as administrator, install it with the published `weblingo`, since the installed one cannot
replace itself while it runs, and put it on the machine's `PATH`:

```bat
..\WebLingo\publish\WebLingo\weblingo.exe run install-weblingo.wlg
"C:\Program Files\WebLingo\weblingo.exe" run add-weblingo-to-path.wlg
```

Run `install-weblingo.wlg` again, the same way, after publishing a newer WebLingo: it updates every site at once.

## Adding a site

From this folder, as administrator, with the site's folder, the one that holds its pages and `web.config`, copied where
IIS is to serve it from, example1 and example2 copied to `C:\WWW`:

```bat
weblingo run add-wasp-site.wlg name=wasp.example1 folder=C:\WWW\wasp.example1 host=example1.wasp.localhost
weblingo run add-wasp-site.wlg name=wasp.example2 folder=C:\WWW\wasp.example2\site host=example2.wasp.localhost writable=C:\WWW\wasp.example2\database
```

`name` names the application pool and the website; `host` the name the site is served under, `https://<host>/`, on
port 443, for that name alone (SNI), so that other sites share the port under names of their own; and `writable` the
folders outside the site that its pool may change, separated by semicolons, such as example2's database's, beside
which SQLite writes its journal. The script makes the site a certificate, unless one is bound to its name already:
self-signed, for that name only, not a certificate authority, its private key not exportable, valid for two years, and
trusted by this machine, a copy of it without its key among the machine's trusted roots (`Cert:\LocalMachine\Root`,
where it is removed to undo that). And it puts the name in the machine's `hosts` file, for `127.0.0.1`: browsers take
every `*.localhost` name for the machine itself, but Windows does not, so that other programs find it there.

## Giving a site its database on SQL Server

A site that reaches SQL Server by Windows authentication, as example3 does, reaches it under IIS as its application
pool's account, `IIS AppPool\<name>`, which needs a login on the server and a user of the database. Once
`add-wasp-site.wlg` has added the pool, as one of the server's administrators:

```bat
weblingo run add-sql-server-login.wlg name=wasp.example3 database=WebLingoTodo
```

which gives that user the database's `db_datareader` and `db_datawriter` roles, to read and change its tables' rows, and
nothing more; `server=` names a SQL Server other than this machine's default instance. A database made anew, as
example3's `setup.wlg` makes its, has lost the user: run it again then.

The scripts remove nothing: a site, its pool, its certificate and its `hosts` entry are removed in IIS's manager, the
certificates' console and the `hosts` file. Nor do they renew a certificate: once one has expired, remove its binding,
`netsh http delete sslcert hostnameport=<host>:443`, and run `add-wasp-site.wlg` again, which makes another.
