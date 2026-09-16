# TDR 0001: Archify artifacts are invisible when the Devbook reads from a branch snapshot

```meta
date: 2026-09-16
related: [".arc42/adr/0008-knowledge-reads-from-a-branch-snapshot-when-there-is-no-clone.md", ".arc42/adr/0004-knowledge-index-is-a-generated-local-database.md", ".arc42/11-risks-and-technical-debt.md#technical-debt", ".domain/devbook/features.md#repository-devbook-areas"]
```

## Status

**Identified** — 2026-09-16. Not planned; no owner assigned beyond the repository
owner. Remediation window: before branch-sourced repositories are the common way
a workspace reads a repository it does not build (see *Severity*).

TDR lifecycle: identified → planned → in-progress → resolved.

## The debt

A repository whose Devbook source is a **branch** (local ADR 0008) never shows a
rendered Archify diagram, never reports one as out of date, and never offers to
render or author one. Every diagram in every chapter falls back to the mermaid
fence. The same repository read through a **local clone** shows all of it.

Nothing in the UI says why. The reader sees mermaid where a colleague on a clone
sees a picture, and the two panels give no hint that the source kind is the
difference.

## Origin

The third amendment to local ADR 0008 (2026-09-15, *the snapshot is lazy*,
commit `2ca1eeb0`) replaced the whole-archive branch snapshot with an index plus
files fetched on demand. As part of that, the content fetch for a knowledge
folder deliberately **excludes `_archify/`**:

- `DevbookFolderSource.PrepareContentAsync` fetches
  `Subtree(<folder>)` with `Exclude("_archify")`
  (`src/Infrastructure/Backlog.Infrastructure.FileSystem/Workspace/DevbookFolderSource.cs`,
  `RenderedArtifactsFolder`).
- The reason given beside the constant is sound: rendered artifacts are hundreds
  of kilobytes each, were the bulk of the old archive, and were the reason it was
  slow — so they are to be fetched "only when a rendered diagram is shown".

The second half of that sentence was never built. `ArchifyDiagramArtifacts.Find`
(`src/Modules/Devbook/Backlog.Modules.Devbook.UI/ArchifyDiagramArtifacts.cs`)
answers straight off disk — `_archify/index.json`, the specification and the
`.html` beside the chapter — with `File.Exists` / `Directory.Exists` against the
snapshot folder. It never asks the folder port to prepare those paths, so for a
branch source the folder is simply absent and the answer is always "nothing
exists for this diagram".

The database path does not rescue it either: `_meta/devbook.db` is a git-ignored
build output (local ADR 0004), so a branch snapshot can never carry one and
`ReadDatabaseIndex` returns null for every branch-sourced chapter.

Before the amendment the archive included `_archify/` and this worked. The
regression is a property of the diff, not of any test — no test covers a
branch-sourced diagram, which is also why it was not caught.

## Affected components

| Component | Role |
|---|---|
| `Backlog.Modules.Devbook.UI` — `ArchifyDiagramArtifacts` | Answers `IDiagramArtifactSource`; reads disk directly, unaware of the source kind. |
| `Backlog.Infrastructure.FileSystem` — `DevbookFolderSource` | Owns the lazy snapshot and the `_archify` exclusion. |
| `Backlog.UI.Components` — `DiagramView` | Renders the artifact or the mermaid fallback; has no "fetching" state for artifacts. |
| `IDevbookFolderSource.PrepareContentAsync` | The port that can fetch explicit relative paths on demand — the mechanism the fix needs, already present. |

## Impact

- **Functional.** The feature `DevbookFeatures.ArchifyDiagrams` is silently a
  no-op for branch-sourced repositories. Rendered diagrams, the *out of date*
  hint and the render/author affordances are all absent.
- **Trust / explainability.** The panel shows a working state (mermaid) rather
  than an error, so there is nothing to report and nothing to fix from the
  reader's side. A user comparing a clone and a branch view of the same
  repository has no way to learn that the source kind is the cause.
- **Delivery.** Archify's value proposition — the picture beside the chapter —
  does not reach the audience ADR 0008 exists for: people reading a repository
  they will never clone.
- **Operations.** None. No error, no log line, no extra network.
- **Quality attributes.** Consistency between the two source kinds is broken;
  performance of the lazy snapshot is preserved (which is what the exclusion
  bought).

## Severity

**Medium.** No data loss and no crash, but a shipped feature is inert on one of
the two supported source kinds with no signal. It rises to **high** the moment
branch-sourced repositories become the normal way a workspace reads its
dependencies, because then most readers never see a rendered diagram at all.

## Remediation options

### Option A — fetch `_archify` lazily per diagram (preferred)

When a diagram's chapter was resolved through a `Branch` location,
`ArchifyDiagramArtifacts` asks `PrepareContentAsync(key, alias, [paths])` for the
chapter's `_archify/index.json` and, once the index names them, the matching
`<stem>.json` and `<stem>.html` — then reads them as it does today.

- *Pro:* keeps the exclusion and its performance win; fetches only what is
  looked at; uses a port that already exists.
- *Con:* `Find` is synchronous and called once per diagram per render. The fetch
  has to be started from an async edge (chapter open, or a `DiagramView`
  "fetching…" state) and `Find` re-answered on landing, the same way the folder
  source's `Changed` event already re-renders a panel after an index fetch.
- *Scope note:* `RenderAsync` and `AuthorAsync` stay clone-only. Both shell out to
  `tools/diagrams/archify-artifacts.mjs` in a working tree; a snapshot has no
  working tree and is read-only by ADR 0008's own rule.

### Option B — fetch `_archify/index.json` with the chapter, artifacts on show

Include the small `index.json` (and specifications) in the content fetch, keep
only the `.html` excluded, and fetch the `.html` on demand as in A.

- *Pro:* the *out of date* hint and the type/quality answer become correct with
  no extra round trip; only the heavy file is lazy.
- *Con:* still needs the async edge for the `.html`; the exclusion rule grows a
  second clause.

### Option C — drop the exclusion

Fetch `_archify/` with the folder again.

- *Pro:* smallest change; restores the pre-amendment behaviour.
- *Con:* reintroduces exactly the cost the amendment removed. Rejected unless
  measurement shows the cost was never the artifacts.

### Not an option — say nothing

Leaving it as-is is the worst state: a feature that is off without saying so.
If A or B is deferred further, the minimum honest change is for `DiagramView`
to state that rendered diagrams are unavailable for a branch source.

## Measurable outcome

Resolved when a repository configured with a branch source shows, for a chapter
whose branch carries `_archify/`:

1. the rendered artifact where the fence hash matches the index;
2. the *out of date* hint where it does not;
3. no render/author controls (clone-only), stated rather than hidden;

and a test in `tests/` exercises a branch-sourced diagram through a fake
`IDevbookFolderSource` — the case that had no coverage when this regressed.

## Links

- Local ADR 0008 — the branch snapshot and its lazy amendment.
- Local ADR 0004 — why `_meta/devbook.db` cannot travel with a snapshot.
- `.arc42/11-risks-and-technical-debt.md`, *Technical Debt* — row D4.
- Commit `2ca1eeb0` — *Make the Devbook branch snapshot lazy: index first, files on demand.*
