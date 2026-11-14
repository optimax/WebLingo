# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] Classes of Values, Continued

Follow-ups to "Typed Catches Syntax: Classes of Values". One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

**Q1, a catch whose name is not a class (A5, "Is there a problem with that?"): no problem, but for one point: `A.equal(e)` runs the program's own `equal` when `A` is an instance whose class overrides it, while the VM is searching for a handler, so that a catch could fail or have side effects, which A3 wanted to avoid. (a) `==`, which runs no code, and differs only for such instances; (b) `A.equal(e)`, as you write, an exception it throws replacing the one being caught.**

A1:

**Q2, `Function` and `Class`: (a) like `Boolean`, `Integer`, `Number` and `String`: no class extends them, and `new` makes none; (b) classes may extend them.**

A2:

**Q3, extending `Array`, `Map` and `Set`: `class Stack extends Array { top() { return this[this.length - 1]; } }`; `new Stack()` is an empty array of class `Stack`: indexed, with `length`, `push` and `pop`, visited by `foreach`, `is Array` and `is Stack`, with its own methods. Agreed?**

A3:

**Q4, fields on such an instance: (a) allowed, as on any instance; (b) none: only methods.**

A4:

**Q5, `new Array()`, `new Map()` and `new Set()` make empty ones, as `[]`, `{}` and `#{}` do. Agreed?**

A5:

**Q6, the text of a derived array: (a) `Stack [1, 2]`, its class's name before it, as an instance's is; (b) `[1, 2]`, as any array's.**

A6:

**Q7, the classes' names: `Integer`, `String` and the rest are in scope everywhere, as `Object` and `Error` are, and a program may declare a name of its own over one. Agreed?**

A7:

## [WORKING] Typed Catches

A `catch` that catches only some exceptions (A7 of "M0.10 Syntax: Static Members, `Object` and Exceptions"; specification, section 3.7), on both VMs. Its syntax, and how a `catch` names what it catches, will be proposed as questions once exceptions are implemented. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.10.6.

2026-11-13: exceptions are implemented; the questions below, one decision each, answerable with "yes" or a letter.

Q1, syntax: (a) `catch (e: NotFound) { … }`, the class after a colon; (b) `catch (NotFound e) { … }`, the class before the name, as C# and Java write it.

A1: (b).

Q2, several catches: a `try` may have several, `catch (e: A) { … } catch (e: B) { … } catch (e) { … }`. An exception goes to the first whose class it is an instance of, a class extending it included; a catch without a class catches any exception, and only the last may be one; an exception that none catches goes on outward, after the `finally`. Agreed?

A2: Yes.

Q3, what names the class: (a) any expression that gives a class, evaluated when an exception reaches the catch (`catch (e: errors.NotFound)`); (b) only a class's name, as it is in scope there.

A3: A middle ground: allow dotted names only (a.b.C), with no calls or arbitrary expressions. That covers the `errors.NotFound` case and still has nothing that can fail or have side effects.

Q4, a catch's class that is not a class when an exception reaches it (`var A = 5;` … `catch (e: A)`): (a) WS3001, which replaces the exception; (b) the catch does not match, and the exception goes on.

A4: Can we make the internals such that any value is implicitly an instance of a class, in this case `Integer`?

Q5, runtime errors' classes: (a) every runtime error is an `Error`, told apart by its `code`, as now; (b) each kind has a class of its own, extending `Error` (`TypeError` for WS3001, `IndexError` for WS3005, `KeyError` for WS3007, and so on), so that a catch can name one kind.

A5: (b).

Q6, a thrown value that is not an instance (`throw "x";`): (a) only a catch without a class catches it; (b) a catch may name a kind of value too (`catch (e: String)`), which needs names for the kinds.

A6: (b), especially if we can make every value an instance of a class.

Q7, the same test outside a catch: `x is Point`, true when `x` is an instance of `Point` or of a class extending it. (a) Add it now, with typed catches; (b) not now.

A7: (a).

