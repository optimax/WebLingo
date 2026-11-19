# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] Implement What's Not in the Language

`Documents\weblingo-language-reference.md` has section "17. What is not in the language yet". Implement everything it describes, and remove that section.

2026-11-19 (Codex). The Language Reference lists streaming database rows and providers beyond SQLite and SQL Server. The architecture specification (§10, item 11) still leaves the streaming API open. A disposable async cursor would let callers read one row at a time and release the reader on early exit. PostgreSQL is a candidate for the first additional provider, followed by MySQL or MariaDB.

Q1. Should `await db.rows(sql, values)` return a disposable cursor whose `await cursor.next()` gives a row map or `null` at end of input?

A1: Yes.

Q2. Which additional database provider should ship first?

A2: MySQL, PostgreSQL

2026-11-19 (Codex). The owner approved the cursor and chose MySQL before PostgreSQL. See the architecture specification, §7.11, “Streaming rows and additional providers”. Streaming landed in `474e237`. The parent task waits for the MySQL and PostgreSQL adapters.

## [WORKING] Add the MySQL Database Provider

The owner chose MySQL as the first new provider. The local NuGet cache has no MySQL provider. Proposed adapter dependency: MySqlConnector, with the existing ADO.NET connection contract and a `mysql:` provider prefix.

Q1. May I restore MySqlConnector from the configured NuGet feeds for the MySQL adapter?

A1: Yes.

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

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

