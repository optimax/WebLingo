# WebLingo — Solution Scaffold

This repository is the **first implementation increment**, not a working WebLingo compiler or interpreter.

The intended project domain is `weblingo.dev`.

## Prerequisites

- .NET 10 SDK (10.0.100 or later compatible feature band)
- Windows or Linux

## Commands

```sh
dotnet restore WebLingo.slnx
dotnet build WebLingo.slnx -c Release
dotnet test WebLingo.slnx -c Release
dotnet run --project src/WebLingo.Cli -- --version
```

`run` and `check` currently return a **not implemented** diagnostic. The `.ws` and `.wsp` examples are future conformance fixtures, not executable demonstrations yet.

## Architecture

See [`Documents/weblingo-architecture.md`](../Documents/weblingo-architecture.md) at the repository root. The VM instruction architecture remains provisional, with register VM first and a later stack-VM benchmark. Includes, layouts, and parameterized components are **all mandatory** for the first template-capable release; they are not implemented in this scaffold.

## Verification status

- **Windows** (12 November 2026, .NET SDK 10.0.404, a fresh clone): `dotnet restore`, the Release build (no warnings), and `dotnet test` (5 tests) succeed, and `weblingo --version` answers.
- **Linux:** not verified yet. The CI workflow (`.github/workflows/build.yml` at the repository root) builds and tests on Linux and Windows; it runs on a push, and has not run yet.