2026-11-13: recorded as approved in the specification, section 3.7 ("Typed catches", "`is`", and the runtime errors' classes): A1, A2, A3 (dotted names), A5, A6 and A7. A4 asks whether every value could be an instance of a class: it can, and "Typed Catches Syntax: Classes of Values" proposes how, with Q4 asked again and the runtime errors' class names. Waits for those answers.

2026-11-13: answered ("Typed Catches Syntax: Classes of Values"); next, but for extending `Array`, `Map` and `Set`, which waits for "Classes of Values, Continued", and the equality a catch whose name is not a class uses, `==` until then (Q1 there).


Done (Claude, 2026-11-13): `catch (KeyError e)`, several catches tried in order, the last of which may catch anything, dotted names, an exception none catches going on after the `finally`; `x is Point`; every value's class (`5 is Integer`, `catch (String e)`), `null` having none; every value's `equal`; and every runtime error an instance of its kind's class (`IndexError`, `KeyError`, …), on both VMs. One new operation, `IsInstance`. Choices of mine, in section 15.2 as proposed (row "Typed catches and classes of values"): a catch's class is evaluated when an exception reaches it; a catch-everything before the last is WS1001; the built-in classes are added to a program that names them, every runtime error's to one with a `try` or a `using`; the kinds of values' classes are sealed, `Array`, `Map` and `Set` too until "Classes That Extend Array, Map and Set"; a catch whose name is not a class compares with `==` until Q1 of "Classes of Values, Continued" is answered. Two conformance cases changed for decisions of yours: `n.equal(5)` is no longer WS3006, and a caught index error is an `IndexError`. Every new conformance case gives the same result on both VMs. Build and 1,238 tests pass on Windows; Linux not tried.

## [IDLE] Classes That Extend Array, Map and Set

A1 of "Typed Catches Syntax: Classes of Values": classes may extend `Array`, `Map` and `Set` (specification, section 3.7, "Classes of values"), on both VMs. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "Classes of Values, Continued".
## [QUESTION] Asynchrony: `async` and `await`

A15 of "M0.11: Templates": asynchrony is designed first, before templates. WebLingo's own operation model (WS-P007), worker-affine executions (WS-H012) and worker-local heaps (WS-P013) are approved; how a program writes asynchrony is not. One decision each, answerable with "yes" or a letter. Once they are answered, its implementation becomes tasks of its own, before M0.11. Added by Claude, 2026-11-13.

**Q1, syntax: `async function f() { … }`, `async` methods and `async function (…) { … }`, whose call gives a task at once; `await t` waits for task `t`, giving its result, or throwing its exception. Agreed?**

A1: Yes.

**Q2, where `await` may stand: (a) in an `async` function, and at the top level of a script or a template; (b) only in an `async` function.**

A2: (a). The top level of a script/module is its executable block. A script/module becomes an instance of `Module`, which has an `exec()` function. Would that work? 

**Q3, when an `async` function starts: (a) at once, when it is called, running until its first `await`, as in C# and JavaScript; (b) only when something awaits its task.**

A3: (a)

**Q4, concurrency: an execution's tasks run on its one worker, one at a time, switching only at `await`, so that no two run at once and nothing needs a lock (WS-H012, WS-P013). Agreed?**

A4: How about a real thread pool?

**Q5, what `await` takes: (a) only a task, anything else being WS3001; (b) any value, a value that is not a task giving itself.**

A5: (b) and awaiting a plain value still yields.

**Q6, a task is a value of its own kind, of the built-in class `Task` (section 3.2), with nothing but `await` for now; `Task.all`, `Task.any` and timeouts come when they are needed. Agreed?**

A6: OK. Add a TO-DO.

**Q7, a task's exception that nothing awaits: (a) it is lost; (b) when the execution ends, the first such exception stops it, as an uncaught one does, so that none goes unnoticed.**

A7: 

**Q8, where tasks come from, besides `async` functions: the host's operations (files, network, database: WS-P004, WS-P009, WS-P011) give tasks, and the standard library has `delay(milliseconds)`, for tests. Agreed?**

A8:

**Q9, the end of an execution: (a) it waits for every task still running; (b) it ends when the top level does, the tasks still running being cancelled.**

A9:

**Q10, how the VMs do it: a waiting `async` function's frames are kept on the heap, held by its task, and resumed by the worker's scheduler when what it waits for completes; no thread blocks (WS-P007, WS-P008). Agreed?**

A10:

**Q11, cancellation: an execution's cancellation (WS3004) ends its waiting tasks too, uncatchable, as now. Agreed?**

A11:

**Q12, `using` and asynchrony: (a) `dispose()` stays synchronous for now, as section 3.4 begins; (b) a `dispose()` may be `async`, and `using` awaits it.**

A12:

**Q13, templates: a template's body may `await` anywhere, as an `async` function's may, and `renderComponent(…)` gives a task, which the page awaits, as section 13's example writes. Agreed?**

A13:

## [QUESTION] M0.11 Syntax: Templates, Continued

Follow-ups to "M0.11: Templates": what its answers leave open. One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-13.

**Q1, the technology's name, WebLingo with templates in a web server: (a) WebLingo Pages, as you suggest; (b) WebLingo Server Pages; (c) another, which you give.**

A1:

**Q2, its command-line program, beside `weblingo`: (a) `wlpages`; (b) `weblingo-pages`; (c) another, which you give. Its project would be `WebLingo.Pages.Cli`, beside `WebLingo.Cli`.**

A2:

**Q3, `import`: (a) the language's module system, for `.ws` files and templates alike (section 2.3 plans modules), designed as a task of its own, since a general-purpose language needs one; (b) for templates only: `<% import "/x.wsp"; %>`, a template run in a scope of its own.**

A3:

**Q4, default values in a component's parameters: `<%@ params product, size = 10 %>`, a default being any expression, evaluated when the parameter is missing. Agreed?**

A4:

**Q5, functions too: `function f(a, b = 1) { … }`, by the same rule, so that functions and components agree: (a) yes; (b) components only.**

A5:

**Q6, a layout's parameters: a layout declares them as a component does, `<%@ params title %>`, and is given them by the page's `layout` statement, a missing or an extra one being an error. Agreed?**

A6:

**Q7, components found at run time (A13): each is compiled on first use and kept, recompiled when its file changes, in the application's generation (WS-T004, WS-T007); a missing one is a runtime error, WS5xxx, which a catch may catch. Agreed?**

A7:

## [IDLE] M0.11: Templates

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

## [IDLE] M0.12: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WS-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

# END-OF-TO-DO
