# WebLingo

WebLingo is a JavaScript-inspired, cross-platform server-side scripting language. This is its M0.6 implementation: a compiler and a register VM for a first subset of the language, not yet a complete one.

The intended project domain is `weblingo.dev`.

## What runs

M0.6 compiles and runs `.wl` scripts made of `var` and `const` declarations, integer and binary64 literals (`10`, `1.5`, `1e21`, and the keywords `Infinity` and `NaN`), `+ - * /` (where `/` gives a binary64: `10 / 2` is `5.0`), parentheses, assignment, unary `+`, `-` and `!`, `true`, `false` and `null`, the comparisons `== != < <= > >=`, `&&` and `||`, blocks `{ … }`, `if` and `else`, `while`, `for` and `do … while` with `break` and `continue`, string literals (`"a"` or `'a'`, with escapes) and their concatenation with `+`, functions (`function f(a) { return a; }`, function expressions, arrow functions (`(a, b) => a + b`, `x => { … }`, `async x => …`), closures, built-ins as values, and default values, `function f(a, b = 1)`), a string's `length` and its characters by index (`s.length`, `s[i]`), arrays (`[1, "a"]`, `a[i]`, `a[i] = v`, `a.length`, `a.push(v)`, `a.pop()`), maps (`{ name: "x", "n": 1 }`, `m[k]`, `m[k] = v`, `m.has(k)`, `m.remove(k)`, `m.length`, and `m?[k]`, which gives `null` for an absent key), classes (`class Point { constructor(x) { this.x = x; } get() { return this.x; } }`, `new Point(1)`, fields, methods, `p?.name`, `class B extends A` with `super(…)` and `super.name(…)`, static members (`static var n = 0;`, `static f() { … }`, `A.f()`, `A.n`), properties (`property area { get { return this.w * this.h; } set(value) { … } }`, with `field`, a property's own field), classes made while the program runs (`var Point = new Class("Point");`, `Point.setMethod("describe", function (this, prefix) { … });`, a function with a receiver, `this`, as its first parameter, `__className`, `__instanceOf` and `__parent`, `setProperty`, `setStaticProperty`, `setConstant` and `static const`, and metaclasses, `class Entity extends Class { … }`, whose instances are classes), every class extending `Object`, whose `equal(other)` and `toString()` a class may override (`write` then writes what `toString()` gives, and `n.toString()` gives any value's text), exceptions (`throw`, `try`, `catch` and `finally`, and `Error`, which a caught runtime error becomes), typed catches (`catch (KeyError e)`, several catches, every runtime error an instance of its kind's class), `x is Point` and every value's class (`5 is Integer`), classes extending `Array`, `Map` and `Set` (`class Stack extends Array { … }`), or `Boolean`, `Integer`, `Number` and `String`, whose instances hold a value (`class Celsius extends Number { … }`, `new Celsius(21.5)`, written `Celsius:21.5`), a string read as a value (`Integer.parse("10")`, `Number.parse("1.5")`, `Boolean.parse("true")`, and `new Integer("10")`), or `Function`, whose instances are functions with methods of their own, operators a class declares (`static operator +(a, b) { … }`), `extend` blocks, which give a class, or a kind of value's, methods and static members (`extend Integer { isPositive() { return this > 0; } }`), static members added by assignment (`Point.origin = …`), and `dispose()` or `destructor()`, run once, a derived class's then its base's, by `x.dispose()` or at the end of `using (var r = new R()) { … }`), sets (`#{1, "a"}`, `s.add(v)`, `s.has(v)`, `s.remove(v)`, `s.length`), `foreach (var x in a)` over an array or a string and `foreach (var (k, v) in m)` over a map, modules (`import db from "lib/db.wl";`, `import { f } from "util";`, `export` before a declaration, parameters, `param size = 10;`, `import box from "box" { size: 1 };`, and a script's own, given as strings by `weblingo run script.wl size=10 name=Ann`, and imports while the program runs, `var db = await Module.import("lib/db");`; `samples/Modules`), `async` functions and `await` (`async function f() { … }`, `async` methods and function expressions, `await t` in them and at a script's top level, `x is Task`, `t.forget()`, `Task.all([…])`, `Task.any([…])`, `Task.timeout(t, milliseconds)`, and an `async` `dispose()`, which `using` awaits), and the built-ins `write` (the value as text), `writeLn` (the same, then a line break) and `delay(milliseconds)` (a task that completes once that time has passed):

```javascript
var a = 10;
var b = 20;
var result = a + b;
writeLn(result);   // 30
```

