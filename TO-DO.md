# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] CLI's VM

The `implementation.md` seems to imply that the WebLingo CLI is only capable of running the register VM, while WASP is running both, as a choice. The intent was to make it possible for either exe to run either VM, based on configuration. If the choice of VM is not implemented that way, implement it.

**Approve "vm": "register" or "stack" in weblingo.config and wasp.config, using the register VM for source when no setting is given? The proposal applies to weblingo run/check/compile and wasp run/check/serve/fastcgi. The existing explicit compile flag would override the setting. With no setting, compiled files would still select the VM from their verified header. With a setting, a compiled file for the other VM would be refused. The VS Code debugger would keep its approved register-only behavior.**

## [QUESTION] Testing Framework

Write a standard testing framework for WebLingo code, so that test classes can easily be written in WebLingo and run via a built-in test harness.

**Approve weblingo test <file.wlg> [more files], with each module exporting a tests map from case names to zero-argument functions? A test class can register a case with a function such as () => new ArithmeticTests().adds(), giving that case a fresh instance. The harness would await both synchronous and asynchronous cases, report each failure and a summary, and exit nonzero on failure. Start std:test with assert(condition, message = null), equal(actual, expected, message = null) and async throws(action, expectedClass = Error). If you prefer automatic discovery of exported classes and test methods, please specify the discovery convention.**

## Example Person's Name

Whenever the name `Ann` is mentioned, in the example code, in the documentation, etc., change it to `Anna`.

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

