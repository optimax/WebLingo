# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] Standard Module `std:tasks`

`Task.all`, `Task.any` and `Task.timeout` as the exports of a standard module, `std:tasks` (A4 of "Task Combinators"; specification, sections 3.8 and 3.9), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for the design of how standard modules are packaged, which A5 of "Native Code Extensions" makes native extensions, installed as other native modules are.

## [IDLE] M0.12: Templates

`.wlp` templates, with includes, layouts and parameterized components together (specification, section 13, WL-T001 to WL-T008), in `WebLingo.Templates`, with `WebLingo.Templates.Tests`, on both VMs, and a benchmark workload for template output (section 5.6). Their open contracts (the include and component syntax, context-aware escaping) will be proposed as questions. Added by Claude, 2026-11-13.

NOTE: WebLingo must be designed in a way that lends itself to using it outside of the templated web environment, as a general purpose, embeddable scripting language. Nothing in the language itself should depend on having templates. Propose a name for the technology that uses WebLingo and templates in a web server (classic-ASP-like). Maybe WebLingoPages?

To propose with it, from the chat (2026-11-13): `<%= value %>` writes `null` as nothing, so that an optional read (`m?[k]`, `x?.name`) that finds nothing shows as blank, while `write` and `writeLn` go on writing `null`.

2026-11-13: waits for M0.10.

2026-11-13: M0.10 is done. The questions below settle what sections 2.2 and 13 leave open, one decision each, answerable with "yes" or a letter. Once they are answered, M0.11 will be split into parts in the specification, as M0.10 was, with includes, layouts and components still released together (WL-T001).

Q1, how a `.wlp` runs: it compiles, through the same compiler and VM as a `.wl` (WL-T001), into a program that writes the page: its text as it stands, `<% … %>` as statements, `<%= … %>` and `<%- … %>` as writes of their expression, and `<%# … %>` as nothing. Agreed?

A1: It compiles through a derivative compiler that incorporates the regular WebLingo compiler and extends it to support templating of web pages.

Q2, statements across blocks: a statement may span several `<% %>` blocks, the text between them in its body: `<% foreach (var p in products) { %><li><%= p.name %></li><% } %>`. Agreed?

A2: Yes, the text being rendered the same as if it were being printed, the same way classic ASP did it. This is useful for e.g. loops, where the text inside the loop may itself contain script expressions, conditionals, etc.

Q3, where a template writes: (a) where `write` writes, standard output under the CLI and a response's body under a host later; (b) to an output of its own, which a host gives, `write` going elsewhere.

A3: (a).

Q4, the CLI: `weblingo run page.wlp` writes the page to standard output, and `weblingo check page.wlp` compiles it, its includes, layout and components included. Agreed?

A4: Yes. Weblingo being strictly the language text compiler. We need a different executable for the combined WebLingo+templates compiler.

Q5, escaping: `<%= value %>` writes the text `write` would write, an instance's `toString()` included, with `&`, `<`, `>`, `"` and `'` as `&amp;`, `&lt;`, `&gt;`, `&quot;` and `&#39;`, the same in text and in attribute values; escaping for URLs, scripts and styles comes later, as its own decision. Agreed?

A5: Yes.

Q6, `null`: `<%= null %>` and `<%- null %>` write nothing, so that an optional read that finds nothing is blank, while `write(null)` goes on writing `null`. Agreed?

A6: Yes.

Q7, line breaks: (a) the text is written exactly as it stands, every line break included; (b) a line holding only a `<% … %>` block, and spaces, writes nothing, not even its line break, so that statements leave no blank lines.

A7: (b).

Q8, includes: `<% include "/includes/header.wlp"; %>`, a statement whose path is a string literal, resolved and compiled with the page, as section 13 illustrates. Agreed?

A8: Yes. 

Q9, include paths: (a) a path starting with `/` is from the application's root, any other from the including file's folder; (b) every path is from the application's root, as section 13 proposes.

