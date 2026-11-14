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

## [IDLE] M0.12.5: Template Output's Workload

A benchmark workload of template output (specification, sections 5.6 and 9, M0.12.5), the third of those "Both VMs, for now" waits for. Added by Claude, 2026-11-14.

2026-11-14: waits for "M0.12.1: Pages".

## [IDLE] M0.13: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WL-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

2026-11-13: M0.13 now, after M0.12, templates (specification, section 9); waits for M0.12.

## Native Code Extensions, Continued

The details A1 of "Native Code Extensions" asks to discuss, and one point between A2 and A4 (specification, section 3.9, "Native modules"). One decision each, answerable with "yes" or a letter. Added by Claude, 2026-11-14.

**Q1, what a native module gives: an assembly with one public class implementing `IWebLingoNativeModule`, whose `Register(IWebLingoNativeRegistry registry)` gives its exports, each a function with a name, an arity, and a delegate that takes the call's arguments, as `WebLingoValue`s, with the execution's heap, and gives a value, or a task for asynchronous work, as the host's built-ins (`IWebLingoBuiltinFunction`) do now. Agreed?**

A1: OK

**Q2, a native module's classes: (a) none at first, functions only, classes coming later; (b) classes from the start, each declared by the module with its methods as native functions (`registry.Class("Connection", …)`).**

A2: (b). That's how built-in classes should be implemented.

**Q3, loading one on .NET (adapter-only, section 7.14): (a) each in an `AssemblyLoadContext` of its own, so that two modules may depend on different versions of one library, and a reload can unload it with its application's generation (WL-H004); (b) into the host's own context, simpler, and never unloaded.**

A3: (b), for now

**Q4, a scheme's folder (A2, A3): (a) the module's assembly in it, `native/sqlite.dll`, its dependencies beside it; (b) a folder of its own for each module, `native/sqlite/sqlite.dll`, its dependencies inside it.**

A4: (a)

**Q5, A2's explicit paths and A4's concern, a script that writes an assembly and imports it: (a) a native module loads only from a configured scheme's folder, an explicit path too, which scripts cannot write to (section 7.13's capabilities); (b) explicit paths anywhere, the host's filesystem capability alone keeping scripts from writing assemblies where they can be loaded from.**

A5: (a)

**Q6, the standard modules as native ones (A5): (a) now: `std:system`'s built-ins moved into a native module installed with the runtime, in the `std` scheme's folder; (b) with the first standard module that is not the host's built-ins (`std:fs`, `std:tasks`).**

A6: (a)

## Arrow Notation, Continued

What JavaScript's arrow functions have beyond "Arrow Notation", which made `(a, b) => { … }` the same as `function (a, b) { … }` (specification, section 3.5). One decision each, answerable with a letter. Added by Claude, 2026-11-14.

**Q1, an expression for a body, `(a, b) => a + b`, giving its value, as `(a, b) => { return a + b; }` does: (a) yes; (b) no, a block always.**

A1: (a)

**Q2, one parameter without parentheses, `x => { … }` (and `x => x * 2`, with Q1's (a)): (a) yes; (b) no, parentheses always.**

A2: (a)

**Q3, `async (x) => { … }`, the same as `async function (x) { … }`: (a) yes; (b) no.**

A3: (a)

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.

## Multi-line Strings

WebLingo's string literals end at their line (WL1010, specification, section 3.5, "Strings"), and the language needs strings that span lines too: SQL, HTML fragments, messages and test data. The owner decided so, 2026-11-14, in a chat with Claude, while weighing how a template's `<% … %>` block ends ("`%>` inside a block's code"): if the template compiler reads a block's code as WebLingo's tokens, an unclosed multi-line string, like an unclosed `/* */` comment, runs on over the blocks and the text after it, so its diagnostic should name both where it opened and the `<%` of the block it is in, and its closing delimiter should be one that ordinary text rarely holds. Added by Claude, 2026-11-14.

**Q1, the delimiter: (a) three double quotes, `"""…"""`, as C# 11 and Python write them, with more quotes (`""""…""""`) for a string that holds three; (b) backticks, `` `…` ``, as JavaScript writes them.**

A1: (a), with alternative three single quotes `'''`.

**Q2, escapes in a multi-line string: (a) none, a raw string, `\` being a character like any other (good for regular expressions and Windows paths); (b) the same escapes as an ordinary string's.**

A2: (a). 

**Q3, indentation: the whitespace before the closing delimiter's column is removed from every line, as C# does, so that a string indented with the code around it does not keep that indentation; and the line breaks just after the opening delimiter and just before the closing one are not part of the string. Agreed?**

A3: Yes.

**Q4, line breaks inside a multi-line string are always LF (`\n`), whether the source file's lines end in CRLF, LF or CR, so that a program gives the same string on every platform. Agreed?**

A4: Yes,

**Q5, interpolation (`"Hello, {name}"`), a separate feature: (a) not now; (b) not now, but the delimiter chosen in Q1 leaves room for it (C# writes `$"""…{name}…"""`, JavaScript `` `…${name}…` ``); (c) designed together with multi-line strings.**

A5: (c), but also needed for single line strings.

# END-OF-TO-DO
