# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## M0.10 Syntax: Disposal That Throws

Asked again: A5 of "M0.10 Syntax: `toString()` and Disposal That Throws" was left as "(". Until it is answered, the disposal goes on, (a), as M0.10.6 implemented it (`WsExceptions.DisposalGoesOn`). Added by Claude, 2026-11-13.

**Q1, disposal that throws: when B's `dispose()` throws while an instance of B is disposed, (a) A's `dispose()` still runs, as section 3.4 proposes, and an exception it throws replaces B's, as A13 has it for `using`; (b) A's does not run, and B's exception goes on.**

A1: (a).

## Typed Catches

A `catch` that catches only some exceptions (A7 of "M0.10 Syntax: Static Members, `Object` and Exceptions"; specification, section 3.7), on both VMs. Its syntax, and how a `catch` names what it catches, will be proposed as questions once exceptions are implemented. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.10.6.

2026-11-13: exceptions are implemented; the questions below, one decision each, answerable with "yes" or a letter.

**Q1, syntax: (a) `catch (e: NotFound) { … }`, the class after a colon; (b) `catch (NotFound e) { … }`, the class before the name, as C# and Java write it.**

A1: (b).

**Q2, several catches: a `try` may have several, `catch (e: A) { … } catch (e: B) { … } catch (e) { … }`. An exception goes to the first whose class it is an instance of, a class extending it included; a catch without a class catches any exception, and only the last may be one; an exception that none catches goes on outward, after the `finally`. Agreed?**

A2: Yes.

**Q3, what names the class: (a) any expression that gives a class, evaluated when an exception reaches the catch (`catch (e: errors.NotFound)`); (b) only a class's name, as it is in scope there.**

A3: A middle ground: allow dotted names only (a.b.C), with no calls or arbitrary expressions. That covers the `errors.NotFound` case and still has nothing that can fail or have side effects.

**Q4, a catch's class that is not a class when an exception reaches it (`var A = 5;` … `catch (e: A)`): (a) WS3001, which replaces the exception; (b) the catch does not match, and the exception goes on.**

A4: Can we make the internals such that any value is implicitly an instance of a class, in this case `Integer`?

**Q5, runtime errors' classes: (a) every runtime error is an `Error`, told apart by its `code`, as now; (b) each kind has a class of its own, extending `Error` (`TypeError` for WS3001, `IndexError` for WS3005, `KeyError` for WS3007, and so on), so that a catch can name one kind.**

A5: (b).

**Q6, a thrown value that is not an instance (`throw "x";`): (a) only a catch without a class catches it; (b) a catch may name a kind of value too (`catch (e: String)`), which needs names for the kinds.**

A6: (b), especially if we can make every value an instance of a class.

**Q7, the same test outside a catch: `x is Point`, true when `x` is an instance of `Point` or of a class extending it. (a) Add it now, with typed catches; (b) not now.**

A7: (a).

## M0.11: Templates

`.wsp` templates, with includes, layouts and parameterized components together (specification, section 13, WS-T001 to WS-T008), in `WebLingo.Templates`, with `WebLingo.Templates.Tests`, on both VMs, and a benchmark workload for template output (section 5.6). Their open contracts (the include and component syntax, context-aware escaping) will be proposed as questions. Added by Claude, 2026-11-13.

NOTE: WebLingo must be designed in a way that lends itself to using it outside of the templated web environment, as a general purpose, embeddable scripting language. Nothing in the language itself should depend on having templates. Propose a name for the technology that uses WebLingo and templates in a web server (classic-ASP-like). Maybe WebLingoPages?

To propose with it, from the chat (2026-11-13): `<%= value %>` writes `null` as nothing, so that an optional read (`m?[k]`, `x?.name`) that finds nothing shows as blank, while `write` and `writeLn` go on writing `null`.

2026-11-13: waits for M0.10.

2026-11-13: M0.10 is done. The questions below settle what sections 2.2 and 13 leave open, one decision each, answerable with "yes" or a letter. Once they are answered, M0.11 will be split into parts in the specification, as M0.10 was, with includes, layouts and components still released together (WS-T001).

**Q1, how a `.wsp` runs: it compiles, through the same compiler and VM as a `.ws` (WS-T001), into a program that writes the page: its text as it stands, `<% … %>` as statements, `<%= … %>` and `<%- … %>` as writes of their expression, and `<%# … %>` as nothing. Agreed?**

