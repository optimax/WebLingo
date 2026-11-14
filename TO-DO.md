# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [DONE] Classes of Values, Continued

Follow-ups to "Typed Catches Syntax: Classes of Values". One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

NOTE: It should be possible to extend a class via `extends` as expected, but also via assigning a method or a property of a class directly, which is the same as declaring those as `static`.

Q1, a catch whose name is not a class (A5, "Is there a problem with that?"): no problem, but for one point: `A.equal(e)` runs the program's own `equal` when `A` is an instance whose class overrides it, while the VM is searching for a handler, so that a catch could fail or have side effects, which A3 wanted to avoid. (a) `==`, which runs no code, and differs only for such instances; (b) `A.equal(e)`, as you write, an exception it throws replacing the one being caught.

A1: (b)

Q2, `Function` and `Class`: (a) like `Boolean`, `Integer`, `Number` and `String`: no class extends them, and `new` makes none; (b) classes may extend them.

A2: I am taking back whatever I said that makes you think "`Boolean`, `Integer`, `Number` and `String`: no class extends them". They should essentially be like any other class and it should be possible to extend them with new methods, for example.

Q3, extending `Array`, `Map` and `Set`: `class Stack extends Array { top() { return this[this.length - 1]; } }`; `new Stack()` is an empty array of class `Stack`: indexed, with `length`, `push` and `pop`, visited by `foreach`, `is Array` and `is Stack`, with its own methods. Agreed?

A3: Sure.

Q4, fields on such an instance: (a) allowed, as on any instance; (b) none: only methods.

A4: (a)

Q5, `new Array()`, `new Map()` and `new Set()` make empty ones, as `[]`, `{}` and `#{}` do. Agreed?

A5: Yes.

Q6, the text of a derived array: (a) `Stack [1, 2]`, its class's name before it, as an instance's is; (b) `[1, 2]`, as any array's.

A6: (a). The text of an instance must agree with its literal representation, if available.

Q7, the classes' names: `Integer`, `String` and the rest are in scope everywhere, as `Object` and `Error` are, and a program may declare a name of its own over one. Agreed?

A7: Sure.

Done (Claude, 2026-11-13): recorded as approved in the specification, section 3.7 ("Classes of values", "Typed catches" and "Static members", the NOTE among them). What A2 leaves open, what extending `Boolean`, `Integer`, `Number`, `String`, `Function` and `Class` means, is asked in "Classes of Values, Further".

## [QUESTION] Classes of Values, Further

A2 of "Classes of Values, Continued": `Boolean`, `Integer`, `Number`, `String`, `Function` and `Class` are to be like any other class, and extendable, with new methods for example. What that means needs deciding. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

**Q1, new methods for a kind's values: (a) a block that adds methods to an existing class, `extend Integer { isEven() { return this % 2 == 0; } }`, after which `4.isEven()`, as C#'s extension methods and Swift's extensions do; (b) only subclasses, `class Even extends Integer { … }`; (c) both.**

A1:

**Q2, a subclass of `Integer`, `Number`, `String` or `Boolean`, if there are any: `new Celsius(21.5)` holds a number, which arithmetic, comparisons and `==` use, `is Number` and `is Celsius`, with its own methods and fields, written `Celsius 21.5`. Agreed?**

A2:

**Q3, `new` on those classes: (a) `new Integer()` is `0`, `new String()` `""`, `new Boolean()` `false`, and `new Integer(5)` `5`; (b) WS3001, their values being made by literals.**

A3:

**Q4, `Function` and `Class`: (a) as (1) to (3) say for the others, a subclass of `Function` being a function with methods of its own; (b) new methods only, as (1) (a), with no subclasses.**

A4:

## [IDLE] Classes That Extend Array, Map and Set

A1 of "Typed Catches Syntax: Classes of Values": classes may extend `Array`, `Map` and `Set` (specification, section 3.7, "Classes of values"), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "Classes of Values, Continued".

2026-11-13: answered; next, with A1's `A.equal(e)` for a catch whose name is not a class, and the NOTE's static members by assignment.

## [DONE] Asynchrony, Continued

