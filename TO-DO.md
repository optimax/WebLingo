# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] M0.9: Collections

Arrays, maps and sets, with indexing, `length`, member access and `foreach` (specification, section 9), on both VMs, with their conformance tests, and a benchmark workload for property access (section 5.6). Its parts are defined in the specification once its syntax is approved. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.9 Syntax: Collections".

2026-11-13: and for the answers to "M0.9 Syntax: Optional Access and Set Literals".

2026-11-13: the syntax is approved but for sets and optional access (specification, section 3.6). M0.9 is split into the parts the specification defines in section 9 ("M0.9 parts"), each a task below, M0.9.1 to M0.9.6. Waits for them.

## [WORKING] M0.9.5: Sets

Sets, their members, `foreach` over them, and their text (specification, section 3.6), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.9 Syntax: Optional Access and Set Literals", which decide how a set is made and written.

2026-11-13: the answers are in (specification, section 3.6); it no longer waits.

Done (Claude, 2026-11-13): sets, `#{1, "a"}` and `#{}`, with `s.add(v)`, `s.has(v)`, `s.remove(v)`, `s.length` and `foreach`, on both VMs. Their values are what a map's keys may be (WS3001 otherwise, "cannot be in a set"), an equal one given twice is there once, and `write` writes a set as its literal, also inside other collections. A set is a heap object the collector traces, within the budget, equal only to itself. Choices of mine, in section 15.2 as proposed: `#{` is one token, and `#` alone stays invalid; a set shares a map's hash table. Every new conformance case gives the same result on both VMs. Build and 908 tests pass on Windows; Linux not tried.

## M0.9.6: Optional Access

`x?[k]` (specification, section 3.6), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.9 Syntax: Optional Access and Set Literals".

2026-11-13: the answers are in (specification, section 3.6); it no longer waits.

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
