# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] M0.9 Syntax: Optional Access and Set Literals

Two follow-ups to "M0.9 Syntax: Collections". In the chat (2026-11-13), the owner chose new syntax for a read that may find nothing: `m?[k]` gives `null` where `m[k]` raises a missing key (WS3007), and `x?.name` extends it to members later, so that a template can show an absent value as blank. Whether a template writes `null` as nothing is decided with M0.11 ("M0.11: Templates"). And A14 asks for a set literal. Added by Claude, 2026-11-13.

**Q1, `?[` and a conditional operator: (a) `m?[k]`, as chosen, and as C# writes it; if WebLingo gets a conditional operator, `c ? a : b`, its parser tells `c ?[k]` and `c ? [k] : d` apart, as C#'s does; (b) `m?.[k]`, as JavaScript writes it, precisely so that `?[` never needs telling apart from a conditional operator.**

A1: (a).

**Q2, arrays and strings too: `a?[i]` and `s?[i]` give `null` for an index out of range, where `a[i]` and `s[i]` raise WS3005. Agreed?**

A2: Yes.

**Q3, a `null` before `?[`: (a) `x?[k]` with `x` null is `null` too, as in C# and JavaScript, so that `rows?[0]?["name"]` stops at the first `null`; (b) only a missing key or index gives `null`, and a `null` `x` is WS3001, as `x[k]` is.**

A3: (a).

**Q4, what `?[` still rejects: a key or index of the wrong kind (`m?[1.5]`, `a?["x"]`), and a value that cannot be indexed (`5?[0]`), stay WS3001: `?[` allows for an absent entry, not for a wrong program. Agreed?**

A4: Yes.

How other languages write sets (A14): Python writes `{1, 2, 3}`, but its empty set is `set()`, since `{}` is a dictionary; Clojure writes `#{1 2 3}`; Swift writes an array literal where a set is expected (`let s: Set = [1, 2, 3]`), and C#'s collection expressions do the same; JavaScript, Kotlin and Ruby have no literal (`new Set([1, 2, 3])`, `setOf(1, 2, 3)`, `Set[1, 2, 3]`). WebLingo cannot take Python's way as it is: with A7, `{ a, b }` would look like a map whose keys are the names `"a"` and `"b"`, and `{}` is already the empty map. Nor Swift's, without declared types.

**Q5, a set literal: (a) `#{1, 2, 3}`, and `#{}` for an empty set, as Clojure writes it, its elements any expressions; `#` is unused in WebLingo code; (b) no literal: a built-in function, `set([1, 2, 3])`, as A14's option (a) said. Either way, `write` writes a set as its literal (A17).**

A5: (a).

## [IDLE] M0.9: Collections

Arrays, maps and sets, with indexing, `length`, member access and `foreach` (specification, section 9), on both VMs, with their conformance tests, and a benchmark workload for property access (section 5.6). Its parts are defined in the specification once its syntax is approved. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.9 Syntax: Collections".

2026-11-13: and for the answers to "M0.9 Syntax: Optional Access and Set Literals".

2026-11-13: the syntax is approved but for sets and optional access (specification, section 3.6). M0.9 is split into the parts the specification defines in section 9 ("M0.9 parts"), each a task below, M0.9.1 to M0.9.6. Waits for them.

## [IDLE] M0.9.5: Sets

Sets, their members, `foreach` over them, and their text (specification, section 3.6), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.9 Syntax: Optional Access and Set Literals", which decide how a set is made and written.

## [IDLE] M0.9.6: Optional Access

`x?[k]` (specification, section 3.6), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.9 Syntax: Optional Access and Set Literals".

## [IDLE] M0.10: Classes, `dispose()` and `using`

Classes, with single inheritance and no exposed prototype chain (specification, section 3.2), the disposal protocol, `dispose()` and its `destructor()` sugar, and `using` blocks (section 3.4, WS-L011), on both VMs. Their syntax and part of their meaning are not in the specification yet, and will be proposed as questions. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.9.

## [IDLE] M0.11: Templates

`.wsp` templates, with includes, layouts and parameterized components together (specification, section 13, WS-T001 to WS-T008), in `WebLingo.Templates`, with `WebLingo.Templates.Tests`, on both VMs, and a benchmark workload for template output (section 5.6). Their open contracts (the include and component syntax, context-aware escaping) will be proposed as questions. Added by Claude, 2026-11-13.

To propose with it, from the chat (2026-11-13): `<%= value %>` writes `null` as nothing, so that an optional read (`m?[k]`, `x?.name`) that finds nothing shows as blank, while `write` and `writeLn` go on writing `null`.

2026-11-13: waits for M0.10.

## [IDLE] M0.12: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WS-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

# END-OF-TO-DO
