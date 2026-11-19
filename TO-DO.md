# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## Change the WebLingo Source Extension to `.wlg`

Replace `.wl` with `.wlg` throughout WebLingo. Rename scripts, modules, WASP route handlers, and paths such as `Examples.wl`. Update CLI and WASP file discovery, import resolution, editor grammars and associations, the Windows file-association script, installers, build and CI scripts, examples, tests, and documentation. Wolfram Language already uses `.wl` for package source.

## Error Reporting

```
function two(a, b) { return a + b; }
two(1);
```

This is an example of an error being reported:

    ... : error WL2006: 'two' takes 2 arguments, not 1.

 The error should be loud and bold: `ERROR`, and it should name the exception that occurred, not just give the error code:

        ERROR WL2006 (ArgumentError): 'two' takes 2 arguments, not 1.

## VS Code Debugging

Buiid an extension for VS Code to debug WebLingo.

## Regex Timeout

The Language Reference says: A pattern that takes more than a second to match, as some do on some text, stops with a `TimeoutError`.  Make it configurable via a command line flag, something like `--timeout 300`, the time being given in milliseconds.

## Unnecessary Imports in Language Ref

The Language Reference is full of examples where this line is present:

    import { writeLn } from "std:system";

This is unnecessary since `std:system` is supposed to be implicitly imported.

## Explain Argument Types in User-Defined Functions

EXPLAIN: How are other methods that expect a certain type of argument handle it? How can a programmer annotate a method to expect a certain type of value so that the compiler could issue a similar error message for user-defined functions?

## Indexes and Integers

Python's `items[-1]` reads the last item. WebLingo rejects that index with `WL3005`. Make it work in WebLingo. Also, take into account similar indexing scheme with C#.

## Implement What's Not in the Language

`Documents\weblingo-language-reference.md` has section "17. What is not in the language yet". Implement everything it describes, and remove that section.

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

