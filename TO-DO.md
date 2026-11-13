# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] M0.10 Syntax: Classes, `dispose()` and `using`

M0.10 brings classes, the disposal protocol and `using` (specification, sections 3.2 and 3.4). The specification approves `dispose()`, its `destructor()` sugar and `using` (WS-L011), and gives section 3.4's example, but not the syntax of classes, nor several points of their meaning. These are my proposals, one decision each, each answerable with "yes" or a letter; whatever is decided is built on both VMs. Added by Claude, 2026-11-13.

**Q1, declaring a class: `class Name { constructor(a) { … } method(b) { … } }`, its methods written as a name, parameters and a body, without `function`, as section 3.4's example writes them. A class is in scope throughout its block, as a function is, and its name is a constant. Agreed?**

A1:

**Q2, making an instance: `new Name(a, b)` makes one and runs its `constructor` with those arguments; a class without a `constructor` takes none. Calling a class without `new`, or `new` on anything but a class, is WS2005. Agreed?**

A2:

**Q3, fields: (a) a field exists once a method assigns it, `this.file = …`, as section 3.4's example does, and reading one that was never assigned is WS3006; (b) fields are declared in the class body, `var file = null;`, each instance getting its own, and assigning or reading an undeclared one is WS3006.**

A3:

**Q4, `this`: a keyword, the instance a method or the constructor runs on; a function expression inside a method sees the method's `this`, as a closure sees a variable. `this` anywhere else is a compile-time error, with a new code, WS2009. Agreed?**

A4:

**Q5, methods as values: `obj.method` without a call (a) gives a function bound to `obj`, so `var f = obj.method; f();` calls it on `obj`; (b) is WS3001, "a method can only be called", as an array's `push` is now.**

A5:

**Q6, inheritance: `class B extends A { … }`; a method of B with the name of one of A's replaces it; `super(…)` in B's constructor runs A's, and `super.name(…)` calls A's method. A class extends at most one other (section 3.2). Agreed?**

A6:

**Q7, a constructor that does not call `super(…)`: (a) is a compile-time error when the base class has a `constructor` (a new code, WS2010); (b) runs the base's constructor first, with no arguments, as C# does.**

A7:

**Q8, visibility: every field and method is public, and there are no static members, for now. Agreed?**

A8:

**Q9, `x?.name`: (a) gives `null` when `x` is `null`, and when it has no field `name`, as `x?[k]` does for a missing key; (b) gives `null` only when `x` is `null`.**

A9:

**Q10, equality: an instance is equal only to itself (section 3.2: object equality uses identity), so two instances with equal fields are not equal. Agreed?**

A10:

**Q11, an instance as text: (a) `write` writes it as its class's name and its fields, `Point {x: 1, y: 2}`, in no defined order, one already being written as `Point {…}`; (b) only its class's name, `Point`.**

A11:

**Q12, `destructor()`: a class may declare `dispose()` or `destructor()`, which are the same method; both in one class is a compile-time error, with a new code, WS2011 (section 3.4 recommends it). Callers always call `dispose()`. Agreed?**

A12:

**Q13, disposing once: the runtime runs an object's disposal at most once; a second `dispose()` does nothing and gives `null` (section 3.4: disposal is idempotent). Agreed?**

A13:

**Q14, derived classes: (a) disposing an instance of B runs B's disposal, then A's, each once, without `super.dispose()`, as section 3.4 proposes; (b) B's disposal calls `super.dispose()` itself, as any other method would.**

A14:

**Q15, `using`: `using (var x = expr) statement` declares `x`, in scope in the statement only, and calls `x.dispose()` when the statement ends, by its end, `break`, `continue` or `return`; a `null` value is not disposed, and a value without `dispose` is WS3006 at the end. One resource per `using`; nest them for more. Agreed?**

A15:

**Q16, a runtime error inside `using`: WebLingo has no exceptions yet, so a runtime error stops the program. (a) As it stops, the VM disposes what the open `using` statements hold, innermost first, and an error in a disposal is reported after the first; (b) nothing is disposed: `using` disposes only on the ways out of Q15, until exceptions exist.**

A16:

**Q17, use after disposal: for a class written in WebLingo, (a) nothing is checked: the class checks its own state if it needs to; (b) calling any method but `dispose()` on a disposed instance is a runtime error, a new WS3008 (section 3.4 proposes a `ResourceClosedError`).**

A17:

## [IDLE] M0.10: Classes, `dispose()` and `using`

Classes, with single inheritance and no exposed prototype chain (specification, section 3.2), the disposal protocol, `dispose()` and its `destructor()` sugar, and `using` blocks (section 3.4, WS-L011), on both VMs. Their syntax and part of their meaning are not in the specification yet, and will be proposed as questions. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.9.

2026-11-13: M0.9 is done; now waits for the answers to "M0.10 Syntax: Classes, `dispose()` and `using`".

## [IDLE] M0.11: Templates

`.wsp` templates, with includes, layouts and parameterized components together (specification, section 13, WS-T001 to WS-T008), in `WebLingo.Templates`, with `WebLingo.Templates.Tests`, on both VMs, and a benchmark workload for template output (section 5.6). Their open contracts (the include and component syntax, context-aware escaping) will be proposed as questions. Added by Claude, 2026-11-13.

To propose with it, from the chat (2026-11-13): `<%= value %>` writes `null` as nothing, so that an optional read (`m?[k]`, `x?.name`) that finds nothing shows as blank, while `write` and `writeLn` go on writing `null`.

2026-11-13: waits for M0.10.

## [IDLE] M0.12: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WS-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

# END-OF-TO-DO
