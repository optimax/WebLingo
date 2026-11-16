# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## After M0.13: The Next Milestone

The roadmap ends with M0.13, which is done (specification, section 9), so the next milestone is the owner's to choose. Hosting and the platform layer are the parts of the design not yet built (sections 6 and 7): `WebLingo.Hosting.AspNetCore` and `WebLingo.Platform.DotNet` exist, but no web host runs a page yet. Added by Claude, 2026-11-16.

**Q1, which comes next: (a) hosting: a web application served through the ASP.NET Core adapter, for IIS first (section 6), its `.wasp` pages and `.wl` route handlers compiled and cached in memory, its generations reloaded atomically (WL-T007), and `weblingo serve` to run it locally (section 8.4); (b) decimal128, the third numeric category (section 3; open question 3); (c) context-aware template escaping, for attributes, JavaScript, URLs and CSS (open question 9); (d) another, which you name.**

A1: (a)

# END-OF-TO-DO

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.
