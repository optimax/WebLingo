# WebLingo

WebLingo is a JavaScript-inspired, cross-platform server-side scripting language. This is its M0.6 implementation: a compiler and a register VM for a first subset of the language, not yet a complete one.

The intended project domain is `weblingo.dev`.

## What runs

M0.6 compiles and runs `.ws` scripts made of `var` and `const` declarations, integer and binary64 literals (`10`, `1.5`, `1e21`, and the keywords `Infinity` and `NaN`), `+ - * /` (where `/` gives a binary64: `10 / 2` is `5.0`), parentheses, assignment, unary `+`, `-` and `!`, `true`, `false` and `null`, the comparisons `== != < <= > >=`, `&&` and `||`, blocks `{ … }`, `if` and `else`, `while`, `for` and `do … while` with `break` and `continue`, string literals (`"a"` or `'a'`, with escapes) and their concatenation with `+`, functions (`function f(a) { return a; }`, function expressions, closures, and built-ins as values), a string's `length` and its characters by index (`s.length`, `s[i]`), arrays (`[1, "a"]`, `a[i]`, `a[i] = v`, `a.length`, `a.push(v)`, `a.pop()`), maps (`{ name: "x", "n": 1 }`, `m[k]`, `m[k] = v`, `m.has(k)`, `m.remove(k)`, `m.length`, and `m?[k]`, which gives `null` for an absent key), classes (`class Point { constructor(x) { this.x = x; } get() { return this.x; } }`, `new Point(1)`, fields, methods, `p?.name`, `class B extends A` with `super(…)` and `super.name(…)`, and `dispose()` or `destructor()`, run once, a derived class's then its base's, by `x.dispose()` or at the end of `using (var r = new R()) { … }`), sets (`#{1, "a"}`, `s.add(v)`, `s.has(v)`, `s.remove(v)`, `s.length`), `foreach (var x in a)` over an array or a string and `foreach (var (k, v) in m)` over a map, and the built-ins `write` (the value as text) and `writeLn` (the same, then a line break):

```javascript
var a = 10;
var b = 20;
var result = a + b;
writeLn(result);   // 30
```

Every variable is initialized where it is declared, and a name means nothing before its declaration (both are compile-time errors); integer overflow is a runtime error. `.wsp` templates come later, so `samples/SimpleWebsite` does not run yet; `samples/HelloWorld` and `samples/Arithmetic` do.

## Prerequisites

- .NET 10 SDK (10.0.100 or later compatible feature band)
- Windows or Linux

## Commands

```sh
dotnet restore WebLingo.slnx
dotnet build WebLingo.slnx -c Release
dotnet test WebLingo.slnx -c Release
dotnet run --project src/WebLingo.Cli -- run samples/Arithmetic/arithmetic.ws
dotnet run --project src/WebLingo.Cli -- check samples/Arithmetic/arithmetic.ws
dotnet run --project src/WebLingo.Cli -- --version
dotnet run --project benchmarks/WebLingo.Benchmarks -c Release
```

The last command times the VM on the benchmark's workloads (`benchmarks/WebLingo.Benchmarks/Workloads`), and writes the results as a table.

`weblingo run <file>` compiles a script and runs it; `weblingo check <file>` compiles it and reports its errors without running it. Errors go to standard error as `file(line,column): error CODE: message`. The exit code is 0 on success, 1 for compilation errors, 2 for a usage error, 3 for a runtime error, and 4 for a file that cannot be read.

## Architecture

See [`Documents/weblingo-architecture.md`](../Documents/weblingo-architecture.md) at the repository root; section 15 has M0.6's status, and the choices made where the specification is silent, all still proposed. The VM instruction architecture remains provisional (WS-BC002): there are a register VM, which `weblingo` runs, and a stack VM, both from the same lowered IR, which the benchmark (`benchmarks/WebLingo.Benchmarks`) compares. Includes, layouts, and parameterized components are **all mandatory** for the first template-capable release; they are not implemented yet.

## Verification status

- **Windows** (12 November 2026, .NET SDK 10.0.404): `dotnet restore`, the Release build (no warnings), and `dotnet test` succeed, and `weblingo run samples/Arithmetic/arithmetic.ws` prints `30`.
- **Linux and Windows in CI:** the CI workflow (`.github/workflows/build.yml` at the repository root) restores, builds and tests on `ubuntu-latest` and `windows-latest` on every push; its first run passed on both (CI run 1, 12 November 2026, commit `5229806`).
