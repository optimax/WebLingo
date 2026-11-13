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

The renamed scaffold was statically validated on Windows with .NET SDK 10.0.401. NuGet configuration initialization failed during restore, so compilation and tests remain pending.
