# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [WORKING] M0.9 Syntax: Collections

M0.9 brings collections (specification, section 9). Their syntax, and part of their meaning, is not in the specification yet. These are my proposals, one decision each, in the order I would build them; each can be answered with "yes" or a letter. Everything here is built on both VMs (section 5.6, "Both VMs, for now"). Added by Claude, 2026-11-13.

Q1, arrays: `[a, b, c]` makes a new array of those values, which may be of any kinds, mixed; `[]` makes an empty one; there is no trailing comma, as in calls. Agreed?

A1: Yes.

Q2, indexing: `x[i]` reads element `i` of an array, counting from 0, and `x[i] = v` replaces it. The index must be an int64: a binary64, even `1.0`, is WS3001, since nothing converts implicitly. An index outside 0 to the length − 1 is a runtime error with a new code, WS3005 "index out of range". Agreed?

A2: Yes.

Q3, member access: `x.name` reads a member of a value, and `x.name(…)` calls one, a method. M0.9 gives collections and strings the members below; a member a value does not have is a runtime error with a new code, WS3006 "no such member" (section 3.2: missing property access throws). Agreed?

A3: Yes.

Q4, length: `x.length` is an array's number of elements, a map's or a set's number of entries, and a string's number of Unicode scalar values (section 3.3). It is an int64, and cannot be assigned. Agreed?

A4: Yes.

Q5, strings by index: `s[i]` is the string's `i`-th Unicode scalar value, as a string of one (`"abc"[1]` is `"b"`), not its number. Strings stay immutable, so `s[i] = v` is WS3001. Agreed?

A5: Yes.

Q6, growing arrays: `a.push(v)` adds `v` at the end, giving `null`; `a.pop()` removes the last element and gives it, and on an empty array is WS3005. Other array methods come when they are needed. Agreed?

A6: Yes.

Q7, writing a map: (a) `{ key: value, … }`, where a key written as a name or as a string literal is a string (`{ active: true }` is `{ "active": true }`, as the specification's examples write it), and `{}` is an empty map; other keys are added by indexing; (b) the same, but a key is any expression, so `{ active: true }` uses the value of the variable `active`; (c) no literal: maps are made by `new Map()`, which waits for classes (M0.10). At the start of a statement, `{` still opens a block in each case.

A7: (a).

Q8, a map's entries: `m[k]` reads the value of key `k`, and a missing key is a runtime error with a new code, WS3007 "no such key"; `m[k] = v` adds or replaces it; `m.has(k)` tells whether it is there; `m.remove(k)` removes it, giving whether it was there. Agreed?

A8: Yes.

Q9, a map's keys as names: (a) no: a map's keys are read only by indexing (`m["active"]`), and `.name` is kept for members, and later for classes' fields; (b) yes: `m.active` also reads the key `"active"`, and `m.active = v` sets it, for every key that is a name and not one of a map's own members (`length`, `has`, `remove`), as JavaScript's objects do.

A9: (a).

Q10, which values are keys: strings, int64s, Booleans and `null`, compared as `==` compares them (so an int64 is never equal to a string key). A binary64 key is WS3001, so that `1` and `1.0`, and NaN, raise no question; so are arrays, maps, sets and functions, until a use for keying by identity comes. The same values may be a set's. Agreed?

A10: Yes.

Q11, order: `foreach` visits a map's entries, and a set's values, in the order they were first added; replacing a value keeps its entry's place. Agreed?

A11: No, the order of keys in a map could well be undefined, and so is set's. Has table implementation does not lend itself to ordering.

Q12, `foreach` over each kind: `foreach (var item in a)` visits an array's elements in order, a set's values, and a string's Unicode scalar values, each as a string of one; `foreach (var (key, value) in m)` visits a map's entries, and `(key, value)` is allowed only there for now (section 3.5). `foreach (var item in m)` over a map is WS3001: a map's loop takes `(key, value)`. Anything else to loop over is WS3001. Agreed?

A12: Yes.

Q13, an array changed while `foreach` visits it: (a) is a runtime error at the loop's next step, with a new code, WS3008 "collection changed during foreach"; (b) the loop sees the change, as a `for` loop with an index would. The same rule holds for maps and sets.

A13: (b).

Q14, making a set: (a) a built-in function, `set(a)`, makes a set of an array's values, until classes give `new Set(…)`; (b) sets wait for classes (M0.10), since they have no literal.

A14: Can we invent a literal for sets? How is it done in other languages?

Q15, a set's members: `s.add(v)` adds `v`, giving whether it was new; `s.has(v)` tells whether `v` is in it; `s.remove(v)` removes it, giving whether it was there. Agreed?

A15: Yes.

Q16, equality: arrays, maps and sets are equal only to themselves, as functions are (section 3.2: object equality uses identity), so `[1] == [1]` is false. Agreed?

A16: Yes.

Q17, writing a collection as text: (a) `write` writes an array as `[1, "a", null]`, a map as `{"active": true}`, and a set as `set([1, 2])`, its strings quoted with their escapes, its collections written the same way, and one it is already writing as `[…]` or `{…}`; (b) it writes only `array`, `map` or `set`, as it writes a function as `function`.

A17: (a) with whatever we decide for literal sets.

Done (Claude, 2026-11-13): recorded as approved in the specification, a new section 3.6 ("Collections"), its decision register, and its section 8.4, with the codes WS3005 to WS3007 (WS3008 is not needed, after A13). A14's question is answered in "M0.9 Syntax: Optional Access and Set Literals", where how a set is made, and so written (A17), is decided.

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
