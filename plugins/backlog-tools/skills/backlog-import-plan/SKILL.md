---
name: backlog-import-plan
description: Turn an agreed specification (a .domain feature, a .backlog item, an ADR, or other planning material) into a Backlog import plan — an ordered, dependency-linked sequence of entries in Backlog's entry-text grammar, ready to paste or upload.
disable-model-invocation: true
---

# Build a Backlog import plan

A one-shot handoff: the spec is settled and it is time to generate the next batch of AI
prompts for the Backlog app to import. This skill reads the agreed material, writes one
Backlog import plan document, and stops — it never talks to the Backlog app, executes a
generated prompt, or touches GitHub.

Read `assets/backlog-import-grammar.md` before writing anything. It carries the exact
entry/sub-item shape, every metadata token and its values, and the plan-identity/re-import
mechanics; do not invent syntax beyond it.

## Inputs

- **Source material.** Path(s) to the agreed `.domain` feature chapter, `.backlog`
  Epic/Story, ADR, or — if nothing is written down yet — the planning notes given inline.
- **Plan subject.** What the batch delivers; derives the shared `#tag`.
- **Target repositories.** One or more repository names the prompts target. Do not check
  whether a name is already registered in Backlog — Import auto-registers an unknown one.
- **Output.** A file path, or "paste it here" — ask if neither is stated.

## Workflow

1. Read the source material in full, following any `depends-on`/prerequisite references it
   names, so ordering is grounded in what is agreed rather than guessed.
2. Derive the plan's `#tag`: one slug from the plan subject. Reuse the exact same slug if
   this plan is later regenerated, so Backlog's re-import recognizes it as a new version
   of this plan — clearing the entries nobody has started yet — instead of a second plan.
3. Break the work into ordered entries, one per repository-scoped unit of work. For each
   entry, in this order:
   - **Session name first.** One opening body line telling whoever runs the prompt to add
     the plan name to their session's title, so every session spawned from this plan is
     recognizable as belonging to it: ``Add the plan name `<tag>` to this session's title
     before you start.`` Every entry carries it, worded the same way.
   - **Instructions next.** The rest of the body — concise, no padding — is the entry's
     primary content.
   - **Setup sub-items.** A `##` sub-item per repository prerequisite the instructions
     assume (installing a plugin, updating one, wiring a related change), ordered ahead of
     everything else in the entry, titled `Setup: ...`.
   - **Manual sub-items.** A `##` sub-item per step only a human can do, titled `Manual: ...`.
   - **Knowledge/devbook reminder.** One more `##` sub-item reminding whoever runs the
     prompt to update the target repository's own knowledge folders or devbook once it is
     done. Every entry carries this; never skip it.
   - **Metadata line.** The type the source material describes, `prompt` by default for a
     prompt to run; always `!ready`, on every entry whatever its place in the chain — order
     is carried by `after:`, never by holding a later entry at `!draft`; always an
     `effort:<points>` estimate off the 1/2/3/5/8/13/21 scale, sized from the instructions
     the entry actually carries; `repo:<name>` once per target repository; `id:<slug>` on
     every entry — a stable slug from its title, reused verbatim when the plan is
     regenerated, since it is how Backlog recognizes an entry already under way or already
     finished; `after:<id>` once per prerequisite, including
     across repositories; the shared `#tag`; and only the `*priority`, `@area` or `due:` the
     source material actually implies.
4. Close every plan with a review entry, last in the document and titled
   `Review the <plan subject> plan for anything missed`. It carries `id:review-plan`,
   `after:` each entry nothing else depends on — the plan's leaves, which is every entry
   transitively — and `repo:` once per repository the plan targeted. Its body asks whoever
   runs it to read the source material against what actually landed: every entry done, and
   nothing dropped, deferred or left half-finished along the way; anything still outstanding
   is written up as a new entry rather than noted and forgotten. It opens with the
   session-name line like every other entry and carries a `Manual: ...` sign-off sub-item,
   but no knowledge/devbook reminder — it changes no repository of its own. Never omit it,
   however small the plan.
5. Assemble the entries into one Markdown document per `assets/backlog-import-grammar.md`.
6. Produce the output: write it to the given path (default `<plan-slug>-import-plan.md` in
   the current working directory) when a file was asked for or implied, and show the full
   text inline either way so it is ready to paste directly.
7. Report the output location (if written), the entry count, the repositories targeted, and
   the dependency chain. Stop — do not open the Backlog app, run a prompt, or create a pull
   request.

## Output expectations

- One Markdown document; every entry's body precedes its `##`/`- [ ]` sub-items.
- Every entry states a type, `!ready`, an `effort:`, `repo:`, an `id:`, and the plan's
  shared `#tag`, and opens with the session-name line.
- `after:` correctly expresses the plan's dependency order, including cross-repository
  dependencies.
- The last entry is the plan review, waiting on every leaf of that order.
- No file changes outside the produced plan document; no call to the Backlog app or GitHub.

## Reference

- `assets/backlog-import-grammar.md`
