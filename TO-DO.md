# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] Argument Errors That Name the Argument

`"tea".trim(1)` is WL3001 with a message saying a string was expected, which reads as if `"tea"` were the problem: the code is right (`trim` now takes an optional set of characters, so one argument is allowed, and an integer is a wrong kind of value, not a wrong number of arguments, WL2006), but the message does not say what should have been a string. A wrong kind of argument's message names the method and the argument, and says what it must be and what it was given: "`trim`'s argument must be a string of characters to trim, not an integer". Go through the built-in classes' methods that take arguments (strings', arrays', maps', `Bytes`', `DateTime`'s, `Regex`'s, and the standard modules' functions) for the same vague wording, and give each such message that form, with a test of the message, not only of its code.

EXPLAIN: How are other methods that expect a certain type of argument handle it? How can a programmer annotate a method to expect a certain type of value so that the compiler could issue a similar error message for user-defined functions?

2026-11-19 (Codex). The shared argument formatter was added in `59a5cab`. Commit `5518785` adds named diagnostics for invalid map and set arguments and documents runtime validation with `is` and `TypeError` for user-defined functions. Those changes passed the Windows checks on main. Array callbacks remain a separate case: `[1].map(1)` raises WL2005 at the attempted call, while an empty array never invokes the callback.

**Should higher-order array methods reject non-callable arguments up front with a named WL3001 diagnostic, including when the array is empty?**

## [QUESTION] Identify Standard Library Gaps

Identify everything that's not in the standard library but should be either because it would be very useful, or because other languages predominantly offer it. Implement it as new std: modules.

2026-11-19 (Codex). Reviewed the Standard Library Reference and the shipped modules. Proposed additions for scripts and WASP applications:

- Path manipulation for joining and splitting paths without touching the filesystem.
- URL parsing, resolution, and query encoding for HTTP clients and server-side applications.
- CSV parsing and writing for exchanging tabular data.
- Gzip and ZIP support for compressed data and archives, with explicit output limits.
- Seeded random numbers for reproducible tests and simulations. Secure random bytes are already in `std:crypto`.
- Subprocess execution for CLI scripts, with a separate decision about what WASP sites may launch.

**Should the first modules be path manipulation, URL handling, and CSV? If so, should paths use host rules or portable slash-separated rules, and should CSV start with arrays of strings or support header-keyed maps from the start?**

## [QUESTION] Implement What's Not in the Language

`Documents\weblingo-language-reference.md` has section "17. What is not in the language yet". Implement everything it describes, and remove that section.

2026-11-19 (Codex). The Language Reference lists streaming database rows and providers beyond SQLite and SQL Server. The architecture specification (§10, item 11) still leaves the streaming API open. A disposable async cursor would let callers read one row at a time and release the reader on early exit. PostgreSQL is a candidate for the first additional provider, followed by MySQL or MariaDB.

**Should streaming use a disposable cursor returned by `await db.rows(sql, values)`, where `await cursor.next()` returns a row map or `null` at end of input? Which additional database providers should ship first?**

# END-OF-TO-DO

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

