# WebLingo — Consolidated Architecture Specification

**Document:** `weblingo-architecture.md`  
**Status:** Consolidated architectural baseline through M0.6, including approved WS-T001–WS-T008 template composition and the WebLingo project name (12 November 2026)  
**Implementation status:** M0.6.1 solution scaffold generated (22 October 2026); renamed to WebLingo on 12 November 2026; M0.6.2–M0.6.7 implemented on 12 November 2026, a working compiler and register VM for the M0.6 subset, built and tested on Windows and Linux (§14, §15).  
**Convention:** **Approved** means explicitly accepted by the project owner. **Proposed** means an implementation recommendation requiring confirmation or validation. **Provisional** means deliberately undecided.

## 1. Executive overview

WebLingo is a JavaScript-inspired, native-oriented, cross-platform **server-side scripting language** designed to combine the low-friction deployment experience of PHP and classic ASP with modern language semantics, secure defaults, efficient execution, and first-class application development.

- Source scripts use **`.ws`**; HTML/server template pages use **`.wsp`**.
- Page-oriented and application-oriented programming are equally first-class.
- The language is JavaScript-inspired but **does not promise ECMAScript compatibility**.
- The initial compiler, bytecode VM, runtime and hosting stack are implemented in **C#/.NET 10**.
- The long-term goal is to compile that C# implementation with the project's **independent Native C# compiler**, eliminating the .NET runtime dependency **without a C++ rewrite**. Native C# and WebLingo remain separate languages and runtime systems.
- The execution engine and platform services must be independent of IIS, ASP.NET Core, FastCGI, Kestrel, Windows and Linux.
- IIS is the priority deployment target; FastCGI and standalone HTTP hosting are architectural targets.

The approved project name is **WebLingo** (formerly WebScript). The intended project domain is **`weblingo.dev`**. The solution is `WebLingo.slnx`, project and namespace roots use `WebLingo`, and the CLI executable is `weblingo`. Existing specification IDs and diagnostic codes retain their `WS` prefix.

### 1.1 Decision register

| ID / area | Decision | Status |
|---|---|---|
| Product direction | JavaScript-inspired, non-ECMAScript-compatible, native-oriented server scripting | Approved |
| Project naming | WebLingo, with `weblingo.dev` as the intended project domain | Approved |
| WS-G001 | Free and open-source project direction; release timing, scope, and MIT licensing proposal | Direction confirmed; release and licensing details proposed |
| Implementation | C#/.NET 10 initially, later Native C# compilation | Approved |
| WS-017 | Native C# portability contract and replaceable adapters | Approved |
| WS-002.3 | `var` mutable, `const` immutable; both block-scoped, in scope from their declaration to the end of the block; no `let` declaration keyword | Approved |
| M0.1 | Mandatory semicolons; no automatic semicolon insertion | Approved |
| M0.1 | Numeric categories int64, IEEE binary64, decimal128 | Approved |
| M0.1 | String indexing and `length` count Unicode scalar values | Approved |
| M0.2 | WebLingo tracing GC, initially nonmoving mark-and-sweep | Approved |
| M0.1/M0.2 | `using` blocks for deterministic resource cleanup | Approved |
| WS-L011 | Canonical `dispose()`; optional `destructor()` declaration sugar | Approved |
| WS-T001–WS-T008 | Includes, layouts, and parameterized components all required in the first template-capable release | **Approved** |
| Template named sections | Optional page-specific layout sections | Proposed extension; not yet approved |
| WS-BC002 | Register VM preferred, stack VM benchmark required before final decision | **Provisional** |
| M0.3 encoding | Variable-length register operands; disk byte offsets / decoded instruction indices | Proposed |
| M0.3 numbers | IEEE 754 decimal128 BID binary representation | Proposed |
| M0.3 exceptions | Table-driven exception and cleanup regions | Proposed |
| M0.3 dispatch | Initial C# `switch` interpreter | Proposed |
| WS-H009 | ASP.NET Core adapter first for IIS | Approved |
| WS-H012 | Worker-affine VM request execution contexts | Approved |
| WS-H013 | Logical application isolation in a shared runtime | Approved |
| WS-H004 | Atomic application generations for hot reload | Approved |
| WS-P007 | WebLingo-owned asynchronous operation model | Approved |
| WS-P004 | Both synchronous and asynchronous filesystem operations | Approved |
| WS-P013 | Worker-local mutable heaps, explicit cross-worker transfers | Approved |
| WS-P016 | Native portability inventory and CI checks | Approved |
| WS-S001–S016 | M0.6 solution scaffolding and initial implementation contracts | Approved |
| M0.6 implementation default | Register VM first; stack comparison later | Adopted M0.6 default; VM architecture still provisional |
| M0.6 testing default | xUnit on .NET 10 | Adopted M0.6 default |
| M0.6 implementation choices | Lexical, syntax, runtime and CLI choices made where this specification is silent, while implementing M0.6.2 onward (§15.2) | Each row of §15.2 has its own status; the lexical and syntax ones, and the binding ones but "Compilation stages", the bytecode ones but "Compiler output", and the runtime and CLI ones asked about, approved 12 November 2026; the rest are Proposed |
| Repository documents | One copy of this specification, `Documents/weblingo-architecture.md`; documents in the repository's root `Documents/` folder; a shallow folder structure, with no `docs/architecture/` folder; the whole repository may become public (§8.2) | Approved (12 November 2026) |

**Approval boundary:** M0.6 was approved as a specification. This does not imply the repository or implementation already exists. Detailed M0.3 encoding choices were recommended but not separately approved; the architectural decision between stack and register VMs remains open.

---

## 2. Product and deployment model (WS-001–WS-016 overview)

### 2.1 Goals

1. Simple deployment: copy `.ws` and `.wsp` files into a configured site; runtime compilation and caching without Visual Studio, MSBuild or npm at deployment.
2. Strong server-side language with lexical scoping, closures, classes, modules, async execution and deterministic resource cleanup.
3. Secure template rendering by default; capabilities constrain filesystem, network, database and native extension access.
4. Portable compiler and execution engine; host-neutral interfaces.
5. Fast startup and request execution, bounded memory and streaming I/O.
6. Hot reload without exposing partially initialized application state.
7. Versioned, verified portable bytecode (WBC).

### 2.2 Source types and template syntax

| Syntax | Meaning |
|---|---|
| `.ws` | WebLingo module or script |
| `.wsp` | HTML plus server-side WebLingo template |
| `<% statements %>` | Execute statements |
| `<%= expression %>` | Output escaped expression |
| `<%- expression %>` | Output raw expression (security-sensitive) |
| `<%# comment %>` | Server-side template comment |

Template composition includes **mandatory first-release includes, layouts, and parameterized components** (WS-T001–WS-T008; see §13). Template escaping is **on by default**. The exact context-sensitive escaping policy (HTML text versus attributes, JavaScript, URLs, CSS) still needs a detailed normative contract; raw output must be explicit and subject to security review. Templates should stream output with backpressure and support asynchronous operations.

### 2.3 Modules and application structure

- Module graph and route table are associated with an application generation.
- Source compilation and bytecode caching are runtime services.
- `.wsp` pages and `.ws` route handlers are equally supported.
- Module initialization occurs once per application generation; failures prevent the generation from becoming active.
- Standard library and database access use provider interfaces.

---

## 3. Language semantics (WS-002.3, WS-L001–WS-L011)

### 3.1 Declarations and scope — approved

```javascript
var count = 1;      // mutable lexical binding
const limit = 10;   // immutable lexical binding
```

- `var` has **modern JavaScript `let`-style block scope**, not historical JavaScript function scope.
- `const` has the same lexical scope but disallows reassignment.
- `let` is **not** a WebLingo declaration keyword.
- A declaration takes effect where it stands, after its initializer: from there to the end of the block, the name means the declared variable; before it, and within the initializer, the name means what it means outside the block (an outer declaration, or nothing, which is an error). There is no temporal dead zone.
- Redeclaration in the same scope is an error; nested shadowing is allowed.
- Implicit global creation is prohibited.
- Per-iteration bindings are correctly captured by closures.
- Every declaration, `var` and `const` alike, requires an initializer: `var x;` is a syntax error. A variable therefore always has a value (there is no `undefined`).
- `const` does not deeply freeze referenced objects.
- Simple statements require semicolons; there is **no ASI**.

