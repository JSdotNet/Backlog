---
name: backlog-import-plan
description: Turn an agreed specification (a .domain feature, a .backlog item, an ADR, or other planning material) into a Backlog import plan — an ordered, dependency-linked sequence of prompt entries in Backlog's entry-text grammar, ready to paste or upload.
disable-model-invocation: true
---

# Build a Backlog import plan

A one-shot handoff: the spec is settled and it is time to generate the next batch of AI
prompts for the Backlog app to import. This skill reads the agreed material, writes one
Backlog import plan document, and stops — it never talks to the Backlog app, executes a
generated prompt, or touches GitHub.

Read `assets/backlog-import-grammar.md` before writing anything. It carries the exact
entry/sub-item shape, the metadata tokens, and the plan-identity/re-import mechanics; do
not invent syntax beyond it.

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
   this plan is later regenerated, so Backlog's upsert-by-`(tag, id:)` re-import recognizes
   it as an update instead of a duplicate.
3. Break the work into ordered prompt entries, one per repository-scoped unit of work. For
   each entry, in this order:
   - **Instructions first.** The body right after the metadata line — concise, no padding —
     is the entry's primary content.
   - **Setup sub-items.** A `##` sub-item per repository prerequisite the instructions
     assume (installing a plugin, updating one, wiring a related change), ordered ahead of
     everything else in the entry, titled `Setup: ...`.
   - **Manual sub-items.** A `##` sub-item per step only a human can do, titled `Manual: ...`.
   - **Knowledge/devbook reminder.** One more `##` sub-item reminding whoever runs the
     prompt to update the target repository's own knowledge folders or devbook once it is
     done. Every entry carries this; never skip it.
   - **Metadata line.** Bare `prompt` type; `repo:<name>` once per target repository;
     `id:<slug>` when a later entry in this plan depends on it; `after:<id>` once per
     prerequisite, including across repositories; the shared `#tag`; and only the sigils
     (`*priority`, `!status`, `@area`) or `due:` the source material actually implies.
4. Assemble the entries into one Markdown document per `assets/backlog-import-grammar.md`
   — a second `# Title` starts the next entry, no wrapper heading, no front matter.
5. Produce the output: write it to the given path (default `<plan-slug>-import-plan.md` in
   the current working directory) when a file was asked for or implied, and show the full
   text inline either way so it is ready to paste directly.
6. Report the output location (if written), the entry count, the repositories targeted, and
   the dependency chain. Stop — do not open the Backlog app, run a prompt, or create a pull
   request.

## Output expectations

- One Markdown document; every entry's body precedes its `##`/`- [ ]` sub-items.
- Every entry carries `repo:` and the plan's shared `#tag`.
- Every entry carries the knowledge/devbook reminder sub-item, with no exception.
- `id:`/`after:` correctly express the plan's dependency order, including cross-repository
  dependencies.
- No file changes outside the produced plan document; no call to the Backlog app or GitHub.

## Reference

- `assets/backlog-import-grammar.md`
