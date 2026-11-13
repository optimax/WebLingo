# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] M0.10 Syntax: `toString()` and Disposal That Throws

Follow-ups to "M0.10 Syntax: Static Members, `Object` and Exceptions": A3 adds `toString()` to `Object`, and exceptions raise what a disposal does when one of its methods throws. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

**Q1, `toString()` when a class does not override it gives (a) the text `write` writes, `Point {x: 1, y: 2}`; (b) the class's name, `Point`, as C#'s `ToString()` does.**

A1: (a).

**Q2, `write`, `writeLn` and a template's `<%= %>` given an instance whose class overrides `toString()`: (a) write what it returns, wherever the instance is, inside an array or a map too, as C#'s `Console.Write` does; (b) write its class and fields as now, `toString()` running only where a program calls it.**

A2: (a).

**Q3, if Q2 is (a): a `toString()` that returns anything but a string is (a) WS3001 where it is written; (b) written as the value it returns.**

A3: (a).

**Q4, `toString()` on other values: (a) only instances have it; (b) every value has it, giving the text `write` writes (`n.toString()` is `"5"` when `n` is 5), so that `"n = " + n.toString()` joins a number to a string, which `+` alone does not (section 3.5).**

A4: (b).

**Q5, disposal that throws: when B's `dispose()` throws while an instance of B is disposed, (a) A's `dispose()` still runs, as section 3.4 proposes, and an exception it throws replaces B's, as A13 has it for `using`; (b) A's does not run, and B's exception goes on.**

A5: (

## [IDLE] M0.10: Classes, `dispose()` and `using`

Classes, with single inheritance and no exposed prototype chain (specification, section 3.2), the disposal protocol, `dispose()` and its `destructor()` sugar, and `using` blocks (section 3.4, WS-L011), on both VMs. Their syntax and part of their meaning are not in the specification yet, and will be proposed as questions. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.9.

2026-11-13: M0.9 is done; now waits for the answers to "M0.10 Syntax: Classes, `dispose()` and `using`".

2026-11-13: the syntax is approved, but for static members, `Object` and exceptions (specification, section 3.7). M0.10 is split into the parts the specification defines in section 9 ("M0.10 parts"), each a task below, M0.10.1 to M0.10.6. Waits for them.

2026-11-13: static members, `Object` and exceptions approved; `toString()` (A3) is a seventh part, M0.10.7, a task below. Waits for its parts.



## [WORKING] M0.10.5: `Object` and `equal()`

The base class `Object` and its `equal()` (specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: Static Members, `Object` and Exceptions".

2026-11-13: answered; waits for M0.10.4.

Done (Claude, 2026-11-13): `Object`, with `equal(other)` giving `this == other`, which a class may override and call as `super.equal(…)`; `new Object()`; every class without `extends` extends it. No new operation, and no change to either VM: `Object` is a class written in WebLingo, which the compiler adds to a program that uses it, so programs without classes compile as before, and `new` runs no extra call, since a class that extends `Object` without saying so does not run its empty constructor (`objects` takes the same time). Choices of mine, in section 15.2 as proposed (row "`Object`"): a program may declare its own `Object`, which classes do not extend; `super` is allowed in every class now, so `super.m()` with no `m` in `Object` is WS3006 when it runs, where it was WS2009 (one conformance case changed for this). `toString()` is M0.10.7. Every new conformance case gives the same result on both VMs. Build and 1,126 tests pass on Windows; Linux not tried.

## [IDLE] M0.10.6: Exceptions

`throw`, `try`, `catch` and `finally`, runtime errors as exceptions, and `using` when an exception leaves it (specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: Static Members, `Object` and Exceptions".

2026-11-13: answered; waits for M0.10.5, whose `Object` `Error` extends.

## [IDLE] M0.10.7: `toString()`

`Object`'s `toString()` (A3 of "M0.10 Syntax: Static Members, `Object` and Exceptions"; specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: `toString()` and Disposal That Throws".

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