### 3.2 Value categories and operations

The language model proposed in M0.1/M0.2 includes:

| Category | Semantics |
|---|---|
| `null` | Single null value; no `undefined` |
| Boolean | Strict Boolean conditions; no implicit truthiness |
| Integer | Signed 64-bit, checked overflow |
| Number | IEEE 754 binary64 |
| Decimal | Decimal128 for exact decimal arithmetic, literal suffix `m` |
| String | Immutable Unicode string; scalar-value indexing and length |
| Bytes | Binary data, distinct from strings |
| Array | Indexed collection |
| Map / Set | Keyed collections |
| Object / Class | Reference identity, class metadata |
| Function | Callable closure |
| Task | Async computation abstraction |
| Native resource | Handle with deterministic disposal |

Additional proposed semantics:

- `7 / 2` yields `3.5`, not integer truncation.
- Integer overflow throws rather than silently wrapping.
- Decimal/binary floating-point mixing requires explicit conversion.
- Object equality uses identity unless a specific operation says otherwise.
- Missing property access throws; optional access may return `null`.
- Single class inheritance, no exposed JavaScript prototype chain.
- Closures capture binding cells, not merely initial values.
- Functions have defined arity/default/rest behavior; `switch` has no implicit fallthrough.

These details were included in the M0.1/M0.2 design; the **five explicitly approved M0.1/M0.2 choices** are semicolons, numeric categories, tracing GC, `using`, and Unicode scalar-value string indexing. Other language semantics remain the proposed specification unless separately approved.

### 3.3 String representation

- **Approved observable semantics:** `length` and indexing count Unicode scalar values (not UTF-8 bytes, UTF-16 code units, or grapheme clusters).
- **Proposed internal representation:** immutable UTF-8 strings, with optional indexing acceleration where needed.
- Malformed Unicode handling and normalization policies need explicit test cases.

### 3.4 Resource disposal — WS-L011 approved

The **canonical WebLingo disposal protocol** is `dispose()`. A class may optionally declare `destructor()` as alternative syntax for implementing the **same** disposal slot. The language does **not** use `~ClassName()` syntax.

```javascript
class FileWriter {
    constructor(path) {
        this.file = File.openWrite(path);
    }

    destructor() {
        this.file.close();
    }
}

using (var writer = new FileWriter("output.txt")) {
    writer.write("Hello, WebLingo!");
}
```

- Callers use `writer.dispose()` even if the class declares `destructor()`.
- `using` performs deterministic, exception-safe disposal on scope exit.
- Disposal releases resources; it **does not free the GC-managed object's memory**.
- Aliases remain references to the disposed object; resource-dependent operations reject use after disposal (proposed `ResourceClosedError`).
- Disposal must be idempotent; the resource cannot be released twice.
- A class should not declare both `dispose()` and `destructor()` directly; compiler error is recommended.
- Compiler/class metadata should map both declaration forms to one disposal slot.
- **Proposed inheritance rule:** compiler-generated derived-to-base cleanup, each implementation once, including when derived cleanup throws.
- **Proposed state model:** Active → Disposing → Disposed; partial failure must not silently reactivate the resource.
- GC finalization may provide a fallback for native handles, but **must never be relied on for timely cleanup**.
- No automatic deterministic destruction merely because a variable leaves scope.
- Native C# object destructors are a **different** mechanism from WebLingo `dispose()`.

**Open implementation contract:** define async disposal policy and interaction between cancellation, outstanding I/O and cleanup. Synchronous `using` is the initial approved mechanism.

### 3.5 Statements, operators and literals — approved 12 November 2026

The syntax M0.7 implements (§9, "M0.7 parts"). Everything here was approved by the owner on 12 November 2026, except where a point says it is still open.

- **Blocks.** `{ statements }` is a statement, with no `;` after its `}`. A block opens a scope (§3.1): a declaration inside it is in scope from where it stands to the block's `}`, and may shadow an outer name. An empty block `{}` is allowed; a lone `;` is not a statement.
- **Literals.** `true`, `false` and `null` are keywords, as are `Infinity` and `NaN` (§15.2, "Binary64 literals").
- **Equality.** `==` and `!=` give a Boolean and never convert, except that an int64 and a binary64 compare as numbers (`1 == 1.0` is true; `NaN == NaN` is false, as IEEE 754 says). `null == null` is true. Values of different kinds are unequal, which is not an error. There is no `===`.
- **Ordering.** `< <= > >=` take numbers (int64 and binary64 compare as numbers), and give a Boolean; anything else is an error (WS3001). Strings may be added to them later.
- **Logic.** `!`, `&&` and `||` take Booleans only, with no truthiness (WS3001 otherwise); `&&` and `||` short-circuit.
- **Precedence**, loosest first: assignment (right-associative), `||`, `&&`, `== !=`, `< <= > >=`, `+ -`, `* /`, unary `- + !`; binary operators are left-associative.
- **`if`.** `if (condition) statement`, with an optional `else statement`; a branch is any statement, and an `else` belongs to the nearest `if` without one. The condition must be a Boolean at run time (WS3001 otherwise).
- **Loops.** `while (condition) statement`, `for` and `do … while`, each with a strict Boolean condition; `break;` and `continue;` act on the innermost loop, and are a compile-time error outside one. A loop is bounded only by the execution's instruction budget (WS3003). The exact syntax of `for` and `do … while` is still open (`TO-DO.md`).
- **Strings.** Literals in double or single quotes (`"a"`, `'a'`), alike in meaning, each on one line. Escapes: `\n`, `\r`, `\t`, `\0`, `\\`, `\'`, `\"`, and `\u{…}`, which names a Unicode scalar value in at most six hex digits (no surrogates). Any other escape is WS1009, an unterminated string WS1010. `+` concatenates two strings; a string and a number are not joined, and nothing converts implicitly (WS3001). `length` and indexing (§3.3) come with member access, later.
- **Functions.** `function name(a, b) { … }` declares a function, in scope throughout its block, so that functions can call each other in any order (unlike `var` and `const`, §3.1). `return value;` ends it, and `return;` gives `null`. Parameters are like `var`s. A function is a value (`var f = name;`), and `function (a) { … }` is a function expression. Closures capture variables (binding cells), not copies.

---

## 4. Runtime values, objects and garbage collection (WS-R001–WS-R010)

### 4.1 Runtime value representation

The proposed implementation uses a compact tagged `WsValue` (target size **16 bytes**) with fields conceptually including kind, flags, auxiliary metadata and a 64-bit payload. Reference-like values use VM-managed handles rather than exposing arbitrary managed object pointers.

- Numeric primitives may fit inline; decimal128 may require boxed storage.
- Objects carry type metadata and reference identity.
- Arrays, functions and closures are VM-managed object types.
- Physical layout and handle generation must be validated against GC, interop and Native C# portability constraints; 16 bytes is an implementation target, not a proven ABI guarantee.

### 4.2 Garbage collection — approved

- WebLingo uses its **own tracing GC**, initially **nonmoving mark-and-sweep**.
- WebLingo GC is distinct from the .NET GC used to run the initial implementation.
- WebLingo GC is also distinct from the future Native C# runtime's deterministic lifetimes and reference-counted structures.
- Resource cleanup through `dispose()` is independent of reachability-based memory reclamation.

### 4.3 Worker-local heap ownership — approved M0.5

- Mutable WebLingo heaps are worker-local.
- Request VM frames and mutable request state belong to their assigned worker.
- Immutable application data may be shared under explicit rules.
- Cross-worker transfer of mutable data requires a defined copy, ownership-transfer, or message-passing contract; unsynchronized shared mutable references are forbidden.
- Async operations must root or retain buffers and handles until completion.
- Native handles cannot be released while an outstanding operation uses them.

---

## 5. Bytecode and VM architecture (WS-BC001–WS-BC016)

### 5.1 WBC goals

WebLingo Bytecode (WBC) is intended to be portable, versioned, verified, architecture-independent and suitable for caching. It must not embed .NET object references, machine pointers or hosting-framework types.

### 5.2 VM architecture — **provisional**

