# Devbook

```meta
status: draft
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

Devbook is a personal project knowledge base — not a task queue. It
collects, organizes, and retrieves Knowledge Notes structured with the PARA
framework (Projects, Areas, Resources, Archive), and links bidirectionally with
[Tasks](../tasks/domain.md#task) so related
context is easy to find later.

Inside the boundary: Knowledge Notes, reading a repository's devbook folders in
place, the reader's remarks on chapters, instruction review, and retrieval across
all of them. Outside it: the folders themselves, which the repository owns and
edits elsewhere; the work items a chapter links to, which
[Tasks](../tasks/domain.md#task) answers; and gathering chapters under planned
work, which [Roadmap Planning](../roadmap/domain.md#roadmap-item) answers.

## Devbook pane

```meta
status: draft
type: feature-flag
key: repository-devbook
default: on
related: [".devbook/domain/devbook/features.md#repository-devbook-areas", ".devbook/domain/devbook/features.md#area-selection-and-scope"]
```

Turning it on shows the Devbook side pane for the selected repository, with its design, architecture, domain, technology, AI adoption, and instruction sections; the Shell reads it to decide whether to offer the pane at all, and this context's own scope service reads it to decide which sections to show. Released and on by default. It absorbed `devbook-sections`, which switched the sections separately and left an empty pane when turned off alone, and it replaced `repository-knowledge`. The catalog still reads all three former keys, `knowledge-sections` among them, so a `features.json` that turned either switch off keeps Devbook off. The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices.

## Instruction optimization

```meta
status: draft
type: feature-flag
key: instruction-optimization
related: [".devbook/domain/devbook/features.md#instruction-optimization"]
```

Reserved for the instruction review the feature it gates describes, and not in the feature catalog yet: no code checks the key, so there is no default to state and nothing to switch. The chapter exists so the feature has its gate before the first line of it is built; when the review ships, the key lands in `DevbookFeatures` and in the catalog, and this chapter gains its `default`.
