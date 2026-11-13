# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] M0.10 Syntax: Static Members, `Object` and Exceptions

Follow-ups to "M0.10 Syntax: Classes, `dispose()` and `using`": A8 asks for static members, A10 for a base class `Object` whose `equal()` can be overridden, and A16 for exceptions, designed now. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

**Q1, static members: `static name(…) { … }` declares a method of the class itself, called as `Name.name(…)`, and `static var count = 0;` a field of the class, `Name.count`, set when the class is declared. In a static method, `this` is WS2009. Agreed?**

A1:

**Q2, statics and inheritance: (a) `B.name` finds A's static member when B has none of its own, as C# does; (b) each class has only its own static members.**

A2:

**Q3, `Object`: a class without `extends` extends `Object`, a built-in class with no fields and one method, `equal(other)`, which gives `this == other` and may be overridden; `new Object()` makes a plain instance. Agreed?**

A3:

**Q4, `==` and `equal`: (a) `==` stays identity for instances (A10), and `a.equal(b)` runs only where a program calls it; (b) `==` between two instances calls the left one's `equal`.**

A4:

**Q5, `equal` on other values: (a) only instances have `equal`; (b) every value has it, meaning `==` for values that are not instances (`"a".equal("a")` is true).**

A5:

**Q6, throwing: `throw value;` throws (a) any value; (b) only an instance of `Error`, or of a class extending it (WS3001 otherwise).**

A6:

**Q7, catching: `try { … } catch (e) { … }`, `try { … } finally { … }`, and `try { … } catch (e) { … } finally { … }`, with one `catch`, which catches every exception, and `e` a `var` in scope in its block. Typed catches may come later. Agreed?**

A7:

**Q8, `Error`: a built-in class, `new Error(message)`, with the fields `message` and `code` (a diagnostic code, or `null` for one a program throws), which programs may extend. Agreed?**

A8:

**Q9, runtime errors as exceptions: the runtime errors (WS2004 to WS2006 at run time, WS3001, WS3002, WS3005 to WS3007) become instances of `Error`, with their code and message, which a `catch` may catch; the execution's limits (WS3003) and a cancellation (WS3004) cannot be caught, and still stop the program. Agreed?**

A9:

**Q10, an exception nothing catches stops the program as a runtime error does now: exit code 3, reported where it was thrown, with its own code for a runtime error, and a new one, WS3008 "uncaught exception", for a value a program threw. Agreed?**

A10:

**Q11, `finally`: it runs on every way out of its `try` (its end, `break`, `continue`, `return`, an exception); a `break`, `continue` or `return` that would leave a `finally` block is a compile-time error, a new WS2012, so that a `finally` cannot drop an exception or a result. Agreed?**

A11:

**Q12, rethrowing: `throw;` alone, in a `catch`, throws the caught exception again, keeping where it was first thrown. Agreed?**

A12:

**Q13, `using` and exceptions: `using` disposes on every way out, an exception's included, as a `finally` would. An exception from `dispose()` while another is on its way out: (a) replaces it, as in C#; (b) is dropped, and the first one goes on.**

A13:

**Q14, how the VMs do it: (a) table-driven protected regions, as section 5.4 prefers: each function lists its `try` ranges and their handlers, and nothing runs on the way into a `try`; (b) instructions that push and pop a handler as the code enters and leaves a `try`. Open question 4 (section 10) keeps this choice for you.**

A14:

## [IDLE] M0.10: Classes, `dispose()` and `using`

Classes, with single inheritance and no exposed prototype chain (specification, section 3.2), the disposal protocol, `dispose()` and its `destructor()` sugar, and `using` blocks (section 3.4, WS-L011), on both VMs. Their syntax and part of their meaning are not in the specification yet, and will be proposed as questions. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.9.

2026-11-13: M0.9 is done; now waits for the answers to "M0.10 Syntax: Classes, `dispose()` and `using`".

2026-11-13: the syntax is approved, but for static members, `Object` and exceptions (specification, section 3.7). M0.10 is split into the parts the specification defines in section 9 ("M0.10 parts"), each a task below, M0.10.1 to M0.10.6. Waits for them.



## [WORKING] M0.10.2: Inheritance

`extends`, `super(…)`, `super.name(…)`, and the base's constructor run first when a constructor does not call `super(…)` (specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

## M0.10.3: Disposal

`dispose()` and `destructor()`, disposal once and chained from a derived class to its base, and `using` on its ways out but exceptions (specification, section 3.7, WS-L011), on both VMs. Added by Claude, 2026-11-13.

## [IDLE] M0.10.4: Static Members

Static methods and fields (specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: Static Members, `Object` and Exceptions".

## [IDLE] M0.10.5: `Object` and `equal()`

The base class `Object` and its `equal()` (specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: Static Members, `Object` and Exceptions".

## [IDLE] M0.10.6: Exceptions

`throw`, `try`, `catch` and `finally`, runtime errors as exceptions, and `using` when an exception leaves it (specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "M0.10 Syntax: Static Members, `Object` and Exceptions".

## [IDLE] M0.11: Templates

`.wsp` templates, with includes, layouts and parameterized components together (specification, section 13, WS-T001 to WS-T008), in `WebLingo.Templates`, with `WebLingo.Templates.Tests`, on both VMs, and a benchmark workload for template output (section 5.6). Their open contracts (the include and component syntax, context-aware escaping) will be proposed as questions. Added by Claude, 2026-11-13.

To propose with it, from the chat (2026-11-13): `<%= value %>` writes `null` as nothing, so that an optional read (`m?[k]`, `x?.name`) that finds nothing shows as blank, while `write` and `writeLn` go on writing `null`.

2026-11-13: waits for M0.10.

## [IDLE] M0.12: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WS-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

# END-OF-TO-DO