The register VM is the **preferred first implementation**, but the final register-versus-stack decision is explicitly reserved until benchmarks compare equivalent workloads compiled from the **same bound/lowered IR**.

| Consideration | Register VM | Stack VM |
|---|---|---|
| Dispatch count | Often lower | Often higher |
| Instruction size | Often larger | Often smaller |
| Compiler complexity | Higher | Lower |
| Dataflow/liveness | More explicit | More implicit |
| Native code / optimization potential | Good | Good |

M0.6 implementation default: implement the minimal register VM first and add a stack VM benchmark prototype later. **This does not approve WS-BC002 permanently.**

### 5.3 Proposed WBC binary container

- Header target: **32 bytes**, magic `WBC\0`, major/minor version, ABI/feature flags, section count, directory offset and total length.
- Section-directory entry target: **32 bytes**.
- Sections: constants, type/module metadata, function table, code, exception tables, debug/source mapping, integrity metadata.
- Integer encoding: signed LEB128 where appropriate.
- Floating point: IEEE binary64.
- Decimal128 encoding: IEEE 754 BID **proposed**, not yet approved.
- Strings: UTF-8.
- Register count: proposed maximum **65,535 per function**.
- Operand encoding: variable-length **proposed**.
- Serialized control-flow targets: byte offsets; decoded VM representation may use instruction indices **proposed**.

The binary layout is **not yet frozen**. M0.6 may begin with an in-memory instruction model.

### 5.4 Proposed instruction families

- Constant/local/global loads and stores.
- Integer, floating and decimal arithmetic; comparisons.
- Branches and conditional jumps.
- Function calls, returns, closures and captures.
- Object, array and property operations.
- Exceptions, cleanup and disposal regions.
- Async await, suspend/resume and cancellation checks.
- Template literal, escaped expression and raw expression writes.

Function calls may use contiguous argument registers and an explicit receiver. The exception design prefers table-driven protected/cleanup regions, **pending approval**.

### 5.5 Verification

Verification is mandatory before executing bytecode. It must validate:

1. Container structure, versions and section bounds.
2. Opcode and operand validity.
3. Branch and exception-region targets.
4. Constant, module and function references.
5. Register initialization and control-flow consistency.
6. Async/cleanup state and resource-safety constraints as implemented.

Malformed bytecode must fail with a defined verification error, not crash the VM.

### 5.6 Dispatch and benchmarks

An initial C# `switch` dispatch loop is **proposed** for clarity and portability. Alternative dispatch techniques may be benchmarked later. Benchmarks should include arithmetic, loops, calls, closures, property access, async suspension, template output, and representative I/O-bound server workloads. Measure throughput, latency, allocation, bytecode size, startup and implementation complexity.

---

## 6. Hosting architecture (WS-H001–WS-H014)

### 6.1 Approved hosting decisions

1. **IIS Stage 1:** ASP.NET Core adapter first (WS-H009).
2. **Concurrency:** worker-affine request execution contexts (WS-H012).
3. **Isolation:** logical isolation among applications in a shared runtime (WS-H013).
4. **Reload:** atomic application generations; old requests drain on old generation (WS-H004).

The initial IIS adapter is an implementation choice, not a permanent dependency of the language or runtime. FastCGI, standalone HTTP and eventually a native IIS integration remain targets.

### 6.2 Host-independent layers

```text
.ws scripts / .wsp templates / modules / routes
                    |
         WebLingo Compiler + VM
                    |
          WebLingo Runtime
                    |
    WSHI: host-neutral request/response/lifecycle API
                    |
      +-------------+--------------+
      |             |              |
  IIS adapter    FastCGI       Standalone HTTP
  (ASP.NET Core) adapter       (Kestrel initially)
```

Hosting adapters must not directly manipulate VM registers, GC internals or compiler structures.

### 6.3 Runtime hierarchy

```text
WsRuntime
  ApplicationManager
    ApplicationGeneration A
    ApplicationGeneration B
  Scheduler
  ModuleCache
  CompilerService
  PlatformServices

ApplicationGeneration
  Configuration
  ModuleGraph
  RouteTable
  CapabilitySet
  Immutable/shared generation state
  Worker-local execution state

RequestContext
  Request / Response
  OwningWorker
  ExecutionContext
  Cancellation
  ResourceBudget
  Request-local heap and resources
```

The runtime can host multiple applications. Application boundaries isolate module namespaces, capabilities and mutable state logically; **OS process isolation is still required for hostile tenants or stronger security guarantees**.

### 6.4 Runtime lifecycle

`Created → Initialized → Running → Stopping → Stopped`

- Initialization configures compiler, VM, scheduler, GC and platform services.
- Shutdown stops accepting requests, drains in-flight work up to a configured deadline and releases resources.
- Host termination after the deadline is possible; cleanup code must not assume process exit always permits graceful completion.

### 6.5 Application generations and hot reload — approved

`Discovered → Loading → Compiled/Validated → Initialized → Ready → Serving → Draining → Unloaded`

- Source/configuration changes create a **new generation**.
- Compile and initialize the replacement before publication.
- Atomically publish only a healthy new generation.
- New requests use the new generation; existing requests finish on the old one.
- The old generation unloads after its active requests and retained operations drain.
- Failed new generations do not displace the last healthy generation.

### 6.6 Request lifecycle

`Received → Select application generation → Resolve route → Create worker-affine execution context → Invoke script/template → Stream response → Cleanup → Release`

- Request-local mutable state is not shared between requests.
- Async suspension does not block an OS thread.
- Completion callbacks enqueue resumptions onto the owning VM worker.
- A request remains bound to its original application generation.

### 6.7 HTTP request/response interfaces

Conceptual interfaces:

```csharp
public interface IWsHttpRequest
{
    string Method { get; }
    string Path { get; }
    string QueryString { get; }
    string Scheme { get; }
    string Host { get; }
    IWsHeaderCollection Headers { get; }
    IWsBodyReader Body { get; }
}

public interface IWsHttpResponse
{
    int StatusCode { get; set; }
    IWsHeaderCollection Headers { get; }
    IWsBodyWriter Body { get; }
}
```

Response states: **Uncommitted → Committed → Completed**. Headers cannot be mutated after commitment; body output must support streaming and backpressure. Request bodies must be streamable without mandatory full buffering.

### 6.8 WSHI native ABI — proposed

A future C-compatible ABI will use opaque runtime, application and request handles, with create/load/begin/cancel/release operations. Final signatures must define ABI versioning, struct sizes, calling conventions, ownership, callback lifetime, threading and error reporting. No managed .NET object crosses the ABI.

### 6.9 IIS and deployment

Example site:

```text
MySite/
  index.wsp
  products.wsp
  api/products.ws
  lib/database.ws
  weblingo.json
```

The desired deployment model is automatic source detection, compilation and caching, with no build tooling required on the server. IIS uses the ASP.NET Core adapter first. FastCGI and standalone hosts translate protocol details into WSHI.

### 6.10 Scheduling — approved affinity, proposed fairness

- Bounded VM worker pool.
- Execution contexts are **worker-affine** and resume on their owning worker.
- Platform I/O completion may happen on another host thread but must post work to the owner.
- Instruction-quantum yielding and bounded blocking-operation pools are recommended.
- Work stealing or migration is deferred until supported by explicit safe-transfer semantics.

### 6.11 Security and diagnostics

Applications have immutable capability policies for filesystem, network, database, environment, process and native extensions. Enforcement occurs at the service boundary; hiding functions is not sufficient. Diagnostics should include request IDs, latency, VM time, await time, GC pauses, allocations, module cache, generation count, worker utilization, failures and leaks.

---

## 7. Platform abstraction layer (WS-P001–WS-P016)

### 7.1 Approved M0.5 decisions

- **WS-P007:** Runtime-owned `WsOperation` semantics, not .NET `Task`/`ValueTask` as the core contract.
- **WS-P004:** Synchronous **and** asynchronous filesystem APIs, with blocking work isolated from VM workers.
- **WS-P013:** Worker-local mutable heaps, immutable shared application data and explicit cross-worker transfers.
- **WS-P016:** Portability inventory and CI enforcement, rather than immediately forbidding every unimplemented Native C# feature.

### 7.2 PAL layers

