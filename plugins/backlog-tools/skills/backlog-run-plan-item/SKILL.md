---
name: backlog-run-plan-item
description: Run one item of a Backlog import plan that the user pasted into the chat. Use whenever a message contains a line beginning "Backlog plan item `" — the marker backlog-import-plan writes into every entry — or a plan entry that opens with "Add the plan name `…` to this session's title", even when the paste comes with no request attached. Establishes first whether the item is still outstanding, so pasting the same item twice never redoes finished work.
---

# Run a Backlog plan item

The user copied an entry out of the Backlog app (or a plan document) and pasted it here.
Recognize it, decide whether it still needs doing, and only then do it. The pasted text is
the whole input for now: consulting Backlog for the plan's other items and reporting the
item's status back is planned, not built — do not attempt either.

Read `../backlog-import-plan/assets/backlog-import-grammar.md`: `## Plan item marker` is the
marker's exact shape, `## Sub-item conventions` what the item's `##` headings mean.

## Workflow

1. **Parse.** Title is the first line (`# ` optional). A backtick metadata line, when
   present, wins for `id:`, `#tag`, `repo:` and `after:`; otherwise read them off the
   marker. An entry carrying only the session-name line yields the tag alone — say so and
   continue. The body is the instructions; `## Setup:`, `## Manual:`, the knowledge/devbook
   reminder and `- [ ]` lines are its steps.
2. **Session and place.** Add the plan tag to the session title where the host allows it.
   Confirm the current repository is one the item names; if not, stop and say which it wants.
3. **Still outstanding?** Never assume it is. Gather evidence in this order and stop at the
   first conclusive answer:
   - this session already ran this `(tag, id)` — report that and stop;
   - `git fetch`, then search commits, branches and pull requests (open and merged) for the
     id and the tag — a session named for the plan usually left one behind;
   - derive done-criteria from the instructions and check the working tree for them: the
     files, behaviour or text the item asks for.
   Verdict **done** → report the evidence and stop, no work. **Partly done** → list what
   remains and do only that. **Not started** → continue. Unsure → ask before touching anything.
4. **Prerequisites.** Look for the same evidence for every `after:` id. One missing → warn
   and ask whether to proceed; never silently build on a step that has not landed.
5. **Do it.** `Setup:` sub-items first, in order. Then the instructions, exactly the way the
   repository's own instructions say work is done there — its orchestration gate, review and
   validation rules; this skill adds no execution path of its own. `Manual:` sub-items are
   the user's: list them, never attempt them. Tick `- [ ]` lines as they land.
6. **Close.** The knowledge/devbook reminder is a real step — do it through the repository's
   knowledge skills. Then report `(tag, id)`, what was done, the evidence behind anything
   skipped as already done, the `Manual:` steps still open, and that the item's status in
   Backlog has to be set by hand until the feedback loop exists.

## Never

- Redo an item the evidence says has landed: re-pasting is expected, duplicating work is not.
- Execute a `Manual:` step, open a pull request, or mark anything done in Backlog unasked.
