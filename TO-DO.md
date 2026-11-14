# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] M0.10 Syntax: Disposal That Throws

Asked again: A5 of "M0.10 Syntax: `toString()` and Disposal That Throws" was left as "(". Until it is answered, the disposal goes on, (a), as M0.10.6 implemented it (`WsExceptions.DisposalGoesOn`). Added by Claude, 2026-11-13.

**Q1, disposal that throws: when B's `dispose()` throws while an instance of B is disposed, (a) A's `dispose()` still runs, as section 3.4 proposes, and an exception it throws replaces B's, as A13 has it for `using`; (b) A's does not run, and B's exception goes on.**

A1:

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