```text
Portable WebLingo core (lexer, compiler, WBC, VM, GC)
                           |
                 PAL abstractions
                           |
            +--------------+--------------+
            |                             |
        .NET adapter                  Native adapter
     (.NET 10 initially)        (Native C# RTL / OS APIs)
```

The core must not directly depend on ASP.NET Core, ADO.NET, `System.IO.File`, `HttpClient`, platform P/Invoke or a specific host. Platform services are injected at runtime initialization and capability-filtered for each application.

### 7.3 Proposed platform services interface

```csharp
public interface IWsPlatformServices
{
    IWsFileSystem FileSystem { get; }
    IWsClock Clock { get; }
    IWsSchedulerHost Scheduler { get; }
    IWsNetworkService Network { get; }
    IWsCryptoService Crypto { get; }
    IWsDiagnostics Diagnostics { get; }
}
```

These are **conceptual contracts**, not frozen source-level APIs.

### 7.4 Filesystem (WS-P004)

- Open/read/write/flush/metadata/delete operations, synchronous and asynchronous.
- Portable paths and file metadata.
- Resource-owning file handles with canonical disposal.
- No unbounded blocking on cooperative VM workers.
- Capability-safe path resolution must address traversal, symlinks, Windows drive/UNC paths, case differences, device names and TOCTOU races; authorize opened objects where possible.

### 7.5 Buffers (WS-P005)

- Mutable and read-only buffer views with explicit handle, offset and length.
- Owners may be VM heap, request allocation, I/O adapter or native resource.
- Pending operations retain or root buffers until completion.
- No raw managed addresses exposed across portable contracts.

### 7.6 Clocks (WS-P006)

- Wall clock for timestamps.
- Monotonic clock for timeouts and elapsed-time measurements.
- Time-zone/calendar services separate from raw clock primitives.
- Injectable clock for deterministic tests.

### 7.7 Asynchronous operations (WS-P007) — approved ownership

Core-visible operations are runtime-owned, with states conceptually:

`Created → Pending → Completed | Failed | Cancelled`

- Exactly one terminal state.
- Adapter completions may arrive on arbitrary threads.
- VM continuation resumes only through the owning worker's queue.
- Cancellation is cooperative and races with completion; terminal-state arbitration must be atomic.
- .NET `Task`/`ValueTask` may be used **inside adapters** but do not define WebLingo execution semantics.
- An optimized production implementation need not allocate a separate interface object for every operation.

### 7.8 Scheduler (WS-P008)

- Post/wake primitives for named VM workers.
- Worker affinity for frames, register storage and mutable heap state.
- Proposed instruction budgets and safe points to prevent starvation.
- Blocking I/O routed to asynchronous APIs or bounded blocking pools.

### 7.9 Networking (WS-P009)

- Capability-aware outbound connections and protocol adapters.
- Authorization must consider hostname, resolved IP, port, protocol, redirects, DNS rebinding and TLS verification.
- HTTP is a higher-level service; transport primitives remain portable.

### 7.10 Cryptography (WS-P010)

Proposed baseline: secure random bytes, SHA-256/SHA-512, HMAC, constant-time comparisons, password hashing via approved algorithms and TLS through networking adapters. Keys require controlled access and feasible zeroization.

### 7.11 Database providers (WS-P011)

Provider-neutral connections, parameter binding, transactions, cancellation, streaming result sets and deterministic disposal. Initial suggested providers: SQL Server and SQLite via ADO.NET; later native providers and ODBC. Connection pooling belongs to providers/adapters, not the VM.

**Illustrative** WebLingo usage (exact async connection API not yet frozen):

```javascript
using (var db = Database.open("MainDatabase")) {
    var rows = await db.query(
        "SELECT Id, Name FROM Customers WHERE Active = @active",
        { active: true }
    );
}
```

### 7.12 Resource ownership (WS-P012–P013)

- PAL C# implementations may expose `IWsDisposable.Dispose()`; WebLingo uses lowercase `dispose()`.
- Native C# destructors may implement internal PAL cleanup but do not change WebLingo semantics.
- In-flight I/O retains handles/buffers until completion or safe cancellation.
- Worker-local mutable objects cannot be concurrently mutated by foreign workers.

### 7.13 Capabilities and errors (WS-P014–P015)

- Application generation owns an immutable capability set; request policy may only restrict it.
- Enforce access at actual filesystem/network/database/process/native boundaries.
- Translate host exceptions (e.g., `IOException`) into stable WebLingo error categories/codes with optional cause and diagnostic context.
- Avoid leaking host internals to HTTP clients.

### 7.14 Native portability (WS-P016)

- Track C# features and BCL dependencies as **Portable**, **Planned**, **Adapted** or **Prohibited**.
- Reflection.Emit and uncontrolled runtime code generation are prohibited in the portable core.
- ASP.NET Core, ADO.NET, P/Invoke and OS-specific threading are adapter-only.
- Generics, exceptions, delegates and other Native C# features are tracked against actual compiler progress.
- CI should check dependency boundaries and run cross-platform conformance tests.

---

## 8. M0.6 solution scaffolding (WS-S001–WS-S016) — approved

### 8.1 Deliverable and scope

A compilable, minimal **.NET 10 Visual Studio solution**, not a full language implementation. First vertical slice:

`Source → Lexer → Parser → Binder → Lowered IR → Bytecode generator → Verifier → VM → Result`

The first implementation may use an in-memory bytecode representation; WBC binary serialization comes after encoding decisions are settled.

### 8.2 Proposed repository layout

```text
WebLingo/
  WebLingo.slnx
  Directory.Build.props
  Directory.Packages.props
  global.json
  README.md
  LICENSE
  docs/
    architecture/
    language/
    bytecode/
    hosting/
    portability/
  src/
    WebLingo.Core/
    WebLingo.Language/
    WebLingo.Compiler/
    WebLingo.Bytecode/
    WebLingo.Runtime/
    WebLingo.VM/
    WebLingo.Platform.Abstractions/
    WebLingo.Platform.DotNet/
    WebLingo.Hosting.Abstractions/
    WebLingo.Hosting.AspNetCore/
    WebLingo.Hosting.Standalone/
    WebLingo.StdLib/
    WebLingo.Cli/
  tests/
    WebLingo.Language.Tests/
    WebLingo.Compiler.Tests/
    WebLingo.Bytecode.Tests/
    WebLingo.Runtime.Tests/
    WebLingo.VM.Tests/
    WebLingo.Platform.Tests/
    WebLingo.Conformance.Tests/
  samples/
    HelloWorld/
    Arithmetic/
    Functions/
    SimpleWebsite/
```

Add FastCGI, database provider and native adapter projects when their implementation milestones begin, rather than creating unnecessary empty projects.

**Documents — approved amendment (12 November 2026):** the repository's root holds the solution folder above, `WebLingo/`, beside a flat `Documents/` folder for the project's documents. This specification exists once, as `Documents/weblingo-architecture.md`; there is no `docs/architecture/` folder, and the folder structure is kept shallow: the portability inventory is `Documents/NativeCompatibility.md`, and there is no `eng/` folder. The whole repository may become public, so nothing that should stay private is put into it.

### 8.3 Project dependencies (WS-S002)

- Core has no WebLingo project dependencies.
- Language depends on core, not compiler/runtime.
- Bytecode depends on core, not compiler/VM.
- Compiler consumes Language and Bytecode.
- Runtime does not depend on Compiler.
- VM consumes Bytecode and Runtime.
- Hosting/CLI compose the engine; framework-specific types do not leak into core.
- All references must form a directed acyclic graph.

### 8.4 Initial contracts (WS-S003–WS-S012)

**Source spans:** stable offsets, lines and columns. Serialized offsets measured in UTF-8 bytes; diagnostics use documented one-based line/column conventions.

**Diagnostics:** severity, stable code, message, source span. Suggested codes:

| Code | Meaning |
|---|---|
| WS1001 | Unexpected token |
| WS1002 | Missing semicolon |
| WS2001 | Undefined identifier |
| WS2002 | Duplicate declaration |
| WS2003 | Assignment to constant |
| WS2004 | Read before initialization |
| WS3001 | Invalid operation |
| WS4001 | Bytecode verification failure |

Codes added while implementing M0.6 (§15.2):

