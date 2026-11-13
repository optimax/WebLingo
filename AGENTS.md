# Agent Instructions

## Purpose

This file holds the general rules for coding agents working in this repository: how to start and end a session, how to change source, how to verify and deliver changes, and when to ask. Nothing in it is specific to this repository's project, so that the same file can serve any project.

What is specific lives beside the code:

- `Documents/implementation.md` describes the project as a whole: what it is, its documents, its layout and projects, its development environment, its design decisions and conventions, how it is built and tested, its known traps, and the choices not to change without asking.
- An `implementation.md` in a project's folder, where there is one, describes that project: its conventions, the user's requirements for it, and how it is built.
- `HANDOFF.md` at the root, and in a project's folder where there is one, records the current state of the work and its next steps.
- `TO-DO.md` holds the tasks in order of priority, worked as `TO-DO-INSTRUCTIONS.md` describes; `DONE.md` holds the tasks that are done.

Every coding agent working on this repository should read this file completely before modifying the project.

The current source tree is authoritative regarding what is actually implemented. The design documents that `Documents/implementation.md` names are authoritative regarding what was decided. If documentation and source disagree, identify the conflict rather than silently guessing which is correct.

------

# 1. Starting an Agent Session

Before making changes:

1. Read this `AGENTS.md` completely.
2. Read `Documents/implementation.md`, and the `implementation.md` of each project you will work on, if present.
3. Read the root `HANDOFF.md`, and the `HANDOFF.md` of each project you will work on, if present.
4. Read the parts of the design documents that bear on the task, as `Documents/implementation.md` describes.
5. Inspect the current solution and project structure.
6. Inspect the current source files involved in the request, and related implementations elsewhere in the solution where useful.
7. Inspect the relevant tests before changing behavior.
8. Build the current baseline when practical.
9. Treat the current source tree as authoritative over previous chats and stale notes.
10. Do not redo work that a `HANDOFF.md` or `DONE.md` identifies as completed.

Do not begin implementing a fix based solely on a previous agent's description of the code.

------

# 2. General Principles

## 2.1 Keep It Lightweight

Do not introduce large frameworks, dependency-injection containers, or other architectural layers merely for architectural purity.

Introduce abstractions when they solve a real problem such as:

- code duplication,
- testability,
- reuse across components or applications,
- a clear separation of calculation from presentation,
- an architectural boundary that the design documents require.

Prefer straightforward, maintainable code over unnecessary abstraction.

## 2.2 Reuse Existing Infrastructure

Before implementing functionality, inspect the rest of the solution for an existing implementation.

Common functionality should behave consistently where appropriate. Do not independently reinvent functionality that already exists elsewhere in the solution without a reason.

## 2.3 Separate Calculation From Presentation

Where practical, calculations should not depend on how their results are presented: on UI objects, on a host, on output formatting. For example, a layout calculation should produce logical placement information independently of rendering.

This improves:

- testability,
- reuse,
- debugging,
- future presentation, export or hosting capabilities.

Do not force this separation where it adds complexity without practical benefit.

------

# 3. Decisions

The design of a project, and who decided what, is recorded in its design documents, which `Documents/implementation.md` names along with how they mark the status of a decision.

- Implement what the owner has approved. Do not change an approved decision, or work around it, without the owner's explicit approval.
- Do not treat a proposal, a recommendation, or a deliberately open question as decided: not in code, in tests, in comments, or in documents. Where a task needs one settled, ask (`TO-DO-INSTRUCTIONS.md` says how). Until it is answered, build so that the choice stays easy to change, and say which choice you made.
- Where the design documents are silent and the work needs a decision that users or other components will depend on (semantics, file and wire formats, protocols, public interfaces), do not invent it: ask. Record a choice made only to unblock work as a proposal, where `Documents/implementation.md` says.
- When the owner approves a decision, record it in the design documents in the same change, with the date.

------

# 4. Version-Sensitive Technologies

Do not assume APIs or syntax from older framework or library versions. `Documents/implementation.md` names the versions in use.

For version-sensitive technologies (frameworks, languages, UI toolkits, test frameworks and their runners, wrappers of native libraries, installer toolsets, CI services, IDE tooling), verify that the proposed syntax applies to the version actually used by the project.

