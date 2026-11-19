# Scripts

Scripts that set up WebLingo, and WASP's sites, on a Windows machine with IIS, written in WebLingo and run by `weblingo`.
They reach Windows through its COM objects, `windows:com` (the Standard Library Reference, section 22): IIS's
configuration through IIS's own, `Microsoft.ApplicationHost.WritableAdminManager`; IIS's `appcmd`, `netsh`, `certutil`
and `icacls` through `WScript.Shell`; a relative path made full through `Scripting.FileSystemObject`; the processes
running through WMI; and certificates through CertEnroll's `X509Enrollment` objects. `weblingo.config`, beside them,
lists the objects they may make, as `windows:com` asks of a script. Files they copy, WebLingo's own as it is installed,
they copy through `std:fs`, without COM.

| Script | What it does |
|---|---|
| `install-weblingo.wl` | Installs WebLingo in `C:\Program Files\WebLingo`, or updates it, from the folder `WebLingo/publish.ps1` publishes it to, `WebLingo\publish\WebLingo`; the application pools of the sites whose `wasp` runs from there are stopped while their files are replaced, and started again |
| `add-weblingo-to-path.wl` | Puts `C:\Program Files\WebLingo` on the machine's `PATH`, once, so that consoles started after run `weblingo` and `wasp` by their names |
| `add-wasp-site.wl` | Adds a WASP site to IIS: its application pool, with no managed code, and its website, served by HTTPS on port 443 under a name of its own, with a certificate this machine trusts |
| `add-sql-server-login.wl` | Gives a site's application pool its database on SQL Server: a login for the pool's account, and a user of the database that reads and changes its rows |
| `windows.wl`, `iis.wl` | What the scripts share: running a program, the processes running, a certificate made and trusted; and IIS's configuration and its pools |

Each changes the machine, and so runs as administrator, in a console run as administrator, from this folder; run
otherwise, it stops before changing anything, but for `add-sql-server-login.wl`, which changes a SQL Server, and runs
as one of its administrators. Each may be run again: it keeps what is there, and adds what is missing.

## Installing WebLingo

With IIS turned on, and the ASP.NET Core Hosting Bundle for .NET 10 installed after it (`Examples/README.md`, example1,
"Deploying it to IIS"), publish WebLingo from the solution's folder, `WebLingo`:

```powershell
powershell -ExecutionPolicy Bypass -File publish.ps1
```

then, from this folder, as administrator, install it with the published `weblingo`, since the installed one cannot
replace itself while it runs, and put it on the machine's `PATH`:

```bat
..\WebLingo\publish\WebLingo\weblingo.exe run install-weblingo.wl
"C:\Program Files\WebLingo\weblingo.exe" run add-weblingo-to-path.wl
```

Run `install-weblingo.wl` again, the same way, after publishing a newer WebLingo: it updates every site at once.

## Adding a site

From this folder, as administrator, with the site's folder, the one that holds its pages and `web.config`, copied where
IIS is to serve it from, example1 and example2 copied to `C:\WWW`:

```bat
weblingo run add-wasp-site.wl name=wasp.example1 folder=C:\WWW\wasp.example1 host=example1.wasp.localhost
weblingo run add-wasp-site.wl name=wasp.example2 folder=C:\WWW\wasp.example2\site host=example2.wasp.localhost writable=C:\WWW\wasp.example2\database
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
`add-wasp-site.wl` has added the pool, as one of the server's administrators:

```bat
weblingo run add-sql-server-login.wl name=wasp.example3 database=WebLingoTodo
```

which gives that user the database's `db_datareader` and `db_datawriter` roles, to read and change its tables' rows, and
nothing more; `server=` names a SQL Server other than this machine's default instance. A database made anew, as
example3's `setup.wl` makes its, has lost the user: run it again then.

The scripts remove nothing: a site, its pool, its certificate and its `hosts` entry are removed in IIS's manager, the
certificates' console and the `hosts` file. Nor do they renew a certificate: once one has expired, remove its binding,
`netsh http delete sslcert hostnameport=<host>:443`, and run `add-wasp-site.wl` again, which makes another.