| Code | Meaning | Status |
|---|---|---|
| WS1003 | Invalid character | Approved (12 November 2026) |
| WS1004 | Invalid numeric literal (malformed, or out of range) | Approved (12 November 2026) |
| WS1005 | Unterminated comment | Approved (12 November 2026) |
| WS1006 | Declaration without an initializer | Approved (12 November 2026) |
| WS1007 | Invalid assignment target | Approved (12 November 2026) |
| WS1008 | Source nested too deeply | Approved (12 November 2026) |
| WS2005 | Not a function (a call of something that cannot be called) | Approved (12 November 2026) |
| WS2006 | Wrong number of arguments in a call | Approved (12 November 2026) |
| WS3002 | Integer overflow (a result outside int64) | Approved (12 November 2026) |
| WS3003 | Execution budget exceeded (instructions or registers) | Approved (12 November 2026) |
| WS3004 | Execution cancelled by the host | Approved (12 November 2026) |
| WS4002 | Program too large for the bytecode (more registers than a function may have) | Approved (12 November 2026) |

**Lexer:** identifiers, keywords (`var`, `const`), literals, operators, punctuation, source spans and malformed-input diagnostics.

**Parser:** initial subset of variable declarations, integer literals, identifiers, arithmetic, parentheses and mandatory semicolons; preserve source spans and recover sufficiently to report multiple errors.

**Binder/IR:** resolve lexical symbols, each from its declaration on (§3.1); produce bound tree and lowered IR independent of register/stack VM choice.

**Bytecode generator:** produce an executable in-memory module; serialization deferred until WBC encoding finalized.

**Verifier:** reject invalid opcodes, operands, branches, constants and uninitialized temporaries.

**Runtime values:** implement null, Boolean, int64 and binary64 first; decimal128 follows without changing approved language semantics.

**VM:** execute verified modules with an execution context containing owner worker, cancellation, instruction budget, resource budget and runtime services. Initial execution may be synchronous.

**CLI:** executable `weblingo`; first working commands `run`, `check`, `--version`; later `compile`, `disasm` and `serve`.

Example:

```javascript
var a = 10;
var b = 20;
var result = a + b;
writeLn(result);
```

Expected output: `30`. `write` and `writeLn` are built-ins the CLI host registers (§15.2).

### 8.5 Build conventions (WS-S013–WS-S014)

- Target framework: `net10.0`.
- Nullable reference types enabled.
- Implicit usings enabled.
- Deterministic builds; warnings treated as errors.
- Central package management; minimal third-party dependencies in portable core.
- Maintain the portability inventory, `Documents/NativeCompatibility.md`.
- **Testing default:** xUnit on .NET 10, adopted with M0.6 approval.

Proposed central props:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

### 8.6 Initial conformance tests (WS-S015)

| Program / scenario | Expected behavior |
|---|---|
| `var x = 10;` | Valid |
| `const x = 10;` | Valid |
| `const x;` | Compile error |
| `var x = 10` | Missing-semicolon compile error |
| `var x = 1; var x = 2;` | Duplicate declaration error |
| `x = 10;` | Undefined identifier error |
| `var x;` | Compile error |
| `writeLn(x); var x = 1;` | Undefined identifier error |
| `var x = 10 + 20;` | Result 30 |
| `var x = 10 / 2;` | Result 5.0 |
| Signed int64 overflow | Runtime error |
| Malformed source | Diagnostic, not compiler crash |
| Malformed bytecode | Verification failure, not VM crash |

**Note:** every declaration has an initializer and a name means nothing before its declaration (§3.1), so no variable can be read before it has a value, and no runtime check for it is needed.

### 8.7 Implementation increments (WS-S016)

1. **M0.6.1:** solution skeleton, projects, references, CLI shell, test harness.
2. **M0.6.2:** lexer and tokenization tests.
3. **M0.6.3:** parser, AST and diagnostics.
4. **M0.6.4:** binder and lowered IR.
5. **M0.6.5:** minimal bytecode generator and verifier.
6. **M0.6.6:** minimal register VM, arithmetic, locals and a built-in that writes output (`write`, `writeLn`).
7. **M0.6.7:** end-to-end conformance on Windows/Linux.

### 8.8 M0.6 acceptance criteria

- Builds cleanly on Windows and Linux.
- Dependency rules enforced.
- CLI compiles and runs simple `.ws` programs.
- Lexer, parser, binder, generator, verifier and VM are separate components.
- Tests pass; malformed inputs fail safely.
- Portability inventory exists.
- Portable core has no ASP.NET Core or ADO.NET dependency.
- Compiler IR is not tied to a permanent VM choice.
- Build and test instructions are documented.

---

## 9. Milestone roadmap and current status

| Milestone | Purpose | Status |
|---|---|---|
| M0.1 | Language grammar and semantics | Key normative choices approved; detailed semantics drafted |
| M0.2 | Runtime value model, GC and disposal | Key choices approved; detailed physical contracts drafted |
| M0.3 | WBC bytecode and VM architecture | Drafted; VM comparison and several encoding choices open |
| M0.4 | Hosting ABI and runtime lifecycle | **Four explicit architectural decisions approved** |
| M0.5 | Platform abstraction and portability | **Four explicit architectural decisions approved** |
| M0.6 | Solution scaffolding and implementation contracts | **Approved; M0.6.1–M0.6.7 implemented and verified on Windows and Linux (§15)** |
| M0.7 | Conformance suite expansion: the language of §3.5, in parts M0.7.1 to M0.7.6 (below), each with its conformance tests | Started (12 November 2026) |
| M0.8 | Windows/Linux CI and portability validation | Started (12 November 2026): the CI workflow builds and tests on Windows and Linux (first run passed: CI run 1, 12 November 2026, commit `5229806`); tests check the portable core's assemblies against the dependency rules (§8.3) and the portability inventory (WS-P016) |

**M0.7 parts** (12 November 2026), each landing on its own with its conformance tests, which `WebLingo.Conformance.Tests` runs beside §8.6's:

1. **M0.7.1:** blocks and nested scopes. Implemented (12 November 2026); a missing `}` is WS1001, at its `{`.
2. **M0.7.2:** `true`, `false` and `null`; `==`, `!=`, `<`, `<=`, `>`, `>=`; `!`, `&&`, `||`. Implemented (12 November 2026).
3. **M0.7.3:** `if` and `else`. Implemented (12 November 2026); a branch that is not a block still has a scope of its own, so `if (c) var y = 1;` declares `y` for that branch only (proposed).
4. **M0.7.4:** `while`, `for` and `do … while`, with `break` and `continue`.
5. **M0.7.5:** string literals and concatenation.
6. **M0.7.6:** functions, function values and closures.

**Immediate next activity:** M0.6's increments are implemented and pass their tests on Windows and Linux (12 November 2026, §15). The owner's decisions on the choices proposed in §15.2 come next, then M0.7. The compiler and VM cover the M0.6 subset only (§8.4).

---

## 10. Open questions and specification risks

These items should be explicitly tracked rather than silently treated as approved:

1. **Register versus stack VM (WS-BC002):** compare equivalent benchmark workloads; register-based remains preferred but provisional.
2. **WBC encoding:** finalize variable-length operand scheme, branch addressing, header/directory layout and binary compatibility.
3. **Decimal128 binary encoding:** choose IEEE BID or alternative representation, define canonicalization, rounding and interoperability.
4. **Exceptions and cleanup:** freeze table-driven versus handler-stack mechanics; define guaranteed disposal chaining and cleanup errors.
5. **Async disposal:** specify `using` behavior around async operations, cancellation and outstanding native resources.
6. **`WsValue` layout:** prove 16-byte representation, GC roots and handle validity across both toolchains.
7. **Cross-worker data:** formalize immutable sharing, serialization, ownership transfer and lifetime rules.
8. **Application generation unloading:** define retained task lifetimes, draining deadlines and leak handling.
9. **Template escaping:** define context-aware escaping and safety rules for raw output.
10. **Native ABI:** finalize versioning, calling convention, callback threading, ownership and error structures.
11. **Database API:** settle synchronous versus asynchronous connection establishment, transactions and streaming rows.
12. **Native C# readiness:** track compiler/RTL capabilities and identify adapter replacements in CI.
13. **Security:** specify concrete capability resolution and sandbox limitations; logical isolation is not a security boundary equivalent to separate processes.
14. **Scheduler fairness:** choose instruction quantum, cancellation safe points and bounded blocking pools.
15. **Initial test infrastructure:** xUnit adopted as the M0.6 default; implementation and CI configuration remain to be generated.

