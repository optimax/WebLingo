# WebLingo — Consolidated Architecture Specification

**Document:** `weblingo-architecture.md`  
**Status:** Consolidated architectural baseline through M0.6, including approved WL-T001–WL-T008 template composition and the WebLingo project name (12 November 2026)  
**Implementation status:** M0.6.1 solution scaffold generated (22 October 2026); renamed to WebLingo on 12 November 2026; M0.6.2–M0.6.7 implemented on 12 November 2026, a working compiler and register VM for the M0.6 subset, built and tested on Windows and Linux (§14, §15).  
**Convention:** **Approved** means explicitly accepted by the project owner. **Proposed** means an implementation recommendation requiring confirmation or validation. **Provisional** means deliberately undecided.

## 1. Executive overview

WebLingo is a JavaScript-inspired, native-oriented, cross-platform **server-side scripting language** designed to combine the low-friction deployment experience of PHP and classic ASP with modern language semantics, secure defaults, efficient execution, and first-class application development.

- Source scripts use **`.wl`**; HTML/server template pages use **`.wlp`**.
- Page-oriented and application-oriented programming are equally first-class.
- The language is JavaScript-inspired but **does not promise ECMAScript compatibility**.
- The initial compiler, bytecode VM, runtime and hosting stack are implemented in **C#/.NET 10**.
- The long-term goal is to compile that C# implementation with the project's **independent Native C# compiler**, eliminating the .NET runtime dependency **without a C++ rewrite**. Native C# and WebLingo remain separate languages and runtime systems.
- The execution engine and platform services must be independent of IIS, ASP.NET Core, FastCGI, Kestrel, Windows and Linux.
- IIS is the priority deployment target; FastCGI and standalone HTTP hosting are architectural targets.

The approved project name is **WebLingo** (formerly WebScript). The intended project domain is **`weblingo.dev`**. The solution is `WebLingo.slnx`, project and namespace roots use `WebLingo`, and the CLI executable is `weblingo`. Scripts and modules are `.wl` files, templates `.wlp` files; specification IDs and diagnostic codes have the prefix `WL` (`WL-BC002`, `WL1001`), and type names in source the prefix `WebLingo` (`WebLingoLexer`). They kept WebScript's `ws` (`.ws`, `.wsp`, `WS`, `Ws`) until the owner had them renamed, with no backward compatibility, on 14 November 2026.

### 1.1 Decision register