Every variable is initialized where it is declared, and a name means nothing before its declaration (both are compile-time errors); integer overflow is a runtime error. `samples/HelloWorld`, `samples/Arithmetic` and `samples/Modules` run.

## Pages

WebLingo Pages are `.wlp` templates, compiled by the template compiler (`WebLingo.Templates`) into WebLingo, and run by `welp`, their command line (`src/WebLingo.Pages.Cli`). A page's text is written as it stands; `<% … %>` holds statements, which may span several blocks, the text between them in their body; `<%= x %>` writes `x`'s text escaped for HTML, `<%- x %>` as it stands, and either nothing for `null`; and `<%# … %>` is a comment. A line holding only `<% … %>` blocks and spaces writes nothing, not even its line break:

```html
<% param who = "you & me"; %>
<ul>
<% foreach (var i in [1, 2]) { %>
  <li>Item <%= i %> for <%= who %></li>
<% } %>
</ul>
```

writes `<ul>`, then `<li>Item 1 for you &amp; me</li>` and the same for 2, each on a line of its own, then `</ul>`. `<% include "/includes/header.wlp"; %>` reads a template in its place, as if its text stood there: a path starting with `/` is from the application's root, which `welp run --root site page.wlp` gives (the page's folder when it is left out), any other from the including file's folder. `await renderComponent("/components/card.wlp", { product: p });` renders a component, a page with a scope of its own, found and compiled when it is first rendered, which declares the values it takes as parameters, `<% param product; param size = "small"; %>`. `<% layout "/layouts/main.wlp", { title: "Products" }; %>`, after a page's parameters and imports and before anything else, runs the layout, a template with parameters of its own, which writes the rest of the page where it calls `renderBody()`. `samples/SimpleWebsite` has a layout, an include and a component: `dotnet run --project src/WebLingo.Pages.Cli -- run samples/SimpleWebsite/index.wlp`.

## Prerequisites

- .NET 10 SDK (10.0.100 or later compatible feature band)
- Windows or Linux

## Commands

```sh
dotnet restore WebLingo.slnx
dotnet build WebLingo.slnx -c Release
dotnet test WebLingo.slnx -c Release
dotnet run --project src/WebLingo.Cli -- run samples/Arithmetic/arithmetic.wl
dotnet run --project src/WebLingo.Cli -- check samples/Arithmetic/arithmetic.wl
dotnet run --project src/WebLingo.Cli -- --version
dotnet run --project src/WebLingo.Pages.Cli -- run page.wlp
dotnet run --project benchmarks/WebLingo.Benchmarks -c Release
```

The last command times the VM on the benchmark's workloads (`benchmarks/WebLingo.Benchmarks/Workloads`), and writes the results as a table.

`weblingo run <file>` compiles a script and runs it; `weblingo check <file>` compiles it and reports its errors without running it. A script imports a native module, an assembly in a folder its host is configured with, by a scheme, `import db from "native:sqlite";`, or by the assembly's path within such a folder: `weblingo run --scheme native=C:/WebLingo/native script.wl`, or a `weblingo.json` in the script's folder, `{ "schemes": { "native": "C:/WebLingo/native" } }`, names the folders. Errors go to standard error as `file(line,column): error CODE: message`. The exit code is 0 on success, 1 for compilation errors, 2 for a usage error, 3 for a runtime error, and 4 for a file that cannot be read. `welp run [--root <folder>] <page.wlp>` and `welp check [--root <folder>] <page.wlp>` do the same for a page, the page being written to standard output.

## Architecture

See [`Documents/weblingo-architecture.md`](../Documents/weblingo-architecture.md) at the repository root; section 15 has M0.6's status, and the choices made where the specification is silent, all still proposed. The VM instruction architecture remains provisional (WL-BC002): there are a register VM, which `weblingo` runs, and a stack VM, both from the same lowered IR, which the benchmark (`benchmarks/WebLingo.Benchmarks`) compares. Includes, layouts, and parameterized components are **all mandatory** for the first template-capable release; pages, their includes, components and layouts are implemented.

## Verification status

- **Windows** (12 November 2026, .NET SDK 10.0.404): `dotnet restore`, the Release build (no warnings), and `dotnet test` succeed, and `weblingo run samples/Arithmetic/arithmetic.wl` prints `30`.
- **Linux and Windows in CI:** the CI workflow (`.github/workflows/build.yml` at the repository root) restores, builds and tests on `ubuntu-latest` and `windows-latest` on every push; its first run passed on both (CI run 1, 12 November 2026, commit `5229806`).
