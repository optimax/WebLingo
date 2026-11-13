# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [QUESTION] Register VM or Stack VM (WS-BC002)

The comparison is done (specification, section 5.6, "The comparison"). On the four workloads (loops, calls, closures, strings), the register VM is 1.10 to 1.65 times as fast as the stack VM, and runs 1.29 to 1.75 times fewer instructions. The stack bytecode is 3 to 35 percent smaller in bytes, and its code generator is simpler. Both allocate the same. Not measured yet, because the language does not have them: property access, async suspension, template output, I/O-bound workloads, startup and latency. Added by Claude, 2026-11-13.

**Q1: Which VM does WebLingo keep? (a) the register VM: WS-BC002 is approved, and the stack VM is removed; (b) the register VM: WS-BC002 is approved, and the stack VM stays as it is, for comparisons later; (c) both, for now: WS-BC002 stays provisional until the workloads not measured yet can be, and every new feature is built on both VMs; (d) the stack VM.**

A1:

## [QUESTION] What Comes After M0.8

The roadmap ends with M0.8 (specification, section 9). M0.7's language is implemented, but for `foreach`, which waits for collections; M0.8's CI and portability checks are in place. The specification does not say what comes next, and each candidate needs syntax or a format it does not give yet, which I would propose here, feature by feature, before building it. Added by Claude, 2026-11-13, from the roadmap (`Documents/implementation.md`, section 13).

**Q1: Which comes next? (a) collections: arrays, maps and sets (section 3.2), with indexing, `length` and member access (section 3.3), and `foreach` (section 3.5); (b) `.wsp` templates: includes, layouts and parameterized components together (WS-T001 to WS-T008); (c) classes, `dispose()` and `using` (sections 3.2 and 3.4, WS-L011); (d) WBC, the bytecode's binary format (sections 5.1 and 5.3).**

A1:

My suggestion is (a): templates and classes both need member access, which comes with it.

# END-OF-TO-DO
