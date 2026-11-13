# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [WORKING] What Comes After M0.8

The roadmap ends with M0.8 (specification, section 9). M0.7's language is implemented, but for `foreach`, which waits for collections; M0.8's CI and portability checks are in place. The specification does not say what comes next, and each candidate needs syntax or a format it does not give yet, which I would propose here, feature by feature, before building it. Added by Claude, 2026-11-13, from the roadmap (`Documents/implementation.md`, section 13).

**Q1: Which comes next? (a) collections: arrays, maps and sets (section 3.2), with indexing, `length` and member access (section 3.3), and `foreach` (section 3.5); (b) `.wsp` templates: includes, layouts and parameterized components together (WS-T001 to WS-T008); (c) classes, `dispose()` and `using` (sections 3.2 and 3.4, WS-L011); (d) WBC, the bytecode's binary format (sections 5.1 and 5.3).**

A1:  (a), then (c), then (b), then (d).

My suggestion is (a): templates and classes both need member access, which comes with it.

# END-OF-TO-DO
