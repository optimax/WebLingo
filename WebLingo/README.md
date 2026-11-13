# WebLingo

WebLingo is a JavaScript-inspired, cross-platform server-side scripting language. This is its M0.6 implementation: a compiler and a register VM for a first subset of the language, not yet a complete one.

The intended project domain is `weblingo.dev`.

## What runs

M0.6 compiles and runs `.ws` scripts made of `var` and `const` declarations, integer literals, `+ - * /` (where `/` gives a binary64: `10 / 2` is `5.0`), parentheses, assignment, and the built-in `print`:

```javascript
var a = 10;
var b = 20;
var result = a + b;
print(result);   // 30
```

Integer overflow, and reading a variable before it has a value, are runtime errors. String literals, functions, control flow and `.wsp` templates come later, so `samples/HelloWorld` and `samples/SimpleWebsite` do not run yet; `samples/Arithmetic` does.

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
```

`weblingo run <file>` compiles a script and runs it; `weblingo check <file>` compiles it and reports its errors without running it. Errors go to standard error as `file(line,column): error CODE: message`. The exit code is 0 on success, 1 for compilation errors, 2 for a usage error or a file that cannot be read, and 3 for a runtime error.

## Architecture

See [`Documents/weblingo-architecture.md`](../Documents/weblingo-architecture.md) at the repository root; section 15 has M0.6's status, and the choices made where the specification is silent, all still proposed. The VM instruction architecture remains provisional, with register VM first and a later stack-VM benchmark. Includes, layouts, and parameterized components are **all mandatory** for the first template-capable release; they are not implemented yet.

## Verification status

- **Windows** (12 November 2026, .NET SDK 10.0.404): `dotnet restore`, the Release build (no warnings), and `dotnet test` succeed, and `weblingo run samples/Arithmetic/arithmetic.ws` prints `30`.
- **Linux:** not verified yet. The CI workflow (`.github/workflows/build.yml` at the repository root) builds and tests on Linux and Windows; it runs on a push, and has not run yet.
