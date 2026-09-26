# ADR 0016: The knowledge folders adopt the devbook convention under `.devbook/`; the derived layer stays a local build output

```meta
date: 2026-09-26
related: [".devbook/arc42/08-crosscutting-concepts.md#devbook-database", ".devbook/arc42/adr/0004-knowledge-index-is-a-generated-local-database.md", ".devbook/arc42/adr/0015-devbook-database-lives-in-app-storage-and-the-app-builds-it.md", ".devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md", ".devbook/arc42/adr/0008-knowledge-reads-from-a-branch-snapshot-when-there-is-no-clone.md", ".devbook/arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md", ".devbook/domain/devbook/features.md#repository-devbook-areas", ".devbook/domain/roadmap/domain.md#roadmap-plan", ".devbook/tech/tooling.md#knowledge-meta-generator"]
```

## Status

```meta
```

Accepted, 2026-09-26, for the `devbook-adoption` plan. Partly built: the move
landed first, so this record is written after the fact rather than beforehand.

The plan asked for this record before any folder moved, numbered 0014. It is
late because pull request #645 adopted `devbook` 1.7.0 and moved the folders
before the record was written. It is numbered 0016 because local 0014
(attachments) and 0015 (the database in app storage, from #654) were
already taken. The plan was written against contract 16. The installation
landed at **contract 17**, and this record states what landed.

| Part of the decision | Where it stands on 2026-09-26 |
|---|---|
| Five folders under `.devbook/`, contract 17 | Built (#645) |
| `ai/` created | Scaffolded (#645); filled by the plan's `write-ai-adoption-record` |
| Derived layer stays a local build output | Built (ADR 0004; in app storage since ADR 0015) |
| Writer imports the installed generator | Built for the `.devbook/` layout (`tools/devbook/generator.mjs`) |
| Four procedures, schedule catalog | Built (#645: four procedures in `.agents/skills/`, `components.schedule` stamped) |
| `.backlog/` retired | Pending: the folder is still at the root |
| `_reading-order.json` retired, all six | Pending: all six are still committed, and `tools/devbook/reading-order.mjs` still reads them |
| Delivery engine replaces the orch-* gate | Pending: sessions already route through the `delivery` flows; `CLAUDE.md` and `bindings.delivery.roles` do not say so yet |

## Context

```meta
```

**The layout was ours, and the checker stopped supporting it.** The five
knowledge folders sat at the repository root as dot-folders (`.arc42`,
`.domain`, `.tech`, `.design`, and a `.backlog` beside them). Their generator
was an unedited install of `knowledge-base`, the predecessor of the `devbook`
plugin. The install was four releases stale, and ADR 0004 called re-syncing it
"the contract v6 follow-up". The current devbook checker only supports the
`.devbook/` layout. Re-syncing the root layout was therefore not a real option:
we had to move the folders, or keep a fork of the generator.

**The product already reads both layouts.** The plan's
`product-reads-devbook-layout` item taught the Devbook pane `.devbook/<name>`
first, with the root-level `.<name>` as the fallback. Its
`product-reads-current-schema` item taught the pane the contract-16 chapter
schema. So moving this repository's own folders changes no product behaviour.

**Several choices were bundled with the move,** because the stack
installs them together: which folders to adopt, what happens to the derived
indexes, which procedure skills and schedules to take, and what governs code
changes. Answered one at a time, the adoption would have contradicted itself.
The plan's `decide-adoption-scope` entry settled them together, and the
repository owner confirmed on 2026-09-26 that ADR 0004's derived-layer call
stands.

## Decision

```meta
```

**The knowledge folders follow the devbook convention, under `.devbook/`, at
the installed contract (17).** All five folders are adopted: `arc42`, `domain`,
`tech`, `design`, and `ai`. This repository never had an `ai/` folder; it is
created here, as a real folder for the adoption record, not as a mention in
the documentation. `components.devbook` in `.devbook/config.json` records the
release, contract, adopted folders, and migration log. `devbook:update` is the
only way that record changes.

**`.backlog/` is retired.** It held one document, `domain-backlog.md`, with one
item: *Add roadmap planning to the backlog*. That item is `done`, and it was
delivered as its own bounded context, [Roadmap
Planning](../../domain/roadmap/domain.md#roadmap-plan). So nothing in the
document needs a new home. The chapter that item implements is already in
`.devbook/domain/`, and git history keeps the document. Work items are Backlog
entries (local ADR 0007), not devbook chapters. The product's
`DevbookFolder.Backlog` member stays, because it is a persisted identifier in a
person's settings.

**The derived layer stays a local build output, per ADR 0004.** So the
`devbook-derived` component is not adopted, and nothing under `_meta/` is
committed. The generated database remains the artifact the panels read, and the
build writes it; it is never checked in. Local ADR 0015 has since moved *where*
the database lives, out of the clone into the app's storage, where the desktop
builds it. It keeps the database a local, uncommitted build output, which is the
call made here.

**The installed generator is `.devbook/_tools/devbook-meta/`, and the
repo-native writer imports it.** `devbook:init` materializes it there and
`devbook:update` refreshes it; it is never edited by hand.
`tools/devbook/build-database.mjs` stays this repository's own and imports the
generator's exported seam instead of forking it. Under ADR 0015 it is CI's build
check and the reference the C# builder is held to, so the seam still matters.
ADR 0004's
[amendment](0004-knowledge-index-is-a-generated-local-database.md#status)
already requires that. The predecessor install under
`.github/tools/knowledge-meta/` no longer has anything left to index.

**`_reading-order.json` is retired, all six files.** The generator derives
order from three things: `index: root` on the document that introduces a
directory, the numbers in filenames, and the folder's convention for its root
document. A committed order file said the same thing a second time, and ADR
0004 only wanted it for as long as nothing derived the order.
`01-introduction-and-goals.md` gains `index: root`; every other directory's
root follows from convention.

**The procedures and the schedules are adopted as shipped.** All four
`devbook-procedures` procedures are adopted: `start`, `show`, `capture`, and
`debug`. They live in `.agents/skills/`, and they are this repository's
procedures, not the seeded examples. The schedule catalog is enabled as it
shipped, with no cadence overrides. The selection lives under
`components.schedule`. The environment and model a scheduled run uses are
personal, so they stay out of the repository.

**The delivery engine replaces the orch-* gate.** Code changes go through
`flow-code`, devbook chapters through `flow-spec`, dependency moves through
`flow-update-packages`, and repository scaffolding through `flow-project`. The
gate stays a gate: it keeps the standing authorization, exploration never goes
straight to implementation, and Personal Validation is never skipped. The
engine's roles are bound to the `jsdotnet-ai-plugins` agents:

| Role | Agent |
|---|---|
| `architecture` | `architecture` |
| `qa` | `qa` |
| `domain` | `domain-design` |
| `ux` | `ux-design` |
| `docs` | `documentation` |

`product` and `security` stay unbound. That marketplace has no agent for either,
and an unbound role is the honest answer.

**Local ADR 0011 stands unchanged.** It decided that a devbook `annotation`
fence and a person's private reading note are two different things. The fence
is a shared review note inside the chapter, written only by `annotations.mjs`.
The note is the person's own data, which Backlog stores and replicates.
Adopting the convention brings in fences but merges nothing: the product
renders a fence as a note and never writes one from C#. The private remark
keeps its own store and its own container. Adopting devbook is not a reason to
revisit that decision.

## Rejected

```meta
```

- **Stay on the root layout and re-sync the generator.** The current checker
  does not support that layout, so it would have meant maintaining a fork of
  the generator. That is the exact situation that made the predecessor install
  four releases stale.
- **Adopt `devbook-derived` and commit the `_meta/` indexes.** This would
  reverse ADR 0004. Committed indexes cause merge conflicts on every change to
  a chapter, and they duplicate text that the Markdown already carries.
  `decide-adoption-scope` put this choice to the owner, who declined it.
- **Keep `.backlog/` as a sixth folder.** The convention has no such folder,
  and no flow would maintain it. What it held was work, and work belongs in
  Backlog entries.
- **Keep `_reading-order.json` beside the derived order.** That would state
  the order twice, with nothing to catch the two copies drifting apart.
- **Adopt only some procedures or schedules.** A procedure the repository does
  not adopt is one a flow has to guess. The catalog's cadences are tuned to
  each other: for example, `devbook-validate` runs before the reports that read
  its output. Trimming it is a later decision `delivery-schedule:update` can
  make, not a starting point.

## Consequences

```meta
```

- The pending parts of this decision are the remaining items of the
  `devbook-adoption` plan. `.backlog/` and all six `_reading-order.json` files
  go, and `tools/devbook/reading-order.mjs` retires with them. The predecessor
  install and its two workflows go too (`retire-predecessor-tooling`).
  `CLAUDE.md`, the instruction files, and the two orch-context copies must stop
  describing the old layout and the old gate
  (`sweep-instruction-and-doc-references`, `reroute-orchestration-gate`).
  `bindings.delivery.roles` must be written as the table above.
- A root-level spelling (`.arc42/…`) is legitimate in only two places: the
  product's layout fallback, and records that describe the history, such as
  this one or ADR 0008's amendment. Anywhere else it is stale.
- The contract moves with the plugin. A future contract bump is a
  `devbook:update` run, recorded in `components.devbook`, and does not need a
  new decision record. A new decision is needed only if the bump would reverse
  one of the choices above.