---

## 11. Guiding architectural invariants

1. **One language semantics across hosts and toolchains.** WebLingo behavior must not change when replacing .NET adapters with native adapters.
2. **No .NET dependency in the final native product.** The .NET 10 stage is a bootstrap and delivery strategy, not a language requirement.
3. **Portable source and bytecode.** WBC must be validated before execution and must not expose host memory structures.
4. **Clear ownership boundaries.** Worker-affine mutable heaps, explicit cross-worker transfer, deterministic resource disposal.
5. **Separation of disposal and GC.** `dispose()` is deterministic resource release; tracing GC reclaims script-object memory.
6. **Stable host-neutral interfaces.** IIS, FastCGI and standalone hosts adapt to WSHI, not the reverse.
7. **Safe publication.** Hot reload swaps whole initialized application generations atomically.
8. **Secure by default.** Escaped template output, capability-aware services and bounded resource use.
9. **First-class template composition.** Includes, layouts, and parameterized components must all ship with the first `.wsp` template-capable release; no phased deferral.
9. **Measure before freezing VM design.** Keep the lowered IR VM-neutral and benchmark register versus stack execution.
10. **Incremental vertical slices.** Establish a working compiler/VM pipeline before expanding language and hosting complexity.

---

## 12. WS-G001 — Open-Source Distribution and Governance (proposed)

**Decision status:** WebLingo is intended to be free and open source. The public-release milestone, release scope, and MIT License remain proposals.

### 12.1 Release objective and timing

- **Proposed public-release milestone:** A functioning, independently deployable native WebLingo compiler/runtime that does not require .NET, Node.js, V8, PHP, or the separate Native C# compiler on end-user systems.
- Continue developing in C#/.NET 10 according to the approved bootstrap architecture. The repository may remain private during implementation.
- The independent Native C# compiler is a build-time toolchain, **not** a required component of the WebLingo runtime distribution; whether its own source code is published is a separate decision.
- Publish language semantics, WBC bytecode documentation, host interfaces, build instructions, and conformance tests sufficient for independent evaluation and interoperability.

### 12.2 Candidate license: MIT

**MIT is the preferred candidate under consideration, not yet selected.** Subject to rights and third-party-license review, MIT would generally permit commercial and noncommercial use, redistribution, modification, embedding in proprietary products, and proprietary forks, while requiring preservation of copyright and license notices in copies or substantial portions of the licensed software. MIT does **not** require downstream applications or modifications to be open-sourced.

Tradeoff: MIT does not require third parties to contribute improvements back. If reciprocal publication of changes becomes a goal, reconsider alternatives such as MPL 2.0 before the public release. MIT also does not expressly grant trademark rights; project branding and names must be handled separately.

**Unresolved license scope:** Determine whether MIT would cover **all** WebLingo components—language compiler, VM, runtime, standard library, IIS/ASP.NET Core and FastCGI adapters, CLI, native adapters, tests, samples, and documentation—or whether any components remain under different terms. Specify documentation licensing explicitly. No assumption is made that the separate Native C# compiler is part of the release.

### 12.3 Open-source readiness requirements (proposed)

1. **Ownership and provenance:** Track copyright holders, contributors, third-party code, assets, and generated content; verify that the project has the rights necessary to publish each component.
2. **Dependency review:** Maintain a software bill of materials or equivalent dependency inventory; verify redistribution compatibility, required notices, and transitive dependencies. Avoid introducing restrictive dependencies into the portable core.
3. **Reproducible releases:** Provide versioned source tags, documented builds, release artifacts, checksums, and automated Windows/Linux conformance tests.
4. **Specification and compatibility:** Publish versioned language, WBC, PAL/WSHI and extension contracts; state compatibility and deprecation policies.
5. **Community governance:** Establish contribution rules, code review and maintainer authority, a security disclosure process, and a code of conduct before accepting substantial public contributions.
6. **Trademark and naming:** Conduct a fresh name/trademark and ecosystem-confusion check for **WebLingo** before public launch; the MIT software license does not establish availability of the name or rights to a logo.
7. **Security:** Document the limitations of capability-based logical isolation and the need for OS-level process isolation in hostile multi-tenant deployments.

### 12.4 Proposed public repository files

```text
LICENSE                 # Standard MIT text, if MIT is finally approved
README.md
CONTRIBUTING.md
CODE_OF_CONDUCT.md
SECURITY.md
NOTICE.md                # Only if needed for third-party notices
THIRD_PARTY_NOTICES.md   # If required by dependencies
CHANGELOG.md
 docs/                   # Versioned language and runtime specifications
 src/
 tests/
 samples/
```

The actual `LICENSE` file must identify the correct copyright holder and year(s). Do not present MIT as the project's operative license until the owner expressly selects it and the repository is licensed accordingly.

### 12.5 Questions reserved for later approval

- Confirm **MIT versus another license**.
- Decide the **components covered** and documentation license.
- Decide **public repository timing**, governance, and contribution licensing (including whether a CLA or DCO is needed).
- Clear the **WebLingo** name and determine whether a trademark/brand policy is appropriate.
- Define the **native-release readiness criteria** and whether preview source releases occur earlier.

---

## 13. Template composition and reuse (WS-T001–WS-T008) — APPROVED

**Decision:** The first implementation that supports `.wsp` pages **MUST provide all three mechanisms together**: template includes, layouts, and parameterized components. Earlier suggestions to implement them gradually are superseded. Named sections remain a proposed extension, not a requirement approved by this decision.

### WS-T001 — Unified composition model

A `.wsp` file is a server-side template compiled through the common WebLingo compiler and executed by the same VM as `.ws` code. Composition does **not** introduce a second template interpreter. Three constructs are first-class:

- **Include:** embeds a reusable template fragment, usually a header, footer, or navigation region.
- **Layout:** supplies a shared page shell and a designated body insertion point.
- **Parameterized component:** renders a reusable template fragment using explicitly passed parameters in an isolated scope.

These are template composition constructs, not independent languages or client-side frameworks.

### WS-T002 — Includes (required at initial template release)

Illustrative proposed syntax:

```html
<!-- index.wsp -->
<% include "/includes/header.wsp"; %>
<main><h1>Welcome</h1></main>
<% include "/includes/footer.wsp"; %>
```

An include is resolved at compilation/linking time where feasible and participates in the template dependency graph. Paths are application-root-relative by default, subject to capability checks. The precise syntax and variable-scope rules for includes remain implementation-contract details to finalize; the **feature itself is approved and mandatory**.

### WS-T003 — Layouts (required at initial template release)

A layout defines shared document structure and a content insertion point. Illustrative syntax:

```html
<!-- /layouts/main.wsp -->
<!DOCTYPE html>
<html><head><title><%= pageTitle %></title></head>
<body>
    <% include "/includes/header.wsp"; %>
    <main><% renderBody(); %></main>
    <% include "/includes/footer.wsp"; %>
</body></html>
```

```html
<!-- products.wsp -->
<% layout "/layouts/main.wsp"; %>
<% const pageTitle = "Products"; %>
<h1>Products</h1>
```

A layout may itself use a parent layout if the implementation defines an unambiguous acyclic resolution order. Layout body insertion must not require unconditional whole-page buffering. Exact `pageTitle`/layout-data binding semantics are still to be specified.

### WS-T004 — Parameterized components (required at initial template release)

Components are independently compiled templates with **explicitly supplied parameters and isolated local scopes**. They must not implicitly capture arbitrary caller-local variables.

Illustrative component:

```html
<!-- /components/product-card.wsp; parameter: product -->
<article class="product-card">
    <h3><%= product.name %></h3>
    <p><%= product.description %></p>
</article>
```

Illustrative caller:

```html
<%
    for (const product of products) {
        await renderComponent(
            "/components/product-card.wsp",
            { product: product }
        );
    }
%>
```

The exact component parameter declaration syntax and whether `renderComponent` is a built-in function or compiler-recognized intrinsic remain to be finalized. Compiled components must be reusable without reparsing for every invocation.