| ID / area | Decision | Status |
|---|---|---|
| Product direction | JavaScript-inspired, non-ECMAScript-compatible, native-oriented server scripting | Approved |
| Project naming | WebLingo, with `weblingo.dev` as the intended project domain; `.wl` and `.wlp` files, `WL` IDs and codes, and `WebLingo` type names in source, in place of WebScript's `.ws`, `.wsp`, `WS` and `Ws` (§1) | Approved (the names 14 November 2026) |
| WL-G001 | Free and open-source project direction; release timing, scope, and MIT licensing proposal | Direction confirmed; release and licensing details proposed |
| Implementation | C#/.NET 10 initially, later Native C# compilation | Approved |
| WL-017 | Native C# portability contract and replaceable adapters | Approved |
| WL-002.3 | `var` mutable, `const` immutable; both block-scoped, in scope from their declaration to the end of the block; no `let` declaration keyword | Approved |
| Statements, operators and literals | The syntax M0.7 implements (§3.5): blocks, `true`, `false` and `null`, comparisons and logic, `if`, loops, `foreach`, strings, and functions, hoisted in their block; a hoisted function reading a variable before its declaration has run is a runtime error (WL2004) | Approved (12 November 2026; the hoisted function's read, 13 November 2026) |
| Collections | Arrays, maps and sets, with indexing, members, `length`, `foreach`, identity, text, set literals and optional access (§3.6) | Approved (13 November 2026) |
| Classes and disposal | Classes, instances, fields, `this`, methods, inheritance, `x?.name`, text, `dispose()` and `destructor()`, disposal once and chained, `using`, static members, `Object` and `equal()`, exceptions, `toString()`, a disposal that throws, typed catches, and the classes of values (§3.7) | Approved (13 November 2026) |
| Asynchrony | `async` functions, `await` of any value, tasks of the class `Task`, exceptions nothing awaits, the end of an execution, frames kept on the heap, cancellation, `async` disposal, templates' `await`, one worker per execution, scripts as `Module`s, and `t.forget()` (§3.8) | Approved (13 November 2026) |
| Modules | `import`, static and dynamic; a module run once per execution; `export`; paths; cycles; `param`; a script's parameters; the standard library and the built-ins as modules (§3.9) | Approved (13 November 2026) |
| Templates (M0.12) | The language is independent of templates; a template compiler extends the language's, with a program of its own; text, statements across blocks, escaping, `null`, line breaks, `include`, layouts and their parameters, components found at run time and their declared parameters, and codes WL5001 on (§13, "Contracts"); the technology's name, `import`, default values, and asynchrony, which comes first, still open | Approved (13 November 2026) |
| M0.1 | Mandatory semicolons; no automatic semicolon insertion | Approved |
| M0.1 | Numeric categories int64, IEEE binary64, decimal128 | Approved |
| M0.1 | String indexing and `length` count Unicode scalar values | Approved |
| M0.2 | WebLingo tracing GC, initially nonmoving mark-and-sweep | Approved |
| M0.1/M0.2 | `using` blocks for deterministic resource cleanup | Approved |
| WL-L011 | Canonical `dispose()`; optional `destructor()` declaration sugar | Approved |
| WL-T001–WL-T008 | Includes, layouts, and parameterized components all required in the first template-capable release | **Approved** |
| Template named sections | Optional page-specific layout sections | Proposed extension; not yet approved |
| WL-BC002 | Register VM preferred, stack VM benchmark required before final decision | **Provisional** |
| WL-BC002, until decided | Both VMs are kept, and every new feature is built on both, until property access, async suspension, template output and I/O-bound workloads can be measured too; the choice is made then (§5.6) | Approved (13 November 2026) |
| WL-BC002 comparison | The stack VM in a project of its own, `WebLingo.VM.Stack`, its code generator in `WebLingo.Compiler`; a console benchmark, `WebLingo.Benchmarks`, timing with `Stopwatch`, with no new package; the register VM's dispatch tuned for speed before the two are compared (§5.6) | Approved (13 November 2026) |
| M0.3 encoding | Variable-length register operands; disk byte offsets / decoded instruction indices | Proposed |
| M0.3 numbers | IEEE 754 decimal128 BID binary representation | Proposed |
| M0.3 exceptions | Table-driven exception and cleanup regions | Approved (13 November 2026, §3.7) |
| M0.3 dispatch | Initial C# `switch` interpreter | Proposed |
| WL-H009 | ASP.NET Core adapter first for IIS | Approved |
| WL-H012 | Worker-affine VM request execution contexts | Approved |
| WL-H013 | Logical application isolation in a shared runtime | Approved |
| WL-H004 | Atomic application generations for hot reload | Approved |
| WL-P007 | WebLingo-owned asynchronous operation model | Approved |
| WL-P004 | Both synchronous and asynchronous filesystem operations | Approved |
| WL-P013 | Worker-local mutable heaps, explicit cross-worker transfers | Approved |
| WL-P016 | Native portability inventory and CI checks | Approved |
| WL-S001–S016 | M0.6 solution scaffolding and initial implementation contracts | Approved |
| M0.6 implementation default | Register VM first; stack comparison later | Adopted M0.6 default; VM architecture still provisional |
| M0.6 testing default | xUnit on .NET 10 | Adopted M0.6 default |
| M0.6 implementation choices | Lexical, syntax, runtime and CLI choices made where this specification is silent, while implementing M0.6.2 onward (§15.2) | Each row of §15.2 has its own status; the lexical and syntax ones, and the binding ones but "Compilation stages", the bytecode ones but "Compiler output", and the runtime and CLI ones asked about, approved 12 November 2026; the rest are Proposed |
| Repository documents | One copy of this specification, `Documents/weblingo-architecture.md`; documents in the repository's root `Documents/` folder; a shallow folder structure, with no `docs/architecture/` folder; the whole repository may become public (§8.2) | Approved (12 November 2026) |
| Milestones after M0.8 | M0.9 collections, M0.10 classes and disposal, M0.11 asynchrony, M0.12 `.wlp` templates, M0.13 WBC, in that order (§9) | Approved (13 November 2026; asynchrony put before templates the same day) |

**Approval boundary:** M0.6 was approved as a specification. This does not imply the repository or implementation already exists. Detailed M0.3 encoding choices were recommended but not separately approved; the architectural decision between stack and register VMs remains open.

---

## 2. Product and deployment model (WL-001–WL-016 overview)

### 2.1 Goals

1. Simple deployment: copy `.wl` and `.wlp` files into a configured site; runtime compilation and caching without Visual Studio, MSBuild or npm at deployment.
2. Strong server-side language with lexical scoping, closures, classes, modules, async execution and deterministic resource cleanup.
3. Secure template rendering by default; capabilities constrain filesystem, network, database and native extension access.
4. Portable compiler and execution engine; host-neutral interfaces.
5. Fast startup and request execution, bounded memory and streaming I/O.
6. Hot reload without exposing partially initialized application state.
7. Versioned, verified portable bytecode (WBC).

### 2.2 Source types and template syntax

| Syntax | Meaning |
|---|---|
| `.wl` | WebLingo module or script |
| `.wlp` | HTML plus server-side WebLingo template |
| `<% statements %>` | Execute statements |
| `<%= expression %>` | Output escaped expression |
| `<%- expression %>` | Output raw expression (security-sensitive) |
| `<%# comment %>` | Server-side template comment |

Template composition includes **mandatory first-release includes, layouts, and parameterized components** (WL-T001–WL-T008; see §13). Template escaping is **on by default**. The exact context-sensitive escaping policy (HTML text versus attributes, JavaScript, URLs, CSS) still needs a detailed normative contract; raw output must be explicit and subject to security review. Templates should stream output with backpressure and support asynchronous operations.

### 2.3 Modules and application structure

- Module graph and route table are associated with an application generation.
- Source compilation and bytecode caching are runtime services.
- `.wlp` pages and `.wl` route handlers are equally supported.
- A generation compiles its modules once; a module runs once per execution, a request's, its instance the request's own (§3.9, the owner's decision of 14 November 2026, in place of running once per generation); a module that fails to compile prevents the generation from becoming active.
- Standard library and database access use provider interfaces.

---

## 3. Language semantics (WL-002.3, WL-L001–WL-L011)

### 3.1 Declarations and scope — approved

```javascript
var count = 1;      // mutable lexical binding
const limit = 10;   // immutable lexical binding
```

- `var` has **modern JavaScript `let`-style block scope**, not historical JavaScript function scope.
- `const` has the same lexical scope but disallows reassignment.
- `let` is **not** a WebLingo declaration keyword.
- A declaration takes effect where it stands, after its initializer: from there to the end of the block, the name means the declared variable; before it, and within the initializer, the name means what it means outside the block (an outer declaration, or nothing, which is an error). There is no temporal dead zone.
- Redeclaration in the same scope is an error; nested shadowing is allowed.
- Implicit global creation is prohibited.
- Per-iteration bindings are correctly captured by closures.
- Every declaration, `var` and `const` alike, requires an initializer: `var x;` is a syntax error. A variable therefore always has a value (there is no `undefined`).
- `const` does not deeply freeze referenced objects.
- Simple statements require semicolons; there is **no ASI**.

### 3.2 Value categories and operations

The language model proposed in M0.1/M0.2 includes:

| Category | Semantics |
|---|---|
| `null` | Single null value; no `undefined` |
| Boolean | Strict Boolean conditions; no implicit truthiness |
| Integer | Signed 64-bit, checked overflow |
| Number | IEEE 754 binary64 |
| Decimal | Decimal128 for exact decimal arithmetic, literal suffix `m` |
| String | Immutable Unicode string; scalar-value indexing and length |
| Bytes | Binary data, distinct from strings |
| Array | Indexed collection |
| Map / Set | Keyed collections |
| Object / Class | Reference identity, class metadata |
| Function | Callable closure |
| Task | Async computation abstraction |
| Native resource | Handle with deterministic disposal |

Additional proposed semantics:

- `7 / 2` yields `3.5`, not integer truncation.
- Integer overflow throws rather than silently wrapping.
- Decimal/binary floating-point mixing requires explicit conversion.
- Object equality uses identity unless a specific operation says otherwise.
- Missing property access throws; optional access may return `null`.
- Single class inheritance, no exposed JavaScript prototype chain.
- Closures capture binding cells, not merely initial values.
- Functions have defined arity/default/rest behavior; `switch` has no implicit fallthrough.

These details were included in the M0.1/M0.2 design; the **five explicitly approved M0.1/M0.2 choices** are semicolons, numeric categories, tracing GC, `using`, and Unicode scalar-value string indexing. Other language semantics remain the proposed specification unless separately approved.

### 3.3 String representation

- **Approved observable semantics:** `length` and indexing count Unicode scalar values (not UTF-8 bytes, UTF-16 code units, or grapheme clusters).
- **Proposed internal representation:** immutable UTF-8 strings, with optional indexing acceleration where needed.
- Malformed Unicode handling and normalization policies need explicit test cases.

### 3.4 Resource disposal — WL-L011 approved

The **canonical WebLingo disposal protocol** is `dispose()`. A class may optionally declare `destructor()` as alternative syntax for implementing the **same** disposal slot. The language does **not** use `~ClassName()` syntax.

```javascript
class FileWriter {
    constructor(path) {
        this.file = File.openWrite(path);
    }

    destructor() {
        this.file.close();
    }
}

using (var writer = new FileWriter("output.txt")) {
    writer.write("Hello, WebLingo!");
}
```

- Callers use `writer.dispose()` even if the class declares `destructor()`.
- `using` performs deterministic, exception-safe disposal on scope exit.
- Disposal releases resources; it **does not free the GC-managed object's memory**.
- Aliases remain references to the disposed object; resource-dependent operations reject use after disposal (proposed `ResourceClosedError`).
- Disposal must be idempotent; the resource cannot be released twice.
- A class should not declare both `dispose()` and `destructor()` directly; compiler error is recommended.
- Compiler/class metadata should map both declaration forms to one disposal slot.
- **Proposed inheritance rule:** compiler-generated derived-to-base cleanup, each implementation once, including when derived cleanup throws.
- **Proposed state model:** Active → Disposing → Disposed; partial failure must not silently reactivate the resource.
- GC finalization may provide a fallback for native handles, but **must never be relied on for timely cleanup**.
- No automatic deterministic destruction merely because a variable leaves scope.
- Native C# object destructors are a **different** mechanism from WebLingo `dispose()`.

**Open implementation contract:** define async disposal policy and interaction between cancellation, outstanding I/O and cleanup. Synchronous `using` is the initial approved mechanism.

### 3.5 Statements, operators and literals — approved 12 November 2026

The syntax M0.7 implements (§9, "M0.7 parts"). Everything here was approved by the owner on 12 November 2026, except where a point says it is still open.

- **Blocks.** `{ statements }` is a statement, with no `;` after its `}`. A block opens a scope (§3.1): a declaration inside it is in scope from where it stands to the block's `}`, and may shadow an outer name. An empty block `{}` is allowed; a lone `;` is not a statement.
- **Literals.** `true`, `false` and `null` are keywords, as are `Infinity` and `NaN` (§15.2, "Binary64 literals").
- **Equality.** `==` and `!=` give a Boolean and never convert, except that an int64 and a binary64 compare as numbers (`1 == 1.0` is true; `NaN == NaN` is false, as IEEE 754 says). `null == null` is true. Values of different kinds are unequal, which is not an error. There is no `===`.
- **Ordering.** `< <= > >=` take numbers (int64 and binary64 compare as numbers), and give a Boolean; anything else is an error (WL3001). Strings may be added to them later.
- **Logic.** `!`, `&&` and `||` take Booleans only, with no truthiness (WL3001 otherwise); `&&` and `||` short-circuit.
- **Precedence**, loosest first: assignment (right-associative), `||`, `&&`, `== !=`, `< <= > >=`, `+ -`, `* /`, unary `- + !`; binary operators are left-associative.
- **`if`.** `if (condition) statement`, with an optional `else statement`; a branch is any statement, and an `else` belongs to the nearest `if` without one. The condition must be a Boolean at run time (WL3001 otherwise).
- **Loops.** `while (condition) statement`, `for` and `do … while`, each with a strict Boolean condition; `break;` and `continue;` act on the innermost loop, and are a compile-time error outside one. A loop is bounded only by the execution's instruction budget (WL3003).
- **`for`.** `for (init; condition; step) statement`, as in C: `init` is a declaration or an expression, or empty; `condition` a Boolean expression, or empty, meaning `true`; `step` an expression, or empty. A `var` declared in `init` is scoped to the loop and gets a new binding on each iteration, so that closures capture each one (§3.1).
- **`do … while`.** `do statement while (condition);`, ending with a `;`.
- **`foreach`.** `foreach (var item in items) statement` loops over a collection (not JavaScript's `for … of`). The loop variable is declared with `var` only, and gets a new binding on each iteration. Over a map, each entry is a key and a value, `foreach (var (key, value) in map)`, with the destructuring syntax decided when maps are. It comes with the collections.
- **Strings.** Literals in double or single quotes (`"a"`, `'a'`), alike in meaning, each on one line. Escapes: `\n`, `\r`, `\t`, `\0`, `\\`, `\'`, `\"`, and `\u{…}`, which names a Unicode scalar value in at most six hex digits (no surrogates). Any other escape is WL1009, an unterminated string WL1010. `+` concatenates two strings; a string and a number are not joined, and nothing converts implicitly (WL3001). `length` and indexing (§3.3) come with member access, later.
- **Functions.** `function name(a, b) { … }` declares a function, in scope throughout its block, so that functions can call each other in any order (unlike `var` and `const`, §3.1). `return value;` ends it, and `return;` gives `null`. Parameters are like `var`s. A function is a value (`var f = name;`), and `function (a) { … }` is a function expression. Closures capture variables (binding cells), not copies. A hoisted function can therefore be called before a variable it uses has been declared; reading that variable then is a runtime error (WL2004, read before initialization), and only such a read can raise it (approved 13 November 2026). A parameter may have a default value, `function f(a, b = 1) { … }`, which makes it optional, as a template's parameters may (approved 13 November 2026, §13).

### 3.6 Collections — approved 13 November 2026

The syntax and meaning of arrays, maps and sets, which M0.9 implements (§9), approved by the owner on 13 November 2026.

- **Arrays.** `[a, b, c]` makes a new array of those values, which may be of any kinds, mixed; `[]` makes an empty one; there is no trailing comma, as in calls.
- **Indexing.** `x[i]` reads element `i` of an array, counting from 0, and `x[i] = v` replaces it. The index must be an int64: a binary64, even `1.0`, is WL3001, since nothing converts implicitly. An index outside 0 to the length − 1 is a runtime error, WL3005 (index out of range).
- **Members.** `x.name` reads a member of a value, and `x.name(…)` calls one, a method. Collections and strings have the members below; a member a value does not have is a runtime error, WL3006 (no such member), as a missing property is (§3.2).
- **Length.** `x.length` is an array's number of elements, a map's or a set's number of entries, and a string's number of Unicode scalar values (§3.3). It is an int64, and cannot be assigned.
- **Strings by index.** `s[i]` is the string's `i`-th Unicode scalar value, as a string of one (`"abc"[1]` is `"b"`), not its number. Strings are immutable, so `s[i] = v` is WL3001.
- **Array methods.** `a.push(v)` adds `v` at the end, giving `null`; `a.pop()` removes the last element and gives it, and on an empty array is WL3005. Other methods come when they are needed.
- **Maps.** `{ key: value, … }` makes a map, where a key written as a name or as a string literal is a string (`{ active: true }` is `{ "active": true }`), and `{}` is an empty map; other keys are added by indexing. At the start of a statement, `{` still opens a block.
- **A map's entries.** `m[k]` reads the value of key `k`, and a missing key is a runtime error, WL3007 (no such key); `m[k] = v` adds or replaces it; `m.has(k)` tells whether it is there; `m.remove(k)` removes it, giving whether it was there. Keys are read only by indexing: `m.active` is a member of the map, not its key `"active"`.
- **Keys.** Strings, int64s, Booleans and `null` may be keys, and a set's values, compared as `==` compares them (so an int64 is never equal to a string key). A binary64 key is WL3001, so that `1` and `1.0`, and NaN, raise no question; so are arrays, maps, sets and functions, until a use for keying by identity comes.
- **Order.** The order of a map's entries, and of a set's values, is not defined: it is a hash table's.
- **`foreach`.** `foreach (var item in a)` visits an array's elements in order, a set's values, and a string's Unicode scalar values, each as a string of one; `foreach (var (key, value) in m)` visits a map's entries, and `(key, value)` is allowed only there for now (§3.5). `foreach (var item in m)` over a map is WL3001, and so is anything else to loop over. A collection changed while `foreach` visits it is not an error: the loop sees the change, as a `for` loop with an index would.
- **Sets.** `s.add(v)` adds `v`, giving whether it was new; `s.has(v)` tells whether `v` is in it; `s.remove(v)` removes it, giving whether it was there. `#{a, b, c}` makes a new set of those values, and `#{}` an empty one; its values are what a map's keys may be (see "Keys"), and an equal value given twice is there once.
- **Optional access.** `x?[k]` reads as `x[k]` does, but gives `null` where `x[k]` would raise a missing key (WL3007) or an index out of range (WL3005), whether `x` is a map, an array or a string, and gives `null` too when `x` is `null`, so that `rows?[0]?["name"]` stops at the first `null`. A key or an index of the wrong kind (`m?[1.5]`, `a?["x"]`), and a value that cannot be indexed (`5?[0]`), stay WL3001: `?[` allows for an absent entry, not for a wrong program. It is written as C# writes it; a conditional operator, `c ? a : b`, if WebLingo gets one, is told apart from it by the parser. `x?.name`, its form for members, comes later.
- **Equality.** Arrays, maps and sets are equal only to themselves, as functions are (§3.2), so `[1] == [1]` is false.
- **Text.** `write` writes an array as `[1, "a", null]` and a map as `{"active": true}`: their strings quoted, with their escapes, their collections written the same way, and one it is already writing as `[…]` or `{…}`. A set is written as its literal, `#{1, "a"}`.

### 3.7 Classes and disposal — approved 13 November 2026

The syntax and meaning of classes, the disposal protocol and `using` (§3.2, §3.4, WL-L011), which M0.10 implements (§9), approved by the owner on 13 November 2026, but for the points that say they are still open.

- **Declaring a class.** `class Name { constructor(a) { … } method(b) { … } }`: a method is written as a name, parameters and a body, with or without `function` before the name (`function method(b) { … }`). A class is in scope throughout its block, as a function is, and its name is a constant.
- **Instances.** `new Name(a, b)` makes one and runs its `constructor` with those arguments; a class without a `constructor` passes its arguments on to the nearest base that declares one, so that `class B extends A {}` makes `new B(1)` run `A`'s with `1`, a special case that saves a constructor that would only call `super(1)`, and with no such base takes none (approved 14 November 2026). Calling a class without `new`, or `new` on anything but a class, is WL2005.
- **Fields.** A field exists once a method assigns it (`this.file = …`), as §3.4's example does; reading one that was never assigned is WL3006.
- **`this`.** A keyword, the instance a method or the constructor runs on; a function expression inside a method sees the method's `this`, as a closure sees a variable. `this` anywhere else is a compile-time error, WL2009.
- **Methods as values.** `obj.method` without a call gives a function bound to `obj`: `var f = obj.method; f();` calls it on `obj`.
- **Inheritance.** `class B extends A { … }`; a method of B with the name of one of A's replaces it; `super(…)` in B's constructor runs A's, and `super.name(…)` calls A's method. A class extends at most one other (§3.2). A constructor that does not call `super(…)` runs the base's constructor first, with no arguments, as C# does.
- **Visibility.** Every field and method is public.
- **Static members.** `static name(…) { … }` declares a method of the class itself, called as `Name.name(…)`, and `static var count = 0;` a field of the class, `Name.count`, set when the class is declared. In a static method, `this` is WL2009. `B.name` finds A's static member when B has none of its own, as C# does. A static member is reached through an instance too, `p.count` and `p.make()`, after the instance's fields and its class's methods, which shadow it (the owner's request, recorded 14 November 2026). Assigning a class a member it does not have, `Point.origin = new Point(0, 0);` or `Point.parse = function (s) { … };`, declares a static member, as `static` would.
- **Properties.** `property area { get { return this.w * this.h; } set(value) { … } }` declares a property, as C# groups one: `p.area` runs its getter, and `p.area = 5` its setter, with `5`; a property with only a getter cannot be assigned (WL3001), and one with only a setter cannot be read (WL3006). A property has a field of its own, `field` inside its getter and setter, as in C# 14, which nothing else can reach. Static properties (`static property count { get { … } }`), properties in `extend` blocks, a subclass's replacing its base's, and `super.area` reading the base's, are as for methods. `write` writes an instance's fields, and not its properties, which are computed, but for a property's own field, which it writes under the property's name (approved 14 November 2026).
- **`x?.name`** gives `null` when `x` is `null`, and when it has no member `name`, as `x?[k]` does for a missing key (§3.6).
- **Equality.** An instance is equal only to itself (§3.2), so two instances with equal fields are not equal: `==` stays identity for instances, and `a.equal(b)` runs only where a program calls it. Every value has `equal`, `Object`'s giving `==` (`"a".equal("a")` is true), which a class may override (the owner's decision of 13 November 2026, once every value had a class, in place of instances only).
- **`Object`.** A class without `extends` extends `Object`, a built-in class with no fields and two methods, which a class may override: `equal(other)`, which gives `this == other`, and `toString()` (see below). `new Object()` makes a plain instance.
- **`toString()`.** Every value has `toString()`, which gives the text `write` writes (`Point {x: 1, y: 2}`, and `n.toString()` is `"5"` when `n` is 5), so that `"n = " + n.toString()` joins a number to a string. A class may override it: `write`, `writeLn` and a template's `<%= %>` then write what it returns, wherever the instance is, inside an array or a map too; a `toString()` that returns anything but a string is WL3001 where it is written.
- **Text.** `write` writes an instance as its class's name and its fields, `Point {x: 1, y: 2}`, in no defined order, one it is already writing as `Point {…}`.
- **`destructor()`.** A class may declare `dispose()` or `destructor()`, which are the same method (§3.4); both in one class is a compile-time error, WL2011. Callers always call `dispose()`.
- **Disposing once.** The runtime runs an object's disposal at most once; a second `dispose()` does nothing and gives `null` (§3.4).
- **Derived classes.** Disposing an instance of B runs B's disposal, then A's, each once, without `super.dispose()` (§3.4).
- **Disposal that throws.** When a dispose method throws while an instance is disposed, the disposal goes on: its next methods still run, each once, and the exception is thrown again after the last; an exception a later one throws replaces it, as for `using`.
- **`using`.** `using (var x = expr) statement` declares `x`, in scope in the statement only, and calls `x.dispose()` when the statement ends, by its end, `break`, `continue` or `return`; a `null` value is not disposed, and a value without `dispose` is WL3006 at the end. One resource per `using`; nest them for more.
- **Throwing.** `throw value;` throws any value. `throw;` alone, in a `catch`, throws the caught exception again, keeping where it was first thrown.
- **Catching.** `try { … } catch (e) { … }`, `try { … } finally { … }`, and `try { … } catch (e) { … } finally { … }`, with one `catch`, which catches every exception; `e` is a `var` in scope in its block. A `catch` may name what it catches (see "Typed catches").
- **`finally`** runs on every way out of its `try`: its end, `break`, `continue`, `return`, and an exception. A `break`, `continue` or `return` that would leave a `finally` block is a compile-time error, WL2012, so that a `finally` cannot drop an exception or a result.
- **`Error`.** A built-in class, `new Error(message)`, with the fields `message` and `code` (a diagnostic code, or `null` for one a program throws), which programs may extend.
- **Runtime errors as exceptions.** The runtime errors (WL2004 to WL2006 at run time, WL3001, WL3002, WL3005 to WL3007) become instances of `Error`, with their code and message, which a `catch` may catch. The execution's limits (WL3003) and a cancellation (WL3004) cannot be caught, and still stop the program. Each kind of runtime error has a class of its own, extending `Error` (`TypeError` for WL3001, `IndexError` for WL3005, `KeyError` for WL3007, and so on), so that a catch can name one kind; the others are `InitializationError` (WL2004), `CallError` (WL2005), `ArgumentError` (WL2006), `OverflowError` (WL3002) and `MemberError` (WL3006).
- **Typed catches.** `catch (NotFound e) { … }` catches only an instance of `NotFound`, or of a class extending it. A `try` may have several catches, `catch (A e) { … } catch (B e) { … } catch (e) { … }`: an exception goes to the first that catches it; a catch without a class catches any exception, and only the last may be one; and an exception that none catches goes on outward, after the `finally`. A catch names its class by a name or a dotted name (`errors.NotFound`), with no calls or other expressions, so that nothing there can fail or have side effects. A catch may name a kind of value by its class (`catch (String e)`). A catch whose name is not a class when an exception reaches it catches an exception equal to it (`var A = 5;` … `catch (A e)` catches `throw 5;`); that equality is `A.equal(e)`, the program's own `equal` if `A`'s class overrides it, an exception it throws replacing the one being caught.
- **`is`.** `x is Point` is `true` when `x` is an instance of `Point`, or of a class extending it: the test a typed catch makes, a name that is not a class included.
- **Classes of values.** Every kind of value but `null` has a class, extending `Object`: `Boolean`, `Integer`, `Number` (binary64), `String`, `Array`, `Map`, `Set`, `Function` and `Class`, so that `5 is Integer` and `catch (String e)`. `null` has no class: `null is Object` is false, and only a catch without a class catches it. A class may extend `Array`, `Map` or `Set`: `class Stack extends Array { top() { return this[this.length - 1]; } }`; `new Stack()` is an empty array of class `Stack`, indexed, with `length`, `push` and `pop`, visited by `foreach`, `is Array` and `is Stack`, with its own methods, and fields as any instance has; it is written `Stack:[1, 2]`, as an instance of a class extending another kind of value is (below). `new Array()`, `new Map()` and `new Set()` make empty ones, as `[]`, `{}` and `#{}` do. `Boolean`, `Integer`, `Number`, `String`, `Function` and `Class` are like any other class. A class's values may get new methods by an `extend` block, `extend Integer { isEven() { … } }`, after which `4.isEven()`; and a class may extend any of them: an instance of a class extending `Number`, `new Celsius(21.5)`, holds a number, which arithmetic, comparisons and `==` use, `is Number` and `is Celsius`, with its own methods and fields, and likewise for `Integer`, `String` and `Boolean`; a subclass of `Function` is a function with methods of its own. `new Integer()` is `0`, `new String()` `""`, `new Boolean()` `false`, and `new Integer(42)` the same as `42`. Such an instance is written as its class's name, a colon, and the value it holds, `Celsius:21.5`, without its fields; calling a class stays WL2005, so that text is for reading, not a program's. An `extend` block extends the class itself, everywhere: from when it runs, every instance of the class and of its subclasses, made before or after, has its methods; it is hoisted with its block's classes, and runs when the block begins, after they are made; a method it gives that the class has already replaces the class's own, as a subclass's would; and it may hold methods and static members, `static` as in a class. Arithmetic on such an instance keeps its class: a result has its left operand's class, if it has one, else its right's, so that `new Celsius(21.5) + 1` and `1 + new Celsius(21.5)` are both a `Celsius`, and `celsius + fahrenheit` a `Celsius`; it is a new instance of that class, holding the result, its constructor not run, with the fields of the operand whose class it is, copied. A class may declare what an operator does with its values, as C# does, by a static method, `static operator +(a, b) { … }`, which `+` calls when either operand's class declares it, the left's first, and likewise for `-`, `*`, `/` and the comparisons, the default applying where none does. A class extending `Number` gives the number it holds by its constructor's `super(value)`, as `new Number(value)` makes one, and a class without a constructor passes its arguments on, so `new Celsius(21.5)` holds `21.5`. `new Function(f)` is `f` itself; `class Handler extends Function { constructor(f) { super(f); } }` makes a function that calls `f`, with `Handler`'s methods and fields; and `new Function()` is a function that does nothing and gives `null`. `new Class()` makes a class, which a program may then name and give methods and fields while it runs, after which it acts as a class declared in the source does, so that a program can build classes, their methods and their fields entirely (approved 13 November 2026). `new Class("Point")` makes one extending `Object`, and `new Class("Point", Shape)` one extending `Shape`; `new Class()` makes an unnamed one, written `class`, its instances `class {x: 1}`, which `c.__className = "Point"` names. `Point.__className` gives a class's name, and assigning it renames any class, a built-in one included (`Integer.__className = "Double"`, at the program's own risk). A function with a receiver, `function (this, prefix) { … }`, whose first parameter is named `this`, is bound to a call's target (`p.describe("x = ")`), or given its receiver as its first argument when it is called directly (`describe(p, "x = ")`). `Point.addMethod("describe", f)` gives a class a method, which its instances, and its subclasses', made before or after, then have, replacing one of that name as an extension's does; `Point.addMethod("constructor", f)` gives it its constructor, which `new Point(…)` runs. A static member is given by assignment, `Point.origin = f;`, as any class's is (see "Static members"), which replaces a static method of that name, a declared class's included (approved 14 November 2026). `Point.setProperty("sum", getter, setter)` gives a class a property, each a function with a receiver, or `null` for none, and `Point.setStaticProperty("unit", getter, setter)` a static one, each without a receiver, acting as declared properties do, but without a `field` of their own; `Point.setConstant("dimensions", 2)` gives it a constant, a static member that cannot be assigned (WL3001), as `static const dimensions = 2;` declares one in a class. Each of them adds its member the first time, and replaces it after (approved 14 November 2026). A class may extend `Class`: an instance of such a class is a class, which its constructor's `super(name)` makes, and whose members are, after its own static members, its class's methods, as a metaclass's are (`class Entity extends Class { … }`, `var User = new Entity("User");`, `User is Entity`). `p.__instanceOf` gives a value's class, and `Point.__parent` a class's base, `Object` for `Point`, names that a program's own members are unlikely to take (approved 14 November 2026). The classes' names are in scope everywhere, as `Object`'s and `Error`'s, and a program may declare a name of its own over one. The text of a value agrees with its literal representation, where it has one.
- **Uncaught exceptions.** An exception nothing catches stops the program as a runtime error does: exit code 3, reported where it was thrown, with its own code for a runtime error, and WL3008 (uncaught exception) for a value a program threw.
- **`using` and exceptions.** `using` disposes on every way out, an exception's included, as a `finally` would. An exception from `dispose()` while another is on its way out replaces it, as in C#.
- **How the VMs do it.** Table-driven protected regions (§5.4): each function lists its `try` ranges and their handlers, and nothing runs on the way into a `try`.
- **Use after disposal.** For a class written in WebLingo, nothing is checked: the class checks its own state if it needs to.

### 3.8 Asynchrony — approved 13 November 2026

How a program writes asynchrony, on WebLingo's own operation model (WL-P007), approved by the owner on 13 November 2026, before templates, which use it (§13), but for the points that say they are still open.

- **`async` and `await`.** `async function f() { … }`, `async` methods and `async function (…) { … }`: a call of one gives a task at once. `await t` waits for the task `t`, giving its result, or throwing its exception.
- **Where `await` stands.** In an `async` function, and at the top level of a script or a template, which is its executable block. A script, or a module, is an instance of a built-in class `Module`: its top level is the body of an `async` method, `exec()`, which its host calls and awaits; a module's exports are its instance's members, as the module system (`import`) will define them.
- **Starting.** An `async` function starts at once, when it is called, and runs until its first `await`, as in C# and JavaScript.
- **What `await` takes.** Any value: a task gives its result once it completes, any other value itself; `await` always lets other tasks run first, even on a value that is not a task.
- **Tasks.** A task is a value of the built-in class `Task`, with nothing but `await` for now; `Task.all`, `Task.any` and timeouts come when they are needed.
- **Exceptions nothing awaits.** When an execution ends, the first exception of a task that nothing awaited stops it, as an uncaught one does, so that none goes unnoticed; `t.forget()` opts a task out of this, for one started and forgotten: nothing reports its exception.
- **Where tasks come from.** Besides `async` functions, the host's operations give tasks (files, network, database: WL-P004, WL-P009, WL-P011), and the standard library has `delay(milliseconds)`, for tests.
- **The end of an execution.** It ends when its top level does; its tasks still running are cancelled.
- **How the VMs do it.** A waiting `async` function's frames are kept on the heap, held by its task, and resumed by the worker's scheduler when what it waits for completes; no thread blocks (WL-P007, WL-P008).
- **Cancellation.** An execution's cancellation (WL3004) ends its waiting tasks too, uncatchable, as now.
- **`using`.** A `dispose()` may be `async`, and `using` awaits it; in a function that is not `async`, where it cannot wait, an `async` `dispose()` is WL3001, at the end of the `using`. `x.dispose()` on an instance whose disposal has an `async` method gives a task, which completes, with `null`, once the last of its methods has run, so that `await x.dispose();` waits for it (approved 13 November 2026).
- **Templates.** A template's body may `await` anywhere, as an `async` function's may, and `renderComponent(…)` gives a task, which the page awaits (§13).
- **Concurrency.** An execution's tasks run on its one worker, one at a time, switching only at `await`, so that no two run at once and nothing needs a lock (WL-H012, WL-P013); many executions, a server's requests, run at once on a pool of workers. Parallel work within one program will come later, as workers with heaps of their own that exchange copied messages (WL-P013's explicit transfer).

### 3.9 Modules — approved 13 November 2026

The module system, `import`, `export` and `param`, approved by the owner on 13 November 2026, but for the points that say they are still open. Templates build on it (§13, "Contracts").

- **Modules.** A script, or a module, is a `.wl` file, and an instance of the built-in class `Module`, whose top level is the body of its `async` method `exec()` (§3.8).
- **Importing.** `import db from "lib/db.wl";` imports a module statically, at a module's top level: it is resolved, and compiled, with the importing program. `Module.import("lib/db.wl")` imports one dynamically, anywhere, its path known only when it runs: the module is compiled then. Either gives the module's instance; `Module.import(…)` gives it as a task, which completes once the module has run, `var db = await Module.import("lib/db.wl");`, a module's top level being `async`. A module also imports names, `import { f, g } from "lib/util.wl";`, as JavaScript does, beside its instance.
- **Running once.** A module runs once per execution, the first time it is imported; every later import gets the same instance, already run. Under a host, an execution is a request's, and the instance the request's own. A module's static imports run before its top level, in their order, each awaited, so that it begins with them run, as JavaScript's do.
- **Exports.** Only the declarations a module marks `export` (`export function f() { … }`) are members of its instance: a `function`, `class`, `var` or `const` declaration at its top level. An importer reads an exported `var` as it is when read (`db.count`), and cannot assign it (WL3001): an export is its module's to change.
- **Paths.** A path is from the importing file's folder, `./` being that folder and `/` the drive's root, as for the language proper in §13 ("Includes"); `.wl` may be left out.
- **Cycles.** A module may import itself, directly or through others: the second import gets the instance as it is so far, as JavaScript does.
- **Parameters.** `param name;` and `param size = 10;` declare a module's parameters, at its top level, before its other statements, each a `const` of it: `param` is a keyword of the language. An importer gives them, `import db from "lib/db.wl" { size: 10 };`, and a template's caller gives a component's or a layout's (§13). A parameter without a default that is not given is an error, where the module is imported, of a code of its own.
- **A script's parameters.** `weblingo run script.wl size=10 name=Ann` gives a script its parameters, each value read as a WebLingo literal if it is one (`10`, `true`), as a string otherwise; a quoted value is a string, so `value="10"` gives `"10"`.
- **The standard library.** Its parts are modules named by a scheme, `std:fs` and `std:system`, which the compiler and the runtime resolve, never as paths (`import fs from "std:fs";`). The built-ins belong to modules too: `write` and `writeLn` are `std:system`'s, which every module imports implicitly.
- **Still open:** how a parameter is given as a string when it looks like another literal, a shell removing the quotes of `value="10"` (the follow-ups of 14 November 2026 approved the rest of this section).

---

## 4. Runtime values, objects and garbage collection (WL-R001–WL-R010)

### 4.1 Runtime value representation

The proposed implementation uses a compact tagged `WebLingoValue` (target size **16 bytes**) with fields conceptually including kind, flags, auxiliary metadata and a 64-bit payload. Reference-like values use VM-managed handles rather than exposing arbitrary managed object pointers.

- Numeric primitives may fit inline; decimal128 may require boxed storage.
- Objects carry type metadata and reference identity.
- Arrays, functions and closures are VM-managed object types.
- Physical layout and handle generation must be validated against GC, interop and Native C# portability constraints; 16 bytes is an implementation target, not a proven ABI guarantee.

### 4.2 Garbage collection — approved

- WebLingo uses its **own tracing GC**, initially **nonmoving mark-and-sweep**.
- WebLingo GC is distinct from the .NET GC used to run the initial implementation.
- WebLingo GC is also distinct from the future Native C# runtime's deterministic lifetimes and reference-counted structures.
- Resource cleanup through `dispose()` is independent of reachability-based memory reclamation.

### 4.3 Worker-local heap ownership — approved M0.5

- Mutable WebLingo heaps are worker-local.
- Request VM frames and mutable request state belong to their assigned worker.
- Immutable application data may be shared under explicit rules.
- Cross-worker transfer of mutable data requires a defined copy, ownership-transfer, or message-passing contract; unsynchronized shared mutable references are forbidden.
- Async operations must root or retain buffers and handles until completion.
- Native handles cannot be released while an outstanding operation uses them.

---

## 5. Bytecode and VM architecture (WL-BC001–WL-BC016)

### 5.1 WBC goals

WebLingo Bytecode (WBC) is intended to be portable, versioned, verified, architecture-independent and suitable for caching. It must not embed .NET object references, machine pointers or hosting-framework types.

### 5.2 VM architecture — **provisional**

The register VM is the **preferred first implementation**, but the final register-versus-stack decision is explicitly reserved until benchmarks compare equivalent workloads compiled from the **same bound/lowered IR**.

| Consideration | Register VM | Stack VM |
|---|---|---|
| Dispatch count | Often lower | Often higher |
| Instruction size | Often larger | Often smaller |
| Compiler complexity | Higher | Lower |
| Dataflow/liveness | More explicit | More implicit |
| Native code / optimization potential | Good | Good |

M0.6 implementation default: implement the minimal register VM first and add a stack VM benchmark prototype later. **This does not approve WL-BC002 permanently.** The stack VM and the comparison exist since 13 November 2026 (§5.6, "The comparison"). The owner kept both VMs that day: every new feature is built on both, and the choice is made once the workloads not yet measurable can be measured.

### 5.3 Proposed WBC binary container

- Header target: **32 bytes**, magic `WBC\0`, major/minor version, ABI/feature flags, section count, directory offset and total length.
- Section-directory entry target: **32 bytes**.
- Sections: constants, type/module metadata, function table, code, exception tables, debug/source mapping, integrity metadata.
- Integer encoding: signed LEB128 where appropriate.
- Floating point: IEEE binary64.
- Decimal128 encoding: IEEE 754 BID **proposed**, not yet approved.
- Strings: UTF-8.
- Register count: proposed maximum **65,535 per function**.
- Operand encoding: variable-length **proposed**.
- Serialized control-flow targets: byte offsets; decoded VM representation may use instruction indices **proposed**.

The binary layout is **not yet frozen**. M0.6 may begin with an in-memory instruction model.

### 5.4 Proposed instruction families

- Constant/local/global loads and stores.
- Integer, floating and decimal arithmetic; comparisons.
- Branches and conditional jumps.
- Function calls, returns, closures and captures.
- Object, array and property operations.
- Exceptions, cleanup and disposal regions.
- Async await, suspend/resume and cancellation checks.
- Template literal, escaped expression and raw expression writes.

Function calls may use contiguous argument registers and an explicit receiver. Exceptions use table-driven protected/cleanup regions, approved by the owner on 13 November 2026 (§3.7).

### 5.5 Verification

Verification is mandatory before executing bytecode. It must validate:

1. Container structure, versions and section bounds.
2. Opcode and operand validity.
3. Branch and exception-region targets.
4. Constant, module and function references.
5. Register initialization and control-flow consistency.
6. Async/cleanup state and resource-safety constraints as implemented.

Malformed bytecode must fail with a defined verification error, not crash the VM.

### 5.6 Dispatch and benchmarks

An initial C# `switch` dispatch loop is **proposed** for clarity and portability. Alternative dispatch techniques may be benchmarked later. Benchmarks should include arithmetic, loops, calls, closures, property access, async suspension, template output, and representative I/O-bound server workloads. Measure throughput, latency, allocation, bytecode size, startup and implementation complexity.

**The comparison's parts** (WL-BC002, open question 1). The owner decided on 13 November 2026 that the stack VM is a project of its own, `WebLingo.VM.Stack`, beside `WebLingo.VM`, with its code generator in `WebLingo.Compiler`; that the benchmark is a console project, `WebLingo.Benchmarks`, timing the VMs with `Stopwatch` over fixed workloads (loops, calls, closures, strings), with no new package; and that the register VM is compared with a dispatch tuned for speed. The work lands in four parts:

1. **The benchmark:** `WebLingo.Benchmarks`, its workloads, and what it measures (time, instructions run, bytecode size, allocation), run on the register VM as it is, for a baseline.
2. **The register VM's dispatch, tuned:** the same bytecode, with the same results, run faster; measured against the baseline.
3. **The stack VM:** stack bytecode and its verifier (`WebLingo.Bytecode`), its code generator (`WebLingo.Compiler`), from the same lowered IR as the register one, and its VM (`WebLingo.VM.Stack`), sharing `WebLingo.Runtime`; every conformance program gives the same output and the same errors on both VMs.
4. **The comparison:** both VMs measured on the same workloads, the results recorded here, and the choice between them put to the owner.

**Workloads** (part 1, 13 November 2026): `loops`, nested `for` loops with int64 arithmetic, comparisons and `if`, a million times; `calls`, a recursive `fib(25)`, about 243,000 calls; `closures`, 20,000 closures made and 200,000 calls of them, sharing captured cells; `strings`, 200,000 concatenations and 20,000 comparisons, with the heap's collector. Each writes one result, which every VM must give.

**Baseline and tuned dispatch** (parts 1 and 2, 13 November 2026), on one Windows development machine (x64, .NET 10.0.15, Release), each the median of 7 timed runs after 3 warm-ups, with every method fully optimized from its first call (the benchmark turns .NET's tiered compilation off, so that no VM is timed while it is still being compiled; with the tiers on, part 1 measured the baseline at 130.3, 33.1, 51.6 and 29.4 ms). The baseline is the register VM as M0.7.6 left it; the tuned one runs the same bytecode, with the same instruction counts and results: one loop with the running frame in locals, the budget and the cancellation signal looked at only when they come due, int64 and binary64 arithmetic and comparisons done in the loop (the rest, and every error, by `WebLingoOperators`), and frames and their registers reused by the next call at the same depth. Allocation is .NET's, a run's. Instructions run, and code size (instructions, and bytes with a byte per operation and each operand it uses in LEB128), are the same for both: 10,010,008 instructions run and 24 instructions of code (76 bytes) for `loops`, 2,063,676 and 23 (74 bytes) for `calls`, 3,120,009 and 35 (106 bytes) for `closures`, 1,920,008 and 26 (83 bytes) for `strings`.

| Workload | Baseline | Tuned | Speed-up | Instructions/s, tuned | Allocated, baseline | Allocated, tuned |
|---|---:|---:|---:|---:|---:|---:|
| loops | 120.1 ms | 19.5 ms | 6.2× | 513.6 M | 3,288 B | 3,992 B |
| calls | 31.9 ms | 9.2 ms | 3.5× | 223.3 M | 48,561,928 B | 10,552 B |
| closures | 44.9 ms | 12.3 ms | 3.7× | 253.5 M | 36,733,560 B | 3,294,656 B |
| strings | 26.2 ms | 9.7 ms | 2.7× | 198.1 M | 10,731,560 B | 10,732,304 B |

**The stack VM** (part 3, 13 November 2026). Its bytecode (`WebLingoStackBytecodeContracts.cs`) is in `WebLingo.Bytecode`, beside the register bytecode, whose constants, imports and captures it shares, since the compiler, which generates it, may not depend on a VM (§8.3). A function has locals, its parameters first, and an operand stack, whose deepest point the generator counts; its frame has a slot for each, at most 65,535 in all, as a register function has registers, and the execution's register budget bounds its slots. An instruction takes its operands from the top of the stack and pushes its result, with at most two operands of its own (a local, a constant, a jump target, an argument count). Its verifier checks what the register verifier checks, and that the stack has the same depth wherever paths meet, never pops what it does not have, and never grows past its counted depth. `WebLingo.VM.Stack` holds the VM, which shares `WebLingo.Runtime` and the register VM's contracts (`WebLingoExecutionResult`), and so depends on `WebLingo.VM`. It is tuned as the register VM is (part 2). Every conformance program gives the same diagnostics, output and errors on both VMs, but for one difference: the instruction budget (WL3003) counts each VM's own instructions, and the stack VM runs more of them for the same program, so a program that runs out of its budget stops at a different point on each.

**The comparison** (part 4, 13 November 2026), as part 2 measured, with 11 timed runs: the tuned register VM, and the stack VM, tuned the same way, on the same workloads, compiled from the same lowered IR. The register VM is faster on every workload, by 1.10 to 1.65 times, and executes 1.29 to 1.75 times fewer instructions; the stack bytecode has more instructions, but is 3 to 35 percent smaller in bytes, since most of its instructions have one operand or none; both allocate the same in .NET's heap, and in WebLingo's.

| Workload | Register | Stack | Stack ÷ register | Instructions run, register | Instructions run, stack | Code, register | Code, stack |
|---|---:|---:|---:|---:|---:|---:|---:|
| loops | 17.3 ms | 28.6 ms | 1.65 | 10,010,008 | 17,514,512 | 24 (76 B) | 41 (74 B) |
| calls | 9.1 ms | 10.0 ms | 1.10 | 2,063,676 | 2,670,639 | 23 (74 B) | 28 (48 B) |
| closures | 12.4 ms | 14.5 ms | 1.17 | 3,120,009 | 4,660,014 | 35 (106 B) | 52 (93 B) |
| strings | 11.0 ms | 12.8 ms | 1.16 | 1,920,008 | 3,100,012 | 26 (83 B) | 42 (76 B) |

Implementation complexity, in lines of source, the stack VM's against the register VM's: the code generator 375 against 468 (it allocates no registers, and needs no copy of a local that a later operand assigns), the verifier 346 against 402, the VM 633 against 604, and the instruction model 121 against 148. Not measured yet, because the language does not have them: property access, async suspension, template output, and I/O-bound server workloads (this section's list); and startup and latency, which matter once there is a host. The choice between the VMs is the owner's (WL-BC002).

**Both VMs, for now** (owner's decision, 13 November 2026). WL-BC002 stays provisional until property access, async suspension, template output and I/O-bound workloads can be measured too. Until then, every new feature is built on both VMs, from the same lowered IR, each with its code generator, its verifier and its VM, and gives the same results on both; and each of those workloads joins the benchmark when the language can express it, so that the choice is made on all of them.

**Collections workload** (M0.9.4, 13 November 2026), measured as part 4 was: `collections` fills an array and a map, and reads, writes and visits them with `foreach` and indexing. The register VM took 3.7 to 4.5 ms over two runs, the stack VM 4.7 to 5.2 ms, running 605,064 and 1,030,106 instructions, from 67 instructions of code (223 bytes) and 115 (218 bytes); both allocated 1.06 MB.

**Property access** (M0.10.1, 13 November 2026), the first of the workloads "Both VMs, for now" waits for, measured as part 4 was: `objects` makes 100,000 instances, adds them, and reads their fields through their methods. The register VM took 80.7 to 81.7 ms over two runs, the stack VM 85.8 to 92.9 ms, running 3,200,025 and 5,000,039 instructions, from 50 instructions of code (168 bytes) and 73 (136 bytes); both allocated 43 MB of .NET's heap, since each instance keeps its fields in a table of its own. Async suspension, template output and I/O-bound work are still to come.

**Async suspension** (M0.11.1, 13 November 2026), the second of them, measured as part 4 was: `async` starts 100 tasks, each awaiting 1,000 times, so that 100,000 suspensions and resumptions take turns through the scheduler. The register VM took 5.4 to 6.4 ms over two runs, the stack VM 7.0 to 10.5 ms, running 701,913 and 1,403,221 instructions, from 34 instructions of code (108 bytes) and 59 (108 bytes); both allocated 77 KB of .NET's heap, a waiting frame being kept as it is, not copied. Template output and I/O-bound work are still to come.

---

## 6. Hosting architecture (WL-H001–WL-H014)

### 6.1 Approved hosting decisions

1. **IIS Stage 1:** ASP.NET Core adapter first (WL-H009).
2. **Concurrency:** worker-affine request execution contexts (WL-H012).
3. **Isolation:** logical isolation among applications in a shared runtime (WL-H013).
4. **Reload:** atomic application generations; old requests drain on old generation (WL-H004).

The initial IIS adapter is an implementation choice, not a permanent dependency of the language or runtime. FastCGI, standalone HTTP and eventually a native IIS integration remain targets.

### 6.2 Host-independent layers

```text
.wl scripts / .wlp templates / modules / routes
                    |
         WebLingo Compiler + VM
                    |
          WebLingo Runtime
                    |
    WLHI: host-neutral request/response/lifecycle API
                    |
      +-------------+--------------+
      |             |              |
  IIS adapter    FastCGI       Standalone HTTP
  (ASP.NET Core) adapter       (Kestrel initially)
```

Hosting adapters must not directly manipulate VM registers, GC internals or compiler structures.

**WLHI, the WebLingo Host Interface**, is what a host and the WebLingo runtime know of each other, and all they know: the contracts through which a host, IIS's adapter, FastCGI's, or a standalone HTTP server, starts and stops the runtime, and has it run requests. It is made of the runtime's lifecycle (§6.4); the lifecycle of a request, from the application generation it is bound to through its execution context to its release (§6.6); the HTTP request and response a script sees, with their headers and streamed bodies (§6.7); and, for a host that is not .NET, the C-compatible native ABI that carries these across a process's boundary (§6.8). A host adapts to WLHI, never the reverse (§11). It was WSHI, the WebScript Host Interface, until the names were renamed (§1; the owner's decision, 14 November 2026).

### 6.3 Runtime hierarchy

```text
WebLingoRuntime
  ApplicationManager
    ApplicationGeneration A
    ApplicationGeneration B
  Scheduler
  ModuleCache
  CompilerService
  PlatformServices

ApplicationGeneration
  Configuration
  ModuleGraph
  RouteTable
  CapabilitySet
  Immutable/shared generation state
  Worker-local execution state

RequestContext
  Request / Response
  OwningWorker
  ExecutionContext
  Cancellation
  ResourceBudget
  Request-local heap and resources
```

The runtime can host multiple applications. Application boundaries isolate module namespaces, capabilities and mutable state logically; **OS process isolation is still required for hostile tenants or stronger security guarantees**.

### 6.4 Runtime lifecycle

`Created → Initialized → Running → Stopping → Stopped`

- Initialization configures compiler, VM, scheduler, GC and platform services.
- Shutdown stops accepting requests, drains in-flight work up to a configured deadline and releases resources.
- Host termination after the deadline is possible; cleanup code must not assume process exit always permits graceful completion.

### 6.5 Application generations and hot reload — approved

`Discovered → Loading → Compiled/Validated → Initialized → Ready → Serving → Draining → Unloaded`

- Source/configuration changes create a **new generation**.
- Compile and initialize the replacement before publication.
- Atomically publish only a healthy new generation.
- New requests use the new generation; existing requests finish on the old one.
- The old generation unloads after its active requests and retained operations drain.
- Failed new generations do not displace the last healthy generation.

### 6.6 Request lifecycle

`Received → Select application generation → Resolve route → Create worker-affine execution context → Invoke script/template → Stream response → Cleanup → Release`

- Request-local mutable state is not shared between requests.
- Async suspension does not block an OS thread.
- Completion callbacks enqueue resumptions onto the owning VM worker.
- A request remains bound to its original application generation.

### 6.7 HTTP request/response interfaces

Conceptual interfaces:

```csharp
public interface IWebLingoHttpRequest
{
    string Method { get; }
    string Path { get; }
    string QueryString { get; }
    string Scheme { get; }
    string Host { get; }
    IWebLingoHeaderCollection Headers { get; }
    IWebLingoBodyReader Body { get; }
}

public interface IWebLingoHttpResponse
{
    int StatusCode { get; set; }
    IWebLingoHeaderCollection Headers { get; }
    IWebLingoBodyWriter Body { get; }
}
```

Response states: **Uncommitted → Committed → Completed**. Headers cannot be mutated after commitment; body output must support streaming and backpressure. Request bodies must be streamable without mandatory full buffering.

### 6.8 WLHI native ABI — proposed

A future C-compatible ABI will use opaque runtime, application and request handles, with create/load/begin/cancel/release operations. Final signatures must define ABI versioning, struct sizes, calling conventions, ownership, callback lifetime, threading and error reporting. No managed .NET object crosses the ABI.

### 6.9 IIS and deployment

Example site:

```text
MySite/
  index.wlp
  products.wlp
  api/products.wl
  lib/database.wl
  weblingo.json
```

The desired deployment model is automatic source detection, compilation and caching, with no build tooling required on the server. IIS uses the ASP.NET Core adapter first. FastCGI and standalone hosts translate protocol details into WLHI.

### 6.10 Scheduling — approved affinity, proposed fairness

- Bounded VM worker pool.
- Execution contexts are **worker-affine** and resume on their owning worker.
- Platform I/O completion may happen on another host thread but must post work to the owner.
- Instruction-quantum yielding and bounded blocking-operation pools are recommended.
- Work stealing or migration is deferred until supported by explicit safe-transfer semantics.

### 6.11 Security and diagnostics

Applications have immutable capability policies for filesystem, network, database, environment, process and native extensions. Enforcement occurs at the service boundary; hiding functions is not sufficient. Diagnostics should include request IDs, latency, VM time, await time, GC pauses, allocations, module cache, generation count, worker utilization, failures and leaks.

---

## 7. Platform abstraction layer (WL-P001–WL-P016)

### 7.1 Approved M0.5 decisions

- **WL-P007:** Runtime-owned `WebLingoOperation` semantics, not .NET `Task`/`ValueTask` as the core contract.
- **WL-P004:** Synchronous **and** asynchronous filesystem APIs, with blocking work isolated from VM workers.
- **WL-P013:** Worker-local mutable heaps, immutable shared application data and explicit cross-worker transfers.
- **WL-P016:** Portability inventory and CI enforcement, rather than immediately forbidding every unimplemented Native C# feature.

### 7.2 PAL layers

```text
Portable WebLingo core (lexer, compiler, WBC, VM, GC)
                           |
                 PAL abstractions
                           |
            +--------------+--------------+
            |                             |
        .NET adapter                  Native adapter
     (.NET 10 initially)        (Native C# RTL / OS APIs)
```

The core must not directly depend on ASP.NET Core, ADO.NET, `System.IO.File`, `HttpClient`, platform P/Invoke or a specific host. Platform services are injected at runtime initialization and capability-filtered for each application.

### 7.3 Proposed platform services interface

```csharp
public interface IWebLingoPlatformServices
{
    IWebLingoFileSystem FileSystem { get; }
    IWebLingoClock Clock { get; }
    IWebLingoSchedulerHost Scheduler { get; }
    IWebLingoNetworkService Network { get; }
    IWebLingoCryptoService Crypto { get; }
    IWebLingoDiagnostics Diagnostics { get; }
}
```

These are **conceptual contracts**, not frozen source-level APIs.

### 7.4 Filesystem (WL-P004)

- Open/read/write/flush/metadata/delete operations, synchronous and asynchronous.
- Portable paths and file metadata.
- Resource-owning file handles with canonical disposal.
- No unbounded blocking on cooperative VM workers.
- Capability-safe path resolution must address traversal, symlinks, Windows drive/UNC paths, case differences, device names and TOCTOU races; authorize opened objects where possible.

### 7.5 Buffers (WL-P005)

- Mutable and read-only buffer views with explicit handle, offset and length.
- Owners may be VM heap, request allocation, I/O adapter or native resource.
- Pending operations retain or root buffers until completion.
- No raw managed addresses exposed across portable contracts.

### 7.6 Clocks (WL-P006)

- Wall clock for timestamps.
- Monotonic clock for timeouts and elapsed-time measurements.
- Time-zone/calendar services separate from raw clock primitives.
- Injectable clock for deterministic tests.

### 7.7 Asynchronous operations (WL-P007) — approved ownership

Core-visible operations are runtime-owned, with states conceptually:

`Created → Pending → Completed | Failed | Cancelled`

- Exactly one terminal state.
- Adapter completions may arrive on arbitrary threads.
- VM continuation resumes only through the owning worker's queue.
- Cancellation is cooperative and races with completion; terminal-state arbitration must be atomic.
- .NET `Task`/`ValueTask` may be used **inside adapters** but do not define WebLingo execution semantics.
- An optimized production implementation need not allocate a separate interface object for every operation.

### 7.8 Scheduler (WL-P008)

- Post/wake primitives for named VM workers.
- Worker affinity for frames, register storage and mutable heap state.
- Proposed instruction budgets and safe points to prevent starvation.
- Blocking I/O routed to asynchronous APIs or bounded blocking pools.

### 7.9 Networking (WL-P009)

- Capability-aware outbound connections and protocol adapters.
- Authorization must consider hostname, resolved IP, port, protocol, redirects, DNS rebinding and TLS verification.
- HTTP is a higher-level service; transport primitives remain portable.

### 7.10 Cryptography (WL-P010)

Proposed baseline: secure random bytes, SHA-256/SHA-512, HMAC, constant-time comparisons, password hashing via approved algorithms and TLS through networking adapters. Keys require controlled access and feasible zeroization.

### 7.11 Database providers (WL-P011)

Provider-neutral connections, parameter binding, transactions, cancellation, streaming result sets and deterministic disposal. Initial suggested providers: SQL Server and SQLite via ADO.NET; later native providers and ODBC. Connection pooling belongs to providers/adapters, not the VM.

**Illustrative** WebLingo usage (exact async connection API not yet frozen):

```javascript
using (var db = Database.open("MainDatabase")) {
    var rows = await db.query(
        "SELECT Id, Name FROM Customers WHERE Active = @active",
        { active: true }
    );
}
```

### 7.12 Resource ownership (WL-P012–P013)

- PAL C# implementations may expose `IWebLingoDisposable.Dispose()`; WebLingo uses lowercase `dispose()`.
- Native C# destructors may implement internal PAL cleanup but do not change WebLingo semantics.
- In-flight I/O retains handles/buffers until completion or safe cancellation.
- Worker-local mutable objects cannot be concurrently mutated by foreign workers.

### 7.13 Capabilities and errors (WL-P014–P015)

- Application generation owns an immutable capability set; request policy may only restrict it.
- Enforce access at actual filesystem/network/database/process/native boundaries.
- Translate host exceptions (e.g., `IOException`) into stable WebLingo error categories/codes with optional cause and diagnostic context.
- Avoid leaking host internals to HTTP clients.

### 7.14 Native portability (WL-P016)

- Track C# features and BCL dependencies as **Portable**, **Planned**, **Adapted** or **Prohibited**.
- Reflection.Emit and uncontrolled runtime code generation are prohibited in the portable core.
- ASP.NET Core, ADO.NET, P/Invoke and OS-specific threading are adapter-only.
- Generics, exceptions, delegates and other Native C# features are tracked against actual compiler progress.
- CI should check dependency boundaries and run cross-platform conformance tests.

---

## 8. M0.6 solution scaffolding (WL-S001–WL-S016) — approved

### 8.1 Deliverable and scope

A compilable, minimal **.NET 10 Visual Studio solution**, not a full language implementation. First vertical slice:

`Source → Lexer → Parser → Binder → Lowered IR → Bytecode generator → Verifier → VM → Result`

The first implementation may use an in-memory bytecode representation; WBC binary serialization comes after encoding decisions are settled.

### 8.2 Proposed repository layout

```text
WebLingo/
  WebLingo.slnx
  Directory.Build.props
  Directory.Packages.props
  global.json
  README.md
  LICENSE
  docs/
    architecture/
    language/
    bytecode/
    hosting/
    portability/
  src/
    WebLingo.Core/
    WebLingo.Language/
    WebLingo.Compiler/
    WebLingo.Bytecode/
    WebLingo.Runtime/
    WebLingo.VM/
    WebLingo.Platform.Abstractions/
    WebLingo.Platform.DotNet/
    WebLingo.Hosting.Abstractions/
    WebLingo.Hosting.AspNetCore/
    WebLingo.Hosting.Standalone/
    WebLingo.StdLib/
    WebLingo.Cli/
  tests/
    WebLingo.Language.Tests/
    WebLingo.Compiler.Tests/
    WebLingo.Bytecode.Tests/
    WebLingo.Runtime.Tests/
    WebLingo.VM.Tests/
    WebLingo.Platform.Tests/
    WebLingo.Conformance.Tests/
  samples/
    HelloWorld/
    Arithmetic/
    Functions/
    SimpleWebsite/
```

Add FastCGI, database provider and native adapter projects when their implementation milestones begin, rather than creating unnecessary empty projects.

**Documents — approved amendment (12 November 2026):** the repository's root holds the solution folder above, `WebLingo/`, beside a flat `Documents/` folder for the project's documents. This specification exists once, as `Documents/weblingo-architecture.md`; there is no `docs/architecture/` folder, and the folder structure is kept shallow: the portability inventory is `Documents/NativeCompatibility.md`, and there is no `eng/` folder. The whole repository may become public, so nothing that should stay private is put into it.

### 8.3 Project dependencies (WL-S002)

- Core has no WebLingo project dependencies.
- Language depends on core, not compiler/runtime.
- Bytecode depends on core, not compiler/VM.
- Compiler consumes Language and Bytecode.
- Runtime does not depend on Compiler.
- VM consumes Bytecode and Runtime.
- Hosting/CLI compose the engine; framework-specific types do not leak into core.
- All references must form a directed acyclic graph.

### 8.4 Initial contracts (WL-S003–WL-S012)

**Source spans:** stable offsets, lines and columns. Serialized offsets measured in UTF-8 bytes; diagnostics use documented one-based line/column conventions.

**Diagnostics:** severity, stable code, message, source span. Suggested codes:

| Code | Meaning |
|---|---|
| WL1001 | Unexpected token |
| WL1002 | Missing semicolon |
| WL2001 | Undefined identifier |
| WL2002 | Duplicate declaration |
| WL2003 | Assignment to constant |
| WL2004 | Read before initialization |
| WL3001 | Invalid operation |
| WL4001 | Bytecode verification failure |

Codes added while implementing M0.6 (§15.2):

| Code | Meaning | Status |
|---|---|---|
| WL1003 | Invalid character | Approved (12 November 2026) |
| WL1004 | Invalid numeric literal (malformed, or out of range) | Approved (12 November 2026) |
| WL1005 | Unterminated comment | Approved (12 November 2026) |
| WL1006 | Declaration without an initializer | Approved (12 November 2026) |
| WL1007 | Invalid assignment target | Approved (12 November 2026) |
| WL1008 | Source nested too deeply | Approved (12 November 2026) |
| WL1009 | Invalid escape in a string literal | Approved (12 November 2026) |
| WL1010 | String literal not closed on its line | Approved (12 November 2026) |
| WL2005 | Not a function (a call of something that cannot be called) | Approved (12 November 2026) |
| WL2006 | Wrong number of arguments in a call | Approved (12 November 2026) |
| WL2007 | `break` or `continue` outside a loop | Proposed |
| WL2008 | `return` outside a function | Proposed |
| WL2009 | `this` outside a method or a constructor | Approved (13 November 2026) |
| WL2010 | `throw;` alone outside a `catch` block | Proposed |
| WL2011 | A class declaring both `dispose()` and `destructor()` | Approved (13 November 2026) |
| WL2012 | `break`, `continue` or `return` leaving a `finally` block | Approved (13 November 2026) |
| WL2013 | A class that extends itself, directly or through others | Proposed |
| WL2014 | `await` outside an `async` function and a script's top level | Proposed |
| WL2015 | A module an import names that cannot be found or read | Proposed |
| WL3002 | Integer overflow (a result outside int64) | Approved (12 November 2026) |
| WL3003 | Execution budget exceeded (instructions or registers) | Approved (12 November 2026) |
| WL3004 | Execution cancelled by the host | Approved (12 November 2026) |
| WL3005 | Index out of range | Approved (13 November 2026) |
| WL3006 | No such member | Approved (13 November 2026) |
| WL3007 | No such key | Approved (13 November 2026) |
| WL3008 | Uncaught exception: a value a program threw, which nothing caught | Approved (13 November 2026) |
| WL3009 | Deadlock: the program waits for a task that nothing can complete | Proposed |
| WL4002 | Program too large for the bytecode (more registers than a function may have) | Approved (12 November 2026) |

**Lexer:** identifiers, keywords (`var`, `const`), literals, operators, punctuation, source spans and malformed-input diagnostics.

**Parser:** initial subset of variable declarations, integer literals, identifiers, arithmetic, parentheses and mandatory semicolons; preserve source spans and recover sufficiently to report multiple errors.

**Binder/IR:** resolve lexical symbols, each from its declaration on (§3.1); produce bound tree and lowered IR independent of register/stack VM choice.

**Bytecode generator:** produce an executable in-memory module; serialization deferred until WBC encoding finalized.

**Verifier:** reject invalid opcodes, operands, branches, constants and uninitialized temporaries.

**Runtime values:** implement null, Boolean, int64 and binary64 first; decimal128 follows without changing approved language semantics.

**VM:** execute verified modules with an execution context containing owner worker, cancellation, instruction budget, resource budget and runtime services. Initial execution may be synchronous.

**CLI:** executable `weblingo`; first working commands `run`, `check`, `--version`; later `compile`, `disasm` and `serve`.

Example:

```javascript
var a = 10;
var b = 20;
var result = a + b;
writeLn(result);
```

Expected output: `30`. `write` and `writeLn` are built-ins the CLI host registers (§15.2).

### 8.5 Build conventions (WL-S013–WL-S014)

- Target framework: `net10.0`.
- Nullable reference types enabled.
- Implicit usings enabled.
- Deterministic builds; warnings treated as errors.
- Central package management; minimal third-party dependencies in portable core.
- Maintain the portability inventory, `Documents/NativeCompatibility.md`.
- **Testing default:** xUnit on .NET 10, adopted with M0.6 approval.

Proposed central props:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

### 8.6 Initial conformance tests (WL-S015)

| Program / scenario | Expected behavior |
|---|---|
| `var x = 10;` | Valid |
| `const x = 10;` | Valid |
| `const x;` | Compile error |
| `var x = 10` | Missing-semicolon compile error |
| `var x = 1; var x = 2;` | Duplicate declaration error |
| `x = 10;` | Undefined identifier error |
| `var x;` | Compile error |
| `writeLn(x); var x = 1;` | Undefined identifier error |
| `var x = 10 + 20;` | Result 30 |
| `var x = 10 / 2;` | Result 5.0 |
| Signed int64 overflow | Runtime error |
| Malformed source | Diagnostic, not compiler crash |
| Malformed bytecode | Verification failure, not VM crash |

**Note:** every declaration has an initializer and a name means nothing before its declaration (§3.1), so no code but a hoisted function (§3.5) can read a variable before it has a value, and only that read is checked at run time (WL2004).

### 8.7 Implementation increments (WL-S016)

1. **M0.6.1:** solution skeleton, projects, references, CLI shell, test harness.
2. **M0.6.2:** lexer and tokenization tests.
3. **M0.6.3:** parser, AST and diagnostics.
4. **M0.6.4:** binder and lowered IR.
5. **M0.6.5:** minimal bytecode generator and verifier.
6. **M0.6.6:** minimal register VM, arithmetic, locals and a built-in that writes output (`write`, `writeLn`).
7. **M0.6.7:** end-to-end conformance on Windows/Linux.

### 8.8 M0.6 acceptance criteria

- Builds cleanly on Windows and Linux.
- Dependency rules enforced.
- CLI compiles and runs simple `.wl` programs.
- Lexer, parser, binder, generator, verifier and VM are separate components.
- Tests pass; malformed inputs fail safely.
- Portability inventory exists.
- Portable core has no ASP.NET Core or ADO.NET dependency.
- Compiler IR is not tied to a permanent VM choice.
- Build and test instructions are documented.

---

## 9. Milestone roadmap and current status

| Milestone | Purpose | Status |
|---|---|---|
| M0.1 | Language grammar and semantics | Key normative choices approved; detailed semantics drafted |
| M0.2 | Runtime value model, GC and disposal | Key choices approved; detailed physical contracts drafted |
| M0.3 | WBC bytecode and VM architecture | Drafted; the VMs compared (13 November 2026, §5.6), the choice and several encoding choices open |
| M0.4 | Hosting ABI and runtime lifecycle | **Four explicit architectural decisions approved** |
| M0.5 | Platform abstraction and portability | **Four explicit architectural decisions approved** |
| M0.6 | Solution scaffolding and implementation contracts | **Approved; M0.6.1–M0.6.7 implemented and verified on Windows and Linux (§15)** |
| M0.7 | Conformance suite expansion: the language of §3.5, in parts M0.7.1 to M0.7.6 (below), each with its conformance tests | Implemented (12 November 2026), on Windows; `foreach` waits for collections |
| M0.8 | Windows/Linux CI and portability validation | Started (12 November 2026): the CI workflow builds and tests on Windows and Linux (first run passed: CI run 1, 12 November 2026, commit `5229806`); tests check the portable core's assemblies against the dependency rules (§8.3) and the portability inventory (WL-P016) |
| M0.9 | Collections: arrays, maps and sets (§3.2), with indexing, `length` and member access (§3.3), and `foreach` (§3.5) | Implemented (13 November 2026), in parts M0.9.1 to M0.9.6 (below), on both VMs, on Windows |
| M0.10 | Classes, `dispose()` and `using` (§3.2, §3.4, WL-L011) | Implemented (13 November 2026), in parts M0.10.1 to M0.10.7 (below), on both VMs, on Windows; typed catches, `is` and the classes of values implemented too (13 November 2026), but for extending `Array`, `Map` and `Set` |
| M0.11 | Asynchrony: `async`, `await` and tasks (§3.8) | Implemented (13 November 2026), in parts M0.11.1 and M0.11.2 (below), on both VMs, on Windows |
| M0.12 | `.wlp` templates: includes, layouts and parameterized components, together (§13, WL-T001 to WL-T008) | Its contracts decided in part (§13, "Contracts", 13 November 2026); after M0.11, and the module system it needs (`import` and `param`) |
| M0.13 | WBC, the bytecode's binary format (§5.1, §5.3) | After M0.12 |

**M0.7 parts** (12 November 2026), each landing on its own with its conformance tests, which `WebLingo.Conformance.Tests` runs beside §8.6's:

1. **M0.7.1:** blocks and nested scopes. Implemented (12 November 2026); a missing `}` is WL1001, at its `{`.
2. **M0.7.2:** `true`, `false` and `null`; `==`, `!=`, `<`, `<=`, `>`, `>=`; `!`, `&&`, `||`. Implemented (12 November 2026).
3. **M0.7.3:** `if` and `else`. Implemented (12 November 2026); a branch that is not a block still has a scope of its own, so `if (c) var y = 1;` declares `y` for that branch only (proposed).
4. **M0.7.4:** `while`, `for` and `do … while`, with `break` and `continue`. Implemented (12 November 2026); `break` or `continue` outside a loop is WL2007 (proposed), and a `for`'s per-iteration bindings (§3.5) become observable, and are implemented, with closures in M0.7.6.
5. **M0.7.5:** string literals and concatenation. Implemented (12 November 2026), with the first heap objects (§15.2, "Heap").
6. **M0.7.6:** functions, function values and closures. Implemented (12 November 2026).

**M0.10 parts** (13 November 2026), each landing on its own with its conformance tests, on both VMs:

1. **M0.10.1:** classes: declarations, `new`, fields, methods, `this`, methods as values, `x?.name`, and text (§3.7). Implemented (13 November 2026), on both VMs.
2. **M0.10.2:** inheritance: `extends`, `super(…)`, `super.name(…)`, and the base's constructor run first. Implemented (13 November 2026), on both VMs.
3. **M0.10.3:** disposal: `dispose()` and `destructor()`, once and chained, and `using` on its ways out but exceptions. Implemented (13 November 2026), on both VMs.
4. **M0.10.4:** static members. Implemented (13 November 2026), on both VMs.
5. **M0.10.5:** `Object` and `equal()`. Implemented (13 November 2026), on both VMs.
6. **M0.10.6:** exceptions: `throw`, `try`, `catch` and `finally`, `Error`, runtime errors as exceptions, and `using` when one leaves it. Implemented (13 November 2026), on both VMs.
7. **M0.10.7:** `toString()`. Implemented (13 November 2026), on both VMs.

**M0.11 parts** (13 November 2026), each landing on its own with its conformance tests, on both VMs:

1. **M0.11.1:** tasks and `await`: `async` functions, methods and function expressions; `await` of any value, in them and at a script's top level; tasks (`Task`, `x is Task`), their exceptions, those nothing awaits, and `t.forget()`; the end of an execution; `delay(milliseconds)`; and a benchmark workload of asynchronous suspension (§5.6). Implemented (13 November 2026), on both VMs.
2. **M0.11.2:** asynchronous disposal: an `async` `dispose()`, which `using` awaits, and WL3001 where it cannot. Implemented (13 November 2026), on both VMs.

**Modules** (§3.9, 14 November 2026), in parts, each landing on its own with its conformance tests, on both VMs, before M0.12, which builds on them:

1. **Static imports and exports:** `import db from "lib/db.wl";` and `import { f, g } from "lib/util.wl";`, `export`, a module's instance and its exports, read as they are when read, each module run once, its imports first, cycles; the host finding the files; diagnostics and runtime errors naming the file they are in. Implemented (14 November 2026), on both VMs.
2. **Parameters:** `param`, the values an importer gives, and a missing one's error.
3. **Dynamic imports:** `await Module.import(path)`, a module compiled when it runs.
4. **Standard modules:** the scheme `std:`, and `std:system`, which every module imports, with `write` and `writeLn`. Implemented (14 November 2026), on both VMs.
5. **A script's parameters:** `weblingo run script.wl name=value`.

**Extending the classes of values** (§3.7, 13 November 2026), in parts, each landing on its own with its conformance tests, on both VMs:

1. **`extend` blocks:** methods and static members given to a class, a program's or a kind of value's (`extend Integer { … }`), hoisted with its block's classes, reaching every instance and subclass, made before or after, a method replacing one of the same name. Implemented (13 November 2026), on both VMs.
2. **Classed collections' text:** `Stack([1, 2])`, in place of `Stack [1, 2]`. Implemented (13 November 2026), on both VMs; replaced by part 6's.
3. **Classes extending `Boolean`, `Integer`, `Number` and `String`:** instances holding a value, given by `super(value)`, which arithmetic, comparisons and `==` use, arithmetic's result keeping a class, written `Celsius:21.5`; and `new` on those classes. Implemented (13 November 2026), on both VMs.
4. **`Function`:** `new Function(f)`, `new Function()`, and classes extending it, whose instances are functions with methods of their own. Implemented (13 November 2026), on both VMs.
5. **`Class`** (designed 14 November 2026, §3.7), in parts of its own, each landing on its own with its conformance tests, on both VMs: (5.1) making classes: `new Class(…)`, functions with a receiver, and `addMethod`, a constructor included, implemented (14 November 2026), on both VMs; (5.2) names: `__className`, `__instanceOf` and `__parent`; (5.3) properties and constants given while the program runs, `setProperty`, `setStaticProperty`, `setConstant` and `static const`, and assignment replacing a class's static method; (5.4) `extends Class`.
6. **Classed values' text:** `Stack:[1, 2]`, in place of `Stack([1, 2])`. Implemented (13 November 2026), on both VMs, for collections, and with part 3 for the other classed values.
7. **Operators:** `static operator +(a, b) { … }`, and the other operators a class may declare. Implemented (13 November 2026), on both VMs.

**M0.9 parts** (13 November 2026), each landing on its own with its conformance tests, on both VMs (§5.6, "Both VMs, for now"):

1. **M0.9.1:** member access (`x.name`, `x.name(…)`) and indexing (`x[i]`, `x[i] = v`), with strings' `length` and indexing (§3.6). Implemented (13 November 2026), on both VMs.
2. **M0.9.2:** arrays: literals, indexing, `length`, `push` and `pop`, identity and text. Implemented (13 November 2026), on both VMs.
3. **M0.9.3:** maps: literals, entries, `has`, `remove`, `length`, keys and text. Implemented (13 November 2026), on both VMs.
4. **M0.9.4:** `foreach` over arrays, strings and maps, and a benchmark workload of collections. Implemented (13 November 2026), on both VMs.
5. **M0.9.5:** sets: literals, `add`, `has`, `remove`, `length`, `foreach` and text. Implemented (13 November 2026), on both VMs.
6. **M0.9.6:** optional access, `x?[k]`. Implemented (13 November 2026), on both VMs.

**After M0.8** (order approved by the owner, 13 November 2026): M0.9, then M0.10, M0.11, M0.12 and M0.13, as the table gives them (asynchrony put before templates on 13 November 2026, the owner asking for it to be designed first). Each needs syntax, or a format, that this specification does not give yet, which is decided feature by feature before it is built; and each is built on both VMs (§5.6, "Both VMs, for now").

**Immediate next activity:** extending the classes of values (§3.7), and the module system (§3.9), the rest of both for the owner's decision; then M0.12, templates. The compiler and both VMs cover M0.6's subset and the language of §3.5 to §3.8.

---

## 10. Open questions and specification risks

These items should be explicitly tracked rather than silently treated as approved:

1. **Register versus stack VM (WL-BC002):** compare equivalent benchmark workloads; register-based remains preferred but provisional. Compared on 13 November 2026 (§5.6, "The comparison"): the register VM is faster on every workload measured, the stack bytecode smaller. The owner kept both VMs, each feature built on both, until property access, async suspension, template output and I/O can be measured too (§5.6, "Both VMs, for now").
2. **WBC encoding:** finalize variable-length operand scheme, branch addressing, header/directory layout and binary compatibility.
3. **Decimal128 binary encoding:** choose IEEE BID or alternative representation, define canonicalization, rounding and interoperability.
4. **Exceptions and cleanup:** freeze table-driven versus handler-stack mechanics; define guaranteed disposal chaining and cleanup errors. Decided 13 November 2026 (§3.7): table-driven regions, and an exception from `dispose()` replaces one already on its way out; and a disposal goes on when one of its methods throws (approved 13 November 2026, §3.7).
5. **Async disposal:** specify `using` behavior around async operations, cancellation and outstanding native resources. `using` awaits an `async` `dispose()` (§3.8, approved 13 November 2026, and implemented in M0.11.2); cancellation and native resources remain.
6. **`WebLingoValue` layout:** prove 16-byte representation, GC roots and handle validity across both toolchains.
7. **Cross-worker data:** formalize immutable sharing, serialization, ownership transfer and lifetime rules.
8. **Application generation unloading:** define retained task lifetimes, draining deadlines and leak handling.
9. **Template escaping:** define context-aware escaping and safety rules for raw output.
10. **Native ABI:** finalize versioning, calling convention, callback threading, ownership and error structures.
11. **Database API:** settle synchronous versus asynchronous connection establishment, transactions and streaming rows.
12. **Native C# readiness:** track compiler/RTL capabilities and identify adapter replacements in CI.
13. **Security:** specify concrete capability resolution and sandbox limitations; logical isolation is not a security boundary equivalent to separate processes.
14. **Scheduler fairness:** choose instruction quantum, cancellation safe points and bounded blocking pools.
15. **Initial test infrastructure:** xUnit adopted as the M0.6 default; implementation and CI configuration remain to be generated.

---

## 11. Guiding architectural invariants

1. **One language semantics across hosts and toolchains.** WebLingo behavior must not change when replacing .NET adapters with native adapters.
2. **No .NET dependency in the final native product.** The .NET 10 stage is a bootstrap and delivery strategy, not a language requirement.
3. **Portable source and bytecode.** WBC must be validated before execution and must not expose host memory structures.
4. **Clear ownership boundaries.** Worker-affine mutable heaps, explicit cross-worker transfer, deterministic resource disposal.
5. **Separation of disposal and GC.** `dispose()` is deterministic resource release; tracing GC reclaims script-object memory.
6. **Stable host-neutral interfaces.** IIS, FastCGI and standalone hosts adapt to WLHI (§6.2), not the reverse.
7. **Safe publication.** Hot reload swaps whole initialized application generations atomically.
8. **Secure by default.** Escaped template output, capability-aware services and bounded resource use.
9. **First-class template composition.** Includes, layouts, and parameterized components must all ship with the first `.wlp` template-capable release; no phased deferral.
9. **Measure before freezing VM design.** Keep the lowered IR VM-neutral and benchmark register versus stack execution.
10. **Incremental vertical slices.** Establish a working compiler/VM pipeline before expanding language and hosting complexity.

---

## 12. WL-G001 — Open-Source Distribution and Governance (proposed)

**Decision status:** WebLingo is intended to be free and open source. The public-release milestone, release scope, and MIT License remain proposals.

### 12.1 Release objective and timing

- **Proposed public-release milestone:** A functioning, independently deployable native WebLingo compiler/runtime that does not require .NET, Node.js, V8, PHP, or the separate Native C# compiler on end-user systems.
- Continue developing in C#/.NET 10 according to the approved bootstrap architecture. The repository may remain private during implementation.
- The independent Native C# compiler is a build-time toolchain, **not** a required component of the WebLingo runtime distribution; whether its own source code is published is a separate decision.
- Publish language semantics, WBC bytecode documentation, host interfaces, build instructions, and conformance tests sufficient for independent evaluation and interoperability.

### 12.2 Candidate license: MIT

**MIT is the preferred candidate under consideration, not yet selected.** Subject to rights and third-party-license review, MIT would generally permit commercial and noncommercial use, redistribution, modification, embedding in proprietary products, and proprietary forks, while requiring preservation of copyright and license notices in copies or substantial portions of the licensed software. MIT does **not** require downstream applications or modifications to be open-sourced.

Tradeoff: MIT does not require third parties to contribute improvements back. If reciprocal publication of changes becomes a goal, reconsider alternatives such as MPL 2.0 before the public release. MIT also does not expressly grant trademark rights; project branding and names must be handled separately.

**Unresolved license scope:** Determine whether MIT would cover **all** WebLingo components—language compiler, VM, runtime, standard library, IIS/ASP.NET Core and FastCGI adapters, CLI, native adapters, tests, samples, and documentation—or whether any components remain under different terms. Specify documentation licensing explicitly. No assumption is made that the separate Native C# compiler is part of the release.

### 12.3 Open-source readiness requirements (proposed)

1. **Ownership and provenance:** Track copyright holders, contributors, third-party code, assets, and generated content; verify that the project has the rights necessary to publish each component.
2. **Dependency review:** Maintain a software bill of materials or equivalent dependency inventory; verify redistribution compatibility, required notices, and transitive dependencies. Avoid introducing restrictive dependencies into the portable core.
3. **Reproducible releases:** Provide versioned source tags, documented builds, release artifacts, checksums, and automated Windows/Linux conformance tests.
4. **Specification and compatibility:** Publish versioned language, WBC, PAL/WLHI and extension contracts; state compatibility and deprecation policies.
5. **Community governance:** Establish contribution rules, code review and maintainer authority, a security disclosure process, and a code of conduct before accepting substantial public contributions.
6. **Trademark and naming:** Conduct a fresh name/trademark and ecosystem-confusion check for **WebLingo** before public launch; the MIT software license does not establish availability of the name or rights to a logo.
7. **Security:** Document the limitations of capability-based logical isolation and the need for OS-level process isolation in hostile multi-tenant deployments.

### 12.4 Proposed public repository files

```text
LICENSE                 # Standard MIT text, if MIT is finally approved
README.md
CONTRIBUTING.md
CODE_OF_CONDUCT.md
SECURITY.md
NOTICE.md                # Only if needed for third-party notices
THIRD_PARTY_NOTICES.md   # If required by dependencies
CHANGELOG.md
 docs/                   # Versioned language and runtime specifications
 src/
 tests/
 samples/
```

The actual `LICENSE` file must identify the correct copyright holder and year(s). Do not present MIT as the project's operative license until the owner expressly selects it and the repository is licensed accordingly.

### 12.5 Questions reserved for later approval

- Confirm **MIT versus another license**.
- Decide the **components covered** and documentation license.
- Decide **public repository timing**, governance, and contribution licensing (including whether a CLA or DCO is needed).
- Clear the **WebLingo** name and determine whether a trademark/brand policy is appropriate.
- Define the **native-release readiness criteria** and whether preview source releases occur earlier.

---

## 13. Template composition and reuse (WL-T001–WL-T008) — APPROVED

**Decision:** The first implementation that supports `.wlp` pages **MUST provide all three mechanisms together**: template includes, layouts, and parameterized components. Earlier suggestions to implement them gradually are superseded. Named sections remain a proposed extension, not a requirement approved by this decision.

### WL-T001 — Unified composition model

A `.wlp` file is a server-side template compiled through the common WebLingo compiler and executed by the same VM as `.wl` code. Composition does **not** introduce a second template interpreter. Three constructs are first-class:

- **Include:** embeds a reusable template fragment, usually a header, footer, or navigation region.
- **Layout:** supplies a shared page shell and a designated body insertion point.
- **Parameterized component:** renders a reusable template fragment using explicitly passed parameters in an isolated scope.

These are template composition constructs, not independent languages or client-side frameworks.

### WL-T002 — Includes (required at initial template release)

Illustrative proposed syntax:

```html
<!-- index.wlp -->
<% include "/includes/header.wlp"; %>
<main><h1>Welcome</h1></main>
<% include "/includes/footer.wlp"; %>
```

An include is resolved at compilation/linking time where feasible and participates in the template dependency graph. Paths are application-root-relative by default, subject to capability checks. The precise syntax and variable-scope rules for includes remain implementation-contract details to finalize; the **feature itself is approved and mandatory**.

### WL-T003 — Layouts (required at initial template release)

A layout defines shared document structure and a content insertion point. Illustrative syntax:

```html
<!-- /layouts/main.wlp -->
<!DOCTYPE html>
<html><head><title><%= pageTitle %></title></head>
<body>
    <% include "/includes/header.wlp"; %>
    <main><% renderBody(); %></main>
    <% include "/includes/footer.wlp"; %>
</body></html>
```

```html
<!-- products.wlp -->
<% layout "/layouts/main.wlp"; %>
<% const pageTitle = "Products"; %>
<h1>Products</h1>
```

A layout may itself use a parent layout if the implementation defines an unambiguous acyclic resolution order. Layout body insertion must not require unconditional whole-page buffering. Exact `pageTitle`/layout-data binding semantics are still to be specified.

### WL-T004 — Parameterized components (required at initial template release)

Components are independently compiled templates with **explicitly supplied parameters and isolated local scopes**. They must not implicitly capture arbitrary caller-local variables.

Illustrative component:

```html
<!-- /components/product-card.wlp; parameter: product -->
<article class="product-card">
    <h3><%= product.name %></h3>
    <p><%= product.description %></p>
</article>
```

Illustrative caller:

```html
<%
    for (const product of products) {
        await renderComponent(
            "/components/product-card.wlp",
            { product: product }
        );
    }
%>
```

The exact component parameter declaration syntax and whether `renderComponent` is a built-in function or compiler-recognized intrinsic remain to be finalized. Compiled components must be reusable without reparsing for every invocation.

### WL-T005 — Escaping and output safety

`<%= expression %>` escapes output by default, including within includes, layouts, and components. `<%- expression %>` is an explicitly unsafe/raw-output mechanism and must not be silently used by composition features. The compiler/runtime must specify context-aware handling of HTML text, attributes, URLs, scripts, and styles before claiming complete injection safety. A component must not implicitly convert untrusted strings into trusted HTML. Output remains subject to the same response commitment and streaming rules as ordinary `.wlp` rendering.

### WL-T006 — Resolution, cycles, and security

- Resolve template references within configured application roots and filesystem capabilities; reject traversal and unauthorized access.
- Detect static include and layout cycles at compilation/linking time and report source-located diagnostics.
- Bound dynamic component recursion or cycles at runtime where static detection is impossible.
- Do not allow template composition to bypass module visibility, application isolation, or capability checks.
- Maintain deterministic path normalization across Windows and Linux.

### WL-T007 — Compilation, caching, and atomic reload

Maintain a dependency graph for pages, includes, layouts, and components. When a shared template changes, invalidate affected compiled artifacts and build a **new application generation**. Publish that generation atomically only after successful compilation/initialization; existing requests finish on their previous generation. Compilation and cache identity must account for transitive dependencies, configuration, compiler version, and relevant security policy. A component invocation must not cause reparsing on every request.

### WL-T008 — Streaming, scheduling, and delivery gate

Rendering must support asynchronous output and backpressure without mandatory full-document buffering. Component and layout rendering must honor the WebLingo-owned operation model and worker-affine execution contexts. The first `.wlp`-capable release is **not complete** until all three mechanisms (includes, layouts, parameterized components) are implemented and covered by end-to-end tests, including nested composition, escaping, invalid paths, cycles, dependency invalidation, cancellation, and streaming.

**Project structure amendment:** Introduce `WebLingo.Templates` as the dedicated `.wlp` parser, composition resolver, dependency-graph builder, and lowering layer feeding the common compiler IR. Add `WebLingo.Templates.Tests` and template composition conformance tests. This is an approved architectural responsibility; the precise project boundaries and public C# interfaces can be finalized during implementation.

**Named sections:** Optional layout sections for page-specific scripts, styles, and metadata are a **proposed extension**. They are not part of the three mandatory mechanisms unless separately approved.

### Contracts — approved 13 November 2026

The owner's answers to what sections 2.2 and 13 left open, approved on 13 November 2026:

- **The language does not depend on templates.** WebLingo is a general-purpose, embeddable scripting language; nothing in the language itself depends on templates. Templates are a technology built on it, for web servers, as classic ASP was: WebLingo Pages.
- **Their compiler.** A `.wlp` compiles through a template compiler (`WebLingo.Templates`), which incorporates the WebLingo compiler and extends it; it has a command-line program of its own, `welp`, in a project of its own, `WebLingo.Pages.Cli`. `weblingo` compiles and runs the language only.
- **What a template is.** Its text is written as it stands, `<% … %>` are statements, `<%= … %>` and `<%- … %>` write their expression, and `<%# … %>` is nothing. A statement may span several blocks, the text between them written as if it were printed there, as classic ASP did: `<% foreach (var p in products) { %><li><%= p.name %></li><% } %>`.
- **Where it writes.** Where `write` writes: standard output under a command-line program, a response's body under a host.
- **Escaping.** `<%= value %>` writes the text `write` would write, an instance's `toString()` included, with `&`, `<`, `>`, `"` and `'` as `&amp;`, `&lt;`, `&gt;`, `&quot;` and `&#39;`, the same in text and in attribute values; escaping for URLs, scripts and styles comes later, as its own decision.
- **`null`.** `<%= null %>` and `<%- null %>` write nothing, so that an optional read that finds nothing is blank; `write(null)` goes on writing `null`.
- **Line breaks.** A line holding only a `<% … %>` block, and spaces, writes nothing, not even its line break, so that statements leave no blank lines.
- **Includes.** `<% include "/includes/header.wlp"; %>` is a statement whose path is a string literal, resolved and compiled with the page. It is textual: the included template sees the variables where it is included, as if its text stood there. A path starting with `/` is from the application's root, any other from the including file's folder. In the language proper, `/` is the drive's root, `./` the current folder, and `xyz/abc` the current folder's `xyz` subfolder, and so on. `import`, for what has a scope of its own, is the language's module system, for `.wl` files and templates alike, designed as a task of its own.
- **Layouts.** `<% layout "/layouts/main.wlp", { title: "Products" }; %>`: at most one for a page, given the values the layout reads as its parameters, which it declares as a component does, a missing or an extra one being an error, so that the page's body runs, and streams, where the layout calls `renderBody()` (WL-T003). A layout may itself have a layout, and so on; a cycle of them is a compile-time error.
- **Components.** `renderComponent(path, { product: p })` is a built-in: its path is any string, and the component is found and compiled when it runs. A component declares its parameters each with a statement of its own, as `var` declares a variable, `<% param product; param size = 10; %>`, each then a variable of it, a missing one an error, an extra one an error, a default making one optional, any expression, evaluated when the parameter is missing. `param` is a keyword of the language itself, a module's parameter (§3.9). A component is compiled on first use and kept, recompiled when its file changes, in the application's generation (WL-T004, WL-T007); a missing one is a runtime error, WL5xxx, which a catch may catch.
- **Asynchrony.** `async` and `await` are designed first, before templates (section 13's examples use `await`): §3.8.
- **Errors.** Template errors (a cycle of includes or layouts, a missing file, a path outside the application, a component's parameters) have a range of codes of their own, WL5001 on.

---

*This document consolidates the decisions and technical designs discussed through approval of M0.6 and WL-T001–WL-T008. It distinguishes approved requirements from proposed implementation details and deliberately unresolved contracts; it is an architectural reference, not a claim that implementation is complete.*


## 14. M0.6.1 solution scaffold — implementation status (22 October 2026)

**Status: generated; built and tested on Windows and Linux.** This is an implementation milestone, not a change to previously approved language semantics.

- Created `WebLingo.slnx`, .NET 10 project files, centralized package versions, solution-wide build settings, and GitHub Actions build/test workflow for Windows and Linux (moved on 12 November 2026 to `.github/workflows/` at the repository root, where GitHub Actions reads it).
- Created portable Core, Language, Bytecode, Compiler, Runtime, VM, Platform.Abstractions, Templates, Hosting.Abstractions, StdLib project boundaries; .NET platform, ASP.NET Core host adapter, and CLI projects.
- Added a minimal lexer seed and unit tests, public scaffolding interfaces, template-composition contract placeholders, examples, and a Native C# portability inventory.
- The CLI supports `--version` only; `run` and `check` explicitly report that execution is not implemented. No compiler, VM execution, or template rendering is claimed.
- **Verification:** On Windows (12 November 2026, .NET SDK 10.0.404, a fresh clone), `dotnet restore WebLingo.slnx`, `dotnet build WebLingo.slnx -c Release` (no warnings) and `dotnet test WebLingo.slnx -c Release` (5 tests) succeed, and `weblingo --version` answers. (An earlier static validation, with .NET SDK 10.0.401, could not restore.) On Linux, the CI workflow's first run (CI run 1, 12 November 2026, commit `5229806`) built and tested the solution, by then M0.6.7's.
- The initial `WebLingoSourceSpan` and lexer positions use .NET string indices in this scaffold; production source-map byte offsets remain specified as UTF-8 and require explicit conversion.
- Register-based execution remains provisional (WL-BC002); both stack and register execution must eventually be benchmarked.
- All three approved template composition features (includes, layouts, parameterized components) remain mandatory together in the first `.wlp`-capable implementation.

## 15. M0.6 implementation — status and proposed choices

This section records the state of the M0.6 increments after M0.6.1 (§8.7), and the choices made while implementing them where this specification is silent. Each choice in §15.2 was made to unblock the work, and is kept in one place in the code so that it stays easy to change; its row gives its status, **Approved** once the owner decided it, **Proposed** while it awaits that decision. None of them changes an approved decision.

### 15.1 Status

| Increment | Status |
|---|---|
| M0.6.2 lexer and tokenization tests | Implemented (12 November 2026); built and tested on Windows and Linux |
| M0.6.3 parser, AST and diagnostics | Implemented (12 November 2026); built and tested on Windows and Linux |
| M0.6.4 binder and lowered IR | Implemented (12 November 2026); built and tested on Windows and Linux |
| M0.6.5 minimal bytecode generator and verifier | Implemented (12 November 2026); built and tested on Windows and Linux |
| M0.6.6 minimal register VM, arithmetic, locals and `write` | Implemented (12 November 2026): `weblingo run` runs §8.4's example and prints `30`; built and tested on Windows and Linux |
| M0.6.7 end-to-end conformance on Windows and Linux | Implemented (12 November 2026): every program of §8.6 runs through the CLI with its expected output, diagnostic and exit code; passes on Windows and Linux (CI run 1, 12 November 2026, commit `5229806`) |

M0.6's acceptance criteria (§8.8), as of 12 November 2026:

| Criterion | Status |
|---|---|
| Builds cleanly on Windows and Linux | Yes, with no warnings (warnings are errors): locally on Windows, and in CI on `ubuntu-latest` and `windows-latest` (CI run 1, 12 November 2026, commit `5229806`) |
| Dependency rules enforced | By the project references, and by a test of the built assemblies' references (`WebLingo.Architecture.Tests`, `DependencyTests`) |
| CLI compiles and runs simple `.wl` programs | Yes: `weblingo run` and `weblingo check` (`WebLingo.Conformance.Tests`) |
| Lexer, parser, binder, generator, verifier and VM are separate components | Yes: `WebLingoLexer`, `WebLingoParser`, `WebLingoBinder` and `WebLingoLowerer`, `WebLingoCodeGenerator`, `WebLingoBytecodeVerifier`, `WebLingoVirtualMachine` |
| Tests pass; malformed inputs fail safely | Yes, on Windows and Linux (294 tests at M0.6.7, 305 with M0.8's checks); fuzz tests give random source to the compiler, and mutated bytecode to the VM, and find diagnostics, never a crash |
| Portability inventory exists | Yes, `Documents/NativeCompatibility.md`, with what M0.6 uses |
| Portable core has no ASP.NET Core or ADO.NET dependency | Yes, checked by `DependencyTests` |
| Compiler IR is not tied to a permanent VM choice | Yes: the lowered IR is a tree; only the code generator is register-specific |
| Build and test instructions are documented | Yes, the solution's `README.md` |

### 15.2 Proposed choices

| Area | Choice | Status |
|---|---|---|
| Line terminators | CRLF, LF, and CR each end a line (`WebLingoSourceText`) | Approved (12 November 2026) |
| Columns | One-based, counting Unicode scalar values from the start of the line; a tab is one column (`WebLingoSourceText`) | Approved (12 November 2026) |
| Whitespace | Space, tab, CR, and LF only; any other character, Unicode spaces included, is invalid (WL1003) | Approved (12 November 2026) |
| Comments | `//` to the end of the line; `/* … */`, not nested; an unterminated `/*` is an error (WL1005) | Approved (12 November 2026) |
| Identifiers | ASCII letters, digits and `_`, not starting with a digit; case-sensitive | Approved (12 November 2026) |
| Keywords | `var` and `const`; `Infinity` and `NaN`, binary64 literals, which therefore cannot be declared or shadowed; `let` reserved and reported (WL-002.3); no other reserved words yet | Approved (12 November 2026) |
| Integer literals | Decimal digits only; leading zeros allowed and still decimal (`007` is 7); at most 9223372036854775807, larger is an error (WL1004) | Approved (12 November 2026) |
| Binary64 literals | Digits, then a fraction (`.` and digits, at least one on each side of the point: `1.` and `.5` are not literals), an exponent (`e` or `E`, an optional sign, and digits), or both: `1.5`, `0.1`, `1e21`, `2.5E-3`. The value is the nearest binary64; one too large for a binary64 is an error (WL1004), and one too small becomes 0. `Infinity` and `NaN` are keywords for those values, and `-Infinity` is unary minus applied to `Infinity` | Approved (12 November 2026) |
| Literal suffixes | Digits followed directly by a letter, digit or `_` are one invalid literal (WL1004), which keeps room for the decimal suffix `m` (§3.2) | Approved (12 November 2026) |
| Not yet lexed | Decimal literals: their characters are invalid (WL1003) until their syntax is specified | Approved (12 November 2026) |
| Statements | Variable declarations and expression statements, each ended by `;`; a lone `;` (an empty statement) is an error (WL1001); blocks, `{ … }`, from M0.7.1 (§3.5) | Approved (12 November 2026) |
| Operators | Unary `-` and `+` (`+` gives the number itself; on anything but a number either is WL3001); binary `* /` above `+ -`, both left-associative | Approved (12 November 2026) |
| Assignment | An expression, right-associative (`a = b = 3`), whose value is the value assigned; only a variable's name may be assigned (WL1007), not even a parenthesized one | Approved (12 November 2026) |
| Calls | `callee(argument, …)`, no trailing comma; the callee is any expression, checked when bound | Approved (12 November 2026) |
| `let` | Reported as not a WebLingo keyword (WL1001), with a hint to use `var` or `const`, and parsed as `var` to recover | Approved (12 November 2026) |
| Declaration without a value | `var x;` and `const x;` are syntax errors (WL1006, §3.1) | Approved |
| Error recovery | After an error, parsing resumes at the next `;` or declaration keyword; a token the lexer reported is not reported again, and a statement gets at most one diagnostic for what follows its first error | Approved (12 November 2026) |
| Nesting limits | Parsing recurses at most 256 levels (parentheses, unary operators, assignments, arguments), and an expression is at most 1000 operations deep, so that a recursive compiler stage cannot overflow its stack; deeper source is an error (WL1008). Both limits are arbitrary, and are constants that are easy to change (`WebLingoParser.MaxRecursionDepth`, `WebLingoParser.MaxExpressionDepth`) | Approved (12 November 2026) |
| Compilation stages | Binding runs only on a source without syntax errors, and lowering only on one that binds without errors: a source reports the errors of its first failing stage | Proposed |
| Host built-ins | The host declares its built-ins (name and arity) to the compiler; they live in a scope around the program's and are not privileged in any way, so a declaration may shadow one (`var write = 1;`) | Approved (12 November 2026) |
| Functions as values | Functions are values (M0.7.6, §3.5), and so are the host's built-ins (`var w = writeLn;`). Calling a literal is WL2005 at compile time, and calling a built-in or a declared function by its name with the wrong number of arguments is WL2006 at compile time; any other call is checked when it runs, with the same codes. Naming a built-in to assign it is WL3001 | Approved (12 November 2026) |
| Functions and calls | A function declaration is in scope throughout its block, and its name is a constant (WL2003). A variable a closure captures lives in a cell, which its block makes when it begins and its declaration initializes, so that every closure of it shares it; a `for`'s captured variable gets a new cell, holding the last value, before each step, so that each iteration's closures see their own. A hoisted function that reads such a variable before its declaration has run is WL2004 at run time (approved 13 November 2026, §3.5). `return` outside a function is WL2008. A call pushes a frame onto the VM's own stack, at most 10,000 deep (`WebLingoResourceBudget.MaxCallDepth`, WL3003). A function is written as `function`, and functions are equal only to themselves | Proposed |
| Members and indexing | From M0.9.1 (§3.6): a point without digits on both sides is a member access's, so `1.` and `.5` are a number and a point, and `5.length` is the integer's member (WL3006). A member is never assigned (`x.length = 1` is WL1007 at compile time) until classes have fields. Calling a member that is not a method (`s.length()`) is WL2005. A method call's target is evaluated before its arguments, and an element assignment's target, then its index, then its value. From M0.9.2: a method read without a call (`a.push`) is WL3001; an array's elements are evaluated in order; one being written already is written `[…]`, with the character U+2026. From M0.9.3: in a map literal, a later key replaces an earlier equal one (`{a: 1, a: 2}` is `{"a": 2}`), and a map's text writes a key as it writes a value (`{1: "one"}`), a map being written already as `{…}`. From M0.9.5: `#{` is one token, and `#` alone is still an invalid character; a set is a map's hash table without values. From M0.9.6: `?[` is one token, and `?` alone is still an invalid character; the index is evaluated even when the target is `null`; each `?[` stands alone, so a member or an index after one that gave `null` is read as usual (`m?["a"].length` is WL3006 when there is no `"a"`); a map is a hash table whose entries a removal fills from the last, so their order is not defined (§3.6) | Proposed |
| Classes | From M0.10.1 (§3.7): `class`, `new` and `this` are keywords, and `?.` one token. A class's value is made when its block begins, as a function's closure is, its methods being closures of the scope around it. A method is a function whose first parameter is `this`. `new` needs its parentheses (`new A;` is WL1001); calling a class by its name is WL2005 at compile time, and any other call of a class at run time. A constructor's own result is dropped: `new` gives the instance. `x.name(…)` on an instance calls a field holding a function as any function is called, and a method on the instance, a field being looked up first; a field cannot take a method's name (WL3001), and only an instance has fields to assign (WL3001 otherwise). A method read as a value is bound anew each time, so `p.m == p.m` is false. A class written as a value is `class Point`, and has no members but its static ones (from M0.10.4; `Point.x` is WL3006 when it has no static `x`). An instance keeps its fields in the order they were first assigned, which its text follows, the order being not defined. From M0.10.2: `extends` and `super` are keywords. A class's base is evaluated when its block begins, as the class is made then: it may name the block's classes, whatever their order (the compiler makes a base before the classes that extend it), and what is declared around the block, but not the block's variables (WL2001); a base that is not a class is WL3001, and a class that extends itself is WL2013. A class without a constructor gets one that only runs its base's; a constructor of a derived class that does not call `super(…)` anywhere, a function inside it included, runs `super()` first. `super(…)` outside a constructor, `super` outside a method of a derived class, and `super` neither called nor naming a member, are WL2009. A class's methods are its base's with its own in their place, gathered when it is made; `super.name` is the base's method bound to `this`, and a method the base does not have is WL3006 | Proposed |
| Disposal | From M0.10.3 (§3.7): `using` is a keyword. A `using`'s parentheses take `var` only: `const` or `let` there, or no keyword, is reported (WL1001) and read as `var`, for one diagnostic. Its value is evaluated before its name exists, so `using (var r = r)` names an outer `r` (WL2001 if there is none). `destructor()` is a name a class may give its `dispose()`: callers call `dispose()`, and `x.destructor()` is WL3006. A class's disposal is its own `dispose`, if it declares one, then its base's disposal, gathered when the class is made. Disposing an instance whose class and bases declare none is WL3006, at `x.dispose()` or at the `using`'s end, and so is a `using` of a value that is not an instance; a field holding a function, even one named `dispose`, does not make a value disposable. Disposal gives its methods no arguments: `x.dispose(1)` is WL2006, and so is a dispose method that takes some, when the disposal begins, before any of its methods runs. An instance's disposal begins once: from then on, `x.dispose()`, inside a dispose method too, does nothing and gives `null`; and a disposal gives `null`, whatever its methods return. `x.dispose` read as a value is bound as any method is, and calling it disposes `x` as `x.dispose()` does; `super.dispose()` is the base's method called as any other is, so it runs, besides the disposal's own run of it. A `using`'s disposal runs on each way out of its statement, innermost first: a `return`'s value is evaluated before it, and a `break` or a `continue` disposes the resources of the `using` statements inside its loop only | Proposed |
| Static members | From M0.10.4 (§3.7): `static` is a keyword. A static field's value is evaluated when its class is made, where its block begins, as a base is: it may name the block's classes and functions, and what is declared around the block, but not the block's variables (WL2001). Once a block's classes are made, their static fields are set, class by class in the order they are made (a base before the classes that extend it), each class's in its own order; a static field exists from when its class is made, as `null`, so that one read before it is set gives `null`. `this` in a static field's value is WL2009, as in a static method, and so is `super` in a static method. `static const` or `static let` is reported (WL1001) and read as `static var`, `static var x;` is WL1006, and a static `constructor` is WL1001. A class's static members have names of their own, apart from its methods' (a static member and a method may share one), but not from each other (WL2002). Static members are reached through a class, and from 14 November 2026 through an instance or a collection of the class, after its fields and its class's methods, a static method so reached called without `this`; assigning one through an instance, `p.count = 5`, assigns a field of the instance, which then shadows the static (asked of the owner). Assigning a static field that neither the class nor a base declares declares it, on the class itself, as the owner's note of 13 November 2026 has it (§3.7), a built-in class included (`Integer.twice = …`); assigning a static method is WL3001. `Name.name(…)` calls a static member as any function is called, a static field holding a function included | Proposed |
| `Object` | From M0.10.5 (§3.7): `Object` is a class written in WebLingo, `class Object { equal(other) { return this == other; } }`, which the compiler adds to a program that uses it, made where the program begins, in a scope around the program's. A program may declare a name `Object` of its own; a class without `extends` still extends the built-in one, and `Object = 1;` is WL2003, as for any class. Every class but `Object` is therefore derived: `super.name` is allowed in any class's methods (`super.m()` where `Object` has no `m` is WL3006 when it runs, no longer WL2009), and `super()` in any constructor; a class without `extends` does not run `Object`'s constructor, which does nothing. An instance of `Object` is written `Object {}`. A limit of the execution reached inside `Object`'s method (WL3003, WL3004) is reported at a position of its source, not of the program's | Proposed |
| Exceptions | From M0.10.6 (§3.7): `try`, `catch`, `finally` and `throw` are keywords. `throw;` outside a `catch` block, in a function inside one included, is WL2010. `Error` is a class written in WebLingo, `class Error { constructor(message) { this.message = message; this.code = null; } }`, extending `Object`, which the compiler adds to a program that names it or has a `try` or a `using`. A runtime error becomes an instance of it only when a handler catches it, its fields set without running its constructor (`Error {message: "…", code: "WL3005"}`), so that a program that catches nothing reports its runtime errors as before. A `catch`'s variable is a new one for each exception, which a closure may capture. An exception that nothing catches is reported where it was first thrown: an instance of `Error`, or of a class extending it, whose `code` is a string, as that code and its `message`, so that a runtime error caught and thrown again (`throw;`, or a `finally`'s end) is reported as it would have been; an `Error` without a code as WL3008 with its message; any other value as WL3008 with its text. `throw e;` throws `e` from where it stands, `throw;` from where it was first thrown. When a dispose method throws, the disposal's next methods still run, The execution's limits (WL3003), a cancellation (WL3004) and invalid bytecode (WL4001) are not exceptions, and run no `finally` | Proposed |
| `toString()` | From M0.10.7 (§3.7): `Object`'s `toString()` gives `this`'s text, an instance's as its class's name and its fields, by an operation, `ToText`, that nothing else has. A value that is not an instance has `Object`'s `toString()` as its own, `null` and a class included (`null.toString()` is `"null"`, `Point.toString()` `"class Point"`), a class's static `toString` coming first. What `write` writes of an instance whose class overrides `toString()` is the string it gives, as it is, unquoted, inside a collection too; an instance being written as its fields, by `Object`'s `toString()`, is written `Name {…}` where it holds itself. A `toString()` that takes arguments is WL2006, and one that throws throws from the `write`, both where the text is written. The VMs run an overriding `toString()` as a call of the instruction that writes, on their own stacks, the text built with a stack of its own (`WebLingoTextBuilder`, `WebLingo.Runtime`), kept as a root while it waits; a built-in that writes text says so (`IWebLingoTextBuiltin`), and is given the text as a string. A program none of whose classes overrides `toString()` writes as before, without any of this | Proposed |
| Typed catches and classes of values | From typed catches (§3.7): `is` is a keyword, at the comparisons' precedence, its class a name or a dotted name. A catch's class is evaluated when an exception reaches it, where the `try` is; catches are tried in order; a catch that catches every exception before the last is WL1001; and an exception none catches is thrown again, from where it was first thrown, after the `finally`. The built-in classes (`Object`, `Error`, the kinds of values' and of runtime errors') are written in WebLingo, and added to a program that names them; a program with a `try` or a `using` gets every runtime error's class, and a runtime error that a handler catches becomes an instance of its kind's class (`IndexError {message: …, code: "WL3005"}`). `Class` is sealed until what extending it means is built: extending it, or `new` on it, is WL3001; and so is `Task`, from M0.11.1 (`Boolean`, `Integer`, `Number`, `String` and `Function` were too, until classes could extend them, below). A value whose kind's class the program has not made is still an `Object`. A catch, or `is`, whose name is not a class calls its `equal(value)`, `IsInstance` making that call as a method call is made, and tests its result as a condition, so that one that is not a Boolean is WL3001 there; before the program has `Object`, `==` decides (`WebLingoMembers.IsInstance`). `x.equal(y)` on a value that is not an instance calls `Object`'s `equal`, as `toString()` is called | Proposed |
| Classes extending `Array`, `Map` and `Set` | From classes extending collections (§3.7): a collection that a class extending `Array`, `Map` or `Set` made is a collection of that kind, with its class and fields beside its elements, and its value marked as such, so that a plain collection's members cost nothing more. On it, a member is its field, then its class's method, then the collection's own member, so that a class may override `push`; `super.push(…)` is WL3006, `Array` declaring no methods in WebLingo. Its text, `Stack:[1, 2]`, does not show its fields; its class's `toString()` is used, as an instance's is. `dispose()` and `using` do not apply to it yet. `new Array()`, `new Map()` and `new Set()` are of no class, as `[]`, `{}` and `#{}` are | Proposed |
| Default values | From default values (§3.5): a default is evaluated each time its parameter is left out, when the function begins, left to right, in the function's scope but before its own parameter is declared, as a variable's initializer is (§3.1), so that it may use the parameters before it (`b = a * 10`), and `this` in a method, while `b = b` names an outer `b`. A parameter without a default after one with a default is WL1001. A call giving too few or too many arguments is WL2006 (`'f' takes 1 to 2 arguments`), at compile time for a function the call names, as before. A VM puts a missing value in the place of a parameter left out, which the function's code replaces with its default before anything else reads it, so that a `null` given is not taken for a missing one | Proposed |
| Asynchrony | From M0.11.1 (§3.8): `async` and `await` are keywords. `async` stands before `function`, in a declaration or an expression, and before a method's name; an `async` constructor is WL1001. `await` is a prefix operator, as unary `-` is, so `await a + await b` adds two results. `await` in a function that is not `async`, a function inside an `async` one included, is WL2014; at the top level, it makes the top level an `async` function itself, whose task is the program's, and the execution ends when that task completes; a top level that does not await ends the execution where it ends, the work its tasks have left being dropped. An `async` function's call makes its task, and runs it on the caller's stack until its first `await`, its `return` or its exception, when the call gives the task; from then on its frame is kept by its task, off the VM's stack, the values it holds being roots of the heap, and the scheduler resumes it alone on the stack. The scheduler resumes tasks in the order they became ready; `await` of a value that is not a task, or of a task already complete, makes its task ready after those ready already. With nothing ready, the earliest `delay` completes, on the host's clock (`WebLingoExecutionContext.Clock`, an `IWebLingoExecutionClock`, which the CLI gives as the platform's monotonic clock), or, without one, in virtual time, at once, so that tests take no time; timers due together complete in the order they were made. With nothing ready and no timer left while the top level waits, nothing can ever complete what it waits for: WL3009. An exception leaving an `async` function faults its task, and is thrown at each `await` of it, from where it was first thrown, a runtime error becoming an instance of its kind's class at each, anew. A task is observed once something awaits it, even before it faults; when the execution ends, the top level's exception, if it faulted, is reported, else the first fault that nothing observed or forgot, in the order they faulted, where it was first thrown. A task is written `task`, is equal only to itself, and cannot be a key (WL3001); `t.forget()` takes no arguments and gives `null`. `delay(milliseconds)` takes an integer of 0 or more (WL3001 otherwise). An `async` `toString()` gives a task, which is WL3001 where the text is written. From M0.11.2, a disposal one of whose methods is `async` is `async` as a whole: its methods run in turn in the frame of one call, which an `await` in one of them suspends, the next running once it returns, and the disposal's task completes, with `null`, after the last, or faults with the exception they threw (§3.7); `x.dispose()` then gives that task (§3.8); a `using` awaits it, dropping what it gives, in an `async` function and at the top level, which a `using` therefore makes `async`, and is WL3001 at its end in a function that is not `async`, before the disposal begins, so that the resource stays undisposed; and a `using` whose disposal is not `async` does not await, even in an `async` function. In the bytecode, `Await` (register: A, the destination, B, the value; stack: pops a value, pushes what the `await` gives) is allowed only in a function marked `IsAsync`, as both verifiers check; a resumed frame goes on after its `Await`, or throws there | Proposed |
| Classes holding a value | From classes extending `Boolean`, `Integer`, `Number` and `String` (§3.7): these four are classes written in WebLingo, whose constructors give the instance its value, `class Number { constructor(value = 0.0) { hold(this, value); } }`, `hold` being theirs alone (`Hold` in the bytecode). `new` on one of them gives a plain value, the one given or its kind's first (`0`, `0.0`, `""`, `false`), an integer given to `Number` becoming a binary64, the one conversion; any other kind is WL3001, and more than one argument WL2006. A class extending one holds a value of that kind, `null` until its base's constructor gives one; a class holding a value that declares no constructor passes its arguments on to its base's (as every class does, §3.7). Arithmetic is on the values held: its result is an instance of its left operand's class, if that holds a value, else of its right's, holding the result, its constructor not run, with that operand's fields; a result not of that class's kind (an integer's class divided, giving a binary64) is a plain value (approved 14 November 2026). Unary `-` and `+` keep the class; comparisons and `==` compare the values held; `!` gives a plain Boolean; and a condition, `&&` and `||` take a Boolean an instance holds. An instance holding a value has its fields, then its class's methods, then its value's members (a string's `length`), and is indexed and visited by `foreach` as its value is. It is written `Celsius:21.5`, a string it holds quoted (`Name:"Ann"`), unless its class overrides `toString()`; it is not a key (WL3001). From `Function`: `Function` is written so too, `constructor(f = function () {})`, holding a function, a built-in or a bound method; `new Function(f)` is `f` itself, and `new Function()` an instance of `Function` holding a function that does nothing and gives `null`, written `Function:function`; an instance holding a function is called as that function, its class's methods and fields beside. `Class` and `Task` stay sealed | Proposed |
| Operators | From operators (§3.7): `operator` is not a keyword. `static operator +(a, b) { … }`, in a class or an `extend` block, declares an operator, a static method named `operator+`, which no program can name otherwise; a class may declare `+`, `-`, `*`, `/`, `<`, `<=`, `>` and `>=`, each with two parameters (WL1001 otherwise), but not `==` or `!=` (WL1001), `==` staying identity for instances (§3.7, "Equality"), nor unary `-` and `+`. An operation calls one when either operand is an instance, or a collection, of a class that declares it, or whose base does, its left operand's class first, with its operands as arguments, as a call of the operation's instruction, whose result is the operation's; a value that is not an instance (`5`) has no operators. The VMs find one where their own arithmetic and comparisons give up, so that an operation on numbers costs nothing more | Proposed |
| Extensions | From `extend` blocks (§3.7): `extend` is a keyword. An `extend` names its class by a name or a dotted name, which is evaluated where its block begins, as a base is: it may name the block's classes and functions, and what is declared around the block, but not the block's variables (WL2001); a value that is not a class is WL3001 there. Extensions run in their order once the block's classes are made and their static fields set, each then setting its own static fields in their order. An extension's methods have `this`, but no `super` (WL2009); it cannot have a constructor (WL1001); `destructor()` is `dispose()`, as in a class (WL2011 for both); its names are its own's once (WL2002), its static members' apart from its methods'. A method it gives replaces the class's own of that name, in the class's disposal too; a static field it declares is the class's own, beside a base's of that name, `null` until its value is set, and replaces a static method of that name, as a static method replaces a static field. A class gathers its methods from its bases anew when they are next looked up after an extension has run, so that its instances and subclasses, made before or after, have the new ones. Once an `extend` has run, a value that is not an instance has its kind's class's methods, before its own members (so `extend Array { push(v) { … } }` replaces arrays' `push`), or `Object`'s if the program has not made that class; `null` has none, but `Object`'s `toString()` and `equal()`; and extending `Class` gives every class methods, after its static members (`P.describe()`). `write` writes a value that is not an instance as before, even when its class's `toString()` is extended, which only `x.toString()` calls. In the bytecode, `Extend` (register: A, the class, B, an entry of the module's class table; stack: A, the entry, popping the class), an extension being an entry without a constructor | Proposed |
| Classes made while the program runs | From making classes (§3.7): a function declaration or expression may take `this` as its first parameter, its receiver, without a default; `this` anywhere else in a parameter list, a method's included, is WL1001. Such a function is bound as a method of no class: `super` in it is WL2009, and a property's `field` does not reach into it; its arity counts its receiver, so `describe(p)` for `describe(this, prefix)` is WL2006 at compile time. A call that has a target, a method's, a bound method's or `new`'s, gives a function with a receiver that target; any other gives it its first argument: a function value's, a static member's, and a field's (`p.g(q, 3)`, where the field `g` holds such a function, runs it on `q`). A function without a receiver given as a method runs without one. `new Class(name, base)` takes at most two arguments (WL2006): a name, a string or `null` for none (WL3001 otherwise), and a base, a class, or `null` for `Object` (WL3001 otherwise, a sealed one included). A class so made has no constructor until `addMethod` gives it one: `new` on it passes its arguments on to the nearest base that declares one, as any class's does, and with none takes none (WL2006), and `super()` in a class extending it runs nothing. An unnamed class is written `class`, its instances `class {x: 1}`, and a message calls it "an unnamed class". `Point.addMethod(name, f)` takes two arguments (WL2006) and gives `null`; its name is a string that is a name, ASCII letters, digits and `_`, not first a digit (WL3001 otherwise), so that no program names a property's accessor or an operator so; `f` is a function (WL3001 otherwise, a built-in, a bound method and a class included); `"constructor"` gives the class its constructor, and `"destructor"` its `dispose`, as in a class. It is a built-in method of every class, a program's own or a built-in one (`Integer.addMethod("twice", f)` gives integers a method, as `extend` does), which a static member of its name hides, and which, read without a call, is WL3001, as `a.push` is. In the bytecode nothing is new: a function with a receiver is marked `HasThis`, as a method is, and `MakeClosure` may make one, the verifiers no longer requiring that it make no method; `new` on `Class` goes the way `new Integer(…)` goes | Proposed |
| Properties | From properties (§3.7): `property`, `get` and `set` are not keywords: `property name {` begins a property in a class or an `extend` block, `static` before it for a static one, and a method may still be named `property`. A property is a getter, `get { … }`, a setter, `set(value) { … }`, or both, each at most once: methods named `get:name` and `set:name`, which no program can name otherwise, so that a class gathers, inherits and replaces them as it does its methods. A property without either is WL1001, and so is a setter that does not take one parameter; an accessor cannot be `async`. A property's name is a member's, as a method's or a static field's is: a property and a method, or a static property and a static field, of one name in one class or extension are WL2002. Reading `x.name` finds an instance's fields, then its class's methods, then its static members, then its properties; a class's static members, then its static properties; and a value that is not an instance its kind's class's properties after its methods, as an `extend` gave them. Assigning `x.name = v` finds a property before a field, so that an instance never has a field of a property's name, and a class's static field before its static property; a property without a setter is WL3001, and reading one without a getter WL3006, `x?.name` too. Calling a property, `p.area()`, is WL3001: it is read, then what it gives called. An assignment's value is the value assigned, whatever the setter does with it: a setter's `return;` ends it, and `return value;` in one is WL1001. `field` is the property's field, an instance's for a property of instances and its class's for a static one, named `field:name`, which no program can name otherwise, `null` until assigned, in a function inside the accessor too; a variable named `field` declared in the accessor means that variable there, while one declared around the property does not reach into it; a static accessor of an `extend` block has no `field` (WL2001). A static property is reached through its class, not through an instance. `super.name` reads the base's property through its getter. In the bytecode nothing is new: `GetMember`, `SetMember` and `GetSuper` call a getter or a setter as a call of their instruction, on the property's target, its result where the instruction's goes, a setter's (the value) to the value's register on the register VM, and dropped on the stack VM for an assignment whose value is not kept | Proposed |
| Modules | From static imports and exports (§3.9): `import` and `export` are keywords, and `from` one only in an import. An import takes a module's instance, `import db from "lib/db.wl";`, names it exports, `import { f, g } from …;`, or both, `import db, { f } from …;`; its names are constants (WL2003), a name it takes read as the module's export each time it is read. Imports and exports stand at a file's top level (WL1001 elsewhere), and `return` does not (WL2008). A file sees the built-in classes, but none of another file's names. A file an import names is found by the host, compiled with the program, and run as an `async` function of its own, once: its instance is made where its function begins, before its imports run, so that a cycle gets it as it is so far, and an export read before its module has set it is WL2004; the program's built-in classes are made before its imports run. A file that cannot be found is WL2015, at its path. A module's instance is written `module lib/db.wl`, is a `Module`, cannot be assigned (WL3001), and has no member it does not export (WL3006); a module that imports itself gets its own instance. The CLI finds a file from the importing file's folder, adding `.wl` to a path without an extension, and names it by its path from the current folder, with `/` between its folders, the program's own by the path it was given; diagnostics and runtime errors name the file they are in (`lib/db.wl(4,12)`). In the bytecode, a module holds a table of its files (`WebLingoUnitInfo`: a name, a function, and the names it exports), the program's first; `RegisterUnit` (register: A, a file, B, its function's closure; stack: pops it), `MakeModule` (register: A, the destination, B, the file, C, the first of D registers holding its exports' cells; stack: A, the file, B, the count, popped) and `Import` (register: A, the destination, B, the file; stack: A, the file, pushing the instance, or the task of running it, which the code then awaits); and each function names its file, if it is not the program's. From standard modules: an import whose path begins with `std:` names a standard module, which the compiler makes itself, never from a file: `std:system`, whose exports are the host's built-ins (`write`, `writeLn` and `delay` under the CLI), its instance made where the program begins, before its imports run; any other is WL2015. Every module still sees the built-ins without importing `std:system`. In the bytecode, a standard module is a file without a function, its name beginning `std:`, its `MakeModule` given the built-ins' values | Proposed |
| `foreach` | From M0.9.4 (§3.5, §3.6): `foreach` is a keyword, and `in` is one only in a `foreach`'s parentheses; `const` or `let` in place of `var`, or another name in place of `in`, is reported and read as meant, for one diagnostic. The collection is evaluated once, before the loop's variables exist, so `foreach (var x in x)` loops over an outer `x`. The loop keeps the collection and its position in two hidden locals, followed by its variables, which a closure captures each in a new cell on each iteration. A string is visited by Unicode scalar value. A collection that cannot be looped over is WL3001, at the collection | Proposed |
| Members and indexing in the bytecode | Register: `GetMember`, `CallMethod` (its target and its arguments in consecutive registers), `GetIndex`, `SetIndex`, `NewArray` (its elements in consecutive registers) and `NewMap` (each key, then its value, in consecutive registers); stack: `GetMember`, `CallMethod`, `GetIndex`, `StoreIndex`, which may push the value back, a `GetIndex` operand (register D, stack A) being 1 for `?[`, `NewArray` and `NewMap`, and from M0.9.5 `NewSet` (its values in consecutive registers, or on the stack). From M0.10.1: `MakeClass` (a class of the module's class table, its methods' closures made as their captures say, from M0.10.2 with the class it extends: register C if D is 1, stack popped if B is 1), `GetSuper` (from M0.10.2: a class, `this` and a name, giving the base's method or constructor bound to `this`), `New` (register: the class and the arguments as `Call` takes them; stack: the class below the arguments) and `SetMember` (stack: which may push the value back), a `GetMember` operand (register D, stack B) being 1 for `?.`; a module holds a class table, each class with its constructor and its methods, and a function a flag, `HasThis`, for a method, which both verifiers check: a class's functions are methods, and `MakeClosure` makes no method. From M0.9.4, `ForEachNext` (register: the exit, the collection's register, with the position and the items in the registers after it, and the item count; stack: the exit and the collection's local, with `ForEachNextEntry` for a key and a value), which the verifiers treat as a conditional jump whose way out writes the position only. From M0.10.3, `Dispose` (register: A, the resource's register; stack: the resource, popped), a `using`'s disposal, which does nothing for `null`, and which the verifiers treat as a read; each VM runs an instance's dispose methods in turn in the frame of one call, the first as that call's, each next one as the last returns. From default values, `JumpIfGiven` (register: A, a parameter's register, B, the target; stack: A, the target, B, the parameter's local), which jumps when the caller gave the parameter, and each function's `OptionalCount`, how many of its last parameters may be left out. From typed catches, `IsInstance` (register: A, the destination, B, the value, C, the class; stack: pops the class, then the value, and pushes a Boolean), and a module names its kinds of values' classes, by `WebLingoValueClass` (`WebLingo.Core`), and its runtime errors' classes, by their codes. From M0.10.7, `ToText` (register: A, the destination, and B; stack: pops a value, pushes its text), and a module names its `Object` class, whose `toString()` a VM keeps as every value's. From M0.10.6, `Throw` (register A; stack: popped) and `Rethrow` (register A, the exception, and B, where it was first thrown, an int64 naming a function and an instruction; stack: both popped), and each function's handler table (§5.4's table-driven regions): ranges of its code, innermost first, each with its handler's instruction and the two registers, or locals, its handler receives the exception and its origin in, and on the stack VM the stack's depth there. The verifiers treat every instruction of a range as able to go to its handler, with what is written before it. A jump out of a protected range (`break`, `continue`, `return`) runs the `finally` bodies and disposals it leaves, copied there, outside the ranges they belong to; and a module names its `Error` class, which a VM keeps when it makes it. From M0.10.4, a module's class table gives each class its static methods, functions without `HasThis`, and its static fields' names, which the verifiers check; `MakeClass` makes the static methods' closures too, and `GetMember`, `SetMember` and `CallMethod` on a class reach its static members. From M0.11.1, `Await`, and each function's `IsAsync` (see "Asynchrony"); and from `extend` blocks, `Extend` (see "Extensions"). A member's name is a string constant, which each VM resolves to a member once, when it loads the module; the semantics are `WebLingoMembers`' (`WebLingo.Runtime`), the same for both VMs | Proposed |
| Use before declaration | A name used before its declaration, or in its own initializer, means the outer declaration, or is undefined (WL2001, at compile time) if there is none (§3.1) | Approved |
| Evaluation order | Left to right: operands, then arguments; an assignment evaluates its value before it stores it | Approved (12 November 2026) |
| Instruction model | An in-memory register instruction model, not WBC (§5.3, §8.4): an operation and up to four integer operands A to D, unused ones 0 (`WebLingoBytecodeContracts.cs`); a module holds its constants (int64 and binary64, kept as IEEE bits), the built-ins it imports by name and arity, and one function, with a source span per instruction and the names of its locals for runtime messages | Approved (12 November 2026) |
| Registers | A function's locals take registers 0 to n − 1 and its temporaries the registers above; at most 65535 registers (§5.3), more is a compile-time error (WL4002) | Approved (12 November 2026) |
| Operations | `LoadConstant`, `Move`, `Negate`, `Plus`, `Add`, `Subtract`, `Multiply`, `Divide`, `CallBuiltin` (arguments in consecutive registers, §5.4), `Jump`, `Return`; from M0.7.2, `LoadNull`, `LoadBoolean`, `Not`, `Equal`, `NotEqual`, `Less`, `LessOrEqual`, `Greater`, `GreaterOrEqual`, `JumpIfFalse` and `JumpIfTrue` (whose condition must be a Boolean, WL3001), and `CheckBoolean` (the right operand of `&&` and `||`); from M0.7.6, `NewCell`, `BoxRegister`, `CopyCell`, `LoadCell`, `StoreCell`, `LoadCapture`, `StoreCapture`, `MakeClosure`, `LoadBuiltin`, `Call` and `ReturnValue`, and a module holds its functions besides its entry point, each with its parameter count and its captures | Approved (12 November 2026) |
| Verification | Besides §5.5: unused operands must be 0; no path may run off the end of the code; every register, local or temporary, must be written before it is read on every path, which is what keeps a variable from being read before it has a value (§3.1); a function whose check needs more than 16 million words of state or 100 million steps is rejected rather than verified slowly. The bounds are arbitrary, and are constants that are easy to change (`WebLingoBytecodeVerifier.MaxStateWords`, `WebLingoBytecodeVerifier.MaxSteps`), as is the register limit (`WebLingoFunction.MaxRegisters`) | Approved (12 November 2026) |
| Compiler output | The compiler verifies what it generates, and reports a module that fails as an internal error (WL4001), with no module | Proposed |
| Runtime values | `WebLingoValue`, a kind and a 64-bit payload, 16 bytes (§4.1's target, met): null, Boolean, int64 and binary64 (§8.4); scripts write `null`, `true` and `false` from M0.7.2 | Proposed |
| Heap | Strings are the first objects, then closures, cells and, from M0.9.2, arrays, and from M0.9.3 maps, whose units are their element or entry count, which grows and shrinks with them, within the budget: a value of kind String holds a handle into its execution's heap (`WebLingoHeap`), never a .NET reference (§4.1); the heap is the execution's own (§4.3), and is collected by WebLingo's nonmoving mark-and-sweep (§4.2) from its roots, the VM's registers and constants. A string's text is kept as a .NET string for now (UTF-16; §3.3's UTF-8 representation is still proposed). Its size is counted in units (a string's UTF-16 length, plus 16 per object): a collection runs at twice what was live after the last one, at least 1M units, and the budget is 64M units (`WebLingoResourceBudget.MaxHeapUnits`), beyond which an allocation is WL3003; all of these are constants that are easy to change | Proposed |
| String operations | `+` of two strings concatenates them; `==` and `!=` compare their texts, code unit by code unit; a string is written as its own text | Approved (12 November 2026, §3.5), but for the comparison, which is proposed |
| Mixed arithmetic | An int64 meeting a binary64 is converted to binary64 (`1 + 0.5`, and `10 / 4 + 1` is 3.5) | Approved (12 November 2026) |
| Binary64 results | IEEE 754 throughout: `1 / 0` is `Infinity`, `-1 / 0` is `-Infinity`, `0 / 0` is `NaN`; only integer arithmetic raises an error (WL3002). `Infinity` and `NaN` are written as keywords (see "Binary64 literals") | Approved (12 November 2026) |
| Invalid operands | Arithmetic on anything but numbers is WL3001; once strings exist, `+` also concatenates them | Approved (12 November 2026) |
| `write` | `write(value)` writes the value as text, with no line break; `writeLn(value)` writes it and a line break (LF); each takes one argument and gives `null`; the host (the CLI) registers them, and they are implemented once in `WebLingo.StdLib`. A line break can also be written as text, once string literals exist | Approved (12 November 2026) |
| Text of a value | `null`, `true`, `false`; an int64 in decimal digits; a binary64 as the shortest decimal that reads back as the same number, always with a decimal point or an exponent (`5.0`, `3.5`, `0.1`, `-0.0`, `1E+21`), or `NaN`, `Infinity`, `-Infinity`; never culture-dependent (`WebLingoValueFormatter`) | Approved (12 November 2026) |
| Linking | The VM verifies a module, then links its imports to the host's built-ins by name and arity; a missing one stops the module before it runs (WL2001) | Proposed |
| Runtime errors | The first runtime error stops the execution, and is reported with the source span of the instruction that raised it; output printed before it stays | Proposed |
| Budgets and cancellation | An execution has an instruction budget (exceeded: WL3003) and a register budget (a module needing more is not run: WL3003); the VM looks at the host's cancellation signal every 1024 instructions (WL3004); a WebLingo-owned signal, not .NET's `CancellationToken` (WL-P007) | Proposed |
| Built-in failures | A built-in that fails, even by throwing, is a runtime error (WL3001) | Proposed |
| CLI | `weblingo run <file>` compiles and runs; `weblingo check <file>` compiles and reports, without running; `weblingo --version` prints `WebLingo M0.6 (pre-alpha)`. Diagnostics go to standard error as `file(line,column): error CODE: message`. Exit codes, each with one meaning: 0 success, 1 compilation errors, 2 a usage error, 3 a runtime error, 4 a file that cannot be read. `write`'s lines end in LF on every platform | Approved (12 November 2026) |
| Diagnostic codes | WL1003 to WL1008, WL3002 to WL3004, and WL4002 (§8.4); WL2005 and WL2006 for invalid calls, and WL3001 at compile time too, for a built-in named other than to call it; WL2004 is reported only at run time, when a hoisted function reads a variable before its declaration has run (§3.5) | Approved (12 November 2026) |
