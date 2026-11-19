# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## BytesBuilder Extendability

A builder prints as `BytesBuilder:[104, 105]`, and nothing can extend `BytesBuilder` now, the same as `Bytes`. Previously a class could extend it — make it extensible again.

All standard classes must behave like real classes from the script's perspective.

## Append Mode

`read(count)` returns at most 16 MiB at a time, and there’s no `"ab"` (append bytes) mode yet. Support append mode.

## Argument Errors That Name the Argument

`"tea".trim(1)` is WL3001 with a message saying a string was expected, which reads as if `"tea"` were the problem: the code is right (`trim` now takes an optional set of characters, so one argument is allowed, and an integer is a wrong kind of value, not a wrong number of arguments, WL2006), but the message does not say what should have been a string. A wrong kind of argument's message names the method and the argument, and says what it must be and what it was given: "`trim`'s argument must be a string of characters to trim, not an integer". Go through the built-in classes' methods that take arguments (strings', arrays', maps', `Bytes`', `DateTime`'s, `Regex`'s, and the standard modules' functions) for the same vague wording, and give each such message that form, with a test of the message, not only of its code.


# END-OF-TO-DO

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