### WS-T005 — Escaping and output safety

`<%= expression %>` escapes output by default, including within includes, layouts, and components. `<%- expression %>` is an explicitly unsafe/raw-output mechanism and must not be silently used by composition features. The compiler/runtime must specify context-aware handling of HTML text, attributes, URLs, scripts, and styles before claiming complete injection safety. A component must not implicitly convert untrusted strings into trusted HTML. Output remains subject to the same response commitment and streaming rules as ordinary `.wsp` rendering.

### WS-T006 — Resolution, cycles, and security

- Resolve template references within configured application roots and filesystem capabilities; reject traversal and unauthorized access.
- Detect static include and layout cycles at compilation/linking time and report source-located diagnostics.
- Bound dynamic component recursion or cycles at runtime where static detection is impossible.
- Do not allow template composition to bypass module visibility, application isolation, or capability checks.
- Maintain deterministic path normalization across Windows and Linux.

### WS-T007 — Compilation, caching, and atomic reload

Maintain a dependency graph for pages, includes, layouts, and components. When a shared template changes, invalidate affected compiled artifacts and build a **new application generation**. Publish that generation atomically only after successful compilation/initialization; existing requests finish on their previous generation. Compilation and cache identity must account for transitive dependencies, configuration, compiler version, and relevant security policy. A component invocation must not cause reparsing on every request.

### WS-T008 — Streaming, scheduling, and delivery gate

Rendering must support asynchronous output and backpressure without mandatory full-document buffering. Component and layout rendering must honor the WebLingo-owned operation model and worker-affine execution contexts. The first `.wsp`-capable release is **not complete** until all three mechanisms (includes, layouts, parameterized components) are implemented and covered by end-to-end tests, including nested composition, escaping, invalid paths, cycles, dependency invalidation, cancellation, and streaming.

**Project structure amendment:** Introduce `WebLingo.Templates` as the dedicated `.wsp` parser, composition resolver, dependency-graph builder, and lowering layer feeding the common compiler IR. Add `WebLingo.Templates.Tests` and template composition conformance tests. This is an approved architectural responsibility; the precise project boundaries and public C# interfaces can be finalized during implementation.

**Named sections:** Optional layout sections for page-specific scripts, styles, and metadata are a **proposed extension**. They are not part of the three mandatory mechanisms unless separately approved.

---

*This document consolidates the decisions and technical designs discussed through approval of M0.6 and WS-T001–WS-T008. It distinguishes approved requirements from proposed implementation details and deliberately unresolved contracts; it is an architectural reference, not a claim that implementation is complete.*


## 14. M0.6.1 solution scaffold — implementation status (22 October 2026)

**Status: generated; built and tested on Windows and Linux.** This is an implementation milestone, not a change to previously approved language semantics.

- Created `WebLingo.slnx`, .NET 10 project files, centralized package versions, solution-wide build settings, and GitHub Actions build/test workflow for Windows and Linux (moved on 12 November 2026 to `.github/workflows/` at the repository root, where GitHub Actions reads it).
- Created portable Core, Language, Bytecode, Compiler, Runtime, VM, Platform.Abstractions, Templates, Hosting.Abstractions, StdLib project boundaries; .NET platform, ASP.NET Core host adapter, and CLI projects.
- Added a minimal lexer seed and unit tests, public scaffolding interfaces, template-composition contract placeholders, examples, and a Native C# portability inventory.
- The CLI supports `--version` only; `run` and `check` explicitly report that execution is not implemented. No compiler, VM execution, or template rendering is claimed.
- **Verification:** On Windows (12 November 2026, .NET SDK 10.0.404, a fresh clone), `dotnet restore WebLingo.slnx`, `dotnet build WebLingo.slnx -c Release` (no warnings) and `dotnet test WebLingo.slnx -c Release` (5 tests) succeed, and `weblingo --version` answers. (An earlier static validation, with .NET SDK 10.0.401, could not restore.) On Linux, the CI workflow's first run (CI run 1, 12 November 2026, commit `5229806`) built and tested the solution, by then M0.6.7's.
- The initial `WsSourceSpan` and lexer positions use .NET string indices in this scaffold; production source-map byte offsets remain specified as UTF-8 and require explicit conversion.
- Register-based execution remains provisional (WS-BC002); both stack and register execution must eventually be benchmarked.
- All three approved template composition features (includes, layouts, parameterized components) remain mandatory together in the first `.wsp`-capable implementation.

## 15. M0.6 implementation — status and proposed choices

This section records the state of the M0.6 increments after M0.6.1 (§8.7), and the choices made while implementing them where this specification is silent. Each choice in §15.2 was made to unblock the work, and is kept in one place in the code so that it stays easy to change; its row gives its status, **Approved** once the owner decided it, **Proposed** while it awaits that decision. None of them changes an approved decision.

### 15.1 Status

| Increment | Status |
|---|---|
| M0.6.2 lexer and tokenization tests | Implemented (12 November 2026); built and tested on Windows and Linux |
| M0.6.3 parser, AST and diagnostics | Implemented (12 November 2026); built and tested on Windows and Linux |
| M0.6.4 binder and lowered IR | Implemented (12 November 2026); built and tested on Windows and Linux |
| M0.6.5 minimal bytecode generator and verifier | Implemented (12 November 2026); built and tested on Windows and Linux |
| M0.6.6 minimal register VM, arithmetic, locals and `write` | Implemented (12 November 2026): `weblingo run` runs §8.4's example and prints `30`; built and tested on Windows and Linux |
| M0.6.7 end-to-end conformance on Windows and Linux | Implemented (12 November 2026): every program of §8.6 runs through the CLI with its expected output, diagnostic and exit code; passes on Windows and Linux (CI run 1, 12 November 2026, commit `5229806`) |

M0.6's acceptance criteria (§8.8), as of 12 November 2026:

