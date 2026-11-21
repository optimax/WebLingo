# Examples of WebLingo's scripts

Scripts that show WebLingo, the language: what it does, what it refuses to do on purpose, and what it does not do yet.
Each is a `.wlg` file that runs by itself, from the simplest to the standard library, with comments that say what it
shows and why. Sites built with WASP, WebLingo's server pages, are in `../Examples.wasp`.

Each script ends with what it writes, in a block comment, `/* Output: … */`, and, where it fails on purpose, its
errors, `/* Errors: … */`. The solution's tests (`ExamplesTests`, in `WebLingo.Conformance.Tests`) run every script here,
from its source and from its compiled bytecode, and check that it writes exactly that, so that the blocks stay true.

## Running them

With WebLingo installed, `weblingo` on the `PATH`, from this folder:

```bash
weblingo 01-hello.wlg
weblingo run 01-hello.wlg
weblingo run 17-parameters.wlg name=Anna cups=3
weblingo check does-not-compile/missing-semicolon.wlg
```

With the .NET 10 SDK instead, from this folder:

```bash
dotnet run --project ../WebLingo/src/WebLingo.Cli -- run 01-hello.wlg
```

`weblingo run` compiles a script in memory and runs it; `weblingo check` compiles it and reports its errors without
running it.

## The scripts

| Script | What it shows |
|---|---|
| `01-hello.wlg` | a first script: `write` and `writeLn`, imported from `std:console`, comments, semicolons |
| `02-values.wlg` | integers, numbers, decimals, strings, Booleans and `null`; literals; classes of values; conversions, always explicit; `std:math` |
| `03-strict-by-design.wlg` | what WebLingo refuses to guess: no implicit conversions, no truthiness, no silent overflow, no `undefined`; each mistake an exception of a class of its own |
| `04-variables.wlg` | `var` and `const`, block scope, shadowing, closures, a binding for each turn of a loop, hoisted functions |
| `05-strings.wlg` | Unicode strings, their methods and aliases, interpolation with formats and alignments, multi-line raw strings |
| `06-regular-expressions.wlg` | `Regex`: matching, named groups, replacing, splitting; the time limit against patterns that backtrack without end |
| `07-collections.wlg` | arrays, maps and sets; their methods; `sort` giving a new array; spreading; optional indexing |
| `08-functions.wlg` | default and rest parameters, arrow functions, functions as values, receivers, checked calls |
| `09-control-flow.wlg` | `if` as an expression, `? :`, `??` and `??=`, `switch` without fall-through, as a statement and an expression, loops |
| `10-classes.wlg` | classes, inheritance, `super`, properties with their own field, static members and constants, operators, `equal`, `toString` |
| `11-classes-of-values.wlg` | `extend Integer`, classes extending `Number`, `String`, `Array` and `DateTime`, their instances taken as their values |
| `12-building-classes.wlg` | classes built while the program runs: `new Class`, `setMethod`, `setProperty`, `setConstant`, metaclasses |
| `13-errors.wlg` | `throw`, typed catches, `finally`, `throw;`, errors of a program's own, runtime errors as exceptions |
| `14-disposal.wlg` | `using`, `dispose()` and `destructor()`, disposal chained, once, on an exception too, and `async` |
| `15-async.wlg` | `async` and `await`, `Task.all`, `Task.any`, `Task.timeout`, tasks completed by hand, `DeadlockError`, `forget()` |
| `16-modules.wlg` | static and dynamic imports, exports read-only to importers, module parameters, a module defined in code; with `lib/teas.wlg` |
| `17-parameters.wlg` | a script's parameters from the command line, `name=value`, always strings |
| `18-files.wlg` | `std:fs`: text, lines, bytes, folders, copies, `IOError` |
| `19-bytes-json-dates.wlg` | `std:bytes`, `std:json` and `std:datetime` |
| `20-crypto.wlg` | `std:crypto`: hashes, HMAC, secure random bytes, passwords' hashes, comparison in constant time |
| `21-database.wlg` | `std:db` on SQLite in memory: values apart from the SQL, a hostile value stored harmlessly, transactions, `DatabaseError` |
| `22-environment.wlg` | `std:env`: the machine's name, its processor and system, and its environment variables |
| `23-network.wlg` | `std:http` and `std:net`; it reaches the internet, so the tests only check it |
| `24-com.wlg` | `windows:com`, on Windows only: COM's objects, allowed one by one by `weblingo.config`, any other refused |
| `25-limits.wlg` | the limits every execution runs within, which no `catch` can catch |

