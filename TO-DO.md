# TO-DO WebLingo

> **IMPORTANT**: Follow the instructions found in [`TO-DO-INSTRUCTIONS.md`](TO-DO-INSTRUCTIONS.md).

## [IDLE] M0.13: WLBC

WLBC, the bytecode's binary format, its writing and its reading, with verification (specification, sections 5.1, 5.3 and 5.5). Its encoding choices are proposed in the specification and not yet approved, and the format is the chosen VM's, so this waits for the choice between the VMs (WL-BC002) as well. Added by Claude, 2026-11-13.

2026-11-13: waits for M0.11.

2026-11-13: M0.13 now, after M0.12, templates (specification, section 9); waits for M0.12.

2026-11-14: M0.12 is done; still waits for the choice between the VMs (WL-BC002), which waits for an I/O-bound workload, the last of those "Both VMs, for now" waits for ("I/O-Bound Workload", below).

2026-11-15: the I/O-bound workload is done; waits for the owner's choice between the VMs ("The Choice Between the VMs (WL-BC002)", below).

2026-11-15 (Claude): the owner kept both VMs, and has M0.13 go ahead now, a format for each VM's bytecode in one container (A1 and A3 of "The Choice Between the VMs (WL-BC002)"). The specification proposes the container (section 5.3), none of it approved, and does not say what uses it. Guessed, for now: a `.wlbc` holds a whole program as the compiler joins it, its static imports included, the native modules and dynamic imports it names found when it runs, as from source; its fixed-size fields little-endian; the files its source positions name written as the compiler was given them, relative to the program's folder, never as the host's own paths; a file of a version its reader does not know rejected, as WL4001. Five decisions, each answerable with a letter.

Q1, what uses WLBC in M0.13: (a) the command line: `weblingo compile script.wl` writes `script.wlbc`, `weblingo run script.wlbc` runs one, and `weblingo disasm script.wlbc` shows one as text, commands section 8.4 lists for later; (b) nothing but its tests yet: the format, its writer and its reader, with verification, the command line and a host's cache coming with hosting.

A1: (a), but also `weblingo run script.wl` should work without saving any  `.wlbc` on disk.

Q2, which VM's bytecode a `.wlbc` holds: (a) one, its header saying which, the compiler writing the one it is asked for (`--vm stack`), a VM refusing the other's; (b) either or both, each in a code section of its own, each VM taking its own.

A2: (a)

Q3, the container as section 5.3 proposes it (a 32-byte header with the magic `WLBC`, a major and a minor version, flags, the sections' count, the directory's offset and the file's length; a directory of 32-byte entries; sections for constants, functions, code, classes and modules, exception tables, source positions and integrity; operands in LEB128, strings in UTF-8, binary64s as their 8 bytes): (a) approved, and frozen as version 1.0, every later change a new version; (b) built as proposed, but kept Proposed, free to change without a new version until the owner freezes it.

A3: (b) Nothing is frozen until we are done. All decisions are preliminary. Don't get hung up on formalities.

Q4, a jump's target in a file: (a) a byte offset, as section 5.3 proposes, turned into the instruction's index when read; (b) the instruction's index, as in memory, since both VMs decode a function before they run it, so that nothing is turned.

A4: (b)

Q5, protecting a file against damage, which the verifier does not catch where it leaves the code valid (a flipped constant or string): (a) a CRC-32 of each section, in its directory entry, checked when the file is read; (b) a SHA-256 of the file, which a cache may key by too, through the platform's cryptography (section 7.10); (c) none yet.

A5: (a)

2026-11-16 (Claude): the answers recorded in the specification (sections 1.1, 5.1, 5.3 and 9), A3's "nothing is frozen until we are done" among them. Split into two parts, which the specification defines (section 9, "M0.13 parts"): "M0.13.1: WLBC's Format" and "M0.13.2: WLBC on the Command Line", below. Waits for them.

## M0.13.2: WLBC on the Command Line

`weblingo compile [--vm stack]`, `weblingo run` of a `.wlbc` on the VM its header names, and `weblingo disasm`, with a disassembler of the stack bytecode; the native modules a `.wlbc` names loaded when it runs; its files named from the program's folder; and `weblingo run` of a `.wl` writing no `.wlbc` (specification, section 9, "M0.13 parts", part 2; A1 and A2 of "M0.13: WLBC"). Added by Claude, 2026-11-16.

# END-OF-TO-DO

## [DEFER] Serialization: Text That Rebuilds a Value

What `write` writes is for people to read, and does not reliably rebuild a value. Writing a classed value as if it were a call (`Celsius(21.5)`, `Stack([1, 2])`, "Classes of Values, Extensions") reads back as the same value only when the class's constructor takes the held value as it is; a class with a constructor of its own (`constructor(fahrenheit) { super((fahrenheit - 32) * 5 / 9); }`) would make something else from it. Discussed with the owner, 2026-11-13; added by Claude the same day.

If text that rebuilds a value (round-trip text) is wanted, it is a serialization feature: a way to write a value, and to read it back into an equal one, that does not depend on its class's constructor (a rebuilt instance gets its fields set directly, as a runtime error's `Error` already does, specification, section 15.2, "Exceptions"). It deserves a design of its own, rather than borrowing call syntax for `write`'s text. Such a design would decide, among other things: its format (JSON, a WebLingo literal form, or both); how a class is named in it, and found again when it is read (a class in another module, or one renamed since); whether a class may shape its own serialized form, as `toString()` shapes its text; what cannot be serialized (functions, closures, tasks, native resources) and what reading one then does; cycles and shared references; and the security of reading text from outside (no code runs, and no class is made, that the reader did not allow).

It is deferred: nothing needs it yet, and it can be added later, if it proves necessary, without changing `write`'s text or any program.
