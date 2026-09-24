---
name: backlog-run-plan-item
description: Run one item of a Backlog import plan that the user pasted into the chat. Invoked directly by the line the Backlog app puts on every entry it copies — "/backlog-tools:backlog-run-plan-item entry `<id>`:" with the entry under it — and otherwise whenever a message contains a line beginning "Backlog plan item `", the marker backlog-import-plan writes into every prompt entry, or a plan entry that opens with "Add the plan name `…` to this session's title", even when the paste comes with no request attached. Establishes first whether the item is still outstanding, so pasting the same item twice never redoes finished work.
---

# Run a Backlog plan item

The user copied an entry out of the Backlog app (or a plan document) and pasted it here.
Recognize it, decide whether it still needs doing, and only then do it. When a `backlog`
MCP server is in the live tool list, the entry's status is read from it and reported back
to it; when it is not, the pasted text is the whole input and nothing is reported.

Read `../backlog-import-plan/assets/backlog-import-grammar.md`: `## Plan item marker` and
`## Entry marker` are the two markers' exact shapes, `## Sub-item conventions` what the
item's `##` headings mean.

## Workflow

1. **Parse.** Invoked as `entry `<id>`:`, the argument is the entry's stored id — the one
   the connector is asked for — and the text under it is the entry. Title is its first line
   (`# ` optional). The entry's metadata line is only a backtick line directly under the
   title; one under a `##`/`###` heading is that step's status line, and its `task` word
   says nothing about the entry — the app's copy drops the entry's line, so an older paste
   can end on a step's. With no entry metadata line, a body with the marker or session-name
   line is a prompt. The entry's metadata line, when present, wins for `id:`, the plan tag
   (`+tag`; a legacy plan may still carry it as `#tag`), `repo:` and `after:`; otherwise
   read them off the plan-item marker; with the connector, `read_item` answers them too. An
   entry carrying only the session-name line yields the tag alone — say so and continue.
   The body is the instructions; `## Setup:`, the knowledge/devbook reminder and `- [ ]`
   lines are its steps. A `task` or `test` type on the metadata line, or from `read_item` — or a body
   with neither marker nor session-name line — is the user's own work, not a prompt: say
   so and stop.
2. **Session and place.** Add the plan tag to the session title where the host allows it.
   Read `repository` off the git remote (`owner/name`); every connector call carries it.
   Where the item names a repository, confirm it is this one; if not, stop and say which.
3. **Still outstanding?** Never assume it is. Gather evidence in this order and stop at the
   first conclusive answer:
   - this session already ran this `(tag, id)` — report that and stop;
   - with the connector: `read_item` for the entry's status — `done`/`archived` is done,
     `in-progress` means look for the partial work below;
   - without it: `git fetch`, then search commits, branches and pull requests (open and
     merged) for the id and the tag — a session named for the plan usually left one behind;
   - derive done-criteria from the instructions and check the working tree for them: the
     files, behaviour or text the item asks for.
   Verdict **done** → report the evidence and stop, no work. **Partly done** → list what
   remains and do only that. **Not started** → continue. Unsure → ask before touching anything.
4. **Prerequisites.** Look for the same evidence for every `after:` id. One missing → warn
   and ask whether to proceed; never silently build on a step that has not landed. Without
   the connector, an `after:` id that names a `task` or `test` the user does leaves no trace in the
   repository — ask whether it is done instead of searching for it.
5. **Do it.** With the connector, `transition` the entry Ready → In progress first. Then
   `Setup:` sub-items in order, then the instructions, exactly the way the repository's own
   instructions say work is done there — its orchestration gate, review and validation
   rules; this skill adds no execution path of its own. Tick `- [ ]` lines as they land.
6. **Answer the notes the item leaves open.** With the connector, `list_annotations` for the
   repository; a chapter this item rewrote may carry a remark the change now answers. For
   each open note: work out the answer, then write it into the chapter with the devbook
   plugin's own `annotations.mjs` — `add` puts the fence under the block the note's
   `BlockIndex` names (`--after` the block's text, which becomes its `quote`; `--author` the
   person, `--date` the note's date), `reply` adds the answer, `resolve` closes the fence —
   and only then `resolve_annotation` for that id. Fence first, resolve second. Name the
   changed chapter files and offer the commit; never push. `backlog-answer-notes` holds the
   full procedure and is the skill to invoke when notes are the whole job.
7. **Close.** The knowledge/devbook reminder is a real step — do it through the repository's
   knowledge skills. Report `(tag, id)`, what was done, and the evidence behind anything
   skipped as already done. With the connector, `transition` In progress → Done once the
   pull request is open; without it, say that the item's status in Backlog has to be set by
   hand.

## Never

- Redo an item the evidence says has landed: re-pasting is expected, duplicating work is not.
- Run a `task` or `test` entry, open a pull request, or mark anything done in Backlog unasked.