Before using a library's API, inspect the actual package and version used by the project rather than assuming an API from another version or from a similar library.

Do not confuse types that share a name or a purpose across libraries.

------

# 5. Behavior Users Rely On

## Interfaces

Whatever users or other programs rely on is part of the project's contract: commands, options, output and exit codes of a command line; public APIs; file and wire formats; error codes and messages that others may match; keyboard shortcuts; settings and their formats.

Before adding to one of them, inspect what exists for conflicts. Before changing one, ask.

## Settings

Avoid introducing additional settings files when settings naturally belong to an existing configuration.

Settings may be externally edited. Preserve existing reload/watch behavior where implemented.

## User Data

Never allow unsaved user data, such as a dirty editable document, to be silently discarded unless that behavior has explicitly been requested.

When closing a dirty document:

- Save should save and continue the requested operation.
- Don't Save/No should discard changes and continue the requested operation.
- Cancel should abort the requested operation.

## Consistency

Components and applications of one solution or suite should look and behave as related.

When introducing a user-visible element, inspect corresponding implementations elsewhere in the solution, and prefer consistency with them. Do not copy blindly if the workflow requires different behavior.

## Licensing

Respect each product's licensing as `Documents/implementation.md` records it. Do not add license text, license files, license headers, or license metadata that a product does not have, including when copying code from a product licensed differently. Rule 10 (section 9) covers third-party notices.

------

# 6. Text, Platforms and Culture

Unless `Documents/implementation.md` says otherwise:

- Code that reads text should work equally with Windows (CRLF), Linux and Unix (LF only), and old Mac (CR only) line endings, and should not depend on the platform's line ending (`Environment.NewLine` and the like).
- Code meant for more than one platform must not depend on one platform's path separator, file-name case rules, or drive letters.
- Parsing and formatting of data must not depend on the current culture.

Be especially careful when translating between different measures of a position in text: string indices, byte offsets in an encoding, Unicode scalar values, user-perceived characters, and line and column numbers.

------

# 7. Testing

Run tests as `Documents/implementation.md` describes. Do not assume a test framework's or runner's older behavior, adapters, or command-line conventions without checking the current project configuration.

- After a change, build and run the tests of what the change affects. Run targeted test classes during active development, and the complete test suite (every test project in the solution, not only the one nearest the change) before a task is done, when a change touches shared code, and at milestones.
- Report failures accurately.
- Do not change tests merely to make a regression appear to pass. When a test fails because the intended behavior has deliberately changed, update the test only after confirming that the new behavior is correct.
- When fixing a bug, consider whether an automated test is practical. Tests are particularly valuable for calculations, parsing of input, serialization, formats, and anything independent of a UI.
- A test of bad input checks the error it reports, not only that it failed.
- Tests must not depend on the machine they run on: its paths, its line endings, its culture, its screen size, or its speed. Where the project runs on more than one platform, tests must pass on each.
- Do not introduce fragile UI automation merely to claim test coverage.

------

# 8. Regression Discipline

A small change can affect apparently unrelated behavior, even in a different component or application.

Before modifying behavior, inspect all applicable layers. When changing code, consider regression effects on what the project's users and its other components rely on (section 5). `Documents/implementation.md`, and a project's own `implementation.md`, list further areas specific to them.

Do not stop after verifying only the originally reported symptom.

------

# 9. Source Modification Rules

These rules are mandatory for coding-agent work.

## Rule 1 — Verify Version-Sensitive Syntax

Check APIs and syntax against the actual framework and package versions the repository uses (section 4).

Do not provide obsolete syntax and correct it later.

## Rule 2 — Identify File and Method

For every code change delivered directly to chat, explicitly identify:

- the exact file path,
- the class where useful,
- the exact method being modified.

## Rule 3 — Complete Replacement Methods

When an existing method must be modified interactively in chat, provide the complete replacement method.

Do not provide fragments with instructions such as:

> Add this after line X.

unless explicitly requested.

## Rule 4 — Keep Code Lines Intact

Do not unnecessarily wrap generated source lines.

