# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] Classes of Values, Extensions, Continued

Follow-ups to "Classes of Values, Extensions". One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

**Q1, the class of an arithmetic result (A6): (a) its left operand's class, if it has one, else its right's: `new Celsius(21.5) + 1` and `1 + new Celsius(21.5)` are both a `Celsius`, and `celsius + fahrenheit` a `Celsius`; (b) a class only when its classed operands all have the same one, a plain value otherwise.**

A1:

**Q2, how arithmetic makes that result: (a) a new instance of the class, holding the result, its constructor not run, with no fields; (b) the same, with the fields of the operand whose class it is, copied.**

A2:

**Q3, operators (A6's remark: defining `+` is what gives a class full control of it): (a) a class may declare what an operator does with its values, as C# does, by a static method, `static operator +(a, b) { … }`, which `+` calls when either operand's class declares it, the left's first, and likewise for `-`, `*`, `/` and the comparisons, Q1's default applying where none does; (b) later, as a design of its own, Q1's default holding until then.**

A3:

**Q4, reading A1's text back: `Celsius(21.5)` and `Stack([1, 2])` agree with a literal representation only if calling a class makes an instance: (a) calling a class makes one, as `new` does, so that `Celsius(21.5)` in a program is `new Celsius(21.5)` (it is WS2005 now); (b) they stay a text only, and calling a class stays WS2005.**

A4:

**Q5, `new Class()` (A9) needs what "Adding Methods at Run Time" describes, a function with a receiver and a way to attach it to a class, and that task is deferred: (a) design them now, with `new Class()` and `extends Class`, as a task of their own after "Extending the Classes of Values"; (b) they wait with that task, and `new Class()` and `extends Class` stay WS3001 until then.**

A5:

## [IDLE] Extending the Classes of Values

`extend` blocks, and classes extending `Boolean`, `Integer`, `Number`, `String`, `Function` and `Class`, with `new` on those classes ("Classes of Values, Further"; specification, section 3.7), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "Classes of Values, Extensions".

2026-11-13: answered; `extend`, classes extending `Boolean`, `Integer`, `Number`, `String` and `Function`, and their text (`Celsius(21.5)`, `Stack([1, 2])`) can be built; the class of an arithmetic result, and `Class`, wait for "Classes of Values, Extensions, Continued".

## [IDLE] Task Combinators

`Task.all`, `Task.any` and timeouts (A6 of "Asynchrony: `async` and `await`"; specification, section 3.8). Added by Claude, 2026-11-13.

2026-11-13: waits for "M0.11: Asynchrony".

## [QUESTION] Modules, Continued

Follow-ups to "Modules: `import` and `param`" (specification, section 3.9). One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

**Q1, `Module.import(path)`: a module's top level is `async` (`exec()`), so it gives a task, which completes with the instance once the module has run, `var db = await Module.import("lib/db.ws");`. Agreed?**

A1:

**Q2, when a static import's module runs: (a) before the importing module's top level, in the order of its imports, each awaited, so that the importer begins with its imports run, as JavaScript's do; (b) where its `import` stands, in order with the statements around it.**

A2:

**Q3, a module's lifetime under a host: A2 runs a module once per execution, and section 2.3 once per application generation: (a) once per execution, a request's, its instance the request's own; (b) once per application generation, its instance shared by the requests, which then cannot change it, since a heap is one worker's (WS-P013).**

A3:

**Q4, `export` marks a `function`, `class`, `var` or `const` declaration at a module's top level, and an importer reads an exported `var` as it is when read (`db.count`); assigning it: (a) WS3001, an export being the module's to change; (b) allowed, as a member is assigned.**

A4:

**Q5, importing names: (a) only the instance, `import util from "lib/util.ws";`, then `util.f()`; (b) names too, `import { f, g } from "lib/util.ws";`, as JavaScript does.**

A5:

**Q6, a quoted parameter (A8): a shell removes the quotes of `value="10"`, so `weblingo` gets `value=10`, an integer: (a) quotes that reach it make a string, `value='"10"'` in a shell; (b) `weblingo run script.ws --string value=10` makes a string, whatever the value looks like.**

A6:

**Q7, the standard modules' names: (a) `system` and `std/fs`, as A10 and Q10 name them; (b) every one under `std/`, `std/system` among them.**

A7:

## [IDLE] Modules

The module system: `import`, static and dynamic, `export`, `param`, a script's parameters, and the standard library and the built-ins as modules (specification, section 3.9), on both VMs, before M0.12's templates, which build on it. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "Modules, Continued", and for "Extending the Classes of Values", above it.

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

2026-11-13: M0.13 now, after M0.12, templates (specification, section 9); waits for M0.12.

## Static Members Callable on Instances

Let's make sure that class static members are accessible through instance references (unless shadowed by the instance member) as well as class references. 

## [DEFER] Adding Methods at Run Time

`extend` (see "Classes of Values, Extensions") adds methods to a class as a declaration, written once in the source, its methods having `this` as a class's do. This task would add a way to do the same while the program runs, for a method, or a class to give it to, that is known only then: chosen by a condition, built by a factory, or named by data. Discussed with the owner, 2026-11-13; added by Claude the same day.

What it needs:

- **A function with a receiver.** A method is a function whose first, hidden parameter is `this` (specification, section 15.2, "Classes"), and a plain function expression has none (`this` in one is WS2009, unless a method around it lends its own). One way: a function expression may declare `this` as its first parameter, `function (this, prefix) { return prefix + this.x; }`, the parameter bound by a call's target (`p.describe("x = ")`), or given as an argument when the function is called directly (`describe(p, "x = ")`).
- **A way to attach it.** A built-in method of classes, `Point.addMethod("describe", describe)`, after which every instance of `Point`, and of its subclasses, has `describe`, with `this` bound, replacing a method of that name as an extension's does (the owner chose replacing for `extend`, 2026-11-13).

Its cost: a class's members could change at any point of a run, so the same call could mean different things at different times, and the VMs could no longer gather a class's methods once, when it is made.

It is deferred: it can be added later, if it proves necessary, since `extend` already covers methods known when the program is written, and nothing in this design would change what programs without it do. When it is taken up, these are the owner's to decide:

**Q1: a function with a receiver, `function (this, a) { … }`, its first parameter named `this`, bound by a call's target, or given as the first argument of a direct call. Agreed?**

A1: 

**Q2: attaching it: (a) a built-in method of every class, `Point.addMethod("describe", f)`; (b) a statement, `extend Point with describe = f;`.**

A2: 

**Q3: may a method attached at run time be removed again (`Point.removeMethod("describe")`)? (a) no; (b) yes.**

A3: 

# END-OF-TO-DO