| Criterion | Status |
|---|---|
| Builds cleanly on Windows and Linux | Yes, with no warnings (warnings are errors): locally on Windows, and in CI on `ubuntu-latest` and `windows-latest` (CI run 1, 12 November 2026, commit `5229806`) |
| Dependency rules enforced | By the project references, and by a test of the built assemblies' references (`WebLingo.Architecture.Tests`, `DependencyTests`) |
| CLI compiles and runs simple `.ws` programs | Yes: `weblingo run` and `weblingo check` (`WebLingo.Conformance.Tests`) |
| Lexer, parser, binder, generator, verifier and VM are separate components | Yes: `WsLexer`, `WsParser`, `WsBinder` and `WsLowerer`, `WsCodeGenerator`, `WsBytecodeVerifier`, `WsVirtualMachine` |
| Tests pass; malformed inputs fail safely | Yes, on Windows and Linux (294 tests at M0.6.7, 305 with M0.8's checks); fuzz tests give random source to the compiler, and mutated bytecode to the VM, and find diagnostics, never a crash |
| Portability inventory exists | Yes, `Documents/NativeCompatibility.md`, with what M0.6 uses |
| Portable core has no ASP.NET Core or ADO.NET dependency | Yes, checked by `DependencyTests` |
| Compiler IR is not tied to a permanent VM choice | Yes: the lowered IR is a tree; only the code generator is register-specific |
| Build and test instructions are documented | Yes, the solution's `README.md` |

### 15.2 Proposed choices

| Area | Choice | Status |
|---|---|---|
| Line terminators | CRLF, LF, and CR each end a line (`WsSourceText`) | Approved (12 November 2026) |
| Columns | One-based, counting Unicode scalar values from the start of the line; a tab is one column (`WsSourceText`) | Approved (12 November 2026) |
| Whitespace | Space, tab, CR, and LF only; any other character, Unicode spaces included, is invalid (WS1003) | Approved (12 November 2026) |
| Comments | `//` to the end of the line; `/* … */`, not nested; an unterminated `/*` is an error (WS1005) | Approved (12 November 2026) |
| Identifiers | ASCII letters, digits and `_`, not starting with a digit; case-sensitive | Approved (12 November 2026) |
| Keywords | `var` and `const`; `Infinity` and `NaN`, binary64 literals, which therefore cannot be declared or shadowed; `let` reserved and reported (WS-002.3); no other reserved words yet | Approved (12 November 2026) |
| Integer literals | Decimal digits only; leading zeros allowed and still decimal (`007` is 7); at most 9223372036854775807, larger is an error (WS1004) | Approved (12 November 2026) |
| Binary64 literals | Digits, then a fraction (`.` and digits, at least one on each side of the point: `1.` and `.5` are not literals), an exponent (`e` or `E`, an optional sign, and digits), or both: `1.5`, `0.1`, `1e21`, `2.5E-3`. The value is the nearest binary64; one too large for a binary64 is an error (WS1004), and one too small becomes 0. `Infinity` and `NaN` are keywords for those values, and `-Infinity` is unary minus applied to `Infinity` | Approved (12 November 2026) |
| Literal suffixes | Digits followed directly by a letter, digit or `_` are one invalid literal (WS1004), which keeps room for the decimal suffix `m` (§3.2) | Approved (12 November 2026) |
| Not yet lexed | String and decimal literals: their characters are invalid (WS1003) until their syntax is specified | Approved (12 November 2026) |
| Statements | Variable declarations and expression statements, each ended by `;`; a lone `;` (an empty statement) is an error (WS1001); blocks, `{ … }`, from M0.7.1 (§3.5) | Approved (12 November 2026) |
| Operators | Unary `-` and `+` (`+` gives the number itself; on anything but a number either is WS3001); binary `* /` above `+ -`, both left-associative | Approved (12 November 2026) |
| Assignment | An expression, right-associative (`a = b = 3`), whose value is the value assigned; only a variable's name may be assigned (WS1007), not even a parenthesized one | Approved (12 November 2026) |
| Calls | `callee(argument, …)`, no trailing comma; the callee is any expression, checked when bound | Approved (12 November 2026) |
| `let` | Reported as not a WebLingo keyword (WS1001), with a hint to use `var` or `const`, and parsed as `var` to recover | Approved (12 November 2026) |
| Declaration without a value | `var x;` and `const x;` are syntax errors (WS1006, §3.1) | Approved |
| Error recovery | After an error, parsing resumes at the next `;` or declaration keyword; a token the lexer reported is not reported again, and a statement gets at most one diagnostic for what follows its first error | Approved (12 November 2026) |
| Nesting limits | Parsing recurses at most 256 levels (parentheses, unary operators, assignments, arguments), and an expression is at most 1000 operations deep, so that a recursive compiler stage cannot overflow its stack; deeper source is an error (WS1008). Both limits are arbitrary, and are constants that are easy to change (`WsParser.MaxRecursionDepth`, `WsParser.MaxExpressionDepth`) | Approved (12 November 2026) |
| Compilation stages | Binding runs only on a source without syntax errors, and lowering only on one that binds without errors: a source reports the errors of its first failing stage | Proposed |
| Host built-ins | The host declares its built-ins (name and arity) to the compiler; they live in a scope around the program's and are not privileged in any way, so a declaration may shadow one (`var write = 1;`) | Approved (12 November 2026) |
| Functions as values | Not yet, but they will be: until then a built-in can only be called, and naming it otherwise is an error (WS3001); calling anything but a built-in is WS2005, and calling one with the wrong number of arguments is WS2006, both at compile time | Approved (12 November 2026); functions as values are still to come |
| Use before declaration | A name used before its declaration, or in its own initializer, means the outer declaration, or is undefined (WS2001, at compile time) if there is none (§3.1) | Approved |
| Evaluation order | Left to right: operands, then arguments; an assignment evaluates its value before it stores it | Approved (12 November 2026) |
| Instruction model | An in-memory register instruction model, not WBC (§5.3, §8.4): an operation and up to four integer operands A to D, unused ones 0 (`WsBytecodeContracts.cs`); a module holds its constants (int64 and binary64, kept as IEEE bits), the built-ins it imports by name and arity, and one function, with a source span per instruction and the names of its locals for runtime messages | Approved (12 November 2026) |
| Registers | A function's locals take registers 0 to n − 1 and its temporaries the registers above; at most 65535 registers (§5.3), more is a compile-time error (WS4002) | Approved (12 November 2026) |
| Operations | `LoadConstant`, `Move`, `Negate`, `Plus`, `Add`, `Subtract`, `Multiply`, `Divide`, `CallBuiltin` (arguments in consecutive registers, §5.4), `Jump`, `Return`; from M0.7.2, `LoadNull`, `LoadBoolean`, `Not`, `Equal`, `NotEqual`, `Less`, `LessOrEqual`, `Greater`, `GreaterOrEqual`, `JumpIfFalse` and `JumpIfTrue` (whose condition must be a Boolean, WS3001), and `CheckBoolean` (the right operand of `&&` and `||`) | Approved (12 November 2026) |
| Verification | Besides §5.5: unused operands must be 0; no path may run off the end of the code; every register, local or temporary, must be written before it is read on every path, which is what keeps a variable from being read before it has a value (§3.1); a function whose check needs more than 16 million words of state or 100 million steps is rejected rather than verified slowly. The bounds are arbitrary, and are constants that are easy to change (`WsBytecodeVerifier.MaxStateWords`, `WsBytecodeVerifier.MaxSteps`), as is the register limit (`WsFunction.MaxRegisters`) | Approved (12 November 2026) |
| Compiler output | The compiler verifies what it generates, and reports a module that fails as an internal error (WS4001), with no module | Proposed |
| Runtime values | `WsValue`, a kind and a 64-bit payload, 16 bytes (§4.1's target, met): null, Boolean, int64 and binary64 (§8.4); scripts write `null`, `true` and `false` from M0.7.2 | Proposed |
| Mixed arithmetic | An int64 meeting a binary64 is converted to binary64 (`1 + 0.5`, and `10 / 4 + 1` is 3.5) | Approved (12 November 2026) |
| Binary64 results | IEEE 754 throughout: `1 / 0` is `Infinity`, `-1 / 0` is `-Infinity`, `0 / 0` is `NaN`; only integer arithmetic raises an error (WS3002). `Infinity` and `NaN` are written as keywords (see "Binary64 literals") | Approved (12 November 2026) |
| Invalid operands | Arithmetic on anything but numbers is WS3001; once strings exist, `+` also concatenates them | Approved (12 November 2026) |
| `write` | `write(value)` writes the value as text, with no line break; `writeLn(value)` writes it and a line break (LF); each takes one argument and gives `null`; the host (the CLI) registers them, and they are implemented once in `WebLingo.StdLib`. A line break can also be written as text, once string literals exist | Approved (12 November 2026) |
| Text of a value | `null`, `true`, `false`; an int64 in decimal digits; a binary64 as the shortest decimal that reads back as the same number, always with a decimal point or an exponent (`5.0`, `3.5`, `0.1`, `-0.0`, `1E+21`), or `NaN`, `Infinity`, `-Infinity`; never culture-dependent (`WsValueFormatter`) | Approved (12 November 2026) |
| Linking | The VM verifies a module, then links its imports to the host's built-ins by name and arity; a missing one stops the module before it runs (WS2001) | Proposed |
| Runtime errors | The first runtime error stops the execution, and is reported with the source span of the instruction that raised it; output printed before it stays | Proposed |
| Budgets and cancellation | An execution has an instruction budget (exceeded: WS3003) and a register budget (a module needing more is not run: WS3003); the VM looks at the host's cancellation signal every 1024 instructions (WS3004); a WebLingo-owned signal, not .NET's `CancellationToken` (WS-P007) | Proposed |
| Built-in failures | A built-in that fails, even by throwing, is a runtime error (WS3001) | Proposed |
| CLI | `weblingo run <file>` compiles and runs; `weblingo check <file>` compiles and reports, without running; `weblingo --version` prints `WebLingo M0.6 (pre-alpha)`. Diagnostics go to standard error as `file(line,column): error CODE: message`. Exit codes, each with one meaning: 0 success, 1 compilation errors, 2 a usage error, 3 a runtime error, 4 a file that cannot be read. `write`'s lines end in LF on every platform | Approved (12 November 2026) |
| Diagnostic codes | WS1003 to WS1008, WS3002 to WS3004, and WS4002 (§8.4); WS2005 and WS2006 for invalid calls, and WS3001 at compile time too, for a built-in named other than to call it; WS2004 is not reported, since no variable can be read before it has a value (§3.1) | Approved (12 November 2026) |