A1: It compiles through a derivative compiler that incorporates the regular WebLingo compiler and extends it to support templating of web pages.

**Q2, statements across blocks: a statement may span several `<% %>` blocks, the text between them in its body: `<% foreach (var p in products) { %><li><%= p.name %></li><% } %>`. Agreed?**

A2: Yes, the text being rendered the same as if it were being printed, the same way classic ASP did it. This is useful for e.g. loops, where the text inside the loop may itself contain script expressions, conditionals, etc.

**Q3, where a template writes: (a) where `write` writes, standard output under the CLI and a response's body under a host later; (b) to an output of its own, which a host gives, `write` going elsewhere.**

A3: (a).

**Q4, the CLI: `weblingo run page.wsp` writes the page to standard output, and `weblingo check page.wsp` compiles it, its includes, layout and components included. Agreed?**

A4: Yes. Weblingo being strictly the language text compiler. We need a different executable for the combined WebLingo+templates compiler.

**Q5, escaping: `<%= value %>` writes the text `write` would write, an instance's `toString()` included, with `&`, `<`, `>`, `"` and `'` as `&amp;`, `&lt;`, `&gt;`, `&quot;` and `&#39;`, the same in text and in attribute values; escaping for URLs, scripts and styles comes later, as its own decision. Agreed?**

A5: Yes.

**Q6, `null`: `<%= null %>` and `<%- null %>` write nothing, so that an optional read that finds nothing is blank, while `write(null)` goes on writing `null`. Agreed?**

A6: Yes.

**Q7, line breaks: (a) the text is written exactly as it stands, every line break included; (b) a line holding only a `<% … %>` block, and spaces, writes nothing, not even its line break, so that statements leave no blank lines.**

A7: (b).

**Q8, includes: `<% include "/includes/header.wsp"; %>`, a statement whose path is a string literal, resolved and compiled with the page, as section 13 illustrates. Agreed?**

A8: Yes. 

**Q9, include paths: (a) a path starting with `/` is from the application's root, any other from the including file's folder; (b) every path is from the application's root, as section 13 proposes.**

A9: (a), with the WebLingo compiler-proper interpreting `/` as drive's root, `./` current folder, `xyz/abc` starting in current folder's `xyz` subfolder, etc.

**Q10, what an include sees: (a) the variables where it is included, as if its text stood there; (b) none of them: it has a scope of its own, and a component's parameters are the way to give it data.**

A10: (a). Also introduce `import` for the other case, so `include` is textual inclusion.

**Q11, layouts: `<% layout "/layouts/main.wsp"; %>`, at most one, before anything the page writes; the layout runs, and the page's own output comes where the layout calls `renderBody()`, as section 13 illustrates. Agreed?**

A11: At moset one for a given page, but  alayout can itself have a layout, all the way down. The compiler must prevent dependency cycles (compile-time error).

**Q12, a layout's data, such as the page's title: (a) values given with it, `<% layout "/layouts/main.wsp", { title: "Products" }; %>`, which the layout reads as its parameters, so that the page's body can run, and stream, at `renderBody()` (WS-T003); (b) the page's top-level declarations, which the layout sees (`pageTitle` in section 13's example), the page's body then running before the layout writes anything, buffered.**

A12: (a).

**Q13, components: `renderComponent("/components/card.wsp", { product: p })`, where the compiler reads the path, a string literal, compiles the component with the page, and checks its parameters: (a) agreed; (b) a built-in instead, its path any string, the component found and compiled when it runs.**

A13: (b), it's more flexible.

**Q14, a component's parameters: (a) declared at its top, `<%@ params product, size %>`, each then a variable of the component, a missing one an error, an extra one an error; (b) no declaration: the component reads `params.product`, a missing one being `null`.**

A14: (a), it's more descriptive, like declaring variables. So, also allow for default values, i.e. optional parameters.

**Q15, asynchrony: WebLingo has no `await` yet, so a component renders when it is called, and section 13's `await renderComponent(…)` is written without `await` until asynchrony is designed. Agreed?**

A15: Design async await first.

**Q16, template errors (a cycle of includes or layouts, a missing file, a path outside the application, a component's parameters): (a) a new range of codes, WS5001 on, for templates; (b) codes in the existing ranges, WS1xxx for what is wrong in a template's text, WS2xxx for the rest.**

A16: (a).

## [IDLE] M0.12: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WS-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

# END-OF-TO-DO