Follow-ups to "Asynchrony: `async` and `await`". One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, concurrency (A4, "How about a real thread pool?"): the approved hosting design runs each execution on one worker (WS-H012), with a heap of its own that no other thread touches (WS-P013), while many executions, a server's requests, run at once on a pool of workers, and blocking I/O runs on a bounded pool of its own (section 6.10), so a server already uses its cores. Tasks of one execution running on several threads at once would share its heap, which needs locks around every object, or a collector that runs beside the program, and makes every program open to data races. (a) Keep one worker per execution, its tasks taking turns at `await`, and add parallel work later as workers with heaps of their own that exchange copied messages (WS-P013's explicit transfer), as JavaScript's workers do; (b) a thread pool for one execution's tasks, their heap shared under locks, changing WS-H012 and WS-P013.

A1: (a)

Q2, scripts as modules (A2, "Would that work?"): it would. A script's or a module's top level becomes the body of an `async` method, `exec()`, of an instance of a built-in class `Module`, which the host calls and awaits; the module's exports would be its instance's members. It belongs with `import` ("M0.11 Syntax: Templates, Continued", Q3). (a) Adopt it now, as what a script is; (b) decide it with `import`, when the module system is designed.

A2: (a)

Q3, forgetting a task (A7's opting out): (a) `t.forget()`, a method of the task, after which nothing reports its exception; (b) a statement, `forget t;`; (c) a function, `Task.forget(t)`.

A3: (a).

Q4, an `async` `dispose()` in a function that is not `async` (A12), where `using` cannot wait for it: (a) WS3001, at the end of the `using`; (b) the disposal starts, and is not waited for, as if forgotten.

A4: (a)

Q5, its milestone: asynchrony comes before templates, so (a) it is M0.11, templates becoming M0.12 and WBC M0.13; (b) it is a part of M0.10, M0.10.8; (c) another, which you give.

A5: (a)

Done (Claude, 2026-11-13): recorded as approved in the specification, section 3.8, its decision register, and section 9, where asynchrony is M0.11, templates M0.12 and WBC M0.13.

## [IDLE] Default Parameter Values

`function f(a, b = 1) { … }`, for functions, methods and constructors (A5 of "M0.11 Syntax: Templates, Continued"; specification, section 3.5), on both VMs. Added by Claude, 2026-11-13.

## [IDLE] M0.11: Asynchrony

`async` functions and methods, `await`, tasks, and asynchronous disposal (specification, section 3.8), on both VMs, before M0.12's templates, which use them. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "Asynchrony, Continued", whose first question, concurrency, shapes it.

2026-11-13: answered; it is M0.11 (specification, section 9), after the tasks above it.

## [IDLE] Task Combinators

`Task.all`, `Task.any` and timeouts (A6 of "Asynchrony: `async` and `await`"; specification, section 3.8). Added by Claude, 2026-11-13.

2026-11-13: waits for "M0.11: Asynchrony".

## [DONE] M0.11 Syntax: Templates, Continued

Follow-ups to "M0.11: Templates": what its answers leave open. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

Q1, the technology's name, WebLingo with templates in a web server: (a) WebLingo Pages, as you suggest; (b) WebLingo Server Pages; (c) another, which you give.

A1: (a)

Q2, its command-line program, beside `weblingo`: (a) `wlpages`; (b) `weblingo-pages`; (c) another, which you give. Its project would be `WebLingo.Pages.Cli`, beside `WebLingo.Cli`.

A2: `welp`.

Q3, `import`: (a) the language's module system, for `.ws` files and templates alike (section 2.3 plans modules), designed as a task of its own, since a general-purpose language needs one; (b) for templates only: `<% import "/x.wsp"; %>`, a template run in a scope of its own.

A3: (a)

Q4, default values in a component's parameters: `<%@ params product, size = 10 %>`, a default being any expression, evaluated when the parameter is missing. Agreed?

A4: Yes. It should be possible to specify each parameter separately, like var declarations, so \

```
<%
  param product;
  param size = 10;
%>
```

Why couldn't we have `param` as a regular keyword in the language (like `var`), specifying a parameter to a module?

Q5, functions too: `function f(a, b = 1) { … }`, by the same rule, so that functions and components agree: (a) yes; (b) components only.

A5:  Yes, functions too.

Q6, a layout's parameters: a layout declares them as a component does, `<%@ params title %>`, and is given them by the page's `layout` statement, a missing or an extra one being an error. Agreed?

A6: Yes, but as in A4.

Q7, components found at run time (A13): each is compiled on first use and kept, recompiled when its file changes, in the application's generation (WS-T004, WS-T007); a missing one is a runtime error, WS5xxx, which a catch may catch. Agreed?

A7: Yes.

Done (Claude, 2026-11-13): recorded as approved in the specification, section 13 ("Contracts") and section 3.5 (functions' default values). A4's question, `param` as a keyword of the language, is asked with the module system in "Modules: `import` and `param`"; functions' default values are the task "Default Parameter Values".

## [QUESTION] Modules: `import` and `param`

The language's module system, which A3 of "M0.11 Syntax: Templates, Continued" makes `import`, and A4's `param` as a keyword of the language, a module's parameter; A2 of "Asynchrony, Continued" makes a script, or a module, an instance of `Module`. Templates (M0.12) need both. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

**Q1, importing: (a) `import db from "lib/db.ws";`, a statement at a module's top level, naming the module's instance; (b) `var db = import("lib/db.ws");`, an expression, anywhere.**

A1:

**Q2, when a module runs: once per execution, the first time it is imported; every later import gets the same instance, already run. Agreed?**

A2:

**Q3, exports: (a) every top-level declaration of a module is a member of its instance; (b) only those marked `export` (`export function f() { … }`).**

A3:

**Q4, paths: as A9 of "M0.11: Templates" has it for the language: from the importing file's folder, `./` that folder, `/` the drive's root; and `.ws` may be left out. Agreed?**

A4:

**Q5, cycles: a module that imports itself, directly or through others: (a) a compile-time error; (b) allowed, the second import getting the instance as it is so far, as JavaScript does.**

A5:

**Q6, `param` in the language: `param name;` and `param size = 10;` declare a module's parameters, at its top level, before its other statements, each a `const` of it; an importer gives them, `import db from "lib/db.ws" { size: 10 };`, and a template's caller gives a component's or a layout's. Agreed?**

A6:

**Q7, a parameter without a default that is not given: (a) an error, where the module is imported, of a new code; (b) `null`.**

A7:

**Q8, a script's parameters from the command line: `weblingo run script.ws size=10 name=Ann`, each value read as a WebLingo literal if it is one (`10`, `true`), as a string otherwise. Agreed?**

A8:

**Q9, when imports are compiled: (a) with the importing program, so that a program is complete before it runs; (b) when they run, as `renderComponent` finds its component.**

A9:

**Q10, the standard library: it becomes modules imported by name (`import fs from "std/fs";`), while `write`, `writeLn` and the like stay built-ins of every program. Agreed?**

A10:

## [IDLE] M0.12: Templates

`.wsp` templates, with includes, layouts and parameterized components together (specification, section 13, WS-T001 to WS-T008), in `WebLingo.Templates`, with `WebLingo.Templates.Tests`, on both VMs, and a benchmark workload for template output (section 5.6). Their open contracts (the include and component syntax, context-aware escaping) will be proposed as questions. Added by Claude, 2026-11-13.

NOTE: WebLingo must be designed in a way that lends itself to using it outside of the templated web environment, as a general purpose, embeddable scripting language. Nothing in the language itself should depend on having templates. Propose a name for the technology that uses WebLingo and templates in a web server (classic-ASP-like). Maybe WebLingoPages?

To propose with it, from the chat (2026-11-13): `<%= value %>` writes `null` as nothing, so that an optional read (`m?[k]`, `x?.name`) that finds nothing shows as blank, while `write` and `writeLn` go on writing `null`.

2026-11-13: waits for M0.10.

2026-11-13: M0.10 is done. The questions below settle what sections 2.2 and 13 leave open, one decision each, answerable with "yes" or a letter. Once they are answered, M0.11 will be split into parts in the specification, as M0.10 was, with includes, layouts and components still released together (WS-T001).

Q1, how a `.wsp` runs: it compiles, through the same compiler and VM as a `.ws` (WS-T001), into a program that writes the page: its text as it stands, `<% … %>` as statements, `<%= … %>` and `<%- … %>` as writes of their expression, and `<%# … %>` as nothing. Agreed?

A1: It compiles through a derivative compiler that incorporates the regular WebLingo compiler and extends it to support templating of web pages.

Q2, statements across blocks: a statement may span several `<% %>` blocks, the text between them in its body: `<% foreach (var p in products) { %><li><%= p.name %></li><% } %>`. Agreed?

A2: Yes, the text being rendered the same as if it were being printed, the same way classic ASP did it. This is useful for e.g. loops, where the text inside the loop may itself contain script expressions, conditionals, etc.

Q3, where a template writes: (a) where `write` writes, standard output under the CLI and a response's body under a host later; (b) to an output of its own, which a host gives, `write` going elsewhere.

A3: (a).

Q4, the CLI: `weblingo run page.wsp` writes the page to standard output, and `weblingo check page.wsp` compiles it, its includes, layout and components included. Agreed?

A4: Yes. Weblingo being strictly the language text compiler. We need a different executable for the combined WebLingo+templates compiler.

Q5, escaping: `<%= value %>` writes the text `write` would write, an instance's `toString()` included, with `&`, `<`, `>`, `"` and `'` as `&amp;`, `&lt;`, `&gt;`, `&quot;` and `&#39;`, the same in text and in attribute values; escaping for URLs, scripts and styles comes later, as its own decision. Agreed?

A5: Yes.

Q6, `null`: `<%= null %>` and `<%- null %>` write nothing, so that an optional read that finds nothing is blank, while `write(null)` goes on writing `null`. Agreed?

A6: Yes.

Q7, line breaks: (a) the text is written exactly as it stands, every line break included; (b) a line holding only a `<% … %>` block, and spaces, writes nothing, not even its line break, so that statements leave no blank lines.

A7: (b).

Q8, includes: `<% include "/includes/header.wsp"; %>`, a statement whose path is a string literal, resolved and compiled with the page, as section 13 illustrates. Agreed?

A8: Yes. 

Q9, include paths: (a) a path starting with `/` is from the application's root, any other from the including file's folder; (b) every path is from the application's root, as section 13 proposes.

A9: (a), with the WebLingo compiler-proper interpreting `/` as drive's root, `./` current folder, `xyz/abc` starting in current folder's `xyz` subfolder, etc.

Q10, what an include sees: (a) the variables where it is included, as if its text stood there; (b) none of them: it has a scope of its own, and a component's parameters are the way to give it data.

A10: (a). Also introduce `import` for the other case, so `include` is textual inclusion.

Q11, layouts: `<% layout "/layouts/main.wsp"; %>`, at most one, before anything the page writes; the layout runs, and the page's own output comes where the layout calls `renderBody()`, as section 13 illustrates. Agreed?

A11: At moset one for a given page, but  alayout can itself have a layout, all the way down. The compiler must prevent dependency cycles (compile-time error).

Q12, a layout's data, such as the page's title: (a) values given with it, `<% layout "/layouts/main.wsp", { title: "Products" }; %>`, which the layout reads as its parameters, so that the page's body can run, and stream, at `renderBody()` (WS-T003); (b) the page's top-level declarations, which the layout sees (`pageTitle` in section 13's example), the page's body then running before the layout writes anything, buffered.

A12: (a).

Q13, components: `renderComponent("/components/card.wsp", { product: p })`, where the compiler reads the path, a string literal, compiles the component with the page, and checks its parameters: (a) agreed; (b) a built-in instead, its path any string, the component found and compiled when it runs.

A13: (b), it's more flexible.

Q14, a component's parameters: (a) declared at its top, `<%@ params product, size %>`, each then a variable of the component, a missing one an error, an extra one an error; (b) no declaration: the component reads `params.product`, a missing one being `null`.

A14: (a), it's more descriptive, like declaring variables. So, also allow for default values, i.e. optional parameters.

Q15, asynchrony: WebLingo has no `await` yet, so a component renders when it is called, and section 13's `await renderComponent(…)` is written without `await` until asynchrony is designed. Agreed?

A15: Design async await first.

Q16, template errors (a cycle of includes or layouts, a missing file, a path outside the application, a component's parameters): (a) a new range of codes, WS5001 on, for templates; (b) codes in the existing ranges, WS1xxx for what is wrong in a template's text, WS2xxx for the rest.

A16: (a).

2026-11-13: recorded as approved in the specification, section 13 ("Contracts"), its decision register and section 9. What the answers leave open (the technology's and its program's names, `import`, default values, layouts' parameters, components found at run time) is asked in "M0.11 Syntax: Templates, Continued"; asynchrony, which A15 asks to come first, in "Asynchrony: `async` and `await`". Waits for those.

## [IDLE] M0.13: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WS-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

# END-OF-TO-DO
