# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] Standard Module `std:tasks`

`Task.all`, `Task.any` and `Task.timeout` as the exports of a standard module, `std:tasks` (A4 of "Task Combinators"; specification, sections 3.8 and 3.9), on both VMs. Added by Claude, 2026-11-14.

2026-11-14: waits for the design of how standard modules are packaged, which A5 of "Native Code Extensions" makes native extensions, installed as other native modules are.

## [IDLE] Modules

The module system: `import`, static and dynamic, `export`, `param`, a script's parameters, and the standard library and the built-ins as modules (specification, section 3.9), on both VMs, before M0.12's templates, which build on it. Added by Claude, 2026-11-13.

2026-11-13: waits for the answers to "Modules, Continued", and for "Extending the Classes of Values", above it.

2026-11-14: answered, but for how a parameter is given as a string ("Modules, Quoted Parameters"), which only a script's parameters wait for.

2026-11-14: split into the parts the specification defines in section 9 ("Modules"), each a task below. Waits for them.

## [IDLE] Modules: A Script's Parameters

`weblingo run script.wl name=value` (specification, sections 3.9 and 9). Added by Claude, 2026-11-14.

2026-11-14: every parameter a string (A1 of "Modules, Quoted Parameters"); a script converting one waits for "Converting Strings to Values", which this part does not need.

2026-11-14: waits for "Modules, Quoted Parameters", and for "Modules: Standard Modules".

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

## [IDLE] M0.13: WBC

WBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WL-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

2026-11-13: M0.13 now, after M0.12, templates (specification, section 9); waits for M0.12.

## Static Members Through Instances, Continued

A follow-up to "Static Members Callable on Instances". Added by Claude, 2026-11-14.

**Q1, `p.count = 5`, where `count` is a static field of `p`'s class: (a) it assigns a field of the instance, which then shadows the static, as built; (b) it assigns the static field, as Java does.**

A1: (b) if the static field already exists.

## Adding Methods at Run Time

`extend` (see "Classes of Values, Extensions") adds methods to a class as a declaration, written once in the source, its methods having `this` as a class's do. This task would add a way to do the same while the program runs, for a method, or a class to give it to, that is known only then: chosen by a condition, built by a factory, or named by data. Discussed with the owner, 2026-11-13; added by Claude the same day.

Again, ensure that the implementation is the same for declared classes as well as those instantiated at runtime.

What it needs:

- **A function with a receiver.** A method is a function whose first, hidden parameter is `this` (specification, section 15.2, "Classes"), and a plain function expression has none (`this` in one is WL2009, unless a method around it lends its own). One way: a function expression may declare `this` as its first parameter, `function (this, prefix) { return prefix + this.x; }`, the parameter bound by a call's target (`p.describe("x = ")`), or given as an argument when the function is called directly (`describe(p, "x = ")`).
- **A way to attach it.** A built-in method of classes, `Point.addMethod("describe", describe)`, after which every instance of `Point`, and of its subclasses, has `describe`, with `this` bound, replacing a method of that name as an extension's does (the owner chose replacing for `extend`, 2026-11-13).

Its cost: a class's members could change at any point of a run, so the same call could mean different things at different times, and the VMs could no longer gather a class's methods once, when it is made.

It is deferred: it can be added later, if it proves necessary, since `extend` already covers methods known when the program is written, and nothing in this design would change what programs without it do. When it is taken up, these are the owner's to decide:

**Q1: a function with a receiver, `function (this, a) { … }`, its first parameter named `this`, bound by a call's target, or given as the first argument of a direct call. Agreed?**

A1:  Yes.

**Q2: attaching it: (a) a built-in method of every class, `Point.addMethod("describe", f)`; (b) a statement, `extend Point with describe = f;`.**

A2: (a)

**Q3: may a method attached at run time be removed again (`Point.removeMethod("describe")`)? (a) no; (b) yes.**

A3: (b)

## Native Code Extensions

Make it possible to install and bind native (C# on CLR, later Native C#) code as (external) modules extending the library available to scripts (like Python does for C).

The design needs the owner's decisions first, and the module system ("Modules"; specification, section 3.9), which such modules extend. One decision each, answerable with "yes" or a letter. Questions added by Claude, 2026-11-14.

**Q1, what a native module is, on .NET: (a) an assembly (a DLL) with a class implementing an interface of WebLingo's (`IWebLingoNativeModule`), whose functions and classes become the module's exports; (b) C# source, which the host compiles when it installs it.**

A1: (a) but let's discuss this in detail.

**Q2, importing one: (a) a scheme of its own, `import db from "native:sqlite";`, as `std:` names the standard modules; (b) the standard modules' scheme, `std:sqlite`, so that a standard module and an installed one look alike; (c) a path to the assembly.**

A2: (c), and (a) when the compiler is configured to understand the scheme as pointing to a particular folder, so `native:sqlite` resolves to `sqlite` module in the folder user-configured as `native`. Otherwise, explicit path to the asembly (relative or absolute) should work.

**Q3, where they are found: (a) installed into a folder the host is configured with (`weblingo.json`, or the CLI's own), and loaded when first imported; (b) beside the script that imports them.**

A3: As in A2, multiple folders can be configured, each as a separate scheme. Native  executable modules and scripts (text) should not in general be mixed.

**Q4, trust: native code runs with the host's own rights, past WebLingo's capabilities (section 7.13): (a) only a module an application's capability policy names may be loaded, never one a script asks for alone; (b) any installed module, by any script.**

A4: (b). Installed modules are like built-ins and trusted. What should be prevented, is script downloading a native module which is then executed.

**Q5, its contract: a native function takes and gives WebLingo's values (`WebLingoValue`) through the runtime's API, may make classes and instances, and may give a task for asynchronous work, on WebLingo's operation model (WS-P007), never on .NET's `Task` in the core; and under Native C#, the same module's source is compiled by that toolchain, the contract unchanged (WL-017). Agreed?**

A5: Yes. In fact, standard modules should follow the same model: they are pre-installed external native extensions that can - for example - be swapped by an alternative implementation. Everything should follow the same packaging model, without priviledged code. Runtime treats every native library/moodule the same.

**Q6, C libraries, as Python's `ctypes` reaches them: (a) not directly: a native module reaches them, through P/Invoke, adapter-only (section 7.14); (b) a foreign-function interface for scripts too, later.**

A6: (b), API TBD.

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