A9: (a), with the WebLingo compiler-proper interpreting `/` as drive's root, `./` current folder, `xyz/abc` starting in current folder's `xyz` subfolder, etc.

Q10, what an include sees: (a) the variables where it is included, as if its text stood there; (b) none of them: it has a scope of its own, and a component's parameters are the way to give it data.

A10: (a). Also introduce `import` for the other case, so `include` is textual inclusion.

Q11, layouts: `<% layout "/layouts/main.wlp"; %>`, at most one, before anything the page writes; the layout runs, and the page's own output comes where the layout calls `renderBody()`, as section 13 illustrates. Agreed?

A11: At moset one for a given page, but  alayout can itself have a layout, all the way down. The compiler must prevent dependency cycles (compile-time error).

Q12, a layout's data, such as the page's title: (a) values given with it, `<% layout "/layouts/main.wlp", { title: "Products" }; %>`, which the layout reads as its parameters, so that the page's body can run, and stream, at `renderBody()` (WL-T003); (b) the page's top-level declarations, which the layout sees (`pageTitle` in section 13's example), the page's body then running before the layout writes anything, buffered.

A12: (a).

Q13, components: `renderComponent("/components/card.wlp", { product: p })`, where the compiler reads the path, a string literal, compiles the component with the page, and checks its parameters: (a) agreed; (b) a built-in instead, its path any string, the component found and compiled when it runs.

A13: (b), it's more flexible.

Q14, a component's parameters: (a) declared at its top, `<%@ params product, size %>`, each then a variable of the component, a missing one an error, an extra one an error; (b) no declaration: the component reads `params.product`, a missing one being `null`.

A14: (a), it's more descriptive, like declaring variables. So, also allow for default values, i.e. optional parameters.

Q15, asynchrony: WebLingo has no `await` yet, so a component renders when it is called, and section 13's `await renderComponent(…)` is written without `await` until asynchrony is designed. Agreed?

A15: Design async await first.

Q16, template errors (a cycle of includes or layouts, a missing file, a path outside the application, a component's parameters): (a) a new range of codes, WL5001 on, for templates; (b) codes in the existing ranges, WL1xxx for what is wrong in a template's text, WL2xxx for the rest.

A16: (a).

2026-11-13: recorded as approved in the specification, section 13 ("Contracts"), its decision register and section 9. What the answers leave open (the technology's and its program's names, `import`, default values, layouts' parameters, components found at run time) is asked in "M0.11 Syntax: Templates, Continued"; asynchrony, which A15 asks to come first, in "Asynchrony: `async` and `await`". Waits for those.

2026-11-14: those are done, and the module system it needs too; split into the parts the specification defines in section 9 ("M0.12 parts"), each a task below. Waits for them.

## [QUESTION] M0.12.1: Pages

`.wlp` pages: their text, `<% … %>`, `<%= … %>`, `<%- … %>` and `<%# … %>`, statements across blocks, escaping, `null` and line breaks, compiled through the WebLingo compiler into a program that writes the page, in `WebLingo.Templates`, with `WebLingo.Templates.Tests`, and `welp run page.wlp` in `WebLingo.Pages.Cli` (specification, section 13, "Contracts", and section 9, M0.12.1), on both VMs. Added by Claude, 2026-11-14.

Its syntax needs one decision first, which section 13 leaves open; answerable with a letter.

**Q1, `%>` inside a block's code, in a string or a comment (`<% var end = "%>"; %>`): (a) it ends the block wherever it stands, as in classic ASP, a string writing it as `"%" + ">"`; (b) the template compiler reads a block's code as WebLingo's tokens, so that `%>` in a string or a comment does not end it.**

A1:

## [QUESTION] M0.12.2: Includes

`<% include "/includes/header.wlp"; %>`, textual, resolved and compiled with the page, a cycle of them an error of the templates' range, WL5001 on (specification, section 13, "Contracts", and section 9, M0.12.2), on both VMs. Added by Claude, 2026-11-14.

Section 13 has a path starting with `/` be "from the application's root", which a host gives; `welp` needs a decision of its own, answerable with a letter.

**Q1, the application's root under `welp`: (a) the folder of the page it is given; (b) the current folder; (c) an option, `welp run --root site page.wlp`, the page's folder when it is left out.**

A1:

## [IDLE] M0.12.3: Components

`<% param product; %>` in a component, `renderComponent(path, values)`, a task, the component found and compiled when it runs, as `Module.import(…)` is, and a missing one's error (specification, section 13, "Contracts", and section 9, M0.12.3), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "M0.12.1: Pages".

## [IDLE] M0.12.4: Layouts

`<% layout "/layouts/main.wlp", { title: "Products" }; %>`, `renderBody()`, a layout's own layout, and a cycle of them (specification, section 13, "Contracts", and section 9, M0.12.4), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for "M0.12.2: Includes", whose paths it shares.

## [IDLE] M0.12.5: Template Output's Workload

A benchmark workload of template output (specification, sections 5.6 and 9, M0.12.5), the third of those "Both VMs, for now" waits for. Added by Claude, 2026-11-14.

2026-11-14: waits for "M0.12.1: Pages".

## [IDLE] M0.13: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WL-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

2026-11-13: M0.13 now, after M0.12, templates (specification, section 9); waits for M0.12.

## [QUESTION] Native Code Extensions, Continued

The details A1 of "Native Code Extensions" asks to discuss, and one point between A2 and A4 (specification, section 3.9, "Native modules"). One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-14.

**Q1, what a native module gives: an assembly with one public class implementing `IWebLingoNativeModule`, whose `Register(IWebLingoNativeRegistry registry)` gives its exports, each a function with a name, an arity, and a delegate that takes the call's arguments, as `WebLingoValue`s, with the execution's heap, and gives a value, or a task for asynchronous work, as the host's built-ins (`IWebLingoBuiltinFunction`) do now. Agreed?**

A1:

**Q2, a native module's classes: (a) none at first, functions only, classes coming later; (b) classes from the start, each declared by the module with its methods as native functions (`registry.Class("Connection", …)`).**

A2:

**Q3, loading one on .NET (adapter-only, section 7.14): (a) each in an `AssemblyLoadContext` of its own, so that two modules may depend on different versions of one library, and a reload can unload it with its application's generation (WL-H004); (b) into the host's own context, simpler, and never unloaded.**

A3:

**Q4, a scheme's folder (A2, A3): (a) the module's assembly in it, `native/sqlite.dll`, its dependencies beside it; (b) a folder of its own for each module, `native/sqlite/sqlite.dll`, its dependencies inside it.**

A4:

**Q5, A2's explicit paths and A4's concern, a script that writes an assembly and imports it: (a) a native module loads only from a configured scheme's folder, an explicit path too, which scripts cannot write to (section 7.13's capabilities); (b) explicit paths anywhere, the host's filesystem capability alone keeping scripts from writing assemblies where they can be loaded from.**

A5:

**Q6, the standard modules as native ones (A5): (a) now: `std:system`'s built-ins moved into a native module installed with the runtime, in the `std` scheme's folder; (b) with the first standard module that is not the host's built-ins (`std:fs`, `std:tasks`).**

A6:

## Constants Inside Classes

This should be allowed:

```
class MathStuff {
    const PI = 3.14;
}

var m = new MathStuff();
var v = m.PI;
```

## Internals on Par with Script

Make sure that there is only one implementation of - for example - how to make a class, so the way to make a class at run time via code (`var Point = new Class("Point");` ) and the way the runtime makes classes from 

```
class Point {
}
```

is *literally* the same implementation in C# (not just identical code in two places - the very same code should be executed in both cases).

## Arrow Notation

```
function (this) { return this.x + this.y; }
```

should be equivalent to

```
(this) => { return this.x + this.y; }
```

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

# END-OF-TO-DO
