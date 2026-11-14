# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] M0.10 Syntax: Disposal That Throws

Asked again: A5 of "M0.10 Syntax: `toString()` and Disposal That Throws" was left as "(". Until it is answered, the disposal goes on, (a), as M0.10.6 implemented it (`WsExceptions.DisposalGoesOn`). Added by Claude, 2026-11-13.

**Q1, disposal that throws: when B's `dispose()` throws while an instance of B is disposed, (a) A's `dispose()` still runs, as section 3.4 proposes, and an exception it throws replaces B's, as A13 has it for `using`; (b) A's does not run, and B's exception goes on.**

A1:

## [IDLE] M0.10: Classes, `dispose()` and `using`

Classes, with single inheritance and no exposed prototype chain (specification, section 3.2), the disposal protocol, `dispose()` and its `destructor()` sugar, and `using` blocks (section 3.4, WS-L011), on both VMs. Their syntax and part of their meaning are not in the specification yet, and will be proposed as questions. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.9.

2026-11-13: M0.9 is done; now waits for the answers to "M0.10 Syntax: Classes, `dispose()` and `using`".

2026-11-13: the syntax is approved, but for static members, `Object` and exceptions (specification, section 3.7). M0.10 is split into the parts the specification defines in section 9 ("M0.10 parts"), each a task below, M0.10.1 to M0.10.6. Waits for them.

2026-11-13: static members, `Object` and exceptions approved; `toString()` (A3) is a seventh part, M0.10.7, a task below. Waits for its parts.



## [WORKING] M0.10.7: `toString()`

`Object`'s `toString()` (A3 of "M0.10 Syntax: Static Members, `Object` and Exceptions"; specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: `toString()` and Disposal That Throws".

2026-11-13: answered; next.

Done (Claude, 2026-11-13): every value's `toString()`, `Object`'s giving the text `write` writes, which a class may override; `write`, `writeLn` and a `toString()` of `Object`'s then write what the override gives, wherever the instance is, and anything but a string is WS3001 (A1 to A4). The VMs run an override as a call on their own stacks, the text being built with a stack of its own that waits for it (`WsTextBuilder`); one new operation, `ToText`, which only `Object` uses. A program that overrides no `toString()` writes as before, and the benchmark's times did not change beyond the noise. Choices of mine, in section 15.2 as proposed (row "`toString()`"): `null.toString()` is `"null"` and `Point.toString()` `"class Point"`; a `toString()` with parameters is WS2006 where the text is written; `Object`'s `toString()` writes an instance that holds itself as `Name {…}`. Every new conformance case gives the same result on both VMs. Build and 1,206 tests pass on Windows; Linux not tried.

## [IDLE] Typed Catches

A `catch` that catches only some exceptions (A7 of "M0.10 Syntax: Static Members, `Object` and Exceptions"; specification, section 3.7), on both VMs. Its syntax, and how a `catch` names what it catches, will be proposed as questions once exceptions are implemented. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.10.6.

## [IDLE] M0.11: Templates

`.wsp` templates, with includes, layouts and parameterized components together (specification, section 13, WS-T001 to WS-T008), in `WebLingo.Templates`, with `WebLingo.Templates.Tests`, on both VMs, and a benchmark workload for template output (section 5.6). Their open contracts (the include and component syntax, context-aware escaping) will be proposed as questions. Added by Claude, 2026-11-13.

To propose with it, from the chat (2026-11-13): `<%= value %>` writes `null` as nothing, so that an optional read (`m?[k]`, `x?.name`) that finds nothing shows as blank, while `write` and `writeLn` go on writing `null`.

2026-11-13: waits for M0.10.

## [IDLE] M0.12: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WS-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

# END-OF-TO-DO
