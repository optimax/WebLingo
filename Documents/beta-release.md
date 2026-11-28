# WebLingo and WASP — The Beta Release

**Begun:** 28 November 2026, at commit `1c7ad3f`, version 0.26.0, at the owner's request ("help me finish up so that
WebLingo and WASP could be released to beta testers").
**Scope:** what a beta release of WebLingo and WASP is, what it still lacks, its parts, each a task of `TO-DO.md`,
how a beta is made, and what its testers are told. Kept to what is decided and built: a part's guesses are replaced by
the owner's answers as they come.

## 1. What a beta is

A release for beta testers, so that WebLingo and WASP are tried in real use by people other than the owner, before a
1.0 and before the native runtime the specification's public release waits for (section 12.1): the owner's direction,
28 November 2026, answering the specification's question of whether preview releases come earlier (section 12.5).
Nothing is frozen for it (the specification, the register, "Versions"; section 12 of `Documents/implementation.md`):
the language, the standard modules, `wasp.config` and WLBC may change from one beta to the next, and a site or a
script may need changing with them, a compiled `.wlb` compiled again. WASP writes no bytecode, so that a site has
nothing to recompile.

The repository is public already, with GitHub's issues turned on. A beta is then:

- a release on GitHub, marked a pre-release, of the packages the installers make, with their SHA-256 checksums;
- its notes: what it is, which package to take, what is known not to work, and how to report a problem;
- a way for testers to report what they find.

## 2. Where it stands

On 28 November 2026, at `1c7ad3f`:

- Every recommendation of `Documents/production-readiness.md` is done, but the pilot, "WASP Pilot: the Forum in Real
  Use", which runs, and "A 1.0 Scope and a Compatibility Policy", deferred until the owner says the work is done.
- CI passes on Windows and Linux, its last five runs in a row, the Linux packages installed and a site served behind
  nginx and Apache among its jobs.
- The installers make WebLingo's and WASP's zips for Windows, six of each, x64, x86 and arm64, self-contained or
  framework-dependent (`Installers/Windows/package.ps1`), and WebLingo's archive and Debian package for Linux on x64
  and arm64 (`Installers/Linux/package.sh`), each with its `readme.md`, `LICENSE` and `THIRD_PARTY_NOTICES.md`.
- There is no release, no tag, no checksum file and no release notes; the Windows programs are not signed.

## 3. Its parts

Each a task of `TO-DO.md`, of the title "Beta Release, Part N: …", under the task "Beta Release for Testers".

1. **Tried as a beta tester would.** Every package made from `main`; WebLingo's zip and WASP's unpacked, not installed,
   and the root `README.md`, the getting-started guide and the WASP User Manual's first site followed from them on
   Windows, and the Linux archive installed in WSL and followed there; every step that fails, or reads wrongly to
   someone new, fixed.
2. **The packages, their checksums and the notes.** One way to make a beta's every package from a tagged commit, with
   a `SHA256SUMS` file beside them, and the release notes, `Installers/release-notes.md`, which the release's page
   carries; the version and the tag a beta has.
3. **How testers report.** GitHub's issue forms for a bug and for a question, and `CONTRIBUTING.md`, saying how to
   report and what is accepted.
4. **The release candidate tried here.** The candidate's zips installed on this machine as a tester installs them,
   `Install WebLingo.bat` and WASP's `iis-install.wlg`, and its sites tried under IIS: the examples, the forum, and a
   debugger attached to a running site.
5. **The release.** The name checked, the tag made, the release drafted with its packages, checksums and notes, and
   published by the owner.

## 4. What its testers are told

The release notes' part that does not change from one beta to the next, kept here until part 2 writes the notes:

- **Platforms.** Windows on x64, x86 and arm64; Linux on x64 and arm64, a Debian package for Debian and Ubuntu, an
  archive for the others. Not macOS. Tried on Windows 11 on x64, and on Ubuntu on x64, CI's and WSL's; the others are
  built, not tried.
- **.NET.** WebLingo runs on .NET 10 for now, which the self-contained packages carry; the framework-dependent ones
  need .NET 10's runtime installed. A native runtime, without .NET, comes later.
- **Nothing is frozen.** The language, the standard modules, `wasp.config` and the bytecode may change from one beta to
  the next; each beta's notes say what changed that a site or a script must follow. A `.wlb` file is compiled again.
- **Unsigned on Windows.** SmartScreen warns of an unknown publisher (**More info**, **Run anyway**), and Smart App
  Control, where it is on, blocks the programs.
- **Hosts.** IIS, through ASP.NET Core's module; `wasp serve`, WebLingo's own HTTP server, behind a reverse proxy for
  TLS; `wasp fastcgi` behind nginx or Apache httpd.
- **Isolation.** Sites sharing one process are isolated by their permissions, not as processes are: mutually untrusted
  sites each need a process of their own (the WASP User Manual, section 19, "Sites sharing a server").
- **Speed.** As the specification's section 5.6 measures it: faster than CPython at loops, slower at most else, far
  slower than Node.js.
- **Reporting.** As part 3 settles it.
