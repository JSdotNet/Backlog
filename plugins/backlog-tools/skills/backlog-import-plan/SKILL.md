---
name: backlog-import-plan
description: Turn an agreed specification (a .domain feature, a .backlog item, an ADR, or other planning material) into a Backlog import plan — an ordered, dependency-linked sequence of entries in Backlog's entry-text grammar, ready to paste or upload — together with a review view of it, always.
disable-model-invocation: true
---

# Build a Backlog import plan

A one-shot handoff: the spec is settled and it is time to generate the next batch of AI
prompts for the Backlog app to import. This skill reads the agreed material, writes one
Backlog import plan document plus a review view of it, and stops — it never talks to the
Backlog app, executes a generated prompt, or touches GitHub.

Read `assets/backlog-import-grammar.md` before writing anything. It carries the exact
entry/sub-item shape, every metadata token and its values, and the plan-identity/re-import
mechanics; do not invent syntax beyond it.

## Inputs

- **Source material.** Path(s) to the agreed `.domain` feature chapter, `.backlog`
  Epic/Story, ADR, or — if nothing is written down yet — the planning notes given inline.
- **Plan subject.** What the batch delivers; derives the shared `#tag`.
- **Target repositories.** One or more repository names the prompts target. Do not check
  whether a name is already registered in Backlog — Import auto-registers an unknown one.
- **Output.** A file path, or "paste it here" — ask if neither is stated. The review view
  (step 7) is produced either way; it is not an option.

## Workflow

1. Read the source material in full, following any `depends-on`/prerequisite references it
   names, so ordering is grounded in what is agreed rather than guessed.
2. Derive the plan's `#tag`: one slug from the plan subject. Reuse the exact same slug if
   this plan is later regenerated, so Backlog's re-import recognizes it as a new version
   of this plan — clearing the entries nobody has started yet — instead of a second plan.
3. Break the work into ordered entries, one per unit of work. Every entry is exactly one
   of two kinds, decided by who does it — see the grammar's `## Entry kinds`:
   - **`prompt`** — work an AI session runs.
   - **`task`** — work only the user can do: a decision, a sign-off, an action in an
     account or on a machine the AI cannot reach.

   Never combine the two. A manual step is never a sub-item, checklist line or body
   instruction inside a prompt, and a task never carries instructions for an AI. When one
   unit of work needs both, split it: the manual step becomes its own `task` entry, and
   every prompt that needs it done waits on it with `after:`.

   For each **`prompt`** entry, in this order:
   - **Marker first.** One opening body line in the exact shape of the grammar's
     `## Plan item marker`, restating the entry's `id`, the plan tag, every `repo:` and every
     `after:` — Backlog's copy button drops the metadata line, so this is how an entry pasted
     back out of the app is still recognized and run by `backlog-run-plan-item`. Every prompt
     carries it; write the metadata line first and derive the marker from it, so the two
     never disagree.
   - **Session name second.** One body line telling whoever runs the prompt to add the plan
     name to their session's title, so every session spawned from this plan is recognizable
     as belonging to it: ``Add the plan name `<tag>` to this session's title before you
     start.`` Every prompt carries it, worded the same way.
   - **Instructions next.** The rest of the body — concise, no padding — is the entry's
     primary content.
   - **Setup sub-items.** A `##` sub-item per repository prerequisite the instructions
     assume (installing a plugin, updating one, wiring a related change), ordered ahead of
     everything else in the entry, titled `Setup: ...`.
   - **Knowledge/devbook reminder.** One more `##` sub-item reminding whoever runs the
     prompt to update the target repository's own knowledge folders or devbook once it is
     done. Every prompt carries this; never skip it.

   For each **`task`** entry: a title naming what the user does, then a body addressed to
   the user — what to do and what "done" looks like, concise. No marker, no session-name
   line, no `Setup:` or knowledge sub-item: those direct an AI session, and a task has
   none. `- [ ]` checklist lines are fine for the user's own steps.

   The **metadata line** of every entry, either kind: the type — `prompt` or `task`, never
   a third value; always `!ready`, on every entry whatever its place in the chain — order
   is carried by `after:`, never by holding a later entry at `!draft`; always an
   `effort:<points>` estimate off the 1/2/3/5/8/13/21 scale, sized from the instructions
   the entry actually carries; `repo:<name>` once per target repository (a task carries it
   only when the step is done in or to that repository); `id:<slug>` on every entry — a
   stable slug from its title, reused verbatim when the plan is regenerated, since it is
   how Backlog recognizes an entry already under way or already finished; `after:<id>`
   once per prerequisite, including across repositories and across kinds; the shared
   `#tag`; and only the `*priority`, `@area` or `due:` the source material actually
   implies.