`weblingo.config`, beside them, is the configuration of the scripts in this folder: it allows `24-com.wlg` its one COM
object.

## What does not compile, on purpose

`does-not-compile/` holds scripts that WebLingo refuses before they run, each for a mistake other languages let
through, each ending with the errors `weblingo check` gives it:

| Script | What it is refused for |
|---|---|
| `missing-semicolon.wlg` | a statement without its `;`: none is ever inserted |
| `let-is-reserved.wlg` | `let`, which declares nothing: `var` and `const` do |
| `declaration-without-value.wlg` | `var total;`: every variable has a value from its declaration on |
| `implicit-global.wlg` | assigning a name never declared, such as a typo: no global is created |
| `constant-assigned.wlg` | assigning a `const` |
| `used-before-declared.wlg` | a name used before its declaration: no hoisted `undefined` |
| `declared-twice.wlg` | a name declared twice in one block |
| `this-outside-a-method.wlg` | `this` outside a method: a function that wants a receiver names it |
| `finally-cannot-drop.wlg` | a `return` leaving a `finally`, which would drop the exception on its way out |
| `await-outside-async.wlg` | `await` in a function not marked `async` |
| `missing-import.wlg` | a standard module's class used without its import |
| `misspelt-parameter.wlg` | a module given a parameter it does not declare |
| `wrong-argument-count.wlg` | a call with too few arguments |

## Choices made on purpose

- **Nothing is guessed.** No value converts to another by itself, a condition must be a Boolean, an integer that
  overflows is an error, and a missing member, key or index is an error rather than `undefined`, which does not exist
  (`03-strict-by-design.wlg`). `x?.name` and `x?[k]` ask for `null` where it is wanted.
- **Mistakes are found before the script runs** where they can be: undeclared names, constants assigned, calls with the
  wrong number of arguments, missing semicolons (`does-not-compile/`).
- **No code from strings.** There is no `eval`: code comes from source files only, compiled and verified before it
  runs; a program may still build classes from functions (`12-building-classes.wlg`).
- **What a script reaches, its imports say.** A standard module's names are a script's only where it imports them, so
  that its imports list everything it depends on and everything its host must grant it (`missing-import.wlg`).
- **What reaches the whole machine is allowed by name.** COM's objects only as `weblingo.config` lists them
  (`24-com.wlg`); a site's files, hosts and environment variables only as its `wasp.config` allows, under `wasp`
  (`../Examples.wasp`); native modules only from the folders a host is configured with.
- **Bounded executions.** Calls nest at most 10,000 deep, the heap is bounded, and a host may limit instructions and
  cancel an execution; none of it can be caught (`25-limits.wlg`). A regular expression gets a second for each match
  (`06-regular-expressions.wlg`).
- **Safe defaults for secrets.** Secure random bytes, slow salted hashes for passwords, comparisons in constant time
  (`20-crypto.wlg`), and a statement's values never written into its SQL (`21-database.wlg`).
- **Errors that say what is wrong**, where and why, each runtime error an exception of a class of its own, its code
  stable, `WL3001`, and a failure naming a path as the script gave it, never more of the machine (`18-files.wlg`).
- **One task at a time.** A program's tasks take turns on one worker, switching only at `await`, so that nothing needs
  a lock (`15-async.wlg`).

## What is not there yet

WebLingo is pre-alpha. These scripts show it as it is today, and these are among what it does not have yet:

- **A database's rows one at a time**, for results too large to hold whole, and databases other than SQLite and SQL
  Server.
- **A module defined in code that `import` can find**: `new Module(name)` makes one, which no path names
  (`16-modules.wlg`).

The WebLingo Language Reference (`../Documents/weblingo-language-reference.md`) describes the language, and the
WebLingo Standard Library Reference (`../Documents/weblingo-standard-library-reference.md`) every class and module.
