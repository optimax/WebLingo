# WebLingo/WASP Release Notes

WebLingo is a JavaScript-inspired scripting language for command-line programs and server-side web applications, and
WASP, Web Application Server Pages, serves its web pages. **This release is a beta**: for trying WebLingo and WASP and
telling us what you find, not yet for sites that others depend on.

## Which package to take

On **Windows**, WebLingo's zip, and, to serve web pages, WASP's zip of the same processor and kind after it:

| Your Windows | WebLingo | WASP |
|---|---|---|
| 64-bit, Intel's or AMD's processor, most PCs | `WebLingo-@VERSION@-win-x64-self-contained.zip` | `WASP-@VERSION@-win-x64-self-contained.zip` |
| On ARM, such as a Snapdragon laptop | `WebLingo-@VERSION@-win-arm64-self-contained.zip` | `WASP-@VERSION@-win-arm64-self-contained.zip` |
| 32-bit, or a site using a 32-bit COM component | `WebLingo-@VERSION@-win-x86-self-contained.zip` | `WASP-@VERSION@-win-x86-self-contained.zip` |

The `…-framework-dependent.zip` ones are smaller, and need .NET 10's runtime installed, and WASP's ASP.NET Core's too.
Install them as their `readme.md` says: [WebLingo's](https://github.com/optimax/WebLingo/blob/v@VERSION@/Installers/Windows/readme.md),
then [WASP's](https://github.com/optimax/WebLingo/blob/v@VERSION@/Installers/Windows/wasp-readme.md), which installs it
for IIS or, with `iis=no`, for `wasp serve` alone.

On **Linux**, one package installs `weblingo` and `wasp` together, with .NET's runtime:

| Your Linux | Package |
|---|---|
| Debian, Ubuntu and their kin, on x64 | `weblingo_@VERSION@_amd64.deb` |
| Debian, Ubuntu and their kin, on 64-bit ARM | `weblingo_@VERSION@_arm64.deb` |
| Any other with glibc, on x64 | `weblingo-@VERSION@-linux-x64.tar.gz` |
| Any other with glibc, on 64-bit ARM | `weblingo-@VERSION@-linux-arm64.tar.gz` |

Install it as [its readme](https://github.com/optimax/WebLingo/blob/v@VERSION@/Installers/Linux/readme.md) says. There
is no package for macOS.

To check a download, compare its SHA-256 with the one `SHA256SUMS` gives: on Linux, `sha256sum -c SHA256SUMS
--ignore-missing` in the folder of the downloads; on Windows, `Get-FileHash <file>` in PowerShell.

## What to know

- **Tried** on Windows 11 on x64 and on Ubuntu on x64; the packages for arm64 and x86 are built, not yet tried.
- **Nothing is frozen.** The language, its standard modules, a site's `wasp.config` and the bytecode may change from
  one beta to the next, and a site or a script may then need changing; each beta's notes say what. A compiled `.wlb`
  file is compiled again. WASP compiles pages itself, so that a site has nothing to recompile.
- **.NET.** WebLingo runs on .NET 10 for now, which the self-contained packages carry. A native runtime comes later.
- **Unsigned on Windows.** Windows warns that the programs come from an unknown publisher: **More info**, then **Run
  anyway**. Where Smart App Control is on, it blocks them.
- **Hosts.** WASP serves sites under IIS, through ASP.NET Core's module; with `wasp serve`, its own HTTP server,
  behind a reverse proxy for TLS; and with `wasp fastcgi`, behind nginx or Apache httpd.
- **Sites sharing one process** are isolated by their permissions, not as processes are: sites that do not trust each
  other each need a process of their own (the WASP User Manual, section 19).
- **Speed.** Faster than CPython at loops, slower at most else, and currently far slower than Node.js.

## Learn

- [Getting started with WebLingo](https://github.com/optimax/WebLingo/blob/v@VERSION@/Documents/getting-started-guide.md)
- [The language reference](https://github.com/optimax/WebLingo/blob/v@VERSION@/Documents/weblingo-language-reference.md)
  and [the standard library reference](https://github.com/optimax/WebLingo/blob/v@VERSION@/Documents/weblingo-standard-library-reference.md)
- [The WASP User Manual](https://github.com/optimax/WebLingo/blob/v@VERSION@/Documents/wasp-user-manual.md)

## Tell us what you find

Open an [issue](https://github.com/optimax/WebLingo/issues/new/choose): a bug, with what you did, what you expected and
what happened, or a question. Say which version (`weblingo --version`), which package and, for a site, which host. A
security vulnerability is reported privately instead, as the
[security policy](https://github.com/optimax/WebLingo/blob/v@VERSION@/SECURITY.md) says. Pull requests are not yet
accepted.

WebLingo is free and open source, under the MIT License.
