# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [WORKING] Use the heap's byte accounting for gzip and ZIP decompression

`gunzip` rejected a 2,048-byte result with `WL3003` when 979 heap units were free. A direct `Bytes` allocation of the same size succeeded. In `WebLingoCompressionModule.TryRead`, use the heap's allocation rule of one unit per eight bytes plus object overhead, and allow collection before reporting exhaustion. Keep the caller's `maxBytes` limit in bytes, cumulative across ZIP entries.

Verify that both `gunzip` and `unzip` accept output that fits under both limits, including when collection frees the necessary space. Check that exceeding `maxBytes` still reports `WL3001` and exhausting the heap still reports `WL3003`.

## Stop surviving subprocesses when capture times out

A launcher can exit while a descendant still holds an output pipe open. When `std:process.run` reaches its deadline, `DotNetProcessRunner.StopIfRunning` sees that the launcher has exited and skips termination. Track the launched process tree through capture so timeout and output-limit cleanup can terminate descendants even after the launcher exits.

Add a regression test with a short-lived launcher and a descendant that inherits an output pipe. Verify that the caller receives `TimeoutError` on timeout or `IOError` on output overflow, and that the descendant terminates in both cases.

## Migrate WebLingo's old Windows `.wl` file association

On accounts that registered WebLingo for `.wl`, running `Scripts/add-wlg-file-type.wlg` registers `.wlg` but leaves Explorer offering WebLingo for the old extension. Remove WebLingo's legacy entries, including its `OpenWithProgids` and `SupportedTypes` entries, while preserving other applications' registrations and the user's explicit default-app choice.

Verify an upgrade with both WebLingo and another application's entries already present for `.wl`. Check that `.wlg` opens with WebLingo when selected, that the other application's entries survive, and that running the migration twice produces the same result.

## Preserve a literal U+FEFF in the first CSV field

When the first field starts with U+FEFF, `WebLingoCsvModule.WriteRow` can emit the character unquoted. The parser then consumes it as a byte-order mark. A four-character field containing U+FEFF followed by `Tea` came back as the three-character string `Tea`. Quote the first field when its leading U+FEFF would otherwise appear at the start of the CSV text.

Add round-trip tests for a leading U+FEFF in the first data field and in the first header. Also check that parsing an input file with a genuine byte-order mark still removes the mark.

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