4. Close every plan with two entries, in this order and last in the document. Never omit
   either, however small the plan.
   - The **review prompt**, titled `Review the <plan subject> plan for anything missed`. It
     carries `id:review-plan`, `after:` each entry nothing else depends on — the plan's
     leaves, which is every entry transitively, tasks included — and `repo:` once per
     repository the plan targeted. Its body asks whoever runs it to read the source material
     against what actually landed: every entry done, and nothing dropped, deferred or left
     half-finished along the way; anything still outstanding is written up as a new entry
     rather than noted and forgotten. It opens with the marker and session-name line like
     every other prompt, but carries no knowledge/devbook reminder — it changes no
     repository of its own.
   - The **sign-off task**, titled `Sign off the <plan subject> plan`. It carries
     `id:sign-off-plan`, `after:review-plan`, `effort:1` and no `repo:`. Its body asks the
     user to read the review's outcome and confirm the plan is complete, or pick up the
     follow-up entries it wrote.
5. Assemble the entries into one Markdown document per `assets/backlog-import-grammar.md`.
6. Produce the output: write it to the given path (default `<plan-slug>-import-plan.md` in
   the current working directory) when a file was asked for or implied, and show the full
   text inline either way so it is ready to paste directly.
7. Build the review view, always, next to the raw plan: copy `assets/plan-review.html`,
   replace `{{PLAN_TITLE}}` with the plan subject as a short name (e.g. `VS Code desktop
   rollout plan`) and `{{PLAN}}` with the plan text verbatim (it must not contain
   `</script>`), and change nothing else. Publish it as an artifact when the host offers
   one — a private page whose link the user can open beside the raw text; otherwise write
   it beside the plan as `<plan-slug>-import-plan.html` and say to open it in a browser.
   The page parses the embedded plan itself, so the view can never disagree with the raw
   text; it shows the checks of `## Output expectations`, the dependency order as a
   diagram, and every entry with its boilerplate folded away, with the raw plan on a
   second tab. Its **Checks** mirror `## Output expectations`, so the reviewer sees at a
   glance what the plan gets wrong; when the user reports a failed check, fix the plan and
   regenerate the view rather than patching the view.
8. Report the output location (if written), the review view's link or path, the entry
   count, the repositories targeted, and the dependency chain. Stop — do not open the
   Backlog app, run a prompt, or create a pull request.

## Output expectations

- One Markdown document; every entry's body precedes its `##`/`- [ ]` sub-items.
- Every entry is `prompt` or `task` and states `!ready`, an `effort:`, an `id:`, and the
  plan's shared `#tag`; every prompt also states `repo:` and opens with the marker line,
  then the session-name line.
- No prompt contains a manual step in any form — no `Manual:` sub-item, no "ask the user
  to…" instruction — and no task contains instructions for an AI.
- `after:` correctly expresses the plan's dependency order, including cross-repository
  dependencies and prompts that wait on a task.
- The last two entries are the plan review, waiting on every leaf of that order, and the
  sign-off task waiting on the review.
- A review view built from `assets/plan-review.html` accompanies the plan every time, and
  none of its checks fail.
- No file changes outside the produced plan document and its review view; no call to the
  Backlog app or GitHub.

## Reference

- `assets/backlog-import-grammar.md`
- `assets/plan-review.html` — the review view template; its checks mirror the grammar.
