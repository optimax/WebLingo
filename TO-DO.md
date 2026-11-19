# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] Identify Standard Library Gaps

Identify everything that's not in the standard library but should be either because it would be very useful, or because other languages predominantly offer it. Implement it as new std: modules.

2026-11-19 (Codex). Reviewed the Standard Library Reference and the shipped modules. Proposed additions for scripts and WASP applications:

- Path manipulation for joining and splitting paths without touching the filesystem, path-delimiter agnostic, i.e. it works both on Windows and Linux.
- URL parsing, resolution, and query encoding for HTTP clients and server-side applications.
- CSV parsing and writing for exchanging tabular data.
- Gzip and ZIP support for compressed data and archives, with explicit output limits.
- Seeded random numbers for reproducible tests and simulations. Secure random bytes are already in `std:crypto`.
- Subprocess execution for CLI scripts, with a separate decision about what WASP sites may launch.

**Q1. Should path manipulation, URL handling, and CSV be the first standard-library additions?**

**Q2. Path helpers should accept either separator. Should they emit the host separator or always emit `/`?**

**Q3. Should the first CSV API handle arrays of strings only or also support maps keyed by the header row?**

## [QUESTION] Implement What's Not in the Language

`Documents\weblingo-language-reference.md` has section "17. What is not in the language yet". Implement everything it describes, and remove that section.

2026-11-19 (Codex). The Language Reference lists streaming database rows and providers beyond SQLite and SQL Server. The architecture specification (§10, item 11) still leaves the streaming API open. A disposable async cursor would let callers read one row at a time and release the reader on early exit. PostgreSQL is a candidate for the first additional provider, followed by MySQL or MariaDB.

**Q1. Should `await db.rows(sql, values)` return a disposable cursor whose `await cursor.next()` gives a row map or `null` at end of input?**

**Q2. Which additional database provider should ship first?**

# END-OF-TO-DO

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

