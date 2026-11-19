# ADR 0057: Archify is retired, and Mermaid draws every chapter diagram

```meta
date: 2026-11-19
related: [".devbook/arc42/adr/0034-archify-renders-are-generated-on-demand-not-committed.md", ".devbook/arc42/tdr/0001-archify-artifacts-are-invisible-on-a-branch-snapshot.md", ".devbook/arc42/adr/0004-knowledge-index-is-a-generated-local-database.md", ".devbook/arc42/adr/0008-knowledge-reads-from-a-branch-snapshot-when-there-is-no-clone.md", ".devbook/arc42/11-risks-and-technical-debt.md#technical-debt", ".devbook/arc42/08-crosscutting-concepts.md#devbook-database", ".devbook/tech/tooling.md#archify", ".devbook/tech/shared.md#mermaid", ".devbook/design/component-libraries.md#diagram-and-graph-strategy", ".devbook/domain/devbook/requirements.md"]
c4: [backlog.desktop.devbook]
```

Backlog stops rendering Archify artifacts. The Devbook pane draws every chapter
diagram from its mermaid fence, and the vendored generator under `tools/archify`
leaves the repository with the code that ran it. This record supersedes ADR 0034.

## Status

```meta
```

Proposed, 2026-11-19, by the flow-spec run of step 5 of the `codebase-pruning` plan,
run unattended. The repository owner took the decision before the plan was imported;
this record writes it down. The code is removed by a `flow-code` run of its own.

A **local** decision, numbered in the local sequence. Every bare ADR number below
means the local one. Number 0056 was skipped, because an open branch claims it.

## Context

```meta
```

**Nobody sees the pictures.** The Archify artifact replaces the mermaid only behind
the `archify-diagrams` flag, which is off by default. With it off, Mermaid already
draws every chapter, so no reader loses a picture.

**The feature is the largest thing the repository carries for its value.** The
vendored generator is about 39,000 lines in 73 files, copied into the desktop build.
The app adds about 2,000 lines: the on-demand renderer of ADR 0034, the artifact
lookup with its own Node launcher and fence scan, the Storybook pages and six test
files. `.devbook/` carries 26 specifications and indexes beside it.

**It costs upkeep on every diagram edit.** A specification is a re-authoring of its
fence. An edited fence leaves its artifact out of date until somebody re-authors the
specification and renders it again, as pull requests #1201, #1329 and #1687 did.

**It carries open debt.** TDR 0001 records that a branch-sourced repository never
shows an artifact. Fixing it would build more on a feature nobody switches on.

## Decision

```meta
```

### 1. The app draws chapter diagrams with Mermaid only

```meta
```

A mermaid fence is drawn by Mermaid, with the source shown when drawing fails. No
other renderer is offered for it, so the diagram has no renderer switch and no
out-of-date notice. C4 views keep their own renderer: they are embedded as
`![…](c4:<view key>)`, not as a mermaid fence.

### 2. The Archify code and the vendored generator leave the repository

```meta
```

`tools/archify/`, `tools/diagrams/archify-artifacts.mjs`, the on-demand renderer,
the artifact lookup, the `archify-diagrams` flag, the Storybook pages and their tests
are deleted. Node stays, for the rest of the repository's tooling.

### 3. Backlog's own devbook carries no Archify files

```meta
```

Every `_archify/` folder under `.devbook/` is deleted. The fences they re-authored
stay, and are the diagram.

### 4. Other repositories' Archify files are still read correctly

```meta
```

Other repositories may still carry `_archify/` folders and `ext.archify.*` metadata.
The Devbook pane keeps its two requirements on them: an `_archify` folder is never
listed, and `ext.archify.*` metadata is shown as another tool's. It draws their
fences with Mermaid like any other.

## Consequences

```meta
```

Positive:

- **About 41,000 lines leave the repository.** The desktop build no longer copies a
  generator, and the app no longer starts Node to view a chapter.
- **A diagram edit is one edit.** The fence is the whole diagram, so nothing goes out
  of date beside it.
- **TDR 0001 is resolved.** A branch-sourced and a cloned repository draw the same
  diagrams the same way.

Negative:

- **A richer picture is no longer available.** A person who switched the flag on
  sees Mermaid again. A diagram that needs more than Mermaid offers has to be a C4
  view or wait for a new decision.

Neutral:

- **The devbook database stops holding artifact rows** once the code goes, and the
  builder parity test moves with the Node writer in the same change.
- **ADR 0004's and ADR 0008's Archify amendments describe code that is gone.** They
  stay as history.

## Rejected

```meta
```

- **Keep Archify behind its flag.** It costs the same upkeep and lines for a picture
  nobody switches on.
- **Fix TDR 0001 first, then decide.** It builds more on a feature the decision
  removes.
- **Keep the vendored generator for authoring by hand.** Nothing in the app would
  show its output, so the specifications would drift from their fences unseen.

## Verification

```meta
```

Look for these once the code is removed:

1. **Nothing Archify is left.** No `tools/archify/`, no `archify-artifacts.mjs`, no
   `archify-diagrams` flag and no `_archify/` folder under `.devbook/`.
2. **Every fence draws.** A chapter's mermaid fence is drawn by Mermaid, with no
   renderer switch.
3. **Another repository's files stay quiet.** A context holding an `_archify` folder
   lists none of it, and `ext.archify.*` metadata is shown as another tool's.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-19 | Proposed: Archify is retired, the app draws chapter diagrams with Mermaid only, and `tools/archify` leaves the repository. Supersedes ADR 0034 and resolves TDR 0001. | The flag was off by default, the pictures cost upkeep on every fence edit, and the code was the repository's largest removable part. Step 5 of the `codebase-pruning` plan. |
