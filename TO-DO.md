# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] Implement What's Not in the Language

`Documents\weblingo-language-reference.md` has section "17. What is not in the language yet". Implement everything it describes, and remove that section.

2026-11-19 (Codex). The Language Reference lists streaming database rows and providers beyond SQLite and SQL Server. The architecture specification (§10, item 11) still leaves the streaming API open. A disposable async cursor would let callers read one row at a time and release the reader on early exit. PostgreSQL is a candidate for the first additional provider, followed by MySQL or MariaDB.

Q1. Should `await db.rows(sql, values)` return a disposable cursor whose `await cursor.next()` gives a row map or `null` at end of input?

A1: Yes.

Q2. Which additional database provider should ship first?

A2: MySQL, PostgreSQL

2026-11-19 (Codex). The owner approved the cursor and chose MySQL before PostgreSQL. See the architecture specification, §7.11, “Streaming rows and additional providers”. Streaming landed in `a2c3fe0`. MySQL landed in `4b45d3b`. The parent task waits for the PostgreSQL adapter.

## [QUESTION] VS Code Debugging

Buiid an extension for VS Code to debug WebLingo.

2026-11-19 (Codex). The VS Code extension currently provides syntax highlighting. The register VM's compiled functions already carry source spans and local names. A debugger needs a VM pause and inspection interface and a host for the Debug Adapter Protocol.

**Q1. Should the first VS Code debugger launch local `.wlg` scripts on the register VM with breakpoints, stepping, a call stack and local variables, with WASP, the stack VM and attaching to running processes left for follow-up tasks?**

## Explain Argument Types in User-Defined Functions

EXPLAIN: How are other methods that expect a certain type of argument handle it? How can a programmer annotate a method to expect a certain type of value so that the compiler could issue a similar error message for user-defined functions?

## Add the PostgreSQL Database Provider

The owner chose PostgreSQL after MySQL. The local NuGet cache has no PostgreSQL provider. Proposed adapter dependency: Npgsql, with the existing ADO.NET connection contract and a `postgresql:` provider prefix.

**Q1. May I restore Npgsql from the configured NuGet feeds for the PostgreSQL adapter?**

A1: Yes.

## Choose the Next Standard-Library Additions

Path, URL, and CSV have shipped on local main. The remaining candidates are compression, seeded random numbers, and subprocess execution.

**Q1. Which of these should be the next standard-library addition?**

A1: As you list them. Don't ask trivial questions. Act autonomously.

# END-OF-TO-DO

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing  a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

## Minimal WebLingo Installer: Linux

Create the easiest installer for Linux, so that WebLingo could be executed like any scripting language (e.g. Python) on a Linux machine.

2026-11-18 (Claude): a script runs as `weblingo run script.wl` now; a first line `#!…` is a syntax error, and `weblingo script.wl` a usage error. Its .NET waits for "Minimal WebLingo Installer: Windows"'s Q1.

Q1, a script that runs by itself, as a Python one does: (a) a first line `#!/usr/bin/env weblingo`, which the lexer skips, a `.wl` file made executable then run by its name, and `weblingo script.wl` the same as `weblingo run script.wl`; (b) the first line only, `#!/usr/bin/env -S weblingo run`, Linux's `env -S` passing `run` on; (c) neither, `weblingo run script.wl` only.

A1: (a)

2026-11-19 (Claude): `weblingo script.wl` runs the script now, as `weblingo run script.wl` (`397c55f`), at the owner's request; the first line `#!/usr/bin/env weblingo` waits for this task.

Q2, the installer: (a) a `.tar.gz` with an `install.sh`, which puts WebLingo in `/opt/weblingo` and links `/usr/local/bin/weblingo`, run as `sudo ./install.sh`; (b) a `.deb` package, `sudo apt install ./weblingo.deb`; (c) both.

A2: (c)

## Rewrite Links

Rewrite the classic ASP application Links into WASP. Run locally, against the local SQL Server.