Keep generated lines of code to less than approximately 125 characters.

## Rule 5 — One Considered Implementation

Validate the approach before presenting it.

Provide one considered implementation for each development step rather than proposing code and immediately revising it.

## Rule 6 — Inspect Current Source

Always inspect the current source before making changes.

Do not assume the source matches an earlier conversation, ZIP, or handoff.

The user may introduce changes even while you are working on something, so always monitor the files you are changing.

## Rule 7 — Minimal Changes

Change only the files necessary for the requested work.

Avoid unrelated:

- refactoring,
- formatting,
- renaming,
- cleanup,
- dependency changes.

Do not turn a focused change into a broad refactoring exercise. Feel free to suggest code improvements and refactorings, but first complete the task at hand, then present them and wait for approval before making them.

## Rule 8 — Preserve Existing Behavior

Do not remove or simplify existing features or contracts merely to make a new feature easier to implement. Always ask for the rationale behind something that may seem like overkill or overengineering: it may exist for a decision already made.

## Rule 9 — Do NOT Clobber Changes by Others

Be aware that other agents or the user you interact with may be making changes concurrently to you.

Sometimes these changes may conflict with what you are trying to do. Never blindly overwrite files that have changed since you last read them. Attempt to reconcile the differences and come up with a consistent merge if possible. Otherwise alert the user, describing the problem, proposing a solution or solutions, and let them decide what to do.

## Rule 10 — Keep the Third-Party Notices Current

Whenever a third-party package that ships with a product is added, upgraded or removed (its own dependencies included), update the project's third-party notices or dependency inventory in the same change: the component, its version, which product uses it, and its license as it is at that version. `Documents/implementation.md` says where they are kept; where the project has none yet, ask before creating one.

------

# 10. Deliverable Rules

When returning source changes as a ZIP:

1. Include only changed files unless a complete solution was explicitly requested. Files to be deleted should also be present in the archive with `_DELETE` appended to their file name (before the extension, so that the extension does not change).
2. Preserve the complete folder hierarchy in the archive being delivered.
3. Include the **repository's root directory itself** in the archive.

For example, for a repository named `Solution`:

```text
Solution.202609171130.zip
└── Solution/
    └── Project/
        ├── ChangedFile.cs
        └── AnotherChangedFile.cs
```

Do **not** produce:

```text
Solution.202609171130.zip
├── ChangedFile.cs
└── AnotherChangedFile.cs
```

The root directory is deliberately included so that the archive can be extracted over the working tree safely.

Exclude generated, build and repository directories unless explicitly required:

