# TO-DO Instructions for Agents

The file [`TO-DO.md`](TO-DO.md) contains tasks that are to be performed top-to-bottom, in order of their priority. Only when I tell you to, process and implement all TO-DO items that are not marked DONE, DEFER, STOP, IGNORE, or QUESTION, one by one.

What these instructions leave to the project, such as the checks a finished task must pass and where new tasks come from, is in `Documents/implementation.md`.

---

**IMPORTANT**: Do not stop mid turn. Continue until you have resolved all tasks in the TO-DO, or until there are no more tasks that are still open. Monitor your own actions and **absolutely make sure the user never waits for an update from you longer than 5 minutes**. A user interrupt without any specific message means "Report now and continue working."

---

Always start and re-start after completing each task from the top to pick up the highest priority tasks.

Markings described below go in at the beginning of a task heading, *before* rather than after the task's title in the TO-DO file.

As soon as you start ***considering*** a TO-DO item, mark it [WORKING]. Don't wait until you start making actual changes.

[WORKING] means that the task is being worked on right now: considered, designed, coded or checked, by you or by one of your subagents. A task whose work has stopped for the time being, because it waits to land in `main`, waits for main's checks, or is queued behind another task or behind a subagent busy with something else, is marked [IDLE] instead, with a dated line under it saying what it waits for. Mark it [WORKING] again when work on it resumes.

When you encounter a task marked [STOP], stop the execution (do not process this task) and wait for user commands.

A task marked [PAUSED] was started and then paused by the agent (for example, when the work had to stop for a move to another machine, or when the session reached its usage limits, `AGENTS.md`, section 15), not by the user: it is not done, and the agent must continue it. Treat it as an open task in its place in the order: mark it [WORKING] again when you take it up, and go on from where its notes in `TO-DO.md` say it stands. It is unlike [STOP], which the user sets to make the agent stop and wait.

When reading TO-DO, read it in its entirety and analyze which tasks are related, so that you don't make design decisions that you are going to regret further down the line. Understand that some tasks may be related to other tasks and choices you make for one may conflict with the best choices for a future task. Where `Documents/implementation.md` says that parts of the work build on one another, read what the design documents say about the later parts too before settling a data structure or a contract.

A task too large to land in one piece is split into tasks of its own, one per part, each placed in `TO-DO.md` in its order and each marked [DONE] when its part lands. Define the parts in a permanent document first (`AGENTS.md`, section 14, "Referring to Work"). The parent task keeps the user's requirement and points to that document and to its parts' tasks; it is marked [IDLE] while any of its parts is open, and [DONE] when the last one is done.

Do not hesitate to ask the user any questions when something is unclear, or when you are faced with a design decision. A task that needs a decision the design documents leave open, as a proposal, as deliberately undecided, or by not making it at all, needs the user's answer: do not settle it yourself (`AGENTS.md`, section 3). When you need to ask the user a clarifying question, do not pop a modal blocking dialog in the interactive chat. Ask it by placing it in `TO-DO.md`, underneath the relevant task description, formatted in **bold**, and continue with other TO-DOs in the meantime, while waiting for the user's answers. When an answer approves a decision, record it in the design documents as part of that task.

When working on a task, create and work in a temporary git branch off of `main`, named `claude/` followed by a name whose relation to the task is obvious (`AGENTS.md`, section 16).

Have at most four subagents at work at the same time. Every build and check shares the machine's cores and memory, and more work at once makes each task's checks slower, so finish and land work before starting more.

When you are truly done and complete with each task, commit that task's work, pull `main`, merge your branch into the main as a fast-forward (`git merge --ff-only`; if `main` has moved on in the meantime, first rebase your branch onto it with `git rebase main`, reconciling any conflicts), so that the history stays a straight line with no merge commits; then delete your working git branch, and mark that task [DONE] in the TO-DO itself. DO NOT mark the task [DONE] while you are still working on it — only when you are completely done with it! Pulling `main` updates the local `main` only: push nothing unless the user tells you to (`AGENTS.md`, section 16).

A task is done when its work has landed in `main` and main's checks have passed on it: the checks `Documents/implementation.md` names for a finished task, run again on `main`. Do not keep a landed task open for anything that comes up later, a follow-on from an answer to one of its questions included: make that a new task of its own, and mark the landed one [DONE].

Do not mark tasks that have active/unanswered questions on them as [DONE] — mark them as [QUESTION]. As long as the heading is marked a [QUESTION], do not try to ingest the answers or act on them — leave that task alone. The user is still actively editing it and the answer may still change. Once the user removes [QUESTION] from the task, it is considered answered. Make sure answered questions are not formatted bold any more.

Once a task is marked as [DONE], move it to the end of [`DONE.md`](DONE.md), removing it from `TO-DO.md`.

TO-DO list is a live document. Be careful to verify that `TO-DO.md` hasn't changed in the meantime before you make any changes to it, or undertake a new task. The user may be editing it and, for example, moving tasks that were marked done into `DONE.md`.

Monitor for TO-DO changes as you are working through a task. When you detect changes, re-read the TO-DO **from the beginning**, as some tasks previously marked as [DONE] may have been rejected, instructions may have changed, etc., and the highest priority tasks are always at the top.

After completing each task, run the checks `Documents/implementation.md` names for a finished task; where it names none, build the solution in Release mode and run its complete test suite. Say in the task's notes which checks could not be run, and on which platforms the work was not tried (`AGENTS.md`, section 11).

Proceed until the end, or until you encounter the line `# END-OF-TO-DO`, or `# STOP` whichever comes first. Do NOT work on TO-DO items beyond one of these markers.

## Proceed Autonomously

Continue generating tasks for what needs to be done next, without waiting for any approval or decision. Take them from where `Documents/implementation.md` says the project's planned work is recorded, in the order it gives, and turn the open questions recorded there into questions for the user when the work reaches them. Insert all the tasks you know you are going to follow, as many as you can reasonably predict at a time, into `TO-DO.md`, below the open tasks there and above `# END-OF-TO-DO`.

Make reasonable guesses about implementation details when needed, and say in the task's notes what you guessed. A guess never settles a decision the design documents leave open, nor changes one the owner approved (`AGENTS.md`, section 3): ask about those, as above, and continue with other work while waiting for the answer.

---