- `bin`
- `obj`
- `publish`
- `TestResults`
- `.vs`
- `.git`
- `.claude` (Claude Code's local settings and its worktrees, whole copies of the repository)

------

# 11. Build and Verification

After modifying source, build the relevant project whenever the environment permits. Prefer building the complete solution when changes could affect shared code or several components.

Run tests appropriate to the affected behavior (section 7).

Do not suppress a compiler or analyzer warning (`#pragma warning disable`, `NoWarn`, and the like) to get a build through without saying so and why.

Report separately:

- compiler errors,
- compiler warnings,
- test failures.

Do not claim a successful build unless the build was actually run and was successful.

Do not claim that something works on a platform unless it was built and tested on that platform. If only one platform was tried, say so.

Do not claim that interactive behavior, or a program's behavior from the command line, was tested unless the program was actually exercised.

If only compilation and automated tests were performed, say so. If testing could not be performed, state that explicitly.

------

# 12. Lessons Learned and Known Traps

Record durable lessons where they apply: a trap of the project or of a technology it uses in `Documents/implementation.md`, a trap of one component in that component's `implementation.md`, and a trap of working in any repository here in this `AGENTS.md`.

### IDE Build Directories

An IDE may recreate `bin`, `obj`, or similar directories immediately after a cleanup script removes them. The reappearance of those directories does not necessarily mean the cleanup command failed.

### File Locks

Antivirus scanning, Explorer preview handlers, an IDE, a test host, or a still-running program may temporarily lock files, built assemblies and documents included. Consider file locking before modifying code to work around what appears to be a filesystem failure.

------

# 13. Things Not to Change Without Asking

Do not make the following changes merely because another architecture seems preferable:

- decisions the owner has approved (section 3)
- the language, UI framework, or other platform a product is built on
- the target framework and the target platforms or architectures
- major dependencies, and the introduction of major frameworks or dependencies
- the solution file format and the shared build settings
- the test framework
- the installer and deployment technology
- interfaces users or other programs rely on (section 5): commands and their options and exit codes, public APIs, keyboard shortcuts
- persistent settings formats and existing public file formats
- product and program names
- licensing
- broad redesign of the user interface or of the architecture

`Documents/implementation.md` names the current choices and may add to this list.

Ask before making changes with significant architectural impact.

------

# 14. Ending an Agent Session

Before handing work to another agent, update the `HANDOFF.md` of the project you changed. Update the root `HANDOFF.md` when the work affects shared code, the build, the tests, the scripts, or more than one project, or when there is no project-level one.

The handoff should record:

- current objective,
- work completed,
- exact files changed,
- important implementation decisions,
- build result,
- test result,
- unresolved problems,
- known regressions,
- recommended next step.

Do not turn a `HANDOFF.md` into a chronological development diary.

Remove obsolete information as work progresses so that the document describes the **current state of development**, not the complete history of the project.

Update an `implementation.md` when the work changes a convention, requirement, decision, or implementation detail it records, or adds one that the next agent needs to know: `Documents/implementation.md` for the project as a whole, a component's own for that component. An `implementation.md` describes how things are and why, not their history. Record approved design decisions in the design documents (section 3).

Keep this `AGENTS.md` generic: nothing in it may be specific to one project. What is specific goes into an `implementation.md`. Always alert the user to any changes you make to `AGENTS.md`.

## Referring to Work

Refer to a numbered or named grouping of work, such as milestones, increments, slices, phases, steps, batches, or chains of branches, only where a permanent document defines it, and name that document and its section. A permanent document is one kept in the repository for its own sake, such as a design document, a specification, or an `implementation.md`. A task's text in a to-do or done list is not one, since it moves or goes away. If a grouping is useful, write it into a permanent document first.

Otherwise, refer to work by what can be found later:

- a commit by its hash;
- a branch by its name, only while the branch exists;
- a task by its title;
- a document by its path and section;
- queued work by what it waits for.

This applies to everything left for others to read: to-do and done lists, `HANDOFF.md` and `implementation.md` files, design documents, commit messages, and messages to the user and to other agents.

------

# 15. Suspending an Agent Session

When running autonomously on a series of tasks and the current session reaches 95% of either the weekly or the hourly limit, preserve enough information to continue after the pause, then pause it safely, so that the limits are not breached, unless the user explicitly directs otherwise. Wait for the user to resume the session after the limits have been lifted.

------

# 16. Committing Changes to git

When working on an assigned task, work in a git branch specific to that task, with a name starting with `claude/`. Commit changes to git when you are done with a task. `TO-DO-INSTRUCTIONS.md` says how a finished task's branch lands in `main`.

When you commit your changes, collect all the files you worked on and commit them to the **local repository only**. **You are allowed to push ==only== if the user explicitly instructs you to.**

When committing your changes to local, identify yourself as Claude@<machineName> with email claude+<machineName>@anthropic.com (or the appropriate agent name, if you are not Claude). <machineName> is the local machine name. Give the identity to the commit itself (`git -c user.name=… -c user.email=… commit`) rather than changing the repository's git configuration.

Do not put the signature

​    Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>

(or anything similar) in the commit comments themselves.

------

# 17. Data Access

You must never access network or removable drives without explicit authorization. Only access local hard drives.

------

# 18. Authority

When instructions conflict, use the following priority:

1. The user's explicit instruction in the current task.
2. A project's own `implementation.md`.
3. `Documents/implementation.md`, and the decisions the owner approved in the design documents it names.
4. This `AGENTS.md`.
5. `HANDOFF.md` files, for current-state information.
6. Existing conventions inferred from source.

The current source tree remains authoritative regarding what is actually implemented.

When uncertain about a design decision that could materially affect existing behavior, ask before making the change.
